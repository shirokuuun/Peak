# Settings redesign verification

Checked October 3, 2026 on the development Windows machine.

The settings layout follows the supplied references: neutral dark background, sidebar sections, rounded cards, white selected segments, warm switches, and quiet sliders and scrollbars. A live mini-notch keeps color previews separate from the settings controls.

The Release build completed without warnings or errors, and all 87 automated tests passed. New cases cover presence modes, attention timing and expiration, full-screen priorities, pointer opacity and click-through, manual recovery, preference migration, and serialization. Existing palette tests verify readable text and status colors.

Native UI checks used isolated demo preferences, with no provider or startup setup changes:

- All seven sidebar sections render and navigate correctly.
- Review timing appears when For a while is selected and remains selected after reopening.
- Fading enables opacity and speed controls; the slider changes to 25% and Soft can be selected.
- White color previews show dark text, while settings and the color picker remain neutral dark.
- Saving preserves color, timing, opacity, speed, and the selected Ctrl+Alt+P shortcut.
- Hovering over the saved notch fades it; Ctrl+Alt+P restores full opacity and pins details.
- Invalid equal warning/critical thresholds route from Shortcuts to Alerts with an inline error.
- Resizing to the minimum size keeps navigation and footer actions visible; the scroll thumb reaches the lower cards.
- Canceling a Dark theme preview restores the saved white appearance; the preferences file hash stays identical.

Demo preferences were restored after verification. The published app and portable test ZIP were updated. Screen-capture exclusion is implemented through Windows display affinity and defaults off; capture-tool compatibility was not exercised in these UI checks. Full-screen timing and Alt-hover priority were checked in the policy tests; additional native capture, multi-monitor, and full-screen application checks remain useful on other machines.
