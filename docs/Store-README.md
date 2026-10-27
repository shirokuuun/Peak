# Peak — Microsoft Store edition

Choose **Settings → Appearance → Notch style** for a floating **Dynamic Island** or a **Fixed Notch** joined to the screen's top edge. Both open smoothly with no outer outline. Peak includes Inter typography and provider images; no font installation is needed.

Publisher: neorangel · Support: peakforwindows.support@gmail.com

Peak is free for personal and workplace use. It has no Peak account, payment, activation, device cap or expiry. Provider accounts and subscriptions are separate. Windows 10 version 2004 or later, or Windows 11, x64 is required. The .NET runtime is included.

## Connect providers

Open Settings → Providers and enable the providers you use. Connection setup detects the Windows CLIs and reuses a compatible existing login. If needed, use Install & connect and complete the provider's own sign-in screen. Save changes to keep the preference.

Codex usage requires a compatible Codex Windows CLI and a ChatGPT login. API-key billing is separate. Codex task activity can observe local lifecycle logs; hook observations require Connect task activity, review and trust in Codex /hooks, and reconnecting existing chats. Peak never grants hook trust or approves tools.

Claude usage installs its bridge automatically. Sign in through the Windows Claude Code CLI with Claude Pro or Max, send a prompt and wait for a response. Restart existing Claude Code sessions to load the bridge. The Claude website or desktop app alone does not provide these quota readings.

## Local data and startup

The Store edition and its external CLI bridges use `%USERPROFILE%\.peak\store`. This per-user folder stays accessible to the CLIs without disabling Windows package virtualization. The website installer uses a different data directory. Store installation starts with new preferences; it does not import credentials or silently enable providers from an older installation.

Use one edition of Peak at a time. Exit the website/portable edition before opening the Store edition. Disconnect its provider integrations before switching editions so the same provider status line or hooks are not managed twice.

Launch at sign-in is optional and disabled initially. Windows Settings → Apps → Startup and Task Manager can disable it; Peak respects that choice. Store updates are managed by Microsoft Store. Reconnect/restart provider sessions after updating if they have already loaded the old bridge.

## Disconnect before uninstalling

Before removing the Store edition, use Settings → Privacy → Disconnect providers…, then restart existing provider sessions and exit Peak. Microsoft Store removal does not run Peak's website-installer cleanup. The copied bridge files and protected restoration information stay outside the package so provider commands do not break when it is removed.

Use Peak's privacy controls to clear retained observations and data while Peak is installed. Do not delete its restoration data before disconnecting. Peak preserves unrelated provider settings and never deletes original provider logs.

## Store submission package

An unsigned `.msix` produced for Partner Center is an upload artifact, not a public download. Microsoft signs it after certification. It cannot be installed by double-clicking as a trusted public package before that step.

Privacy: https://peakforwindows.vercel.app/privacy/
Support: https://peakforwindows.vercel.app/support/
Freeware terms: https://peakforwindows.vercel.app/terms/
Third-party notices are included under Policies and licenses.
