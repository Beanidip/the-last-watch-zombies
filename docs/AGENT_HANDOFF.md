# Agent Handoff — persistent development context

> Read this first. Then `docs/ARCHITECTURE.md`, `docs/KNOWN_ISSUES.md`,
> `docs/DEVELOPMENT_ROADMAP.md`. Update the **State** and **Dev log** sections
> at the end of every working session and commit them.

## State (update every session)

| Field | Value |
|---|---|
| Project version | **2.0.9** (stable gameplay; no gameplay changes since baseline) |
| Game target | Vintage Story 1.22.7, .NET 10 |
| Repository | https://github.com/Beanidip/the-last-watch-zombies (visibility: **public** — owner confirmed public is fine) |
| Primary branch | `main` |
| Current development branch | `main` (docs/tooling only so far). Use `feature/<topic>` or `fix/<issue#>-<slug>` branches for code changes |
| Pristine baseline | commit `b1f4675` on branch **`baseline/v2.0.9`** (never move/rebase it). Tree is byte-identical to `TheLastWatchZombies_Source_v2.0.9_Stable.zip` (SHA-256 `8c43186eb50a11f65ef7620f56a744259054bef74c7e51f985b5facc5b6ad062`). Parent `11b5b30` is the owner's GitHub "Initial commit" (LICENSE only) |
| Tag `v2.0.9` | **Not on GitHub yet.** The cloud agent session's proxy blocks tag pushes. Owner: run the command below once |
| Last completed task | Onboarding: import, CI, documentation, audit, issue backlog |
| Build status | ✅ Release build 0 warnings / 0 errors (local Linux + GitHub Actions) |
| Test status | ✅ 1825/1825 regression assertions (local + CI). ❌ no dedicated-server or in-game test performed by agents yet |
| Open known issues | `docs/KNOWN_ISSUES.md` (16 items, none reproduced in game) |
| Immediate next steps | Milestone M1 in `DEVELOPMENT_ROADMAP.md`, starting with the dedicated-server smoke test (#1) |
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
