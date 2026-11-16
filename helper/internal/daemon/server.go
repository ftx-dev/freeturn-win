package daemon

import (
	"bufio"
	"context"
	"encoding/json"
	"fmt"
	"net"
	"sync"
	"time"
)

// ProtocolVersion меняется вместе с протоколом сокета: приложение сверяет её и
// предлагает переустановить службу при расхождении.
const ProtocolVersion = "1"

type Config struct {
	Socket   string
	Engine   string
	StateDir string
	// OwnerUID - пользователь, установивший службу (unix). На Windows доступ задаёт ACL канала.
	OwnerUID     int
	StartTimeout time.Duration
	StopTimeout  time.Duration
}

type request struct {
	Cmd    string `json:"cmd"`
	Config string `json:"config"`
}

type message map[string]any

type server struct {
	cfg Config

	mu     sync.Mutex
	client net.Conn // единственный активный клиент
	engine *engine
}

// Serve слушает сокет до отмены ctx. Движок живёт, только пока жив клиент.
func Serve(ctx context.Context, cfg Config) error {
	if cfg.StartTimeout == 0 {
		cfg.StartTimeout = 20 * time.Second
	}
	if cfg.StopTimeout == 0 {
		cfg.StopTimeout = 5 * time.Second
	}
	ln, cleanup, err := listen(cfg)
	if err != nil {
		return err
	}
	defer cleanup()

	s := &server{cfg: cfg}
	var wg sync.WaitGroup
	go func() {
		<-ctx.Done()
		ln.Close()
		s.mu.Lock()
		if s.client != nil {
			s.client.Close()
		}
		s.mu.Unlock()
	}()
	for {
		conn, err := ln.Accept()
		if err != nil {
			wg.Wait()
			s.stopEngine()
			if ctx.Err() != nil {
				return nil
			}
			return err
		}
		wg.Add(1)
		go func() {
			defer wg.Done()
			s.handle(conn)
		}()
	}
}

func (s *server) handle(conn net.Conn) {
	defer conn.Close()
	enc := json.NewEncoder(conn)
	var writeMu sync.Mutex
	send := func(m message) {
		writeMu.Lock()
		defer writeMu.Unlock()
		_ = enc.Encode(m)
	}

	if !peerAllowed(conn, s.cfg) {
		send(message{"ok": false, "err": "forbidden"})
		return
	}
	s.mu.Lock()
	if s.client != nil {
		s.mu.Unlock()
		send(message{"ok": false, "err": "busy"})
		return
	}
	s.client = conn
	s.mu.Unlock()
	defer func() {
		// Клиент ушёл (в том числе упал) - туннель без хозяина оставлять нельзя.
		s.stopEngine()
		s.mu.Lock()
		s.client = nil
		s.mu.Unlock()
	}()

	sc := bufio.NewScanner(conn)
	sc.Buffer(make([]byte, 64*1024), 4*1024*1024)
	for sc.Scan() {
		var req request
		if err := json.Unmarshal(sc.Bytes(), &req); err != nil {
			send(message{"ok": false, "err": "bad request: " + err.Error()})
			continue
		}
		switch req.Cmd {
		case "status":
			send(message{"ok": true, "running": s.running(), "version": ProtocolVersion})
		case "start":
			if err := s.start(req.Config, send); err != nil {
				send(message{"ok": false, "err": err.Error()})
			} else {
				send(message{"ok": true})
			}
		case "stop":
			s.stopEngine()
			send(message{"ok": true})
		default:
			send(message{"ok": false, "err": fmt.Sprintf("unknown command %q", req.Cmd)})
		}
	}
}

func (s *server) running() bool {
	s.mu.Lock()
	defer s.mu.Unlock()
	return s.engine != nil
}

func (s *server) start(rawConfig string, send func(message)) error {
	config, err := Sanitize([]byte(rawConfig))
	if err != nil {
		return err
	}
	s.stopEngine()

	e, err := launch(s.cfg.Engine, s.cfg.StateDir, config, func(line string) {
		send(message{"ev": "log", "line": line})
	})
	if err != nil {
		return fmt.Errorf("launch engine: %w", err)
	}

	select {
	case <-e.started:
	case <-e.exited:
		return fmt.Errorf("engine exited with code %d: %s", e.exitCode(), e.tailText())
	case <-time.After(s.cfg.StartTimeout):
		e.stop(s.cfg.StopTimeout)
		return fmt.Errorf("engine start timeout after %s: %s", s.cfg.StartTimeout, e.tailText())
	}

	s.mu.Lock()
	s.engine = e
	s.mu.Unlock()
	go func() {
		<-e.exited
		s.mu.Lock()
		own := s.engine == e
		if own {
			s.engine = nil
		}
		stopped := e.stopped
		s.mu.Unlock()
		// Движок умер сам - клиент должен свернуть ядро.
		if own && !stopped {
			send(message{"ev": "exit", "code": e.exitCode(), "tail": e.tailText()})
		}
	}()
	return nil
}

func (s *server) stopEngine() {
	s.mu.Lock()
	e := s.engine
	s.engine = nil
	if e != nil {
		e.stopped = true
	}
	s.mu.Unlock()
	if e != nil {
		e.stop(s.cfg.StopTimeout)
	}
}
