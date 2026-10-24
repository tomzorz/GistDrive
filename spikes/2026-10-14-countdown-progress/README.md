# Spike: why does the countdown bar not move?

**Date**: 2026-10-14
**Timebox**: 20 minutes
**Time spent**: about 15 minutes
**Sandbox**: `spikes/2026-10-14-countdown-progress/`

## Question

The first real countdown notification showed a progress bar that never moved, although every `ToastNotifier.Update` returned `Succeeded`. Does Windows apply the updates, and if not, why not?

## Findings

- `CountdownProbe/` shows a 10 s countdown through GistDrive's own `WindowsNotifier` and updates it every second, as the engine's tick does. After each update it reads the notification back from `ToastNotificationManager.History` and prints the data Windows holds.
- With sequence numbers counting up from 1, every update returned `Succeeded`, and the stored data never changed: the value stayed 1 and the text "10 s". The stored sequence number read 401941, not the 1 GistDrive sent, so Windows had put its own counter in. Each later update numbered 2, 3, 4 and so on looked out of date, and Windows dropped it without saying so.
- With sequence number 0, which the docs define as "apply regardless of order", the stored data followed every update (value 1, 0.90, 0.80, down to 0.09, text "10 s" to "1 s"). The read-back lags one step, because Windows applies updates asynchronously.
- `sample-popup.ps1` and `window-diff.ps1` tried to read the popup itself through UI Automation. No popup window appeared: Windows was not showing banners, as Focus assist does during quiet hours, so the notifications went straight to Action Center.

## Verdict

**Go.** `WindowsNotifier` sends sequence number 0. The notifier contract suite now reads back the shown countdown instead of trusting the return value of `Update`, which was `Succeeded` the whole time the bug was there.

## Caveats and open questions

- A screenshot of the popup on 2026-10-24 showed the bar at the right fill. Windows draws the fill in the accent color, though, so with a gray accent the bar looks still (`docs/SPEC.md`, "Undo window").
