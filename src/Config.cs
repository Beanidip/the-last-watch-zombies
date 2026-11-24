using Newtonsoft.Json;

namespace SeventhNightZombies;

public class Config
{
    // One roll per non-Alpha spawn. The remainder is an ordinary zombie.
    public double GrabberSpawnChance = .04;
    public double HiveSpawnChance = .02;
    public double ScreamerSpawnChance = .01;
    public double BloaterSpawnChance = .03;
    public double GrabberLeapCooldownSeconds = 8;
    public double HiveBeeCooldownSeconds = 30;
    public double BloaterBurstRadius = 4;
    public float BloaterBurstDamage = 4;
    public int BloaterRotDrop = 32;
    public double BloaterPoisonSeconds = 10;
    public float BloaterPoisonDamagePerSecond = .5f;
    public bool PerformanceDiagnostics = false;
    public int AiPreyScansPerSecond = 160;
    public int AiRouteRequestsPerSecond = 60;
    public double AiPreyScanIntervalSeconds = 0.85;
    // Moving prey is steered toward directly in nearby open space; this does
    // not queue an expensive A* request on every movement update.
    public double PursuitSteerIntervalSeconds = 0.55;
    public double PursuitDirectWalkRangeBlocks = 10;
    public double PursuitRepathTargetMovementBlocks = 1.5;
    // Time spent without valid prey before using the engine's expiry pipeline.
    // One scalar per loaded zombie; no global live-entity ledger.
    public double ZombieIdleDespawnSeconds = 300;
    // Limit repeated targeting of traders and humanoid NPCs. A zombie gets one
    // two-minute interest window per encounter; chasing another NPC never refreshes
    // it. Only losing a player target opens a fresh window.
    public double NpcTraderInterestSeconds = 120;
    // An untargeted zombie stays active while a living Survival player is nearby.
    // This check is rate-limited on each zombie and shares a cached player-position snapshot.
    public bool ZombieStayActiveNearPlayers = true;
    public int ZombieKeepActiveRadiusBlocks = 64;
    public double ZombiePresenceCheckSeconds = 10;
    // A lost target causes one short walk toward its last observed position
    // before the zombie resumes Alpha-following or slower idle roaming.
    public double ZombieLostTargetPursuitSeconds = 25;
    public int LastSeenPursuitTuningVersion = 0;
    public void UpgradeLastSeenPursuit()
    {
        if (LastSeenPursuitTuningVersion >= 1) return;
        if (ZombieLostTargetPursuitSeconds == 10) ZombieLostTargetPursuitSeconds = 25;
        LastSeenPursuitTuningVersion = 1;
    }

    // Last Watch migration: once per in-game day at ZoneMigrationHour. All
    // chances are per dormant individual and reflect activity accumulated
    // since the preceding sunset roll.
    public bool LastWatchEnabled = true;
    // World-calendar hour for the once-per-day roll (18:00 by default).
    public double ZoneMigrationHour = 18;
    // Base-only scheduled horde clock, independent of Last Watch migration.
    public double HordeStartHour = 18;
    public double HordeMinutesBeforeStartHour = 5;
    // The scheduled seventh-night horde starts HordeMinutesBeforeStartHour
    // BEFORE the sunset migration roll. Manual hordes start immediately.
    // Ordinary migrations move one zone; only an ACTIVE horde at sunset may
    // take a second adjacent step toward the chosen attraction source.
    public int HordeMigrationMaxZoneSteps = 2;
    public double ZonePlayerPresenceBonus = 0.10;
    public double ZoneSettlementBonus = 0.30;
    // Base chance (per dormant zombie per sunset) to drift without any attraction.
    public double ZoneBaseDailyMigrationChance = 0.02;
    // Retired (see StandaloneConfigStorage.RetiredKeys): accepted on read only.
    public double ZoneChangesBonusPerBlock = 0.006;
    public double ZoneStonePathBonusPerBlock = 0.002;
    public double ZoneDeathBonusPerDeath = 0.075;
    public double ZoneStrongholdBonus = 0.40;
    public double ZoneHotspotBonus = 0.15;
    public double ZoneMaxDailyMigrationChance = 0.85;
    public int ZoneHotspotBlocksChanged = 50;
    public int ZoneAttractionMinBlocksChanged = 10;
    public int ZoneMaxPathsCounted = 300;
    public bool ZoneMigrationChatAnnouncement = true;
    public int ZoneBellRangeZones = 1;
    public bool LastWatchVerboseLogging = false;
    public double ZoneBonfireAttractionBonus = 0.70;
    // New migration tiers. One score/radius per attracting zone per sunset.
    // Strongholds and settlements can compound with residents, industry and livestock.
    public double ZoneAttractionTier4Threshold = 0.20;
    public double ZoneAttractionTier6Threshold = 0.40;
    public double ZoneAttractionTier8Threshold = 0.65;
    public double ZoneAttractionTier10Threshold = 0.90;
    public double ZoneStayWeight = 0.35;
    public double ZoneRandomNeighborWeight = 0.65;
    public double ZoneForgeActivityBonus = 0.08;
    public double ZoneCharcoalActivityBonus = 0.10;
    public double ZoneAnvilActivityBonus = 0.06;
    public int ZoneFarmAnimalThreshold = 10;
    public double ZoneFarmAnimalAttractionBonus = 0.12;
    public int ZoneTraderDormantSeed = 10;
    // Once-daily search of loaded block entities (forge, anvil, charcoal pit)
    // near the surface, only in stronghold and Survival-player zones.
    public int ZoneIndustrySurfaceBelowBlocks = 8;
    public int ZoneIndustrySurfaceAboveBlocks = 24;
    public int ZoneBellOverrunThreshold = 30;

    // Damaging hits within HitWindowSeconds needed to infect, per attacker type.
    public int NormalHitsToInfect = 4;
    public int AlphaHitsToInfect = 2;
    public double HitWindowSeconds = 60;
    // Damage is dealt as discrete poison ticks in real-time, not every game tick.
    public double InfectionTickSeconds = 10;
    public float InfectionDamagePerTick = 0.2f;
    // Treatment requires six doses separated by full in-game days.
    // Untreated infection becomes fatal after seven in-game days by default.
    public bool InfectionFatalEnabled = true;
    public double InfectionFatalDays = 7;
    public int CureDosesRequired = 6;
    public double CureDoseIntervalDays = 1;
    // The first dose must be taken within this many in-game days of infection.
    // A course started in time can always be finished: while treated, the
    // fatal deadline extends to firstDose + CureDosesRequired x interval.
    public double CureStartWindowDays = 2;
    // Taking the first valid dose doubles the time between poison ticks.
    public double TreatedInfectionTickSeconds = 20;
    // Accepted cure doses progress in order through six mushroom-effect
    // strengths, from weakest at dose one to strongest at the sixth dose.
    // Purely visual: does not add mushroom food, poison, or saturation effects.
    public bool CurePsychedelicEnabled = true;
    public int[] QuietEyeBandageItemIds = [3889, 3890];
    // Corpse lifetime is in real seconds while the corpse chunk is loaded.
    public double CorpseDelaySeconds = 30;
    public int CorpseRot = 3;
    public int CorpseBones = 2;
    // Rusty gears replaced small raw hides in 2.0.10. A saved CorpseSmallHides
    // value is carried over once by StandaloneConfigStorage.Load.
    public int CorpseRustyGears = 1;
    // Exactly one of the eight vanilla funeral masks has a 10% chance to drop
    // when an Alpha Zombie's corpse poofs and its normal loot is spawned.
    public int AlphaFuneralMaskChanceDenominator = 10;
    // All eight vanilla masks provide identical crouching-only camouflage.
    // Mask 1 lasts longer; none of the masks attract zombie followers.
    // Only applies to vanilla funeral masks; while equipped AND crouching.
    public int FuneralMask1Durability = 200;
    public int OtherFuneralMaskDurability = 50;
    public double MaskDrainSeconds = 1;
    // Rot Covered Tunic: one durability per RotTunicDrainSeconds while equipped.
    // Initial equip applies one delayed 3 HP poison effect; speed -30% is the
    // native wearable stat modifier (-0.30 walkSpeed) in the tunic JSON.
    public float RotTunicFirstWearDamage = 3f;
    // Rot tunic loses one durability per real-time second WHILE EQUIPPED.
    public double RotTunicDrainSeconds = 1;
    public static readonly string[] DefaultAlphaFuneralMaskItemCodes =
    [
        "game:clothes-face-funeralmask1",
        "game:clothes-face-funeralmask2",
        "game:clothes-face-funeralmask3",
        "game:clothes-face-funeralmask4",
        "game:clothes-face-funeralmask5",
        "game:clothes-face-funeralmask6",
        "game:clothes-face-funeralmask7",
        "game:clothes-face-funeralmask8"
    ];
    // Explicit pool of item codes (not registry-generated item IDs).
    // Legacy config files with [] also fall back to the eight defaults.
    // Custom entries may be "game:item-code" or just "item-code".
    public string[] AlphaFuneralMaskItemCodes = (string[])DefaultAlphaFuneralMaskItemCodes.Clone();
    public float AlphaSpeedMultiplier = 1.65f;
    public float AlphaAttackDamageMultiplier = 1.25f;
    // Base-mod standalone options. A zero active cap explicitly disables the cap.
    // Active means ALIVE and LOADED; corpses, unloaded chunks and stored dormant
    // counts never consume slots. Event-driven registry avoids tick-based scans.
    public int MaxActiveZombies = 250;
    // Restrict routine ambient additions and optional Last Watch wake-ups to
    // scheduled HordeEveryDays nights. Manual /zombiehordestart still summons a horde.
    public bool ZombieSpawnHordeNightOnly = false;
    // Strict mode: ambient creation, Last Watch wakes and death-created Alphas
    // only appear at night. Scheduled opening horde waves are exempt because
    // they deliberately begin five in-game minutes before 18:00.
    public bool ZombieSpawnNightOnly = true;
    // How many in-game days between scheduled hordes (7 default).
    public int HordeEveryDays = 7;
    public double HordeDurationSeconds = 600;
    public double HordeWaveIntervalSeconds = 180;
    public int HordeWaveMinZombiesPerPlayer = 1;
    // Initial horde wave spawns at least three per player (when suitable sites exist).
    public int HordeInitialMinZombiesPerPlayer = 3;
    public int HordeWaveMaxZombiesPerPlayer = 5;
    public int ManualHordeMinDistance = 20;
    public int ManualHordeMaxDistance = 48;
    // A wider search if an initial horde wave cannot locate suitable loaded sites.
    public int HordeInitialFallbackMaxDistance = 48;
    // If the opening wave cannot reach three zombies per player, retry every
    // few seconds using additional locations. Spawn caps still apply.
    public double HordeInitialRetrySeconds = 10;
    public double HordeInitialRetryWindowSeconds = 120;
    public int HordeInitialRecoveryMaxDistance = 96;
    // Maximum real ambient light (sunlight adjusted for time of day OR block light),
    // inclusive: 12 allows light levels 0 through 12. Applies to ordinary
    // ambient and Last Watch dormant reactivation; horde exceptions are separate.
    public int SpawnMaxLightLevel = 12;
    // During a horde, regular zombies may appear even in torchlight and daylight.
    // Still respects terrain, loaded chunks and population caps.
    // Horde waves use ambient terrain and block-light checks by default.
    // Optional override for servers that want zombies in fully lit towns.
    public bool HordeSpawnIgnoreLightLevel = false;
    // Death-created Alphas obey automatic nighttime and ambient light rules.
    // The horde light override affects explicit horde waves, not death Alphas.
    // Horde mobs may appear inside land claims; claims STILL protect player blocks.
    public bool HordeSpawnsInClaims = false;
    // The player the spawn is centered on uses SpawnMinDistance.
    // Other players need only this smaller safety distance; groups won't block every candidate.
    public int SpawnMinimumOtherPlayerDistance = 12;
    // Optional once-per-minute server log when a night spawn attempt finds no candidates.
    public bool SpawnDebugLogging = true;
    public int SpawnPositionAttempts = 64;
    // Ordinary ambient growth is surface-only and after dark. Horde and dormant
    // wake rules stay separate; no cave spawning in the ambient pathway.
    public bool AmbientSpawnSurfaceOnly = true;
    public int SpawnSurfaceVerticalTolerance = 5;
    // Death alpha is delayed and placed at a safe distance, never on the corpse/camera.
    public bool SpawnAlphaOnPlayerDeath = true;
    public int AlphaDeathSpawnDelayMilliseconds = 1200;
    // One-time tuning migration when an existing ModConfig predates v1.5.15.
    public float ManualHordeSoundRange = 38;
    public int ManualHordeMoans = 3;
    public string[] EyeBandageCodeFragments = ["eyepatch", "eye-patch", "eye-bandage", "eyebandage", "eyeband", "bandage-eye", "patch-eye", "eyecover", "eye-cover"];
    public string[] EyeBandageInventoryClasses = ["character", "accessory", "equipment", "wearable"];
    public double NightStartsAtHour = 18;
    public double NightEndsAtHour = 6;
    // Independent probability of ONE new ambient zombie per eligible player per
    // real second of server time (applied on the existing 1s server tick).
    // A failed position check doesn't debit any per-player or per-zone quota.
    // Chance for ONE new ambient spawn attempt PER OCCUPIED ZONE per second.
    public double AmbientSpawnChancePerSecond = 0.30;
    // New zone ambient spawns are nighttime-only by default.
    // Set true for the original nighttime-only ambient creation rule.
    public bool AmbientSpawnRequiresNight = true;
    // Maximum new ambient zombies shared by ALL Survival players in ONE zone per sunset cycle.
    // Migrants, horde spawns, and dormant reactivations never consume this quota.
    [JsonProperty("zombiepopdailyaddlimit")]
    public int ZombiePopDailyAddLimit = 5;
    public int DormantPopulationCap = 250;
    // Maximum SUCCESSFUL ordinary new ambient zombies credited to each Survival
    // player per in-game night. The limit survives reconnects and world saves.
    // Horde waves, admin test summons, death Alphas and dormant reactivations
    // do not consume this quota. Zero disables ordinary new ambient creation.
    public int MaxNaturalZombiesPerPlayerPerNight = 5;
    public float AlphaDetectionRangeBonus = 32;
    public bool FollowAlphaEnabled = true;
    public float AlphaFollowRange = 32;
    public float AlphaFollowStopDistance = 3;
    public double AlphaFollowRefreshSeconds = 3;
    // Legacy: simplified pursuit always takes precedence; not consulted.
    // On each acquisition scan, a normal zombie following an Alpha has this
    // chance to independently pursue a detectable survival player. Once
    // acquired, the ordinary player-target retention rules apply.
    public float AlphaFollowPlayerBreakChance = 0.75f;
    public float NpcPlayerRetargetChance = .25f;
    public double NpcPlayerRetargetIntervalSeconds = 5;
    // Retired (accepted on read only).
    public double DormantWakeRangeMultiplier = 2;
    // Dormant zombies wake only in the 3x3-chunk zone a Survival player occupies,
    // after that zone's new-ambient allowance is spent.
    // Optional successful wake-up ceiling per zone per sunset cycle.
    // Zero means UNLIMITED: wake all stored zombies over multiple small batches.
    // Positive values restore a strict per-zone daily reactivation limit.
    public int DormantReactivationPerZonePerDay = 0;
    // At most this many dormant zombies per occupied zone per wake pass.
    public int DormantWakeBatchSize = 3;
    // Each wake pass is deliberately separate from ambient spawning.
    public double DormantWakeIntervalSeconds = 5;
    // Limit total reactivations across all occupied zones on one server pass.
    public int DormantMaxTotalWakesPerPass = 8;
    public int SpawnMinDistance = 20;
    public int SpawnMaxDistance = 48;
    // Sneaking affects target acquisition (not ambient spawn rates) on the server.
    // A sneaking player has only 40% of the normal detection range and a 25%
    // acquisition chance per scan. At <= 4 blocks, stealth cannot conceal them.
    public float SneakDetectionRangeMultiplier = 0.40f;
    public float SneakAcquireChance = 0.25f;
    public float SneakAlwaysDetectedDistance = 4f;
    // Target-list entries are full entity codes or trailing-* prefixes. For example:
    //   "game:wolf-*", "game:humanoid-trader-*", "someaddon:bandit-*".
    // Whitelist adds creatures to the default targets; blacklist removes them.
    // Blacklist wins if both match. Neither list overrides game-mode protection,
    // other zombies, or the absolute rust/mechanical exclusions.
    public string[] TargetWhitelist = [];
    public string[] TargetBlacklist = [];
    // Non-humanoid vanilla (game:) entities, including animals, are excluded
    // by default. Modded living EntityAgents and humanoids remain targetable.
    public bool IgnoreVanillaAnimals = true;
    public float DetectionRange = 32;
    public float HordeDetectionRange = 80;
    public float MoveSpeed = 0.018f;
    public bool RoamEnabled = true;
    public double RoamWalkSeconds = 30;
    public double RoamPauseSeconds = 10;
    public double RoamRouteRefreshSeconds = 4;
    public float RoamSpeedMultiplier = 0.55f;
    // Subtle side-to-side drift and uneven walking pace during idle roaming.
    public double RoamShambleSideDriftBlocks = 0.65;
    public double RoamShambleSpeedVariation = 0.12;
    // Legacy: the simplified idle AI follows its last prey heading directly.
    // Walk on in the last OBSERVED direction before seeking a new target.
    // Set seconds to 0 to disable the follow-through behavior.
    public double LostTargetFollowThroughSeconds = 5;
    public double LostTargetTurnSeconds = 5;
    public double LostTargetTurnAngleDegrees = 25;
    public double LostTargetFollowThroughDistanceBlocks = 7;
    public float AttackDamage = 2;
    public float AttackCooldownSeconds = 2.5f;
    // Siege: normal routes first. Only attack nearby barriers after progress stalls.
    public bool SiegeEnabled = true;
    public double SiegeProgressSampleSeconds = 2.5;
    public double SiegeStuckSeconds = 5.0;
    public double SiegeMinimumTravelBlocks = 0.4;
    public double SiegeNavigationRefreshSeconds = 2.0;
    public double SiegeRepathDelaySeconds = 1.0;
    public bool BlockBreakingEnabled = true;
    public double BlockBreakIntervalSeconds = 2.5;
    public float BlockBreakChance = 0.65f;
    public float HordeBlockBreakChance = 0.90f;
    public int NonHordeBlockReinforcementDamage = 1;
    public int HordeBlockReinforcementDamage = 2;
    // Only blocks physically between the zombie and its target and this near
    // the zombie can be attacked. Claims and tile entities remain protected.
    public float BlockBreakReach = 2.2f;
    public bool HordeOpensDoors = true;
    public float HordeDoorAttemptCooldownSeconds = 2;
    // Claims always stay protected by default. When enabled, reinforcement
    // protection prevents both wall strikes and opening reinforced doors.
    // When disabled, reinforcement can be chipped by configured strike damage.
    public bool ProtectClaims = true;
    public bool ProtectReinforcedBlocks = false;
    public float MoanMinSeconds = 1;
    public float MoanMaxSeconds = 30;
    // Volume for randomized zombie groans and separate impact grunts.
    public float ZombieMoanVolume = 0.9f;
    public float ZombieHurtSoundVolume = 0.85f;
    public float ZombieHurtSoundCooldownSeconds = 0.65f;

    public void Validate()
    {
        GrabberSpawnChance = Finite(GrabberSpawnChance, .04, 0, 1);
        HiveSpawnChance = Finite(HiveSpawnChance, .02, 0, 1);
        ScreamerSpawnChance = Finite(ScreamerSpawnChance, .01, 0, 1);
        BloaterSpawnChance = Finite(BloaterSpawnChance, .03, 0, 1);
        double variantTotal = GrabberSpawnChance + HiveSpawnChance + ScreamerSpawnChance + BloaterSpawnChance;
        if (variantTotal > 1) { GrabberSpawnChance /= variantTotal; HiveSpawnChance /= variantTotal;
            ScreamerSpawnChance /= variantTotal; BloaterSpawnChance /= variantTotal; }
        GrabberLeapCooldownSeconds = Finite(GrabberLeapCooldownSeconds, 8, 3, 120);
        HiveBeeCooldownSeconds = Finite(HiveBeeCooldownSeconds, 30, 20, 300);
        BloaterBurstRadius = Finite(BloaterBurstRadius, 4, 1, 8);
        BloaterBurstDamage = (float)Finite(BloaterBurstDamage, 4, 0, 30);
        BloaterRotDrop = Math.Clamp(BloaterRotDrop, 1, 64);
        BloaterPoisonSeconds = Finite(BloaterPoisonSeconds, 10, 0, 60);
        BloaterPoisonDamagePerSecond = (float)Finite(BloaterPoisonDamagePerSecond, .5, 0, 5);

        AiPreyScansPerSecond = Math.Clamp(AiPreyScansPerSecond, 10, 2000);
        AiRouteRequestsPerSecond = Math.Clamp(AiRouteRequestsPerSecond, 5, 1000);
        AiPreyScanIntervalSeconds = Finite(AiPreyScanIntervalSeconds, .85, .5, 10);
        PursuitSteerIntervalSeconds = Finite(PursuitSteerIntervalSeconds, .55, .25, 3);
        PursuitDirectWalkRangeBlocks = Finite(PursuitDirectWalkRangeBlocks, 10, 3, 20);
        PursuitRepathTargetMovementBlocks = Finite(PursuitRepathTargetMovementBlocks, 1.5, .5, 10);
        ZoneMigrationHour = Finite(ZoneMigrationHour, 18, 0, 23.99);
        HordeStartHour = Finite(HordeStartHour, 18, 0, 23.99);
        HordeMinutesBeforeStartHour = Finite(HordeMinutesBeforeStartHour, 5, 0, 120);
        ZonePlayerPresenceBonus = Finite(ZonePlayerPresenceBonus, .10, 0, 1);
        ZoneAttractionTier4Threshold = Finite(ZoneAttractionTier4Threshold, .20, .01, 1);
        ZoneAttractionTier6Threshold = Finite(ZoneAttractionTier6Threshold, .40, ZoneAttractionTier4Threshold, 2);
        ZoneAttractionTier8Threshold = Finite(ZoneAttractionTier8Threshold, .65, ZoneAttractionTier6Threshold, 3);
        ZoneAttractionTier10Threshold = Finite(ZoneAttractionTier10Threshold, .90, ZoneAttractionTier8Threshold, 4);
        ZoneStayWeight = Finite(ZoneStayWeight, .35, .01, 10);
        ZoneRandomNeighborWeight = Finite(ZoneRandomNeighborWeight, .65, .01, 10);
        ZoneForgeActivityBonus = Finite(ZoneForgeActivityBonus, .08, 0, 1);
        ZoneCharcoalActivityBonus = Finite(ZoneCharcoalActivityBonus, .10, 0, 1);
        ZoneAnvilActivityBonus = Finite(ZoneAnvilActivityBonus, .06, 0, 1);
        ZoneFarmAnimalThreshold = Math.Clamp(ZoneFarmAnimalThreshold, 0, 1000);
        ZoneFarmAnimalAttractionBonus = Finite(ZoneFarmAnimalAttractionBonus, .12, 0, 1);
        ZoneTraderDormantSeed = Math.Clamp(ZoneTraderDormantSeed, 0, 100);
        ZoneIndustrySurfaceBelowBlocks = Math.Clamp(ZoneIndustrySurfaceBelowBlocks, 0, 64);
        ZoneIndustrySurfaceAboveBlocks = Math.Clamp(ZoneIndustrySurfaceAboveBlocks, 0, 64);
        ZoneBellOverrunThreshold = Math.Clamp(ZoneBellOverrunThreshold, 0, 10000);
        ZoneSettlementBonus = Finite(ZoneSettlementBonus,0.30,0,1);
        ZoneBonfireAttractionBonus = Finite(ZoneBonfireAttractionBonus,0.70,0,1);
        ZoneBaseDailyMigrationChance = Finite(ZoneBaseDailyMigrationChance,0.02,0,1);
        ZoneChangesBonusPerBlock = Finite(ZoneChangesBonusPerBlock,0.006,0,1);
        ZoneStonePathBonusPerBlock = Finite(ZoneStonePathBonusPerBlock,0.002,0,1);
        ZoneDeathBonusPerDeath = Finite(ZoneDeathBonusPerDeath,0.075,0,1);
        ZoneStrongholdBonus = Finite(ZoneStrongholdBonus,0.40,0,1);
        ZoneHotspotBonus = Finite(ZoneHotspotBonus,0.15,0,1);
        ZoneMaxDailyMigrationChance = Finite(ZoneMaxDailyMigrationChance,0.85,0,1);
        ZoneHotspotBlocksChanged = Math.Clamp(ZoneHotspotBlocksChanged,10,10000);
        ZoneAttractionMinBlocksChanged = Math.Clamp(ZoneAttractionMinBlocksChanged,1,ZoneHotspotBlocksChanged);
        ZoneMaxPathsCounted = Math.Clamp(ZoneMaxPathsCounted,1,100000);
        ZoneBellRangeZones = Math.Clamp(ZoneBellRangeZones,0,10);
        NormalHitsToInfect = Math.Clamp(NormalHitsToInfect, 1, 100);
        AlphaHitsToInfect = Math.Clamp(AlphaHitsToInfect, 1, 100);
        HitWindowSeconds = Finite(HitWindowSeconds, 60, 1, 3600);
        InfectionTickSeconds = Finite(InfectionTickSeconds, 10, 1, 60);
        TreatedInfectionTickSeconds = Finite(TreatedInfectionTickSeconds, 20, InfectionTickSeconds, 180);
        InfectionDamagePerTick = (float)Finite(InfectionDamagePerTick, 0.2, 0, 50);
        InfectionFatalDays = Finite(InfectionFatalDays, 7, 1, 365);
        CureDosesRequired = Math.Clamp(CureDosesRequired, 1, 10);
        CureDoseIntervalDays = Finite(CureDoseIntervalDays, 1, 0.05, 30);
        CureStartWindowDays = Finite(CureStartWindowDays, 2, 0.05, InfectionFatalDays);
        QuietEyeBandageItemIds ??= [3889, 3890];
        CorpseDelaySeconds = Finite(CorpseDelaySeconds, 30, 1, 3600);
        CorpseRot = Math.Clamp(CorpseRot, 0, 64);
        CorpseBones = Math.Clamp(CorpseBones, 0, 64);
        CorpseRustyGears = Math.Clamp(CorpseRustyGears, 0, 64);
        AlphaFuneralMaskChanceDenominator = Math.Clamp(AlphaFuneralMaskChanceDenominator, 1, 10000);
        FuneralMask1Durability = Math.Clamp(FuneralMask1Durability, 1, 20000);
        OtherFuneralMaskDurability = Math.Clamp(OtherFuneralMaskDurability, 1, 20000);
        MaskDrainSeconds = Finite(MaskDrainSeconds, 1, 1, 600);
        RotTunicFirstWearDamage = (float)Finite(RotTunicFirstWearDamage, 3, 0, 20);
        RotTunicDrainSeconds = Finite(RotTunicDrainSeconds, 1, 0.1, 600);
        AlphaFuneralMaskItemCodes ??= (string[])DefaultAlphaFuneralMaskItemCodes.Clone();
        AlphaSpeedMultiplier = (float)Finite(AlphaSpeedMultiplier, 1.65, 1.01, 5);
        AlphaAttackDamageMultiplier = (float)Finite(AlphaAttackDamageMultiplier, 1.25, 1, 5);
        MaxActiveZombies = Math.Clamp(MaxActiveZombies, 0, 100000);
        HordeEveryDays = Math.Clamp(HordeEveryDays, 1, 365);
        HordeMigrationMaxZoneSteps = Math.Clamp(HordeMigrationMaxZoneSteps, 1, 2);
        HordeDurationSeconds = Finite(HordeDurationSeconds, 600, 10, 3600);
        HordeWaveIntervalSeconds = Finite(HordeWaveIntervalSeconds, 180, 10, 3600);
        HordeWaveMinZombiesPerPlayer = Math.Clamp(HordeWaveMinZombiesPerPlayer, 1, 50);
        HordeWaveMaxZombiesPerPlayer = Math.Clamp(HordeWaveMaxZombiesPerPlayer, HordeWaveMinZombiesPerPlayer, 50);
        HordeInitialMinZombiesPerPlayer = Math.Clamp(HordeInitialMinZombiesPerPlayer, 1, 50);
        HordeWaveMaxZombiesPerPlayer = Math.Max(HordeWaveMaxZombiesPerPlayer, HordeInitialMinZombiesPerPlayer);
        ManualHordeMinDistance = Math.Clamp(ManualHordeMinDistance, 6, 64);
        ManualHordeMaxDistance = Math.Clamp(ManualHordeMaxDistance, ManualHordeMinDistance + 1, 96);
        HordeInitialFallbackMaxDistance = Math.Clamp(HordeInitialFallbackMaxDistance, ManualHordeMaxDistance, 128);
        HordeInitialRetrySeconds = Finite(HordeInitialRetrySeconds, 10, 2, 60);
        HordeInitialRetryWindowSeconds = Finite(HordeInitialRetryWindowSeconds, 120, HordeInitialRetrySeconds, 600);
        HordeInitialRecoveryMaxDistance = Math.Clamp(HordeInitialRecoveryMaxDistance, HordeInitialFallbackMaxDistance, 192);
        SpawnMaxLightLevel = Math.Clamp(SpawnMaxLightLevel, 0, 31);
        SpawnPositionAttempts = Math.Clamp(SpawnPositionAttempts, 8, 256);
        SpawnSurfaceVerticalTolerance = Math.Clamp(SpawnSurfaceVerticalTolerance, 0, 8);
        AlphaDeathSpawnDelayMilliseconds = Math.Clamp(AlphaDeathSpawnDelayMilliseconds, 250, 10000);
        ManualHordeSoundRange = (float)Finite(ManualHordeSoundRange, 38, 8, 128);
        ManualHordeMoans = Math.Clamp(ManualHordeMoans, 1, 8);
        EyeBandageCodeFragments ??= [];
        EyeBandageInventoryClasses ??= [];
        NightStartsAtHour = Finite(NightStartsAtHour, 18, 12, 23.9);
        NightEndsAtHour = Finite(NightEndsAtHour, 6, 0, 11.9);
        AmbientSpawnChancePerSecond = Finite(AmbientSpawnChancePerSecond, .30, 0, 1);
        ZombiePopDailyAddLimit = Math.Clamp(ZombiePopDailyAddLimit, 0, 1000);
        DormantPopulationCap = Math.Clamp(DormantPopulationCap, 0, 1000000);
        ZombieIdleDespawnSeconds = Finite(ZombieIdleDespawnSeconds, 300, 30, 3600);
        NpcTraderInterestSeconds = Finite(NpcTraderInterestSeconds, 120, 10, 3600);
        ZombieKeepActiveRadiusBlocks = Math.Clamp(ZombieKeepActiveRadiusBlocks, 16, 256);
        ZombiePresenceCheckSeconds = Finite(ZombiePresenceCheckSeconds, 10, 2, 60);
        ZombieLostTargetPursuitSeconds = Finite(ZombieLostTargetPursuitSeconds, 25, 0, 60);
        MaxNaturalZombiesPerPlayerPerNight = Math.Clamp(MaxNaturalZombiesPerPlayerPerNight, 0, 10000);
        SpawnMinimumOtherPlayerDistance = Math.Clamp(SpawnMinimumOtherPlayerDistance, 2, 64);
        AlphaDetectionRangeBonus = (float)Finite(AlphaDetectionRangeBonus, 32, 0, 128);
        AlphaFollowRange = (float)Finite(AlphaFollowRange, 32, 4, 256);
        AlphaFollowStopDistance = (float)Finite(AlphaFollowStopDistance, 3, 1, AlphaFollowRange);
        AlphaFollowRefreshSeconds = Finite(AlphaFollowRefreshSeconds, 3, .5, 30);
        AlphaFollowPlayerBreakChance = (float)Finite(AlphaFollowPlayerBreakChance, .75, 0, 1);
        NpcPlayerRetargetChance = (float)Finite(NpcPlayerRetargetChance, .25, 0, 1);
        NpcPlayerRetargetIntervalSeconds = Finite(NpcPlayerRetargetIntervalSeconds, 5, 1, 120);
        DormantWakeRangeMultiplier = Finite(DormantWakeRangeMultiplier, 2, 1, 8);
        DormantReactivationPerZonePerDay = Math.Clamp(DormantReactivationPerZonePerDay, 0, 10000);
        DormantWakeBatchSize = Math.Clamp(DormantWakeBatchSize, 1, 16);
        DormantWakeIntervalSeconds = Finite(DormantWakeIntervalSeconds, 5, 1, 60);
        DormantMaxTotalWakesPerPass = Math.Clamp(DormantMaxTotalWakesPerPass, 1, 32);
        SpawnMinDistance = Math.Clamp(SpawnMinDistance, 20, 96);
        SpawnMaxDistance = Math.Clamp(SpawnMaxDistance, SpawnMinDistance + 1, 128);
        SneakDetectionRangeMultiplier = (float)Finite(SneakDetectionRangeMultiplier, .40, .05, 1);
        SneakAcquireChance = (float)Finite(SneakAcquireChance, .25, 0, 1);
        SneakAlwaysDetectedDistance = (float)Finite(SneakAlwaysDetectedDistance, 4, 1, 16);
        TargetWhitelist = NormalizeTargetPatterns(TargetWhitelist);
        TargetBlacklist = NormalizeTargetPatterns(TargetBlacklist);
        DetectionRange = (float)Finite(DetectionRange, 32, 32, 128);
        HordeDetectionRange = (float)Finite(HordeDetectionRange, 80, 4, 128);
        MoveSpeed = (float)Finite(MoveSpeed, .018, .001, .08);
        RoamWalkSeconds = Finite(RoamWalkSeconds, 30, 1, 300);
        RoamPauseSeconds = Finite(RoamPauseSeconds, 10, 1, 120);
        RoamRouteRefreshSeconds = Finite(RoamRouteRefreshSeconds, 4, 1, 30);
        RoamSpeedMultiplier = (float)Finite(RoamSpeedMultiplier, .55, .05, 2);
        RoamShambleSideDriftBlocks = Finite(RoamShambleSideDriftBlocks, .65, 0, 2);
        RoamShambleSpeedVariation = Finite(RoamShambleSpeedVariation, .12, 0, .4);
        LostTargetFollowThroughSeconds = Finite(LostTargetFollowThroughSeconds, 5, 1, 10);
        LostTargetTurnSeconds = Finite(LostTargetTurnSeconds, 5, 1, 10);
        LostTargetTurnAngleDegrees = Finite(LostTargetTurnAngleDegrees, 25, 5, 75);
        LostTargetFollowThroughDistanceBlocks = Finite(LostTargetFollowThroughDistanceBlocks, 7, 2, 18);
        SiegeProgressSampleSeconds = Finite(SiegeProgressSampleSeconds, 2.5, .5, 15);
        SiegeStuckSeconds = Finite(SiegeStuckSeconds, 5, SiegeProgressSampleSeconds, 60);
        SiegeMinimumTravelBlocks = Finite(SiegeMinimumTravelBlocks, .4, .05, 5);
        SiegeNavigationRefreshSeconds = Finite(SiegeNavigationRefreshSeconds, 2, .5, 15);
        SiegeRepathDelaySeconds = Finite(SiegeRepathDelaySeconds, 1, .1, 10);
        BlockBreakIntervalSeconds = Finite(BlockBreakIntervalSeconds, 2.5, 0.5, 120);
        BlockBreakChance = (float)Finite(BlockBreakChance, 0.65, 0, 1);
        HordeBlockBreakChance = (float)Finite(HordeBlockBreakChance, 0.90, 0, 1);
        NonHordeBlockReinforcementDamage = Math.Clamp(NonHordeBlockReinforcementDamage, 1, 25);
        HordeBlockReinforcementDamage = Math.Clamp(HordeBlockReinforcementDamage, 1, 25);
        BlockBreakReach = (float)Finite(BlockBreakReach, 2.2, 1, 4);
        AttackDamage = (float)Finite(AttackDamage, 2, .01, 100);
        AttackCooldownSeconds = (float)Finite(AttackCooldownSeconds, 2.5, .25, 60);
        HordeDoorAttemptCooldownSeconds = (float)Finite(HordeDoorAttemptCooldownSeconds, 2, .25, 60);
        MoanMinSeconds = (float)Finite(MoanMinSeconds, 1, 1, 600);
        MoanMaxSeconds = (float)Finite(MoanMaxSeconds, 30, MoanMinSeconds, 900);
        ZombieMoanVolume = (float)Finite(ZombieMoanVolume, .9, 0, 2);
        ZombieHurtSoundVolume = (float)Finite(ZombieHurtSoundVolume, .85, 0, 2);
        ZombieHurtSoundCooldownSeconds = (float)Finite(ZombieHurtSoundCooldownSeconds, .65, 0, 30);
    }
    private static string[] NormalizeTargetPatterns(string[] values) =>
        (values ?? []).Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().ToLowerInvariant())
            .Distinct(StringComparer.OrdinalIgnoreCase).Take(256).ToArray();
    private static double Finite(double v, double fallback, double min, double max) => Math.Clamp(double.IsFinite(v) ? v : fallback, min, max);
}

public static class Rules
{
    // Target matching uses entity type IDs, NOT item IDs. A final * means
    // "starts with"; without * the pattern must match the complete code.
    // A pattern without a domain matches any domain's path (for convenience).
    public static bool EntityPatternMatches(string code, string pattern)
    {
        if (string.IsNullOrWhiteSpace(code) || string.IsNullOrWhiteSpace(pattern)) return false;
        code = code.Trim();
        pattern = pattern.Trim();
        bool prefix = pattern.EndsWith('*');
        if (prefix) pattern = pattern[..^1];
        if (pattern.Length == 0) return false;
        string candidate = pattern.Contains(':') ? code :
            (code.Contains(':') ? code[(code.IndexOf(':') + 1)..] : code);
        return prefix ? candidate.StartsWith(pattern, StringComparison.OrdinalIgnoreCase)
            : string.Equals(candidate, pattern, StringComparison.OrdinalIgnoreCase);
    }

    public static bool MatchesTargetList(string code, string[] patterns) =>
        patterns?.Any(pattern => EntityPatternMatches(code, pattern)) == true;

    public static bool TargetCodeAllowed(string code, bool allowedByDefault, Config config)
    {
        if (MatchesTargetList(code, config.TargetBlacklist)) return false;
        if (MatchesTargetList(code, config.TargetWhitelist)) return true;
        return allowedByDefault;
    }

    // Dawn belongs to the previous evening's night. Day 1 is TotalDays [0,1).
    // The horde's calendar trigger is the beginning of night number N (N=7, 14,...).
    // For a day-relative total, night N begins at (N - 1) + NightStartsAtHour/24.
    // The next *scheduled* start is strictly future, even if today's event has
    // already started or finished. These calculations don't use real-time seconds.
    public static double NextNaturalHordeStartDay(double outbreakDays, Config c)
    {
        int frequency = Math.Max(1, c.HordeEveryDays);
        long dayNumber = Math.Max(1, (long)Math.Floor(outbreakDays) + 1);
        long night = ((dayNumber + frequency - 1) / frequency) * frequency;
        double leadHour = Math.Max(0, c.HordeStartHour -
            Math.Clamp(c.HordeMinutesBeforeStartHour, 0, 120) / 60.0);
        double next = night - 1 + leadHour / 24.0;
        if (next <= outbreakDays + 1e-8) next += frequency;
        return next;
    }
    public static string FormatGameTimeUntil(double gameDays)
    {
        long minutes = Math.Max(0, (long)Math.Ceiling(gameDays * 1440 - 1e-7));
        return $"{minutes / 1440} days, {(minutes % 1440) / 60} hours, {minutes % 60} minutes";
    }

    public static bool IsNight(double days, Config c)
    {
        double hour = (days - Math.Floor(days)) * 24;
        return hour >= c.NightStartsAtHour || hour < c.NightEndsAtHour;
    }
    public static long NightNumber(double days, Config c)
    {
        long day = (long)Math.Floor(days) + 1;
        return (days - Math.Floor(days)) * 24 < c.NightEndsAtHour ? day - 1 : day;
    }
    public static bool IsHorde(double days, Config c) => IsNight(days, c) && NightNumber(days, c) > 0 && NightNumber(days, c) % Math.Max(1, c.HordeEveryDays) == 0;
    // This checks only routine automatic spawns: ambient creation, dormant
    // wake-ups and death-created Alphas. Admin test summons and explicit horde
    // waves can bypass timing, but never bypass the active entity cap.
    public static bool CanAutomaticallySpawn(double outbreakDays, double worldDays, Config c)
    {
        if (c.ZombieSpawnNightOnly && !IsNight(worldDays, c)) return false;
        if (c.ZombieSpawnHordeNightOnly && !IsHorde(outbreakDays, c)) return false;
        return true;
    }
    public static bool IsEyeBandage(string code, string[] fragments)
    {
        if (string.IsNullOrWhiteSpace(code)) return false;
        // Recognize all normal left/right/variant eyepatches regardless of
        // older serialized EyeBandageCodeFragments settings. A newly made
        // world does NOT reset the server's existing mod configuration.
        string[] builtIn = ["eyepatch", "eye-patch", "eyebandage", "eye-bandage", "eye-cover", "eyecover", "bandage-eye", "patch-eye"];
        return builtIn.Any(part => code.Contains(part, StringComparison.OrdinalIgnoreCase)) ||
            (fragments?.Any(part => !string.IsNullOrWhiteSpace(part) &&
                code.Contains(part, StringComparison.OrdinalIgnoreCase)) == true);
    }
    public static float PoisonDamage(Config c) => c.InfectionDamagePerTick;
    public static double PoisonIntervalSeconds(int dosesTaken, Config c) =>
        dosesTaken > 0 ? c.TreatedInfectionTickSeconds : c.InfectionTickSeconds;
    // Untreated deadline only (no doses taken).
    public static bool IsFatalInfection(double infectionDay, double currentDay, Config c) =>
        IsFatalInfection(infectionDay, currentDay, 0, 0, c);
    // While treated, the deadline extends so a course started inside the start
    // window can always be completed on schedule (with one interval of slack):
    // max(infection + InfectionFatalDays, firstDose + CureDosesRequired x interval).
    public static double FatalDeadlineDay(double infectionDay, int dosesTaken, double firstDoseDay, Config c)
    {
        double deadline = infectionDay + c.InfectionFatalDays;
        if (dosesTaken > 0)
            deadline = Math.Max(deadline, firstDoseDay + Math.Max(1, c.CureDosesRequired) * c.CureDoseIntervalDays);
        return deadline;
    }
    public static bool IsFatalInfection(double infectionDay, double currentDay, int dosesTaken, double firstDoseDay, Config c) =>
        c.InfectionFatalEnabled && currentDay >= FatalDeadlineDay(infectionDay, dosesTaken, firstDoseDay, c);
    // The first dose must be taken within CureStartWindowDays of infection.
    // With fatality disabled there is no deadline, so the window does not apply
    // (otherwise a late player would stay infected and poisoned forever).
    public static bool CanStartCure(double infectionDay, double currentDay, Config c) =>
        !c.InfectionFatalEnabled || currentDay - infectionDay <= c.CureStartWindowDays + 1e-7;
    public static bool MayTakeCureDose(int doses, double previousDay, double currentDay, Config c) =>
        doses <= 0 || currentDay - previousDay + 1e-7 >= c.CureDoseIntervalDays;
    // Is a sneaking player eligible to be noticed, given relative distance and a single RNG roll?
    public static bool CanAcquireSneakingPlayer(double distanceSquared, float normalRange, double roll, Config c)
    {
        if (distanceSquared <= c.SneakAlwaysDetectedDistance * c.SneakAlwaysDetectedDistance) return true;
        double effectiveRange = normalRange * c.SneakDetectionRangeMultiplier;
        return distanceSquared <= effectiveRange * effectiveRange && roll < c.SneakAcquireChance;
    }
    public static bool IsEyeCovering(int id, string code, Config c) =>
        c.QuietEyeBandageItemIds.Contains(id) || IsEyeBandage(code, c.EyeBandageCodeFragments);
    public static bool IsSpawnLightSafe(int light, Config c) => light <= c.SpawnMaxLightLevel;
    public static bool RecordHit(Queue<double> hits, double now, int requiredHits, double windowSeconds)
    {
        while (hits.Count > 0 && now - hits.Peek() > windowSeconds) hits.Dequeue();
        hits.Enqueue(now);
        return hits.Count >= requiredHits;
    }
    public static int GetInfectionHitThreshold(bool alpha, Config c) =>
        alpha ? c.AlphaHitsToInfect : c.NormalHitsToInfect;
}
