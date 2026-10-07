param(
    [string]$Promo = (Join-Path $PSScriptRoot '../../Peak-Promo-Remotion/Peak-Promo.mp4'),
    [string]$OutputDirectory = (Join-Path $PSScriptRoot '../artifacts/store/listing-assets/trailer'),
    [string]$Ffmpeg,
    [string]$Voice = 'Microsoft Mark'
)
$ErrorActionPreference = 'Stop'
$peakRoot = [IO.Path]::GetFullPath((Split-Path -Parent $PSScriptRoot))
$peakPromo = [IO.Path]::GetFullPath($Promo, $peakRoot)
$peakOutput = [IO.Path]::GetFullPath($OutputDirectory, $peakRoot)
$peakAllowedOutput = [IO.Path]::GetFullPath((Join-Path $peakRoot 'artifacts')) + [IO.Path]::DirectorySeparatorChar
if (-not $peakOutput.StartsWith($peakAllowedOutput, [StringComparison]::OrdinalIgnoreCase)) { throw 'Keep generated trailer assets inside this workspace artifacts folder.' }
for ($peakParent = $peakOutput; $peakParent; $peakParent = Split-Path -Parent $peakParent) {
    if ((Test-Path -LiteralPath $peakParent) -and ((Get-Item -LiteralPath $peakParent).Attributes -band [IO.FileAttributes]::ReparsePoint)) { throw 'Linked output folders are not allowed.' }
}
if (-not (Test-Path -LiteralPath $peakPromo -PathType Leaf)) { throw 'The supplied Peak promo is required.' }
if (-not $Ffmpeg) {
    $peakCommand = Get-Command ffmpeg.exe -ErrorAction SilentlyContinue
    $Ffmpeg = if ($peakCommand) { $peakCommand.Source } else {
        Join-Path (Split-Path -Parent $peakPromo) 'node_modules/@remotion/compositor-win32-x64-msvc/ffmpeg.exe'
    }
}
if (-not (Test-Path -LiteralPath $Ffmpeg -PathType Leaf)) { throw 'FFmpeg is required to extract the frame and encode the description.' }
foreach ($peakName in 'Peak-Promo.mp4', 'Peak-Promo-thumbnail.png', 'Peak-Promo.en-US.vtt', 'Peak-Promo-audio-description.mp3', 'Peak-Promo-audio-description.en-US.vtt', 'upload-fields.json', 'verification.json', 'README.txt') {
    if (Test-Path -LiteralPath (Join-Path $peakOutput $peakName)) { throw 'Preserve existing trailer outputs and choose a new OutputDirectory.' }
}
New-Item -ItemType Directory -Force -Path $peakOutput | Out-Null
$peakResearch = Join-Path $peakRoot ('.research/store-trailer-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $peakResearch | Out-Null
$peakFields = Get-Content -LiteralPath (Join-Path $peakRoot 'store/trailer/upload-fields.json') -Raw | ConvertFrom-Json
if ((Get-FileHash -LiteralPath $peakPromo -Algorithm SHA256).Hash.ToLowerInvariant() -ne $peakFields.sourceSha256) { throw 'The promo changed; review and update its synchronized description and captions first.' }
$peakDescription = @(Get-Content -LiteralPath (Join-Path $peakRoot 'store/trailer/audio-description.en-US.json') -Raw | ConvertFrom-Json)
& $Ffmpeg -nostdin -hide_banner -loglevel error -ss $peakFields.thumbnailTimestamp -i $peakPromo -frames:v 1 -update 1 -compression_level 9 (Join-Path $peakOutput 'Peak-Promo-thumbnail.png')
if ($LASTEXITCODE -ne 0) { throw 'Thumbnail extraction failed.' }
Copy-Item -LiteralPath (Join-Path $peakRoot 'store/trailer/captions.en-US.vtt') -Destination (Join-Path $peakOutput 'Peak-Promo.en-US.vtt')
Copy-Item -LiteralPath $peakPromo -Destination (Join-Path $peakOutput 'Peak-Promo.mp4')
Copy-Item -LiteralPath (Join-Path $peakRoot 'store/trailer/upload-fields.json') -Destination $peakOutput
Add-Type -AssemblyName System.Speech
$peakSpeech = [System.Speech.Synthesis.SpeechSynthesizer]::new()
$peakSegments = @()
try {
    $peakSpeech.SelectVoice($Voice)
    $peakIndex = 0
    foreach ($peakCue in $peakDescription) {
        $peakIndex++
        $peakWave = Join-Path $peakResearch "voice-$peakIndex.wav"
        $peakSlot = [double]$peakCue.end - [double]$peakCue.start
        if ($peakSlot -le 0 -or $peakCue.start -lt 0 -or $peakCue.end -gt $peakFields.sourceDurationSeconds) { throw 'Invalid narration window.' }
        $peakRate = 0
        do {
            $peakSpeech.Rate = $peakRate
            $peakSpeech.SetOutputToWaveFile($peakWave)
            $peakSpeech.Speak($peakCue.text)
            $peakSpeech.SetOutputToNull()
            $peakMedia = & (Join-Path (Split-Path -Parent $Ffmpeg) 'ffprobe.exe') -v error -show_entries format=duration -of json $peakWave | ConvertFrom-Json
            if ($LASTEXITCODE -ne 0) { throw 'Narration duration inspection failed.' }
            $peakSeconds = [double]$peakMedia.format.duration
            if ($peakSeconds -le $peakSlot) { break }
            $peakRate++
        } while ($peakRate -le 3)
        if ($peakSeconds -gt $peakSlot) { throw "Narration does not fit scene $peakIndex; shorten the description." }
        $peakSegments += [pscustomobject]@{ index=$peakIndex; start=[double]$peakCue.start; end=([double]$peakCue.start + $peakSeconds); text=$peakCue.text; duration=$peakSeconds; rate=$peakRate; wave=$peakWave }
    }
} finally { $peakSpeech.Dispose() }
$peakFfmpegArgs = @('-nostdin', '-hide_banner', '-loglevel', 'error', '-i', $peakPromo)
$peakFilterParts = @()
$peakVoiceLabels = ''
foreach ($peakSegment in $peakSegments) {
    $peakFfmpegArgs += @('-i', $peakSegment.wave)
    $peakDelay = [int][Math]::Round($peakSegment.start * 1000)
    $peakFilterParts += "[$($peakSegment.index):a]aresample=48000,adelay=${peakDelay}:all=1[v$($peakSegment.index)]"
    $peakVoiceLabels += "[v$($peakSegment.index)]"
}
$peakDuration = ([double]$peakFields.sourceDurationSeconds).ToString('0.000', [Globalization.CultureInfo]::InvariantCulture)
$peakFilterParts += "${peakVoiceLabels}amix=inputs=$($peakSegments.Count):normalize=0:duration=longest,apad,atrim=duration=$peakDuration,loudnorm=I=-16:TP=-3:LRA=7[voice]"
$peakFilterParts += '[0:a]aresample=48000,volume=0.12[music]'
$peakFilterParts += '[music][voice]amix=inputs=2:normalize=0:duration=longest[mix]'
$peakFfmpegArgs += @('-filter_complex', ($peakFilterParts -join ';'), '-map', '[mix]', '-ac', '2', '-ar', '48000', '-c:a', 'libmp3lame', '-b:a', '192k', '-t', $peakDuration, (Join-Path $peakOutput 'Peak-Promo-audio-description.mp3'))
& $Ffmpeg @peakFfmpegArgs
if ($LASTEXITCODE -ne 0) { throw 'Audio description mixing failed.' }
function Format-PeakVttTime([double]$seconds) { [TimeSpan]::FromMilliseconds([Math]::Round($seconds * 1000)).ToString('hh\:mm\:ss\.fff') }
$peakDescriptionVtt = "WEBVTT`n`n"
foreach ($peakSegment in $peakSegments) {
    $peakDescriptionVtt += "$($peakSegment.index)`n$(Format-PeakVttTime $peakSegment.start) --> $(Format-PeakVttTime $peakSegment.end)`n$($peakSegment.text)`n`n"
}
[IO.File]::WriteAllText((Join-Path $peakOutput 'Peak-Promo-audio-description.en-US.vtt'), $peakDescriptionVtt, [Text.UTF8Encoding]::new($false))
$peakVerification = [ordered]@{
    source = $peakPromo; sourceSha256 = (Get-FileHash -LiteralPath $peakPromo -Algorithm SHA256).Hash.ToLowerInvariant()
    voice = $Voice; descriptionDuration = $peakFields.sourceDurationSeconds
    segments = @($peakSegments | Select-Object index,start,end,duration,rate,text)
    generatedAt = (Get-Date).ToUniversalTime().ToString('O')
}
[IO.File]::WriteAllText((Join-Path $peakOutput 'verification.json'), ($peakVerification | ConvertTo-Json -Depth 6), [Text.UTF8Encoding]::new($false))
$peakGuide = @"
Peak promo Store upload

Title: $($peakFields.title)
Image caption: $($peakFields.imageCaption)

Trailer video: Peak-Promo.mp4 (an unchanged copy of $peakPromo)
Thumbnail: Peak-Promo-thumbnail.png (1920 x 1080, still at 34.5 seconds)
Closed captions for the original trailer: Peak-Promo.en-US.vtt (music cues; no spoken dialogue)
Audio description: Peak-Promo-audio-description.mp3 (English narration synchronized to the visuals; original music quieter underneath)

The extra Peak-Promo-audio-description.en-US.vtt contains the narration transcript for accessibility players that support captions for the described audio. Use the music-cue VTT for the original trailer's closed-caption upload.

The source MP4 was preserved. Upload files through Partner Center and save the trailer. Optional audio description was generated locally using the installed $Voice voice.
"@
[IO.File]::WriteAllText((Join-Path $peakOutput 'README.txt'), $peakGuide, [Text.UTF8Encoding]::new($false))
Write-Output "Trailer thumbnail, captions and synchronized audio description ready: $peakOutput"
