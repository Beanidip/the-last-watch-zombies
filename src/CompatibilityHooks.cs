using HarmonyLib;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.Config;
using Vintagestory.GameContent;

namespace SeventhNightZombies;

/// <summary>Suppress only the equipped eye-bandage pain vocalization, and show
/// zombie infection in the character dialog's Traits tab. No healing hooks.</summary>
internal static class CompatibilityHooks
{
    private static Harmony harmony;
    // Set by SeventhNightSystem.StartClientSide; the Traits tab is client-only.
    internal static ICoreClientAPI ClientApi;
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

        // Vanilla builds the Traits tab text from the character class only.
        // Append one infection line; never alter the class traits themselves.
        var traitText = AccessTools.Method(typeof(CharacterSystem), "getClassTraitText");
        if (traitText != null)
        {
            try { harmony.Patch(traitText, postfix: new HarmonyMethod(typeof(CompatibilityHooks), nameof(AppendInfectionTrait))); }
            catch (Exception e) { api.Logger.Warning("Seventh Night Zombies: infection trait hook unavailable: " + e.Message); }
        }
        else api.Logger.Warning("Seventh Night Zombies: CharacterSystem.getClassTraitText not found; infection will not appear under Traits.");

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

    private static void AppendInfectionTrait(ref string __result)
    {
        try
        {
            var capi = ClientApi;
            var watched = capi?.World?.Player?.Entity?.WatchedAttributes;
            if (watched == null || capi.World.Calendar == null) return;
            string line = InfectionTrait.Describe(watched, capi.World.Calendar.TotalDays, (key, args) => Lang.Get(key, args));
            if (line != null) __result = (__result ?? "") + line + "\n";
        }
        catch (Exception e)
        {
            // A display problem must never break the character dialog.
            ClientApi?.Logger.Warning("Seventh Night Zombies: infection trait text failed: " + e.Message);
        }
    }

    public static void Uninstall()
    {
        ClientApi = null;
        harmony?.UnpatchAll("beani.seventhnight.compatibility");
        harmony = null;
    }
}
