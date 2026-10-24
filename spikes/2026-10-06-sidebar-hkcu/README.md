# Spike: do the HKCU registry steps alone give GistDrive a File Explorer sidebar entry?

**Date**: 2026-10-06
**Timebox**: 20 minutes
**Time spent**: about 15 minutes
**Sandbox**: `spikes/2026-10-06-sidebar-hkcu/`

## Question

Microsoft's "Integrate a Cloud Storage Provider" page pairs twelve HKCU registry steps for the navigation pane with an HKLM `SyncRootManager` registration. Do the HKCU steps alone give a pinned sidebar entry that opens a real folder, on Windows 10 build 19045?

## Findings

- `sidebar-test.ps1` writes the twelve values with `reg add`, all under HKCU, with a fixed test CLSID and `TargetFolderPath` pointing at `root\`. It then notifies the shell, runs the checks, and removes everything in a `finally` block.
- `shell:::{CLSID}` resolves to the target folder with the title "GistDrive Spike" and lists `Public` and `Secret`.
- The navigation pane shows the desktop folder enumerated with `SHCONTF_NAVIGATION_ENUM`, keeping the items whose `System.IsPinnedToNameSpaceTree` is true. That enumeration lists the entry as pinned, right after This PC (`SortOrderIndex` 0x42).
- No admin rights were needed, and nothing was written outside HKCU.
- The look-at-it check did not work: the PC was locked, and while it is locked, UI Automation finds Explorer's navigation pane tree but no items in it, even after 10 seconds.

## Verdict

**Go.** The HKCU steps alone register the sidebar entry, so GistDrive skips the HKLM sync root registration.

## Caveats and open questions

- The entry was checked by eye on 2026-10-14, after a real install. To look at the spike's own entry: run `powershell -NoProfile -File sidebar-test.ps1 -Keep`, open a new Explorer window, then run it again with `-RemoveOnly`.
- Windows 11 is untested.
