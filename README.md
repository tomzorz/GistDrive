# GistDrive

GistDrive puts your GitHub gists in File Explorer. A sidebar entry opens two real folders, `Secret` and `Public`, with a `.url` shortcut for every gist file. Save a text file into one of them and it becomes a gist, then turns into its shortcut. Save a file with the same name next to a shortcut and the gist gets updated. Right-click a shortcut for **Edit**, **Delete gist** and **Make public**, and use **Send to** to upload a copy of a file from anywhere. The GitHub CLI does the talking to GitHub, so GistDrive stores no token of its own.

## Requirements

- Windows 10 version 2004 or later. It is tested on Windows 10 22H2. On Windows 11 the menu entries sit under **Show more options**, and nobody has tested it there yet.
- The [GitHub CLI](https://cli.github.com/), signed in: `gh auth status` should say you are logged in.

## Install

1. Download the zip from the [releases page](https://github.com/tomzorz/GistDrive/releases) and unzip it into a folder you will keep. GistDrive runs from there.
2. Run `GistDrive.exe install` in that folder. The exe is not signed, so Windows SmartScreen may ask before it runs the first time.

`install` works under your own user, no admin rights needed. It adds:

- the **GistDrive** entry in the File Explorer sidebar, opening `%USERPROFILE%\GistDrive` with `Secret` and `Public` inside
- **Edit**, **Delete gist** and **Make public** on gist shortcuts and gist folders (**Make public** only in `Secret`)
- **Send to** entries for **Secret gist** and **Public gist**
- a shortcut in your Startup folder, so GistDrive runs after every sign-in
- icons for the three folders

Then it starts GistDrive, which fills the folders with the gists you already have. Running `install` again is harmless: it stops the running copy, writes everything again and starts it.

## How it behaves

- A file saved into `Secret` becomes a secret gist straight away. A file saved into `Public` waits 30 seconds first, with a notification and a **Cancel** button, much like undo-send in a mail client. Saving again restarts the wait.
- A cancelled file stays where it is and goes up only after you save it again, even if GistDrive restarts in between.
- A folder you add becomes one gist with several files, described by the folder name.
- Deleting a shortcut does not delete the gist unless you turn `DeletionSetting` on. With it off, the shortcut comes back and a notification says why. **Delete gist** always deletes, after the same 30-second wait.
- **Edit** downloads the file next to its shortcut and opens it. Saving a change uploads it; a download you never change just sits there.
- **Make public** creates a public copy and deletes the secret gist, because GitHub has no switch for that. The link changes and the revision history of the secret gist is lost.
- The **Date created** and **Date modified** columns of every shortcut show when its gist was created and last changed on GitHub.
- A file that can't be uploaded lands in a `_failed` folder with a `.why.txt` beside it. Move it back to try again.

UTF-8 and UTF-16 text files of up to 1,000,000 bytes go up; anything else is refused.

`docs/SPEC.md` is the full specification, with every rule and the date it was decided.

## Settings

`config.json` next to the exe, written with these defaults on first run:

| Setting | Default | Meaning |
|---|---|---|
| `RootFolder` | `%USERPROFILE%\GistDrive` | where `Secret` and `Public` live |
| `UndoWindowSeconds` | 30 | how long public uploads and gist deletions wait for **Cancel** |
| `DeletionSetting` | `off` | `on` makes deleting a shortcut delete its gist file on GitHub |
| `RetryPeriodMinutes` | 5 | how long GistDrive retries when GitHub is out of reach |

Run `GistDrive.exe install` after a change, so the running copy picks it up.

## Commands

| Command | What it does |
|---|---|
| `GistDrive.exe install` | adds everything listed above and starts GistDrive |
| `GistDrive.exe uninstall` | removes all of it again; the `GistDrive` folder and your files stay |
| `GistDrive.exe quit` | stops the background copy, for example before you replace the exe with a new version |
| `GistDrive.exe refresh` | reads the gist list from GitHub now instead of within 5 minutes |

To update, run `quit`, replace the files with the ones from the new zip, then run `install`.

## When something goes wrong

- `gistdrive.log` next to the exe has a line for every countdown, upload, retry, cancel and failure.
- `held.json` next to the exe lists the files GistDrive leaves alone until they change: cancelled uploads and unchanged **Edit** downloads.
- If the countdown bar in a notification never seems to move, check your accent color. Windows draws the bar in it, and with a dark gray accent the bar is nearly invisible against its track.

## Building from source

The layout follows [archetype](https://github.com/tomzorz/archetype):

- `sources/GistDrive`: the app, a .NET 10 WinExe with no package references
- `sources/GistDrive.Tests`: xunit.v3 tests
- `sources/IconMaker`: the WPF program that draws `sources/GistDrive/icons`. Change the drawing there and run `dotnet run --project sources/IconMaker -- sources/GistDrive/icons` rather than editing the `.ico` files.
- `docs/SPEC.md`: the specification
- `spikes`: the experiments that settled how the Windows side works, each with its findings

The repository uses Git LFS for images, so run `git lfs install --local` after cloning.

```
dotnet build sources/GistDrive/GistDrive.csproj
dotnet test --project sources/GistDrive.Tests/GistDrive.Tests.csproj
```

The default test run touches nothing outside `%TEMP%` and a sandbox registry key that it deletes again. Three opt-in suites go further:

| Variable | What runs | What it touches |
|---|---|---|
| `GISTDRIVE_REAL_NOTIFIER=1` | the notifier contract against Windows | shows and removes test notifications |
| `GISTDRIVE_LIVE_READONLY=1` | reads your real gist list and mirrors it into `%TEMP%` | nothing on GitHub |
| `GISTDRIVE_LIVE_GITHUB=1` | the gist contract suite against GitHub | creates and deletes secret gists named "GistDrive contract test" |

There is no CI. Releases get built by hand, and the steps are in `docs/SPEC.md`, under "Release".

## License

MIT, see `LICENSE`.
