# 2.0.9 Zombieween playtest

1. On a backed-up Vintage Story 1.22.7 test server with newly compiled 2.0.9, check normal players are refused `/zombieween` but an admin and server console can use it.
2. Use `/lastwatch` before and after the command to verify +5 dormant normals in your zone and no immediate live entity wave.
3. Repeat the command to verify another +5; confirm commands do not reset ambient or dormant return quotas.
4. Observe that every online player receives exactly two Mycobiotic Cure doses, including one with an empty slot and another with a full inventory (leftover items drop safely).
5. Save, restart and recheck dormant counts. Load a new distant zone, run the command and verify this generated/loaded zone is now counted. Test with the normal dormant storage cap at 250.
6. Verify the normal sunset migration and dormant reactivation still obey light, claims, nighttime, max active and reactivation batch constraints.

---

# 2.0.9-rc.1 client playtest

- Place concentrate on solid ground with sneak + right-click; add to and pick up the pile normally.
- Place ordinary rot nearby: only concentrate should lure zombies.
- Spawn a zombie with `/zombiespawn normal`, within 32 blocks of the pile. Confirm it diverts and eats, including when it was previously chasing a player.
- Repeat with `/zombiespawn grabber`, `hive`, `screamer`, `bloater`, and `alpha`.
- Use several zombies on a pile: it should lose only one concentrate every two seconds.
- Empty/remove the pile and confirm normal pursuit resumes. Save/reload a remaining pile and verify attraction returns.
- Check that the 30-second stakes still work and crafting still accepts concentrate.

Graphical rendering, networked client interaction and multiplayer pathfinding require a connected-client playtest.
