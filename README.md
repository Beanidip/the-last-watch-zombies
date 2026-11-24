# The Last Watch Zombies

A persistent zombie apocalypse overhaul for **Vintage Story 1.22.7**, featuring
migration, infection, sieges, settlement ecology and recurring seventh-night
hordes.

**Author:** Beani · **Version:** 2.0.9 · **Mod ID:** `seventhnightzombies`
(kept from the original Seventh Night Zombies mod so existing worlds keep their
items and entities) · **Side:** required on server and clients.

Zombies here aren't just random monsters that spawn and vanish. The world keeps
a **persistent population**. Zombies that wander off become *dormant* counts tied
to 96×96-block zones. Every sunset those counts migrate toward roads,
strongholds, livestock, forges and people, and they come back when you return.
Every seventh night a horde arrives.

## Features

* **Six zombie types.** Regular and Alpha zombies, plus four rare variants:
  Grabber (leaps), Hive (releases bees), Screamer (summons ten more) and
  Bloater (poison burst when killed).
* **Zone-based night spawning.** Each zone gets 5 new zombies per night,
  shared by everyone in it. Spawns need darkness, open ground and unclaimed
  land, and there is a server-wide cap on active zombies.
* **Persistent dormant population.** It survives restarts, can't be duplicated
  by reloading chunks, returns in small batches, and failed spawn attempts never
  lose zombies.
* **Sunset migration.** Attraction tiers come from players, roads, settlements,
  strongholds, deaths, livestock and industry. Pit bonfires pin or lure
  hordes, and watchtower bells warn you of arrivals.
* **Seventh-night hordes.** They run for 10 real minutes, with waves every
  3 minutes scaled to the number of players. Zombies hit harder at walls and
  open doors during a horde.
* **Infection.** 4 hits from regular zombies, or 2 from an Alpha, infect you.
  Untreated infection is fatal after 7 in-game days. It is cured by six daily
  doses of **Mycobiotic Cure**, made from rot concentrate, a poisonous
  mushroom and a bandage.
* **Sieges.** Zombies that get stuck chew through the blocks in their way.
  Reinforcement wears down, while land claims and blocks with stored contents
  are always protected.
* **Defences.**
  * Wood, copper and iron strongholds auto-reinforce new building in their
    zone (5, 50 or 300 strength).
  * Sharpened-stake traps catch zombies.
  * Rot-concentrate bait distracts them.
* **Stealth.** Crouch while wearing a funeral mask, or wear a Rot Covered
  Tunic, to become invisible to zombies.
* **Admin tools.** 20 commands covering status, map overlay, test spawns,
  seeding, forced migration, pause, live config editing, profiler and the
  **`/zombieween`** apocalypse event.

See [`docs/FEATURES.md`](docs/FEATURES.md) for the verified, numbers-included
inventory.

## Installing (players / server owners)

1. **Back up your world and `ModConfig` folder.**
2. Remove older `seventhnightzombies_*.zip`, `thelastwatch_*.zip` and
   `thelastwatchzombies_*.zip` from `Mods`.
3. Put `thelastwatchzombies_2.0.9.zip` in `Mods` on the server **and** on every client.
4. Start the server. It writes
   `ModConfig/thelastwatchzombiesconfigmain.json` and `…configadvanced.json`
   (see [`docs/CONFIGURATION.md`](docs/CONFIGURATION.md)).

This repository contains **source only**. There is no published release; you
build the ZIP yourself (below).

## Building from source

Requires the .NET 10 SDK and the Vintage Story 1.22.7 assemblies. The game
assemblies are proprietary, so they are never included here.

```bash
# Linux / macOS / CI: fetch the official dedicated server for references (SHA-256 pinned)
bash scripts/fetch-vs-server.sh 1.22.7 vs-server
dotnet build TheLastWatchZombies.csproj -c Release -p:VintageStoryPath=$PWD/vs-server
dotnet run --project tests/Regression.csproj -c Release -p:VintageStoryPath=$PWD/vs-server
```

```powershell
# Windows, against your game install
.\BUILD-WINDOWS.ps1 -GamePath "$env:APPDATA\Vintagestory" -NoInstall
.\TEST-WINDOWS.ps1  -GamePath "$env:APPDATA\Vintagestory"
```

Output: `dist/thelastwatchzombies_2.0.9.zip`. Details are in
[`docs/TESTING.md`](docs/TESTING.md).

## Repository map

| Path | Contents |
|---|---|
| `src/` | All C# (26 files; namespace `SeventhNightZombies`) |
| `assets/seventhnightzombies/` | Entities, shapes, textures, sounds, recipes, lang, ground-storage patch |
| `assets/seventhnight/` | Legacy entity and item codes for pre-merge worlds |
| `tests/` | Regression console program (1,825 assertions) |
| `tools/ConfigDump/` | Regenerates configuration defaults from source |
| `scripts/` | VS reference fetcher, asset validator, config-doc generator |
| `validation/` | Author's historical build, test and server logs, and the throwaway QA mod source |
| `*.md` at root | Author's release notes (`UPDATE-NOTES`, `RELEASE-*`, `ZOMBIEWEEN-ADMIN`, …) |
| `DEFAULT-*.json` | Author's example configs. **Stale**; use `docs/CONFIGURATION.md` |
| `docs/` | Developer documentation (start with `ARCHITECTURE.md`) |

## Documentation

[Architecture](docs/ARCHITECTURE.md) ·
[Features](docs/FEATURES.md) ·
[Population system](docs/POPULATION_SYSTEM.md) ·
[Migration](docs/MIGRATION.md) ·
[Configuration](docs/CONFIGURATION.md) ·
[Commands](docs/COMMANDS.md) ·
[Testing](docs/TESTING.md) ·
[Known issues](docs/KNOWN_ISSUES.md) ·
[Roadmap](docs/DEVELOPMENT_ROADMAP.md) ·
[Agent handoff](docs/AGENT_HANDOFF.md) ·
[Changelog](CHANGELOG.md) ·
[Zombieween admin guide](ZOMBIEWEEN-ADMIN.md)

## Status

The source compiles cleanly against 1.22.7 and all 1,825 regression assertions
pass, both locally and in CI. A live multiplayer acceptance test has **not**
been performed yet; see [`docs/KNOWN_ISSUES.md`](docs/KNOWN_ISSUES.md) before
running a public event.

## License

MIT. See [`LICENSE`](LICENSE).
