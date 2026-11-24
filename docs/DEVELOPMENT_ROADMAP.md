# Development Roadmap

Principles: stability before features; preserve gameplay and save compatibility
(mod ID, entity/item codes, SaveGame keys and JSON shapes); every change ships
with a regression test where the fake-server harness allows it; never claim
in-game verification that was not performed. Issue numbers refer to the GitHub
backlog; `KI#` to `KNOWN_ISSUES.md`.

## M0 — Onboarding ✅ (2026-11)

Repository, pristine baseline, reproducible Linux/CI build, docs, issue backlog.

## M1 — 2.0.10 "Stability" ← **in progress** (code fixes done on `fix/2.0.10-stability`; in-game verification outstanding)

Goal: fix confirmed-by-code defects with no balance changes, plus the first
real dedicated-server verification.

1. **Dedicated-server smoke test** (#1) (headless boot with the mod, fail on
   exceptions/patch errors) — locally and, if feasible, in CI. Gate for all later work.
2. **Horde persistence across restart** (KI#1, #11): persist remaining duration +
   natural flag; restore on load; regression test via fixture.
3. **Zombieween partial-insert cure loss** (KI#2, #8): always drop leftovers.
4. ✅ **Ghost strongholds** (KI#4, #7): validate loaded stronghold records (as bells
   are) on placement-reinforcement and at migration.
5. ✅ **Unreachable bait** (KI#5, #12): give up after repeated route failures / no
   progress; temporary per-zombie ignore.
6. ✅ **Migration conservation check** (KI#7, #3): recompute from chunks.
7. ✅ **Infection/cure feasibility** (KI#3, #6) — author chose a 2-day start window: at minimum, a `Validate()` cross-check
   and a log warning when `(CureDosesRequired-1)×CureDoseIntervalDays ≥
   InfectionFatalDays`; gameplay rule change only with the author's decision.
8. ✅ Refresh shipped `DEFAULT-*.json` from `tools/ConfigDump`; fix bell message
   hardcoded "30"; align NaN fallbacks with defaults.
9. Population tests (#2), then run the full `TESTING.md` §4 plan on a two-player server; record results.

Exit criteria: CI green, all new tests pass, smoke test passes, §4 plan run
with results recorded in `TESTING.md`, CHANGELOG entry, author approval
before any release.

## M2 — Performance on long-running servers

1. Measure first (TESTING §4.J, #9, #5): 250 active zombies, 50k dormant, 2k ground piles,
   10+ player zones at sunset. Record `/zombieperf`.
2. ✅ Bait index without per-pile tick listeners (KI#10).
3. ✅ Block-entity industry scan (KI#11).
4. Avoid full-pool copies in `/lastwatch` and map snapshot; per-zone dormant
   totals maintained incrementally (KI#12).
5. Bound or compact `ExploredZones` (e.g. run-length or region bitsets) while
   keeping the save key backward compatible.

## M3 — Configuration hygiene (#10)

1. ✅ Inert keys removed (KI#8), including `DormantWakeOnlyInSameZone`.
2. Decide on `DetectionRange` minimum clamp (32).
3. ✅ Never-called `Upgrade*` routines removed. Policy for future default
   changes: versioned marker + explicit migration, as `MigrateCorpseDrops` does.
4. Client durability sync for funeral masks (KI#15) — e.g. send server values
   via a small network channel or watched config.
5. Generate `CONFIG-REFERENCE.md` from the same tool or retire it in favour of
   `docs/CONFIGURATION.md`.

## M4 — Gameplay depth (only with the author's direction)

Candidates gathered during the audit — **not commitments**:

* Line-of-sight / noise-based detection (KI#14), sneaking that can lose pursuit.
* Optional retention of variant type in dormant storage (would need a new
  save format version with migration).
* Horde catch-up for players who join after sunset on horde night (KI#6, #13).
* Treatment that pauses or extends the fatal clock (KI#3).
* Admin command to export population statistics.

## Release process (proposal — requires author authorization to publish)

1. Bump `<Version>` and `modinfo.json` together (CI enforces equality).
2. Update `CHANGELOG.md` and `docs/AGENT_HANDOFF.md`.
3. CI green; smoke test; manual plan subset.
4. Tag `vX.Y.Z` on `main`. Build the ZIP from the tag. Publish only when the
   author says so.
