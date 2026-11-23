using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeventhNightZombies;

// Pure rules shared by the runtime and regression tests.
public static class PalisadeTrapRules
{
    public const double Radius = 2;
    public const float Damage = 5;
    public static bool Matches(AssetLocation code) => code?.Domain == "game" &&
        code.Path is "palisadestakes-west" or "palisadestakes-east" or
            "palisadestakes-north" or "palisadestakes-south";
    public static int Duration(Random random) => random.Next(3, 21);
    // Distance from the zombie's feet to the block cell, including height.
    // No visibility, burial or intervening-wall checks by design.
    public static bool WithinRange(double x, double y, double z, int bx, int by, int bz)
    {
        double dx = Math.Max(0, Math.Max(bx - x, x - (bx + 1)));
        double dy = Math.Max(0, Math.Max(by - y, y - (by + 1)));
        double dz = Math.Max(0, Math.Max(bz - z, z - (bz + 1)));
        return dx * dx + dy * dy + dz * dz <= Radius * Radius;
    }
}

public sealed class PalisadeTrapState
{
    public double Remaining { get; private set; }
    public PalisadeTrapState(double remaining = 0)
    {
        Remaining = double.IsFinite(remaining) ? Math.Clamp(remaining, 0, 20) : 0;
    }
    public bool TryCatch(Random random)
    {
        if (Remaining > 0) return false;
        Remaining = PalisadeTrapRules.Duration(random);
        return true;
    }
    public void Advance(float dt)
    {
        if (float.IsFinite(dt) && dt > 0) Remaining = Math.Max(0, Remaining - dt);
    }
}

public partial class Zombie
{
    const string PalisadeRemainingKey = "thelastwatch-palisade-trap-seconds";
    PalisadeTrapState palisadeTrap;
    long nextPalisadeScanMs;

    void LoadPalisadeTrap()
    {
        palisadeTrap = new(Attributes.GetDouble(PalisadeRemainingKey));
        nextPalisadeScanMs = World.ElapsedMilliseconds + World.Rand.Next(1000);
        if (palisadeTrap.Remaining > 0) StopNavigation();
    }

    void SavePalisadeTrap()
    {
        // Server-only Attributes persist with the entity, without network spam.
        Attributes.SetDouble(PalisadeRemainingKey, palisadeTrap.Remaining);
    }

    void HoldPalisadeMovement()
    {
        Controls.StopAllMovement();
        Controls.WalkVector.Set(0, 0, 0);
        Controls.FlyVector.Set(0, 0, 0);
        Pos.Motion.X = Pos.Motion.Z = 0;
        // Prevent climbing/jumping out; gravity can still bring it down.
        if (Pos.Motion.Y > 0) Pos.Motion.Y = 0;
        SetWalking(false);
    }

    bool TickPalisadeTrap(float dt)
    {
        if (palisadeTrap == null) return false;
        if (palisadeTrap.Remaining > 0)
        {
            palisadeTrap.Advance(dt);
            SavePalisadeTrap();
            if (palisadeTrap.Remaining > 0) { HoldPalisadeMovement(); return true; }
            nextRouteMs = nextPreyScanMs = nextIdleRouteMs = nextFollowScanMs = World.ElapsedMilliseconds;
            return false;
        }
        if (World.ElapsedMilliseconds < nextPalisadeScanMs) return false;
        nextPalisadeScanMs = World.ElapsedMilliseconds + 1000;
        if (system.SharpenedStakeBlockIds.Count == 0 || system.LooseStickBlockId <= 0 || !system.StakeScanBudget.TakeScan(World.ElapsedMilliseconds, 64)) return false;
        int x0 = (int)Math.Floor(Pos.X - 2) - 1, x1 = (int)Math.Floor(Pos.X + 2);
        int y0 = Math.Max(0, (int)Math.Floor(Pos.Y - 2) - 1), y1 = (int)Math.Floor(Pos.Y + 2);
        int z0 = (int)Math.Floor(Pos.Z - 2) - 1, z1 = (int)Math.Floor(Pos.Z + 2);
        var blockPos = new BlockPos(0, 0, 0, Pos.Dimension);
        for (int y = y0; y <= y1; y++)
        for (int x = x0; x <= x1; x++)
        for (int z = z0; z <= z1; z++)
        {
            if (!PalisadeTrapRules.WithinRange(Pos.X, Pos.Y, Pos.Z, x, y, z)) continue;
            blockPos.Set(x, y, z);
            if (World.BlockAccessor.GetChunkAtBlockPos(blockPos) == null) continue;
            var block = World.BlockAccessor.GetBlock(blockPos);
            if (!system.SharpenedStakeBlockIds.Contains(block.Id)) continue;
            if (!palisadeTrap.TryCatch(World.Rand)) return false;
            // Commit before damage, so death/save callbacks cannot retrigger it.
            SavePalisadeTrap();
            system.TriggerStake(blockPos, block.Id);
            StopNavigation();
            leapUntilMs = 0; // cancel an in-flight Grabber leap
            HoldPalisadeMovement();
            ReceiveDamage(new DamageSource {
                Source = EnumDamageSource.Block,
                Type = EnumDamageType.PiercingAttack,
                IgnoreInvFrames = true
            }, PalisadeTrapRules.Damage);
            return true;
        }
        return false;
    }
}
