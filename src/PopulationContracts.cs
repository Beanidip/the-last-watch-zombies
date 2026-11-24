using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SeventhNightZombies;

// Shared ABI; base has no assembly reference to the optional expansion.
public interface IZombiePopulation
{
    string ApproximatePopulationSummary { get; }
    void Load();
    void Save();
    void OnLoaded(Entity entity);
    void OnDespawn(Zombie zombie, EntityDespawnData reason);
    void OnZombieDied(Zombie zombie);
    Zombie[] DeadCorpses();
    void AddPending(Vec3d pos, int dimension, bool alpha);
    bool HasDormantInZone(WatchZone zone);
    bool TryReactivateAtZone(WatchZone zone, Vec3d pos, int dimension);
}

public interface IZombieWorldDirector
{
    bool CanAmbientPopulationSpawn(WatchZone zone, string playerUid);
    bool CanAddAmbientPopulationSpawn(WatchZone zone);
    bool CanAddAmbientPopulationSpawn(WatchZone zone, string playerUid);
    void RecordAmbientPopulationSpawn(WatchZone zone, string playerUid);
    bool CanReactivateDormant(WatchZone zone);
    void RecordDormantReactivation(WatchZone zone);
}
