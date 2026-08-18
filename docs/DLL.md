# The injected client DLL

`src/costume-dll/dllmain.cpp` — built as **`MRGIPPY_COSTUME_MOD.dll`** and injected into the
game client by `MHLauncher.exe` before the client's entry point runs.

The client is a sealed 2017 binary that will never be updated, so every address in here is
permanent. The DLL hooks a handful of the client's own functions with
[MinHook](https://github.com/TsudaKageyu/minhook) and answers differently for the ids the mod
invented.

---

## What problem it solves

A stock costume change works because the client knows every costume: a property carries a
64-bit id, that id resolves to a costume record, and the record names the art package to load.

A **custom** costume has no record anywhere. So:

| Layer | What the DLL does |
|---|---|
| The property arrives | Recognise the custom slot number the server sent and note which costume is now active |
| The client resolves the id | Hand back the **donor** costume's record, so every existing code path is satisfied |
| Packages load | Load the custom art package alongside, through the client's own loader |
| The engine asks for art | Answer with the **custom** class instead of the donor's |

The result renders as the custom costume while every other part of the client believes it is
looking at an ordinary one.

## The hooks

| Hook | Client function | Job |
|---|---|---|
| 0 | property dispatch | Sees the costume property change; arms or disarms the active costume |
| 1 | slot → id | Translates a custom slot number to the custom id |
| 2, 7 | id → record | Returns the donor's record for a custom id — or a patched **clone** of it, which is how custom icons and names are delivered without touching stock data |
| B2 | package loop | Loads the custom package chain at the moment the costume is applied |
| 4 | package cache | Caches the custom class as soon as its package becomes resident |
| 3 | class resolve | The substitution itself: answers with the custom class |
| 5, 6 | archive encode/decode | Carries custom ids through the client's network archives |
| P, C, M, MP, HS | power / condition / missile / projectile / hotspot class resolve | Per-costume visual effects, scoped to the caster rather than the whole client |
| I, L | icon path, locale string | Custom icons and display names |
| D | assert reporter | Diagnostic: logs the client's *own* failed assertions, with its original file and line |

Hook D is worth knowing about even if you change nothing else. The shipping client kept its
assert strings, so hooking its assert reporter turns "the client silently refused something"
into the client telling you which check failed and where.

## Configuration

The DLL reads **`CustomCostumes.mhc`** from the game's `Binaries\Win64` folder — the same
directory it is injected from. That file is written by the Costume Manager and lists every
installed costume: its slot, its ids, the packages to load, its icons, its effects.

`.mhc` is the same JSON as the older `CustomCostumes.json`, XOR-folded with a fixed key so it is
not casually hand-edited. **It is obfuscation, not security** — the key is in the source on both
sides — and both readers still accept the plain `.json`, so deleting the `.mhc` is always the way
back. The transform exists once in C# (`src/core/CostumeShare.Core.cs`) and once in C++
(`dllmain.cpp`) and the two must stay byte-identical.

### Root flags

| Key | Default | Effect |
|---|---|---|
| `diagnostics` | `false` | Per-item and per-event logging. Turn on to investigate anything. |
| `safeMode` | `true` | Crash-loop protection (below) |
| `hotspotFx` | `true` | Per-costume ground effects and projectiles |
| `perAvatarMesh` | `false` | Scope the mesh swap to the wearer instead of everyone on screen |
| `fxDryRun` | `true` | Compute effect redirects but do not apply them |
| `register` | `true` | Tell the server which custom ids this client can decode |

**An absent key takes the built-in default**, and two of those defaults change behaviour
significantly, so the DLL logs the resolved value of every flag at startup and marks the ones
that were absent. If a flag seems not to be working, read that block first.

## The log

`CostumeMod.log`, beside the DLL. One session per file; the previous run is kept as
`CostumeMod.prev.log`. A second client started from the same folder writes
`CostumeMod.<pid>.log`, and only the newest two of those are kept.

Every line is flushed as it is written — deliberately, so the log is complete on disk when the
client dies. That also makes log volume cost load *time*, which is why the default is basic:

- always on — the resolved configuration, what loaded, arm/disarm, chain completion, missing
  packages, quarantine, the client's own assertion failures, and every error
- behind `"diagnostics": true` — per-package, per-effect, per-icon and per-resolve detail

## Crash-loop protection

A custom package that crashes the client during load would otherwise be re-applied on every
login, because the server remembers what you were wearing. Before driving the loader the DLL
writes a sentinel naming the costume, and deletes it once the chain completes. A sentinel still
present at startup means the last session died there: that costume is added to
`CostumeMod.quarantine`, skipped, and everything else still runs.

The degraded state is deliberate — a quarantined costume still gets its icon and name; only the
mesh falls back to the donor. Remove its line from the quarantine file to re-enable it.

## Working on it

The DLL cannot be tested without the game, and a mistake here takes the client down with no
managed stack. Some hard-won constraints:

- **Never drive the loader at a package that is not on disk.** It does not fail cleanly; it
  corrupts loader state and the damage surfaces somewhere unrelated. The config load checks
  every package exists and disables costumes whose files are missing.
- **Never load a package twice.** Re-driving the loader at something already resident returns
  negative codes and crashes.
- **A forged id is only safe where this DLL is the sole consumer of it.** An id the client's own
  machinery tries to resolve, and cannot, is not inert — it is destructive.
- The effect hooks are production code, not diagnostics. Compiling one out presents in game as
  "projectiles went back to stock", never as a build error.
