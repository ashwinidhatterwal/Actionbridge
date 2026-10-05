package main

import (
	"encoding/base64"
	"encoding/json"
	"errors"
	"fmt"
	"github.com/pion/webrtc/v4"
	"sync"
	"sync/atomic"
	"time"
)

// The transport never launches actions. The parent dispatches into its existing
// durable Transfers journal, shared with LAN jobs, over private inherited pipes.
type bridgeCall func(map[string]any) (json.RawMessage, error)
type pipeBridge struct {
	mu      sync.Mutex
	pending map[string]chan pipeReply
	next    atomic.Uint64
}
type pipeReply struct {
	ID     string          `json:"rpcId"`
	Result json.RawMessage `json:"result"`
	Error  string          `json:"error"`
}

var outputMu sync.Mutex

func output(v any) {
	b, _ := json.Marshal(v)
	outputMu.Lock()
	defer outputMu.Unlock()
	fmt.Println(string(b))
}
func (b *pipeBridge) reply(line []byte) {
	var p pipeReply
	if json.Unmarshal(line, &p) != nil {
		return
	}
	b.mu.Lock()
	q := b.pending[p.ID]
	b.mu.Unlock()
	if q != nil {
		select {
		case q <- p:
		default:
		}
	}
}
func (b *pipeBridge) call(v map[string]any) (json.RawMessage, error) {
	id := fmt.Sprint(b.next.Add(1))
	q := make(chan pipeReply, 1)
	b.mu.Lock()
	b.pending[id] = q
	b.mu.Unlock()
	defer func() { b.mu.Lock(); delete(b.pending, id); b.mu.Unlock() }()
	v["rpcId"] = id
	output(v)
	select {
	case p := <-q:
		if p.Error != "" {
			return nil, errors.New(p.Error)
		}
		return p.Result, nil
	case <-time.After(90 * time.Second):
		return nil, errors.New("PC action response timed out; reconnect and check activity before retrying")
	}
}
func attachJobs(dc *webrtc.DataChannel, call bridgeCall) {
	var mu sync.Mutex
	var job string
	var received, committed, size int64
	var buffer []byte
	var fragments []byte
	var fragmentID string
	var fragmentIndex, fragmentTotal int
	reply := func(v any) { b, _ := json.Marshal(v); dc.SendText(string(b)) }
	flush := func() error {
		if len(buffer) == 0 {
			return nil
		}
		_, err := call(map[string]any{"method": "append", "jobId": job, "offset": committed, "data": base64.StdEncoding.EncodeToString(buffer)})
		if err != nil {
			return err
		}
		committed = received
		buffer = nil
		reply(map[string]any{"type": "ack", "offset": committed})
		return nil
	}
	dc.OnMessage(func(m webrtc.DataChannelMessage) {
		mu.Lock()
		defer mu.Unlock()
		fail := func(e error) { job = ""; buffer = nil; reply(map[string]any{"type": "error", "error": e.Error()}) }
		if !m.IsString {
			if job == "" || len(m.Data) > 32768 || received+int64(len(m.Data)) > size {
				fail(errors.New("Unexpected file chunk"))
				return
			}
			buffer = append(buffer, m.Data...)
			received += int64(len(m.Data))
			if len(buffer) >= 262144 || received == size {
				if e := flush(); e != nil {
					fail(e)
				}
			}
			return
		}
		if len(m.Data) > 100000 {
			fail(errors.New("Control message too large"))
			return
		}
		var v map[string]any
		if json.Unmarshal(m.Data, &v) != nil {
			fail(errors.New("Invalid action request"))
			return
		}
		typ, _ := v["type"].(string)
		if typ == "fragment" {
			rid, _ := v["requestId"].(string)
			index, ok := v["index"].(float64)
			total, tok := v["total"].(float64)
			encoded, eok := v["data"].(string)
			if !ok || !tok || !eok || total < 1 || total > 20 || total != float64(int(total)) || index != float64(int(index)) || len(rid) > 80 {
				fail(errors.New("Invalid command fragment"))
				return
			}
			if index == 0 {
				fragments = nil
				fragmentID = rid
				fragmentIndex = 0
				fragmentTotal = int(total)
			}
			if rid != fragmentID || int(index) != fragmentIndex || int(total) != fragmentTotal {
				fail(errors.New("Command fragments out of order"))
				return
			}
			chunk, e := base64.StdEncoding.DecodeString(encoded)
			if e != nil || len(chunk) > 24000 || len(fragments)+len(chunk) > 400000 {
				fail(errors.New("Command too large"))
				return
			}
			fragments = append(fragments, chunk...)
			fragmentIndex++
			if fragmentIndex < fragmentTotal {
				return
			}
			if json.Unmarshal(fragments, &v) != nil {
				fail(errors.New("Invalid fragmented command"))
				return
			}
			fragments = nil
			typ, _ = v["type"].(string)
			if typ != "rpc" {
				fail(errors.New("Only action requests may be fragmented"))
				return
			}
		}
		if typ == "rpc" {
			rid, _ := v["requestId"].(string)
			method, _ := v["method"].(string)
			if method != "append" && method != "printers" && method != "create" && method != "status" && method != "finish" && method != "cancel" && method != "outbox" && method != "download" && method != "received" {
				reply(map[string]any{"type": "rpc", "requestId": rid, "error": "Unsupported action"})
				return
			}
			result, e := call(v)
			if e != nil {
				reply(map[string]any{"type": "rpc", "requestId": rid, "error": e.Error()})
			} else {
				reply(map[string]any{"type": "rpc", "requestId": rid, "result": result})
			}
			return
		}
		if typ == "upload" {
			candidate, _ := v["jobId"].(string)
			result, e := call(map[string]any{"method": "status", "jobId": candidate})
			if e != nil {
				fail(e)
				return
			}
			var j struct {
				Offset  int64  `json:"offset"`
				State   string `json:"state"`
				Request struct {
					Size int64 `json:"size"`
				} `json:"request"`
			}
			if json.Unmarshal(result, &j) != nil || j.State != "uploading" {
				fail(errors.New("Transfer is no longer uploading"))
				return
			}
			job = candidate
			received = j.Offset
			committed = j.Offset
			size = j.Request.Size
			buffer = nil
			reply(map[string]any{"type": "ready", "offset": committed})
			return
		}
		fail(errors.New("Unsupported transport command"))
	})
}
