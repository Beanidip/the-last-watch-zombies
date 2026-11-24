# Changelog

All notable changes to The Last Watch Zombies. Gameplay versions follow
`modinfo.json`. Repository-only changes (docs, tooling, CI) are listed under
the version they were made against and do not change the packaged gameplay.

## [Unreleased]

### Repository (no gameplay changes)
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
