# Package Identity and Maintenance Status

Release preparation, 2026-10-16: after accepting the skin-pass candidate, the
user explicitly authorized publishing the other package types and requested
that shaders and runtime INIs sit directly inside the extraction directory.
The resulting release is v0.2.3: pure Mod and drop-in ZIP roots are flat;
normal standalone retains its launcher/EFMI/Mods structure. v0.2.2 assets and
the separate no-touch diagnostic builds are preserved. Publication must be
confirmed from the release API, not inferred from this preparation note.
The release runtime is byte-identical to the accepted skin-pass candidate.
The configurator is rebuilt with that runtime, supports thirteen native passes,
and upgrades/restores the actual version-named direct child of Mods.

Reviewed on 2026-10-16, Asia/Shanghai. This is a maintenance snapshot, not a
release announcement. GitHub release assets were checked through
`https://api.github.com/repos/woakato/EndfieldJiggle-EFMI/releases?per_page=100`.
Recheck the API before claiming a later upload or latest release.

## Published Versions

| Tag | Purpose |
| --- | --- |
| `v0.1.0-source` | Source research prerelease; not an installable Mod. |
| `v0.2.0` | Installable pure Mod and optional configurator; predates the restored outfit bridge. |
| `v0.2.1` | Outfit bridge repair, pure Mod, complete installer/configurator bundle, separate configurator and source. |
| `v0.2.2` | Three published installable package types below. Local maintenance builds also use this version, so the version string alone is insufficient. |

## Published v0.2.2 Packages

| Asset | Intended installation | Contents |
| --- | --- | --- |
| `EndfieldJiggleEFMI-v0.2.2-win64.zip` | Existing EFMI | Pure touch Mod; no configurator or launcher. |
| `EndfieldJiggleEFMI-v0.2.2-drop-in-win64.zip` | Existing EFMI | Touch Mod plus optional configurator. |
| `EndfieldJiggleEFMI-v0.2.2-standalone-win64.zip` | Separate portable installation | Launcher, official EFMI/libraries, touch Mod and configurator. This is the normal standalone package. |

GitHub asset SHA-256 values matched these local archives. Paths are relative
to `D:\progect\EndfieldJiggle-EFMI\reports\publication`:

| Local archive | SHA-256 |
| --- | --- |
| `github-v022-maintenance-20261012/packages/EndfieldJiggleEFMI-v0.2.2-win64.zip` | `60D1A20332C74754CDDB2197FA68DFB1CB206623ACE8E89F851D8E53394305F5` |
| `github-v022-maintenance-20261012/packages/EndfieldJiggleEFMI-v0.2.2-drop-in-win64.zip` | `88433A0402C9E85C1D3790632C83D020DCF4699A90A7358D11E47B9789FD57E3` |
| `v0.2.2/standalone-uac-fix-20261012/EndfieldJiggleEFMI-v0.2.2-standalone-win64.zip` | `8B15A7715E8F4A08CCA9DEE1F34FB19624610733E6E8CE28AC1AFFA4BBF53964` |

## Local Maintenance Builds

Paths below are relative to the same publication directory unless stated.
Folder names such as `final`, `release` or `verified` do not prove publication.

| Build family | Purpose and status |
| --- | --- |
| `v0.2.2/portable-configurator-{candidate,final,fix2}-20261011` | Historical portable configurator/package revisions. The reviewed `fix2` Mod ZIP has hash `87F05BFF53B9D7413FAB2E0EEF9EE840068F38451E2E89DB3C0E5E299AD8408E`, distinct from the published Mod ZIP. Do not substitute solely by filename. |
| `v0.2.2/standalone-20261012` | Initial standalone build; followed by the UAC fix. |
| `v0.2.2/standalone-uac-fix-20261012` | Published standalone baseline identified above. Launch feedback does not establish touch/rendering verification. |
| `v0.2.2/diagnostic*-20261014*` | No-touch diagnostic revisions and audits, including GPU telemetry and bounded collection. These are not black-skin fixes. |
| `v0.2.2/manual-launch-20261015` | Normal and diagnostic standalone builds waiting for the official launcher. |
| `v0.2.2/manual-launch-queryfix-20261015` | Subsequent process-query repair; superseded locally by startup-pathfix. |
| `v0.2.2/startup-pathfix-20261015` | Normal and diagnostic standalone builds with cache-path preflight and shorter extraction roots. Local, not the GitHub standalone asset checked above. |
| This repo's `reports/shading-maintenance/candidate-20261015-232307-737` | First pure Mod shading candidate. User reports black surfaces remain on 2026-10-16. Not an accepted rendering fix. |
| This repo's `reports/shading-maintenance/skin-pass-20261016-193543-936` | Second pure Mod candidate. Adds the captured original skin color VS `5869b530fc5ca4cf`; only runtime `Passes.ini` changes. Offline checks passed; in-game confirmation pending. |

The startup-pathfix normal ZIP hash is
`B3248922DD94D1866BD764E8519681508A5FED50F7EDCB6880E07C089BAACB68`.
Its diagnostic ZIP hash is
`C3EDDF001FF32BF99E03401117E32036E107743B016838B4B7605CD4E7F2209D`.

The diagnostic suffix is `standalone-diagnostic-win64.zip`. Its purpose is
investigating missing touch/deformation or startup failures. Preserve that
purpose, telemetry, bindings and collector independently of shading work.

## Current Work

The user first prioritized drop-in and normal packages, then narrowed the
immediate task to the pure Mod package. Fix and verify its black-surface
behavior before rebuilding other package types.

The first shading candidate is a pure Mod package containing numerical
surface-frame corrections. It is neither the no-touch diagnostic package
nor a complete drop-in/standalone update. Its 34 numerical and 152
static/GPU/loader checks did not reproduce the user's current game materials.
On 2026-10-16 the user reported duplicate-loading warnings, removed the extra
copy, and confirmed that black surfaces still occur. Treat the rendering bug
as unresolved; do not attribute it to duplicate loading again without evidence.
The user subsequently confirmed that the affected surface uses the original
game skin, not an outfit Mod.

Historical test reports stay unchanged. Record new game feedback separately.
Current-version captures were subsequently received on 2026-10-16 at 17:52:21,
17:53:02 and 17:53:49, in user-specified state order 2, 3, 1. Local findings
are in `reports/shading-maintenance/capture-20261016/FINDINGS.md`. They show
an uncovered skin color VS (`5869b530fc5ca4cf`) while related passes receive
deformation. The original instructions were exported during the 19:20 game
session and the missing color program is now adapted in the second candidate.
Its 226 static/GPU/loader and 102 captured-vertex checks passed. For all 4,825
skin vertices at three synthetic touch locations, current world-position
outputs from color, prepass and outline now agree exactly. Five captured draws
preserve every native output bit at zero control. This is vertex-stage evidence,
not confirmation of the final game's appearance.

The new pure Mod archive is
`EndfieldJiggleEFMI-v0.2.2-skin-pass-fix-candidate-win64.zip`, SHA-256
`C0B143F6E8C65C78C669A6EE70D610E441855D51F2A42F317DCE4AE0BB5533A3`.
Its root contains `EndfieldJiggleEFMI` and notices/instructions. Existing users
of the first shading candidate need only replace `Passes.ini`. No drop-in,
normal standalone, diagnostic, or published package was rebuilt. Shader export
was restored to 0 after collection; no live Mod was deployed.

The user's separate enabled-directory comparison is deferred at their request;
do not explain it away by the example path's `DISABLED_` prefix.
