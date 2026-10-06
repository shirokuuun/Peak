# Peak 1.0.1 provider connection verification

Reviewed October 6, 2026.

Enabling either usage switch opens connection setup. Existing Codex ChatGPT logins are reused; missing native CLIs can be installed from the fixed official provider installer URLs. Sign-in runs in the provider's own terminal/browser. Enabling Claude installs its bridge automatically, then waits for fresh reported subscription windows. Preferences still require Save changes. Connection actions are immediate and canceling preferences does not undo installations, logins or bridge setup.

- Release build: zero warnings or errors; all 153 tests pass with no skips.
- Connection tests cover missing CLIs, native installer locations with stale PATH, npm native Codex resolution, configured credential-store inheritance, existing ChatGPT login, API-key account separation, recoverable transport failures, cancellation, and fresh versus stale Claude limits.
- Claude integration tests run the actual installed status-line command through PowerShell and Git Bash, including a bridge path with spaces and Unicode. Fixture payloads produce the correct 25% used / 75% remaining snapshot without writing user data.
- A development-PC read-only native Codex RPC check returned a signed-in account and two rate-limit windows through the configured login store. Account labels and raw responses were never logged or saved. This confirms compatibility on that machine, not on the affected person's machine.
- New setup windows were rendered and visually inspected with fixture labels. Official installation and interactive provider authentication were not run by automated tests, so a fresh Windows account walkthrough remains necessary.
- Package and public-download hash verification are recorded in the release notes. Updates remain manual; an installed 1.0.0 build does not gain this flow until updated.

The report supplied by the affected user says both codex and claude commands are unrecognized in PowerShell. This establishes that those commands were unavailable in that terminal; it does not by itself prove the binaries were absent from every provider-specific install location. Peak now checks the normal native installation folders as well.
