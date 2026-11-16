//go:build windows

// freeturnd - служба Windows (LocalSystem): поднимает системный туннель по команде FreeTurn.exe.
//
//	freeturnd install    - зарегистрировать и запустить службу (зовёт установщик)
//	freeturnd uninstall  - остановить и удалить службу
//	freeturnd            - работа под диспетчером служб
package main

import (
	"context"
	"fmt"
	"log"
	"os"
	"path/filepath"
	"time"

	"golang.org/x/sys/windows"
	"golang.org/x/sys/windows/svc"
	"golang.org/x/sys/windows/svc/eventlog"
	"golang.org/x/sys/windows/svc/mgr"

	"freeturn-win/helper/internal/daemon"
)

const (
	serviceName = "FreeTurnDaemon"
	displayName = "FreeTurn — служба туннеля"
	// Совпадает с DaemonProtocol.PipeName в приложении.
	pipeName = `\\.\pipe\freeturnd`
	// Каталог состояния: конфиг движка с ключами, только SYSTEM и администраторы.
	stateSDDL = "D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;BA)"
)

func main() {
	if len(os.Args) > 1 {
		var err error
		switch os.Args[1] {
		case "install":
			err = install()
		case "uninstall":
			err = uninstall()
		default:
			err = fmt.Errorf("unknown command %q", os.Args[1])
		}
		if err != nil {
			log.Fatalf("freeturnd: %v", err)
		}
		return
	}
	if isService, err := svc.IsWindowsService(); err != nil || !isService {
		log.Fatal("freeturnd: запускается диспетчером служб; для установки - freeturnd install")
	}
	if err := svc.Run(serviceName, &handler{}); err != nil {
		log.Fatalf("freeturnd: %v", err)
	}
}

type handler struct{}

func (handler) Execute(_ []string, requests <-chan svc.ChangeRequest, status chan<- svc.Status) (bool, uint32) {
	status <- svc.Status{State: svc.StartPending}
	elog, _ := eventlog.Open(serviceName)

	cfg, err := config()
	if err != nil {
		report(elog, err)
		return true, 1
	}
	ctx, cancel := context.WithCancel(context.Background())
	defer cancel()
	done := make(chan error, 1)
	go func() { done <- daemon.Serve(ctx, cfg) }()
	status <- svc.Status{State: svc.Running, Accepts: svc.AcceptStop | svc.AcceptShutdown}

	for {
		select {
		case err := <-done:
			if err != nil {
				report(elog, err)
				return true, 2
			}
			return false, 0
		case r := <-requests:
			switch r.Cmd {
			case svc.Interrogate:
				status <- r.CurrentStatus
			case svc.Stop, svc.Shutdown:
				status <- svc.Status{State: svc.StopPending}
				cancel()
				select {
				case <-done:
				case <-time.After(10 * time.Second):
				}
				return false, 0
			}
		}
	}
}

func report(elog *eventlog.Log, err error) {
	if elog != nil {
		_ = elog.Error(1, err.Error())
	}
}

// config: движок лежит рядом со службой в Program Files (писать туда может только администратор),
// путь к нему клиент не задаёт.
func config() (daemon.Config, error) {
	exe, err := os.Executable()
	if err != nil {
		return daemon.Config{}, err
	}
	state := filepath.Join(os.Getenv("ProgramData"), "FreeTurn", "daemon")
	if err := secureDir(state); err != nil {
		return daemon.Config{}, err
	}
	return daemon.Config{
		Socket:   pipeName,
		Engine:   filepath.Join(filepath.Dir(exe), "amnezia-box.exe"),
		StateDir: state,
	}, nil
}

func secureDir(path string) error {
	if err := os.MkdirAll(path, 0o700); err != nil {
		return err
	}
	sd, err := windows.SecurityDescriptorFromString(stateSDDL)
	if err != nil {
		return err
	}
	dacl, _, err := sd.DACL()
	if err != nil {
		return err
	}
	return windows.SetNamedSecurityInfo(path, windows.SE_FILE_OBJECT,
		windows.DACL_SECURITY_INFORMATION|windows.PROTECTED_DACL_SECURITY_INFORMATION, nil, nil, dacl, nil)
}

func install() error {
	exe, err := os.Executable()
	if err != nil {
		return err
	}
	m, err := mgr.Connect()
	if err != nil {
		return err
	}
	defer m.Disconnect()
	if old, err := m.OpenService(serviceName); err == nil {
		_, _ = old.Control(svc.Stop)
		_ = old.Delete()
		old.Close()
		time.Sleep(2 * time.Second)
	}
	s, err := m.CreateService(serviceName, exe, mgr.Config{
		DisplayName: displayName,
		Description: "Поднимает системный туннель FreeTurn по команде приложения.",
		StartType:   mgr.StartAutomatic,
	})
	if err != nil {
		return err
	}
	defer s.Close()
	// Упала - перезапустить: без службы приложение не подключится.
	_ = s.SetRecoveryActions([]mgr.RecoveryAction{
		{Type: mgr.ServiceRestart, Delay: 2 * time.Second},
		{Type: mgr.ServiceRestart, Delay: 5 * time.Second},
	}, 60)
	_ = eventlog.InstallAsEventCreate(serviceName, eventlog.Error|eventlog.Warning|eventlog.Info)
	return s.Start()
}

func uninstall() error {
	m, err := mgr.Connect()
	if err != nil {
		return err
	}
	defer m.Disconnect()
	s, err := m.OpenService(serviceName)
	if err != nil {
		return nil // уже удалена
	}
	defer s.Close()
	_, _ = s.Control(svc.Stop)
	for i := 0; i < 20; i++ {
		st, err := s.Query()
		if err != nil || st.State == svc.Stopped {
			break
		}
		time.Sleep(500 * time.Millisecond)
	}
	_ = eventlog.Remove(serviceName)
	return s.Delete()
}
