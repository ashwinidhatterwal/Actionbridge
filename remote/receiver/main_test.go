package main

import (
	"bytes"
	"crypto/sha256"
	"encoding/base64"
	"encoding/hex"
	"encoding/json"
	"errors"
	"github.com/pion/ice/v4"
	"github.com/pion/logging"
	"github.com/pion/transport/v5/vnet"
	"github.com/pion/webrtc/v4"
	"os"
	"path/filepath"
	"sync"
	"testing"
	"time"
)

func TestSignalAuthentication(t *testing.T) {
	m := Envelope{Type: "offer", Session: "abc", SDP: "fingerprint"}
	m.MAC = sign("secret", m)
	if !verified("secret", m) {
		t.Fatal("valid envelope rejected")
	}
	m.SDP = "attacker fingerprint"
	if verified("secret", m) {
		t.Fatal("tampering accepted")
	}
	if verified("other", m) {
		t.Fatal("wrong key accepted")
	}
}
func TestConfigAndPathSafety(t *testing.T) {
	for _, name := range []string{"../../escape.exe", "a\\b.pdf", "CON", "trailing. ", "\x00bad"} {
		s := safeName(name)
		if filepath.Base(s) != s || s == "" {
			t.Fatalf("unsafe name %q", s)
		}
	}
	c := Config{Service: "http://bad", Room: "abc"}
	if validConfig(c) == nil {
		t.Fatal("insecure endpoint accepted")
	}
}
func channel(t *testing.T, r *receiver) (*webrtc.DataChannel, chan map[string]any, func()) {
	t.Helper()
	settings := webrtc.SettingEngine{}
	settings.SetIncludeLoopbackCandidate(true)
	settings.SetICEMulticastDNSMode(ice.MulticastDNSModeDisabled)
	settings.SetNetworkTypes([]webrtc.NetworkType{webrtc.NetworkTypeUDP4})
	router, _ := vnet.NewRouter(&vnet.RouterConfig{CIDR: "10.0.0.0/24", LoggerFactory: logging.NewDefaultLoggerFactory()})
	na, _ := vnet.NewNet(&vnet.NetConfig{StaticIPs: []string{"10.0.0.1"}})
	nb, _ := vnet.NewNet(&vnet.NetConfig{StaticIPs: []string{"10.0.0.2"}})
	router.AddNet(na)
	router.AddNet(nb)
	router.Start()
	settings.SetNet(na)
	api := webrtc.NewAPI(webrtc.WithSettingEngine(settings))
	a, e := api.NewPeerConnection(webrtc.Configuration{})
	if e != nil {
		t.Fatal(e)
	}
	settings.SetNet(nb)
	b, e := webrtc.NewAPI(webrtc.WithSettingEngine(settings)).NewPeerConnection(webrtc.Configuration{})
	if e != nil {
		t.Fatal(e)
	}
	b.OnDataChannel(func(d *webrtc.DataChannel) {
		if r.call != nil {
			attachJobs(d, r.call)
		} else {
			r.attach(d)
		}
	})
	label := "files-v1"
	if r.call != nil {
		label = "jobs-v2"
	}
	d, e := a.CreateDataChannel(label, nil)
	if e != nil {
		t.Fatal(e)
	}
	opened := make(chan struct{})
	replies := make(chan map[string]any, 100)
	d.OnOpen(func() { close(opened) })
	d.OnMessage(func(m webrtc.DataChannelMessage) {
		var value map[string]any
		json.Unmarshal(m.Data, &value)
		replies <- value
	})
	gather := webrtc.GatheringCompletePromise(a)
	offer, _ := a.CreateOffer(nil)
	a.SetLocalDescription(offer)
	<-gather
	b.SetRemoteDescription(*a.LocalDescription())
	gather = webrtc.GatheringCompletePromise(b)
	answer, _ := b.CreateAnswer(nil)
	b.SetLocalDescription(answer)
	<-gather
	a.SetRemoteDescription(*b.LocalDescription())
	select {
	case <-opened:
	case <-time.After(10 * time.Second):
		t.Fatal("WebRTC data channel did not open")
	}
	return d, replies, func() { a.Close(); b.Close(); router.Stop() }
}
func command(t *testing.T, d *webrtc.DataChannel, v any) {
	t.Helper()
	b, _ := json.Marshal(v)
	if e := d.SendText(string(b)); e != nil {
		t.Fatal(e)
	}
}
func reply(t *testing.T, q chan map[string]any, kind string) map[string]any {
	t.Helper()
	select {
	case m := <-q:
		if m["type"] != kind {
			t.Fatalf("wanted %s got %#v", kind, m)
		}
		return m
	case <-time.After(5 * time.Second):
		t.Fatal("reply timeout")
		return nil
	}
}
func TestWebRTCTransferResumeAndDuplicate(t *testing.T) {
	folder := t.TempDir()
	r := newReceiver(folder)
	d, q, closePair := channel(t, r)
	defer closePair()
	data := bytes.Repeat([]byte("real file content"), 10000)
	sum := sha256.Sum256(data)
	id := hex.EncodeToString(sum[:])
	begin := metadata{Type: "begin", ID: id, Name: "../../test.pdf", Size: int64(len(data))}
	command(t, d, begin)
	if reply(t, q, "ready")["offset"].(float64) != 0 {
		t.Fatal("wrong offset")
	}
	d.Send(data[:32768])
	reply(t, q, "ack")
	command(t, d, metadata{Type: "cancel"})
	reply(t, q, "cancelled")
	command(t, d, begin)
	if reply(t, q, "ready")["offset"].(float64) != 32768 {
		t.Fatal("resume lost")
	}
	for offset := 32768; offset < len(data); {
		end := min(offset+32768, len(data))
		d.Send(data[offset:end])
		reply(t, q, "ack")
		offset = end
	}
	command(t, d, metadata{Type: "finish", ID: id})
	saved := reply(t, q, "saved")
	name := saved["name"].(string)
	actual, e := os.ReadFile(filepath.Join(folder, name))
	if e != nil || !bytes.Equal(data, actual) {
		t.Fatal("file mismatch", e)
	}
	command(t, d, begin)
	reply(t, q, "saved")
	entries, _ := os.ReadDir(folder)
	if len(entries) != 1 {
		t.Fatal("duplicate file committed")
	}
}
func TestChecksumFailureAndOversizeRejected(t *testing.T) {
	folder := t.TempDir()
	r := newReceiver(folder)
	d, q, closePair := channel(t, r)
	defer closePair()
	command(t, d, metadata{Type: "begin", ID: hex.EncodeToString(make([]byte, 32)), Name: "bad", Size: 3})
	reply(t, q, "ready")
	d.Send([]byte("bad"))
	reply(t, q, "ack")
	command(t, d, metadata{Type: "finish", ID: hex.EncodeToString(make([]byte, 32))})
	reply(t, q, "error")
	entries, _ := os.ReadDir(folder)
	if len(entries) != 0 {
		t.Fatal("bad file retained")
	}
	command(t, d, metadata{Type: "begin", ID: hex.EncodeToString(make([]byte, 32)), Name: "big", Size: 2147483649})
	reply(t, q, "error")
}

func TestJobsChannelCommittedResumeAndPrinters(t *testing.T) {
	data := bytes.Repeat([]byte("transport-content"), 21000)
	var mu sync.Mutex
	var stored []byte
	id := "b1137e92-bc62-4f78-91e7-6267edb26d9e"
	r := newReceiver(t.TempDir())
	r.call = func(v map[string]any) (json.RawMessage, error) {
		mu.Lock()
		defer mu.Unlock()
		var result any
		switch v["method"] {
		case "status":
			if v["jobId"] != id {
				return nil, errors.New("Unknown job")
			}
			result = map[string]any{"state": "uploading", "offset": len(stored), "request": map[string]any{"size": len(data)}}
		case "append":
			chunk, e := base64.StdEncoding.DecodeString(v["data"].(string))
			if e != nil {
				return nil, e
			}
			if v["offset"].(int64) != int64(len(stored)) {
				return nil, errors.New("Wrong durable offset")
			}
			stored = append(stored, chunk...)
			result = map[string]any{"offset": len(stored)}
		case "printers":
			result = []map[string]any{{"name": "Real PC printer", "supportsDuplex": true}}
		default:
			return nil, errors.New("Unsupported")
		}
		b, e := json.Marshal(result)
		return b, e
	}
	d, q, closePair := channel(t, r)
	command(t, d, map[string]any{"type": "rpc", "requestId": "printers", "method": "printers"})
	p := reply(t, q, "rpc")
	if p["result"].([]any)[0].(map[string]any)["name"] != "Real PC printer" {
		t.Fatal("printer response lost")
	}
	command(t, d, map[string]any{"type": "upload", "jobId": id})
	reply(t, q, "ready")
	for offset := 0; offset < 262144; offset += 32768 {
		d.Send(data[offset : offset+32768])
	}
	if reply(t, q, "ack")["offset"].(float64) != 262144 {
		t.Fatal("durable ACK incorrect")
	}
	// Uncommitted tail is deliberately discarded when the channel closes.
	d.Send(data[262144:294912])
	closePair()
	d, q, closePair = channel(t, r)
	defer closePair()
	command(t, d, map[string]any{"type": "upload", "jobId": id})
	if reply(t, q, "ready")["offset"].(float64) != 262144 {
		t.Fatal("resume includes uncommitted data")
	}
	for offset := 262144; offset < len(data); {
		end := min(offset+32768, len(data))
		d.Send(data[offset:end])
		offset = end
	}
	reply(t, q, "ack")
	mu.Lock()
	equal := bytes.Equal(stored, data)
	mu.Unlock()
	if !equal {
		t.Fatal("resumed bytes differ")
	}
	command(t, d, map[string]any{"type": "rpc", "requestId": "bad", "method": "shell"})
	p = reply(t, q, "rpc")
	if p["error"] == nil {
		t.Fatal("arbitrary command accepted")
	}
}

func TestUnicodeActionFragmentation(t *testing.T) {
	text := string(bytes.Repeat([]byte("क"), 65536))
	received := ""
	r := newReceiver(t.TempDir())
	r.call = func(v map[string]any) (json.RawMessage, error) {
		if v["method"] != "create" {
			return nil, errors.New("Unsupported")
		}
		received = v["job"].(map[string]any)["text"].(string)
		return json.RawMessage(`{"state":"uploading"}`), nil
	}
	d, q, closePair := channel(t, r)
	defer closePair()
	raw, _ := json.Marshal(map[string]any{"type": "rpc", "requestId": "unicode", "method": "create", "job": map[string]any{"text": text}})
	total := (len(raw) + 23999) / 24000
	for index := 0; index < total; index++ {
		start := index * 24000
		end := min(start+24000, len(raw))
		command(t, d, map[string]any{"type": "fragment", "requestId": "unicode", "index": index, "total": total, "data": base64.StdEncoding.EncodeToString(raw[start:end])})
	}
	reply(t, q, "rpc")
	if received != text {
		t.Fatal("large Unicode text changed")
	}
}

func TestOutgoingRPC(t *testing.T) {
	data := bytes.Repeat([]byte("phone file"), 2000)
	r := newReceiver(t.TempDir())
	r.call = func(v map[string]any) (json.RawMessage, error) {
		switch v["method"] {
		case "outbox":
			return json.RawMessage(`[{"id":"file-1","kind":"file"}]`), nil
		case "download":
			return json.Marshal(map[string]any{"offset": 0, "data": base64.StdEncoding.EncodeToString(data[:16384])})
		case "received":
			return json.RawMessage(`{"delivered":true}`), nil
		}
		return nil, errors.New("unsupported")
	}
	d, q, closePair := channel(t, r)
	defer closePair()
	for _, method := range []string{"outbox", "download", "received"} {
		command(t, d, map[string]any{"type": "rpc", "requestId": method, "method": method, "itemId": "file-1", "offset": 0, "count": 16384})
		m := reply(t, q, "rpc")
		if m["error"] != nil {
			t.Fatal(m)
		}
		if method == "download" {
			value := m["result"].(map[string]any)
			decoded, e := base64.StdEncoding.DecodeString(value["data"].(string))
			if e != nil || !bytes.Equal(decoded, data[:16384]) {
				t.Fatal("outgoing bytes changed")
			}
		}
	}
}
