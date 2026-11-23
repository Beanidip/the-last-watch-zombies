# The Last Watch Zombies — 2.0.9 stable

**Author:** Beani  
**Game:** Vintage Story 1.22.7  
**Mod ID:** `seventhnightzombies` (unchanged to preserve world compatibility)

This stable version is built from 2.0.9-rc.2 with **no gameplay source-code changes**. Only version metadata, documentation, and Windows packaging were updated. The prior RC2 source compiled successfully in the user-provided Windows build log. Live multiplayer testing has not yet been reported.

## What's included

- Admin `/zombieween` adds 5 dormant normal zombies to every known 96×96-block zone, and supplies 2 Mycobiotic Cure doses to each online player. No immediate spawning.
- Historical distant/unloaded areas without a recorded zone require a revisit to be counted.
- Ground-placement and 32-block lure of rot concentrate from 2.0.9-rc.1 remain.
- The compiled mod will be named `thelastwatchzombies_2.0.9.zip`.

## Build/install on Windows

```powershell
.\BUILD-WINDOWS.ps1 -GamePath "$env:APPDATA\Vintagestory"
```

The Windows build automatically backs up previous merged or split zombie-mod ZIPs to a `TheLastWatchModBackups` folder **outside Mods** before installing the new build. It never deletes worlds, server configuration, or inventories. Use `-NoInstall` to compile a distributable ZIP without altering installed mods.

The download is a **source ZIP**, not a compiled `.dll` mod package. Keep existing server saves backed up and run the checks in `RELEASE-PLAYTEST.md` before a public event.
