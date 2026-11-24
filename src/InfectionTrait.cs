using Vintagestory.API.Config;
using Vintagestory.API.Datastructures;

namespace SeventhNightZombies;

// Infection status shown as an extra line in the vanilla character dialog's
// Traits tab (see CompatibilityHooks). The client cannot know the server's
// config, so the server publishes the exact deadlines in watched attributes
// (SeventhNightSystem.SyncInfectionDisplay) and this class only formats them.
public static class InfectionTrait
{
    public const string FatalDayKey = "seventhnight-infection-fatal-day";
    public const string CureStartByDayKey = "seventhnight-cure-start-by-day";
    public const string NextDoseDayKey = "seventhnight-cure-next-dose-day";
    public const string DosesRequiredKey = "seventhnight-cure-doses-required";

    // Returns null when the player is not infected. `t` is Lang.Get in game.
    public static string Describe(ITreeAttribute watched, double nowDays, Func<string, object[], string> t)
    {
        if (watched == null || !watched.HasAttribute(SeventhNightSystem.InfectionKey)) return null;
        int doses = watched.GetInt(SeventhNightSystem.CureDosesKey);
        int required = watched.GetInt(DosesRequiredKey, 6);
        double fatalDay = watched.GetDouble(FatalDayKey, -1);
        string fatal = fatalDay < 0 ? t("seventhnightzombies:traitdesc-infected-notfatal", [])
            : t("seventhnightzombies:traitdesc-infected-fatal", [Until(fatalDay - nowDays)]);
        string detail;
        if (doses > 0)
        {
            double nextDay = watched.GetDouble(NextDoseDayKey, nowDays);
            string next = nextDay <= nowDays ? t("seventhnightzombies:traitdesc-infected-dose-ready", [])
                : t("seventhnightzombies:traitdesc-infected-dose-in", [Until(nextDay - nowDays)]);
            detail = t("seventhnightzombies:traitdesc-infected-treated", [doses, required, next]);
        }
        else
        {
            double startBy = watched.GetDouble(CureStartByDayKey, -1);
            detail = startBy < 0 || nowDays <= startBy
                ? t("seventhnightzombies:traitdesc-infected-untreated",
                    [startBy < 0 ? "-" : Until(startBy - nowDays)])
                : t("seventhnightzombies:traitdesc-infected-toolate", []);
        }
        return $"<font color=\"#ff6b5e\">{t("seventhnightzombies:trait-infected", [])}</font> " +
               $"<font opacity=\"0.8\">({detail} {fatal})</font>";
    }

    // Compact in-game duration: "1d 4h", "5h 20m", "12m".
    public static string Until(double gameDays)
    {
        long minutes = Math.Max(0, (long)Math.Ceiling(gameDays * 1440 - 1e-7));
        long d = minutes / 1440, h = minutes % 1440 / 60, m = minutes % 60;
        return d > 0 ? $"{d}d {h}h" : h > 0 ? $"{h}h {m}m" : $"{m}m";
    }
}
