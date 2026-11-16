// freeturn-core - процесс ядра free-turn-proxy под управлением FreeTurn.app.
package main

import (
	"flag"
	"fmt"
	"os"

	"freeturn-win/helper/internal/bridge"
)

func main() {
	stateDir := flag.String("state-dir", "", "каталог состояния ядра")
	flag.Parse()
	if *stateDir == "" {
		fmt.Fprintln(os.Stderr, "freeturn-core: -state-dir is required")
		os.Exit(2)
	}
	if err := os.MkdirAll(*stateDir, 0o700); err != nil {
		fmt.Fprintln(os.Stderr, "freeturn-core:", err)
		os.Exit(1)
	}
	if err := bridge.Run(os.Stdin, os.Stdout, *stateDir); err != nil {
		fmt.Fprintln(os.Stderr, "freeturn-core:", err)
		os.Exit(1)
	}
}
