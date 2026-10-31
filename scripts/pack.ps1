#!/usr/bin/env pwsh
# Pack every tool into artifacts/, or one with -Package OpenApi, AsyncApi or Protobuf. MinVer versions each
# tool from its own tag: openapi/v*, asyncapi/v* or protobuf/v*.
#
# A tool is a pointer package (-Rid any) plus one native AOT package per RID. Native AOT compiles only for the
# OS it runs on (x64 and Arm64 cross-compile on macOS and Windows), so each workflow packs every RID on its own
# runner. By default this packs the pointer and this machine's RID, enough to install and try the tool.
# Run from the repo root: pwsh scripts/pack.ps1 [-Package OpenApi] [-Rid any,osx-arm64]

param(
    [string]$Package = '',
    [string[]]$Rid = @('any', [System.Runtime.InteropServices.RuntimeInformation]::RuntimeIdentifier),
    [string]$Configuration = 'Release',
    [string]$Output = 'artifacts'
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$tools = if ($Package) { @($Package) } else { @('OpenApi', 'AsyncApi', 'Protobuf') }
foreach ($tool in $tools) {
    $target = "src/Digitals.$tool"
    if (-not (Test-Path $target)) {
        Write-Host "No tool at $target. Use OpenApi, AsyncApi or Protobuf." -ForegroundColor Red
        exit 1
    }

    foreach ($runtime in $Rid) {
        $ridArgs = if ($runtime -eq 'any') { @() } else { @('--runtime', $runtime) }
        Write-Host "`n==> 🗜️ Pack $target ($runtime)" -ForegroundColor Cyan
        dotnet pack $target --configuration $Configuration --output $Output @ridArgs
        if ($LASTEXITCODE -ne 0) {
            Write-Host "FAILED: dotnet pack $target $runtime (exit $LASTEXITCODE)" -ForegroundColor Red
            exit $LASTEXITCODE
        }
    }
}

Write-Host "`n✅ Packed to $Output/." -ForegroundColor Green
