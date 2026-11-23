using SeventhNightZombies;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Vintagestory.API.MathTools;
using System.Reflection;

int passed = 0;
void Check(bool ok, string name) { if (!ok) throw new Exception("FAIL: " + name); passed++; }
var c = new Config(); c.Validate();
Check(Rules.IsSpawnLightSafe(12, c) && !Rules.IsSpawnLightSafe(13, c), "inclusive light 12");
Check(!Rules.IsNight(6 / 24d, c) && Rules.IsNight(18 / 24d, c), "night boundaries");
Check(Rules.IsHorde(6.9, c) && Rules.IsHorde(7.1, c) && !Rules.IsHorde(7.5, c), "seventh night crosses midnight");
Check(Math.Abs(Rules.NextNaturalHordeStartDay(0, c) - (6 + (18 - 5/60d)/24)) < 1e-8, "first horde clock");
Check(!Rules.CanAutomaticallySpawn(6.5, 6.5, c), "day spawn blocked");
c.ZombieSpawnHordeNightOnly = true;
Check(!Rules.CanAutomaticallySpawn(5.9, 5.9, c) && Rules.CanAutomaticallySpawn(6.9, 6.9, c), "horde-only schedule");
Check(!Rules.IsFatalInfection(0, 6.99, c) && Rules.IsFatalInfection(0, 7, c), "infection deadline");
Check(Rules.MayTakeCureDose(0, 0, 0, c) && !Rules.MayTakeCureDose(1, 0, .99, c) && Rules.MayTakeCureDose(1, 0, 1, c), "dose spacing");
Check(Rules.PoisonIntervalSeconds(0,c)==10 && Rules.PoisonIntervalSeconds(1,c)==20, "treatment slows poison");
var hits = new Queue<double>();
Check(!Rules.RecordHit(hits,0,4,60) && !Rules.RecordHit(hits,1,4,60) && !Rules.RecordHit(hits,2,4,60) && Rules.RecordHit(hits,3,4,60), "four normal bites");
Check(!Rules.RecordHit(hits,100,4,60) && hits.Count == 1, "old bites expire");
c.TargetWhitelist = ["game:pig*"]; c.TargetBlacklist = ["game:pig-wild"];
Check(Rules.TargetCodeAllowed("game:pig-tame",false,c) && !Rules.TargetCodeAllowed("game:pig-wild",true,c), "blacklist wins");
for (int x=-300;x<=300;x++) {
 var zone = WatchZone.FromBlock(x,x,0);
 Check(zone.X == (int)Math.Floor(x/96d), "negative coordinate floor");
 Check(zone == WatchZone.FromChunk((int)Math.Floor(x/32d),(int)Math.Floor(x/32d),0), "chunk/block agreement");
}
Check(new WatchZone(0,0,0).Distance(new WatchZone(1,0,0)) == int.MaxValue,"dimension isolation");
Check(LastWatchZones.DestinationForMigration(new(0,0,0),new(0,9,-9),1)==new WatchZone(0,1,-1),"normal migration one step");
Check(LastWatchZones.DestinationForMigration(new(0,0,0),new(0,9,-9),2)==new WatchZone(0,2,-2),"horde migration two steps");
Check(LastWatchZones.DestinationForMigration(new(0,0,0),new(0,1,-1),2)==new WatchZone(0,1,-1),"migration no overshoot");
var zoneState = new WatchZoneState { AmbientPopulationAddedToday=5, DormantReactivatedToday=8, PlayerDeaths=2, TraderPopulationSeeded=true, PermanentSettlement=true, StonePaths=25 };
zoneState.AmbientPopulationAddedByPlayer["player"] = 5;
zoneState.Strongholds.Add(new WatchBuilding { StrongholdStrength=300 });
zoneState.BonfireDailyRecords.Add(new() { Day=2, PeakFuelStacks=6 });
zoneState.BonfireDailyRecords.Add(new() { Day=4, PeakFuelStacks=12 });
LastWatchZones.ResetDailyAllowances(zoneState,3);
Check(zoneState.AmbientPopulationAddedToday==0 && zoneState.DormantReactivatedToday==0 && zoneState.AmbientPopulationAddedByPlayer.Count==0 && zoneState.PlayerDeaths==0,"daily quotas reset");
Check(zoneState.TraderPopulationSeeded && zoneState.PermanentSettlement && zoneState.StonePaths==25 && zoneState.Strongholds.Count==1,"reset preserves settlement and trader state");
Check(zoneState.BonfireDailyRecords.Count==1 && zoneState.BonfireDailyRecords[0].Day==4,"future bonfire record preserved");
var budget = new WorkBudget();
Check(budget.TakeRoute(0,2) && budget.TakeRoute(0,2) && !budget.TakeRoute(999,2) && budget.TakeRoute(1000,2),"route budget window");
Check(budget.TakeScan(0,1) && !budget.TakeScan(1,1),"independent scan budget");
c = new Config { InfectionTickSeconds=60, TreatedInfectionTickSeconds=double.NaN, MoanMinSeconds=500, MoanMaxSeconds=float.NaN };
c.Validate();
Check(c.TreatedInfectionTickSeconds >= c.InfectionTickSeconds && c.MoanMaxSeconds >= c.MoanMinSeconds,"nonfinite dependent bounds");
c = new Config();
Check(!AdminConfiguration.TrySet(c,nameof(Config.AmbientSpawnChancePerSecond),"NaN",out _),"reject NaN command");
Check(!AdminConfiguration.TrySet(c,nameof(Config.SpawnMaxLightLevel),"32",out _),"reject excessive light");
Check(AdminConfiguration.TrySet(c,nameof(Config.SpawnMaxLightLevel),"12",out _),"accept light 12");
var custom = new Config { HordeSpawnIgnoreLightLevel=true,CurePsychedelicEnabled=false,CureDosesRequired=3,SpawnMaxLightLevel=3 };
custom.Validate();
Check(custom.HordeSpawnIgnoreLightLevel && !custom.CurePsychedelicEnabled && custom.CureDosesRequired==3 && custom.SpawnMaxLightLevel==3,"validate preserves intentional config");
var core = new SeventhNightSystem(); core.Settings.DormantPopulationCap=100;
var pool = new ZombiePopulation(core);
for(int i=0;i<60;i++) pool.AddPending(new Vec3d(-1,64,-1),0,i%3==0);
Check(pool.DormantCount==60,"seed count");
string source=pool.MigrationSnapshot().Single().Key;
var moves=new[]{new MigrationTransfer{Source=source,DestX=3,DestY=2,DestZ=3,DestDimension=0,Count=10},new MigrationTransfer{Source=source,DestX=3,DestY=2,DestZ=3,DestDimension=0,Count=5,Alpha=true}};
Check(pool.ApplyMigration(moves),"migration accepted");
Check(pool.DormantCount==60 && pool.MigrationSnapshot().Sum(x=>x.Normal+x.Alpha)==60,"actual conservation");
string before=JsonConvert.SerializeObject(pool.MigrationSnapshot());
Check(!pool.ApplyMigration(new[]{new MigrationTransfer{Source=source,DestX=0,DestY=2,DestZ=0,DestDimension=0,Count=999}}),"reject overdraw");
Check(before==JsonConvert.SerializeObject(pool.MigrationSnapshot()),"failed transaction atomic");
Check(!pool.ApplyMigration(new[]{new MigrationTransfer{Source=source,DestX=0,DestY=2,DestZ=0,DestDimension=1,Count=1}}),"migration cannot change dimension");
for(int i=0;i<100;i++) pool.AddPending(new Vec3d(1,64,1),0,false);
Check(pool.DormantCount==100,"seeding cap");
var director=new LastWatchZones();
typeof(LastWatchZones).GetField("core",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(director,core);
var key=new WatchZone(0,0,0);
for(int i=0;i<5;i++) director.RecordAmbientPopulationSpawn(key,i%2==0?"a":"b");
Check(!director.CanAddAmbientPopulationSpawn(key,"a") && !director.CanAddAmbientPopulationSpawn(key,"b"),"two players share quota");
Check(director.CanAddAmbientPopulationSpawn(new(0,1,0),"a"),"different zone independent quota");
core.Settings.DormantReactivationPerZonePerDay=2;
director.RecordDormantReactivation(key); director.RecordDormantReactivation(key);
Check(!director.CanReactivateDormant(key),"wake daily limit");
core.Settings.DormantReactivationPerZonePerDay=0;
Check(director.CanReactivateDormant(key),"zero wake limit is unlimited");

var untouched = new WatchZone(0,8,8);
Check(!director.CanReactivateDormant(untouched), "unvisited zone waits for ambient quota");
for (int i=0;i<4;i++) director.RecordAmbientPopulationSpawn(untouched,"a");
Check(director.AmbientSlotsRemaining(untouched)==1 && !director.CanReactivateDormant(untouched), "partial ambient quota blocks dormant wake");
director.RecordAmbientPopulationSpawn(untouched,"a");
Check(director.CanReactivateDormant(untouched), "fifth ambient unlocks dormant wake");
director.ResetCurrentZoneAmbientQuota(untouched);
Check(!director.CanReactivateDormant(untouched), "ambient reset closes dormant gate again");
core.Settings.ZombiePopDailyAddLimit=0;
Check(director.CanReactivateDormant(untouched), "zero ambient allowance permits dormant-only play");
core.Settings.ZombiePopDailyAddLimit=5;
var retry=new SpawnRetryBackoff();
retry.Record(key,0,false); retry.Record(key,0,false);
Check(retry.Ready(key,0), "first two placement failures have no backoff");
retry.Record(key,0,false);
Check(!retry.Ready(key,14999) && retry.Ready(key,15000), "third failure retries after 15 seconds");
retry.Record(key,15000,false);
Check(retry.RemainingSeconds(key,15000)==30, "fourth failure retries after 30 seconds");
retry.Record(key,45000,false);retry.Record(key,105000,false);
Check(retry.RemainingSeconds(key,105000)==60, "retry delay capped at one minute");
retry.Record(key,106000,true);
Check(retry.Ready(key,106000), "successful placement clears failure history");
retry.Record(key,0,false);retry.Record(key,0,false);retry.Record(key,0,false);
retry.Retain(new[]{untouched});
Check(retry.Ready(key,0), "unoccupied zone does not retain retry state");
var expiring=new Zombie();
expiring.Pos.SetPos(1,64,1);
pool.OnDespawn(expiring,new Vintagestory.API.Common.Entities.EntityDespawnData {
 Reason=Vintagestory.API.Common.EnumDespawnReason.Expire });
Check(pool.DormantCount==100, "natural despawn cannot exceed dormant cap");
core.Settings.DormantPopulationCap=101;
var hordeReturn=new Zombie();hordeReturn.Pos.SetPos(1,64,1);
hordeReturn.WatchedAttributes.SetBool("seventhnight-horde-spawn",true);
pool.OnDespawn(hordeReturn,new Vintagestory.API.Common.Entities.EntityDespawnData {
 Reason=Vintagestory.API.Common.EnumDespawnReason.Expire });
pool.OnDespawn(hordeReturn,new Vintagestory.API.Common.Entities.EntityDespawnData {
 Reason=Vintagestory.API.Common.EnumDespawnReason.Expire });
Check(pool.DormantCount==101, "horde despawn pools once when capacity exists");
var saved=new Dictionary<string,JObject>();
var api=Fake.Make<Vintagestory.API.Server.ICoreServerAPI>((m,a)=> {
 if(m.Name=="StoreModConfig") {saved[(string)a[1]]=(JObject)a[0];return null;}
 if(m.Name=="LoadModConfig") return saved.TryGetValue((string)a[0],out var value)?value:null;
 return null;
});
StandaloneConfigStorage.Save(api,new Config());
Check(saved.Values.All(j=>j.Properties().All(p=>!StandaloneConfigStorage.IsRetired(p.Name))), "generated configs omit retired fields");
Check(saved[StandaloneConfigStorage.MainFile]["NormalHitsToInfect"]!=null && saved[StandaloneConfigStorage.MainFile]["NightStartsAtHour"]!=null, "functional rule settings remain visible");
saved[StandaloneConfigStorage.AdvancedFile]["LostTargetTurnSeconds"]=17;
Check(StandaloneConfigStorage.Load(api).LostTargetTurnSeconds==17, "legacy settings still deserialize safely");
var tuning=new Config {ZombieLostTargetPursuitSeconds=10};tuning.UpgradeLastSeenPursuit();
Check(tuning.ZombieLostTargetPursuitSeconds==25, "former pursuit default upgrades once");
tuning.ZombieLostTargetPursuitSeconds=10;tuning.UpgradeLastSeenPursuit();
Check(tuning.ZombieLostTargetPursuitSeconds==10, "later explicit pursuit choice survives restart");
Check(AdminConfiguration.Keys["lastseen"]==nameof(Config.ZombieLostTargetPursuitSeconds), "pursuit command maps to active setting");

VariantRegression.Run(Check);
VariantRegression.RunRelease5(Check);
var baitItem = new Vintagestory.API.Common.Item { Code = new("seventhnightzombies:rotconcentrate") };
var ordinaryRot = new Vintagestory.API.Common.Item { Code = new("game:rot") };
var baitStack = new Vintagestory.API.Common.ItemStack(baitItem, 3);
Check(RotBaitRules.IsBait(baitStack) && !RotBaitRules.IsBait(new(ordinaryRot, 3)) && !RotBaitRules.IsBait(null), "only concentrate is bait");
Check(!RotBaitRules.IsBait(new(baitItem, 0)), "empty bait stack ignored");
var pilePos = new BlockPos(0, 10, 0, 0);
Check(RotBaitRules.InRange(new(32.5,10.2,.5),0,pilePos), "bait includes 32-block boundary");
Check(!RotBaitRules.InRange(new(32.501,10.2,.5),0,pilePos), "bait excludes outside radius");
Check(!RotBaitRules.InRange(new(.5,10.2,.5),1,pilePos), "bait does not cross dimensions");
Check(!RotBaitRules.InRange(new(32.5,10.2,32.5),0,pilePos), "bait uses spherical distance");
var inventory = new Vintagestory.API.Common.InventoryGeneric(3, "bait-test", null);
inventory[0].Itemstack = new(ordinaryRot, 4);
inventory[1].Itemstack = baitStack;
Check(RotBaitRules.FoodSlot(inventory) == inventory[1], "bait slot found among unrelated items");
RotBaitRules.FoodSlot(inventory).TakeOut(1);
Check(inventory[1].StackSize == 2 && inventory[0].StackSize == 4, "one bite consumes exactly one concentrate");
RotBaitRules.FoodSlot(inventory).TakeOut(2);
Check(RotBaitRules.FoodSlot(inventory) == null && !inventory.Empty, "bait exhaustion preserves unrelated inventory");
Check(RotBaitRules.EatIntervalMs == 2000, "shared pile eating interval is two seconds");
StakesRegression.Run(Check);
BonfireRegression.Run(Check);
Console.WriteLine($"PASS: {passed} regression assertions");

public class Fake : DispatchProxy
{
 public Func<MethodInfo,object[],object> Handler;
 protected override object Invoke(MethodInfo m,object[] a) => Handler(m,a) ?? (m.ReturnType.IsValueType && m.ReturnType!=typeof(void)?Activator.CreateInstance(m.ReturnType):null);
 public static T Make<T>(Func<MethodInfo,object[],object> handler) where T:class
 {var proxy=Create<T,Fake>();((Fake)(object)proxy).Handler=handler;return proxy;}
}
