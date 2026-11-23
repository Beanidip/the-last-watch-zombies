using System.Text;
using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;

namespace SeventhNightZombies;

// A native WorldMapManager layer. The vanilla map passes open/pan/close events
// to server layers and already transports byte[] via SendMapDataToClient.
// Server enforces permissions BEFORE serializing zone populations.
public sealed class LastWatchMapRegistrar : ModSystem
{
    public override void Start(ICoreAPI api) =>
        api.ModLoader.GetModSystem<WorldMapManager>()?.RegisterMapLayer<LastWatchMapLayer>("lastwatch", 2.5);
}

public sealed class LastWatchMapLayer : MapLayer
{
    readonly Dictionary<WatchZone, LastWatchZones.ZoneView> cells = new();
    // Server-side opt-in; no zone information is sent until an admin toggles /zombiemap.
    static readonly HashSet<string> optedInAdmins = new();
    public static bool ToggleFor(string uid)
    {
        if (!optedInAdmins.Add(uid)) { optedInAdmins.Remove(uid); return false; }
        return true;
    }
    public static bool IsEnabledFor(string uid) => optedInAdmins.Contains(uid);
    ICoreServerAPI sapi;
    bool authorized;
    const int ZoneBlocks=3*GlobalConstants.ChunkSize;
    public LastWatchMapLayer(ICoreAPI api,IWorldMapManager mapSink):base(api,mapSink)
    {
        ZIndex=200;
        RequirePrivilege="controlserver";
    }
    public override string Title=>"The Last Watch — Zombie Zones";
    public override string LayerGroupCode=>"chunks";
    public override EnumMapAppSide DataSide=>EnumMapAppSide.Server;
    public override bool RequireChunkLoaded=>false;
    public override void OnLoaded()
    {
        sapi = api as ICoreServerAPI;
        // Admin opt-ins are session-local; a fresh world starts with no overlay.
        if (sapi != null) optedInAdmins.Clear();
    }
    public override void OnMapOpenedServer(IServerPlayer player)
    {
        if (player.HasPrivilege("controlserver") && IsEnabledFor(player.PlayerUID))
            SendSnapshot(player); // exactly one snapshot per map open
    }
    public override void OnMapClosedServer(IServerPlayer player) { }
    public override void OnViewChangedServer(IServerPlayer player,int x1,int z1,int x2,int z2) { }
    void SendSnapshot(IServerPlayer player)
    {
        if(!player.HasPrivilege("controlserver") || !IsEnabledFor(player.PlayerUID)) return;
        var zones=sapi.ModLoader.GetModSystem<LastWatchZones>();
        if(zones==null) return;
        // Only nonempty, modified, fortified or infested zones are transmitted;
        // all other white squares are drawn by the client as an empty grid.
        using var timing = sapi.ModLoader.GetModSystem<SeventhNightSystem>().Profiler.Measure("map.snapshot");
        var snapshot=zones.SnapshotAll().Where(z=>z.ActiveZombies>0||z.Dormant>0||
            z.Paths>0||z.Deaths>0||z.Bells>0||z.Strongholds>0||z.BurningBonfires>0||z.AmbientAdded>0||z.Settlement).Take(10000).ToArray();
        mapSink.SendMapDataToClient(this,player,Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(snapshot)));
    }
    public override void OnMapOpenedClient() { authorized=false;cells.Clear(); }
    public override void OnMapClosedClient() { authorized=false;cells.Clear(); }
    public override void OnDataFromServer(byte[] data)
    {
        var entries=JsonConvert.DeserializeObject<LastWatchZones.ZoneView[]>(Encoding.UTF8.GetString(data));
        cells.Clear();
        foreach(var c in entries??Array.Empty<LastWatchZones.ZoneView>())
            cells[new WatchZone(c.Dimension,c.X,c.Z)]=c;
        authorized=true;
    }
    public override void Render(GuiElementMap mapElem,float dt)
    {
        if (!Active || !authorized || api is not ICoreClientAPI capi) return;
        var bounds=mapElem.CurrentBlockViewBounds;
        int minX=(int)Math.Floor(bounds.X1/ZoneBlocks);
        int maxX=(int)Math.Floor(bounds.X2/ZoneBlocks);
        int minZ=(int)Math.Floor(bounds.Z1/ZoneBlocks);
        int maxZ=(int)Math.Floor(bounds.Z2/ZoneBlocks);
        int dim=capi.World.Player.Entity.Pos.Dimension;
        if ((long)(maxX-minX+1)*(maxZ-minZ+1)>1800) return;
        for(int x=minX;x<=maxX;x++) for(int z=minZ;z<=maxZ;z++)
        {
            var key=new WatchZone(dim,x,z);
            cells.TryGetValue(key,out var cell);
            int color=cell?.Dormant>0?unchecked((int)0xFFFFA52C):unchecked((int)0xFFFFFFFF);
            int thickness=1;
            var a=new Vec2f(); var b=new Vec2f();
            mapElem.TranslateWorldPosToViewPos(new Vec3d(x*ZoneBlocks,0,z*ZoneBlocks),ref a);
            mapElem.TranslateWorldPosToViewPos(new Vec3d((x+1)*ZoneBlocks,0,(z+1)*ZoneBlocks),ref b);
            float sx=(float)mapElem.Bounds.absX+a.X;
            float sy=(float)mapElem.Bounds.absY+a.Y;
            float w=b.X-a.X,h=b.Y-a.Y;
            if(w<1||h<1) continue;
            bool fortified = cell?.Strongholds > 0;
            const int blue = unchecked((int)0xFF3287FF);
            bool hasActiveZombies = cell?.ActiveZombies > 0;
            const int activeRed = unchecked((int)0xFFFF3C45);
            if(w<12 || h<12)
            {
                if (fortified) capi.Render.RenderRectangle(sx,sy,194,w,h,blue);
                // Small map cells retain their basic population swatch, with a
                // one-pixel red rim when at least one live zombie is loaded.
                capi.Render.RenderRectangle(sx+(fortified?1:0),sy+(fortified?1:0),195,
                    Math.Max(0,w-(fortified?2:0)),Math.Max(0,h-(fortified?2:0)),color);
                if (hasActiveZombies)
                {
                    const float edge = 1;
                    capi.Render.RenderRectangle(sx,sy,197,w,edge,activeRed);
                    capi.Render.RenderRectangle(sx,sy+h-edge,197,w,edge,activeRed);
                    capi.Render.RenderRectangle(sx,sy,197,edge,h,activeRed);
                    capi.Render.RenderRectangle(sx+w-edge,sy,197,edge,h,activeRed);
                }
                continue;
            }
            if (fortified)
            {
                // Solid blue perimeter underneath the red/orange/white dotted
                // population border, visible in each gap and below the dashes.
                const float underlay = 4;
                capi.Render.RenderRectangle(sx,sy,194,w,underlay,blue);
                capi.Render.RenderRectangle(sx,sy+h-underlay,194,w,underlay,blue);
                capi.Render.RenderRectangle(sx,sy,194,underlay,h,blue);
                capi.Render.RenderRectangle(sx+w-underlay,sy,194,underlay,h,blue);
            }
            const float stride=11;
            float dash=3+thickness;
            for(float d=0;d<w;d+=stride)
            {
                capi.Render.RenderRectangle(sx+d,sy,195,Math.Min(dash,w-d),thickness,color);
                capi.Render.RenderRectangle(sx+d,sy+h-thickness,195,Math.Min(dash,w-d),thickness,color);
            }
            for(float d=0;d<h;d+=stride)
            {
                capi.Render.RenderRectangle(sx,sy+d,195,thickness,Math.Min(dash,h-d),color);
                capi.Render.RenderRectangle(sx+w-thickness,sy+d,195,thickness,Math.Min(dash,h-d),color);
            }
            if (hasActiveZombies)
            {
                // Solid inner red line: alive/loaded zombies exist in this zone.
                // Retain the outer orange dormant dashes and blue fortification
                // rim, so red is a separate signal rather than a replacement.
                const float inset = 2;
                const float edge = 2;
                capi.Render.RenderRectangle(sx+inset,sy+inset,197,w-2*inset,edge,activeRed);
                capi.Render.RenderRectangle(sx+inset,sy+h-inset-edge,197,w-2*inset,edge,activeRed);
                capi.Render.RenderRectangle(sx+inset,sy+inset,197,edge,h-2*inset,activeRed);
                capi.Render.RenderRectangle(sx+w-inset-edge,sy+inset,197,edge,h-2*inset,activeRed);
            }
        }
    }
    public override void OnMouseMoveClient(MouseEvent args,GuiElementMap mapElem,StringBuilder hoverText)
    {
        if (!authorized || api is not ICoreClientAPI capi) return;
        var view=new Vec2f((float)(args.X-mapElem.Bounds.absX),(float)(args.Y-mapElem.Bounds.absY));
        var pos=new Vec3d();
        mapElem.TranslateViewPosToWorldPos(view,ref pos);
        var zone=WatchZone.FromBlock((int)Math.Floor(pos.X),(int)Math.Floor(pos.Z),
            capi.World.Player.Entity.Pos.Dimension);
        cells.TryGetValue(zone,out var d);
        hoverText.Append($"\n[Last Watch] zone {zone.X},{zone.Z} — active loaded zombies {d?.ActiveZombies??0}, dormant population {d?.Dormant??0} (map-open snapshot)"+
            $"\nAmbient population added today {d?.AmbientAdded??0} (newly created only)" +
            $"\nStone paths {d?.Paths??0}; deaths {d?.Deaths??0}; " +
            $"bells {d?.Bells??0}; strongholds {d?.Strongholds??0}" +
            $"\nPit bonfires burning {d?.BurningBonfires??0}; live max {d?.BonfireStacks??0} stacks" +
            $"\nToday recorded bonfire {d?.BonfireDayStacks??0} stacks; next roll lure {d?.BonfireDayRadius??0} zones" +
            (d?.Settlement==true?" [PERMANENT SETTLEMENT]":"") +
            "");
    }
}
