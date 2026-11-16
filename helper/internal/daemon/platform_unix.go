//go:build !windows

package daemon

import (
	"errors"
	"net"
	"os"
	"os/exec"
	"syscall"

	"golang.org/x/sys/unix"
)

func listen(cfg Config) (net.Listener, func(), error) {
	_ = os.Remove(cfg.Socket) // след прошлого запуска
	ln, err := net.Listen("unix", cfg.Socket)
	if err != nil {
		return nil, nil, err
	}
	cleanup := func() { _ = os.Remove(cfg.Socket) }
	if err := os.Chmod(cfg.Socket, 0o600); err != nil {
		ln.Close()
		return nil, nil, err
	}
	if err := os.Chown(cfg.Socket, cfg.OwnerUID, -1); err != nil {
		ln.Close()
		return nil, nil, err
	}
	return ln, cleanup, nil
}

// peerAllowed: права файла сокета уже закрывают чужих; это вторая проверка по uid собеседника.
func peerAllowed(conn net.Conn, cfg Config) bool {
	uid, err := peerUID(conn)
	return err == nil && uid == cfg.OwnerUID
}

func peerUID(conn net.Conn) (int, error) {
	uc, ok := conn.(*net.UnixConn)
	if !ok {
		return -1, errors.New("not a unix socket")
	}
	raw, err := uc.SyscallConn()
	if err != nil {
		return -1, err
	}
	uid := -1
	var credErr error
	if err := raw.Control(func(fd uintptr) {
		cred, e := unix.GetsockoptXucred(int(fd), unix.SOL_LOCAL, unix.LOCAL_PEERCRED)
		if e != nil {
			credErr = e
			return
		}
		uid = int(cred.Uid)
	}); err != nil {
		return -1, err
	}
	return uid, credErr
}

func prepare(cmd *exec.Cmd) { cmd.Env = []string{"PATH=/usr/bin:/bin:/usr/sbin:/sbin"} }

func terminate(p *os.Process) { _ = p.Signal(syscall.SIGTERM) }
