using System.Collections.Generic;
using UnityEngine;

namespace ChitakTech
{
    public partial class CTShared
    {
        private readonly Dictionary<Camera, Quaternion> explosionCameraRotations = new Dictionary<Camera, Quaternion>();
        private float explosionShakeUntil, explosionShakeStrength;

        private void UpdateExplosionLighting(double now)
        {
            bool map = HighLogic.LoadedSceneIsFlight && MapView.MapIsEnabled;
            Camera observer = map ? (PlanetariumCamera.fetch != null ? PlanetariumCamera.Camera : null)
                : FlightCamera.fetch != null ? FlightCamera.fetch.mainCamera : Camera.main;
            bool visible = observer != null && ExplosionCamera(observer, out _);
            foreach (var effect in explosionVisuals)
            {
                var body = effect.Body;
                double age = now - effect.startedAt;
                if (!visible || body == null || body.scaledBody == null)
                {
                    SetExplosionLightEnabled(effect, false);
                    effect.previousAge = age;
                    continue;
                }
                Vector3d center = effect.ScaledPosition(body);
                Vector3d observerPosition = map ? (Vector3d)observer.transform.position
                    : ScaledSpace.LocalToScaledSpace(observer.transform.position);
                Vector3d fromBlast = observerPosition - center;
                double distance = fromBlast.magnitude / (effect.radius * ScaledSpace.InverseScaleFactor);
                if (!effect.mainArrived && ExplosionAnimation.Crossed(effect.previousAge, age, distance, 1))
                { effect.mainArrived = true; BeginExplosionShake(.7f); }
                if (!effect.shockArrived && ExplosionAnimation.Crossed(effect.previousAge, age, distance, 10))
                { effect.shockArrived = true; BeginExplosionShake((float)(1.2 / (1 + distance * .15))); }
                effect.previousAge = age;

                if (map) { SetExplosionLightEnabled(effect, false); continue; }
                if (effect.mainLight == null) effect.mainLight = CreateExplosionLight("Fireball light", new Color(1, .45f, .12f));
                if (effect.shockLight == null) effect.shockLight = CreateExplosionLight("Shockwave flash", new Color(1, .88f, .68f));
                // A directional approximation avoids Unity's point-light precision/range
                // limits for astronomical blasts. Intensity falls with observer distance;
                // no light is applied to scaled planets, map icons or UI.
                Vector3 direction = fromBlast.sqrMagnitude > 1e-12 ? (Vector3)fromBlast.normalized : Vector3.down;
                Quaternion rotation = Quaternion.LookRotation(direction);
                effect.mainLight.transform.rotation = rotation;
                effect.shockLight.transform.rotation = rotation;
                double scale = ExplosionAnimation.MainScale(age, effect.lifetime);
                float attenuation = Mathf.Clamp01(1 - (float)(distance / 3));
                float flicker = .85f + .15f * Mathf.PerlinNoise(effect.seed, (float)(age % 4096) * 3);
                effect.mainLight.intensity = 1.7f * (float)scale * attenuation * flicker;
                double wave = ExplosionAnimation.ShockScale(age);
                float band = Mathf.Clamp01(1 - (float)(System.Math.Abs(distance - wave) / .3));
                effect.shockLight.intensity = 8 * band * (float)(ExplosionAnimation.ShockOpacity(age) / .35);
                effect.mainLight.enabled = effect.mainLight.intensity > .001f;
                effect.shockLight.enabled = effect.shockLight.intensity > .001f;
            }
        }

        private Light CreateExplosionLight(string name, Color color)
        {
            var obj = new GameObject("ChitakTech " + name);
            obj.transform.SetParent(transform, false);
            var light = obj.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = color;
            light.shadows = LightShadows.None;
            light.cullingMask = ~((1 << 10) | (1 << 5));
            light.renderMode = LightRenderMode.ForcePixel;
            light.enabled = false;
            return light;
        }

        private static void SetExplosionLightEnabled(ExplosionVisual effect, bool enabled)
        {
            if (effect.mainLight != null) effect.mainLight.enabled = enabled;
            if (effect.shockLight != null) effect.shockLight.enabled = enabled;
        }

        private void SuspendExplosionLighting()
        {
            RestoreAllExplosionCameras();
            foreach (var effect in explosionVisuals) SetExplosionLightEnabled(effect, false);
        }

        private void DestroyExplosionLights(ExplosionVisual effect)
        {
            if (effect.mainLight != null) Destroy(effect.mainLight.gameObject);
            if (effect.shockLight != null) Destroy(effect.shockLight.gameObject);
            effect.mainLight = effect.shockLight = null;
        }

        private void BeginExplosionShake(float strength)
        {
            explosionShakeStrength = Time.realtimeSinceStartup < explosionShakeUntil
                ? Mathf.Max(explosionShakeStrength, strength) : strength;
            explosionShakeUntil = Time.realtimeSinceStartup + 1;
        }

        private void ShakeExplosionCamera(Camera camera)
        {
            RestoreExplosionCamera(camera);
            float remaining = explosionShakeUntil - Time.realtimeSinceStartup;
            if (remaining <= 0 || !ExplosionCamera(camera, out _)) return;
            float amplitude = explosionShakeStrength * remaining * remaining;
            float t = Time.realtimeSinceStartup * 27;
            var offset = new Vector3(Mathf.Sin(t * 1.7f), Mathf.Sin(t * 2.3f + 2), Mathf.Sin(t * 1.3f + 4)) * amplitude;
            explosionCameraRotations[camera] = camera.transform.localRotation;
            camera.transform.localRotation *= Quaternion.Euler(offset);
        }

        private void RestoreExplosionCamera(Camera camera)
        {
            if (camera == null || !explosionCameraRotations.TryGetValue(camera, out Quaternion original)) return;
            camera.transform.localRotation = original;
            explosionCameraRotations.Remove(camera);
        }

        private void RestoreAllExplosionCameras()
        {
            foreach (var entry in explosionCameraRotations)
                if (entry.Key != null) entry.Key.transform.localRotation = entry.Value;
            explosionCameraRotations.Clear();
            explosionShakeUntil = 0;
        }
    }
}
