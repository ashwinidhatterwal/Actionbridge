package main

import (
	"bufio"
	"context"
	"crypto/rand"
	"encoding/base64"
	"encoding/hex"
	"encoding/json"
	"errors"
	"fmt"
	"github.com/coder/websocket"
	"github.com/pion/webrtc/v4"
	"strings"
	"sync"
	"sync/atomic"
	"time"
)

// The desktop initiator uses the existing authenticated phone role; no backend change.
type desktopRPC struct {
	dc      *webrtc.DataChannel
	sendMu  sync.Mutex
	mu      sync.Mutex
	pending map[string]chan pipeReply
	next    atomic.Uint64
	ctx     context.Context
}

func newDesktopRPC(ctx context.Context, dc *webrtc.DataChannel) *desktopRPC {
	r := &desktopRPC{dc: dc, pending: map[string]chan pipeReply{}, ctx: ctx}
	dc.OnMessage(func(m webrtc.DataChannelMessage) {
		if !m.IsString || len(m.Data) > 100000 {
			return
		}
		var v struct {
			Type   string          `json:"type"`
			ID     string          `json:"requestId"`
			Result json.RawMessage `json:"result"`
			Error  string          `json:"error"`
		}
		if json.Unmarshal(m.Data, &v) != nil || v.Type != "rpc" {
			return
		}
		r.mu.Lock()
		q := r.pending[v.ID]
		r.mu.Unlock()
		if q != nil {
			select {
			case q <- pipeReply{Result: v.Result, Error: v.Error}:
			default:
			}
		}
	})
	return r
}
func (r *desktopRPC) call(value map[string]any) (json.RawMessage, error) {
	id := fmt.Sprint(r.next.Add(1))
	q := make(chan pipeReply, 1)
	r.mu.Lock()
	r.pending[id] = q
	r.mu.Unlock()
	defer func() { r.mu.Lock(); delete(r.pending, id); r.mu.Unlock() }()
	command := make(map[string]any)
	for k, v := range value {
		if k != "requestId" {
			command[k] = v
		}
	}
	command["type"] = "rpc"
	command["requestId"] = id
	b, e := json.Marshal(command)
	if e != nil || len(b) > 400000 {
		return nil, errors.New("Desktop command too large")
	}
	r.sendMu.Lock()
	if len(b) <= 32000 {
		e = r.dc.SendText(string(b))
	} else {
		total := (len(b) + 23999) / 24000
		for index := 0; index < total; index++ {
			end := min((index+1)*24000, len(b))
			fragment, _ := json.Marshal(map[string]any{"type": "fragment", "requestId": id, "index": index, "total": total, "data": base64.StdEncoding.EncodeToString(b[index*24000 : end])})
			if e = r.dc.SendText(string(fragment)); e != nil {
				break
			}
		}
	}
	r.sendMu.Unlock()
	if e != nil {
		return nil, e
	}
	select {
	case p := <-q:
		if p.Error != "" {
			return nil, errors.New(p.Error)
		}
		return p.Result, nil
	case <-r.ctx.Done():
		return nil, errors.New("Computer disconnected")
	case <-time.After(90 * time.Second):
		return nil, errors.New("Computer response timed out; transfer remains retryable")
	}
}

var newClientPeer = func() (*webrtc.PeerConnection, error) {
	settings := webrtc.SettingEngine{}
	if err := settings.SetEphemeralUDPPortRange(45840, 45860); err != nil {
		return nil, err
	}
	return webrtc.NewAPI(webrtc.WithSettingEngine(settings)).NewPeerConnection(webrtc.Configuration{})
}

func clientConnect(parent context.Context, c Config) (*desktopRPC, func(), error) {
	ctx, cancel := context.WithTimeout(parent, 45*time.Second)
	endpoint := "wss" + strings.TrimRight(c.Service, "/")[5:] + "/rooms/" + c.Room + "/ws?role=phone"
	ws, _, err := websocket.Dial(ctx, endpoint, &websocket.DialOptions{Subprotocols: []string{"ab1", "key." + c.PcKey}})
	if err != nil {
		cancel()
		return nil, nil, err
	}
	ws.SetReadLimit(50000)
	// Use a persistent lifetime after the bounded initial handshake.
	lifetime, end := context.WithCancel(parent)
	pc, err := newClientPeer()
	if err != nil {
		ws.CloseNow()
		cancel()
		end()
		return nil, nil, err
	}
	closeAll := func() { end(); cancel(); pc.Close(); ws.CloseNow() }
	var sendMu sync.Mutex
	go func() {
		ticker := time.NewTicker(45 * time.Second)
		defer ticker.Stop()
		for {
			select {
			case <-ticker.C:
				sendMu.Lock()
				timeout, done := context.WithTimeout(lifetime, 10*time.Second)
				e := ws.Write(timeout, websocket.MessageText, []byte("ping"))
				done()
				sendMu.Unlock()
				if e != nil {
					end()
					return
				}
			case <-lifetime.Done():
				return
			}
		}
	}()
	send := func(m Envelope) error {
		m.MAC = sign(c.Secret, m)
		raw, _ := json.Marshal(m)
		sendMu.Lock()
		defer sendMu.Unlock()
		deadline, done := context.WithTimeout(lifetime, 10*time.Second)
		defer done()
		return ws.Write(deadline, websocket.MessageText, raw)
	}
	sessionBytes := make([]byte, 16)
	rand.Read(sessionBytes)
	session := hex.EncodeToString(sessionBytes)
	opened := make(chan struct{})
	dc, err := pc.CreateDataChannel("jobs-v2", nil)
	if err != nil {
		closeAll()
		return nil, nil, err
	}
	rpc := newDesktopRPC(lifetime, dc)
	var once sync.Once
	dc.OnOpen(func() { once.Do(func() { close(opened) }) })
	dc.OnClose(end)
	pc.OnConnectionStateChange(func(s webrtc.PeerConnectionState) {
		if s == webrtc.PeerConnectionStateFailed || s == webrtc.PeerConnectionStateClosed {
			end()
		}
	})
	var offered bool
	var local []string
	var candidateMu sync.Mutex
	pc.OnICECandidate(func(candidate *webrtc.ICECandidate) {
		if candidate == nil {
			return
		}
		b, _ := json.Marshal(candidate.ToJSON())
		candidateMu.Lock()
		defer candidateMu.Unlock()
		if offered {
			if send(Envelope{Type: "candidate", Session: session, SDP: string(b)}) != nil {
				end()
			}
		} else {
			local = append(local, string(b))
		}
	})
	errorsQ := make(chan error, 1)
	go func() {
		var started bool
		var remote []webrtc.ICECandidateInit
		for {
			_, raw, e := ws.Read(lifetime)
			if e != nil {
				select {
				case errorsQ <- e:
				default:
				}
				end()
				return
			}
			if string(raw) == "pong" {
				continue
			}
			var m Envelope
			if json.Unmarshal(raw, &m) != nil {
				continue
			}
			if m.Type == "presence" {
				if !m.Online {
					if started {
						end()
						return
					}
					continue
				}
				if started {
					continue
				}
				started = true
				ice, e := getICEFor(ctx, c, "phone")
				if e == nil {
					e = pc.SetConfiguration(webrtc.Configuration{ICEServers: ice})
				}
				var offer webrtc.SessionDescription
				if e == nil {
					offer, e = pc.CreateOffer(nil)
				}
				if e == nil {
					e = pc.SetLocalDescription(offer)
				}
				if e == nil {
					e = send(Envelope{Type: "offer", Session: session, SDP: offer.SDP})
				}
				if e != nil {
					select {
					case errorsQ <- e:
					default:
					}
					end()
					return
				}
				candidateMu.Lock()
				offered = true
				for _, sdp := range local {
					if send(Envelope{Type: "candidate", Session: session, SDP: sdp}) != nil {
						end()
					}
				}
				local = nil
				candidateMu.Unlock()
				continue
			}
			if m.Session != session || !verified(c.Secret, m) {
				continue
			}
			switch m.Type {
			case "answer":
				if pc.SetRemoteDescription(webrtc.SessionDescription{Type: webrtc.SDPTypeAnswer, SDP: m.SDP}) != nil {
					end()
					return
				}
				for _, candidate := range remote {
					pc.AddICECandidate(candidate)
				}
				remote = nil
			case "candidate":
				var candidate webrtc.ICECandidateInit
				if json.Unmarshal([]byte(m.SDP), &candidate) == nil {
					if pc.RemoteDescription() != nil {
						pc.AddICECandidate(candidate)
					} else {
						if len(remote) < 256 {
							remote = append(remote, candidate)
						}
					}
				}
			}
		}
	}()
	select {
	case <-opened:
		cancel()
		return rpc, closeAll, nil
	case err := <-errorsQ:
		closeAll()
		return nil, nil, err
	case <-ctx.Done():
		closeAll()
		return nil, nil, errors.New("Computer is offline or direct connection unavailable")
	case <-lifetime.Done():
		closeAll()
		return nil, nil, errors.New("Computer connection closed")
	}
}
func runDesktopClient(ctx context.Context, c Config, scanner *bufio.Scanner) {
	rpc, closeAll, e := clientConnect(ctx, c)
	if e != nil {
		event("error", e.Error())
		return
	}
	defer closeAll()
	event("ready", "Connected to computer")
	go func() { <-rpc.ctx.Done(); event("offline", "Computer disconnected") }()
	var calls sync.WaitGroup
	limit := make(chan struct{}, 32)
	for scanner.Scan() {
		var value map[string]any
		if json.Unmarshal(scanner.Bytes(), &value) != nil {
			continue
		}
		id, _ := value["requestId"].(string)
		if len(id) > 80 {
			continue
		}
		select {
		case limit <- struct{}{}:
		case <-rpc.ctx.Done():
			return
		}
		calls.Add(1)
		go func(v map[string]any, id string) {
			defer calls.Done()
			defer func() { <-limit }()
			result, e := rpc.call(v)
			if e != nil {
				output(map[string]any{"requestId": id, "error": e.Error()})
			} else {
				output(map[string]any{"requestId": id, "result": result})
			}
		}(value, id)
	}
	closeAll()
	calls.Wait()
}
