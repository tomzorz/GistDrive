# GistDrive design

The key words MUST, MUST NOT, SHOULD, SHOULD NOT, and MAY in this document are to be interpreted as described in BCP 14 (RFC 2119, RFC 8174) when, and only when, they appear in all capitals, as shown here.

This document is normative. Each decision carries its date and sits in the section that it affects. The section "Open items" lists each question that the design interview of 2026-10-06 left open, together with the assumption that holds until a decision.

## Purpose

GistDrive gives the GitHub gists of its owner a place in File Explorer. A text file that the owner saves into a top folder becomes a gist. GistDrive then replaces the text file with a gist shortcut. Each gist of the owner appears in File Explorer. The gists that existed before GistDrive appear too.

## Glossary

### GistDrive (noun)
The background program that this specification defines.
Do not use: the tool, the app, the watcher, the service.

### owner (noun)
The person who runs GistDrive. The GitHub CLI on the computer of the owner is signed in to the GitHub account of the owner.
Do not use: user.

### GitHub CLI (noun)
The `gh` command-line program from GitHub.
Do not use: gh CLI.

### gist (noun)
A GitHub gist in the GitHub account of the owner.

### gist file (noun)
One file in a gist.

### single-file gist (noun)
A gist with exactly 1 gist file.

### multi-file gist (noun)
A gist with at least 2 gist files.

### public gist (noun)
A gist that GitHub lists publicly.

### secret gist (noun)
A gist that GitHub does not list. Each person with the URL of a secret gist can read the secret gist.
Do not use: private gist.

### root folder (noun)
The folder that holds the public folder and the secret folder.

### public folder (noun)
The folder `Public` in the root folder. The public folder holds the public gists.

### secret folder (noun)
The folder `Secret` in the root folder. The secret folder holds the secret gists.
Do not use: private folder.

### top folder (noun)
The public folder and the secret folder are the 2 top folders. A statement about a top folder applies to each of the 2 top folders separately.

### sidebar entry (noun)
The entry in the navigation pane of File Explorer that opens the root folder.
Do not use: namespace extension, navigation pane item.

### gist shortcut (noun)
A `.url` file that GistDrive writes for exactly 1 gist file.
Do not use: shortcut, link, `.lnk` file.

### shortcut name (noun)
The file name of a gist shortcut without the `.url` extension. File Explorer shows the shortcut name. Example: the gist shortcut `notes.md.url` has the shortcut name `notes.md`.

### gist folder (noun)
A folder in a top folder that stands for exactly 1 multi-file gist.

### mirror (noun)
The set of all gist shortcuts and all gist folders in the top folders.

### refresh (noun)
One run in which GistDrive reads the list of gists from GitHub and updates the mirror.
Do not use: sync, poll.

### text file (noun)
A file that passes the text check. OPEN-7 tracks the definition of the text check.

### upload (verb)
To send the content of a text file to GitHub. Each upload does exactly one of the following: create a gist, add a gist file to a gist, or update a gist file.

### undo window (noun)
The time for which GistDrive holds back each upload to a public gist and each gist deletion. The owner can cancel the held-back action during the undo window.

### secret upload delay (noun)
The time for which GistDrive holds back each upload to a secret gist.

### pending action (noun)
An action that GistDrive holds back. Each pending action waits for exactly one of the following: the undo window, or the secret upload delay.

### notification (noun)
A Windows app notification that GistDrive shows.
Do not use: toast, popup, alert.

### deletion setting (noun)
The setting that decides what the deletion of a gist shortcut does on GitHub. The deletion setting has exactly 2 values: `off` and `on`.

### menu entry (noun)
An item that GistDrive adds to the right-click menu of File Explorer.
Do not use: verb, context menu item.

### Send To entry (noun)
An item that GistDrive adds to the **Send to** menu of File Explorer.

### source path (noun)
The full path of the original file that the owner sent through a Send To entry.

### short gist ID (noun)
The first 7 characters of the ID of a gist.

### failed folder (noun)
The folder `_failed` in a top folder.

### reason file (noun)
A text file next to a file in a failed folder. The reason file states why the upload failed. The name of the reason file is the name of the failed file plus `.why.txt`.

### transient failure (noun)
An upload failure that can end without action by the owner. OPEN-16 tracks the exact list.

### permanent failure (noun)
An upload failure that only the owner can fix. The permanent failures are the following:
- The GitHub CLI is missing.
- The GitHub CLI is not signed in.
- The file is not a text file.
- The file is larger than the size limit (OPEN-7).

### retry period (noun)
The time for which GistDrive retries an upload after a transient failure.

### config file (noun)
The file `config.json` next to the executable of GistDrive.

### icon folder (noun)
The folder `icons` next to the executable of GistDrive. The icon folder holds the icons and the notification image of GistDrive.

### cancel record (noun)
The file `cancelled.json` next to the executable of GistDrive. The cancel record lists each recorded item with its stamp at the moment of the cancel.

### recorded item (noun)
A file or a folder whose upload the owner cancelled, as the cancel record lists it.

### stamp (noun)
The write time of a file. The stamp of a folder is the latest write time of the files in the folder.

## Scope and prerequisites

Decision (2026-10-06): GistDrive starts as a personal tool. The owner publishes GistDrive if GistDrive works well.

Decision (2026-10-06): the GitHub CLI is a prerequisite, so that GistDrive needs no sign-in of its own.

Convention: GistDrive is a .NET 10 single-file executable. GistDrive keeps its config file, its log file, and its cancel record next to its executable.

- REQ-ENV-1: The owner MUST install the GitHub CLI before the owner starts GistDrive.
- REQ-ENV-2: The owner MUST sign in to the GitHub CLI before the owner starts GistDrive.
- REQ-ENV-3: GistDrive MUST use the sign-in of the GitHub CLI for each call to GitHub.

## File Explorer integration

Decision (2026-10-06): the top folders are real folders under one sidebar entry. GistDrive uses no Cloud Files API, no placeholder files, and no shell extension DLL. Reason: apps save to file paths, and the sidebar entry needs only registry values under `HKCU`.

Decision (2026-10-06): the secret folder is named `Secret`, not `Private`. Reason: GitHub has no private gists. Each person with the URL of a secret gist can read the secret gist.

Spike (2026-10-06): the `HKCU` values alone register the sidebar entry. The shell resolves the sidebar entry to the root folder and lists the sidebar entry as pinned in the navigation pane. The owner checked the sidebar entry in File Explorer on 2026-10-14. Details: `spikes/2026-10-06-sidebar-hkcu/`.

Decision (2026-10-06): menu entries for gist shortcuts live under `AllFilesystemObjects`, and menu entries for gist folders live under `Directory`. Reason: the ProgID of `.url` files differs per user, and Windows ignores entries under `*` and `SystemFileAssociations\.url` for `.url` files. An `AppliesTo` condition on the path limits each menu entry. Details: `spikes/2026-10-06-menu-appliesto/`.

Decision (2026-10-14): the root folder, the public folder, and the secret folder get icons. The owner asked for the icons on 2026-10-14. Each icon shows a folder with a badge: code brackets for the root folder, a lock for the secret folder, and a globe for the public folder. The program `sources/IconMaker` draws the icons. The sidebar entry and the notifications use the icon of the root folder.

Decision (2026-10-14): GistDrive sets the icon of a folder through the `IconResource` value in the file `desktop.ini` of the folder. File Explorer reads `desktop.ini` only in a folder with the read-only attribute. On a folder, the read-only attribute marks the folder as customized and does not protect the files in the folder. GistDrive recognizes an icon of its own by the file name and by the folder name `icons`, so a moved GistDrive replaces the icons of its old location.

- REQ-SHELL-1: GistDrive MUST create the public folder and the secret folder as real folders in the root folder.
- REQ-SHELL-2: GistDrive MUST register exactly 1 sidebar entry.
- REQ-SHELL-3: GistDrive MUST point the sidebar entry at the root folder.
- REQ-SHELL-4: GistDrive MUST register the sidebar entry with registry values under `HKCU` only.
- REQ-SHELL-5: GistDrive MUST NOT use the Cloud Files API.
- REQ-SHELL-6: GistDrive MUST NOT install a shell extension DLL.
- REQ-SHELL-7: GistDrive MUST use `%USERPROFILE%\GistDrive` as the default root folder.
- REQ-SHELL-8: The owner MAY set a different root folder in the config file.
- REQ-SHELL-9: GistDrive MUST register each menu entry for gist shortcuts under `HKCU\Software\Classes\AllFilesystemObjects\shell`.
- REQ-SHELL-10: GistDrive MUST register each menu entry for gist folders under `HKCU\Software\Classes\Directory\shell`.
- REQ-SHELL-11: GistDrive MUST limit each menu entry with an `AppliesTo` condition on the path, so that the menu entry appears only on the items that its requirement names.
- REQ-SHELL-12: If the owner changes the root folder, GistDrive MUST update the sidebar entry and each `AppliesTo` condition.
- REQ-SHELL-13: GistDrive MUST give each of the following folders the named icon from the icon folder:
  - the root folder: `gistdrive.ico`
  - the public folder: `public.ico`
  - the secret folder: `secret.ico`
- REQ-SHELL-14: GistDrive MUST use `gistdrive.ico` from the icon folder as the icon of the sidebar entry.
- REQ-SHELL-15: GistDrive MUST register `gistdrive.png` from the icon folder as the image that each notification shows.
- REQ-SHELL-16: If the owner picks a different icon for a folder of REQ-SHELL-13, GistDrive MUST keep the icon that the owner picked.
- REQ-SHELL-17: In each `desktop.ini` file, GistDrive MUST change only the `IconResource` value.
- REQ-SHELL-18: If the icon file for one of the folders of REQ-SHELL-13 is missing, GistDrive MUST leave the icon of that folder unchanged.
- REQ-SHELL-19: If `gistdrive.ico` is missing from the icon folder, GistDrive MUST use the standard folder icon of Windows for the sidebar entry.

## Gist shortcuts and layout

Decision (2026-10-06): a gist shortcut is a `.url` file, the Windows file format for a link to a web address. File Explorer hides the `.url` extension, so `notes.md.url` shows as `notes.md`.

Decision (2026-10-06): a single-file gist appears as a gist shortcut. A multi-file gist appears as a gist folder with 1 gist shortcut per gist file. The name of a gist folder is the description of its gist.

Decision (2026-10-06): the gist shortcut holds the state of its gist. The state is the gist URL, plus the source path for a gist from a Send To entry.

Spike (2026-10-06): an extra INI section in a `.url` file survives the `.url` object of the shell, `WScript.Shell`, a shell copy, and a shell rename. Details: `spikes/2026-10-06-url-custom-data/`.

Decision (2026-10-14): each gist shortcut and each gist folder carries the times of its gist. The owner asked for the times on 2026-10-14. GitHub reports the creation time of a gist as `created_at` and the time of the last change as `updated_at`. GitHub keeps both times per gist, not per gist file. Each gist shortcut in a gist folder therefore shows the times of the whole gist. Reason: the date columns of File Explorer then show the history of each gist, not the time of the last refresh.

- REQ-LAY-1: For each single-file gist, GistDrive MUST keep exactly 1 gist shortcut in the top folder that matches the visibility of the gist.
- REQ-LAY-2: For each multi-file gist, GistDrive MUST keep exactly 1 gist folder in the top folder that matches the visibility of the gist.
- REQ-LAY-3: GistDrive MUST keep exactly 1 gist shortcut per gist file in each gist folder.
- REQ-LAY-4: GistDrive MUST give each gist shortcut the name of its gist file as the shortcut name. REQ-LAY-7 states the exception.
- REQ-LAY-5: GistDrive MUST name each gist folder after the description of its gist.
- REQ-LAY-6: If the description of a multi-file gist is empty, GistDrive MUST name the gist folder after the first gist file (OPEN-23).
- REQ-LAY-7: If at least 2 gist shortcuts in one folder have the same shortcut name, GistDrive MUST add the short gist ID in parentheses before the file extension (OPEN-24). Example: `gistfile1 (a1b2c3d).txt`.
- REQ-LAY-8: GistDrive MUST write the URL of the gist into each gist shortcut.
- REQ-LAY-9: GistDrive MUST store the persistent state of each gist only in its gist shortcut.
- REQ-LAY-10: If at least 2 gist folders in one top folder have the same name, GistDrive MUST add the short gist ID in parentheses to the end of the folder name (OPEN-24).
- REQ-LAY-11: GistDrive MUST keep its own data in a `[GistDrive]` section of the gist shortcut.
- REQ-LAY-12: GistDrive MUST set the creation time of each gist shortcut and each gist folder to the `created_at` time of its gist.
- REQ-LAY-13: GistDrive MUST set the modification time of each gist shortcut and each gist folder to the `updated_at` time of its gist.
- REQ-LAY-14: If GistDrive cannot set the time of REQ-LAY-12 or the time of REQ-LAY-13, or both, GistDrive MUST continue the refresh.
- REQ-LAY-15: GistDrive MUST write each failure of REQ-LAY-14 to the log file.

## Mirror and refresh

Decision (2026-10-06): the gists that existed before GistDrive appear in the mirror too.

Decision (2026-10-06): GistDrive runs a refresh at startup, every 5 minutes, and after each change that GistDrive makes on GitHub.

- REQ-MIR-1: GistDrive MUST run a refresh at startup.
- REQ-MIR-2: GistDrive MUST run a refresh every 5 minutes.
- REQ-MIR-3: GistDrive MUST run a refresh after each change that GistDrive makes to a gist.
- REQ-MIR-4: If a refresh finds a gist that the mirror does not show, GistDrive MUST add the gist to the mirror as REQ-LAY-1 and REQ-LAY-2 specify (OPEN-9).

## Uploading

Decision (2026-10-06): a text file that the owner saves into a top folder becomes a gist with the visibility of that top folder. After the upload, GistDrive deletes the text file, and the gist shortcut takes its place.

Decision (2026-10-06): the owner updates a gist file by saving a file with the shortcut name into the folder of the gist shortcut. Saving onto the gist shortcut itself is not the mechanism, because file dialogs follow a shortcut to its target.

Decision (2026-10-06): a folder that the owner adds to a top folder becomes a multi-file gist. The folder name becomes the gist description.

Decision (2026-10-06): the last save wins. Reason: the revision history of the gist keeps the other version.

- REQ-UP-1: GistDrive MUST create a gist from a text file if both of the following are true:
  - The text file appears in a top folder.
  - No gist shortcut in that top folder has the name of the text file as its shortcut name.
- REQ-UP-2: GistDrive MUST create a public gist from a text file in the public folder.
- REQ-UP-3: GistDrive MUST create a secret gist from a text file in the secret folder.
- REQ-UP-4: GistDrive MUST update a gist file from a text file if both of the following are true:
  - The text file appears in the folder of the gist shortcut for that gist file.
  - The name of the text file equals the shortcut name of that gist shortcut.
- REQ-UP-5: GistDrive MUST add a text file to the gist of a gist folder if both of the following are true:
  - The text file appears in the gist folder.
  - No gist shortcut in the gist folder has the name of the text file as its shortcut name.
- REQ-UP-6: If the owner adds a folder to a top folder, GistDrive MUST create a multi-file gist from the files in the added folder (OPEN-21).
- REQ-UP-7: GistDrive MUST use the name of the added folder as the description of the new gist.
- REQ-UP-8: After an upload succeeds, GistDrive MUST delete the uploaded text file.
- REQ-UP-9: After GistDrive creates a gist file, GistDrive MUST write the gist shortcut for that gist file.
- REQ-UP-10: GistDrive MUST NOT upload a file that is not a text file. REQ-FAIL-4 governs that file.
- REQ-UP-11: GistDrive MUST upload the local content even if the gist file changed on GitHub after the last refresh.

## Undo window

Decision (2026-10-06): GistDrive holds back each upload to a public gist and each gist deletion for the undo window, so that the owner can cancel the action. The owner compared the undo window to the undo-send feature of email clients.

Decision (2026-10-06): the undo window defaults to 30 seconds. An upload to a secret gist waits for the secret upload delay instead, which defaults to 0 seconds. The owner can change both values.

Spike (2026-10-06 and 2026-10-14): an unpackaged .NET 10 process shows a notification with buttons, updates the notification, and removes the notification. The process needs only a registration under `HKCU`, and no Windows App SDK. On 2026-10-14 the owner selected a button of the probe notification, and the in-process `Activated` event delivered the click to the running probe. The **Cancel** button of GistDrive has the same activation type, `foreground`. GistDrive therefore needs no COM activator. Details: `spikes/2026-10-06-notifications/`.

Spike (2026-10-14): each countdown update of GistDrive carries the sequence number 0. Windows replaces the sequence number of a shown notification with a large number of its own. Windows then drops each update with a lower sequence number, although the update call reports `Succeeded`. An update with the sequence number 0 always applies. Details: `spikes/2026-10-14-countdown-progress/`.

Finding (2026-10-24): Windows draws the filled part of the progress bar in the accent color of Windows. The toast schema has no attribute for the color of the bar. With a gray accent color such as `#4C4A48`, the filled part differs from the empty part (`#505050`) by about 4 levels of 255. The bar therefore looks still, although its value changes. A screenshot showed the correct fill at 26 of 30 seconds.

Decision (2026-10-24): GistDrive keeps the progress bar of Windows unchanged. The owner chooses a brighter accent color instead.

Decision (2026-10-24): a cancel survives a restart of GistDrive. GistDrive scans the top folders at each startup. Without a record of the cancel, the scan starts a new undo window for each cancelled file, and nobody might watch the computer during a startup. GistDrive therefore keeps the cancel record next to its executable.

- REQ-UNDO-1: Before GistDrive uploads to a public gist, GistDrive MUST wait for the undo window.
- REQ-UNDO-2: Before GistDrive deletes a gist, GistDrive MUST wait for the undo window.
- REQ-UNDO-3: Before GistDrive removes a gist file from a gist, GistDrive MUST wait for the undo window.
- REQ-UNDO-4: Before GistDrive uploads to a secret gist, GistDrive MUST wait for the secret upload delay.
- REQ-UNDO-5: GistDrive MUST use 30 seconds as the default undo window.
- REQ-UNDO-6: GistDrive MUST use 0 seconds as the default secret upload delay.
- REQ-UNDO-7: The owner MAY change the undo window in the config file.
- REQ-UNDO-8: The owner MAY change the secret upload delay in the config file.
- REQ-UNDO-9: If the owner saves a file again before its pending upload starts, GistDrive MUST upload only the latest content (OPEN-10). GistDrive MUST upload that content exactly once.
- REQ-UNDO-10: During each undo window, GistDrive MUST show a notification with a **Cancel** button.
- REQ-UNDO-11: If the owner selects **Cancel**, GistDrive MUST drop the pending action (OPEN-11).
- REQ-UNDO-12: If the owner restores a deleted gist shortcut from the Recycle Bin during the undo window, GistDrive MUST drop the pending deletion.
- REQ-UNDO-13: If the owner does not cancel a pending action before its undo window ends, GistDrive MUST execute the pending action.
- REQ-UNDO-14: GistDrive MUST show the remaining time of the undo window in the notification of REQ-UNDO-10.
- REQ-UNDO-15: GistDrive MUST update the remaining time every 1 second.
- REQ-UNDO-16: If the owner cancels the upload of a file or of a folder, GistDrive MUST add that file or folder to the cancel record.
- REQ-UNDO-17: GistDrive MUST NOT upload a recorded item while the stamp of the item equals the stamp in the cancel record.
- REQ-UNDO-18: If the owner renames a recorded item, GistDrive MUST keep the item recorded under the new name.
- REQ-UNDO-19: If a recorded item no longer exists, GistDrive MUST remove the item from the cancel record.
- REQ-UNDO-20: If GistDrive cannot read the cancel record, GistDrive MUST start with an empty cancel record.
- REQ-UNDO-21: GistDrive MUST write each failure to read the cancel record and each failure to write the cancel record to the log file.

## Deleting

Decision (2026-10-06): the deletion setting decides what the deletion of a gist shortcut does. With `off`, the gist stays on GitHub, the gist shortcut comes back, and a notification says why. With `on`, GistDrive deletes the gist file on GitHub after the undo window.

Decision (2026-10-06): the **Delete gist** menu entry deletes on purpose and ignores the deletion setting.

Decision (2026-10-06): in a gist folder, the deletion of a gist shortcut removes that gist file from the gist. The deletion of the gist folder deletes the gist. Both deletions follow the deletion setting and the undo window.

Spike (2026-10-06): a `FileSystemWatcher` sees a deletion to the Recycle Bin as a deleted file and a restore as a created file, both under the original name. The watcher cannot tell the Recycle Bin from a permanent deletion. Details: `spikes/2026-10-06-recycle-bin-events/`.

WARNING: GistDrive cannot restore a gist after a gist deletion. The revision history of the gist does not survive the gist deletion.

- REQ-DEL-1: GistDrive MUST read the deletion setting from the config file.
- REQ-DEL-2: GistDrive MUST use `off` as the default deletion setting (OPEN-29).
- REQ-DEL-3: If the deletion setting is `off` and the owner deletes a gist shortcut, GistDrive MUST show a notification.
- REQ-DEL-4: In the notification of REQ-DEL-3, GistDrive MUST state that gist deletion is off and that the gist shortcut will reappear.
- REQ-DEL-5: If the deletion setting is `off` and the owner deletes a gist shortcut, GistDrive MUST recreate the gist shortcut within 2 seconds after the deletion.
- REQ-DEL-6: GistDrive MUST apply REQ-DEL-3 through REQ-DEL-5 to the deletion of a gist folder in the same way.
- REQ-DEL-7: If the deletion setting is `on` and the owner deletes the gist shortcut of a single-file gist, GistDrive MUST delete the gist.
- REQ-DEL-8: If the deletion setting is `on` and the owner deletes a gist shortcut in a gist folder, GistDrive MUST remove the gist file from the gist.
- REQ-DEL-9: If the deletion setting is `on` and the owner deletes a gist folder, GistDrive MUST delete the gist.
- REQ-DEL-10: GistDrive MUST add a **Delete gist** menu entry to each gist shortcut (OPEN-13).
- REQ-DEL-11: If the owner selects **Delete gist**, GistDrive MUST delete as REQ-DEL-7 and REQ-DEL-8 specify, independent of the deletion setting (OPEN-12).

## Renaming

Decision (2026-10-06): a rename in File Explorer renames on GitHub. The update call of the GitHub API accepts a new file name, so GistDrive can rename a gist file.

Spike (2026-10-06): a rename in File Explorer keeps the hidden `.url` extension. A rename of `notes.md` to `ideas.md` turns `notes.md.url` into `ideas.md.url`.

- REQ-REN-1: If the owner renames a gist shortcut, GistDrive MUST rename the gist file to the new shortcut name (OPEN-25).
- REQ-REN-2: If the owner renames a gist folder, GistDrive MUST set the description of the gist to the new folder name.

## Make public

Decision (2026-10-06): **Make public** creates a new public gist with the same gist files and deletes the secret gist. Reason: the update call of the GitHub API has no visibility field. GitHub removed its two-way visibility switch in 2016. Today the documented way to make a secret gist public is an edit on the GitHub website.

WARNING: **Make public** gives the gist a new URL. The URL of the secret gist stops working. The revision history of the secret gist does not survive.

- REQ-PUB-1: GistDrive MUST add a **Make public** menu entry to each gist shortcut directly in the secret folder (OPEN-32).
- REQ-PUB-2: GistDrive MUST add a **Make public** menu entry to each gist folder in the secret folder.
- REQ-PUB-3: GistDrive MUST NOT show the **Make public** menu entry outside the secret folder.
- REQ-PUB-4: If the owner selects **Make public**, GistDrive MUST create a public gist with the same gist files and the same description as the secret gist. REQ-UNDO-1 applies to this upload.
- REQ-PUB-5: After the public gist exists, GistDrive MUST delete the secret gist, independent of the deletion setting.

## Edit

Decision (2026-10-06): the **Edit** menu entry downloads a gist file next to its gist shortcut and opens the downloaded file. A changed save then updates the gist file as REQ-UP-4 specifies.

- REQ-EDIT-1: GistDrive MUST add an **Edit** menu entry to each gist shortcut.
- REQ-EDIT-2: If the owner selects **Edit**, GistDrive MUST download the gist file into the folder of the gist shortcut.
- REQ-EDIT-3: GistDrive MUST give the downloaded file the shortcut name as its file name.
- REQ-EDIT-4: After the download, GistDrive MUST open the downloaded file with the default app for its file type.
- REQ-EDIT-5: GistDrive MUST NOT upload a downloaded file whose content equals the content of its gist file (OPEN-27).

## Send To

Decision (2026-10-06): the Send To entries **Secret gist** and **Public gist** upload a copy of a file. The original file stays in its folder.

Decision (2026-10-06): each gist shortcut from a Send To entry stores its source path. A second send of the same original file updates the gist. A different file with the same name becomes a new gist.

- REQ-SEND-1: GistDrive MUST add exactly 2 Send To entries: **Secret gist** and **Public gist**.
- REQ-SEND-2: If the owner sends a text file through **Secret gist**, GistDrive MUST upload the text file to a secret gist.
- REQ-SEND-3: If the owner sends a text file through **Public gist**, GistDrive MUST upload the text file to a public gist.
- REQ-SEND-4: GistDrive MUST leave the original file unchanged in its folder.
- REQ-SEND-5: GistDrive MUST store the source path in the gist shortcut of each gist that a Send To entry creates.
- REQ-SEND-6: If the top folder for the chosen visibility holds a gist shortcut with the same source path, GistDrive MUST update the gist file of that gist shortcut.
- REQ-SEND-7: If no gist shortcut in that top folder has the same source path, GistDrive MUST create a new gist. REQ-LAY-7 applies if the shortcut names clash.
- REQ-SEND-8: If the owner sends a file that is not a text file, GistDrive MUST refuse the file with a notification (OPEN-26).

## Failures

Decision (2026-10-06): an upload that fails moves its file into the failed folder of its top folder. A reason file next to the file says what went wrong. Moving the file back retries the upload.

Decision (2026-10-06): GistDrive retries a transient failure for the retry period before GistDrive moves the file. A permanent failure moves the file without a retry.

- REQ-FAIL-1: GistDrive MUST create a failed folder in each top folder.
- REQ-FAIL-2: After a transient failure, GistDrive MUST retry the upload until the retry period ends (OPEN-16, OPEN-17).
- REQ-FAIL-3: If the upload still fails at the end of the retry period, GistDrive MUST move the file into the failed folder of its top folder.
- REQ-FAIL-4: After a permanent failure, GistDrive MUST move the file into the failed folder of its top folder without a retry.
- REQ-FAIL-5: Next to each file that GistDrive moves into a failed folder, GistDrive MUST write a reason file (OPEN-18).
- REQ-FAIL-6: If the owner moves a file from a failed folder into its top folder, GistDrive MUST upload the file again (OPEN-15).

## Configuration

- REQ-CFG-1: GistDrive MUST read the following settings from the config file:
  - the root folder (REQ-SHELL-7)
  - the undo window (REQ-UNDO-5)
  - the secret upload delay (REQ-UNDO-6)
  - the deletion setting (REQ-DEL-2)
  - the retry period (OPEN-17)

## Open items

Each open item states its assumption. The assumption holds until the owner decides.

- OPEN-6: Windows 11 menus. Windows 11 shows registry menu entries only under **Show more options**. The top level of the Windows 11 menu needs `IExplorerCommand` and an app identity. Assumption: this placement is acceptable until GistDrive gets published.
- OPEN-7: Text check and size limit. Assumption: a text file decodes as UTF-8 and contains no NUL byte. The size limit is 1 MB, because the GitHub API returns at most 1 MB of content for each gist file. The GitHub documentation does not say whether 1 MB means 10^6 bytes or 2^20 bytes.
- OPEN-8: Save detection. Apps save through temporary files, renames, and lock files. Assumption: GistDrive acts on a file only after the file is closed and unchanged for 2 seconds. GistDrive ignores the names of temporary files and lock files. GistDrive ignores its own writes.
- OPEN-9: Changes on the GitHub side. Assumption: each refresh makes the mirror match GitHub. GistDrive removes the gist shortcut of a gist that no longer exists. GistDrive renames gist shortcuts and gist folders to match GitHub. GistDrive moves a gist that became public into the public folder.
- OPEN-10: Repeated saves during the undo window. Assumption: each save restarts the undo window.
- OPEN-11: A cancelled upload. Assumption: the file stays in its folder. GistDrive does not upload the file until the owner saves the file again.
- OPEN-12: The gist shortcut during the undo window of **Delete gist**. Assumption: the gist shortcut stays until the deletion executes.
- OPEN-13: **Delete gist** on gist folders. Assumption: each gist folder gets the menu entry too, and the menu entry deletes the whole gist. The `AppliesTo` condition matches the folders directly in a top folder and excludes the failed folder.
- OPEN-14: Batch actions. Assumption: one action on at least 2 items at the same time shows one notification for the whole batch.
- OPEN-15: A failed upload from a gist folder. Assumption: the file goes into the failed folder of the top folder, and the reason file names the gist. If the owner moves the file back into the gist folder, GistDrive retries the upload to that gist.
- OPEN-16: Transient failures. Assumption: network errors, server errors (HTTP 5xx), and rate limits are transient failures. Each other error from GitHub is a permanent failure.
- OPEN-17: The default retry period. Assumption: 5 minutes, changeable in the config file.
- OPEN-18: Reason file cleanup. Assumption: GistDrive deletes the reason file after the owner moves its file out of the failed folder.
- OPEN-19: Failures without a file: a rename, a deletion, and **Make public**. Assumption: GistDrive restores the previous state of the mirror and shows a notification.
- OPEN-20: The description of a new single-file gist. Assumption: the description is empty.
- OPEN-21: An added folder with subfolders or with files that are not text files. A gist cannot hold a subfolder. Assumption: GistDrive moves the whole added folder into the failed folder and writes a reason file.
- OPEN-22: A gist shortcut that the owner moves by hand between the top folders. Assumption: GistDrive moves the gist shortcut back and shows a notification that points to **Make public**.
- OPEN-23: Gist folder names. Windows forbids the characters `< > : " / \ | ? *` in file names, and "the first gist file" needs an order. Assumption: GistDrive replaces each forbidden character with `_`, and "first" means first in ordinal sort order. GistDrive cuts a gist folder name to 100 characters, because a description can be a whole paragraph. A rename of the gist folder then writes the replacement characters and the cut into the description.
- OPEN-24: Clash suffixes. Assumption: every item in a clash gets the short gist ID. The short gist ID goes away after the clash ends.
- OPEN-25: The rename of a gist shortcut that carries a short gist ID. Assumption: GistDrive removes the short gist ID from the new name of the gist file.
- OPEN-26: Send To with a folder, and Send To with at least 2 files at the same time. Assumption: each file becomes its own gist. A folder behaves like a folder that the owner adds to the top folder.
- OPEN-27: A downloaded file from **Edit** that never changes. Assumption: the file stays in the folder, and GistDrive ignores it.
- OPEN-28: Startup, installation, and removal. Assumption: GistDrive starts from the Windows Startup folder. The `install` command of GistDrive adds the sidebar entry, every registry value, the Send To entries, and the folder icons. The `uninstall` command removes the same items. The `quit` command stops the running GistDrive, so that an update can replace the executable. Before the `install` command starts GistDrive, the `install` command stops a running GistDrive.
- OPEN-29: The default of the deletion setting. The owner asked for gist deletion as an option "if we want to allow that". Assumption: the default is `off`.
- OPEN-30: A notification during a secret upload delay longer than 0 seconds. Assumption: GistDrive shows the same notification with a **Cancel** button that REQ-UNDO-10 requires for the undo window.
- OPEN-31: A restore after a recreation. With the deletion setting `off`, GistDrive recreates the gist shortcut, so a later restore from the Recycle Bin meets a file with the same name. Assumption: this case is acceptable. Windows asks the owner whether to replace the file, and both files hold the same URL.
- OPEN-32: **Make public** on a gist shortcut inside a gist folder. Assumption: the menu entry does not appear there. The owner uses **Make public** on the gist folder.
- OPEN-33: Empty files. File Explorer creates an empty file for **New** > **Text Document**, and GitHub rejects empty content. Assumption: GistDrive waits until a file has content. An empty file is neither uploaded nor moved into a failed folder.
- OPEN-34: A `.url` file that GistDrive did not write, for example a link that the owner dragged from a browser. Assumption: GistDrive ignores the file. GistDrive neither uploads the file nor counts the file as a gist shortcut.
- OPEN-35: A gist shortcut or a gist folder that the owner moves out of the root folder. The file watcher reports the move as a deletion. Assumption: the move counts as a deletion and follows the deletion setting.
- OPEN-36: The source path after **Make public**. The public copy is a new gist. Assumption: the public copy has no source path, so a later send of the original file creates a new gist.
