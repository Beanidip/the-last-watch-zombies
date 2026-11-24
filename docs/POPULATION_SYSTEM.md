# Persistent Population System

The defining feature of the mod. This document describes what v2.0.9 code
actually does (`src/ZombiePopulation.cs`, `src/LastWatchZones.cs`,
`src/SeventhNightSystem.cs`). Items marked **⚠** are discrepancies or risks;
they are tracked in `KNOWN_ISSUES.md`.

## 1. Zones

`WatchZone(Dimension, X, Z)` (`src/WatchZone.cs`) = 3×3 chunk columns =
**96×96 blocks**, all heights. Coordinates use floor division so negative world
coordinates map correctly (`FromBlock`, `FromChunk`; covered by tests).
`Distance` is Chebyshev (ring distance) and `int.MaxValue` across dimensions.

Per-zone persistent state lives in `WatchZoneState` (only zones with something
worth saving are kept — `HasZoneStateWorthSaving`):

| Field | Meaning | Reset |
|---|---|---|
| `AmbientPopulationAddedToday` | NEW ambient zombies created this sunset cycle (shared by all players in the zone) | each sunset roll |
| `AmbientPopulationAddedByPlayer` | per-player statistics only (not a limit) | each sunset roll |
| `DormantReactivatedToday` | dormant wakes this cycle | each sunset roll |
| `PlayerDeaths` | deaths this cycle (migration attraction) | each sunset roll |
| `StonePaths` | placed blocks whose code contains `stonepath`/`stone-path` (max `ZoneMaxPathsCounted`) | never (decremented when a player breaks one) |
| `PermanentSettlement` | set when ≥25 paths and ≥1 stronghold | never |
| `TraderPopulationSeeded` | a trader was seen here once | never |
| `Bells`, `Strongholds`, `Bonfires` | building records (position, stronghold tier, bonfire burn snapshot) | on player break |
| `BonfireDailyRecords` | peak bonfire fuel booked for a migration day | pruned at roll |

The **explored-zone ledger** (`LastWatchSave.ExploredZones`) records every zone
whose chunk column was loaded or generated (`ChunkColumnLoaded`, dimension 0
only, may arrive off-thread → guarded by `exploredLock`). It exists for
`/zombieween` and is never pruned.

## 2. Active vs dormant

| | Active | Dormant |
|---|---|---|
| Representation | real `Zombie` entity | a count in `DormantChunk { Dimension, X, Y, Z, Normal, Alpha }` |
| Key | entity ID | `"dim:chunkX:chunkY:chunkZ"` (chunk Y = 32-block height band) |
| Persistence | engine chunk save (with all attributes) | SaveGame key `seventhnightzombies-dormant-population-v1` |
| Retains variant / HP / cooldowns | yes | **no** — only Normal vs Alpha |
| Counted toward `MaxActiveZombies` | yes (alive + loaded) | no |
| Counted toward `DormantPopulationCap` | no | yes |
| Simulated | full AI | migrates once per sunset |

**Important:** an active zombie in a chunk that unloads stays an *entity* saved in
that chunk. It is **not** converted to a dormant count and does not migrate.
Dormancy only happens through the idle-expiry pipeline below.

## 3. How zombies enter the dormant pool

| Source | Code | Respects `DormantPopulationCap`? |
|---|---|---|
| Idle expiry: no prey and no Survival player within `ZombieKeepActiveRadiusBlocks` (64) for `ZombieIdleDespawnSeconds` (300 s) → `Die(Expire)` → `OnEntityDespawn` → `ZombiePopulation.OnDespawn` (reasons `Expire` or `OutOfRange` only) | `Zombie.OnGameTick`, `ZombiePopulation.OnDespawn` | yes — at cap the zombie is simply removed |
| Death-created Alpha that finds no safe spawn site after respawn | `SeventhNightSystem.OnRespawn` → `AddPending(alpha)` | yes |
| First trader seen in a zone: `ZoneTraderDormantSeed` (10) normals | `LastWatchZones.OnPotentialTrader` | yes |
| `/snseed` (10 in your zone), `/snseedneighbor` (10 one zone east) | `LastWatchZones` commands | yes |
| `/zombieween`: 5 normals per recorded zone | `ZombiePopulation.AddEventDormant` | **no** (deliberate) |
| Migration | `ZombiePopulation.ApplyMigration` | n/a — transfers existing counts |

Never dormant: deaths (they become corpses → loot), `Removed` despawns (admin
removal, pause, corpse cleanup), `Unload` despawns (entity persists in chunk).

### Duplication guard

`OnDespawn` sets `IsDormant` and the watched attribute
`seventhnight-dormant-counted` **before** adding the count. If that entity is
ever loaded again (callback retry, unload race), `OnLoaded` sees the marker and
despawns it with `Removed` without adding another count. Corpses
(`CorpseAgeKey` set) are never counted.

## 4. Reactivation (the only exit)

Driven by `SeventhNightSystem.Tick` every `DormantWakeIntervalSeconds` (5 s):

```
for each zone containing ≥1 online Survival player in dimension 0 (random order):
    skip unless Population.HasDormantInZone(zone)
              and LastWatchZones.CanReactivateDormant(zone)   // ambient slots == 0 AND daily wake limit not hit
              and SpawnRetryBackoff.Ready(zone)
    budget = min(DormantWakeBatchSize=3, remaining global DormantMaxTotalWakesPerPass=8)
    for each player in zone: while budget: TrySpawnNear(player, dormantOnly) ; stop on first failure
```

Global preconditions (`TrySpawnNear`, `TryReactivateAtZone`): spawns not paused,
automatic window open (`ZombieSpawnNightOnly` ⇒ 18:00–06:00;
`ZombieSpawnHordeNightOnly` ⇒ only horde nights), active cap not reached.

Placement (shared with ambient spawning, `TrySpawnNear` ~line 820):
up to `SpawnPositionAttempts` (64) random columns **inside the player's zone**,
20–48 blocks horizontally from that player (75% of attempts within 40), each
tested at heightmap+1 ±`SpawnSurfaceVerticalTolerance` (5): chunk loaded, feet and
head free of collision boxes and liquid, solid ground, unclaimed (if
`ProtectClaims`), `MaxTimeOfDayLight` ≤ `SpawnMaxLightLevel` (12) at feet and head,
no live zombie within 1×2 blocks, ≥ `SpawnMinimumOtherPlayerDistance` (12) from
every player.

On a valid site `ZombiePopulation.TryReactivateAtZone` picks the first chunk in
that zone with a count (Alpha first), calls `Spawn`, and **only after success**
records the reactivation and decrements the count. A new variant is rolled
(Grabber 4% / Hive 2% / Screamer 1% / Bloater 3%); an Alpha count always
returns as an Alpha. If the rolled variant does not fit, the attempt fails and
the count is kept.

Failure handling: `SpawnRetryBackoff` — after 3 consecutive failures in a zone,
wait 15 s, then 30 s, then 60 s (max). Success clears it; leaving the zone
clears it.

### Ordering rule: ambient first

`CanReactivateDormant` returns false while the zone still has NEW ambient slots
(`zombiepopdailyaddlimit`, default 5). So each night a zone first produces up
to five fresh ambient zombies, then starts returning its dormant population.
`DormantReactivationPerZonePerDay = 0` (default) means no daily limit on wakes.

## 5. Invariants and how the code enforces them

| Requirement | Mechanism | Status |
|---|---|---|
| Survives restarts | JSON blob saved on `GameWorldSave`; load validates version, nulls, negatives, duplicate keys | ✔ by code; ⚠ not tested with a live save/reload in this repo |
| No duplication via unload/reload | dormancy only on `Expire`/`OutOfRange`; marker attribute; `Unload` ignored | ✔ by code |
| Counts never negative | decrement only after a spawn succeeds and only when `count > 0`; load rejects negatives; `ApplyMigration` validates source totals before any debit | ✔ by code + unit tests |
| Failed reactivation never destroys population | decrement after success only | ✔ |
| Reactivation bounded | 3 per zone / 8 global per 5 s pass, failures count; 64 placement attempts per try; backoff | ✔ |
| Active cap enforced | `Spawn()` checks `ActiveZombieCapReached` for every source | ✔ |
| Dormant ≠ ambient | separate counters; ambient quota never spent by wakes, wakes never create new population | ✔ |
| Corrupt save never overwritten | load exception → `zonesLoaded/populationLoaded=false` → no save, server shutdown scheduled | ✔ by code |
| Migration conserves counts | atomic transaction; `CountStored()` compared before/after; `VerifyDormantCount()` repairs counter drift | ✔ by code + unit tests (2.0.10) |

## 6. Removed dead code (2.0.10)

v2.0.9 contained never-called wake paths (`WakeNearby`, `ForceWakeCheck`,
`WakeMigrantsInOccupiedZones`, `MoveDormant`, `NearPlayer`, `TryWake`,
`CandidatesNear`, `WithinWakeRadius`) and the never-set
`DormantChunk.AwaitDeparture` flag. They were removed in 2.0.10, together with
the settings that only they read (`DormantWakeOnlyInSameZone`,
`DormantWakeRadiusChunks`, `DormantMaxChunkAttemptsPerCheck`,
`DormantDebugLogging`). Wakes have always been zone-only; nothing changed in
behaviour. Old dormant snapshots still load: their `"AwaitDeparture": false`
fields are ignored. The source is preserved on branch `baseline/v2.0.9` if a
radius-based wake mode is ever wanted.

## 7. Zombieween interaction

`/zombieween` builds a target set = saved zone states ∪ zones holding dormant
counts ∪ explored-zone ledger ∪ zones of online players, then adds 5 dormant
normals to chunk `(zone.X*3+1, SeaLevel/32, zone.Z*3+1)` of each, bypassing the
cap, and saves immediately. Nothing is spawned. Because the pool is then above
`DormantPopulationCap`, **natural idle expiries are discarded rather than stored
until the pool falls below the cap again** (by design, see ZOMBIEWEEN-ADMIN.md).
On large worlds the pool can reach hundreds of thousands — see KNOWN_ISSUES.

## 8. Testing hooks

* `/lastwatch` — zone active/dormant counts, quotas, spawn status, buildings.
* `/zombiemap` + open map — red = loaded zombies, orange = dormant.
* `/snseed`, `/snseedneighbor` — seed counts; `/snambientreset` — reopen ambient slots.
* `/zombiecycle` — run migration without resetting daily allowances.
* Unit coverage: `tests/Program.cs` (seed/migration conservation, reset rules).
