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
    // Snapshots written before 2.0.10 also carry "AwaitDeparture": false. That
    // retired flag was never set; it is ignored on load and no longer written.
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
    void Index(DormantChunk c)
    {
        var zone = WatchZone.FromChunk(c.X, c.Z, c.Dimension);
        if (!byZone.TryGetValue(zone, out var keys)) byZone[zone] = keys = new();
        keys.Add(c.Key);
    }
    void RemoveChunk(string key)
    {
        if (!chunks.Remove(key, out var c)) return;
        var zone = WatchZone.FromChunk(c.X, c.Z, c.Dimension);
        if (byZone.TryGetValue(zone, out var keys))
        { keys.Remove(key); if (keys.Count == 0) byZone.Remove(zone); }
    }
    void RebuildIndex()
    {
        byZone.Clear();
        foreach (var c in chunks.Values) Index(c);
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
    // Authoritative total, recomputed from the stored chunk counts.
    public long CountStored() => chunks.Values.Sum(c => c.Normal + c.Alpha);
    // The running counter must always equal the stored counts. A mismatch is
    // logged and the counter repaired; stored counts are never changed.
    public bool VerifyDormantCount()
    {
        long actual = CountStored();
        if (actual == dormantCount) return true;
        system.Server?.Logger.Error($"Last Watch dormant counter mismatch: counter {dormantCount}, stored {actual}. Counter repaired; report this to the mod author.");
        dormantCount = actual;
        return false;
    }
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
        long countBefore = CountStored();
        var totals=transfers.GroupBy(t=>(t.Source,t.Alpha)).ToArray();
        foreach(var group in totals)
        {
            if (!chunks.TryGetValue(group.Key.Source,out var source) ||
                (group.Key.Alpha ? source.Alpha : source.Normal) < group.Sum(t=>(long)t.Count)) return false;
            // Destination preserves source dimension and height-band metadata;
            // actual spawn sites also search near destination terrain height.
            if (group.Any(t => t.DestDimension != source.Dimension || t.DestY != source.Y || t.Count <= 0))
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
        long countAfter = CountStored();
        if (countAfter != countBefore)
            system.Server?.Logger.Error($"Last Watch migration conservation ERROR: dormant {countBefore} before, {countAfter} after. Report this to the mod author.");
        VerifyDormantCount();
        return true;
    }

    public void AddPending(Vec3d pos, int dimension, bool alpha)
    {
        Add(Location(pos, dimension), alpha);
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

    void Add(DormantChunk location, bool alpha)
    {
        // A full pool discards additional natural returns as well as seeded additions.
        // Existing saved counts are retained; migration only transfers them.
        if (DormantCount >= C.DormantPopulationCap) return;
        if (!chunks.TryGetValue(location.Key, out var entry))
            chunks.Add(location.Key, entry = location);
        if (alpha) entry.Alpha++; else entry.Normal++;
        dormantCount++;
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
        Add(Location(SeventhNightSystem.LocalPosition(z.Pos), z.Pos.Dimension), z.IsAlpha);
    }

    void Remove(Zombie z)
    {
        if (system.Server.World.LoadedEntities.ContainsKey(z.EntityId))
            system.Server.World.DespawnEntity(z, new EntityDespawnData { Reason = EnumDespawnReason.Removed });
    }

}
