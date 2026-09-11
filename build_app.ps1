$ErrorActionPreference = "Stop"

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $root "src"
$buildRoot = Join-Path $root "build_artifacts"
$releaseDir = Join-Path $root "release"
$outputExe = Join-Path $buildRoot "MinecraftControlCenter.exe"
$releaseExe = Join-Path $releaseDir "MinecraftControlCenter.exe"
$releaseZip = Join-Path $releaseDir "MinecraftControlCenter.zip"
$icon = Join-Path $root "assets\MinecraftControlCenter.ico"
$csc = Join-Path $env:WINDIR "Microsoft.NET\Framework64\v4.0.30319\csc.exe"

if (-not (Test-Path -LiteralPath $csc)) {
    throw ".NET Framework C# compiler was not found at $csc"
}

if (Test-Path -LiteralPath $buildRoot) {
    Remove-Item -LiteralPath $buildRoot -Recurse -Force
}
New-Item -ItemType Directory -Path $buildRoot -Force | Out-Null
New-Item -ItemType Directory -Path $releaseDir -Force | Out-Null

$sources = Get-ChildItem -LiteralPath $source -Filter "*.cs" | ForEach-Object FullName
if (-not $sources) {
    throw "No C# source files were found in $source"
}

Write-Host "Building MinecraftControlCenter.exe..." -ForegroundColor Cyan
& $csc /nologo /target:winexe /platform:anycpu /optimize+ "/win32icon:$icon" "/out:$outputExe" `
    /reference:System.dll /reference:System.Core.dll /reference:System.Drawing.dll `
    /reference:System.Management.dll /reference:System.Web.Extensions.dll /reference:System.Windows.Forms.dll `
    $sources
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

Write-Host "App: $releaseExe" -ForegroundColor Green
Write-Host "Update/portable ZIP: $releaseZip" -ForegroundColor Green
