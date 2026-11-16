package daemon

import (
	"encoding/json"
	"os"
	"strings"
	"testing"
)

const minimal = `{"log":{"level":"debug"},"dns":{"servers":[{"type":"udp","tag":"vpn-dns","server":"1.1.1.1","detour":"vpn"},{"type":"local","tag":"local-dns"}],"final":"vpn-dns"},` +
	`"inbounds":[{"type":"tun","tag":"tun-in","address":["172.19.0.1/30"],"mtu":1280,"auto_route":true}],` +
	`"endpoints":[{"type":"wireguard","tag":"vpn","address":["10.0.0.2/32"],"private_key":"k","peers":[{"address":"127.0.0.1","port":41000,"public_key":"p"}]}],` +
	`"outbounds":[{"type":"direct","tag":"direct"}],` +
	`"route":{"final":"vpn","rules":[{"action":"sniff"},{"process_path":["/usr/bin/curl"],"outbound":"direct"},{"process_path_regex":["^/Applications/A\\.app/"],"outbound":"direct"}]}}`

// edit меняет валидный конфиг: сначала разбор, потом правка, потом обратно в JSON.
func edit(t *testing.T, change func(cfg map[string]any)) []byte {
	t.Helper()
	var cfg map[string]any
	if err := json.Unmarshal([]byte(minimal), &cfg); err != nil {
		t.Fatal(err)
	}
	change(cfg)
	b, _ := json.Marshal(cfg)
	return b
}

func mustReject(t *testing.T, raw []byte, want string) {
	t.Helper()
	_, err := Sanitize(raw)
	if err == nil {
		t.Fatalf("accepted, want error containing %q", want)
	}
	if !strings.Contains(err.Error(), want) {
		t.Fatalf("error = %q, want it to contain %q", err, want)
	}
}

func TestAcceptsGeneratedShape(t *testing.T) {
	// Эталон пишет Swift (EngineConfig.build), см. EngineConfigTests.
	raw, err := os.ReadFile("testdata/engine.json")
	if err != nil {
		t.Fatal(err)
	}
	if _, err := Sanitize(raw); err != nil {
		t.Fatalf("generated config rejected: %v", err)
	}
}

func TestAcceptsMinimalWithProcessRules(t *testing.T) {
	if _, err := Sanitize([]byte(minimal)); err != nil {
		t.Fatal(err)
	}
}

func TestLogIsRewritten(t *testing.T) {
	out, err := Sanitize(edit(t, func(c map[string]any) {
		c["log"] = map[string]any{"level": "debug", "output": "/etc/passwd"}
	}))
	if err != nil {
		t.Fatal(err)
	}
	var got struct {
		Log map[string]any `json:"log"`
	}
	_ = json.Unmarshal(out, &got)
	if _, has := got.Log["output"]; has {
		t.Fatal("log.output survived")
	}
	if got.Log["level"] != "debug" || got.Log["timestamp"] != true {
		t.Fatalf("log = %v", got.Log)
	}
}

func TestUnknownLogLevelBecomesInfo(t *testing.T) {
	out, _ := Sanitize(edit(t, func(c map[string]any) { c["log"] = map[string]any{"level": "trace"} }))
	if !strings.Contains(string(out), `"level":"info"`) {
		t.Fatalf("out = %s", out)
	}
}

func TestRejectsUnknownTopLevel(t *testing.T) {
	mustReject(t, edit(t, func(c map[string]any) {
		c["experimental"] = map[string]any{"cache_file": map[string]any{"enabled": true}}
	}), "experimental")
}

func TestRejectsPathKeys(t *testing.T) {
	mustReject(t, edit(t, func(c map[string]any) {
		c["endpoints"].([]any)[0].(map[string]any)["tls"] = map[string]any{"certificate_path": "/etc/x"}
	}), "certificate_path")
	mustReject(t, edit(t, func(c map[string]any) {
		c["route"].(map[string]any)["rules"] = []any{map[string]any{"path": "/x"}}
	}), "path")
}

func TestRejectsRuleSetsAndGeoFiles(t *testing.T) {
	for _, key := range []string{"rule_set", "geoip", "geosite"} {
		mustReject(t, edit(t, func(c map[string]any) {
			c["route"].(map[string]any)[key] = []any{map[string]any{"type": "remote", "url": "http://x"}}
		}), key)
	}
}

func TestRejectsForeignTypes(t *testing.T) {
	mustReject(t, edit(t, func(c map[string]any) {
		c["inbounds"] = []any{map[string]any{"type": "mixed", "listen": "0.0.0.0", "listen_port": 1080}}
	}), "inbound")
	mustReject(t, edit(t, func(c map[string]any) {
		c["outbounds"] = []any{map[string]any{"type": "socks", "server": "1.2.3.4"}}
	}), "outbound")
	mustReject(t, edit(t, func(c map[string]any) {
		c["endpoints"] = []any{map[string]any{"type": "tailscale"}}
	}), "endpoint")
	mustReject(t, edit(t, func(c map[string]any) {
		c["dns"].(map[string]any)["servers"] = []any{map[string]any{"type": "hosts", "path": []any{"/etc/hosts"}}}
	}), "dns server")
}

func TestRejectsBadJSON(t *testing.T) {
	mustReject(t, []byte(`{"log":`), "json")
	mustReject(t, []byte(`[1,2]`), "json")
}
