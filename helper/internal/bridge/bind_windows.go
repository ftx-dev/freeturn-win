//go:build windows

package bridge

import (
	"encoding/binary"

	"golang.org/x/sys/windows"
)

// Опции из ws2ipdef.h; в x/sys/windows их нет.
const (
	ipUnicastIf   = 31
	ipv6UnicastIf = 31
)

// bindToInterface уводит сокет мимо wintun: сокет с IP_UNICAST_IF уходит через свой
// интерфейс, а не по маршруту по умолчанию туннеля (аналог VpnService.protect).
// Семейство сокета здесь неизвестно, поэтому ставим обе опции - хватит одной.
func bindToInterface(fd int, ifIndex int) bool {
	if ifIndex == 0 {
		return true
	}
	h := windows.Handle(fd)
	// Для IPv4 индекс передаётся в сетевом порядке байт, для IPv6 - в обычном.
	var be [4]byte
	binary.BigEndian.PutUint32(be[:], uint32(ifIndex))
	err4 := windows.SetsockoptInt(h, windows.IPPROTO_IP, ipUnicastIf, int(binary.LittleEndian.Uint32(be[:])))
	err6 := windows.SetsockoptInt(h, windows.IPPROTO_IPV6, ipv6UnicastIf, ifIndex)
	return err4 == nil || err6 == nil
}
