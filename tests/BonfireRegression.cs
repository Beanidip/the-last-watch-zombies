using System.Reflection;
using SeventhNightZombies;
using Vintagestory.API.Common;
using Vintagestory.API.Datastructures;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

public static class BonfireRegression
{
    public static void Run(Action<bool,string> check)
    {
        var wood=new Item{Code=new AssetLocation("game:firewood")};
        var coal=new Item{Code=new AssetLocation("game:charcoal")};
        var piles=new Dictionary<BlockPos,BlockEntityGroundStorage>();
        var removed=new List<BlockPos>();int drops=0;
        for(int x=0;x<2;x++)
        {
            var storage=new BlockEntityGroundStorage();
            storage.Inventory[0].Itemstack=new ItemStack(wood,32);
            piles[new BlockPos(x,64,0,0)]=storage;
        }
        var blocks=Fake.Make<IBlockAccessor>((m,a)=>{
            if(m.Name=="GetBlockEntity")return piles.GetValueOrDefault((BlockPos)a[0]);
            if(m.Name=="SetBlock")removed.Add(((BlockPos)a[1]).Copy());
            return null;
        });
        double hours=8;
        var calendar=Fake.Make<IGameCalendar>((m,a)=>m.Name=="get_TotalHours"?hours:null);
        var world=Fake.Make<IWorldAccessor>((m,a)=>{
            if(m.Name=="SpawnItemEntity")drops+=((ItemStack)a[0]).StackSize;
            return m.Name switch {"get_BlockAccessor"=>blocks,"get_Calendar"=>calendar,"GetItem"=>coal,_=>null};
        });
        var mods=Fake.Make<IModLoader>((m,a)=>null);
        var api=Fake.Make<ICoreAPI>((m,a)=>m.Name switch {"get_World"=>world,"get_ModLoader"=>mods,"get_Side"=>EnumAppSide.Server,_=>null});
        var pit=new BlockEntityPitBonfire{Api=api,Pos=new BlockPos(0,65,0,0)};
        var tree=new TreeAttribute();tree.SetBool("burning",true);tree.SetInt("kindling",4);
        tree.SetInt("fuelStacks",2);tree.SetDouble("litAtHours",0);tree.SetString("fuelCoords","0,64,0;1,64,0");
        pit.FromTreeAttributes(tree,world);
        void Tick()=>typeof(BlockEntityPitBonfire).GetMethod("ServerTick",BindingFlags.NonPublic|BindingFlags.Instance)!.Invoke(pit,null);
        Tick();check(removed.Count==0 && drops==0,"bonfire retains fuel and starter before burnout");
        hours=9;Tick();
        check(removed.Count==3 && removed.Last().Equals(pit.Pos),"burnout removes two fuel piles then starter");
        check(drops==3,"burnout preserves charcoal yield");
        check(!pit.IsBurning && pit.Kindling==0 && pit.FuelStacks==0,"burnout clears bonfire activity");
        Tick();check(removed.Count==3 && drops==3,"later tick cannot repeat burnout drops");
    }
}
