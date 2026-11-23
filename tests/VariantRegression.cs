using System.Reflection;
using System.Runtime.CompilerServices;
using SeventhNightZombies;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.Essentials;

public static class VariantRegression
{
    const BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance;
    static void Set(object o,string field,object value)=>o.GetType().GetField(field,Hidden)!.SetValue(o,value);
    static object Call(object o,string method,params object[] args)=>o.GetType().GetMethod(method,Hidden)!.Invoke(o,args)!;
    static void Kind(Entity entity,string code)
    { entity.Code=new AssetLocation("seventhnightzombies:"+code); typeof(Entity).GetProperty("Properties")!.SetValue(entity,new EntityProperties{Code=entity.Code}); }
    public static void Run(Action<bool,string> check)
    {
        var config=new Config();
        check(ZombieVariantRules.Roll(.01,config)==ZombieVariant.Grabber,"grabber chance branch");
        check(ZombieVariantRules.Roll(.05,config)==ZombieVariant.Hive,"hive chance branch");
        check(ZombieVariantRules.Roll(.065,config)==ZombieVariant.Screamer,"screamer chance branch");
        check(ZombieVariantRules.Roll(.08,config)==ZombieVariant.Bloater,"bloater chance branch");
        check(ZombieVariantRules.Roll(.5,config)==ZombieVariant.Normal,"ordinary remainder");
        for(int i=0;i<100;i++)check(ZombieVariantRules.Roll(i/100.0,config)!=ZombieVariant.Alpha,"random rolls never create Alpha");
        config.GrabberSpawnChance=1;config.HiveSpawnChance=1;config.Validate();
        check(config.GrabberSpawnChance+config.HiveSpawnChance+config.ScreamerSpawnChance+config.BloaterSpawnChance<=1.000001,"variant chances normalize");
        var leap=ZombieVariantRules.LeapMotion(-3,4);
        check(leap.X<0 && leap.Z>0 && leap.Y>0 && Math.Abs(Math.Sqrt(leap.X*leap.X+leap.Z*leap.Z)-.22)<.00001,"leap direction and bounded speed");
        check(ZombieVariantRules.LeapMotion(0,0).X==0,"zero-distance leap is finite");
        var f=new Fixture();
        f.Core.SetSpawnsPaused(true);
        check(f.Saved.Single().Value[0]==1 && f.Core.SpawnsPaused,"pause persists into world data immediately");
        check(!f.Core.AutomaticSpawnWindowOpen(),"pause closes automatic spawn window");
        foreach(var kind in Enum.GetValues<ZombieVariant>())
            check(!f.Core.Spawn(new Vec3d(20,65,20),0,variant:kind),"pause blocks central spawn "+kind);
        var screamer=f.Zombie("screamer");
        check(!f.Core.TrySpawnScreamerReinforcement(screamer),"pause blocks Screamer summons");
        check(!f.Core.TryReleaseBees(f.Zombie("hivezombie")),"pause blocks bees");
        var creative=f.Zombie("grabber"); Call(f.Core,"OnNewZombie",creative);
        check(creative.IsDormant && f.Callbacks.Count==1,"pause suppresses creative/external new zombie");
        f.Callbacks[0](0);
        check(f.Removed==1,"suppressed new entity is removed without pooling");
        var pool=(ZombiePopulation)f.Core.Population;
        pool.AddPending(new Vec3d(0,65,0),0,false);
        check(!pool.TryReactivateAtZone(new WatchZone(0,0,0),new Vec3d(20,65,20),0) && pool.DormantCount==1,
            "paused reactivation preserves dormant count");
        var existing=f.Zombie("zombie");Call(f.Core,"TrackZombie",existing);
        check(!existing.IsDormant && existing.Alive,"paused reload preserves existing zombies");
        f.Core.SetSpawnsPaused(false);
        check(!f.Core.SpawnsPaused && f.Saved.Single().Value[0]==0,"unpause updates saved state");
        // A concrete existing target triggers one saved scream, then exactly ten normals.
        Set(screamer,"target",f.Target);
        Call(screamer,"TickVariant",.5f);
        check(screamer.Attributes.GetBool("lastwatch-scream-used") && screamer.Attributes.GetInt("lastwatch-scream-remaining")==10,"scream arms ten reinforcements once");
        f.Core.SetSpawnsPaused(true);
        var remaining=screamer.Attributes.GetDouble("lastwatch-scream-retry-seconds");
        Call(screamer,"TickVariant",.5f);
        check(screamer.Attributes.GetInt("lastwatch-scream-remaining")==10 && screamer.Attributes.GetDouble("lastwatch-scream-retry-seconds")==remaining,"pause freezes pending summon count and retry clock");
        f.Core.SetSpawnsPaused(false);
        for(int i=0;i<12;i++){f.Now+=500;Call(screamer,"TickVariant",.5f);}
        check(f.Spawned.OfType<Zombie>().Count()==10,"screamer emits ten zombies in bounded ticks");
        check(f.Spawned.OfType<Zombie>().All(z=>z.VariantKind==ZombieVariant.Normal),"reinforcements cannot chain special variants");
        for(int i=0;i<10;i++){f.Now+=500;Call(screamer,"TickVariant",.5f);}
        check(f.Spawned.Count==10 && screamer.Attributes.GetInt("lastwatch-scream-remaining")==0,"used Screamer never starts another wave");
        f.Core.Settings.MaxActiveZombies=f.Core.ActiveZombieCount;
        check(!f.Core.Spawn(new Vec3d(20,65,20),0,variant:ZombieVariant.Grabber),"special variants respect active cap");
        check(!f.Core.TrySpawnScreamerReinforcement(screamer),"reinforcements respect active cap");
        f.Core.Settings.MaxActiveZombies=250;
        var blockedScreamer=f.Zombie("screamer");
        blockedScreamer.Attributes.SetBool("lastwatch-scream-used",true);
        blockedScreamer.Attributes.SetInt("lastwatch-scream-remaining",10);
        blockedScreamer.Attributes.SetDouble("lastwatch-scream-retry-seconds",.5);
        Call(blockedScreamer,"TickVariant",.5f);
        check(blockedScreamer.Attributes.GetInt("lastwatch-scream-remaining")==0,"expired summon queue discards unplaceable remainder");
        // Work cap counts failed searches too.
        f.Blocked=true;f.Now+=2000;
        int before=f.BlockChecks;
        for(int i=0;i<12;i++)f.Core.TrySpawnScreamerReinforcement(screamer);
        int after=f.BlockChecks;
        f.Core.TrySpawnScreamerReinforcement(screamer);
        check(after>before && after==f.BlockChecks,"global special work budget stops repeated failed terrain searches");
        f.Blocked=false;f.Now+=2000;
        var hive=f.Zombie("hivezombie");Set(hive,"target",f.Target);
        Call(hive,"TickVariant",.5f);
        check(f.Spawned.Count(e=>e.Code?.Path=="zombiebees")==1,"hive releases one vanilla-based bee swarm");
        Call(hive,"TickVariant",.5f);
        check(f.Spawned.Count(e=>e.Code?.Path=="zombiebees")==1,"bee cooldown prevents per-tick emission");
        var bees=f.Spawned.Last();
        check(!f.Core.CanZombieTarget(bees),"zombies do not fight their own bees");
        var behavior=new TemporaryZombieSwarm(bees);
        bees.Attributes.SetDouble("lastwatch-swarm-age",19.5);
        behavior.OnGameTick(.5f);
        check(!bees.Alive,"bee swarm expires after twenty simulated seconds");
        var grabber=f.Zombie("grabber");Set(grabber,"target",f.Target);grabber.OnGround=true;
        Call(grabber,"TickVariant",.5f);
        check(grabber.Pos.Motion.Y>0 && grabber.Pos.Motion.X>0 && !grabber.OnGround,"grounded Grabber launches toward prey");
        check(grabber.Attributes.GetDouble("lastwatch-ability-cooldown")>=8,"leap stores cooldown");
        var bloater=f.Zombie("bloater");
        Call(bloater,"OnVariantKilled");Call(bloater,"OnVariantKilled");
        check(f.RotDropped==32,"bloater burst drops extra rot only once");
        check(f.Player.Damage==4 && f.Player.Attributes.GetDouble(SeventhNightSystem.RotPoisonKey)==10,"nearby survivor receives burst and temporary poison");
        for(int i=0;i<10;i++)Call(f.Core,"TickRotPoison",f.Player,1f);
        check(f.Player.Damage==9 && !f.Player.Attributes.HasAttribute(SeventhNightSystem.RotPoisonKey),"poison deals bounded damage and expires");
        Call(f.Core,"TickRotPoison",f.Player,1f);
        check(f.Player.Damage==9,"expired poison cannot tick again");
        check(!f.Player.WatchedAttributes.HasAttribute(SeventhNightSystem.InfectionKey),"rot poison is distinct from bite infection");
        f.Player.Pos.SetPos(20,65,0);
        Call(f.Zombie("bloater"),"OnVariantKilled");
        check(f.Player.Damage==9,"distant survivor is outside burst radius");
        f.Player.Pos.SetPos(1,65,0);f.Blocked=true;
        Call(f.Zombie("bloater"),"OnVariantKilled");
        check(f.Player.Damage==9,"solid obstruction blocks burst");
        f.Blocked=false;f.Player.Pos.Dimension=1;
        Call(f.Zombie("bloater"),"OnVariantKilled");
        check(f.Player.Damage==9,"burst cannot cross dimensions");
        f.Player.Pos.Dimension=0;
        int dropsBeforeExpiry=f.RotDropped;
        var expiry=f.Zombie("bloater");expiry.Die(EnumDespawnReason.Expire);
        check(f.RotDropped==dropsBeforeExpiry && !expiry.WatchedAttributes.GetBool("lastwatch-burst-used"),"natural expiry cannot burst or drop rot");
    }
    public static void RunRelease5(Action<bool,string> check)
    {
        var f=new Fixture(true);
        f.LowCeiling=true;
        check(f.Core.CanFitVariant(new Vec3d(20,65,20),0,.33),"ordinary zombie fits two-block space");
        foreach(var kind in new[]{ZombieVariant.Grabber,ZombieVariant.Hive,ZombieVariant.Screamer})
            check(!f.Core.Spawn(new Vec3d(20,65,20),0,variant:kind),"taller variant rejects low ceiling: "+kind);
        f.Core.Settings.CurePsychedelicEnabled=false;
        check(!f.Core.TryTakeCureDose(f.Player) && f.Player.Healing==0,"uninfected cure rejection cannot heal");
        f.Player.WatchedAttributes.SetDouble(SeventhNightSystem.InfectionKey,0);
        check(f.Core.TryTakeCureDose(f.Player) && f.Player.Healing==20,"accepted cure dose heals twenty HP");
        check(!f.Core.TryTakeCureDose(f.Player) && f.Player.Healing==20,"early repeated cure cannot heal");
        f.Day=f.Core.Settings.CureDoseIntervalDays;
        check(f.Core.TryTakeCureDose(f.Player) && f.Player.Healing==40,"next valid dose also heals twenty HP");
        f.Player.Alive=false;f.Day+=2;
        check(!f.Core.TryTakeCureDose(f.Player) && f.Player.Healing==40,"dead player cannot consume cure or heal");
    }
    sealed class SilentServerPlayer:Vintagestory.Server.ServerPlayer,IServerPlayer
    {
        public SilentServerPlayer():base(null,null){}
        public new void SendMessage(int groupId,string message,EnumChatType chatType,string data=null){}
    }
    sealed class TestPlayer:EntityPlayer
    {public float Damage,Healing; public override bool ReceiveDamage(DamageSource source,float damage){if(source.Type==EnumDamageType.Heal)Healing+=damage;else Damage+=damage;return true;}}
    sealed class TestBees:EntityAgent
    {public override void Die(EnumDespawnReason reason=EnumDespawnReason.Death,DamageSource source=null){Alive=false;}}
    sealed class Traverser:WaypointsTraverser
    {public Traverser(EntityAgent e):base(e){} public override void Stop(){Active=false;}}
    sealed class Fixture
    {
        public SeventhNightSystem Core;
        public ICoreServerAPI Api;
        public IServerWorldAccessor World;
        public long Now;
        public int Removed,BlockChecks,RotDropped;
        public bool Blocked,LowCeiling;
        public double Day;
        public Dictionary<string,byte[]> Saved=new();
        public List<Action<float>> Callbacks=new();
        public List<Entity> Spawned=new();
        public TestPlayer Player=new(){Alive=true};
        public EntityAgent Target=new(){Alive=true};
        long nextId=1000;
        public Fixture(bool cure=false)
        {
            var air=new Block{BlockId=0,CollisionBoxes=Array.Empty<Cuboidf>()};
            var ground=new Block{BlockId=1,CollisionBoxes=new[]{new Cuboidf(0,0,0,1,1,1)}};
            ground.SideSolid[BlockFacing.UP.Index]=true;
            var chunk=Fake.Make<IWorldChunk>((m,a)=>null);
            var blocks=Fake.Make<IBlockAccessor>((m,a)=>{
                if(m.Name=="GetBlock") {BlockChecks++; return a.Length>1 && a[1] is int layer && layer==BlockLayersAccess.Fluid?air:Blocked || ((BlockPos)a[0]).Y<=64 || (LowCeiling && ((BlockPos)a[0]).Y>=67)?ground:air;}
                return m.Name=="GetChunkAtBlockPos"?chunk:null;
            });
            var claims=Fake.Make<ILandClaimAPI>((m,a)=>m.Name=="Get"?Array.Empty<LandClaim>():null);
            var save=Fake.Make<ISaveGame>((m,a)=>{
                if(m.Name=="StoreData"){Saved[(string)a[0]]=(byte[])a[1];return null;}return null;
            });
            var manager=Fake.Make<IWorldManagerAPI>((m,a)=>m.Name=="get_SaveGame"?save:null);
            var events=Fake.Make<IServerEventAPI>((m,a)=>{
                if(m.Name=="RegisterCallback")Callbacks.Add((Action<float>)a[0]);return null;
            });
            var random=new Random(42);
            var loaded=(System.Collections.IDictionary)Activator.CreateInstance(typeof(IServerWorldAccessor).GetProperty("LoadedEntities")!.PropertyType)!;
            IPlayer player=MakePlayer(Player,cure);
            var calendar=Fake.Make<IGameCalendar>((m,a)=>m.Name=="get_TotalDays"?Day:null);
            var registry=Fake.Make<IClassRegistryAPI>((m,a)=>{
                if(m.Name!="CreateEntity")return null;
                var props=(EntityProperties)a[0];
                Entity e=props.Code.Path=="zombiebees"?new TestBees{Alive=true}:Zombie(props.Code.Path);
                e.World=World;e.Api=Api;e.Code=props.Code;e.EntityId=nextId++;
                typeof(Entity).GetProperty("Properties")!.SetValue(e,props);return e;
            });
            World=Fake.Make<IServerWorldAccessor>((m,a)=>{
                if(m.Name=="SpawnEntity"){Spawned.Add((Entity)a[0]);return null;}
                if(m.Name=="DespawnEntity"){Removed++;return null;}
                if(m.Name=="SpawnItemEntity"){RotDropped+=((ItemStack)a[0]).StackSize;return null;}
                return m.Name switch {
                    "get_ElapsedMilliseconds"=>Now,"get_BlockAccessor"=>blocks,"get_Claims"=>claims,
                    "get_Calendar"=>calendar,"get_Rand"=>random,"get_ClassRegistry"=>registry,"get_AllOnlinePlayers"=>new[]{player},
                    "PlayerByUid"=>player,"get_LoadedEntities"=>loaded,"GetEntitiesAround"=>Array.Empty<Entity>(),
                    "GetItem"=>new Item{Code=new AssetLocation("game:rot")},
                    "GetEntityType"=>new EntityProperties{Code=(AssetLocation)a[0]},_=>null};
            });
            Api=Fake.Make<ICoreServerAPI>((m,a)=>m.Name switch {
                "get_World"=>World,"get_WorldManager"=>manager,"get_Event"=>events,"get_Side"=>EnumAppSide.Server,_=>null});
            Core=new SeventhNightSystem{Server=Api};typeof(SeventhNightSystem).GetProperty("Population")!.SetValue(Core,new ZombiePopulation(Core));
            Player.World=World;Player.WatchedAttributes.SetString("playerUID","test");Player.Pos.SetPos(1,65,0);
            Target.World=World;Kind(Target,"testnpc");Target.Pos.SetPos(5,65,0);
        }
        public Zombie Zombie(string code)
        {
            var z=new Zombie{World=World,Api=Api,Alive=true,EntityId=nextId++};Kind(z,code);z.Pos.SetPos(0,65,0);
            Set(z,"system",Core);Set(z,"path",RuntimeHelpers.GetUninitializedObject(typeof(Traverser)));return z;
        }
        static IPlayer MakePlayer(EntityPlayer entity,bool silent=false)
        {
            var assembly=typeof(Vintagestory.Server.ServerPlayer).Assembly;
            object Blank(Type t)=>RuntimeHelpers.GetUninitializedObject(t);
            void Field(object o,string name,object value)
            {
                for(var type=o.GetType();type!=null;type=type.BaseType)
                {var field=type.GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.DeclaredOnly);if(field!=null){field.SetValue(o,value);return;}}
                throw new Exception("Missing fixture field: "+name);
            }
            var player=(IServerPlayer)Blank(silent?typeof(SilentServerPlayer):typeof(Vintagestory.Server.ServerPlayer));
            var wd=Blank(assembly.GetType("Vintagestory.Server.ServerWorldPlayerData")!);
            var sd=Blank(assembly.GetType("Vintagestory.Server.ServerPlayerData")!);
            Field(wd,"GameMode",EnumGameMode.Survival);Field(wd,"connected",true);Field(wd,"Entityplayer",entity);
            Field(wd,"PlayerUID","test");Field(sd,"PlayerUID","test");Field(player,"worlddata",wd);Field(player,"serverdata",sd);
            return player;
        }
    }
}
