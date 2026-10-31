using System;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

// Run with -batchmode (without -nographics) -executeMethod ExplosionRenderChecks.Run -quit.
// Tests real GPU pixels using the compiled/bundled shader, including depth and fog.
public static class ExplosionRenderChecks
{
    public static void Run()
    {
        BuildExplosionShader.Build();
        string path = System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath, "../../../ChitakTech/Shaders/explosion-shaders"));
        var bundle = AssetBundle.LoadFromFile(path);
        var material = new Material(bundle.LoadAsset<Shader>("Assets/ExplosionVolume.shader"));
        if (!material.shader.isSupported) throw new Exception("Shader unsupported on test GPU.");
        var quad = new Mesh { vertices = new[] { new Vector3(-1,-1,0), new Vector3(1,-1,0), new Vector3(1,1,0), new Vector3(-1,1,0) }, triangles = new[] { 0,1,2,0,2,3 } };
        var go = new GameObject("Explosion render checks");
        var camera = go.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.nearClipPlane = .1f;
        camera.farClipPlane = 1000;
        camera.fieldOfView = 60;
        camera.aspect = 1;
        camera.enabled = false;
        var target = new RenderTexture(128, 128, 24);
        camera.targetTexture = target;
        var readback = new Texture2D(128, 128, TextureFormat.RGB24, false);
        Vector4 sphere = new Vector4(0,0,3,1);
        Camera.CameraCallback draw = current => {
            if (current != camera) return;
            material.SetVector("_CenterRadius", sphere);
            material.SetVector("_Forward", camera.transform.forward);
            material.SetVector("_Clip", new Vector4(camera.nearClipPlane, camera.farClipPlane,0,0));
            material.SetVector("_Fog", new Vector4(RenderSettings.fog ? (float)RenderSettings.fogMode : 0,
                RenderSettings.fogStartDistance, RenderSettings.fogEndDistance, RenderSettings.fogDensity));
            material.SetColor("_FogColor", RenderSettings.fogColor);
            // KSP draws to the back buffer. RT projection is appropriate for this test target.
            material.SetMatrix("_Projection", GL.GetGPUProjectionMatrix(camera.projectionMatrix, true));
            for (int y=0;y<2;y++) for (int x=0;x<2;x++)
            {
                var ray = camera.ViewportPointToRay(new Vector3(x,y,0));
                material.SetVector("_Ray" + x + y, ray.direction / Vector3.Dot(ray.direction, camera.transform.forward));
                material.SetVector("_Origin" + x + y, Vector3.zero);
            }
            material.SetPass(0);
            Graphics.DrawMeshNow(quad, Matrix4x4.identity);
        };
        Camera.onPostRender += draw;
        Action render = () => {
            camera.Render();
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0,0,128,128),0,0);
            readback.Apply();
            RenderTexture.active = null;
        };
        int checks = 0;
        Action<bool,string> check = (success, name) => {
            if (!success) throw new Exception("Explosion rendering failed: " + name);
            checks++; Debug.Log("Explosion rendering PASS: " + name);
        };
        try
        {
            RenderSettings.fog = false;
            render();
            check(readback.GetPixel(64,64).r > .8f && readback.GetPixel(0,0).r < .05f, "external sphere and silhouette");
            sphere = new Vector4(0,0,0,100000000);
            render();
            check(readback.GetPixel(64,64).r > .8f && readback.GetPixel(0,0).r > .8f, "inside 100,000-km sphere beyond far clip");
            sphere = new Vector4(0,0,0,100000000f / 6000);
            render();
            check(readback.GetPixel(64,64).r > .8f, "scaled-space interior");
            sphere = new Vector4(0,0,-3,1);
            render();
            check(readback.GetPixel(64,64).r < .05f, "sphere entirely behind viewer");
            sphere = new Vector4(0,0,3,1);
            var blocker = GameObject.CreatePrimitive(PrimitiveType.Cube);
            blocker.transform.position = new Vector3(0,0,1);
            blocker.transform.localScale = Vector3.one * .5f;
            var blue = new Material(Shader.Find("Unlit/Color")); blue.color = Color.blue;
            blocker.GetComponent<Renderer>().sharedMaterial = blue;
            render();
            check(readback.GetPixel(64,64).b > .8f && readback.GetPixel(64,64).r < .05f, "nearer scenery occludes explosion");
            Object.DestroyImmediate(blocker); Object.DestroyImmediate(blue);
            RenderSettings.fog = true; RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogStartDistance = 0; RenderSettings.fogEndDistance = 1;
            RenderSettings.fogColor = Color.blue;
            material.EnableKeyword("FOG_LINEAR");
            render();
            check(readback.GetPixel(64,64).b > .8f && readback.GetPixel(64,64).r < .05f, "atmospheric fog variant");
            Debug.Log(checks + " explosion GPU checks passed.");
        }
        finally
        {
            Camera.onPostRender -= draw;
            RenderSettings.fog = false;
            Object.DestroyImmediate(go); Object.DestroyImmediate(target);
            Object.DestroyImmediate(readback); Object.DestroyImmediate(quad); Object.DestroyImmediate(material);
            bundle.Unload(true);
        }
    }
}
