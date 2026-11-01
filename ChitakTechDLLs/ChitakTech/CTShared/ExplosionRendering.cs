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
            public string bodyName;
            public Vector3d scaledBodyOffset;
            public double radius, startedAt, lifetime;
            internal double previousAge = -1;
            internal bool mainArrived, shockArrived;
            internal float seed;
            internal Light mainLight, shockLight;
            internal CelestialBody Body => FlightGlobals.Bodies?.FirstOrDefault(b => b.bodyName == bodyName);
            internal Vector3d ScaledPosition(CelestialBody body)
                => (Vector3d)body.scaledBody.transform.position + scaledBodyOffset;
        }
        public readonly List<ExplosionVisual> explosionVisuals = new List<ExplosionVisual>();
        private string explosionSaveFolder;
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
            if (explosionSaveFolder != HighLogic.SaveFolder) ClearExplosionVisuals();
            explosionSaveFolder = HighLogic.SaveFolder;
            explosionVisuals.Add(new ExplosionVisual { bodyName = body.bodyName,
                scaledBodyOffset = ScaledSpace.LocalToScaledSpace(center) - (Vector3d)body.scaledBody.transform.position,
                radius = range, startedAt = Planetarium.GetUniversalTime(), lifetime = lifetime,
                seed = (Guid.NewGuid().GetHashCode() & 0xffff) / 100f });
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
                Camera.onPreCull += ShakeExplosionCamera;
                Camera.onPostRender += RenderExplosionVolumes;
                explosionRendererRegistered = true;
            }
        }

        private void UpdateExplosionVisuals()
        {
            // KSP replaces Game instances when moving between flight and KSC. A
            // stable save identity survives those changes and transient loading gaps.
            if (HighLogic.LoadedScene == GameScenes.MAINMENU
                || !string.IsNullOrEmpty(HighLogic.SaveFolder) && explosionSaveFolder != HighLogic.SaveFolder)
            { ClearExplosionVisuals(); return; }
            if (HighLogic.CurrentGame == null) return;
            double now = Planetarium.GetUniversalTime();
            for (int i = explosionVisuals.Count - 1; i >= 0; i--)
            {
                var effect = explosionVisuals[i];
                double age = now - effect.startedAt;
                if (age >= ExplosionAnimation.Duration(effect.lifetime))
                { DestroyExplosionLights(effect); explosionVisuals.RemoveAt(i); }
            }
            if (explosionVisuals.Count == 0) { ClearExplosionVisuals(); return; }
            EnsureExplosionRenderer();
            UpdateExplosionLighting(now);
        }

        // Draw analytically in each KSP world camera, using that camera's depth buffer.
        // Unlike a gigantic mesh, the screen quad cannot be culled when the viewer is
        // inside the fireball or when the sphere's back surface is beyond the far plane.
        private void RenderExplosionVolumes(Camera camera)
        {
            try { DrawExplosionVolumes(camera); }
            finally { RestoreExplosionCamera(camera); }
        }

        private bool ExplosionCamera(Camera camera, out bool scaled)
        {
            scaled = camera != null && (ScaledCamera.Instance != null && camera == ScaledCamera.Instance.cam
                || PlanetariumCamera.fetch != null && camera == PlanetariumCamera.Camera);
            bool local = camera != null && (FlightCamera.fetch != null && FlightCamera.fetch.cameras != null && FlightCamera.fetch.cameras.Contains(camera)
                || HighLogic.LoadedScene == GameScenes.SPACECENTER && camera == Camera.main);
            return camera != null && camera.cameraType == CameraType.Game && (scaled || local)
                && (HighLogic.LoadedSceneIsFlight || HighLogic.LoadedScene == GameScenes.SPACECENTER)
                && (scaled || !HighLogic.LoadedSceneIsFlight || !MapView.MapIsEnabled);
        }

        private void DrawExplosionVolumes(Camera camera)
        {
            if (explosionMaterial == null || explosionQuad == null || !ExplosionCamera(camera, out bool scaled)) return;
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
            double now = Planetarium.GetUniversalTime();
            // Opaque fireballs first; transparent shockwaves back-to-front afterwards.
            foreach (var effect in explosionVisuals) DrawExplosion(effect, camera, scaled, now, false);
            foreach (var effect in explosionVisuals.OrderByDescending(e => ExplosionDistance(e, camera, scaled)))
                DrawExplosion(effect, camera, scaled, now, true);
        }

        private double ExplosionDistance(ExplosionVisual effect, Camera camera, bool scaled)
        {
            var body = effect.Body;
            if (body == null || body.scaledBody == null) return 0;
            Vector3d center = effect.ScaledPosition(body);
            if (!scaled) center = ScaledSpace.ScaledToLocalSpace(center);
            return Vector3d.Distance(center, camera.transform.position);
        }

        private void DrawExplosion(ExplosionVisual effect, Camera camera, bool scaled, double now, bool shock)
        {
            var body = effect.Body;
            if (body == null || body.scaledBody == null) return; // Scene still loading; retain the record.
            double age = now - effect.startedAt;
            double scale = shock ? ExplosionAnimation.ShockScale(age) : ExplosionAnimation.MainScale(age, effect.lifetime);
            float opacity = shock ? (float)ExplosionAnimation.ShockOpacity(age) : 1;
            if (scale <= 0 || opacity <= 0) return;
            Vector3d position = effect.ScaledPosition(body);
            double radius = effect.radius * scale;
            if (scaled) radius *= ScaledSpace.InverseScaleFactor;
            else position = ScaledSpace.ScaledToLocalSpace(position);
            Vector3d relative = (position - (Vector3d)camera.transform.position) / radius;
            explosionMaterial.SetVector("_CenterRadius", new Vector4((float)relative.x, (float)relative.y, (float)relative.z, (float)radius));
            explosionMaterial.SetVector("_Effect", new Vector4((float)(Math.Max(0, age) % 4096), effect.seed, opacity, shock ? 1 : 0));
            if (explosionMaterial.SetPass(shock ? 1 : 0)) Graphics.DrawMeshNow(explosionQuad, Matrix4x4.identity);
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
            if (explosionRendererRegistered)
            {
                Camera.onPreCull -= ShakeExplosionCamera;
                Camera.onPostRender -= RenderExplosionVolumes;
            }
            RestoreAllExplosionCameras();
            foreach (var effect in explosionVisuals) DestroyExplosionLights(effect);
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
