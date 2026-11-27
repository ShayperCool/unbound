using System;
using System.IO;
using System.Linq;
using System.Text;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Presets;
#endif
using UnityEngine;

/// <summary>
/// Writes values.jsonl in $PRESETS_OUT (tests/value_probe.py rows): what the Editor's Preset API
/// answers for the presets in Assets/Presets (a Transform's, a script's, a BoxCollider's, a
/// Material's), applying them, the project's default presets (ProjectSettings/PresetManager.asset)
/// and PresetType and DefaultPreset values.
/// </summary>
public class PresetsProbe : MonoBehaviour
{
    const string Variable = "PRESETS_OUT";
    static StreamWriter output;

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");

    static void Try(string key, Func<string> action)
    {
        string result;
        try { result = action(); }
        catch (Exception e) { result = "throws " + e.GetType().FullName; }
        Row(key, result);
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("PresetsProbe").AddComponent<PresetsProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable(Variable);
        Directory.CreateDirectory(dir);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));
#if UNITY_EDITOR
        try { Run(); }
        catch (Exception e) { Row("aborted", e.GetType().FullName + ": " + e.Message); }
#endif
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Dispose();
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#endif
    }

#if UNITY_EDITOR
    static string Names(Preset[] presets) => presets == null ? "null" : "[" + string.Join(",", presets.Select(p => p == null ? "null" : p.name)) + "]";

    static string Type(PresetType type) =>
        "valid " + type.IsValid() + " default " + type.IsValidDefault() + " managed '" + type.GetManagedTypeName() + "'";

    static string Modifications(Preset preset) =>
        string.Join("\n", preset.PropertyModifications.Select(m => m.propertyPath + "=" + m.value + (m.objectReference != null ? " ref " + m.objectReference.name : "")));

    static void Describe(string key, Preset preset)
    {
        Try(key + "/name", () => preset == null ? "null" : preset.name);
        Try(key + "/fullType", () => preset.GetTargetFullTypeName());
        Try(key + "/type", () => preset.GetTargetTypeName());
        Try(key + "/valid", () => "" + preset.IsValid());
        Try(key + "/presetType", () => Type(preset.GetPresetType()));
        Try(key + "/modifications", () => Modifications(preset));
        Try(key + "/excluded", () => string.Join(",", preset.excludedProperties));
    }

    static void Run()
    {
        var transformPreset = AssetDatabase.LoadAssetAtPath<Preset>("Assets/Presets/Transform.preset");
        var tunedPreset = AssetDatabase.LoadAssetAtPath<Preset>("Assets/Presets/Tuned.preset");
        var boxPreset = AssetDatabase.LoadAssetAtPath<Preset>("Assets/Presets/Box.preset");
        var materialPreset = AssetDatabase.LoadAssetAtPath<Preset>("Assets/Presets/Material.preset");
        Describe("asset/Transform", transformPreset);
        Describe("asset/Tuned", tunedPreset);
        Describe("asset/Box", boxPreset);
        Try("asset/Material/fullType", () => materialPreset.GetTargetFullTypeName());
        Try("asset/Material/valid", () => "" + materialPreset.IsValid());

        var named = new GameObject("NamedTarget");
        var other = new GameObject("OtherTarget");
        var tuned = named.AddComponent<Tuned>();
        var box = named.AddComponent<BoxCollider>();
        var material = new Material(Shader.Find("Standard"));
        Try("canApply/TransformToTransform", () => "" + transformPreset.CanBeAppliedTo(named.transform));
        Try("canApply/TransformToBox", () => "" + transformPreset.CanBeAppliedTo(box));
        Try("canApply/TransformToNull", () => "" + transformPreset.CanBeAppliedTo(null));
        Try("canApply/TunedToTuned", () => "" + tunedPreset.CanBeAppliedTo(tuned));
        Try("canApply/TunedToBox", () => "" + tunedPreset.CanBeAppliedTo(box));
        Try("canApply/MaterialToMaterial", () => "" + materialPreset.CanBeAppliedTo(material));

        Try("apply/Transform", () => transformPreset.ApplyTo(named.transform) + " " + named.transform.localPosition + " " + named.transform.localScale);
        Try("apply/TransformPartial", () => transformPreset.ApplyTo(other.transform, new[] { "m_LocalScale" }) + " " + other.transform.localPosition + " " + other.transform.localScale);
        Try("apply/TransformToBox", () => "" + transformPreset.ApplyTo(box));
        Try("apply/Tuned", () => tunedPreset.ApplyTo(tuned) + " " + tuned.count + " " + tuned.speed + " " + tuned.label + " " + tuned.tint);
        Try("apply/Box", () => boxPreset.ApplyTo(box) + " " + box.size + " " + box.center + " " + box.isTrigger);
        Try("apply/Material", () => materialPreset.ApplyTo(material) + " " + material.color);
        Try("apply/Null", () => "" + transformPreset.ApplyTo(null));

        Try("defaults/NamedTransform", () => Names(Preset.GetDefaultPresetsForObject(named.transform)));
        Try("defaults/OtherTransform", () => Names(Preset.GetDefaultPresetsForObject(other.transform)));
        Try("defaults/Box", () => Names(Preset.GetDefaultPresetsForObject(box)));
        Try("defaults/Tuned", () => Names(Preset.GetDefaultPresetsForObject(tuned)));
        Try("defaults/Null", () => Names(Preset.GetDefaultPresetsForObject(null)));
        Try("defaultsForType/Transform", () => string.Join("|", Preset.GetDefaultPresetsForType(new PresetType(named.transform))
            .Select(d => "'" + d.filter + "' " + (d.preset == null ? "null" : d.preset.name) + " " + d.enabled)));
        Try("defaultsForType/Tuned", () => Preset.GetDefaultPresetsForType(new PresetType(tuned)).Length.ToString());

        Try("type/Transform", () => Type(new PresetType(named.transform)));
        Try("type/Tuned", () => Type(new PresetType(tuned)));
        Try("type/Material", () => Type(new PresetType(material)));
        Try("type/GameObject", () => Type(new PresetType(named)));
        Try("type/Default", () => Type(default(PresetType)));
        Try("type/ScriptableObject", () => Type(new PresetType(ScriptableObject.CreateInstance<TunedData>())));
        Try("type/TextureImporter", () => Type(new PresetType(AssetImporter.GetAtPath("Assets/Presets/Tex.png"))));
        Try("type/AnimationClip", () => Type(new PresetType(new AnimationClip())));
        Try("type/Texture2D", () => Type(new PresetType(AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Presets/Tex.png"))));
        Try("type/Null", () => Type(new PresetType(null)));
        Try("type/Preset", () => Type(new PresetType(boxPreset)));
        Try("type/Equal", () => "" + (new PresetType(named.transform) == new PresetType(other.transform)) + " " + (new PresetType(named.transform) == new PresetType(box)));
        Try("defaultPreset/two", () => { var d = new DefaultPreset("f", boxPreset); return "'" + d.filter + "' " + d.preset.name + " " + d.enabled; });
        Try("defaultPreset/three", () => { var d = new DefaultPreset("g", boxPreset, false); return "'" + d.filter + "' " + d.preset.name + " " + d.enabled; });
        Try("defaultPreset/default", () => { var d = default(DefaultPreset); return "'" + d.filter + "' " + (d.preset == null ? "null" : "set") + " " + d.enabled; });

        // A preset made from a live object.
        var source = new GameObject("Source");
        source.transform.localPosition = new Vector3(4, 5, 6);
        Describe("made/Transform", new Preset(source.transform));
        var made = source.AddComponent<Tuned>();
        made.label = "made";
        Describe("made/Tuned", new Preset(made));
        Try("made/GameObject/valid", () => "" + new Preset(source).IsValid());

        // Controls for missing script, ScriptedImporter, filter edge cases, GetAllDefaultTypes,
        // DataEquals, excludedProperties, UpdateProperties, ScriptableObject preset, RemoveFromDefault.
        var missingPreset = AssetDatabase.LoadAssetAtPath<Preset>("Assets/Presets/MissingScript.preset");
        Try("asset/MissingScript/valid", () => "" + missingPreset.IsValid());
        Try("asset/MissingScript/presetType", () => Type(missingPreset.GetPresetType()));
        Try("type/ScriptedImporter", () => Type(new PresetType(AssetImporter.GetAtPath("Assets/Presets/Sample.tune"))));

        var lowerNamed = new GameObject("namedLower");
        var childNamed = new GameObject("NamedChild");
        childNamed.transform.SetParent(other.transform, false);
        var slashNamed = new GameObject("Named/Slash");
        Try("defaults/LowerNamedTransform", () => Names(Preset.GetDefaultPresetsForObject(lowerNamed.transform)));
        Try("defaults/ChildNamedTransform", () => Names(Preset.GetDefaultPresetsForObject(childNamed.transform)));
        Try("defaults/SlashNamedTransform", () => Names(Preset.GetDefaultPresetsForObject(slashNamed.transform)));
        Try("allDefaultTypes", () => string.Join("|", Preset.GetAllDefaultTypes().Select(Type)));

        Try("dataEquals/TransformAfterApply", () => "" + transformPreset.DataEquals(named.transform));
        Try("dataEquals/TransformPartial", () => "" + transformPreset.DataEquals(other.transform));
        Try("dataEquals/TunedAfterApply", () => "" + tunedPreset.DataEquals(tuned));
        Try("dataEquals/BoxAfterApply", () => "" + boxPreset.DataEquals(box));
        Try("dataEquals/MaterialAfterApply", () => "" + materialPreset.DataEquals(material));
        Try("dataEquals/WrongType", () => "" + transformPreset.DataEquals(box));
        Try("dataEquals/Null", () => "" + transformPreset.DataEquals(null));

        var exSource = new GameObject("ExSource");
        exSource.transform.localPosition = new Vector3(1, 2, 3);
        exSource.transform.localScale = new Vector3(2, 2, 2);
        var exPreset = new Preset(exSource.transform);
        exPreset.excludedProperties = new[] { "m_LocalPosition" };
        var exTarget = new GameObject("ExTarget");
        Try("excluded/Apply", () => exPreset.ApplyTo(exTarget.transform) + " " + exTarget.transform.localPosition + " " + exTarget.transform.localScale);
        Try("excluded/DataEqualsDiffExcluded", () => "" + exPreset.DataEquals(exTarget.transform));
        Try("excluded/List", () => string.Join(",", exPreset.excludedProperties));

        var upPreset = new Preset(made);
        made.count = 42;
        made.label = "updated";
        var upTarget = new GameObject("UpTarget").AddComponent<Tuned>();
        Try("update/Tuned", () => upPreset.UpdateProperties(made) + " " + upPreset.ApplyTo(upTarget) + " " + upTarget.count + " " + upTarget.label);
        Try("update/WrongType", () => "" + upPreset.UpdateProperties(box));
        Try("update/Null", () => "" + upPreset.UpdateProperties(null));

        var soData = ScriptableObject.CreateInstance<TunedData>();
        soData.value = 11;
        Describe("made/ScriptableObject", new Preset(soData));
        Try("removeFromDefault/Null", () => { Preset.RemoveFromDefault(null); return "ok"; });
    }
#endif
}
