# Using the Launcher — a player's guide

`MHLauncher.exe`. Start the game with custom costumes, and install content people send you.

![The launcher](media/launcher-main.png)

---

## First run

1. Open **Settings** (bottom right of the launcher window).
2. **Game program** — *Browse…* to your `MarvelHeroesOmega.exe`.
3. **Mod file** — *Browse…* to `MRGIPPY_COSTUME_MOD.dll`. If it is already sitting next to
   `MHLauncher.exe`, the launcher finds it and you can skip this.
4. **Servers** — *Manage servers…*, then *Add* with the server's name and address. *Save*.
5. **Done**.

The button now reads **Play with mod**. If it says *Not ready to launch*, one of the paths
above is still missing — the launcher names which.

## Playing

Press **Play with mod**. The launcher starts the game with the mod loaded and gets out of the
way. Log in normally.

**Your account must be allowed to use custom costumes.** Once, in game chat:

```
!player costumes enable
```

Without it the server deliberately shows you the *donor* costume instead of the custom one —
that is the safety net that stops an unmodded client being sent something it cannot render.
The symptom is "my custom costumes stopped working" with everything else normal.

## Installing content

**Tools** opens the installer.

![The installer](media/launcher-tools.png)

| You were given | Use |
|---|---|
| `.mhbundle` | **Install a bundle…** — costumes and the effect packs they need, in one file |
| `.mhcostume` | **Install costume…** |
| `.mhfxpack` | **Install effects pack…** |
| a folder of the above | **Install a folder…** |

Prefer a bundle when you have one: it installs the effect packs first, so nothing is left
waiting for a pack that arrives later.

A bundle installs item by item and keeps going if one fails, then reports what succeeded and
what did not. One bad file does not lose the other nineteen.

**Restart the game after installing.** The mod reads its configuration once, when the client
starts.

## Checking you have everything

**Check content** compares the server's list against yours and shows anything missing.

This matters more than it looks. A costume the server knows about and you do not shows up as
the wrong costume, and a missing effect pack can make a ground effect **not appear at all**
rather than look plain. Neither reports an error anywhere.

**Copy missing content** puts the list on your clipboard — paste it to whoever runs the server
and ask for those files. **Recheck** after installing them.

## Removing something

In **Tools**: select it and press *Remove selected costume* or *Remove selected pack*. That
takes out its files, its texture rows and its configuration entry.

**Check install** verifies what is there is intact — worth running if something looks wrong
after a game update or a manual file copy.

## When something is wrong

**A costume shows the wrong character's outfit.** That is the donor costume. Either the account
flag is not set (`!player costumes enable`), or the server has a costume you have not installed
— run *Check content*.

**A ground effect or projectile is missing entirely, not just plain-looking.** You are missing
the effect pack for it. *Check content* will name it.

**The game closes shortly after the character screen.** Usually a costume whose files are
incomplete. The mod protects itself against this: a costume that crashes the client while
loading is disabled automatically on the next start and listed in `CostumeMod.quarantine` next
to the game's `Binaries\Win64`. Delete that costume's line to re-enable it after reinstalling.

**Nothing custom appears at all.** Check `CostumeMod.log` in `Binaries\Win64` — its first lines
name the configuration file it read and how many costumes loaded. If that file is missing
entirely, the mod was not injected: confirm you pressed *Play with mod* rather than starting the
game some other way.

For anything else, the useful things to send are `CostumeMod.log` and the output of
*Check install*.
