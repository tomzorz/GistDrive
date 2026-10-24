# GistDrive

GistDrive puts a GitHub account's gists in File Explorer: a sidebar entry over two real folders, `Secret` and `Public`, with a `.url` shortcut per gist file, and uploads, edits, deletions and Make public driven from those folders and their menus. The specification is `docs/SPEC.md`, in Lojbanlite, with each decision in the section it affects and no separate decisions file. Code follows the spec; a change of behaviour changes the spec first.

## Repo

- Single-branch: commit to `main`, push `main`.
- This repository is public. Nothing in it names a particular machine, person, path or setup: not in code, docs, examples, test fixtures, spike notes or commit messages. Examples use generic paths such as `D:\Sources\...` and `%USERPROFILE%\...`.
- License: MIT.
- Archetype layout: projects go in `sources/<project>/`, each with its stack's ignore file from archetype, created on the day the project exists.
- LFS is on. A fresh clone needs `git lfs install --local`.
- Spikes live in `spikes/YYYY-MM-DD-<slug>/` and are committed. Each has a README with its question, findings and verdict.
- Anything worth keeping past this session goes through the Record skill. It reads `AGENTS.records.md` next to this file for where each kind of information lives here. "Remember this" and "park this" are always worth keeping; anything the code or git history already says never is.

## Building and testing

- `dotnet test --project sources/GistDrive.Tests/GistDrive.Tests.csproj` runs the offline suite against the real disk, the file watcher and a sandbox registry key, with fakes for GitHub and for notifications that share contract suites with the real implementations.
- The opt-in suites reach real things: `GISTDRIVE_REAL_NOTIFIER=1` shows Windows notifications, `GISTDRIVE_LIVE_READONLY=1` reads the signed-in account's gists, and `GISTDRIVE_LIVE_GITHUB=1` creates and deletes secret gists in that account. Run the last one only with the account owner's yes.
- There is no CI. Releases are built by hand with the procedure in `docs/SPEC.md`, section "Release".
- `sources/IconMaker` draws the icons; never edit the `.ico` files by hand.
- `sources/DemoVideo` renders `docs/demo.gif` and the 1080p MP4; change the page and run `node render.mjs` again rather than editing the GIF. The MP4 stays out of the repo.
