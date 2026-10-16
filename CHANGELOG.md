# Changelog

## v0.2.3 - 2026-10-16

- Added pattern-only outfit preparation before EFMI replacement draws, plus
  the new skin-color picker callback. Existing wrappers retain their Begin/End
  interface. All three complete packages and the embedded installer now carry
  fifteen runtime files; settings, upgrade and restore cover the added file.
- Replaced the initial v0.2.3 assets after feedback that Tangtang improved but
  other original characters still showed black regions. The accepted revision
  retains thirteen exact rules, then adds 28 structural surface receivers and
  14 geometry fallbacks. They match shared native transforms without an operator
  registry and preserve native material instructions and legacy picker priority.
- Synchronized Mod, drop-in, standalone and the configurator's embedded runtime.
  Existing downloads of the initial v0.2.3 need to be downloaded again.
- The structural runtime passed 790 static/GPU/loader checks and 431 numerical
  geometry/captured-vertex checks. The user accepted the candidate for replacing
  v0.2.3. This is not an all-character or future-shader compatibility guarantee.
- Added the current-game original skin color program to the touch deformation
  pipeline, synchronizing color with prepass and outline. Kept native material
  instructions and corrected zero-displacement/large-coordinate surface frames.
- Flattened Mod and drop-in archives: shaders and runtime INIs are at ZIP root.
  Drop-in adds an optional Configurator directory; standalone retains EFMI/Mods.
- Updated the embedded installer and configurator for the thirteen-program
  runtime, flat archives, and in-place upgrades/restores in version-named folders.
- Included the normal standalone manual-official-launcher and cache-path fixes.
  Kept no-touch diagnostics separate from the black-surface repair.
- At initial publication, kept the original twelve outfit callbacks; new color
  coverage then applied to native geometry. The replacement above extends the
  outfit entry separately.
- Before the initial publication, the skin runtime passed 226 static/GPU/loader checks and
  102 captured-vertex checks. The user accepted it for release. This does not
  establish all-character, all-outfit or standalone startup compatibility.

## v0.2.2 Manual Official Launcher Revision - 2026-10-15

- Diagnosed a post-load crash caused by deeply nested extraction paths overflowing
  the upstream fixed-size shader-cache filename buffer. Added a read-only path
  preflight before runtime loading and short extraction-directory guidance.
- Fixed the reported Windows 299/5 failure in the launcher path check: use
  QueryFullProcessImageNameW and GetExitCodeProcess with limited query rights,
  not Process.MainModule or module enumeration. Still refuse unverifiable targets.
- Replaced direct game execution in both standalone bundles with bounded waiting
  for a game launched by the user through the official launcher.
- Kept the existing upstream loading interface and official runtime DLLs unchanged.
  No authentication, game security or anti-cheat components are modified.
- Matched the selected game path and rejected existing or ambiguous game processes,
  concurrent XXMI sessions and inaccessible targets before loading.
- Removed automatic elevation/relaunch. Added cancellation, separate process/load
  timeouts, launcher phase logs and explicit manual DirectX 11 selection guidance.
- Diagnostic revision 3 retains revision 2 GPU telemetry and bounded logging,
  adding a launch-stage summary. A loaded DLL is not a verified touch result.
- Isolated tests do not establish compatibility with the updated live game.

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
  installation were modified.
- On October 12, 2026, the user confirmed that the corrected standalone launcher
  starts successfully, resolving the reported Windows error 740. This feedback
  does not establish new model, touch or same-process F10 verification.

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
