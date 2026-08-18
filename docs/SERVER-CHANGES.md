# Changes to MHServerEmu

This tree contains a **modified version of [MHServerEmu](https://github.com/Crypto137/MHServerEmu)**,
which is licensed under the **GNU Affero General Public License v3.0**. This document is the
statement of modifications that licence requires, and it is generated from the diff rather than
written from memory.

| | |
|---|---|
| Upstream project | Crypto137/MHServerEmu |
| Merged upstream commit | `22630d67ebdc35f6b771aadf3dbd6edbe5c492cb` |
| Fork point | `593ec1a4e` (2026-06-15) |
| Client version targeted | **1.52.0.1700 only** (upstream also supports 1.48 and 1.53) |
| Size of our delta | **36 files, +2054 / −155 lines**, of which 5 files are new |

Everything else in this directory is upstream's work, unmodified. Paths in the tables below
are relative to `MHServerEmu/src/`.

Reproduce this diff with:

```
git diff 22630d67e..HEAD -- src Build.bat Build_v52.bat
```

---

## What the modifications are for

The stock game has a fixed set of costumes and visual effects baked into a sealed 2017 client.
This fork adds **custom costumes and custom per-costume visual effects that coexist with the
stock ones** — nothing is overwritten, and a player without the client mod still sees a normal
game.

The server's job in that system is **identity**. A custom costume has no entry anywhere in the
game's data, so the server fabricates one: it mints an id, aliases it onto a real "donor"
costume's data record so every existing code path keeps working, and puts that id on the wire in
the fields the client already inspects. The client-side mod (a separate, injected DLL) recognises
those ids and substitutes the custom art.

Three properties shaped nearly every decision below:

- **A player without the mod must not be harmed.** An id they cannot decode does not degrade
  gracefully — the client discards the entity outright — so anything forged is either gated on an
  explicit opt-in or substituted back to the stock value per recipient.
- **Ids are on the wire.** Costume enums and forged hotspot ids are agreed between the server, the
  client mod and the desktop install tool. They are therefore append-only; renumbering one breaks
  a costume that works today.
- **One bad row must never take down the instance.** Custom content is authored by hand, so the
  failure modes are real.

---

## New files

| File | Purpose |
|---|---|
| `MHServerEmu.Games/GameData/CustomCostumeLoader.cs` | Loads `ServerCostumes.json`; owns the custom-costume table, the per-avatar override cache, item stamps, forged hotspot/summon ids and the pending-purge pass. |
| `MHServerEmu.Games/GameData/CustomCostumeRegistry.cs` | Records which forged ids each client session reported it can decode. |
| `MHServerEmu.WebFrontend/Handlers/CustomCostumes/RegisterWebHandler.cs` | `POST /CustomCostumes/Register` — the endpoint clients report those ids to. |
| `MHServerEmu.WebFrontend/Handlers/CustomCostumes/CatalogWebHandler.cs` | Publishes the installed custom-costume catalog so a player can check what they are missing. |
| `MHServerEmu.DatabaseAccess/SQLite/Scripts/Migrations/7.sql` | Schema 7 → 8: adds the `Player.CustomCostumes` column. |

---

## Modified files, by feature

### Custom costume identity

| File | Change |
|---|---|
| `GameData/DataDirectory.cs` (+236) | `InjectCustomCostume` / `InjectCustomHotspot`: alias a forged id onto a real record and register it in the global, class-scoped and blueprint enum lookups. Without all three the client silently discards anything carrying the id. Also `ForgeCustomCostumeGuid`, used to find a costume's items after it has been uninstalled. |
| `GameData/Calligraphy/Blueprint.cs` (+28) | `InjectCustomEnum`, the blueprint half of the above. |
| `GameData/GameDatabase.cs` (+11/−7) | Calls `CustomCostumeLoader.LoadFromJson` during data load, and makes one prototype-name lookup case-insensitive. |

### Wearing a costume

| File | Change |
|---|---|
| `Entities/Avatars/Avatar.cs` (+311/−96) | The largest change. `ChangeCostume` gains the custom-id path, a fallback to the donor costume for clients without the mod, and a `UsableBy` check so a costume cannot be applied to the wrong hero. `ChangeCostumeForEquippedItem` resolves a custom-costume token when one is equipped. A re-assert sequence re-applies the custom costume on every world entry, because a costume arriving inside an entity's creation archive never reaches the client mod as a property change. |
| `Entities/Player.cs` (+27) | `RefreshCurrentAvatar()` — an in-place exit/re-enter of the world. The client will not re-resolve a costume whose donor package is already loaded, so a swap involving a custom costume needs one. |

### Store and tokens

| File | Change |
|---|---|
| `MTXStore/CatalogManager.cs` (+108/−2) | Generates store entries from the installed custom costumes at catalog load, and mints a custom-costume token when one is bought. |
| `Entities/Persistence/PersistenceUtility.cs` (+12/−2) | Reports which database row failed to restore, with the statement needed to remove it. |
| `Entities/Items/Item.cs` (+7/−1) | An item whose spec cannot be applied is dropped instead of being restored broken. |
| `Achievements/AchievementDatabase.cs` (+3) | Returns nothing for an unresolvable item rather than throwing. Together with the previous two, this is what stops a single bad row crashing the game instance during login. |

### Persistence

| File | Change |
|---|---|
| `DatabaseAccess/SQLite/SQLiteDBManager.cs` (+47/−1) | Schema version 8, and `PurgeCustomCostume` — deletes a removed costume's tokens and strips it from every player's saved state, so an uninstalled costume cannot leave rows behind that no longer resolve. |
| `DatabaseAccess/Models/DBPlayer.cs` (+5) | The `CustomCostumes` column. |
| `DatabaseAccess/MHServerEmu.DatabaseAccess.csproj` (+2) | Ships `7.sql` as an embedded resource. |
| `ServerApp.cs` (+14/−7) | Runs the pending purge at startup, before anyone can log in — the only safe moment to rewrite player rows. |

### Per-costume visual effects

| File | Change |
|---|---|
| `Powers/SummonPower.cs` (+21) | When a player wearing a custom costume summons an effect entity, the forged id is sent instead of the stock one, so the client mod knows which costume the effect belongs to. |
| `Powers/MissilePower.cs` (+17) | The same for server-created missiles. |
| `Network/ArchiveMessageBuilder.cs` (+24/−4) | Substitutes the stock id back for any recipient that cannot decode the forged one. Entity-create messages are already built per client, so this is per recipient and the entity id itself never changes. |
| `Network/AreaOfInterest.cs` (+2/−2) | Passes the recipient through to the above. |

### Client registration

| File | Change |
|---|---|
| `Network/PlayerConnection.cs` (+57) | `CanDecodeForgedRef` — the single predicate deciding whether a client gets a forged id. |
| `Core/Network/GameServiceProtocol.cs` (+26), `WebFrontend/Network/GameServiceTaskManager.cs` (+25), `WebFrontend/Network/WebFrontendServiceMailbox.cs` (+4), `WebFrontend/WebFrontendService.cs` (+5) | Route the registration request from the web frontend into the game service. |
| `Core/Network/Web/WebRequestContext.cs` (+14), `Core/Network/IFrontendClient.cs` (+4), `Frontend/FrontendClient.cs` (+17) | Expose the request's real remote address, and the session id used to key a registration. |
| `Network/InstanceManagement/GameInstanceService.cs` (+17/−1) | Delivers it to the running instance. |
| `CustomGameOptionsConfig.cs` (+2), `Config.ini` (+4) | `RequireCustomCostumeRegistration`. |

### Commands and build

| File | Change |
|---|---|
| `Commands/Implementations/PlayerCommands.cs` (+85/−5) | `!player costume <name>` accepts custom costumes, and `!player costumes enable/disable` is the per-account opt-in. It is deliberately **not** admin-only: a player must be able to turn the mod on for their own account. |
| `Build_v52.bat` (+3/−2) | Deploys to `.\build` rather than `.\build\v52`, and never overwrites a live `Config.ini`. |
| `Build.bat` (+2) | Restored as a shim calling `Build_v52.bat`. |

---

## Notes for anyone merging upstream into this fork again

- The fork point is `593ec1a4e`. The working copy this was merged in is a real git fork of
  upstream, so the next upgrade is `git fetch upstream && git merge` rather than a hand port.
- **Costume enums and forged hotspot ids are on the wire and are append-only.** Hotspot rows are
  numbered by array index, so inserting one renumbers every row after it and breaks costumes that
  work today. `tools/probes/hstest` asserts this against ids taken from the running config.
- **Reference any upstream project as `Platform=x64`.** `Gazillion.csproj` keeps the three client
  versions in sibling folders and excludes two of them on a condition that is only set in its
  `|x64` property groups; built as AnyCPU it compiles all three at once and fails with hundreds of
  duplicate-member errors.
- Building is not deploying. The server runs from `build/`, which only `Build.bat`'s robocopy
  fills, and that directory holds the live `Account.db` — never mirror or clean over it.
