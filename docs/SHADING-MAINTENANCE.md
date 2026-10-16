# Shading Maintenance Candidate

Follow-up, 2026-10-16: the current-game missing color program is now adapted
in `reports/shading-maintenance/skin-pass-20261016-193543-936`. See
[SKIN-PASS-CANDIDATE.md](SKIN-PASS-CANDIDATE.md) and the capture findings.
Its offline checks passed; in-game confirmation is pending. The sections
below describe the earlier numerical candidate and its limits.

User feedback on 2026-10-16: the first pure Mod candidate still produces black
surfaces after the user resolved duplicate loading. The numerical fixes below
did not resolve the reported in-game artifact. Keep this candidate as failed
rendering evidence, not an accepted repair. Continue with the pure Mod package
before rebuilding drop-in or normal standalone packages. Package identities
and the separate no-touch diagnostic purpose are recorded in
[PACKAGE-MAP.md](PACKAGE-MAP.md).

This is a local test candidate, not a new release or a confirmed fix for the
reported black-skin artifact. Do not replace the three published packages until
the affected scene has been retested in the current game version.

## Reproduced Defects

The previous `field_vs.hlsl` reconstructs the surface frame even with zero
displacement and outside the deformation support. This changes non-orthogonal
native tangents on otherwise untouched vertices.

It also adds millimeter-sized derivative samples to absolute world positions.
At large coordinates these samples lose precision. A D3D11 WARP stream-output
test reproduced a normal-vector difference of approximately 0.622 when moving
the same local fixture from the origin to camera coordinates (32768,32768,32768).
This is synthetic evidence, not a measured coordinate from the user's game.

The candidate evaluates the field and its derivatives in anchor-local space,
keeps the original normal and tangent for zero displacement and outside support,
and uses safe normalization for the normal-only helper tangent. Current and
previous positions use the same coordinate treatment with separate offsets.

## Package Scope

Only `Mods/EndfieldJiggleEFMI/Passes.ini` changes in the runtime. All twelve exact
shader patterns, input/output signatures, native material instructions, picking,
spring controls, key bindings, outfit bridge, and default-off setting remain.
The picker cache bytes and paired timestamps are preserved.

The same runtime is shared by the Mod-only, drop-in and standalone packages.
The candidate does not contain a launcher/configurator update, an injection DLL,
global ShaderFixes, new shader-family guesses, or a third-party outfit.
The builder verifies both the historical native evidence and its correspondence
to the exact published v0.2.2 shader replacements before producing a candidate.

## In-Game Comparison

Exit the game and back up the existing touch Mod outside every loaded Mods tree.
In an existing working EFMI installation, use just one copy of this candidate's
`Mods/EndfieldJiggleEFMI` directory. For the standalone package this is under its
own `EFMI/Mods`. Do not run an old configurator installer against this candidate:
its embedded payload would restore the old runtime. Existing user settings and
the configurator are not included; preserve them separately for rollback.

Restart the game and compare the same character, outfit, camera and lighting:

1. Touch disabled.
2. Touch enabled, with no drag.
3. A short drag and release on the affected surface.
4. Unaffected skin/clothing outside the touched region.

Confirm both that the black area is gone and that drag/rebound still work.
Also check any already-adapted outfit. If the artifact remains, record whether
it starts on enable, first click, displacement or release; capture matching
before/after images. The old Mod directory is the rollback. Do not mix packages
or clear global shader caches as part of this test.

## Offline Verification

Build `surface_frame_host` from `tests/CMakeLists.txt` into a new `reports` folder.
Run `tools/Test-FieldShading.ps1 -HostPath <host.exe>` for the four public HLSL
variants. It exercises zero/disabled controls, support boundaries, degenerate
tangents, large camera translations and current/previous displacement isolation.

`tools/Build-ShadingCandidate.ps1` additionally requires the unchanged local
native build report, the reviewed v0.2.2 Mod ZIP and the DXBC assembler.
Captured game programs and generated assembly remain under local reports.
Its `inGameVerified` and `publicationApproved` flags remain false.

`tools/Test-ShadingCandidate.ps1` verifies untouched payloads and binding sections,
creates all twelve patched programs on WARP, exercises the two native color
families with picking and spring input, and loads the actual candidate through
the official EFMI library. Supply the historical `TouchGpu.Native.cs` fixture,
native report, loader host, and an isolated official loader directory explicitly.
Use a short `-LoaderSandboxRoot` under `reports`; the legacy compiler can fail
inside deeply nested paths. These fixtures do not reproduce current game
materials, post-processing, shadows, or an actual third-party outfit.
