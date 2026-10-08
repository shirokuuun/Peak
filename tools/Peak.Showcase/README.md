# Peak showcase renderer

This tool renders the actual controls from the already published Store assembly with isolated sample data. It never runs Peak's application startup or opens native windows, installs hooks, polls providers, registers shortcuts, or modifies the user's existing Peak instance. The settings renderer redirects CLI configuration probes to nonexistent fixture directories. MainViewModel uses cached sample usage and a finite in-memory task monitor.

Run from the app repository with an unused output path:

```powershell
dotnet run --project tools/Peak.Showcase/Peak.Showcase.csproj -- artifacts/store/Peak-Showcase-2026-10-08
```

The default assembly root is `dist/Peak-Store-1.0.2-final`. Override the MSBuild `PeakAssemblyRoot` property when using another published assembly; the renderer does not compile or modify Peak. Five PNG feature images export at 1920 × 1080, with native controls captured at 3× resolution before composition. The six `native-*` PNGs contain transparent app captures for other layouts. Export captions and an upload guide accompany the images.

Presentation graphics use Bahnschrift, a Windows-provided font. Headings, frames, and shadows sit outside the native UI. Usage percentages indicate **remaining** limits. Attention examples concern Codex; they do not imply Claude task monitoring. All provider values, task titles, and projects are fictional and labelled as sample data.

Upload the five numbered images through the existing Store listing update. The approved MSIX and its version are not altered by this tool. The reference MSIX SHA-256 remains `beb0ac3117fd6db62a8e73da05f53c6488148612d2ca1fa7154611bc2e29ebdc`.
