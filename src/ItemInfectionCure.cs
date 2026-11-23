using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;

namespace SeventhNightZombies;

// Right-click once to administer one dose. Server owns the inventory and
// infection state; failed uses do not consume an item.
public class ItemInfectionCure : Item
{
    public override void OnHeldInteractStart(ItemSlot slot, EntityAgent byEntity, BlockSelection blockSel,
        EntitySelection entitySel, bool firstEvent, ref EnumHandHandling handHandling)
    {
        handHandling = EnumHandHandling.PreventDefault;
        if (!firstEvent || byEntity.World.Side != EnumAppSide.Server || slot.Empty) return;
        if (byEntity is not EntityPlayer player) return;
        var system = api.ModLoader.GetModSystem<SeventhNightSystem>();
        if (system?.TryTakeCureDose(player) == true)
        {
            slot.TakeOut(1);
            slot.MarkDirty();
        }
    }
}
