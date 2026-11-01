# v0.2.6 Publication

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
