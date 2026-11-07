using System;
using System.Linq;
using ChitakTech;

namespace ChitakTech
{
    public class ChitakTechRuntimeData { public string planet="Kerbin",biome="Grasslands"; }
    public partial class ChitakTechISRU
    {
        internal static bool IsPartEVAConfig(ConfigNode m)=>m.GetValue("moduleType")=="partEVA"||m.GetValues("recipeType").Contains("partEVA");
        internal static bool IsKerbalEVAConfig(ConfigNode m)=>m.GetValue("moduleType")=="kerbalEVA"||m.GetValues("recipeType").Contains("kerbalEVA");
    }
}
internal static class SurvivalProfileTests
{
    internal static int Run()
    {
        int count=0;
        void Check(bool ok,string why){count++;if(!ok)throw new Exception(why+": "+string.Join("; ",CTShared.Errors));}
        ConfigNode Biome(ConfigNode n,string planet,string biome,string max)
        { var p=n.AddNode("PLANET_CONFIG");p.AddValue("name",planet);var b=p.AddNode("BIOME_CONFIG");b.AddValue("name",biome);b.AddValue("maxAltitude",max);return b; }
        ConfigNode Recipe(string name,string max)
        { var r=new ConfigNode("RECIPE");r.AddValue("name",name);r.AddValue("recipeType","test");r.AddValue("recipeTime",1);
          var o=r.AddNode("OUTPUT_RESOURCE");o.AddValue("name","Water");o.AddValue("amount",1);Biome(r,"ALL","ALL",max);return r; }
        var globals=new[]{new ChitakTechISRURecipe(Recipe("Imported","500"),"test")};
        var data=new ChitakTechRuntimeData();var saved=new ConfigNode();
        foreach(string type in new[]{"ChitakTechStorage","ChitakTechMaterialStorage","ChitakTechDestructivePart","ChitakTechGenerator"})
        {
            var m=new ConfigNode();m.AddValue("name",type);var b=Biome(m,"ALL","ALL","100");
            if(type=="ChitakTechGenerator")
            {
                Check(SurvivalProfile.Create(m,saved,globals,"test")==null,"default generator VOID cannot destroy an unloaded vessel");
                m.AddValue("powerfail","EXPLODE");
            }
            b.AddValue("maxOperatingAltitude",1);b.AddValue("disabled",true);b.AddValue("volumeMul",2);
            var profile=SurvivalProfile.Create(m,saved,globals,"test");var result=profile.At(data);
            Check(result.config.GetValue("maxAltitude")=="100","survival profile exists for "+type);
            Check(!result.config.HasValue("disabled")&&!result.config.HasValue("maxOperatingAltitude")&&!result.config.HasValue("volumeMul"),"operating limits/multipliers do not enter background survival");
            if(type=="ChitakTechGenerator")
            {
                m.AddValue("displayName","  ");
                Check(SurvivalProfile.Create(m,saved,globals,"test").At(data)!=null,"blank generator name uses its normal fallback");
            }
        }
        var module=new ConfigNode();module.AddValue("name","ChitakTechISRU");module.AddValue("recipeType","test");
        module.AddNode(Recipe("Local","200"));saved.AddValue("activeRecipeName","Imported");
        Check(SurvivalProfile.Create(module,saved,globals,"test").At(data).config.GetValue("maxAltitude")=="500","saved imported recipe selected over first local");
        var overrideBiome=Biome(module,"ALL","ALL","400");
        Check(SurvivalProfile.Create(module,saved,globals,"test").At(data).config.GetValue("maxAltitude")=="400","module planet override merges into selected recipe");
        Biome(module,"Kerbin","Grasslands","300");
        Check(SurvivalProfile.Create(module,saved,globals,"test").At(data).config.GetValue("maxAltitude")=="300","exact planet/biome wins");
        data.planet="Duna";
        Check(SurvivalProfile.Create(module,saved,globals,"test").At(data).config.GetValue("maxAltitude")=="400","global fallback retained");
        var setting=module.AddNode("SETTING");setting.AddValue("name","Ceiling");setting.AddValue("var","ceiling");setting.AddValue("max",1000);setting.AddValue("default",100);
        overrideBiome.SetValue("maxAltitude","(SETTING:ceiling)");
        var savedSetting=saved.AddNode("RECIPE_SETTING_VALUES").AddNode("RECIPE");savedSetting.AddValue("name","Imported");
        var entry=savedSetting.AddNode("SETTING");entry.AddValue("var","ceiling");entry.AddValue("value",250);
        Check(SurvivalProfile.Create(module,saved,globals,"test").At(data).config.GetValue("maxAltitude")=="250","saved numeric settings affect limits");
        module.SetValue("moduleType","partEVA");Check(SurvivalProfile.Create(module,saved,globals,"test")==null,"inactive inventory processor excluded");
        var dynamicModule=new ConfigNode();dynamicModule.AddValue("name","ChitakTechStorage");
        Biome(dynamicModule,"[currentBody]","[currentBiome]","123");
        var dynamicProfile=SurvivalProfile.Create(dynamicModule,saved,globals,"test");
        Check(dynamicProfile.At(data).config.GetValue("maxAltitude")=="123","environment strings resolve at the sampled location");
        data.planet="Eve";data.biome="Lowlands";
        Check(dynamicProfile.At(data).config.GetValue("maxAltitude")=="123","environment strings refresh for another location");
        dynamicModule.GetNode("PLANET_CONFIG").GetNode("BIOME_CONFIG").SetValue("maxAltitude","100*solarMultiplier");
        Check(SurvivalProfile.Create(dynamicModule,saved,globals,"test")==null,"unavailable background sunlight does not invent destruction thresholds");
        return count;
    }
}
