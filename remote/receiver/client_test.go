package main

import (
	"bytes"
	"context"
	"encoding/base64"
	"encoding/json"
	"errors"
	"sync"
	"testing"
)

func TestDesktopRPCFragmentedAppendAndParallelDownloads(t *testing.T) {
	r := newReceiver(t.TempDir())
	var mu sync.Mutex
	var stored []byte
	r.call = func(v map[string]any) (json.RawMessage, error) {
		mu.Lock()
		defer mu.Unlock()
		switch v["method"] {
		case "append":
			b, e := base64.StdEncoding.DecodeString(v["data"].(string))
			if e != nil {
				return nil, e
			}
			if v["offset"].(float64) != float64(len(stored)) {
				return nil, errors.New("Offset changed")
			}
			stored = append(stored, b...)
			return json.Marshal(map[string]any{"offset": len(stored), "state": "uploading"})
		case "download":
			offset := int(v["offset"].(float64))
			count := int(v["count"].(float64))
			return json.Marshal(map[string]any{"offset": offset, "data": base64.StdEncoding.EncodeToString(stored[offset : offset+count])})
		default:
			return nil, errors.New("Unsupported action")
		}
	}
	dc, _, closePair := channel(t, r)
	defer closePair()
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	rpc := newDesktopRPC(ctx, dc)
	body := bytes.Repeat([]byte("क file\n"), 30000)
	body = body[:262144]
	result, e := rpc.call(map[string]any{"method": "append", "jobId": "00000000-0000-0000-0000-000000000001", "offset": 0, "data": base64.StdEncoding.EncodeToString(body)})
	if e != nil {
		t.Fatal(e)
	}
	var status struct{ Offset int }
	json.Unmarshal(result, &status)
	if status.Offset != len(body) {
		t.Fatal("uncommitted offset")
	}
	var wg sync.WaitGroup
	for i := 0; i < 16; i++ {
		wg.Add(1)
		go func(offset int) {
			defer wg.Done()
			raw, e := rpc.call(map[string]any{"method": "download", "itemId": "00000000-0000-0000-0000-000000000001", "offset": offset, "count": 16384})
			if e != nil {
				t.Error(e)
				return
			}
			var v struct {
				Offset int
				Data   string
			}
			json.Unmarshal(raw, &v)
			actual, _ := base64.StdEncoding.DecodeString(v.Data)
			if v.Offset != offset || !bytes.Equal(actual, body[offset:offset+16384]) {
				t.Error("mismatched concurrent reply")
			}
		}(i * 16384)
	}
	wg.Wait()
	_, e = rpc.call(map[string]any{"method": "shell"})
	if e == nil {
		t.Fatal("unsupported method accepted")
	}
}
