using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// `-executeMethod ProbeRunner.Create` saves the empty probe scene; `-executeMethod ProbeRunner.Record`
/// with <c>SETTINGS_PROBE_OUT</c> set plays it, where <see cref="SettingsProbe"/> runs (edits made in
/// Play mode are not saved). `-executeMethod ProbeRunner.Save` makes the same edits in Edit mode,
/// saves them, copies the files they changed into <c>Reference/saved</c> and puts the originals back.
/// `-executeMethod ProbeRunner.Schema` writes the settings classes' type trees, as SerializedObjects
/// iterate them, into <c>Reference/schema.jsonl</c>: every empty array gets an element (not applied)
/// so its element's tree and new elements' values are there too (arrays are emptied first, so the
/// values are a new element's, not the project's).
/// </summary>
public static class ProbeRunner
{
    public static void Create()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }

    public static void Save()
    {
        var files = Directory.GetFiles("ProjectSettings", "*.asset");
        var before = files.ToDictionary(f => f, File.ReadAllText);

        var input = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/InputManager.asset")[0]);
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
        input.ApplyModifiedProperties();

        var tagManager = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/TagManager.asset")[0]);
        var tags = tagManager.FindProperty("tags");
        tags.InsertArrayElementAtIndex(tags.arraySize);
        tags.GetArrayElementAtIndex(tags.arraySize - 1).stringValue = "ProbeTag";
        tagManager.FindProperty("layers").GetArrayElementAtIndex(8).stringValue = "ProbeLayer";
        tagManager.ApplyModifiedProperties();

        var player = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/ProjectSettings.asset")[0]);
        player.FindProperty("productName").stringValue = "Probe Product";
        player.ApplyModifiedProperties();

        var physics = new SerializedObject(AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/DynamicsManager.asset")[0]);
        physics.FindProperty("m_Gravity").vector3Value = new Vector3(0, -5, 0);
        physics.ApplyModifiedProperties();

        AssetDatabase.SaveAssets();
        Directory.CreateDirectory("Reference/saved");
        foreach (var file in files)
        {
            var now = File.ReadAllText(file);
            if (now == before[file]) continue;
            File.WriteAllText(Path.Combine("Reference/saved", Path.GetFileName(file)), now);
            File.WriteAllText(file, before[file]);
        }
    }

    static string J(string v) => v == null ? "null" : "\"" + v.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

    static string Value(SerializedProperty p)
    {
        switch (p.propertyType)
        {
            case SerializedPropertyType.Integer: return p.longValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
            case SerializedPropertyType.Boolean: return p.boolValue ? "true" : "false";
            case SerializedPropertyType.Float: return p.doubleValue.ToString("R", System.Globalization.CultureInfo.InvariantCulture);
            case SerializedPropertyType.String: return J(p.stringValue);
            case SerializedPropertyType.Enum: case SerializedPropertyType.LayerMask: case SerializedPropertyType.Character: case SerializedPropertyType.ArraySize:
                return p.intValue.ToString(System.Globalization.CultureInfo.InvariantCulture);
            default: return "null";
        }
    }

    public static void Schema()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var path in Directory.GetFiles("ProjectSettings", "*.asset").OrderBy(f => f))
        {
            foreach (var obj in AssetDatabase.LoadAllAssetsAtPath(path.Replace('\\', '/')))
            {
                if (obj == null) continue;
                var so = new SerializedObject(obj);
                // Every array emptied, then given one new element: the values are a new element's.
                var arrays = new System.Collections.Generic.List<string>();
                var all = so.GetIterator();
                while (all.Next(all.propertyType != SerializedPropertyType.String))
                    if (all.isArray && all.propertyType != SerializedPropertyType.String && all.name != "Array") arrays.Add(all.propertyPath);
                foreach (var a in arrays.Where(a => !arrays.Any(o => o != a && a.StartsWith(o + ".", System.StringComparison.Ordinal))))
                    so.FindProperty(a).arraySize = 0;
                for (int pass = 0; pass < 6; pass++)
                {
                    var empty = new System.Collections.Generic.List<string>();
                    var it = so.GetIterator();
                    while (it.Next(true))
                        if (it.isArray && it.propertyType != SerializedPropertyType.String && it.name != "Array" && it.arraySize == 0) empty.Add(it.propertyPath);
                    if (empty.Count == 0) break;
                    foreach (var p in empty) so.FindProperty(p)?.InsertArrayElementAtIndex(0);
                }
                var visible = new System.Collections.Generic.HashSet<string>();
                var vis = so.GetIterator();
                while (vis.NextVisible(true))
                    visible.Add(System.Text.RegularExpressions.Regex.Replace(vis.propertyPath, @"data\[[0-9]+\]", "data[0]"));
                var iter = so.GetIterator();
                bool enter = true;
                while (iter.Next(enter))
                {
                    enter = iter.propertyType != SerializedPropertyType.String;
                    var p = iter.propertyPath;
                    if (System.Text.RegularExpressions.Regex.IsMatch(p, @"data\[[1-9][0-9]*\]")) { enter = false; continue; }
                    sb.Append("{\"file\":").Append(J(Path.GetFileNameWithoutExtension(path))).Append(",\"class\":").Append(J(obj.GetType().FullName))
                      .Append(",\"path\":").Append(J(p)).Append(",\"name\":").Append(J(iter.name)).Append(",\"type\":").Append((int)iter.propertyType)
                      .Append(",\"typeName\":").Append(J(iter.type)).Append(",\"depth\":").Append(iter.depth).Append(",\"isArray\":").Append(iter.isArray ? "true" : "false")
                      .Append(",\"editable\":").Append(iter.editable ? "true" : "false").Append(",\"visible\":").Append(visible.Contains(p) ? "true" : "false").Append(",\"value\":").Append(Value(iter)).Append("}\n");
                }
            }
        }
        File.WriteAllText("Reference/schema.jsonl", sb.ToString());
    }
}
