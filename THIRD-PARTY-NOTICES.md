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

The optional Windows configurator is a self-contained .NET 8 WPF application.
Its Release archive includes the .NET license and third-party notice files
corresponding to the SDK used for that build. It contains no game runtime or
captured game shader payload.

The separate Windows Mod Release contains hash-specific rendering compatibility
rules and adapted shader payloads produced from local game shader captures.
Those Release assets are not part of the source tree and are not relicensed by
the GPL notices in this repository. Refer to the game publisher's terms and the
Release disclaimer before use or redistribution.

## Standalone Release

The standalone package adds this project's launcher and bundles these pinned
upstream releases, without distributing XXMI Launcher's application or branding:

- EFMI-Package v1.4.8, commit `3d40c54ba86fdfd0e26d8ccbbd3848632f063b06`.
  License: GNU GPL v3. Full corresponding source:
  https://github.com/SpectrumQT/EFMI-Package/archive/3d40c54ba86fdfd0e26d8ccbbd3848632f063b06.zip
- XXMI-Libs-Package v1.2.2, commit `a7847eae82aaac32175c9a7d199673d5c9323bcd`.
  Includes `3dmloader.dll`, `d3d11.dll`, and `d3dcompiler_47.dll` from the official
  release ZIP, with no binary patches. GNU GPL v3 applies to the covered
  3Dmigoto/InjectorLib components, with additional component notices and Microsoft
  DirectX runtime terms. Full corresponding upstream source:
  https://github.com/SpectrumQT/XXMI-Libs-Package/archive/a7847eae82aaac32175c9a7d199673d5c9323bcd.zip
- DirectXTK and PCRE2 notices are retained in the standalone package's `Licenses`
  directory, together with the upstream GPL license and `COPYING.txt`.
- Both desktop programs bundle the .NET 8 runtime and preserve its license and
  third-party notices. The project source snapshot is included under `Sources`.

Only the standalone package's `d3dx.ini` is configured for its own launcher and
default non-hunting mode. EFMI's Core resources and the three official DLLs remain
unchanged. This package is independently assembled by EndfieldJiggle and is not an
official EFMI or XXMI Launcher distribution.
