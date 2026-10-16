# Package Identity and Maintenance Status

## Complete Outfit Repair Packages

After receiving the complete bundles, the user explicitly accepted publishing
them to GitHub on October 16, 2026. The authorized operation replaces the existing
v0.2.3 assets, updates its source/tag and release notes, and preserves v0.2.2 and
the separate no-touch diagnostic builds. The publication record is maintained
under the parent project's `reports/publication/v0.2.3/outfit-complete-20261016`.

The user subsequently requested all three complete packages directly.
The rebuilt configurator now recognizes and fingerprints OutfitPreparation.ini,
checks that it accompanies the matching bridge, and includes it in installation,
upgrade and recovery. It retains the candidate's fifteen runtime files without
changing their bytes. Fourteen-file v0.2.3 migration and restoration passed.

Flat Mod/drop-in build:
`reports/releases/v0.2.3-outfit-complete-20261016-r1`.
Normal standalone build, relative to the parent project:
`reports/v023-outfit-complete`.
Local delivery and verification:
`reports/shading-fixes/20261016-outfit-complete`.
These complete packages supersede the candidate's "old configurator not
supported" instruction. Existing EJ-OUTFIT/1 wrappers need no re-adaptation.
Building these archives alone did not imply current-game outfit visual
acceptance or publication. The user's subsequent acceptance authorizes release;
it does not supply an all-outfit visual test matrix. The diagnostic standalone
remains separate.

All three complete local packages were verified and delivered under that
directory's `assets` subdirectory:

| Package | SHA-256 |
| --- | --- |
| `EndfieldJiggleEFMI-v0.2.3-win64.zip` | `B084C2D92FB93D6EE2206AB7EC04C4893D577F794EA2A52EEEA06EFF3DDCBE79` |
| `EndfieldJiggleEFMI-v0.2.3-drop-in-win64.zip` | `BD179CFC665BE785BD61909AB537E76A7A1CA4FA0F722561D0CC42B016740ADC` |
| `EndfieldJiggleEFMI-v0.2.3-standalone-win64.zip` | `ABAB99BB2F73A5B4A9DBB69B1BA1F9AA75E9416FF65B8E6828DFAE9ACB0435EC` |

Validation includes 73 configurator checks, 22 installation checks (including
fourteen-to-fifteen-file migration and restore), 16 discovery checks, outfit
adapter tests and the embedded configurator smoke. Standalone passed 60 launcher
checks, all 87 archive file checksums and four shader/cache timestamp pairs.
The final 126 cross-package checks confirm identical runtime payloads and the
rebuilt embedded installer. The compiled assembly's embedded runtime ZIP was
also hashed against the complete pure Mod ZIP.
`delivery-receipt.json` records paths, identities and the earlier 525 outfit
GPU checks. Its publication flags describe the local delivery before the user's
subsequent request to publish; preserve that historical receipt.

## Outfit Feedback Before Replacement Upload

Later on 2026-10-16, while the three replacement packages were being prepared,
the user reported black blocks after adapting outfit Mods. The replacement
upload was deferred for this investigation. No new commit, tag update or
release-asset overwrite has occurred during this replacement task.

The installed native `Passes.ini` was read-only verified against receiver r5;
its `Outfits.ini` still uses the legacy twelve-program entry gate. An isolated
GPU probe reproduced zero touch control for the other 21 of 33 native programs
during replacement draws, despite native shader patch coverage.

New pure Mod candidate: `reports/shading-maintenance/outfit-receiver-20261016-r1`.
It changes `Outfits.ini` and adds `OutfitPreparation.ini`, whose pattern-only
callbacks run before EFMI's replacement draws. They select the already reviewed
instance-buffer contract without changing shaders or author filter indexes.
The additional exact skin-color program uses the existing compatible picker.
`Passes.ini`, shaders, and all other native runtime bytes/timestamps are unchanged.
Existing EJ-OUTFIT/1 wrappers keep their Begin/End interface.

This candidate has fifteen owned runtime files. The previously rebuilt
configurator and the prepared native-only replacement archives still have the
fourteen-file contract; they are not the outfit repair and must not be published
as such. At candidate delivery, configurator/package integration was pending;
the complete-package request and integration above supersede that status.
Do not tell the user to re-adapt an already wrapped outfit for this bridge fix.

Delivery and verification records belong under the parent project's
`reports/shading-fixes/20261016-outfit-receiver`. The two-file update is only for
an installation already using receiver r5. A separate flat full pure Mod ZIP
is also supplied. No live deployment was performed.

Delivery identities:

- Full pure Mod ZIP SHA-256:
  `7B89C35AA436F8B4800B0953B571A667C7D40E056CA31086B98616D626E6220D`.
- Two-file update ZIP SHA-256:
  `023E30F91B3634B32F20C25E4D424867B127B2DE86E93BEEBBF90413804EEB64`.
- Outfits.ini SHA-256:
  `DC5E5464F7C2F05960886AF9FDD099889443A59224DF34E40AC1956D698A40E3`.
- OutfitPreparation.ini SHA-256:
  `D0EC67C88CE25120532D10C50232F5A6E11944745B4740B5B38712E5E8C094E1`.

All 525 final GPU/loader checks passed, plus two raster runs with six successful
replacement-range picks. No live outfit visual result has been reported.

## Accepted Structural Replacement

On 2026-10-16 the user reported that the structural receiver candidate should
be working and explicitly requested pushing it and replacing the existing
v0.2.3 release. This supersedes the pending-acceptance status below.
The accepted runtime is receiver r5, with `Passes.ini` SHA-256
`104CBCB3C2614C1219670C8D598C72A90DEAED756F235D42EB072D9929C509ED`.
No all-character visual test matrix was supplied with that feedback.

Replacement packaging synchronizes pure Mod, drop-in, normal standalone and
the configurator's embedded installer. Mod/drop-in roots remain flat.
The configurator recognizes the two structural receiver command lists in
addition to thirteen exact passes, while preserving receiver content during
settings changes and retaining the twelve existing outfit callbacks.
The no-touch diagnostic package and v0.2.2 remain separate.

Final replacement assets and the publication receipt belong under the parent
project's `reports/publication/v0.2.3/native-receiver-20261016`.
The initial publication records and hashes below identify the superseded
October 16 21:05 release assets, not the structural replacement.

## Rendering Feedback After v0.2.3

On 2026-10-16, after publication, the user reported that Tangtang improved but
other original characters still show black regions. The earlier acceptance
did not establish a general rendering fix. The structural candidate was
subsequently accepted as recorded above.

The current local pure Mod candidate is
`reports/shading-maintenance/receiver-20261016-r5`. It retains the thirteen
earlier exact rules, then recognizes shared native position transforms while
preserving intervening material/skinning instructions. Interface-specific
rules also update normals/tangents; geometry fallbacks accept new varying
layouts without inventing their material semantics. Existing color picking
still establishes the touch session. There is no operator or mesh registry.

Only runtime `Passes.ini` changes. The candidate ZIP is flat, with `shaders`
directly at its root. At candidate delivery, no configurator, standalone or
published asset had changed. The subsequent replacement task updates the
three release package types and embedded configurator.

Delivery is under the parent project's
`reports/shading-fixes/20261016-native-receiver`. The candidate ZIP SHA-256 is
`354FAD670117B4A58B8E08F4B8FFE2AABE70CE2B51B951F5DF6B6F703AB18BC1`.
See `NATIVE-RECEIVER-VALIDATION.md` for the offline evidence and its limits.

## Initial Publication Record

Published on 2026-10-16 at 21:05:55 +08:00: after accepting the skin-pass candidate, the
user explicitly authorized publishing the other package types and requested
that shaders and runtime INIs sit directly inside the extraction directory.
The resulting release is v0.2.3: pure Mod and drop-in ZIP roots are flat;
normal standalone retains its launcher/EFMI/Mods structure. v0.2.2 assets and
the separate no-touch diagnostic builds are preserved. The release API confirmed
v0.2.3 as the latest stable release, with eight uploaded asset digests matching
local files. Release id: `414970104`; tag commit:
`9628ae4f6868cc125ec4ef045cf894b67bb0afa1`.
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
| `v0.2.3` | Published original-skin black-surface repair, flat pure Mod/drop-in packages and normal standalone; updated embedded configurator and source. |

## Initial v0.2.3 Packages (Superseded)

Final local archives are under the parent project's `reports/publication/v0.2.3`.
The source archive and installation instructions are separate assets. Pure Mod
and drop-in archives put shaders and runtime INIs directly at ZIP root; extract
each into its own directory under Mods. Standalone retains the EndfieldJiggle
product root and its EFMI/Mods/EndfieldJiggleEFMI directory.

| Asset | SHA-256 |
| --- | --- |
| `EndfieldJiggleEFMI-v0.2.3-win64.zip` | `316bf5c51cbde4991d2b03e32b09bceb7769e55f5bf561874488a25b66ea4622` |
| `EndfieldJiggleEFMI-v0.2.3-drop-in-win64.zip` | `1cec1d603214b4e0b54bcbdfb500882705e59f316639585b4906314ed7e789d1` |
| `EndfieldJiggleEFMI-v0.2.3-standalone-win64.zip` | `02a28a7c007114381bcc97257d5fdf1f9de189bbf9abe0756393c75ada2685ea` |

The normal standalone includes the validated manual-official-launcher,
limited-rights process-query and cache-path preflight revisions. No new
diagnostic package was built or published. The original v0.2.2 three-package
hashes were rechecked after publication and remain unchanged.

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
immediate task to the pure Mod package. After accepting receiver r5, the user
authorized synchronizing the other package types and replacing v0.2.3.

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
