//go:build !windows

package bridge

// Сборка для разработки на других ОС: тесты моста гоняются без привязки к интерфейсу.
func bindToInterface(int, int) bool { return true }
