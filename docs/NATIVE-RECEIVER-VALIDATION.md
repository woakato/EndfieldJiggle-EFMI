# Structural Native Receiver Candidate

Date: October 16, 2026. The offline record below describes the delivered
candidate. The user subsequently reported "should be working" and explicitly
authorized replacing v0.2.3 with this runtime. That feedback is acceptance for
release, not a complete all-character visual test matrix.

## Problem And Change

The user reports that v0.2.3 improves Tangtang but other original characters
still develop black regions during touch. The preceding repair added one exact
skin-color program to twelve existing programs. It did not solve interface
coverage generally.

The new pure Mod changes only `Passes.ini`. It retains all thirteen exact
rules and their picking/outfit callbacks, then adds 28 structural receiver
templates and 14 geometry fallback templates. They recognize both native
camera-relative world transforms, retaining intervening skinning and material
instructions as captured text. Register allocation is matched symbolically.
No operator names, mesh hashes or index-count lists select the runtime effect.

Known interfaces update their normal/tangent outputs as well. New varying
layouts use geometry-only reception, retaining their material outputs.
Conditional material tails also use geometry-only reception, so output writes
are not moved outside their native branches. The existing common color picker
still establishes the touch session. Completely different position/skinning
contracts are outside these rules.

Rule ordering matters: lowercase `EJ_zzReceiver` follows all hexadecimal exact
rule names in the actual loader. Earlier uppercase-Z development builds could
intercept a legacy color program before its picking callback. They are retained
as failed/intermediate evidence, not delivery artifacts.

## Final Artifact

Build: `reports/shading-maintenance/receiver-20261016-r5`.

Delivery relative to the parent project:
`reports/shading-fixes/20261016-native-receiver`.

- ZIP: `EndfieldJiggleEFMI-v0.2.3-native-receiver-candidate-win64.zip`
- ZIP SHA-256: `354FAD670117B4A58B8E08F4B8FFE2AABE70CE2B51B951F5DF6B6F703AB18BC1`
- `Passes.ini` SHA-256: `104CBCB3C2614C1219670C8D598C72A90DEAED756F235D42EB072D9929C509ED`

The ZIP is flat. The expanded delivery directory directly contains `shaders`
and the three runtime INIs. Other runtime bytes and timestamps are unchanged,
including the four native source/cache pairs. Captured sources and fixture
binaries remain outside the installable package.

## Verification

Final static/GPU/official-loader evidence:
`receiver-20261016-r5/verification-20261016-221953-034/test-report.json`.

790 checks passed. Coverage includes 33 captured native programs, 20 beyond
the preceding thirteen-program runtime, and three synthetic variants with new
register assignments and output layouts. All three loader modes passed without
warnings: default, active picking and unseen layout. Each program receives one
patch; previous picking rules retain priority; foreign t119 bindings are
restored. Unrelated cached programs and incorrect camera/bone contracts are
rejected.

The final r5 change only narrows surface-rule matching for conditional tails.
All 36 original/patched binary pairs are byte-identical to r4. The delivery
receipt verifies those hashes and binds the following r4 numerical evidence
to the delivered implementation:

- `geometry-20261016-221235-899/test-report.json`: 108 checks across all 33
  original programs and the three unseen-layout variants. Zero control
  preserves all native output bits; projection deformation is nonzero;
  active control introduces no new nonfinite output.
- `capture-replay-20261016-221236-106/test-report.json`: Tangtang, 124 checks.
  Six draws include the previously unadapted `1a35d3c7704c57ff` program.
- `capture-replay-20261016-221236-639/test-report.json`: Chen Qianyu, 66 checks.
- `capture-replay-20261016-221247-789/test-report.json`: Eldelara, 66 checks.
- `capture-replay-20261016-221252-382/test-report.json`: Perlica, 67 checks.

The four capture sets total 323 checks. For each selected mesh, three synthetic
touch anchors produce zero world-position difference between its selected
color, prepass and outline draws. The three older-character captures predate
the current game update; they are regression data, not fresh reproduction of
the user's newly reported characters.

The stream-output host was corrected to address packed semantics by their
semantic-relative component index. The previous host incorrectly used the
signature register's zw lane offset and rejected a valid native shader.

These tests replay vertex/constant/bone buffers. They do not reproduce the full
pixel pipeline, and native vertex texture resources are not supplied by the
replay host. Zero-control comparisons use identical resource bindings on both
sides. The subsequent user feedback accepts the candidate; specific characters
and scenes were not enumerated in that feedback.

No live EFMI/Mod/game deployment or GitHub publication was performed during
candidate validation. See PACKAGE-MAP.md for the subsequent release status.
