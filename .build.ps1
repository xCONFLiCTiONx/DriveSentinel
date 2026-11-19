<#
.SYNOPSIS
    Build, deploy, and optionally run DriveSentinel.
.DESCRIPTION
    Builds DriveSentinel in Release mode (default) and copies the compiled
    binaries to %LOCALAPPDATA%\xPrograms\DriveSentinel.
#>
[CmdletBinding()]
param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",

    [switch]$Clean,
    [switch]$Run
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = $PSScriptRoot
$solutionPath = Join-Path $repoRoot "DriveSentinel.sln"
$deployDir = (Join-Path $env:LOCALAPPDATA "xPrograms\DriveSentinel")

if (-not (Get-Command "dotnet" -ErrorAction SilentlyContinue)) {
    Write-Error "The 'dotnet' CLI was not found in PATH."
    exit 1
}

if ($Clean) {
    Write-Host "Cleaning bin/obj..." -ForegroundColor Yellow
    foreach ($dir in "bin", "obj") {
        $p = Join-Path $repoRoot "src\DriveSentinel.App\$dir"
        if (Test-Path $p) { Remove-Item -Path $p -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

Write-Host "Building DriveSentinel ($Configuration)..." -ForegroundColor Cyan
& dotnet build $solutionPath -c $Configuration --nologo
if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed with exit code $LASTEXITCODE."
    exit $LASTEXITCODE
}

$outDir = Join-Path $repoRoot "src\DriveSentinel.App\bin\$Configuration\net10.0-windows"
if (-not (Test-Path (Join-Path $outDir "DriveSentinel.exe"))) {
    Write-Error "Build output not found in $outDir"
    exit 1
}

if ($Configuration -eq "Release") {
    Get-Process -Name "DriveSentinel" -ErrorAction SilentlyContinue | Stop-Process -Force
    Start-Sleep -Milliseconds 300

    New-Item -ItemType Directory -Path $deployDir -Force | Out-Null
    Copy-Item -Path "$outDir\*" -Destination $deployDir -Recurse -Force
    Write-Host "Deployed Release binaries to $deployDir" -ForegroundColor Green
}

if ($Run) {
    $exeToRun = if ($Configuration -eq "Release") { Join-Path $deployDir "DriveSentinel.exe" } else { Join-Path $outDir "DriveSentinel.exe" }
    Start-Process -FilePath $exeToRun
}

