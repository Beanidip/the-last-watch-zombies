# Architecture — The Last Watch Zombies v2.0.10

Verified against the v2.0.9 source (`baseline/v2.0.9` branch / commit `b1f4675`).
Line references are to that commit. Everything here was read from code; nothing
was verified in a running game unless stated.

## 1. Identity and packaging

| Item | Value |
|---|---|
| Mod ID (`modinfo.json`) | `seventhnightzombies` — deliberately kept from the pre-merge mod so saved item/entity codes keep working |
| Display name | The Last Watch Zombies |
| Version | 2.0.10 on `fix/2.0.10-stability` (`modinfo.json` and `<Version>` in the csproj must match; CI checks this) |
| Game | Vintage Story **1.22.7**, `side: Universal`, required on client and server |
| C# namespace | `SeventhNightZombies` |
| Target framework | `net10.0` (VS 1.22 runs on .NET 10) |
| Harmony ID | `beani.seventhnight.compatibility` |
| Asset domains | `seventhnightzombies` (all current assets), `seventhnight` (legacy entity/item codes for old saves) |
| Output | `bin/Release/net10.0/TheLastWatchZombies.dll` → packaged by the `PackageMod` MSBuild target into `dist/thelastwatchzombies_<version>.zip` |

The packaged ZIP contains the DLL, `modinfo.json`, `modicon.png`, `LICENSE`,
`README.md`, four release-note markdown files and `assets/`. Game assemblies are
referenced with `Private=false` and are never copied.

## 2. Mod systems and load order

| ModSystem | File | ExecuteOrder | Sides | Responsibility |
|---|---|---|---|---|
| `SeventhNightSystem` | `src/SeventhNightSystem.cs` (+ partials `VariantSpawning.cs`, `StakeTimers.cs`) | 0.2 | both (most logic server) | Config load/save, entity/item class registration, Harmony hook, 1 s server tick, ambient spawning, dormant reactivation driver, hordes, infection/cure, death Alphas, active-zombie ledger, admin commands |
| `LastWatchZones` | `src/LastWatchZones.cs` | 0.3 | server | Zone state (`WatchZoneState`), explored-zone ledger, ambient/dormant quotas, buildings (bells/strongholds/bonfires), stronghold auto-reinforcement, sunset migration, bells, `/lastwatch`, `/zombieween`, `/zombiecycle`, `/snseed*`, `/zombiemap` toggle |
| `RotBaitSystem` | `src/RotBait.cs` | default | server | Index of loaded ground-storage piles containing Rot Concentrate |
| `LastWatchMapRegistrar` | `src/LastWatchMap.cs` | default | both | Registers the `lastwatch` world-map layer |

`SeventhNightSystem.StartServerSide` resolves `LastWatchZones` via
`GetModSystem` and throws if it is missing; `LastWatchZones.StartServerSide`
throws unless the core already installed it as `WorldDirector` and created a
`ZombiePopulation`. The two systems are a single unit; there is no optional mode.

Non-ModSystem services owned by `SeventhNightSystem`:

| Class | File | Role |
|---|---|---|
| `ZombiePopulation : IZombiePopulation` | `src/ZombiePopulation.cs` | Dormant pool (per-chunk counts), zone index, corpse registry, dormancy on despawn, reactivation, migration transaction |
| `Config` / `Rules` | `src/Config.cs` | All settings, validation/clamping, pure rule helpers (night, horde calendar, infection, light, target patterns) |
| `StandaloneConfigStorage` | `src/StandaloneConfigStorage.cs` | Splits `Config` into main/advanced ModConfig JSON; retired keys |
| `AdminConfiguration` | `src/AdminConfiguration.cs` | `/zombieconfig`, `/zombiediagnostics` (edit saved config; restart to apply) |
| `WorkBudget`, `WorkProfiler` | `src/WorkBudget.cs` | Shared per-second budgets for AI scans/routes; optional timing counters |
| `SpawnRetryBackoff` | `src/SpawnRetryBackoff.cs` | Per-zone 15/30/60 s backoff after repeated placement failures |
| `ZombieMaskService` | `src/ZombieMaskService.cs` | Funeral-mask camouflage + durability drain |
| `RotCoveredTunicService` | `src/RotCoveredTunicService.cs` | Tunic camouflage, durability drain, first-wear sickness |
| `CompatibilityHooks` | `src/CompatibilityHooks.cs` | Harmony prefix on `EntityPlayer.PlayEntitySound` (mute pain sounds with an eye bandage); postfix on `CharacterSystem.getClassTraitText` (client) appending the infection line from `InfectionTrait` |
| `InfectionTrait` | `src/InfectionTrait.cs` | Formats the Traits-tab infection line from watched attributes the server publishes (`SyncInfectionDisplay`) |
| `RotTunicVisuals` | `src/RotTunicVisuals.cs` | `AssetsFinalize`: copies vanilla grass-shirt shape/attachment data onto the tunic |

Registered classes (`Start`): entity `SeventhNightZombie` → `Zombie`; entity
behavior `lastwatchtemporaryswarm`; item `SeventhNightInfectionCure`; block
`SeventhNightPitBonfire` + BE `SeventhNightPitBonfireBE`; BE behavior
`LastWatchRotBait` (patched onto vanilla `groundstorage` by
`assets/seventhnightzombies/patches/rotbait-groundstorage.json`).

## 3. Entity: `Zombie : EntityAgent`

`src/Zombie.cs` plus partials `ZombieVariants.cs`, `ZombiePalisadeTrap.cs`,
`RotBait.cs`. One class serves all six entity codes; `VariantKind` is derived
from `Properties.Code.Path` (`zombie`, `alphazombie`, `grabber`, `hivezombie`,
`screamer`, `bloater`). All AI runs server-side in `OnGameTick`.

Per-tick decision order (`Zombie.OnGameTick`, ~line 300):

1. **Stake trap** — if caught (`TickPalisadeTrap`), hold still and skip AI.
2. Dead or `IsDormant` → stop navigation.
3. **Variant ability** (`TickVariant`, every 0.5 s): Grabber leap, Hive bees,
   Screamer scream/summons. A Grabber mid-leap skips the rest of the tick.
4. Traverser tick, cooldown timers, NPC-interest budget.
5. **Rot bait** (`TickRotBait`): if a loaded concentrate pile is within 32 blocks,
   it overrides every other behaviour until the pile is gone or out of range.
6. **Prey scan** every `AiPreyScanIntervalSeconds` (+0–250 ms jitter), gated by
   the shared `AiBudget.TakeScan` (`AiPreyScansPerSecond`). `FindPrey` uses
   `World.GetNearestEntity` within `DetectionRange` (horde: `HordeDetectionRange`;
   Alpha: `+AlphaDetectionRangeBonus`). No line-of-sight test. Players win over
   NPCs when both are detected.
7. **Chase** (`ChasePrey`): direct steering (`WalkTowards`) in open space within
   `PursuitDirectWalkRangeBlocks`; otherwise async A* (`NavigateTo_Async`) gated by
   `AiBudget.TakeRoute` (`AiRouteRequestsPerSecond`) and repathed only when prey
   moved `PursuitRepathTargetMovementBlocks`. Stuck detection drives **siege**.
   Melee within √2.4 blocks with a clear torso-height line; damage →
   `SeventhNightSystem.Hit` for infection. During hordes, doors ahead are opened.
8. No prey → idle-despawn timer (reset while a Survival player is within
   `ZombieKeepActiveRadiusBlocks`, checked every `ZombiePresenceCheckSeconds`
   against a shared 5 s player snapshot). Timer ≥ `ZombieIdleDespawnSeconds` →
   `Die(Expire)` → becomes dormant (see §5).
9. Walk to last known prey position for `ZombieLostTargetPursuitSeconds`.
10. Follow the nearest Alpha (`FollowAlpha`) or **idle roam** (walk/pause cycle).

Death (`Die(Death)`): starts corpse aging (`CorpseAgeKey`), Bloater burst, removes
from the active ledger, registers the corpse. `SeventhNightSystem.Tick` ages
corpses (dead entities may stop ticking) and after `CorpseDelaySeconds` drops
loot once (`LootDroppedKey` guard) and despawns with `Removed`.

## 4. Server tick (`SeventhNightSystem.Tick`, 1000 ms)

1. Stake countdowns (`TickStakeTimers`).
2. Return unless population loaded.
3. Corpse aging.
4. Scheduled horde check (`TryStartScheduledHordeBeforeMigration`).
5. Horde end announcement; opening-wave retries; horde waves every
   `HordeWaveIntervalSeconds`.
6. Per online player: bloater poison, eye-bandage flag, mask tick, tunic tick,
   infection (fatal check, poison ticks).
7. If the automatic spawn window is open: **ambient spawning** — per occupied
   zone, one roll with probability `1-(1-AmbientSpawnChancePerSecond)^dt`.
8. Every `DormantWakeIntervalSeconds`: **dormant reactivation** passes in zones
   whose ambient quota is exhausted (`DormantWakeBatchSize` per zone,
   `DormantMaxTotalWakesPerPass` globally, failed attempts included).

`LastWatchZones.Tick` (5000 ms): one-time trader scan, then the **sunset
migration** when the calendar passes `ZoneMigrationHour` on a new day.

## 5. Population model (summary — see `POPULATION_SYSTEM.md`)

* **Active** zombies are ordinary saved entities. The engine persists them in
  chunk data; unloading a chunk does *not* make them dormant.
* **Dormant** zombies are counts in `ZombiePopulation.chunks`, keyed by
  `dimension:chunkX:chunkY:chunkZ` (chunk Y = 32-block height band), split into
  `Normal` and `Alpha`. Variant, HP and inventory are not retained.
* An active zombie becomes dormant only when it despawns with reason `Expire` or
  `OutOfRange` (idle timeout). A watched-attribute marker
  (`seventhnight-dormant-counted`) makes this idempotent across reloads.
* Reactivation re-creates a fresh entity (variant re-rolled; Alpha preserved).
* The active cap is an O(1) `HashSet<long>` of entity IDs maintained by
  spawn/load/death/despawn events and rebuilt at world load.

## 6. Persistence (world save data)

| SaveGame key | Owner | Format | Contents |
|---|---|---|---|
| `seventhnightzombies-dormant-population-v1` | `ZombiePopulation` | JSON `PopulationSnapshot` v2 | Dormant chunk counts |
| `seventhnightzombies-lastwatch-zones-v1` | `LastWatchZones` | JSON `LastWatchSave` v8 | Zone states, `LastDailyRoll`, explored-zone ledger |
| `thelastwatch-stake-timers` | `SeventhNightSystem` | JSON list | Triggered stake countdowns |
| `seventhnight-day-origin` | `SeventhNightSystem` | 8-byte double | Day origin for the horde calendar (`OutbreakDays`) |
| `seventhnight-last-natural-horde-night` | `SeventhNightSystem` | 8-byte long | Last scheduled horde night already run |
| `lastwatch-zombie-spawns-paused` | `SeventhNightSystem` | 1 byte | `/zombiepause` state |

Entity/player attributes: infection (`seventhnight-infection-seconds`,
`-infected-at-day`, `-cure-doses`, `-last-cure-dose-day` — watched; poison clock in
server-only `Attributes`), tunic sickness, rot poison, NPC-interest window, stake
trap timer, variant cooldowns, corpse age / loot flag, dormant marker. Block
entity: pit bonfire state (`kindling`, `burning`, `fuelStacks`, `litAtHours`,
`fuelCoords`).

Load failures of either JSON blob throw, schedule a server shutdown and refuse
to save, so a corrupt blob is never overwritten with empty data
(`SeventhNightSystem.LoadPopulation`, `LastWatchZones.Load`).

**Not persisted:** active horde state (`hordeUntilMs`, wave timers — real-time,
in memory), spawn backoff, AI budgets, map opt-ins, mask wear accumulators,
pending death Alphas, hit queues for infection.

## 7. Configuration files

`VintagestoryData/ModConfig/thelastwatchzombiesconfigmain.json` and
`…configadvanced.json`. Load = defaults ← advanced ← main (JSON populate), then
`Validate()` clamps every field. The server **rewrites both files at every start**
with all non-retired fields, so unknown/retired keys are dropped and values are
normalized. `/zombieconfig` edits the saved file only (restart required). The
client loads its own local copy only to set funeral-mask durability for display.
See `CONFIGURATION.md`.

## 8. Networking / client

There are no custom network channels. Client-visible state travels via entity
`WatchedAttributes` (infection markers, `psychedelic`, quiet-bandage flag, mask
tier, corpse age) and the vanilla world-map data channel (`SendMapDataToClient`)
for the admin overlay. Clients render variant tint (`RenderColor`), bonfire
particles and the map layer.

## 9. External engine dependencies

`VintagestoryAPI`, `VSEssentials` (`WaypointsTraverser`), `VSSurvivalMod`
(`ModSystemBlockReinforcement`, `BlockEntityGroundStorage`, `BEBehaviorDoor`,
`WorldMapManager`, `EntityBehaviorHealth`), `0Harmony`, `Newtonsoft.Json`. The
test project additionally references `VintagestoryLib` and `protobuf-net`.

Vanilla content relied on by code: `game:palisadestakes-{west,east,north,south}`
(stake traps), `game:loosestick-free`, `game:groundstorage` (patched),
`game:clothes-face-funeralmask1..8`, `game:clothes-upperbody-grass`,
`game:rot`, `game:bone`, `game:hide-raw-small`, `game:charcoal`, `game:firewood`,
the vanilla bee entity (via `seventhnightzombies:zombiebees`).

## 10. Expensive operations (performance map)

| Operation | Where | Frequency | Bound |
|---|---|---|---|
| `GetNearestEntity` prey scan (radius 32; 80 in hordes; +32 for Alpha) | `Zombie.FindPrey` | per zombie every ~0.85–1.1 s | global `AiPreyScansPerSecond`=160/s; a second scan only if an NPC is nearer |
| Async A* route | `Zombie.RequestRoute` | on target change / prey moved ≥1.5 blocks, ≥ every 2 s | global `AiRouteRequestsPerSecond`=60/s, one pending per zombie |
| Direct-pursuit ray (≤15 block samples) | `CanDirectPursue` | every 0.55 s while chasing | per zombie |
| Stake proximity scan (≈6×6×6 blocks) | `TickPalisadeTrap` | every 1 s per zombie | `StakeScanBudget` 64/s |
| Bait nearest-pile search | `RotBaitSystem.Nearest` | every ~1 s per idle zombie | O(loaded bait piles) |
| Bait pile index | `RotBaitBehavior` / `RotBaitSystem.Prune` | on inventory `SlotModified` events; 10 s sweep of indexed bait piles only | O(bait piles) (2.0.10; was a 2 s timer per ground pile) |
| Spawn placement search | `TrySpawnNear` | ≤1 per occupied zone per second | `SpawnPositionAttempts`=64 × ≤11 heights |
| Industry scan over loaded chunks' block entities (≤72 chunk lookups) | `ScanIndustryInLoadedZone` | once per sunset per player/stronghold zone | O(block entities); stops early when all 3 found (2.0.10; was ~304k `GetBlock`) |
| Migration | `RunMigration` | once per sunset | O(dormant individuals) RNG draws + O(chunks) |
| `/lastwatch` dormant count | `SnapshotAt` → `MigrationSnapshot()` | per command | copies the entire dormant pool |

See `KNOWN_ISSUES.md` for risks derived from this table.
