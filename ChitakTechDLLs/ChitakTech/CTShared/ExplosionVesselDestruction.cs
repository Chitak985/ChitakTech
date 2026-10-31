using System.Linq;

namespace ChitakTech
{
    internal static class ExplosionVesselDestruction
    {
        private static int PartDepth(Part part)
        {
            int depth = 0;
            while (part.parent != null) { depth++; part = part.parent; }
            return depth;
        }

        internal static void Destroy(Vessel victim)
        {
            // Vessel.Die alone deliberately leaves an active vessel's live parts intact.
            // Use stock part death (crew, docking, physics and camera callbacks included),
            // leaves first so no surviving children are decoupled into new vessels.
            var parts = victim.parts.Where(p => p != null).OrderByDescending(PartDepth).ToArray();
            foreach (Part doomed in parts)
            {
                if (doomed.State == PartStates.DEAD) continue;
                doomed.Die();
            }
            // Root Part.Die calls Vessel.Die. Unloaded vessels have no live parts.
            if (victim != null && victim.state != Vessel.State.DEAD) victim.Die();
            // Stock keeps the inactive root as the post-flight camera/result anchor
            // when this is the active vessel. Never destroy that anchor underneath KSP.
            // No renderer, collider, module or resource on it remains active.
            if (victim == FlightGlobals.ActiveVessel && FlightCamera.fetch != null)
                FlightCamera.ClearTarget();
        }

    }
}
