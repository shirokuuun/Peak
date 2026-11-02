# Peak versions

Name new versioned build folders `Peak-<version>` (for example, `Peak-1.0.3`). Record Microsoft Store availability here instead of adding issue names to build folders.

Development and Microsoft Store versions advance independently. The development version is in the app project; the Store upload version is in `store/AppxManifest.xml`. A Store build uses the Store version in its executable, assemblies, About view and MSIX package, while including all current development changes.

## Microsoft Store 1.0.5

Not yet released on Microsoft Store.

- Package version `1.0.5.0`, containing the latest development 1.0.6 features. The earlier 1.0.4 upload package is preserved.
- Fixed Notch has curved shoulders that join the top screen edge in compact, expanded and Appearance-preview views, while keeping the original content width.
- Transparent space around the curves passes clicks through, and opening still uses a stable host window.
- Includes the VS Code activity fix, long-task recovery, partial-write handling, pending questions and completion/cancellation safeguards.
- Retains Dynamic Island, borderless transitions, configurable hover fading, Inter typography and the supplied provider images.
- Upload package: `artifacts/store/Peak-1.0.5.0-Windows-x64-Store.msix`.
- Verification: 210 Store-configuration tests passed, plus native render checks for compact, expanded and settings-preview shoulders and opening/closing transitions.
- Windows SDK package validation passed; all 700 payload files match the staging build. See [Store verification](Store-verification.md).

## Microsoft Store 1.0.4

Not yet released on Microsoft Store.

- Next upload after Store 1.0.3; package version `1.0.4.0`, including development 1.0.6.
- Fixes running Codex tasks in VS Code appearing only as recent activity.
- Recovers work from turn-tagged model records, supports legacy assistant activity, and keeps lifecycle state as large outputs move the recent log window forward.
- Handles partial writes, completion, cancellation, pending questions and stale observations without retaining prompts, outputs or token totals.
- Upload package: `artifacts/store/Peak-1.0.4.0-Windows-x64-Store.msix`.
- Verification: all 210 automated tests passed in both development and Store configurations, including replay coverage for the affected VS Code format.
- Windows SDK package validation passed; all 700 payload files match their staging build. See [Store verification](Store-verification.md).

## Development 1.0.6

Not yet released on Microsoft Store.

- Fixed Notch now has outward-curving shoulders that merge into the screen's top edge, including its expanded view and Appearance preview. The content area keeps its original width.
- Transparent space around the curved edges lets clicks pass through. The opening animation still uses a stable host window.
- This appearance refinement was added after the Store 1.0.4 candidate was built and is included in the Store 1.0.5 candidate.
- VS Code task detection supports its model usage records, assistant messages, reasoning and turn context alongside the desktop activity format.
- Local task reads advance from the last complete record and retain only lifecycle state in memory. Cold-start reads and large updates remain bounded to 256 KiB.
- Original observed start times and pending questions survive log-window rollover. Truncated/replaced logs reset the previous state; incomplete records are retried after the remaining bytes arrive.
- Completion and cancellation still clear work, unrelated chats cannot claim its turn, and expired activity does not keep the notch open.
- Replaying the affected VS Code session now reports Working before completion and Idle after completion.
- Verification: 210 automated tests passed, including 19 VS Code and incremental-reader regression cases. Native render checks cover the attached shoulders at compact, expanded and settings-preview sizes, plus opening, closing and rapid reopening.

## Microsoft Store 1.0.3

Prepared October 28, 2026. Store package 1.0.3.0 was observed installed on November 2, 2026.

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
