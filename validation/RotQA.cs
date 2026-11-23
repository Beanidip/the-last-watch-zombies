using Vintagestory.API.Common;
using Vintagestory.API.Server;
using Vintagestory.API.MathTools;
using Vintagestory.API.Common.Entities;
using Vintagestory.GameContent;
using SeventhNightZombies;
using System.Reflection;
using System.Runtime.CompilerServices;
public class RotQA:ModSystem {
 public override void StartServerSide(ICoreServerAPI api) {
 api.Event.ServerRunPhase(EnumServerRunPhase.RunGame,()=>api.Event.RegisterCallback(_=>Test(api,0),1000));
 }
 void Test(ICoreServerAPI api,int attempt) {
 var w=api.World;var ba=w.BlockAccessor;var p=new BlockPos(512001,140,512000,0);
 if(ba.GetChunkAtBlockPos(p)==null) {if(attempt<20)api.Event.RegisterCallback(_=>Test(api,attempt+1),2000);return;}
 try {
 int floor=w.GetBlock(new AssetLocation("game:rock-granite")).Id;
 for(int x=511999;x<=512003;x++)for(int z=511998;z<=512002;z++) {
 ba.SetBlock(floor,new BlockPos(x,139,z,0));for(int y=140;y<=144;y++)ba.SetBlock(0,new BlockPos(x,y,z,0));}
 var item=w.GetItem(new AssetLocation("seventhnightzombies:rotconcentrate"));
 if(!item.CollectibleBehaviors.Any(b=>b is CollectibleBehaviorGroundStorable))throw new Exception("Missing placement behavior");
 var slot=new DummySlot(new ItemStack(item,3));var handling=EnumHandHandling.NotHandled;
 var entity=new EntityPlayer{Api=api,Alive=true};
                var assembly=Assembly.Load("VintagestoryLib");
                object Blank(string name)=>RuntimeHelpers.GetUninitializedObject(assembly.GetType(name)!);
                void Field(object o,string name,object value)=>o.GetType().GetField(name,BindingFlags.Instance|BindingFlags.Public|BindingFlags.NonPublic)!.SetValue(o,value);
                var player=(IPlayer)Blank("Vintagestory.Server.ServerPlayer");
                var wd=Blank("Vintagestory.Server.ServerWorldPlayerData");
                var sd=Blank("Vintagestory.Server.ServerPlayerData");

                Field(wd,"Entityplayer",entity);Field(wd,"GameMode",EnumGameMode.Survival);Field(wd,"PlayerUID","stakeqa");
                Field(sd,"PlayerUID","stakeqa");Field(player,"worlddata",wd);Field(player,"serverdata",sd);

 var inventories=new Vintagestory.API.Datastructures.OrderedDictionary<string,InventoryBase>();
 var hotbar=new InventoryGeneric(11,"hotbar","stakeqa",api);hotbar[0].Itemstack=slot.Itemstack;hotbar[10].Itemstack=slot.Itemstack;slot=new DummySlot(slot.Itemstack);
 inventories["hotbar"]=hotbar;inventories["hotbar-stakeqa"]=hotbar;
 var manager=Activator.CreateInstance(assembly.GetType("Vintagestory.Server.ServerPlayerInventoryManager")!,new object[]{inventories,player,w});
 Field(player,"inventoryMgr",manager);Field(player,"server",w);
 var claims=Fake.Make<ILandClaimAPI>((m,a)=>m.Name=="TryAccess"?true:m.Invoke(w.Claims,a));
 entity.World=Fake.Make<IServerWorldAccessor>((m,a)=>m.Name switch {"PlayerByUid"=>player,"get_Claims"=>claims,_=>m.Invoke(w,a)});
 entity.Pos.SetPos(512000.5,140,512000.5);entity.Controls.ShiftKey=true;entity.Controls.CtrlKey=true;
 var selection=new BlockSelection{Position=p.DownCopy(),Face=BlockFacing.UP,HitPosition=new Vec3d(.5,1,.5)};
 var behaviorHandling=EnumHandling.PassThrough;
 item.GetBehavior<CollectibleBehaviorGroundStorable>().OnHeldInteractStart(slot,entity,selection,null,true,ref handling,ref behaviorHandling);
 var pile=ba.GetBlockEntity(p) as BlockEntityGroundStorage;
 if(pile==null)throw new Exception("Ground pile not placed, handling="+handling);
 if(!pile.Behaviors.Any(b=>b is RotBaitBehavior))throw new Exception("Missing pile bait behavior");
 var bait=api.ModLoader.GetModSystem<RotBaitSystem>();bait.Refresh(pile);
 if(!bait.Available(w,p) || bait.Nearest(w,new Vec3d(p.X+20,p.Y,p.Z),0)==null)throw new Exception("Bait discovery failed");
 // Ensure multiple bites can be tested independently of how many the vanilla placement gesture deposits.
 pile.Inventory[0].Itemstack=new ItemStack(item,2);pile.MarkDirty(true);bait.Refresh(pile);
 if(!bait.TryEat(w,p) || pile.Inventory[0].StackSize!=1)throw new Exception("First bite failed");
 if(bait.TryEat(w,p))throw new Exception("Shared bite cooldown failed");
 var zmb=(Zombie)w.ClassRegistry.CreateEntity(w.GetEntityType(new AssetLocation("seventhnightzombies:zombie")));
 zmb.Pos.SetPos(p.X-5,p.Y,p.Z);w.SpawnEntity(zmb);
 var hidden=BindingFlags.NonPublic|BindingFlags.Instance;
 if(!(bool)typeof(Zombie).GetMethod("TickRotBait",hidden)!.Invoke(zmb,new object[]{w.ElapsedMilliseconds})!)throw new Exception("Zombie did not select bait");
 w.DespawnEntity(zmb,new EntityDespawnData{Reason=EnumDespawnReason.Removed});
 api.Event.RegisterCallback(_=>{try {if(!bait.TryEat(w,p) || bait.Available(w,p) || ba.GetBlock(p).Code.Path=="groundstorage")throw new Exception("Empty pile not removed");api.Logger.Notification("ROT QA PASS: vanilla floor placement, loaded bait behavior, 32-block discovery, zombie diversion, shared eating cooldown, consumption and empty-pile cleanup.");}catch(Exception e){api.Logger.Error("ROT QA FAIL: "+e);}},2200);
 } catch(Exception e){api.Logger.Error("ROT QA FAIL: "+e);}
 }
}

public class Fake : DispatchProxy
{
 public System.Func<MethodInfo,object[],object> Handler;
 protected override object Invoke(MethodInfo m,object[] a) => Handler(m,a) ?? (m.ReturnType.IsValueType && m.ReturnType!=typeof(void)?Activator.CreateInstance(m.ReturnType):null);
 public static T Make<T>(System.Func<MethodInfo,object[],object> handler) where T:class
 {var proxy=Create<T,Fake>();((Fake)(object)proxy).Handler=handler;return proxy;}
}
