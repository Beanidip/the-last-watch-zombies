# Zombieween — Server admin guide (2.0.9)

**Command:** `/zombieween`  
**Privilege:** `controlserver` (admin or server console)  
**Author:** Beani  
**Game:** Vintage Story 1.22.7

1. Back up your world and remove older compiled mod ZIPs.
2. Install/build 2.0.9 on the server and clients. The game saves a small list of zones as loaded/generated map chunk columns become known to the mod.
3. Run `/zombieween` from the server console or as an admin in-game.
4. The command **adds five dormant NORMAL zombies to every recorded 3×3-chunk zone**, and gives **two Mycobiotic Cure items** to every player online at that moment. If a player's inventory is full, the leftover cure stack drops at their feet.
5. The command reports the number of zones and dormant additions; verify your own zone via `/lastwatch` and the admin map via `/zombiemap`.
6. Dormant zombies are not instantly spawned. They can reactivate normally on later eligible spawn cycles after ambient allowance is exhausted, respecting time, lighting, safe placement, claims, active zombie cap and wake batch settings. Migration may move the dormant counts on subsequent migration days.

**Important:** The event deliberately bypasses the configured global dormant storage cap (`DormantPopulationCap`, default 250). This allows the full five-per-zone event on large worlds. While the pool is over that cap, ordinary natural dormant additions are suppressed by the existing cap behavior. Event commands are repeatable, so running it twice adds 10 per zone. The event is not controlled by `/zombiepause`, since it does not spawn live zombies.

**Historical world limitation:** A public API call cannot enumerate every generated-but-unloaded chunk column on a pre-existing world. This release persists all zone coordinates it encounters from chunk-column loads, and also includes pre-existing saved Last Watch and dormant-population zones, plus currently occupied player zones. Old, distant, unloaded zones with no recorded activity become eligible once visited/reloaded on this release. No terrain is generated as part of the command.

**Testing:** Run in a backed-up world, watch `/lastwatch` dormant totals rise by five for your zone per invocation, then confirm an online character gains exactly two cure doses. Try a full inventory, server console invocation, save/reload, repeated invocation, and zones with the existing 250 population cap already reached.

**Build:** `.\BUILD-WINDOWS.ps1 -GamePath "$env:APPDATA\Vintagestory" -NoInstall` (requires .NET 10 SDK). This source release has not had a 1.22.7 build or live-server test in the current environment.
