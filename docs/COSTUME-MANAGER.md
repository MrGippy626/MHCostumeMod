# Costume Manager

`src/CostumeManager/` — the authoring tool. WinUI 3, unpackaged, run by whoever maintains a
server or builds costume packs. Players do not need it; they use `MHLauncher.exe`.

It takes a costume mod as downloaded — a `.upk` built to *replace* a stock costume — and turns
it into one that **coexists** with the original.

---

## Why a downloaded mod cannot just be copied in

Costume mods are authored to overwrite. The file is named after the costume it replaces, its
internal objects carry that costume's names, and its textures are registered in the game's
texture manifest under that costume's package name. Drop it in and the original is gone.

To make it coexist, everything that identifies it has to be renamed to something unique — and
the difficult part is that **some names must not be renamed**, because the engine resolves them
against stock data by name. Rename one of those and the costume loads and then crashes, or
renders white, or silently renders the donor.

The Manager's job is knowing which is which.

## The install pipeline

1. **Detect the donor.** Which stock costume is this mod built from? Read from the mod's own
   package name, its manifest if it has one, or the frequency of hero tokens across its object
   names — cross-checked against the mesh's group, which is the most reliable signal.
2. **Refuse impossible donors.** A donor from a different hero puts one hero's mesh on another's
   skeleton. A donor with no costume record cannot be aliased. Both are rejected outright.
3. **Plan the renames.** Every object that must become unique gets the costume's token; every
   object that must stay stock is left alone. The exclusion list is not stylistic — each entry
   is a specific crash.
4. **Detect collisions.** Objects whose names collide with the *resident donor's* are found by
   querying `costume_reference.db`, a prebuilt index of every official costume package. A
   colliding mesh group is the reason a costume renders as its donor.
5. **Rewrite the package** with the new name table and write it into the game folder.
6. **Alias the textures.** Textures stored in the shared texture cache resolve by name through
   the game's manifest. Renaming them breaks the lookup, so instead the *package half* of the
   manifest key is aliased — the texture keeps its name and is found under the new package.
7. **Build the icons** (optional) into a small texture package, with ids the DLL registers at
   runtime.
8. **Record everything** in the ledger, and write both configs — `CustomCostumes.mhc` for the
   client and `ServerCostumes.json` for the server.

Installs are **adds-only** at the manifest level. Nothing stock is removed, which is what makes
the whole thing reversible.

## Effects

A visual-effect pack is a set of packages that replace a hero's power, condition, projectile and
hotspot art. The same coexistence problem applies, with an extra twist: effect packages contain
*shared* objects, and whichever copy loads first defines them for everyone. So a partially
renamed pack is worse than none — the custom costume shows stock art for what was not renamed,
and the stock costume shows custom art for the same objects.

Effect packs are therefore **shared per hero** rather than welded to one costume: they live in
`fxpacks.json` and a costume points at one. Two costumes of the same hero cost one pack.

Which objects may be renamed is decided by measurement, not convention — `effect_reference.db`
indexes every official effect package, so "is this class name shared with stock?" is a query.
A class exported by more than one official package is refused; a *particle system* shared by
several is fine, and the asymmetry is real: a class is resolved globally by name, a particle
system is drawn by whichever class references it.

## Packs and bundles

| File | Contains |
|---|---|
| `.mhcostume` | one costume — the rewritten package, its manifest rows, its icon art, its config entry |
| `.mhfxpack` | one effects pack — its packages and the parent packages they need |
| `.mhbundle` | a zip of the above, with the dependency closure resolved |

A bundle is deliberately **not a new format** — it is a zip of the other two, so identity, slot
numbers, texture rows and parent packages all keep the single implementation they already have.
Exporting several costumes pulls in the effect packs they reference, once each.

**Slot numbers travel with a pack.** The server sends the slot on the wire, so the importing
machine keeps the author's number when it is free and only reallocates on a real collision.

## Repair

The Repair page exists because the failures it fixes are silent. It can check the texture
manifest against the pristine backup taken on first install, restore rows an older bug removed,
rebuild the package load chains, and audit every installed costume for imports that were
orphaned by a rename.

The check that matters most asks the *opposite* question to the obvious one: not "did we lose
stock data" but "is what we added still there". A manifest reverted to stock looks perfectly
healthy by the first test and freezes the client on every custom costume equip.

## Layout

| File | Role |
|---|---|
| `Views/InstallPage` | pick a `.upk`, verify, install |
| `Views/IconsPage` | icon art per costume |
| `Views/EffectsPage` | effect packs, listed pack-first with the costumes using each |
| `Views/ManagePage` | show/hide, uninstall, export packs and bundles |
| `Views/RepairPage` | manifest, chains, imports |
| `Views/TexturesPage` | inspect a package's textures |
| `AppState` | the small amount of state pages share — deliberately holds no logic |

Everything that is not UI lives in `src/core/`, which is plain .NET with no GUI dependency, and is
the same code the launcher links.
