# 2.0.9 STABLE — Zombieween dormant event

- Added admin-only `/zombieween` (`controlserver` privilege), callable by console or an admin player.
- Adds five dormant normal zombies to each recorded generated/loaded 96×96-block Last Watch zone. No immediate world entities, spawn queues, terrain loads or new terrain generation.
- Records generated/loaded chunk-column zones in the saved Last Watch zone index; old saved zone states, dormant-population zones, and current player zones are included too. Historical zones that have never loaded since 2.0.9 and have no saved Last Watch activity cannot be discovered from the public engine API; visit/reload them first if desired.
- The admin event bypasses `DormantPopulationCap` to ensure five per zone. All existing activation, ambient, active zombie, light, time and claim constraints still apply. Subsequent normal dormant additions remain capped as configured.
- Every currently online player receives a stack of two `seventhnightzombies:mycobioticcure`. If inventory insertion fails, uninserted doses drop at the player's location rather than being destroyed.
- Event is additive/repeatable; immediately saves dormant counts and generated-zone records. No automatic one-time lock.
- Preserves the rot concentrate pile and lure fixes from 2.0.9-rc.1.
- Stable release: packaging/version and Windows upgrade handling updated; gameplay identical to 2.0.9-rc.2.

---

# 2.0.9-rc.1 — rot concentrate lure restored

The 2.0.8 release contained rot concentrate as a plain item. The floor-placement behavior and lure implementation had remained in the earlier branch. Both are restored here without reverting the current traps, variants, or bell.

## Use

1. Replace the previous mod ZIP on the server and all clients, then restart.
2. Hold Rot Concentrate. Hold your sneak key (Shift by default) and right-click the top of a solid floor to create a vanilla ground-storage pile. Existing concentrate stacks work; no recrafting is needed.
3. Zombies in the same dimension within 32 blocks prioritize the pile over ordinary chasing. Discovery may take about three seconds.
4. At the pile, zombies eat one concentrate per pile every two seconds, even with several zombies present. They must be close enough and have an unobstructed bite path.
5. Once the concentrate is gone, zombies return to their normal targeting behavior. Empty piles disappear; any unrelated stored items remain.

Vanilla ground storage owns the inventory, saves and player access checks. Only loaded bait piles are indexed; unload/removal clears them and reloading restores discovery. No persistent zombie ID ledger is added for the lure. Variant movement speeds are retained.

The current 30-second stake countdown and vanilla-style watchtower bell remain in this release.
