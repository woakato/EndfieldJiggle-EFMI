# Package Identity and Maintenance Status

## v0.2.7 Stable, Package Revision 3

On November 2, 2026 the user confirmed the reported issues resolved and accepted
revision 2 as stable. Package revision 3 consolidates documentation into one
README.txt and a Licenses directory in each installation archive. It retains
all fourteen executable rendering files and both InputGuard binaries; the
fifteenth historical runtime entry was the identical GPL text, now relocated
to Licenses/GPL-3.0.txt. Developer documents remain in the corresponding source
archive. The configurator's embedded installer uses the same layout.
Older sections below retain their original publication snapshots.

Local copies of the five verified publication assets are collected in
[`repo/reports/releases/v0.2.7`](../reports/releases/v0.2.7). The local
[`repo/reports/releases/current.json`](../reports/releases/current.json) points to package revision 3.

## v0.2.7 Directory Repair, Revision 2

The November 2, 2026 repair adds complete-payload discovery and a configurator
repair transaction for enabled residual INIs. All fifteen rendering runtime files
and the Alt input module remain byte-identical to the first v0.2.7 prerelease.
See [directory repair instructions](DIRECTORY-REPAIR-zh-CN.md). Isolated loader
validation and in-game dragging are tracked separately; no new game acceptance
is claimed. Older publication sections below describe their original snapshots.

## Local Candidate: v0.2.6

The November 1, 2026 local build extends v0.2.5 with folder-independent discovery
throughout EFMI Mods, nested in-place upgrade/restore, and correct outfit root
selection even under a category called Mods. It retains all fifteen accepted
v0.2.4 runtime files. Build output: `repo/reports/releases/v0.2.6-folder-layout`;
validation: `repo/reports/runtime-location-v026`. Unpublished; in-game behavior
still needs acceptance. See [v0.2.6 notes](RELEASE-v0.2.6-zh-CN.md).

## Previous Local Candidate: v0.2.5

The October 24, 2026 local build improves generic outfit inspection and adaptation
in the configurator. It retains all fifteen accepted v0.2.4 runtime files, adds no
outfit or operator allowlist, and does not substitute author textures.
Local builds and validation live in `repo/reports/releases/v0.2.5-outfit-compatibility-r2`
and `repo/reports/outfit-compatibility-v025`. This candidate is not published and
has no new in-game acceptance. The stable publication record below is unchanged.

## Current Release: v0.2.4

The current stable release is v0.2.4 (October 17, 2026), with unified Alt-plus-mouse
input, default-on startup and Ctrl+Shift+F9 toggling. All three installation
packages and the embedded installer are synchronized. See
[v0.2.4 publication identities and validation](V024-PUBLICATION.md).
The v0.2.3 records below are preserved history.

## Current Published Packages

Publication snapshot: October 17, 2026, 00:02:44 +08:00.
The accepted native/outfit repair replaced the original v0.2.3 attachments.
Release id: `414970104`. Source/tag commit:
`537077c4b607f6577504dcb02852fa303bb129a6`.
Publication-ledger commit: `09c8f0a9674916e0adba62af02ac97527df87f10`.

The three complete archives are identical to the delivered packages:

| Package | Purpose | SHA-256 |
| --- | --- | --- |
| `EndfieldJiggleEFMI-v0.2.3-win64.zip` | Pure Mod for an existing EFMI installation | `B084C2D92FB93D6EE2206AB7EC04C4893D577F794EA2A52EEEA06EFF3DDCBE79` |
| `EndfieldJiggleEFMI-v0.2.3-drop-in-win64.zip` | Mod plus optional configurator | `BD179CFC665BE785BD61909AB537E76A7A1CA4FA0F722561D0CC42B016740ADC` |
| `EndfieldJiggleEFMI-v0.2.3-standalone-win64.zip` | Normal standalone, launcher, official EFMI/libraries and configurator | `ABAB99BB2F73A5B4A9DBB69B1BA1F9AA75E9416FF65B8E6828DFAE9ACB0435EC` |

Pure Mod/drop-in roots directly contain `shaders` and runtime INIs.
Standalone retains `EndfieldJiggle/EFMI/Mods/EndfieldJiggleEFMI`.
All three and the configurator's embedded installer share fifteen runtime files.
Keep `Outfits.ini` and `OutfitPreparation.ini` from the same package.
Existing `EJ-OUTFIT/1` wrappers need no re-adaptation for this repair.

## Runtime Identity

| File | SHA-256 |
| --- | --- |
| `Passes.ini` | `104CBCB3C2614C1219670C8D598C72A90DEAED756F235D42EB072D9929C509ED` |
| `Outfits.ini` | `DC5E5464F7C2F05960886AF9FDD099889443A59224DF34E40AC1956D698A40E3` |
| `OutfitPreparation.ini` | `D0EC67C88CE25120532D10C50232F5A6E11944745B4740B5B38712E5E8C094E1` |

Native rules retain thirteen exact matches before 28 structural surface and
14 geometry rules. Preserve lowercase `EJ_zzReceiver` ordering after the exact
rules. Pattern-only outfit preparation uses the early `AEndfieldJiggleEFMI`
namespace before recursive EFMI replacement draws.

## Local Locations

Paths below are relative to the parent local workspace unless marked checkout.

| Content | Location |
| --- | --- |
| Current Git checkout | `repo` |
| Current published archives and companion files | `releases/v0.2.3` |
| Publication receipt and old-asset backups | `reports/publication/v0.2.3/outfit-complete-20261016` |
| Original complete-package delivery receipt | `reports/shading-fixes/20261016-outfit-complete` |
| Complete flat Mod/drop-in build | Checkout `reports/releases/v0.2.3-outfit-complete-20261016-r1` |
| Complete standalone build | `reports/v023-outfit-complete` |
| Outfit receiver build/evidence | Checkout `reports/shading-maintenance/outfit-receiver-20261016-r1` |
| Original capture and GPU evidence | `reports/receiver-loader` and checkout `reports/shading-maintenance` |

The previous checkout location under `reports/publication/github-v022-maintenance-20261012/repo`
is a directory junction to `repo`. The publication's previous `assets` location
is a junction to `releases/v0.2.3`. Historical report paths remain valid.
Preserve the junction boundaries when recursively inventorying or archiving.

## Previous Versions And Diagnostics

The full chronological record, initial v0.2.3 hashes, all v0.2.2 maintenance
variants and earlier releases are preserved in [PACKAGE-HISTORY.md](PACKAGE-HISTORY.md).
Historical candidate sections describe their stage at that time.

The parent `releases/history/v0.2.2` contains the three original published ZIPs.
The publication replacement preserved all nine v0.2.2 asset IDs and digests.
The separate `standalone-diagnostic-win64.zip` investigates missing touch or
startup failures; preserve its identity, telemetry and collector separately.
Earlier native-only and skin-only v0.2.3 builds are superseded by the complete
outfit-repair packages above.

## Verification Record

All eight final GitHub asset digests matched local files. Native evidence:
790 static/GPU/loader and 431 numerical checks. Outfit evidence: 525 GPU/loader
checks and six synthetic replacement-range picks. Complete-package evidence:
73 configurator, 22 installation, 16 discovery, 60 launcher and 126
cross-package checks; standalone also passed 87 archive-file and four
source/cache timestamp-pair checks. See
[native validation](NATIVE-RECEIVER-VALIDATION.md) and
[outfit validation](OUTFIT-RECEIVER-VALIDATION.md).

The user accepted publication; no complete all-character/all-outfit visual
matrix was supplied. Local package organization did not deploy to a live game.
Recheck the remote before making a later claim about release status.
