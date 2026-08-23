# Custom Costume Framework — Install unlimited custom costumes WITHOUT overwriting originals

Most costume mods force a sacrifice: if you want to run a Jubilee mod over Gambit Classic, you lose Gambit Classic. This tool changes the rules. 

To be clear, this download isn't a custom costume itself—it's a client mod and authoring framework that lets you install *any* custom costume as a completely independent, coexisting option. Both the stock costume and your new custom one will show up and work perfectly without overwriting a single file in `CookedPCConsole`. (I currently have 27 custom costumes running simultaneously on my server right now).

### What It Does
* **True Coexistence:** Adds new custom costumes that live alongside the stock set—zero file replacement.
* **Full UI Support:** Custom icons and display names in your inventory, store cards, and character sheet.
* **Isolated VFX:** Per-costume skill effects scoped perfectly to the wearer. If two players are in the same zone—one in the donor costume and one in the custom—each sees their own correct VFX.
* **Fully Integrated:** Custom costumes are purchasable as store tokens with Gs and equippable just like official ones.
* **Converts Existing Mods:** Takes the costume mods you already have (like a `.upk` from Nexus) as input and converts them into independent, non-replacing slots.

### How It Works (Briefly)
The server mints an entirely new identity for a costume the game has never seen before. An injected client DLL intercepts this new identity on the fly, loading the custom art while the client thinks it's looking at an ordinary costume. Textures resolve through an alias added to the manifest, keeping all stock files completely untouched.

> **⚠ Architecture & Requirements:** 
> Because the new costume identities are minted server-side, this mod requires coordination between the server and the client. The server you play on *must* run the modified `MHServerEmu` (source included in the repo). Furthermore, **everyone** connecting to that server—whether you're the server owner playing locally or a player joining a friend's private server—must launch the game using the included `MHLauncher`. Vanilla clients won't see the costumes, and vanilla servers cannot mint the new identities.

### Distributing & Installing Costumes (`.mhbundle`)
Custom costumes are managed and shared using a new file format: **`.mhbundle`**.
* **Exporting (Server Owners):** When a server owner adds new custom costumes to their server using the `CostumeManager`, the tool exports an `.mhbundle` file containing all the necessary setup data for their specific server.
* **Importing (Players):** Anyone playing on that server uses a built-in tool within `MHLauncher` to easily import the server's `.mhbundle` file into their own game client.
* **Important Caveat (No Multi-Server Profiles Yet):** `.mhbundle` files are **strictly server-specific**. Because each server assigns unique internal IDs to its costumes, an `.mhbundle` provided by one server owner will *only* work on their server. Currently, the launcher does not support multi-server profiles. **If you want to switch from Server A to Server B, you must first uninstall all custom costumes and VFX through the launcher before importing Server B's `.mhbundle`.**

### Downloads & Links
* **Releases:** [Download on GitHub](https://github.com/MrGippy626/MHCostumeMod/releases)
* **Source + Docs:** [View Source on GitHub](https://github.com/MrGippy626/MHCostumeMod)

### Getting Started
* **MHLauncher.zip:** Required for everyone (both players and server owners). This is fully self-contained and includes the client mod and the `.mhbundle` installer tool.
* **CostumeManager.zip:** Grab this if you are a server owner who wants to *author* custom costumes. You use this to convert existing `.upk` mods, install them to your server, and export the `.mhbundle` files for your players to download and install.

This is the first release, so expect a few rough edges. Happy to answer any questions!