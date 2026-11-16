param(
    [ValidateSet('x64', 'arm64')][string[]]$Architectures = @('x64', 'arm64'),
    [string]$Dotnet = 'dotnet',
    [string]$MakeNSIS = "${env:ProgramFiles(x86)}\NSIS\makensis.exe"
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
[xml]$props = Get-Content Directory.Build.props
$version = [string]$props.Project.PropertyGroup.Version
New-Item -ItemType Directory -Force dist | Out-Null
foreach ($arch in $Architectures) {
    $output = "build/release-$version-$arch"
    # Always publish current sources; never package an old build/x64 client.
    & $Dotnet publish src/FreeTurnX -c Release -r "win-$arch" -o $output "-p:Version=$version"
    if ($LASTEXITCODE -ne 0) { throw 'Client publish failed' }
    foreach ($binary in @('freeturn-core.exe', 'freeturnd.exe', 'amnezia-box.exe')) {
        Copy-Item "build/$arch/$binary" "$output/$binary" -Force
    }
    if ((Get-Item "$output/FreeTurnX.exe").VersionInfo.FileVersion -ne "$version.0") {
        throw 'Client version does not match installer version'
    }
    & $MakeNSIS /INPUTCHARSET UTF8 /V2 "/DVERSION=$version" "/DARCH=$arch" "/DSRC=$output" "/DOUT=dist/FreeTurnX-$version-$arch-setup.exe" "$root/installer/freeturnx.nsi"
    if ($LASTEXITCODE -ne 0) { throw 'Installer compilation failed' }
}
$sums = Get-ChildItem "dist/FreeTurnX-$version-*-setup.exe" | Sort-Object Name | ForEach-Object {
    '{0}  {1}' -f (Get-FileHash $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant(), $_.Name
}
[IO.File]::WriteAllLines("$root/dist/SHA256SUMS.txt", $sums, (New-Object Text.UTF8Encoding($false)))
