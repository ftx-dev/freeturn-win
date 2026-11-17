//go:build windows

package bridge

import (
	"net"
	"testing"

	"golang.org/x/sys/windows"
)

func TestBindIPv4(t *testing.T) {
	ifaces, _ := net.Interfaces()
	var idx int
	for _, i := range ifaces {
		if i.Flags&net.FlagLoopback != 0 {
			idx = i.Index
		}
	}
	if idx == 0 {
		t.Skip("no loopback interface")
	}
	s, err := windows.Socket(windows.AF_INET, windows.SOCK_DGRAM, 0)
	if err != nil {
		t.Fatal(err)
	}
	defer windows.Closesocket(s)
	if !bindToInterface(int(s), idx) {
		t.Fatal("bindToInterface = false")
	}
	got, err := windows.GetsockoptInt(s, windows.IPPROTO_IP, ipUnicastIf)
	// IP_UNICAST_IF is set in network byte order, but getsockopt returns host order.
	if err != nil || got != idx {
		t.Fatalf("IP_UNICAST_IF = %d, %v; want %d", got, err, idx)
	}
}

func TestBindZeroIsNoop(t *testing.T) {
	if !bindToInterface(0, 0) {
		t.Fatal("bindToInterface(0) = false")
	}
}
