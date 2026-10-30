using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace ChitakTech
{
    public partial class CTShared
    {
        public sealed class ExplosionVisual
        {
            public CelestialBody body;
            public double latitude, longitude, altitude, radius, expiresAt;
            internal GameObject local, scaled;
        }
        public readonly List<ExplosionVisual> explosionVisuals = new List<ExplosionVisual>();
        private Game explosionGame;
        private Material explosionMaterial;

        public void MassiveVesselExplosion(Vessel origin, double range, double lifetime)
        {
            if (origin == null || !IsFinite(range) || range <= 0 || !IsFinite(lifetime) || lifetime <= 0) return;
            Vector3d center = origin.GetWorldPos3D();
            CelestialBody body = origin.mainBody;
            // Snapshot BEFORE deleting anything; unloaded vessels are included too.
            var victims = FlightGlobals.Vessels.Where(v => v != null
                && (v == origin || Vector3d.Distance(v.GetWorldPos3D(), center) <= range)).ToList();
            if (explosionGame != HighLogic.CurrentGame) ClearExplosionVisuals();
            explosionGame = HighLogic.CurrentGame;
            explosionVisuals.Add(new ExplosionVisual { body = body,
                latitude = body.GetLatitude(center), longitude = body.GetLongitude(center),
                altitude = body.GetAltitude(center), radius = range,
                expiresAt = Planetarium.GetUniversalTime() + lifetime });
            // Delete the origin last so nearby vessels are processed before active-vessel teardown.
            foreach (Vessel victim in victims.OrderBy(v => v == origin ? 1 : 0))
            {
                try { victim.Die(); }
                catch (Exception ex) { LogError("Could not delete vessel: " + ex.Message, "CTShared/MassiveVesselExplosion"); }
            }
        }

        private GameObject CreateExplosionSphere(bool scaled)
        {
            if (explosionMaterial == null)
            {
                // Built-in lit shaders participate in Unity fog. Emission keeps the fireball bright.
                Shader shader = Shader.Find("Legacy Shaders/Self-Illumin/Diffuse") ?? Shader.Find("Standard");
                if (shader == null) return null;
                explosionMaterial = new Material(shader);
                explosionMaterial.color = new Color(1f, .25f, .025f, 1f);
                explosionMaterial.EnableKeyword("_EMISSION");
                explosionMaterial.SetColor("_EmissionColor", new Color(1f, .18f, .015f));
            }
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "ChitakTech Explosion";
            sphere.transform.SetParent(transform, false);
            sphere.layer = scaled ? 10 : 0;
            var collider = sphere.GetComponent<Collider>();
            collider.enabled = false;
            Destroy(collider);
            // Render inward faces as well: entering the sphere must not reveal a hollow shell.
            var mesh = Instantiate(sphere.GetComponent<MeshFilter>().sharedMesh);
            var outer = mesh.triangles;
            var both = new int[outer.Length * 2];
            Array.Copy(outer, both, outer.Length);
            for (int i = 0; i < outer.Length; i += 3)
            { both[outer.Length + i] = outer[i]; both[outer.Length + i + 1] = outer[i + 2]; both[outer.Length + i + 2] = outer[i + 1]; }
            mesh.triangles = both;
            sphere.GetComponent<MeshFilter>().sharedMesh = mesh;
            var renderer = sphere.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = explosionMaterial;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return sphere;
        }

        private void UpdateExplosionVisuals()
        {
            if (explosionGame != HighLogic.CurrentGame || HighLogic.LoadedScene == GameScenes.MAINMENU)
            { ClearExplosionVisuals(); return; }
            bool visible = HighLogic.LoadedSceneIsFlight || HighLogic.LoadedScene == GameScenes.SPACECENTER;
            double now = Planetarium.GetUniversalTime();
            foreach (var effect in explosionVisuals.ToArray())
            {
                if (effect.body == null || now >= effect.expiresAt)
                { DestroyExplosionSphere(effect.local); DestroyExplosionSphere(effect.scaled); explosionVisuals.Remove(effect); continue; }
                if (visible)
                {
                    if (effect.local == null) effect.local = CreateExplosionSphere(false);
                    if (effect.scaled == null) effect.scaled = CreateExplosionSphere(true);
                    Vector3d position = effect.body.GetWorldSurfacePosition(effect.latitude, effect.longitude, effect.altitude);
                    if (effect.local != null)
                    {
                        effect.local.transform.position = position;
                        effect.local.transform.localScale = Vector3.one * (float)(effect.radius * 2);
                    }
                    if (effect.scaled != null)
                    {
                        Vector3d scaled = ScaledSpace.LocalToScaledSpace(position);
                        effect.scaled.transform.position = scaled;
                        double radius = Vector3d.Distance(scaled, ScaledSpace.LocalToScaledSpace(position + Vector3d.right * effect.radius));
                        effect.scaled.transform.localScale = Vector3.one * (float)(radius * 2);
                    }
                }
                bool map = HighLogic.LoadedSceneIsFlight && MapView.MapIsEnabled;
                if (effect.local != null) effect.local.SetActive(visible && !map);
                if (effect.scaled != null) effect.scaled.SetActive(visible && map);
            }
        }

        private void DestroyExplosionSphere(GameObject sphere)
        {
            if (sphere == null) return;
            Destroy(sphere.GetComponent<MeshFilter>().sharedMesh);
            Destroy(sphere);
        }
        private void ClearExplosionVisuals()
        {
            foreach (var effect in explosionVisuals)
            { DestroyExplosionSphere(effect.local); DestroyExplosionSphere(effect.scaled); }
            explosionVisuals.Clear();
            if (explosionMaterial != null) Destroy(explosionMaterial);
            explosionMaterial = null;
        }
    }
}
