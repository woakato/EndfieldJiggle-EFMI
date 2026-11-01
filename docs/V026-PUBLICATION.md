# v0.2.6 Publication

Published November 2, 2026 at 00:35:03 +08:00 and verified as latest.
Release id: `430708097`. Source/tag commit:
`ef6967a379ff845d2d880bd26d9454afcc3cc0c9`.
All eight remote asset sizes and SHA-256 digests match the verified local
publication inputs. The v0.2.4 release body, attachment IDs/digests and source
tag are unchanged; all earlier releases remain available.

## Package Identities

| Package | SHA-256 |
| --- | --- |
| `EndfieldJiggleEFMI-v0.2.6-win64.zip` | `d928203b70aadb0d60746c450e1862768a7d066e33d2859c643d1f767806513b` |
| `EndfieldJiggleEFMI-v0.2.6-drop-in-win64.zip` | `a35a2f07e344b0182aaf6304cfa74db6529eeceec6e9815d55e3ff366bfb9a3f` |
| `EndfieldJiggleEFMI-v0.2.6-standalone-win64.zip` | `e3008448da46edae39f4ea6220e5d55b48f27486570299910c88c370f24942cd` |
| `EndfieldJiggleEFMI-v0.2.6-source.zip` | `7a8e0e403e56945ef205969e518cdf8af4d603acb60709f2eb6984ef006c3764` |

Both executables report `0.2.6.0` and the source commit above. All 159 source
files come from that commit; the public source attachment is byte-identical
to the standalone's embedded source and an independent export. The exported
source passed 65 public source checks. The final builds passed 482 package
checks, 60 launcher checks, 87 archive-file checks and four source/cache
timestamp-pair checks, in addition to the runtime checks below.

## Scope And Validation

Release date: November 2, 2026. The three installation forms are pure Mod,
drop-in with configurator, and standalone with official EFMI/libraries.
The release also supplies the matching source archive, two installation
guides, release notes and a SHA-256 checksum file.

The source tag is `v0.2.6`. The standalone's embedded source and the public
source attachment are exported from the same source commit. Publication
identities are recorded on main after upload verification; the source tag
continues to identify the build inputs.

All fifteen runtime files are retained from accepted v0.2.4, including its
Alt-plus-mouse input, original solver and picker caches. New behavior is in
the configurator: generic outfit inspection and folder-independent discovery,
nested in-place upgrade and restore.

Validation includes 80 configurator, 22 installation, 73 discovery/workflow
and 60 adapter checks, ten UI renders, six actual EFMI directory layouts and
291 nested outfit GPU assertions. Two real outfits passed 18 isolated
adaptation checks without new loader diagnostics; pre-existing missing
author textures and 23 diagnostics were retained. Gilberta's current source
was already adapted, so its original INI was reconstructed only in an
isolated fixture using the verified backup hashes.

The package build also checks standalone launch behavior, archive contents,
cross-package runtime identities and source/cache timestamps. The new
directory and adaptation workflows have no new in-game acceptance.
Publication does not modify the user's live EFMI, game or outfits.
Previous releases and diagnostic packages are retained.

See [release notes](RELEASE-v0.2.6-zh-CN.md),
[installation](INSTALL-zh-CN.md) and [package map](PACKAGE-MAP.md).
