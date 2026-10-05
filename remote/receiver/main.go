package main

import (
	"bufio"
	"context"
	"crypto/hmac"
	"crypto/sha256"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"io"
	"log"
	"net/http"
	"net/url"
	"os"
	"os/signal"
	"path/filepath"
	"regexp"
	"runtime"
	"strings"
	"sync"
	"time"
	"unicode"

	"github.com/coder/websocket"
	"github.com/pion/webrtc/v4"
)

type Config struct {
	Mode    string `json:"mode"`
	Parent  int    `json:"parent"`
	Service string `json:"service"`
	Room    string `json:"room"`
	PcKey   string `json:"pcKey"`
	Secret  string `json:"secret"`
	Folder  string `json:"folder"`
	Jobs    bool   `json:"jobs"`
}
type Envelope struct {
	Type    string `json:"type"`
	Session string `json:"session"`
	SDP     string `json:"sdp"`
	MAC     string `json:"mac"`
	Online  bool   `json:"online,omitempty"`
}

func sign(secret string, m Envelope) string {
	h := hmac.New(sha256.New, []byte(secret))
	io.WriteString(h, m.Session+"\n"+m.Type+"\n"+m.SDP)
	return hex.EncodeToString(h.Sum(nil))
}
func verified(secret string, m Envelope) bool {
	want, e := hex.DecodeString(m.MAC)
	if e != nil {
		return false
	}
	got, _ := hex.DecodeString(sign(secret, m))
	return hmac.Equal(want, got)
}

var hashPattern = regexp.MustCompile(`^[a-f0-9]{64}$`)
var sessionPattern = regexp.MustCompile(`^[a-f0-9]{32}$`)
var keyPattern = regexp.MustCompile(`^[A-Za-z0-9_-]{43}$`)

func validConfig(c Config) error {
	u, e := url.Parse(c.Service)
	if e != nil || u.Scheme != "https" || u.Host == "" || u.User != nil || u.RawQuery != "" || u.Fragment != "" || u.Path != "" && u.Path != "/" {
		return errors.New("service must be an HTTPS origin")
	}
	if !sessionPattern.MatchString(c.Room) || !keyPattern.MatchString(c.PcKey) || !keyPattern.MatchString(c.Secret) || !filepath.IsAbs(c.Folder) {
		return errors.New("invalid receiver configuration")
	}
	return nil
}
func event(state, detail string) {
	output(map[string]string{"state": state, "detail": detail})
}
func main() {
	log.SetOutput(io.Discard)
	scanner := bufio.NewScanner(os.Stdin)
	scanner.Buffer(make([]byte, 4096), 4*1024*1024)
	if !scanner.Scan() {
		event("error", "Missing receiver configuration")
		return
	}
	var c Config
	if json.Unmarshal(scanner.Bytes(), &c) != nil || validConfig(c) != nil {
		event("error", "Invalid receiver configuration")
		return
	}
	if e := os.MkdirAll(c.Folder, 0700); e != nil {
		event("error", "Cannot create received folder")
		return
	}
	ctx, cancel := signal.NotifyContext(context.Background(), os.Interrupt)
	defer cancel()
	if c.Parent > 0 && runtime.GOOS == "windows" {
		go func() {
			p, e := os.FindProcess(c.Parent)
			if e == nil {
				p.Wait()
			}
			cancel()
		}()
	}
	if c.Mode == "client" {
		runDesktopClient(ctx, c, scanner)
		return
	}
	r := newReceiver(c.Folder)
	pipe := &pipeBridge{pending: make(map[string]chan pipeReply)}
	if c.Jobs {
		r.call = pipe.call
		go func() {
			for scanner.Scan() {
				pipe.reply(scanner.Bytes())
			}
			cancel()
		}()
	}
	delay := time.Second
	for ctx.Err() == nil {
		event("connecting", "Connecting to remote service")
		e := connect(ctx, c, r)
		if ctx.Err() != nil {
			break
		}
		if e != nil {
			event("offline", "Remote service disconnected; retrying")
		}
		select {
		case <-time.After(delay):
		case <-ctx.Done():
			return
		}
		if delay < 30*time.Second {
			delay *= 2
		}
	}
}
func getICE(ctx context.Context, c Config) ([]webrtc.ICEServer, error) {
	return getICEFor(ctx, c, "pc")
}
func getICEFor(ctx context.Context, c Config, role string) ([]webrtc.ICEServer, error) {
	req, _ := http.NewRequestWithContext(ctx, "GET", strings.TrimRight(c.Service, "/")+"/rooms/"+c.Room+"/ice?role="+role, nil)
	req.Header.Set("Authorization", "Bearer "+c.PcKey)
	resp, e := (&http.Client{Timeout: 15 * time.Second}).Do(req)
	if e != nil {
		return nil, e
	}
	defer resp.Body.Close()
	if resp.StatusCode != 200 {
		return nil, fmt.Errorf("ICE status %d", resp.StatusCode)
	}
	var b struct {
		ICEServers []webrtc.ICEServer `json:"iceServers"`
	}
	e = json.NewDecoder(io.LimitReader(resp.Body, 65536)).Decode(&b)
	return b.ICEServers, e
}

var newReceiverPeer = func(settings webrtc.SettingEngine, config webrtc.Configuration) (*webrtc.PeerConnection, error) {
	return webrtc.NewAPI(webrtc.WithSettingEngine(settings)).NewPeerConnection(config)
}

func connect(parent context.Context, c Config, r *receiver) error {
	ctx, cancel := context.WithCancel(parent)
	defer cancel()
	endpoint := "wss" + strings.TrimRight(c.Service, "/")[5:] + "/rooms/" + c.Room + "/ws?role=pc"
	ws, _, e := websocket.Dial(ctx, endpoint, &websocket.DialOptions{Subprotocols: []string{"ab1", "key." + c.PcKey}})
	if e != nil {
		return e
	}
	defer ws.CloseNow()
	ws.SetReadLimit(50000)
	event("ready", "Remote receiver online")
	var writeMu sync.Mutex
	send := func(m Envelope) {
		m.MAC = sign(c.Secret, m)
		b, _ := json.Marshal(m)
		writeMu.Lock()
		defer writeMu.Unlock()
		timeout, done := context.WithTimeout(ctx, 10*time.Second)
		defer done()
		if ws.Write(timeout, websocket.MessageText, b) != nil {
			cancel()
		}
	}
	var pc *webrtc.PeerConnection
	var current string
	var seen = map[string]bool{}
	defer func() {
		if pc != nil {
			pc.Close()
		}
	}()
	// Protocol pings keep the transport alive without billing application work.
	go func() {
		t := time.NewTicker(45 * time.Second)
		defer t.Stop()
		for {
			select {
			case <-ctx.Done():
				return
			case <-t.C:
				p, done := context.WithTimeout(ctx, 10*time.Second)
				e := ws.Ping(p)
				done()
				if e != nil {
					cancel()
					return
				}
			}
		}
	}()
	for {
		_, data, e := ws.Read(ctx)
		if e != nil {
			return e
		}
		var m Envelope
		if json.Unmarshal(data, &m) != nil {
			continue
		}
		if m.Type == "presence" {
			continue
		}
		if !sessionPattern.MatchString(m.Session) || !verified(c.Secret, m) {
			continue
		}
		switch m.Type {
		case "offer":
			if seen[m.Session] || len(seen) > 1000 {
				continue
			}
			seen[m.Session] = true
			if pc != nil {
				pc.Close()
			}
			current = m.Session
			ice, e := getICE(ctx, c)
			if e != nil {
				event("error", "Could not obtain connection routes")
				continue
			}
			settings := webrtc.SettingEngine{}
			if e = settings.SetEphemeralUDPPortRange(45840, 45860); e != nil {
				return e
			}
			pc, e = newReceiverPeer(settings, webrtc.Configuration{ICEServers: ice})
			if e != nil {
				return e
			}
			session := current
			peer := pc
			pc.OnICECandidate(func(candidate *webrtc.ICECandidate) {
				if candidate != nil {
					b, _ := json.Marshal(candidate.ToJSON())
					send(Envelope{Type: "candidate", Session: session, SDP: string(b)})
				}
			})
			pc.OnConnectionStateChange(func(s webrtc.PeerConnectionState) { event("connection", s.String()) })
			pc.OnDataChannel(func(dc *webrtc.DataChannel) {
				if dc.Label() == "jobs-v2" && r.call != nil {
					attachJobs(dc, r.call)
					return
				}
				if dc.Label() != "files-v1" {
					dc.Close()
					return
				}
				r.attach(dc)
			})
			if e = peer.SetRemoteDescription(webrtc.SessionDescription{Type: webrtc.SDPTypeOffer, SDP: m.SDP}); e != nil {
				peer.Close()
				continue
			}
			answer, e := peer.CreateAnswer(nil)
			if e != nil {
				peer.Close()
				continue
			}
			if e = peer.SetLocalDescription(answer); e != nil {
				peer.Close()
				continue
			}
			send(Envelope{Type: "answer", Session: session, SDP: answer.SDP})
		case "candidate":
			if pc != nil && m.Session == current {
				var candidate webrtc.ICECandidateInit
				if json.Unmarshal([]byte(m.SDP), &candidate) == nil {
					pc.AddICECandidate(candidate)
				}
			}
		case "cancel":
			if pc != nil && m.Session == current {
				pc.Close()
				pc = nil
			}
		}
	}
}

type receiver struct {
	folder string
	call   bridgeCall
	gate   sync.Mutex
}

func newReceiver(folder string) *receiver { return &receiver{folder: folder} }
func safeName(name string) string {
	var b strings.Builder
	for _, c := range name {
		if unicode.IsControl(c) || strings.ContainsRune(`/\<>:"|?*`, c) {
			b.WriteRune('_')
		} else {
			b.WriteRune(c)
		}
	}
	s := strings.Trim(b.String(), " .")
	if s == "" {
		s = "received-file"
	}
	if len(s) > 160 {
		s = strings.ToValidUTF8(s[:160], "")
	}
	return s
}

type metadata struct {
	Type string `json:"type"`
	ID   string `json:"id"`
	Name string `json:"name"`
	Size int64  `json:"size"`
}

func (r *receiver) attach(dc *webrtc.DataChannel) {
	var mu sync.Mutex
	var file *os.File
	var meta metadata
	var locked bool
	var offset int64
	var part, final string
	reply := func(v any) { b, _ := json.Marshal(v); dc.SendText(string(b)) }
	cleanup := func() {
		if file != nil {
			file.Close()
			file = nil
		}
		if locked {
			locked = false
			r.gate.Unlock()
		}
	}
	dc.OnClose(func() { mu.Lock(); defer mu.Unlock(); cleanup() })
	dc.OnMessage(func(msg webrtc.DataChannelMessage) {
		mu.Lock()
		defer mu.Unlock()
		fail := func(message string) { cleanup(); reply(map[string]any{"type": "error", "error": message}) }
		if msg.IsString {
			if len(msg.Data) > 2048 {
				fail("Control message too large")
				return
			}
			var m metadata
			if json.Unmarshal(msg.Data, &m) != nil {
				fail("Invalid file request")
				return
			}
			switch m.Type {
			case "begin":
				cleanup()
				if !hashPattern.MatchString(m.ID) || m.Size < 0 || m.Size > 2147483648 || len(m.Name) < 1 || len(m.Name) > 500 {
					fail("Invalid file size or identity")
					return
				}
				if !r.gate.TryLock() {
					reply(map[string]any{"type": "error", "error": "PC is receiving another file"})
					return
				}
				locked = true
				meta = m
				final = filepath.Join(r.folder, m.ID[:12]+"-"+safeName(m.Name))
				part = filepath.Join(r.folder, ".partial-"+m.ID)
				if f, e := os.Open(final); e == nil {
					h := sha256.New()
					n, err := io.Copy(h, f)
					f.Close()
					if err == nil && n == m.Size && hex.EncodeToString(h.Sum(nil)) == m.ID {
						cleanup()
						reply(map[string]any{"type": "saved", "name": filepath.Base(final)})
						return
					}
					fail("A conflicting destination file exists")
					return
				}
				// Bound retained partial data and remove abandoned files after seven days.
				entries, _ := os.ReadDir(r.folder)
				var total int64
				for _, item := range entries {
					if strings.HasPrefix(item.Name(), ".partial-") {
						if info, e := item.Info(); e == nil {
							if time.Since(info.ModTime()) > 7*24*time.Hour {
								os.Remove(filepath.Join(r.folder, item.Name()))
							} else {
								total += info.Size()
							}
						}
					}
				}
				if total > 4*1024*1024*1024 {
					fail("Partial transfer storage is full")
					return
				}
				var e error
				file, e = os.OpenFile(part, os.O_CREATE|os.O_RDWR, 0600)
				if e != nil {
					fail("Cannot create received file")
					return
				}
				offset, e = file.Seek(0, io.SeekEnd)
				if e != nil || offset > m.Size {
					file.Truncate(0)
					file.Seek(0, 0)
					offset = 0
				}
				reply(map[string]any{"type": "ready", "offset": offset})
			case "finish":
				if file == nil || m.ID != meta.ID || offset != meta.Size {
					fail("File is incomplete")
					return
				}
				if file.Sync() != nil {
					fail("Could not save received file")
					return
				}
				file.Seek(0, 0)
				h := sha256.New()
				_, e := io.Copy(h, file)
				if e != nil || hex.EncodeToString(h.Sum(nil)) != meta.ID {
					file.Close()
					file = nil
					os.Remove(part)
					fail("File verification failed; retry from the start")
					return
				}
				file.Close()
				file = nil
				// Atomic publication of verified bytes, with no replacement and no second file copy.
				// Downloads normally lives on NTFS, which supports hard links.
				if e = os.Link(part, final); e != nil {
					fail("Could not commit file. Destination exists or this folder does not support hard links.")
					return
				}

				os.Remove(part)
				cleanup()
				reply(map[string]any{"type": "saved", "name": filepath.Base(final)})
				event("received", filepath.Base(final))
			case "cancel":
				cleanup()
				reply(map[string]any{"type": "cancelled"})
			default:
				fail("Unknown file command")
			}
			return
		}
		if file == nil || len(msg.Data) > 32768 || offset+int64(len(msg.Data)) > meta.Size {
			fail("Unexpected file chunk")
			return
		}
		n, e := file.Write(msg.Data)
		offset += int64(n)
		if e != nil {
			fail("PC storage write failed")
			return
		}
		reply(map[string]any{"type": "ack", "offset": offset})
	})
}
