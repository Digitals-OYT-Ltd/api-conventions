#!/usr/bin/env pwsh
# Build and pack every tool into artifacts/, or one with -Package OpenApi, AsyncApi or Protobuf. MinVer
# versions each tool from its own tag: openapi/v*, asyncapi/v* or protobuf/v*.
# Run from the repo root: pwsh scripts/pack.ps1 [-Package OpenApi]

param(
    [string]$Package = '',
    [string]$Configuration = 'Release',
    [string]$Output = 'artifacts'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$target = if ($Package) { "src/Digitals.$Package" } else { '.' }
if (-not (Test-Path $target)) {
    Write-Host "No tool at $target. Use OpenApi, AsyncApi or Protobuf." -ForegroundColor Red
    exit 1
}

Write-Host "`n==> 🗜️ Pack $target" -ForegroundColor Cyan
dotnet pack $target --configuration $Configuration --output $Output
if ($LASTEXITCODE -ne 0) {
    Write-Host "FAILED: dotnet pack (exit $LASTEXITCODE)" -ForegroundColor Red
    exit $LASTEXITCODE
}

Write-Host "`n✅ Packed to $Output/." -ForegroundColor Green
