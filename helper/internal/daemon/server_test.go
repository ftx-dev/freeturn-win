//go:build !windows

package daemon

import (
	"bufio"
	"context"
	"encoding/json"
	"net"
	"os"
	"path/filepath"
	"strconv"
	"strings"
	"syscall"
	"testing"
	"time"
)

type testDaemon struct {
	t      *testing.T
	cfg    Config
	cancel context.CancelFunc
}

func startDaemon(t *testing.T, engine string) *testDaemon {
	t.Helper()
	abs, err := filepath.Abs(filepath.Join("testdata", engine))
	if err != nil {
		t.Fatal(err)
	}
	// Короткий путь: длина пути unix-сокета ограничена ~104 байтами.
	dir, err := os.MkdirTemp("/tmp", "ftd")
	if err != nil {
		t.Fatal(err)
	}
	t.Cleanup(func() { os.RemoveAll(dir) })
	cfg := Config{
		Socket: filepath.Join(dir, "d.sock"), Engine: abs, StateDir: filepath.Join(dir, "state"),
		OwnerUID: os.Getuid(), StartTimeout: 2 * time.Second, StopTimeout: 2 * time.Second,
	}
	ctx, cancel := context.WithCancel(context.Background())
	done := make(chan error, 1)
	go func() { done <- Serve(ctx, cfg) }()
	t.Cleanup(func() { cancel(); <-done })
	for i := 0; i < 100; i++ {
		if _, err := os.Stat(cfg.Socket); err == nil {
			return &testDaemon{t: t, cfg: cfg, cancel: cancel}
		}
		time.Sleep(10 * time.Millisecond)
	}
	t.Fatal("socket did not appear")
	return nil
}

type client struct {
	t    *testing.T
	conn net.Conn
	sc   *bufio.Scanner
}

func (d *testDaemon) dial() *client {
	d.t.Helper()
	conn, err := net.Dial("unix", d.cfg.Socket)
	if err != nil {
		d.t.Fatal(err)
	}
	d.t.Cleanup(func() { conn.Close() })
	return &client{t: d.t, conn: conn, sc: bufio.NewScanner(conn)}
}

func (c *client) next() map[string]any {
	c.t.Helper()
	c.conn.SetReadDeadline(time.Now().Add(8 * time.Second))
	if !c.sc.Scan() {
		c.t.Fatalf("connection closed: %v", c.sc.Err())
	}
	var m map[string]any
	if err := json.Unmarshal(c.sc.Bytes(), &m); err != nil {
		c.t.Fatalf("not JSON: %q", c.sc.Text())
	}
	return m
}

// call шлёт запрос и возвращает ответ, пропуская асинхронные события.
func (c *client) call(req map[string]any) map[string]any {
	c.t.Helper()
	b, _ := json.Marshal(req)
	if _, err := c.conn.Write(append(b, '\n')); err != nil {
		c.t.Fatal(err)
	}
	for {
		if m := c.next(); m["ev"] == nil {
			return m
		}
	}
}

func (d *testDaemon) enginePID() int {
	b, err := os.ReadFile(filepath.Join(d.cfg.StateDir, "engine.pid"))
	if err != nil {
		return 0
	}
	pid, _ := strconv.Atoi(strings.TrimSpace(string(b)))
	return pid
}

func alive(pid int) bool { return pid > 0 && syscall.Kill(pid, 0) == nil }

func waitGone(t *testing.T, pid int, limit time.Duration) {
	t.Helper()
	deadline := time.Now().Add(limit)
	for alive(pid) {
		if time.Now().After(deadline) {
			t.Fatalf("engine pid %d still alive after %s", pid, limit)
		}
		time.Sleep(20 * time.Millisecond)
	}
}

func TestStatusIdle(t *testing.T) {
	c := startDaemon(t, "fake-engine.sh").dial()
	r := c.call(map[string]any{"cmd": "status"})
	if r["ok"] != true || r["running"] != false || r["version"] != ProtocolVersion {
		t.Fatalf("status = %v", r)
	}
}

func TestStartStop(t *testing.T) {
	d := startDaemon(t, "fake-engine.sh")
	c := d.dial()
	if r := c.call(map[string]any{"cmd": "start", "config": minimal}); r["ok"] != true {
		t.Fatalf("start = %v", r)
	}
	pid := d.enginePID()
	if !alive(pid) {
		t.Fatal("engine not running after start")
	}
	if r := c.call(map[string]any{"cmd": "status"}); r["running"] != true {
		t.Fatalf("status = %v", r)
	}
	if r := c.call(map[string]any{"cmd": "stop"}); r["ok"] != true {
		t.Fatalf("stop = %v", r)
	}
	waitGone(t, pid, time.Second)
	if r := c.call(map[string]any{"cmd": "status"}); r["running"] != false {
		t.Fatalf("status after stop = %v", r)
	}
}

func TestConfigFileIsPrivate(t *testing.T) {
	d := startDaemon(t, "fake-engine.sh")
	d.dial().call(map[string]any{"cmd": "start", "config": minimal})
	st, err := os.Stat(filepath.Join(d.cfg.StateDir, "config.json"))
	if err != nil || st.Mode().Perm() != 0o600 {
		t.Fatalf("config.json: %v, %v", st, err)
	}
}

// Клиент исчез (приложение упало) - туннель обязан сняться сам.
func TestCloseKillsEngine(t *testing.T) {
	d := startDaemon(t, "fake-engine.sh")
	c := d.dial()
	c.call(map[string]any{"cmd": "start", "config": minimal})
	pid := d.enginePID()
	if !alive(pid) {
		t.Fatal("engine not running")
	}
	c.conn.Close()
	waitGone(t, pid, 6*time.Second)
}

func TestEngineEarlyExit(t *testing.T) {
	c := startDaemon(t, "fake-engine-fail.sh").dial()
	r := c.call(map[string]any{"cmd": "start", "config": minimal})
	if r["ok"] != false || !strings.Contains(r["err"].(string), "decode config: boom") {
		t.Fatalf("start = %v", r)
	}
}

func TestEngineStartTimeout(t *testing.T) {
	d := startDaemon(t, "fake-engine-silent.sh")
	r := d.dial().call(map[string]any{"cmd": "start", "config": minimal})
	if r["ok"] != false || !strings.Contains(r["err"].(string), "timeout") {
		t.Fatalf("start = %v", r)
	}
	waitGone(t, d.enginePID(), 3*time.Second)
}

func TestSecondClientBusy(t *testing.T) {
	d := startDaemon(t, "fake-engine.sh")
	first := d.dial()
	first.call(map[string]any{"cmd": "status"})
	second := d.dial()
	if r := second.next(); r["ok"] != false || r["err"] != "busy" {
		t.Fatalf("second client got %v", r)
	}
	// Первый ушёл - место свободно.
	first.conn.Close()
	time.Sleep(100 * time.Millisecond)
	if r := d.dial().call(map[string]any{"cmd": "status"}); r["ok"] != true {
		t.Fatalf("third client status = %v", r)
	}
}

func TestSocketMode0600(t *testing.T) {
	d := startDaemon(t, "fake-engine.sh")
	st, err := os.Stat(d.cfg.Socket)
	if err != nil || st.Mode().Perm() != 0o600 {
		t.Fatalf("socket mode = %v, %v", st.Mode().Perm(), err)
	}
}

func TestStartRejectsUnsanitary(t *testing.T) {
	d := startDaemon(t, "fake-engine.sh")
	r := d.dial().call(map[string]any{"cmd": "start", "config": `{"experimental":{}}`})
	if r["ok"] != false || !strings.Contains(r["err"].(string), "experimental") {
		t.Fatalf("start = %v", r)
	}
	if d.enginePID() != 0 {
		t.Fatal("engine was launched for a rejected config")
	}
}

func TestExitEvent(t *testing.T) {
	d := startDaemon(t, "fake-engine.sh")
	c := d.dial()
	c.call(map[string]any{"cmd": "start", "config": minimal})
	syscall.Kill(d.enginePID(), syscall.SIGKILL)
	for {
		if m := c.next(); m["ev"] == "exit" {
			break
		}
	}
	if r := c.call(map[string]any{"cmd": "status"}); r["running"] != false {
		t.Fatalf("status after engine death = %v", r)
	}
}

func TestLogEventsStreamed(t *testing.T) {
	c := startDaemon(t, "fake-engine.sh").dial()
	b, _ := json.Marshal(map[string]any{"cmd": "start", "config": minimal})
	c.conn.Write(append(b, '\n'))
	for {
		m := c.next()
		if m["ev"] == "log" && strings.Contains(m["line"].(string), "sing-box started") {
			return
		}
		if m["ev"] == nil && m["ok"] != true {
			t.Fatalf("start failed: %v", m)
		}
	}
}

func TestUnknownCommand(t *testing.T) {
	r := startDaemon(t, "fake-engine.sh").dial().call(map[string]any{"cmd": "exec", "config": "rm -rf /"})
	if r["ok"] != false {
		t.Fatalf("unknown command accepted: %v", r)
	}
}

func TestServeRemovesSocketOnExit(t *testing.T) {
	d := startDaemon(t, "fake-engine.sh")
	c := d.dial()
	c.call(map[string]any{"cmd": "start", "config": minimal})
	pid := d.enginePID()
	d.cancel()
	waitGone(t, pid, 4*time.Second)
}
