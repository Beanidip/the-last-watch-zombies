using Newtonsoft.Json.Linq;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;

namespace SeventhNightZombies;

// Resolve registered vanilla clothing rather than guessing shape asset paths.
public static class RotTunicVisuals
{
    public static void InheritGrassShirt(ICoreAPI api)
    {
        if (api.Side != EnumAppSide.Server) return;
        var tunic = api.World.GetItem(new AssetLocation(RotCoveredTunicService.ItemCode));
        if (tunic == null) return;
        var grass = api.World.GetItem(new AssetLocation("game:clothes-upperbody-grass"));
        if (grass == null || grass.Textures == null || grass.Textures.Count == 0)
        {
            api.Logger.Warning("Seventh Night Zombies: Vanilla Grass Shirt missing or has no textures; rot tunic retains its default appearance.");
            return;
        }
        if (grass.Shape != null) tunic.Shape = grass.Shape.Clone();
        // Copy attachment geometry but preserve our upper-body equipment slot
        // and gameplay attributes. The donor itself is never mutated.
        if (grass.Attributes?.Token is JObject vanilla)
        {
            var merged = (JObject)vanilla.DeepClone();
            if (tunic.Attributes?.Token is JObject ours)
                foreach (var property in ours.Properties())
                    merged[property.Name] = property.Value.DeepClone();
            tunic.Attributes = new JsonObject(merged);
        }
        // Never inherit donor clothing bonuses/penalties. Keep exactly the
        // native wearable walk-speed modifier from the Rot Tunic asset (-30%).
        // This is calculated by vanilla when equipped: no per-tick player
        // movement or animation changes are performed by our code.
        if (tunic.Attributes?.Token is JObject finalTunic)
            finalTunic["statModifiers"] = new JObject { ["walkSpeed"] = -0.30 };
        var textures = new Dictionary<string, CompositeTexture>();
        foreach (var entry in grass.Textures)
        {
            if (entry.Value == null) continue;
            bool skin = entry.Key.Contains("skin", StringComparison.OrdinalIgnoreCase) ||
                entry.Key.Contains("seraph", StringComparison.OrdinalIgnoreCase);
            // Fresh cloth textures cannot retain vanilla tint overlays or stale
            // baked atlas data. Preserve skin bindings separately.
            textures[entry.Key] = skin ? entry.Value.Clone() : new CompositeTexture
            {
                Base = new AssetLocation("seventhnightzombies:item/rotcoveredtunic-reference")
            };
        }
        if (textures.Count > 0) tunic.Textures = textures;
        api.Logger.Notification("Seventh Night Zombies: Rot Tunic visual donor: {0}; reference texture installed. Equipment slot remains upperbody.", grass.Code);
    }
}
