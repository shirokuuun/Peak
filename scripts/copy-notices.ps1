param([string]$Published = (Join-Path $PSScriptRoot '../dist/Peak'))
$ErrorActionPreference = 'Stop'
$peakPublished = [IO.Path]::GetFullPath($Published)
$peakLicenseFolder = Join-Path $peakPublished 'licenses'
New-Item -ItemType Directory -Path $peakLicenseFolder -Force | Out-Null
$peakPackages = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget/packages' }
$peakLibraries = @()
foreach ($peakDeps in @('Peak.deps.json', 'bridge/UsageNotch.ClaudeBridge.deps.json')) {
    $peakData = Get-Content -LiteralPath (Join-Path $peakPublished $peakDeps) -Raw | ConvertFrom-Json
    $peakLibraries += $peakData.libraries.PSObject.Properties.Name
}
$peakNotices = [Collections.Generic.List[string]]::new()
foreach ($peakLibrary in ($peakLibraries | Sort-Object -Unique)) {
    $peakParts = $peakLibrary.Split('/')
    $peakName = $peakParts[0] -replace '^runtimepack\.', ''
    if ($peakName -in @('Peak', 'UsageNotch.Core', 'UsageNotch.Providers', 'UsageNotch.Windows', 'UsageNotch.ClaudeBridge')) { continue }
    $peakPackage = Join-Path $peakPackages ($peakName.ToLowerInvariant() + '/' + $peakParts[1])
    if (-not (Test-Path -LiteralPath $peakPackage -PathType Container)) { throw "Dependency package not found: $peakLibrary" }
    $peakNuspec = Get-ChildItem -LiteralPath $peakPackage -Filter '*.nuspec' | Select-Object -First 1
    [xml]$peakMetadata = Get-Content -LiteralPath $peakNuspec.FullName -Raw
    $peakLicenseNode = $peakMetadata.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='license']")
    $peakLicenseUrl = $peakMetadata.SelectSingleNode("//*[local-name()='metadata']/*[local-name()='licenseUrl']")
    $peakLicenseFiles = @(Get-ChildItem -LiteralPath $peakPackage -File | Where-Object { $_.Name -match '^(LICENSE|License\.|THIRD-PARTY-NOTICES)' })
    if ($peakLicenseNode -and $peakLicenseNode.GetAttribute('type') -eq 'file') {
        $peakSpecified = Join-Path $peakPackage $peakLicenseNode.InnerText
        if (-not (Test-Path -LiteralPath $peakSpecified -PathType Leaf)) { throw "License file missing for $peakLibrary" }
        if ($peakSpecified -notin $peakLicenseFiles.FullName) { $peakLicenseFiles += Get-Item -LiteralPath $peakSpecified }
    }
    foreach ($peakFile in $peakLicenseFiles) {
        Copy-Item -LiteralPath $peakFile.FullName -Destination (Join-Path $peakLicenseFolder ($peakName + '-' + $peakFile.Name)) -Force
    }
    if ($peakLicenseFiles.Count -eq 0 -and -not $peakLicenseNode -and -not $peakLicenseUrl) { throw "No license information found for $peakLibrary" }
    $peakDescription = "$peakLibrary"
    if ($peakLicenseNode) { $peakDescription += ' | ' + $peakLicenseNode.InnerText }
    if ($peakLicenseUrl) { $peakDescription += ' | ' + $peakLicenseUrl.InnerText }
    $peakNotices.Add($peakDescription)
    # The SDK package supplies its license by URL. Preserve the official document in the distribution.
    if ($peakName -eq 'Microsoft.Windows.SDK.NET.Ref') {
        $peakSdkLicense = Join-Path $peakLicenseFolder 'Microsoft.Windows.SDK.NET.Ref-license.rtf'
        Invoke-WebRequest -Uri 'https://aka.ms/WinSDKLicenseURL' -OutFile $peakSdkLicense
        if (-not ([IO.File]::ReadAllText($peakSdkLicense).StartsWith('{\rtf'))) { throw 'The Windows SDK license URL did not return its expected license document. Review it before packaging.' }
    }
}
Copy-Item -LiteralPath (Join-Path $PSScriptRoot '../src/UsageNotch.App/Policies/Third-party-notices.md') -Destination (Join-Path $peakLicenseFolder 'Peak-third-party-notices.txt') -Force
[IO.File]::WriteAllLines((Join-Path $peakLicenseFolder 'dependencies.txt'), $peakNotices, [Text.UTF8Encoding]::new($false))
Write-Output "Dependency notices copied: $peakLicenseFolder"
