# Changelog

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
