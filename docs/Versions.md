# Peak versions

Name new versioned build folders `Peak-<version>` (for example, `Peak-1.0.3`). Record Microsoft Store availability here instead of adding issue names to build folders.

Development and Microsoft Store versions advance independently. The development version is in the app project; the Store upload version is in `store/AppxManifest.xml`. A Store build uses the Store version in its executable, assemblies, About view and MSIX package, while including all current development changes.

## Microsoft Store 1.0.3

Not yet released on Microsoft Store.

- Next upload after Microsoft Store 1.0.2; package version `1.0.3.0`.
- Includes all development changes through 1.0.5: resolved approval alerts, hover fading, VS Code activity detection, Inter typography, supplied provider images, borderless transitions, and both notch styles.
- Upload package: `artifacts/store/Peak-1.0.3.0-Windows-x64-Store.msix`.
- Preparing an upload does not mark the development builds as separately released on Microsoft Store.
- Verification: all 191 tests passed in each build configuration; Windows SDK package validation passed and all 700 payload files match the staging build. See [Store verification](Store-verification.md).

## Development 1.0.5

Not yet released on Microsoft Store.

- The notch has no outer outline and opens as one continuous shape, with smoothly timed expansion and fading details.
- Opening and closing animate inside a stable window, keeping text layout steady and allowing transitions to reverse smoothly.
- Settings → Appearance → Notch style offers Dynamic Island or Fixed Notch. Fixed Notch joins the screen's top edge and keeps a compact resting size until opened.
- The floating island's saved top gap is restored when switching back from Fixed Notch. Windows' reduced-animation preference is respected.
- The supplied Codex and Claude images replace the Cx/Cl abbreviations inside the notch's usage rings.
- Bundled Inter typography uses bolder titles and usage figures, semibold task text, and medium-weight body text throughout Peak.
- Active local Codex turns, including work from VS Code, show as working without requiring fresh hook events.
- Fresh model lifecycle records recover ongoing work when the start of a long turn is outside the bounded log tail.
- Resumed chats are found by recent file updates even when their session folder is older.
- Late results do not reopen finished turns; unresolved hook approvals and questions retain priority.
- Verification: all 191 tests passed, plus rendered opening, closing, interruption, and style previews.

## Development 1.0.4

Not yet released on Microsoft Store.

- Approval and question alerts respect the configured hover opacity and let clicks pass through while faded.
- Moving the pointer away restores the alert; Alt hover and pinned details keep it readable and interactive.
- Verification: all 168 tests passed.

## Development 1.0.3

Not yet released on Microsoft Store.

- Completed commands clear their matching approval requests even when Codex adds an approval description.
- Each request expires after five minutes independently of other task activity.
- Verification: all 162 tests passed.
