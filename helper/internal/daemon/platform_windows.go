//go:build windows

package daemon

import (
	"net"
	"os"
	"os/exec"
	"syscall"

	"github.com/Microsoft/go-winio"
)

// PipeSDDL: полный доступ - SYSTEM и администраторам, чтение и запись - вошедшим
// в систему интерактивным пользователям. Сетевые и служебные учётки к каналу не допускаются.
const PipeSDDL = "D:P(A;;GA;;;SY)(A;;GA;;;BA)(A;;GRGW;;;IU)"

func listen(cfg Config) (net.Listener, func(), error) {
	ln, err := winio.ListenPipe(cfg.Socket, &winio.PipeConfig{SecurityDescriptor: PipeSDDL})
	if err != nil {
		return nil, nil, err
	}
	return ln, func() {}, nil
}

// peerAllowed: доступ к каналу уже отрезан его ACL.
func peerAllowed(net.Conn, Config) bool { return true }

const createNoWindow = 0x08000000

func prepare(cmd *exec.Cmd) {
	cmd.SysProcAttr = &syscall.SysProcAttr{HideWindow: true, CreationFlags: createNoWindow}
}

// shortcut: у службы нет консоли, мягкого сигнала движку не отправить - процесс сразу
// завершается; wintun-адаптер без сессии отключается, маршруты через него перестают действовать.
func terminate(p *os.Process) { _ = p.Kill() }
