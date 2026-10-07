# Peak Microsoft Store submission

Reserved name: **Peak - AI Usage Limits & Activity**

Publisher: **neorangel**

Package identity: `neorangel.Peak-AIUsageLimitsActivity`

Package publisher: `CN=1A8E4068-1357-4BD8-8811-797CDAFAA54F`

## Upload

Open the current draft → Packages and upload:

`D:\Languages\codex\notch\artifacts\store\Peak-1.0.2.0-Windows-x64-Store.msix`

This x64 package includes the runtime, Claude/task bridge, policy bundle 1.1, licenses, Start-menu assets, notification registration and an optional Windows startup task. It starts with providers and startup disabled. It is intentionally unsigned for Partner Center ingestion. Microsoft signs Store-distributed MSIX packages after certification. Do not offer this unsigned upload as a public website download.

Microsoft's [MSIX submission requirements](https://learn.microsoft.com/en-us/windows/apps/publish/publish-your-app/msix/app-package-requirements) accept individual MSIX packages. The fourth version component is zero. The exact reserved name, package identity, publisher and publisher display name are included in the manifest.

## Pricing and properties

- Set the base price to **Free**; choose the markets where you want to distribute Peak.
- Choose the category that best matches this desktop utility (Utilities & tools, if available).
- Privacy-policy URL: https://peakforwindows.vercel.app/privacy/
- Support URL: https://peakforwindows.vercel.app/support/
- Website: https://peakforwindows.vercel.app/
- Support email: peakforwindows.support@gmail.com
- Complete the age-rating questionnaire based on Peak's actual functionality. It is a local usage monitor, not an AI chat or content-generation client. Do not invent a rating.

## English (United States) listing

### Description

Live readings require compatible Windows provider tools and accounts: Codex CLI with ChatGPT sign-in, or Claude Code CLI with a Claude Pro or Max subscription. Claude reports subscription limits after its first response. Peak does not provide an AI subscription or credits.

Peak puts AI subscription usage and Codex task activity in a compact Windows notch, so you can check your remaining limits without switching away from your work.

See reported 5-hour and weekly usage windows, reset countdowns and the freshness of your last reading. Follow Codex task activity and observed requests for attention, with details kept in the provider itself. Missing or stale readings remain clearly labeled rather than being shown as zero usage.

Choose when Peak stays visible, adjust its appearance, keep it out of the way in fullscreen, and bring it back with a tray menu or keyboard shortcut. Optional usage and task alerts help you notice the updates you select.

Connection setup checks your installed Windows CLIs and reuses compatible existing logins. If a CLI is missing, Peak offers its official installer; provider sign-in happens in the provider's own terminal or browser. Codex hook observations require your separate review and trust in Codex. Peak never approves tools or runs model prompts on your behalf.

Peak is free for personal and workplace use, on unlimited devices, with no Peak account, payment, activation or expiry. It has no telemetry. Usage observations and preferences stay on your computer; provider services continue to follow their own terms and privacy practices.

Try the labeled sample demo from Settings → About & Support without connecting a provider. For live readings, finish provider setup and save your choices. Claude task monitoring is not included.

Before uninstalling, disconnect providers in Peak's Privacy settings and restart their existing sessions. The Store edition retains copied bridges and protected restoration information in your per-user data folder until safely removed. See the included user guide and the support website for details.

Peak is an independent utility and is not affiliated with or endorsed by OpenAI, Anthropic or Microsoft.

### Short description

Keep AI subscription limits and Codex task activity in view with a compact Windows notch.

### Feature bullets

- Reported Codex and Claude subscription usage windows and reset countdowns
- Codex task activity with observed attention states and freshness indicators
- Guided Windows CLI connection setup and an automatic Claude usage bridge
- Configurable appearance, visibility, fullscreen behavior and reveal shortcut
- Optional usage, reset and Codex task notifications
- Local observations, explicit provider consent and protected restoration information
- Free personal and workplace use without a Peak account or activation

### Search terms

AI usage; usage limits; Codex; Claude Code; desktop utility; subscription monitor; Windows notch

### What's new

Initial Microsoft Store edition. Guided provider setup, a shared per-user bridge folder, Windows-managed startup and a sample preview available without provider sign-in.

### Listing graphics

The prepared listing assets are in `artifacts/store/listing-assets`. The screenshots use sample data. Their captions should say so:

- Usage limits and reset countdowns — sample data.
- Codex task activity — sample data.
- Dark and light appearance options — sample data.
- Configure Peak's appearance and visibility.
- Store edition settings and local-data controls — sample preview.

For the supplied promo, use the [trailer upload guide](Store-trailer.md). Its 1920 × 1080 thumbnail, music captions and synchronized audio description are prepared under `artifacts/store/listing-assets/trailer`.

## Notes for certification

Peak is a self-contained WPF desktop utility; it requires no administrator privileges, driver or Windows service. The runFullTrust capability is needed to display its tray/desktop overlay, use native window and hotkey APIs, query the user's installed Codex CLI app-server, and connect to local Codex/Claude configuration and observation files after the user opts in. It does not read or save provider credential files, approve provider tools, or issue model prompts.

Codex CLI and Claude Code CLI are external dependencies for live readings and are disclosed at the start of the description. Peak offers only the providers' fixed official installers after the user chooses Install & connect. These tools supply Peak's described core functionality. Users complete sign-in directly with the provider. There is no Peak login, payment or activation.

To review the interface without credentials: launch Peak, choose No on any request to keep existing provider choices, open Settings → About & Support → Preview sample demo. Sample readings are labeled DEMO. Provider setup, startup changes, live provider polling and live-data cleanup are disabled in the demo. Use Leave sample demo to return to the normal session. Normal installation never substitutes demo readings for live data.

Live integration testing requires a compatible provider account and local Windows CLI. No shared provider credentials are included or invented. If the reviewer needs a dedicated live account, the publisher must arrange an approved testing method rather than disclose a personal account.

The Store edition uses `%USERPROFILE%\.peak\store` for app/bridge state so external provider processes can share sanitized observations without disabling MSIX virtualization. It preserves unrelated provider settings and protects previous Claude status-line restoration data with Windows DPAPI. Startup is disabled by default and honors Windows Task Manager opt-outs. Notifications use the manifest-declared COM activator.

Before uninstalling, disconnect providers from Settings → Privacy → Disconnect providers…, restart existing CLI sessions, and exit Peak. Copied bridge files and restoration data remain usable outside the package until safely cleared, so uninstalling does not leave references to a deleted executable in provider configuration. The app and user guide disclose this behavior.

## Final checks before submitting

Run Windows App Certification Kit on a clean Windows development/test machine with administrator access, then verify a fresh install, update, uninstall and interactive provider sign-in. The local automated checks and isolated packaged fixture do not replace certification or testing on other Windows machines. See [the candidate verification record](Store-verification.md) for completed checks and remaining limitations. Complete all required dashboard sections and review the final listing before Submit for certification.

Reference: [Microsoft Store policies](https://learn.microsoft.com/en-us/windows/apps/publish/store-policies), including external-dependency disclosure and testability requirements.

## Rebuild

```powershell
./scripts/publish.ps1 -PublicRelease -StoreBuild -OutputDirectory dist/Peak-Store
./scripts/package-store.ps1 -Published dist/Peak-Store
```

Generate logo size variants with `./scripts/create-store-assets.ps1` after changing the existing app icon. Preserve published or uploaded artifacts; increment the application version before a new submission. Website installer and portable packaging deliberately reject Store builds.
