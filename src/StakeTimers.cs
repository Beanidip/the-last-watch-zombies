using System.Text;
using Newtonsoft.Json;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace SeventhNightZombies;

public partial class SeventhNightSystem
{
    const string StakeTimersKey = "thelastwatch-stake-timers";
    internal sealed class StakeTimer
    {
        public int X, Y, Z, Dimension;
        public string Code;
        public double Remaining = 30;
        [JsonIgnore] public BlockPos Position => new(X, Y, Z, Dimension);
    }
    // Only triggered blocks are tracked. No zombie identities or world scans.
    internal readonly Dictionary<BlockPos, StakeTimer> StakeTimers = new();

    internal void TriggerStake(BlockPos pos, int blockId)
    {
        if (StakeTimers.ContainsKey(pos)) return; // never extend the first trigger
        StakeTimers.Add(pos.Copy(), new StakeTimer {
            X = pos.X, Y = pos.Y, Z = pos.Z, Dimension = pos.dimension,
            Code = Server.World.GetBlock(blockId).Code.ToString()
        });
    }

    internal void TickStakeTimers(float dt)
    {
        if (StakeTimers.Count == 0 || !float.IsFinite(dt) || dt <= 0) return;
        var blocks = Server.World.BlockAccessor;
        foreach (var pair in StakeTimers.ToArray())
        {
            var timer = pair.Value;
            timer.Remaining = Math.Max(0, timer.Remaining - dt);
            // Do not load chunks for traps. An expired unloaded trap is replaced
            // on its first loaded tick, retaining its saved zero countdown.
            if (blocks.GetChunkAtBlockPos(pair.Key) == null) continue;
            var block = blocks.GetBlock(pair.Key);
            if (block.Code?.ToString() != timer.Code)
            {
                StakeTimers.Remove(pair.Key);
                continue;
            }
            if (timer.Remaining > 0 || LooseStickBlockId <= 0) continue;
            blocks.SetBlock(LooseStickBlockId, pair.Key);
            StakeTimers.Remove(pair.Key);
        }
    }

    void SaveStakeTimers() => Server.WorldManager.SaveGame.StoreData(StakeTimersKey,
        Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(StakeTimers.Values)));

    void LoadStakeTimers()
    {
        StakeTimers.Clear();
        var data = Server.WorldManager.SaveGame.GetData(StakeTimersKey);
        if (data == null) return;
        foreach (var timer in JsonConvert.DeserializeObject<List<StakeTimer>>(Encoding.UTF8.GetString(data)) ?? new())
        {
            if (!double.IsFinite(timer.Remaining) || !PalisadeTrapRules.Matches(new AssetLocation(timer.Code))) continue;
            timer.Remaining = Math.Clamp(timer.Remaining, 0, 30);
            StakeTimers[timer.Position] = timer;
        }
    }
}
