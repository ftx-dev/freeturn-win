param(
    [ValidateSet('x64', 'arm64')][string[]]$Architectures = @('x64', 'arm64'),
    [string]$Dotnet = 'dotnet',
    [string]$MakeNSIS = "${env:ProgramFiles(x86)}\NSIS\makensis.exe"
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$previousOS = $env:GOOS
$previousArch = $env:GOARCH
Push-Location $root
try {
    & "$PSScriptRoot/Patch-Engine.ps1"
    New-Item -ItemType Directory -Force build | Out-Null
    if (-not (Test-Path build/MicrosoftEdgeWebview2Setup.exe)) {
        Invoke-WebRequest 'https://go.microsoft.com/fwlink/p/?LinkId=2124703' -OutFile build/MicrosoftEdgeWebview2Setup.exe -UseBasicParsing
    }
    $env:GOOS = 'windows'
    foreach ($arch in $Architectures) {
        $env:GOARCH = if ($arch -eq 'x64') { 'amd64' } else { 'arm64' }
        New-Item -ItemType Directory -Force "build/$arch" | Out-Null
        Push-Location helper
        try {
            go build -trimpath -ldflags '-checklinkname=0 -s -w -X github.com/samosvalishe/free-turn-proxy/mobile.version=v4.1.5' -o "../build/$arch/" ./cmd/...
            if ($LASTEXITCODE -ne 0) { throw 'Go helper build failed' }
        } finally { Pop-Location }
        Push-Location third_party/amnezia-box
        try {
            go build -trimpath -ldflags '-s -w' -tags 'with_gvisor,with_wireguard,with_awg' -o "../../build/$arch/amnezia-box.exe" ./cmd/sing-box
            if ($LASTEXITCODE -ne 0) { throw 'Engine build failed' }
        } finally { Pop-Location }
    }
    & "$PSScriptRoot/Package-Windows.ps1" -Architectures $Architectures -Dotnet $Dotnet -MakeNSIS $MakeNSIS
} finally {
    $env:GOOS = $previousOS
    $env:GOARCH = $previousArch
    Pop-Location
}
