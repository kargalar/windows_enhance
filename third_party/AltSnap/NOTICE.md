# AltSnap 1.68

Unmodified upstream x64 portable engine, by Raymond Gillibert and contributors,
derived from AltDrag by Stefan Sundin. Runs as a separate process; no upstream
source is compiled into WinForge.

Project: https://github.com/RamonUnch/AltSnap
Release: https://github.com/RamonUnch/AltSnap/releases/tag/1.68
License: GNU GPL v3; full text is License.txt inside the binary archive and is
extracted alongside the executable. The complete corresponding upstream source,
including build files, is provided in AltSnap1.68src.zip and is also extracted
as an archive alongside the runtime. No changes were made to either archive.

Official release assets and SHA-256 (also published by GitHub release API):

| Asset | SHA-256 |
| --- | --- |
| AltSnap1.68bin_x64.zip | 7db3dad3746e7b23857db92fc05ee2d3e17eee1b28e237709a612366ba909c79 |
| AltSnap1.68src.zip | 0881ccc46bfdfc244bf82e5c0de5b22bdc631a00b98674ca784794728314ab8f |

Download URLs:
- https://github.com/RamonUnch/AltSnap/releases/download/1.68/AltSnap1.68bin_x64.zip
- https://github.com/RamonUnch/AltSnap/releases/download/1.68/AltSnap1.68src.zip

Both archives are embedded in WinForge.Modules.AltSnap.dll so builds and first
activation work offline. AltSnapRuntime verifies both hashes before extraction.
The runtime and its own portable INI live under
%LOCALAPPDATA%\WinForge\AltSnap\1.68. Existing standalone AltSnap installations
and their configuration files are never used or modified.
