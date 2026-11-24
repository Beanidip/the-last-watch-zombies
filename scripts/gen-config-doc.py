#!/usr/bin/env python3
"""Generate docs/CONFIGURATION.md from source-generated defaults.

Normally run via scripts/refresh-config-files.sh. Manually:
1. dotnet run --project tools/ConfigDump -c Release -p:VintageStoryPath=<vs> -- config-dump .
2. python3 scripts/gen-config-doc.py [config-dump] > docs/CONFIGURATION.md
   python3 scripts/gen-config-doc.py [config-dump] --reference > CONFIG-REFERENCE.md

Defaults come only from the ConfigDump output (i.e. from src/Config.cs after
Validate()). Descriptions below are hand-written from reading the code; keys
without a description are flagged so the doc never silently omits a setting.
Status: A = active, I = inert in v2.0.9 (written to file, never read),
M = one-time migration marker (no gameplay effect).
"""
import json
import pathlib
import sys

REFERENCE = "--reference" in sys.argv
QUICKSTART = "--quickstart" in sys.argv
ARGS = [a for a in sys.argv[1:] if not a.startswith("--")]
DUMP = pathlib.Path(ARGS[0] if ARGS else "config-dump")

I, M = "I", "M"
D = {
    # --- variants
    "GrabberSpawnChance": "Chance a non-Alpha ambient/horde/reactivated spawn is a Grabber. Variant chances are normalized if they sum above 1.",
    "HiveSpawnChance": "Chance of a Hive Zombie.",
    "ScreamerSpawnChance": "Chance of a Screamer.",
    "BloaterSpawnChance": "Chance of a Bloater.",
    "GrabberLeapCooldownSeconds": "Seconds between Grabber leaps (leap at prey 2–7 blocks away, clear line, <3 blocks height difference). Range 3–120.",
    "HiveBeeCooldownSeconds": "Seconds between Hive bee releases (≤3 swarms within 16 blocks; swarms live 20 s). Range 20–300.",
    "BloaterBurstRadius": "Death-burst radius in blocks (Survival players with a clear line). Range 1–8.",
    "BloaterBurstDamage": "Instant poison damage from the burst. Range 0–30.",
    "BloaterRotDrop": "Extra rot dropped when a Bloater dies (on top of normal corpse loot). Range 1–64.",
    "BloaterPoisonSeconds": "Duration of the lingering rot poison (cleared on respawn). Range 0–60.",
    "BloaterPoisonDamagePerSecond": "Lingering poison damage per real second. Range 0–5.",
    # --- lifecycle
    "ZombieIdleDespawnSeconds": "Seconds without prey (and without a Survival player within the keep-active radius) before a zombie expires into the dormant pool. Range 30–3600.",
    "NpcTraderInterestSeconds": "Per-zombie budget for chasing traders/humanoid NPCs; renewed only after losing a player target.",
    "ZombieStayActiveNearPlayers": "Reset the idle timer while a Survival player is within ZombieKeepActiveRadiusBlocks (±32 Y).",
    "ZombieKeepActiveRadiusBlocks": "Horizontal radius for the keep-active check. Range 16–256.",
    "ZombiePresenceCheckSeconds": "How often each idle zombie re-checks nearby players (shared 5 s snapshot). Range 2–60.",
    "ZombieLostTargetPursuitSeconds": "Seconds a zombie walks toward the last seen prey position after losing it. Range 0–60.",
    # --- schedule
    "ZoneMigrationHour": "In-game hour of the daily migration roll.",
    "HordeStartHour": "Hour used by the horde calendar; the horde starts HordeMinutesBeforeStartHour earlier.",
    "HordeMinutesBeforeStartHour": "Minutes before HordeStartHour that a scheduled horde begins (17:55 by default).",
    "HordeMigrationMaxZoneSteps": "Zone steps a migrant may take when a horde is active at the roll (1–2).",
    "HordeEveryDays": "Scheduled horde every N in-game nights (night N where N % this == 0).",
    "HordeDurationSeconds": "Real seconds a horde lasts (natural or /zombiehordestart). Not persisted across restart.",
    "HordeWaveIntervalSeconds": "Real seconds between reinforcement waves.",
    "HordeWaveMinZombiesPerPlayer": "Minimum zombies per eligible player per reinforcement wave.",
    "HordeInitialMinZombiesPerPlayer": "Minimum zombies per player in the opening wave; shortfalls are retried.",
    "HordeWaveMaxZombiesPerPlayer": "Maximum zombies per player per wave (forced ≥ both minimums).",
    "HordeInitialRetrySeconds": "Retry interval for a short opening wave.",
    "HordeInitialRetryWindowSeconds": "How long opening-wave retries continue.",
    "HordeSpawnIgnoreLightLevel": "Let horde waves ignore the light limit.",
    "HordeSpawnsInClaims": "Let horde waves spawn inside land claims (claims still protect blocks).",
    "ZombieSpawnNightOnly": "Strict: ambient spawns, dormant wakes and death Alphas only between NightStartsAtHour and NightEndsAtHour.",
    "ZombieSpawnHordeNightOnly": "Restrict automatic spawns/wakes to scheduled horde nights.",
    "AmbientSpawnRequiresNight": "Additional night requirement for NEW ambient zombies only.",
    "NightStartsAtHour": "Start of night (12–23.9).",
    "NightEndsAtHour": "End of night (0–11.9).",
    # --- spawning
    "MaxActiveZombies": "Global cap on alive, loaded zombies for every spawn source (0 = unlimited).",
    "AmbientSpawnChancePerSecond": "Per occupied zone, chance per second of one NEW ambient spawn attempt.",
    "zombiepopdailyaddlimit": "NEW ambient zombies per zone per sunset cycle, shared by all players in the zone. Dormant wakes in a zone start only once this is spent. 0 disables ambient creation.",
    "DormantPopulationCap": "Global cap on stored dormant zombies for natural additions (expiry, seeds, death-Alpha fallback). /zombieween bypasses it.",
    "SpawnMaxLightLevel": "Max MaxTimeOfDayLight (sun+block light) at feet and head, inclusive, for ambient/dormant/death-Alpha placement. 0–31.",
    "SpawnMinDistance": "Minimum horizontal spawn distance from the player (20–96). Dormant wakes also keep this from every player.",
    "SpawnMaxDistance": "Maximum horizontal spawn distance (clipped to the player's zone).",
    "SpawnAlphaOnPlayerDeath": "Spawn an Alpha 12–16 blocks from a player's death site after they respawn (falls back to a dormant Alpha).",
    "SpawnMinimumOtherPlayerDistance": "Spawns keep at least this distance from every other online player. 2–64.",
    "SpawnDebugLogging": "Log (≤1/min) why an ambient attempt failed.",
    "SpawnPositionAttempts": "Candidate columns per placement attempt (8–256).",
    "AmbientSpawnSurfaceOnly": "Limit ambient vertical search to ±SpawnSurfaceVerticalTolerance around the heightmap (else ±8).",
    "SpawnSurfaceVerticalTolerance": "Vertical search half-range around the terrain heightmap (0–8).",
    "AlphaDeathSpawnDelayMilliseconds": "Delay after respawn before the death Alpha is placed.",
    # --- dormant
    "DormantReactivationPerZonePerDay": "Max dormant wakes per zone per cycle (0 = unlimited).",
    "DormantWakeBatchSize": "Max wake attempts per zone per pass (failures count). 1–16.",
    "DormantWakeIntervalSeconds": "Seconds between wake passes. 1–60.",
    "DormantMaxTotalWakesPerPass": "Max wake attempts per pass across all zones. 1–32.",
    # --- migration
    "ZoneBaseDailyMigrationChance": "Base per-zombie daily move chance; also scales stay/neighbour drift weights.",
    "ZoneMaxDailyMigrationChance": "Cap on per-zombie daily move chance and on each attraction influence.",
    "ZonePlayerPresenceBonus": "Attraction per unmasked Survival player present at sunset.",
    "ZoneSettlementBonus": "Attraction for a permanent settlement (≥25 paths + stronghold).",
    "ZoneStrongholdBonus": "Attraction if the zone has any stronghold.",
    "ZoneBonfireAttractionBonus": "Bonfire lure strength (6/9/12 stacks → 1/2/3 rings).",
    "ZoneStonePathBonusPerBlock": "Attraction per counted stone-path block.",
    "ZoneDeathBonusPerDeath": "Attraction per player death this cycle.",
    "ZoneTraderDormantSeed": "Dormant normals seeded once per zone when a trader is first seen there.",
    "ZoneMigrationChatAnnouncement": "Server-wide chat line after each migration that moved zombies.",
    "ZoneBellRangeZones": "Zone rings around a bell in which players hear it.",
    "ZoneBellOverrunThreshold": "Dormant count in a bell zone that triggers three rings.",
    "ZoneFarmAnimalThreshold": "Loaded farm animals in a zone above which it gains livestock attraction.",
    "ZoneFarmAnimalAttractionBonus": "Livestock attraction bonus.",
    "ZoneAttractionTier4Threshold": "Score for a 4-ring attraction radius (below: 2 rings).",
    "ZoneAttractionTier6Threshold": "Score for 6 rings.",
    "ZoneAttractionTier8Threshold": "Score for 8 rings.",
    "ZoneAttractionTier10Threshold": "Score for 10 rings.",
    "ZoneStayWeight": "Relative weight of the random 'stay' outcome.",
    "ZoneRandomNeighborWeight": "Relative weight of random drift to the 8 neighbours.",
    "ZoneForgeActivityBonus": "Attraction for a forge found by the sunset scan of loaded block entities.",
    "ZoneCharcoalActivityBonus": "Attraction for an active charcoal pit (block entity). Finished charcoal piles no longer count (2.0.10).",
    "ZoneAnvilActivityBonus": "Attraction for an anvil.",
    "ZoneIndustrySurfaceBelowBlocks": "Industry block entities (forge, anvil, charcoal pit) count only from this far below the surface…",
    "ZoneIndustrySurfaceAboveBlocks": "…to this far above it.",
    "ZoneMaxPathsCounted": "Cap on counted stone-path blocks per zone.",
    "LastWatchVerboseLogging": "Log a line after each migration.",
    # --- infection
    "NormalHitsToInfect": "Damaging hits from regular zombies within HitWindowSeconds that cause infection.",
    "AlphaHitsToInfect": "Damaging hits from Alphas (counted separately from regular hits).",
    "HitWindowSeconds": "Sliding window for counting infecting hits (real seconds).",
    "InfectionTickSeconds": "Real seconds between infection poison ticks while untreated.",
    "InfectionDamagePerTick": "Poison damage per tick.",
    "InfectionFatalEnabled": "Kill the player at the fatal deadline (InfectionFatalDays after infection, extended while a timely course of treatment runs).",
    "InfectionFatalDays": "In-game days from infection to death.",
    "CureDosesRequired": "Mycobiotic Cure doses needed. Each accepted dose also heals 20 HP.",
    "CureDoseIntervalDays": "Minimum in-game days between doses.",
    "CureStartWindowDays": "In-game days after infection within which the FIRST dose must be taken (later first doses are refused; ignored when InfectionFatalEnabled is false). A course started in time can always finish: while treated, the fatal deadline is max(infection + InfectionFatalDays, first dose + CureDosesRequired × CureDoseIntervalDays). 0.05–InfectionFatalDays.",
    "TreatedInfectionTickSeconds": "Poison tick interval after the first dose (≥ InfectionTickSeconds).",
    "CurePsychedelicEnabled": "Visual 'psychedelic' strength 0.4→2.0 across the course of doses.",
    # --- loot
    "CorpseDelaySeconds": "Real seconds a corpse lies (while loaded) before poofing and dropping loot.",
    "CorpseRot": "game:rot dropped per corpse.",
    "CorpseBones": "game:bone dropped per corpse.",
    "CorpseRustyGears": "game:gear-rusty dropped per corpse (replaced small raw hides in 2.0.10; a saved CorpseSmallHides value is carried over once).",
    "AlphaFuneralMaskChanceDenominator": "Alpha corpses drop one funeral mask with chance 1/N.",
    "AlphaFuneralMaskItemCodes": "Pool of item codes for the Alpha mask drop ('game:' assumed if no domain).",
    # --- equipment
    "FuneralMask1Durability": "Max durability of funeral mask 1 (also sets the vanilla item's durability).",
    "OtherFuneralMaskDurability": "Max durability of funeral masks 2–8.",
    "MaskDrainSeconds": "Seconds of crouching-with-mask per durability point.",
    "RotTunicFirstWearDamage": "Total sickness damage the first time a tunic item is worn (0.5 HP/s).",
    "RotTunicDrainSeconds": "Seconds worn per tunic durability point (any stance).",
    "QuietEyeBandageItemIds": "Legacy numeric item IDs treated as eye coverings (mutes pain sounds).",
    "EyeBandageCodeFragments": "Item-code fragments treated as eye coverings.",
    "EyeBandageInventoryClasses": "Inventory class-name fragments searched for eye coverings.",
    # --- combat & AI
    "AlphaSpeedMultiplier": "Alpha movement multiplier (others: Grabber 1.15, Hive 0.9, Bloater 0.7 — fixed).",
    "AlphaAttackDamageMultiplier": "Alpha melee damage multiplier.",
    "AlphaDetectionRangeBonus": "Extra detection range for Alphas.",
    "AttackDamage": "Base melee damage per hit.",
    "AttackCooldownSeconds": "Seconds between melee hits.",
    "MoveSpeed": "Base movement speed (traverser units).",
    "DetectionRange": "Prey detection radius (clamped to 32–128, cannot be lowered below 32).",
    "HordeDetectionRange": "Detection radius while a horde is active.",
    "TargetWhitelist": "Extra targetable entity codes/prefixes (e.g. 'game:wolf-*').",
    "TargetBlacklist": "Never-target entity codes/prefixes (wins over whitelist; applies to players/NPCs too).",
    "IgnoreVanillaAnimals": "Do not target non-humanoid game: entities unless whitelisted.",
    "PerformanceDiagnostics": "Enable WorkProfiler counters for /zombieperf (toggle live with /zombiediagnostics).",
    "AiPreyScansPerSecond": "Server-wide budget of prey/Alpha scans per second.",
    "AiRouteRequestsPerSecond": "Server-wide budget of async path requests per second.",
    "AiPreyScanIntervalSeconds": "Per-zombie interval between prey scans (+0–0.25 s jitter).",
    "PursuitSteerIntervalSeconds": "Interval between direct-steer updates in open terrain.",
    "PursuitDirectWalkRangeBlocks": "Max distance for direct steering instead of A*.",
    "PursuitRepathTargetMovementBlocks": "Prey movement that triggers a new route.",
    "LastSeenPursuitTuningVersion": M,
    "FollowAlphaEnabled": "Idle normal zombies follow a nearby Alpha.",
    "AlphaFollowRange": "Range at which an Alpha is followed.",
    "AlphaFollowStopDistance": "Distance at which followers stop.",
    "AlphaFollowRefreshSeconds": "Follow re-route interval.",
    "SneakDetectionRangeMultiplier": "Detection range multiplier vs a sneaking player (acquisition only).",
    "SneakAcquireChance": "Per-scan chance to acquire a sneaking player in range.",
    "SneakAlwaysDetectedDistance": "Sneaking players closer than this are always detected.",
    "RoamEnabled": "Idle roaming.",
    "RoamWalkSeconds": "Roam walk phase length.",
    "RoamPauseSeconds": "Roam pause phase length.",
    "RoamRouteRefreshSeconds": "Roam re-route interval.",
    "RoamSpeedMultiplier": "Roam speed multiplier.",
    "ManualHordeSoundRange": "Range of the horde raven/moan cue.",
    "ManualHordeMoans": "Moans played to each player at horde start.",
    # --- siege
    "BlockBreakingEnabled": "Zombies may damage blocks between them and their target when stuck.",
    "BlockBreakIntervalSeconds": "Seconds between siege strike attempts.",
    "BlockBreakChance": "Chance a siege strike happens (outside hordes).",
    "HordeBlockBreakChance": "Siege strike chance during hordes.",
    "NonHordeBlockReinforcementDamage": "Reinforcement points removed per strike outside hordes.",
    "HordeBlockReinforcementDamage": "Reinforcement points removed per strike during hordes.",
    "HordeOpensDoors": "During hordes, zombies open unclaimed doors toward their target.",
    "ProtectClaims": "Never spawn in, or damage/open blocks in, land claims (hordes may spawn there if HordeSpawnsInClaims).",
    "ProtectReinforcedBlocks": "Reinforced blocks/doors are immune instead of losing strength.",
    "SiegeEnabled": "Master switch for stuck-based siege.",
    "SiegeProgressSampleSeconds": "Interval for sampling movement progress.",
    "SiegeStuckSeconds": "Seconds without progress before siege strikes begin.",
    "SiegeMinimumTravelBlocks": "Movement per sample below which a zombie counts as stuck.",
    "SiegeNavigationRefreshSeconds": "Chase re-route interval.",
    "BlockBreakReach": "Max distance to an attackable obstruction.",
    "HordeDoorAttemptCooldownSeconds": "Seconds between door-open attempts.",
    # --- sound
    "MoanMinSeconds": "Minimum seconds between moans.",
    "MoanMaxSeconds": "Maximum seconds between moans.",
    "ZombieMoanVolume": "Moan volume.",
    "ZombieHurtSoundVolume": "Hurt grunt volume.",
    "ZombieHurtSoundCooldownSeconds": "Minimum seconds between hurt grunts.",
}

GROUPS = [
    ("Special variants", ["GrabberSpawnChance", "HiveSpawnChance", "ScreamerSpawnChance", "BloaterSpawnChance", "GrabberLeapCooldownSeconds", "HiveBeeCooldownSeconds", "BloaterBurstRadius", "BloaterBurstDamage", "BloaterRotDrop", "BloaterPoisonSeconds", "BloaterPoisonDamagePerSecond"]),
    ("Spawning, light and time", ["MaxActiveZombies", "ZombieSpawnNightOnly", "ZombieSpawnHordeNightOnly", "AmbientSpawnRequiresNight", "NightStartsAtHour", "NightEndsAtHour", "AmbientSpawnChancePerSecond", "zombiepopdailyaddlimit", "SpawnMaxLightLevel", "SpawnMinDistance", "SpawnMaxDistance", "SpawnMinimumOtherPlayerDistance", "SpawnPositionAttempts", "AmbientSpawnSurfaceOnly", "SpawnSurfaceVerticalTolerance", "SpawnAlphaOnPlayerDeath", "AlphaDeathSpawnDelayMilliseconds", "SpawnDebugLogging"]),
    ("Active lifetime and dormancy", ["ZombieIdleDespawnSeconds", "ZombieStayActiveNearPlayers", "ZombieKeepActiveRadiusBlocks", "ZombiePresenceCheckSeconds", "DormantPopulationCap", "DormantReactivationPerZonePerDay", "DormantWakeBatchSize", "DormantWakeIntervalSeconds", "DormantMaxTotalWakesPerPass"]),
    ("Migration and settlements", ["ZoneMigrationHour", "ZoneBaseDailyMigrationChance", "ZoneMaxDailyMigrationChance", "ZoneStayWeight", "ZoneRandomNeighborWeight", "ZonePlayerPresenceBonus", "ZoneSettlementBonus", "ZoneStrongholdBonus", "ZoneStonePathBonusPerBlock", "ZoneMaxPathsCounted", "ZoneDeathBonusPerDeath", "ZoneBonfireAttractionBonus", "ZoneFarmAnimalThreshold", "ZoneFarmAnimalAttractionBonus", "ZoneForgeActivityBonus", "ZoneCharcoalActivityBonus", "ZoneAnvilActivityBonus", "ZoneIndustrySurfaceBelowBlocks", "ZoneIndustrySurfaceAboveBlocks", "ZoneAttractionTier4Threshold", "ZoneAttractionTier6Threshold", "ZoneAttractionTier8Threshold", "ZoneAttractionTier10Threshold", "ZoneTraderDormantSeed", "ZoneBellRangeZones", "ZoneBellOverrunThreshold", "ZoneMigrationChatAnnouncement", "LastWatchVerboseLogging"]),
    ("Seventh-night hordes", ["HordeEveryDays", "HordeStartHour", "HordeMinutesBeforeStartHour", "HordeDurationSeconds", "HordeWaveIntervalSeconds", "HordeInitialMinZombiesPerPlayer", "HordeWaveMinZombiesPerPlayer", "HordeWaveMaxZombiesPerPlayer", "HordeInitialRetrySeconds", "HordeInitialRetryWindowSeconds", "HordeSpawnIgnoreLightLevel", "HordeSpawnsInClaims", "HordeMigrationMaxZoneSteps", "HordeDetectionRange", "ManualHordeSoundRange", "ManualHordeMoans"]),
    ("Infection and treatment", ["NormalHitsToInfect", "AlphaHitsToInfect", "HitWindowSeconds", "InfectionTickSeconds", "InfectionDamagePerTick", "InfectionFatalEnabled", "InfectionFatalDays", "CureDosesRequired", "CureDoseIntervalDays", "CureStartWindowDays", "TreatedInfectionTickSeconds", "CurePsychedelicEnabled"]),
    ("Combat, targeting and AI", ["AttackDamage", "AttackCooldownSeconds", "MoveSpeed", "AlphaSpeedMultiplier", "AlphaAttackDamageMultiplier", "DetectionRange", "AlphaDetectionRangeBonus", "TargetWhitelist", "TargetBlacklist", "IgnoreVanillaAnimals", "NpcTraderInterestSeconds", "ZombieLostTargetPursuitSeconds", "SneakDetectionRangeMultiplier", "SneakAcquireChance", "SneakAlwaysDetectedDistance", "FollowAlphaEnabled", "AlphaFollowRange", "AlphaFollowStopDistance", "AlphaFollowRefreshSeconds", "RoamEnabled", "RoamWalkSeconds", "RoamPauseSeconds", "RoamRouteRefreshSeconds", "RoamSpeedMultiplier", "AiPreyScansPerSecond", "AiRouteRequestsPerSecond", "AiPreyScanIntervalSeconds", "PursuitSteerIntervalSeconds", "PursuitDirectWalkRangeBlocks", "PursuitRepathTargetMovementBlocks", "PerformanceDiagnostics"]),
    ("Siege, claims and reinforcement", ["SiegeEnabled", "BlockBreakingEnabled", "BlockBreakIntervalSeconds", "BlockBreakChance", "HordeBlockBreakChance", "NonHordeBlockReinforcementDamage", "HordeBlockReinforcementDamage", "ProtectClaims", "ProtectReinforcedBlocks", "HordeOpensDoors", "HordeDoorAttemptCooldownSeconds", "BlockBreakReach", "SiegeProgressSampleSeconds", "SiegeStuckSeconds", "SiegeMinimumTravelBlocks", "SiegeNavigationRefreshSeconds"]),
    ("Corpses and loot", ["CorpseDelaySeconds", "CorpseRot", "CorpseBones", "CorpseRustyGears", "AlphaFuneralMaskChanceDenominator", "AlphaFuneralMaskItemCodes"]),
    ("Stealth equipment", ["FuneralMask1Durability", "OtherFuneralMaskDurability", "MaskDrainSeconds", "RotTunicFirstWearDamage", "RotTunicDrainSeconds", "QuietEyeBandageItemIds", "EyeBandageCodeFragments", "EyeBandageInventoryClasses"]),
    ("Sound", ["MoanMinSeconds", "MoanMaxSeconds", "ZombieMoanVolume", "ZombieHurtSoundVolume", "ZombieHurtSoundCooldownSeconds"]),
]

main = json.load(open(DUMP / "thelastwatchzombiesconfigmain.json"))
adv = json.load(open(DUMP / "thelastwatchzombiesconfigadvanced.json"))
retired = json.load(open(DUMP / "retired-keys.json"))

if QUICKSTART:
    # Server-owner quick start shipped in the mod ZIP as QUICKSTART.md.
    import re
    admin_src = (pathlib.Path(__file__).resolve().parent.parent / "src" / "AdminConfiguration.cs").read_text()
    short = {field: name for name, field in re.findall(r'\["([a-z]+)"\] = nameof\(Config\.([A-Za-z0-9]+)\)', admin_src)}
    allv = {**main, **adv}
    def fmtq(v):
        if isinstance(v, bool): return "on" if v else "off"
        if isinstance(v, float) and v.is_integer(): return str(int(v))
        return str(v)
    QS = [
        ("Spawning", [
            ("MaxActiveZombies", "Most zombies alive at once, server-wide (0 = no limit)"),
            ("zombiepopdailyaddlimit", "New zombies per 96×96-block zone each night"),
            ("AmbientSpawnChancePerSecond", "Chance per second of a spawn attempt in each occupied zone"),
            ("SpawnMaxLightLevel", "Brightest light zombies may spawn in (0–31)"),
            ("ZombieSpawnNightOnly", "Natural spawns only at night (18:00–06:00)"),
            ("DormantPopulationCap", "Most stored (dormant) zombies kept for later"),
            ("SpawnAlphaOnPlayerDeath", "An Alpha zombie appears near where a player died"),
            ("GrabberSpawnChance", "Chance a new zombie is a Grabber"),
            ("HiveSpawnChance", "Chance of a Hive Zombie"),
            ("ScreamerSpawnChance", "Chance of a Screamer"),
            ("BloaterSpawnChance", "Chance of a Bloater"),
        ]),
        ("Seventh-night hordes", [
            ("HordeEveryDays", "A horde every N in-game days"),
            ("HordeDurationSeconds", "Horde length in real seconds"),
            ("HordeWaveIntervalSeconds", "Real seconds between horde waves"),
            ("HordeWaveMaxZombiesPerPlayer", "Most zombies per player per wave"),
        ]),
        ("Combat and sieges", [
            ("AttackDamage", "Damage per zombie hit (Alphas ×1.25)"),
            ("DetectionRange", "How far zombies notice prey, in blocks (32–128)"),
            ("BlockBreakingEnabled", "Stuck zombies damage blocks in their way"),
            ("HordeBlockBreakChance", "Chance per strike during hordes (0.65 otherwise)"),
            ("ProtectClaims", "Land claims are safe from zombie damage and spawning"),
            ("ProtectReinforcedBlocks", "Reinforced blocks are fully immune (otherwise they wear down)"),
        ]),
        ("Infection and cure", [
            ("InfectionFatalEnabled", "Untreated infection can kill"),
            ("InfectionFatalDays", "In-game days until untreated infection kills"),
            ("CureStartWindowDays", "In-game days to take the first cure dose"),
            ("NormalHitsToInfect", "Bites from regular zombies (within 60 s) that infect"),
            ("AlphaHitsToInfect", "Bites from Alpha zombies that infect"),
            ("CureDosesRequired", "Mycobiotic Cure doses for a full cure"),
            ("CureDoseIntervalDays", "Minimum in-game days between doses"),
        ]),
        ("Loot and chat", [
            ("CorpseRustyGears", "Rusty gears dropped per zombie corpse"),
            ("ZoneMigrationChatAnnouncement", "Server-wide chat line when zombies migrate at sunset"),
        ]),
    ]
    out = []
    w = out.append
    w("# The Last Watch Zombies — Server Quick Start\n")
    w("<!-- GENERATED by scripts/gen-config-doc.py --quickstart. Defaults and names come from the source. -->\n")
    w("## First start\n")
    w("1. Install the same mod ZIP on the server **and** every client. Remove older `seventhnightzombies_*`, `thelastwatch_*` and `thelastwatchzombies_*` ZIPs, and back up your world first.")
    w("2. Start the server once. It creates `VintagestoryData/ModConfig/thelastwatchzombiesconfigmain.json` (gameplay) and `…configadvanced.json` (fine tuning — usually leave it alone).")
    w("3. Zombies spawn at night around Survival players. A horde arrives every 7th night. `/zombiehorde` shows when.\n")
    w("## Changing settings\n")
    w("* **In game (admins):** `/zombieconfig <name> <value>` — for example `/zombieconfig activecap 150`. The value is checked, saved to the config file and **takes effect immediately**. `/zombieconfig <name>` shows the current value; `/zombieconfig list` shows every name.")
    w("* **By file:** stop the server, edit the JSON file, start it again. Out-of-range values are corrected automatically on start.\n")
    w("## Most useful settings\n")
    for title, rows in QS:
        w(f"### {title}\n")
        w("| Setting | `/zombieconfig` name | Default | What it does |")
        w("|---|---|---|---|")
        for key, text in rows:
            if key not in allv:
                sys.stderr.write(f"quickstart key missing from source: {key}\n"); sys.exit(1)
            name = f"`{short[key]}`" if key in short else "edit the file"
            if key == "zombiepopdailyaddlimit" and "ZombiePopDailyAddLimit" in short: name = f"`{short['ZombiePopDailyAddLimit']}`"
            w(f"| `{key}` | {name} | {fmtq(allv[key])} | {text} |")
        w("")
    w("## Commands\n")
    w("**Everyone**\n")
    w("| Command | What it does |")
    w("|---|---|")
    w("| `/zombiehorde` | When the next horde comes, and how many zombies are active |")
    w("| `/zombieinfection` | Your infection details (also shown under Character > Traits) |\n")
    w("**Admins** (`controlserver` privilege or the server console)\n")
    w("| Command | What it does |")
    w("|---|---|")
    for cmd, text in [
        ("/zombieconfig list \\| <name> \\| <name> <value>", "List, show or change settings (applied immediately)"),
        ("/zombiepause, /zombieunpause", "Stop / resume all new zombie spawning (saved with the world)"),
        ("/zombiehordestart", "Start a horde now"),
        ("/zombiespawn <type>", "Spawn normal, alpha, grabber, hive, screamer or bloater next to you"),
        ("/snspawnstatus", "Explain why zombies are or are not spawning where you stand"),
        ("/snambienttest", "Try one real spawn and report why it failed"),
        ("/snambientreset", "Let your zone spawn its nightly zombies again (testing)"),
        ("/lastwatch", "Your zone: zombies, quotas, buildings, bonfires"),
        ("/zombiemap", "Toggle the zombie map overlay, then reopen the map (M)"),
        ("/zombiecycle", "Run the sunset migration now"),
        ("/snseed, /snseedneighbor", "Add 10 stored zombies to your zone / the zone east of you"),
        ("/zombieween", "Event: 5 stored zombies in every known zone, 2 cures per online player"),
        ("/zombiediagnostics on\\|off, /zombieperf", "Performance counters"),
        ("/snspawn", "Spawn one random zombie next to you"),
    ]:
        w(f"| `{cmd}` | {text} |")
    w("\nEvery setting, with descriptions: `docs/CONFIGURATION.md` in the source repository, or `CONFIG-REFERENCE.md` in this ZIP for the defaults.")
    print("\n".join(out))
    sys.exit(0)

if REFERENCE:
    # Compact key/default index shipped inside the mod ZIP as CONFIG-REFERENCE.md.
    print("# The Last Watch Zombies — Full Config Reference\n")
    print("<!-- GENERATED by scripts/gen-config-doc.py --reference from tools/ConfigDump output. -->\n")
    print("The mod writes two JSON files under `VintagestoryData/ModConfig/`. Defaults below are generated "
          "from the source after validation. Descriptions: see docs/CONFIGURATION.md in the source repository.\n")
    for title, data in (("Main gameplay settings (`thelastwatchzombiesconfigmain.json`)", main),
                        ("Advanced settings (`thelastwatchzombiesconfigadvanced.json`)", adv)):
        print(f"## {title}\n")
        print("| JSON key | Default |")
        print("|---|---|")
        for k, v in data.items():
            print(f"| `{k}` | `{json.dumps(v)}` |")
        print()
    print("## Retired keys\n")
    print("Accepted when reading an old file, removed when the server rewrites it: " + ", ".join(f"`{k}`" for k in retired) + ".\n")
    print("Spawn pause is immediate world state, controlled by `/zombiepause` and `/zombieunpause`, not a restart-only config field.")
    sys.exit(0)
allkeys = {**{k: ("main", v) for k, v in main.items()}, **{k: ("advanced", v) for k, v in adv.items()}}

def fmt(v):
    s = json.dumps(v)
    return s if len(s) <= 40 else s[:37] + "…"

out = []
w = out.append
w("# Configuration Reference\n")
w("<!-- GENERATED by scripts/gen-config-doc.py from tools/ConfigDump output. Do not hand-edit defaults. -->\n")
w("Defaults below are produced from `src/Config.cs` (after `Validate()`). The shipped `DEFAULT-*.json` files and `CONFIG-REFERENCE.md` are generated from the same output by `scripts/refresh-config-files.sh`; CI fails if they drift.\n")
w("## Files and lifecycle\n")
w("* `VintagestoryData/ModConfig/thelastwatchzombiesconfigmain.json` — gameplay settings (`main`).")
w("* `VintagestoryData/ModConfig/thelastwatchzombiesconfigadvanced.json` — budgets, tuning, markers (`advanced`).")
w("* Load order: built-in defaults ← advanced ← main, then `Config.Validate()` clamps every value into its allowed range (out-of-range values are silently corrected).")
w("* **The server rewrites both files on every start** with every non-retired key, so values persist but unknown or retired keys are removed. Back up custom files before upgrading.")
w("* `/zombieconfig <name> [value]` validates the value, saves it to the owning file **and applies it to the running server immediately** (dependent settings are re-validated together). The 60 short names are listed in `COMMANDS.md` and the most useful ones in `QUICKSTART.md`. Hand edits to the JSON files still need a restart.")
w("* Pre-merge files (`seventhnightconfig*.json`, `thelastwatchconfig*.json`) are not read.")
w("* Clients read their own local copy only for funeral-mask durability display.\n")
w("Status column: **A** active · **I** inert in v2.0.9 (written, never read) · **M** one-time migration marker (no gameplay effect).\n")
w("### Regenerating\n")
w("```bash\nbash scripts/fetch-vs-server.sh 1.22.7 vs-server\nbash scripts/refresh-config-files.sh vs-server\n```\n")
seen = set()
missing = []
for title, keys in GROUPS:
    w(f"## {title}\n")
    w("| Key | File | Default | St | Description |")
    w("|---|---|---|---|---|")
    for k in keys:
        if k not in allkeys:
            missing.append(k); continue
        seen.add(k)
        f, v = allkeys[k]
        d = D.get(k, "**UNDOCUMENTED**")
        st = "A"
        if d in (I, M):
            st, d = d, ("Not read by any code path." if d == I else "Upgrade marker; leave unchanged.")
        elif "No effect in v2.0.9" in d:
            st = "I"
        w(f"| `{k}` | {f} | `{fmt(v)}` | {st} | {d} |")
    w("")
rest = [k for k in allkeys if k not in seen]
if rest:
    w("## Migration markers\n")
    w("| Key | File | Default | St | Description |")
    w("|---|---|---|---|---|")
    for k in rest:
        f, v = allkeys[k]
        d = D.get(k, "**UNDOCUMENTED**")
        st = "A"
        if d in (I, M):
            st, d = d, ("Not read by any code path." if d == I else "Upgrade marker; leave unchanged.")
        w(f"| `{k}` | {f} | `{fmt(v)}` | {st} | {d} |")
    w("")
w("## Retired keys (accepted on read, removed on write)\n")
w(", ".join(f"`{k}`" for k in retired) + "\n")
w("## Fixed constants (not configurable)\n")
w("| Constant | Value | Source |")
w("|---|---|---|")
w("| Zone size | 3×3 chunks (96×96 blocks) | `WatchZone.ChunksPerSide` |")
w("| Stronghold reinforcement (wood / copper / iron) | 5 / 50 / 300 | `LastWatchZones` |")
w("| Permanent settlement | ≥25 stone paths + ≥1 stronghold | `LastWatchZones.Placed` |")
w("| Bonfire: protect / lure rings | ≥3 stacks / 6, 9, 12 stacks → 1, 2, 3 rings | `LastWatchZones.BonfireRadius` |")
w("| Bonfire burn time | 9 in-game hours | `BlockEntityPitBonfire.BurnHours` |")
w("| Rot bait lure radius / bite interval | 32 blocks / 2 s per pile | `RotBaitRules` |")
w("| Stake trap radius / hold / damage / lifetime | 2 blocks / 3–20 s / 5 / 30 s | `PalisadeTrapRules`, `StakeTimer` |")
w("| Screamer summons | 10 normals, retried for 60 s | `ZombieVariants.cs` |")
w("| Variant HP (Grabber/Hive/Screamer/Bloater) | entity JSON | `assets/seventhnightzombies/entities/*.json` |")
w("| Cure dose heal | 20 HP | `TryTakeCureDose` |")
w("| Zombieween seed | 5 normals per zone + 2 cures per online player | `LastWatchZones` |")
w("| Psychedelic strengths | 0.4, 0.6, 0.8, 1.0, 1.4, 2.0 | `MushroomPsychedelicStrengths` |")
print("\n".join(out))
if missing:
    print(f"\n<!-- keys listed in GROUPS but absent from source: {', '.join(missing)} -->")
undoc = [k for k in allkeys if k not in D]
if undoc:
    sys.stderr.write("Undocumented keys: " + ", ".join(undoc) + "\n")
