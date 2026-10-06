param([string]$Published = (Join-Path $PSScriptRoot '../dist/Peak'))
$ErrorActionPreference = 'Stop'
$peakRoot = Split-Path -Parent $PSScriptRoot
$peakPublished = [IO.Path]::GetFullPath($Published, $peakRoot)
& (Join-Path $PSScriptRoot 'check-publication.ps1') -Configuration (Join-Path $peakPublished 'publishing.json')
foreach ($peakRequired in @('Peak.exe', 'Peak.dll', 'coreclr.dll', 'PresentationFramework.dll', 'bridge/UsageNotch.ClaudeBridge.exe', 'licenses/dependencies.txt', 'Policies/Terms.md', 'Policies/Privacy.md', 'Policies/Third-party-notices.md', 'README.md')) {
    if (-not (Test-Path -LiteralPath (Join-Path $peakPublished $peakRequired) -PathType Leaf)) { throw "Missing self-contained release file: $peakRequired" }
}
$peakItems = @(Get-Item -LiteralPath $peakPublished) + @(Get-ChildItem -LiteralPath $peakPublished -Recurse -Force)
if ($peakItems | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Refusing a package containing linked files.' }
$peakFiles = @($peakItems | Where-Object { -not $_.PSIsContainer })
foreach ($peakFile in $peakFiles) {
    $peakRelative = [IO.Path]::GetRelativePath($peakPublished, $peakFile.FullName).Replace('\', '/')
    if ($peakRelative -match '(^|/)(commercial\.json|license\.dat|settings\.json|auth\.json|setup\.json|integrations\.json)$' -or $peakFile.Extension -in '.pfx', '.key', '.pem', '.pdb', '.jsonl', '.log') { throw "Private, obsolete or debug file in package: $peakRelative" }
}
$peakVersion = (Get-Item -LiteralPath (Join-Path $peakPublished 'Peak.exe')).VersionInfo.ProductVersion.Split('+')[0]
if ($peakVersion -notmatch '^\d+\.\d+\.\d+$') { throw 'Use a versioned release.' }
$peakOutput = Join-Path $peakRoot 'artifacts/releases'
New-Item -ItemType Directory -Force -Path $peakOutput | Out-Null
$peakZip = Join-Path $peakOutput "Peak-$peakVersion-Windows-x64-Portable.zip"
$peakInstaller = Join-Path $peakOutput "Peak-$peakVersion-Windows-x64-Setup.exe"
if (-not (Test-Path -LiteralPath $peakInstaller)) { throw 'Build the installer before assembling release metadata.' }
if (Test-Path -LiteralPath $peakZip) { throw 'A portable package with this version already exists. Increment the version rather than replacing a published artifact.' }
Add-Type -AssemblyName System.IO.Compression
$peakStream = [IO.File]::Open($peakZip, [IO.FileMode]::CreateNew)
$peakArchive = [IO.Compression.ZipArchive]::new($peakStream, [IO.Compression.ZipArchiveMode]::Create)
try {
    foreach ($peakFile in $peakFiles) {
        $peakRelative = [IO.Path]::GetRelativePath($peakPublished, $peakFile.FullName).Replace('\', '/')
        if ($peakFile.Extension -eq '.pdb') { continue }
        [IO.Compression.ZipFileExtensions]::CreateEntryFromFile($peakArchive, $peakFile.FullName, 'Peak/' + $peakRelative, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    }
} finally { $peakArchive.Dispose(); $peakStream.Dispose() }
$peakInstallerHash = (Get-FileHash -LiteralPath $peakInstaller -Algorithm SHA256).Hash.ToLowerInvariant()
$peakZipHash = (Get-FileHash -LiteralPath $peakZip -Algorithm SHA256).Hash.ToLowerInvariant()
$peakSums = "$peakInstallerHash  $([IO.Path]::GetFileName($peakInstaller))`n$peakZipHash  $([IO.Path]::GetFileName($peakZip))`n"
[IO.File]::WriteAllText((Join-Path $peakOutput 'SHA256SUMS.txt'), $peakSums, [Text.UTF8Encoding]::new($false))
$peakConfig = Get-Content -LiteralPath (Join-Path $peakPublished 'publishing.json') -Raw | ConvertFrom-Json
$peakRepository = $peakConfig.releasesUrl -replace '/releases/?$', ''
$peakSigning = if ((Get-AuthenticodeSignature -LiteralPath $peakInstaller).Status -eq 'Valid') { 'signed' } else { 'unsigned' }
$peakManifest = [ordered]@{
    available = $false; verified = $false; version = $peakVersion
    publishedAt = (Get-Date).ToUniversalTime().ToString('yyyy-MM-dd'); architecture = 'x64'
    requirements = 'Windows 10 version 2004 or later, or Windows 11 · x64'
    signingStatus = $peakSigning; repositoryUrl = $peakRepository
    releaseNotesUrl = "$peakRepository/releases/tag/v$peakVersion"
    installer = @{ url = "$peakRepository/releases/download/v$peakVersion/$([IO.Path]::GetFileName($peakInstaller))"; sha256 = $peakInstallerHash }
    portable = @{ url = "$peakRepository/releases/download/v$peakVersion/$([IO.Path]::GetFileName($peakZip))"; sha256 = $peakZipHash }
}
[IO.File]::WriteAllText((Join-Path $peakOutput 'release.json'), ($peakManifest | ConvertTo-Json -Depth 5) + "`n", [Text.UTF8Encoding]::new($false))
Write-Output "Installer and portable package ready. Verify the uploaded public files before enabling website downloads: $peakOutput"
