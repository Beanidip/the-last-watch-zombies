# Commands and Permissions

All commands are registered server-side in `StartServerSide`. Verified against
v2.0.9 source; syntax is exactly as registered (VS chat command API).
`controlserver` is the vanilla admin privilege (server console has it);
`chat` is held by every normal player.

## Player commands (`chat`)

| Command | Args | Source | Effect |
|---|---|---|---|
| `/zombiehorde` | — | `SeventhNightSystem.cs` | Next scheduled horde (in-game time), frequency, active zombies / cap. Also sent to each player on join (`PlayerReady`). |
| `/zombieinfection` | — | `SeventhNightSystem.cs` | Your infection state, poison rate, doses taken / required, time left to start the cure (2-day window) or next-dose time, fatal countdown. Player only. |

## Admin commands (`controlserver`)

### Hordes and spawning

| Command | Args | Effect / notes |
|---|---|---|
| `/zombiehordestart` | — | Start a horde now (duration `HordeDurationSeconds`). Requires ≥1 Survival player in dimension 0; refused while paused. If tonight is a scheduled horde night it counts as that night's horde. |
| `/zombiepause` | — | Pause **all new** zombies (ambient, hordes, dormant wakes, death Alphas, screamer summons, bee swarms, creative spawn items). Saved in the world. Existing zombies keep acting. `/zombieween` is not affected (it spawns nothing). |
| `/zombieunpause` | — | Resume. |
| `/zombiespawn <variant>` | `normal\|alpha\|grabber\|hive\|screamer\|bloater` | Spawn one variant 4–10 blocks from you. Respects pause, loaded space, claims and the active cap; ignores time, light and quotas. |
| `/snspawn` | — | Spawn one zombie (random variant roll) 3 blocks east of you. Ignores quotas/time/light; respects pause and cap. |
| `/snambienttest` | — | Force one *genuine* ambient attempt for your zone with every normal rule; on failure prints counts per rejection reason (zone/quota, unloaded chunk, ground/clearance, light, claims, nearby players, cap). A success spends one ambient slot. |
| `/snambientreset` | — | **Testing only.** Reset your zone's NEW-ambient counter (not dormant, not settlement data). |
| `/snspawnstatus` | — | Explain spawn eligibility: pause, Survival, clock, schedule window, active count/cap, zone allowance, light limit, ring, chance. |

### Population and migration

| Command | Args | Effect / notes |
|---|---|---|
| `/lastwatch` | — | Your zone: active zombies in zone, dormant (zone/global/cap), ambient used/limit, dormant wakes today, your contribution, spawn status, settlement, paths, deaths, strongholds, bells, bonfire state, migration hour. Player only. |
| `/zombiemap` | — | Toggle the admin map overlay **for this session** (not saved); then close/reopen the map (M) to receive one snapshot. Red = loaded zombies, orange = dormant. Max 10,000 zones per snapshot. |
| `/zombiecycle` | — | Run a migration roll now. Does **not** reset daily allowances or change `LastDailyRoll`. Saves population and zones. |
| `/snseed` | — | Add 10 dormant normals to your zone (respects `DormantPopulationCap`). Survival player only. |
| `/snseedneighbor` | — | Add 10 dormant normals to the zone directly east (+X). |
| `/zombieween` | — | Apocalypse event: +5 dormant normals in every recorded zone (bypasses the dormant cap) and 2 Mycobiotic Cure to every online player (ground drop if inventory full — see KNOWN_ISSUES #2). Repeatable; saves immediately; spawns nothing. Console or admin. |

### Configuration and diagnostics

| Command | Args | Effect / notes |
|---|---|---|
| `/zombieconfig` | `list` \| `<name>` \| `<name> <value>` | List short names; show running vs saved value; validate a value, save it to the owning ModConfig file and **apply it immediately** (no restart). Booleans accept `true/false/on/off`. Values outside the allowed range are rejected with the nearest valid value. |
| `/zombiediagnostics` | `on` \| `off` | Shortcut for `/zombieconfig diagnostics …` (`PerformanceDiagnostics`). Takes effect immediately. |
| `/zombieperf` | — | Print profiler counters since diagnostics were enabled (`system.tick`, `ai.entityTick`, `ai.preyScans`, `ai.routes.*`, `spawn.*`, `migration.*`, `map.snapshot` …). |

## `/zombieconfig` short names

| Short name | Config field | Short name | Config field |
|---|---|---|---|
| `grabberchance` | `GrabberSpawnChance` | `routebudget` | `AiRouteRequestsPerSecond` |
| `hivechance` | `HiveSpawnChance` | `scanbudget` | `AiPreyScansPerSecond` |
| `screamerchance` | `ScreamerSpawnChance` | `scaninterval` | `AiPreyScanIntervalSeconds` |
| `bloaterchance` | `BloaterSpawnChance` | `lastseen` | `ZombieLostTargetPursuitSeconds` |
| `leapcooldown` | `GrabberLeapCooldownSeconds` | `keepaliverange` | `ZombieKeepActiveRadiusBlocks` |
| `beecooldown` | `HiveBeeCooldownSeconds` | `stayactive` | `ZombieStayActiveNearPlayers` |
| `diagnostics` | `PerformanceDiagnostics` | `wakebatch` | `DormantWakeBatchSize` |
| `ambientlimit` | `ZombiePopDailyAddLimit` | `wakeinterval` | `DormantWakeIntervalSeconds` |
| `dormantpopulationcap` | `DormantPopulationCap` | `wakeglobalbatch` | `DormantMaxTotalWakesPerPass` |
| `despawndelay` | `ZombieIdleDespawnSeconds` | `dormantreactivation` | `DormantReactivationPerZonePerDay` |
| `npcinterest` | `NpcTraderInterestSeconds` | `migrationhour` | `ZoneMigrationHour` |
| `nightonly` | `AmbientSpawnRequiresNight` | `migrationchance` | `ZoneBaseDailyMigrationChance` |
| `strictnightonly` | `ZombieSpawnNightOnly` | `migrationmaxchance` | `ZoneMaxDailyMigrationChance` |
| `hordeonly` | `ZombieSpawnHordeNightOnly` | `strongholdbonus` | `ZoneStrongholdBonus` |
| `activecap` | `MaxActiveZombies` | `playerbonus` | `ZonePlayerPresenceBonus` |
| `blockbreaking` | `BlockBreakingEnabled` | `bonfirebonus` | `ZoneBonfireAttractionBonus` |
| `blockbreakinterval` | `BlockBreakIntervalSeconds` | `migrationannounce` | `ZoneMigrationChatAnnouncement` |
| `blockbreakchance` | `BlockBreakChance` | `fatality` | `InfectionFatalEnabled` |
| `nonhordereinforcementdamage` | `NonHordeBlockReinforcementDamage` | `fataldays` | `InfectionFatalDays` |
| `hordereinforcementdamage` | `HordeBlockReinforcementDamage` | `curestartwindow` | `CureStartWindowDays` |
| `protectreinforced` | `ProtectReinforcedBlocks` | `rustygears` | `CorpseRustyGears` |
| `spawnchance` | `AmbientSpawnChancePerSecond` | `protectclaims` | `ProtectClaims` |
| `spawnlight` | `SpawnMaxLightLevel` | `normalhits` | `NormalHitsToInfect` |
| `spawnmindistance` | `SpawnMinDistance` | `alphahits` | `AlphaHitsToInfect` |
| `spawnmaxdistance` | `SpawnMaxDistance` | `curedoses` | `CureDosesRequired` |
| `hordedays` | `HordeEveryDays` | `doseinterval` | `CureDoseIntervalDays` |
| `hordeduration` | `HordeDurationSeconds` | `attackdamage` | `AttackDamage` |
| `hordewaveinterval` | `HordeWaveIntervalSeconds` | `detectionrange` | `DetectionRange` |
| `hordewavemin` | `HordeWaveMinZombiesPerPlayer` | `hordeblockbreakchance` | `HordeBlockBreakChance` |
| `hordewavemax` | `HordeWaveMaxZombiesPerPlayer` | `deathalpha` | `SpawnAlphaOnPlayerDeath` |

Any other setting must be edited in the JSON files while the server is stopped
(see `CONFIGURATION.md`). Server owners: `QUICKSTART.md` (shipped in the mod ZIP)
lists the most useful settings with their short names.

## Discrepancies with older docs

* The brief/README list `/snspawn` as "spawn test zombie" — it rolls a random
  variant like any natural spawn; use `/zombiespawn normal` for a plain zombie.
* `/snspawnstatus` and `/snseedneighbor` exist but were missing from some lists.
* `/lastwatch` requires `controlserver` (it is not a player command).
