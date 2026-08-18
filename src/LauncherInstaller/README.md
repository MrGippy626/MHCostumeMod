# MHLauncher (WinUI 3) - the PLAYER-side tool

Two windows, one app. **LauncherWindow** is the front door (play, settings, server address);
**MainWindow** is the costume installer, opened from its **Tools** button.

Merging them is what makes the settings honest: the installer needs the game FOLDER while the
launcher needs the game EXECUTABLE and the DLL. Two apps would ask for the same install twice and
be able to disagree about it. Design notes: `docs/launcher-design.md`.

The assembly is now **MHLauncher.exe** - it was `MHLauncherInstaller.exe`.

## The launcher half

Replaces `src/native/launcher/launcher.cpp`, which does the right thing with **four hardcoded
absolute paths compiled in**. Everything it hardcoded is now editable, which is the entire point:
a server owner hands this to a friend, who sets the server address once and presses Play.

The four settings live in `launcher.json` beside the exe. Defaults are DERIVED, not blank - the
game comes from `GamePaths.AutoDetect()` (the same one the installer uses, so both agree about
which install they mean) and the DLL from beside the launcher.

### The rules that matter

**The suspended start is load-bearing, not tidiness.**
`CreateProcess(CREATE_SUSPENDED)` -> `VirtualAllocEx` -> `WriteProcessMemory` ->
`CreateRemoteThread(LoadLibraryW)` -> `ResumeThread`, ported verbatim from `launcher.cpp` because
that sequence is what is proven against this client. The DLL must be in before the entry point:
`DllMain` reads `GetCommandLineW()` for `-siteconfigurl=`, and the package preload pass the hooks
need is over by the time a late injector lands. **A late injection produces a client that runs,
logs, and never swaps a mesh.**

**A game that starts WITHOUT the mod looks exactly like success.** Two guards exist for that one
failure:

- `LauncherCore.Is64Bit` reads the DLL's PE header up front. A 32-bit DLL injects
  "successfully" - `VirtualAllocEx` and `WriteProcessMemory` both return true - and then
  `LoadLibraryW` returns NULL *inside the target* with no message anywhere.
- Every failure after `CreateProcess` **terminates** the suspended process. Leaving it suspended
  leaves an invisible process holding the game's files open, and the next launch then fails for a
  completely unrelated-looking reason.

**Background image:** drop `background.png` beside the exe and the launcher uses it. Absent is the
normal case and falls back to a designed gradient, so there is no broken-image state and a server
owner can brand their own copy without rebuilding anything.

**Status text is not decoration.** It says whether the mod went in, because that is the one thing
the window cannot show any other way.

---

Not the Costume Manager. This imports a `.mhcostume` exactly as its author built it, removes it
again, and health-checks the install. Players do not host the server, so every part of the
Manager that renames UPKs, detects donors, **allocates enums**, writes `ServerCostumes.json` or
queues purges assumes you own the server and must never appear here.

## ⛔ Build with Visual Studio MSBuild, not `dotnet build`

```
"C:\Program Files\Microsoft Visual Studio\2022\Community\MSBuild\Current\Bin\MSBuild.exe" ^
    src\winui\LauncherInstaller\LauncherInstaller.csproj ^
    -p:Configuration=Release -p:Platform=x64 -restore
```

`dotnet build` fails with `MSB4062 ... ExpandPriContent could not be loaded` — that task ships
with Visual Studio's UWP/MSIX tooling and `dotnet build` resolves it under the .NET SDK, where it
will never be. **Do not report this project as broken on that basis.**

## ⛔ Zero ProjectReference, and that is the defining constraint

It **links** the dependency-light sources (`CostumeShare.Core.cs`, `TfcAlias.Core.cs`,
`InstallLedger.cs`, `IconPack.Roles.cs`, `FxPackRegistry.cs`) rather than referencing
`MHCostume.Core`, which would drag in UpkManager, DDSLib, MHTexLib, ImageSharp and Pfim.

That is only possible because **a player never changes an enum**. The server owns the costume
enum and the hotspot enums and sends both on the wire, so the author's prebuilt icon package and
renamed UPKs are already correct byte-for-byte and nothing needs rebuilding.

⚑ If something added here needs an image or UPK library, that is the signal it was put in the
wrong file — **split the file, do not add the reference.** `IconPack.Roles.cs` exists for exactly
this reason.

## Publish: self-contained, and the opposite of the Manager

| | Manager | Installer |
|---|---|---|
| audience | the server owner | **players** |
| `SelfContained` | `false` (101 MB, needs .NET 8 Desktop Runtime) | **`true`** |
| `WindowsAppSDKSelfContained` | — | **`true`** |

The WPF installer this replaces was self-contained single-file with the comment *"Players should
not have to install a .NET runtime."* Requiring a player to install two runtimes before they can
install a costume is friction the WPF version never had.

⛔ **The values live in `Properties\PublishProfiles\win-x64.pubxml`, not the csproj.**
`<PublishProfile>` imports the profile and its values **override** the project properties, so
setting `SelfContained` in the csproj alone silently does nothing.

### The prune, measured

The `Microsoft.WindowsAppSDK` **metapackage is deliberately not referenced.** It pulls ten
sub-packages; the dependency graph in `obj\project.assets.json` shows WinUI needs only three of
them, all transitive. `AI`, `ML`, `Search` and `Widgets` are referenced by nothing here:

| | files | size |
|---|---|---|
| metapackage | 511 | **263.0 MB** |
| WinUI + Runtime + DWrite only | 447 | **209.1 MB** |

`onnxruntime.dll` (20.7 MB) and `DirectML.dll` (17.8 MB) are gone. A costume installer does not
do machine learning.

DWrite is **kept** although nothing references it — `Microsoft.ui.xaml.dll` loads `DWriteCore`
for text, and 3 MB is not worth finding that out the hard way.

⚠ **VERIFY BY RUNNING, NOT BY BUILDING.** A missing WindowsAppSDK component does not fail the
build — it takes the process down with `0xC000027B`, no managed stack, before any window appears.
```powershell
$p = Start-Process .\MHLauncherInstaller.exe -PassThru; Start-Sleep 8; $p.HasExited
```
(Bash exit codes lie for GUI apps — `timeout 12 ./app.exe` once returned 127 for an exe that
exists.)

⚠ 209 MB is the floor without trimming. `Microsoft.Windows.SDK.NET.dll` alone is 54 MB and only
shrinks under `PublishTrimmed`, which is **off on purpose**: every config, ledger and
pack-manifest path here is reflection-based `System.Text.Json`, and a trimmer's failure is a
silent runtime round-trip loss in Release only, not a build error.
