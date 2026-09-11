param(
    [string]$ServerRoot = ""
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$versionSource = Get-Content -Raw (Join-Path $root "src\VersionInfo.cs")
$versionMatch = [regex]::Match($versionSource, 'Version\s*=\s*"([^"]+)"')
if (-not $versionMatch.Success) {
    throw "Could not determine the version from src\VersionInfo.cs"
}

$tag = "v" + $versionMatch.Groups[1].Value
$imagePath = Join-Path $root "release-notes\$tag.png"
$notesPath = Join-Path $root "release-notes\$tag.md"
$executable = Join-Path $root "release\MinecraftControlCenter.exe"

& (Join-Path $root "build_app.ps1")

$temporaryRoot = Join-Path ([IO.Path]::GetTempPath()) ("MinecraftControlCenter-Screenshot-" + [Guid]::NewGuid().ToString("N"))
$configRoot = Join-Path $temporaryRoot "config"
$fixtureRoot = Join-Path $temporaryRoot "Crafty"
New-Item -ItemType Directory -Path $configRoot -Force | Out-Null

$resolvedServerRoot = $ServerRoot
$playitPath = ""
$prismPath = ""
$lastDiscoveryUtc = ""
if ([string]::IsNullOrWhiteSpace($resolvedServerRoot)) {
    New-Item -ItemType Directory -Path (Join-Path $fixtureRoot "app") -Force | Out-Null
    New-Item -ItemType File -Path (Join-Path $fixtureRoot "crafty.exe") -Force | Out-Null

    $playitDirectory = Join-Path $temporaryRoot "playit"
    $prismDirectory = Join-Path $temporaryRoot "Prism Launcher"
    New-Item -ItemType Directory -Path $playitDirectory,$prismDirectory -Force | Out-Null
    $playitPath = Join-Path $playitDirectory "playit.exe"
    $prismPath = Join-Path $prismDirectory "prismlauncher.exe"
    Copy-Item -LiteralPath $executable -Destination $playitPath
    Copy-Item -LiteralPath $executable -Destination $prismPath

    $resolvedServerRoot = $fixtureRoot
    $lastDiscoveryUtc = [DateTime]::UtcNow.ToString("O")
}
elseif (-not (Test-Path -LiteralPath (Join-Path $resolvedServerRoot "crafty.exe"))) {
    throw "The supplied server root does not contain crafty.exe: $resolvedServerRoot"
}

$settings = @{
    serverRoot = [IO.Path]::GetFullPath($resolvedServerRoot)
    playitPath = $playitPath
    prismLauncherPath = $prismPath
    tLauncherPath = ""
    lastDiscoveryUtc = $lastDiscoveryUtc
}
$settings | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $configRoot "settings.json") -Encoding UTF8

$previousConfig = $env:MCC_CONFIG_DIR
$previousServer = $env:MCC_SERVER_ROOT
try {
    $env:MCC_CONFIG_DIR = $configRoot
    $env:MCC_SERVER_ROOT = $resolvedServerRoot
    $process = Start-Process -FilePath $executable -ArgumentList @("--screenshot", $imagePath) -Wait -PassThru
    if ($process.ExitCode -ne 0 -or -not (Test-Path -LiteralPath $imagePath)) {
        throw "Screenshot capture failed with exit code $($process.ExitCode)"
    }
}
finally {
    $env:MCC_CONFIG_DIR = $previousConfig
    $env:MCC_SERVER_ROOT = $previousServer
    Remove-Item -LiteralPath $temporaryRoot -Recurse -Force -ErrorAction SilentlyContinue
}

if (Test-Path -LiteralPath $notesPath) {
    $imageUrl = "https://raw.githubusercontent.com/TheQuantifier/MinecraftControlCenter/$tag/release-notes/$tag.png"
    $notes = Get-Content -Raw -LiteralPath $notesPath
    if ($notes -notmatch [regex]::Escape($imageUrl)) {
        Add-Content -LiteralPath $notesPath -Encoding UTF8 -Value "`r`n## Interface preview`r`n`r`n![Minecraft Control Center $tag]($imageUrl)"
    }
}

Write-Host "Release image: $imagePath" -ForegroundColor Green
