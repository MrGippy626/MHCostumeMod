# Custom Costume Framework — Install unlimited custom costumes WITHOUT overwriting originals

**Add as many custom costumes as you like to Marvel Heroes Omega without deleting the ones the
game came with.**

---

## Start here: what this is, and what it isn't

**This is not a costume.** Downloading it will not give you a new skin to wear.

It is the **plumbing** that makes custom costumes possible at all — a client mod, a server mod,
and an authoring tool. It gives the game the ability to hold costumes it was never shipped
with, and it gives server owners the ability to add them.

**What you get depends on who you are:**

| | |
|---|---|
| **A player** on a server that uses this | Install the launcher, import the costume pack your server owner gives you, wear the costumes |
| **A server owner** | Install the server mod and the authoring tool, convert costume mods into coexisting ones, hand a pack to your players |
| **A modder** with `.upk` costume mods | This converts your existing mods into ones that add rather than replace |

⚠ **It only works if the server runs it too.** This is the single most important thing to
understand before downloading — see [Requirements](#-requirements-the-server-has-to-run-it-too).

---

## Contents

- [The problem it solves](#the-problem-it-solves)
- [Core concepts](#core-concepts)
- [What it does, in detail](#what-it-does-in-detail)
- [Requirements](#-requirements-the-server-has-to-run-it-too)
- [For players](#for-players)
- [For server owners](#for-server-owners)
- [Distributing costumes](#distributing-costumes)
- [What stops it breaking your game](#what-stops-it-breaking-your-game)
- [How it works under the hood](#how-it-works-under-the-hood)
- [Troubleshooting](#troubleshooting)
- [Limits and known gaps](#limits-and-known-gaps)
- [Glossary](#glossary)

---

## The problem it solves

Marvel Heroes costume mods work by **replacement**. A mod that puts Jubilee's look on Gambit is
literally a file named after one of Gambit's costumes. You drop it into `CookedPCConsole`, it
overwrites the original, and that costume slot shows Jubilee from then on. Gambit Classic is
gone until you restore the file from a backup.

That model has hard limits:

- **One custom look per costume slot, permanently.** The slot is the resource, and there are
  only as many as the game shipped with.
- **You lose a stock costume for every custom one you gain.**
- **Two mods built on the same costume can never coexist.** Two different Gambit Classic
  replacements means picking one.
- **Swapping means moving files by hand**, and forgetting which original you overwrote.
- **Nothing is shareable as a unit.** "Install these six costumes" means six manual file drops
  and a list of what they replaced.

### What changes

A custom costume becomes an **additional entry** rather than a replacement. The stock costume
stays exactly where it was, the custom one appears next to it, and both are selectable in the
costume panel.

| | Traditional mod | This framework |
|---|---|---|
| Stock costume after install | Overwritten | Untouched, still selectable |
| Custom costumes per hero | One per slot | As many as you install |
| Two mods on the same donor | Impossible | Fine |
| Uninstalling | Restore a backup by hand | Removes cleanly, original state returns |
| Sharing a set | Zip of loose files | One `.mhbundle` |
| Custom icon and name | No | Yes |
| Custom skill effects | Global if at all | Per costume, only for the wearer |

**There is no designed limit on how many you install.** Custom costumes are allocated slot
numbers from an open range rather than competing for the fixed set the game shipped with, so
nothing in the design runs out. I currently run **27 at once** on my own server, alongside every
stock costume, with nothing replaced — that is the number I have actually tested, not a ceiling.

---

## Core concepts

### The donor

This one explains most of the rest, including several behaviours that otherwise look like bugs.

The client has a fixed list of costumes baked into it. It has no concept of a costume that did
not ship with the game — there is literally nowhere to put one.

So every custom costume is paired with a **donor**: a real stock costume of the same hero. The
donor supplies the *identity* the game understands — which hero it belongs to, that it is a
valid costume, how it behaves in every system that touches costumes. The custom costume
supplies the *art*.

Consequences worth knowing:

- **A custom costume belongs to exactly one hero.** A Gambit costume cannot be worn by Magneto.
  The tool refuses a cross-hero donor outright, because that puts one hero's mesh on another's
  skeleton and takes the client down on the next equip.
- **Someone without the mod sees the donor costume.** Not a broken model, not an error — the
  ordinary stock costume yours was built on. That is deliberate and is what keeps unmodded
  players safe.
- **The donor is not consumed.** Other players wear it normally, and so can you. It is a
  template, not a slot being taken.
- **Several custom costumes can share one donor.** Three different Gambit Classic conversions
  can all be installed together.
- **The donor is chosen at install time** and is usually detected automatically from the mod's
  own contents. You confirm it before anything is written.

### Slot numbers

Each custom costume gets a **slot number** the server assigns, above the range the game itself
uses. That number is what travels between server and client when you equip something.

Because it is agreed between both sides, it cannot be changed unilaterally — which is why
costume packs are tied to the server that produced them (see
[Distributing costumes](#distributing-costumes)).

### Coexistence

Installing is **additive**. New files are written into `CookedPCConsole` alongside the stock
ones; the game's texture manifest gains new rows and loses none. Uninstalling removes exactly
what was added.

That property is what makes the whole thing safe to try: a bad install is removable, and the
stock game is never in a modified state.

---

## What it does, in detail

### Costumes that coexist

The headline feature. Custom costumes appear in the costume panel next to the stock ones and
work identically — equip, unequip, keep across logout, switch heroes and back.

### Custom icons

A custom costume gets its own artwork, not the donor's. Four separate places in the UI show a
costume icon, and all four are covered:

| Surface | Where you see it |
|---|---|
| Inventory tile | The costume token in your bags |
| Store card | The in-game store listing |
| Character sheet | Your equipped costume |
| Social panel | Nearby-players list |

These are genuinely different fields inside the game's data, which is why an icon can look
correct in one place and wrong in another if only some are set. Source art can be PNG, JPG or
DDS; anything that isn't already a DDS is converted with mipmaps generated.

### Custom display names

Its own name in the UI rather than showing the donor's. This covers the tooltip and the store
card.

### Per-costume skill effects, scoped to the wearer

The part that took longest to get right, and the most distinctive thing here.

A custom costume can carry its own visual effects — recoloured powers, projectiles, ground
areas, buff visuals — and **they apply only to the player wearing that costume**.

Two players in the same zone, one wearing a custom costume and one wearing the stock donor,
cast the same power: each sees their own version. Not "whoever equipped last wins", not
"everyone in the zone gets the custom effects". Per-wearer.

Effect coverage spans the kinds the game treats separately:

| Kind | Example |
|---|---|
| Powers | The cast animation and its particles |
| Conditions | Buff and debuff visuals on the character |
| Projectiles / missiles | Thrown cards, energy bolts |
| Hotspots | Ground areas a power leaves behind |

**Effect packs are shared per hero.** A pack is a set of 30–60 packages and roughly 14 MB, so
welding one to a single costume would mean paying that cost again for every costume of the same
hero. Instead a pack is installed once and costumes point at it — two Scarlet Witch costumes
share one pack rather than duplicating it.

### Store integration

Custom costumes can be sold in the in-game store. They get a store entry with a price you set
(default 750 Gs), a card with the custom icon and name, and they are bought like any other
costume. What arrives in your inventory is a token you equip normally.

### Converts the mods you already have

The authoring tool takes an ordinary `.upk` costume mod — the kind already on Nexus — and
converts it into a coexisting one. You do not need new art, and mod authors do not need to do
anything differently. What already exists is the input.

---

## ⚠ Requirements: the server has to run it too

**The server you play on must run the modified MHServerEmu.**

Custom costume identities are created *server-side*. A stock server has no way to invent them,
so it cannot tell your client to wear something the game has never heard of. This is not
something the client can paper over — it is where the identity comes from.

So:

- **Every player on that server needs the launcher**, including the server owner playing
  locally. Launch through `MHLauncher` or you will not see custom costumes.
- **You cannot use this on a server that has not installed it.** You will connect and play
  normally; there simply are no custom costumes to see.
- **The server owner builds the server mod** from source in the repository. It ships as source
  because it is a modified MHServerEmu, which is AGPL-licensed.

### Downloads

| File | Who needs it | Notes |
|---|---|---|
| **`MHLauncher.zip`** | **Everyone** on a server using this | Self-contained, no prerequisites. Includes the client mod and the bundle installer. |
| **`CostumeManager.zip`** | **Server owners** who author costumes | Needs the .NET 8 Desktop Runtime and the Windows App Runtime |
| `CostumeDLL.zip` | Rarely — replacing just the client mod | The launcher already contains it |

Windows x64. **[Download on GitHub](https://github.com/MrGippy626/MHCostumeMod/releases)** ·
**[Source and docs](https://github.com/MrGippy626/MHCostumeMod)**

---

## For players

### First run

1. Extract `MHLauncher.zip` anywhere and run **`MHLauncher.exe`**.
2. **Settings** → **Game program** → browse to your `MarvelHeroesOmega.exe`.
3. **Mod file** is found automatically if the DLL is sitting beside the launcher, which it is
   in the zip. Otherwise browse to `MRGIPPY_COSTUME_MOD.dll`.
4. **Manage servers…** → **Add**, with the server's name and its address. **Save**, then
   **Done**.
5. The button reads **PLAY**. If it says *Not ready to launch*, the launcher names which path
   is still missing.

### Enable custom costumes on your account

Once, in game chat:

```
!player costumes enable
```

Until you run that, the server deliberately sends you the **donor** costume instead of the
custom one. It is the safety net that stops a client without the mod being handed something it
cannot render.

**If your custom costumes look like ordinary stock ones, this is almost always why.**

### Installing content

**Tools** opens the installer.

| You were given | Use |
|---|---|
| `.mhbundle` | **Install a bundle…** — costumes and their effect packs in one file |
| `.mhcostume` | **Install costume…** |
| `.mhfxpack` | **Install effects pack…** |
| A folder of the above | **Install a folder…** |

Prefer a bundle where one exists: it installs effect packs before the costumes that use them,
so nothing waits on a missing dependency.

**Restart the game after installing.** The client mod reads its configuration once, at startup.

### Checking you have everything

**Check content** compares what the server has against what you have installed and names
anything missing. Worth running when a costume looks like the wrong character or an effect is
absent — both symptoms of missing content rather than a broken install.

**Copy missing content** puts the list on your clipboard to paste to your server owner.

### Removing something

In **Tools**, select it and use *Remove selected costume* or *Remove selected pack*. Files,
texture rows and configuration entry all go. **Check install** verifies what remains is intact.

---

## For server owners

You need three things: the server mod (built from source), the Costume Manager, and the
launcher — you are a player on your own server too.

### Setting up the Manager

**Settings** → browse to the game folder, and to the server folder. With the server folder set,
every install and uninstall updates the server's configuration directly, with no manual copying.

The Manager needs its data tables beside the executable (`Costumes.json`, `Effects.json` and two
reference databases). They ship in the zip. The **Home** page reports what it found and whether
the install is healthy — worth glancing at first, because most confusing failures on this
project turn out to be one program running on stale data.

### Installing a costume

1. **Install** → browse to the mod's `.upk`.
2. The Manager identifies the **donor** — the stock costume the mod was built from. Confirm it.
   It is usually right, and when it is wrong nothing downstream can be right.
3. Give the costume a **name** (its identity), a **display name** if you want something
   different shown in game, and a **store price** if it should be purchasable.
4. Optionally tick **Install custom icons with this costume** and choose the art.
5. Press **Verify**. This is a dry run: it lists every rename it plans, every collision it
   found, and everything it refuses to touch. Read it before continuing.
6. Press **Install costume**.
7. **Restart the server, and restart the client.**

Both restarts matter. The server reads its costume configuration once at startup and so does
the client mod. Installing while either is running leaves it on the old data, and the symptom
is "my change did nothing" rather than an error.

### Adding effects

**Effects** → *Scan folder…* at a folder of effect `.upk` files. The Manager sorts them into
what it can install and what it refuses, with the reason for each refusal. Install, then
**Assign** the pack to a costume.

Assignment is configuration only — no files are written, and any other costume of the same hero
can use the same pack.

### Sharing with your players

**Manage** → *Export…* for a single costume, or *Export bundle…* for several. A bundle
automatically includes the effect packs the selected costumes use, once each. Send the file;
players import it through the launcher.

### Removing and hiding

**To stop a costume loading, hide it rather than uninstalling.** *Show / hide…*, untick,
**Apply**. A hidden costume keeps its files, its texture rows and its slot number, so turning it
back on is one click and nothing is rebuilt.

Uninstall is for removing permanently. It deletes the package, its texture rows and its
configuration entry, and tells the server to clean up any store tokens players bought for it —
which is why the server needs a restart afterwards.

---

## Distributing costumes

Three file formats, each a zip with a manifest:

| Format | Contains |
|---|---|
| **`.mhcostume`** | One costume: its converted package, its texture rows, its icon art, its configuration |
| **`.mhfxpack`** | One effect pack: its packages and any base packages they depend on |
| **`.mhbundle`** | Several of the above together, with dependencies resolved |

A bundle is deliberately not a new format — it is a zip of the other two, so everything about
identity and slot numbers has exactly one implementation rather than two that can drift.

### ⚠ Bundles are server-specific

Each server assigns its own slot numbers, and those numbers travel on the wire. A bundle from
Server A therefore only works on Server A.

**There are no multi-server profiles yet.** If you play on two servers that both use this, you
must uninstall the custom content from one before importing the other's bundle. This is the
biggest rough edge in the current release, and the thing most likely to be addressed next.

---

## What stops it breaking your game

Reasonable thing to be cautious about. The protections that exist:

**Installs never overwrite stock files.** New packages are added beside the originals, and the
texture manifest only ever gains rows. That is what makes uninstalling clean rather than
best-effort.

**The manifest is backed up before the first change**, and the Manager can check the live one
against that backup in both directions — has anything stock been lost, *and* is everything we
added still present. The second question matters more than it sounds: a manifest silently
reverted to its stock copy looks perfectly healthy by the first test while every custom costume
has lost its textures.

**A costume that crashes the client is disabled automatically.** Before loading custom art the
mod writes a marker naming the costume and clears it once loading finishes. If that marker is
still there at the next startup, the previous session died there — the costume is quarantined,
skipped, and everything else still runs. You lose one costume, not your ability to play. It is
listed in `CostumeMod.quarantine` beside the game's `Binaries\Win64`; delete its line to
re-enable it.

**A costume whose files are missing never loads.** The mod checks every package exists at
startup and disables any costume with something absent, rather than driving the game's loader
at a file that isn't there.

**Players without the mod are protected.** The server tracks which custom identities each
connected client can actually understand, and sends anyone else the ordinary stock version
instead. That is why an unmodded player sees a normal costume and normal effects rather than
missing objects.

**Uninstalling is a real operation, not a suggestion.** It removes the package, the texture rows
recorded for that install, and the configuration entry — and the record of exactly which rows to
remove is kept precisely so removal doesn't have to guess.

---

## How it works under the hood

The client is a sealed 2017 binary that will never be updated. Nothing about it can be
recompiled, so everything here works by intercepting what it already does.

**1. The server invents an identity.** A custom costume gets a forged 64-bit id and a slot
number above the range the game uses. That id is aliased onto the donor's real data record, so
every existing code path on both sides keeps working — nothing has to be taught about a new kind
of costume.

**2. The client mod intercepts it.** An injected DLL hooks the client's own lookup functions.
When the game asks "what costume is this?", it gets the donor's record back and stays satisfied
it is looking at something ordinary.

**3. The custom art loads alongside.** The mod drives the client's *own* package loader to bring
in the custom package at the moment the costume is applied — not a separate loading path, which
is what keeps it stable.

**4. The substitution happens at render time.** When the engine asks which model to use, the mod
answers with the custom class instead of the donor's.

**5. Textures resolve through an alias.** The game finds textures by name through a manifest.
Rather than renaming textures — which would break the lookup — the install adds an alias so the
custom costume's textures are found under a new package name while the stock entries stay
exactly as they were.

**6. Effects are scoped by asking who cast the power.** The client's effect-resolution functions
know which character is casting at the moment they run, so the substitution can be limited to
that player rather than applied to everyone.

### The genuinely hard part

It is not renaming things. It is knowing what must **not** be renamed.

Some names are resolved against stock data by name at load time. Rename one of those and the
costume crashes on equip, renders white, or silently renders the donor instead. Other names must
be renamed, or your copy collides with the stock one already in memory and the game shows you
the donor's skin.

The Manager decides that from a prebuilt index of every official package in the game, so "does
this name collide with stock?" is a lookup rather than a guess. Effects work the same way: a
class exported by more than one official package is refused, while a particle system shared by
several is fine — a class is resolved globally by name, a particle system is drawn by whichever
class references it.

Deeper detail lives in [how the client mod works](DLL.md),
[what the Costume Manager does](COSTUME-MANAGER.md) and [server changes](SERVER-CHANGES.md).

---

## Troubleshooting

**A custom costume shows as a different character's outfit.**
That is the donor costume. Either the account flag is not set (`!player costumes enable`), or
the server has a costume you have not installed — run *Check content* in the launcher.

**A ground effect or projectile is missing entirely, not just plain-looking.**
Missing effect pack. *Check content* names it. "Absent" and "stock-looking" are different
symptoms with different causes, and the difference is a useful clue.

**Effects look stock even though the costume is right.**
The costume's effect pack isn't installed or isn't assigned. On the server side, the Effects tab
shows which costumes use which pack.

**"My change did nothing."**
Almost always version skew. Four things cache data at startup — the Manager, the client mod, the
server, and the configuration file — and any one of them running on old data produces exactly
this. Restart the server and the client after any install.

**The game closes shortly after the character screen.**
Usually a costume whose files are incomplete. It will normally be quarantined automatically on
the next start. If it keeps happening, the Manager's **Repair** tab can check the texture
manifest, which is the other common cause.

**Nothing custom at all, no errors anywhere.**
Check `CostumeMod.log` in `Binaries\Win64`. Its opening lines name the configuration file it
read and how many costumes it loaded. If the file doesn't exist, the mod wasn't injected —
confirm you launched through `MHLauncher` and not the game directly.

**A costume renders white.**
Its textures aren't resolving. On the server side, *Check costume imports* in the Repair tab
finds the usual cause.

When reporting anything, `CostumeMod.log` is the single most useful file to include.

---

## Limits and known gaps

Stated plainly, because finding these out yourself is worse:

- **No multi-server profiles.** Uninstall before switching between servers that both use this.
- **Everyone on the server needs the launcher**, not only the people who want custom costumes.
- **A custom costume belongs to one hero** — its donor's hero, permanently.
- **Not every effect can be made per-costume.** Some art is shared between powers at a level the
  game resolves globally. Where that happens, the honest options are stock-for-everyone or
  custom-for-everyone, and the tool refuses rather than half-doing it and leaving you with a
  costume that leaks its effects onto other players.
- **The Costume Manager is a server owner's tool.** It assumes you control the server, and it is
  not designed for players.
- **Windows only**, and the Manager needs two Microsoft runtimes installed.
- **First release, tested by one person on one server.** It works — I run it daily — but the
  number of configurations it has seen is one. Expect rough edges and please report them.

---

## Glossary

| Term | Meaning |
|---|---|
| **Donor** | The stock costume a custom one is built on. Supplies identity; the custom supplies art. |
| **Slot number** | The id the server assigns a custom costume. Agreed between server and client, which is why packs are server-specific. |
| **`.mhcostume`** | One packaged custom costume. |
| **`.mhfxpack`** | One packaged effect pack, shared by costumes of a hero. |
| **`.mhbundle`** | Several of the above in one file with dependencies resolved. |
| **Effect pack** | A set of packages providing a hero's custom skill visuals. |
| **Quarantine** | Automatic disabling of a costume that crashed the client while loading. |
| **`CookedPCConsole`** | The game's package folder, where costume files live. |
| **Client mod / DLL** | `MRGIPPY_COSTUME_MOD.dll`, injected at launch by the launcher. |

---

## See it working

**[Screenshots and gameplay footage](SHOWCASE.md)** — every page of both tools, plus clips of
two clients side by side showing that a custom costume's effects belong only to the player
wearing it, and of a costume being bought in the store and equipped.
