param([switch]$FrameworkDependent, [switch]$PublicRelease, [switch]$StoreBuild, [string]$OutputDirectory)
$ErrorActionPreference = 'Stop'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$notchRoot = Split-Path -Parent $PSScriptRoot
[xml]$peakAppProject = Get-Content -LiteralPath (Join-Path $notchRoot 'src/UsageNotch.App/UsageNotch.App.csproj') -Raw
$peakDevelopmentVersion = [string]($peakAppProject.Project.PropertyGroup.Version | Where-Object { $_ } | Select-Object -First 1)
if ($peakDevelopmentVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'A semantic development version is required.' }
$peakVersion = $peakDevelopmentVersion
if ($StoreBuild) {
    # Store releases advance independently of local development builds. Keep the
    # upload version in the Store manifest and apply it to every published project.
    [xml]$peakStoreManifest = Get-Content -LiteralPath (Join-Path $notchRoot 'store/AppxManifest.xml') -Raw
    $peakStorePackageVersion = [string]$peakStoreManifest.Package.Identity.Version
    if ($peakStorePackageVersion -notmatch '^(\d+\.\d+\.\d+)\.0$') { throw 'The Store manifest version must have three numeric components followed by .0.' }
    $peakVersion = $Matches[1]
}
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
$peakBuildProperties = @('-p:DebugType=None', '-p:DebugSymbols=false', "-p:PeakStoreBuild=$($StoreBuild.IsPresent.ToString().ToLowerInvariant())")
if ($StoreBuild) { $peakBuildProperties += "-p:Version=$peakVersion" }
& $notchDotnet publish (Join-Path $notchRoot 'src\UsageNotch.App\UsageNotch.App.csproj') -c Release -r win-x64 --self-contained $notchSelfContained -o $notchOutput --nologo @peakBuildProperties
if ($LASTEXITCODE -ne 0) { throw 'App publish failed.' }
& $notchDotnet publish (Join-Path $notchRoot 'src\UsageNotch.ClaudeBridge\UsageNotch.ClaudeBridge.csproj') -c Release -r win-x64 --self-contained $notchSelfContained -o (Join-Path $notchOutput 'bridge') --nologo @peakBuildProperties
if ($LASTEXITCODE -ne 0) { throw 'Bridge publish failed.' }
Copy-Item -LiteralPath (Join-Path $notchRoot 'docs/Download-README.md') -Destination (Join-Path $notchOutput 'README.md') -Force
if ($StoreBuild) {
    Copy-Item -LiteralPath (Join-Path $notchRoot 'docs/Store-README.md') -Destination (Join-Path $notchOutput 'README.md') -Force
    $peakStoreBuild = [ordered]@{ distribution = 'microsoft-store'; dataRoot = 'user-profile/.peak/store'; version = $peakVersion; developmentVersion = $peakDevelopmentVersion }
    [IO.File]::WriteAllText((Join-Path $notchOutput 'store-build.json'), ($peakStoreBuild | ConvertTo-Json) + "`n", [Text.UTF8Encoding]::new($false))
}
[IO.File]::WriteAllText((Join-Path $notchOutput 'VERSION.txt'), "$peakVersion`nNot yet released on Microsoft Store.`n", [Text.UTF8Encoding]::new($false))
if (-not $FrameworkDependent) { & (Join-Path $PSScriptRoot 'copy-notices.ps1') -Published $notchOutput }
Write-Output "Ready: $notchOutput\Peak.exe"
