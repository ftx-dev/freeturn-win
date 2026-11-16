//go:build !windows

// freeturnd - служба launchd с правами root: поднимает системный туннель по команде FreeTurn.app.
package main

import (
	"context"
	"flag"
	"log"
	"os"
	"os/signal"
	"syscall"

	"freeturn-win/helper/internal/daemon"
)

func main() {
	var cfg daemon.Config
	flag.StringVar(&cfg.Socket, "socket", "/var/run/freeturnd.sock", "unix-сокет службы")
	flag.StringVar(&cfg.Engine, "engine", "", "путь к бинарнику движка")
	flag.StringVar(&cfg.StateDir, "state-dir", "/var/db/freeturnd", "каталог службы")
	flag.IntVar(&cfg.OwnerUID, "owner-uid", -1, "uid пользователя, которому разрешено управление")
	flag.Parse()
	if cfg.Engine == "" || cfg.OwnerUID < 0 {
		log.Fatal("freeturnd: -engine and -owner-uid are required")
	}

	ctx, stop := signal.NotifyContext(context.Background(), syscall.SIGTERM, syscall.SIGINT)
	defer stop()
	if err := daemon.Serve(ctx, cfg); err != nil {
		log.Fatalf("freeturnd: %v", err)
	}
	os.Exit(0)
}
