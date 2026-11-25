using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes values.jsonl in $IMPORTERMETA_OUT (tests/value_probe.py rows): the .meta files of
/// textures, audio clips and models whose importer settings are in the release's own layout or an
/// older one (keys missing, keys the release does not have, keys out of order, an older platform
/// name, an older serialized version), as the project opened with them and after a settings change
/// is saved (SaveAndReimport, WriteImportSettingsIfDirty), after a save without a change, a forced
/// import, and with nothing done. GUID lines read "guid: (guid)"; the per-platform entries of the
/// installation's platform modules are left out of a text (`.platforms` says whether they were the
/// installed ones).
/// </summary>
public class ImporterMetaProbe : MonoBehaviour
{
    const string Variable = "IMPORTERMETA_OUT";
    static StreamWriter output;
    static readonly List<string> logs = new List<string>();

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");

    static readonly Regex s_Guid = new Regex("(?m)^guid: [0-9a-f]{32}$");

    // The same list and entry pattern as tests/projects/projectwrites (ProjectWritesProbe.cs), the
    // entries limited to the installed modules' names; keep them in step.
    static IEnumerable<string> Installed()
    {
        var platforms = new (BuildTargetGroup group, BuildTarget target, string name)[]
        {
            (BuildTargetGroup.Android, BuildTarget.Android, "Android"), (BuildTargetGroup.iOS, BuildTarget.iOS, "iOS"),
            (BuildTargetGroup.WebGL, BuildTarget.WebGL, "WebGL"), (BuildTargetGroup.WSA, BuildTarget.WSAPlayer, "Windows Store Apps"),
            (BuildTargetGroup.tvOS, BuildTarget.tvOS, "tvOS"), (BuildTargetGroup.VisionOS, BuildTarget.VisionOS, "VisionOS"),
        };
        return platforms.Where(p => BuildPipeline.IsBuildTargetSupported(p.group, p.target)).Select(p => p.name);
    }

    static Regex s_InstalledEntry;

    /// <summary>A .meta's text with its GUID and the installed modules' platform entries left out.</summary>
    static void Meta(string key, string path)
    {
        var meta = path + ".meta";
        if (!File.Exists(meta)) { Row(key, "<none>"); return; }
        var text = File.ReadAllText(meta);
        var removed = new List<string>();
        text = s_InstalledEntry.Replace(text, m => { removed.Add(m.Groups[2].Value); return ""; });
        Row(key, s_Guid.Replace(text, "guid: (guid)"));
        if (text.Contains("platformSettings:"))
            Row(key + ".platforms", string.Join(",", removed) == string.Join(",", Installed()) ? "the installed modules" : "differ: " + string.Join(",", removed));
    }

    static void Try(string key, Func<string> action)
    {
        logs.Clear();
        string result;
        try { result = action(); }
        catch (Exception e) { result = "throws " + e.GetType().FullName + ": " + e.Message; }
        Row(key, result);
        if (logs.Count > 0) Row(key + ".logs", string.Join("\n---\n", logs));
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("ImporterMetaProbe").AddComponent<ImporterMetaProbe>();
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable(Variable);
        Directory.CreateDirectory(dir);
        var names = string.Join("|", Installed().Select(Regex.Escape));
        s_InstalledEntry = new Regex("(?m)^( *)- serializedVersion: [0-9]+\n\\1  buildTarget: (" + (names.Length > 0 ? names : "(?!)") + ")\n(?:\\1  .*\n)*");
        Application.logMessageReceived += (message, stack, type) => logs.Add(type + ": " + message);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));
        try { Run(); }
        catch (Exception e) { Row("aborted", e.GetType().FullName + ": " + e.Message); }
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Dispose();
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#endif
    }

    static readonly string[] Textures = { "Current", "Missing", "Extra", "Reordered", "SwitchName", "Version12", "Untouched", "Reimported", "SameValue", "WriteIfDirty" };

    static void Run()
    {
        var all = Textures.Select(n => "Assets/Textures/" + n + ".png")
            .Concat(new[] { "Assets/Audio/Current.wav", "Assets/Audio/Missing.wav", "Assets/Models/Current.obj", "Assets/Models/Missing.obj" }).ToArray();
        // What opening the project left.
        foreach (var path in all) Meta("open/" + path, path);

        foreach (var name in new[] { "Current", "Missing", "Extra", "Reordered", "SwitchName", "Version12" })
        {
            string path = "Assets/Textures/" + name + ".png";
            Try("change/" + path, () =>
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(path);
                var settings = new TextureImporterSettings();
                importer.ReadTextureSettings(settings);
                string before = "readable " + importer.isReadable + " mipmapsPreserveCoverage " + importer.mipMapsPreserveCoverage
                    + " flipbook " + settings.flipbookRows + "x" + settings.flipbookColumns;
                importer.isReadable = true;
                importer.SaveAndReimport();
                return before;
            });
            Meta("change/" + path + "/meta", path);
        }
        Try("forced/Assets/Textures/Reimported.png", () =>
        {
            AssetDatabase.ImportAsset("Assets/Textures/Reimported.png", ImportAssetOptions.ForceUpdate);
            return "imported";
        });
        Meta("forced/Assets/Textures/Reimported.png/meta", "Assets/Textures/Reimported.png");
        Try("same/Assets/Textures/SameValue.png", () =>
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath("Assets/Textures/SameValue.png");
            importer.isReadable = importer.isReadable;
            importer.SaveAndReimport();
            return "saved";
        });
        Meta("same/Assets/Textures/SameValue.png/meta", "Assets/Textures/SameValue.png");
        Try("written/Assets/Textures/WriteIfDirty.png", () =>
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath("Assets/Textures/WriteIfDirty.png");
            importer.isReadable = true;
            return "written " + AssetDatabase.WriteImportSettingsIfDirty("Assets/Textures/WriteIfDirty.png");
        });
        Meta("written/Assets/Textures/WriteIfDirty.png/meta", "Assets/Textures/WriteIfDirty.png");
        foreach (var name in new[] { "Current", "Missing" })
        {
            string audio = "Assets/Audio/" + name + ".wav";
            Try("change/" + audio, () =>
            {
                var importer = (AudioImporter)AssetImporter.GetAtPath(audio);
                string before = "mono " + importer.forceToMono + " background " + importer.loadInBackground + " preload " + importer.defaultSampleSettings.preloadAudioData;
                importer.forceToMono = true;
                importer.SaveAndReimport();
                return before;
            });
            Meta("change/" + audio + "/meta", audio);
            string model = "Assets/Models/" + name + ".obj";
            Try("change/" + model, () =>
            {
                var importer = (ModelImporter)AssetImporter.GetAtPath(model);
                string before = "scale " + importer.globalScale + " constraints " + importer.importConstraints + " removeConstantScaleCurves " + importer.removeConstantScaleCurves;
                importer.globalScale = 2;
                importer.SaveAndReimport();
                return before;
            });
            Meta("change/" + model + "/meta", model);
        }
        Meta("end/Assets/Textures/Untouched.png", "Assets/Textures/Untouched.png");
    }
}
