param([switch]$FrameworkDependent, [switch]$PublicRelease, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$notchRoot = Split-Path -Parent $PSScriptRoot
$notchDotnet = (Get-Command dotnet -ErrorAction SilentlyContinue).Source
if (-not $notchDotnet) { $notchDotnet = Join-Path $env:ProgramFiles 'dotnet\dotnet.exe' }
if (-not (Test-Path -LiteralPath $notchDotnet)) { throw '.NET 10 SDK is required.' }
$notchOutput = if ($OutputDirectory) { $OutputDirectory } else { Join-Path $notchRoot 'dist\Peak' }
if ($PublicRelease) {
    if ($FrameworkDependent) { throw 'The public release requires a self-contained build.' }
    & (Join-Path $PSScriptRoot 'check-publication.ps1')
}
# Rebuild the known output directory so obsolete binaries cannot enter a new installer.
$notchOutput = [IO.Path]::GetFullPath($notchOutput, $notchRoot)
$notchAllowed = @(([IO.Path]::GetFullPath((Join-Path $notchRoot 'dist')) + '\'), ([IO.Path]::GetFullPath((Join-Path $notchRoot '.research')) + '\'))
if (-not ($notchAllowed | Where-Object { $notchOutput.StartsWith($_, [StringComparison]::OrdinalIgnoreCase) })) { throw 'Publish only to a subfolder of this workspace dist or .research folder.' }
for ($notchAncestor = $notchOutput; $notchAncestor; $notchAncestor = Split-Path -Parent $notchAncestor) {
    if ((Test-Path -LiteralPath $notchAncestor) -and ((Get-Item -LiteralPath $notchAncestor).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'The publish path contains a linked folder.' }
}
if (Get-Process -ErrorAction SilentlyContinue | Where-Object { $_.Path -and $_.Path.StartsWith($notchOutput + '\', [StringComparison]::OrdinalIgnoreCase) }) { throw 'Close the app running from this output folder before publishing, or choose an isolated -OutputDirectory.' }
if (Test-Path -LiteralPath $notchOutput) {
    $notchPublishItems = @(Get-Item -LiteralPath $notchOutput) + @(Get-ChildItem -LiteralPath $notchOutput -Recurse -Force)
    if ($notchPublishItems | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Remove links from the publish directory before rebuilding.' }
    Remove-Item -LiteralPath $notchOutput -Recurse -Force
}
$notchSelfContained = if ($FrameworkDependent) { 'false' } else { 'true' }
& $notchDotnet publish (Join-Path $notchRoot 'src\UsageNotch.App\UsageNotch.App.csproj') -c Release -r win-x64 --self-contained $notchSelfContained -o $notchOutput --nologo -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'App publish failed.' }
& $notchDotnet publish (Join-Path $notchRoot 'src\UsageNotch.ClaudeBridge\UsageNotch.ClaudeBridge.csproj') -c Release -r win-x64 --self-contained $notchSelfContained -o (Join-Path $notchOutput 'bridge') --nologo -p:DebugType=None -p:DebugSymbols=false
if ($LASTEXITCODE -ne 0) { throw 'Bridge publish failed.' }
Copy-Item -LiteralPath (Join-Path $notchRoot 'docs/Download-README.md') -Destination (Join-Path $notchOutput 'README.md') -Force
if (-not $FrameworkDependent) { & (Join-Path $PSScriptRoot 'copy-notices.ps1') -Published $notchOutput }
Write-Output "Ready: $notchOutput\Peak.exe"
