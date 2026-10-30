using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

/// <summary>`Unity -batchmode -projectPath tests/projects/builtinrp -executeMethod ProbeRunner.Record`
/// with <c>BUILTIN_PROBE_OUT</c> set: the probe scene in Play mode, where <see cref="BuiltinProbe"/>
/// records and exits the Editor. <see cref="Create"/> made the scene and the assets.</summary>
public static class ProbeRunner
{
    public static void Create()
    {
        System.IO.Directory.CreateDirectory("Assets/Scenes");
        System.IO.Directory.CreateDirectory("Assets/Resources");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };

        // A material asset whose shadow caster pass is disabled.
        var noShadow = new Material(Shader.Find("Standard")) { color = new Color(0.4f, 0.4f, 1f) };
        noShadow.SetShaderPassEnabled("ShadowCaster", false);
        AssetDatabase.CreateAsset(noShadow, "Assets/Resources/NoShadow.mat");

        // Render texture assets with settings away from the defaults.
        var target = new RenderTexture(new RenderTextureDescriptor(96, 64, GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.D24_UNorm_S8_UInt))
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp,
            anisoLevel = 3,
        };
        AssetDatabase.CreateAsset(target, "Assets/Resources/ProbeTarget.renderTexture");
        var half = new RenderTexture(new RenderTextureDescriptor(64, 32, GraphicsFormat.R16G16B16A16_SFloat, GraphicsFormat.None)
        {
            useMipMap = true,
            autoGenerateMips = false,
            msaaSamples = 2,
            enableRandomWrite = false,
        })
        {
            filterMode = FilterMode.Trilinear,
            wrapModeU = TextureWrapMode.Repeat,
            wrapModeV = TextureWrapMode.Mirror,
            mipMapBias = 0.5f,
        };
        AssetDatabase.CreateAsset(half, "Assets/Resources/ProbeHalf.renderTexture");
        var cube = new RenderTexture(new RenderTextureDescriptor(32, 32, GraphicsFormat.R8G8B8A8_SRGB, GraphicsFormat.D32_SFloat)
        {
            dimension = TextureDimension.Cube,
            useMipMap = true,
            autoGenerateMips = true,
        });
        AssetDatabase.CreateAsset(cube, "Assets/Resources/ProbeCube.renderTexture");
        var volume = new RenderTexture(new RenderTextureDescriptor(16, 16, GraphicsFormat.R32_SFloat, GraphicsFormat.None)
        {
            dimension = TextureDimension.Tex2DArray,
            volumeDepth = 4,
            enableRandomWrite = true,
        })
        {
            wrapModeW = TextureWrapMode.MirrorOnce,
        };
        AssetDatabase.CreateAsset(volume, "Assets/Resources/ProbeVolume.renderTexture");
        var shadow = new RenderTexture(new RenderTextureDescriptor(128, 128, GraphicsFormat.None, GraphicsFormat.D16_UNorm)
        {
            shadowSamplingMode = ShadowSamplingMode.CompareDepths,
        });
        AssetDatabase.CreateAsset(shadow, "Assets/Resources/ProbeShadow.renderTexture");
        AssetDatabase.SaveAssets();
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
