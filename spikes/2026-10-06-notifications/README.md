# Spike: can GistDrive show a notification with buttons and a live countdown without the Windows App SDK?

**Date**: 2026-10-06
**Timebox**: 20 minutes
**Time spent**: about 15 minutes
**Sandbox**: `spikes/2026-10-06-notifications/`

## Question

Can an unpackaged, always-running .NET process show a notification with **Cancel** and **Publish now** buttons, update its countdown, remove it, and receive the button click, with only an HKCU registration and no Windows App SDK?

## Findings

- PowerShell 5.1 is a dead end for this: it cannot index into the WinRT map behind `NotificationData.Values`, and it refuses WinRT events outright ("Windows PowerShell cannot subscribe to Windows RT events"). `toast-test.ps1` stays only as the record of that.
- `ToastProbe/` is a .NET 10 console app targeting `net10.0-windows10.0.19041.0`, with no package references. The .NET 10.0.401 SDK pins the Windows SDK projection to `10.0.19041.57`, which is already in the local NuGet cache, so it restores offline.
- The probe registers `HKCU\Software\Classes\AppUserModelId\GistDrive.Spike` with only `DisplayName`. It then shows a notification with both buttons and a data-bound progress bar, updates the bar three times, waits for a click, removes the notification, and removes the registration. Results:
  - Show: delivered. `ToastNotificationManager.History.GetHistory` returns it.
  - Updates: `Succeeded` three times. `Succeeded` does not mean Windows applied them: `spikes/2026-10-14-countdown-progress/` found that every update after the first was dropped as stale.
  - `History.Remove`: the notification is gone.
  - `ToastNotifier.Setting` throws for this app ID, so GistDrive must not rely on it.
  - Click (2026-10-14): **Publish now** printed `Activated with arguments: action=now&id=42`. Both buttons are `activationType="foreground"`, the same as the **Cancel** button in GistDrive.
- The Windows App SDK would have been the fallback route, at the cost of a package dependency.

## Verdict

**Go**, on every point. An unpackaged process shows, updates and removes a notification, and a button click reaches the running process through the in-process `Activated` event. No COM activator and no Windows App SDK.

## Caveats and open questions

- To repeat the click test:

  ```
  dotnet restore ToastProbe --source %USERPROFILE%\.nuget\packages
  dotnet run --project ToastProbe --no-restore -- 60
  ```

- The probe is a console app and GistDrive is a `WinExe`. GistDrive's notifications show up fine as a `WinExe` (its countdown showed on 2026-10-14). A click on **Cancel** in GistDrive itself is not confirmed yet.
