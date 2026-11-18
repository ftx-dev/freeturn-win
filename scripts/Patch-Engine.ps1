$ErrorActionPreference = 'Stop'
$path = Join-Path (Split-Path $PSScriptRoot -Parent) 'third_party/amnezia-box/transport/wireguard/endpoint.go'
$text = [IO.File]::ReadAllText($path)
$marker = '// FreeTurn: use connected UDP for the Windows loopback bridge.'
if ($text.Contains($marker)) { return }
$needle = 'wgListener, isWgListener := common.Cast[conn.Listener](e.options.Dialer)'
if (!$text.Contains($needle)) { throw 'WireGuard bind selection changed upstream; review the patch.' }
$text = $text.Replace('"os"', '"os"' + "`n`t" + '"runtime"')
$text = $text.Replace($needle, $needle + "`n`t" + $marker + @'

	if runtime.GOOS == "windows" && len(e.peers) == 1 && e.peers[0].endpoint.Addr().IsLoopback() {
		isWgListener = false
	}
'@)
[IO.File]::WriteAllText($path, $text, (New-Object Text.UTF8Encoding($false)))
