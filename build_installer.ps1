$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

& (Join-Path $root "build_app.ps1")

$versionSource = Get-Content -Raw (Join-Path $root "src\VersionInfo.cs")
$versionMatch = [regex]::Match($versionSource, 'Version\s*=\s*"([^"]+)"')
if (-not $versionMatch.Success) {
    throw "Could not determine the version from src\VersionInfo.cs"
}
$version = $versionMatch.Groups[1].Value

$possibleIscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "${env:ProgramFiles}\Inno Setup 6\ISCC.exe"
)
$iscc = $possibleIscc | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if (-not $iscc) {
    throw "Inno Setup 6 was not found. Install it from https://jrsoftware.org/isdl.php"
}

Write-Host "Compiling installer..." -ForegroundColor Cyan
& $iscc "/DAppVersion=$version" "installer\MinecraftControlCenter.iss"
if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup failed with exit code $LASTEXITCODE"
}

Write-Host "Installer: release\MinecraftControlCenter-Setup.exe" -ForegroundColor Green
Write-Host "ZIP: release\MinecraftControlCenter.zip" -ForegroundColor Green
Write-Host "Checksum: release\MinecraftControlCenter.zip.sha256" -ForegroundColor Green
