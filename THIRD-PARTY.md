# Third-party components

This release bundles the following third-party code. Their licences apply to those parts,
not to the rest of this repository.

| Component | Where | Licence | Notice shipped |
|---|---|---|---|
| **nlohmann/json** 3.12.0 | `src/costume-dll/json.hpp` | MIT | yes — SPDX headers in the file |
| **MinHook** | `src/costume-dll/minhook/` | BSD-2-Clause (upstream) | ⚠ **no notice in the vendored copy** |
| **libsquish** (Simon Brown) | `src/lib/DDSLib/Compression/` | MIT-style | yes — `Compression/License.txt` |
| **DDS plugin for Paint.NET** (Dean Ashton) | `src/lib/DDSLib/` | MIT-style | yes — `License.txt` |
| **UpkManager** (Unreal-Library derived) | `src/lib/UpkManager/` | see below | ⚠ **partial** |
| **LZO** (Markus F.X.J. Oberhumer) | `src/lib/UpkManager/Compression/lib64/lzo2_64.dll` | **GPL-2.0-or-later** | yes — `LICENSE.lzo.txt` |

NuGet packages are restored at build time and are not vendored here: ImageSharp 2.1.11
(Apache-2.0 — pinned deliberately, 3.x moved to the Six Labors Split Licence and 4.x
enforces it at build time), BCnEncoder.Net, MessagePack, Microsoft.Data.Sqlite, Pfim, and
the Windows App SDK.

`MHServerEmu/` is a modified copy of [MHServerEmu](https://github.com/Crypto137/MHServerEmu)
and is **AGPL-3.0**. It keeps its own `LICENSE`, and `docs/SERVER-CHANGES.md` is the
statement of modifications that licence requires.

---

## ⚠ Two things to settle before this is redistributed

**1. `lzo2_64.dll` is GPL-2.0, and it is not optional.**
It is P/Invoked by `src/lib/UpkManager/Compression/Lzo2.cs`, `src/lib/MHTexLib/Lzo.cs` and
`src/lib/MHTexLib/LzoHelper.cs` — every UPK read decompresses through it, so the Costume
Manager does not function without it.

This project's own code is **source-available, not redistributable** (see `LICENSE`), which
means the question the GPL would otherwise force — "under what terms do you distribute the
combined work?" — does not arise while nothing is redistributed. It *does* arise the moment a
built binary is published. Before doing that, pick one: relicense this project's code
GPL-compatible, replace LZO with a permissively licensed decompressor, or take the commercial
LZO licence its author offers.

**2. `MinHook` and `UpkManager` are vendored without a full notice.**
MinHook is BSD-2-Clause upstream and its two-clause text should sit beside the vendored
`MinHook.h` and `src/lib/`. UpkManager derives from Eliot van Uytfanghe's Unreal-Library
(referenced in comments in `Models/UpkFile/Core/UClass.cs` and
`Models/UpkFile/Objects/UnrealObjectBase.cs`) and carries only the LZO notice today.
Add the upstream licence text for both before publishing.
