using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Records, in Play mode, what AssetDatabase.OpenAsset and CanOpenAssetInEditor do with the
/// [OnOpenAsset] methods of OpenAssetCallbacks: which are called, in which order and with which
/// arguments, where the calls stop, the answers and the selection afterwards (a scene object
/// named Selected is selected before every call). An asset of another format than the Editor's
/// own is only ever opened with a method of an order below the default answering true, so no
/// application outside the Editor starts.
/// </summary>
static class OpenAssetProbe
{
    static readonly StringBuilder s_Output = new StringBuilder();
    const int NoObject = 2000000001;
    const string ShapeReport = "does not match any of [OnOpenAssetAttribute] expected signatures";
    /// <summary>The selection before every call.</summary>
    static GameObject s_Selected;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("OPEN_ASSET_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        Directory.CreateDirectory(directory);

        var marker = new GameObject("Marker");
        var other = new GameObject("Other");
        s_Selected = new GameObject("Selected");
        var memory = ScriptableObject.CreateInstance<ProbeData>();
        memory.name = "Memory";
        OpenAssetCallbacks.Names[NoObject] = "<no object>";

        // Objects that are no assets: nothing outside the Editor can open them.
        Open("open.scene.none", () => AssetDatabase.OpenAsset(marker.GetInstanceID()));
        Open("open.scene.line", () => AssetDatabase.OpenAsset(marker.GetInstanceID(), 7));
        Open("open.scene.lineColumn", () => AssetDatabase.OpenAsset(marker.GetInstanceID(), 7, 3));
        Open("open.scene.object", () => AssetDatabase.OpenAsset(marker));
        Open("open.scene.objectLine", () => AssetDatabase.OpenAsset(marker, 7));
        Open("open.scene.objectLineColumn", () => AssetDatabase.OpenAsset(marker, 7, 3));
        Open("open.noObject.none", () => AssetDatabase.OpenAsset(NoObject));
        Open("open.zero.none", () => AssetDatabase.OpenAsset(0));
        Open("open.memory.none", () => AssetDatabase.OpenAsset(memory));
        foreach (var name in new[] { "Negative", "Zero", "Default", "Three", "Hundred", "Validate", "ValidateEarly",
                     "WrongReturn", "WrongParameter", "NoParameters", "FourParameters", "ValidateTwoParameters", "Instance" })
            Open("open.scene.handledBy." + name, () => AssetDatabase.OpenAsset(marker.GetInstanceID(), 7, 3), handle: new[] { name });
        Open("open.scene.handledByTwo", () => AssetDatabase.OpenAsset(marker.GetInstanceID()), handle: new[] { "Zero", "Three" });
        foreach (var name in new[] { "Negative", "Zero", "Hundred", "Validate" })
            Open("open.scene.throws." + name, () => AssetDatabase.OpenAsset(marker.GetInstanceID()), throws: name);
        Open("open.scene.throwsThenHandled", () => AssetDatabase.OpenAsset(marker.GetInstanceID()), handle: new[] { "Three" }, throws: "Zero");
        Open("open.noObject.handled", () => AssetDatabase.OpenAsset(NoObject), handle: new[] { "Zero" });
        Open("open.zero.handled", () => AssetDatabase.OpenAsset(0), handle: new[] { "Zero" });
        Open("open.memory.handled", () => AssetDatabase.OpenAsset(memory), handle: new[] { "Zero" });

        Open("open.object.null", () => AssetDatabase.OpenAsset((Object)null), handle: new[] { "Zero" });
        Open("open.object.nullLine", () => AssetDatabase.OpenAsset((Object)null, 7, 3), handle: new[] { "Zero" });
        var destroyed = new GameObject("Destroyed");
        Object.DestroyImmediate(destroyed);
        Open("open.object.destroyed", () => AssetDatabase.OpenAsset(destroyed), handle: new[] { "Zero" });
        Open("open.array.allHandled", () => AssetDatabase.OpenAsset(new Object[] { marker, other }), handle: new[] { "Zero" });
        Open("open.array.withNull", () => AssetDatabase.OpenAsset(new Object[] { marker, null, other }), handle: new[] { "Zero" });
        Open("open.array.noneHandled", () => AssetDatabase.OpenAsset(new Object[] { marker, other }));
        Open("open.array.empty", () => AssetDatabase.OpenAsset(new Object[0]));
        Open("open.array.null", () => AssetDatabase.OpenAsset((Object[])null));

        // Assets, always handled before the default order.
        var paths = AssetPaths();
        foreach (var path in paths)
        {
            var main = AssetDatabase.LoadMainAssetAtPath(path);
            if (main == null) { Check("open.asset:" + path, () => "<no main asset>"); continue; }
            Open("open.asset:" + path, () => AssetDatabase.OpenAsset(main, 7, 3), handle: new[] { "Zero" });
        }
        var sprite = AssetDatabase.LoadAllAssetsAtPath(SpritePath(paths)).OfType<Sprite>().OrderBy(s => s.name).FirstOrDefault();
        Open("open.subAsset", () => AssetDatabase.OpenAsset(sprite, 7, 3), handle: new[] { "Negative" });
        var text = AssetDatabase.LoadMainAssetAtPath("Assets/Data/Plain.txt");
        var material = AssetDatabase.LoadMainAssetAtPath("Assets/Native/Plain.mat");
        Open("open.text.defaults", () => AssetDatabase.OpenAsset(text.GetInstanceID()), handle: new[] { "MinusThree" });
        Open("open.text.line", () => AssetDatabase.OpenAsset(text.GetInstanceID(), 7), handle: new[] { "MinusThree" });
        Open("open.text.objectDefaults", () => AssetDatabase.OpenAsset(text), handle: new[] { "MinusThree" });
        Open("open.text.objectLine", () => AssetDatabase.OpenAsset(text, 7), handle: new[] { "MinusThree" });
        Open("open.text.handledByTwo", () => AssetDatabase.OpenAsset(text), handle: new[] { "Negative", "Zero" });
        Open("open.text.throwsThenHandled", () => AssetDatabase.OpenAsset(text), handle: new[] { "Zero" }, throws: "Negative");
        Open("open.text.throwsAndAnswers", () => AssetDatabase.OpenAsset(text), handle: new[] { "Negative", "Zero" }, throws: "Negative");
        Open("open.text.validatorsAnswer", () => AssetDatabase.OpenAsset(text), handle: new[] { "ValidateEarly", "Validate", "ValidateTwoParameters", "Zero" });
        Open("open.text.wrongShapesAnswer", () => AssetDatabase.OpenAsset(text),
            handle: new[] { "WrongReturn", "WrongParameter", "NoParameters", "FourParameters", "Instance", "ValidateWrongReturn", "Zero" });
        Open("open.array.assets", () => AssetDatabase.OpenAsset(new Object[] { text, material }), handle: new[] { "Zero" });
        Open("open.array.assetsWithNull", () => AssetDatabase.OpenAsset(new Object[] { text, null, material, marker }), handle: new[] { "Zero" });
        // Assets of the Editor's own format that no method opens: nothing outside the Editor opens them either.
        foreach (var path in new[] { "Assets/Native/Plain.mat", "Assets/Native/Data.asset", "Assets/Native/Bouncy.physicMaterial", "Assets/Native" })
        {
            var p = path;
            Open("open.unhandled:" + p, () => AssetDatabase.OpenAsset(AssetDatabase.LoadMainAssetAtPath(p)));
        }
        Open("open.unhandled.throws", () => AssetDatabase.OpenAsset(material), throws: "Hundred");

        // CanOpenAssetInEditor: every asset with no validator answering, then with one answering.
        var targets = new List<KeyValuePair<string, Func<int>>>
        {
            new KeyValuePair<string, Func<int>>("scene", () => marker.GetInstanceID()),
            new KeyValuePair<string, Func<int>>("noObject", () => NoObject),
            new KeyValuePair<string, Func<int>>("zero", () => 0),
            new KeyValuePair<string, Func<int>>("memory", () => memory.GetInstanceID()),
            new KeyValuePair<string, Func<int>>("subAsset", () => sprite.GetInstanceID()),
            new KeyValuePair<string, Func<int>>("nativeSubAsset", () => Sub<UnityEditor.Animations.AnimatorStateMachine>("Assets/Native/Controller.controller").GetInstanceID()),
            new KeyValuePair<string, Func<int>>("modelMesh", () => Sub<Mesh>("Assets/Models/Lods.fbx").GetInstanceID()),
            new KeyValuePair<string, Func<int>>("modelPart", () => PrefabPart("Assets/Models/Lods.fbx").GetInstanceID()),
        };
        foreach (var path in paths)
        {
            var p = path;
            targets.Add(new KeyValuePair<string, Func<int>>(p, () => AssetDatabase.LoadMainAssetAtPath(p).GetInstanceID()));
        }
        foreach (var target in targets)
        {
            Open("can.none:" + target.Key, () => AssetDatabase.CanOpenAssetInEditor(target.Value()));
            Open("can.validate:" + target.Key, () => AssetDatabase.CanOpenAssetInEditor(target.Value()), handle: new[] { "Validate" });
        }
        Func<bool> canText = () => AssetDatabase.CanOpenAssetInEditor(text.GetInstanceID());
        Open("can.text.validateEarly", canText, handle: new[] { "ValidateEarly" });
        Open("can.text.both", canText, handle: new[] { "ValidateEarly", "Validate" });
        Open("can.text.executeAnswers", canText, handle: new[] { "Negative", "Zero", "Default", "Three", "Hundred" });
        Open("can.text.wrongShapesAnswer", canText, handle: new[] { "WrongReturn", "WrongParameter", "NoParameters", "FourParameters", "ValidateTwoParameters", "Instance" });
        Open("can.text.wrongValidatorAnswers", canText, handle: new[] { "ValidateWrongReturn" });
        Open("can.material.validateThrows", () => AssetDatabase.CanOpenAssetInEditor(material.GetInstanceID()), throws: "Validate");
        Open("can.text.validateEarlyThrows", canText, handle: new[] { "Validate" }, throws: "ValidateEarly");
        Open("can.text.validateThrows", canText, throws: "Validate");

        s_Output.Append("{\"k\":\"complete\",\"v\":true}\n");
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), s_Output.ToString());
        EditorApplication.Exit(0);
    }

    static string[] AssetPaths() =>
        AssetDatabase.FindAssets("", new[] { "Assets/Native", "Assets/Data", "Assets/Sprites", "Assets/Scenes", "Assets/Scripts", "Assets/OpenAsset", "Assets/Models" })
            .Select(AssetDatabase.GUIDToAssetPath).Concat(new[] { "Assets", "Assets/Native" }).Distinct().OrderBy(p => p, StringComparer.Ordinal).ToArray();

    static string SpritePath(string[] paths) => paths.First(p => AssetDatabase.LoadAllAssetsAtPath(p).OfType<Sprite>().Any());

    static T Sub<T>(string path) where T : Object =>
        AssetDatabase.LoadAllAssetsAtPath(path).OfType<T>().OrderBy(o => o.name, StringComparer.Ordinal).First();

    /// <summary>A GameObject of the asset that is not its main object (the first by name).</summary>
    static GameObject PrefabPart(string path)
    {
        var main = AssetDatabase.LoadMainAssetAtPath(path);
        return AssetDatabase.LoadAllAssetsAtPath(path).OfType<GameObject>().Where(g => g != main).OrderBy(g => g.name, StringComparer.Ordinal).First();
    }

    static void Open(string key, Func<bool> call, string[] handle = null, string throws = null)
    {
        Check(key, () =>
        {
            OpenAssetCallbacks.Reset();
            if (handle != null) foreach (var name in handle) OpenAssetCallbacks.Handle.Add(name);
            OpenAssetCallbacks.Throw = throws;
            Selection.activeObject = s_Selected;
            var logs = new List<string>();
            Application.LogCallback handler = (condition, stackTrace, type) => logs.Add(type + ":" + condition.Replace("\n", "\\n"));
            Application.logMessageReceived += handler;
            string result;
            try { result = call().ToString(); }
            catch (Exception e) { result = "throws " + e.GetType().Name; }
            finally { Application.logMessageReceived -= handler; }
            var selection = Selection.activeObject;
            // The reports of methods of another shape come class by class in an order that differs
            // between Editor runs: they are written sorted, apart from the other messages.
            var shapes = logs.Where(l => l.Contains(ShapeReport)).OrderBy(l => l, StringComparer.Ordinal).ToList();
            logs.RemoveAll(l => l.Contains(ShapeReport));
            return "result=" + result + "|selection=" + (selection == null ? "<null>" : OpenAssetCallbacks.Describe(selection.GetInstanceID()))
                + "|shapeReports=" + (shapes.Count == 0 ? "<none>" : string.Join("||", shapes))
                + "|logs=" + (logs.Count == 0 ? "<none>" : string.Join("||", logs))
                + "|calls=" + (OpenAssetCallbacks.Calls.Count == 0 ? "<none>" : string.Join(" ", OpenAssetCallbacks.Calls));
        });
    }

    static void Check(string key, Func<string> read)
    {
        string value;
        try { value = read(); }
        catch (Exception e) { value = "throws " + e.GetType().Name + ":" + e.Message; }
        s_Output.Append("{\"k\":").Append(Quote(key)).Append(",\"v\":").Append(Quote(value)).Append("}\n");
    }

    static string Quote(string s)
    {
        var sb = new StringBuilder(s.Length + 2).Append('"');
        foreach (char c in s)
        {
            switch (c)
            {
                case '\\': sb.Append("\\\\"); break;
                case '"': sb.Append("\\\""); break;
                case '\n': sb.Append("\\n"); break;
                case '\r': sb.Append("\\r"); break;
                case '\t': sb.Append("\\t"); break;
                default: sb.Append(c); break;
            }
        }
        return sb.Append('"').ToString();
    }
}
