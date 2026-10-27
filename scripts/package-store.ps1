param(
    [string]$Published = (Join-Path $PSScriptRoot '../dist/Peak-Store'),
    [string]$MakeAppx,
    [string]$MakePri
)
$ErrorActionPreference = 'Stop'
$peakRoot = Split-Path -Parent $PSScriptRoot
$peakPublished = [IO.Path]::GetFullPath($Published, $peakRoot)
$peakFlavor = Get-Content -LiteralPath (Join-Path $peakPublished 'store-build.json') -Raw | ConvertFrom-Json
if ($peakFlavor.distribution -ne 'microsoft-store' -or $peakFlavor.dataRoot -ne 'user-profile/.peak/store') { throw 'Publish with -PublicRelease -StoreBuild before making a Store package.' }
& (Join-Path $PSScriptRoot 'check-publication.ps1') -Configuration (Join-Path $peakPublished 'publishing.json')
foreach ($peakFile in @('Peak.exe', 'Peak.dll', 'coreclr.dll', 'PresentationFramework.dll', 'bridge/UsageNotch.ClaudeBridge.exe', 'licenses/dependencies.txt', 'README.md')) {
    if (-not (Test-Path -LiteralPath (Join-Path $peakPublished $peakFile) -PathType Leaf)) { throw "Missing self-contained file: $peakFile" }
}
$peakItems = @(Get-Item -LiteralPath $peakPublished) + @(Get-ChildItem -LiteralPath $peakPublished -Recurse -Force)
if ($peakItems | Where-Object { $_.Attributes -band [IO.FileAttributes]::ReparsePoint }) { throw 'Linked package files are not allowed.' }
foreach ($peakFile in $peakItems | Where-Object { -not $_.PSIsContainer }) {
    if ($peakFile.Name -in 'commercial.json', 'license.dat', 'settings.json', 'auth.json', 'setup.json', 'integrations.json' -or
        $peakFile.Extension -in '.pfx', '.key', '.pem', '.pdb', '.jsonl', '.log') { throw "Private or debug file in publish directory: $($peakFile.Name)" }
}
if (-not $MakeAppx) {
    $peakTool = Get-Command makeappx.exe -ErrorAction SilentlyContinue
    $MakeAppx = if ($peakTool) { $peakTool.Source } else {
        Get-ChildItem -Path "${env:ProgramFiles(x86)}/Windows Kits/10/bin/*/x64/makeappx.exe" |
            Sort-Object FullName -Descending | Select-Object -First 1 -ExpandProperty FullName
    }
}
if (-not $MakeAppx -or -not (Test-Path -LiteralPath $MakeAppx -PathType Leaf)) { throw 'Windows SDK MakeAppx.exe is required.' }
if (-not $MakePri) { $MakePri = Join-Path (Split-Path -Parent $MakeAppx) 'makepri.exe' }
if (-not (Test-Path -LiteralPath $MakePri -PathType Leaf)) { throw 'Windows SDK MakePri.exe is required for the scaled Store assets.' }
$peakVersion = (Get-Item -LiteralPath (Join-Path $peakPublished 'Peak.exe')).VersionInfo.ProductVersion.Split('+')[0]
if ($peakVersion -notmatch '^(\d+)\.(\d+)\.(\d+)$') { throw 'A semantic application version is required.' }
# The Store reserves the fourth component; increment the third for subsequent submissions.
$peakPackageVersion = "$peakVersion.0"
[xml]$peakManifest = Get-Content -LiteralPath (Join-Path $peakRoot 'store/AppxManifest.xml') -Raw
if ($peakManifest.Package.Identity.Version -ne $peakPackageVersion -or $peakFlavor.version -ne $peakVersion) {
    throw 'The published Store version does not match its manifest. Rebuild with publish.ps1 -StoreBuild.'
}
$peakOutput = Join-Path $peakRoot 'artifacts/store'
New-Item -ItemType Directory -Force -Path $peakOutput | Out-Null
$peakPackage = Join-Path $peakOutput "Peak-$peakPackageVersion-Windows-x64-Store.msix"
if (Test-Path -LiteralPath $peakPackage) { throw 'Preserve existing Store artifacts and increment the version before rebuilding.' }
$peakStage = Join-Path $peakRoot ('.research/store-staging-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $peakStage | Out-Null
Copy-Item -Path (Join-Path $peakPublished '*') -Destination $peakStage -Recurse
Copy-Item -LiteralPath (Join-Path $peakRoot 'store/Assets') -Destination $peakStage -Recurse
$peakIdentity = Get-Content -LiteralPath (Join-Path $peakRoot 'store/identity.json') -Raw | ConvertFrom-Json
$peakManifest.Package.Identity.Name = $peakIdentity.name
$peakManifest.Package.Identity.Publisher = $peakIdentity.publisher
$peakManifest.Package.Identity.Version = $peakPackageVersion
$peakManifest.Package.Properties.DisplayName = $peakIdentity.displayName
$peakManifest.Package.Properties.PublisherDisplayName = $peakIdentity.publisherDisplayName
$peakManifest.Package.Applications.Application.VisualElements.DisplayName = $peakIdentity.displayName
$peakManifest.Save((Join-Path $peakStage 'AppxManifest.xml'))
$peakPriConfig = Join-Path $peakRoot ('.research/store-priconfig-' + [Guid]::NewGuid().ToString('N') + '.xml')
$peakResourceRoot = Join-Path $peakRoot ('.research/store-resource-input-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $peakResourceRoot | Out-Null
Copy-Item -LiteralPath (Join-Path $peakStage 'Assets') -Destination $peakResourceRoot -Recurse
& $MakePri createconfig /cf $peakPriConfig /dq en-US > (Join-Path $peakOutput 'makepri-validation.txt') 2>&1
if ($LASTEXITCODE -ne 0) { throw 'Store resource configuration failed.' }
[xml]$peakPri = Get-Content -LiteralPath $peakPriConfig
# Index a resource-only project from its root so the logical keys retain Assets/.
$peakPri.resources.index.startIndexAt = ''
# Keep all image scales in this single package rather than producing resource-package indexes.
if ($peakPri.resources.packaging) { [void]$peakPri.resources.RemoveChild($peakPri.resources.packaging) }
$peakPri.Save($peakPriConfig)
& $MakePri new /pr $peakResourceRoot /cf $peakPriConfig /of (Join-Path $peakStage 'resources.pri') /in $peakIdentity.name >> (Join-Path $peakOutput 'makepri-validation.txt') 2>&1
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath (Join-Path $peakStage 'resources.pri'))) { throw 'Store image resource indexing failed.' }
$peakPriDump = Join-Path $peakRoot ('.research/store-pridump-' + [Guid]::NewGuid().ToString('N') + '.xml')
& $MakePri dump /if (Join-Path $peakStage 'resources.pri') /of $peakPriDump /dt detailed >> (Join-Path $peakOutput 'makepri-validation.txt') 2>&1
if ($LASTEXITCODE -ne 0) { throw 'Store resource index inspection failed.' }
[xml]$peakResources = Get-Content -LiteralPath $peakPriDump
foreach ($peakLogo in 'StoreLogo.png', 'Square44x44Logo.png', 'Square150x150Logo.png') {
    $peakUri = "ms-resource://$($peakIdentity.name)/Files/Assets/$peakLogo"
    if (-not $peakResources.SelectSingleNode("//NamedResource[@uri='$peakUri']")) { throw "Store resource key does not match its manifest path: $peakLogo" }
}
$peakResourceValues = @($peakResources.SelectNodes('//Candidate/Value') | ForEach-Object { $_.InnerText })
foreach ($peakAsset in Get-ChildItem -LiteralPath (Join-Path $peakStage 'Assets') -File) {
    if ("Assets\$($peakAsset.Name)" -notin $peakResourceValues) { throw "Unindexed Store asset: $($peakAsset.Name)" }
}
# Semantic validation stays enabled. This upload is intentionally unsigned; the Store signs after certification.
& $MakeAppx pack /d $peakStage /p $peakPackage /h SHA256 > (Join-Path $peakOutput 'makeappx-validation.txt') 2>&1
if ($LASTEXITCODE -ne 0 -or -not (Test-Path -LiteralPath $peakPackage)) { throw 'MSIX packaging or manifest validation failed.' }
$peakSha256 = (Get-FileHash -LiteralPath $peakPackage -Algorithm SHA256).Hash.ToLowerInvariant()
[IO.File]::WriteAllText(($peakPackage + '.sha256'), "$peakSha256  $([IO.Path]::GetFileName($peakPackage))`n", [Text.UTF8Encoding]::new($false))
$peakMetadata = [ordered]@{
    version = $peakPackageVersion; architecture = 'x64'; identity = $peakIdentity
    developmentVersion = $peakFlavor.developmentVersion
    package = [IO.Path]::GetFileName($peakPackage); sha256 = $peakSha256
    signed = $false; target = 'Partner Center MSIX submission'; publicDownload = $false
    stageDirectory = $peakStage; builtAt = (Get-Date).ToUniversalTime().ToString('O')
}
[IO.File]::WriteAllText((Join-Path $peakOutput 'package.json'), ($peakMetadata | ConvertTo-Json -Depth 5) + "`n", [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText(([IO.Path]::ChangeExtension($peakPackage, '.json')), ($peakMetadata | ConvertTo-Json -Depth 5) + "`n", [Text.UTF8Encoding]::new($false))
Write-Output "Partner Center upload ready: $peakPackage"
Write-Output "SHA-256: $peakSha256"
