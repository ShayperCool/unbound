using System;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Records, in Play mode, what ObjectFactory.CreateInstance makes of the engine object types the
/// release declares no public parameterless constructor for (its own list, below): the object's type,
/// name and hide flags, or what it throws, and what a made texture or material holds. With
/// FACTORY_INSTANCES_LIST=1 it also writes that list as the running Editor's types give it
/// (types.txt), to regenerate the list for another release.
/// </summary>
static class FactoryInstancesProbe
{
    static readonly string[] s_Types = { "UnityEditor.Animations.AnimatorTransitionBase", "UnityEditor.BrokenPrefabAsset", "UnityEditor.Build.Reporting.BuildReport", "UnityEditor.DefaultAsset", "UnityEditor.EditorBuildSettings", "UnityEditor.EditorSettings", "UnityEditor.EditorUserBuildSettings", "UnityEditor.EditorUserSettings", "UnityEditor.LightingDataAsset", "UnityEditor.PlayerSettings", "UnityEditor.Presets.Preset", "UnityEditor.SceneAsset", "UnityEditor.VersionControlSettings", "UnityEditorInternal.AssemblyDefinitionAsset", "UnityEditorInternal.AssemblyDefinitionReferenceAsset", "UnityEditorInternal.PackageManifest", "UnityEngine.AssetBundle", "UnityEngine.AssetBundleManifest", "UnityEngine.Audio.AudioMixer", "UnityEngine.Audio.AudioMixerGroup", "UnityEngine.Audio.AudioMixerSnapshot", "UnityEngine.AudioClip", "UnityEngine.Avatar", "UnityEngine.ComputeShader", "UnityEngine.Cubemap", "UnityEngine.CubemapArray", "UnityEngine.CustomRenderTexture", "UnityEngine.LightProbes", "UnityEngine.LightmapSettings", "UnityEngine.Material", "UnityEngine.Motion", "UnityEngine.MovieTexture", "UnityEngine.ProceduralMaterial", "UnityEngine.ProceduralTexture", "UnityEngine.QualitySettings", "UnityEngine.RenderSettings", "UnityEngine.RenderTexture", "UnityEngine.Rendering.GraphicsSettings", "UnityEngine.Rendering.RayTracingShader", "UnityEngine.RuntimeAnimatorController", "UnityEngine.Shader", "UnityEngine.SparseTexture", "UnityEngine.SpeedTreeWindAsset", "UnityEngine.Sprite", "UnityEngine.Texture", "UnityEngine.Texture2D", "UnityEngine.Texture2DArray", "UnityEngine.Texture3D", "UnityEngine.Video.VideoClip" };

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("FACTORY_INSTANCES_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var output = new StringBuilder();
        if (Environment.GetEnvironmentVariable("FACTORY_INSTANCES_LIST") == "1") WriteList(directory);
        foreach (var name in s_Types)
        {
            var type = Type.GetType(name + ", UnityEngine") ?? Type.GetType(name + ", UnityEditor");
            if (type == null)
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                    if ((type = assembly.GetType(name)) != null) break;
            string value, state = null;
            try
            {
                var made = type == null ? null : ObjectFactory.CreateInstance(type);
                value = type == null ? "<no type>" : made == null ? "null" : made.GetType().FullName + "|" + Quote(made.name) + "|" + (int)made.hideFlags;
                if (made != null)
                {
                    try { state = State(made); }
                    catch (Exception e) { state = "|throws " + e.GetType().Name; }
                }
            }
            catch (Exception e) { value = "throws " + e.GetType().Name + ": " + e.Message; }
            // What a made object holds has a row of its own.
            if (!string.IsNullOrEmpty(state)) output.Append("{\"k\":\"state.").Append(name).Append("\",\"v\":\"").Append(state.Substring(1)).Append("\"}\n");
            output.Append("{\"k\":\"").Append(name).Append("\",\"v\":\"").Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n")).Append("\"}\n");
        }
        output.Append("{\"k\":\"complete\",\"v\":true}\n");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), output.ToString());
        EditorApplication.Exit(0);
    }

    /// <summary>The engine object types without a public parameterless constructor, as the running
    /// Editor's assemblies declare them, with whether a hidden one exists.</summary>
    static void WriteList(string directory)
    {
        var names = new System.Collections.Generic.List<string>();
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var an = assembly.GetName().Name;
            if (!an.StartsWith("UnityEngine.", StringComparison.Ordinal) && !an.StartsWith("UnityEditor.", StringComparison.Ordinal)
                && an != "UnityEngine" && an != "UnityEditor") continue;
            Type[] all;
            try { all = assembly.GetTypes(); }
            catch (System.Reflection.ReflectionTypeLoadException e) { all = Array.FindAll(e.Types, x => x != null); }
            foreach (var t in all)
            {
                if (!t.IsPublic || t.IsAbstract || !typeof(UnityEngine.Object).IsAssignableFrom(t) || typeof(Component).IsAssignableFrom(t)
                    || t == typeof(GameObject) || typeof(ScriptableObject).IsAssignableFrom(t) || t.GetConstructor(Type.EmptyTypes) != null) continue;
                var hidden = t.GetConstructor(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, null, Type.EmptyTypes, null) != null;
                names.Add(t.FullName + (hidden ? " hidden" : " none"));
            }
        }
        names.Sort(StringComparer.Ordinal);
        Directory.CreateDirectory(directory);
        File.WriteAllLines(Path.Combine(directory, "types.txt"), names);
    }

    /// <summary>What a made object holds: a texture's size, format, mip count and readability and
    /// whether Apply throws; a material's shader.</summary>
    static string State(UnityEngine.Object made)
    {
        var parts = new System.Collections.Generic.List<string>();
        if (made is Texture texture)
        {
            parts.Add(texture.width + "x" + texture.height);
            if (made is Texture3D t3) parts.Add("depth=" + t3.depth);
            if (made is Texture2DArray array) parts.Add("depth=" + array.depth);
            if (made is CubemapArray cubes) parts.Add("cubemaps=" + cubes.cubemapCount);
            parts.Add(texture.graphicsFormat.ToString());
            parts.Add("mips=" + texture.mipmapCount);
            parts.Add("readable=" + texture.isReadable);
            Action apply = made switch
            {
                Texture2D t => () => t.Apply(),
                Texture3D t => () => t.Apply(),
                Texture2DArray t => () => t.Apply(),
                Cubemap t => () => t.Apply(),
                CubemapArray t => () => t.Apply(),
                _ => null,
            };
            if (apply != null)
            {
                try { apply(); parts.Add("apply=ok"); }
                catch (Exception e) { parts.Add("apply=" + e.GetType().Name); }
            }
            if (made is CustomRenderTexture crt) parts.Add("material=" + (crt.material == null ? "null" : crt.material.name));
        }
        if (made is Material material) parts.Add("shader=" + (material.shader == null ? "null" : material.shader.name));
        return parts.Count == 0 ? "" : "|" + string.Join("|", parts);
    }

    static string Quote(string s) => s == null ? "<null>" : "'" + s + "'";
}
