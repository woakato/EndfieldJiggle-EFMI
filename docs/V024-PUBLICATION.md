# v0.2.4 Publication

Published on October 17, 2026 as a new stable release, preserving v0.2.3 and
all earlier release bodies, attachments and the v0.2.3 tag.
Release id: `415649643`. Source/tag commit:
`32c6f01fe452889604f51a5d8a24b2725dddf8f6`.

## Package Identities

| Package | SHA-256 |
| --- | --- |
| `EndfieldJiggleEFMI-v0.2.4-win64.zip` | `1ba2bd66ea1c0b8cd9b89f9b89be561f5d5ab1fe8725bf6a2f0840c93e2bff8c` |
| `EndfieldJiggleEFMI-v0.2.4-drop-in-win64.zip` | `e713eed2689a88d7e53d7400c804b108861fc91fd6aefc307dee9a2cd83881bc` |
| `EndfieldJiggleEFMI-v0.2.4-standalone-win64.zip` | `748fc7f47794b1898f31d335d88dcbc86d7e5a50a2100f7b7e7c20b8ab2cce2c` |

The source archive has 149 files. Every file agrees with the standalone's
embedded source snapshot. Both executables report version `0.2.4.0` and the
source commit above. GitHub attachment sizes and SHA-256 digests were checked
for all eight assets; the companion checksum file covers the other seven.

The three packages and the embedded installer contain the same fifteen runtime
files, matching the user's accepted installed build. Only `EndfieldJiggle.ini`
changes relative to the accepted v0.2.3 rendering runtime. Its hash is
`73AC986348FAF32A3A89DC8E3E5C3D559CA9F136E146619E5049F1B168398BF2`.
Native/outfit rules, both native pickers, the pixel picker, original press-only
solver and their source/cache pairs are retained.

## Behavior And Validation

Touch starts enabled without a startup banner. Hold Alt plus left mouse to drag,
release the mouse button to let go, and click again to select another point.
Ctrl+Shift+F9 toggles touch, F7 forces it off, and F8 toggles diagnostics, all
with Ctrl+Shift. The configurator can disable startup touch.

The input update passed 5,760 synthetic GPU assertions before the user confirmed
the installed correction works and requested publication. Final packaging passed
80 configurator, 22 installation/upgrade/restore, 16 discovery and 60 launcher
checks, plus outfit-adapter and key-conflict tests. Configurator smoke covered
ten desktop/compact renders and the actual embedded installation workflow.
Cross-package checks covered 45 runtime identities, twelve source/cache pairs,
all 26 shared drop-in/standalone files, 87 standalone archive checksums and all
149 embedded/public source files. Exported source passed 63 independent checks,
including Windows line endings.

This feedback is not an all-character, all-part, all-outfit or combat-state
visual matrix. No game or live installation was changed by publication.
Installer upgrades retain old custom keys and startup choices; select the new
defaults in the configurator when that is desired.

## Local Evidence

Paths below are relative to the parent workspace:

- Published attachments: `releases/v0.2.4`.
- Publication receipt:
  `repo/reports/publication/v0.2.4/unified-alt-20261017/publication-receipt.json`.
- Flat builds:
  `repo/reports/releases/v0.2.4-unified-alt-20261017-final-publish`.
- Standalone build: `reports/v024-alt-final-publish`.
- Accepted input build: `repo/reports/world-touch/alt-mouse-20261017-r1`.

See [release notes](RELEASE-v0.2.4-zh-CN.md),
[input details](UNIFIED-ALT-INPUT.md) and [installation](INSTALL-zh-CN.md).
