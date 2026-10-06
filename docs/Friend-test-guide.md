# Peak — Windows friend test

This is a portable test build with the custom Notch icon and the continuous expanded design. Your readings, preferences, and Claude connection belong to your own Windows account. The app does not need the sender's computer to be running.

## Start the app

1. Extract the entire ZIP into a permanent folder, such as `Documents\Peak`. Keep all the files and the `bridge` folder together.
2. Open `Peak.exe` inside the extracted `Peak` folder. The notch floats slightly below the top of the screen and has an icon in the system tray. Its default gap is 12 pixels at standard Windows scaling; adjust **Gap from top** in Settings to change it.
3. Hover over the notch to open details. Click its header to pin the details; click again or press Escape to collapse. `Ctrl+Alt+U` or the tray's **Show details** can bring it back.

The idle notch is a slim pill without an extra activity row. Recent log updates alone do not stretch it; confirmed work or attention requests can expand it automatically. Hover and click still open the details.

The compact percentages and rings show **5-hour usage remaining** for each provider. Hover to see both 5-hour and weekly limits. When the 5-hour reading is unavailable, its compact percentage is a dash.

The build contains its .NET runtime, so a separate .NET installation is unnecessary. This package targets Windows x64; use Windows 11 or Windows 10 version 2004 or later. It is a test ZIP, without a conventional installer or automatic updates. Allow about 300 MB for the extracted folder. See [Microsoft's self-contained publishing documentation](https://learn.microsoft.com/en-us/dotnet/core/deploying/#self-contained-deployment).

## Connect your Claude Code account

1. Use the native Windows installation of Claude Code and sign in to your own Claude account. This test does not automatically connect a Claude installation inside WSL.
2. For the personal 5-hour and weekly usage readings, use an **up-to-date Claude Code CLI** with a **Claude Pro or Max subscription**. These fields appear after Claude has returned its first response. See [Claude's rate-limit status-line documentation](https://code.claude.com/docs/en/statusline#rate-limit-usage).
3. Open the notch's **Settings** using the gear button or the tray menu.
4. In Settings → **Providers**, turn off **Codex usage** and **Codex task activity** if you only use Claude.
5. Enable **Claude usage**. Peak installs the bridge and opens connection setup. Install or open Claude Code there if needed, then click **Save changes**.
6. Restart Claude Code, send a prompt, and wait for a response. Open the notch and click refresh if needed.

Connect Claude installs a local status-line helper in `%LOCALAPPDATA%\UsageNotch\bridge` and updates the current user's Claude `statusLine` setting. It saves the previous status-line setting and chains its command so its output can continue to appear. The connection button applies immediately; Cancel does not undo it. Use **Disconnect** to restore the saved setting, provided it has not since been replaced by another tool.

An API-key-only account does not supply these personal subscription windows. If a reading is missing, the notch shows **Unavailable** rather than inventing a percentage. A configured Claude apps gateway's spend-limit field is not supported in this build.

## Features available in this build

| Feature | Claude Code | Codex |
| --- | --- | --- |
| Custom icon, continuous expanded panel, hover, pin, settings, tray | Yes | Yes |
| Subscription usage remaining, reset countdowns, optional usage/reset alerts | Yes, when the CLI supplies the fields | Yes, with the user's own Codex CLI login |
| Task titles, activity, elapsed time, approval details, automatic task expansion | Not implemented for Claude yet | Yes, with task monitoring connected |

The automatic task view currently connects to Codex events. Connecting Claude supplies usage data; it does not give Claude the same task-progress features yet. The moving task indicator represents observed activity, not a completion percentage.

Settings now has a sidebar and grouped cards inspired by the notch's quiet design. **Appearance** includes any RGB color with a live preview and automatic text contrast. **Presence** lets you choose Always, When active, or On hover; reported review requests have their own timing and full-screen choices. **Out of the way** offers pointer fading, adjustable opacity, and fade speed. Hold Alt over a faded notch to interact; pinned details stay readable. **Shortcuts** offers three reveal shortcuts, with Ctrl+Alt+U as the default. Optional screen-sharing exclusion works with supported Windows capture tools; check your recording preview because apps can capture differently.

Notifications start disabled. Enable usage threshold or reset notifications in Settings → **Alerts** if you want them, then **Save changes**. Thresholds are expressed as percent **used**, while the notch displays percent **remaining**. To start the notch when you sign in, keep the extracted folder in its permanent location before enabling **Launch at sign-in** under **System**.

## If something is missing

- A dash beside **Co** is expected when Codex is disabled. A dash beside **Cl** means no usable current Claude usage reading is available.
- Check the Claude version and account type, confirm **Connect Claude** succeeded, and get a new Claude response after connecting. A project-level Claude status-line override can take precedence over the user setting.
- Readings can become stale when Claude is idle. Refreshing the notch does not force Claude to produce fresh quota data. A countdown reaching zero needs a new provider reading to confirm the reset.
- If clicks pass through the notch, use your reveal shortcut or the tray's **Show details**, then adjust **Always let clicks through** or **Step aside under the pointer** under **Out of the way**.
- If full-screen software hides the notch, choose **Stay visible** under Settings → **Presence → In full-screen apps**, or use the reveal shortcut.

For feedback, note the Windows version, Claude Code version, whether connection succeeded, and which reading or interaction failed. A screenshot of the notch is useful; account credentials and conversation transcripts are unnecessary.

## Update or remove the test

Exit the notch from its tray menu before replacing its folder with a newer build. After updating, click **Connect Claude** again to refresh the stable helper copy, then restart Claude Code.

To remove it, first disable **Launch when I sign in**, click **Disconnect** for Claude, Save, and exit from the tray. Then delete the extracted app folder. Local preferences and cached readings remain under `%LOCALAPPDATA%\UsageNotch` until you remove that folder yourself.

The ZIP includes only published application files, this guide, third-party notices, and file checksums. It contains no sender login, personal settings, usage history, or conversations. The app has been checked on the development machine; testing on a separate Windows account and PC is the purpose of this package.
