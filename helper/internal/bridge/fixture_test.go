package bridge

import (
	"os"
	"testing"

	"github.com/samosvalishe/free-turn-proxy/mobile"
)

// Конфиги клиента проверяются настоящим декодером ядра.
func TestClientFixtureAccepted(t *testing.T) {
	for _, name := range []string{"testdata/core-config-direct.json", "testdata/core-config-relay.json"} {
		raw, err := os.ReadFile(name)
		if err != nil {
			t.Fatal(err)
		}
		if msg := mobile.ValidateConfig(string(raw)); msg != "" {
			t.Errorf("%s rejected by core: %s", name, msg)
		}
	}
}
