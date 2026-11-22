using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Records, in Play mode, what ObjectFactory.CreateInstance makes of engine object types that have
/// no public parameterless constructor: the object's type, name and hide flags, or what it throws.
/// </summary>
static class FactoryInstancesProbe
{
    static readonly string[] s_Types = { "UnityEditor.Animations.AnimatorTransitionBase", "UnityEditor.Build.Reporting.BuildReport", "UnityEditor.DefaultAsset", "UnityEditor.LightingDataAsset", "UnityEditor.Presets.Preset", "UnityEditor.SceneAsset", "UnityEngine.AssetBundleManifest", "UnityEngine.Audio.AudioMixerGroup", "UnityEngine.Audio.AudioMixerSnapshot", "UnityEngine.AudioClip", "UnityEngine.Avatar", "UnityEngine.ComputeShader", "UnityEngine.Cubemap", "UnityEngine.CubemapArray", "UnityEngine.CustomRenderTexture", "UnityEngine.LightProbes", "UnityEngine.Material", "UnityEngine.Motion", "UnityEngine.ProceduralMaterial", "UnityEngine.Rendering.GraphicsSettings", "UnityEngine.Rendering.RayTracingShader", "UnityEngine.RenderSettings", "UnityEngine.RenderTexture", "UnityEngine.Shader", "UnityEngine.SparseTexture", "UnityEngine.SpeedTreeWindAsset", "UnityEngine.Sprite", "UnityEngine.Texture", "UnityEngine.Texture2D", "UnityEngine.Texture2DArray", "UnityEngine.Texture3D", "UnityEngine.Video.VideoClip",  };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("FACTORY_INSTANCES_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var output = new StringBuilder();
        foreach (var name in s_Types)
        {
            var type = Type.GetType(name + ", UnityEngine") ?? Type.GetType(name + ", UnityEditor");
            if (type == null)
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    if ((type = assembly.GetType(name)) != null) break;
            string value;
            try
            {
                var made = type == null ? null : ObjectFactory.CreateInstance(type);
                value = type == null ? "<no type>" : made == null ? "null" : made.GetType().FullName + "|" + Quote(made.name) + "|" + (int)made.hideFlags;
            }
            catch (Exception e) { value = "throws " + e.GetType().Name + ": " + e.Message; }
            output.Append("{\"k\":\"").Append(name).Append("\",\"v\":\"").Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n")).Append("\"}\n");
        }
        output.Append("{\"k\":\"complete\",\"v\":true}\n");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), output.ToString());
        EditorApplication.Exit(0);
    }

    static string Quote(string s) => s == null ? "<null>" : "'" + s + "'";
}
