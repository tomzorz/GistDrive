# Spike: does an extra INI section in a `.url` file survive Windows?

**Date**: 2026-10-06
**Timebox**: 20 minutes
**Time spent**: about 5 minutes
**Sandbox**: `spikes/2026-10-06-url-custom-data/`

## Question

GistDrive wants to keep the source path of a Send To upload inside the gist shortcut, in a `[GistDrive]` section. Does that section survive the ways Windows rewrites `.url` files?

## Findings

`url-data-test.ps1` writes a shortcut with a `[GistDrive]` section, runs it through each rewriter, and checks the section afterwards. The section survived all of them:

- the shell's own `.url` object (`CLSID_InternetShortcut`, which the Properties dialog uses) with `SetURL` and `Save`
- the same object with `SetIconLocation` and `Save`
- `WScript.Shell` `CreateShortcut`, a new `TargetPath`, and `Save`
- `Shell.Application` `CopyHere`
- a shell rename with the name Explorer shows

Side effects:

- The `.url` object adds `IDList=` and a `[{000214A0-0000-0000-C000-000000000046}]` section with `Prop3=19,11`. Harmless.
- `SetIconLocation` does not expand `%SystemRoot%`. It treated the path as relative and prefixed the current directory, giving `IconFile=<current directory>\%SystemRoot%\...`. Any icon path GistDrive writes must be absolute and already expanded.
- Renaming through the shell with the shown name `ideas.md` turned `notes.md.url` into `ideas.md.url`, so a rename in Explorer keeps the hidden `.url` extension.

## Verdict

**Go.** GistDrive keeps its own data, the source path first, in a `[GistDrive]` section of the gist shortcut. No state file is needed.

## Caveats and open questions

- The Properties dialog itself was not driven through its UI. It uses the same `.url` object tested here.
