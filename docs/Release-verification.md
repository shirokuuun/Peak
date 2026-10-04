# Provider logos and initial repository verification

Checked October 4, 2026 on Windows.

The compact usage rings now display the original Codex and Claude logo images instead of initials. Expanded provider headings use the same images. Native checks confirmed both images load from the published executable and remain visible on dark and light notch backgrounds. The compact sample readings stay tied to the 5-hour window: Codex shows 10% remaining and Claude 68%, while their weekly readings remain in the expanded cards.

The Release build completed with no warnings or errors, and all 94 automated tests passed. The dependency audit reported no known vulnerable packages from the configured NuGet source, including transitive dependencies.

Before the first GitHub commit, the staged diff and an exported copy of the exact staged files were independently scanned with Gitleaks 8.30.1, with redacted output. Both scans reported zero findings. The staged file list was reviewed for credentials, private runtime observations, build output, diagnostics, and linked files. Only source, synthetic tests, scripts, documentation, and the four intended image/icon assets were selected. Image metadata contained no personal text fields; the test email is a synthetic `example.test` address.

Runtime preferences, provider caches, bridge setup, Claude credentials, and GitHub authentication stay outside the repository. No credential-like environment variables were present in the process, user, or machine environment during the check. Repository ignore rules exclude local credentials and diagnostics, and the initial commit uses the GitHub account's private commit email.

These checks document the selected files at publication time; they do not guarantee detection of every possible secret or future dependency issue. Additional-machine, mixed-DPI, full-screen, and capture-tool checks remain as described in the settings verification and README.
