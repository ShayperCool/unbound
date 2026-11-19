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
/// <c>MODEL_IMPORT_PROBE_OUT</c> set opens it, renders once (as an Editor that has drawn its
/// views) and enters Play mode, where the probe records the reference and exits the Editor.
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
        // As an Editor that has drawn its views before Play mode (the case Unbound plays: the
        // configured pipeline's shader tag is active as the scene loads, tests/projects/graphqueue):
        // a camera made for it renders once and goes, so the pipeline is made.
        var camera = new GameObject("ProbeRunner.Render").AddComponent<Camera>();
        var target = new RenderTexture(64, 64, 24);
        camera.targetTexture = target;
        camera.Render();
        camera.targetTexture = null;
        UnityEngine.Object.DestroyImmediate(camera.gameObject);
        UnityEngine.Object.DestroyImmediate(target);
        Debug.Log("model import probe: after a render, pipeline tag '" + Shader.globalRenderPipeline + "'");
        if (UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline != null && string.IsNullOrEmpty(Shader.globalRenderPipeline))
        {
            // Without the tag the capture would be the batch-mode case this render exists to avoid.
            Debug.LogError("model import probe: the render made no pipeline shader tag; not recording");
            EditorApplication.Exit(1);
            return;
        }
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

/// <summary>
/// With <c>MODEL_IMPORT_PROBE_OUT</c> set, appends the state of each material of the Exotic models
/// at points of their import to that path plus <c>.stages</c>, which <see cref="ModelImportProbe"/>
/// adds to its record (<c>stages.&lt;model&gt;.&lt;material&gt;.&lt;stage&gt;.*</c>): after the
/// render pipeline's FBX preprocessor (order -975), after its Autodesk Interactive one (-965),
/// and after its Arnold and 3ds Max Physical ones (-955); no OnPostprocessMaterial (one that
/// reads a material's values changes how the legacy importer's materials end: the URPHookRecord
/// profile, README). Each point gives the shader, the GI flags, whether the shader has _EmissionColor and its
/// _QueueControl (none without one).
/// </summary>
public static class MaterialStages
{
    public static void Record(string assetPath, Material material, string stage)
    {
        var path = Environment.GetEnvironmentVariable("MODEL_IMPORT_PROBE_OUT");
        if (string.IsNullOrEmpty(path) || !assetPath.Contains("/Exotic")) return;
        var key = "stages." + Path.GetFileNameWithoutExtension(assetPath) + "." + material.name + "." + stage;
        var shader = material.shader;
        var b = new StringBuilder();
        b.Append(key).Append(".shader\t").Append(shader != null ? shader.name : "null").Append('\n');
        b.Append(key).Append(".gi\t").Append(material.globalIlluminationFlags.ToString()).Append('\n');
        b.Append(key).Append(".hasEmissionColor\t").Append(material.HasProperty("_EmissionColor") ? "true" : "false").Append('\n');
        b.Append(key).Append(".queueControl\t").Append(material.HasProperty("_QueueControl")
            ? material.GetFloat("_QueueControl").ToString("R", System.Globalization.CultureInfo.InvariantCulture) : "none").Append('\n');
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        File.AppendAllText(path + ".stages", b.ToString());
    }
}

public class MaterialStageAfterFbx : AssetPostprocessor
{
    public override int GetPostprocessOrder() => -975;

    public void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips) =>
        MaterialStages.Record(assetPath, material, "afterFbx");

}

public class MaterialStageAfterInteractive : AssetPostprocessor
{
    public override int GetPostprocessOrder() => -965;

    public void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips) =>
        MaterialStages.Record(assetPath, material, "afterInteractive");
}

public class MaterialStageAfterArnold : AssetPostprocessor
{
    public override int GetPostprocessOrder() => -955;

    public void OnPreprocessMaterialDescription(MaterialDescription description, Material material, AnimationClip[] clips) =>
        MaterialStages.Record(assetPath, material, "afterArnold");
}
