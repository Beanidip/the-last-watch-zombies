# Current stable release: 2.0.9 — /zombieween dormant apocalypse

Admin command `/zombieween` adds **five dormant normal zombies per recorded generated/visited Last Watch zone** (3×3 chunks), plus **two Mycobiotic Cure doses to each currently online player**. This does **not** spawn live zombies immediately or change the normal dormant reactivation, migration, lighting, claims, active cap, or ambient spawn quotas. The command is repeatable; each use adds five more per recorded zone. See ZOMBIEWEEN-ADMIN.md.

Event seeding intentionally bypasses `DormantPopulationCap`, since that cap defaults to 250 and would otherwise prevent five additions in every zone. After the event, ordinary dormant additions remain subject to the configured cap. The mod records generated/loaded zones as chunk columns are encountered, and persists that index; a pre-existing world can have historical, *currently unloaded* areas not yet indexed by this mod. They are added to the index when loaded again. Existing zones already known from Last Watch state or dormant counts are also included.

The build script safely moves older zombie mod ZIPs outside the Mods folder into a backup directory before installing the new build. The source kit includes the Windows build script; **this archive is source, not an already compiled mod ZIP**. Build on Windows against Vintage Story 1.22.7 using .NET 10 SDK, and install the resulting `dist/thelastwatchzombies_2.0.9.zip`. The preceding RC2 gameplay source compiled successfully on Windows (as confirmed by the build log); this stable source changes no gameplay code. A live multiplayer acceptance test has not been reported.

# Current update: 2.0.9-rc.1

Restores vanilla ground-pile placement and the 32-block rot concentrate lure. Sneak + right-click a solid floor with concentrate. Zombies prioritize the pile and eat one concentrate per pile every two seconds, then return to normal behavior. The 30-second stake traps remain unchanged. See UPDATE-NOTES.md.

# Previous update: 2.0.8-rc.1

Sharpened wooden stakes catch any zombie within two blocks for 3–20 seconds and deal 5 damage per catch. The first trigger starts a fixed 30-second countdown; other zombies can use the same trap before it becomes a loose stick. Released zombies can be caught again. Countdown progress persists across saves. Recipe and vanilla bell mob model from 2.0.7 remain unchanged. See UPDATE-NOTES.md.

# Previous features

# 2.0.5-rc.1

Taller tinted variants, skep-headed Hive, fortified crate strongholds, 20 HP per accepted cure dose, and consumed bonfire starters. See UPDATE-NOTES.md and RELEASE-PLAYTEST.md.

# The Last Watch Zombies — v2.0.5-rc.1 (unified)

New in this candidate: `/zombiepause` and `/zombieunpause` immediately pause/resume all new zombies and variant-created bee swarms, with world-saved persistence. Existing zombies continue behaving normally. Creative spawn items are also blocked while paused; loading existing saved zombies is allowed.

Four rare non-Alpha variants now appear in ambient and horde spawns, and on ordinary dormant reactivation: Grabber (4%), Hive Zombie (2%), Screamer (1%), and Bloater (3%). The remaining 90% are ordinary zombies. Rates are configurable. Alphas are never selected by the random roll.

- **Grabber:** 24 HP, longer arms and faster movement; jumps/leaps toward visible prey within 2–7 blocks, with an 8-second cooldown. Uses collision physics rather than teleporting.
- **Hive Zombie:** 28 HP, visible hive growths; emits a player-hostile vanilla-based bee swarm when prey is nearby, at most once per 30 seconds. Swarms expire after 20 simulated seconds, with local and server-wide emission limits.
- **Screamer:** 18 HP; screams once when it sees prey within 16 blocks and summons ten ordinary zombies over several seconds. Unsafe positions or a full active cap are retried for up to 60 seconds. No recursive Screamer summons. Killing it cancels remaining summons.
- **Bloater:** 40 HP, wider body and slower movement; actual death immediately drops 32 extra rot and bursts within 4 blocks, dealing 4 poison-type damage plus temporary poison (0.5 HP/second for 10 seconds) to nearby Survival players with a clear line. No block damage. Ordinary corpse loot still follows. Natural expiry/removal never bursts. Poison is separate from bite infection and clears on respawn.

Dormant storage remains count-only: non-Alpha special zombies return to the ordinary pool. Their specific type, health, cooldowns and identities are not retained in dormant storage; a new type is rolled on reactivation. Normal chunk saves preserve each existing entity and its ability state.

Admin test commands: `/zombiespawn grabber`, `/zombiespawn hive`, `/zombiespawn screamer`, `/zombiespawn bloater`. They respect pause, loaded space, claims and active cap; they bypass time/light/ambient quotas for testing. Screamer summons also bypass time/light/ambient quotas but require safe space and respect claims/cap/pause.


**Author:** Beani  
**Game:** Vintage Story 1.22.7 Stable  
**Mod ID:** `seventhnightzombies` (kept deliberately to preserve installed item and entity IDs)  
**Compiled package:** `thelastwatchzombies_2.0.5-rc.1.zip`

## Why one mod?

This release merges Seventh Night Zombies and The Last Watch into ONE code mod. There is no dependency ZIP, no separate ambient-spawning switch, and no per-player quota. The unified 96×96-block zones manage ALL new natural zombie creation; dormant populations and migration are separate. **Do not install either older mod alongside this release.**

## Natural spawning — zone-only

- A **zone** is 3×3 chunks, or 96×96 horizontal blocks.
- Every occupied zone gets ONE ambient spawning opportunity per second, with probability `AmbientSpawnChancePerSecond` (0.30 by default). This does **not** scale with the number of players in the zone.
- Only real Survival players occupy a zone for spawning; Creative/Spectator do not.
- A successful new spawn counts toward `zombiepopdailyaddlimit` (**5 per zone per sunset cycle** by default). Unsuccessful placement never spends quota.
- The engine checks loaded terrain, clear head/feet, solid ground, claims, other nearby players, zone membership, time of day (18:00–06:00), and light at/below 12 (inclusive).
- Placement searches within about **20–48 blocks** of a player and stays inside that player's occupied zone. Each opportunity creates at most ONE zombie, regardless of the number of players.
- Nightly new-ambient quota only resets on the next sunset migration cycle; killing those five zombies does NOT create more daily quota. This is deliberate and can make an already-used zone seem empty.
- `MaxActiveZombies` caps total living loaded zombies across the server. Horde waves and admin spawns do not consume zone ambient quota.

## Dormant reactivation

When eligible zombies naturally expire, The Last Watch stores counts in their current zone. This is separate from new ambient creation. A zone with a Survival player can reactivate stored zombies only after its new ambient allowance is fully spent. Defaults: **3 placement attempts per occupied zone per five-second pass**, maximum **8 across all zones per pass**, including failed attempts. `DormantReactivationPerZonePerDay=0` means no daily limit; the system keeps retrying until it can wake everyone, subject to nighttime, light, terrain, claims, and active cap. Failed wakes never erase stored zombies.

`DormantPopulationCap` (**250** default) limits all additions, including natural despawns and horde returns. At capacity, an expiring zombie is removed without adding a stored count. Existing saves above the cap are preserved; no new counts are added until below it. Migration transfers existing counts without spending capacity.

After three consecutive failed placements, a zone retries after 15 seconds, then 30, then at most 60. A success clears this delay; leaving the zone clears its transient retry state. `/lastwatch` reports remaining ambient slots, dormant eligibility, schedule/cap blocks and retry delays. It does not guess local light/terrain causes without a placement attempt; use `/snambienttest` for that.

Zombies remain active near Survival players (64-block horizontal range and 32-block vertical tolerance by default), using cached presence checks. Last-known pursuit now defaults to 25 seconds, then roaming resumes. The five-minute idle expiry remains configurable.

## World simulation

Each sunset (18:00 default), migration shifts dormant populations between neighboring zones according to attraction. Players add 0.10 attraction each unless disguised; paths, player deaths, bell ringing, bonfires, industry, livestock, traders and strongholds can affect attraction. The strongest path is bounded to short migration steps, with a special horde option.

## Zombies and survival features

- **Regular and Alpha zombies:** Alphas are tougher, faster, attract other zombies, and may appear from player deaths. Zombies stalk prey, investigate last-known positions, and try to open doors or break blocks when pursuing a blocked target.
- **Seventh-night hordes:** By default every seventh in-game day near 17:55, running 600 real seconds, with starting waves and reinforcements. Horde spawns ignore the normal daily ambient quota.
- **Infection:** Regular zombie hits infect after four qualifying hits, Alpha hits after two; untreated infection can be fatal after seven game days.
- **Cure:** Rot Concentrate + poison mushroom creates Mycobiotic; use with a bandage for the Mycobiotic Cure. Six daily doses advance psychedelic visuals at strengths 0.4, 0.6, 0.8, 1.0, 1.4, 2.0 (when enabled).
- **Funeral masks:** Wearing a usable mask while crouching provides camouflage and consumes mask durability.
- **Rot Covered Tunic:** 20 durability and 30% slower movement while worn; camouflage and initial sickness retained. Its wear is 1 durability per real second unless configured otherwise.
- **Siege:** Normal reinforced-block damage defaults to 1, horde reinforced-block damage to 2, `ProtectReinforcedBlocks=false`, `ProtectClaims=true`.
- **Loot:** Rot, bones, hide drops, and a rare Alpha funeral mask (all configurable).

## Expansion structures/items

All the following are now native features of this single mod:

- **Watchtower bell** (`seventhnightzombies:watchtowerbell`) — affects migration attraction/range and is shown on the admin map.
- **Wood stronghold** (`seventhnightzombies:strongholdwood`) — weakest tier of fortification/zone influence.
- **Copper stronghold** (`seventhnightzombies:stronghold`) — middle tier; retains existing copper stronghold IDs.
- **Iron stronghold** (`seventhnightzombies:strongholdiron`) — strongest fortification tier.
- **Bonfire pit** (`seventhnightzombies:pitbonfire` plus loaded/lit states) — uses fuel stacks to affect nearby migration.
- **Rot Concentrate, Mycobiotic, Mycobiotic Cure, Rot Covered Tunic, funeral masks** — item codes retain their pre-merge namespace and recipes.

**Custom models/textures** stay under `assets/seventhnightzombies/`: `shapes/block/` and `textures/block/`. Three bonfire shapes are in `shapes/block/pitbonfire/{empty,loaded,burning}.json`. Bells/strongholds currently use simple block geometry; custom JSON shapes require the corresponding blocktype to reference them.

## Commands

- `/zombiehorde` — next horde schedule, frequency, time, living loaded zombies.
- `/zombieinfection` — your infection severity, doses, and treatment countdown.
- `/zombiehordestart` — start an admin horde.
- `/lastwatch` — local zone's active/dormant population, ambient additions, attraction state.
- `/zombiemap` — admin overlay; red line means loaded zombies present, orange dots indicate dormant population.
- `/snspawnstatus` — diagnose spawn eligibility, daily quota, night/light and cap.
- `/snambienttest` — force exactly ONE genuine ambient placement attempt subject to all rules.
- `/snambientreset` — **testing only**: reset current zone's new ambient quota, not dormant or world state.
- `/snspawn` — admin entity summon, bypassing ambient quota but not active cap.
- `/snseed`, `/snseedneighbor` — seed dormant zone zombies for testing.
- `/zombiecycle` — run the migration roll now without resetting daily allowances.
- `/zombieconfig list` — see editable settings (saved for restart).
- `/zombieperf`, `/zombiediagnostics` — optional performance diagnosis.

## Configuration

Delete/rename obsolete `seventhnightconfigmain.json`, `seventhnightconfigadvanced.json`, `thelastwatchconfigmain.json`, `thelastwatchconfigadvanced.json` if you wish; this merged mod does not read them. It writes only:

- `VintagestoryData/ModConfig/thelastwatchzombiesconfigmain.json` — spawning, ambient cap, dormant cap/batching, horde and combat, infection, loot, block breaking, attractions and migration.
- `VintagestoryData/ModConfig/thelastwatchzombiesconfigadvanced.json` — fine-grained scan/AI budgets, advanced migration weights, sounds, spawn validation and compatibility markers.

See `CONFIG-REFERENCE.md` for a generated full key-by-key default index.

## Upgrading from two mods

1. **Back up the world and ModConfig** before switching. Test on a *copy* of your existing world.
2. Delete/move `seventhnightzombies_*.zip` and `thelastwatch_*.zip` compiled mods out of your Mods folder. Also remove any old `thelastwatchzombies_*.zip`.
3. Compile THIS source zip using `BUILD-WINDOWS.ps1`; install only `thelastwatchzombies_2.0.5-rc.1.zip`.
4. Keep the internal `seventhnightzombies` ID, item/entity IDs, and Last Watch save keys intact. Existing dormant populations and built structures **should be readable**, but live upgrade still needs verification.
5. Create fresh merged configs at startup. Test `/snspawnstatus`, `/snambienttest`, `/lastwatch` at night.

This is a **compiled release candidate**. Compilation and executable regressions pass against Vintage Story 1.22.7. Dedicated-server startup was smoke-tested. Connected-client gameplay and copied-world upgrade testing remain required. See RELEASE-REVIEW.md and RELEASE-PLAYTEST.md.
