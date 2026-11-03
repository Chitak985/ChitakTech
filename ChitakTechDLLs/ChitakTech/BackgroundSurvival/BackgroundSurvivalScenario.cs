using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using UnityEngine;

namespace ChitakTech
{
    [KSPScenario(ScenarioCreationOptions.AddToAllGames, GameScenes.FLIGHT,
        GameScenes.SPACECENTER, GameScenes.TRACKSTATION, GameScenes.EDITOR)]
    public sealed class ChitakTechBackgroundSurvivalScenario : ScenarioModule
    {
        private sealed class Record
        {
            internal double time;
            internal string trajectory;
            internal ProtoVessel snapshot;
            internal List<SurvivalProfile> profiles;
        }
        private readonly Dictionary<Guid, Record> records = new Dictionary<Guid, Record>();
        private readonly HashSet<Guid> errors = new HashSet<Guid>();
        private float nextUpdate;
        private int nextVessel;

        public override void OnLoad(ConfigNode node)
        {
            base.OnLoad(node); records.Clear(); errors.Clear();
            foreach (ConfigNode entry in node.GetNodes("CHECKED_VESSEL"))
                if (Guid.TryParse(entry.GetValue("id"), out Guid id)
                    && double.TryParse(entry.GetValue("time"), NumberStyles.Float, CultureInfo.InvariantCulture, out double time)
                    && SurvivalTimeline.Finite(time))
                    records[id] = new Record { time = time, trajectory = entry.GetValue("trajectory") };
        }
        public override void OnSave(ConfigNode node)
        {
            base.OnSave(node);
            while (node.HasNode("CHECKED_VESSEL")) node.RemoveNode("CHECKED_VESSEL");
            foreach (var pair in records)
            {
                var entry = node.AddNode("CHECKED_VESSEL"); entry.AddValue("id", pair.Key);
                entry.AddValue("time", pair.Value.time.ToString("R", CultureInfo.InvariantCulture));
                entry.AddValue("trajectory", pair.Value.trajectory ?? "");
            }
        }

        public void Update()
        {
            if (HighLogic.CurrentGame == null || CTShared.Instance == null || FlightGlobals.Vessels == null
                || Time.realtimeSinceStartup < nextUpdate) return;
            nextUpdate = Time.realtimeSinceStartup + 0.25f;
            double now = Planetarium.GetUniversalTime();
            if (!SurvivalTimeline.Finite(now)) return;
            var vessels = FlightGlobals.Vessels.Where(v => v != null && v.state != Vessel.State.DEAD).ToArray();
            // Scene loading can briefly expose an empty vessel list. Keep saved cursors.
            if (vessels.Length == 0) return;
            var ids = new HashSet<Guid>(vessels.Select(v => v.id));
            foreach (Guid stale in records.Keys.Where(id => !ids.Contains(id)).ToArray())
            { records.Remove(stale); errors.Remove(stale); }
            int budget = 512;
            for (int visited = 0; visited < vessels.Length && budget > 0; visited++)
            {
                if (nextVessel >= vessels.Length) nextVessel = 0;
                Vessel vessel = vessels[nextVessel++];
                if (vessel.loaded)
                {
                    // A subsequent unload begins a new trajectory/configuration interval.
                    records.Remove(vessel.id); errors.Remove(vessel.id); continue;
                }
                if (vessel.protoVessel == null || vessel.mainBody == null) continue;
                try { Check(vessel, now, ref budget); }
                catch (Exception ex)
                {
                    if (errors.Add(vessel.id)) CTShared.LogError("Could not check " + vessel.vesselName + ": " + ex,
                        "BackgroundSurvival");
                }
            }
        }

        private void Check(Vessel vessel, double now, ref int budget)
        {
            string trajectory = Trajectory(vessel);
            if (!records.TryGetValue(vessel.id, out var record))
                records[vessel.id] = record = new Record { time = now, trajectory = trajectory };
            // Never extrapolate today's orbit backwards across an SOI change, teleport,
            // maneuver, or time reversal. KSP does not retain the previous trajectory here.
            if (record.time > now || record.trajectory != trajectory)
            { record.time = now; record.trajectory = trajectory; record.profiles = null; }
            if (record.profiles == null || !ReferenceEquals(record.snapshot, vessel.protoVessel))
            {
                if (record.snapshot != null && !ReferenceEquals(record.snapshot, vessel.protoVessel)) record.time = now;
                record.snapshot = vessel.protoVessel;
                record.profiles = ReadProfiles(vessel);
            }
            if (record.profiles.Count == 0) { record.time = now; return; }
            budget--;
            if (CheckAt(vessel, now, now, record.profiles)) { records.Remove(vessel.id); return; }
            if (OnSurface(vessel)) { record.time = now; return; }
            Orbit orbit = vessel.orbit;
            if (orbit == null || orbit.referenceBody != vessel.mainBody) return;
            double meanMotion = Math.Sqrt(vessel.mainBody.gravParameter / Math.Pow(Math.Abs(orbit.semiMajorAxis), 3));
            double periapsis = orbit.epoch - orbit.meanAnomalyAtEpoch / meanMotion;
            // Check the most recent extrema immediately, even when dense catch-up is
            // backlogged. Narrow radial/pressure violations must not wait behind months
            // of biome samples after extreme warp.
            foreach (double epoch in orbit.eccentricity < 1 ? new[] { periapsis, periapsis + orbit.period / 2 } : new[] { periapsis })
            {
                double critical = SurvivalTimeline.LatestApsis(now, epoch, orbit.eccentricity < 1 ? orbit.period : 0);
                if (!SurvivalTimeline.Finite(critical) || critical <= record.time || critical > now) continue;
                budget--;
                if (CheckAt(vessel, critical, now, record.profiles)) { records.Remove(vessel.id); return; }
            }
            for (int count = 0; count < 64 && budget > 0 && record.time < now; count++, budget--)
            {
                double sample = SurvivalTimeline.Next(record.time, now, orbit.period, vessel.mainBody.rotationPeriod,
                    periapsis, orbit.eccentricity < 1);
                if (sample <= record.time) break;
                if (CheckAt(vessel, sample, now, record.profiles)) { records.Remove(vessel.id); return; }
                record.time = sample;
            }
        }

        private static List<SurvivalProfile> ReadProfiles(Vessel vessel)
        {
            var profiles = new List<SurvivalProfile>();
            foreach (ProtoPartSnapshot part in vessel.protoVessel.protoPartSnapshots)
            {
                ConfigNode definition = (part.partInfo ?? PartLoader.getPartInfoByName(part.partName))?.partConfig;
                if (definition == null) continue;
                var modules = definition.GetNodes("MODULE");
                var indices = new Dictionary<string, int>();
                foreach (ProtoPartModuleSnapshot saved in part.modules)
                {
                    indices.TryGetValue(saved.moduleName, out int index); indices[saved.moduleName] = index + 1;
                    if (!SurvivalProfile.ModuleNames.Contains(saved.moduleName)) continue;
                    if (bool.TryParse(saved.moduleValues.GetValue("isEnabled"), out bool enabled) && !enabled) continue;
                    var module = modules.Where(n => n.GetValue("name") == saved.moduleName).ElementAtOrDefault(index);
                    if (module == null) continue;
                    if (saved.moduleName == "ChitakTechDestructivePart" && modules.Count(n =>
                        SurvivalProfile.ModuleNames.Contains(n.GetValue("name") ?? "")) > 1) continue;
                    var profile = SurvivalProfile.Create(module, saved.moduleValues, CTShared.Instance.batchRecipeList,
                        part.partName + "/" + saved.moduleName);
                    if (profile != null) profiles.Add(profile);
                }
            }
            return profiles;
        }

        private static bool CheckAt(Vessel vessel, double time, double now, List<SurvivalProfile> profiles)
        {
            var data = Sample(vessel, time, now);
            if (data == null) return false;
            foreach (var profile in profiles)
            {
                var config = profile.At(data);
                if (config == null || !ChitakTechEnvironmentConditions.Destructive(config, data)) continue;
                string reason = Failure(config, data);
                string message = vessel.vesselName + " was destroyed: " + reason + " (" + data.planet + "/" + data.biome + ").";
                CTShared.LogWarning(message + " Checked UT=" + time.ToString("R", CultureInfo.InvariantCulture), "BackgroundSurvival");
                ScreenMessages.PostScreenMessage(message, 8f, ScreenMessageStyle.UPPER_CENTER);
                // No Part.explode on unloaded snapshots. This uses stock vessel death,
                // including crew/save bookkeeping, and removes the entire craft.
                ExplosionVesselDestruction.Destroy(vessel);
                return true;
            }
            return false;
        }

        private static string Failure(ChitakTechISRUBiomeConfig c, ChitakTechRuntimeData d)
        {
            if (c.situationDestructive.Contains(d.sitCon)) return "destructive situation " + d.sitCon;
            if (d.alt < c.minAltitude || d.alt > c.maxAltitude) return "altitude outside survival limits";
            if (d.g < c.minG || d.g > c.maxG) return "G-force outside survival limits";
            if (d.temperature < c.minTemperature || d.temperature > c.maxTemperature) return "ambient temperature outside survival limits";
            return "pressure outside survival limits";
        }

        private static bool OnSurface(Vessel v) => v.Landed || v.Splashed || v.situation == Vessel.Situations.PRELAUNCH;

        private static ChitakTechRuntimeData Sample(Vessel v, double time, double now)
        {
            CelestialBody body = v.mainBody;
            double latitude = v.latitude, longitude = v.longitude, altitude = v.altitude;
            bool surface = OnSurface(v);
            if (!surface)
            {
                if (v.orbit == null || v.orbit.referenceBody != body) return null;
                Vector3d orbital = v.orbit.getRelativePositionAtUT(time);
                // KSP orbital coordinates use Z as north; Unity world coordinates use Y.
                Vector3d relative = new Vector3d(orbital.x, orbital.z, orbital.y);
                altitude = relative.magnitude - body.Radius;
                latitude = body.GetLatitude(body.position + relative);
                longitude = body.GetLongitude(body.position + relative);
                if (body.rotates && body.rotationPeriod != 0)
                    longitude += 360 * (now - time) / body.rotationPeriod;
                longitude = ((longitude + 180) % 360 + 360) % 360 - 180;
            }
            if (!SurvivalTimeline.Finite(altitude) || !SurvivalTimeline.Finite(latitude) || !SurvivalTimeline.Finite(longitude)) return null;
            var d = new ChitakTechRuntimeData(null) { planet = body.name, alt = altitude,
                biome = ScienceUtil.GetExperimentBiome(body, latitude, longitude) ?? "",
                pressure = body.GetPressure(Math.Max(0, altitude)),
                temperature = FlightGlobals.getExternalTemperature(Math.Max(0, altitude), body),
                g = surface ? body.GeeASL * Math.Pow(body.Radius / (body.Radius + altitude), 2) : 0 };
            if (surface) d.sitCon = v.Splashed ? CTShared.SituationCondition.Splashed : CTShared.SituationCondition.Landed;
            else if (body.atmosphere && altitude < body.atmosphereDepth)
                d.sitCon = altitude >= body.scienceValues.flyingAltitudeThreshold ? CTShared.SituationCondition.FlyingHigh : CTShared.SituationCondition.FlyingLow;
            else if (v.orbit.PeA < 0) d.sitCon = CTShared.SituationCondition.SubOrbital;
            else d.sitCon = altitude >= body.scienceValues.spaceAltitudeThreshold ? CTShared.SituationCondition.SpaceHigh : CTShared.SituationCondition.SpaceLow;
            return SurvivalTimeline.Finite(d.pressure) && SurvivalTimeline.Finite(d.temperature) && SurvivalTimeline.Finite(d.g) ? d : null;
        }

        private static string Trajectory(Vessel v)
        {
            if (OnSurface(v)) return v.mainBody.name + "/surface/" + v.latitude.ToString("R", CultureInfo.InvariantCulture)
                + "/" + v.longitude.ToString("R", CultureInfo.InvariantCulture) + "/" + v.altitude.ToString("R", CultureInfo.InvariantCulture);
            Orbit o = v.orbit;
            if (o == null) return v.mainBody.name + "/unknown";
            return v.mainBody.name + "/" + string.Join("/", new[] { o.inclination, o.eccentricity, o.semiMajorAxis,
                o.LAN, o.argumentOfPeriapsis, o.meanAnomalyAtEpoch, o.epoch }.Select(n => n.ToString("R", CultureInfo.InvariantCulture)));
        }
    }
}
