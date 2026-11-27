using System;
using System.IO;
using System.Linq;
using System.Text;
#if UNITY_EDITOR
using UnityEditor;
#endif
using UnityEngine;

/// <summary>
/// Writes values.jsonl in $MATERIALSAVE_OUT (tests/value_probe.py rows): what loaded materials
/// answer for the textures of a shader whose importer gives a non-modifiable texture (_Locked)
/// and a default one (_Other), for references to a GUID no asset has and to a missing object of
/// an existing file, and what saving them writes; a material made by script and created as an
/// asset as the control.
/// </summary>
public class MaterialSaveProbe : MonoBehaviour
{
    const string Variable = "MATERIALSAVE_OUT";
    static readonly string[] Materials = { "NoLocked", "OtherLocked", "MissingProps", "BuiltinMissing", "SetLocked", "Replaced" };
    static readonly string[] Properties = { "_MainTex", "_Other", "_Locked", "_Missing", "_BumpMap" };
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
        new GameObject("MaterialSaveProbe").AddComponent<MaterialSaveProbe>();
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
    static string Name(Texture texture) => texture == null ? "null" : "'" + texture.name + "'";

    static void Textures(string key, Material material)
    {
        foreach (var property in Properties)
            Try(key + "/" + property, () => (material.HasProperty(property) ? "has " : "no ") + Name(material.GetTexture(property)));
        Try(key + "/names", () => string.Join(",", material.GetTexturePropertyNames().OrderBy(n => n, StringComparer.Ordinal)));
    }

    static string File(string name) => System.IO.File.ReadAllText("Assets/Mats/" + name + ".mat").Replace("\r\n", "\n");

    static void Run()
    {
        var tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Mats/Tex.png");
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/Mats/Locked.shader");
        foreach (var name in Materials)
        {
            var material = AssetDatabase.LoadAssetAtPath<Material>("Assets/Mats/" + name + ".mat");
            Textures("loaded/" + name, material);
            if (name == "SetLocked") material.SetTexture("_Locked", tex);
            if (name == "Replaced") material.SetTexture("_MainTex", tex);
            EditorUtility.SetDirty(material);
        }
        Try("save", () => { AssetDatabase.SaveAssets(); return "done"; });
        foreach (var name in Materials)
        {
            Try("file/" + name, () => File(name));
            Textures("saved/" + name, AssetDatabase.LoadAssetAtPath<Material>("Assets/Mats/" + name + ".mat"));
        }

        // A material made by script and created as an asset.
        Try("new/create", () =>
        {
            var made = new Material(shader);
            Textures("new/made", made);
            AssetDatabase.CreateAsset(made, "Assets/Mats/New.mat");
            return "done";
        });
        Try("file/New", () => File("New"));
        Textures("saved/New", AssetDatabase.LoadAssetAtPath<Material>("Assets/Mats/New.mat"));
    }
#endif
}
