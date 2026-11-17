// Package daemon - служба с правами администратора: запускает движок по команде приложения.
package daemon

import (
	"encoding/json"
	"fmt"
	"strings"
)

var (
	topLevelKeys  = set("log", "dns", "inbounds", "endpoints", "outbounds", "route")
	logLevels     = set("debug", "info", "warn", "error")
	inboundTypes  = set("tun")
	outboundTypes = set("direct", "socks")
	endpointTypes = set("wireguard", "awg")
	dnsTypes      = set("udp", "tcp", "local")
	// Источники правил из файлов и сети: движок от root читал бы и писал их на диск.
	routeForbidden = []string{"rule_set", "geoip", "geosite"}
)

func set(items ...string) map[string]bool {
	m := make(map[string]bool, len(items))
	for _, i := range items {
		m[i] = true
	}
	return m
}

// Sanitize принимает конфиг движка от клиента и возвращает безопасный для запуска от root.
// Служба - граница доверия: конфиг пишет процесс пользователя, а движок работает от root,
// поэтому разрешён только тот вид конфига, который собирает приложение, и никаких путей
// к файлам (журнал, сертификаты, списки правил).
func Sanitize(raw []byte) ([]byte, error) {
	var cfg map[string]any
	if err := json.Unmarshal(raw, &cfg); err != nil {
		return nil, fmt.Errorf("config json: %w", err)
	}
	for key := range cfg {
		if !topLevelKeys[key] {
			return nil, fmt.Errorf("config: top-level key %q is not allowed", key)
		}
	}

	level := "info"
	if l, ok := cfg["log"].(map[string]any); ok {
		if s, _ := l["level"].(string); logLevels[s] {
			level = s
		}
	}
	// Журнал движка идёт в stdout службы; путь к файлу клиент задать не может.
	cfg["log"] = map[string]any{"level": level, "timestamp": true}

	if err := checkTypes(cfg["inbounds"], "inbound", inboundTypes); err != nil {
		return nil, err
	}
	if err := checkTypes(cfg["outbounds"], "outbound", outboundTypes); err != nil {
		return nil, err
	}
	if err := checkTypes(cfg["endpoints"], "endpoint", endpointTypes); err != nil {
		return nil, err
	}
	if dns, ok := cfg["dns"].(map[string]any); ok {
		if err := checkTypes(dns["servers"], "dns server", dnsTypes); err != nil {
			return nil, err
		}
	}
	if route, ok := cfg["route"].(map[string]any); ok {
		for _, key := range routeForbidden {
			if _, has := route[key]; has {
				return nil, fmt.Errorf("config: route.%s is not allowed", key)
			}
		}
	}
	if err := rejectPaths(cfg); err != nil {
		return nil, err
	}
	return json.Marshal(cfg)
}

func checkTypes(list any, what string, allowed map[string]bool) error {
	items, _ := list.([]any)
	for i, item := range items {
		m, _ := item.(map[string]any)
		if t, _ := m["type"].(string); !allowed[t] {
			return fmt.Errorf("config: %s[%d] type %q is not allowed", what, i, t)
		}
	}
	return nil
}

// rejectPaths обходит всё дерево: вторая линия обороны на случай поля с путём,
// которое не закрыто списками типов выше.
func rejectPaths(v any) error {
	switch x := v.(type) {
	case map[string]any:
		for key, val := range x {
			// process_path - условие правила (чей это трафик), а не файл для движка.
			if key == "path" || key == "output" || (strings.HasSuffix(key, "_path") && key != "process_path") {
				return fmt.Errorf("config: key %q is not allowed", key)
			}
			if err := rejectPaths(val); err != nil {
				return err
			}
		}
	case []any:
		for _, item := range x {
			if err := rejectPaths(item); err != nil {
				return err
			}
		}
	}
	return nil
}
