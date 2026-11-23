using HarmonyLib;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;

namespace SeventhNightZombies;

/// <summary>Suppress only the equipped eye-bandage pain vocalization; no healing hooks.</summary>
internal static class CompatibilityHooks
{
    private static Harmony harmony;
    public static void Install(ICoreAPI api)
    {
        if (harmony != null) return;
        harmony = new Harmony("beani.seventhnight.compatibility");
        var voice = AccessTools.Method(typeof(EntityPlayer), "PlayEntitySound", [typeof(string), typeof(IPlayer)]);
        if (voice != null)
        {
            try { harmony.Patch(voice, prefix: new HarmonyMethod(typeof(CompatibilityHooks), nameof(AllowPlayerSound))); }
            catch (Exception e) { api.Logger.Warning("Seventh Night Zombies: player sound hook unavailable: " + e.Message); }
        }
        else api.Logger.Warning("Seventh Night Zombies: player sound suppression hook unavailable.");

    }

    // Only the pain vocalization is muted; hit damage, sound of impacts and death remain.
    private static bool AllowPlayerSound(Entity __instance, string type)
    {
        // PlayEntitySound may be declared on Entity and inherited by every creature.
        // Always leave other entities' sounds alone, even if Harmony patched the base method.
        if (__instance is not EntityPlayer player || string.IsNullOrEmpty(type) ||
            !player.WatchedAttributes.GetBool(SeventhNightSystem.QuietBandageKey))
            return true;
        return !(type.Equals("hurt", StringComparison.OrdinalIgnoreCase) ||
                 type.Equals("pain", StringComparison.OrdinalIgnoreCase) ||
                 type.Equals("damage", StringComparison.OrdinalIgnoreCase) ||
                 type.Equals("injured", StringComparison.OrdinalIgnoreCase) ||
                 type.StartsWith("hurt-", StringComparison.OrdinalIgnoreCase) ||
                 type.StartsWith("pain-", StringComparison.OrdinalIgnoreCase));
    }

    public static void Uninstall()
    {
        harmony?.UnpatchAll("beani.seventhnight.compatibility");
        harmony = null;
    }
}
