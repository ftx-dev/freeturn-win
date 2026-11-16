package bridge

import (
	"bufio"
	"encoding/json"
	"fmt"
	"io"
	"net"
	"strings"
	"testing"
	"time"

	"github.com/samosvalishe/free-turn-proxy/mobile"
)

type harness struct {
	t    *testing.T
	in   *io.PipeWriter
	evs  chan map[string]any
	done chan error
}

func start(t *testing.T) *harness {
	t.Helper()
	inR, inW := io.Pipe()
	outR, outW := io.Pipe()
	h := &harness{t: t, in: inW, evs: make(chan map[string]any, 256), done: make(chan error, 1)}
	go func() { h.done <- Run(inR, outW, t.TempDir()); outW.Close() }()
	go func() {
		sc := bufio.NewScanner(outR)
		sc.Buffer(make([]byte, 1<<20), 1<<20)
		for sc.Scan() {
			var m map[string]any
			if err := json.Unmarshal(sc.Bytes(), &m); err != nil {
				t.Errorf("not JSON: %q", sc.Text())
				continue
			}
			h.evs <- m
		}
		close(h.evs)
	}()
	t.Cleanup(func() { inW.Close(); <-h.done })
	return h
}

func (h *harness) send(v any) {
	h.t.Helper()
	b, _ := json.Marshal(v)
	if _, err := h.in.Write(append(b, '\n')); err != nil {
		h.t.Fatal(err)
	}
}

// wait возвращает первое событие, для которого match истинно.
func (h *harness) wait(what string, match func(map[string]any) bool) map[string]any {
	h.t.Helper()
	deadline := time.After(10 * time.Second)
	for {
		select {
		case ev, ok := <-h.evs:
			if !ok {
				h.t.Fatalf("stream closed while waiting for %s", what)
			}
			if match(ev) {
				return ev
			}
		case <-deadline:
			h.t.Fatalf("timeout waiting for %s", what)
		}
	}
}

func ev(name string) func(map[string]any) bool {
	return func(m map[string]any) bool { return m["ev"] == name }
}

func state(s string) func(map[string]any) bool {
	return func(m map[string]any) bool { return m["ev"] == "state" && m["state"] == s }
}

// directConfig - валидный конфиг ядра без сети: direct на закрытый локальный порт.
func directConfig(t *testing.T) string {
	t.Helper()
	pc, err := net.ListenPacket("udp", "127.0.0.1:0")
	if err != nil {
		t.Fatal(err)
	}
	port := pc.LocalAddr().(*net.UDPAddr).Port
	pc.Close()

	var cfg map[string]any
	if err := json.Unmarshal([]byte(mobile.DefaultConfigJSON()), &cfg); err != nil {
		t.Fatal(err)
	}
	cfg["peer"] = "127.0.0.1:1"
	cfg["provider"] = "direct"
	cfg["clientId"] = "0123456789abcdef0123456789abcdef"
	cfg["proxy"].(map[string]any)["listen"] = fmt.Sprintf("127.0.0.1:%d", port)
	b, _ := json.Marshal(cfg)
	return string(b)
}

func TestHello(t *testing.T) {
	h := start(t)
	first := <-h.evs
	if first["ev"] != "hello" || first["version"] == "" {
		t.Fatalf("first event = %v", first)
	}
}

func TestStartBadConfig(t *testing.T) {
	h := start(t)
	h.send(map[string]any{"cmd": "start", "config": "{}"})
	e := h.wait("start error", ev("error"))
	if e["cmd"] != "start" || e["err"] == "" {
		t.Fatalf("error event = %v", e)
	}
}

func TestStartStopDirect(t *testing.T) {
	h := start(t)
	h.send(map[string]any{"cmd": "start", "config": directConfig(t), "ifIndex": 0})
	h.wait("connecting", state("connecting"))
	h.send(map[string]any{"cmd": "stop"})
	h.wait("idle", state("idle"))
}

func TestEOFExits(t *testing.T) {
	h := start(t)
	h.send(map[string]any{"cmd": "start", "config": directConfig(t), "ifIndex": 0})
	h.wait("connecting", state("connecting"))
	h.in.Close()
	select {
	case err := <-h.done:
		h.done <- err
		if err != nil {
			t.Fatalf("Run = %v", err)
		}
	case <-time.After(6 * time.Second):
		t.Fatal("Run did not return after stdin EOF")
	}
	if s := mobile.GetState().State; s != mobile.StateIdle {
		t.Fatalf("core state after EOF = %q", s)
	}
}

func TestArgs(t *testing.T) {
	h := start(t)
	h.send(map[string]any{"cmd": "args", "config": directConfig(t)})
	e := h.wait("args", ev("args"))
	if !strings.Contains(e["args"].(string), "-provider direct") {
		t.Fatalf("args = %v", e["args"])
	}
}
