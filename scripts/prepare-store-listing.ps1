param([string]$Website = (Join-Path $PSScriptRoot '../../peak_website'))
$ErrorActionPreference = 'Stop'
$peakRoot = Split-Path -Parent $PSScriptRoot
$peakAssets = Join-Path $peakRoot 'artifacts/store/listing-assets'
New-Item -ItemType Directory -Force -Path $peakAssets | Out-Null
Copy-Item -LiteralPath (Join-Path $peakRoot 'store/Assets/Square150x150Logo.scale-200.png') -Destination (Join-Path $peakAssets 'store-logo-300.png')
foreach ($peakScreen in 'limits-dark.png', 'limits-light.png', 'tasks-dark.png', 'settings-appearance.png', 'settings-presence.png') {
    Copy-Item -LiteralPath (Join-Path $Website "public/screens/$peakScreen") -Destination $peakAssets
}
Copy-Item -LiteralPath (Join-Path $Website 'public/media/peak-promo.mp4') -Destination $peakAssets
Copy-Item -LiteralPath (Join-Path $peakRoot 'docs/Store-submission.md') -Destination (Join-Path $peakAssets 'Store-submission.md')
Copy-Item -LiteralPath (Join-Path $peakRoot 'docs/Store-verification.md') -Destination (Join-Path $peakAssets 'Store-verification.md')
Copy-Item -LiteralPath (Join-Path $peakRoot 'docs/Store-trailer.md') -Destination (Join-Path $peakAssets 'Store-trailer.md')
Write-Output "Store listing copy and assets ready: $peakAssets"
