# Changelog

## v0.2.2 Standalone Bundle - 2026-10-12

- Fixed Windows error 740 when the game requires administrator privileges:
  retry the same saved selection through Windows elevation, handle cancellation,
  and prevent repeated elevation loops. Kept ordinary launcher startup asInvoker.
- Clarified the game executable field with an Endfield.exe label, empty-field
  prompt, a named browse button and a persistent path hint.
- Added Hypergryph Launcher locations to bounded game discovery and replaced raw
  Windows startup exceptions with actionable Chinese messages.
- Added a portable bundle for users without XXMI Launcher, alongside the existing
  XXMI drop-in and Mod-only packages in the same Release.
- Bundled unmodified official EFMI 1.4.8 and XXMI-Libs 1.2.2 libraries, preserving
  their notices and pinned source references.
- Added a self-contained Windows launcher with game selection, bounded discovery,
  saved paths, DX11 startup, cancellation, local logs and library integrity checks.
- Added shortcuts to the existing configurator and the bundle's own Mods directory.
  The v0.2.2 touch runtime and configurator are unchanged.
- Kept installation inside the extracted bundle. No game files or existing XXMI
  installation were modified. Real game launch and touch verification remain pending.

## v0.2.2 - 2026-10-11

- Restored a browsable Mod library, named preview images, search and disabled filters.
- Added selectable indexed draws, parameter presets, shortcut recording, JSON profiles
  and local diagnostic reports to the embedded portable configurator.
- Shortcut and parameter saves/restores now support the explicit in-game config
  reload workflow. Read and display the actual EFMI reload binding.
- Added read-only third-party key conflict warnings, including Shift menu conflicts.
  No third-party menu bindings are rewritten.
- Added a drop-in ZIP whose `EndfieldJiggleEFMI` folder goes directly into `Mods`.
  EXE remains optional; installation and initial upgrades still require a restart.
- Retained the v0.2.1 outfit bridge and unchanged shader/cache payload.
- In-game visual and same-process F10 verification remains pending.
- Moved directory discovery, library/image loading, outfit/dependency inspection
  and configuration writes off the UI thread. Serialized operations with
  cancellable scans and local operation timing logs.
- Added bounded automatic runtime discovery, launcher path lookup and detection
  of accidentally double-nested drop-in folders. No directories are moved.
- Diagnostic view separately reports EFMI Mods Display and show_original bindings.

## v0.2.1 - 2026-10-10

- Restored the outfit bridge omitted from the v0.2.0 distribution: explicit
  replacement-range picking, native-pass exclusions and twelve shader callbacks.
- Fixed mouse/session cancellation after changing drag bindings.
- Added reproducible package generation and release regression negative controls.
- Added an optional combined Mod/configurator archive, with persistent INI settings.
- Added a separate, evidence-based QAQM missing-state recovery exporter.
- Embedded the reviewed runtime in the optional EXE; added one-click installation,
  settings-preserving upgrades, owned-file rollback and installation recovery.
- Added reversible in-place indexed outfit adaptation without duplicate outfit Mods.
- Added general QAQM state inspection/recovery using actual include dependencies,
  not fixed character or Bridge IDs.
- Added an explicit F10 reload workflow for supported outfit INI changes.
- In-game outfit UI and F10 model-disappearance verification remains pending.

## v0.2.0

- Added an optional self-contained Windows x64 configurator for installed runtime
  settings; game startup remains independent of the EXE.
- Added exact per-file configuration backups, guarded apply/restore and isolated
  tests against the published v0.2.0 Mod ZIP.
- Added a separate Windows installable Mod release; kept captured compatibility
  payloads out of the source tree.
- Added manual installation, backup, recovery and removal instructions.
- Reworked the Chinese README for public sharing.
- Added usage, troubleshooting, development and disclaimer documents.
- Clarified source-release boundaries without changing runtime code.

## v0.1.0-source - 2026-10-04

- First public source-only research snapshot.
- Shared native-interface eligibility without per-operator or mesh whitelist.
- Shared picking, input, spring motion and deformation source.
- Native-interface adaptation and test-host source.
- Local complete build's user-reported migration success documented.
- Game shader resources, caches, DLLs and installable payload intentionally
  excluded. Complete runtime rebuild from this snapshot alone is unavailable.
