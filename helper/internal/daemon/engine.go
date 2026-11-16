package daemon

import (
	"bufio"
	"errors"
	"io"
	"os"
	"os/exec"
	"path/filepath"
	"strings"
	"sync"
	"time"
)

const (
	startedMarker = "sing-box started"
	tailLines     = 20
)

// engine - один запущенный процесс движка.
type engine struct {
	cmd     *exec.Cmd
	started chan struct{} // закрыт, когда движок сообщил о готовности
	exited  chan struct{} // закрыт после выхода процесса
	stopped bool          // остановлен нами: событие exit клиенту не нужно

	mu   sync.Mutex
	tail []string
	code int
}

// launch пишет конфиг и запускает движок; onLine получает каждую строку его вывода.
func launch(binary, stateDir string, config []byte, onLine func(string)) (*engine, error) {
	if err := os.MkdirAll(stateDir, 0o700); err != nil {
		return nil, err
	}
	cfgPath := filepath.Join(stateDir, "config.json")
	// В конфиге приватный ключ туннеля.
	if err := os.WriteFile(cfgPath, config, 0o600); err != nil {
		return nil, err
	}
	if err := os.Chmod(cfgPath, 0o600); err != nil {
		return nil, err
	}

	r, w := io.Pipe()
	cmd := exec.Command(binary, "run", "-c", cfgPath, "-D", stateDir)
	cmd.Dir = stateDir
	prepare(cmd)
	cmd.Stdout = w
	cmd.Stderr = w
	if err := cmd.Start(); err != nil {
		w.Close()
		return nil, err
	}

	e := &engine{cmd: cmd, started: make(chan struct{}), exited: make(chan struct{})}
	go func() {
		sc := bufio.NewScanner(r)
		sc.Buffer(make([]byte, 64*1024), 1024*1024)
		ready := false
		for sc.Scan() {
			line := sc.Text()
			e.mu.Lock()
			e.tail = append(e.tail, line)
			if len(e.tail) > tailLines {
				e.tail = e.tail[1:]
			}
			e.mu.Unlock()
			onLine(line)
			if !ready && strings.Contains(line, startedMarker) {
				ready = true
				close(e.started)
			}
		}
	}()
	go func() {
		err := cmd.Wait()
		w.Close()
		code := 0
		var ee *exec.ExitError
		if errors.As(err, &ee) {
			code = ee.ExitCode()
		}
		e.mu.Lock()
		e.code = code
		e.mu.Unlock()
		close(e.exited)
	}()
	return e, nil
}

func (e *engine) tailText() string {
	e.mu.Lock()
	defer e.mu.Unlock()
	return strings.Join(e.tail, "\n")
}

func (e *engine) exitCode() int {
	e.mu.Lock()
	defer e.mu.Unlock()
	return e.code
}

// stop завершает движок: мягко, а если не вышел за limit - принудительно.
func (e *engine) stop(limit time.Duration) {
	terminate(e.cmd.Process)
	select {
	case <-e.exited:
	case <-time.After(limit):
		_ = e.cmd.Process.Kill()
		<-e.exited
	}
}
