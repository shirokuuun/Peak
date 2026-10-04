$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$notchDotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $notchDotnet) { $notchDotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe' }
$notchRoot = Split-Path -Parent $PSScriptRoot
& $notchDotnet build (Join-Path $notchRoot 'UsageNotch.slnx') -c Release --nologo
if ($LASTEXITCODE -ne 0) { throw 'Build failed.' }
& $notchDotnet test (Join-Path $notchRoot 'tests\UsageNotch.Core.Tests\UsageNotch.Core.Tests.csproj') -c Release --no-build --nologo
if ($LASTEXITCODE -ne 0) { throw 'Tests failed.' }
