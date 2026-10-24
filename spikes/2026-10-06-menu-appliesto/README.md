# Spike: can right-click entries be limited to the GistDrive folders, and Make public to `Secret`?

**Date**: 2026-10-06
**Timebox**: 20 minutes
**Time spent**: about 20 minutes
**Sandbox**: `spikes/2026-10-06-menu-appliesto/`

## Question

Can registry menu entries (no shell extension DLL) be limited by an `AppliesTo` condition so that they appear on gist shortcuts and gist folders under the GistDrive root folder only, and Make public on items in `Secret` only?

## Findings

- The first try put entries under `HKCU\Software\Classes\InternetShortcut\shell`, and none appeared, not even one without a condition. On the test machine, `.url` files belong to the per-user default ProgID `IE.AssocFile.URL` (the `UserChoice` key), so Windows never reads `InternetShortcut`.
- `url-verb-locations.ps1` put one unconditional entry in each of six places. A `.url` file got only two of them: the active ProgID (`IE.AssocFile.URL`) and `AllFilesystemObjects`. Entries under `*`, `SystemFileAssociations\.url`, `InternetShortcut` and `.url\shell` did not appear. A `.txt` file got both `*` and `AllFilesystemObjects`, so `*` just skips shortcut files.
- `appliesto-test.ps1` registers conditional entries under `AllFilesystemObjects\shell` (files and folders) and `Directory\shell` (folders), then asks the shell which entries each test item gets (`FolderItem.Verbs()`, the same context menu Explorer builds). Items outside the root got only the unconditional entries. The rest:

| Entry | `AppliesTo` | Shown on |
|---|---|---|
| GDS1 | `System.ItemFolderPathDisplay:="<root>\Secret"` | the shortcut in Secret, the gist folder in Secret |
| GDS2 | `System.ItemFolderPathDisplay:~<"<root>"` | everything under the root |
| GDS3 | `System.ItemPathDisplay:~<"<root>\Secret\"` | everything under Secret |
| GDS4 | `System.ParsingPath:~<"<root>\"` | everything under the root |
| GDS5 | GDS1 `AND System.FileExtension:=".url"` | only the shortcut directly in Secret |
| GDS6 | `System.ItemPathDisplay:~<"<root>\" AND System.FileExtension:=".url"` | every shortcut under the root, including one inside a gist folder, and no folders |
| GDSD1 (Directory) | `System.ItemFolderPathDisplay:="<root>\Secret"` | the gist folder in Secret |
| GDSD2 (Directory) | `System.ItemPathDisplay:~<"<root>\"` | the gist folder |

## Verdict

**Go.** Register file entries under `AllFilesystemObjects\shell` with conditions like GDS5 and GDS6, and folder entries under `Directory\shell` like GDSD1. Never register under `InternetShortcut`, `*` or `SystemFileAssociations\.url`.

## Caveats and open questions

- The conditions hold absolute paths, so moving the root folder means rewriting them.
- A prefix condition on the root also matches `Public`, `Secret` and `_failed` themselves, so gist folder entries need a tighter condition. `or-not-test.ps1` (run later the same day) shows that `OR`, `NOT` and parentheses all work: `(System.ItemFolderPathDisplay:="<root>\Secret" OR System.ItemFolderPathDisplay:="<root>\Public") AND NOT System.ItemNameDisplay:="_failed"` matches the gist folders in both top folders, and not `_failed`.
- `FolderItem.Verbs()` builds Explorer's menu without drawing it. Nobody has looked at the real menu, because the PC was locked.
- Windows 11 shows these entries under **Show more options** only.
- `AllFilesystemObjects` conditions are evaluated on every right-click anywhere. No slowdown was noticeable, but nothing was measured.
