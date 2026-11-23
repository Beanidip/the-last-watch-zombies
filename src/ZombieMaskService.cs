using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SeventhNightZombies;

// Masks only camouflage the wearer; zombies never follow a disguised player.
// Check equipment and crouch state directly in IsProtected so removing/breaking
// the mask cancels protection without waiting for the 1-second server listener.
public sealed class ZombieMaskService
{
    public const string MaskTierKey = "seventhnightzombies-active-mask-tier"; // old save compatibility
    private sealed class WearState { public ItemSlot Slot; public ItemStack Stack; public double Elapsed; }
    private readonly Dictionary<string, WearState> wearing = new();

    public static bool IsVanillaFuneralMask(string code)
    {
        const string prefix = "game:clothes-face-funeralmask";
        if (code == null || !code.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        string number = code.Substring(prefix.Length);
        return int.TryParse(number, out int n) && n >= 1 && n <= 8;
    }

    // Only actual face equipment inventory, never carried or held items.
    static ItemSlot WornMaskSlot(IPlayer player)
    {
        if (player?.InventoryManager == null) return null;
        foreach (var inventory in player.InventoryManager.Inventories.Values)
        {
            if (inventory == null) continue;
            string cls = inventory.ClassName?.ToLowerInvariant() ?? "";
            if (!(cls.Contains("character") || cls.Contains("equipment") || cls.Contains("wearable"))) continue;
            foreach (var slot in inventory)
            {
                string code = slot?.Itemstack?.Collectible?.Code?.ToString();
                if (IsVanillaFuneralMask(code)) return slot;
            }
        }
        return null;
    }

    static bool HasUsableMask(ItemSlot slot)
    {
        var stack = slot?.Itemstack;
        return stack?.Collectible != null &&
               IsVanillaFuneralMask(stack.Collectible.Code?.ToString()) &&
               stack.Collectible.GetRemainingDurability(stack) > 0;
    }

    // Attraction reduction applies if a player is wearing a usable funeral mask,
    // regardless of sneaking. Camouflage still separately requires crouching.
    public static bool IsWearingUsableFuneralMask(IPlayer player) => HasUsableMask(WornMaskSlot(player));

    public static bool IsProtected(EntityPlayer entity) => entity != null && entity.Alive &&
        entity.Controls?.Sneak == true && HasUsableMask(WornMaskSlot(entity.Player));

    public void TickPlayer(IPlayer player, double dt, Config cfg)
    {
        var entity = player?.Entity;
        if (entity == null) return;
        var slot = WornMaskSlot(player);
        var stack = slot?.Itemstack;
        if (stack?.Collectible != null && IsVanillaFuneralMask(stack.Collectible.Code?.ToString()))
        {
            // Normalize pre-update stacks which may still carry a value above
            // their new max, especially masks 2-8 upgraded from 100 to 50.
            int max = stack.Collectible.GetMaxDurability(stack);
            int current = stack.Collectible.GetRemainingDurability(stack);
            if (current > max && max > 0)
            {
                stack.Collectible.SetDurability(stack, max);
                slot.MarkDirty();
            }
        }
        bool active = entity.Alive && entity.Controls?.Sneak == true && HasUsableMask(slot);
        string uid = player.PlayerUID;
        if (!active)
        {
            wearing.Remove(uid);
        }
        else
        {
            if (!wearing.TryGetValue(uid, out var state) ||
                !ReferenceEquals(state.Slot, slot) || !ReferenceEquals(state.Stack, stack))
                wearing[uid] = state = new WearState { Slot = slot, Stack = stack };
            state.Elapsed += dt;
            while (state.Elapsed >= cfg.MaskDrainSeconds && HasUsableMask(slot))
            {
                state.Elapsed -= cfg.MaskDrainSeconds;
                slot.Itemstack.Collectible.DamageItem(entity.World, entity, slot, 1, true);
                slot.MarkDirty();
            }
            active = HasUsableMask(slot);
            if (!active) wearing.Remove(uid);
        }
        // Collapse the former 2-tier disguise state into one protected/unprotected
        // state. Old alpha-mask watched attributes cannot grant new immunity.
        int value = active ? 1 : 0;
        if (entity.WatchedAttributes.GetInt(MaskTierKey) != value)
            entity.WatchedAttributes.SetInt(MaskTierKey, value);
    }

    public void Forget(string uid) => wearing.Remove(uid);

    // Both client and server must agree on base durability for the vanilla items.
    public static void SetMaskDurabilities(ICoreAPI api, int firstDurability, int otherDurability)
    {
        for (int i = 1; i <= 8; i++)
        {
            var item = api.World.GetItem(new AssetLocation($"game:clothes-face-funeralmask{i}"));
            if (item != null) item.Durability = i == 1 ? firstDurability : otherDurability;
        }
    }
}
