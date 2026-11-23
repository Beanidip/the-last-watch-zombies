namespace SeventhNightZombies;

// Transient state for occupied zones only. No zombie identities or saved timers.
public sealed class SpawnRetryBackoff
{
    readonly Dictionary<WatchZone, (int Failures, long Until)> blocked = new();
    public bool Ready(WatchZone zone, long now) => RemainingSeconds(zone, now) == 0;
    public long RemainingSeconds(WatchZone zone, long now) => blocked.TryGetValue(zone, out var state)
        ? Math.Max(0, (state.Until - now + 999) / 1000) : 0;
    public void Record(WatchZone zone, long now, bool success)
    {
        if (success) { blocked.Remove(zone); return; }
        int failures = blocked.TryGetValue(zone, out var old) ? Math.Min(5, old.Failures + 1) : 1;
        int seconds = failures < 3 ? 0 : Math.Min(60, 15 << (failures - 3));
        blocked[zone] = (failures, now + seconds * 1000L);
    }
    public void Retain(IEnumerable<WatchZone> occupied)
    {
        var keep = occupied.ToHashSet();
        foreach (var key in blocked.Keys.Where(k => !keep.Contains(k)).ToArray()) blocked.Remove(key);
    }
}
