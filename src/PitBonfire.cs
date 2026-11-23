using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace SeventhNightZombies;

// A separate, uncovered alternative to vanilla charcoal pits. The player places
// firewood in connected ground-storage piles, sets a 1-cattail/3-resin starter
// on top, adds four firewood via right-click, and ignites with a normal igniter.
// Both the visible flames and the charcoal output cover every connected FULL pile.
public sealed class BlockPitBonfire : Block, IIgnitable
{
    // Shapes and opaque textures are declared in the three block JSONs.
    // Do not overwrite Shapes or Textures here: doing so after the texture atlas
    // is built produced missing faces / see-through terrain on some clients.

    public override bool OnBlockInteractStart(IWorldAccessor world, IPlayer byPlayer, BlockSelection sel)
    {
        if (sel?.Position == null || byPlayer == null) return false;
        if (world.BlockAccessor.GetBlockEntity(sel.Position) is not BlockEntityPitBonfire pit) return false;
        var slot = byPlayer.InventoryManager.ActiveHotbarSlot;
        if (slot?.Itemstack?.Collectible?.Code?.Path == "firewood" && !pit.IsBurning && pit.Kindling < 4)
        {
            if (world.Side == EnumAppSide.Server)
            {
                slot.TakeOut(1);
                slot.MarkDirty();
                pit.AddKindling();
            }
            return true;
        }
        return false;
    }

    public EnumIgniteState OnTryIgniteStack(EntityAgent byEntity, BlockPos pos, ItemSlot slot, float secondsIgniting) =>
        EnumIgniteState.NotIgnitable;

    public EnumIgniteState OnTryIgniteBlock(EntityAgent byEntity, BlockPos pos, float secondsIgniting)
    {
        var pit = byEntity.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityPitBonfire;
        if (pit == null || !pit.CanIgnite()) return EnumIgniteState.NotIgnitablePreventDefault;
        return secondsIgniting > 2 ? EnumIgniteState.IgniteNow : EnumIgniteState.Ignitable;
    }

    public void OnTryIgniteBlockOver(EntityAgent byEntity, BlockPos pos, float secondsIgniting, ref EnumHandling handling)
    {
        handling = EnumHandling.PreventDefault;
        if (byEntity.World.Side == EnumAppSide.Server)
            (byEntity.World.BlockAccessor.GetBlockEntity(pos) as BlockEntityPitBonfire)?.Ignite();
    }
}

public sealed class BlockEntityPitBonfire : BlockEntity
{
    // Stock charcoal pit takes ~18 in-game hours; this version takes nine.
    const double BurnHours = 9;
    const int MaxTrackedPiles = 128; // prevent unbounded searches / fuel exploits
    public int Kindling { get; private set; }
    public bool IsBurning { get; private set; }
    public int FuelStacks { get; private set; }
    double litAtHours;
    readonly List<BlockPos> piles = new();
    int nextVisualPile;
    long tickId;

    public override void Initialize(ICoreAPI api)
    {
        base.Initialize(api);
        if (api.Side == EnumAppSide.Server)
            tickId = RegisterGameTickListener(_ => ServerTick(), 2000);
        else
            tickId = RegisterGameTickListener(_ => ClientEmbers(), 550);
    }

    void ClientEmbers()
    {
        if (!IsBurning) return;
        // Show fire at the starter AND on each full connected pile, not only
        // at the ignition block. The client receives pile positions in the BE
        // attributes when ignition marks the BE dirty.
        EmitFlame(Pos, 0.42);
        // Every tracked full pile emits fire, in rotating batches to bound
        // particle load. The server verifies these piles still contain wood;
        // do not re-check client GroundStorage BEs, which may not yet be synced.
        int count = Math.Min(piles.Count, 32);
        for (int i = 0; i < count; i++)
        {
            var p = piles[(nextVisualPile + i) % piles.Count];
            EmitFlame(p, 0.40);
        }
        if (piles.Count > 0) nextVisualPile = (nextVisualPile + count) % piles.Count;
    }

    void EmitFlame(BlockPos at, double height)
    {
        var flame = new SimpleParticleProperties(
            3, 6, ColorUtil.ToRgba(255, 255, 117, 19),
            at.ToVec3d().Add(0.08, height, 0.08), new Vec3d(0.84, 0.17, 0.84),
            new Vec3f(-0.08f, 0.15f, -0.08f), new Vec3f(0.08f, 0.34f, 0.08f),
            0.6f, -0.012f, 0.28f, 0.70f, EnumParticleModel.Quad);
        Api.World.SpawnParticles(flame);
        var smoke = new SimpleParticleProperties(
            1, 2, ColorUtil.ToRgba(115, 69, 64, 60),
            at.ToVec3d().Add(0.28, height + 0.3, 0.28), new Vec3d(0.44, 0.18, 0.44),
            new Vec3f(-0.015f, 0.06f, -0.015f), new Vec3f(0.015f, 0.19f, 0.015f),
            1.35f, 0.018f, 0.35f, 0.85f, EnumParticleModel.Quad);
        Api.World.SpawnParticles(smoke);
    }

    public void AddKindling()
    {
        if (IsBurning) return;
        Kindling = Math.Min(4, Kindling + 1);
        MarkDirty(true);
        // Four inserted firewood pieces visibly switch to the loaded starter.
        // ExchangeBlock keeps this block entity and all four consumed pieces.
        if (Kindling >= 4 && Api?.Side == EnumAppSide.Server) SwapVisual();
    }

    static bool IsFirewoodStack(ICoreAPI api, BlockPos pos)
    {
        // VS 1.22 uses GroundStorage for manually placed firewood piles.
        if (api.World.BlockAccessor.GetBlockEntity(pos) is not BlockEntityGroundStorage storage) return false;
        var inv = storage.Inventory;
        return inv != null && inv.Count > 0 &&
            inv[0]?.Itemstack?.Collectible?.Code?.Path == "firewood" &&
            storage.TotalStackSize >= 32;
    }

    // Flood-fill FULL 32-piece firewood piles connected to the pile immediately
    // under the bonfire. No enclosure needed. Same zone only; this prevents a
    // bonfire on a border from receiving fuel credit from another migration zone.
    List<BlockPos> FindPiles()
    {
        var result = new List<BlockPos>();
        if (Api == null) return result;
        var start = Pos.DownCopy();
        var zone = WatchZone.FromBlock(Pos.X, Pos.Z, Pos.dimension);
        var todo = new Queue<BlockPos>();
        var seen = new HashSet<(int,int,int)>();
        todo.Enqueue(start);
        while (todo.Count != 0 && result.Count < MaxTrackedPiles)
        {
            var p = todo.Dequeue();
            if (!seen.Add((p.X,p.Y,p.Z))) continue;
            if (Math.Abs(p.X-Pos.X)>7 || Math.Abs(p.Z-Pos.Z)>7 ||
                p.Y>Pos.Y-1 || Pos.Y-p.Y>7 ||
                WatchZone.FromBlock(p.X,p.Z,p.dimension)!=zone ||
                !IsFirewoodStack(Api,p)) continue;
            result.Add(p.Copy());
            foreach (var face in BlockFacing.ALLFACES) todo.Enqueue(p.AddCopy(face));
        }
        return result;
    }

    public bool CanIgnite() => !IsBurning && Kindling >= 4 && FindPiles().Count > 0;

    public void Ignite()
    {
        if (Api?.Side != EnumAppSide.Server || !CanIgnite()) return;
        piles.Clear();
        piles.AddRange(FindPiles());
        FuelStacks = piles.Count;
        litAtHours = Api.World.Calendar.TotalHours;
        IsBurning = true;
        MarkDirty(true);
        Api.ModLoader.GetModSystem<LastWatchZones>()?.UpdateBonfire(Pos, litAtHours + BurnHours, FuelStacks);
        SwapVisual();
    }

    void SwapVisual()
    {
        // Empty old-firepit -> wood-loaded -> burning; all variants use the
        // same BE class and retain fuel / burn-state across ExchangeBlock.
        string code = IsBurning ? "pitbonfirelit" : Kindling >= 4 ? "pitbonfireloaded" : "pitbonfire";
        var block = Api.World.GetBlock(new AssetLocation("seventhnightzombies", code));
        if (block?.Id > 0 && Api.World.BlockAccessor.GetBlock(Pos)?.Id != block.Id)
            Api.World.BlockAccessor.ExchangeBlock(block.Id, Pos); // preserves block entity
    }

    public bool ActiveForMigration => IsBurning && FuelStacks >= 3 &&
        Api != null && Api.World.Calendar.TotalHours < litAtHours + BurnHours &&
        piles.Count >= FuelStacks && piles.All(p => IsFirewoodStack(Api,p));

    void ServerTick()
    {
        if (!IsBurning) return;
        // Extinguish if the fuel was dismantled. Missing unloaded neighbors do
        // not grant protection because ActiveForMigration validates them.
        if (piles.Any(p => !IsFirewoodStack(Api,p))) { Extinguish(); return; }
        if (Api.World.Calendar.TotalHours < litAtHours + BurnHours) return;
        Finish();
    }

    void Extinguish()
    {
        IsBurning=false; FuelStacks=0; piles.Clear(); Kindling=0;
        Api.ModLoader.GetModSystem<LastWatchZones>()?.UpdateBonfire(Pos, 0, 0);
        MarkDirty(true);
        SwapVisual();
    }

    void Finish()
    {
        // Vanilla pits yield roughly 4-7 charcoal per stack. Our lure now
        // yields just 1.5 on average (alternate 1,2 per full 32-firewood pile).
        var coal = Api.World.GetItem(new AssetLocation("game", "charcoal"));
        if (coal == null) { Api.Logger.Error("Pit Bonfire: game:charcoal was not found; keeping fuel unchanged."); Extinguish(); return; }
        int consumed = 0;
        foreach (var p in piles)
        {
            if (!IsFirewoodStack(Api,p)) continue;
            // Remove without normal break-drop logic. Each 32-piece pile is fuel.
            Api.World.BlockAccessor.SetBlock(0,p);
            consumed++;
            // Alternating 1 and 2 avoids fractional items (three per two piles).
            int yield = consumed % 2 == 0 ? 2 : 1;
            Api.World.SpawnItemEntity(new ItemStack(coal,yield), Pos.ToVec3d().Add(0.5,1.15,0.5));
        }
        // Successful burnout consumes the starter as well as the wood. Clear
        // migration protection first; SetBlock removes the BE/listeners normally
        // and does not create a reusable starter drop.
        IsBurning = false;
        FuelStacks = Kindling = 0;
        piles.Clear();
        Api.ModLoader.GetModSystem<LastWatchZones>()?.UpdateBonfire(Pos, 0, 0);
        Api.World.BlockAccessor.SetBlock(0, Pos);
    }

    public override void OnBlockRemoved()
    {
        if (Api?.Side == EnumAppSide.Server)
            Api.ModLoader.GetModSystem<LastWatchZones>()?.UpdateBonfire(Pos, 0, 0);
        base.OnBlockRemoved();
    }

    public override void ToTreeAttributes(ITreeAttribute tree)
    {
        base.ToTreeAttributes(tree);
        tree.SetInt("kindling",Kindling);
        tree.SetBool("burning",IsBurning);
        tree.SetInt("fuelStacks",FuelStacks);
        tree.SetDouble("litAtHours",litAtHours);
        tree.SetString("fuelCoords",string.Join(";",piles.Select(p=>$"{p.X},{p.Y},{p.Z}")));
    }
    public override void FromTreeAttributes(ITreeAttribute tree, IWorldAccessor world)
    {
        base.FromTreeAttributes(tree,world);
        Kindling=Math.Clamp(tree.GetInt("kindling"),0,4);
        IsBurning=tree.GetBool("burning");
        FuelStacks=tree.GetInt("fuelStacks");
        litAtHours=tree.GetDouble("litAtHours");
        piles.Clear();
        foreach (var part in (tree.GetString("fuelCoords")??"").Split(';',StringSplitOptions.RemoveEmptyEntries))
        {
            var values=part.Split(',');
            if (values.Length==3 && int.TryParse(values[0],out var x) &&
                int.TryParse(values[1],out var y) && int.TryParse(values[2],out var z) &&
                piles.Count < MaxTrackedPiles) piles.Add(new BlockPos(x,y,z,Pos.dimension));
        }
    }
}
