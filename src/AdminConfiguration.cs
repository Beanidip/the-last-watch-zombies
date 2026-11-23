using System.Globalization;
using System.Reflection;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.Server;

namespace SeventhNightZombies;

// Persist edits without mutating live AI, profiler or population state halfway
// through a tick. Multiple edits read the latest saved values before writing.
public static class AdminConfiguration
{
    public static readonly Dictionary<string, string> Keys = new(StringComparer.OrdinalIgnoreCase)
    {
        ["grabberchance"] = nameof(Config.GrabberSpawnChance),
        ["hivechance"] = nameof(Config.HiveSpawnChance),
        ["screamerchance"] = nameof(Config.ScreamerSpawnChance),
        ["bloaterchance"] = nameof(Config.BloaterSpawnChance),
        ["leapcooldown"] = nameof(Config.GrabberLeapCooldownSeconds),
        ["beecooldown"] = nameof(Config.HiveBeeCooldownSeconds),
        ["diagnostics"] = nameof(Config.PerformanceDiagnostics),
        ["ambientlimit"] = nameof(Config.ZombiePopDailyAddLimit),
        ["dormantpopulationcap"] = nameof(Config.DormantPopulationCap),
        ["despawndelay"] = nameof(Config.ZombieIdleDespawnSeconds),
        ["npcinterest"] = nameof(Config.NpcTraderInterestSeconds),
        ["nightonly"] = nameof(Config.AmbientSpawnRequiresNight),
        ["strictnightonly"] = nameof(Config.ZombieSpawnNightOnly),
        ["hordeonly"] = nameof(Config.ZombieSpawnHordeNightOnly),
        ["activecap"] = nameof(Config.MaxActiveZombies),
        ["blockbreaking"] = nameof(Config.BlockBreakingEnabled),
        ["blockbreakinterval"] = nameof(Config.BlockBreakIntervalSeconds),
        ["blockbreakchance"] = nameof(Config.BlockBreakChance),
        ["nonhordereinforcementdamage"] = nameof(Config.NonHordeBlockReinforcementDamage),
        ["hordereinforcementdamage"] = nameof(Config.HordeBlockReinforcementDamage),
        ["protectreinforced"] = nameof(Config.ProtectReinforcedBlocks),
        ["spawnchance"] = nameof(Config.AmbientSpawnChancePerSecond),
        ["spawnlight"] = nameof(Config.SpawnMaxLightLevel),
        ["spawnmindistance"] = nameof(Config.SpawnMinDistance),
        ["spawnmaxdistance"] = nameof(Config.SpawnMaxDistance),
        ["hordedays"] = nameof(Config.HordeEveryDays),
        ["hordeduration"] = nameof(Config.HordeDurationSeconds),
        ["hordewaveinterval"] = nameof(Config.HordeWaveIntervalSeconds),
        ["hordewavemin"] = nameof(Config.HordeWaveMinZombiesPerPlayer),
        ["hordewavemax"] = nameof(Config.HordeWaveMaxZombiesPerPlayer),
        ["routebudget"] = nameof(Config.AiRouteRequestsPerSecond),
        ["scanbudget"] = nameof(Config.AiPreyScansPerSecond),
        ["scaninterval"] = nameof(Config.AiPreyScanIntervalSeconds),
        ["lastseen"] = nameof(Config.ZombieLostTargetPursuitSeconds),
        ["keepaliverange"] = nameof(Config.ZombieKeepActiveRadiusBlocks),
        ["stayactive"] = nameof(Config.ZombieStayActiveNearPlayers),
        ["wakebatch"] = nameof(Config.DormantWakeBatchSize),
        ["wakeinterval"] = nameof(Config.DormantWakeIntervalSeconds),
        ["wakeglobalbatch"] = nameof(Config.DormantMaxTotalWakesPerPass),
        ["dormantreactivation"] = nameof(Config.DormantReactivationPerZonePerDay),
        ["migrationhour"] = nameof(Config.ZoneMigrationHour),
        ["migrationchance"] = nameof(Config.ZoneBaseDailyMigrationChance),
        ["migrationmaxchance"] = nameof(Config.ZoneMaxDailyMigrationChance),
        ["strongholdbonus"] = nameof(Config.ZoneStrongholdBonus),
        ["playerbonus"] = nameof(Config.ZonePlayerPresenceBonus),
        ["bonfirebonus"] = nameof(Config.ZoneBonfireAttractionBonus),
        ["migrationannounce"] = nameof(Config.ZoneMigrationChatAnnouncement),
        ["fatality"] = nameof(Config.InfectionFatalEnabled),
        ["fataldays"] = nameof(Config.InfectionFatalDays)
    };

    public static void Register(ICoreServerAPI api, SeventhNightSystem core)
    {
        api.ChatCommands.Create("zombieconfig")
            .WithDescription("Admin: list settings, inspect a setting, or save a value for the next server restart")
            .RequiresPrivilege("controlserver")
            .WithArgs(api.ChatCommands.Parsers.OptionalAll("setting and optional value"))
            .HandleWith(args => Execute(api, core, (args[0] as string) ?? ""));
        api.ChatCommands.Create("zombiediagnostics")
            .WithDescription("Admin: save diagnostics on/off for the next server restart")
            .RequiresPrivilege("controlserver")
            .WithArgs(api.ChatCommands.Parsers.OptionalAll("on or off"))
            .HandleWith(args => Execute(api, core, "diagnostics " + ((args[0] as string) ?? "").Trim()));
    }

    public static TextCommandResult Execute(ICoreServerAPI api, SeventhNightSystem core, string input)
    {
        var words = input.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (words.Length == 0 || words[0].Equals("list", StringComparison.OrdinalIgnoreCase) || words[0] == "help")
            return TextCommandResult.Success("/zombieconfig <setting> [value] — all changes require restart. Settings: " + string.Join(", ", Keys.Keys));
        if (words.Length > 2 || !Keys.TryGetValue(words[0], out var name))
            return TextCommandResult.Error("Use /zombieconfig list or /zombieconfig <setting> [value].");

        string filename = StandaloneConfigStorage.FileFor(name);
        try
        {
            var saved = api.LoadModConfig<JObject>(filename) ?? new JObject();
            var pending = JsonConvert.DeserializeObject<Config>(JsonConvert.SerializeObject(core.Settings));
            // Unified configuration: all fields come from the same two files.
            foreach (var file in new[] { StandaloneConfigStorage.AdvancedFile,
                StandaloneConfigStorage.MainFile })
            {
                var overlay = api.LoadModConfig<JObject>(file);
                if (overlay != null) JsonConvert.PopulateObject(overlay.ToString(), pending);
            }
            var field = typeof(Config).GetField(name)!;
            var propertyName = field.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName ?? name;
            if (words.Length == 1)
                return TextCommandResult.Success($"{words[0]} ({name}): running={field.GetValue(core.Settings)}, saved={field.GetValue(pending)}. Changes apply after restart.");
            if (!TrySet(pending, name, words[1], out var error)) return TextCommandResult.Error(error);
            // Preserve unrelated keys, including upgrade markers. Serialize the
            // validated config so dependent min/max pairs are saved together.
            var validated = JObject.FromObject(pending);
            foreach (var p in validated.Properties())
            {
                if (StandaloneConfigStorage.IsRetired(p.Name)) continue;
                if (StandaloneConfigStorage.FileFor(ResolveField(p.Name)) != filename) continue;
                // A pre-existing JSON key may use a different capitalization.
                // Replace case-insensitively to avoid duplicate conflicting keys.
                foreach (var previous in saved.Properties().Where(old =>
                    old.Name.Equals(p.Name, StringComparison.OrdinalIgnoreCase)).ToArray())
                    previous.Remove();
                saved[p.Name] = p.Value.DeepClone();
            }
            foreach (var old in saved.Properties().Where(p => StandaloneConfigStorage.IsRetired(p.Name)).ToArray()) old.Remove();
            api.StoreModConfig(saved, filename);
            return TextCommandResult.Success($"Saved {words[0]}={field.GetValue(pending)} in {filename}. Restart the server to apply. Running value: {field.GetValue(core.Settings)}.");
        }
        catch (Exception e)
        {
            api.Logger.Error("Zombie config command failed: {0}", e);
            return TextCommandResult.Error("Could not save configuration; see the server log. Running settings were unchanged.");
        }
    }
    static string ResolveField(string jsonName) => typeof(Config).GetFields().FirstOrDefault(f =>
        (f.GetCustomAttribute<JsonPropertyAttribute>()?.PropertyName ?? f.Name) == jsonName)?.Name ?? jsonName;

    public static bool TrySet(Config pending, string name, string raw, out string error)
    {
        error = "Use a finite number, or true/false (on/off) for toggles.";
        var field = typeof(Config).GetField(name);
        if (field == null || !Keys.Values.Contains(name)) { error = "Unknown setting."; return false; }
        object value;
        if (field.FieldType == typeof(bool))
        {
            if (raw.Equals("on", StringComparison.OrdinalIgnoreCase)) value = true;
            else if (raw.Equals("off", StringComparison.OrdinalIgnoreCase)) value = false;
            else if (bool.TryParse(raw, out bool b)) value = b;
            else return false;
        }
        else
        {
            if (!double.TryParse(raw, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) || !double.IsFinite(number)) return false;
            try { value = Convert.ChangeType(raw, field.FieldType, CultureInfo.InvariantCulture); }
            catch { return false; }
        }
        var before = field.GetValue(pending);
        field.SetValue(pending, value);
        pending.Validate();
        if (!Equals(field.GetValue(pending), value))
        {
            error = $"Value is outside the allowed range or conflicts with another setting. Nearest valid value: {field.GetValue(pending)}. Nothing saved.";
            field.SetValue(pending, before);
            return false;
        }
        return true;
    }
}
