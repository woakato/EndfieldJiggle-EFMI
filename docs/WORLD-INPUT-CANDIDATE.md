# World Input Candidate

Status: local experiment following the October 17, 2026 rollback. This is not
an accepted release or a verified in-game fix.

The restored runtime's drag binding explicitly excludes Alt. This prevents
drag input when Alt is held. Separately, its cursor mapping defaults to the
vertically inverted character-menu target. There is no new world capture
establishing whether that scene uses the existing color pickers.

## Scope

`tools/Build-WorldInputCandidate.ps1` builds a separate copy under `reports`.
Only `EndfieldJiggle.ini` changes:

- The existing left-button hold binding allows an additional Alt modifier.
  Its Ctrl/Shift requirements and sole writer of `$drag` remain intact.
- Ctrl+Shift+F6 explicitly toggles the existing `$cursor_flip_y` parameter.
  Startup retains the character-menu mapping.
- Mapping changes require release of both drag input and the left button.
  They clear the previous capture, motion and pick, and restart session warmup.
- A four-second message identifies the selected mapping.

The native/outfit hooks and all picker, pixel and solver shader bytes and
source/cache timestamps remain unchanged. No automatic projection detection
or shader-interface extension from the withdrawn candidate is used.

## Local Build

```powershell
.\tools\Build-WorldInputCandidate.ps1 `
    -BaselineDirectory '<accepted runtime with current user settings>' `
    -OutputDirectory '<checkout>\reports\world-touch\<new directory>'
```

The builder preserves the baseline's startup setting, physics and drag chord.
It does not deploy, package a configurator, modify other mods or publish.
The output includes the original INI and an identity report.
`Test-WorldInputCandidate.ps1` verifies the unchanged published payload and
tests loading and mapping/session transitions using an isolated EFMI loader.
Those command-list tests do not simulate physical keyboard polling.

## In-Game Acceptance Still Required

For the current user's left-button binding, first confirm ordinary menu
dragging with the startup mapping. In the world, compare Alt+left drag with
that mapping before changing anything else. Release Alt and the mouse,
press Ctrl+Shift+F6 once, then compare Alt+left drag using the WORLD TEST
mapping. Switch back before testing the character menu again.

Ctrl+Shift+F9 retains the existing status overlay. `NO COMPATIBLE COLOR DRAW`
or `NO COMPATIBLE NATIVE PROGRAM` calls for world draw evidence; changing the
cursor direction cannot add a missing shader hook. `DRAG INPUT HELD` only
confirms input, not a successful pick.

The stable configurator writes `no_alt` back when saving bindings. Do not
treat this experiment as configurator-integrated release support.
Live deployment remains a separate operation under the workspace AGENTS.md.
