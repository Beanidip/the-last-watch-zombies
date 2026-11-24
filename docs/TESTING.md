# Build, Test and Regression Procedures

## 1. Requirements

| Need | Version | Notes |
|---|---|---|
| .NET SDK | 10.0 (built here with 10.0.115) | `TargetFramework net10.0` |
| Vintage Story assemblies | **1.22.7** | `VintagestoryAPI.dll`, `VintagestoryLib.dll` (tests), `Mods/VSEssentials.dll`, `Mods/VSSurvivalMod.dll`, `Lib/0Harmony.dll`, `Lib/Newtonsoft.Json.dll`, `Lib/protobuf-net.dll` (tests) |
| Python 3 | any | static asset checks, doc generator |

Game assemblies are proprietary. They are **never committed**; obtain them from
your own game install or the official dedicated-server download.

## 2. Building

### Linux / CI (no game install needed)

```bash
bash scripts/fetch-vs-server.sh 1.22.7 vs-server          # official CDN, SHA-256 pinned, git-ignored
dotnet build TheLastWatchZombies.csproj -c Release -m:1 -t:Rebuild -p:VintageStoryPath=$PWD/vs-server
```

### Windows (author's workflow)

```powershell
.\BUILD-WINDOWS.ps1 -GamePath "$env:APPDATA\Vintagestory" -NoInstall   # build only
.\BUILD-WINDOWS.ps1 -GamePath "$env:APPDATA\Vintagestory"              # build + install (old ZIPs backed up outside Mods)
.\TEST-WINDOWS.ps1  -GamePath "$env:APPDATA\Vintagestory"              # build + regression tests
```

Output: `bin/Release/net10.0/TheLastWatchZombies.dll` and
`dist/thelastwatchzombies_<version>.zip` (install this ZIP on server **and**
clients). The csproj fails fast with a clear message if `VintageStoryPath` is
wrong.

## 3. Automated checks

| Check | Command | What it covers |
|---|---|---|
| Static assets | `python3 scripts/validate-assets.py` | 49 JSON files parse; modinfo/csproj version match; game = 1.22.7; lang + entity files for all six zombie codes |
| Regression suite | `dotnet run --project tests/Regression.csproj -c Release -p:VintageStoryPath=…` | 1,875 assertions (2.0.10): night/horde calendar, light limit, infection/cure rules, hit windows, target patterns, zone math incl. negative coords, migration steps/conservation, daily resets, budgets, config validation and `/zombieconfig` parsing, dormant pool seed/migrate/cap, stake trap rules (`StakesRegression.cs`), bonfire rules (`BonfireRegression.cs`), variant rules & bait rules (`VariantRegression.cs`) |
| Config artifacts | `bash scripts/refresh-config-files.sh [--check] vs-server` | Regenerates (or checks) `DEFAULT-*.json`, `CONFIG-REFERENCE.md`, `docs/CONFIGURATION.md` from source |
| CI | `.github/workflows/ci.yml` | All of the above on every push/PR + "package contains no game assemblies" audit |

The tests are a single console program (`tests/Program.cs` calls
`VariantRegression.Run/RunRelease5`, `StakesRegression.Run`,
`BonfireRegression.Run`); any failed assertion throws `FAIL: <name>` and exits
non-zero. They compile `src/**` directly and run without a game. Pure rules are
tested directly; selected server paths (spawn pause, variant abilities, screamer
summons, bloater burst, cure acceptance, bait consumption, stakes) are tested
through a `DispatchProxy`-based fake server (`Fake.Make<T>` and `Fixture` in
`tests/VariantRegression.cs`), with zombies built via
`RuntimeHelpers.GetUninitializedObject`. **Not covered:** real pathfinding,
chunk loading/unloading, world save/reload, networking, client rendering,
ambient placement against real terrain, the sunset migration end to end, and
hordes. Extend `Fixture` for new server-side tests rather than inventing a new
harness.

`validation/RotQA.cs` is the source of a throwaway QA mod used once for the
2.0.9-rc.1 dedicated-server bait test; it is not built by any project.

### Recorded results

| Date | Commit | Build | Tests | Notes |
|---|---|---|---|---|
| 2026-11-24 | `b1f4675` (v2.0.9 baseline) | ✅ 0 warnings / 0 errors (.NET 10.0.115, Linux, VS 1.22.7 server assemblies) | ✅ 1825/1825 | Local, this audit |
| 2026-11-24 | `808aa6a` | ✅ | ✅ 1825/1825 | GitHub Actions run 1 (first valid workflow) |
| 2026-11-24 | — | — | — | Dedicated-server load test **not run** (blocked in the audit environment) |
| 2026-11-24 | `fix/2.0.10-stability` + poultice recipe & Traits tab | ✅ 0 warnings / 0 errors | ✅ 1885/1885 | Local. Harmony target signature asserted against 1.22.7. Owner confirmed the earlier 2.0.10 build works in game (screenshot); Traits tab and poultice recipe not yet seen in game |
| 2026-11-24 | `fix/2.0.10-stability` (2.0.10) | ✅ 0 warnings / 0 errors | ✅ 1875/1875 | Local. All 49 repo JSON files parse; config artifacts match source (drift 0); new tests mutation-checked (re-adding the hide drop / disabling stronghold validation makes them fail). No in-game test |

Never record a test as passed unless it was executed. Append rows here.

## 4. Manual multiplayer regression plan

Run on a **copy** of a world, Vintage Story 1.22.7 dedicated server, the freshly
built ZIP on the server and two clients (P1 admin, P2 normal). Use
`/zombiediagnostics on` + restart for perf rows. Record pass/fail, server log
excerpts and `/lastwatch` output for each row.

### A. Startup and save/reload
1. Fresh world: server starts; log shows `unified zone spawning ready`; both ModConfig files created; no errors from `seventhnightzombies`.
2. Existing 2.0.x world: loads; dormant counts and zone records match pre-upgrade `/lastwatch`.
3. Save, stop, restart: `/lastwatch` dormant/zone values identical; triggered stakes keep their countdown; `/zombiepause` state persists.
4. Corrupt-save guard: (copy world) hand-corrupt is out of scope; just confirm no exceptions on normal load.

### B. Natural spawning
1. P1 alone at night on open dark terrain: within a few minutes ≤5 new zombies appear 20–48 blocks away; `/lastwatch` shows ambient 5/5 afterwards.
2. P1 and P2 in the **same** zone: still 5 total for the zone. In **different** zones: 5 each.
3. Torch-lit / daylight / claimed area: no ambient spawns; `/snambienttest` reports light/claim reasons.
4. Creative/Spectator player: no spawns around them, never targeted.
5. Active cap: set `activecap` low (e.g. 5), restart, confirm spawning stops at the cap for all sources.

### C. Dormant reactivation
1. Kill/avoid nothing; walk >64 blocks away for 5+ minutes while chunks stay loaded → zombies expire; `/lastwatch` dormant rises by the same amount (check global too).
2. Return at night after ambient slots are spent → dormant count falls in batches of ≤3 per 5 s; no duplicate entities.
3. Unload/reload test: leave the area far enough to unload chunks with live zombies, return → the same zombies are present as entities, dormant count unchanged (no duplication).
4. Restart mid-night → counts preserved; reactivation resumes.
5. Fill the dormant pool to `DormantPopulationCap` with `/snseed`; further expiries are not stored (count stays at cap).

### D. Migration
1. `/snseedneighbor`, stand still in your zone, `/zombiecycle` several times → counts move toward you; total global dormant unchanged after every cycle (conservation).
2. Natural sunset roll happens once per day; sleeping through several days runs one roll.
3. Place a stronghold + paths → `/lastwatch` settlement/strongholds; attraction pulls from further away.
4. Bonfire with ≥3 stacks: zone's own dormant population does not leave; 6/9/12 stacks: pulls from 1/2/3 rings.
5. Bell rings 1/2/3 as specified; chat announcement appears.
6. Funeral-mask wearer at sunset is not counted as presence.

### E. Seventh-night hordes
1. `/zombiehorde` shows correct next night; advance time to night 7 17:54 with both players online → raven + announcement at 17:55; opening wave ≥3 per player (watch log for "fell short"/"recovered").
2. Waves every 3 real minutes for 10 minutes; "The horde has ended" once.
3. `/zombiehordestart` works any time; on a scheduled night it prevents a second announcement.
4. **Restart during a horde** → document behaviour (expected per code: horde ends; KNOWN_ISSUES #1).
5. Doors toward players open during the horde (unclaimed only); claims block spawning unless `HordeSpawnsInClaims`.

### F. Infection and cure
1. Take 4 regular hits within 60 s → infected message; 2 Alpha hits → infected.
2. Poison every 10 s; after dose 1 every 20 s; `/zombieinfection` values correct.
3. Dose again within <1 in-game day → refused, item not consumed.
4. Complete 6 doses on consecutive days → cured; psychedelic strength increments.
5. Leave untreated 7 in-game days → death ("turned" announcement); respawn clears infection.
6. Try the first dose at 2.1 days → refused, item kept, `/zombieinfection` says too late. First dose at 1.9 days, then daily → cured on day ~6.9 without dying.
7. Corpse loot: 3 rot, 2 bones, 1 rusty gear (no hide).
8. Craft the cure with Mycobiotic + clean bandage, and with Mycobiotic + horsetail poultice (reed and linen); honey-sulfur poultice must not work.
9. While infected, open the character dialog → Traits: a red "Zombie infection" line with start window / doses / next dose / fatal countdown; it disappears after the cure or respawn. Check on a remote client (not only single-player).
7. Relog while infected → state persists; P2 sees no desync.

### G. Siege, strongholds and claims
1. Wall yourself in with dirt; zombie stuck 5 s → strikes and breaks blocks between it and you; never ceilings/floors.
2. Reinforced wall: strength decreases by 1 (2 in horde) per strike; owner unchanged.
3. Claimed land: no block damage, no door opening, no spawning.
4. Chests/doors/BE blocks never broken.
5. Place each stronghold tier; newly placed blocks in the zone get 5/50/300 reinforcement.
6. Let zombies break an unclaimed stronghold → `/lastwatch` no longer lists it; new blocks in the zone are not auto-reinforced.

### H. Traps, bait, equipment, variants
1. Stakes: zombie within 2 blocks held 3–20 s, 5 damage; stake becomes a loose stick 30 s after first trigger; survives save/reload.
2. Bait: sneak-place concentrate; zombies within 32 divert, eat 1 per 2 s, resume chasing after; bait behind a sealed wall → zombies give up within ~15 s and hunt players again.
3. Mask crouch → untargeted, durability drains; tunic → untargeted, −30% speed, sickness once per item.
4. `/zombiespawn grabber|hive|screamer|bloater|alpha` → abilities as in FEATURES.md; bloater burst only on kill.
5. Alpha funeral-mask drop rate (spawn many alphas; expect ≈10%).

### I. Zombieween
1. Non-admin refused; admin and console succeed.
2. `/lastwatch` +5 dormant per invocation in your zone; nothing spawns immediately.
3. Each online player +2 cures; full inventory → ground drop; inventory with one free space (KNOWN_ISSUES #2).
4. Save/restart → counts and explored ledger persist.
5. Natural wakes afterwards still obey night/light/claims/cap.

### J. Performance / stress
1. 250 active zombies (`/snseed` + night, or repeated `/zombiespawn`) around two players: record `/zombieperf` (`system.tick`, `ai.entityTick` max/avg, `ai.routes.deferred`) and server TPS over 10 minutes.
2. Large dormant pool (e.g. 50k via repeated `/zombieween` on a well-explored copy): time `/zombiecycle`, `/lastwatch`, map open, world save; save-file size.
3. Many ground-storage piles (≥2,000) loaded: measure tick cost with/without the mod (KNOWN_ISSUES #10).
4. Sunset with 10+ players in separate zones: measure the migration tick (`migration.daily`) (KNOWN_ISSUES #11).
