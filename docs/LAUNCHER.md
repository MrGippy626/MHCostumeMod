# Launcher / Installer

`src/LauncherInstaller/` — ships as **`MHLauncher.exe`**. This is what a player runs.

Two jobs in one window: install content someone sent you, and start the game with the mod
loaded.

---

## Why it is separate from the Costume Manager

Everything in the Manager that renames packages, detects donors, allocates slot numbers, writes
the server config or queues purges assumes **you own the server**. A player owns none of that.

So the launcher imports a pack exactly as its author built it, removes it again, and checks the
install is healthy. It deliberately cannot do anything that would require agreement with a
server it does not control:

**⛔ It never changes a slot number.** The server sends the slot on the wire, so one the
launcher picked would not resolve. If a different costume already holds an imported pack's slot,
that means the server reassigned it — which makes the local one already dead — so the launcher
removes it, takes the slot, and says so.

## Zero project references

`LauncherInstaller.csproj` has **no `ProjectReference`** and no NuGet package beyond the Windows
App SDK. It reaches the shared code by **linking source files** from `src/core/`:

```
src/core/CostumeShare.Core.cs   config, paths, backups, the player install path
src/core/FxPackFile.cs          .mhfxpack
src/core/FxPackInstall.cs       installing one
src/core/BulkPack.cs            .mhbundle
src/core/LauncherCore.cs        settings, servers, injection, starting the game
src/core/CatalogCompare.cs      the content check
src/core/InstallLedger.cs       what is installed
src/core/TfcAlias.Core.cs       texture manifest rows
src/core/IconPack.Roles.cs      icon naming
src/core/FxPackRegistry.cs      shared effect packs
```

That is a constraint, not an accident. Those files are pure .NET with no image, UPK or database
dependency, so the launcher is a small assembly a player downloads and runs. **If a change here
seems to need `UpkManager` or `MHTexLib`, something has been put in the wrong file** — move it
rather than adding the reference. Importing a pack is copying files and writing config; it never
needs to parse a package.

## Starting the game

`LauncherCore` does the injection itself, in managed code: start the game suspended, allocate a
page in it, write the DLL path, run `LoadLibraryW` there on a remote thread, resume. Injecting
before the entry point is what lets the DLL hook the client's own functions before anything
calls them.

The DLL is located **by file name** beside the launcher, with the previous name still accepted
so an existing install keeps working.

The server to connect to comes from the launcher's own server list, and the mod resolves its
registration endpoint from the same `SiteConfig.xml` the client uses to find the login server —
so joining a friend's server needs no configuration.

## The content check

*Check content* compares what the server has against what is installed locally and names the
difference. It exists because the failure it catches is invisible otherwise: a costume the
server knows about and the client does not renders as the donor, or an effect entity simply does
not appear, with nothing in any log that looks like an error.

*Copy missing content* puts the list on the clipboard so it can be pasted to whoever maintains
the server.

## Files it keeps beside itself

| File | What |
|---|---|
| `launcher.json` | game path, mod path, server list, options |
| `installed.json` | the ledger — what is installed, and the texture manifest rows each install added |
| `fxpacks.json` | shared effect packs and which costumes use them |

The ledger is the only record of which manifest rows an install added, so removing a costume
with a *different* copy of the tool cannot clean up rows it never saw.
