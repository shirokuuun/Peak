# Peak Store candidate verification

## 1.0.3

Checked October 28, 2026 (Asia/Manila).

Not yet released on Microsoft Store. Prepared for upload after Store 1.0.2, using all changes through development 1.0.5. Source commit: `72bf080737b3b83c39c324df363d8162275e0fae`.

Candidate: `Peak-1.0.3.0-Windows-x64-Store.msix`, 119,311,845 bytes.

SHA-256: `f52e3ea12fe9fb81a517a7ec8a6f3d420a844598ac5de4f8f36b05780c7e1507`

- Development and Store configurations each passed all 191 tests, without skips. The development Release build and self-contained Store publication completed without warnings or errors.
- Development stays at 1.0.5 in the app project. Store publication reads 1.0.3.0 from the manifest and applies version 1.0.3 to the executable, app assemblies and bridge. The build marker and versioned package metadata record development 1.0.5 as the source of its changes.
- All eight app/bridge project binaries report product version 1.0.3. The published Core assembly reports the Store flavor and assembly version 1.0.3.0; the app and bridge contain identical Core bytes.
- MakeAppx packaged and semantically validated the manifest. Identity, publisher, version and x64 architecture were checked again inside the finished package. MakePri generated and validated the image resource index, including all scaled assets and their manifest keys.
- All 700 payload files match their staging bytes. Nine license files include the Inter font license. The package contains no personal settings, credentials, observation logs, debug symbols or signing keys, and has no signing signature.
- The final SHA-256 matches both package metadata and its checksum sidecar. The original 1.0.2.0 package remains unchanged, with its previous recorded SHA-256.
- Packaging rejects the older 1.0.5 publication when it does not match the Store manifest. Existing package files are preserved rather than overwritten.
- The 1.0.5 opening, closing, rapid-reversal and notch-style previews were inspected before this Store rebuild. The staged source scan reported no secret findings before its GitHub push.

Upload artifact: `artifacts/store/Peak-1.0.3.0-Windows-x64-Store.msix`. Build folder: `dist/store/Peak-1.0.3`. The upload, Microsoft signing and certification have not occurred. This check did not repeat the earlier isolated package integration fixture, run Windows App Certification Kit, or perform fresh install/update/uninstall and live-provider sign-in testing. Those interactive and certification checks remain required before release.

## Previous 1.0.2 candidate

Checked October 7, 2026 on Windows 11 build 26200 with .NET SDK 10.0.401 and Windows SDK tools 10.0.19041.0.

Candidate: `Peak-1.0.2.0-Windows-x64-Store.msix`, 118,592,703 bytes.

SHA-256: `beb0ac3117fd6db62a8e73da05f53c6488148612d2ca1fa7154611bc2e29ebdc`

This is an unsigned Partner Center upload, not an approved Store release or a trusted website installer.

## Completed checks

- Desktop and Store builds each passed all 156 tests without skips. The final self-contained Store publication completed without warnings or errors.
- MakeAppx packaged and semantically validated the manifest. The reserved name, identity, publisher, version and x64 architecture match the supplied Partner Center details. `runFullTrust` is the only capability.
- MakePri produced one resource index containing all 11 image variants. All three logical logo keys match the manifest's `Assets` paths; scale and target-size variants are indexed in the main package.
- All 698 payload files match their staging bytes. The package contains eight license files and matching policy bundle 1.1. Private credentials, personal settings, debug symbols, signing keys and observation logs were excluded. App and external bridge Core assemblies have identical Store flavor bytes.
- A separate temporary development package used the published assemblies to save/read isolated preferences, copy/install the Claude bridge into a UUID fixture, and read a sanitized 25% usage snapshot written by the real bridge from an external shell. Removal restored the fixture provider settings.
- That temporary package enabled and disabled its own declared Windows startup task and registered the notification compatibility layer. No notification was sent; notification delivery and activation still need interactive verification.
- A harmless child terminal launched through the same provider-install launch settings had no package identity, and its new AppData fixture was visible outside the package. Actual vendor installation and provider sign-in were not performed by this check.
- The final WPF About and Privacy views were rendered and inspected. Store branding, setup links and sample preview controls were present. Live cleanup controls were disabled in sample mode.
- Website installer and portable packaging reject the Store build marker. Packaging scripts passed PowerShell syntax checks.

The development test package was unregistered and its UUID fixtures removed. Existing Peak instances and real provider configurations were not changed. Development mode was already enabled; no machine security settings were changed for this verification.

## Still required before certification

Windows App Certification Kit did not run: the installed tool requires an elevated session. On a clean development/test machine, run the kit and verify actual package installation, first-run behavior, provider installation/sign-in, live readings, notification delivery/activation, Windows startup opt-outs, update and uninstall. These checks do not establish compatibility on other machines or Windows 10.

For uninstall testing, disconnect providers first, restart existing CLI sessions and verify unrelated settings remain intact. Also verify that retained bridges/restoration data remain usable when the package is removed without prior disconnection.

Review the dashboard's required sections and final listing before submitting. Microsoft Store certification and signing have not occurred. The existing website download remains version 1.0.1.
