#!/usr/bin/env pwsh
# Run every test project, or one tool's with -Package OpenApi, AsyncApi or Protobuf.
# Run from the repo root: pwsh scripts/test-unit.ps1 [-Package OpenApi]

param(
    [string]$Package = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$filter = if ($Package) { "Digitals.$Package.Tests" } else { '*' }
$projects = @(Get-ChildItem tests -Directory -Filter $filter)
if ($projects.Count -eq 0) {
    Write-Host "No test project matches '$Package'. Use OpenApi, AsyncApi or Protobuf." -ForegroundColor Red
    exit 1
}

foreach ($project in $projects) {
    Write-Host "`n==> 🔬 $($project.Name)" -ForegroundColor Cyan
    dotnet test --project $project.FullName
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAILED: $($project.Name) (exit $LASTEXITCODE)" -ForegroundColor Red
        exit $LASTEXITCODE
    }
}
