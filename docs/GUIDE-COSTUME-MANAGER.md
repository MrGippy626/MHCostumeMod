# Using the Costume Manager — a guide

For whoever maintains the server and builds the content. Players use `MHLauncher.exe` instead.

![Costume Manager](media/manager-home.png)

---

## Setup

**Settings** → *Browse* to the game folder, and *Browse* to the server folder if you run one.
With the server folder set, every install and uninstall rewrites `ServerCostumes.json` there
directly.

Keep beside `CostumeManager.exe`: `Costumes.json`, `Effects.json`, `costume_reference.db` and
`effect_reference.db`. The build copies them; without them donor detection, effect planning and
collision checking all quietly stop working. **Home** reports what it found.

---

## Installing a costume

1. **Install** → *Browse* to the mod's `.upk`.
2. The Manager identifies the **donor** — the stock costume the mod was built from. Check it.
   It is usually right, and when it is wrong nothing else can be.
3. Give the costume a **name**. This is its identity: it derives the id, and it is what someone
   importing your pack will see. Set the **display name** too if you want something different
   in game, and the **store price** if it should be buyable.
4. Optionally tick **Install custom icons with this costume** and pick the art.
5. Press **Verify**. This is the dry run — it reports the renames it plans, the collisions it
   found and anything it refuses to touch. Read it.
6. Press **Install costume**.
7. Restart the server, and restart the client.

**Both restarts matter.** The server reads `ServerCostumes.json` once at startup and the client
reads its configuration once when it starts. Installing while either is running leaves it on
the old data, and the symptom is "my change had no effect" rather than an error.

### When Verify says something

- **"donor is from a different hero"** — detection landed on an unrelated hero, usually because
  this one has no costumes in the table. Pick the donor by hand; it must be the same hero.
- **A collision list** — expected. Those are the objects being uniquified so the mod does not
  bind to the resident stock costume.
- **An orphaned import** — the mod refers to something by a name that has been renamed. Not
  always fatal (a mod shipping its own textures is fine) but worth looking at if the costume
  renders white.

---

## Icons

**Icons** → pick the costume, drop art into the roles, *Update icons*.

Icons cover four separate surfaces — the inventory tile, the store card, the character sheet
and the social panel — and they are driven from different fields, so use *Refresh* and check in
game rather than assuming one implies the others.

Source art can be PNG, JPG or DDS. A supplied DDS is used as-is; anything else is converted,
with mipmaps — an icon without them renders as coloured noise.

---

## Effects

![Effects](media/manager-effects.png)

**Effects** lists **effect packs first**, each expanding to the costumes using it. Search
matches both sides, so typing a costume name selects the pack that holds it.

### Adding a pack

1. *Scan folder…* at the folder of effect `.upk` files.
2. The Manager sorts them into what it can install, what it refuses, and why. A refusal is a
   measurement — "this class is exported by more than one official package" — not a guess.
3. Install, then **Assign** the pack to a costume.

Assigning is configuration only; no files are written, and two costumes of the same hero share
one pack rather than duplicating ~14 MB.

### The sibling-class tick

Some packs need one extra rename, and no offline signal can decide it — the answer depends on
which copy of a shared object wins the load race at runtime. The row says whether it is likely,
and the tooltip tells you how to judge it in two casts:

- custom looks right, stock looks stock → leave it **off**
- custom looks **stock** → turn it **on**
- **both** look custom → it is leaking onto the stock costume; **on** stops that, but may take
  the custom art with it

Cases that are provably unsafe are never offered.

### Retrofitting

*Sync hotspot ids* updates costumes installed before the ground-effect support existed. It
rebuilds no packages — it is configuration only. *Prune missing FX* clears entries whose files
are gone.

---

## Sharing

**Manage** → *Export…* on one costume, or *Export bundle…* for several.

A bundle pulls in the effect packs the selected costumes use, once each, so the recipient needs
one file. Send it; they use *Install a bundle…* in the launcher.

Slot numbers travel with the pack. The recipient keeps yours when it is free, which is what
keeps your server and their client agreeing.

---

## Removing and hiding

**⛔ To stop a costume loading, hide it — do not uninstall it.** *Show / hide…*, untick, *Apply*.
A hidden costume keeps its files, its texture rows and its slot, so turning it back on is one
click and nothing is rebuilt.

Uninstall is for removing it permanently. It deletes the package, its manifest rows and its
config entry, and queues the server to delete any store tokens players bought for it — which is
why the server must be restarted afterwards.

---

## Repair

Run these when something is wrong, in this order:

| Button | Answers |
|---|---|
| **Check manifest** | Is the texture manifest intact — both "did we lose stock rows" and "are the rows we added still there"? |
| **Restore missing rows** | Puts back stock rows only, never removes anything |
| **Rebuild load chains** | Adds parent packages that costumes installed before that check existed are missing |
| **Check costume imports** | Finds names a rename orphaned |

**The second half of *Check manifest* is the one to know about.** If the manifest has been
replaced with the pristine backup, "lost stock rows" is zero and it looks perfectly healthy —
while every custom costume's texture rows are gone and the client freezes the moment one is
equipped. The check for rows *we added* is what catches it.

---

## Before calling anything done

Restart the server, restart the client, equip the costume, and look at `CostumeMod.log`. Its
opening lines name the configuration file actually in use and how many costumes loaded — which
is the fastest way to catch the thing that goes wrong most often here, which is one of the four
programs still running on old data.
