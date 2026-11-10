# AndroLink build script
# Usage:
#   .\scripts\build.ps1                 # build + test
#   .\scripts\build.ps1 -Publish        # + publish win-x64 into out\publish
#   .\scripts\build.ps1 -Publish -Arch win-arm64   # publish for Windows-on-ARM
#   .\scripts\build.ps1 -Publish -Installer   # + compile Inno Setup package (needs ISCC.exe)
#
# Requires: .NET 8 SDK (Windows for publishing/running; compile-only also works on Linux).

[CmdletBinding()]
param(
    [switch]$Publish,
    [switch]$Installer,
    [switch]$SkipTests,
    [ValidateSet("win-x64", "win-arm64")]
    [string]$Arch = "win-x64"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot

Write-Host "== AndroLink build ==" -ForegroundColor Cyan
Write-Host "Solution root: $root"

dotnet --version | Out-Null
if ($LASTEXITCODE -ne 0) { throw ".NET 8 SDK not found. Install from https://dotnet.microsoft.com/download/dotnet/8.0" }

Write-Host "`n[1/4] Restoring..." -ForegroundColor Yellow
dotnet restore "$root\AndroLink.sln"
if ($LASTEXITCODE -ne 0) { throw "restore failed" }

Write-Host "`n[2/4] Building..." -ForegroundColor Yellow
dotnet build "$root\AndroLink.sln" -c Release --no-restore
if ($LASTEXITCODE -ne 0) { throw "build failed" }

if (-not $SkipTests) {
    Write-Host "`n[3/4] Testing..." -ForegroundColor Yellow
    dotnet test "$root\src\AndroLink.Tests\AndroLink.Tests.csproj" -c Release --no-build --nologo
    if ($LASTEXITCODE -ne 0) { throw "tests failed" }
}
else {
    Write-Host "`n[3/4] Tests skipped" -ForegroundColor DarkYellow
}

if ($Publish) {
    Write-Host "`n[4/4] Publishing $Arch..." -ForegroundColor Yellow
    # win-x64: standard PCs. win-arm64: Surface Pro X / Snapdragon / Windows-on-ARM
    # devices - AndroLink itself and its arm64-v8a system images all run natively.
    dotnet publish "$root\src\AndroLink.Manager\AndroLink.Manager.csproj" `
        -c Release -r $Arch --self-contained false `
        -o "$root\out\publish" `
        /p:PublishSingleFile=false
    if ($LASTEXITCODE -ne 0) { throw "publish failed" }

    # Plugin staging dir (drop backend dlls here; the manager scans it at startup)
    New-Item -ItemType Directory -Force -Path "$root\out\publish\backends" | Out-Null

    Copy-Item "$root\README.md" "$root\out\publish\" -ErrorAction SilentlyContinue
    if (Test-Path "$root\docs") { Copy-Item "$root\docs" "$root\out\publish\docs" -Recurse -Force }

    Write-Host "Published to $root\out\publish" -ForegroundColor Green

    if ($Installer) {
        $iscc = @(
            "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
            "$env:ProgramFiles\Inno Setup 6\ISCC.exe"
        ) | Where-Object { Test-Path $_ } | Select-Object -First 1
        if (-not $iscc) { throw "Inno Setup 6 not found (ISCC.exe). Install from https://jrsoftware.org/isinfo.php" }
        & $iscc "$root\installer\AndroLinkSetup.iss"
        if ($LASTEXITCODE -ne 0) { throw "installer build failed" }
        Write-Host "Installer written to $root\installer\Output\AndroLinkSetup.exe" -ForegroundColor Green
    }
}

Write-Host "`nDone." -ForegroundColor Green
