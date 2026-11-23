using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;

namespace SeventhNightZombies;

public partial class SeventhNightSystem
{
    const string SpawnPauseKey = "lastwatch-zombie-spawns-paused";
    public const string RotPoisonKey = "lastwatch-rot-poison-seconds";
    public bool SpawnsPaused { get; private set; }
    readonly WorkBudget specialSpawnBudget = new();
    public void SetSpawnsPaused(bool paused)
    {
        Server.WorldManager.SaveGame.StoreData(SpawnPauseKey, new[] { (byte)(paused ? 1 : 0) });
        SpawnsPaused = paused;
        if (paused) pendingDeathAlphas.Clear();
    }
    void RegisterVariantCommands(ICoreServerAPI api)
    {
        api.ChatCommands.Create("zombiepause").RequiresPrivilege("controlserver")
            .WithDescription("Pause all new zombie spawns immediately, including hordes and dormant returns")
            .HandleWith(_ => { SetSpawnsPaused(true); return TextCommandResult.Success("Zombie spawning PAUSED. Existing zombies remain active. Saved across restarts."); });
        api.ChatCommands.Create("zombieunpause").RequiresPrivilege("controlserver")
            .WithDescription("Resume normal zombie spawning immediately")
            .HandleWith(_ => { SetSpawnsPaused(false); return TextCommandResult.Success("Zombie spawning RESUMED. Normal quotas, lighting and caps apply."); });
        api.ChatCommands.Create("zombiespawn").RequiresPrivilege("controlserver")
            .WithDescription("Spawn a test variant: normal, alpha, grabber, hive, screamer or bloater")
            .WithArgs(api.ChatCommands.Parsers.Word("variant"))
            .HandleWith(args =>
            {
                if (SpawnsPaused) return TextCommandResult.Error("Spawning is paused. Use /zombieunpause first.");
                if (!Enum.TryParse<ZombieVariant>((args[0] as string) ?? "", true, out var kind) || !Enum.IsDefined(kind))
                    return TextCommandResult.Error("Use normal, alpha, grabber, hive, screamer or bloater.");
                var player = args.Caller.Player?.Entity;
                if (player == null) return TextCommandResult.Error("Run this command in-game.");
                return TrySpawnSpecialNear(LocalPosition(player.Pos), player.Pos.Dimension, kind)
                    ? TextCommandResult.Success("Spawned " + ZombieVariantRules.Name(kind) + ".")
                    : TextCommandResult.Error("No safe nearby space, or active cap reached.");
            });
    }
    void OnNewZombie(Entity entity)
    {
        // Covers creative spawn items / other mods; loading existing saved zombies is separate.
        if (SpawnsPaused && entity is Zombie z)
        {
            z.IsDormant = true;
            Server.Event.RegisterCallback(_ => Server.World.DespawnEntity(z,
                new EntityDespawnData { Reason = EnumDespawnReason.Removed }), 0);
            return;
        }
        TrackZombie(entity);
    }
    public bool CanFitVariant(Vec3d pos, int dimension, double radius, double height = 1.85)
    {
        for (int x = (int)Math.Floor(pos.X - radius); x <= (int)Math.Floor(pos.X + radius); x++)
        for (int z = (int)Math.Floor(pos.Z - radius); z <= (int)Math.Floor(pos.Z + radius); z++)
        for (int y = (int)Math.Floor(pos.Y); y <= (int)Math.Floor(pos.Y + height - .01); y++)
        {
            var block = new BlockPos(x, y, z, dimension);
            if (Server.World.BlockAccessor.GetChunkAtBlockPos(block) == null || !IsClearSpawnSpace(block)) return false;
        }
        return true;
    }
    public bool TrySpawnScreamerReinforcement(Zombie source)
    {
        if (SpawnsPaused || ActiveZombieCapReached || !source.Alive || source.IsDormant ||
            !specialSpawnBudget.TakeRoute(Server.World.ElapsedMilliseconds, 4)) return false;
        // Always ordinary: summoned zombies cannot trigger a Screamer chain.
        return TrySpawnSpecialNear(LocalPosition(source.Pos), source.Pos.Dimension, ZombieVariant.Normal);
    }
    bool TrySpawnSpecialNear(Vec3d origin, int dimension, ZombieVariant kind)
    {
        if (SpawnsPaused || ActiveZombieCapReached) return false;
        var world = Server.World;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            double angle = world.Rand.NextDouble() * Math.PI * 2;
            double range = 4 + world.Rand.NextDouble() * 6;
            int x = (int)Math.Floor(origin.X + Math.Cos(angle) * range);
            int z = (int)Math.Floor(origin.Z + Math.Sin(angle) * range);
            for (int dy = 2; dy >= -3; dy--)
            {
                var feet = new BlockPos(x, (int)Math.Floor(origin.Y) + dy, z, dimension);
                if (world.BlockAccessor.GetChunkAtBlockPos(feet.DownCopy()) == null ||
                    !world.BlockAccessor.GetBlock(feet.DownCopy()).SideSolid[BlockFacing.UP.Index]) continue;
                var point = new Vec3d(x + .5, feet.Y, z + .5);
                if (!CanFitVariant(point, dimension, kind == ZombieVariant.Bloater ? .48 : .33,
                        ZombieVariantRules.Height(kind)) ||
                    (Settings.ProtectClaims && (world.Claims.Get(feet)?.Length ?? 0) > 0) ||
                    world.GetEntitiesAround(new Vec3d(point.X, feet.InternalY, point.Z), 1, 2,
                        e => e is EntityAgent && e.Alive).Length > 0) continue;
                return Spawn(point, dimension, variant: kind);
            }
        }
        return false;
    }
    public bool TryReleaseBees(Zombie source)
    {
        if (SpawnsPaused || !source.Alive || source.IsDormant ||
            !specialSpawnBudget.TakeScan(Server.World.ElapsedMilliseconds, 2)) return false;
        var world = Server.World;
        // Vanilla spatial query on a 30s emitter cooldown; avoid piling up nearby swarms.
        if (world.GetEntitiesAround(source.Pos.XYZ, 16, 16,
            e => e.Alive && e.Code?.ToString() == "seventhnightzombies:zombiebees").Length >= 3) return false;
        var type = world.GetEntityType(new AssetLocation("seventhnightzombies:zombiebees"));
        if (type == null) return false;
        var at = LocalPosition(source.Pos).Add(0, 1, 0);
        if (!IsClearSpawnSpace(new BlockPos((int)Math.Floor(at.X), (int)Math.Floor(at.Y), (int)Math.Floor(at.Z), source.Pos.Dimension))) return false;
        var bees = world.ClassRegistry.CreateEntity(type);
        bees.Pos.SetPos(at); bees.Pos.Dimension = source.Pos.Dimension;
        bees.Attributes.SetString("origin", "brokenbeehive");
        world.SpawnEntity(bees);
        return true;
    }
    void TickRotPoison(EntityPlayer player, float dt)
    {
        double remaining = player.Attributes.GetDouble(RotPoisonKey);
        if (remaining <= 0) return;
        if (!IsTarget(player)) { player.Attributes.RemoveAttribute(RotPoisonKey); return; }
        double elapsed = Math.Min(remaining, Math.Clamp(dt, 0, 1));
        remaining -= elapsed;
        if (remaining <= 0) player.Attributes.RemoveAttribute(RotPoisonKey);
        else player.Attributes.SetDouble(RotPoisonKey, remaining);
        player.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Internal,
            Type = EnumDamageType.Poison, IgnoreInvFrames = true },
            (float)(Settings.BloaterPoisonDamagePerSecond * elapsed));
    }
}
