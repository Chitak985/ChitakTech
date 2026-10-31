using System;
using System.Collections.Generic;
using System.Linq;
using ChitakTech;

// Model the relevant stock contract: Vessel.Die does NOT deactivate active parts;
// Part.Die deactivates itself, and the root calls Vessel.Die.
public enum PartStates { ALIVE, DEAD }
public class Part
{
    public Part parent;
    public Vessel vessel;
    public PartStates State;
    public bool visible = true;
    public void Die()
    {
        if (vessel.parts.Any(p => p.parent == this && p.State != PartStates.DEAD))
            throw new Exception("Children would escape as newly decoupled vessels.");
        State = PartStates.DEAD; visible = false;
        if (parent == null) vessel.Die();
    }
}
public class Vessel
{
    public enum State { ALIVE, DEAD }
    public State state;
    public int deaths;
    public List<Part> parts = new List<Part>();
    public void Die() { state = State.DEAD; deaths++; }
}
public static class FlightGlobals { public static Vessel ActiveVessel; }
public class FlightCamera
{
    public static FlightCamera fetch = new FlightCamera();
    public static int clears;
    public static void ClearTarget() { clears++; }
}
class Program
{
    static int checks;
    static void Check(bool value, string reason) { checks++; if (!value) throw new Exception(reason); }
    static void Main()
    {
        foreach (bool active in new[] { false, true })
        {
            var vessel = new Vessel();
            var root = new Part { vessel = vessel };
            var child = new Part { parent = root, vessel = vessel };
            var grandchild = new Part { parent = child, vessel = vessel };
            vessel.parts.AddRange(new[] { child, root, grandchild }); // deliberately unordered
            FlightGlobals.ActiveVessel = active ? vessel : null;
            int before = FlightCamera.clears;
            ExplosionVesselDestruction.Destroy(vessel);
            Check(vessel.parts.All(p => !p.visible && p.State == PartStates.DEAD), "All live parts must disappear");
            Check(vessel.state == Vessel.State.DEAD && vessel.deaths == 1, "Root handles vessel death exactly once");
            Check(FlightCamera.clears - before == (active ? 1 : 0), "Clear only the destroyed active vessel's camera target");
        }
        var unloaded = new Vessel();
        ExplosionVesselDestruction.Destroy(unloaded);
        Check(unloaded.state == Vessel.State.DEAD && unloaded.deaths == 1, "Unloaded vessels use stock vessel deletion");
        ExplosionVesselDestruction.Destroy(unloaded);
        Check(unloaded.deaths == 1, "Dead vessels must not receive duplicate death events");
        Console.WriteLine(checks + " explosion destruction assertions passed.");
    }
}
