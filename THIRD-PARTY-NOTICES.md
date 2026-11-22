# Third-Party Notices

EndfieldJiggle EFMI adapts JiggleForge's input controller, spring-motion solver
and deformation field. The selected upstream sources and GPL-3.0 license are
preserved in `third_party/JiggleForge`.

Upstream project: https://github.com/wlytsgd2/JiggleForge

The selected runtime files were preserved from the local JiggleForge v0.1.23
runtime during development. The separate application reference was pinned to
commit `9725cd510c4655da8d6dd7aa48e93dd7b1ffda6b`; it is not included in this
source snapshot. Do not infer that the selected runtime bytes were independently
verified against that application commit.

Upstream notices and branding restrictions are preserved. This project does
not ship the upstream application, logos, installer or game-specific ZZMI
shader replacements.

The source repository and XXMI drop-in packages do not redistribute injection
libraries. The optional standalone Release bundles unmodified official EFMI and
XXMI-Libs binaries under their own licenses; details are recorded below.
Windows SDK tools and the game are separately obtained dependencies.
The GPL license does not apply to game assets or material from other Mod authors.

The optional Alt input guard uses MinHook v1.3.4, upstream commit
`c3fcafdc10146beb5919319d0683e44e3c30d537`, under its BSD 2-clause license:
https://github.com/TsudaKageyu/minhook
The build fetches that exact source. Installation packages preserve its
upstream `LICENSE.txt` as `Licenses/MINHOOK-LICENSE.txt`; the separate developer
build retains it alongside the input guard.
The guard is a separate input module loaded through the existing official
InjectorLib export. It does not replace the injector or EFMI libraries.

The optional Windows configurator is a self-contained .NET 8 WPF application.
Its Release archive includes the .NET license and third-party notice files
corresponding to the SDK used for that build. It contains no game runtime or
captured game shader payload.

Installation packages collect notices in `Licenses` and provide one `README.txt`.
The byte-identical GPL texts for this project, JiggleForge and the input guard
share `Licenses/GPL-3.0.txt`. JiggleForge's copyright and author notices remain
in its shader source headers. Full upstream notices and branding restrictions
remain in the corresponding source ZIP under `third_party/JiggleForge`.
This is an independent community Mod, supplied without warranty; game assets,
third-party Mods, trademarks and upstream branding are not relicensed.
The repository's full disclaimer is available in the corresponding source ZIP.

The separate Windows Mod Release contains hash-specific rendering compatibility
rules and adapted shader payloads produced from local game shader captures.
Those Release assets are not part of the source tree and are not relicensed by
the GPL notices in this repository. Refer to the game publisher's terms and the
Release disclaimer before use or redistribution.

## Standalone Release

The 2026-11-22 v0.2.7c standalone bundles the unmodified official XXMI Launcher
Portable v2.5.0 (GPLv3). Corresponding source:
https://github.com/SpectrumQT/XXMI-Launcher/archive/4f5ef31cc237394568119f9feac2d99f44665466.zip
The following upstream components are also included:

- EFMI-Package v1.5.1, commit `45e0ca07af9db81b0907c537c413d281db4f235b`.
  License: GNU GPL v3. Full corresponding source:
  https://github.com/SpectrumQT/EFMI-Package/archive/45e0ca07af9db81b0907c537c413d281db4f235b.zip
- XXMI-Libs-Package v1.3.3, commit `8bd31f74727f033b3d9049509e19da3563c8b98c`.
  Includes `3dmloader.dll`, `d3d11.dll`, and `d3dcompiler_47.dll` from the official
  release ZIP, with no binary patches. GNU GPL v3 applies to the covered
  3Dmigoto/InjectorLib components, with additional component notices and Microsoft
  DirectX runtime terms. Full corresponding upstream source:
  https://github.com/SpectrumQT/XXMI-Libs-Package/archive/8bd31f74727f033b3d9049509e19da3563c8b98c.zip
- DirectXTK and PCRE2 notices are retained in the standalone package's `Licenses`
  directory, together with the upstream GPL license and `COPYING.txt`.
- The configurator bundles the .NET 8 runtime and preserves its license and
  third-party notices. The project source snapshot is included under `Sources`.

The standalone preserves EFMI's official `d3dx.ini`, Core resources, the XXMI
signature manifest and the three official DLLs. This package is independently
assembled by EndfieldJiggle and is not an official EFMI or XXMI Launcher distribution.
