using System.Text;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

namespace SeventhNightZombies;

// Pool contains only dimension/chunk coordinates and counts, never per-zombie
// IDs, health, inventories, paths or exact positions.
public sealed class DormantChunk
{
    public int Dimension, X, Y, Z;
    public long Normal, Alpha;
    // Parked by the 10-minute idle timer while a player was already nearby:
    // only rearm when all players leave the five-chunk radius. Prevents an
    // idle zombie from respawning on the next wake scan beside that same player.
    // Older snapshots were written with this field false.
    public bool AwaitDeparture;
    [JsonIgnore] public string Key => $"{Dimension}:{X}:{Y}:{Z}";
    public DormantChunk Copy() => (DormantChunk)MemberwiseClone();
}

public sealed class MigrationTransfer
{
    public string Source = "";
    public int DestX, DestY, DestZ, DestDimension, Count;
    public bool Alpha;
}

public sealed class PopulationSnapshot
{
    public int Version = 2;
    public List<DormantChunk> Chunks = new();
}

public sealed class ZombiePopulation : IZombiePopulation
{
    const string SaveKey = "seventhnightzombies-dormant-population-v1";
    const string DormantMarkerKey = "seventhnight-dormant-counted";
    readonly SeventhNightSystem system;
    readonly Dictionary<string, DormantChunk> chunks = new();
    readonly Dictionary<WatchZone, HashSet<string>> byZone = new();
    long dormantCount;
    readonly HashSet<string> awaitingDeparture = new();
    int wakeCursor;
    void Index(DormantChunk c)
    {
        var zone = WatchZone.FromChunk(c.X, c.Z, c.Dimension);
        if (!byZone.TryGetValue(zone, out var keys)) byZone[zone] = keys = new();
        keys.Add(c.Key);
        if (c.AwaitDeparture) awaitingDeparture.Add(c.Key);
    }
    void RemoveChunk(string key)
    {
        if (!chunks.Remove(key, out var c)) return;
        awaitingDeparture.Remove(key);
        var zone = WatchZone.FromChunk(c.X, c.Z, c.Dimension);
        if (byZone.TryGetValue(zone, out var keys))
        { keys.Remove(key); if (keys.Count == 0) byZone.Remove(zone); }
    }
    void RebuildIndex()
    {
        byZone.Clear(); awaitingDeparture.Clear();
        foreach (var c in chunks.Values) Index(c);
    }
    // Query only occupied zones (or configured radius), including every height band.
    // Negative coordinates and dimensions use the same floor division as storage.
    public DormantChunk[] CandidatesNear(Vec3d pos, int dimension)
    {
        var loc = Location(pos, dimension);
        var center = WatchZone.FromChunk(loc.X, loc.Z, dimension);
        int r = C.DormantWakeOnlyInSameZone ? 0 : C.DormantWakeRadiusChunks;
        int minX = r == 0 ? center.X : WatchZone.ChunkToZone(loc.X - r);
        int maxX = r == 0 ? center.X : WatchZone.ChunkToZone(loc.X + r);
        int minZ = r == 0 ? center.Z : WatchZone.ChunkToZone(loc.Z - r);
        int maxZ = r == 0 ? center.Z : WatchZone.ChunkToZone(loc.Z + r);
        var found = new List<DormantChunk>();
        for (int x = minX; x <= maxX; x++) for (int z = minZ; z <= maxZ; z++)
            if (byZone.TryGetValue(new WatchZone(dimension, x, z), out var keys))
                foreach (var key in keys)
                {
                    var c = chunks[key];
                    if (r == 0 || WithinWakeRadius(c, pos, dimension, r)) found.Add(c);
                }
        return found.ToArray();
    }
    // Keep only dead entities that still need corpse aging and loot handling.
    readonly Dictionary<long, Zombie> corpses = new();
    // Active living entities are counted by the base mod's inexpensive ID ledger.
    public void OnZombieDied(Zombie zombie)
    {
        corpses[zombie.EntityId] = zombie;
    }
    public Zombie[] DeadCorpses() => corpses.Values.ToArray();
    public string ApproximatePopulationSummary => $"dormant {DormantCount}; living population is not tracked";
    Config C => system.Settings;
    public long DormantCount => dormantCount;
    public bool HasDormantInZone(WatchZone zone) => byZone.TryGetValue(zone, out var keys) &&
        keys.Any(key => chunks.TryGetValue(key, out var c) && c.Normal + c.Alpha > 0);

    // Reactivation uses the already-validated ambient spawn point. This keeps
    // the work bounded to one zone lookup and avoids a second terrain search.
    public bool TryReactivateAtZone(WatchZone zone, Vec3d pos, int dimension)
    {
        // The standalone mod owns the spawn schedule and active cap. Never
        // allow an expansion caller to bypass those checks.
        if (!system.AutomaticSpawnWindowOpen() || system.ActiveZombieCapReached ||
            system.WorldDirector?.CanReactivateDormant(zone) != true) return false;
        if (dimension != zone.Dimension || !byZone.TryGetValue(zone, out var keys)) return false;
        foreach (var key in keys.ToArray())
        {
            if (!chunks.TryGetValue(key, out var chunk) || chunk.Normal + chunk.Alpha <= 0) continue;
            bool alpha = chunk.Alpha > 0;
            if (!system.Spawn(pos, dimension, alpha, restoring: true)) return false;
            system.WorldDirector.RecordDormantReactivation(zone);
            if (alpha) chunk.Alpha--; else chunk.Normal--;
            dormantCount--;
            if (chunk.Normal + chunk.Alpha == 0) RemoveChunk(key);
            return true;
        }
        return false;
    }

    public ZombiePopulation(SeventhNightSystem system) { this.system = system; }

    static DormantChunk Location(Vec3d pos, int dimension) => new()
    {
        Dimension = dimension,
        X = (int)Math.Floor(pos.X / GlobalConstants.ChunkSize),
        Y = (int)Math.Floor(pos.Y / GlobalConstants.ChunkSize),
        Z = (int)Math.Floor(pos.Z / GlobalConstants.ChunkSize)
    };

    public void Load()
    {
        byte[] bytes = system.Server.WorldManager.SaveGame.GetData(SaveKey);
        if (bytes == null || bytes.Length == 0) return;
        // Fail visibly instead of silently replacing a corrupt population save.
        var state = JsonConvert.DeserializeObject<PopulationSnapshot>(Encoding.UTF8.GetString(bytes));
        if (state == null || (state.Version != 1 && state.Version != 2) || state.Chunks == null)
            throw new InvalidDataException("Seventh Night Zombies: invalid dormant population save.");
        foreach (var c in state.Chunks)
        {
            if (c == null || c.Normal < 0 || c.Alpha < 0 || chunks.ContainsKey(c.Key))
                throw new InvalidDataException("Seventh Night Zombies: invalid dormant chunk counts.");
            // Departure cooldowns were retired with the count-only ambient
            // reactivation model. Do not strand v1/v2 residents on upgrade.
            c.AwaitDeparture = false;
            if (c.Normal + c.Alpha > 0)
            {
                chunks.Add(c.Key, c);
                dormantCount += c.Normal + c.Alpha;
            }
        }
        RebuildIndex();
        var loaded = system.Server?.World?.LoadedEntities;
        if (loaded != null)
            foreach (var zombie in loaded.Values.OfType<Zombie>()) OnLoaded(zombie);
    }

    public void Save()
    {
        var copy = chunks.ToDictionary(p => p.Key, p => p.Value.Copy());
        var state = new PopulationSnapshot { Chunks = copy.Values.ToList() };
        system.Server.WorldManager.SaveGame.StoreData(SaveKey,
            Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(state)));
    }

    // Last Watch migrates *stored counts*, never live/serialized entities.
    // Transactions are performed against day-start snapshots by LastWatchZones.
    public List<DormantChunk> MigrationSnapshot() => chunks.Values.Select(c => c.Copy()).ToList();

    // One atomic day transaction: removes all source counts before adding any
    // destination counts, so an arrival can never migrate twice that day.
    public bool ApplyMigration(IReadOnlyList<MigrationTransfer> transfers)
    {
        long countBefore = DormantCount;
        var totals=transfers.GroupBy(t=>(t.Source,t.Alpha)).ToArray();
        foreach(var group in totals)
        {
            if (!chunks.TryGetValue(group.Key.Source,out var source) || source.AwaitDeparture ||
                (group.Key.Alpha ? source.Alpha : source.Normal) < group.Sum(t=>(long)t.Count)) return false;
            // Destination preserves source dimension and height-band metadata;
            // actual spawn sites also search near destination terrain height.
            if (group.Any(t => t.DestDimension != source.Dimension || t.DestY != source.Y || t.Count <= 0))
                return false;
        }
        // Protect cooldown residents even when transactions come from another
        // integration rather than the daily planner. Validate before any debit.
        foreach (var t in transfers)
        {
            string destinationKey = $"{t.DestDimension}:{t.DestX}:{t.DestY}:{t.DestZ}";
            if (chunks.TryGetValue(destinationKey, out var destination) && destination.AwaitDeparture)
                return false;
        }
        var incoming=new List<(DormantChunk Dest, bool Alpha, int Count)>();
        foreach(var t in transfers)
        {
            var c=chunks[t.Source];
            incoming.Add((new DormantChunk { Dimension=t.DestDimension, Y=t.DestY,
                X=t.DestX, Z=t.DestZ },t.Alpha,t.Count));
            if(t.Alpha)c.Alpha-=t.Count;else c.Normal-=t.Count;
        }
        foreach(var t in incoming)
        {
            if (!chunks.TryGetValue(t.Dest.Key,out var to)) chunks[t.Dest.Key]=to=t.Dest;
            if(t.Alpha)to.Alpha+=t.Count;else to.Normal+=t.Count;
        }
        foreach(var key in chunks.Keys.ToArray())
            if(chunks[key].Alpha+chunks[key].Normal==0) RemoveChunk(key);
        RebuildIndex();
        if (DormantCount != countBefore)
            system.Server.Logger.Error($"Last Watch migration conservation ERROR: dormant {countBefore} before, {DormantCount} after. Report this to the mod author.");
        return true;
    }

    public bool MoveDormant(DormantChunk source, int destinationChunkX, int destinationChunkZ, bool alpha, int count)
    {
        if (count <= 0 || !chunks.TryGetValue(source.Key, out var from) || from.AwaitDeparture) return false;
        if ((alpha ? from.Alpha : from.Normal) < count) return false;
        // Do not relocate a protected cooldown population to bypass the departure rule.
        var dest = new DormantChunk { Dimension = from.Dimension, X = destinationChunkX,
            Y = from.Y, Z = destinationChunkZ };
        if (from.Key == dest.Key) return false;
        if (chunks.TryGetValue(dest.Key, out var existing) && existing.AwaitDeparture) return false;
        if (alpha) from.Alpha -= count; else from.Normal -= count;
        if (!chunks.TryGetValue(dest.Key, out var to)) chunks.Add(dest.Key, to = dest);
        if (alpha) to.Alpha += count; else to.Normal += count;
        Index(to);
        if (from.Normal + from.Alpha == 0) RemoveChunk(from.Key);
        return true;
    }

    public void AddPending(Vec3d pos, int dimension, bool alpha)
    {
        Add(Location(pos, dimension), alpha, awaitDeparture: false);
    }

    // Mass admin event seeding: dormant counts ONLY, never entity instances.
    // Deliberately bypasses the natural population storage cap (250 by default),
    // because /zombieween explicitly requests five per recorded zone. Existing
    // night-time, wake-batch, placement and active-entity caps remain unchanged.
    public int AddEventDormant(WatchZone zone, int count, int seaLevel)
    {
        if (count <= 0 || dormantCount > long.MaxValue - count) return 0;
        var loc = new DormantChunk {
            Dimension = zone.Dimension,
            X = checked(zone.X * WatchZone.ChunksPerSide + 1),
            Y = Math.Max(0, seaLevel / GlobalConstants.ChunkSize),
            Z = checked(zone.Z * WatchZone.ChunksPerSide + 1)
        };
        if (!chunks.TryGetValue(loc.Key, out var entry))
            chunks.Add(loc.Key, entry = loc);
        if (entry.Normal > long.MaxValue - count) return 0;
        entry.Normal += count;
        dormantCount += count;
        Index(entry);
        return count;
    }

    // Explicit admin test seed: wake as if this were an already stored
    // population. Does not bypass terrain, lighting or claim safeguards.
    public void ForceWakeCheck()
    {
        // Admin test wakes still respect the configured night window and cap.
        if (system.AutomaticSpawnWindowOpen() && !system.ActiveZombieCapReached) WakeNearby();
    }

    void Add(DormantChunk location, bool alpha, bool awaitDeparture)
    {
        // A full pool discards additional natural returns as well as seeded additions.
        // Existing saved counts are retained; migration only transfers them.
        if (DormantCount >= C.DormantPopulationCap) return;
        if (!chunks.TryGetValue(location.Key, out var entry))
            chunks.Add(location.Key, entry = location);
        if (alpha) entry.Alpha++; else entry.Normal++;
        dormantCount++;
        if (awaitDeparture) entry.AwaitDeparture = true;
        Index(entry);
    }

    public void OnLoaded(Entity entity)
    {
        if (entity is not Zombie z) return;
        if (z.IsDormant || z.WatchedAttributes.GetBool(DormantMarkerKey))
        {
            // Serialized remnant of a pooled entity; it must not add another count.
            corpses.Remove(z.EntityId);
            z.IsDormant = true;
            system.Server.Event.RegisterCallback(_ =>
            {
                Remove(z);
            }, 0);
        }
        else if (!z.Alive) corpses[z.EntityId] = z;
    }

    public void OnDespawn(Zombie z, EntityDespawnData reason)
    {
        corpses.Remove(z.EntityId);
        // Expiry clears Alive before this callback. Only permanent natural
        // removal enters the pool. Unload persists the entity in its chunk;
        // counting it here would duplicate it when that chunk loads again.
        if (reason == null || (reason.Reason != EnumDespawnReason.Expire &&
            reason.Reason != EnumDespawnReason.OutOfRange) ||
            z.WatchedAttributes.HasAttribute(Zombie.CorpseAgeKey) ||
            z.IsDormant || z.WatchedAttributes.GetBool(DormantMarkerKey)) return;

        // Count at the entity's actual last position before the engine drops it.
        // The marker makes callback retries and chunk unload/reload idempotent.
        z.IsDormant = true;
        z.WatchedAttributes.SetBool(DormantMarkerKey, true);
        Add(Location(SeventhNightSystem.LocalPosition(z.Pos), z.Pos.Dimension), z.IsAlpha,
            awaitDeparture: false);
    }

    void Remove(Zombie z)
    {
        if (system.Server.World.LoadedEntities.ContainsKey(z.EntityId))
            system.Server.World.DespawnEntity(z, new EntityDespawnData { Reason = EnumDespawnReason.Removed });
    }

    // Treat five chunks as a horizontal radius around a player's chunk:
    // a square of 11 x 11 chunk columns, independent of detection range and Y.
    // Never crosses dimensions; TryWake separately requires loaded, safe space.
    public static bool WithinWakeRadius(DormantChunk c, Vec3d playerPos, int playerDimension, int radiusChunks)
    {
        if (playerDimension != c.Dimension) return false;
        int size = GlobalConstants.ChunkSize;
        int px = (int)Math.Floor(playerPos.X / size);
        int pz = (int)Math.Floor(playerPos.Z / size);
        return Math.Abs(px - c.X) <= radiusChunks && Math.Abs(pz - c.Z) <= radiusChunks;
    }

    bool NearPlayer(DormantChunk c)
    {
        foreach (var player in system.Server.World.AllOnlinePlayers)
        {
            var e = player.Entity;
            // Disguised or sneaking survival players still wake dormant zombies.
            // Creative/Spectator players do not.
            if (e == null || !SeventhNightSystem.IsTarget(e)) continue;
            if (C.DormantWakeOnlyInSameZone)
            {
                // In the default zone-local wake mode the occupied 3x3-chunk zone is the only wake
                // trigger, regardless of which chunk stores the dormant count.
                if (WatchZone.FromChunk(c.X, c.Z, c.Dimension) ==
                    WatchZone.FromBlock((int)Math.Floor(e.Pos.X),
                        (int)Math.Floor(e.Pos.Z), e.Pos.Dimension))
                    return true;
            }
            else if (WithinWakeRadius(c, e.Pos.XYZ, e.Pos.Dimension, C.DormantWakeRadiusChunks))
                return true;
        }
        return false;
    }

    // Called only after successful daily migration. Wake up to today's
    // arriving count in an occupied destination chunk, not unrelated dormant
    // zombies which happen to have been stored there before this migration.
    // Failed attempts preserve the counts for ordinary wake retries.
    public int WakeMigrantsInOccupiedZones(IReadOnlyCollection<MigrationTransfer> transfers)
    {
        if (!system.AutomaticSpawnWindowOpen() || system.ActiveZombieCapReached) return 0;
        var occupied = system.Server.World.AllOnlinePlayers
            .Where(p => p.Entity != null && SeventhNightSystem.IsTarget(p.Entity))
            .Select(p => WatchZone.FromBlock((int)Math.Floor(p.Entity.Pos.X),
                (int)Math.Floor(p.Entity.Pos.Z), p.Entity.Pos.Dimension)).ToHashSet();
        if (occupied.Count == 0 || transfers.Count == 0) return 0;
        int budget = C.DormantWakeBatchSize;
        int count = 0, attemptedChunks = 0;
        var arrivals = transfers.GroupBy(t => (t.DestDimension, t.DestX, t.DestY, t.DestZ, t.Alpha));
        foreach (var group in arrivals)
        {
            if (budget <= 0) break;
            var key = group.Key;
            var zone = WatchZone.FromChunk(key.DestX, key.DestZ, key.DestDimension);
            if (!occupied.Contains(zone)) continue;
            string chunkKey = $"{key.DestDimension}:{key.DestX}:{key.DestY}:{key.DestZ}";
            if (!chunks.TryGetValue(chunkKey, out var chunk) || chunk.AwaitDeparture) continue;
            if (++attemptedChunks > C.DormantMaxChunkAttemptsPerCheck) break;
            long expected = group.Sum(t => (long)t.Count);
            long available = key.Alpha ? chunk.Alpha : chunk.Normal;
            long quota = Math.Min(expected, available);
            while (budget > 0 && quota-- > 0 && !system.ActiveZombieCapReached && system.AutomaticSpawnWindowOpen())
            {
                if (!TryWake(chunk, key.Alpha)) break;
                if (key.Alpha) chunk.Alpha--; else chunk.Normal--;
                dormantCount--;
                count++; budget--;
            }
            if (chunk.Normal + chunk.Alpha == 0) RemoveChunk(chunk.Key);
        }
        return count;
    }

    void WakeNearby()
    {
        if (!system.AutomaticSpawnWindowOpen() || system.ActiveZombieCapReached) return;
        using var timing = system.Profiler.Measure("population.wake");
        if (chunks.Count == 0) return;
        int budget = C.DormantWakeBatchSize;
        if (budget == 0) return;
        // Inspect every eligible stored chunk, not an arbitrary 64-chunk
        // slice: with large ruins maps that slice delayed waking by minutes.
        int restored = 0, blocked = 0;
        var candidates = system.Server.World.AllOnlinePlayers
            .Where(p => p.Entity != null && SeventhNightSystem.IsTarget(p.Entity))
            .SelectMany(p => CandidatesNear(p.Entity.Pos.XYZ, p.Entity.Pos.Dimension))
            .DistinctBy(c => c.Key).Where(c => !c.AwaitDeparture).ToArray();
        system.Profiler.Count("population.wakeCandidates", candidates.Length);
        int inspected = 0;
        int startIndex = candidates.Length == 0 ? 0 : wakeCursor % candidates.Length;
        for (int i = 0; i < candidates.Length && inspected < C.DormantMaxChunkAttemptsPerCheck; i++)
        {
            if (budget == 0) break;
            var c = candidates[(startIndex + i) % candidates.Length];
            inspected++;
            wakeCursor = (startIndex + i + 1) % candidates.Length;
            system.Profiler.Count("population.wakeChunksTried");
            // Prefer Alpha so ordinary zombies can join its pack.
            while (budget > 0 && c.Normal + c.Alpha > 0 &&
                !system.ActiveZombieCapReached && system.AutomaticSpawnWindowOpen())
            {
                bool alpha = c.Alpha > 0;
                if (!TryWake(c, alpha)) { blocked++; break; }
                if (alpha) c.Alpha--; else c.Normal--;
                dormantCount--;
                budget--;
                restored++;
            }
            if (c.Normal + c.Alpha == 0) RemoveChunk(c.Key);
        }
        if (C.DormantDebugLogging && (restored > 0 || blocked > 0))
            system.Server.Logger.Notification($"Seventh Night Zombies: dormant wake scan restored {restored}, " +
                $"blocked by unloaded/unsafe space {blocked}, remaining dormant pool {DormantCount}.");
    }

    bool TryWake(DormantChunk c, bool alpha)
    {
        // Defense in depth: direct wake calls must obey exactly the same
        // global schedule/cap as ambient reactivation.
        if (!system.AutomaticSpawnWindowOpen() || system.ActiveZombieCapReached) return false;
        system.Profiler.Count("population.wakeSpawnAttempts");
        var world = system.Server.World;
        var zone = WatchZone.FromChunk(c.X, c.Z, c.Dimension);
        if (system.WorldDirector?.CanReactivateDormant(zone) != true) return false;
        var eligiblePlayers = system.Server.World.AllOnlinePlayers
            .Where(p => p.Entity != null && SeventhNightSystem.IsTarget(p.Entity) &&
                p.Entity.Pos.Dimension == c.Dimension &&
                // Zone-local wake requires this particular target in the zone;
                // optional legacy wake radius also checks each player directly.
                (C.DormantWakeOnlyInSameZone
                    ? WatchZone.FromBlock((int)Math.Floor(p.Entity.Pos.X),
                        (int)Math.Floor(p.Entity.Pos.Z), c.Dimension) == zone
                    : WithinWakeRadius(c, p.Entity.Pos.XYZ, c.Dimension,
                        C.DormantWakeRadiusChunks)))
            .Select(p => p.Entity).ToArray();
        if (eligiblePlayers.Length == 0) return false;
        int attempts = Math.Max(48, C.SpawnPositionAttempts);
        int minimum = C.SpawnMinDistance;
        int maximum = C.SpawnMaxDistance;
        // Direct dormant wakeups also pick player-centered columns inside the
        // stored zombie's zone, avoiding cross-zone/unloaded samples at the
        // outer edge of the ring. Nearby columns are preferred; far columns
        // remain available as a fallback.
        foreach (var player in eligiblePlayers.OrderBy(_ => world.Rand.Next()))
        {
            for (int attempt = 0; attempt < attempts; attempt++)
            {
                int sampleRadius = attempt < attempts * 3 / 4
                    ? Math.Min(maximum, minimum + 20) : maximum;
                int size = WatchZone.ChunksPerSide * GlobalConstants.ChunkSize;
                int minX = Math.Max((int)Math.Floor(player.Pos.X - sampleRadius), zone.X * size);
                int maxX = Math.Min((int)Math.Floor(player.Pos.X + sampleRadius), (zone.X + 1) * size - 1);
                int minZ = Math.Max((int)Math.Floor(player.Pos.Z - sampleRadius), zone.Z * size);
                int maxZ = Math.Min((int)Math.Floor(player.Pos.Z + sampleRadius), (zone.Z + 1) * size - 1);
                if (maxX < minX || maxZ < minZ) continue;
                int x = world.Rand.Next(minX, maxX + 1);
                int z = world.Rand.Next(minZ, maxZ + 1);
                double dx = x + .5 - player.Pos.X;
                double dz = z + .5 - player.Pos.Z;
                double radiusSquared = dx * dx + dz * dz;
                if (radiusSquared < (double)minimum * minimum ||
                    radiusSquared > (double)maximum * maximum) continue;
                int surface = world.BlockAccessor.GetTerrainMapheightAt(
                    new BlockPos(x, 0, z, c.Dimension));
                // Surface-only reactivation: use the same short height search as
                // ambient population spawning, respecting the base spawn schedule.
                for (int step = 0; step <= 2 * C.SpawnSurfaceVerticalTolerance; step++)
                {
                    int offset = step == 0 ? 0 : (step % 2 == 1 ? (step + 1) / 2 : -step / 2);
                    int y = surface + 1 + offset;
                    if (y < 1 || Math.Abs(y - player.Pos.Y) > 32) continue;
                    var feet = new BlockPos(x, y, z, c.Dimension);
                    var head = feet.UpCopy();
                    var ground = feet.DownCopy();
                    if (world.BlockAccessor.GetChunkAtBlockPos(feet) == null ||
                        world.BlockAccessor.GetChunkAtBlockPos(head) == null ||
                        world.BlockAccessor.GetChunkAtBlockPos(ground) == null) continue;
                    if (!system.IsClearSpawnSpace(feet) || !system.IsClearSpawnSpace(head) ||
                        !world.BlockAccessor.GetBlock(ground).SideSolid[BlockFacing.UP.Index]) continue;
                    if (C.ProtectClaims && (world.Claims.Get(feet)?.Length ?? 0) > 0) continue;
                    // MaxTimeOfDayLight uses sunlight AND emitted/block light.
                    // Block-light-only checks used to wake zombies in daylight.
                    if (!Rules.IsSpawnLightSafe(world.BlockAccessor.GetLightLevel(feet,
                            EnumLightLevelType.MaxTimeOfDayLight), C) ||
                        !Rules.IsSpawnLightSafe(world.BlockAccessor.GetLightLevel(head,
                            EnumLightLevelType.MaxTimeOfDayLight), C)) continue;
                    var pos = new Vec3d(x + .5, y, z + .5);
                    // Preserve the twenty-block minimum around *all* players.
                    if (world.AllOnlinePlayers.Any(p => p.Entity != null &&
                        p.Entity.Pos.Dimension == c.Dimension &&
                        Math.Pow(p.Entity.Pos.X - pos.X, 2) +
                        Math.Pow(p.Entity.Pos.Z - pos.Z, 2) <
                        (double)C.SpawnMinDistance * C.SpawnMinDistance)) continue;
                    if (world.GetEntitiesAround(pos, 1, 2, e => e is Zombie && e.Alive).Length > 0) continue;
                    if (!system.Spawn(pos, c.Dimension, alpha, restoring: true)) return false;
                    system.WorldDirector.RecordDormantReactivation(zone);
                    return true;
                }
            }
        }
        return false;
    }
}
