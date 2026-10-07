param([string]$OutputDirectory = (Join-Path $PSScriptRoot '../store/Assets'))
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.Drawing
$peakSource = [Drawing.Image]::FromFile((Join-Path $PSScriptRoot '../src/UsageNotch.App/Assets/Notch.png'))
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
try {
    $peakAssets = @{
        'StoreLogo.png' = 50; 'StoreLogo.scale-200.png' = 100
        'Square44x44Logo.png' = 44; 'Square44x44Logo.scale-200.png' = 88
        'Square150x150Logo.png' = 150; 'Square150x150Logo.scale-200.png' = 300
        'Square44x44Logo.targetsize-16_altform-unplated.png' = 16
        'Square44x44Logo.targetsize-24_altform-unplated.png' = 24
        'Square44x44Logo.targetsize-32_altform-unplated.png' = 32
        'Square44x44Logo.targetsize-48_altform-unplated.png' = 48
        'Square44x44Logo.targetsize-256_altform-unplated.png' = 256
    }
    foreach ($peakAsset in $peakAssets.GetEnumerator()) {
        $peakBitmap = [Drawing.Bitmap]::new($peakAsset.Value, $peakAsset.Value, [Drawing.Imaging.PixelFormat]::Format32bppArgb)
        $peakGraphics = [Drawing.Graphics]::FromImage($peakBitmap)
        try {
            $peakGraphics.Clear([Drawing.Color]::Transparent)
            $peakGraphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
            $peakGraphics.PixelOffsetMode = [Drawing.Drawing2D.PixelOffsetMode]::HighQuality
            $peakGraphics.DrawImage($peakSource, [Drawing.Rectangle]::new(0, 0, $peakAsset.Value, $peakAsset.Value))
            $peakBitmap.Save((Join-Path $OutputDirectory $peakAsset.Key), [Drawing.Imaging.ImageFormat]::Png)
        } finally { $peakGraphics.Dispose(); $peakBitmap.Dispose() }
    }
} finally { $peakSource.Dispose() }
Write-Output "Store logo assets ready: $OutputDirectory"
