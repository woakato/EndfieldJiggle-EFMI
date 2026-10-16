# Original Skin Pass Candidate

Local pure Mod candidate, 2026-10-16. In-game confirmation is pending.
This is separate from the standalone no-touch diagnostic package.

The capture shows original skin color VS `5869b530fc5ca4cf` was not adapted,
while its prepass and outline moved with touch. This candidate adds an exact
rule for that program, preserving its native material instructions and
reusing the equivalent native picker. Existing rules and settings are retained.

## Install

Exit the game. In the active `Mods/EndfieldJiggleEFMI` directory, replace only
`Passes.ini` with the file from this package. Back up the old file outside
the loaded Mods tree first. All other runtime files are unchanged from the
first shading candidate. Keep your current settings and only one loaded
touch Mod.

For a fresh installation into an existing EFMI setup, the package's
`EndfieldJiggleEFMI` folder goes directly inside `Mods`. It contains the
complete pure Mod, without a launcher or configurator.

Restart and open the original Tangtang skin. Compare touch disabled, enabled
without dragging, dragging the previously black area, and release/rebound.
Confirm both normal appearance and working touch. Retest existing outfit
adapters separately. Restoring the previous `Passes.ini` rolls back this change.

Offline shader, captured-geometry and loader checks do not establish the
final game's appearance. No release upload or live Mod deployment is included.
