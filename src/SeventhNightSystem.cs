using System.Text;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Config;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeventhNightZombies;

public partial class SeventhNightSystem : ModSystem
{
    internal readonly HashSet<int> SharpenedStakeBlockIds = new();
    internal int LooseStickBlockId;
    internal readonly WorkBudget StakeScanBudget = new();
    public const string InfectionKey = "seventhnight-infection-seconds";
    // Seconds between poison ticks belong to server-only saved entity Attributes.
    // The watched marker above remains for clients, infection checks and old worlds,
    // but no longer dirties the entire player watched tree every server tick.
    internal const string InfectionServerClockKey = "seventhnight-infection-clock-server";
    private const string LegacyPoulticeKey = "seventhnight-honeysulfur-slowed";
    public const string InfectionStartDayKey = "seventhnight-infected-at-day";
    public const string CureDosesKey = "seventhnight-cure-doses";
    public const string CureLastDoseDayKey = "seventhnight-last-cure-dose-day";
    // Added in 2.0.10. Older saves with doses but no record are estimated from
    // the last dose (see FirstDoseDay), never backdated past infection.
    public const string CureFirstDoseDayKey = "seventhnight-first-cure-dose-day";
    public const string QuietBandageKey = "seventhnight-quiet-bandage";
    public Config Settings = new();
    public readonly WorkProfiler Profiler = new();
    public readonly WorkBudget AiBudget = new();
    readonly ZombieMaskService masks = new();
    readonly RotCoveredTunicService tunics = new();
    public ICoreServerAPI Server;
    public IZombiePopulation Population { get; private set; }
    public IZombieWorldDirector WorldDirector { get; private set; }
    // One zone director owns all ambient and dormant population accounting.
    IZombieWorldDirector AmbientDirector => WorldDirector;
    public override double ExecuteOrder() => 0.2;
    bool populationLoaded;
    void LoadPopulation()
    {
        try { LoadStakeTimers(); Population.Load(); RebuildActiveZombieRegistry(); populationLoaded = true; }
        catch
        {
            // Event callbacks can be caught and ignored by the engine. Explicitly
            // stop instead of continuing with an empty/uninitialized population.
            // Schedule shutdown on the server loop: stopping synchronously inside
            // world-loading callbacks can leave the engine's startup pipeline stuck.
            Server.Event.RegisterCallback(_ => Server.Server.ShutDown(), 1);
            throw;
        }
    }
    void SavePopulation()
    {
        if (!populationLoaded) return;
        SaveStakeTimers();
        Population.Save();
    }

    // O(1) active cap ledger. No per-second living-entity recount.
    // We rebuild once at world load and update via spawn/load/death/despawn.
    readonly HashSet<long> activeZombieIds = new();
    public int ActiveZombieCount => activeZombieIds.Count;
    public bool ActiveZombieCapReached => Settings.MaxActiveZombies > 0 &&
        activeZombieIds.Count >= Settings.MaxActiveZombies;
    void TrackZombie(Entity entity)
    {
        if (entity is Zombie z && z.EntityId > 0 && z.Alive && !z.IsDormant)
            activeZombieIds.Add(z.EntityId);
        Population.OnLoaded(entity);
    }
    public void UntrackActiveZombie(Zombie zombie)
    {
        if (zombie != null) activeZombieIds.Remove(zombie.EntityId);
    }
    void RebuildActiveZombieRegistry()
    {
        activeZombieIds.Clear();
        var loaded = Server.World.LoadedEntities;
        if (loaded == null) return;
        foreach (var zombie in loaded.Values.OfType<Zombie>())
            if (zombie.Alive && !zombie.IsDormant && zombie.EntityId > 0)
                activeZombieIds.Add(zombie.EntityId);
    }
    // Schedule options are centralized so standalone and Last Watch share rules.
    public bool AutomaticSpawnWindowOpen()
    {
        if (Server == null || SpawnsPaused) return false;
        if (!Settings.ZombieSpawnNightOnly && !Settings.ZombieSpawnHordeNightOnly) return true;
        return Rules.CanAutomaticallySpawn(OutbreakDays, Server.World.Calendar.TotalDays, Settings);
    }
    bool AmbientSpawnWindowOpen() => AutomaticSpawnWindowOpen() &&
        (!Settings.AmbientSpawnRequiresNight || Rules.IsNight(Server.World.Calendar.TotalDays, Settings));
    public float DetectionRange(bool alpha) => (Horde ? Settings.HordeDetectionRange : Settings.DetectionRange)
        + (alpha ? Settings.AlphaDetectionRangeBonus : 0);
    // The Last Watch stores dormant populations; loaded zombie entities are
    // managed by the engine without a separately calculated active total.
    // XYZ contains InternalY (dimension offset). These callers carry dimension
    // separately, so terrain/spawn/storage coordinates must use local Y.
    public static Vec3d LocalPosition(EntityPos pos) => new(pos.X, pos.Y, pos.Z);
    // Track bites from regular and Alpha Zombies separately, so the 4/2
    // thresholds do not mix when both types attack the same player.
    readonly Dictionary<(string PlayerUid, bool Alpha), Queue<double>> hits = new();
    // Capture only death coordinates during the death animation. Spawn the Alpha
    // after a successful normal respawn, never while the death camera is active.
    readonly Dictionary<string, (Vec3d Position, int Dimension)> pendingDeathAlphas = new();
    long tickId;
    // Only dormant returns use a fixed cadence: ambient zombies use a vanilla-style probability roll.
    readonly SpawnRetryBackoff spawnRetry = new();
    public string ZoneSpawnStatus(WatchZone zone)
    {
        if (SpawnsPaused) return "all zombie spawns PAUSED; /zombieunpause to resume";
        if (!AutomaticSpawnWindowOpen()) return "automatic schedule closed (night/horde settings)";
        if (ActiveZombieCapReached) return "active population cap reached";
        long now = Server.World.ElapsedMilliseconds;
        string retry = spawnRetry.RemainingSeconds(zone, now) is var seconds && seconds > 0
            ? $"; placement retry in {seconds}s" : "";
        if (WorldDirector is LastWatchZones zones && zones.AmbientSlotsRemaining(zone) > 0)
        {
            if (Settings.AmbientSpawnRequiresNight && !Rules.IsNight(Server.World.Calendar.TotalDays, Settings))
                return "ambient waiting for night; dormant waiting for ambient allowance";
            return $"ambient first: {zones.AmbientSlotsRemaining(zone)} slots remaining; dormant waiting" + retry;
        }
        if (Population?.HasDormantInZone(zone) != true) return "ambient exhausted; dormant pool empty";
        if (WorldDirector?.CanReactivateDormant(zone) != true) return "daily dormant allowance exhausted";
        return "dormant reactivation eligible; light/terrain/claims checked at placement" + retry;
    }
    double dormantWakeTimer;
    // One cheap Survival-player snapshot serves all zombie presence guards.
    readonly List<(double X, double Y, double Z, int Dimension)> presencePlayers = new();
    long nextPresenceSnapshotMs = -1;
    void RefreshPresenceSnapshot()
    {
        if (Server == null) return;
        long now = Server.World.ElapsedMilliseconds;
        if (now < nextPresenceSnapshotMs && now >= nextPresenceSnapshotMs - 10000) return;
        presencePlayers.Clear();
        foreach (var player in Server.World.AllOnlinePlayers)
        {
            var entity = player.Entity;
            if (entity == null || !entity.Alive || !IsTarget(entity)) continue;
            presencePlayers.Add((entity.Pos.X, entity.Pos.Y, entity.Pos.Z, entity.Pos.Dimension));
        }
        nextPresenceSnapshotMs = now + 5000;
    }
    public bool HasNearbySurvivalPlayer(Vec3d zombiePosition, int dimension, double radius)
    {
        if (Server == null || !Settings.ZombieStayActiveNearPlayers) return false;
        RefreshPresenceSnapshot();
        double radiusSquared = radius * radius;
        foreach (var player in presencePlayers)
        {
            if (player.Dimension != dimension || Math.Abs(player.Y - zombiePosition.Y) > 32) continue;
            double dx = player.X - zombiePosition.X, dz = player.Z - zombiePosition.Z;
            if (dx * dx + dz * dz <= radiusSquared) return true;
        }
        return false;
    }
    long lastSpawnDebugLogMs = -60000;
    long hordeUntilMs;
    long nextHordeWaveMs;
    long nextInitialHordeRetryMs;
    long initialHordeRetryUntilMs;
    // When an opening horde wave is short, keep trying for the missing zombies
    // rather than silently waiting three minutes for the next normal wave.
    readonly Dictionary<string, int> pendingOpeningZombies = new();
    long lastNaturalHordeNight = -1;
    bool lastHorde;
    double? dayOrigin;
    public double OutbreakDays
    {
        get
        {
            if (dayOrigin == null)
            {
                var stored = Server.WorldManager.SaveGame.GetData("seventhnight-day-origin");
                dayOrigin = stored?.Length == 8 ? BitConverter.ToDouble(stored, 0) : Math.Floor(Server.World.Calendar.TotalDays);
                Server.WorldManager.SaveGame.StoreData("seventhnight-day-origin", BitConverter.GetBytes(dayOrigin.Value));
            }
            return Server.World.Calendar.TotalDays - dayOrigin.Value;
        }
    }
    // Natural and commanded hordes both last exactly HordeDurationSeconds
    // of elapsed real-world time (while the world is running).
    public bool Horde => Server != null && Server.World.ElapsedMilliseconds < hordeUntilMs;

    public override void Start(ICoreAPI api)
    {
        api.RegisterEntity("SeventhNightZombie", typeof(Zombie));
        api.RegisterEntityBehaviorClass("lastwatchtemporaryswarm", typeof(TemporaryZombieSwarm));
        api.RegisterItemClass("SeventhNightInfectionCure", typeof(ItemInfectionCure));
        CompatibilityHooks.Install(api);
    }
    public override void AssetsFinalize(ICoreAPI api)
    {
        RotTunicVisuals.InheritGrassShirt(api);
    }

    public override void StartClientSide(Vintagestory.API.Client.ICoreClientAPI api)
    {
        // Use the locally configured values for client-side durability display.
        // The server's config remains authoritative for wear and camouflage.
        CompatibilityHooks.ClientApi = api;
        var localSettings = StandaloneConfigStorage.LoadClient(api);
        localSettings.Validate();
        ZombieMaskService.SetMaskDurabilities(api, localSettings.FuneralMask1Durability, localSettings.OtherFuneralMaskDurability);
    }
    public override void StartServerSide(ICoreServerAPI api)
    {
        Server = api;
        SharpenedStakeBlockIds.Clear();
        foreach (var block in api.World.Blocks)
            if (PalisadeTrapRules.Matches(block?.Code)) SharpenedStakeBlockIds.Add(block.Id);
        LooseStickBlockId = api.World.GetBlock(new AssetLocation("game:loosestick-free"))?.Id ?? 0;
        if (SharpenedStakeBlockIds.Count != 4 || LooseStickBlockId <= 0)
            api.Logger.Warning("Zombie stakes: expected four directional stakes and game:loosestick-free; found {0} orientations, remnant ID {1}.", SharpenedStakeBlockIds.Count, LooseStickBlockId);
        SpawnsPaused = api.WorldManager.SaveGame.GetData(SpawnPauseKey)?.FirstOrDefault() == 1;
        RegisterVariantCommands(api);
        var lastNight = api.WorldManager.SaveGame.GetData("seventhnight-last-natural-horde-night");
        if (lastNight?.Length == sizeof(long)) lastNaturalHordeNight = BitConverter.ToInt64(lastNight, 0);
        // Standalone server owners use only the two current configuration files.
        Settings = StandaloneConfigStorage.Load(api);
        // One unified mod: zones and dormant populations are mandatory, never an optional handoff.
        Settings.LastWatchEnabled = true;
        // Merged config values are authoritative; do not run split-mod migrations.
        Settings.UpgradeLastSeenPursuit();
        Settings.Validate();
        Profiler.Enabled = Settings.PerformanceDiagnostics;
        ZombieMaskService.SetMaskDurabilities(api, Settings.FuneralMask1Durability, Settings.OtherFuneralMaskDurability);
        // Both subsystems are now shipped together and share two unified config files.
        StandaloneConfigStorage.Save(api, Settings);
        WorldDirector = api.ModLoader.GetModSystem<LastWatchZones>()
            ?? throw new InvalidOperationException("The Last Watch Zombies: zone controller missing; cannot start.");
        Population = new ZombiePopulation(this);
        api.Logger.Notification($"The Last Watch Zombies: unified zone spawning ready (new ambient limit {Settings.ZombiePopDailyAddLimit}/zone/cycle; dormant {Settings.DormantPopulationCap} seeded globally).");
        api.Event.SaveGameLoaded += LoadPopulation;
        api.Event.GameWorldSave += SavePopulation;
        api.Event.OnEntityLoaded += TrackZombie;
        api.Event.OnEntitySpawn += OnNewZombie;
        tickId = api.Event.RegisterGameTickListener(Tick, 1000);
        api.Event.PlayerDeath += OnDeath;
        api.Event.PlayerDisconnect += OnDisconnect;
        api.Event.PlayerRespawn += OnRespawn;
        // PlayerReady runs after the client has finished loading and selected its character.
        api.Event.PlayerReady += OnPlayerReady;
        // Diagnostic spawn exercises the same terrain, light, claims and
        // zone ambient quota checks as normal spawning.
        // It is not a force-spawn command; success increments the daily count.
        AdminConfiguration.Register(api, this);
        api.ChatCommands.Create("zombieperf")
            .WithDescription("Admin: performance counters since world load; use /zombiediagnostics on, then restart")
            .RequiresPrivilege("controlserver").HandleWith(_ => TextCommandResult.Success(Profiler.Report()));
        api.ChatCommands.Create("snambienttest")
            .WithDescription("Admin: attempt one real ambient zombie spawn near your survival character")
            .RequiresPrivilege("controlserver").HandleWith(args =>
            {
                if (SpawnsPaused) return TextCommandResult.Error("Zombie spawns are paused. Use /zombieunpause first.");
                var entity = args.Caller.Player?.Entity;
                if (entity is not EntityPlayer player || !IsTarget(entity))
                    return TextCommandResult.Error("Ambient spawns require a SURVIVAL player, not Creative/Spectator.");
                if (!AmbientSpawnWindowOpen())
                    return TextCommandResult.Error("Automatic spawning is disabled by the current night-only / horde-night-only settings. Try an eligible night or change /zombieconfig.");
                var lastWatch = AmbientDirector;
                if (lastWatch == null)
                    return TextCommandResult.Error("Zone controller unavailable: ambient spawning is disabled to protect the world. Check the server log.");
                if (lastWatch != null)
                {
                    var playerZone = WatchZone.FromBlock((int)Math.Floor(player.Pos.X),
                        (int)Math.Floor(player.Pos.Z), player.Pos.Dimension);
                    if (!lastWatch.CanAddAmbientPopulationSpawn(playerZone, args.Caller.Player?.PlayerUID))
                        return TextCommandResult.Error("No new ambient slots remain in this Last Watch zone this cycle (or The Last Watch is disabled). Check /lastwatch; dormant reactivation uses a separate allowance.");
                }
                var debug = new SpawnAttemptDiagnostics();
                if (TrySpawnNear(player, Settings.SpawnMinDistance, Settings.SpawnMaxDistance,
                        false, debug, ambientOnly: true))
                    return TextCommandResult.Success("New ambient zombie spawned. Check /lastwatch for this zone's remaining daily allowance.");
                if (lastWatch == null)
                    return TextCommandResult.Error("Zone controller unavailable; see server log.");
                var zone = WatchZone.FromBlock((int)Math.Floor(entity.Pos.X), (int)Math.Floor(entity.Pos.Z), entity.Pos.Dimension);
                return TextCommandResult.Error($"Last Watch ambient spawn failed in zone {zone.X},{zone.Z} " +
                    $"(eligible={lastWatch.CanAmbientPopulationSpawn(zone, args.Caller.Player?.PlayerUID)}). {debug}. " +
                    "Try a safe, dimly lit surface with room to spawn 20-48 blocks away.");
            });
        api.ChatCommands.Create("snambientreset")
            .WithDescription("Admin: reset only the current zone's NEW ambient spawn count for testing")
            .RequiresPrivilege("controlserver")
            .HandleWith(args =>
            {
                var player = args.Caller.Player;
                if (player?.Entity == null || WorldDirector is not LastWatchZones zones)
                    return TextCommandResult.Error("Join the game and verify zone initialization first.");
                var pos = player.Entity.Pos;
                var zone = WatchZone.FromBlock((int)Math.Floor(pos.X), (int)Math.Floor(pos.Z), pos.Dimension);
                zones.ResetCurrentZoneAmbientQuota(zone);
                return TextCommandResult.Success($"Reset NEW ambient quota for zone {zone.X},{zone.Z}. Dormant population and settlement data unchanged.");
            });
        api.ChatCommands.Create("snspawnstatus")
            .WithDescription("Admin: explain ambient spawning eligibility and remaining quotas without spawning a zombie")
            .RequiresPrivilege("controlserver").HandleWith(args =>
            {
                var player = args.Caller.Player;
                var entity = player?.Entity;
                if (entity == null) return TextCommandResult.Error("Join the world as a Survival player first.");
                bool eligible = entity.Alive && IsTarget(entity) && entity.Pos.Dimension == 0;
                bool schedule = AutomaticSpawnWindowOpen();
                bool ambientWindow = AmbientSpawnWindowOpen();
                var director = AmbientDirector;
                string allowance;
                if (director != null)
                {
                    var zone = WatchZone.FromBlock((int)Math.Floor(entity.Pos.X),
                        (int)Math.Floor(entity.Pos.Z), entity.Pos.Dimension);
                    allowance = $"Last Watch zone {zone.X},{zone.Z}: " +
                        (director.CanAddAmbientPopulationSpawn(zone, player.PlayerUID)
                            ? "new-ambient allowance available" : "NO new-ambient allowance (see /lastwatch)");
                }
                else allowance = "zone controller unavailable";
                double days = Server.World.Calendar.TotalDays;
                double hour = (days - Math.Floor(days)) * 24;
                return TextCommandResult.Success(
                    $"Spawns paused={SpawnsPaused}; Survival eligible={eligible}; clock={hour:0.00}h; " +
                    $"schedule open={schedule}; ambient night open={ambientWindow}; " +
                    $"active loaded zombies={activeZombieIds.Count}/{Settings.MaxActiveZombies} " +
                    $"(0 cap means unlimited); {allowance}; light <= {Settings.SpawnMaxLightLevel}; " +
                    $"spawn ring {Settings.SpawnMinDistance}-{Settings.SpawnMaxDistance} blocks; " +
                    $"chance per second={Settings.AmbientSpawnChancePerSecond:0.###}. " +
                    "Use /snambienttest to see terrain/light/claim failure counts.");
            });
        api.ChatCommands.Create("snspawn").WithDescription("Admin: spawn one test zombie near you")
            .RequiresPrivilege("controlserver").HandleWith(args =>
            {
                if (SpawnsPaused) return TextCommandResult.Error("Zombie spawns are paused. Use /zombieunpause first.");
                var origin = args.Caller.Player?.Entity.Pos ?? api.World.DefaultSpawnPosition;
                var point = LocalPosition(origin).Add(3, 1, 0);
                if (api.World.BlockAccessor.GetChunkAtBlockPos(new BlockPos((int)Math.Floor(point.X), (int)Math.Floor(point.Y), (int)Math.Floor(point.Z), origin.Dimension)) == null)
                    return TextCommandResult.Error("Destination chunk is not loaded.");
                return Spawn(point, origin.Dimension) ? TextCommandResult.Success("Spawned a Seventh Night Zombies mob.") : TextCommandResult.Error("Spawn failed; check server log.");
            });
        api.ChatCommands.Create("zombiehordestart").WithDescription("Admin: start a timed zombie horde now")
            .RequiresPrivilege("controlserver").HandleWith(args => StartManualHorde());
        api.ChatCommands.Create("zombiehorde")
            .WithDescription("Show the next horde, its schedule, and active zombie count")
            .RequiresPrivilege("chat")
            .HandleWith(_ => TextCommandResult.Success(BuildHordeStatus()));
        api.ChatCommands.Create("zombieinfection")
            .WithDescription("Show your infection and Mycobiotic Cure progress")
            .RequiresPrivilege("chat")
            .HandleWith(args => args.Caller.Player?.Entity is EntityPlayer player
                ? TextCommandResult.Success(BuildInfectionStatus(player))
                : TextCommandResult.Error("Run /zombieinfection as a player."));
    }

    string BuildHordeStatus()
    {
        double outbreakDays = OutbreakDays;
        double nextNight = Rules.NextNaturalHordeStartDay(outbreakDays, Settings);
        long nextNightNumber = (long)Math.Floor(nextNight) + 1;
        // Use IN-GAME days/hours for the schedule, not wall-clock time.
        string until = Rules.FormatGameTimeUntil(nextNight - outbreakDays);
        double hour = Math.Max(0, Settings.HordeStartHour - Settings.HordeMinutesBeforeStartHour / 60.0);
        int hh = (int)Math.Floor(hour);
        int mm = (int)Math.Round((hour - hh) * 60);
        if (mm == 60) { hh++; mm = 0; }
        string cap = Settings.MaxActiveZombies == 0 ? "unlimited" : Settings.MaxActiveZombies.ToString();
        return $"Next horde: night {nextNightNumber} at {hh:00}:{mm:00}, in {until} (in-game). Every {Settings.HordeEveryDays} in-game days. Active zombies: {ActiveZombieCount}/{cap}.";
    }

    string BuildInfectionStatus(EntityPlayer entity)
    {
        if (!entity.WatchedAttributes.HasAttribute(InfectionKey))
            return "Infection status: healthy (not infected). Cure doses needed: 0.";
        int taken = entity.WatchedAttributes.GetInt(CureDosesKey);
        double now = Server.World.Calendar.TotalDays;
        double infectedAt = entity.WatchedAttributes.GetDouble(InfectionStartDayKey, now);
        double elapsed = Math.Max(0, now - infectedAt);
        double lastDose = entity.WatchedAttributes.GetDouble(CureLastDoseDayKey, now);
        double remaining = taken == 0 ? 0 : Math.Max(0, Settings.CureDoseIntervalDays - (now - lastDose));
        string nextDose = remaining > 0 ? $"next dose in {Rules.FormatGameTimeUntil(remaining)}" : "next dose ready";
        double deadline = Rules.FatalDeadlineDay(infectedAt, taken, FirstDoseDay(entity, taken, infectedAt), Settings);
        string fatal = Settings.InfectionFatalEnabled
            ? $"fatal in {Rules.FormatGameTimeUntil(Math.Max(0, deadline - now))}"
            : "fatality disabled";
        if (taken == 0)
            nextDose = Rules.CanStartCure(infectedAt, now, Settings)
                ? $"start the cure within {Rules.FormatGameTimeUntil(Math.Max(0, infectedAt + Settings.CureStartWindowDays - now))}"
                : "too late to start the cure";
        return $"Infection status: INFECTED for {Rules.FormatGameTimeUntil(elapsed)} (in-game). " +
            $"Poison: {Rules.PoisonDamage(Settings):0.##} HP every {Rules.PoisonIntervalSeconds(taken, Settings):0.##} real seconds. " +
            $"Cure doses: {taken}/{Settings.CureDosesRequired}; {nextDose}. {fatal}.";
    }

    void OnPlayerReady(IServerPlayer player)
    {
        if (Server == null || player?.Entity == null) return;
        player.SendMessage(0,
            $"<font color=\"#FF4040\"><strong>[Seventh Night Zombies]</strong> {BuildHordeStatus()}</font>",
            EnumChatType.Notification);
    }
    // Zombie targeting is separate from player-centric horde spawning.
    // Only Survival-mode players are prey; Creative (2), Spectator (3), and
    // Guest players never become targets even if somebody whitelists "player".
    public static bool IsTarget(Entity e) => e is EntityPlayer p && e.Alive && p.Player != null &&
        p.Player.WorldData?.CurrentGameMode == EnumGameMode.Survival;

    public static bool IsMechanicalOrRustCreature(Entity e)
    {
        if (e == null) return false;
        string code = (e.Code?.Path ?? string.Empty).ToLowerInvariant();
        string kind = e.GetType().Name.ToLowerInvariant();
        string[] forbidden = ["locust", "bell", "drifter", "shiver", "bowtorn", "rust", "mechanical", "clockwork"];
        return forbidden.Any(word => code.Contains(word) || kind.Contains(word));
    }

    // Dynamic code-based inclusion/exclusion rules apply immediately to
    // target acquisition AND to an existing target's continued pursuit/attack.
    public bool CanZombieTarget(Entity e)
    {
        if (e == null || !e.Alive || e is Zombie || e.Code?.ToString() == "seventhnightzombies:zombiebees") return false;
        if (e is not EntityAgent) return false;
        if (IsMechanicalOrRustCreature(e)) return false;
        // Player game modes are mandatory protection and cannot be bypassed by
        // the whitelist. Also prevents the v1.1.8 EntityAgent fallback bug.
        if (e is EntityPlayer player)
        {
            if (!IsTarget(e) || ZombieMaskService.IsProtected(player) || RotCoveredTunicService.IsProtected(player)) return false;
        }

        string code = e.Code?.ToString() ?? string.Empty;
        bool isPlayerEntity = e is EntityPlayer;
        bool humanoid = IsHumanoidPrey(e);
        bool vanilla = e.Code?.Domain == "game";
        bool defaultsToPrey = isPlayerEntity || humanoid || !vanilla || !Settings.IgnoreVanillaAnimals;
        // A whitelist can opt specific vanilla animals in. Blacklists win,
        // including for players and humanoid NPCs. Always respect hard safety.
        return Rules.TargetCodeAllowed(code, defaultsToPrey, Settings);
    }

    public static bool IsHumanoidPrey(Entity e)
    {
        if (e == null) return false;
        if (e is EntityPlayer) return true;
        string code = (e.Code?.Path ?? string.Empty).ToLowerInvariant();
        string kind = e.GetType().Name.ToLowerInvariant();
        string shape = (e.Properties?.Client?.Shape?.Base?.Path ?? string.Empty).ToLowerInvariant();
        return code.Contains("trader") || code.Contains("villager") || code.Contains("npc") ||
               code.Contains("humanoid") || code.Contains("seraph") ||
               kind.Contains("trader") || kind.Contains("villager") || kind.Contains("npc") ||
               kind.Contains("humanoid") || kind.Contains("seraph") ||
               shape.Contains("trader") || shape.Contains("villager") ||
               shape.Contains("humanoid") || shape.Contains("seraph") || shape.Contains("player");
    }
    public void Hit(EntityPlayer p, bool alpha)
    {
        if (p.WatchedAttributes.HasAttribute(InfectionKey) || !p.Alive || p.Player == null) return;
        string uid = p.Player.PlayerUID;
        var key = (uid, alpha);
        int required = Rules.GetInfectionHitThreshold(alpha, Settings);
        if (!hits.TryGetValue(key, out var recent)) hits[key] = recent = new();
        if (Rules.RecordHit(recent, Server.World.ElapsedMilliseconds / 1000.0, required, Settings.HitWindowSeconds))
        {
            p.WatchedAttributes.SetDouble(InfectionKey, 0); // infection marker / legacy checkpoint
            p.Attributes.SetDouble(InfectionServerClockKey, 0);
            p.WatchedAttributes.SetDouble(InfectionStartDayKey, Server.World.Calendar.TotalDays);
            p.WatchedAttributes.SetInt(CureDosesKey, 0);
            p.WatchedAttributes.RemoveAttribute(CureLastDoseDayKey);
            p.WatchedAttributes.RemoveAttribute(CureFirstDoseDayKey);
            p.WatchedAttributes.RemoveAttribute(LegacyPoulticeKey);
            ClearHits(uid);
            SyncInfectionDisplay(p);
            Tell(p, $"The wound burns. Take your first Mycobiotic Cure dose within {Settings.CureStartWindowDays:0.##} in-game day(s) or the infection can no longer be stopped; it is fatal after {Settings.InfectionFatalDays:0.##} in-game days. A full course is {Settings.CureDosesRequired} doses, one every {Settings.CureDoseIntervalDays:0.##} in-game day(s).");
        }
        else Tell(p, $"{(alpha ? "Alpha Zombie" : "Zombie")} wound: {recent.Count}/{required} hits within {Settings.HitWindowSeconds:0} seconds.");
    }
    // Called ONLY on the server by the cure item's right-click. Return true to
    // consume one item. Invalid/early uses do not consume supplies.
    public bool TryTakeCureDose(EntityPlayer player)
    {
        if (Server == null || player?.Player == null || !player.Alive) return false;
        if (!player.WatchedAttributes.HasAttribute(InfectionKey))
        {
            Tell(player, "You are not infected; save this cure for someone who needs it.");
            return false;
        }
        double now = Server.World.Calendar.TotalDays;
        double infectedAt = player.WatchedAttributes.GetDouble(InfectionStartDayKey, now);
        int doses = player.WatchedAttributes.GetInt(CureDosesKey);
        if (Rules.IsFatalInfection(infectedAt, now, doses, FirstDoseDay(player, doses, infectedAt), Settings))
        {
            KillFromInfection(player);
            return false;
        }
        if (doses <= 0 && !Rules.CanStartCure(infectedAt, now, Settings))
        {
            Tell(player, $"Too late: the infection took hold more than {Settings.CureStartWindowDays:0.##} in-game day(s) ago. The Mycobiotic Cure can no longer stop it.");
            return false;
        }
        if (doses > 0)
        {
            double last = player.WatchedAttributes.GetDouble(CureLastDoseDayKey, -1);
            double remaining = Settings.CureDoseIntervalDays - (now - last);
            if (!Rules.MayTakeCureDose(doses, last, now, Settings))
            {
                Tell(player, $"Too soon for another dose. Wait {Rules.FormatGameTimeUntil(remaining)} (in-game time).");
                return false;
            }
        }
        doses++;
        // The vanilla health behavior clamps healing to maximum health. Only
        // accepted doses reach this point, so cooldown spam cannot grant HP.
        player.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Internal,
            Type = EnumDamageType.Heal }, 20f);
        if (doses >= Settings.CureDosesRequired)
        {
            player.WatchedAttributes.RemoveAttribute(InfectionKey);
            player.Attributes.RemoveAttribute(InfectionServerClockKey);
            player.WatchedAttributes.RemoveAttribute(InfectionStartDayKey);
            player.WatchedAttributes.RemoveAttribute(CureDosesKey);
            player.WatchedAttributes.RemoveAttribute(CureLastDoseDayKey);
            player.WatchedAttributes.RemoveAttribute(CureFirstDoseDayKey);
            ClearInfectionDisplay(player);
            ClearHits(player.Player.PlayerUID);
            Tell(player, "The Mycobiotic Cure has worked. The infection has been eliminated!");
        }
        else
        {
            player.WatchedAttributes.SetInt(CureDosesKey, doses);
            player.WatchedAttributes.SetDouble(CureLastDoseDayKey, now);
            if (doses == 1)
            {
                player.WatchedAttributes.SetDouble(CureFirstDoseDayKey, now);
                // Make the first treatment grant a full longer interval immediately.
                player.Attributes.SetDouble(InfectionServerClockKey, 0);
                player.WatchedAttributes.SetDouble(InfectionKey, 0);
                Tell(player, $"First Mycobiotic Cure dose received. Poison now strikes every {Rules.PoisonIntervalSeconds(doses, Settings):0} seconds.");
            }
            SyncInfectionDisplay(player);
            Tell(player, $"Mycobiotic Cure dose {doses}/{Settings.CureDosesRequired} applied. Take the next dose after {Settings.CureDoseIntervalDays:0.##} in-game day(s).");
        }
        // ONLY after an accepted dose, while the player is alive. This is the
        // same watched-attribute visual channel as vanilla psychedelic mushrooms.
        // Never touch player controls, movement, camera transforms or death flow.
        if (Settings.CurePsychedelicEnabled) ApplyMycobioticSideEffect(player, doses);
        return true;
    }

    static readonly float[] MushroomPsychedelicStrengths = [0.4f, 0.6f, 0.8f, 1.0f, 1.4f, 2.0f];

    void ApplyMycobioticSideEffect(EntityPlayer player, int acceptedDose)
    {
        if (player == null || !player.Alive || Server == null) return;
        try
        {
            // Map the configurable treatment length onto our ONE ordered list.
            // A normal six-dose course visits every strength; shorter or longer
            // courses still begin weak and end on the strongest mushroom effect.
            int stages = MushroomPsychedelicStrengths.Length;
            int configuredDoses = Math.Max(1, Settings.CureDosesRequired);
            int index = configuredDoses == 1 ? stages - 1 :
                Math.Clamp((int)Math.Round((acceptedDose - 1.0) * (stages - 1) / (configuredDoses - 1)), 0, stages - 1);
            float strength = MushroomPsychedelicStrengths[index];
            player.WatchedAttributes.SetFloat("psychedelic", strength);
            Tell(player, $"Mycobiotic Cure dose {acceptedDose}: the mushroom vision intensifies ({strength:0.0}/2.0).");
        }
        catch (Exception ex)
        {
            // The cure is already successfully administered. A visual-only
            // failure must never interrupt treatment, item consumption or respawning.
            Server.Logger.Warning("Seventh Night Zombies: Mycobiotic visual effect failed: " + ex.Message);
        }
    }

    // Server-controlled irreversible turning when the configured deadline passes.
    void KillFromInfection(EntityPlayer player)
    {
        if (!player.Alive || !player.WatchedAttributes.HasAttribute(InfectionKey)) return;
        Tell(player, "The infection has overcome you. You have turned.");
        var health = player.GetBehavior<EntityBehaviorHealth>();
        float lethalDamage = Math.Max(1000f, (health?.Health ?? 0f) + 100f);
        player.ReceiveDamage(new DamageSource
        {
            Source = EnumDamageSource.Internal,
            Type = EnumDamageType.Poison,
            IgnoreInvFrames = true
        }, lethalDamage);
    }
    // Publish the server's infection deadlines for the client Traits tab.
    internal void SyncInfectionDisplay(EntityPlayer player)
    {
        var w = player?.WatchedAttributes;
        if (w == null || Server == null) return;
        if (!w.HasAttribute(InfectionKey)) { ClearInfectionDisplay(player); return; }
        double now = Server.World.Calendar.TotalDays;
        double infectedAt = w.GetDouble(InfectionStartDayKey, now);
        int doses = w.GetInt(CureDosesKey);
        bool fatalOn = Settings.InfectionFatalEnabled;
        SetIfChanged(w, InfectionTrait.FatalDayKey, fatalOn
            ? Rules.FatalDeadlineDay(infectedAt, doses, FirstDoseDay(player, doses, infectedAt), Settings) : -1);
        SetIfChanged(w, InfectionTrait.CureStartByDayKey, fatalOn ? infectedAt + Settings.CureStartWindowDays : -1);
        SetIfChanged(w, InfectionTrait.NextDoseDayKey, doses > 0
            ? w.GetDouble(CureLastDoseDayKey, now) + Settings.CureDoseIntervalDays : -1);
        if (w.GetInt(InfectionTrait.DosesRequiredKey, -1) != Settings.CureDosesRequired)
            w.SetInt(InfectionTrait.DosesRequiredKey, Settings.CureDosesRequired);
    }
    static void SetIfChanged(Vintagestory.API.Datastructures.ITreeAttribute w, string key, double value)
    {
        if (!w.HasAttribute(key) || Math.Abs(w.GetDouble(key) - value) > 1e-9) w.SetDouble(key, value);
    }
    internal static void ClearInfectionDisplay(EntityPlayer player)
    {
        var w = player?.WatchedAttributes;
        if (w == null) return;
        foreach (var key in new[] { InfectionTrait.FatalDayKey, InfectionTrait.CureStartByDayKey,
                     InfectionTrait.NextDoseDayKey, InfectionTrait.DosesRequiredKey })
            if (w.HasAttribute(key)) w.RemoveAttribute(key);
    }
    // Day of the first accepted dose. Saves from before 2.0.10 have no record:
    // estimate it from the last dose and the regular interval, clamped so it
    // never predates infection.
    double FirstDoseDay(EntityPlayer player, int doses, double infectedAt)
    {
        if (doses <= 0) return infectedAt;
        if (player.WatchedAttributes.HasAttribute(CureFirstDoseDayKey))
            return player.WatchedAttributes.GetDouble(CureFirstDoseDayKey);
        double last = player.WatchedAttributes.GetDouble(CureLastDoseDayKey, infectedAt);
        return Math.Max(infectedAt, last - (doses - 1) * Settings.CureDoseIntervalDays);
    }
    void ClearHits(string uid)
    {
        hits.Remove((uid, false));
        hits.Remove((uid, true));
    }
    void Tell(EntityPlayer p, string message) => (p.Player as IServerPlayer)?.SendMessage(0, message, EnumChatType.Notification);
    // The seventh night's ordinal is stable throughout the afternoon;
    // Rules.IsHorde() only turns true at night and is too late for this event.
    public bool TryStartScheduledHordeBeforeMigration(bool atMigrationRoll)
    {
        if (Server == null || SpawnsPaused || Horde) return false;
        int frequency = Math.Max(1, Settings.HordeEveryDays);
        double days = OutbreakDays;
        long night = (long)Math.Floor(days) + 1;
        if (night <= 0 || night % frequency != 0 || night == lastNaturalHordeNight) return false;
        double calendar = Server.World.Calendar.TotalDays;
        double hour = (calendar - Math.Floor(calendar)) * 24;
        double leadHour = Math.Max(0, Settings.HordeStartHour -
            Math.Clamp(Settings.HordeMinutesBeforeStartHour, 0, 120) / 60.0);
        // Outside the just-before-sunset window, only the roll's fallback
        // can start an event (e.g. after a /time jump over the sunset).
        if (hour < leadHour || (!atMigrationRoll && hour >= Settings.HordeStartHour)) return false;
        if (!Server.World.AllOnlinePlayers.Any(p => p.Entity != null &&
            IsTarget(p.Entity) && p.Entity.Pos.Dimension == 0)) return false;
        lastNaturalHordeNight = night;
        Server.WorldManager.SaveGame.StoreData("seventhnight-last-natural-horde-night", BitConverter.GetBytes(night));
        BeginHorde(natural: true);
        return true;
    }

    void Tick(float dt)
    {
        TickStakeTimers(dt);
        if (!populationLoaded) return;
        using var tickTiming = Profiler.Measure("system.tick");
        // Corpse simulation uses registered zombies, not all loaded entities.
        foreach (var corpse in Population.DeadCorpses())
            corpse.TickCorpse(dt);
        // Trigger the seventh-night horde shortly BEFORE the sunset migration.
        // LastWatchZones also calls this immediately before its roll, so a
        // server tick order or calendar time jump cannot put migration first.
        TryStartScheduledHordeBeforeMigration(atMigrationRoll: false);
        bool horde = Horde;
        // Announce the end exactly once. Existing spawned zombies persist.
        if (!horde && lastHorde)
        {
            foreach (var p in Server.World.AllOnlinePlayers.OfType<IServerPlayer>())
                p.SendMessage(0, "The horde has ended, but its dead remain.", EnumChatType.Notification);
            lastHorde = false;
            pendingOpeningZombies.Clear();
        }
        // The scheduled calendar event and admin command both run BeginHorde.
        // If the first wave was blocked by terrain/claims/chunk constraints,
        // retry missing opening spawns promptly while the horde is still active.
        long nowMs = Server.World.ElapsedMilliseconds;
        if (!SpawnsPaused && horde && pendingOpeningZombies.Count > 0 && nowMs >= nextInitialHordeRetryMs)
        {
            if (nowMs < initialHordeRetryUntilMs)
            {
                RetryOpeningHordeWave();
                nextInitialHordeRetryMs = nowMs + (long)(Settings.HordeInitialRetrySeconds * 1000);
            }
            else
            {
                Server.Logger.Warning($"Seventh Night Zombies: initial horde retries expired; {pendingOpeningZombies.Count} player(s) still lack their minimum opening wave. Check claims, loaded chunks and the active zombie cap.");
                pendingOpeningZombies.Clear();
            }
        }
        if (!SpawnsPaused && horde && Server.World.ElapsedMilliseconds >= nextHordeWaveMs)
        {
            SpawnHordeWave();
            // At most one wave per tick even if the server stalled for minutes.
            nextHordeWaveMs = Server.World.ElapsedMilliseconds + (long)(Settings.HordeWaveIntervalSeconds * 1000);
        }
        foreach (var player in Server.World.AllOnlinePlayers)
        {
            var e = player.Entity;
            // Do not touch player watched attributes, inventories, durability,
            // damage, or any other client-synced state while the death animation
            // runs. This also prevents tunic/mask ticking on a dead body.
            if (e == null || !e.Alive) continue;
            TickRotPoison(e, dt);
            if (!e.Alive) continue;
            // Client and server use this synchronized state to suppress only hurt vocalizations.
            bool quiet = HasEyeBandage(player);
            if (e.WatchedAttributes.GetBool(QuietBandageKey) != quiet)
                e.WatchedAttributes.SetBool(QuietBandageKey, quiet);
            masks.TickPlayer(player, dt, Settings);
            tunics.TickPlayer(player, dt, Settings);
            if (!IsTarget(e) || !e.WatchedAttributes.HasAttribute(InfectionKey)) continue;
            // The clock uses *in-game* calendar days, including time passed while sleeping.
            // Migrate saves made before InfectionStartDayKey existed, without backdating.
            if (!e.WatchedAttributes.HasAttribute(InfectionStartDayKey))
                e.WatchedAttributes.SetDouble(InfectionStartDayKey, Server.World.Calendar.TotalDays);
            double infectedAt = e.WatchedAttributes.GetDouble(InfectionStartDayKey);
            int dosesTaken = e.WatchedAttributes.GetInt(CureDosesKey);
            // Keeps the Traits-tab deadlines current (old saves, config changes).
            // Writes only when a value actually changes.
            SyncInfectionDisplay((EntityPlayer)e);
            if (Rules.IsFatalInfection(infectedAt, Server.World.Calendar.TotalDays, dosesTaken,
                    FirstDoseDay((EntityPlayer)e, dosesTaken, infectedAt), Settings))
            {
                KillFromInfection((EntityPlayer)e);
                continue;
            }
            // Clear obsolete poultice state; it no longer has any effect.
            if (e.WatchedAttributes.HasAttribute(LegacyPoulticeKey))
                e.WatchedAttributes.RemoveAttribute(LegacyPoulticeKey);
            int doses = e.WatchedAttributes.GetInt(CureDosesKey);
            double interval = Rules.PoisonIntervalSeconds(doses, Settings);
            // Entity.Attributes are saved with the world but are not synchronized
            // to clients. This keeps the one-second poison clock from forcing
            // repetitive player model/animation and movement attribute updates.
            // Old worlds fall back to their last watched timer checkpoint once.
            double sinceTick = (e.Attributes.HasAttribute(InfectionServerClockKey)
                ? e.Attributes.GetDouble(InfectionServerClockKey)
                : e.WatchedAttributes.GetDouble(InfectionKey)) + dt;
            if (!double.IsFinite(sinceTick) || sinceTick < 0) sinceTick = 0;
            // Preserve fractional real-time seconds. Cap catch-up during server stalls.
            int due = Math.Min(10, (int)(sinceTick / interval));
            double nextClock = due == 10 ? 0 : sinceTick - due * interval;
            e.Attributes.SetDouble(InfectionServerClockKey, nextClock);
            // Update the legacy watched clock only when poison actually ticks
            // (10 or 20 seconds by default), never every server heartbeat.
            if (due > 0) e.WatchedAttributes.SetDouble(InfectionKey, nextClock);
            for (int i = 0; i < due && e.Alive; i++)
            {
                float poison = Rules.PoisonDamage(Settings);
                if (poison > 0)
                {
                    e.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Internal, Type = EnumDamageType.Poison, IgnoreInvFrames = true }, poison);
                }
            }
        }
        // Unified zone spawning: at most one successful NEW ambient zombie per
        // occupied zone per opportunity, irrespective of the number of players.
        // Successful spawns spend the zone's shared daily budget.  Dormant
        // reactivations below use a separate queue and never consume this quota.
        if (!AutomaticSpawnWindowOpen()) return;
        var players = Server.World.AllOnlinePlayers
            .Where(p => p.Entity != null && IsTarget(p.Entity) && p.Entity.Pos.Dimension == 0)
            .ToArray();
        var occupied = players.GroupBy(p => WatchZone.FromBlock((int)Math.Floor(p.Entity.Pos.X),
            (int)Math.Floor(p.Entity.Pos.Z), p.Entity.Pos.Dimension)).ToArray();
        spawnRetry.Retain(occupied.Select(g => g.Key));
        long spawnNow = Server.World.ElapsedMilliseconds;
        var director = WorldDirector;
        bool canAddAmbientNow = !Settings.AmbientSpawnRequiresNight ||
            Rules.IsNight(Server.World.Calendar.TotalDays, Settings);
        if (canAddAmbientNow && !ActiveZombieCapReached && director != null)
        {
            double chance = 1 - Math.Pow(1 - Settings.AmbientSpawnChancePerSecond,
                Math.Clamp(dt, 0, 1.5));
            var groups = players.GroupBy(p => WatchZone.FromBlock(
                (int)Math.Floor(p.Entity.Pos.X), (int)Math.Floor(p.Entity.Pos.Z),
                p.Entity.Pos.Dimension)).OrderBy(_ => Server.World.Rand.Next());
            foreach (var group in groups)
            {
                if (ActiveZombieCapReached) break;
                if (!director.CanAddAmbientPopulationSpawn(group.Key) || !spawnRetry.Ready(group.Key, spawnNow)) continue;
                if (Server.World.Rand.NextDouble() >= chance) continue;
                // Sample a player's neighborhood inside this SAME zone.
                // Do not multiply the zone's opportunities by player count.
                var playersInZone = group.ToArray();
                var player = playersInZone[Server.World.Rand.Next(playersInZone.Length)];
                var diagnostics = Settings.SpawnDebugLogging ? new SpawnAttemptDiagnostics() : null;
                bool spawned = TrySpawnNear(player.Entity, Settings.SpawnMinDistance,
                    Settings.SpawnMaxDistance, false, diagnostics, ambientOnly: true);
                spawnRetry.Record(group.Key, spawnNow, spawned);
                if (!spawned && Settings.SpawnDebugLogging &&
                    Server.World.ElapsedMilliseconds - lastSpawnDebugLogMs >= 60000)
                {
                    lastSpawnDebugLogMs = Server.World.ElapsedMilliseconds;
                    Server.Logger.Notification($"The Last Watch Zombies: zone {group.Key} new ambient spawn rejected for {player.PlayerName}: {diagnostics}.");
                }
            }
        }

        // Dormant population waits for the ambient quota to be exhausted,
        // then uses small bounded batches. Unsuccessful wakes remain stored for the next
        // pass. All dormant zombies can eventually return while players occupy
        // the zone and safe positions / free active slots exist.
        if (director == null || Population == null || ActiveZombieCapReached) return;
        dormantWakeTimer += Math.Clamp(dt, 0, 1.5);
        if (dormantWakeTimer < Settings.DormantWakeIntervalSeconds) return;
        dormantWakeTimer = 0;
        int globalBudget = Settings.DormantMaxTotalWakesPerPass;
        foreach (var group in players.GroupBy(p => WatchZone.FromBlock(
            (int)Math.Floor(p.Entity.Pos.X), (int)Math.Floor(p.Entity.Pos.Z),
            p.Entity.Pos.Dimension)).OrderBy(_ => Server.World.Rand.Next()))
        {
            if (globalBudget <= 0 || ActiveZombieCapReached) break;
            if (!Population.HasDormantInZone(group.Key) || !director.CanReactivateDormant(group.Key) ||
                !spawnRetry.Ready(group.Key, spawnNow)) continue;
            int zoneBudget = Math.Min(Settings.DormantWakeBatchSize, globalBudget);
            foreach (var player in group)
            {
                while (zoneBudget > 0 && globalBudget > 0 && !ActiveZombieCapReached &&
                    Population.HasDormantInZone(group.Key) && director.CanReactivateDormant(group.Key))
                {
                    // Spend work budget before searching, including failed placement.
                    zoneBudget--;
                    globalBudget--;
                    bool woke = TrySpawnNear(player.Entity, Settings.SpawnMinDistance,
                        Settings.SpawnMaxDistance, false, null, dormantOnly: true);
                    spawnRetry.Record(group.Key, spawnNow, woke);
                    if (!woke) break;
                }
                if (zoneBudget == 0 || globalBudget == 0 || ActiveZombieCapReached ||
                    !spawnRetry.Ready(group.Key, spawnNow)) break;
            }
        }
    }
    bool TrySpawnNear(EntityPlayer player, SpawnAttemptDiagnostics diagnostics = null) =>
        TrySpawnNear(player, Settings.SpawnMinDistance, Settings.SpawnMaxDistance, false, diagnostics);

    // Diagnostic counters are server-only, never affect entity physics or spawning rules.
    sealed class SpawnAttemptDiagnostics
    {
        public int Zone, Chunk, Surface, Light, Claim, Distance, CapOrEntity;
        public override string ToString() =>
            $"Last Watch quota/zone {Zone}, unloaded chunks {Chunk}, invalid ground/clearance {Surface}, " +
            $"light {Light}, claims {Claim}, nearby players {Distance}, entity/cap {CapOrEntity}";
    }
    // Alpha Zombies are created directly when a player dies, bypassing normal spawn tests.
    // The horde flag overrides block light; claims may be permitted for hordes, but
    // wall/door destruction still has its own claim and reinforcement protections.
    bool TrySpawnNear(EntityPlayer player, int minDistance, int maxDistance, bool isHordeSpawn = false, SpawnAttemptDiagnostics diagnostics = null,
        bool ambientOnly = false, bool dormantOnly = false)
    {
        if (SpawnsPaused) return false;
        if (ambientOnly && dormantOnly) return false;
        if (ActiveZombieCapReached || (!isHordeSpawn && !AutomaticSpawnWindowOpen()))
        { if (diagnostics != null) diagnostics.CapOrEntity++; return false; }
        using var timing = Profiler.Measure(isHordeSpawn ? "spawn.horde" : "spawn.ambient");
        var w = Server.World;
        int dimension = player.Pos.Dimension;
        bool ignoreLight = isHordeSpawn && Settings.HordeSpawnIgnoreLightLevel;
        // Horde waves bypass the zone's new-ambient limit.
        var lastWatch = isHordeSpawn ? null : AmbientDirector;
        var playerZone = lastWatch != null
            ? WatchZone.FromBlock((int)Math.Floor(player.Pos.X), (int)Math.Floor(player.Pos.Z), dimension)
            : default;
        string spawningPlayerUid = player.Player?.PlayerUID;
        if (!isHordeSpawn && lastWatch == null)
        { if (diagnostics != null) diagnostics.Zone++; return false; }
        if (!isHordeSpawn && lastWatch != null &&
            ((!dormantOnly && !lastWatch.CanAddAmbientPopulationSpawn(playerZone, spawningPlayerUid)) ||
             (dormantOnly && (Population == null || !Population.HasDormantInZone(playerZone) ||
                                   !lastWatch.CanReactivateDormant(playerZone)))))
        { if (diagnostics != null) diagnostics.Zone++; return false; }
        if (dormantOnly && lastWatch == null)
        { if (diagnostics != null) diagnostics.Zone++; return false; }
        // Choose candidate locations inside the occupied zone, near a player.
        // Never consume a zone allowance until entity creation succeeds.
        // Keep bounded attempts, player spacing, claims, and lighting tests.
        double goldenAngle = 2.399963229728653;
        double startAngle = w.Rand.NextDouble() * Math.PI * 2;
        for (int attempt = 0; attempt < Settings.SpawnPositionAttempts; attempt++)
        {
            int x, z;
            if (lastWatch != null)
            {
                // 75% of attempts favor the near, usually loaded area.
                // Clipping before sampling prevents the far edge of the ring
                // from landing in a neighboring/unoccupied Last Watch zone.
                int sampleRadius = attempt < Settings.SpawnPositionAttempts * 3 / 4
                    ? Math.Min(maxDistance, minDistance + 20) : maxDistance;
                int size = WatchZone.ChunksPerSide * GlobalConstants.ChunkSize;
                int minX = Math.Max((int)Math.Floor(player.Pos.X - sampleRadius), playerZone.X * size);
                int maxX = Math.Min((int)Math.Floor(player.Pos.X + sampleRadius), (playerZone.X + 1) * size - 1);
                int minZ = Math.Max((int)Math.Floor(player.Pos.Z - sampleRadius), playerZone.Z * size);
                int maxZ = Math.Min((int)Math.Floor(player.Pos.Z + sampleRadius), (playerZone.Z + 1) * size - 1);
                if (minX > maxX || minZ > maxZ)
                { if (diagnostics != null) diagnostics.Zone++; continue; }
                x = w.Rand.Next(minX, maxX + 1);
                z = w.Rand.Next(minZ, maxZ + 1);
            }
            else
            {
                // Restore the older near-first golden-angle coverage for
                // standalone ambient and deliberate horde waves.
                double progress = (double)attempt / Math.Max(1, Settings.SpawnPositionAttempts - 1);
                double radius = minDistance + (maxDistance - minDistance) * progress * progress;
                double angle = startAngle + attempt * goldenAngle + (w.Rand.NextDouble() - .5) * .25;
                x = (int)Math.Floor(player.Pos.X + Math.Cos(angle) * radius);
                z = (int)Math.Floor(player.Pos.Z + Math.Sin(angle) * radius);
            }
            double dpx = (x + .5) - player.Pos.X;
            double dpz = (z + .5) - player.Pos.Z;
            double distanceSquared = dpx * dpx + dpz * dpz;
            if (distanceSquared < (double)minDistance * minDistance ||
                distanceSquared > (double)maxDistance * maxDistance)
            { if (diagnostics != null) diagnostics.Distance++; continue; }
            var candidateZone = lastWatch != null ? WatchZone.FromBlock(x, z, dimension) : default;
            // Player eligibility was checked once before the candidate loop.
            // Every candidate must remain in that SAME zone, so rechecking
            // the online-player roster here would only waste work.
            if (lastWatch != null && candidateZone != playerZone)
            { if (diagnostics != null) diagnostics.Zone++; continue; }
            Profiler.Count("spawn.surfaceSamples");
            int surface = w.BlockAccessor.GetTerrainMapheightAt(new BlockPos(x, 0, z, dimension));
            // The terrain heightmap is a generation-time estimate, not the actual
            // present-day surface. Scan nearby heights to handle foliage, snow,
            // low structures and terrain changes, instead of requiring exact air.
            int verticalChecks = !isHordeSpawn && Settings.AmbientSpawnSurfaceOnly
                ? 2 * Settings.SpawnSurfaceVerticalTolerance : 16;
            for (int step = 0; step <= verticalChecks; step++)
            {
                int offset = step == 0 ? 0 : (step % 2 == 1 ? (step + 1) / 2 : -step / 2);
                int y = surface + 1 + offset;
                if (y < 1 || Math.Abs(y - player.Pos.Y) > 32) continue;
                var feet = new BlockPos(x, y, z, dimension);
                if (w.BlockAccessor.GetChunkAtBlockPos(feet) == null)
                { if (diagnostics != null) diagnostics.Chunk++; continue; }
                // A monster can stand in tallgrass and similar non-solid plants.
                // Solid blocks and all liquids remain invalid for either body space.
                var head = feet.UpCopy();
                if (w.BlockAccessor.GetChunkAtBlockPos(head) == null ||
                    w.BlockAccessor.GetChunkAtBlockPos(feet.DownCopy()) == null ||
                    !IsClearSpawnSpace(feet) || !IsClearSpawnSpace(head))
                { if (diagnostics != null) diagnostics.Surface++; continue; }
                var groundPos = feet.DownCopy();
                var ground = w.BlockAccessor.GetBlock(groundPos);
                if (!ground.SideSolid[BlockFacing.UP.Index])
                { if (diagnostics != null) diagnostics.Surface++; continue; }
                if (Settings.ProtectClaims && (!isHordeSpawn || !Settings.HordeSpawnsInClaims) &&
                    (w.Claims.Get(feet)?.Length ?? 0) > 0)
                { if (diagnostics != null) diagnostics.Claim++; continue; }
                // MaxTimeOfDayLight includes torches AND sunlight adjusted for
                // the current day/night cycle. OnlyBlockLight ignored daylight,
                // causing outdoor spawning in bright daytime with old configs.
                if (!ignoreLight &&
                    (!Rules.IsSpawnLightSafe(w.BlockAccessor.GetLightLevel(feet, EnumLightLevelType.MaxTimeOfDayLight), Settings) ||
                     !Rules.IsSpawnLightSafe(w.BlockAccessor.GetLightLevel(head, EnumLightLevelType.MaxTimeOfDayLight), Settings)))
                { if (diagnostics != null) diagnostics.Light++; continue; }
                var point = new Vec3d(x + .5, y, z + .5);
                // Particularly important during batched dormant returns: do not
                // place many zombies into one valid block in the same tick.
                if (w.GetEntitiesAround(point, 1, 2,
                    e => e is Zombie zombie && zombie.Alive).Length > 0)
                { if (diagnostics != null) diagnostics.Surface++; continue; }
                // The target player's minimum is already enforced by the radial search.
                // Don't exclude the entire neighborhood of a multiplayer group.
                if (w.AllOnlinePlayers.Any(p => p.Entity != null && p.Entity.Pos.Dimension == dimension &&
                    Math.Pow(p.Entity.Pos.X - point.X, 2) + Math.Pow(p.Entity.Pos.Z - point.Z, 2) <
                    (double)Settings.SpawnMinimumOtherPlayerDistance * Settings.SpawnMinimumOtherPlayerDistance))
                { if (diagnostics != null) diagnostics.Distance++; continue; }
                bool ambientAddition = !isHordeSpawn && !dormantOnly &&
                    lastWatch != null && lastWatch.CanAddAmbientPopulationSpawn(candidateZone, spawningPlayerUid);
                if (ambientOnly && !ambientAddition && lastWatch != null)
                { if (diagnostics != null) diagnostics.Zone++; continue; }
                bool spawned = dormantOnly || (!isHordeSpawn && lastWatch != null && !ambientAddition)
                    ? Population?.TryReactivateAtZone(candidateZone, point, dimension) == true
                    : Spawn(point, dimension, hordeSpawn: isHordeSpawn);
                if (!spawned)
                { if (diagnostics != null) diagnostics.CapOrEntity++; return false; }
                if (ambientAddition) lastWatch.RecordAmbientPopulationSpawn(candidateZone, spawningPlayerUid);
                return true;
            }
        }
        return false;
    }

    public bool IsClearSpawnSpace(BlockPos pos)
    {
        var w = Server.World.BlockAccessor;
        var block = w.GetBlock(pos);
        if (w.GetBlock(pos, BlockLayersAccess.Fluid).Id != 0) return false;
        // Foliage generally has no collision box. Ignore its item/block ID.
        var boxes = block.GetCollisionBoxes(w, pos);
        return boxes == null || boxes.Length == 0;
    }

    public bool Spawn(Vec3d pos, int dimension, bool alpha = false, bool restoring = false, bool hordeSpawn = false, ZombieVariant? variant = null)
    {
        // One shared cap for ambient, hordes, dormant wakes, admin test spawns,
        // and death-created Alphas. It never debits a dormant count on failure.
        if (SpawnsPaused || ActiveZombieCapReached) return false;
        var chosen = alpha ? ZombieVariant.Alpha : variant ?? ZombieVariantRules.Roll(Server.World.Rand.NextDouble(), Settings);
        // The bloater's wider body must fit too; failure never consumes dormant/ambient counts.
        if (chosen != ZombieVariant.Normal && chosen != ZombieVariant.Alpha &&
            !CanFitVariant(pos, dimension, chosen == ZombieVariant.Bloater ? .48 : .33,
                ZombieVariantRules.Height(chosen))) return false;
        var type = Server.World.GetEntityType(new AssetLocation("seventhnightzombies:" + ZombieVariantRules.Code(chosen)));
        if (type == null) { Server.Logger.Error("Seventh Night Zombies: requested zombie entity type is missing."); return false; }
        var e = Server.World.ClassRegistry.CreateEntity(type);
        e.Pos.SetPos(pos);
        e.Pos.Dimension = dimension;
        e.Pos.Yaw = (float)(Server.World.Rand.NextDouble() * Math.PI * 2);
        if (hordeSpawn) e.WatchedAttributes.SetBool("seventhnight-horde-spawn", true);
        Server.World.SpawnEntity(e);
        // OnEntitySpawn normally adds this ID; HashSet protects both event
        // orderings and ensures the next spawn in this wave sees the cap.
        if (e is Zombie zombie && e.EntityId > 0 && zombie.Alive && !zombie.IsDormant)
            activeZombieIds.Add(e.EntityId);
        return true;
    }
    bool HasEyeBandage(IPlayer player)
    {
        var manager = player.InventoryManager;
        if (manager == null) return false;
        foreach (var inventory in manager.Inventories.Values)
        {
            if (inventory == null || !Settings.EyeBandageInventoryClasses.Any(s =>
                 !string.IsNullOrWhiteSpace(s) && inventory.ClassName.Contains(s, StringComparison.OrdinalIgnoreCase))) continue;
            foreach (var slot in inventory)
                if (!slot.Empty && Rules.IsEyeCovering(slot.Itemstack?.Collectible?.Id ?? -1,
                         slot.Itemstack?.Collectible?.Code?.ToString(), Settings))
                    return true;
        }
        return false;
    }
    TextCommandResult StartManualHorde()
    {
        if (SpawnsPaused) return TextCommandResult.Error("Zombie spawns are paused. Use /zombieunpause first.");
        int count = Server.World.AllOnlinePlayers.Count(p => p.Entity != null &&
            IsTarget(p.Entity) && p.Entity.Pos.Dimension == 0);
        if (count == 0) return TextCommandResult.Error("No online surface survival players to spawn near.");
        int summoned = BeginHorde(natural: false);
        return TextCommandResult.Success($"Horde started for {Settings.HordeDurationSeconds:0} real seconds: spawned {summoned} zombies near {count} players. Further waves every {Settings.HordeWaveIntervalSeconds:0} seconds.");
    }

    // Called by BOTH the seventh-night calendar and /zombiehordestart. The
    // identical announcement, cue and initial spawning run for both pathways.
    int BeginHorde(bool natural)
    {
        if (!natural && Rules.IsHorde(OutbreakDays, Settings))
        {
            // A manual event fulfills this night's scheduled event, avoiding
            // another announcement after the manual event ends.
            lastNaturalHordeNight = Rules.NightNumber(OutbreakDays, Settings);
            Server.WorldManager.SaveGame.StoreData("seventhnight-last-natural-horde-night",
                BitConverter.GetBytes(lastNaturalHordeNight));
        }
        long now = Server.World.ElapsedMilliseconds;
        hordeUntilMs = now + (long)(Settings.HordeDurationSeconds * 1000);
        nextHordeWaveMs = now + (long)(Settings.HordeWaveIntervalSeconds * 1000);
        initialHordeRetryUntilMs = now + (long)(Settings.HordeInitialRetryWindowSeconds * 1000);
        nextInitialHordeRetryMs = now + (long)(Settings.HordeInitialRetrySeconds * 1000);
        pendingOpeningZombies.Clear();
        lastHorde = true;

        // The manual and scheduled events should give the SAME announcement.
        string announcement = $"A raven cries overhead. A {Settings.HordeDurationSeconds / 60.0:0.#}-minute horde is approaching!";
        foreach (var p in Server.World.AllOnlinePlayers.OfType<IServerPlayer>())
            p.SendMessage(0, announcement, EnumChatType.Notification);

        // IMPORTANT: cues are unconditional for each eligible surface survival
        // player. The old nearby-zombie gate silenced scheduled hordes when
        // their first wave had no suitable spawn locations.
        PlayHordeOpeningSounds();
        int spawned = SpawnHordeWave(initialWave: true);

        // Newly created zombies and old nearby zombies each moan once at the
        // start as additional positional audio, independently of the raven.
        foreach (var zombie in Server.World.LoadedEntities.Values.OfType<Zombie>().Where(z => z.Alive))
            if (Server.World.AllOnlinePlayers.Any(p => p.Entity != null && IsTarget(p.Entity) &&
                p.Entity.Pos.Dimension == zombie.Pos.Dimension &&
                p.Entity.Pos.SquareDistanceTo(zombie.Pos.XYZ) <= 28 * 28))
                zombie.OnHordeStarted();

        if (pendingOpeningZombies.Count > 0)
            Server.Logger.Warning($"Seventh Night Zombies: opening horde wave fell short for {pendingOpeningZombies.Count} player(s); retrying every {Settings.HordeInitialRetrySeconds:0}s for up to {Settings.HordeInitialRetryWindowSeconds:0}s.");
        return spawned;
    }

    void PlayHordeOpeningSounds()
    {
        foreach (var p in Server.World.AllOnlinePlayers.OfType<IServerPlayer>())
        {
            if (p.Entity == null || !IsTarget(p.Entity) || p.Entity.Pos.Dimension != 0) continue;
            Server.World.PlaySoundFor(new AssetLocation("seventhnightzombies:sounds/ravencaw"), p,
                false, Settings.ManualHordeSoundRange, 0.9f);
            string recipientId = p.PlayerUID;
            for (int i = 0; i < Settings.ManualHordeMoans; i++)
            {
                int moanIndex = 1 + Server.World.Rand.Next(3);
                int delay = 400 + i * 550;
                Server.Event.RegisterCallback(_ =>
                {
                    var recipient = Server.World.PlayerByUid(recipientId);
                    if (recipient is IServerPlayer living && living.Entity != null && IsTarget(living.Entity) && living.Entity.Pos.Dimension == 0)
                        Server.World.PlaySoundFor(new AssetLocation($"seventhnightzombies:sounds/moan{moanIndex}"), living,
                            false, Settings.ManualHordeSoundRange, 0.75f);
                }, delay);
            }
        }
    }

    // Horde additions are bounded by the requested wave size; they never
    // consume the ambient per-player per-zone allowance.
    // Hordes create FRESH zombies using the same 24-64-block radial search,
    // lighting checks and player spacing as ambient spawning. They
    // create new zombies but NEVER consume ambient quotas.
    int SpawnHordeNearPlayer(EntityPlayer player, int wanted, bool widerFallback)
    {
        int spawned = 0;
        for (int i = 0; i < wanted && !ActiveZombieCapReached; i++)
        {
            // 'widerFallback' intentionally does not bypass ambient placement
            // constraints: the horde just retries the same safe spawn search.
            if (!TrySpawnNear(player, Settings.SpawnMinDistance,
                Settings.SpawnMaxDistance, isHordeSpawn: true)) continue;
            spawned++;
        }
        return spawned;
    }

    int SpawnHordeWave(bool initialWave = false)
    {
        int total = 0;
        foreach (var p in Server.World.AllOnlinePlayers.OrderBy(_ => Server.World.Rand.Next()))
        {
            if (p.Entity == null || !IsTarget(p.Entity) || p.Entity.Pos.Dimension != 0) continue;
            int minWave = initialWave ? Settings.HordeInitialMinZombiesPerPlayer : Settings.HordeWaveMinZombiesPerPlayer;
            int wanted = Server.World.Rand.Next(minWave, Settings.HordeWaveMaxZombiesPerPlayer + 1);
            int added = SpawnHordeNearPlayer(p.Entity, wanted, widerFallback: initialWave);
            total += added;
            if (initialWave && added < Settings.HordeInitialMinZombiesPerPlayer)
            {
                pendingOpeningZombies[p.PlayerUID] = Settings.HordeInitialMinZombiesPerPlayer - added;
                Server.Logger.Warning($"Seventh Night Zombies: opening horde wave spawned {added}/{Settings.HordeInitialMinZombiesPerPlayer} minimum zombies for {p.PlayerName}; queued automatic retries.");
            }
        }
        Server.Logger.Notification($"Seventh Night Zombies: horde added {total} new active zombies (ambient quota unchanged; block-light bypass: {Settings.HordeSpawnIgnoreLightLevel}; claims bypass: {Settings.HordeSpawnsInClaims}).");
        return total;
    }

    void RetryOpeningHordeWave()
    {
        foreach (var entry in pendingOpeningZombies.ToArray())
        {
            var player = Server.World.PlayerByUid(entry.Key) as IServerPlayer;
            if (player?.Entity == null || !IsTarget(player.Entity) || player.Entity.Pos.Dimension != 0)
            {
                pendingOpeningZombies.Remove(entry.Key);
                continue;
            }
            int added = SpawnHordeNearPlayer(player.Entity, entry.Value, widerFallback: true);
            int remaining = entry.Value - added;
            if (remaining <= 0) pendingOpeningZombies.Remove(entry.Key);
            else pendingOpeningZombies[entry.Key] = remaining;
            if (added > 0)
                Server.Logger.Notification($"Seventh Night Zombies: recovered {added} missing opening-wave zombie(s) for {player.PlayerName}; remaining {Math.Max(0, remaining)}.");
        }
    }
    void OnDeath(IServerPlayer p, DamageSource source)
    {
        if (p?.Entity == null) return;
        ClearHits(p.PlayerUID);
        bool infected = p.Entity.WatchedAttributes.HasAttribute(InfectionKey);
        bool eaten = source?.SourceEntity is Zombie;
        bool turned = infected && !eaten && source?.Type == EnumDamageType.Poison;
        if (eaten || turned)
        {
            string text = $"{p.PlayerName} was {(eaten ? "eaten" : "turned")}.";
            foreach (var recipient in Server.World.AllOnlinePlayers.OfType<IServerPlayer>())
                recipient.SendMessage(0, text, EnumChatType.Notification);
        }
        // No player attribute, position, health, movement, or animation changes
        // during death. The game must own the death camera and respawn entirely.
        // A UID-keyed record also prevents duplicate Alpha spawns if a mod or
        // engine edge case delivers the same death more than once.
        if (!Settings.SpawnAlphaOnPlayerDeath || !AutomaticSpawnWindowOpen())
        {
            pendingDeathAlphas.Remove(p.PlayerUID);
            return;
        }
        if (!pendingDeathAlphas.ContainsKey(p.PlayerUID))
            pendingDeathAlphas[p.PlayerUID] = (LocalPosition(p.Entity.Pos), p.Entity.Pos.Dimension);
    }
    // Avoid clipping the death camera/corpse with a hostile full-size entity.
    // This checks actual block collisions rather than touching player motion.
    bool TrySpawnDeathAlpha(Vec3d deathPos, int dimension)
    {
        var world = Server.World;
        int centerX = (int)Math.Floor(deathPos.X);
        int centerZ = (int)Math.Floor(deathPos.Z);
        (int dx, int dz)[] offsets =
        [
            (12, 0), (-12, 0), (0, 12), (0, -12),
            (9, 9), (-9, 9), (9, -9), (-9, -9),
            (16, 0), (0, 16), (-16, 0), (0, -16),
            (12, 12), (-12, 12), (12, -12), (-12, -12)
        ];
        int baseY = (int)Math.Floor(deathPos.Y);
        foreach (var (dx, dz) in offsets)
        {
            int x = centerX + dx, z = centerZ + dz;
            for (int yOffset = 0; yOffset <= 6; yOffset++)
            {
                int offsetY = yOffset == 0 ? 0 : (yOffset % 2 == 1 ? (yOffset + 1) / 2 : -yOffset / 2);
                int y = baseY + offsetY;
                if (y < 1) continue;
                var feet = new BlockPos(x, y, z, dimension);
                if (world.BlockAccessor.GetChunkAtBlockPos(feet) == null ||
                    !IsClearSpawnSpace(feet) || !IsClearSpawnSpace(feet.UpCopy())) continue;
                if (!world.BlockAccessor.GetBlock(feet.DownCopy()).SideSolid[BlockFacing.UP.Index]) continue;
                // Death-created Alphas are automatic spawns too. Respect the
                // same configured inclusive light limit used by ordinary zombies.
                if (!Rules.IsSpawnLightSafe(world.BlockAccessor.GetLightLevel(feet,
                        EnumLightLevelType.MaxTimeOfDayLight), Settings) ||
                    !Rules.IsSpawnLightSafe(world.BlockAccessor.GetLightLevel(feet.UpCopy(),
                        EnumLightLevelType.MaxTimeOfDayLight), Settings)) continue;
                // A recently respawned player might be near the original death
                // location. Never place a hostile Alpha directly on any player.
                if (world.AllOnlinePlayers.Any(other => other.Entity != null && other.Entity.Alive &&
                    other.Entity.Pos.Dimension == dimension &&
                    Math.Pow(other.Entity.Pos.X - x - .5, 2) + Math.Pow(other.Entity.Pos.Z - z - .5, 2) < 144))
                    continue;
                return Spawn(new Vec3d(x + .5, y, z + .5), dimension, alpha: true);
            }
        }
        return false;
    }

    void OnDisconnect(IServerPlayer p)
    {
        ClearHits(p.PlayerUID);
        masks.Forget(p.PlayerUID);
        pendingDeathAlphas.Remove(p.PlayerUID);
    }

    void OnRespawn(IServerPlayer p)
    {
        if (p?.Entity == null) return;
        // Infection and tunic sickness are cleaned AFTER the game's normal
        // respawn. Never modify the dying entity's watched state during death.
        RotCoveredTunicService.ClearSickness(p.Entity);
        p.Entity.Attributes.RemoveAttribute(RotPoisonKey);
        ClearHits(p.PlayerUID);
        masks.Forget(p.PlayerUID);
        p.Entity.WatchedAttributes.RemoveAttribute(InfectionKey);
        p.Entity.Attributes.RemoveAttribute(InfectionServerClockKey);
        p.Entity.WatchedAttributes.RemoveAttribute(InfectionStartDayKey);
        p.Entity.WatchedAttributes.RemoveAttribute(LegacyPoulticeKey);
        p.Entity.WatchedAttributes.RemoveAttribute(CureDosesKey);
        p.Entity.WatchedAttributes.RemoveAttribute(CureLastDoseDayKey);
        p.Entity.WatchedAttributes.RemoveAttribute(CureFirstDoseDayKey);
        ClearInfectionDisplay(p.Entity);
        p.Entity.WatchedAttributes.RemoveAttribute(QuietBandageKey);

        if (!pendingDeathAlphas.TryGetValue(p.PlayerUID, out var pending)) return;
        pendingDeathAlphas.Remove(p.PlayerUID);
        // This scheduled callback is now registered ONLY after player respawn,
        // never during the first-person death animation or respawn transition.
        Server.Event.RegisterCallback(_ =>
        {
            try
            {
                if (!AutomaticSpawnWindowOpen()) return;
                if (!TrySpawnDeathAlpha(pending.Position, pending.Dimension))
                {
                    // No physical space is available: the optional expansion
                    // may preserve a pending Alpha as dormant. Never bypass cap.
                    Population?.AddPending(pending.Position, pending.Dimension, alpha: true);
                    Server.Logger.Warning("Seventh Night Zombies: no safe post-respawn death-Alpha spawn site; saved one dormant Alpha instead.");
                }
            }
            catch (Exception ex)
            {
                Server.Logger.Error("Seventh Night Zombies: post-respawn Alpha spawn failed; player respawn is unaffected: " + ex);
            }
        }, Settings.AlphaDeathSpawnDelayMilliseconds);
    }
    public override void Dispose()
    {
        CompatibilityHooks.Uninstall();
        if (Server == null) return;
        pendingDeathAlphas.Clear();
        activeZombieIds.Clear();
        Server.Event.SaveGameLoaded -= LoadPopulation;
        Server.Event.GameWorldSave -= SavePopulation;
        Server.Event.OnEntityLoaded -= TrackZombie;
        Server.Event.OnEntitySpawn -= OnNewZombie;
        Server.Event.UnregisterGameTickListener(tickId);
        Server.Event.PlayerDeath -= OnDeath;
        Server.Event.PlayerDisconnect -= OnDisconnect;
        Server.Event.PlayerRespawn -= OnRespawn;
        Server.Event.PlayerReady -= OnPlayerReady;
    }
}
