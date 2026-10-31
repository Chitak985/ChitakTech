using System;
using System.Collections.Generic;
using System.IO;
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
        }
        public readonly List<ExplosionVisual> explosionVisuals = new List<ExplosionVisual>();
        private Game explosionGame;
        private Material explosionMaterial;
        private Mesh explosionQuad;
        private bool explosionRendererRegistered, explosionShaderFailed;

        public void MassiveVesselExplosion(Vessel origin, double range, double lifetime)
        {
            if (origin == null || !IsFinite(range) || range <= 0 || !IsFinite(lifetime) || lifetime <= 0) return;
            Vector3d center = origin.GetWorldPos3D();
            CelestialBody body = origin.mainBody;
            var victims = FlightGlobals.Vessels.Where(v => v != null && v.state != Vessel.State.DEAD
                && (v == origin || Vector3d.Distance(v.GetWorldPos3D(), center) <= range)).ToList();
            // At high warp a one-hour visual could otherwise expire between frames.
            // Stop warp for the player hit by the blast, as part of handling their loss.
            if (HighLogic.LoadedSceneIsFlight && victims.Contains(FlightGlobals.ActiveVessel))
                TimeWarp.SetRate(0, true);
            if (explosionGame != HighLogic.CurrentGame) ClearExplosionVisuals();
            explosionGame = HighLogic.CurrentGame;
            explosionVisuals.Add(new ExplosionVisual { body = body,
                latitude = body.GetLatitude(center), longitude = body.GetLongitude(center),
                altitude = body.GetAltitude(center), radius = range,
                expiresAt = Planetarium.GetUniversalTime() + lifetime });
            EnsureExplosionRenderer();
            Log("Massive explosion at " + body.bodyName + ": radius " + range
                + " m, lifetime " + lifetime + " s, affected vessels " + victims.Count + ".",
                "CTShared/MassiveVesselExplosion");
            // Always process the ACTIVE vessel last, even if another vessel is the source.
            foreach (Vessel victim in victims.OrderBy(v => v == FlightGlobals.ActiveVessel ? 1 : 0))
            {
                try { ExplosionVesselDestruction.Destroy(victim); }
                catch (Exception ex) { LogError("Could not delete vessel " + victim.vesselName + ": " + ex,
                    "CTShared/MassiveVesselExplosion"); }
            }
        }

        private void EnsureExplosionRenderer()
        {
            if (explosionMaterial == null && !explosionShaderFailed)
            {
                string path = Path.Combine(KSPUtil.ApplicationRootPath, "GameData/ChitakTech/Shaders/explosion-shaders");
                AssetBundle bundle = null;
                try
                {
                    bundle = AssetBundle.LoadFromFile(path);
                    Shader shader = bundle == null ? null : bundle.LoadAsset<Shader>("Assets/ExplosionVolume.shader");
                    if (shader == null || !shader.isSupported) throw new InvalidOperationException("Explosion volume shader is missing or unsupported: " + path);
                    explosionMaterial = new Material(shader);
                    explosionQuad = new Mesh { name = "ChitakTech Explosion Screen Quad" };
                    explosionQuad.vertices = new[] { new Vector3(-1,-1,0), new Vector3(1,-1,0), new Vector3(1,1,0), new Vector3(-1,1,0) };
                    explosionQuad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
                }
                catch (Exception ex)
                {
                    explosionShaderFailed = true;
                    LogError("Cannot render explosion: " + ex, "CTShared/ExplosionRendering");
                }
                finally { if (bundle != null) bundle.Unload(false); }
            }
            if (!explosionRendererRegistered && explosionMaterial != null)
            {
                Camera.onPostRender += RenderExplosionVolumes;
                explosionRendererRegistered = true;
            }
        }

        private void UpdateExplosionVisuals()
        {
            if (explosionGame != HighLogic.CurrentGame || HighLogic.LoadedScene == GameScenes.MAINMENU)
            { ClearExplosionVisuals(); return; }
            double now = Planetarium.GetUniversalTime();
            explosionVisuals.RemoveAll(effect => effect.body == null || now >= effect.expiresAt);
            if (explosionVisuals.Count == 0) { ClearExplosionVisuals(); return; }
            EnsureExplosionRenderer();
        }

        // Draw analytically in each KSP world camera, using that camera's depth buffer.
        // Unlike a gigantic mesh, the screen quad cannot be culled when the viewer is
        // inside the fireball or when the sphere's back surface is beyond the far plane.
        private void RenderExplosionVolumes(Camera camera)
        {
            if (explosionMaterial == null || explosionQuad == null || camera == null
                || camera.cameraType != CameraType.Game
                || !(HighLogic.LoadedSceneIsFlight || HighLogic.LoadedScene == GameScenes.SPACECENTER)) return;
            bool scaled = ScaledCamera.Instance != null && camera == ScaledCamera.Instance.cam
                || PlanetariumCamera.fetch != null && camera == PlanetariumCamera.Camera;
            bool local = FlightCamera.fetch != null && FlightCamera.fetch.cameras != null && FlightCamera.fetch.cameras.Contains(camera)
                || HighLogic.LoadedScene == GameScenes.SPACECENTER && camera == Camera.main;
            if (!scaled && !local) return;
            // Map has no local scenery. In flight BOTH scaled sky and local cameras
            // need the effect, otherwise distant or very large blasts disappear.
            if (!scaled && HighLogic.LoadedSceneIsFlight && MapView.MapIsEnabled) return;
            explosionMaterial.SetMatrix("_Projection", GL.GetGPUProjectionMatrix(camera.projectionMatrix, camera.targetTexture != null));
            explosionMaterial.SetVector("_Forward", camera.transform.forward);
            explosionMaterial.SetVector("_Clip", new Vector4(camera.nearClipPlane, camera.farClipPlane, 0, 0));
            explosionMaterial.SetVector("_Fog", new Vector4(!scaled && RenderSettings.fog ? (float)RenderSettings.fogMode : 0,
                RenderSettings.fogStartDistance, RenderSettings.fogEndDistance, RenderSettings.fogDensity));
            explosionMaterial.SetColor("_FogColor", RenderSettings.fogColor);
            SetExplosionRay(camera, "00", 0, 0);
            SetExplosionRay(camera, "10", 1, 0);
            SetExplosionRay(camera, "01", 0, 1);
            SetExplosionRay(camera, "11", 1, 1);
            foreach (var effect in explosionVisuals)
            {
                if (effect.body == null || Planetarium.GetUniversalTime() >= effect.expiresAt) continue;
                Vector3d position = effect.body.GetWorldSurfacePosition(effect.latitude, effect.longitude, effect.altitude);
                double radius = effect.radius;
                if (scaled)
                {
                    Vector3d scaledPosition = ScaledSpace.LocalToScaledSpace(position);
                    radius = Vector3d.Distance(scaledPosition, ScaledSpace.LocalToScaledSpace(position + Vector3d.right * radius));
                    position = scaledPosition;
                }
                Vector3d relative = (position - (Vector3d)camera.transform.position) / radius;
                explosionMaterial.SetVector("_CenterRadius", new Vector4((float)relative.x, (float)relative.y, (float)relative.z, (float)radius));
                if (explosionMaterial.SetPass(0)) Graphics.DrawMeshNow(explosionQuad, Matrix4x4.identity);
            }
        }

        private void SetExplosionRay(Camera camera, string corner, float x, float y)
        {
            Ray ray = camera.ViewportPointToRay(new Vector3(x, y, 0));
            Vector3 direction = ray.direction / Vector3.Dot(ray.direction, camera.transform.forward);
            explosionMaterial.SetVector("_Ray" + corner, direction);
            explosionMaterial.SetVector("_Origin" + corner, camera.orthographic
                ? ray.origin - camera.transform.position : Vector3.zero);
        }

        private void ClearExplosionVisuals()
        {
            if (explosionRendererRegistered) Camera.onPostRender -= RenderExplosionVolumes;
            explosionRendererRegistered = false;
            explosionVisuals.Clear();
            if (explosionMaterial != null)
            {
                Destroy(explosionMaterial.shader);
                Destroy(explosionMaterial);
            }
            if (explosionQuad != null) Destroy(explosionQuad);
            explosionMaterial = null;
            explosionQuad = null;
            explosionShaderFailed = false;
        }
    }
}
