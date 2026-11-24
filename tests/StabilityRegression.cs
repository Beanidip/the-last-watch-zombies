using System.Reflection;
using Newtonsoft.Json.Linq;
using SeventhNightZombies;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

// 2.0.10 stability fixes: cure window, corpse drop migration, dormant counter
// verification, bait give-up, stale building records, block-entity industry scan.
public static class StabilityRegression
{
    sealed class TestBlockEntity : BlockEntity { }

    public static void Run(Action<bool, string> check)
    {
        CureWindow(check);
        CorpseDropConfig(check);
        DormantCounter(check);
        BaitGiveUp(check);
        BuildingRecords(check);
        IndustryScan(check);
        TraitText(check);
        LiveConfig(check);
    }

    static void LiveConfig(Action<bool, string> check)
    {
        var saved = new Dictionary<string, JObject>();
        var api = Fake.Make<ICoreServerAPI>((m, a) =>
        {
            if (m.Name == "StoreModConfig") { saved[(string)a[1]] = (JObject)a[0]; return null; }
            if (m.Name == "LoadModConfig") return saved.TryGetValue((string)a[0], out var v) ? v : null;
            return null;
        });
        var core = new SeventhNightSystem();
        bool Ok(TextCommandResult r) => r.Status == EnumCommandStatus.Success;
        check(Ok(AdminConfiguration.Execute(api, core, "activecap 100")) && core.Settings.MaxActiveZombies == 100 &&
              (int)saved[StandaloneConfigStorage.MainFile]["MaxActiveZombies"] == 100, "/zombieconfig applies immediately and saves");
        check(Ok(AdminConfiguration.Execute(api, core, "spawnmindistance 60")) && core.Settings.SpawnMinDistance == 60 &&
              core.Settings.SpawnMaxDistance >= 61 && (int)saved[StandaloneConfigStorage.MainFile]["SpawnMaxDistance"] == core.Settings.SpawnMaxDistance,
              "dependent settings stay consistent live and on disk");
        check(!Ok(AdminConfiguration.Execute(api, core, "activecap -5")) && core.Settings.MaxActiveZombies == 100,
              "rejected value changes nothing");
        check(Ok(AdminConfiguration.Execute(api, core, "protectclaims off")) && !core.Settings.ProtectClaims &&
              Ok(AdminConfiguration.Execute(api, core, "curedoses 4")) && core.Settings.CureDosesRequired == 4 &&
              Ok(AdminConfiguration.Execute(api, core, "deathalpha false")) && !core.Settings.SpawnAlphaOnPlayerDeath,
              "new short names work live");
        check(!core.Profiler.Enabled && Ok(AdminConfiguration.Execute(api, core, "diagnostics on")) && core.Profiler.Enabled,
              "diagnostics toggle without restart");
        check(AdminConfiguration.Keys.All(k => typeof(Config).GetField(k.Value) != null && !StandaloneConfigStorage.IsRetired(k.Value)),
              "every /zombieconfig name maps to an active setting");
        foreach (var name in new[] { "protectclaims", "normalhits", "alphahits", "curedoses", "doseinterval", "attackdamage",
                     "detectionrange", "hordeblockbreakchance", "deathalpha" })
            check(AdminConfiguration.Keys.ContainsKey(name), "short name exists: " + name);
    }

    static void TraitText(Action<bool, string> check)
    {
        string T(string key, object[] args) => key.Replace("seventhnightzombies:", "") + (args.Length > 0 ? "[" + string.Join("|", args) + "]" : "");
        var w = new Vintagestory.API.Datastructures.TreeAttribute();
        check(InfectionTrait.Describe(w, 0, T) == null, "healthy player has no infection trait");
        w.SetDouble(SeventhNightSystem.InfectionKey, 0);
        w.SetDouble(InfectionTrait.FatalDayKey, 7);
        w.SetDouble(InfectionTrait.CureStartByDayKey, 2);
        w.SetInt(InfectionTrait.DosesRequiredKey, 6);
        string untreated = InfectionTrait.Describe(w, 0.5, T);
        check(untreated.Contains("trait-infected") && untreated.Contains("traitdesc-infected-untreated[1d 12h]") &&
              untreated.Contains("traitdesc-infected-fatal[6d 12h]"), "untreated trait shows start window and fatal countdown");
        check(InfectionTrait.Describe(w, 2.5, T).Contains("traitdesc-infected-toolate"), "after the window the trait says too late");
        w.SetInt(SeventhNightSystem.CureDosesKey, 2);
        w.SetDouble(InfectionTrait.NextDoseDayKey, 3.25);
        w.SetDouble(InfectionTrait.FatalDayKey, 7.5);
        string treated = InfectionTrait.Describe(w, 3, T);
        check(treated.Contains("traitdesc-infected-treated[2|6|traitdesc-infected-dose-in[6h 0m]]") &&
              treated.Contains("traitdesc-infected-fatal[4d 12h]"), "treated trait shows doses and next dose");
        check(InfectionTrait.Describe(w, 3.3, T).Contains("traitdesc-infected-dose-ready"), "due dose shows ready");
        w.SetDouble(InfectionTrait.FatalDayKey, -1);
        check(InfectionTrait.Describe(w, 3.3, T).Contains("traitdesc-infected-notfatal"), "fatality disabled is shown");
        var hookTarget = HarmonyLib.AccessTools.Method(typeof(Vintagestory.GameContent.CharacterSystem), "getClassTraitText");
        check(hookTarget != null && hookTarget.ReturnType == typeof(string) && hookTarget.GetParameters().Length == 0,
              "VS 1.22.7 still has CharacterSystem.getClassTraitText() for the Traits-tab hook");
        check(InfectionTrait.Until(0) == "0m" && InfectionTrait.Until(1.0 / 1440 * 75) == "1h 15m" && InfectionTrait.Until(2.5) == "2d 12h",
              "compact durations");
    }

    static void CureWindow(Action<bool, string> check)
    {
        var c = new Config(); c.Validate();
        check(c.CureStartWindowDays == 2, "cure start window defaults to two days");
        check(Rules.CanStartCure(0, 0, c) && Rules.CanStartCure(0, 2, c) && !Rules.CanStartCure(0, 2.01, c), "cure can start up to day 2 only");
        check(!Rules.IsFatalInfection(0, 6.99, c) && Rules.IsFatalInfection(0, 7, c), "untreated infection still fatal after 7 days");
        var nonFatal = new Config { InfectionFatalEnabled = false }; nonFatal.Validate();
        check(Rules.CanStartCure(0, 30, nonFatal) && !Rules.IsFatalInfection(0, 30, nonFatal),
              "with fatality disabled the cure can always be started (no stuck infection)");
        // Every start inside the window must allow a full on-schedule course.
        bool feasible = true;
        foreach (var start in new[] { 0.0, 0.5, 1.0, 1.5, 1.99, 2.0 })
            for (int dose = 1; dose <= c.CureDosesRequired; dose++)
            {
                double day = start + (dose - 1) * c.CureDoseIntervalDays;
                feasible &= !Rules.IsFatalInfection(0, day, dose - 1, start, c);
            }
        check(feasible, "any course started within the window can finish");
        check(Math.Abs(Rules.FatalDeadlineDay(0, 1, 2, c) - 8) < 1e-9 && Rules.IsFatalInfection(0, 8, 1, 2, c) &&
              !Rules.IsFatalInfection(0, 7.9, 3, 2, c), "treated deadline is first dose + doses x interval");
        check(Math.Abs(Rules.FatalDeadlineDay(0, 1, 0, c) - 7) < 1e-9, "early treatment never shortens the 7-day deadline");
        var longCourse = new Config { CureDosesRequired = 10 }; longCourse.Validate();
        bool longFeasible = true;
        for (int dose = 1; dose <= 10; dose++)
            longFeasible &= !Rules.IsFatalInfection(0, 2 + dose - 1, dose - 1, 2, longCourse);
        check(longFeasible, "a 10-dose course started on day 2 is still curable");
        var clamp = new Config { CureStartWindowDays = 99, InfectionFatalDays = 5 }; clamp.Validate();
        check(clamp.CureStartWindowDays == 5, "start window cannot exceed the fatal period");
        clamp = new Config { CureStartWindowDays = double.NaN }; clamp.Validate();
        check(clamp.CureStartWindowDays == 2, "non-finite start window falls back to 2");
        check(AdminConfiguration.TrySet(new Config(), nameof(Config.CureStartWindowDays), "3", out _) &&
              AdminConfiguration.Keys["curestartwindow"] == nameof(Config.CureStartWindowDays), "start window editable with /zombieconfig");
    }

    static void CorpseDropConfig(Action<bool, string> check)
    {
        var c = new Config(); c.Validate();
        check(c.CorpseRustyGears == 1, "one rusty gear per corpse by default");
        var legacy = new Config();
        StandaloneConfigStorage.MigrateCorpseDrops(legacy, null, JObject.Parse("{\"CorpseSmallHides\": 3}"));
        check(legacy.CorpseRustyGears == 3, "saved small-hide count carries over to rusty gears");
        var explicitGears = new Config { CorpseRustyGears = 2 };
        StandaloneConfigStorage.MigrateCorpseDrops(explicitGears, null,
            JObject.Parse("{\"CorpseSmallHides\": 3, \"CorpseRustyGears\": 2}"));
        check(explicitGears.CorpseRustyGears == 2, "explicit rusty gear setting wins over legacy hides");
        check(StandaloneConfigStorage.IsRetired("CorpseSmallHides") &&
              StandaloneConfigStorage.FileFor(nameof(Config.CorpseRustyGears)) == StandaloneConfigStorage.MainFile,
              "hide key retired, gear key lives in the main config");
        var saved = new Dictionary<string, JObject>();
        var api = Fake.Make<ICoreServerAPI>((m, a) =>
        {
            if (m.Name == "StoreModConfig") { saved[(string)a[1]] = (JObject)a[0]; return null; }
            if (m.Name == "LoadModConfig") return saved.TryGetValue((string)a[0], out var v) ? v : null;
            return null;
        });
        saved[StandaloneConfigStorage.MainFile] = JObject.Parse("{\"CorpseSmallHides\": 0}");
        var loaded = StandaloneConfigStorage.Load(api);
        check(loaded.CorpseRustyGears == 0, "Load migrates CorpseSmallHides from the main file");
        StandaloneConfigStorage.Save(api, loaded);
        check(saved[StandaloneConfigStorage.MainFile]["CorpseSmallHides"] == null &&
              (int)saved[StandaloneConfigStorage.MainFile]["CorpseRustyGears"] == 0, "rewrite drops the old key and keeps the value");
    }

    static void DormantCounter(Action<bool, string> check)
    {
        var core = new SeventhNightSystem();
        var pool = new ZombiePopulation(core);
        for (int i = 0; i < 12; i++) pool.AddPending(new Vec3d(5, 64, 5), 0, i % 4 == 0);
        check(pool.VerifyDormantCount() && pool.CountStored() == 12, "counter matches stored counts");
        typeof(ZombiePopulation).GetField("dormantCount", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(pool, 40L);
        check(!pool.VerifyDormantCount() && pool.DormantCount == 12 && pool.CountStored() == 12,
              "counter mismatch is detected and repaired without touching stored counts");
        string source = pool.MigrationSnapshot().Single().Key;
        typeof(ZombiePopulation).GetField("dormantCount", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(pool, 99L);
        check(pool.ApplyMigration(new[] { new MigrationTransfer { Source = source, DestX = 4, DestY = 2, DestZ = 4, Count = 5 } }) &&
              pool.DormantCount == 12 && pool.CountStored() == 12, "migration verifies and repairs the counter");
    }

    static void BaitGiveUp(Action<bool, string> check)
    {
        var pile = new BlockPos(10, 64, 10, 0);
        var state = new BaitPursuitState();
        state.Begin(pile, 20, 0);
        check(!state.Update(20, false, 14999) && state.Update(20, false, 15000), "no progress for 15 s gives up");
        state.Begin(pile, 20, 0);
        check(!state.Update(18.9, false, 10000) && !state.Update(18.9, false, 24999) && state.Update(18.9, false, 25000),
              "each block of progress buys another 15 s");
        state.Begin(pile, 20, 0);
        check(!state.Update(19.5, false, 10000) && state.Update(19.5, false, 15000), "sub-block shuffling is not progress");
        state.Begin(pile, 1, 0);
        check(!state.Update(1, true, 60000), "standing at the pile with a clear bite never gives up");
        state.Begin(pile, 20, 0);
        state.GiveUp(15000);
        check(state.Pile == null && pile.Equals(state.Ignored(15000)) && pile.Equals(state.Ignored(74999)) && state.Ignored(75000) == null,
              "abandoned pile is ignored for 60 s, then eligible again");
        state.Clear();
        check(state.Ignored(20000) != null, "clearing a target keeps the ignore memory");
    }

    // Fake world for LastWatchZones: a block map, loaded-chunk set, block entities.
    sealed class World
    {
        public readonly Dictionary<(int, int, int), Block> Blocks = new();
        public readonly HashSet<(int, int)> LoadedColumns = new();
        public readonly Dictionary<(int, int, int), Dictionary<BlockPos, BlockEntity>> ChunkEntities = new();
        public int Surface = 60;
        public ICoreServerAPI Api;
        public World()
        {
            var air = new Block { BlockId = 0, Code = new AssetLocation("game:air") };
            var chunk = Fake.Make<IWorldChunk>((m, a) => null);
            var accessor = Fake.Make<IBlockAccessor>((m, a) =>
            {
                switch (m.Name)
                {
                    case "GetChunkAtBlockPos":
                        var p = (BlockPos)a[0];
                        return LoadedColumns.Contains(((int)Math.Floor(p.X / 32.0), (int)Math.Floor(p.Z / 32.0))) ? chunk : null;
                    case "GetBlock" when a.Length > 0 && a[0] is BlockPos bp:
                        return Blocks.TryGetValue((bp.X, bp.Y, bp.Z), out var b) ? b : air;
                    case "GetChunk" when a.Length == 3:
                        var key = ((int)a[0], (int)a[1], (int)a[2]);
                        if (!LoadedColumns.Contains((key.Item1, key.Item3))) return null;
                        var entities = ChunkEntities.TryGetValue(key, out var list) ? list : new Dictionary<BlockPos, BlockEntity>();
                        return Fake.Make<IWorldChunk>((mm, aa) => mm.Name == "get_BlockEntities" ? entities : null);
                    case "GetTerrainMapheightAt":
                        return Surface;
                }
                return null;
            });
            var world = Fake.Make<IServerWorldAccessor>((m, a) => m.Name == "get_BlockAccessor" ? accessor : null);
            var manager = Fake.Make<IWorldManagerAPI>((m, a) => m.Name == "get_MapSizeY" ? 256 : null);
            Api = Fake.Make<ICoreServerAPI>((m, a) => m.Name switch
            {
                "get_World" => world, "get_WorldManager" => manager, _ => null
            });
        }
        public void Set(int x, int y, int z, string code) => Blocks[(x, y, z)] = new Block { BlockId = 1, Code = new AssetLocation(code) };
        public void AddEntity(int x, int y, int z, string code)
        {
            var key = ((int)Math.Floor(x / 32.0), (int)Math.Floor(y / 32.0), (int)Math.Floor(z / 32.0));
            if (!ChunkEntities.TryGetValue(key, out var list)) ChunkEntities[key] = list = new();
            var pos = new BlockPos(x, y, z, 0);
            list[pos] = new TestBlockEntity { Pos = pos, Block = new Block { Code = new AssetLocation(code) } };
        }
    }

    static LastWatchZones Director(World world, out Dictionary<WatchZone, WatchZoneState> zones)
    {
        var director = new LastWatchZones();
        var core = new SeventhNightSystem { Server = world.Api };
        var flags = BindingFlags.Instance | BindingFlags.NonPublic;
        typeof(LastWatchZones).GetField("core", flags)!.SetValue(director, core);
        typeof(LastWatchZones).GetField("sapi", flags)!.SetValue(director, world.Api);
        zones = (Dictionary<WatchZone, WatchZoneState>)typeof(LastWatchZones).GetField("zones", flags)!.GetValue(director);
        return director;
    }

    static void BuildingRecords(Action<bool, string> check)
    {
        var world = new World();
        world.LoadedColumns.Add((0, 0));
        var director = Director(world, out var zones);
        var state = new WatchZoneState { StonePaths = 30, PermanentSettlement = true };
        state.Strongholds.Add(new WatchBuilding { X = 5, Y = 64, Z = 5, StrongholdStrength = 300 });   // destroyed
        state.Strongholds.Add(new WatchBuilding { X = 6, Y = 64, Z = 6, StrongholdStrength = 50 });    // swapped to iron
        state.Strongholds.Add(new WatchBuilding { X = 200, Y = 64, Z = 200, StrongholdStrength = 50 }); // unloaded
        state.Bells.Add(new WatchBuilding { X = 7, Y = 64, Z = 7 });                                    // destroyed
        world.Set(6, 64, 6, LastWatchZones.IronStrongholdCode);
        zones[state.Key] = state;
        check(director.ValidateBuildings(state), "validation reports stale records");
        check(state.Strongholds.Count == 2 && !state.Strongholds.Any(s => s.X == 5), "destroyed stronghold record removed");
        check(state.Strongholds.Single(s => s.X == 6).StrongholdStrength == LastWatchZones.IronStrongholdStrength, "stronghold tier follows the actual block");
        check(state.Strongholds.Any(s => s.X == 200), "unloaded stronghold record kept");
        check(state.Bells.Count == 0, "destroyed bell record removed");
        check(!director.ValidateBuildings(state), "validation is idempotent");

        var pathBlock = new Block { Code = new AssetLocation("game:stonepath-free") };
        director.ForgetBlock(new BlockPos(1, 64, 1, 0), pathBlock);
        check(state.StonePaths == 29, "zombie-broken stone path is uncounted");
        var stronghold = new Block { Code = new AssetLocation(LastWatchZones.IronStrongholdCode) };
        director.ForgetBlock(new BlockPos(6, 64, 6, 0), stronghold);
        check(state.Strongholds.Count == 1 && state.Strongholds[0].X == 200, "zombie-broken stronghold is forgotten immediately");
        director.ForgetBlock(new BlockPos(6, 64, 6, 0), stronghold);
        check(state.Strongholds.Count == 1, "forgetting a stronghold twice is harmless");
    }

    static void IndustryScan(Action<bool, string> check)
    {
        var world = new World();
        var director = Director(world, out _);
        var scan = typeof(LastWatchZones).GetMethod("ScanIndustryInLoadedZone", BindingFlags.Instance | BindingFlags.NonPublic)!;
        int Scan(WatchZone zone) => (int)scan.Invoke(director, new object[] { zone })!;
        var zone = new WatchZone(0, 0, 0);
        world.AddEntity(10, 62, 10, "game:forge");
        check(Scan(zone) == 0, "unloaded chunks are never scanned");
        for (int x = 0; x < 3; x++) for (int z = 0; z < 3; z++) world.LoadedColumns.Add((x, z));
        check(Scan(zone) == 1, "forge block entity detected");
        world.AddEntity(40, 20, 40, "game:anvil-iron");
        check(Scan(zone) == 1, "deep underground anvil outside the surface band ignored");
        world.AddEntity(41, 70, 41, "game:anvil-iron");
        world.AddEntity(90, 61, 90, "game:charcoalpit");
        check(Scan(zone) == 7, "forge, anvil and charcoal pit all detected");
        check(Scan(new WatchZone(0, 1, 1)) == 0, "neighbouring zone without industry scores nothing");
        world.LoadedColumns.Add((3, 3));
        world.AddEntity(100, 61, 100, "game:forge");
        check(Scan(new WatchZone(0, 1, 1)) == 1 && Scan(zone) == 7, "block entities count only for their own zone");
        check(LastWatchZones.IndustryFlag(new Block { Code = new AssetLocation("game:chest-east") }) == 0, "ordinary block entities ignored");
    }
}
