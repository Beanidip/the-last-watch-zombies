# Agent Handoff — persistent development context

> Read this first. Then `docs/ARCHITECTURE.md`, `docs/KNOWN_ISSUES.md`,
> `docs/DEVELOPMENT_ROADMAP.md`. Update the **State** and **Dev log** sections
> at the end of every working session and commit them.

## State (update every session)

| Field | Value |
|---|---|
| Project version | **2.0.10** (unreleased) on `fix/2.0.10-stability`; `main` = 2.0.9 gameplay + docs/tooling until merged |
| Game target | Vintage Story 1.22.7, .NET 10 |
| Repository | https://github.com/Beanidip/the-last-watch-zombies (visibility: **public** — owner confirmed public is fine) |
| Primary branch | `main` |
| Current development branch | `fix/2.0.10-stability` (gameplay fixes). Use `feature/<topic>` or `fix/<issue#>-<slug>` branches for code changes |
| Pristine baseline | commit `b1f4675` on branch **`baseline/v2.0.9`** (never move/rebase it). Tree is byte-identical to `TheLastWatchZombies_Source_v2.0.9_Stable.zip` (SHA-256 `8c43186eb50a11f65ef7620f56a744259054bef74c7e51f985b5facc5b6ad062`). Parent `11b5b30` is the owner's GitHub "Initial commit" (LICENSE only) |
| Tag `v2.0.9` | **Not on GitHub yet.** The cloud agent session's proxy blocks tag pushes. Owner: run the command below once |
| Last completed task | Horsetail poultice as alternative cure binder; infection shown in the Traits tab (on top of the 2.0.10 stability fixes) |
| Build status | ✅ Release build 0 warnings / 0 errors (local Linux + GitHub Actions) |
| Test status | ✅ 1903/1903 regression assertions locally on 2.0.10. Owner reported the first 2.0.10 build working in game (CI result: see the branch's latest Actions run). ❌ no dedicated-server or in-game test performed by agents yet |
| Open known issues | `docs/KNOWN_ISSUES.md`: 8 fixed in 2.0.10 (code + tests), 8 open (#1 smoke test, #2/#8 zombieween cure loss, #11 horde restart, #12-scale growth, #13 late-join horde, …) |
| Immediate next steps | Owner review/merge of `fix/2.0.10-stability`; dedicated-server smoke test (#1); in-game run of TESTING §4 F/G/H; then #11 horde persistence and #8 zombieween cure loss |
| Issue backlog | #1 build smoke test · #2 population tests · #3 migration tests + vacuous check · #4 spawning verification · #5 AI perf · #6 infection/cure feasibility · #7 siege/ghost strongholds · #8 zombieween cure loss/growth · #9 stress tests · #10 docs/config review · #11 horde restart · #12 unreachable bait · #13 late-join horde (design) |

Create the tag (owner, from any clone):

```bash
git fetch origin baseline/v2.0.9 && git tag -a v2.0.9 -m "The Last Watch Zombies 2.0.9 stable — pristine source baseline" origin/baseline/v2.0.9 && git push origin v2.0.9
```

## Architecture in one screen

* Two cooperating ModSystems: **`SeventhNightSystem`** (config, 1 s tick,
  spawning, hordes, infection, death Alphas, active-zombie ledger, most
  commands) and **`LastWatchZones`** (zones, quotas, buildings, sunset
  migration, zombieween). They hard-require each other.
* **`ZombiePopulation`** = dormant pool of per-chunk `Normal`/`Alpha` counts,
  indexed by 3×3-chunk `WatchZone`. Entry: idle expiry (`Expire`/`OutOfRange`
  despawn), trader seed, admin seeds, death-Alpha fallback, zombieween.
  Exit: `TryReactivateAtZone` only (after a successful spawn).
* **`Zombie : EntityAgent`** — one class for six entity codes; server-side AI
  in `OnGameTick` with shared scan/route budgets (`WorkBudget`).
* Order of a zone's night: 5 NEW ambient zombies first, then dormant wakes in
  batches (3/zone, 8/global per 5 s).
* Persistence = SaveGame JSON blobs (keys in `ARCHITECTURE.md` §6) + entity
  attributes. Load errors shut the server down rather than overwrite saves.
* Config: two ModConfig JSONs, rewritten on every server start;
  `docs/CONFIGURATION.md` is generated from source.

## Ground rules for agents

1. **Never** modify, rebase or force-push `baseline/v2.0.9`, or delete history.
2. Gameplay changes go on a branch, with a regression test (extend the
   `Fixture` fake server in `tests/VariantRegression.cs`) and a CHANGELOG entry.
3. Preserve save compatibility: mod ID, entity/item/block codes, SaveGame keys,
   JSON field names. Bump the snapshot `Version` with a migration path if a
   format must change.
4. Never commit Vintage Story assemblies, compiled DLLs, dist ZIPs, worlds or
   secrets. Use `scripts/fetch-vs-server.sh` (git-ignored `vs-server/`).
5. Never publish a release or upload the compiled mod without the author's
   explicit authorization. Repository is public: assume everything pushed is public.
6. Never claim a test passed or an in-game behaviour was observed unless it was
   actually run. Record results in `docs/TESTING.md` §3.
7. Don't overwrite users' ModConfig values; config default changes need an
   explicit, versioned migration (see roadmap M3).
8. Keep `modinfo.json` version and csproj `<Version>` equal (CI enforces).
9. Keep this file's State table and the Dev log current; commit before stopping.

## Environment notes (cloud agent sessions)

* `gh` works through a proxy that allows **repository-scoped** API calls only:
  issues, labels, PR reads and workflow-run reads work; creating repositories,
  changing repo settings, creating refs/tags via API and **pushing tags** were
  refused (HTTP 403 / remote hang-up). Branch pushes work.
* `dotnet` is not preinstalled: `apt-get install -y dotnet-sdk-10.0` worked
  (dot.net install script host was blocked). `cdn.vintagestory.at` is reachable.
* Starting the dedicated server from an agent session was blocked by the
  session's permission policy in the first session; ask the owner before
  retrying or run it locally.
* `ilspycmd` (`dotnet tool install -g ilspycmd`, run with
  `DOTNET_ROLL_FORWARD=Major`) is handy for checking engine behaviour in
  `VintagestoryLib.dll`.

## Quick commands

```bash
bash scripts/fetch-vs-server.sh 1.22.7 vs-server
dotnet build TheLastWatchZombies.csproj -c Release -m:1 -t:Rebuild -p:VintageStoryPath=$PWD/vs-server
dotnet run --project tests/Regression.csproj -c Release -p:VintageStoryPath=$PWD/vs-server
python3 scripts/validate-assets.py
dotnet run --project tools/ConfigDump -c Release -p:VintageStoryPath=$PWD/vs-server -- config-dump .
python3 scripts/gen-config-doc.py config-dump > docs/CONFIGURATION.md
```

## Dev log (newest first)

### 2026-11-24 — live /zombieconfig, quick start (Claude Code, cloud)
- Owner: hold the merge; add missing short names, live config apply, and an
  in-ZIP quick start.
- `AdminConfiguration.ApplyLive`: set the field on the running Settings, then
  `Validate()` (keeps dependent pairs consistent) and sync `Profiler.Enabled`.
  Audit found only profiler + mask durabilities cached at startup (masks not exposed).
- 9 new short names (60 total). `QUICKSTART.md` generated by
  `gen-config-doc.py --quickstart`, packaged via the csproj, checked in CI.
- Tests 1903/1903. Merge of `fix/2.0.10-stability` on hold per owner.

### 2026-11-24 — infection chat cleanup (Claude Code, cloud)
- Owner confirmed poultice recipe + Traits tab work in game.
- Removed per-bite "Zombie wound" chat; infection now sends one localized line
  (`infected-acquired`) via `SeventhNightSystem.Text` (English fallback; tests
  run without a loaded language). Cure-dose messages unchanged.
- Tests 1888/1888 (fixture now records chat via `SilentServerPlayer.Messages`).

### 2026-11-24 — cure recipe + Traits tab (Claude Code, cloud)
- Owner confirmed the 2.0.10 build works in game; asked for the horsetail
  poultice as a cure ingredient and infection under Traits.
- Recipe file is now an array: bandage-clean OR `game:poultice-*-horsetail`
  (reed + linen; honey-sulfur excluded). `validate-assets.py` asserts it.
- Vanilla Traits tab (`CharacterSystem.getClassTraitText`) only lists class
  traits — `extraTraits` would NOT show — so a Harmony postfix appends a line.
  Server publishes deadlines via `SyncInfectionDisplay` (writes only on change).
- Tests 1885/1885. Not yet seen in game.

### 2026-11-24 — 2.0.10 stability session (Claude Code, cloud)
- Owner requested: rusty gears instead of raw hide; 2 days to start the cure;
  fix #3, #5/#9 lag sources, #7, #10, #12; clean up; keep JSON valid.
- Branch `fix/2.0.10-stability`, version bumped to 2.0.10 (modinfo + csproj).
- Loot: `game:gear-rusty` (`CorpseRustyGears`, migrates `CorpseSmallHides`).
- Cure: `CureStartWindowDays=2`; treated deadline `firstDose + doses×interval`;
  window ignored when fatality is disabled (avoids a permanently poisoned player).
- #7 `ValidateBuildings`/`ForgetBlock`; #12 `BaitPursuitState`; #3
  `CountStored`/`VerifyDormantCount`; #5 event-driven bait index; #9 block-entity
  industry scan (charcoal piles no longer count — note for the author).
- #10: removed dead wake code, `AwaitDeparture`, 34 inert settings and 10 dead
  upgrade routines; added `scripts/refresh-config-files.sh` (+ CI `--check`).
- Tests 1875/1875 (50 new, mutation-checked). All 49 JSON files valid. Build 0/0.
- Not run in game. `DetectionRange` min clamp (32) intentionally left as is.

### 2026-11-24 — onboarding session (Claude Code, cloud)
- Extracted the v2.0.9 archive, verified the tree is identical, committed it as
  `b1f4675` on top of the owner's `11b5b30` (LICENSE-only initial commit). The
  baseline commit replaces that LICENSE with the archive's (MIT, "Beani" instead
  of "Beanidip"; otherwise the same text, re-wrapped). Pushed `main` and
  `baseline/v2.0.9`. Tag push blocked (see above).
- Installed .NET 10.0.115 and fetched the VS 1.22.7 server; clean Release
  build 0/0; 1825/1825 tests. Added tooling + CI; CI run green.
- Full source read; wrote docs; generated the config reference (194 active-file
  keys + 21 retired) from source; found 58 drift entries vs the shipped DEFAULT files.
- Decompiled `TryGiveItemstack` and `ServerMain.ElapsedMilliseconds` to confirm
  KI#2 and KI#1.
- Dedicated-server smoke test attempted but blocked by session policy, **not run**.
- Created GitHub issues #1–#13 (labels
  `bug`, `performance`, `enhancement`, `testing`, `documentation`, `build`,
  `needs-verification`).
