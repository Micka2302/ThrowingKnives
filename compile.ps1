#!/usr/bin/env pwsh
[CmdletBinding()]
param(
    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
Set-Location $root

$solution = Join-Path $root 'ThrowingKnives.sln'
$buildOutput = Join-Path $root "bin/$Configuration/net8.0"
$compiledRoot = Join-Path $root 'compiled'
$pluginName = 'ThrowingKnives'
$pluginTarget = Join-Path $compiledRoot "counterstrikesharp/plugins/$pluginName"

# Clean staging directory
Remove-Item -Recurse -Force $compiledRoot -ErrorAction SilentlyContinue
New-Item -ItemType Directory -Path $pluginTarget -Force | Out-Null

dotnet restore $solution
dotnet build $solution -c $Configuration --no-restore --nologo

if (-not (Test-Path $buildOutput)) {
    throw "Build output not found at $buildOutput"
}

# Stage plugin files
Copy-Item -Path (Join-Path $buildOutput '*') -Destination $pluginTarget -Recurse -Force

# Remove CSS API (provided by the server)
$cssApi = Join-Path $pluginTarget 'CounterStrikeSharp.API.dll'
if (Test-Path $cssApi) {
    Remove-Item $cssApi -Force
}

# Keep only linux and Windows runtimes if present
$runtimeDir = Join-Path $pluginTarget 'runtimes'
if (Test-Path $runtimeDir) {
    $keep = @('linux-x64', 'win-x64')
    Get-ChildItem $runtimeDir -Directory | Where-Object { $keep -notcontains $_.Name } | Remove-Item -Recurse -Force
}

# Zip the staged plugin for convenience
$zipPath = Join-Path $compiledRoot "$pluginName.zip"
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}
Compress-Archive -Path (Join-Path $pluginTarget '*') -DestinationPath $zipPath

Write-Host "[OK] Build finished."
Write-Host " - Folder: $pluginTarget"
Write-Host " - Zip:    $zipPath"
