# Peak Store trailer assets

Use the supplied 36-second `Peak-Promo.mp4`. It contains music and no spoken narration. Its screens use sample data.

The generated files are under `artifacts/store/listing-assets/trailer`:

| Partner Center field | File or text |
| --- | --- |
| Trailer video | `Peak-Promo.mp4`, an unchanged copy of the supplied video |
| Thumbnail | `Peak-Promo-thumbnail.png`, 1920 × 1080 PNG extracted at 34.5 seconds |
| Title | Peak - Stay in Flow |
| Image caption | Peak for Windows: AI usage limits and Codex task activity. Sample data shown. |
| Closed captions | `Peak-Promo.en-US.vtt` |
| Audio descriptions | `Peak-Promo-audio-description.mp3` |

The caption file describes the original music, without inventing dialogue. The audio description uses an installed English Windows voice to describe all seven scenes in synchronization with the video, with the original music quieter underneath. The extra `Peak-Promo-audio-description.en-US.vtt` transcribes that narration for players supporting captions for the described track; it is not the caption file for the original music-only video.

The source MP4 is 1920 × 1080, 30 fps, H.264/AVC1 High profile, progressive 4:2:0 with stereo AAC at 48 kHz. The MP3 is stereo, 48 kHz, 192 kbps, about 36 seconds. The thumbnail and supplementary files are below the dashboard's size limits. The MP4 copy is byte-identical to the source. These are local media checks, not Store certification.

After uploading the video, add its thumbnail, title and caption, then upload the music-cue VTT and description MP3 in their respective sections. Save the trailer and review playback in the dashboard.

Microsoft requires a thumbnail and title for each trailer; captions and audio descriptions are accessibility additions. See [Microsoft's trailer guidance](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/screenshots-and-images#upload-trailers).

## Re-create the assets

Run `./scripts/prepare-store-trailer.ps1`. It uses the local promo project's FFmpeg and the installed Microsoft Mark voice by default. If that voice is unavailable, pass an installed English voice with `-Voice`, such as `Microsoft Zira Desktop`. Choose a new `-OutputDirectory` beneath the workspace artifacts folder to preserve existing outputs.

The description windows, music captions and upload text are under `store/trailer`. The script checks the supplied video's hash so a replacement video cannot silently reuse outdated timing or narration. Review those sources after editing the promo.
