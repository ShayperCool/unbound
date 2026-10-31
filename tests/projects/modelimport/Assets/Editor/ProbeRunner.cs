using System;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.AssetImporters;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// <c>-executeMethod ProbeRunner.Create</c> makes the probe scene: a <see cref="ModelImportProbe"/>
/// holding every model in Assets/Models. <c>-executeMethod ProbeRunner.Record</c> with
/// <c>MODEL_IMPORT_PROBE_OUT</c> set opens it and enters Play mode, where the probe records the
/// reference and exits the Editor.
/// </summary>
public static class ProbeRunner
{
    public static void Create()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var probe = new GameObject("ModelImportProbe").AddComponent<ModelImportProbe>();
        probe.models = AssetDatabase.FindAssets("t:Model", new[] { "Assets/Models" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(p => p, StringComparer.Ordinal)
            .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
            .ToArray();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}

/// <summary>
/// With <c>MODEL_IMPORT_DESCRIPTIONS_OUT</c> set, appends every material description the FBX
/// importer hands the preprocessors to that file (order -1000: before Unity's own, -990), for the
/// importer's unit test.
/// </summary>
public class DescriptionDump : AssetPostprocessor
{
    public override int GetPostprocessOrder() => -1000;

    public void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips)
    {
        var path = Environment.GetEnvironmentVariable("MODEL_IMPORT_DESCRIPTIONS_OUT");
        if (string.IsNullOrEmpty(path)) return;
        var b = new StringBuilder();
        b.Append(assetPath).Append(" | ").Append(description.materialName).Append('\n');
        var names = new System.Collections.Generic.List<string>();
        description.GetFloatPropertyNames(names);
        foreach (var n in names.OrderBy(n => n, StringComparer.Ordinal))
        {
            description.TryGetProperty(n, out float f);
            b.Append("  float ").Append(n).Append(" = ").Append(f.ToString("R")).Append('\n');
        }
        names.Clear();
        description.GetVector4PropertyNames(names);
        foreach (var n in names.OrderBy(n => n, StringComparer.Ordinal))
        {
            description.TryGetProperty(n, out Vector4 v);
            b.Append("  vector ").Append(n).Append(" = ").Append(v.ToString("R")).Append('\n');
        }
        names.Clear();
        description.GetStringPropertyNames(names);
        foreach (var n in names.OrderBy(n => n, StringComparer.Ordinal))
        {
            description.TryGetProperty(n, out string s);
            b.Append("  string ").Append(n).Append(" = ").Append(s).Append('\n');
        }
        names.Clear();
        description.GetTexturePropertyNames(names);
        foreach (var n in names.OrderBy(n => n, StringComparer.Ordinal))
        {
            description.TryGetProperty(n, out TexturePropertyDescription t);
            b.Append("  texture ").Append(n).Append(" = ").Append(t.path)
                .Append(" texture=").Append(t.texture != null ? AssetDatabase.GetAssetPath(t.texture) + ":" + t.texture.name : "null")
                .Append(" offset=").Append(t.offset.ToString("R")).Append(" scale=").Append(t.scale.ToString("R")).Append('\n');
        }
        File.AppendAllText(path, b.ToString());
    }
}
