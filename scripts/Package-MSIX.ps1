param(
    [Parameter(Mandatory)][string]$IdentityName,
    [Parameter(Mandatory)][string]$Publisher,
    [Parameter(Mandatory)][string]$PublisherDisplayName,
    [ValidateSet('x64','arm64')][string[]]$Architectures = @('x64','arm64'),
    [string]$Dotnet = 'dotnet',
    [string]$MakeAppx,
    [string]$OutputDirectory = 'dist/msix'
)
$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
Set-Location $root
if (!$MakeAppx) {
    $MakeAppx = Get-ChildItem "${env:ProgramFiles(x86)}/Windows Kits/10/bin/*/x64/makeappx.exe" |
        Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
}
if (!$MakeAppx) { throw 'Install the Windows SDK packaging tools (MakeAppx.exe).' }
[xml]$props = Get-Content Directory.Build.props
$version = [string]$props.Project.PropertyGroup.Version
Add-Type -AssemblyName System.Drawing
New-Item -ItemType Directory -Force $OutputDirectory | Out-Null
$destination = (Resolve-Path -LiteralPath $OutputDirectory).Path
foreach ($arch in $Architectures) {
    $stage = "$root/build/msix-$version-$arch-$([Guid]::NewGuid().ToString('N'))"
    New-Item -ItemType Directory -Force "$stage/Assets" | Out-Null
    & $Dotnet publish src/FreeTurnX -c Release -r "win-$arch" -o $stage -p:PublishSingleFile=false
    if ($LASTEXITCODE) { throw 'MSIX client publish failed' }
    foreach ($binary in @('freeturn-core.exe','freeturnd.exe','amnezia-box.exe')) {
        Copy-Item "build/$arch/$binary" "$stage/$binary" -Force
    }
    $source = [Drawing.Image]::FromFile("$root/src/FreeTurnX/Resources/logo-x.png")
    try {
        foreach ($size in @(44,50,150)) {
            $bitmap = New-Object Drawing.Bitmap($size,$size)
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.DrawImage($source,0,0,$size,$size)
                $bitmap.Save("$stage/Assets/Logo$size.png",[Drawing.Imaging.ImageFormat]::Png)
            } finally { $graphics.Dispose(); $bitmap.Dispose() }
        }
    } finally { $source.Dispose() }
    $name = [Security.SecurityElement]::Escape($IdentityName)
    $pub = [Security.SecurityElement]::Escape($Publisher)
    $display = [Security.SecurityElement]::Escape($PublisherDisplayName)
    $manifest = @"
<?xml version="1.0" encoding="utf-8"?>
<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"
 xmlns:uap="http://schemas.microsoft.com/appx/manifest/uap/windows10"
 xmlns:desktop6="http://schemas.microsoft.com/appx/manifest/desktop/windows10/6"
 xmlns:rescap="http://schemas.microsoft.com/appx/manifest/foundation/windows10/restrictedcapabilities"
 IgnorableNamespaces="uap desktop6 rescap">
 <Identity Name="$name" Publisher="$pub" Version="$version.0" ProcessorArchitecture="$arch" />
 <Properties><DisplayName>FreeTurnX</DisplayName><PublisherDisplayName>$display</PublisherDisplayName><Logo>Assets\Logo50.png</Logo></Properties>
 <Dependencies><TargetDeviceFamily Name="Windows.Desktop" MinVersion="10.0.18362.0" MaxVersionTested="10.0.26100.0" /></Dependencies>
 <Resources><Resource Language="ru-RU"/><Resource Language="en-US"/></Resources>
 <Applications><Application Id="FreeTurnX" Executable="FreeTurnX.exe" EntryPoint="Windows.FullTrustApplication">
  <uap:VisualElements DisplayName="FreeTurnX" Description="FreeTurnX VPN client" BackgroundColor="transparent" Square150x150Logo="Assets\Logo150.png" Square44x44Logo="Assets\Logo44.png" />
  <Extensions>
   <uap:Extension Category="windows.protocol"><uap:Protocol Name="freeturn"><uap:DisplayName>FreeTurnX</uap:DisplayName></uap:Protocol></uap:Extension>
   <desktop6:Extension Category="windows.service" Executable="freeturnd.exe" EntryPoint="Windows.FullTrustApplication">
   <desktop6:Service Name="FreeTurnDaemon" StartupType="auto" StartAccount="localSystem" />
  </desktop6:Extension></Extensions>
 </Application></Applications>
 <Capabilities><rescap:Capability Name="runFullTrust"/><rescap:Capability Name="packagedServices"/><rescap:Capability Name="localSystemServices"/></Capabilities>
</Package>
"@
    [IO.File]::WriteAllText("$stage/AppxManifest.xml",$manifest,(New-Object Text.UTF8Encoding($false)))
    & $MakeAppx pack /d $stage /p "$destination/FreeTurnX-$version-$arch.msix" /o > "$stage-makeappx.log"
    if ($LASTEXITCODE) { Get-Content "$stage-makeappx.log" -Tail 30; throw 'MSIX validation or packing failed' }
    Write-Output "Validated package: $destination/FreeTurnX-$version-$arch.msix"
}
# Store signs accepted submissions. These upload packages are intentionally unsigned.
