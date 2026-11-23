using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;

namespace SeventhNightZombies;

public enum ZombieVariant { Normal, Alpha, Grabber, Hive, Screamer, Bloater }

public static class ZombieVariantRules
{
    // ARGB, as consumed by the vanilla EntityShapeRenderer. No extra textures,
    // shaders, watched attributes or per-tick color processing are needed.
    public static int Tint(ZombieVariant v) => unchecked((int)(v switch {
        ZombieVariant.Grabber => 0xFFD9F2E6u, // cool green
        ZombieVariant.Screamer => 0xFFE8D2EDu, // bruised mauve
        ZombieVariant.Hive => 0xFFF3E1BAu, // warm straw
        _ => 0xFFFFFFFFu }));
    public static double Height(ZombieVariant v) => v switch {
        ZombieVariant.Grabber => 2.25, ZombieVariant.Screamer => 2.2,
        ZombieVariant.Hive => 2.15, _ => 1.85 };
    public static string Code(ZombieVariant v) => v switch {
        ZombieVariant.Alpha => "alphazombie", ZombieVariant.Grabber => "grabber",
        ZombieVariant.Hive => "hivezombie", ZombieVariant.Screamer => "screamer",
        ZombieVariant.Bloater => "bloater", _ => "zombie" };
    public static string Name(ZombieVariant v) => v switch {
        ZombieVariant.Alpha => "Alpha Zombie", ZombieVariant.Grabber => "Grabber",
        ZombieVariant.Hive => "Hive Zombie", ZombieVariant.Screamer => "Screamer",
        ZombieVariant.Bloater => "Bloater", _ => "Zombie" };
    public static ZombieVariant FromCode(string code) => code switch {
        "alphazombie" => ZombieVariant.Alpha, "grabber" => ZombieVariant.Grabber,
        "hivezombie" => ZombieVariant.Hive, "screamer" => ZombieVariant.Screamer,
        "bloater" => ZombieVariant.Bloater, _ => ZombieVariant.Normal };
    public static ZombieVariant Roll(double roll, Config c)
    {
        if (roll < c.GrabberSpawnChance) return ZombieVariant.Grabber;
        roll -= c.GrabberSpawnChance;
        if (roll < c.HiveSpawnChance) return ZombieVariant.Hive;
        roll -= c.HiveSpawnChance;
        if (roll < c.ScreamerSpawnChance) return ZombieVariant.Screamer;
        roll -= c.ScreamerSpawnChance;
        return roll < c.BloaterSpawnChance ? ZombieVariant.Bloater : ZombieVariant.Normal;
    }
    public static Vec3d LeapMotion(double dx, double dz)
    {
        double length = Math.Sqrt(dx * dx + dz * dz);
        return length < .01 ? new Vec3d(0, .18, 0) : new Vec3d(dx / length * .22, .18, dz / length * .22);
    }
}

public partial class Zombie
{
    public ZombieVariant VariantKind => ZombieVariantRules.FromCode(Properties?.Code?.Path);
    float VariantSpeedMultiplier => VariantKind switch {
        ZombieVariant.Alpha => C.AlphaSpeedMultiplier, ZombieVariant.Bloater => .7f,
        ZombieVariant.Grabber => 1.15f, ZombieVariant.Hive => .9f, _ => 1f };
    const string AbilityCooldownKey = "lastwatch-ability-cooldown";
    const string ScreamUsedKey = "lastwatch-scream-used";
    const string ScreamRemainingKey = "lastwatch-scream-remaining";
    const string ScreamRetryKey = "lastwatch-scream-retry-seconds";
    const string BurstUsedKey = "lastwatch-burst-used";
    double variantTickSeconds;
    long leapUntilMs, leapStartedMs;

    // Scalar timers only. No trail history, per-frame scans or identity lists.
    bool TickVariant(float dt)
    {
        if (leapUntilMs > 0)
        {
            long now = World.ElapsedMilliseconds;
            if (now < leapUntilMs && (!OnGround || now - leapStartedMs < 100)) return true;
            leapUntilMs = 0;
        }
        if (VariantKind is ZombieVariant.Normal or ZombieVariant.Alpha or ZombieVariant.Bloater) return false;
        variantTickSeconds += Math.Clamp(dt, 0, .5f);
        if (variantTickSeconds < .5) return false;
        double elapsed = variantTickSeconds;
        variantTickSeconds = 0;
        double cooldown = Math.Max(0, Attributes.GetDouble(AbilityCooldownKey) - elapsed);
        Attributes.SetDouble(AbilityCooldownKey, cooldown);

        // Pausing prevents new swarms and summons but doesn't freeze existing AI.
        if (VariantKind == ZombieVariant.Screamer && !system.SpawnsPaused &&
            Attributes.GetInt(ScreamRemainingKey) > 0)
        {
            double remainingTime = Attributes.GetDouble(ScreamRetryKey) - elapsed;
            Attributes.SetDouble(ScreamRetryKey, remainingTime);
            if (remainingTime <= 0) Attributes.SetInt(ScreamRemainingKey, 0);
            else if (system.TrySpawnScreamerReinforcement(this))
                Attributes.SetInt(ScreamRemainingKey, Attributes.GetInt(ScreamRemainingKey) - 1);
        }
        if (target == null || !system.CanZombieTarget(target) || target.Pos.Dimension != Pos.Dimension) return false;
        double distance = Pos.SquareDistanceTo(target.Pos.XYZ);
        if (VariantKind == ZombieVariant.Grabber && cooldown <= 0 && OnGround &&
            !Swimming && distance >= 4 && distance <= 49 && Math.Abs(target.Pos.Y - Pos.Y) < 3 && ClearAttackLine(target))
        {
            StopNavigation();
            SetWalking(false);
            Vec3d impulse = ZombieVariantRules.LeapMotion(target.Pos.X - Pos.X, target.Pos.Z - Pos.Z);
            Pos.Motion.Set(impulse);
            OnGround = false;
            Controls.Jump = false;
            leapStartedMs = World.ElapsedMilliseconds;
            leapUntilMs = leapStartedMs + 1400;
            Attributes.SetDouble(AbilityCooldownKey, C.GrabberLeapCooldownSeconds);
            return true;
        }
        if (system.SpawnsPaused || distance > 256) return false;
        if (VariantKind == ZombieVariant.Hive && cooldown <= 0 && ClearAttackLine(target))
        {
            // One attempt per cooldown, even when placement/global budget rejects it.
            Attributes.SetDouble(AbilityCooldownKey, C.HiveBeeCooldownSeconds);
            system.TryReleaseBees(this);
        }
        if (VariantKind == ZombieVariant.Screamer && !Attributes.GetBool(ScreamUsedKey) && ClearAttackLine(target))
        {
            Attributes.SetBool(ScreamUsedKey, true);
            Attributes.SetInt(ScreamRemainingKey, 10);
            Attributes.SetDouble(ScreamRetryKey, 60);
            World.PlaySoundAt(new AssetLocation("seventhnightzombies:sounds/screamer"), this, null, 1f, 48, 1f);
        }
        return false;
    }

    void OnVariantKilled()
    {
        if (VariantKind != ZombieVariant.Bloater || WatchedAttributes.GetBool(BurstUsedKey)) return;
        WatchedAttributes.SetBool(BurstUsedKey, true);
        Drop("game:rot", C.BloaterRotDrop);
        // One bounded player pass only when killed. Never on expiry or chunk unload.
        foreach (var player in World.AllOnlinePlayers)
        {
            var victim = player.Entity;
            if (victim == null || !SeventhNightSystem.IsTarget(victim) || victim.Pos.Dimension != Pos.Dimension ||
                Pos.SquareDistanceTo(victim.Pos.XYZ) > C.BloaterBurstRadius * C.BloaterBurstRadius || !ClearAttackLine(victim)) continue;
            victim.ReceiveDamage(new DamageSource { Source = EnumDamageSource.Entity,
                SourceEntity = this, Type = EnumDamageType.Poison }, C.BloaterBurstDamage);
            if (victim.Alive)
                victim.Attributes.SetDouble(SeventhNightSystem.RotPoisonKey,
                    Math.Max(victim.Attributes.GetDouble(SeventhNightSystem.RotPoisonKey), C.BloaterPoisonSeconds));
        }
        World.SpawnParticles(55f, ColorUtil.ColorFromRgba(35, 100, 75, 210),
            Pos.XYZ.AddCopy(-.5, .2, -.5), Pos.XYZ.AddCopy(.5, 1.5, .5),
            new Vec3f(-.15f, .05f, -.15f), new Vec3f(.15f, .2f, .15f),
            1f, .2f, .25f, EnumParticleModel.Cube);
    }
}

// Uses vanilla bee rendering/AI, with a saved short lifetime independent of players.
public sealed class TemporaryZombieSwarm : EntityBehavior
{
    public TemporaryZombieSwarm(Entity entity) : base(entity) { }
    public override string PropertyName() => "lastwatchtemporaryswarm";
    public override void OnGameTick(float deltaTime)
    {
        if (entity.Api.Side != EnumAppSide.Server || !entity.Alive) return;
        double age = entity.Attributes.GetDouble("lastwatch-swarm-age") + Math.Clamp(deltaTime, 0, 1);
        entity.Attributes.SetDouble("lastwatch-swarm-age", age);
        if (age >= 20) entity.Die(EnumDespawnReason.Expire);
    }
}
