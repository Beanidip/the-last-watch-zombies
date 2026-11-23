using Vintagestory.API.Config;
namespace SeventhNightZombies;

// One zone = 3x3 horizontal chunk columns (96x96 blocks at 32-block chunks).
// X/Z are floor-divided so negative world coordinates work correctly.
public readonly record struct WatchZone(int Dimension, int X, int Z)
{
    public const int ChunksPerSide = 3;
    public static int ChunkToZone(int c) => (int)Math.Floor(c / 3.0);
    public static WatchZone FromBlock(int x, int z, int dimension)
    {
        int size = GlobalConstants.ChunkSize * ChunksPerSide;
        return new(dimension, (int)Math.Floor((double)x / size), (int)Math.Floor((double)z / size));
    }
    public static WatchZone FromChunk(int cx, int cz, int dimension) =>
        new(dimension, ChunkToZone(cx), ChunkToZone(cz));
    public int Distance(WatchZone other) => Dimension != other.Dimension ? int.MaxValue :
        Math.Max(Math.Abs(X - other.X), Math.Abs(Z - other.Z));
}

