# Known Issues and Risks (v2.0.9 audit)

Audit performed 2026-11 by static reading of the v2.0.9 source, a clean build,
the regression suite, and decompilation of the VS 1.22.7 reference assemblies
where engine behaviour mattered. **No in-game or dedicated-server session was
run during this audit.**

Confidence levels:

* **Confirmed (code)** — follows deterministically from the source / engine
  code; not yet reproduced in a running game.
* **Plausible** — likely from the code but depends on runtime engine behaviour
  that was not verified.
* **Design question** — works as written; may or may not be intended.

Severity: High (data loss / broken core loop), Medium (gameplay bug or notable
perf risk), Low (cosmetic, diagnostics, docs).

| # | Issue | Area | Confidence | Severity |
|---|---|---|---|---|
| 1 | Active horde does not survive a server restart | Horde | Confirmed (code + engine) | Medium |
| 2 | `/zombieween` can silently lose a cure dose on partial inventory insert | Zombieween | Confirmed (code + engine) | Low |
| 3 | Infection can become uncurable: fatal clock keeps running during treatment; no config cross-check | Infection | Confirmed (code) | Medium |
| 4 | "Ghost" strongholds after non-player removal keep attracting and reinforcing | Settlements/siege | Confirmed (code) | Medium |
| 5 | Unreachable rot-bait pile pacifies zombies indefinitely | AI / bait | Confirmed (code) | Medium |
| 6 | Scheduled horde silently skipped when no Survival player is online at 17:55–18:00 | Horde | Confirmed (code) | Low–Medium (design?) |
| 7 | Migration conservation check can never fire | Population | Confirmed (code) | Low |
| 8 | Inert / dead configuration and code paths | Config | Confirmed (code) | Low |
| 9 | Shipped `DEFAULT-*.json` and README out of date with source | Docs | Confirmed (tool) | Low |
| 10 | Per-ground-storage 2 s tick listener for bait detection | Performance | Confirmed (code) | Medium (large servers) |
| 11 | Sunset industry scan (~304k block reads per zone) on main thread | Performance | Confirmed (code) | Medium (many players) |
| 12 | Unbounded explored-zone ledger and `/zombieween` pool growth | Performance/persistence | Confirmed (code) | Medium (large worlds) |
| 13 | Opening horde wave at 17:55 may be blocked by daylight | Horde | Plausible | Low–Medium |
| 14 | Prey detection ignores walls; sneaking never loses an acquired target | AI | Design question | Low |
| 15 | Client funeral-mask durability uses client's local config | Equipment | Confirmed (code) | Low |
| 16 | Static Harmony instance shared by client and server sides | Compatibility | Plausible | Low |

---

### 1. Active horde does not survive a server restart

`hordeUntilMs`, `nextHordeWaveMs` and the opening-wave retry state are in-memory
values based on `World.ElapsedMilliseconds`, which is a server `Stopwatch`
(`ServerMain.totalUnpausedTime`) that restarts at zero. On restart `Horde` is
false. `seventhnight-last-natural-horde-night` *is* persisted, so the scheduled
horde for that night is considered done and will not resume.
Files: `src/SeventhNightSystem.cs` (`Horde`, `BeginHorde`,
`TryStartScheduledHordeBeforeMigration`).
Fix direction: persist the remaining horde duration (and natural/manual flag) in
SaveGame data on `GameWorldSave`, restore on load.

### 2. `/zombieween` can lose a cure on partial insert

`PlayerInventoryManager.TryGiveItemstack` (VintagestoryLib 1.22.7, decompiled)
returns `true` if *any* items moved; leftovers stay in the passed stack.
`/zombieween` only spawns the leftover on the ground when the call returns
`false` (`src/LastWatchZones.cs` ~line 188). A player who can accept exactly one
cure receives one; the second is discarded. Fix: after the call, always drop
`stack.StackSize > 0`.

### 3. Infection can become uncurable

`Rules.IsFatalInfection` compares calendar days since infection with
`InfectionFatalDays` regardless of doses taken. With defaults (6 doses, ≥1 day
apart, fatal at 7 days) the last dose lands at `firstDose + 5 days`, so a player
who takes the first dose later than ~2.0 days after infection cannot finish.
`Config.Validate` clamps `CureDosesRequired` (1–10), `CureDoseIntervalDays`
(0.05–30) and `InfectionFatalDays` (1–365) independently, so e.g.
`CureDosesRequired=10` with defaults makes a cure impossible for everyone.
Death + respawn clears infection, so nothing gets permanently stuck, but the
player is guaranteed to die. `/zombieinfection` shows the fatal countdown, not
the latest viable start. Decide the intended rule (pause/extend the fatal clock
while under treatment? warn?) and add a validation cross-check.

### 4. Ghost strongholds

Stronghold blocks are plain `Block`s (no block entity), so zombies may break
them via `TryDamageWallBlock` → `BlockAccessor.BreakBlock(pos, null)` (unless
claimed). `LastWatchZones.Broken` is only reached for player breaks and returns
early when `player == null`, and stronghold records are never re-validated
(bells are, during migration; bonfires are validated via their BE). A destroyed
stronghold therefore keeps: +0.40 migration attraction, the permanent-settlement
flag path, and **auto-reinforcement of every new block placed in that zone** at
its tier strength. The same applies to explosions, WorldEdit, or other mods
removing the block. Fix: validate stronghold positions (when loaded) during
`Placed`/migration as is done for bells.

### 5. Unreachable rot bait pacifies zombies

`Zombie.TickRotBait` runs before prey scanning and returns `true` while any
loaded concentrate pile is within 32 blocks, resetting the idle timer. If the
pile cannot be reached (sealed room, other side of a wall, different floor) the
zombie keeps re-routing every 2 s, never targets players, and never expires.
This works even during hordes. Fix direction: give up on a pile after N failed
routes / T seconds without progress and blacklist it for that zombie for a
while; optionally keep player targeting within melee range.

### 6. Scheduled horde skipped without an online player at sunset

`TryStartScheduledHordeBeforeMigration` requires a Survival player in dimension
0 between 17:55 and 18:00 (or at the migration roll). A player joining at 19:00
on the seventh night gets no horde, and the night is not marked as done (so it
also never runs later). Probably acceptable for multiplayer, surprising for
small servers. Design question.

### 7. Migration conservation check is vacuous

`ZombiePopulation.ApplyMigration` compares `DormantCount` before and after, but
`DormantCount` is the `dormantCount` field, which the method never changes —
it is not recomputed from `chunks`. The check cannot detect a real conservation
error. Fix: compare against `chunks.Values.Sum(c => c.Normal + c.Alpha)`.

### 8. Inert / dead configuration and code

* Never-called population methods: `WakeNearby`, `ForceWakeCheck`,
  `WakeMigrantsInOccupiedZones`, `MoveDormant`, `NearPlayer`, `TryWake`,
  `CandidatesNear`. Hence `DormantWakeOnlyInSameZone=false`,
  `DormantWakeRadiusChunks`, `DormantMaxChunkAttemptsPerCheck`,
  `DormantDebugLogging` have **no effect** (the main config advertises the
  first as a real option).
* `DormantChunk.AwaitDeparture` is never set; all checks on it are no-ops.
* Inert migration keys (see `MIGRATION.md` §7), `HordeLeadMinutesBeforeMigration`,
  `ManualHordeZombieDetectionRadius`, `ZoneActivitySurface*`, `HitsToInfect`
  (superseded), `SneakLoseTargetChance` (`Rules.CanRetainSneakingTarget` unused).
* All `Upgrade*Defaults()` methods except `UpgradeLastSeenPursuit` are never
  called (intentional per a comment in `StartServerSide`). Two of them would
  conflict if ever re-enabled (`SiegeNavigationRefreshSeconds` 2→5 vs 5→2).
* `DetectionRange` is clamped to a minimum of 32, so it cannot be lowered.
* `Finite()` NaN fallbacks differ from declared defaults for
  `ZoneBaseDailyMigrationChance` (0.08 vs 0.02) and `ZoneStrongholdBonus`
  (0.20 vs 0.40).
* The 3-ring bell message hardcodes "30" although the threshold is configurable.

### 9. Stale shipped references

`tools/ConfigDump` reports 58 differences between source defaults and the
shipped `DEFAULT-thelastwatchzombiesconfig*.json`: all ten variant settings are
missing, `ZombieLostTargetPursuitSeconds` is 10 (source 25), 21 retired keys are
still listed. The original `README.md` mixes 2.0.5-rc.1 instructions and package
names with 2.0.9 notes. `docs/CONFIGURATION.md` is generated from source and
supersedes both for defaults.

### 10. Bait listener on every ground-storage pile

The patch adds `LastWatchRotBait` to **all** vanilla ground-storage blocks; each
loaded pile registers a 2 s tick listener that scans its inventory, whether or
not it holds concentrate. On bases with thousands of piles this is a constant
background cost. Fix direction: refresh on inventory-change events / on
placement of concentrate only.

### 11. Sunset industry scan

`ScanIndustryInLoadedZone` reads every block from 8 below to 24 above the
surface across all 96×96 columns (≈304k `GetBlock` calls) per zone with a
player or stronghold, synchronously during the migration tick, stopping early
only when forge, charcoal and anvil are all found. 20 dispersed players ≈ 6M
reads in one tick. Fix direction: cache per zone, scan incrementally across
ticks, or track industry blocks on placement.

### 12. Growth on large worlds

`ExploredZones` grows by one entry per 3×3-chunk area ever loaded and is
re-serialized on every world save. Each `/zombieween` adds a dormant chunk per
recorded zone (5 counts each), repeatable. `/lastwatch` and the map snapshot
copy the entire dormant pool (`MigrationSnapshot`) per call; migration does one
RNG draw per dormant individual. Needs measurement (see `TESTING.md` stress
plan) before large public events.

### 13. Opening horde wave in daylight

Scheduled hordes begin at 17:55. With `HordeSpawnIgnoreLightLevel=false` and
`SpawnMaxLightLevel=12`, outdoor `MaxTimeOfDayLight` at 17:55 may exceed 12 so
the opening wave falls short; the code has a 2-minute retry window for exactly
this case. Verify in-game how many opening zombies actually appear.

### 14. Detection model

`FindPrey` uses distance only (no line of sight), so zombies detect players
through walls and floors within 32 blocks (80 during hordes, +32 for Alphas).
Sneaking only reduces *acquisition*; once targeted, sneaking does nothing.
Crouching with a usable funeral mask, or wearing the rot tunic, makes a player
completely untargetable.

### 15. Client mask durability

`StartClientSide` sets funeral-mask max durability from the client's own local
ModConfig (normally absent → defaults). A server with custom
`FuneralMask1Durability`/`OtherFuneralMaskDurability` shows wrong durability
bars on clients. Server-side wear is authoritative.

### 16. Static Harmony instance

`CompatibilityHooks.harmony` is static; in single-player the client and server
mod instances share it. `Install` is guarded, but whichever side disposes first
unpatches for both. Low impact (both sides normally dispose together).
