Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "  Building Caret Installer" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

$ErrorActionPreference = "Stop"
$ProjectDir = Split-Path -Parent $MyInvocation.MyCommand.Path

$publishDir = Join-Path $ProjectDir "bin\Release\net10.0-windows\win-x64\publish"
$installerDir = Join-Path $ProjectDir "CaretInstaller"
$installerPublishDir = Join-Path $installerDir "bin\Release\net10.0-windows\win-x64\publish"
$outputDir = Join-Path $ProjectDir "Output"
$payloadZip = Join-Path $installerDir "Payload.zip"

# ── Step 1: Clean ────────────────────────────────────────────────────

Write-Host "[1/5] Cleaning previous builds..." -ForegroundColor Yellow
if (Test-Path $publishDir) { Remove-Item -Recurse -Force $publishDir }
if (Test-Path $installerPublishDir) { Remove-Item -Recurse -Force $installerPublishDir }
if (Test-Path $outputDir) { Remove-Item -Recurse -Force $outputDir }
if (Test-Path $payloadZip) { Remove-Item -Force $payloadZip }
New-Item -ItemType Directory -Path $outputDir -Force | Out-Null

# ── Step 2: Publish Caret ────────────────────────────────────────────

Write-Host "[2/5] Publishing Caret..." -ForegroundColor Yellow
Push-Location $ProjectDir
dotnet publish -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:DebugType=none `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: dotnet publish failed!" -ForegroundColor Red
    Pop-Location
    exit 1
}
Pop-Location

$exePath = Join-Path $publishDir "Caret.exe"
if (-not (Test-Path $exePath)) {
    Write-Host "ERROR: Published exe not found at $exePath" -ForegroundColor Red
    exit 1
}

$exeSize = [math]::Round((Get-Item $exePath).Length / 1MB, 1)
Write-Host "  Published: Caret.exe ($exeSize MB)" -ForegroundColor Green

# ── Step 3: Create Payload ───────────────────────────────────────────

Write-Host "[3/5] Creating installer payload..." -ForegroundColor Yellow
$payloadTemp = Join-Path $installerDir "_payload_temp"
if (Test-Path $payloadTemp) { Remove-Item -Recurse -Force $payloadTemp }
New-Item -ItemType Directory -Path $payloadTemp -Force | Out-Null

Copy-Item $exePath $payloadTemp

$icoPath = Join-Path $ProjectDir "App.ico"
$pngPath = Join-Path $ProjectDir "Icon.png"
if (Test-Path $icoPath) { Copy-Item $icoPath $payloadTemp }
if (Test-Path $pngPath) { Copy-Item $pngPath $payloadTemp }

Compress-Archive -Path (Join-Path $payloadTemp "*") -DestinationPath $payloadZip -Force
Remove-Item -Recurse -Force $payloadTemp

$zipSize = [math]::Round((Get-Item $payloadZip).Length / 1MB, 1)
Write-Host "  Payload: $zipSize MB" -ForegroundColor Green

# ── Step 4: Build Installer ─────────────────────────────────────────

Write-Host "[4/5] Building installer..." -ForegroundColor Yellow
$installerCsproj = Join-Path $installerDir "CaretInstaller.csproj"
dotnet publish $installerCsproj -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) {
    Write-Host "ERROR: Installer build failed!" -ForegroundColor Red
    exit 1
}

$installerExe = Join-Path $installerPublishDir "CaretSetup.exe"
if (-not (Test-Path $installerExe)) {
    Write-Host "ERROR: Installer exe not found!" -ForegroundColor Red
    exit 1
}

# ── Step 5: Finalize ────────────────────────────────────────────────

Write-Host "[5/5] Finalizing..." -ForegroundColor Yellow
$outputInstaller = Join-Path $outputDir "Caret_Setup_1.1.1.exe"
Copy-Item $installerExe $outputInstaller

if (Test-Path $payloadZip) { Remove-Item -Force $payloadZip }

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "  Build Complete!" -ForegroundColor Green
Write-Host "========================================" -ForegroundColor Green
$installerSize = [math]::Round((Get-Item $outputInstaller).Length / 1MB, 1)
Write-Host "  Installer: $outputInstaller" -ForegroundColor White
Write-Host "  Size: $installerSize MB" -ForegroundColor White
Write-Host ""
