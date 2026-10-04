param(
    [ValidatePattern('^[A-Za-z0-9][A-Za-z0-9._-]*$')]
    [string]$Tag = (Get-Date -Format 'yyyy-MM-dd')
)

$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression
Add-Type -AssemblyName System.IO.Compression.FileSystem

$notchRoot = Split-Path -Parent $PSScriptRoot
$notchPublished = [IO.Path]::GetFullPath((Join-Path $notchRoot 'dist\Peak'))
$notchPrefix = $notchPublished.TrimEnd('\') + '\'
$notchGuide = Join-Path $notchRoot 'docs\Friend-test-guide.md'
$notchProviderNotices = Join-Path $notchRoot 'src\UsageNotch.App\Assets\Providers\README.md'
$notchRequired = @(
    'Peak.exe', 'Peak.dll', 'Peak.deps.json', 'Peak.runtimeconfig.json',
    'coreclr.dll', 'PresentationFramework.dll', 'bridge\UsageNotch.ClaudeBridge.exe',
    'bridge\UsageNotch.ClaudeBridge.dll', 'bridge\UsageNotch.ClaudeBridge.deps.json',
    'bridge\UsageNotch.ClaudeBridge.runtimeconfig.json', 'bridge\coreclr.dll'
)
foreach ($notchRelative in $notchRequired) {
    if (-not (Test-Path -LiteralPath (Join-Path $notchPublished $notchRelative) -PathType Leaf)) {
        throw "Missing self-contained published file: $notchRelative. Run scripts\publish.ps1 first."
    }
}
if (-not (Test-Path -LiteralPath $notchGuide -PathType Leaf)) { throw 'Friend setup guide is missing.' }
if (-not (Test-Path -LiteralPath $notchProviderNotices -PathType Leaf)) { throw 'Provider logo notices are missing.' }

# Package published binaries and runtime metadata only. User data and debug symbols are never inputs.
$notchItems = @(Get-ChildItem -LiteralPath $notchPublished -Recurse -Force)
if ($notchItems | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) {
    throw 'Published output contains a link or reparse point. Refusing to follow it into the package.'
}
$notchFiles = @($notchItems | Where-Object {
    -not $_.PSIsContainer -and ($_.Extension -in '.exe', '.dll' -or
        $_.Name -in 'Peak.deps.json', 'Peak.runtimeconfig.json',
            'UsageNotch.ClaudeBridge.deps.json', 'UsageNotch.ClaudeBridge.runtimeconfig.json')
} | Sort-Object FullName)
foreach ($notchFile in $notchFiles) {
    if (-not $notchFile.FullName.StartsWith($notchPrefix, [StringComparison]::OrdinalIgnoreCase)) {
        throw 'A published file resolves outside the expected app folder.'
    }
}

$notchDependencies = Get-Content -LiteralPath (Join-Path $notchPublished 'Peak.deps.json') -Raw | ConvertFrom-Json
$notchLibraryNames = @($notchDependencies.libraries.PSObject.Properties.Name)
$notchRuntimeVersion = ($notchLibraryNames | Where-Object { $_ -like 'runtimepack.Microsoft.NETCore.App.Runtime.win-x64/*' } | Select-Object -First 1).Split('/')[1]
$notchDesktopVersion = ($notchLibraryNames | Where-Object { $_ -like 'runtimepack.Microsoft.WindowsDesktop.App.Runtime.win-x64/*' } | Select-Object -First 1).Split('/')[1]
$notchToolkitVersion = ($notchLibraryNames | Where-Object { $_ -like 'CommunityToolkit.WinUI.Notifications/*' } | Select-Object -First 1).Split('/')[1]
$notchNuget = if ($env:NUGET_PACKAGES) { $env:NUGET_PACKAGES } else { Join-Path $env:USERPROFILE '.nuget\packages' }
$notchNotices = [ordered]@{
    'licenses/dotnet-LICENSE.txt' = Join-Path $notchNuget "microsoft.netcore.app.runtime.win-x64\$notchRuntimeVersion\LICENSE.TXT"
    'licenses/dotnet-THIRD-PARTY-NOTICES.txt' = Join-Path $notchNuget "microsoft.netcore.app.runtime.win-x64\$notchRuntimeVersion\THIRD-PARTY-NOTICES.TXT"
    'licenses/windows-desktop-LICENSE.txt' = Join-Path $notchNuget "microsoft.windowsdesktop.app.runtime.win-x64\$notchDesktopVersion\LICENSE"
    'licenses/community-toolkit-LICENSE.md' = Join-Path $notchNuget "communitytoolkit.winui.notifications\$notchToolkitVersion\License.md"
}
foreach ($notchNotice in $notchNotices.Values) {
    if (-not (Test-Path -LiteralPath $notchNotice -PathType Leaf)) { throw "Required dependency notice is missing: $notchNotice" }
}

$notchReleaseDirectory = Join-Path $notchRoot 'artifacts\releases'
New-Item -ItemType Directory -Path $notchReleaseDirectory -Force | Out-Null
$notchZipPath = Join-Path $notchReleaseDirectory "Peak-Windows-x64-test-$Tag.zip"
if (Test-Path -LiteralPath $notchZipPath) { throw 'This package already exists. Choose a different -Tag to preserve it.' }

$notchManifest = [Collections.Generic.List[string]]::new()
$notchArchiveStream = $null
$notchArchive = $null
function Add-NotchPackageFile([string]$Source, [string]$Relative) {
    $notchEntryName = 'Peak/' + $Relative.Replace('\', '/')
    [IO.Compression.ZipFileExtensions]::CreateEntryFromFile(
        $notchArchive, $Source, $notchEntryName, [IO.Compression.CompressionLevel]::Optimal) | Out-Null
    $notchManifest.Add((Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $Relative.Replace('\', '/'))
}
try {
    $notchArchiveStream = [IO.File]::Open($notchZipPath, [IO.FileMode]::CreateNew, [IO.FileAccess]::Write, [IO.FileShare]::None)
    $notchArchive = [IO.Compression.ZipArchive]::new($notchArchiveStream, [IO.Compression.ZipArchiveMode]::Create, $true)
    foreach ($notchFile in $notchFiles) {
        Add-NotchPackageFile $notchFile.FullName $notchFile.FullName.Substring($notchPrefix.Length)
    }
    Add-NotchPackageFile $notchGuide 'README.md'
    Add-NotchPackageFile $notchProviderNotices 'licenses/provider-logos.md'
    foreach ($notchNotice in $notchNotices.GetEnumerator()) { Add-NotchPackageFile $notchNotice.Value $notchNotice.Key }
    $notchNoticeEntry = $notchArchive.CreateEntry('Peak/licenses/README.txt')
    $notchNoticeWriter = [IO.StreamWriter]::new($notchNoticeEntry.Open(), [Text.UTF8Encoding]::new($false))
    try {
        $notchNoticeWriter.WriteLine("This package includes .NET $notchRuntimeVersion, Windows Desktop $notchDesktopVersion, and CommunityToolkit.WinUI.Notifications $notchToolkitVersion. Their notices are in this folder.")
        $notchNoticeWriter.WriteLine('Microsoft.Windows.SDK.NET.dll and WinRT.Runtime.dll come from Microsoft.Windows.SDK.NET.Ref; its package identifies the license at https://aka.ms/WinSDKLicenseURL and project at https://aka.ms/WinSDKProjectURL.')
    } finally { $notchNoticeWriter.Dispose() }
    $notchManifestEntry = $notchArchive.CreateEntry('Peak/files.sha256')
    $notchManifestWriter = [IO.StreamWriter]::new($notchManifestEntry.Open(), [Text.UTF8Encoding]::new($false))
    try { foreach ($notchLine in $notchManifest) { $notchManifestWriter.WriteLine($notchLine) } }
    finally { $notchManifestWriter.Dispose() }
} catch {
    if ($notchArchive) { $notchArchive.Dispose(); $notchArchive = $null }
    if ($notchArchiveStream) { $notchArchiveStream.Dispose(); $notchArchiveStream = $null }
    if (Test-Path -LiteralPath $notchZipPath) { Remove-Item -LiteralPath $notchZipPath }
    throw
} finally {
    if ($notchArchive) { $notchArchive.Dispose() }
    if ($notchArchiveStream) { $notchArchiveStream.Dispose() }
}

$notchArchiveHash = (Get-FileHash -LiteralPath $notchZipPath -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText($notchZipPath + '.sha256', "$notchArchiveHash  $([IO.Path]::GetFileName($notchZipPath))`n", [Text.UTF8Encoding]::new($false))
$notchPackage = Get-Item -LiteralPath $notchZipPath
Write-Output "Ready: $notchZipPath"
Write-Output ("Published files: {0}; ZIP size: {1:N1} MB; SHA-256: {2}" -f $notchFiles.Count, ($notchPackage.Length / 1MB), $notchArchiveHash)
