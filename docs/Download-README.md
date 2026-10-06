# Peak for Windows

Peak is free for personal and workplace use on unlimited devices. No Peak account, payment, activation or expiry. Provider accounts and subscriptions remain separate.

Publisher: neorangel

Support: peakforwindows.support@gmail.com

Website: https://peakforwindows.vercel.app

Official downloads: https://github.com/shirokuuun/Peak-releases/releases

## Install or run

Windows 10 version 2004 or later, or Windows 11, x64. The complete package includes .NET; a separate runtime installation is not required.

- Installer: run the official Setup.exe. Installs for your Windows user under LocalAppData/Programs/Peak and adds a Start menu shortcut. Administrator access is not needed.
- Portable: extract the entire ZIP to a folder you own, then run Peak/Peak.exe. Keep all files together, including bridge, Policies and licenses. Peak.exe alone is incomplete. Preferences still use LocalAppData/UsageNotch to preserve upgrades.

Version 1.0.0 is unsigned. Windows may show a publisher or reputation warning. Verify the download source and the published SHA-256 checksum, and follow your organization's software policy. A matching checksum establishes file consistency, not a trusted publisher signature. Do not disable Windows protections to install Peak.

## Connect your providers

Providers start disabled. Read Privacy in Settings before enabling access. Use Providers to enable Codex usage, Codex tasks or Claude usage. A supported installed CLI and your own eligible provider account are required.

Codex usage reads the installed CLI's local app-server interface. Task monitoring uses local lifecycle logs and optional observer hooks. If you connect hooks, review and trust them in Codex /hooks and reconnect existing chats. Peak observes requests; approvals stay in your provider tool.

Claude usage uses the status-line bridge. Connecting it preserves the previous command's output and a protected restoration backup. Restart existing sessions after connecting or disconnecting. Claude task and approval monitoring are not included. Missing readings are unknown, not zero; older CLIs may not report supported quota fields.

Right-click Peak's tray icon for Settings or Exit. The default reveal shortcut is Ctrl+Alt+U. Choose another in Settings if it conflicts. For sample data, run Peak.exe --demo; normal startup always uses the providers you enable.

## Update, privacy and uninstall

Updates are manual. Exit Peak and existing provider CLI sessions first. Install the newer complete installer in the same location, or replace the entire portable folder. Never copy only Peak.exe over an older package. Keep your preferences and restoration backups.

Settings > Privacy can disconnect providers or clear observations. Original provider logs and unrelated settings are preserved. Owned task events and Claude snapshots are bounded to seven days and 10 MiB per observation folder. No Peak telemetry or automatic diagnostic uploads.

Uninstall the installed version through Windows Settings. The uninstaller safely disconnects integrations and offers complete local-data deletion. Preferences remain unless you choose deletion. For portable removal, first disconnect providers in Peak and turn off launch at sign-in, exit Peak, then remove its extracted folder. If you also want to remove local data, close provider sessions and run Peak.exe --delete-local-data before deleting the package. Cleanup stops on an unsafe restoration and preserves backups.

An old paid-license file is ignored and never used for access or contacted by this release. Only explicit complete local-data deletion removes it.

Read the complete Terms, Privacy and Third-party-notices documents in Policies. Full dependency license texts are in licenses. Complete unchanged packages can be shared without charging, with all notices intact. Peak's source code is not licensed as open source by these freeware terms.
