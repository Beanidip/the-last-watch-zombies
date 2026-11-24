using System.Diagnostics;
namespace SeventhNightZombies;

// Per-world rolling windows, shared by all zombies. No global static state.
public sealed class WorkBudget
{
    long scanWindow = -1, routeWindow = -1;
    int scans, routes;
    public bool TakeScan(long now, int limit) => Take(now, limit, ref scanWindow, ref scans);
    public bool TakeRoute(long now, int limit) => Take(now, limit, ref routeWindow, ref routes);
    static bool Take(long now, int limit, ref long window, ref int used)
    {
        if (window < 0 || now < window || now - window >= 1000) { window = now; used = 0; }
        if (used >= limit) return false;
        used++; return true;
    }
}

public sealed class WorkProfiler
{
    public bool Enabled;
    readonly Dictionary<string, (long Calls, double Total, double Max, long Items)> counters = new();
    public IDisposable Measure(string name) => Enabled ? new Sample(this, name) : Empty.Instance;
    public void Count(string name, long items = 1)
    {
        if (!Enabled) return;
        var c = counters.GetValueOrDefault(name);
        counters[name] = (c.Calls, c.Total, c.Max, c.Items + items);
    }
    void Record(string name, double ms)
    {
        var c = counters.GetValueOrDefault(name);
        counters[name] = (c.Calls + 1, c.Total + ms, Math.Max(c.Max, ms), c.Items);
    }
    public string Report() => !Enabled
        ? "Diagnostics disabled. Use /zombiediagnostics on, then check /zombieperf again later."
        : "Zombie work since diagnostics were enabled: " + string.Join("; ", counters.OrderBy(p => p.Key).Select(p =>
            $"{p.Key}: calls={p.Value.Calls}, total={p.Value.Total:0.##}ms, max={p.Value.Max:0.##}ms, items={p.Value.Items}"));
    sealed class Sample : IDisposable
    {
        readonly WorkProfiler owner; readonly string name; readonly long start = Stopwatch.GetTimestamp();
        public Sample(WorkProfiler owner, string name) { this.owner = owner; this.name = name; }
        public void Dispose() => owner.Record(name, Stopwatch.GetElapsedTime(start).TotalMilliseconds);
    }
    sealed class Empty : IDisposable
    { public static readonly Empty Instance = new(); public void Dispose() { } }
}
