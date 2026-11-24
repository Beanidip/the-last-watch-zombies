using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeventhNightZombies;

public static class RotBaitRules
{
    public const double Radius = 32;
    public const long EatIntervalMs = 2000;
    public static bool IsBait(ItemStack stack) => stack?.StackSize > 0 &&
        stack.Collectible?.Code?.ToString() == "seventhnightzombies:rotconcentrate";
    public static bool InRange(Vec3d zombie, int dimension, BlockPos pile) =>
        dimension == pile.dimension && zombie.SquareDistanceTo(pile.X + .5, pile.Y + .2, pile.Z + .5) <= Radius * Radius;
    public static ItemSlot FoodSlot(InventoryBase inventory) =>
        inventory?.FirstOrDefault(slot => IsBait(slot?.Itemstack));
    // A zombie that makes no progress toward a pile for GiveUpMs (it is not
    // getting at least ProgressBlocks closer, and is not standing at the pile
    // with a clear bite) ignores that pile for IgnoreMs and resumes hunting.
    public const long GiveUpMs = 15000;
    public const long IgnoreMs = 60000;
    public const double ProgressBlocks = 1;
}

// Per-zombie, in-memory progress tracker for one bait pile. Pure logic so the
// give-up rule is unit-testable without a world.
public sealed class BaitPursuitState
{
    public BlockPos Pile { get; private set; }
    double bestDistance;
    long deadlineMs;
    BlockPos ignoredPile;
    long ignoredUntilMs;
    public void Begin(BlockPos pile, double distance, long now)
    {
        Pile = pile;
        bestDistance = distance;
        deadlineMs = now + RotBaitRules.GiveUpMs;
    }
    // Returns true when the zombie should give up on the current pile.
    public bool Update(double distance, bool atPile, long now)
    {
        if (Pile == null) return false;
        if (atPile || distance <= bestDistance - RotBaitRules.ProgressBlocks)
        {
            bestDistance = Math.Min(bestDistance, distance);
            deadlineMs = now + RotBaitRules.GiveUpMs;
        }
        return now >= deadlineMs;
    }
    public void GiveUp(long now)
    {
        if (Pile != null) { ignoredPile = Pile.Copy(); ignoredUntilMs = now + RotBaitRules.IgnoreMs; }
        Pile = null;
    }
    public void Clear() => Pile = null;
    public BlockPos Ignored(long now) => ignoredPile != null && now < ignoredUntilMs ? ignoredPile : null;
}

// Only loaded vanilla ground-storage piles that hold concentrate are indexed.
// Their inventories remain the sole saved authority; no zombie IDs or dormant
// data are added. The index is updated from inventory events, so ordinary
// ground piles (firewood, bricks...) cost nothing after they load.
public sealed class RotBaitSystem : ModSystem
{
    readonly Dictionary<(int D, int X, int Y, int Z), BlockEntityGroundStorage> piles = new();
    readonly Dictionary<(int D, int X, int Y, int Z), long> nextBite = new();
    static (int,int,int,int) Key(BlockPos p) => (p.dimension,p.X,p.Y,p.Z);
    public override void Start(ICoreAPI api) => api.RegisterBlockEntityBehaviorClass("LastWatchRotBait", typeof(RotBaitBehavior));
    public override void StartServerSide(Vintagestory.API.Server.ICoreServerAPI api)
    {
        // Cheap safety sweep over INDEXED piles only (e.g. concentrate that
        // perished into rot without an inventory event).
        api.Event.RegisterGameTickListener(_ => Prune(api.World), 10000);
    }
    void Prune(IWorldAccessor world)
    {
        foreach (var pile in piles.Values.ToArray())
            if (!ReferenceEquals(world.BlockAccessor.GetBlockEntity(pile.Pos), pile) ||
                RotBaitRules.FoodSlot(pile.Inventory) == null) Remove(pile);
    }
    public int IndexedPiles => piles.Count;
    public void Refresh(BlockEntityGroundStorage pile)
    {
        if (RotBaitRules.FoodSlot(pile.Inventory) != null) piles[Key(pile.Pos)] = pile;
        else Remove(pile);
    }
    public void Remove(BlockEntityGroundStorage pile)
    {
        var key = Key(pile.Pos);
        if (piles.TryGetValue(key, out var existing) && ReferenceEquals(existing, pile))
        { piles.Remove(key); nextBite.Remove(key); }
    }
    public bool Available(IWorldAccessor world, BlockPos pos) => pos != null &&
        world.BlockAccessor.GetBlockEntity(pos) is BlockEntityGroundStorage pile &&
        RotBaitRules.FoodSlot(pile.Inventory) != null;
    public BlockPos Nearest(IWorldAccessor world, Vec3d position, int dimension, BlockPos ignore = null)
    {
        double best = RotBaitRules.Radius * RotBaitRules.Radius;
        BlockPos found = null;
        foreach (var pile in piles.Values)
        {
            if (pile.Pos.dimension != dimension || RotBaitRules.FoodSlot(pile.Inventory) == null) continue;
            if (ignore != null && pile.Pos.Equals(ignore)) continue;
            double distance = position.SquareDistanceTo(pile.Pos.X + .5, pile.Pos.Y + .2, pile.Pos.Z + .5);
            if (distance > best || !ReferenceEquals(world.BlockAccessor.GetBlockEntity(pile.Pos), pile)) continue;
            best = distance; found = pile.Pos;
        }
        return found?.Copy();
    }
    public bool TryEat(IWorldAccessor world, BlockPos pos)
    {
        if (world.Side != EnumAppSide.Server || world.BlockAccessor.GetBlockEntity(pos) is not BlockEntityGroundStorage pile) return false;
        var key = Key(pos);
        long now = world.ElapsedMilliseconds;
        if (nextBite.TryGetValue(key, out long next) && now < next) return false;
        var slot = RotBaitRules.FoodSlot(pile.Inventory);
        if (slot == null) { Remove(pile); return false; }
        // Server entity ticks serialize bites. One item per pile per interval,
        // regardless of how many zombies arrive together.
        nextBite[key] = now + RotBaitRules.EatIntervalMs;
        slot.TakeOut(1);
        slot.MarkDirty();
        pile.MarkDirty(true);
        Refresh(pile);
        // Vanilla removes only a completely empty pile; unrelated items stay.
        pile.CheckInventoryClearedMidTick();
        return true;
    }
    public override void Dispose() { piles.Clear(); nextBite.Clear(); }
}

public sealed class RotBaitBehavior : BlockEntityBehavior
{
    RotBaitSystem bait;
    BlockEntityGroundStorage pile;
    public RotBaitBehavior(BlockEntity entity) : base(entity) { }
    public override void Initialize(ICoreAPI api, JsonObject properties)
    {
        base.Initialize(api, properties);
        if (api.Side != EnumAppSide.Server || Blockentity is not BlockEntityGroundStorage storage) return;
        bait = api.ModLoader.GetModSystem<RotBaitSystem>();
        pile = storage;
        // The ground-storage inventory is created once in the BE constructor;
        // every put/take/MarkDirty raises SlotModified. No tick listener.
        if (pile.Inventory != null) pile.Inventory.SlotModified += OnSlotModified;
        bait.Refresh(pile);
    }
    void OnSlotModified(int slotId) { if (pile != null) bait?.Refresh(pile); }
    // Loaded contents may arrive after Initialize depending on load order.
    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor worldAccessForResolve)
    {
        base.FromTreeAttributes(tree, worldAccessForResolve);
        if (pile != null) bait?.Refresh(pile);
    }
    void Detach()
    {
        if (pile == null) return;
        if (pile.Inventory != null) pile.Inventory.SlotModified -= OnSlotModified;
        bait?.Remove(pile);
        pile = null;
    }
    public override void OnBlockRemoved()
    {
        Detach();
        base.OnBlockRemoved();
    }
    public override void OnBlockUnloaded()
    {
        Detach();
        base.OnBlockUnloaded();
    }
}

public partial class Zombie
{
    RotBaitSystem rotBaitSystem;
    BlockPos rotBaitTarget;
    long nextRotBaitScanMs, nextRotBaitRouteMs;
    BaitPursuitState baitPursuitState;
    // Lazy: test fixtures create zombies without running field initializers.
    BaitPursuitState baitPursuit => baitPursuitState ??= new();

    void ReleaseRotBait()
    {
        if (rotBaitTarget == null) return;
        rotBaitTarget = null;
        baitPursuit.Clear();
        StopNavigation();
        nextPreyScanMs = nextRouteMs = nextIdleRouteMs = nextFollowScanMs = World.ElapsedMilliseconds;
    }
    bool ClearBiteTo(BlockPos pile)
    {
        var start = new Vec3d(Pos.X, Pos.Y + .6, Pos.Z);
        var end = new Vec3d(pile.X + .5, pile.Y + .2, pile.Z + .5);
        for (int i = 1; i < 8; i++)
        {
            double t = i / 8.0;
            var p = new BlockPos((int)Math.Floor(start.X + (end.X-start.X)*t),
                (int)Math.Floor(start.Y + (end.Y-start.Y)*t),
                (int)Math.Floor(start.Z + (end.Z-start.Z)*t), Pos.Dimension);
            if (p.Equals(pile)) continue;
            if (World.BlockAccessor.GetBlock(p).GetCollisionBoxes(World.BlockAccessor,p)?.Length > 0) return false;
        }
        return true;
    }
    double BaitDistance(BlockPos pile) =>
        Math.Sqrt(SeventhNightSystem.LocalPosition(Pos).SquareDistanceTo(pile.X + .5, pile.Y + .2, pile.Z + .5));
    bool TickRotBait(long now)
    {
        rotBaitSystem ??= Api.ModLoader.GetModSystem<RotBaitSystem>();
        var local = SeventhNightSystem.LocalPosition(Pos);
        if (rotBaitTarget != null && (!RotBaitRules.InRange(local,Pos.Dimension,rotBaitTarget) ||
            !rotBaitSystem.Available(World,rotBaitTarget))) ReleaseRotBait();
        if (rotBaitTarget == null && now >= nextRotBaitScanMs)
        {
            nextRotBaitScanMs = now + 1000 + World.Rand.Next(250);
            rotBaitTarget = rotBaitSystem.Nearest(World,local,Pos.Dimension,baitPursuit.Ignored(now));
            if (rotBaitTarget != null)
            {
                baitPursuit.Begin(rotBaitTarget, BaitDistance(rotBaitTarget), now);
                if (target is Vintagestory.API.Common.EntityPlayer) ResetNpcInterestAfterPlayerLost();
                target = null; alphaLeader = null; lastKnownPreyPosition = null; lostPreyUntilMs = 0;
                StopNavigation(); ResetProgress(); nextRotBaitRouteMs = now;
            }
        }
        if (rotBaitTarget == null) return false;
        AdvanceIdleTimer(0, true);
        var destination = new Vec3d(rotBaitTarget.X + .5, rotBaitTarget.Y + .2, rotBaitTarget.Z + .5);
        double dx=Pos.X-destination.X, dz=Pos.Z-destination.Z;
        bool atPile = dx*dx+dz*dz <= 1.4*1.4 && Math.Abs(Pos.Y-destination.Y) <= 1.5 && ClearBiteTo(rotBaitTarget);
        // An unreachable pile (sealed room, wrong floor, blocked bite) must not
        // pacify this zombie forever: give up, ignore it for a while, hunt again.
        if (baitPursuit.Update(BaitDistance(rotBaitTarget), atPile, now))
        {
            baitPursuit.GiveUp(now);
            rotBaitTarget = null;
            StopNavigation();
            nextPreyScanMs = nextRouteMs = nextIdleRouteMs = nextFollowScanMs = now;
            return false;
        }
        if (atPile)
        {
            StopNavigation(); SetWalking(false);
            if (rotBaitSystem.TryEat(World,rotBaitTarget)) AnimManager.StartAnimation("attack");
            if (!rotBaitSystem.Available(World,rotBaitTarget)) { ReleaseRotBait(); return false; }
            return true;
        }
        if (now >= nextRotBaitRouteMs)
        {
            // Traverser destinations use internal Y, unlike block coordinates.
            destination.Y += Pos.InternalY - Pos.Y;
            nextRotBaitRouteMs = RequestRoute(destination, C.MoveSpeed * VariantSpeedMultiplier, .9f, false, 1500)
                ? now + 2000 : RetryAt(now);
        }
        SetWalking(path.Active);
        return true;
    }
}
