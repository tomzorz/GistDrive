# Spike: does a file watcher see a gist shortcut go to the Recycle Bin and come back?

**Date**: 2026-10-06
**Timebox**: 20 minutes
**Time spent**: about 5 minutes
**Sandbox**: `spikes/2026-10-06-recycle-bin-events/`

## Question

When the owner deletes a gist shortcut in Explorer and restores it from the Recycle Bin, what does a `FileSystemWatcher` on the folder report?

## Findings

`recycle-test.ps1` creates `spike-notes.md.url`, watches its folder, deletes the file to the Recycle Bin the way Explorer does (`SHFileOperation` with undo, through `Microsoft.VisualBasic.FileIO.FileSystem.DeleteFile`), finds it in the bin, and restores it with the bin's own `undelete` verb. It ran on a folder on `I:` and on a temp folder on `C:`, with the same result:

```
Deleted spike-notes.md.url
Created spike-notes.md.url
```

- There were no Renamed or Changed events in between.
- In the bin, the item carries `System.Recycle.DeletedFrom` with the original folder and offers **Restore**.
- The restore put the file back under its original name.

## Verdict

**Go.** A delete arrives as Deleted and a restore as Created, under the original name, so GistDrive can drop a pending deletion when a Created event with the deleted name arrives during the undo window.

## Caveats and open questions

- The watcher alone cannot tell a delete to the Recycle Bin from a permanent delete or a move out of the folder: all of them arrive as Deleted. If GistDrive ever needs to know which, it can look the item up in the bin (`Shell.Application` namespace 10, `System.Recycle.DeletedFrom`).
- With the deletion setting off, GistDrive recreates the shortcut, so a later restore from the bin finds a file with the same name, and Windows asks whether to replace it. `docs/SPEC.md` settles that case in the section "Deleting".
