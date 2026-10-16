# Outfit Receiver Candidate Validation

Date: October 16, 2026. This is a local pure Mod candidate, not an uploaded
v0.2.3 replacement or a visual acceptance record for the user's outfit.

Subsequent packaging: the user requested all three complete bundles.
The rebuilt configurator and installer now support the fifteen-file runtime.
Their complete-package validation and identities are in PACKAGE-MAP.md;
the original candidate evidence below remains unchanged.

## Reproduction

The installed Passes.ini matched accepted native receiver r5. The installed
Outfits.ini still gated replacement draws on twelve exact shader callbacks.
An isolated official-loader test exercised all 33 supported native programs.
Its replacement draw called the existing EJ-OUTFIT/1 Begin/End API and read
the actual bound VS t119 data back through a compute probe.

The twelve older programs received `(0.05, 0, 0, 1)` under synthetic active
touch. The other 21, including the newly adapted skin-color program, received
zero control. This reproduces inconsistent deformation between replacement
draw paths. It does not by itself identify every material involved in the
user's reported black pixels.

Baseline evidence, relative to the parent project:
`reports/receiver-loader/outfit-gpu-baseline-20261016/probe-report.json`.

## Change

Build: `reports/shading-maintenance/outfit-receiver-20261016-r1`.

- Added fourteen pattern-only preparation rules in OutfitPreparation.ini.
  They reuse the reviewed geometry-recognition patterns and execute before
  EFMI's replacement draws, using the deliberately early AEndfieldJiggleEFMI
  namespace. They do not patch shaders or assign filter_index.
- The Begin callback uses the prepared native instance slot, so it is no
  longer restricted to twelve exact programs. Its existing bounds, default
  off state, completion/cancellation checks and foreign resource restoration
  remain in force.
- Added the new exact skin-color callback to the existing compatible picker
  with the outfit's explicit index range.
- Preserved the EJ-OUTFIT/1 Begin/End interface. Existing adapted INIs do not
  need rewrapping for this change.

Only Outfits.ini changes and OutfitPreparation.ini is added. Passes.ini and
every shader/source-cache pair remain byte- and timestamp-identical to r5.
The candidate has fifteen runtime files. The old configurator's fourteen-file
package contract is not yet updated; use this candidate with existing settings.

## Offline Evidence

Final result: 525 checks passed (291 active and 39 in each of six negative
scenarios), plus both raster runs and all six replacement-range pick results.

The active test checks all 33 native programs, three unseen-layout variants,
and an unsupported vertex program. All supported replacement paths now read
the same nonzero control. The short-camera negative draw reads zero. Original
author filter values survive, and foreign t119 resources are restored.
An unknown program after the supported ones retains its original sentinel
data and does not inherit a picker or prepared instance slot.

Additional scenarios cover disabled touch, cancellation, incomplete sessions,
disabled outfits, invalid ranges and invalid instance counts. These checks
exercise actual bound GPU data, not only command counters.

Two isolated raster runs cover the old color pair and the new skin-color
program, including mouse/drag overlap. Each run confirms three pick results
at the replacement triangle's world Z 345.2, excluding the caller's other
triangle, and checks explicit index ranges, author filter values and absence
recovery.

Final test records live under the parent project's
`reports/receiver-loader/outfit-final-*-20261016/test-report.json` and
`outfit-raster-{legacy,skin}-20261016/raster-report.json`.

These tests do not render the user's complete outfit/material/pixel pipeline.
Current-game visual confirmation is still needed. Completely different native
contracts and custom vertex shaders remain outside this candidate.

No game process was driven, no live Mod was written, and no release was
overwritten during this investigation.
