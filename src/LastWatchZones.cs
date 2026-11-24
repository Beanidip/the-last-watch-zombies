using System.Text;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeventhNightZombies;

public sealed class WatchBuilding
{
    public int X, Y, Z, Dimension;
    // Stronghold type must survive chunk unload/restart so iron can still
    // reinforce new construction even with its block chunk unloaded.
    public int StrongholdStrength;
    // Bonfire markers additionally store a calendar-expiring burn snapshot, so
    // unloaded chunks still influence the daily server-wide migration roll.
    public double BonfireBurnUntilHours;
    public int BonfireFuelStacks;
    public BlockPos Position => new(X, Y, Z, Dimension);
}
// One daily entry per upcoming migration-cycle day. Recorded at ignition,
// not at the migration roll, so completed/broken pits keep their day's lure.
public sealed class BonfireDailyRecord
{
    public long Day;
    public int PeakFuelStacks;
}
public sealed class WatchZoneState
{
    public int Dimension, X, Z;
    // New ambient zombies only, NOT migrations, horde spawns or dormant wakeups.
    // Persisted with Last Watch zone saves and reset with the sunset cycle.
    public int AmbientPopulationAddedToday;
    // Separate from new ambient additions. Saved and reset at each sunset roll.
    // All Survival players in a zone share the same daily wake allowance.
    public int DormantReactivatedToday;
    // Optional per-player contribution statistics ONLY. The cap is zone-wide,
    // based on AmbientPopulationAddedToday, not on this dictionary.
    public Dictionary<string, int> AmbientPopulationAddedByPlayer = new();
    public int PlayerDeaths, StonePaths;
    public bool PermanentSettlement;
    // Once-per-zone ecology seeds; never repeat after unloading or restarting.
    public bool TraderPopulationSeeded;
    public List<WatchBuilding> Bells = new();
    public List<WatchBuilding> Strongholds = new();
    public List<WatchBuilding> Bonfires = new();
    // At most the current and preceding day are retained through normal play.
    public List<BonfireDailyRecord> BonfireDailyRecords = new();
    // Newtonsoft omits empty collections from disk; old saves without these
    // fields load through the initializers above. Persistent building records
    // and trader-once flags are never discarded.
    public bool ShouldSerializeAmbientPopulationAddedByPlayer() => AmbientPopulationAddedByPlayer.Count > 0;
    public bool ShouldSerializeBells() => Bells.Count > 0;
    public bool ShouldSerializeStrongholds() => Strongholds.Count > 0;
    public bool ShouldSerializeBonfires() => Bonfires.Count > 0;
    public bool ShouldSerializeBonfireDailyRecords() => BonfireDailyRecords.Count > 0;
    [JsonIgnore] public WatchZone Key => new(Dimension, X, Z);
}
public sealed class LastWatchSave
{
    public int Version = 8;
    public long LastDailyRoll = -1;
    public List<WatchZoneState> Zones = new();
    // Recorded generated/loaded map columns, compressed to unique 3x3-chunk zones.
    public List<WatchZone> ExploredZones = new();
}

public sealed class LastWatchZones : ModSystem, IZombieWorldDirector
{
    const string SaveKey = "seventhnightzombies-lastwatch-zones-v1";
    public const string BellCode = "seventhnightzombies:watchtowerbell";
    // Existing strongholds are the copper (50-strength) tier for save compatibility.
    public const string StrongholdCode = "seventhnightzombies:stronghold";
    public const string IronStrongholdCode = "seventhnightzombies:strongholdiron";
    public const string WoodStrongholdCode = "seventhnightzombies:strongholdwood";
    public const int WoodStrongholdStrength = 5;
    public const int CopperStrongholdStrength = 50;
    public const int IronStrongholdStrength = 300;
    public static int StrongholdStrength(Block b) => IsCode(b, WoodStrongholdCode) ? WoodStrongholdStrength :
        IsCode(b, IronStrongholdCode) ? IronStrongholdStrength : IsCode(b, StrongholdCode) ? CopperStrongholdStrength : 0;
    static bool IsStronghold(Block b) => StrongholdStrength(b) > 0;
    public const string BonfireCode = "seventhnightzombies:pitbonfire";
    public const string BonfireLitCode = "seventhnightzombies:pitbonfirelit";
    public const string BonfireLoadedCode = "seventhnightzombies:pitbonfireloaded";
    readonly Dictionary<WatchZone, WatchZoneState> zones = new();
    // Loaded/generated chunk-column notifications may arrive off the game thread.
    // Only coordinates are recorded. No chunk access or population edits off-thread.
    readonly object exploredLock = new();
    readonly HashSet<WatchZone> exploredZones = new();
    bool startupEcologyChecked;
    ICoreServerAPI sapi;
    SeventhNightSystem core;
    ZombiePopulation Population => (ZombiePopulation)core.Population;
    public override double ExecuteOrder() => 0.3;
    public override void Start(ICoreAPI api)
    {
        api.RegisterBlockClass("SeventhNightPitBonfire", typeof(BlockPitBonfire));
        api.RegisterBlockEntityClass("SeventhNightPitBonfireBE", typeof(BlockEntityPitBonfire));
    }
    ModSystemBlockReinforcement reinforce;
    long lastRoll = -1;
    long ticker;
    bool zonesLoaded;
    // Maps ignition time to the sunset migration cycle it will influence.
    long UpcomingRollDay(double totalDays)
    {
        long day = (long)Math.Floor(totalDays);
        double hour = (totalDays - day) * 24.0;
        return hour >= core.Settings.ZoneMigrationHour ? day + 1 : day;
    }
    long StartupRollDay(double totalDays)
    {
        long day = (long)Math.Floor(totalDays);
        double hour = (totalDays - day) * 24.0;
        return hour >= core.Settings.ZoneMigrationHour ? day : day - 1;
    }
    public override void StartServerSide(ICoreServerAPI api)
    {
        sapi = api;
        core = api.ModLoader.GetModSystem<SeventhNightSystem>();
        // The base system has already installed this zone controller and the
        // persistent dormant pool; never create a competing population owner.
        if (!ReferenceEquals(core.WorldDirector, this) || core.Population is not ZombiePopulation)
            throw new InvalidOperationException("The Last Watch Zombies: unified population was not initialized.");
        reinforce = api.ModLoader.GetModSystem<ModSystemBlockReinforcement>();
        api.Event.SaveGameLoaded += Load;
        api.Event.GameWorldSave += Save;
        api.Event.DidPlaceBlock += Placed;
        api.Event.DidBreakBlock += Broken;
        api.Event.OnEntitySpawn += OnPotentialTrader;
        api.Event.OnEntityLoaded += OnPotentialTrader;
        api.Event.PlayerDeath += Died;
        api.Event.BreakBlock += PlayerBreak;
        api.Event.ChunkColumnLoaded += RememberLoadedColumn;
        ticker = api.Event.RegisterGameTickListener(Tick, 5000);
        api.ChatCommands.Create("zombiecycle").WithDescription("Admin: run migration now without resetting daily spawn allowances")
            .RequiresPrivilege("controlserver").HandleWith(_ =>
            {
                if (!zonesLoaded || !core.Settings.LastWatchEnabled) return TextCommandResult.Error("Last Watch is disabled or has not finished loading.");
                RunMigration(UpcomingRollDay(sapi.World.Calendar.TotalDays), false);
                Population.Save();
                Save();
                return TextCommandResult.Success("Migration cycle complete. Sunset schedule and daily allowances preserved; see /lastwatch and /zombieperf.");
            });
        api.ChatCommands.Create("zombieween")
            .WithDescription("Admin Halloween event: add five dormant normal zombies to every recorded generated zone and grant two Mycobiotic Cure doses per online player")
            .RequiresPrivilege("controlserver")
            .HandleWith(_ =>
            {
                if (!zonesLoaded || !core.Settings.LastWatchEnabled)
                    return TextCommandResult.Error("Last Watch zones are disabled or have not finished loading.");

                // Backfill saved settlement/activity zones, historical dormant zones,
                // and currently occupied zones. Recorded chunk loads cover every
                // other generated zone encountered since this version was installed.
                var targets = new HashSet<WatchZone>(zones.Keys);
                foreach (var entry in Population.MigrationSnapshot())
                    targets.Add(WatchZone.FromChunk(entry.X, entry.Z, entry.Dimension));
                lock (exploredLock) targets.UnionWith(exploredZones);
                foreach (var player in sapi.World.AllOnlinePlayers)
                {
                    if (player.Entity == null) continue;
                    var pos = player.Entity.Pos;
                    targets.Add(WatchZone.FromBlock((int)Math.Floor(pos.X),
                        (int)Math.Floor(pos.Z), pos.Dimension));
                }
                if (targets.Count == 0)
                    return TextCommandResult.Error("No visited/generated zones have been recorded yet.");

                // Explicit admin events bypass the natural dormant *storage* cap.
                // Nothing is spawned or activated here, and the normal wake limits
                // and existing cap on ACTIVE zombies still apply when players return.
                long added = 0;
                foreach (var zone in targets)
                    added += Population.AddEventDormant(zone, 5, sapi.World.SeaLevel);
                Population.Save();
                lock (exploredLock) exploredZones.UnionWith(targets);
                Save();

                var cure = sapi.World.GetItem(new AssetLocation("seventhnightzombies:mycobioticcure"));
                int recipients = 0, groundDrops = 0;
                if (cure != null)
                    foreach (var player in sapi.World.AllOnlinePlayers.OfType<IServerPlayer>())
                    {
                        if (player.Entity == null || player.InventoryManager == null) continue;
                        var stack = new ItemStack(cure, 2);
                        if (!player.InventoryManager.TryGiveItemstack(stack))
                        {
                            // Never silently discard a reward when all player slots are full.
                            if (stack.StackSize > 0)
                            {
                                sapi.World.SpawnItemEntity(stack, player.Entity.Pos.XYZ.AddCopy(0, 0.5, 0));
                                groundDrops++;
                            }
                        }
                        recipients++;
                    }
                else sapi.Logger.Error("The Last Watch Zombies: /zombieween could not resolve the Mycobiotic Cure item.");

                sapi.Logger.Notification($"The Last Watch Zombies: /zombieween added {added} dormant normals across {targets.Count} zones; {recipients} cure recipients.");
                return TextCommandResult.Success(
                    $"Zombieween! Added {added} dormant normal zombies across {targets.Count} recorded zones " +
                    $"(5 per zone; global dormant now {Population.DormantCount}). " +
                    (cure == null ? "Cure item missing; no cures distributed." :
                        $"Granted two Mycobiotic Cure doses to {recipients} online players" +
                        (groundDrops > 0 ? $"; {groundDrops} full inventories caused ground drops." : ".")) +
                    " They return through normal zone reactivation, not an immediate horde.");
            });
        api.ChatCommands.Create("snseed").WithDescription("Admin: add ten dormant normal zombies to your current Last Watch zone")
            .RequiresPrivilege("controlserver").HandleWith(args =>
            {
                var player = args.Caller.Player;
                if (player?.Entity == null || !SeventhNightSystem.IsTarget(player.Entity))
                    return TextCommandResult.Error("Enter a world as a survival player to seed the dormant population.");
                long before = Population.DormantCount;
                for (int i = 0; i < 10; i++)
                    Population.AddPending(SeventhNightSystem.LocalPosition(player.Entity.Pos), player.Entity.Pos.Dimension, alpha: false);
                Population.Save();
                return TextCommandResult.Success($"Added {Population.DormantCount - before} dormant normals to this zone (global cap {core.Settings.DormantPopulationCap}). They await normal ambient reactivation; see /lastwatch.");
            });
        api.ChatCommands.Create("snseedneighbor").WithDescription("Admin: add ten dormant normals to the zone immediately east of yours")
            .RequiresPrivilege("controlserver").HandleWith(args =>
            {
                var player = args.Caller.Player;
                if (player?.Entity == null || !SeventhNightSystem.IsTarget(player.Entity))
                    return TextCommandResult.Error("Enter a world as a survival player to seed a neighboring zone.");
                // Moving exactly one zone width in +X preserves the offset
                // inside its 3x3 chunk zone, even at negative coordinates.
                var east = SeventhNightSystem.LocalPosition(player.Entity.Pos).Add(WatchZone.ChunksPerSide * GlobalConstants.ChunkSize, 0, 0);
                long before = Population.DormantCount;
                for (int i = 0; i < 10; i++)
                    Population.AddPending(east, player.Entity.Pos.Dimension, alpha: false);
                Population.Save();
                return TextCommandResult.Success($"Added {Population.DormantCount - before} dormant normal zombies one zone east (global cap {core.Settings.DormantPopulationCap}). Nothing spawned; check the admin map.");
            });
        api.ChatCommands.Create("zombiemap")
            .WithDescription("Admin: toggle the zombie zone overlay; close and reopen M to refresh it")
            .RequiresPrivilege("controlserver")
            .HandleWith(args =>
            {
                if (args.Caller.Player == null) return TextCommandResult.Error("Only players can toggle the map.");
                bool enabled = LastWatchMapLayer.ToggleFor(args.Caller.Player.PlayerUID);
                return TextCommandResult.Success(enabled
                    ? "Zombie map ON. Close and reopen the vanilla map (M) for one snapshot."
                    : "Zombie map OFF. Close and reopen the vanilla map to hide it.");
            });
        api.ChatCommands.Create("lastwatch")
            .WithDescription("Admin: last-watch zone population and migration status at your position")
            .RequiresPrivilege("controlserver")
            .HandleWith(args =>
            {
                var player = args.Caller.Player;
                if (player?.Entity == null) return TextCommandResult.Error("Only a player can inspect a zone.");
                var p = player.Entity.Pos;
                var key = WatchZone.FromBlock((int)Math.Floor(p.X), (int)Math.Floor(p.Z), p.Dimension);
                var data = SnapshotAt(key);
                string rollClock = TimeSpan.FromHours(core.Settings.ZoneMigrationHour).ToString(@"hh\:mm");
                return TextCommandResult.Success($"zone {key.X},{key.Z} dimension {key.Dimension}: " +
                    $"active loaded zombies in this zone {data.ActiveZombies}, dormant this zone {data.Dormant}, global dormant {Population.DormantCount} (storage cap {core.Settings.DormantPopulationCap}), new ambient spawns this zone {data.AmbientAdded}/{core.Settings.ZombiePopDailyAddLimit} this cycle (shared), dormant reactivated today {ReactivatedToday(key)}/{(core.Settings.DormantReactivationPerZonePerDay == 0 ? "unlimited" : core.Settings.DormantReactivationPerZonePerDay.ToString())}, your contribution {AmbientAddedForPlayer(key, player.PlayerUID)}, spawn status: {core.ZoneSpawnStatus(key)}, settlement {data.Settlement}, " +
                    $"paths {data.Paths}, deaths {data.Deaths}, strongholds {data.Strongholds}, " +
                    $"bells {data.Bells}, burning bonfires {data.BurningBonfires}, live bonfire {data.BonfireStacks} stacks, " +
                    $"sunset-cycle bonfire peak {data.BonfireDayStacks} stacks, next-roll lure {data.BonfireDayRadius} zones. " +
                    $"Migration rolls daily at {rollClock} (world time). Admins: use /zombiemap, then open M for a single zone snapshot.");
            });
    }
    public override void Dispose()
    {
        if (sapi == null) return;
        sapi.Event.SaveGameLoaded -= Load;
        sapi.Event.GameWorldSave -= Save;
        sapi.Event.DidPlaceBlock -= Placed;
        sapi.Event.DidBreakBlock -= Broken;
        sapi.Event.OnEntitySpawn -= OnPotentialTrader;
        sapi.Event.OnEntityLoaded -= OnPotentialTrader;
        sapi.Event.PlayerDeath -= Died;
        sapi.Event.BreakBlock -= PlayerBreak;
        sapi.Event.ChunkColumnLoaded -= RememberLoadedColumn;
        sapi.Event.UnregisterGameTickListener(ticker);
    }
    // The engine also calls this for existing chunks loaded back from disk.
    // It never generates terrain: it merely records a 3x3-chunk zone key.
    void RememberLoadedColumn(Vec2i chunkCoord, IWorldChunk[] chunks)
    {
        var zone = WatchZone.FromChunk(chunkCoord.X, chunkCoord.Y, 0);
        lock (exploredLock) exploredZones.Add(zone);
    }

    WatchZoneState GetOrCreate(WatchZone key)
    {
        if (!zones.TryGetValue(key, out var state)) zones[key] = state = new WatchZoneState
            { Dimension = key.Dimension, X = key.X, Z = key.Z };
        return state;
    }
    // Drop building records whose block is gone (only where the chunk is
    // loaded; unloaded records are kept). Updates a stronghold's tier if the
    // block was swapped. Returns true if anything changed.
    internal bool ValidateBuildings(WatchZoneState state)
    {
        if (sapi == null || state == null) return false;
        var blocks = sapi.World.BlockAccessor;
        bool changed = false;
        bool Gone(WatchBuilding b, System.Func<Block, bool> stillThere)
        {
            var pos = b.Position;
            if (blocks.GetChunkAtBlockPos(pos) == null) return false;
            return !stillThere(blocks.GetBlock(pos));
        }
        changed |= state.Strongholds.RemoveAll(b => Gone(b, IsStronghold)) > 0;
        foreach (var stronghold in state.Strongholds)
        {
            var pos = stronghold.Position;
            if (blocks.GetChunkAtBlockPos(pos) == null) continue;
            int tier = StrongholdStrength(blocks.GetBlock(pos));
            if (tier > 0 && tier != stronghold.StrongholdStrength) { stronghold.StrongholdStrength = tier; changed = true; }
        }
        changed |= state.Bells.RemoveAll(b => Gone(b, block => IsCode(block, BellCode))) > 0;
        changed |= state.Bonfires.RemoveAll(b => Gone(b, block => IsCode(block, BonfireCode) ||
            IsCode(block, BonfireLoadedCode) || IsCode(block, BonfireLitCode))) > 0;
        return changed;
    }
    void Load()
    {
        try { LoadZones(); zonesLoaded = true; }
        catch
        {
            zonesLoaded = false;
            // A swallowed engine load callback must not allow empty/partial
            // zone data to overwrite the save on the next autosave or shutdown.
            sapi.Event.RegisterCallback(_ => sapi.Server.ShutDown(), 1);
            throw;
        }
    }
    void LoadZones()
    {
        byte[] data = sapi.WorldManager.SaveGame.GetData(SaveKey);
        if (data == null) { lastRoll = StartupRollDay(sapi.World.Calendar.TotalDays); return; }
        var state = JsonConvert.DeserializeObject<LastWatchSave>(Encoding.UTF8.GetString(data));
        if (state == null || (state.Version != 1 && state.Version != 2 && state.Version != 3 && state.Version != 4 && state.Version != 5 && state.Version != 6 && state.Version != 7 && state.Version != 8) || state.Zones == null)
            throw new InvalidDataException("Invalid Last Watch zone save");
        zones.Clear();
        // Merge, do not replace: chunk loads can overlap the save-load event.
        lock (exploredLock) exploredZones.UnionWith(state.ExploredZones ?? new List<WatchZone>());
        foreach (var z in state.Zones)
        {
            if (z == null || z.AmbientPopulationAddedToday < 0 || z.DormantReactivatedToday < 0 ||
                z.StonePaths < 0 || z.PlayerDeaths < 0 ||
                z.Bells == null || z.Strongholds == null || zones.ContainsKey(z.Key))
                throw new InvalidDataException("Corrupt Last Watch zone entry");
            // Old versions stored no strength; their blocks become copper tier.
            foreach (var stronghold in z.Strongholds)
                if (stronghold.StrongholdStrength <= 0)
                    stronghold.StrongholdStrength = CopperStrongholdStrength;
            z.AmbientPopulationAddedByPlayer ??= new();
            z.Bonfires ??= new(); // old saves had no bonfires
            z.BonfireDailyRecords ??= new(); // versions 1-3 had no daily ledger
            if (z.BonfireDailyRecords.Any(x => x == null || x.PeakFuelStacks < 0 || x.PeakFuelStacks > 128))
                throw new InvalidDataException("Invalid bonfire day record");
            if (state.Version < 4)
            {
                // Best-effort migration for existing v1.5.2 fires still saved
                // as lit. A completed fire in the older save cannot be recovered.
                foreach (var b in z.Bonfires)
                {
                    if (b.BonfireFuelStacks < 3 || b.BonfireBurnUntilHours <= 0) continue;
                    long ignitionDay = (long)Math.Floor((b.BonfireBurnUntilHours - 9) / 24.0);
                    long calendarDay = (long)Math.Floor(sapi.World.Calendar.TotalDays);
                    if (ignitionDay >= calendarDay - 1 && ignitionDay <= calendarDay)
                        RecordBonfirePeak(z, ignitionDay, b.BonfireFuelStacks);
                }
            }
            // Legacy block-change and ruin flags are intentionally ignored;
            // migrated saved zombie counts remain in the separate population pool.
            zones.Add(z.Key, z);
        }
        if (state.Version < 5)
        {
            // Old saves rolled at midnight. On upgrade, begin a fresh sunset
            // schedule and carry forward recorded bonfire influence, rather
            // than silently discarding fires lit under the old schedule.
            long pendingRoll = UpcomingRollDay(sapi.World.Calendar.TotalDays);
            foreach (var zone in zones.Values)
            {
                int peak = zone.BonfireDailyRecords.Where(x => x.Day >= pendingRoll - 2)
                    .Select(x => x.PeakFuelStacks).DefaultIfEmpty(0).Max();
                zone.BonfireDailyRecords.Clear();
                RecordBonfirePeak(zone, pendingRoll, peak);
            }
            lastRoll = StartupRollDay(sapi.World.Calendar.TotalDays);
        }
        else lastRoll = state.LastDailyRoll;
        // Never replay multiple offline daily rolls after sleep/time skips.
        long currentRoll = StartupRollDay(sapi.World.Calendar.TotalDays);
        if (lastRoll < currentRoll)
        {
            foreach (var zone in zones.Values) ResetDailyAllowances(zone, currentRoll);
            lastRoll = currentRoll;
        }
    }
    // Dormant counts live in ZombiePopulation, not here. Keep only zone records
    // carrying a persistent building/settlement/trader marker, or live daily data.
    // Removal of an empty zone never removes its dormant population.
    internal static bool HasZoneStateWorthSaving(WatchZoneState z) =>
        z.TraderPopulationSeeded || z.PermanentSettlement || z.StonePaths > 0 ||
        z.Strongholds.Count > 0 || z.Bells.Count > 0 || z.Bonfires.Count > 0 ||
        z.BonfireDailyRecords.Count > 0 || z.PlayerDeaths > 0 ||
        z.AmbientPopulationAddedToday > 0 || z.DormantReactivatedToday > 0 ||
        z.AmbientPopulationAddedByPlayer.Count > 0;

    void PruneEmptyZones()
    {
        foreach (var key in zones.Keys.ToArray())
            if (!HasZoneStateWorthSaving(zones[key])) zones.Remove(key);
    }

    void Save()
    {
        if (!zonesLoaded) return;
        PruneEmptyZones();
        List<WatchZone> recorded;
        lock (exploredLock) recorded = exploredZones.ToList();
        var snapshot = new LastWatchSave { LastDailyRoll = lastRoll,
            Zones = zones.Values.ToList(), ExploredZones = recorded };
        // Omit optional zero/default fields without changing the on-disk key or
        // losing compatibility with existing full-size JSON zone saves.
        sapi.WorldManager.SaveGame.StoreData(SaveKey,
            Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(snapshot,
                new JsonSerializerSettings { DefaultValueHandling = DefaultValueHandling.IgnoreAndPopulate })));
    }
    // New ambient population: maximum five per occupied zone per sunset cycle,
    // shared by every Survival player in the zone. This overrides the base
    // nightly per-player quota. Dormant returns consume no new-spawn allowance.
    public bool CanAmbientPopulationSpawn(WatchZone zone, string playerUid)
    {
        if (!core.Settings.LastWatchEnabled || string.IsNullOrEmpty(playerUid)) return false;
        var present = sapi.World.AllOnlinePlayers.Any(p => p.PlayerUID == playerUid &&
            p.Entity != null && SeventhNightSystem.IsTarget(p.Entity) &&
            WatchZone.FromBlock((int)Math.Floor(p.Entity.Pos.X),
                (int)Math.Floor(p.Entity.Pos.Z), p.Entity.Pos.Dimension) == zone);
        if (!present) return false;
        return CanAddAmbientPopulationSpawn(zone, playerUid) ||
            (Population.HasDormantInZone(zone) && CanReactivateDormant(zone));
    }

    // Zone-wide diagnostic: true while this zone has a remaining NEW-spawn
    // allowance and at least one eligible online Survival player occupies it.
    public bool CanAddAmbientPopulationSpawn(WatchZone zone) => sapi?.World?.AllOnlinePlayers?.Any(p =>
        p.Entity != null && SeventhNightSystem.IsTarget(p.Entity) &&
        WatchZone.FromBlock((int)Math.Floor(p.Entity.Pos.X),
            (int)Math.Floor(p.Entity.Pos.Z), p.Entity.Pos.Dimension) == zone &&
        CanAddAmbientPopulationSpawn(zone, p.PlayerUID)) == true;

    public bool CanAddAmbientPopulationSpawn(WatchZone zone, string playerUid)
    {
        if (!core.Settings.LastWatchEnabled || core.Settings.ZombiePopDailyAddLimit <= 0 ||
            string.IsNullOrEmpty(playerUid)) return false;
        if (!zones.TryGetValue(zone, out var state)) return true;
        // Use the aggregate zone count. Two players in one zone share the SAME
        // five slots, rather than each receiving a separate allocation.
        return state.AmbientPopulationAddedToday < core.Settings.ZombiePopDailyAddLimit;
    }

    // New ambient slots take priority; dormant returns never spend those slots.
    public bool CanReactivateDormant(WatchZone zone)
    {
        if (!core.Settings.LastWatchEnabled || AmbientSlotsRemaining(zone) > 0) return false;
        // Zero is unlimited (not disabled): keep trying small batches until
        // the zone's stored dormant population has returned.
        if (core.Settings.DormantReactivationPerZonePerDay == 0) return true;
        return !zones.TryGetValue(zone, out var state) ||
            state.DormantReactivatedToday < core.Settings.DormantReactivationPerZonePerDay;
    }

    public int AmbientSlotsRemaining(WatchZone zone) => Math.Max(0,
        core.Settings.ZombiePopDailyAddLimit - (zones.TryGetValue(zone, out var state)
            ? state.AmbientPopulationAddedToday : 0));

    public void RecordDormantReactivation(WatchZone zone)
    {
        var state = GetOrCreate(zone);
        if (state.DormantReactivatedToday < int.MaxValue) state.DormantReactivatedToday++;
    }

    public int ReactivatedToday(WatchZone zone) =>
        zones.TryGetValue(zone, out var state) ? state.DormantReactivatedToday : 0;

    public int AmbientAddedForPlayer(WatchZone zone, string playerUid)
    {
        if (string.IsNullOrEmpty(playerUid) || !zones.TryGetValue(zone, out var state)) return 0;
        return state.AmbientPopulationAddedByPlayer != null &&
            state.AmbientPopulationAddedByPlayer.TryGetValue(playerUid, out int count) ? count : 0;
    }

    public void RecordAmbientPopulationSpawn(WatchZone zone, string playerUid)
    {
        if (string.IsNullOrEmpty(playerUid)) return;
        var state = GetOrCreate(zone);
        state.AmbientPopulationAddedByPlayer.TryGetValue(playerUid, out int current);
        if (current < int.MaxValue) state.AmbientPopulationAddedByPlayer[playerUid] = current + 1;
        if (state.AmbientPopulationAddedToday < int.MaxValue)
            state.AmbientPopulationAddedToday++;
    }

    public void ResetCurrentZoneAmbientQuota(WatchZone zone)
    {
        var state = GetOrCreate(zone);
        state.AmbientPopulationAddedToday = 0;
        state.AmbientPopulationAddedByPlayer.Clear();
        Save();
    }

    static bool IsPath(Block b) => b?.Code?.Path?.Contains("stonepath", StringComparison.OrdinalIgnoreCase) == true ||
        b?.Code?.Path?.Contains("stone-path", StringComparison.OrdinalIgnoreCase) == true;
    static bool IsCode(Block b, string code) => b?.Code?.ToString() == code;
    static WatchBuilding BuildingAt(BlockPos pos) => new() { X = pos.X, Y = pos.Y, Z = pos.Z, Dimension = pos.dimension };
    // Industry is not tracked per interaction or persisted per block. Once per
    // sunset, inspect only already-loaded terrain in a Survival-player or
    // stronghold zone. Stop scanning as soon as all three block types are found.
    // This allows old/unused forges and anvils to count without saving positions.
    internal static int IndustryFlag(Block b)
    {
        string code = b?.Code?.Path ?? "";
        if (code.Contains("charcoalpit", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("charcoal-pit", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("charcoalpile", StringComparison.OrdinalIgnoreCase) ||
            code.Contains("charcoal-pile", StringComparison.OrdinalIgnoreCase)) return 2;
        if (code.Contains("forge", StringComparison.OrdinalIgnoreCase)) return 1;
        if (code.Contains("anvil", StringComparison.OrdinalIgnoreCase)) return 4;
        return 0;
    }
    // Forges, anvils and charcoal pits are block entities, so the once-per-
    // sunset scan enumerates the block entities of the zone's LOADED chunks
    // (3x3 columns, every height) instead of reading ~300k individual blocks.
    // Only entities within the configured band around the terrain surface
    // count. Unloaded chunks are skipped, never loaded.
    int ScanIndustryInLoadedZone(WatchZone zone)
    {
        int flags = 0;
        var world = sapi.World.BlockAccessor;
        int below = core.Settings.ZoneIndustrySurfaceBelowBlocks;
        int above = core.Settings.ZoneIndustrySurfaceAboveBlocks;
        int chunksHigh = Math.Max(1, sapi.WorldManager.MapSizeY / GlobalConstants.ChunkSize);
        int dimensionOffset = zone.Dimension * GlobalConstants.DimensionSizeInChunks;
        for (int cx = zone.X * WatchZone.ChunksPerSide; cx < (zone.X + 1) * WatchZone.ChunksPerSide; cx++)
        for (int cz = zone.Z * WatchZone.ChunksPerSide; cz < (zone.Z + 1) * WatchZone.ChunksPerSide; cz++)
        for (int cy = 0; cy < chunksHigh; cy++)
        {
            var entities = world.GetChunk(cx, cy + dimensionOffset, cz)?.BlockEntities;
            if (entities == null || entities.Count == 0) continue;
            core.Profiler.Count("migration.industryBlockEntities", entities.Count);
            foreach (var entity in entities.Values)
            {
                int type = IndustryFlag(entity?.Block);
                if (type == 0 || (flags & type) == type || entity.Pos == null) continue;
                int surface = world.GetTerrainMapheightAt(entity.Pos);
                if (entity.Pos.Y < surface - below || entity.Pos.Y > surface + above) continue;
                flags |= type;
                if (flags == 7) return flags;
            }
        }
        return flags;
    }
    void OnPotentialTrader(Entity entity)
    {
        if (!zonesLoaded || !core.Settings.LastWatchEnabled || entity == null || !entity.Alive ||
            entity.Properties?.Code?.Path?.Contains("trader", StringComparison.OrdinalIgnoreCase) != true) return;
        var position = entity.Pos;
        var state = GetOrCreate(WatchZone.FromBlock((int)Math.Floor(position.X),
            (int)Math.Floor(position.Z), position.Dimension));
        if (state.TraderPopulationSeeded) return;
        state.TraderPopulationSeeded = true;
        for (int i = 0; i < core.Settings.ZoneTraderDormantSeed; i++)
            Population.AddPending(SeventhNightSystem.LocalPosition(position), position.Dimension, false);
    }
    void Placed(IServerPlayer player, int oldId, BlockSelection sel, ItemStack item)
    {
        if (!core.Settings.LastWatchEnabled || sel?.Position == null || player == null) return;
        var pos = sel.Position;
        var now = sapi.World.BlockAccessor.GetBlock(pos);
        var key = WatchZone.FromBlock(pos.X, pos.Z, pos.dimension);
        if (!zones.TryGetValue(key, out var state))
        {
            if (!IsPath(now) && !IsStronghold(now) && !IsCode(now, BellCode) &&
                !IsCode(now, BonfireCode) && !IsCode(now, BonfireLoadedCode) && !IsCode(now, BonfireLitCode)) return;
            state = GetOrCreate(key);
        }
        if (IsPath(now)) state.StonePaths = Math.Min(core.Settings.ZoneMaxPathsCounted, state.StonePaths + 1);
        if (state.StonePaths >= 25 && state.Strongholds.Count > 0)
            state.PermanentSettlement = true;
        if (IsCode(now, BellCode) && !state.Bells.Any(b => b.X == pos.X && b.Y == pos.Y && b.Z == pos.Z))
            state.Bells.Add(BuildingAt(pos));
        if (IsStronghold(now) && !state.Strongholds.Any(b => b.X == pos.X && b.Y == pos.Y && b.Z == pos.Z))
        {
            var building = BuildingAt(pos);
            building.StrongholdStrength = StrongholdStrength(now);
            state.Strongholds.Add(building);
            if (state.StonePaths >= 25) state.PermanentSettlement = true;
        }
        if ((IsCode(now, BonfireCode) || IsCode(now, BonfireLoadedCode) || IsCode(now, BonfireLitCode)) &&
            !state.Bonfires.Any(b => b.X == pos.X && b.Y == pos.Y && b.Z == pos.Z))
            state.Bonfires.Add(BuildingAt(pos));
        ValidateBuildings(state);
        if (state.Strongholds.Count > 0 && reinforce != null &&
            !IsStronghold(now) && !IsCode(now, BellCode) &&
            !IsCode(now, BonfireCode) && !IsCode(now, BonfireLoadedCode) && !IsCode(now, BonfireLitCode))
        {
            // Vanilla's actual reinforcement registry; never overwrite others'
            // existing reinforcements and only affect newly placed eligible blocks.
            // Multiple strongholds use the strongest available tier, not their sum.
            int strength = state.Strongholds.Max(b => b.StrongholdStrength > 0
                ? b.StrongholdStrength : CopperStrongholdStrength);
            reinforce.StrengthenBlock(pos, player, strength);
        }
    }
    void Broken(IServerPlayer player, int oldId, BlockSelection sel)
    {
        if (player == null || sel?.Position == null) return;
        ForgetBlock(sel.Position, sapi.World.GetBlock(oldId));
    }
    // Remove zone records for a block that no longer exists. Called for player
    // breaks and for blocks destroyed by zombie sieges. Idempotent for
    // buildings; each call decrements the stone-path count at most once.
    public void ForgetBlock(BlockPos pos, Block old)
    {
        if (sapi == null || !core.Settings.LastWatchEnabled || pos == null || old == null) return;
        if (!zones.TryGetValue(WatchZone.FromBlock(pos.X, pos.Z, pos.dimension), out var state)) return;
        if (IsPath(old)) state.StonePaths = Math.Max(0, state.StonePaths - 1);
        if (IsCode(old, BellCode)) state.Bells.RemoveAll(b => b.X == pos.X && b.Y == pos.Y && b.Z == pos.Z);
        if (IsStronghold(old)) state.Strongholds.RemoveAll(b => b.X == pos.X && b.Y == pos.Y && b.Z == pos.Z);
        if (IsCode(old, BonfireCode) || IsCode(old, BonfireLoadedCode) || IsCode(old, BonfireLitCode))
            state.Bonfires.RemoveAll(b => b.X == pos.X && b.Y == pos.Y && b.Z == pos.Z);
    }
    void Died(IServerPlayer p, DamageSource source)
    {
        if (!core.Settings.LastWatchEnabled || p?.Entity == null) return;
        var pos = p.Entity.Pos;
        GetOrCreate(WatchZone.FromBlock((int)Math.Floor(pos.X), (int)Math.Floor(pos.Z), pos.Dimension)).PlayerDeaths++;
    }
    void PlayerBreak(IServerPlayer p, BlockSelection sel, ref float dropMultiplier, ref EnumHandling handling)
    {
        if (!core.Settings.LastWatchEnabled || p?.Entity == null || sel?.Position == null || reinforce == null) return;
        var pos = sel.Position;
        if (!zones.TryGetValue(WatchZone.FromBlock(pos.X, pos.Z, pos.dimension), out var state)) return;
        ValidateBuildings(state);
        if (state.Strongholds.Count == 0) return;
        // Respect existing claim ownership / permission rules. Never turn a
        // stronghold into a land-claim or lock bypass.
        if (!sapi.World.Claims.TryAccess(p, pos, EnumBlockAccessFlags.BuildOrBreak)) return;
        var existing = reinforce.GetReinforcment(pos);
        if (existing == null) return;
        // Claim access alone does not grant reinforcement ownership.
        // Group and other-owner blocks retain vanilla removal rules.
        if (existing.GroupUid != 0 || existing.PlayerUID != p.PlayerUID) return;
        // No plumb-and-square removal step for permitted players within a
        // stronghold zone; zombies still consume one strength per strike.
        reinforce.ClearReinforcement(pos);
    }
    void Tick(float dt)
    {
        if (!zonesLoaded) return;
        if (!core.Settings.LastWatchEnabled || core.Population == null) return;
        if (!startupEcologyChecked)
        {
            startupEcologyChecked = true;
            foreach (var e in sapi.World.LoadedEntities.Values) OnPotentialTrader(e);
        }
        double totalDays = sapi.World.Calendar.TotalDays;
        long today = (long)Math.Floor(totalDays);
        double hour = (totalDays - today) * 24.0;
        if (hour < core.Settings.ZoneMigrationHour || today <= lastRoll) return;
        // A single roll at or after sunset, even if time skips several days.
        // Ensure the horde begins BEFORE migration even when the server
        // skips across the scheduled pre-sunset trigger in one calendar tick.
        core.TryStartScheduledHordeBeforeMigration(atMigrationRoll: true);
        lastRoll = today;
        DailyMigration(today);
        // A fire still alight at sunset also contributes to the NEXT cycle.
        RecordStillBurningBonfires(today + 1);
    }
    public sealed class ZoneView
    {
        public int Dimension, X, Z, Dormant, ActiveZombies, Paths, Deaths, Strongholds, Bells, BurningBonfires, BonfireStacks, BonfireRadius;
        public int BonfireDayStacks, BonfireDayRadius, AmbientAdded;
        public long Changes, PlacedToday, TotalChanges, TotalPlaced;
        public bool Hotspot, Settlement;
    }
    // Called by an ignited/extinguished pit. Ignition records a full day's
    // attraction even if the fire goes out before the next daily migration.
    public void UpdateBonfire(BlockPos pos, double burnUntilHours, int stacks)
    {
        if (sapi == null) return;
        var state = GetOrCreate(WatchZone.FromBlock(pos.X,pos.Z,pos.dimension));
        var marker = state.Bonfires.FirstOrDefault(b => b.X==pos.X && b.Y==pos.Y && b.Z==pos.Z);
        if (marker == null)
        {
            marker=BuildingAt(pos);
            state.Bonfires.Add(marker);
        }
        marker.BonfireBurnUntilHours = burnUntilHours;
        marker.BonfireFuelStacks = stacks;
        if (stacks >= 3 && burnUntilHours > sapi.World.Calendar.TotalHours)
        {
            // Ignition immediately books the strongest tier for this day.
            RecordBonfirePeak(state, UpcomingRollDay(sapi.World.Calendar.TotalDays), stacks);
        }
        // A later extinguish/removal clears the live marker only, not today's
        // accrued influence. This prevents burn expiry from cancelling a lure.
    }

    static void RecordBonfirePeak(WatchZoneState zone, long day, int stacks)
    {
        if (stacks < 3) return;
        zone.BonfireDailyRecords ??= new();
        var record = zone.BonfireDailyRecords.FirstOrDefault(x => x.Day == day);
        if (record == null)
            zone.BonfireDailyRecords.Add(new BonfireDailyRecord { Day = day, PeakFuelStacks = stacks });
        else
            record.PeakFuelStacks = Math.Max(record.PeakFuelStacks, stacks);
        // A fire cannot generate an infinite historical influence list.
        zone.BonfireDailyRecords.RemoveAll(x => x.Day < day - 1);
    }

    static int BonfirePeakForDay(WatchZoneState zone, long day) =>
        zone?.BonfireDailyRecords?.Where(x => x.Day == day)
            .Select(x => x.PeakFuelStacks).DefaultIfEmpty(0).Max() ?? 0;

    void RecordStillBurningBonfires(long day)
    {
        double now = sapi.World.Calendar.TotalHours;
        foreach (var zone in zones.Values)
        foreach (var b in zone.Bonfires)
        {
            if (b.BonfireFuelStacks < 3 || b.BonfireBurnUntilHours <= now) continue;
            bool loaded = sapi.World.BlockAccessor.GetChunkAtBlockPos(b.Position) != null;
            if (loaded && !(sapi.World.BlockAccessor.GetBlockEntity(b.Position)
                is BlockEntityPitBonfire pit && pit.ActiveForMigration)) continue;
            // Unloaded chunks use the recorded expiry; loaded ones validate fuel.
            RecordBonfirePeak(zone, day, b.BonfireFuelStacks);
        }
    }

    // Chebyshev distance: ring 1 = all eight adjacent zones, ring 2 = the
    // next sixteen neighboring zones, ring 3 = the outer twenty-four.
    // All attracted zombies move AT MOST one zone on a single daily roll.
    static int BonfireRadius(int stacks) => stacks >= 12 ? 3 : stacks >= 9 ? 2 : stacks >= 6 ? 1 : 0;
    (int Count,int Stacks,int Radius) BonfireStatus(WatchZoneState z)
    {
        if (z?.Bonfires == null) return (0,0,0);
        int count = 0, maxStacks = 0;
        foreach (var b in z.Bonfires)
        {
            bool loaded = sapi.World.BlockAccessor.GetChunkAtBlockPos(b.Position) != null;
            if (loaded)
            {
                // A loaded fire must prove its fuel piles still exist.
                if (sapi.World.BlockAccessor.GetBlockEntity(b.Position) is BlockEntityPitBonfire pit && pit.ActiveForMigration)
                {
                    count++; maxStacks = Math.Max(maxStacks,pit.FuelStacks);
                }
            }
            else if (b.BonfireFuelStacks >= 3 && b.BonfireBurnUntilHours > sapi.World.Calendar.TotalHours)
            {
                count++; maxStacks = Math.Max(maxStacks,b.BonfireFuelStacks);
            }
        }
        return (count,maxStacks,BonfireRadius(maxStacks));
    }
    public ZoneView SnapshotAt(WatchZone key)
    {
        zones.TryGetValue(key, out var z);
        ValidateBuildings(z);
        int dormant = (int)Math.Min(int.MaxValue, Population.MigrationSnapshot().Where(c =>
            WatchZone.FromChunk(c.X, c.Z, c.Dimension) == key).Sum(c => c.Normal + c.Alpha));
        var fire = BonfireStatus(z);
        int active = sapi.World.LoadedEntities == null ? 0 : sapi.World.LoadedEntities.Values
            .OfType<Zombie>().Count(entity => entity.Alive && !entity.IsDormant &&
                WatchZone.FromBlock((int)Math.Floor(entity.Pos.X),
                    (int)Math.Floor(entity.Pos.Z), entity.Pos.Dimension) == key);
        int dayPeak = BonfirePeakForDay(z, UpcomingRollDay(sapi.World.Calendar.TotalDays));
        return new ZoneView { Dimension=key.Dimension, X=key.X, Z=key.Z,
            ActiveZombies=active,
            BurningBonfires=fire.Count, BonfireStacks=fire.Stacks,BonfireRadius=fire.Radius,
            BonfireDayStacks=dayPeak, BonfireDayRadius=BonfireRadius(dayPeak),
            Dormant=dormant, AmbientAdded=z?.AmbientPopulationAddedToday ?? 0, Changes=0,
            PlacedToday=0, TotalChanges=0,
            TotalPlaced=0, Settlement=z?.PermanentSettlement ?? false,
            Paths=z?.StonePaths ?? 0, Deaths=z?.PlayerDeaths ?? 0,
            Strongholds=z?.Strongholds.Count ?? 0, Bells=z?.Bells.Count ?? 0,
            Hotspot=false };
    }
    public List<ZoneView> SnapshotAll()
    {
        var dormant = Population.MigrationSnapshot().GroupBy(c => WatchZone.FromChunk(c.X,c.Z,c.Dimension))
            .ToDictionary(g=>g.Key, g=>g.Sum(c=>c.Normal+c.Alpha));
        // Active zombies are NOT saved as zone population. Count only living,
        // currently loaded entities in a one-time map-open snapshot; include
        // zones with active zombies even if they have no persistent zone record.
        var loadedZombies = sapi.World.LoadedEntities == null
            ? Enumerable.Empty<Zombie>() : sapi.World.LoadedEntities.Values.OfType<Zombie>();
        var active = loadedZombies.Where(entity => entity.Alive && !entity.IsDormant)
            .GroupBy(entity => WatchZone.FromBlock((int)Math.Floor(entity.Pos.X),
                (int)Math.Floor(entity.Pos.Z), entity.Pos.Dimension))
            .ToDictionary(group => group.Key, group => group.Count());
        var keys = zones.Keys.Concat(dormant.Keys).Concat(active.Keys).Distinct().ToArray();
        long today = UpcomingRollDay(sapi.World.Calendar.TotalDays);
        var result = new List<ZoneView>(keys.Length);
        foreach (var k in keys)
        {
            zones.TryGetValue(k, out var z);
            dormant.TryGetValue(k, out var d);
            active.TryGetValue(k, out var living);
            var fire = BonfireStatus(z);
            int dayPeak = BonfirePeakForDay(z, today);
            result.Add(new ZoneView { Dimension=k.Dimension,X=k.X,Z=k.Z,
                BurningBonfires=fire.Count,BonfireStacks=fire.Stacks,BonfireRadius=fire.Radius,
                BonfireDayStacks=dayPeak,BonfireDayRadius=BonfireRadius(dayPeak),
                Dormant=(int)Math.Min(int.MaxValue,d),ActiveZombies=living,
                AmbientAdded=z?.AmbientPopulationAddedToday??0, Changes=0,
                PlacedToday=0, TotalChanges=0,
                TotalPlaced=0,Settlement=z?.PermanentSettlement??false,
                Paths=z?.StonePaths??0,Deaths=z?.PlayerDeaths??0,
                Strongholds=z?.Strongholds.Count??0,Bells=z?.Bells.Count??0,
                Hotspot=false });
        }
        return result;
    }
    internal static WatchZone StepToward(WatchZone start, WatchZone goal) =>
        new(start.Dimension, start.X + Math.Sign(goal.X - start.X),
            start.Z + Math.Sign(goal.Z - start.Z));
    // Move at most one zone normally or two during a horde. Never
    // overshoot a nearby destination or leap to a ten-ring lure directly.
    internal static WatchZone DestinationForMigration(WatchZone from, WatchZone goal, int steps)
    {
        var current = from;
        for (int i = 0; i < Math.Clamp(steps, 1, 2) && current != goal; i++)
            current = StepToward(current, goal);
        return current;
    }

    // Every unmasked Survival player adds the same fixed bonus. No group scaling.
    public static double PlayerPresenceAttraction(int unmaskedPlayers, double bonus) =>
        Math.Max(0, unmaskedPlayers) * bonus;

    internal static int AttractionTierRadius(double score, Config c) =>
        score >= c.ZoneAttractionTier10Threshold ? 10 :
        score >= c.ZoneAttractionTier8Threshold ? 8 :
        score >= c.ZoneAttractionTier6Threshold ? 6 :
        score >= c.ZoneAttractionTier4Threshold ? 4 : 2;

    static bool IsFarmAnimal(Entity e)
    {
        string code = e.Properties?.Code?.Path?.ToLowerInvariant() ?? "";
        return code.Contains("sheep") || code.Contains("bighorn") || code.Contains("goat") ||
            code.Contains("pig") || code.Contains("chicken") ||
            code.Contains("hen") || code.Contains("rooster") ||
            code.Contains("cow") || code.Contains("bull") ||
            code.Contains("calf");
    }

    void DailyMigration(long today) => RunMigration(today, true);

    void RunMigration(long today, bool finishDay)
    {
        using var timing = core.Profiler.Measure("migration.daily");
        var C = core.Settings;
        // Explosions, other mods or world edits can remove buildings without a
        // player break event; never let a vanished stronghold keep attracting.
        foreach (var zone in zones.Values) ValidateBuildings(zone);
        var start = Population.MigrationSnapshot();
        core.Profiler.Count("migration.originChunks", start.Count);
        // Freeze attraction for the entire day; updated populations cannot
        // migrate a second time in one day's pass.

        // A sunset attraction score selects 2 / 4 / 6 / 8 zone rings,
        // with ten rings reserved for exceptional combined attraction.
        // A migrant moves one directed zone normally, or up to two during a horde.
        // A distant attraction always requires multiple sunset cycles.
        // Snapshot the live player presence precisely when sunset rolls.
        // Usable funeral masks exclude their wearers from the presence lure.
        var playersAtSunset = sapi.World.AllOnlinePlayers
            .Where(p => p.Entity != null && SeventhNightSystem.IsTarget(p.Entity))
            .GroupBy(p => WatchZone.FromBlock((int)Math.Floor(p.Entity.Pos.X),
                (int)Math.Floor(p.Entity.Pos.Z), p.Entity.Pos.Dimension))
            .ToDictionary(g => g.Key, g => g.Count(p =>
                !ZombieMaskService.IsWearingUsableFuneralMask(p)));
        // One sunset pass over loaded livestock; unloaded animals are not guessed.
        var livestockByZone = (sapi.World.LoadedEntities?.Values?.AsEnumerable()
            ?? Enumerable.Empty<Entity>())
            .Where(e => e.Alive && e is not Zombie && IsFarmAnimal(e))
            .GroupBy(e => WatchZone.FromBlock((int)Math.Floor(e.Pos.X),
                (int)Math.Floor(e.Pos.Z), e.Pos.Dimension))
            .ToDictionary(g => g.Key, g => g.Count());
        // A single per-sunset snapshot. Player presence includes masked players:
        // disguises cancel their attraction bonus, not the ability to work here.
        var industryZones = sapi.World.AllOnlinePlayers
            .Where(p => p.Entity != null && SeventhNightSystem.IsTarget(p.Entity))
            .Select(p => WatchZone.FromBlock((int)Math.Floor(p.Entity.Pos.X),
                (int)Math.Floor(p.Entity.Pos.Z), p.Entity.Pos.Dimension))
            .Concat(zones.Values.Where(z => z.Strongholds.Count > 0).Select(z => z.Key))
            .Distinct().ToArray();
        var industryAtSunset = new Dictionary<WatchZone, int>();
        foreach (var key in industryZones)
        {
            int found = ScanIndustryInLoadedZone(key);
            if (found != 0) industryAtSunset[key] = found;
        }
        core.Profiler.Count("migration.industryZonesInspected", industryZones.Length);
        var targets = zones.Values.Where(z => z.PermanentSettlement ||
            z.StonePaths > 0 || z.PlayerDeaths > 0 || z.Strongholds.Count > 0 ||
            industryAtSunset.ContainsKey(z.Key)).ToArray();
        // Use the strongest bonfire ignited DURING THE FINISHED DAY (today-1),
        // regardless of whether its nine-hour physical fire is still alight.
        // 3+ stacks block outbound migration; 6/9/12 attract from 1/2/3 zones.
        // Dormant zombies never move more than two zones even during a horde.
        long finishedDay = today;
        var bonfireZones = zones.Values.Select(z => (z.Key,
            Status: new { Stacks = BonfirePeakForDay(z, finishedDay),
                Radius = BonfireRadius(BonfirePeakForDay(z, finishedDay)) }))
            .Where(x => x.Status.Stacks >= 3).ToArray();
        var protectedZones = bonfireZones.Select(b => b.Key).ToHashSet();
        var bonfireAttractors = bonfireZones.Where(b => b.Status.Stacks >= 6)
            .ToDictionary(b => b.Key, b => b.Status.Radius);
        // Bonfires remain an independent 1/2/3-ring lure and do not
        // automatically extend the settlement tier.
        var targetKeys = targets.Select(z => z.Key).Concat(playersAtSunset.Keys)
            .Concat(industryAtSunset.Keys)
            .Concat(livestockByZone.Where(p => p.Value > C.ZoneFarmAnimalThreshold).Select(p => p.Key))
            .Distinct().ToArray();
        // Four simple attraction tiers plus a rare ten-ring maximum. Compute
        // target scores ONCE, then index only origins in each target's radius.
        var attraction = new Dictionary<WatchZone, (double Score, int Radius)>();
        foreach (var key in targetKeys)
        {
            zones.TryGetValue(key, out var t);
            double score = (t?.StonePaths ?? 0) * C.ZoneStonePathBonusPerBlock +
                (t?.PlayerDeaths ?? 0) * C.ZoneDeathBonusPerDeath +
                ((t?.Strongholds.Count ?? 0) > 0 ? C.ZoneStrongholdBonus : 0) +
                (t?.PermanentSettlement == true ? C.ZoneSettlementBonus : 0) +
                PlayerPresenceAttraction(playersAtSunset.GetValueOrDefault(key), C.ZonePlayerPresenceBonus) +
                ((livestockByZone.GetValueOrDefault(key) > C.ZoneFarmAnimalThreshold) ? C.ZoneFarmAnimalAttractionBonus : 0) +
                ((industryAtSunset.GetValueOrDefault(key) & 1) != 0 ? C.ZoneForgeActivityBonus : 0) +
                ((industryAtSunset.GetValueOrDefault(key) & 2) != 0 ? C.ZoneCharcoalActivityBonus : 0) +
                ((industryAtSunset.GetValueOrDefault(key) & 4) != 0 ? C.ZoneAnvilActivityBonus : 0);
            if (score <= 0) continue;
            int reach = AttractionTierRadius(score, C);
            attraction[key] = (score, reach);
        }
        var originZones = start
            .Select(c => WatchZone.FromChunk(c.X, c.Z, c.Dimension)).ToHashSet();
        var nearbyAttractions = new Dictionary<WatchZone, List<WatchZone>>();
        foreach (var entry in attraction)
        {
            var target = entry.Key;
            int radius = entry.Value.Radius;
            for (int dx = -radius; dx <= radius; dx++)
            for (int dz = -radius; dz <= radius; dz++)
            {
                var originZone = new WatchZone(target.Dimension, target.X + dx, target.Z + dz);
                if (originZone == target || !originZones.Contains(originZone)) continue;
                if (!nearbyAttractions.TryGetValue(originZone, out var near))
                    nearbyAttractions[originZone] = near = new List<WatchZone>();
                near.Add(target);
            }
        }
        var nearbyBonfires = new Dictionary<WatchZone, List<(WatchZone Key, int Stacks, int Radius)>>();
        foreach (var fire in bonfireZones)
        {
            if (fire.Status.Stacks < 6) continue;
            int r = fire.Status.Radius;
            for (int dx = -r; dx <= r; dx++)
            for (int dz = -r; dz <= r; dz++)
            {
                var originZone = new WatchZone(fire.Key.Dimension, fire.Key.X + dx, fire.Key.Z + dz);
                if (originZone == fire.Key || !originZones.Contains(originZone)) continue;
                if (!nearbyBonfires.TryGetValue(originZone, out var near))
                    nearbyBonfires[originZone] = near = new List<(WatchZone, int, int)>();
                near.Add((fire.Key, fire.Status.Stacks, fire.Status.Radius));
            }
        }
        var arrivals = new Dictionary<WatchZone, int>();
        long moved = 0;
        var transactions = new Dictionary<(string Source, int X, int Z, bool Alpha), MigrationTransfer>();
        var weightCache = new Dictionary<WatchZone, (KeyValuePair<WatchZone, double>[] Weighted, double Sum, double Chance)>();
        foreach (var origin in start)
        {
            var from = WatchZone.FromChunk(origin.X, origin.Z, origin.Dimension);
            if (protectedZones.Contains(from)) continue; // no outbound migration
            // Random drift and staying remain available at every sunset;
            // attraction increases directional likelihood, not certainty.
            if (!weightCache.TryGetValue(from, out var cached))
            {
            core.Profiler.Count("migration.weightCalculations");
            var weights = new Dictionary<WatchZone, double>();
            // Every sunset retains an independent possibility to stay put
            // or drift to ANY of the eight neighboring zones, even if strong
            // attractions are present. A "stay" roll consumes no population.
            weights[from] = C.ZoneBaseDailyMigrationChance * C.ZoneStayWeight;
            for (int dx = -1; dx <= 1; dx++) for (int dz = -1; dz <= 1; dz++)
            {
                if (dx == 0 && dz == 0) continue;
                weights[new WatchZone(from.Dimension, from.X + dx, from.Z + dz)] =
                    C.ZoneBaseDailyMigrationChance * C.ZoneRandomNeighborWeight / 8;
            }
            double rollChance = C.ZoneBaseDailyMigrationChance;
            foreach (var key in nearbyAttractions.TryGetValue(from, out var localTargets)
                ? localTargets : Enumerable.Empty<WatchZone>())
            {
                if (!attraction.TryGetValue(key, out var attractor)) continue;
                int distance = from.Distance(key);
                if (distance <= 0 || distance > attractor.Radius) continue;
                double influence = Math.Min(C.ZoneMaxDailyMigrationChance,
                    C.ZoneBaseDailyMigrationChance + attractor.Score) / Math.Sqrt(distance);
                weights[key] = weights.GetValueOrDefault(key) + influence;
                rollChance = Math.Max(rollChance, influence);
            }
            foreach (var fire in nearbyBonfires.TryGetValue(from, out var localBonfires)
                ? localBonfires : Enumerable.Empty<(WatchZone Key, int Stacks, int Radius)>())
            {
                int distance = from.Distance(fire.Key);
                if (distance <= 0 || distance > fire.Radius) continue;
                // Strong lure can dominate normal stone paths or building
                // activity; uses the same movement cap and delayed wake system.
                double chance = Math.Min(C.ZoneMaxDailyMigrationChance,
                    C.ZoneBaseDailyMigrationChance + C.ZoneBonfireAttractionBonus);
                weights[fire.Key] = weights.GetValueOrDefault(fire.Key) + chance / Math.Sqrt(distance);
                rollChance = Math.Max(rollChance,chance / Math.Sqrt(distance));
            }
            rollChance = Math.Min(C.ZoneMaxDailyMigrationChance, rollChance);
            var computed = weights.Where(w => w.Value > 0).ToArray();
            cached = (computed, computed.Sum(w => w.Value), rollChance);
            weightCache[from] = cached;
            }
            int migrationSteps = core.Horde ? Math.Clamp(C.HordeMigrationMaxZoneSteps, 1, 2) : 1;
            var weighted = cached.Weighted;
            double sum = cached.Sum;
            double chanceToMove = cached.Chance;
            if (sum <= 0 || chanceToMove <= 0) continue;
            // Landing chunks for each goal: any chunk column of the destination zone.
            var eligibleChunks = new Dictionary<WatchZone, (int X, int Z)[]>();
            foreach (var goal in weighted.Select(w => w.Key))
            {
                var dest = DestinationForMigration(from, goal, migrationSteps);
                var possible = new List<(int X, int Z)>();
                for (int x = dest.X * WatchZone.ChunksPerSide;
                    x < (dest.X + 1) * WatchZone.ChunksPerSide; x++)
                for (int z = dest.Z * WatchZone.ChunksPerSide;
                    z < (dest.Z + 1) * WatchZone.ChunksPerSide; z++)
                {
                    possible.Add((x, z));
                }
                eligibleChunks[goal] = possible.ToArray();
            }
            foreach (var pair in new[] { (alpha: false, n: origin.Normal), (alpha: true, n: origin.Alpha) })
            {
                long remain = pair.n;
                while (remain > 0)
                {
                    int batch = (int)Math.Min(4096, remain);
                    for (int i = 0; i < batch; i++)
                    {
                        if (sapi.World.Rand.NextDouble() >= chanceToMove) continue;
                        double pick = sapi.World.Rand.NextDouble() * sum;
                        WatchZone goal = weighted[^1].Key;
                        foreach (var entry in weighted)
                        {
                            pick -= entry.Value;
                            if (pick <= 0) { goal = entry.Key; break; }
                        }
                        if (goal == from) continue; // stay roll is a no-op
                        WatchZone dest = DestinationForMigration(from, goal, migrationSteps);
                        var choices = eligibleChunks[goal];
                        if (choices.Length == 0) continue;
                        var chunk = choices[sapi.World.Rand.Next(choices.Length)];
                        var key = (origin.Key, chunk.X, chunk.Z, pair.alpha);
                        if (!transactions.TryGetValue(key, out var transfer))
                            transactions[key] = transfer = new MigrationTransfer {
                                Source = origin.Key, DestX = chunk.X, DestY = origin.Y,
                                DestZ = chunk.Z, DestDimension = origin.Dimension, Alpha = pair.alpha
                            };
                        transfer.Count++;
                        arrivals[dest] = arrivals.GetValueOrDefault(dest) + 1;
                        moved++;
                    }
                    remain -= batch;
                }
            }
        }
        bool migrationCommitted = Population.ApplyMigration(transactions.Values.ToArray());
        if (!migrationCommitted)
        {
            sapi.Logger.Error("Last Watch migration aborted: underlying dormant pool changed during daily transaction.");
            arrivals.Clear(); moved=0;
        }
        // Bells are deliberately simple: one ring for arrivals in a neighboring
        // zone, two for arrivals at the bell, three when the bell's own zone has
        // more than ZoneBellOverrunThreshold dormant zombies after the migration.
        if (migrationCommitted)
        {
            var dormantByZone = Population.MigrationSnapshot()
                .GroupBy(c => WatchZone.FromChunk(c.X, c.Z, c.Dimension))
                .ToDictionary(g => g.Key, g => g.Sum(c => c.Normal + c.Alpha));
            foreach (var state in zones.Values.Where(z => z.Bells.Count > 0))
            {
                var bellZone = state.Key;
                int rings = dormantByZone.GetValueOrDefault(bellZone) > C.ZoneBellOverrunThreshold ? 3 :
                    arrivals.GetValueOrDefault(bellZone) > 0 ? 2 :
                    arrivals.Keys.Any(k => k.Distance(bellZone) == 1) ? 1 : 0;
                if (rings == 0) continue;
                foreach (var bell in state.Bells.ToArray())
                {
                    if (sapi.World.BlockAccessor.GetChunkAtBlockPos(bell.Position) == null) continue;
                    if (!IsCode(sapi.World.BlockAccessor.GetBlock(bell.Position), BellCode))
                    { state.Bells.Remove(bell); continue; }
                    AnnounceBell(bell, bellZone, rings);
                }
            }
        }
        if (finishDay) foreach (var z in zones.Values) ResetDailyAllowances(z, finishedDay);
        if (finishDay) PruneEmptyZones();
        // Lifetime history, permanent settlements, and road density persist.
        if (C.ZoneMigrationChatAnnouncement && moved > 0)
        {
            // One public announcement per daily migration, not per migrating
            // individual or zone. Watchtower bell alarms remain local.
            string msg = $"[The Last Watch] The undead are migrating! " +
                $"{moved} dormant zombie(s) moved into {arrivals.Count} zone(s) " +
                "during a migration cycle; they await normal reactivation.";
            foreach (var player in sapi.World.AllOnlinePlayers.OfType<IServerPlayer>())
                player.SendMessage(0, msg, EnumChatType.Notification);
        }
        if (C.LastWatchVerboseLogging) sapi.Logger.Notification($"Last Watch: day {today}, {moved} dormant zombies migrated into {arrivals.Count} zones; no immediate spawns.");
    }
    internal static void ResetDailyAllowances(WatchZoneState zone, long finishedDay)
    {
        zone.PlayerDeaths = 0;
        zone.AmbientPopulationAddedToday = 0;
        zone.DormantReactivatedToday = 0;
        zone.AmbientPopulationAddedByPlayer?.Clear();
        zone.BonfireDailyRecords.RemoveAll(x => x.Day <= finishedDay);
    }

    void AnnounceBell(WatchBuilding bell, WatchZone zone, int rings)
    {
        // Notify only the bell's 3x3 zone neighborhood, regardless of the
        // physical distance to the bell. Each ring is one individual sound.
        var listeners=sapi.World.AllOnlinePlayers.OfType<IServerPlayer>().Where(p=>p.Entity!=null &&
            WatchZone.FromBlock((int)Math.Floor(p.Entity.Pos.X),
                (int)Math.Floor(p.Entity.Pos.Z),p.Entity.Pos.Dimension).Distance(zone)
                <=core.Settings.ZoneBellRangeZones).ToArray();
        if(listeners.Length==0) return;
        var tone=new AssetLocation("seventhnightzombies:sounds/watchbell");
        for(int i=0;i<rings;i++)
        {
            sapi.Event.RegisterCallback(_=>
            {
                foreach(var listener in listeners)
                    if (listener.Entity!=null &&
                        WatchZone.FromBlock((int)Math.Floor(listener.Entity.Pos.X),
                        (int)Math.Floor(listener.Entity.Pos.Z),listener.Entity.Pos.Dimension)
                            .Distance(zone)<=core.Settings.ZoneBellRangeZones)
                        sapi.World.PlaySoundFor(tone,listener,false,32,1f);
            },i*1100);
        }
        foreach(var p in listeners)
            p.SendMessage(0, $"[The Last Watch] The bell rings {rings} " +
                (rings == 1 ? "time" : "times") + ": " +
                (rings switch
                {
                    1 => "Dormant zombies migrated into the neighboring ring.",
                    2 => "Dormant zombies migrated into this zone.",
                    _ => $"More than {core.Settings.ZoneBellOverrunThreshold} dormant zombies are in this zone."
                }), EnumChatType.Notification);
    }
}
