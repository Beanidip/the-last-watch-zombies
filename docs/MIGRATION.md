# Migration and Settlement Ecology

Source: `LastWatchZones.RunMigration` (`src/LastWatchZones.cs` ~line 820) and
`ZombiePopulation.ApplyMigration`. Only **dormant counts** migrate; live
entities never move between zones by this system.

## 1. Schedule

`LastWatchZones.Tick` runs every 5 s. When the calendar hour ≥
`ZoneMigrationHour` (18:00) and the current day number > `LastDailyRoll`:

1. `TryStartScheduledHordeBeforeMigration(atMigrationRoll: true)` — guarantees a
   due seventh-night horde starts first even after a `/time` jump.
2. `RunMigration(today)` — exactly one roll per in-game day. Skipped days
   (sleeping, time jumps, server offline) are **not** replayed; on load,
   daily allowances are reset once if a roll was missed.
3. Bonfires still burning at sunset book their fuel for the next cycle.
4. Daily allowances reset (ambient/dormant counters, per-player stats,
   `PlayerDeaths`, used bonfire records).

`/zombiecycle` runs the same roll immediately **without** the daily reset.

## 2. Attraction score per target zone

Candidate target zones = zones with a settlement, paths, deaths or strongholds
∪ zones with an online Survival player ∪ zones with industry ∪ zones with more
than `ZoneFarmAnimalThreshold` loaded farm animals.

| Source | Contribution (default) | Notes |
|---|---|---|
| Stone paths | `StonePaths × 0.002` (max 300 paths → 0.6) | block code contains `stonepath`/`stone-path` |
| Player deaths this cycle | `× 0.075` each | reset every sunset |
| Any stronghold in zone | `+0.40` (`ZoneStrongholdBonus`) | flat, any tier |
| Permanent settlement | `+0.30` | ≥25 paths **and** a stronghold |
| Unmasked Survival players present at sunset | `+0.10` each | a player wearing a usable funeral mask (crouching not required) is not counted |
| Livestock | `+0.12` if > 10 loaded sheep/goat/pig/chicken/cow… | loaded entities only |
| Forge | `+0.08` | industry scan, see below |
| Charcoal pit / pile | `+0.10` | |
| Anvil | `+0.06` | |

Industry is found by a once-per-sunset scan of **loaded** columns in zones that
contain a Survival player or a stronghold, from 8 below to 24 above the
heightmap surface (block code contains `forge`, `charcoalpit/pile`, `anvil`).

Score → attraction **radius (zone rings)** via `AttractionTierRadius`:

| Score | Radius |
|---|---|
| < 0.20 | 2 |
| ≥ 0.20 | 4 |
| ≥ 0.40 | 6 |
| ≥ 0.65 | 8 |
| ≥ 0.90 | 10 |

## 3. Pit bonfires (independent lure)

A lit bonfire books its fuel stack count for the upcoming roll at ignition
(so an extinguished fire still counts that day). For the zone of the fire:

| Fuel stacks | Effect |
|---|---|
| ≥ 3 | **No outbound migration** from that zone |
| ≥ 6 / 9 / 12 | Lures dormant zombies from 1 / 2 / 3 rings away |

Bonfire lure weight = `min(0.85, 0.02 + 0.70) / √distance`.

## 4. Per-zombie roll

For every origin zone that holds dormant counts (and isn't bonfire-protected),
a weight table is computed once:

* stay: `0.02 × 0.35 = 0.007`
* each of the 8 neighbours: `0.02 × 0.65 / 8 = 0.001625`
* each attraction target within its radius at ring distance `d`:
  `min(0.85, 0.02 + score) / √d`
* each bonfire lure in range: as above

`chanceToMove = min(0.85, max(0.02, strongest single influence))`.

Then for **each dormant individual** (Normal and Alpha separately): with
probability `chanceToMove`, pick a goal by weight; "stay" is a no-op; otherwise
move one zone step toward the goal (**two** steps if a horde is active at the
roll — which is the case on every scheduled horde night because the horde starts
at 17:55). The landing chunk is random within the destination zone; the height
band is preserved.

All moves are collected into one transaction and applied atomically
(`ApplyMigration` validates every source total before debiting anything; if
validation fails nothing moves and an error is logged).

### Worked examples (defaults)

* **One player alone, adjacent zone with dormant zombies.** Score 0.10 → radius 2.
  Influence at d=1: 0.12. Weights: stay 0.007 + neighbours 0.013 + target 0.12 →
  P(a given zombie moves toward the player today) ≈ 0.12 × 0.1216/0.14 ≈ **10%**.
* **Fortified settlement** (stronghold, 100 paths, 1 player, settlement flag):
  score 0.40+0.20+0.10+0.30 = 1.00 → radius 10. A zone 5 rings away:
  influence 0.85/√5 ≈ 0.38 → roughly a third of that zone's dormant zombies
  step one zone closer every night. Distant hordes take several nights to arrive.
* **Big bonfire** (12 stacks) 3 rings away: 0.72/√3 ≈ 0.42 per zombie per night,
  and the bonfire's own zone keeps everything it has.

## 5. Watchtower bells

After a committed migration, for every zone with a bell (only loaded bells
ring; a loaded position that is no longer a bell is removed):

| Rings | Condition |
|---|---|
| 3 | bell zone holds > `ZoneBellOverrunThreshold` (30) dormant zombies |
| 2 | migrants arrived in the bell's zone |
| 1 | migrants arrived in an adjacent zone |

Players within `ZoneBellRangeZones` (1) zone rings hear the sound and get a
chat message. A global chat line announces the total moved if
`ZoneMigrationChatAnnouncement` is on.

## 6. How players influence migration

* **Draw zombies in:** build roads (paths), strongholds, settle permanently,
  keep livestock, run forges/anvils/charcoal pits, stay in the zone at sunset,
  die (temporarily).
* **Push them away / hide:** wear a usable funeral mask at sunset (removes your
  presence bonus), avoid industry in exposed zones.
* **Control them:** a ≥3-stack bonfire pins a zone's population in place;
  6/9/12 stacks pull zombies from 1/2/3 rings — usable as a decoy away from a
  base, or a trap.
* **Watch them:** bells report arrivals and overruns.
* Strongholds both attract (+0.40) and protect (auto-reinforce newly placed
  blocks in their zone at 5/50/300 strength for wood/copper/iron).

## 7. Inert migration settings

These exist in `Config` and are written to the advanced config file but are not
read by v2.0.9 migration code: `ZoneSettlementThresholdChanges`,
`ZoneSettlementAttractionRadiusZones`, `ZoneBlockChangeAttractionRadiusZones`,
`ZonePlayerDeathAttractionRadiusZones`, `ZoneStrongholdAttractionRadiusZones`,
`ZoneHotspotAttractionRadiusZones`, `ZoneStonePathAttractionRadiusZones`,
`ZonePlayerPresenceAttractionRadiusZones`, `ZoneHighAttractionRadiusZones`,
`ZoneHighAttractionThreshold`, `ZoneRandomDirectionWeightMultiplier`,
`ZoneFuneralMaskPresenceReduction` (masks remove the whole presence bonus
instead), `ZoneStrongholdReinforcement` (tiers are fixed constants).
