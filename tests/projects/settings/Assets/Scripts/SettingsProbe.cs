using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
using UnityEditorInternal;
#endif

/// <summary>
/// The project's settings assets as editor code sees them: what AssetDatabase loads at each
/// <c>ProjectSettings/*.asset</c> path, every property of a SerializedObject over each (the
/// iterator's order, types and values), the singletons <c>Unsupported</c> hands out; then edits
/// through SerializedObjects (an input axis as URP's InputRegistering adds it, a tag, a layer, the
/// product name, the gravity), what the engine makes of them at once, and the files before and
/// after AssetDatabase.SaveAssets. Writes <c>settings.jsonl</c> into <c>SETTINGS_PROBE_OUT</c>,
/// then quits.
/// </summary>
public class SettingsProbe : MonoBehaviour
{
    static readonly StringBuilder s_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("SettingsProbe").AddComponent<SettingsProbe>();

    static string J(string s)
    {
        if (s == null) return "null";
        var b = new StringBuilder("\"");
        foreach (var c in s)
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c == '\n') b.Append("\\n");
            else if (c == '\r') b.Append("\\r");
            else if (c == '\t') b.Append("\\t");
            else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }

    static string F(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string B(bool v) => v ? "true" : "false";
    static void Record(string kind, string body) => s_Out.Append("{\"case\":\"").Append(kind).Append("\",").Append(body).Append("}\n");

    static readonly string[] Files =
    {
        "AudioManager", "ClusterInputManager", "DynamicsManager", "EditorBuildSettings", "EditorSettings",
        "GraphicsSettings", "InputManager", "MemorySettings", "MultiplayerManager", "NavMeshAreas",
        "Physics2DSettings", "PresetManager", "ProjectSettings", "QualitySettings", "TagManager",
        "TimeManager", "UnityConnectSettings", "VFXManager", "VersionControlSettings",
    };

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable("SETTINGS_PROBE_OUT") ?? "/tmp/settings";
        Directory.CreateDirectory(dir);
#if UNITY_EDITOR
        try { Probe(); }
        catch (Exception e) { Record("error", $"\"message\":{J(e.ToString())}"); }
#endif
        File.WriteAllText(Path.Combine(dir, "settings.jsonl"), s_Out.ToString());
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

#if UNITY_EDITOR
    static string Value(SerializedProperty p)
    {
        switch (p.propertyType)
        {
            case SerializedPropertyType.Integer: return p.longValue.ToString(CultureInfo.InvariantCulture);
            case SerializedPropertyType.Boolean: return B(p.boolValue);
            case SerializedPropertyType.Float: return F(p.doubleValue);
            case SerializedPropertyType.String: return J(p.stringValue);
            case SerializedPropertyType.Color: return J(p.colorValue.ToString("R"));
            case SerializedPropertyType.ObjectReference:
                return p.objectReferenceValue == null ? "null" : J(p.objectReferenceValue.GetType().Name + ":" + p.objectReferenceValue.name);
            case SerializedPropertyType.LayerMask: return p.intValue.ToString(CultureInfo.InvariantCulture);
            case SerializedPropertyType.Enum: return p.intValue.ToString(CultureInfo.InvariantCulture);
            case SerializedPropertyType.Vector2: return J(p.vector2Value.ToString("R"));
            case SerializedPropertyType.Vector3: return J(p.vector3Value.ToString("R"));
            case SerializedPropertyType.Vector4: return J(p.vector4Value.ToString("R"));
            case SerializedPropertyType.Rect: return J(p.rectValue.ToString("R"));
            case SerializedPropertyType.ArraySize: return p.intValue.ToString(CultureInfo.InvariantCulture);
            case SerializedPropertyType.Character: return p.intValue.ToString(CultureInfo.InvariantCulture);
            case SerializedPropertyType.AnimationCurve: return (p.animationCurveValue?.length ?? -1).ToString(CultureInfo.InvariantCulture);
            case SerializedPropertyType.Bounds: return J(p.boundsValue.ToString("R"));
            case SerializedPropertyType.Quaternion: return J(p.quaternionValue.ToString("R"));
            case SerializedPropertyType.Vector2Int: return J(p.vector2IntValue.ToString());
            case SerializedPropertyType.Vector3Int: return J(p.vector3IntValue.ToString());
            case SerializedPropertyType.Hash128: return J(p.hash128Value.ToString());
            default: return "null";
        }
    }

    static void Dump(string file, UnityEngine.Object obj)
    {
        var so = new SerializedObject(obj);
        var it = so.GetIterator();
        int n = 0;
        while (it.Next(true))
        {
            Record("property", $"\"file\":{J(file)},\"index\":{n++},\"path\":{J(it.propertyPath)},\"name\":{J(it.name)},\"type\":{(int)it.propertyType},\"typeName\":{J(it.type)}," +
                $"\"depth\":{it.depth},\"isArray\":{B(it.isArray)},\"arraySize\":{(it.isArray ? it.arraySize : -1)},\"hasChildren\":{B(it.hasChildren)},\"editable\":{B(it.editable)},\"value\":{Value(it)}");
        }
        var visible = so.GetIterator();
        int v = 0;
        while (visible.NextVisible(true)) v++;
        Record("object", $"\"file\":{J(file)},\"type\":{J(obj.GetType().FullName)},\"name\":{J(obj.name)},\"hideFlags\":{(int)obj.hideFlags},\"properties\":{n},\"visible\":{v}," +
            $"\"assetPath\":{J(AssetDatabase.GetAssetPath(obj))},\"contains\":{B(AssetDatabase.Contains(obj))},\"isPersistent\":{B(EditorUtility.IsPersistent(obj))}");
    }

    static string Text(string file) => File.ReadAllText(Path.Combine(Application.dataPath, "..", "ProjectSettings", file + ".asset"));

    static void Probe()
    {
        // What each path loads.
        foreach (var file in Files)
        {
            var path = "ProjectSettings/" + file + ".asset";
            var all = AssetDatabase.LoadAllAssetsAtPath(path);
            var main = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
            Record("load", $"\"file\":{J(file)},\"count\":{all.Length},\"types\":[{string.Join(",", all.Select(o => J(o == null ? "null" : o.GetType().FullName)))}]," +
                $"\"names\":[{string.Join(",", all.Select(o => J(o == null ? "null" : o.name)))}],\"main\":{(main == null ? "null" : J(main.GetType().FullName + ":" + main.name))}," +
                $"\"guid\":{J(AssetDatabase.AssetPathToGUID(path))},\"mainType\":{J(AssetDatabase.GetMainAssetTypeAtPath(path)?.FullName)}");
            foreach (var obj in all)
                if (obj != null) Dump(file, obj);
        }

        // The singletons editor code asks for by name.
        foreach (var name in new[] { "InputManager", "TagManager", "PlayerSettings", "NavMeshProjectSettings", "QualitySettings", "PhysicsManager", "TimeManager", "AudioManager", "GraphicsSettings", "EditorSettings", "Physics2DSettings", "NoSuchManager" })
        {
            UnityEngine.Object o = null;
            string error = null;
            try { o = Unsupported.GetSerializedAssetInterfaceSingleton(name); }
            catch (Exception e) { error = e.GetType().Name + ": " + e.Message; }
            var loaded = o == null ? null : AssetDatabase.GetAssetPath(o);
            Record("singleton", $"\"name\":{J(name)},\"type\":{(o == null ? "null" : J(o.GetType().FullName))},\"objectName\":{(o == null ? "null" : J(o.name))},\"assetPath\":{J(loaded)},\"error\":{J(error)}");
        }
        var inputManager = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/InputManager.asset")[0];
        Record("same", $"\"loadedIsSingleton\":{B(inputManager == Unsupported.GetSerializedAssetInterfaceSingleton("InputManager"))},\"loadedTwiceSame\":{B(inputManager == AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/InputManager.asset")[0])}");

        var before = Files.ToDictionary(f => f, Text);

        // An axis, as URP's InputRegistering adds one.
        var input = new SerializedObject(inputManager);
        var axes = input.FindProperty("m_Axes");
        int count = axes.arraySize;
        axes.arraySize = count + 1;
        var axis = axes.GetArrayElementAtIndex(count);
        axis.FindPropertyRelative("m_Name").stringValue = "Unbound Probe Axis";
        axis.FindPropertyRelative("descriptiveName").stringValue = "A probe";
        axis.FindPropertyRelative("descriptiveNegativeName").stringValue = "";
        axis.FindPropertyRelative("negativeButton").stringValue = "page down";
        axis.FindPropertyRelative("positiveButton").stringValue = "page up";
        axis.FindPropertyRelative("altNegativeButton").stringValue = "";
        axis.FindPropertyRelative("altPositiveButton").stringValue = "";
        axis.FindPropertyRelative("gravity").floatValue = 1000f;
        axis.FindPropertyRelative("dead").floatValue = 0.001f;
        axis.FindPropertyRelative("sensitivity").floatValue = 1000f;
        axis.FindPropertyRelative("snap").boolValue = false;
        axis.FindPropertyRelative("invert").boolValue = false;
        axis.FindPropertyRelative("type").intValue = 0;
        axis.FindPropertyRelative("axis").intValue = 0;
        axis.FindPropertyRelative("joyNum").intValue = 0;
        bool applied = input.ApplyModifiedProperties();
        string axisResult;
        try { axisResult = F(Input.GetAxisRaw("Unbound Probe Axis")); }
        catch (Exception e) { axisResult = J(e.GetType().Name + ": " + e.Message); }
        string missingAxis;
        try { missingAxis = F(Input.GetAxisRaw("Unbound Missing Axis")); }
        catch (Exception e) { missingAxis = J(e.GetType().Name + ": " + e.Message); }
        Record("edit-input", $"\"applied\":{B(applied)},\"axes\":{new SerializedObject(inputManager).FindProperty("m_Axes").arraySize},\"axis\":{axisResult},\"missing\":{missingAxis}");

        // A tag and a layer.
        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var tags = tagManager.FindProperty("tags");
        tags.InsertArrayElementAtIndex(tags.arraySize);
        tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = "ProbeTag";
        tagManager.FindProperty("layers").GetArrayElementAtIndex(8).stringValue = "ProbeLayer";
        applied = tagManager.ApplyModifiedProperties();
        var go = new GameObject("Tagged");
        string tagged;
        try { go.tag = "ProbeTag"; tagged = J(go.tag) + ",\"compare\":" + B(go.CompareTag("ProbeTag")); }
        catch (Exception e) { tagged = J(e.GetType().Name + ": " + e.Message) + ",\"compare\":false"; }
        Record("edit-tags", $"\"applied\":{B(applied)},\"tags\":[{string.Join(",", InternalEditorUtility.tags.Select(J))}],\"layers\":[{string.Join(",", InternalEditorUtility.layers.Select(J))}]," +
            $"\"nameToLayer\":{LayerMask.NameToLayer("ProbeLayer")},\"layerToName\":{J(LayerMask.LayerToName(8))},\"tag\":{tagged}");

        // The product name, through PlayerSettings' own asset.
        var player = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
        player.FindProperty("productName").stringValue = "Probe Product";
        applied = player.ApplyModifiedProperties();
        Record("edit-player", $"\"applied\":{B(applied)},\"playerSettings\":{J(PlayerSettings.productName)},\"application\":{J(Application.productName)}");

        // The gravity.
        var physics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset")[0]);
        physics.FindProperty("m_Gravity").vector3Value = new Vector3(0, -5, 0);
        applied = physics.ApplyModifiedProperties();
        Record("edit-physics", $"\"applied\":{B(applied)},\"gravity\":{J(Physics.gravity.ToString("R"))}");

        // The files: after the edits, then saved.
        foreach (var file in Files)
        {
            var now = Text(file);
            if (now != before[file]) Record("file-applied", $"\"file\":{J(file)},\"text\":{J(now)}");
        }
        AssetDatabase.SaveAssets();
        foreach (var file in Files)
        {
            var now = Text(file);
            if (now != before[file]) Record("file-saved", $"\"file\":{J(file)},\"text\":{J(now)}");
        }
    }
#endif
}
