# Verified Feature Inventory — v2.0.9

Status legend: **Implemented** = present in source and exercised by unit tests or
clearly wired end-to-end in code; **Implemented (untested in game)** = wired in
code, never observed in a running game by this audit; **Partial / inert** =
code or setting exists but has no effect. "Brief" refers to the onboarding
mission description; deviations from it are called out.

## Zombie types

| Type | Entity code | HP | Speed × | Special | Status |
|---|---|---|---|---|---|
| Zombie | `seventhnightzombies:zombie` | 20 | 1.0 | infects after 4 hits / 60 s | Implemented |
| Alpha Zombie | `alphazombie` | 44 | 1.65 | 1.25× damage, +32 detection, 2 hits infect, followed by normals, spawns after player deaths, 1/10 funeral-mask drop | Implemented |
| Grabber | `grabber` | 24 | 1.15 | leap (physics impulse) at prey 2–7 blocks away, 8 s cooldown; green tint; 2.25 tall | Implemented (untested in game) |
| Hive Zombie | `hivezombie` | 28 | 0.9 | releases a hostile vanilla-bee swarm (20 s life, ≤3 per 16 blocks) every 30 s with clear line | Implemented (untested in game) |
| Screamer | `screamer` | 18 | 1.0 | one scream on first clear sight → summons 10 normals over up to 60 s (respect pause/claims/cap; no chains) | Implemented (untested in game) |
| Bloater | `bloater` | 40 | 0.7 | on real death: +32 rot, 4 poison damage + 0.5 HP/s for 10 s to Survival players within 4 blocks with clear line; never on expiry | Implemented (untested in game) |

* Variant roll for every non-Alpha natural spawn (ambient, horde, dormant
  reactivation, `/snspawn`): Grabber 4%, Hive 2%, Screamer 1%, Bloater 3%,
  otherwise normal. Alphas are never rolled; they come from player deaths or
  dormant Alpha counts. Screamer summons are always normal.
* All variants share the `SeventhNightZombie` class; variant = entity code.
* Legacy codes `seventhnight:zombie` / `seventhnight:alphazombie` still exist so
  pre-merge worlds load (note: legacy entity JSON has `canClimb: false`).

## Targeting and AI

| Behaviour | Status |
|---|---|
| Detect & pursue Survival players (32 blocks; 80 in hordes; Alphas +32); no line-of-sight requirement | Implemented |
| Creative/Spectator players never targeted; whitelist cannot override | Implemented |
| Traders / humanoid NPCs targeted with a 120 s attention budget per zombie | Implemented |
| Vanilla animals ignored by default; mechanical/rust creatures never; whitelist/blacklist patterns | Implemented (unit-tested patterns) |
| Sneaking: 40% range, 25% acquisition chance per scan, always seen within 4 blocks | Implemented (acquisition only; brief's "reduced detection while sneaking" holds, but an acquired target is never lost by sneaking) |
| Last-known-position pursuit (25 s) then roam | Implemented |
| Follow nearest Alpha when idle | Implemented |
| Idle roam: walk 30 s / pause 10 s at 55% speed | Implemented |
| Direct steering in open terrain + budgeted async A* | Implemented |
| Shared per-second scan (160) and route (60) budgets | Implemented (unit-tested budget window) |
| Idle expiry after 300 s away from players → dormant | Implemented |

## Spawning

| Feature | Default | Status |
|---|---|---|
| Night-only ambient spawning (18:00–06:00) | on | Implemented |
| One roll per occupied zone per second (p = 0.30) | — | Implemented |
| 5 new ambient zombies per zone per sunset cycle (shared) | `zombiepopdailyaddlimit=5` | Implemented |
| Spawn 20–48 blocks from a player, inside their zone, on the surface, light ≤ 12, unclaimed, ≥12 from other players | — | Implemented |
| Global active cap | 250 | Implemented (O(1) ledger) |
| Per-zone placement backoff 15/30/60 s | — | Implemented (unit-tested) |
| Only Survival players in dimension 0 drive spawning | — | Implemented |
| Death-created Alpha placed 12–16 blocks away after respawn (dormant fallback) | on | Implemented |

## Persistent population & migration

See `POPULATION_SYSTEM.md` and `MIGRATION.md`. Dormant pool, zone index,
ambient-first reactivation, sunset migration with tiered attraction, bonfire
pinning/lures, bells, trader seeding, explored-zone ledger: **Implemented**.
Radius-based wake mode: **inert**.

## Seventh-night hordes

| Feature | Default | Status |
|---|---|---|
| Scheduled every 7th night at 17:55 (5 min before sunset roll) | — | Implemented (calendar unit-tested) |
| 600 real seconds; opening wave 3–5 per player; waves every 180 s of 1–5 per player | — | Implemented |
| Opening-wave shortfall retries every 10 s for 120 s | — | Implemented |
| Horde waves ignore ambient quotas; obey active cap, light (unless overridden), claims (unless overridden) | — | Implemented |
| Raven caw + 3 moans per player; nearby zombies moan | — | Implemented |
| Hordes raise detection to 80, siege chance to 90%, reinforcement damage to 2, open doors | — | Implemented |
| Migrants may take two zone steps on horde nights | — | Implemented |
| `/zombiehordestart` manual horde | — | Implemented |
| Survives server restart | — | **No** (KNOWN_ISSUES #1) |

## Infection and Mycobiotic treatment

| Mechanic | Default | Status |
|---|---|---|
| Regular / Alpha hits to infect (within 60 s, damaging hits only) | 4 / 2 | Implemented (unit-tested) |
| Poison 0.2 HP every 10 real s; 20 s once treatment starts | — | Implemented |
| Fatal after 7 in-game days (calendar, includes sleep) | on | Implemented — clock does not pause during treatment (KNOWN_ISSUES #3) |
| 6 doses, ≥1 in-game day apart; early/invalid doses not consumed | — | Implemented (unit-tested spacing) |
| Each accepted dose heals 20 HP | — | Implemented |
| Psychedelic visuals 0.4 → 2.0 over the course | on | Implemented (visual only) |
| Death + respawn clears infection, tunic sickness, rot poison | — | Implemented |
| "Eaten" / "turned" death announcements | — | Implemented |
| State persistence | watched/entity attributes on the player | Implemented |

Crafting chain: 4 rot → **Rot Concentrate** (cooking pot); concentrate + a
poisonous mushroom (bitterbolete, deathcap, devilbolete, devilstooth, …) →
**Mycobiotic** (cooking); Mycobiotic + clean bandage (grid) → **Mycobiotic
Cure**. Right-click the cure to take a dose.

## Siege and block destruction

| Feature | Default | Status |
|---|---|---|
| Siege only after 5 s without ≥0.4 block progress while chasing | — | Implemented |
| Strike every 2.5 s with 65% (horde 90%) chance on the first collidable block on a horizontal ray toward the target, within 2.2 blocks, feet then head height | — | Implemented |
| Never: claimed blocks (`ProtectClaims`), blocks with block entities (chests, doors, machines), bedrock/mantle/admin/creative/portal codes, `Replaceable > 2000` | — | Implemented |
| Reinforced blocks lose 1 (horde 2) strength per strike, owner preserved; final point removed without breaking; fail-closed if owner unknown | — | Implemented |
| `ProtectReinforcedBlocks=true` makes reinforcement immune | off | Implemented |
| Horde door opening (unclaimed, toward target) | on | Implemented |

## Defensive structures and traps

| Structure | Recipe | Behaviour | Status |
|---|---|---|---|
| Wooden stronghold | 6 sticks + 2 rope + axe (tool) | +0.40 attraction; new blocks placed in its zone auto-reinforced at **5** | Implemented (ghost-record risk, KNOWN_ISSUES #4) |
| Copper stronghold | 5 copper ingots | auto-reinforce **50** | Implemented |
| Iron stronghold | 5 iron ingots | auto-reinforce **300**; strongest tier in the zone wins | Implemented |
| Stronghold-zone convenience | — | in a stronghold zone, a player with claim access may break their own reinforced blocks without removing reinforcement first | Implemented |
| Watchtower bell | 3 copper ingots + 2 sticks | 1/2/3 rings after migration (neighbour arrivals / local arrivals / >30 dormant here); shown on map | Implemented |
| Pit bonfire | 3 resin + 1 cattail tops | add 4 firewood (right-click), ignite over full 32-firewood ground piles connected beneath (≤128 piles, same zone); burns 9 in-game hours; ≥3 stacks pins zone population, 6/9/12 lure from 1/2/3 rings; yields 1–2 charcoal per pile and consumes the starter | Implemented |
| Sharpened stakes | 2 sticks + knife + 2 firewood + rope → vanilla `game:palisadestakes-west` | any zombie within 2 blocks is held 3–20 s and takes 5 damage; first trigger starts a 30 s countdown (persisted) after which the stake becomes a loose stick; reusable until then; re-catchable after release | Implemented (unit-tested rules). Brief's "zombie capture … destruction after activation … loose-stick drop" matches: the stake is *replaced by* a loose-stick block rather than dropping an item |
| Rot Concentrate bait | sneak + right-click a floor → vanilla ground pile | zombies within 32 blocks (same dimension) prioritise the pile, eat 1 per pile per 2 s, then resume; only loaded piles indexed | Implemented (unreachable-pile risk, KNOWN_ISSUES #5) |

## Stealth equipment

| Item | Behaviour | Status |
|---|---|---|
| Funeral masks 1–8 (vanilla) | Crouching while wearing a usable mask → **untargetable** (also drops current pursuit). Durability drains 1/s while crouched: mask 1 = 200, others 50. Wearing any usable mask (crouch not needed) removes your +0.10 migration presence. | Implemented |
| Rot Covered Tunic | 16 rot (8 slots × 2) + grass shirt. While worn and durable (20): **untargetable**, −30% walk speed (item stat). Drains 1 durability per real second worn. First wear of each tunic item: 3 HP sickness over 6 s. Uses the vanilla grass-shirt shape. | Implemented |
| Eye bandage / eyepatch | Mutes the wearer's pain vocalizations (Harmony hook) | Implemented |

## Corpses and loot

Real deaths leave a corpse for 30 real seconds (while loaded), then a red dust
puff and: 3 rot, 2 bones, 1 small raw hide; Alphas also 1/10 chance of one
random funeral mask; Bloaters +32 rot immediately on death. A loot-dropped flag
prevents duplicates across reloads. Expiry/removal never drops loot. Dormancy is
never a death. **Implemented.**

## Zombieween

`/zombieween`: +5 dormant normals per recorded zone (cap bypass), 2 cures per
online player, no immediate spawns, repeatable, saved immediately.
**Implemented (untested in game)** — partial-insert cure loss (KNOWN_ISSUES #2)
and large-world growth (KNOWN_ISSUES #12).

## Admin & tooling

20 commands (`COMMANDS.md`), admin map overlay, profiler, persistent spawn
pause, config editor. **Implemented.**
