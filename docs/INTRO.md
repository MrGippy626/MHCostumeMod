# Custom Costume Framework

**Add custom costumes to Marvel Heroes Omega without deleting the ones the game came with.**

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

⚠ **It only works if the server runs it too.** More on that below — it is the single most
important thing to understand before downloading.

---

## The problem it solves

Marvel Heroes costume mods work by **replacement**. A mod that puts Jubilee's look on Gambit is
literally a file named after one of Gambit's costumes. You drop it into `CookedPCConsole`, it
overwrites the original, and that costume slot shows Jubilee from then on. Gambit Classic is
gone until you restore the file.

Which means:

- One custom look per costume slot, forever
- You lose a stock costume for every custom one you gain
- Swapping mods means swapping files by hand
- Two mods built on the same costume can never be installed together

**With this framework a custom costume becomes an additional entry.** The stock costume stays
exactly where it was, the custom one appears next to it, and both are selectable. Nothing in
`CookedPCConsole` is overwritten — new files are added beside the originals.

I currently run **27 custom costumes at once** on my own server, alongside every stock costume,
with nothing replaced.

---

## The one concept worth understanding: the donor

This explains almost everything else, including a few behaviours that otherwise look like bugs.

The client has a fixed list of costumes baked into it. It has no concept of a costume that did
not ship with the game — there is nowhere to put one.

So every custom costume is paired with a **donor**: a real stock costume of the same hero. The
donor supplies the *identity* the game understands — which hero it belongs to, that it is a
valid costume, how it behaves. The custom costume supplies the *art*.

Three consequences worth knowing:

- **A custom costume belongs to exactly one hero.** A Gambit costume cannot be worn by Magneto.
  The tool refuses a cross-hero donor outright, because that puts one hero's mesh on another's
  skeleton and takes the client down.
- **Someone without the mod sees the donor costume.** Not a broken model, not an error — the
  stock costume yours was built on. That is deliberate, and it is what keeps unmodded players
  safe.
- **The donor is not consumed.** Other players still wear it normally, and so can you.

---

## What it does

**Costumes that coexist.** Custom costumes install alongside the stock set. Removing one puts
the install back exactly as it was.

**Custom icons.** Its own artwork in the inventory tile, the store card, the character sheet and
the social panel — not the donor's icon over a different model.

**Custom display names.** Its own name in the UI rather than the donor's.

**Per-costume skill effects, scoped to the wearer.** A custom costume can bring its own VFX —
recoloured powers, projectiles, ground effects — and they apply **only to the player wearing
it**. Two players in the same zone, one custom and one stock, cast the same power and each sees
their own version. Not "last equip wins", not "everyone gets it". Per-wearer.

**Store integration.** Custom costumes can be sold in the in-game store for Gs, bought like any
other costume, and arrive as a token you equip normally.

**Converts the mods you already have.** Point the authoring tool at a `.upk` costume mod — the
kind already on Nexus — and it converts it into a coexisting costume. No new art required.

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
  because it is a modified MHServerEmu (AGPL).

Each account also opts in once, in game chat:

```
!player costumes enable
```

Until you run that, the server deliberately sends you the **donor** costume instead of the
custom one — the safety net that stops a client without the mod being handed something it
cannot render. If your custom costumes look like ordinary stock ones, this is almost always
why.

---

## How a costume gets from an author to a player

```
   SERVER OWNER                                     PLAYER
   ------------                                     ------
   1. Has a .upk costume mod
   2. Costume Manager converts it
      into a coexisting costume
   3. Installs it on the server
   4. Exports a .mhbundle       ---- sends it --->  5. MHLauncher imports it
                                                    6. Restarts the game
                                                    7. Wears the costume
```

**`.mhbundle`** is the distribution format: one file carrying the costumes *and* the effect
packs they need, ready to import.

### ⚠ Bundles are server-specific

Each server assigns its own internal slot numbers to its costumes, and those numbers travel on
the wire between server and client. A bundle from Server A therefore only works on Server A.

**There are no multi-server profiles yet.** If you play on two servers that both use this, you
must uninstall the custom content from one before importing the other's bundle. This is the
biggest rough edge in the current release.

---

## Downloads

| File | Who needs it | Notes |
|---|---|---|
| **`MHLauncher.zip`** | **Everyone** on a server using this | Self-contained, no prerequisites. Includes the client mod and the bundle installer. |
| **`CostumeManager.zip`** | **Server owners** who author costumes | Needs the .NET 8 Desktop Runtime and the Windows App Runtime |
| `CostumeDLL.zip` | Rarely — replacing just the client mod | The launcher already contains it |

**[Download on GitHub](https://github.com/MrGippy626/MHCostumeMod/releases)** ·
**[Source and docs](https://github.com/MrGippy626/MHCostumeMod)**

Server owners also need the server itself, which is source-only:
[`MHServerEmu/`](https://github.com/MrGippy626/MHCostumeMod/tree/main/MHServerEmu).

---

## Questions people have asked

**"I downloaded it and I don't have any new costumes."**
Expected — it is not a costume pack. You need a server running the mod, and a `.mhbundle` from
that server's owner.

**"Do I need this just to see someone else's custom costume?"**
Yes. Without the client mod you see the donor costume theirs was built on. Nothing breaks; you
just see a stock costume.

**"Will this break my install or get me banned?"**
It adds files rather than replacing them, and removing a costume restores the original state.
The client mod is injected at launch, which some anti-virus products dislike on principle — the
full source is in the repository if you want to check what it does.

**"Can I use it on any public server?"**
Only one that has installed the server mod. Ask whoever runs it.

**"Does it work with costume mods from Nexus?"**
That is the intended input. The Costume Manager takes a `.upk` and converts it.

**"Can two custom costumes share the same donor?"**
Yes. Several custom costumes can be built on one stock costume.

**"What if a custom costume is broken?"**
The client mod notices a costume that crashed the game while loading, disables it on the next
start, and lists it in `CostumeMod.quarantine` beside the game's `Binaries\Win64`. You lose that
one costume, not your ability to play.

**"Does it change anything for players who don't have it?"**
No. They see the donor costume and play normally.

---

## How it works, for the curious

The client is a sealed 2017 binary — nothing about it can be recompiled — so everything here
works by intercepting what it already does.

1. **The server invents an identity.** A custom costume gets a forged 64-bit id and a slot
   number above the range the game uses, and that id is aliased onto the donor's real data
   record, so every existing code path on both sides keeps working.
2. **The client mod intercepts it.** An injected DLL hooks the client's own lookup functions.
   Asked "what costume is this?", it returns the donor's record — the client stays satisfied it
   is looking at something ordinary.
3. **Meanwhile it loads the custom art** through the client's own package loader, and when the
   engine asks which model to render, answers with the **custom** one.
4. **Textures resolve through an alias** added to the texture manifest. The custom costume's
   textures are found under a new name and the stock entries are untouched — installs are
   adds-only there, which is what makes uninstalling clean.
5. **Effects are scoped by asking who cast the power.** The client's own effect-resolution
   functions know which avatar is casting, so the substitution can be limited to that player
   instead of applied globally.

The hard part is not renaming things — it is knowing what must **not** be renamed. Some names
are resolved against stock data by name at load time; rename one and the costume crashes on
equip, renders white, or silently renders the donor. Others must be renamed or your copy
collides with the resident stock one. The Manager decides from a prebuilt index of every
official package, so it is a lookup rather than a guess.

Full detail: [how the client mod works](DLL.md) ·
[what the Costume Manager does](COSTUME-MANAGER.md) ·
[server changes](SERVER-CHANGES.md)

---

## Current limits

Worth knowing before committing to it:

- **No multi-server profiles.** Uninstall before switching between servers that both use this.
- **Everyone on the server needs the launcher**, not only people who want custom costumes.
- **A custom costume belongs to one hero** — its donor's.
- **Not every effect can be per-costume.** Some art is shared between powers at a level the game
  resolves globally. Where that happens the honest choice is stock-for-everyone or
  custom-for-everyone, and the tool refuses rather than half-doing it.
- **First release.** It works — I run it daily — but it has been tested by one person on one
  server. Expect rough edges, and please report them.

---

## See it working

**[Screenshots and gameplay footage](SHOWCASE.md)** — both tools, plus clips of two clients side
by side showing that a custom costume's effects belong only to the player wearing it, and of a
costume bought in the store and equipped.
