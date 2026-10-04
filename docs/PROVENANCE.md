# Provenance And Verification

Source publication: October 4, 2026, Asia/Shanghai.

## Included Material

- Selected JiggleForge input, motion and deformation source files, with the
  upstream GPL license, third-party notices and branding notice.
- Endfield integration HLSL and universal session template.
- Original adapter-generation and source-verification tool code.
- Windows native test-host source and its CMake build definition.

Adapter tools transform a locally supplied native shader. They do not contain
the full captured original or patched game shader programs. Interface anchors
and resource bindings in tool code describe interoperability requirements.
The generated complete ShaderRegex package and native-picker caches stay local.

## Source ZIP Exclusions

Captured/disassembled game shaders, generated full game-derived shader
replacements and caches, frame captures, models, textures, game/injection DLLs,
local reports, account information, machine paths, installation journals,
obsolete character registries and the paused desktop prototype are excluded.

The `v0.1.0-source` public source ZIP is not an installable package or a complete
self-contained rebuild of the locally tested runtime.

## Windows Mod Release

The separate Windows x64 Release ZIP contains the thirteen owned Mod files from
the locally installed, verified universal package, plus an installation guide.
Its `Passes.ini`, native picker adapters and cache files include compatibility
material generated from local captures of original game shader interfaces.
The archive does not contain the raw capture archive, full original-shader
export set, game installation, injected loader DLL, other Mods or installation
logs. The project source license does not relicense game-derived compatibility
material.

The distributable was round-trip checked against the active local install:
thirteen module file hashes and all four exact HLSL/cache timestamp pairs
survived ZIP extraction. See the versioned Release notes and SHA256 attachment.

## Local Verification Summary

The complete local universal build passed 62 synthetic WARP GPU checks,
24 native draw scenarios, 130 captured native draw rows and an isolated actual
EFMI loader test. The installed-file loader copy produced 12 shader matches,
four cache loads, 12 foreign binding restores and zero diagnostics.

These local results are summarized, not reproduced by downloading this source
snapshot. GPU fixtures used synthetic vertices; captured-buffer protocol tests
did not rasterize captured character vertex buffers.

The user reported successful in-game migration on October 4, 2026 after the
local universal package was installed. No operator names, individual surface
results or automated rendered proof were supplied. F10 reload, world scenes,
all-operator coverage and replacement-outfit compatibility are not established.

## References

- JiggleForge: https://github.com/wlytsgd2/JiggleForge
- XXMI Launcher: https://github.com/SpectrumQT/XXMI-Launcher
- EFMI Package: https://github.com/SpectrumQT/EFMI-Package
- Loader source consulted: https://github.com/SpectrumQT/XXMI-Libs-Package/tree/v1.1.7

This project is independent and does not relicense these dependencies.
