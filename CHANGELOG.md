# Changelog

All notable changes to The Last Watch Zombies. Gameplay versions follow
`modinfo.json`. Repository-only changes (docs, tooling, CI) are listed under
the version they were made against and do not change the packaged gameplay.

## [2.0.10] — unreleased (branch `fix/2.0.10-stability`)

Gameplay
- Corpses drop 1 `game:gear-rusty` instead of 1 `game:hide-raw-small`. Config
  `CorpseSmallHides` → `CorpseRustyGears` (a saved hide count is carried over
  once; the old key is retired). `/zombieconfig rustygears`.
- Infection: new `CureStartWindowDays` (default 2): the first Mycobiotic Cure
  dose must be taken within 2 in-game days of infection, otherwise it is refused
  and not consumed (window ignored when `InfectionFatalEnabled=false`). While
  treated the fatal deadline is `max(infection + InfectionFatalDays,
  firstDose + CureDosesRequired × CureDoseIntervalDays)`, so a timely course
  always completes. New player attribute `seventhnight-first-cure-dose-day`
  (estimated for saves from 2.0.9). `/zombieinfection` shows the start window.
  `/zombieconfig curestartwindow`. (#6)

Fixes
- Stronghold/bell/bonfire records are validated against the world and blocks
  broken by zombies are forgotten immediately: no more "ghost" strongholds
  attracting zombies and reinforcing new blocks. (#7)
- Zombies give up on an unreachable rot-concentrate pile after 15 s without
  progress and ignore it for 60 s. (#12)
- Migration conservation check now compares recomputed stored counts and
  repairs counter drift. (#3)
- Bell overrun message uses `ZoneBellOverrunThreshold`; NaN fallbacks for
  `ZoneBaseDailyMigrationChance`/`ZoneStrongholdBonus` match their defaults.

Performance (#5, #9)
- Bait index is event-driven (`SlotModified`) instead of a 2 s timer on every
  ground-storage pile.
- Sunset industry scan enumerates loaded block entities instead of ~304k block
  reads per zone. Finished charcoal piles no longer count; active pits do.

Cleanup (#10)
- Removed never-called dormant wake code and the never-set `AwaitDeparture`
  flag; removed 34 inert settings/markers and 10 never-called config upgrade
  routines (old values are ignored and dropped on the next config rewrite).
- `DEFAULT-*.json`, `CONFIG-REFERENCE.md`, `docs/CONFIGURATION.md` regenerated
  from source by `scripts/refresh-config-files.sh`; CI fails on drift.
- 50 new regression assertions (`tests/StabilityRegression.cs`,
  `VariantRegression.RunStability`); 1875 total.

Not verified in a running game or dedicated server.

## Repository onboarding — 2026-11-23 (no gameplay changes)

### Repository
- Imported the v2.0.9 stable source unmodified as the baseline
  (archive SHA-256 `8c43186e…6ad062`, branch `baseline/v2.0.9`, tag `v2.0.9`
  pending — see `docs/AGENT_HANDOFF.md`).
- Added `.gitignore`, `scripts/fetch-vs-server.sh`, `scripts/validate-assets.py`,
  `scripts/gen-config-doc.py`, `tools/ConfigDump`, GitHub Actions CI.
- Added documentation under `docs/` (architecture, features, population,
  migration, configuration, commands, testing, known issues, roadmap, handoff).
- Rewrote `README.md` as a repository overview. The original README text is
  preserved verbatim in `docs/history/README-v2.0.9-original.md` and at the
  baseline commit. Note that `README.md` is copied into the packaged mod ZIP.

## [2.0.9] — stable

From `UPDATE-NOTES.md` / `CHANGELOG-v2.0.1.md` (author's notes, unverified by
this audit beyond code reading):

- Admin-only `/zombieween`: +5 dormant normals per recorded generated/loaded
  zone (bypasses `DormantPopulationCap`), +2 Mycobiotic Cure per online player.
- World-saved explored-zone ledger (includes old saved activity and dormant zones).
- Windows packaging derives the ZIP name from the version and backs up older
  mod ZIPs outside `Mods` before installing.
- Gameplay identical to 2.0.9-rc.2.

## [2.0.9-rc.1]
- Restored vanilla ground-pile placement of Rot Concentrate and the 32-block
  lure (one concentrate eaten per pile every 2 s).

## [2.0.8-rc.1]
- Sharpened wooden stakes: catch zombies within 2 blocks for 3–20 s, 5 damage,
  30 s persisted countdown, then become a loose stick.

## [2.0.5-rc.1]
- `/zombiepause` / `/zombieunpause` (persisted). Rare variants: Grabber 4%,
  Hive 2%, Screamer 1%, Bloater 3%. Tinted taller variants, crate strongholds,
  20 HP per accepted cure dose, consumed bonfire starters.
- Seventh Night Zombies and The Last Watch merged into one mod (mod ID
  `seventhnightzombies` kept).

## [2.0.1]
- `SpawnMaxLightLevel` default 10 → 12 (inclusive) for ambient, dormant and
  death-Alpha placement.

Earlier history is summarised in `docs/history/README-v2.0.9-original.md`.
