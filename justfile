set shell := ["pwsh", "-NoProfile", "-Command"]
set windows-shell := ["powershell.exe", "-NoProfile", "-Command"]

# List all available recipes
default:
    @just --list

# Restore and build the solution
build:
    dotnet build

# Run every ruleset and command test, or one tool's: just test OpenApi
test package="":
    & ./scripts/test-unit.ps1 -Package '{{package}}'

# Pack every tool into artifacts/, or one: just pack OpenApi (version from its openapi/v* tag via MinVer)
pack package="":
    & ./scripts/pack.ps1 -Package '{{package}}'

# Fix .cs file formatting
format:
    dotnet format

# Fail if any .cs file is not formatted (what CI runs)
format-check:
    dotnet format --verify-no-changes
