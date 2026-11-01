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
        Check(ExplosionAnimation.MainScale(0, 30) == 0, "fireball starts at zero size");
        Check(ExplosionAnimation.MainScale(1, 30) == .5, "half grown after one second");
        Check(ExplosionAnimation.MainScale(2, 30) == 1, "fully grown after two seconds");
        Check(ExplosionAnimation.MainScale(32, 30) == 1, "full configured lifetime follows growth");
        Check(ExplosionAnimation.MainScale(34.5, 30) == .5, "halfway through five-second shrink");
        Check(ExplosionAnimation.MainScale(37, 30) == 0, "fireball shrinks completely");
        Check(ExplosionAnimation.ShockScale(2) == 1 && ExplosionAnimation.ShockScale(20) == 10,
            "shockwave uses the same radial speed and reaches ten times the radius");
        Check(ExplosionAnimation.ShockOpacity(20) == 0 && ExplosionAnimation.ShockOpacity(10) < ExplosionAnimation.ShockOpacity(2),
            "shockwave fades completely at its target radius");
        Check(ExplosionAnimation.Duration(1) == 20, "short fireball does not cut off its shockwave");
        Check(ExplosionAnimation.Duration(30) == 37, "normal fireball retains its hold plus animations");
        Check(ExplosionAnimation.Crossed(.9, 1.1, .5, 1), "main front arrival detected between frames");
        Check(ExplosionAnimation.Crossed(1, 19, 7, 10), "shockwave arrival detected after large time step");
        Check(!ExplosionAnimation.Crossed(21, 22, 3, 10), "returning to flight does not replay an expired shockwave");
        Check(!ExplosionAnimation.Crossed(1, 2, 2, 1), "main fireball does not shake viewers outside it");
        Check(ExplosionAnimation.MainScale(-1, 30) == 0, "transient loading-time clock rollback stays safe");
        Console.WriteLine(checks + " explosion destruction assertions passed.");
    }
}
