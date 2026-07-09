# Build toàn bộ Quản Lý Gỗ (WoodInventory) Desktop (WPF thuần .NET + SQLite, không web):
#   1. Publish exe self-contained single-file  -> build\desktop-app\WoodInventory.exe
#   2. Biên dịch installer Inno Setup          -> build\installer\WoodInventory-Setup.exe
#
# Cách chạy:  powershell -ExecutionPolicy Bypass -File .\build-wpf-desktop.ps1
# Yêu cầu: .NET 8 SDK, Inno Setup 6.

$ErrorActionPreference = "Stop"
$root = $PSScriptRoot

Write-Host "==> [1/2] Publish exe self-contained..." -ForegroundColor Cyan
dotnet publish "$root\wpf-src\WoodInventory.csproj" `
    -c Release -r win-x64 --self-contained true `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -o "$root\build\desktop-app"

Write-Host "==> [2/2] Bien dich installer..." -ForegroundColor Cyan
$iscc = @(
    "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
    "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
) | Where-Object { Test-Path $_ } | Select-Object -First 1

if (-not $iscc) {
    throw "Khong tim thay Inno Setup 6. Tai tai https://jrsoftware.org/isdl.php"
}
& $iscc "$root\installer\WoodInventory.iss"

Write-Host ""
Write-Host "==> Hoan tat." -ForegroundColor Green
Write-Host "  Exe chay truc tiep : $root\build\desktop-app\WoodInventory.exe"
Write-Host "  File cai dat       : $root\build\installer\WoodInventory-Setup.exe"
