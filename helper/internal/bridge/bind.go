package bridge

import "sync/atomic"

// Индекс физического интерфейса; 0 - не привязывать.
var boundIf atomic.Int32

// SetInterface задаёт интерфейс, к которому привязываются исходящие сокеты ядра.
func SetInterface(ifIndex int) { boundIf.Store(int32(ifIndex)) }
