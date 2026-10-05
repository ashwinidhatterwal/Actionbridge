package main

import (
	"bytes"
	"context"
	"crypto/rand"
	"encoding/base64"
	"encoding/json"
	"github.com/pion/ice/v4"
	"github.com/pion/logging"
	"github.com/pion/transport/v5/vnet"
	"github.com/pion/webrtc/v4"
	"net/http"
	"os"
	"testing"
	"time"
)

// Opt-in live signaling test. Virtual UDP peers avoid sandbox interface restrictions;
// authenticated WebSockets, ICE requests and negotiation use the real service.
func TestLiveDesktopSignaling(t *testing.T) {
	if os.Getenv("ACTIONBRIDGE_LIVE_TEST") != "1" {
		t.Skip("opt-in temporary Cloudflare room")
	}
	ctx, cancel := context.WithTimeout(context.Background(), 65*time.Second)
	defer cancel()
	key := func() string { b := make([]byte, 32); rand.Read(b); return base64.RawURLEncoding.EncodeToString(b) }
	pcKey, phoneKey, secret := key(), key(), key()
	service := "https://actionbridge-connect.actionbridge.workers.dev"
	body, _ := json.Marshal(map[string]string{"pcKey": pcKey, "phoneKey": phoneKey})
	req, _ := http.NewRequestWithContext(ctx, "POST", service+"/enroll", bytes.NewReader(body))
	req.Header.Set("Content-Type", "application/json")
	res, e := http.DefaultClient.Do(req)
	if e != nil {
		t.Fatal(e)
	}
	var response struct{ Room string }
	e = json.NewDecoder(res.Body).Decode(&response)
	res.Body.Close()
	if res.StatusCode != 200 || e != nil {
		t.Fatalf("Enrollment status %d", res.StatusCode)
	}
	defer func() {
		clean, done := context.WithTimeout(context.Background(), 15*time.Second)
		defer done()
		req, _ := http.NewRequestWithContext(clean, "DELETE", service+"/rooms/"+response.Room+"/delete?role=pc", nil)
		req.Header.Set("Authorization", "Bearer "+pcKey)
		res, e := http.DefaultClient.Do(req)
		if e != nil {
			t.Error("Temporary room cleanup failed")
			return
		}
		res.Body.Close()
		if res.StatusCode != 200 && res.StatusCode != 404 {
			t.Errorf("Cleanup status %d", res.StatusCode)
		}
	}()
	router, _ := vnet.NewRouter(&vnet.RouterConfig{CIDR: "10.0.0.0/24", LoggerFactory: logging.NewDefaultLoggerFactory()})
	a, _ := vnet.NewNet(&vnet.NetConfig{StaticIPs: []string{"10.0.0.1"}})
	b, _ := vnet.NewNet(&vnet.NetConfig{StaticIPs: []string{"10.0.0.2"}})
	router.AddNet(a)
	router.AddNet(b)
	router.Start()
	defer router.Stop()
	oldClient, oldReceiver := newClientPeer, newReceiverPeer
	defer func() { newClientPeer, newReceiverPeer = oldClient, oldReceiver }()
	configure := func(s *webrtc.SettingEngine, n *vnet.Net) {
		s.SetNet(n)
		s.SetICEMulticastDNSMode(ice.MulticastDNSModeDisabled)
		s.SetNetworkTypes([]webrtc.NetworkType{webrtc.NetworkTypeUDP4})
	}
	newClientPeer = func() (*webrtc.PeerConnection, error) {
		s := webrtc.SettingEngine{}
		configure(&s, a)
		return webrtc.NewAPI(webrtc.WithSettingEngine(s)).NewPeerConnection(webrtc.Configuration{})
	}
	newReceiverPeer = func(s webrtc.SettingEngine, c webrtc.Configuration) (*webrtc.PeerConnection, error) {
		configure(&s, b)
		return webrtc.NewAPI(webrtc.WithSettingEngine(s)).NewPeerConnection(c)
	}
	r := newReceiver(t.TempDir())
	r.call = func(v map[string]any) (json.RawMessage, error) {
		return json.Marshal(map[string]any{"state": "completed", "method": v["method"]})
	}
	receiverDone := make(chan error, 1)
	go func() {
		receiverDone <- connect(ctx, Config{Service: service, Room: response.Room, PcKey: pcKey, Secret: secret}, r)
	}()
	rpc, closeAll, e := clientConnect(ctx, Config{Service: service, Room: response.Room, PcKey: phoneKey, Secret: secret})
	if e != nil {
		t.Fatal(e)
	}
	result, e := rpc.call(map[string]any{"method": "status", "jobId": "00000000-0000-0000-0000-000000000001"})
	if e != nil {
		t.Fatal(e)
	}
	var status struct{ State string }
	json.Unmarshal(result, &status)
	if status.State != "completed" {
		t.Fatal("Reply not correlated")
	}
	closeAll()
	cancel()
	select {
	case <-receiverDone:
	case <-time.After(5 * time.Second):
		t.Error("Receiver did not close")
	}
}
