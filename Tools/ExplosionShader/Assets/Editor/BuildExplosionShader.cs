using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

public static class BuildExplosionShader
{
    // Unity 2019.4 (KSP's Unity generation), -batchmode -executeMethod BuildExplosionShader.Build -quit
    public static void Build()
    {
        string output = Path.GetFullPath(Path.Combine(Application.dataPath, "../../../ChitakTech/Shaders"));
        Directory.CreateDirectory(output);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,
            new[] { GraphicsDeviceType.Direct3D11, GraphicsDeviceType.OpenGLCore, GraphicsDeviceType.Vulkan });
        var build = new AssetBundleBuild { assetBundleName = "explosion-shaders",
            assetNames = new[] { "Assets/ExplosionVolume.shader" } };
        if (BuildPipeline.BuildAssetBundles(output, new[] { build },
            BuildAssetBundleOptions.ForceRebuildAssetBundle | BuildAssetBundleOptions.ChunkBasedCompression,
            BuildTarget.StandaloneWindows64) == null)
            throw new System.Exception("Explosion shader bundle build failed.");
        if (ShaderUtil.ShaderHasError(AssetDatabase.LoadAssetAtPath<Shader>("Assets/ExplosionVolume.shader")))
            throw new System.Exception("Explosion shader compilation failed; see shader compiler errors.");
    }
}
