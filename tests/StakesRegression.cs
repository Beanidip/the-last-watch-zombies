using System.Reflection;
using System.Runtime.CompilerServices;
using SeventhNightZombies;
using Vintagestory.API.Common;
using Vintagestory.API.Common.Entities;
using Vintagestory.API.MathTools;
using Vintagestory.API.Server;
using Vintagestory.GameContent;
using Vintagestory.Essentials;

public static class StakesRegression
{
    const BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance;
    static void Set(Zombie z,string name,object value)=>typeof(Zombie).GetField(name,Hidden)!.SetValue(z,value);
    static object Call(Zombie z,string name,params object[] args)=>typeof(Zombie).GetMethod(name,Hidden)!.Invoke(z,args)!;
    class TrapZombie:Zombie
    { public float Damage;public Action OnDamage; public override bool ReceiveDamage(DamageSource src,float amount){OnDamage?.Invoke();Damage+=amount;return true;} }
    class StakeBE:BlockEntity {}
    class Traverser:WaypointsTraverser
    {public Traverser(EntityAgent e):base(e){}public override void Stop(){Active=false;}}
    public static void Run(Action<bool,string> check)
    {
        foreach(var side in new[]{"west","east","north","south"})
            check(PalisadeTrapRules.Matches(new AssetLocation("game:palisadestakes-"+side)),"pictured stakes orientation "+side);
        foreach(var code in new[]{"game:clutter","game:palisadewall","game:loosestick-free","other:palisadestakes-west"})
            check(!PalisadeTrapRules.Matches(new AssetLocation(code)),"unrelated block excluded "+code);
        check(PalisadeTrapRules.WithinRange(3,65,.5,0,65,0),"two block boundary included");
        check(!PalisadeTrapRules.WithinRange(3.01,65,.5,0,65,0),"outside range excluded");
        var random=new Random(42);var durations=new HashSet<int>();
        for(int i=0;i<500;i++)durations.Add(PalisadeTrapRules.Duration(random));
        check(durations.Min()==3 && durations.Max()==20,"3-20 inclusive duration range");
        bool loaded=true;long now=0;int reads=0;var stakePos=new BlockPos(1,65,0,0);
        var air=new Block{BlockId=0};var stake=new Block{BlockId=2,Code=new AssetLocation("game:palisadestakes-west")};
        var loose=new Block{BlockId=6,Code=new AssetLocation("game:loosestick-free")};
        Block atStake=stake;int replacements=0;bool intactBeforeDamage=false;
        var chunk=Fake.Make<IWorldChunk>((m,a)=>null);
        var blocks=Fake.Make<IBlockAccessor>((m,a)=>{
            if(m.Name=="GetChunkAtBlockPos")return loaded?chunk:null;
            if(m.Name=="SetBlock"){
                check((int)a[0]==6 && ((BlockPos)a[1]).Equals(stakePos),"replacement is exact loose-stick block at trap position");
                atStake=loose;replacements++;return null;
            }
            if(m.Name=="GetBlock"){reads++;return ((BlockPos)a[0]).Equals(stakePos)?atStake:air;}
            return null;
        });
        var world=Fake.Make<IServerWorldAccessor>((m,a)=>m.Name switch {
            "GetBlock"=>stake,"get_BlockAccessor"=>blocks,"get_ElapsedMilliseconds"=>now,"get_Rand"=>random,_=>null});
        var core=new SeventhNightSystem{LooseStickBlockId=6, Server=Fake.Make<ICoreServerAPI>((m,a)=>m.Name=="get_World"?world:null)};
        foreach(int id in new[]{2,3,4,5})core.SharpenedStakeBlockIds.Add(id);
        TrapZombie Make(string code)
        {
            var z=new TrapZombie{Alive=true,World=world,Code=new AssetLocation("seventhnightzombies:"+code)};
            typeof(Entity).GetProperty("Properties")!.SetValue(z,new EntityProperties{Code=z.Code});
            z.Pos.SetPos(0,65,0);Set(z,"system",core);Set(z,"path",RuntimeHelpers.GetUninitializedObject(typeof(Traverser)));
            Call(z,"LoadPalisadeTrap");return z;
        }
        foreach(var side in new[]{"west","east","north","south"})
        foreach(var code in new[]{"zombie","alphazombie","grabber","hivezombie","screamer","bloater"})
        {
            core.StakeTimers.Clear();atStake=stake;stake.Code=new AssetLocation("game:palisadestakes-"+side);
            stake.BlockId=2+Array.IndexOf(new[]{"west","east","north","south"},side);
            int oldReplacements=replacements;
            var z=Make(code);z.OnDamage=()=>intactBeforeDamage=ReferenceEquals(atStake,stake);now+=2000;z.Pos.Motion.Set(1,1,1);Set(z,"leapUntilMs",now+1000);
            check((bool)Call(z,"TickPalisadeTrap",.1f),"variant caught "+code);
            check(z.Damage==5,"one damage hit "+code);
            check(z.Pos.Motion.X==0 && z.Pos.Motion.Y==0 && z.Pos.Motion.Z==0,"movement and leap halted "+code);
            check(replacements==oldReplacements && intactBeforeDamage,"trap remains intact on trigger");
            core.TickStakeTimers(10);
            var second=Make("zombie");now+=2000;
            check((bool)Call(second,"TickPalisadeTrap",.1f) && second.Damage==5,"second zombie caught by same stakes");
            check(core.StakeTimers[stakePos].Remaining==20,"additional trigger does not reset countdown");
            atStake=stake;
            var duration=z.Attributes.GetDouble("thelastwatch-palisade-trap-seconds");Call(z,"LoadPalisadeTrap");
            check(z.Attributes.GetDouble("thelastwatch-palisade-trap-seconds")==duration,"saved trap resumes "+code);
            Call(z,"TickPalisadeTrap",21f);now+=2000;
            check((bool)Call(z,"TickPalisadeTrap",.1f) && z.Damage==10,"released zombie can be caught again");
            core.TickStakeTimers(19.9f);
            check(ReferenceEquals(atStake,stake),"trap intact before thirty seconds");
            core.TickStakeTimers(.11f);
            check(replacements==oldReplacements+1 && ReferenceEquals(atStake,loose),"trap becomes loose stick at thirty seconds");
            core.TickStakeTimers(30);
            check(replacements==oldReplacements+1 && core.StakeTimers.Count==0,"timer removed after one replacement");
            Call(z,"TickPalisadeTrap",21f);now+=2000;
            check(!(bool)Call(z,"TickPalisadeTrap",.1f) && z.Damage==10,"loose stick does not trap");

        }
        // Only triggered blocks persist, including their dimension and remaining time.
        core.StakeTimers.Clear();atStake=stake;core.TriggerStake(stakePos,stake.Id);
        core.TickStakeTimers(12);
        var saved=Newtonsoft.Json.JsonConvert.SerializeObject(core.StakeTimers.Values);
        var restored=Newtonsoft.Json.JsonConvert.DeserializeObject<List<SeventhNightSystem.StakeTimer>>(saved)!;
        core.StakeTimers.Clear();foreach(var timer in restored)core.StakeTimers[timer.Position]=timer;
        check(core.StakeTimers[stakePos].Remaining==18,"saved countdown retains eighteen seconds");
        loaded=false;core.TickStakeTimers(20);
        check(ReferenceEquals(atStake,stake) && core.StakeTimers[stakePos].Remaining==0,"unloaded expired trap waits without chunk loading");
        loaded=true;core.TickStakeTimers(1);
        check(ReferenceEquals(atStake,loose) && core.StakeTimers.Count==0,"expired trap becomes loose stick when loaded");
        atStake=stake;core.TriggerStake(stakePos,stake.Id);atStake=air;core.TickStakeTimers(30);
        check(ReferenceEquals(atStake,air) && core.StakeTimers.Count==0,"removed trap never overwrites replacement block");
        atStake=stake;var legacy=Make("zombie");legacy.Attributes.SetBool("thelastwatch-palisade-trap-used",true);
        Call(legacy,"LoadPalisadeTrap");now+=2000;
        check((bool)Call(legacy,"TickPalisadeTrap",.1f),"legacy used flag no longer grants immunity");
        core.StakeTimers.Clear();
        atStake=new Block{BlockId=99,Code=new AssetLocation("game:clutter")};var wrong=Make("zombie");now+=2000;
        check(!(bool)Call(wrong,"TickPalisadeTrap",.1f) && wrong.Damage==0,"clutter is harmless");
        atStake=stake;wrong.Pos.Dimension=1;now+=2000;
        check(!(bool)Call(wrong,"TickPalisadeTrap",.1f),"other dimension harmless");
        now+=2000;
        for(int i=0;i<64;i++)check(core.StakeScanBudget.TakeScan(now,64),"bounded scan slot");
        var deferred=Make("zombie");Set(deferred,"nextPalisadeScanMs",0L);int beforeDeferred=reads;
        check(!(bool)Call(deferred,"TickPalisadeTrap",.1f) && reads==beforeDeferred,"global scan limit prevents work");
    }
}
