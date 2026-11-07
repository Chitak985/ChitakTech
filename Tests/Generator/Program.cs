using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ChitakTech;

// Only the host is mocked. The production batch insertion/rollback/reset/save code
// is linked into this executable, including the retry-after-void return contract.
public class ConfigNode
{
    readonly Dictionary<string,string> values = new Dictionary<string,string>();
    readonly List<ConfigNode> nodes = new List<ConfigNode>(); public string name;
    public ConfigNode(string name = "") { this.name = name; }
    public void AddValue(string k, object v) => values[k] = Convert.ToString(v, CultureInfo.InvariantCulture);
    public string GetValue(string k) => values.TryGetValue(k, out var v) ? v : null;
    public ConfigNode AddNode(string n) { var child=new ConfigNode(n); nodes.Add(child); return child; }
    public void AddNode(ConfigNode n) => nodes.Add(n);
    public ConfigNode GetNode(string n) => nodes.FirstOrDefault(c=>c.name==n);
    public ConfigNode[] GetNodes(string n) => nodes.Where(c=>c.name==n).ToArray();
    public bool HasNode(string n) => GetNode(n)!=null;
    public void RemoveNode(string n) => nodes.Remove(GetNode(n));
    public void CopyTo(ConfigNode n) { foreach(var p in values)n.AddValue(p.Key,p.Value); foreach(var c in nodes){var copy=n.AddNode(c.name);c.CopyTo(copy);} }
}
public class Part { public List<object> Modules = new List<object>(); public double stock, stockCapacity=100; public AvailablePart partInfo = new AvailablePart(); }
public class Vessel { public List<Part> parts = new List<Part>(); }
public enum ResourceFlowMode { STAGE_PRIORITY_FLOW }
public enum ScreenMessageStyle { UPPER_CENTER }
public static class ScreenMessages { public static int count; public static void PostScreenMessage(string m,float s,ScreenMessageStyle p) { count++; } }
public class PartResourceLibrary { public static PartResourceLibrary Instance=new PartResourceLibrary(); public object GetDefinition(string name)=>name==null?null:new object(); }
namespace UnityEngine { public static class Time { public static float realtimeSinceStartup=10; } }
namespace ChitakTech
{
    public class CTShared
    {
        public static CTShared Instance=new CTShared(); public List<ChitakTechMaterial> materialList=new List<ChitakTechMaterial>();
        public static bool IsFinite(double n)=>!double.IsNaN(n)&&!double.IsInfinity(n);
        public static void LogError(string s,string c) { throw new Exception(s); }
        public static void LogWarning(string s,string c) { }
        public enum ChargablePartState { Off, On }
    }
    public class ChitakTechMaterial { public string name="Wood"; }
    public class ChitakTechMaterialInstance
    {
        public int amount; public float volume=1; public Dictionary<string,string> parameters=new Dictionary<string,string>();
        public ChitakTechMaterialInstance(ChitakTechMaterialInstance m) { amount=m.amount; volume=m.volume; }
        public ChitakTechMaterialInstance(ChitakTechMaterial d,string shape,string size,float volume,Dictionary<string,string> p){this.volume=volume;}
        public ConfigNode ToConfigNode(string n){var c=new ConfigNode(n);c.AddValue("amount",amount);return c;}
        public static bool TryFromConfigNode(ConfigNode n,out ChitakTechMaterialInstance m){m=new ChitakTechMaterialInstance(null,"","",1,null){amount=int.Parse(n.GetValue("amount"))};return true;}
    }
    public class ChitakTechMaterialStorage
    {
        public List<ChitakTechMaterialInstance> contents=new List<ChitakTechMaterialInstance>(); public bool TransfersEnabled=true; public int capacity=100;
        public int TransferCapacity(ChitakTechMaterialInstance m,int n)=>Math.Max(0,Math.Min(n,capacity-contents.Sum(c=>c.amount)));
        public bool AddMaterial(ChitakTechMaterialInstance m){if(TransferCapacity(m,m.amount)<m.amount)return false; if(contents.Count>0)contents[0].amount+=m.amount;else contents.Add(m);return true;}
    }
    public class ChitakTechStorage
    {
        public string storageType="single",activeResource="Water"; public string[] SupportedResources={"Water"};
        public double amount,capacity=100,throughput;
        public double RequestStoredResource(string n,double quantity){double moved=Math.Min(-quantity,capacity-amount);amount+=moved;throughput+=moved;return -moved;}
        public void UndoTransfer(string n,double signed){amount+=signed;throughput+=signed;}
    }
    public static class ChitakTechResourceNetwork
    {
        public class Transfer { internal double amount; internal Action<double> undo; }
        public static void Rollback(List<Transfer> t){foreach(var entry in t.AsEnumerable().Reverse())entry.undo(entry.amount);t.Clear();}
        public static double Request(Part p,string n,double amount,ResourceFlowMode f,List<Transfer> t){double moved=Math.Min(-amount,p.stockCapacity-p.stock);p.stock+=moved;t.Add(new Transfer{amount=-moved,undo=s=>p.stock+=s});return -moved;}
    }
    public class ChitakTechISRURecipe
    {
        public enum PowerfailResult { Pause, Void, Explode, Maint }
        public struct ResourceOutput { public double amount; }
        public List<ResourceOutput> _outputs = new List<ResourceOutput>(); public double _biomeResourceScale = 1;
        public struct ResourceOutputMaterial { public string name,id,shape,size,outVolume; public double amount; public Dictionary<string,string> parameters; }
        public List<ResourceOutputMaterial> _outputMaterials=new List<ResourceOutputMaterial>(); public string _recipeTimeExpression="1";
    }
    public class ChitakTechISRUBiomeConfig { public double inputMultiplier=1,outputMultiplier=1; public List<ChitakTechISRURecipe.ResourceOutput> outputs = new List<ChitakTechISRURecipe.ResourceOutput>(); }
    public static class CTMathExpr { public static bool TryEvaluateNumericExpression(string s,out double v,out string e){e=null;return double.TryParse(s,out v);} }
    public partial class ChitakTechISRU
    {
        public Vessel vessel=new Vessel(); public Part part=new Part(); public bool isRunning=true,runOneCycle; public double batchProgress=1,resolvedRecipeTime=1;
        public string ConverterName="Test generator"; protected ChitakTechISRURecipe _activeRecipe=new ChitakTechISRURecipe();
        private struct PreparedResourceOutput { public string name; public double amount; }
        List<PreparedResourceOutput> PrepareResourceOutputs(ChitakTechISRUBiomeConfig b)=>new List<PreparedResourceOutput>();
        bool RefreshEnvironmentRecipe()=>environmentAvailable;
        bool TryResolveInputMaterialOutputs(List<ChitakTechISRURecipe.ResourceOutputMaterial> a,Dictionary<string,ChitakTechMaterialInstance>b,out List<ChitakTechISRURecipe.ResourceOutputMaterial> c,out Dictionary<string,ChitakTechISRURecipe.ResourceOutputMaterial>d,double e){c=null;d=null;return false;}
        bool TryResolveMaterialReferences(string a,Dictionary<string,ChitakTechMaterialInstance>b,Dictionary<string,ChitakTechISRURecipe.ResourceOutputMaterial>c,bool d,string e,out string f){f=a;return true;}
        bool TryCreateOutputMaterialReferenceValue(ChitakTechISRURecipe.ResourceOutputMaterial a,ChitakTechMaterial b,out ChitakTechISRURecipe.ResourceOutputMaterial c){c=a;return true;}
        void ClearPassiveConsumption(){} void ResetPassiveTimers(){}
        public ChitakTechISRU(){vessel.parts.Add(part);}
        public void Plan(double resource,int material)
        {
            generatorResources=new List<PreparedResourceOutput>{new PreparedResourceOutput{name="Water",amount=resource}};
            generatorMaterials=new List<ChitakTechMaterialInstance>(); if(material>0)generatorMaterials.Add(new ChitakTechMaterialInstance(null,"","",1,null){amount=material});
            generatorResourceMultiplier=generatorMaterialMultiplier=1;
        }
        public bool Complete()=>CompleteGeneratorBatch(new ChitakTechISRUBiomeConfig());
        public void Stop(){isRunning=false;ResetGeneratorBatch();}
        public ConfigNode Save(){var n=new ConfigNode();SaveGeneratorBatch(n);return n;}
        public void Load(ConfigNode n){LoadGeneratorBatch(n);RestoreGeneratorBatch();}
        public void LoadBeforeStart(ConfigNode n) => LoadGeneratorBatch(n);
    }
}
class Program
{
    static int checks;
    static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
    static void Main()
    {
        var g=new ChitakTechISRU();var r=new ChitakTechStorage();var m=new ChitakTechMaterialStorage();g.part.Modules.Add(r);g.part.Modules.Add(m);
        g.Plan(5,3);Check(g.Complete(),"successful batch completes");Check(r.amount==5&&g.part.stock==0&&m.contents.Sum(v=>v.amount)==3,"internal storage preferred and materials delivered");
        m.capacity=4;g.Plan(7,2);Check(g.Complete(),"overflow finishes interval instead of blocking retries");Check(r.amount==5&&r.throughput==5&&m.contents.Sum(v=>v.amount)==3,"overflow rolls back resources, throughput and material merges");
        Check(ScreenMessages.count==1,"overflow notifies player");
        r.capacity=5;g.Plan(1,0);g.Complete();Check(g.part.stock==0&&r.amount==5,"full compatible storage does not bypass to stock");
        g.part.Modules.Remove(r);g.Plan(4,0);g.Complete();Check(g.part.stock==4,"stock fallback when no compatible internal storage exists");
        g.Plan(6,2);g.Complete();Check(g.part.stock==4&&m.contents[0].amount==3,"material failure restores stock fallback output too");
        m.capacity=20;g.Plan(2,5);var saved=g.Save();var restored=new ChitakTechISRU();var target=new ChitakTechMaterialStorage();restored.part.Modules.Add(target);restored.Load(saved);restored.Complete();
        Check(restored.part.stock==2&&target.contents.Single().amount==5,"saved resolved batch is restored without regenerating output");
        var paused=new ChitakTechISRU { isRunning=false }; paused.Load(saved);
        Check(paused.Save().HasNode("GENERATOR_BATCH"),"maintenance PAUSE retains a saved batch");
        paused.isRunning=true;paused.part.Modules.Add(new ChitakTechMaterialStorage());paused.Complete();
        Check(paused.part.stock==2,"maintenance PAUSE resumes the saved output");
        var unloaded=new ChitakTechISRU();unloaded.LoadBeforeStart(saved);
        Check(unloaded.Save().GetNode("GENERATOR_BATCH").GetNode("RESOURCE").GetValue("amount")=="2","saving before OnStart retains pending batch");
        g.Plan(9,2);g.Stop();Check(g.batchProgress==0&&g.resolvedRecipeTime==0&&!g.Save().HasNode("GENERATOR_BATCH"),"stop discards progress and pending output");
        GeneratorOptions();
        Console.WriteLine(checks+" generator assertions passed.");
    }

    static void GeneratorOptions()
    {
        var g = new ChitakTechGenerator { isRunning = false };
        Check(!g.alwaysOn && !g.hideVoidMessage, "new options default off");
        g.OnStart(StartState.None); g.FixedUpdate();
        Check(!g.isRunning && g.Events["StartConverter"].active, "ordinary generator remains manually started");
        g.StartConverter();
        Check(g.isRunning && g.Events["StopConverter"].active, "ordinary generator keeps stop control");
        g.StopConverter();
        g.alwaysOn = true; g.FixedUpdate();
        Check(g.isRunning && g.state == CTShared.ChargablePartState.On, "always-on starts automatically");
        Check(new[] { "StartConverter", "StopConverter" }.All(n => !g.Events[n].active
            && !g.Events[n].guiActive && !g.Events[n].guiActiveUnfocused && !g.Events[n].guiActiveEditor)
            && !g.Actions["StopConverterAction"].active, "always-on hides all manual start and stop controls");
        g.batchProgress = 0.5; g.resolvedRecipeTime = 2; g.FixedUpdate();
        Check(g.batchProgress == 0.5 && g.resolvedRecipeTime == 2, "automatic retry leaves a running batch intact");
        g.stopDuringNextTick = true; g.FixedUpdate();
        Check(g.isRunning && g.batchProgress == 0.5, "automatically resumes a paused batch after shared simulation stops it");
        g.StopConverter(); g.needsMaintenance = true; g.FixedUpdate();
        Check(!g.isRunning, "required maintenance prevents automatic start");
        g.needsMaintenance = false; g.maintenanceBlocksRunning = true; g.FixedUpdate();
        Check(!g.isRunning, "preventRunning maintenance result still blocks automatic start");
        g.maintenanceBlocksRunning = false; g.atDeposit = false; g.FixedUpdate();
        Check(!g.isRunning, "missing required deposit blocks automatic start");
        g.atDeposit = true; g.environmentAvailable = false; g.FixedUpdate();
        Check(!g.isRunning, "unresolved environment blocks automatic start");
        g.environmentAvailable = true; g.FixedUpdate();
        Check(g.isRunning, "generator retries as soon as prerequisites recover");
        g.StopConverter(); HighLogic.LoadedSceneIsFlight = false; g.FixedUpdate();
        Check(!g.isRunning, "always-on does not run in the editor");
        HighLogic.LoadedSceneIsFlight = true; g._fatalConfigError = true; g.FixedUpdate();
        Check(!g.isRunning, "invalid configuration never autostarts");
        g._fatalConfigError = false; g.isEnabled = false; g.FixedUpdate();
        Check(!g.isRunning, "disabled module never autostarts");

        var quiet = new ChitakTechGenerator { hideVoidMessage = true };
        quiet.part.stockCapacity = 0; quiet.Plan(5, 0);
        int notices = ScreenMessages.count;
        Check(quiet.Complete() && quiet.part.stock == 0 && ScreenMessages.count == notices,
            "hidden void message still discards output and completes the interval");
        quiet.hideVoidMessage = false; quiet.Plan(5, 0); quiet.Complete();
        Check(ScreenMessages.count == notices + 1, "default notification still works after a silent discard");
    }
}
