param([switch]$FrameworkDependent)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$notchRoot = Split-Path -Parent $PSScriptRoot
$notchDotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $notchDotnet) { $notchDotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe' }
if (-not (Test-Path -LiteralPath $notchDotnet)) { throw '.NET 10 SDK is required.' }
$notchOutput = Join-Path $notchRoot 'dist\Peak'
$notchSelfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
& $notchDotnet publish (Join-Path $notchRoot 'src\UsageNotch.App\UsageNotch.App.csproj') -c Release -r win-x64 --self-contained $notchSelfContained -o $notchOutput --nologo
if ($LASTEXITCODE -ne 0) { throw 'App publish failed.' }
& $notchDotnet publish (Join-Path $notchRoot 'src\UsageNotch.ClaudeBridge\UsageNotch.ClaudeBridge.csproj') -c Release -r win-x64 --self-contained $notchSelfContained -o (Join-Path $notchOutput 'bridge') --nologo
if ($LASTEXITCODE -ne 0) { throw 'Bridge publish failed.' }
Copy-Item -LiteralPath (Join-Path $notchRoot 'README.md') -Destination $notchOutput -Force
Write-Output "Ready: $notchOutput\Peak.exe"
