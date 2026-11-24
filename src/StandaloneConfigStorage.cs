using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Server;
using Vintagestory.API.Client;

namespace SeventhNightZombies;

/// <summary>
/// Unified The Last Watch Zombies: one gameplay config and one advanced config.
/// The old four split-mod JSON files are intentionally not loaded.
/// </summary>
public static class StandaloneConfigStorage
{
    public const string MainFile = "thelastwatchzombiesconfigmain.json";
    public const string AdvancedFile = "thelastwatchzombiesconfigadvanced.json";


    // Accept legacy JSON on read, but do not advertise inactive settings on write.
    public static readonly HashSet<string> RetiredKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(Config.LastWatchEnabled),
        nameof(Config.MaxNaturalZombiesPerPlayerPerNight),
        nameof(Config.AlphaFollowPlayerBreakChance),
        nameof(Config.NpcPlayerRetargetChance),
        nameof(Config.NpcPlayerRetargetIntervalSeconds),
        nameof(Config.DormantWakeRangeMultiplier),
        nameof(Config.RoamShambleSideDriftBlocks),
        nameof(Config.RoamShambleSpeedVariation),
        nameof(Config.LostTargetFollowThroughSeconds),
        nameof(Config.LostTargetTurnSeconds),
        nameof(Config.LostTargetTurnAngleDegrees),
        nameof(Config.LostTargetFollowThroughDistanceBlocks),
        nameof(Config.SiegeRepathDelaySeconds),
        nameof(Config.ManualHordeMinDistance),
        nameof(Config.ManualHordeMaxDistance),
        nameof(Config.HordeInitialFallbackMaxDistance),
        nameof(Config.HordeInitialRecoveryMaxDistance),
        nameof(Config.ZoneChangesBonusPerBlock),
        nameof(Config.ZoneHotspotBonus),
        nameof(Config.ZoneHotspotBlocksChanged),
        nameof(Config.ZoneAttractionMinBlocksChanged),
        // Removed fields: a saved value is ignored (or migrated, see Load).
        LegacyCorpseSmallHidesKey,
    };
    // Corpses dropped small raw hides until 2.0.10; rusty gears replaced them.
    public const string LegacyCorpseSmallHidesKey = "CorpseSmallHides";
    public static bool IsRetired(string name) => RetiredKeys.Contains(name);

    // Unified setting ownership for the user-facing main/advanced JSON files.
    public static readonly string[] LastWatchMainKeyOrder =
    {
        nameof(Config.ZombiePopDailyAddLimit),
        nameof(Config.DormantPopulationCap),
        nameof(Config.DormantReactivationPerZonePerDay),
        nameof(Config.DormantWakeBatchSize),
        nameof(Config.DormantWakeIntervalSeconds),
        nameof(Config.ZoneMigrationHour),
        nameof(Config.HordeMigrationMaxZoneSteps),
        nameof(Config.ZoneBaseDailyMigrationChance),
        nameof(Config.ZoneMaxDailyMigrationChance),
        nameof(Config.ZonePlayerPresenceBonus),
        nameof(Config.ZoneSettlementBonus),
        nameof(Config.ZoneStrongholdBonus),
        nameof(Config.ZoneBonfireAttractionBonus),
        nameof(Config.ZoneStonePathBonusPerBlock),
        nameof(Config.ZoneDeathBonusPerDeath),
        nameof(Config.ZoneTraderDormantSeed),
        nameof(Config.ZoneMigrationChatAnnouncement),
        nameof(Config.ZoneBellRangeZones),
        nameof(Config.ZoneBellOverrunThreshold),
        nameof(Config.ZoneFarmAnimalThreshold),
        nameof(Config.ZoneFarmAnimalAttractionBonus),
    };
    public static readonly HashSet<string> LastWatchMainKeys =
        new(LastWatchMainKeyOrder, StringComparer.OrdinalIgnoreCase);

    public static readonly HashSet<string> MainKeys = new(StringComparer.OrdinalIgnoreCase)
    {
        nameof(Config.GrabberSpawnChance), nameof(Config.HiveSpawnChance),
        nameof(Config.ScreamerSpawnChance), nameof(Config.BloaterSpawnChance),
        nameof(Config.GrabberLeapCooldownSeconds), nameof(Config.HiveBeeCooldownSeconds),
        nameof(Config.BloaterBurstRadius), nameof(Config.BloaterBurstDamage),
        nameof(Config.BloaterRotDrop), nameof(Config.BloaterPoisonSeconds),
        nameof(Config.BloaterPoisonDamagePerSecond),
        nameof(Config.MaxActiveZombies),
        nameof(Config.ZombieSpawnNightOnly),
        nameof(Config.ZombieSpawnHordeNightOnly),
        nameof(Config.AmbientSpawnRequiresNight),
        nameof(Config.NightStartsAtHour),
        nameof(Config.NightEndsAtHour),
        nameof(Config.AmbientSpawnChancePerSecond),
        nameof(Config.SpawnMaxLightLevel),
        nameof(Config.SpawnMinDistance),
        nameof(Config.SpawnMaxDistance),
        nameof(Config.SpawnAlphaOnPlayerDeath),
        nameof(Config.ZombieIdleDespawnSeconds),
        nameof(Config.NpcTraderInterestSeconds),
        nameof(Config.ZombieStayActiveNearPlayers),
        nameof(Config.ZombieKeepActiveRadiusBlocks),
        nameof(Config.ZombiePresenceCheckSeconds),
        nameof(Config.ZombieLostTargetPursuitSeconds),
        nameof(Config.HordeEveryDays),
        nameof(Config.HordeStartHour),
        nameof(Config.HordeMinutesBeforeStartHour),
        nameof(Config.HordeDurationSeconds),
        nameof(Config.HordeWaveIntervalSeconds),
        nameof(Config.HordeInitialMinZombiesPerPlayer),
        nameof(Config.HordeWaveMinZombiesPerPlayer),
        nameof(Config.HordeWaveMaxZombiesPerPlayer),
        nameof(Config.HordeSpawnIgnoreLightLevel),
        nameof(Config.HordeSpawnsInClaims),
        nameof(Config.HordeInitialRetrySeconds),
        nameof(Config.HordeInitialRetryWindowSeconds),
        nameof(Config.BlockBreakingEnabled),
        nameof(Config.BlockBreakIntervalSeconds),
        nameof(Config.BlockBreakChance),
        nameof(Config.HordeBlockBreakChance),
        nameof(Config.NonHordeBlockReinforcementDamage),
        nameof(Config.HordeBlockReinforcementDamage),
        nameof(Config.ProtectClaims),
        nameof(Config.ProtectReinforcedBlocks),
        nameof(Config.HordeOpensDoors),
        nameof(Config.AttackDamage),
        nameof(Config.AttackCooldownSeconds),
        nameof(Config.MoveSpeed),
        nameof(Config.AlphaSpeedMultiplier),
        nameof(Config.AlphaAttackDamageMultiplier),
        nameof(Config.DetectionRange),
        nameof(Config.HordeDetectionRange),
        nameof(Config.AlphaDetectionRangeBonus),
        nameof(Config.NpcPlayerRetargetChance),
        nameof(Config.NpcPlayerRetargetIntervalSeconds),
        nameof(Config.TargetWhitelist),
        nameof(Config.TargetBlacklist),
        nameof(Config.IgnoreVanillaAnimals),
        nameof(Config.CorpseDelaySeconds),
        nameof(Config.CorpseRot),
        nameof(Config.CorpseBones),
        nameof(Config.CorpseRustyGears),
        nameof(Config.AlphaFuneralMaskChanceDenominator),
        nameof(Config.AlphaFuneralMaskItemCodes),
        nameof(Config.FuneralMask1Durability),
        nameof(Config.OtherFuneralMaskDurability),
        nameof(Config.NormalHitsToInfect),
        nameof(Config.AlphaHitsToInfect),
        nameof(Config.HitWindowSeconds),
        nameof(Config.InfectionTickSeconds),
        nameof(Config.InfectionDamagePerTick),
        nameof(Config.InfectionFatalEnabled),
        nameof(Config.InfectionFatalDays),
        nameof(Config.CureDosesRequired),
        nameof(Config.CureDoseIntervalDays),
        nameof(Config.CureStartWindowDays),
        nameof(Config.TreatedInfectionTickSeconds),
        nameof(Config.CurePsychedelicEnabled),
    };

    public static bool MainOwns(string key) => MainKeys.Contains(key) || LastWatchMainKeys.Contains(key) ||
        key.Equals("zombiepopdailyaddlimit", StringComparison.OrdinalIgnoreCase);
    public static string FileFor(string fieldName) => MainOwns(fieldName) ? MainFile : AdvancedFile;

    private static void Overlay(Config target, JObject json)
    {
        if (json != null) JsonConvert.PopulateObject(json.ToString(), target);
    }

    // Starting with v1.6.18, admins delete old config files before updating.
    // Never import settings from the obsolete monolithic config.
    public static Config Load(ICoreServerAPI api)
    {
        var settings = new Config();
        var advanced = api.LoadModConfig<JObject>(AdvancedFile);
        var main = api.LoadModConfig<JObject>(MainFile);
        Overlay(settings, advanced);
        Overlay(settings, main);
        MigrateCorpseDrops(settings, advanced, main);
        return settings;
    }

    // One-time carry-over: an admin-chosen small-hide count becomes the rusty
    // gear count, unless the new key is already present. The old key is then
    // dropped by Save because it is retired.
    public static void MigrateCorpseDrops(Config settings, params JObject[] files)
    {
        if (files.Any(f => f?.Properties().Any(p => p.Name.Equals(nameof(Config.CorpseRustyGears),
                StringComparison.OrdinalIgnoreCase)) == true)) return;
        foreach (var file in files.Reverse())
        {
            var legacy = file?.Properties().FirstOrDefault(p =>
                p.Name.Equals(LegacyCorpseSmallHidesKey, StringComparison.OrdinalIgnoreCase));
            if (legacy == null || legacy.Value.Type != JTokenType.Integer) continue;
            settings.CorpseRustyGears = (int)legacy.Value;
            return;
        }
    }

    public static Config LoadClient(ICoreClientAPI api)
    {
        var settings = new Config();
        Overlay(settings, api.LoadModConfig<JObject>(AdvancedFile));
        Overlay(settings, api.LoadModConfig<JObject>(MainFile));
        return settings;
    }

    public static void Save(ICoreServerAPI api, Config settings)
    {
        var main = new JObject();
        var advanced = new JObject();
        foreach (var prop in JObject.FromObject(settings).Properties())
        {
            if (IsRetired(prop.Name)) continue;
            (MainOwns(prop.Name) ? main : advanced)[prop.Name] = prop.Value.DeepClone();
        }
        api.StoreModConfig(main, MainFile);
        api.StoreModConfig(advanced, AdvancedFile);
    }
}
