using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ChitakTech;

public class ConfigNode
{
    public string name; public Dictionary<string,string> values = new(); public List<ConfigNode> nodes = new();
    public ConfigNode(string name="") { this.name=name; }
    public string GetValue(string n)=>values.GetValueOrDefault(n);
    public void AddValue(string n,object v)=>values[n]=Convert.ToString(v,CultureInfo.InvariantCulture);
    public ConfigNode AddNode(string n){var c=new ConfigNode(n);nodes.Add(c);return c;}
    public ConfigNode[] GetNodes(string n)=>nodes.Where(c=>c.name==n).ToArray();
    public bool HasNode(string n)=>nodes.Any(c=>c.name==n);
    public void RemoveNode(string n)=>nodes.Remove(nodes.First(c=>c.name==n));
}
public class KSPScenario : Attribute { public KSPScenario(ScenarioCreationOptions o,params GameScenes[] s){} }
public enum ScenarioCreationOptions { AddToAllGames }
public enum GameScenes { FLIGHT,SPACECENTER,TRACKSTATION,EDITOR }
public class ScenarioModule { public virtual void OnLoad(ConfigNode n){} public virtual void OnSave(ConfigNode n){} }
public static class HighLogic { public static object CurrentGame=new(); }
namespace UnityEngine { public static class Time { public static float realtimeSinceStartup; } }
public static class Planetarium { public static double now; public static double GetUniversalTime()=>now; }
public struct Vector3d
{
    public double x,y,z; public Vector3d(double x,double y,double z){this.x=x;this.y=y;this.z=z;}
    public double magnitude=>Math.Sqrt(x*x+y*y+z*z);
    public static Vector3d operator +(Vector3d a,Vector3d b)=>new(a.x+b.x,a.y+b.y,a.z+b.z);
}
public class CelestialBody
{
    public string name="Kerbin"; public double Radius=1000,GeeASL=1,gravParameter=9806650,rotationPeriod=1000,atmosphereDepth=100;
    public bool rotates=true,atmosphere=true; public Vector3d position;
    public class Science { public double flyingAltitudeThreshold=50,spaceAltitudeThreshold=1000; }
    public Science scienceValues=new();
    public double GetLatitude(Vector3d p)=>Math.Asin(p.y/p.magnitude)*180/Math.PI;
    public double GetLongitude(Vector3d p)=>Math.Atan2(p.z,p.x)*180/Math.PI;
    public double GetPressure(double altitude)=>Math.Max(0,100-altitude);
}
public class Orbit
{
    public CelestialBody referenceBody; public double period=100,epoch=75,meanAnomalyAtEpoch,semiMajorAxis=1200,
        inclination,eccentricity=0.1,LAN,argumentOfPeriapsis,PeA=50;
    public Func<double,Vector3d> position=t=>new Vector3d(1150-100*Math.Cos(2*Math.PI*(t-75)/100),0,0);
    public Vector3d getRelativePositionAtUT(double t)=>position(t);
}
public class Vessel
{
    public enum State { ALIVE,DEAD } public enum Situations { PRELAUNCH,ORBITING }
    public Guid id=Guid.NewGuid(); public State state; public Situations situation=Situations.ORBITING;
    public bool loaded,Landed,Splashed; public double latitude,longitude,altitude=150;
    public string vesselName="Test craft"; public CelestialBody mainBody=new(); public Orbit orbit=new(); public ProtoVessel protoVessel=new();
    public Vessel(){orbit.referenceBody=mainBody;var p=new ProtoPartSnapshot();protoVessel.protoPartSnapshots.Add(p);
        var m=p.partInfo.partConfig.AddNode("MODULE");m.AddValue("name","ChitakTechStorage");p.modules.Add(new ProtoPartModuleSnapshot());}
}
public class ProtoVessel { public List<ProtoPartSnapshot> protoPartSnapshots=new(); }
public class ProtoPartSnapshot { public AvailablePart partInfo=new();public string partName="test";public List<ProtoPartModuleSnapshot> modules=new(); }
public class AvailablePart { public ConfigNode partConfig=new(); }
public class ProtoPartModuleSnapshot { public string moduleName="ChitakTechStorage";public ConfigNode moduleValues=new(); }
public static class PartLoader { public static AvailablePart getPartInfoByName(string n)=>null; }
public static class ScienceUtil { public static string GetExperimentBiome(CelestialBody b,double lat,double lon)=>lon>0?"East":"West"; }
public static class FlightGlobals { public static List<Vessel> Vessels=new();public static double getExternalTemperature(double a,CelestialBody b)=>250; }
public enum ScreenMessageStyle { UPPER_CENTER }
public static class ScreenMessages { public static int notices;public static void PostScreenMessage(string s,float t,ScreenMessageStyle style){notices++;} }
namespace ChitakTech
{
    public class CTShared
    {
        public static CTShared Instance=new();public List<object> batchRecipeList=new();
        public enum SituationCondition { Landed,Splashed,FlyingHigh,FlyingLow,SpaceHigh,SpaceLow,SubOrbital }
        public static void LogWarning(string s,string c){} public static void LogError(string s,string c)=>throw new Exception(s);
    }
    public class ChitakTechRuntimeData { public ChitakTechRuntimeData(object v){} public string planet,biome;public double alt,g,temperature,pressure;public CTShared.SituationCondition sitCon; }
    public class ChitakTechISRUBiomeConfig { public List<CTShared.SituationCondition> situationDestructive=new();public double minAltitude=100,maxAltitude=double.MaxValue,minG=0,maxG=double.MaxValue,minTemperature=0,maxTemperature=double.MaxValue; }
    internal class SurvivalProfile
    {
        internal static HashSet<string> ModuleNames=new(){"ChitakTechStorage","ChitakTechDestructivePart"};
        internal static ChitakTechISRUBiomeConfig limits=new(); internal static List<ChitakTechRuntimeData> samples=new();
        internal static SurvivalProfile Create(ConfigNode m,ConfigNode s,IEnumerable<object> g,string c)=>new();
        internal ChitakTechISRUBiomeConfig At(ChitakTechRuntimeData d){samples.Add(d);return limits;}
    }
    internal static class ChitakTechEnvironmentConditions
    {
        internal static bool Destructive(ChitakTechISRUBiomeConfig c,ChitakTechRuntimeData d)=>d.alt<c.minAltitude||d.g>c.maxG;
    }
    internal static class ExplosionVesselDestruction { internal static void Destroy(Vessel v){v.state=Vessel.State.DEAD;} }
}
static class Program
{
    static int count;
    static void Check(bool ok,string s){count++;if(!ok)throw new Exception(s);}
    static void Tick(ChitakTechBackgroundSurvivalScenario s,double ut){Planetarium.now=ut;UnityEngine.Time.realtimeSinceStartup+=1;s.Update();}
    static double SavedTime(ChitakTechBackgroundSurvivalScenario s){var n=new ConfigNode();s.OnSave(n);return double.Parse(n.GetNodes("CHECKED_VESSEL").Single().GetValue("time"),CultureInfo.InvariantCulture);}
    static int Main()
    {
        try
        {
            Check(SurvivalTimeline.Next(70,1000,100,1000,75,true)==75 || SurvivalTimeline.Next(74.99,1000,100,1000,75,true)==75,"periapsis is included even between regular steps");
            Check(SurvivalTimeline.Next(124.99,1000,100,1000,75,true)==125,"apoapsis included");
            Check(SurvivalTimeline.Next(74.99,1000,double.NaN,1000,75,false)==75,"hyperbolic periapsis included");
            Check(SurvivalTimeline.LatestApsis(10000,75,100)==9975,"latest apsis under extreme warp");
            var v=new Vessel();FlightGlobals.Vessels=new(){v};var s=new ChitakTechBackgroundSurvivalScenario();Tick(s,0);Tick(s,100);
            Check(v.state==Vessel.State.DEAD&&ScreenMessages.notices==1,"safe endpoints do not hide a destructive periapsis; notify once");
            v=new Vessel{loaded=true};FlightGlobals.Vessels=new(){v};s=new();Tick(s,75);Check(v.state!=Vessel.State.DEAD,"loaded vessels excluded");
            v.loaded=false;Tick(s,75);Check(v.state==Vessel.State.DEAD,"unload checks current danger immediately");
            v=new Vessel();v.orbit.position=t=>new Vector3d(1300,0,0);FlightGlobals.Vessels=new(){v};s=new();Tick(s,0);Tick(s,10000);
            double cursor=SavedTime(s);Check(cursor>0&&cursor<10000,"budget leaves catch-up cursor without dropping interval");
            var save=new ConfigNode();s.OnSave(save);var restored=new ChitakTechBackgroundSurvivalScenario();restored.OnLoad(save);Tick(restored,10000);
            Check(SavedTime(restored)>cursor&&SavedTime(restored)<10000,"save/load resumes bounded catch-up");
            double beforeGap=SavedTime(restored);FlightGlobals.Vessels.Clear();Tick(restored,10000);
            Check(SavedTime(restored)==beforeGap,"temporary empty vessel list during scene loading preserves saved progress");
            FlightGlobals.Vessels.Add(v);
            v.orbit.semiMajorAxis+=10;Tick(restored,10001);Check(SavedTime(restored)==10001,"changed trajectory does not replay invented history");
            Tick(restored,5);Check(SavedTime(restored)==5,"time reversal resets cursor");
            SurvivalProfile.limits.minAltitude=0;SurvivalProfile.limits.maxG=0.5;
            v=new Vessel{Landed=true,altitude=0};FlightGlobals.Vessels=new(){v};s=new();Tick(s,0);
            Check(v.state==Vessel.State.DEAD,"landed craft experience surface gravity");
            Check(SurvivalProfile.samples.Last().g==1,"sea-level G uses KSP's own reference instead of rounding across a survival limit");
            v=new Vessel();FlightGlobals.Vessels=new(){v};s=new();Tick(s,0);
            Check(v.state!=Vessel.State.DEAD&&SurvivalProfile.samples.Last().g==0,"orbital gravity is not felt acceleration");
            SurvivalProfile.limits.maxG=100;
            v.orbit.position=t=>new Vector3d(0,0,1300);v.orbit.semiMajorAxis+=1;Tick(s,1);
            var point=SurvivalProfile.samples.Last();Check(Math.Abs(point.alt-300)<1e-8&&point.temperature==250&&point.pressure==0,"environment derived from orbit and body, not stale vessel fields");
            var method=typeof(ChitakTechBackgroundSurvivalScenario).GetMethod("Sample",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Static);
            v.orbit.position=t=>new Vector3d(1300,0,0);
            var old=(ChitakTechRuntimeData)method.Invoke(null,new object[]{v,0d,100d});
            Check(old.biome=="East","historical biome accounts for body rotation");
            Console.WriteLine(count+" background survival assertions passed.");return 0;
        }
        catch(Exception ex){Console.Error.WriteLine(ex);return 1;}
    }
}
