$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $root "src"
$buildRoot = Join-Path $root "build_artifacts"
$releaseDir = Join-Path $root "release"
$publishDir = Join-Path $buildRoot "publish"
$outputExe = Join-Path $publishDir "MinecraftControlCenter.exe"
$releaseExe = Join-Path $releaseDir "MinecraftControlCenter.exe"
$releaseZip = Join-Path $releaseDir "MinecraftControlCenter.zip"
$releaseChecksum = $releaseZip + ".sha256"
$icon = Join-Path $root "assets\MinecraftControlCenter.ico"
if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
    throw ".NET 8 SDK was not found. Install it from https://dotnet.microsoft.com/download/dotnet/8.0"
}

New-Item -ItemType Directory -Path $buildRoot -Force | Out-Null
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null
foreach ($generatedDirectory in @($publishDir, (Join-Path $buildRoot "zip"))) {
    if (Test-Path -LiteralPath $generatedDirectory) {
        Remove-Item -LiteralPath $generatedDirectory -Recurse -Force
    }
}

Write-Host "Publishing self-contained MinecraftControlCenter.exe..." -ForegroundColor Cyan
& dotnet publish (Join-Path $root "MinecraftControlCenter.csproj") -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true -o $publishDir
if ($LASTEXITCODE -ne 0) {
    throw "C# compilation failed with exit code $LASTEXITCODE"
}

Copy-Item -LiteralPath $outputExe -Destination $releaseExe -Force
if (Test-Path -LiteralPath $releaseZip) {
    Remove-Item -LiteralPath $releaseZip -Force
}

$zipStage = Join-Path $buildRoot "zip"
New-Item -ItemType Directory -Path $zipStage -Force | Out-Null
Copy-Item -LiteralPath $releaseExe -Destination (Join-Path $zipStage "MinecraftControlCenter.exe")
Copy-Item -LiteralPath (Join-Path $root "PORTABLE_README.txt") -Destination $zipStage
Compress-Archive -Path (Join-Path $zipStage "*") -DestinationPath $releaseZip -CompressionLevel Optimal
$hash = (Get-FileHash -LiteralPath $releaseZip -Algorithm SHA256).Hash.ToLowerInvariant()
Set-Content -LiteralPath $releaseChecksum -Value "$hash  MinecraftControlCenter.zip" -NoNewline -Encoding ASCII

Write-Host "App: $releaseExe" -ForegroundColor Green
Write-Host "Update/portable ZIP: $releaseZip" -ForegroundColor Green
Write-Host "SHA-256: $releaseChecksum" -ForegroundColor Green
