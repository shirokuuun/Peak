param(
    [string]$Manifest = (Join-Path $PSScriptRoot '../artifacts/releases/release.json'),
    [Parameter(Mandatory = $true)][string]$WebsiteRoot
)
$ErrorActionPreference = 'Stop'
$peakManifest = Get-Content -LiteralPath $Manifest -Raw | ConvertFrom-Json
$peakWebsite = [IO.Path]::GetFullPath($WebsiteRoot)
$peakConfig = Get-Content -LiteralPath (Join-Path $PSScriptRoot '../src/UsageNotch.App/publishing.json') -Raw | ConvertFrom-Json
$peakPublication = Get-Content -LiteralPath (Join-Path $peakWebsite 'src/data/publication.json') -Raw | ConvertFrom-Json
foreach ($peakField in @('publisherName', 'supportEmail', 'websiteUrl', 'policyVersion')) {
    if ($peakConfig.$peakField -ne $peakPublication.$peakField) { throw "App and website $peakField differ." }
}
$peakPolicies = @{ 'Privacy.md' = 'privacy.md'; 'Terms.md' = 'terms.md'; 'Third-party-notices.md' = 'notices.md' }
foreach ($peakPair in $peakPolicies.GetEnumerator()) {
    $peakAppText = [IO.File]::ReadAllText((Join-Path $PSScriptRoot "../src/UsageNotch.App/Policies/$($peakPair.Key)")).Replace("`r`n", "`n")
    $peakSiteText = [IO.File]::ReadAllText((Join-Path $peakWebsite "src/content/policies/$($peakPair.Value)")).Replace("`r`n", "`n")
    if ($peakAppText -cne $peakSiteText) { throw "The app and website policy $($peakPair.Key) differ." }
}
$peakDownloadFolder = Join-Path $PSScriptRoot '../artifacts/download-verification'
New-Item -ItemType Directory -Force -Path $peakDownloadFolder | Out-Null
foreach ($peakKind in @('installer', 'portable')) {
    $peakAsset = $peakManifest.$peakKind
    $peakUri = [Uri]$peakAsset.url
    if ($peakUri.Scheme -ne 'https' -or $peakUri.Host -ne 'github.com' -or $peakUri.UserInfo -or $peakUri.AbsolutePath -notmatch '/releases/download/v[0-9]') { throw 'Use official versioned public GitHub assets.' }
    if ($peakAsset.sha256 -notmatch '^[a-f0-9]{64}$') { throw 'Missing SHA-256 checksum.' }
    $peakFile = Join-Path $peakDownloadFolder ([IO.Path]::GetFileName($peakUri.LocalPath))
    # Anonymous download proves users need no account.
    Invoke-WebRequest -Uri $peakUri.AbsoluteUri -OutFile $peakFile
    $peakHash = (Get-FileHash -LiteralPath $peakFile -Algorithm SHA256).Hash.ToLowerInvariant()
    if ($peakHash -ne $peakAsset.sha256) { throw "Downloaded $peakKind does not match the packaged checksum." }
    Write-Output "Anonymous download and SHA-256 verified: $peakKind"
}
$peakManifest.available = $true
$peakManifest.verified = $true
$peakJson = ($peakManifest | ConvertTo-Json -Depth 6) + "`n"
[IO.File]::WriteAllText($Manifest, $peakJson, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $peakWebsite 'src/data/release.json'), $peakJson, [Text.UTF8Encoding]::new($false))
Write-Output 'Verified release manifest copied to the website. Public builds can now enable downloads.'
