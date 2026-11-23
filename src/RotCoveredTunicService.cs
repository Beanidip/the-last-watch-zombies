using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SeventhNightZombies;

// A separate wearable from the masks. The item has a static -30% vanilla
// walk-speed penalty while worn; this service never changes controls,
// animations, camera transforms, or speed attributes on each tick.
public sealed class RotCoveredTunicService
{
    public const string ItemCode = "seventhnightzombies:rotcoveredtunic";
    const string FirstWearKey = "seventhnightzombies-rot-tunic-first-wear";
    const string RemainingDamageKey = "seventhnightzombies-rot-tunic-damage-remaining";
    const string DamageTimerKey = "seventhnightzombies-rot-tunic-damage-timer";
    const string WearElapsedKey = "seventhnightzombies-rot-tunic-wear-elapsed";

    public static bool IsTunic(ItemStack stack) =>
        string.Equals(stack?.Collectible?.Code?.ToString(), ItemCode, StringComparison.OrdinalIgnoreCase);

    // Search only worn clothing slots, not ordinary inventory or hotbar storage.
    public static ItemSlot WornTunicSlot(IPlayer player)
    {
        if (player?.InventoryManager == null) return null;
        foreach (var inventory in player.InventoryManager.Inventories.Values)
        {
            if (inventory == null) continue;
            string cls = inventory.ClassName?.ToLowerInvariant() ?? "";
            if (!(cls.Contains("character") || cls.Contains("equipment") || cls.Contains("wearable"))) continue;
            foreach (var slot in inventory)
                if (IsTunic(slot?.Itemstack)) return slot;
        }
        return null;
    }

    // Instant protection as long as the tunic is actually in a wearable slot,
    // the owner is alive, and durability remains. No crouching requirement.
    // Target acquisition and active pursuit both query this on every check.
    public static bool IsProtected(EntityPlayer entity) => entity?.Alive == true &&
        WornTunicSlot(entity.Player) is ItemSlot slot &&
        slot.Itemstack.Collectible.GetRemainingDurability(slot.Itemstack) > 0;

    public void TickPlayer(IPlayer player, double dt, Config cfg)
    {
        var entity = player?.Entity;
        if (entity == null) return;
        var slot = WornTunicSlot(player);
        var stack = slot?.Itemstack;
        bool equipped = IsTunic(stack);
        if (!double.IsFinite(dt) || dt <= 0) return;
        // Inventory changes can refresh the first-person equipment rendering.
        // Coalesce item updates to a single dirty notification per heartbeat.
        bool wearableChanged = false;

        // The first equip of *this item* starts one course of 3 HP damage.
        // Flag stored on the item itself: removing/re-equipping cannot farm/restart it.
        if (equipped && entity.Alive && stack.Collectible.GetRemainingDurability(stack) > 0 &&
            !stack.Attributes.GetBool(FirstWearKey))
        {
            stack.Attributes.SetBool(FirstWearKey, true);
            wearableChanged = true;
            MigrateLegacySickness(entity);
            float pending = entity.Attributes.GetFloat(RemainingDamageKey);
            entity.Attributes.SetFloat(RemainingDamageKey, pending + cfg.RotTunicFirstWearDamage);
            (player as Vintagestory.API.Server.IServerPlayer)?.SendMessage(0,
                "The rot sinks into your skin. You feel sick and heavy.", EnumChatType.Notification);
        }

        // Protection and durability cost are continuous WHILE WORN, even while
        // standing or walking. This prevents unlimited disguise by not crouching.
        bool active = equipped && entity.Alive &&
            stack.Collectible.GetRemainingDurability(stack) > 0;
        if (active)
        {
            double elapsed = stack.Attributes.GetDouble(WearElapsedKey);
            if (!double.IsFinite(elapsed) || elapsed < 0) elapsed = 0;
            elapsed += dt;
            double interval = double.IsFinite(cfg.RotTunicDrainSeconds) ? Math.Max(0.1, cfg.RotTunicDrainSeconds) : 1;
            while (elapsed >= interval &&
                   slot.Itemstack != null && IsTunic(slot.Itemstack) &&
                   slot.Itemstack.Collectible.GetRemainingDurability(slot.Itemstack) > 0)
            {
                elapsed -= interval;
                slot.Itemstack.Collectible.DamageItem(entity.World, entity, slot, 1, true);
                wearableChanged = true;
            }
            // Carry fractional wear with the item through removal, transfers,
            // disconnects and saves; rapid re-equipping cannot reset the clock.
            if (ReferenceEquals(slot.Itemstack, stack))
            {
                stack.Attributes.SetDouble(WearElapsedKey, elapsed);
                wearableChanged = true;
            }
        }

        // Never issue multiple inventory-dirty notifications for the same tick;
        // the wearable has no custom arm or locomotion animations.
        if (wearableChanged) slot?.MarkDirty();

        // Six real-second ticks of 0.5 HP by default: a total of 3 HP once.
        // This effect is independent of zombie infection and continues after
        // taking the tunic off. Its remaining damage survives save/load.
        MigrateLegacySickness(entity);
        float remaining = entity.Attributes.GetFloat(RemainingDamageKey);
        if (remaining <= 0 || !entity.Alive)
        {
            // This runs for EVERY healthy player once per second. Only mutate
            // the saved timer when there is actually one to clear.
            if (entity.Attributes.HasAttribute(DamageTimerKey))
                entity.Attributes.RemoveAttribute(DamageTimerKey);
            return;
        }
        double clock = entity.Attributes.GetDouble(DamageTimerKey) + dt;
        if (clock >= 1)
        {
            int due = Math.Min(10, (int)clock);
            clock -= due;
            for (int i = 0; i < due && remaining > 0 && entity.Alive; i++)
            {
                float hit = Math.Min(.5f, remaining);
                bool accepted = entity.ReceiveDamage(new DamageSource
                {
                    Source = EnumDamageSource.Internal,
                    Type = EnumDamageType.Poison,
                    IgnoreInvFrames = true
                }, hit);
                if (accepted) remaining -= hit;
            }
            entity.Attributes.SetFloat(RemainingDamageKey, Math.Max(0, remaining));
        }
        // The timer is gameplay state only; no client needs a per-second sync.
        entity.Attributes.SetDouble(DamageTimerKey, clock);
    }

    static void MigrateLegacySickness(EntityPlayer entity)
    {
        // One-time upgrade from the old client-synchronized sickness clocks.
        // Preserve in-progress damage across a source update or a saved world.
        if (entity.WatchedAttributes.HasAttribute(RemainingDamageKey))
        {
            if (!entity.Attributes.HasAttribute(RemainingDamageKey))
                entity.Attributes.SetFloat(RemainingDamageKey,
                    entity.WatchedAttributes.GetFloat(RemainingDamageKey));
            entity.WatchedAttributes.RemoveAttribute(RemainingDamageKey);
        }
        if (entity.WatchedAttributes.HasAttribute(DamageTimerKey))
        {
            if (!entity.Attributes.HasAttribute(DamageTimerKey))
                entity.Attributes.SetDouble(DamageTimerKey,
                    entity.WatchedAttributes.GetDouble(DamageTimerKey));
            entity.WatchedAttributes.RemoveAttribute(DamageTimerKey);
        }
    }

    public static void ClearSickness(EntityPlayer entity)
    {
        entity?.Attributes.RemoveAttribute(RemainingDamageKey);
        entity?.Attributes.RemoveAttribute(DamageTimerKey);
        entity?.WatchedAttributes.RemoveAttribute(RemainingDamageKey); // old saves
        entity?.WatchedAttributes.RemoveAttribute(DamageTimerKey);
    }
}
