# 2.0.9

- Admin-only `/zombieween` adds five dormant normals per recorded generated/loaded zone; grants two cure doses to each online player.
- World-saved exploration zone ledger; includes old saved activity and dormant zones. Normal wake logic is unchanged.
- Admin-event additions bypass the natural dormant storage cap. Existing active cap and ambient quotas remain intact.
- Stable release of the 2.0.9-rc.2 gameplay code. Mod ID and author preserved.
- Windows packaging derives its filename from the version and backs up prior mod ZIPs outside Mods before installing.

---

# The Last Watch Zombies v2.0.1

- Increased default `SpawnMaxLightLevel` from 10 to **12 inclusive**.
- The same threshold applies to ambient zombie spawning, dormant zombie reactivation, and death-created Alpha placement when normal lighting rules apply. Explicit horde light exceptions remain configurable.
- No changes to ambient spawn chance, zone quota, player radius, or dormant batch rates.
- Spawn locations still obey the night window, safe terrain, player distance, and claims protections.

**Existing server settings:** `thelastwatchzombiesconfigmain.json` keeps saved values, so change `SpawnMaxLightLevel` manually to 12, or remove that config after backing up to regenerate the default.

**Verification required:** Compile with Vintage Story 1.22.7 DLLs and test in-game before publishing.
