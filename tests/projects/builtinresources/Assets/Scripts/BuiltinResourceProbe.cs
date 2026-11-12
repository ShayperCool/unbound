using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Writes what Resources.GetBuiltinResource and AssetDatabase.GetBuiltinExtraResource answer for
/// every path of Resources/BuiltinPaths.txt (`R` or `E`, the path, the requested type) and for a
/// few paths and types outside it, with the messages each call logs, into values.jsonl in
/// $BUILTIN_RESOURCES_OUT (tests/value_probe.py).
/// </summary>
public static class BuiltinResourceProbe
{
    static StreamWriter output;
    static readonly List<string> s_Logs = new List<string>();

    static string Escape(string s)
    {
        var b = new StringBuilder();
        foreach (var c in s)
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c == '\n') b.Append("\\n");
            else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.ToString();
    }

    static void Row(string key, string value) =>
        output.Write("{\"k\":\"" + Escape(key) + "\",\"v\":\"" + Escape(value) + "\"}\n");

    static string F(float f) => f.ToString("0.####", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => F(v.x) + "," + F(v.y) + "," + F(v.z);

    static Type Find(string name)
    {
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            var type = assembly.GetType("UnityEngine." + name) ?? assembly.GetType("UnityEditor." + name);
            if (type != null) return type;
        }
        throw new ArgumentException("no type " + name);
    }

    /// <summary>The object's class, built-in file and file id, name, and a few facts of its content.</summary>
    static string Describe(Object o)
    {
        if (ReferenceEquals(o, null)) return "null";
        if (o == null) return "destroyed";
        AssetDatabase.TryGetGUIDAndLocalFileIdentifier(o, out string guid, out long id);
        string file = guid == "0000000000000000e000000000000000" ? "default" : guid == "0000000000000000f000000000000000" ? "extra" : guid;
        var s = o.GetType().Name + " " + file + " " + id + " '" + o.name + "'";
        switch (o)
        {
            case Mesh mesh:
                s += " vertices " + mesh.vertexCount + " submeshes " + mesh.subMeshCount + " indices " + mesh.GetIndexCount(0)
                    + " bounds " + V(mesh.bounds.center) + " " + V(mesh.bounds.size) + " readable " + mesh.isReadable;
                break;
            case Texture2D texture:
                s += " " + texture.width + "x" + texture.height + " " + texture.format + " mips " + texture.mipmapCount
                    + " wrap " + texture.wrapMode + " filter " + texture.filterMode + " readable " + texture.isReadable;
                break;
            case Cubemap cube:
                s += " " + cube.width + " " + cube.format + " mips " + cube.mipmapCount;
                break;
            case Material material:
                s += " shader '" + (material.shader != null ? material.shader.name : "null") + "' queue " + material.renderQueue;
                break;
            case Shader shader:
                s += " shader '" + shader.name + "'";
                break;
            case Font font:
                s += " size " + font.fontSize + " dynamic " + font.dynamic;
                break;
            case Sprite sprite:
                s += " rect " + F(sprite.rect.x) + "," + F(sprite.rect.y) + "," + F(sprite.rect.width) + "," + F(sprite.rect.height)
                    + " border " + F(sprite.border.x) + "," + F(sprite.border.y) + "," + F(sprite.border.z) + "," + F(sprite.border.w)
                    + " ppu " + F(sprite.pixelsPerUnit) + " texture '" + (sprite.texture != null ? sprite.texture.name : "null") + "'";
                break;
            case TextAsset text:
                s += " length " + text.text.Length;
                break;
        }
        return s;
    }

    /// <summary>One call: its answer (or exception) and the messages it logged.</summary>
    static string Call(Func<Object> call)
    {
        s_Logs.Clear();
        string answer;
        try { answer = Describe(call()); }
        catch (Exception e) { answer = "throws " + e.GetType().Name + ": " + e.Message; }
        return s_Logs.Count == 0 ? answer : answer + " | " + string.Join(" | ", s_Logs);
    }

    static void OnLog(string message, string stackTrace, LogType type)
    {
        if (output != null) s_Logs.Add(type + ": " + message);
    }

    static Object Get(string manager, Type type, string path) =>
        manager == "R" ? Resources.GetBuiltinResource(type, path) : AssetDatabase.GetBuiltinExtraResource(type, path);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        string dir = Environment.GetEnvironmentVariable("BUILTIN_RESOURCES_OUT");
        if (string.IsNullOrEmpty(dir)) return;
        try
        {
            Directory.CreateDirectory(dir);
            Application.logMessageReceived += OnLog;
            using (output = new StreamWriter(Path.Combine(dir, "values.jsonl")))
            {
                var list = Resources.Load<TextAsset>("BuiltinPaths").text;
                foreach (var line in list.Split('\n'))
                {
                    if (line.Length == 0) continue;
                    var parts = line.Split('\t');
                    var type = Find(parts[2]);
                    Row(parts[0] + "/" + parts[1] + "/" + parts[2], Call(() => Get(parts[0], type, parts[1])));
                }

                // Names, types and managers outside the registrations.
                Row("case/pyramid.FBX", Call(() => Resources.GetBuiltinResource<Mesh>("pyramid.FBX")));
                Row("missing/Nope.fbx", Call(() => Resources.GetBuiltinResource<Mesh>("Nope.fbx")));
                Row("base/Cube.fbx/Object", Call(() => Resources.GetBuiltinResource(typeof(Object), "Cube.fbx")));
                Row("other/Cube.fbx/Texture2D", Call(() => Resources.GetBuiltinResource<Texture2D>("Cube.fbx")));
                Row("arial/Font", Call(() => Resources.GetBuiltinResource<Font>("Arial.ttf")));
                Row("arial/Material", Call(() => Resources.GetBuiltinResource<Material>("Arial.ttf")));
                Row("empty/Mesh", Call(() => Resources.GetBuiltinResource<Mesh>("")));
                Row("null/Mesh", Call(() => Resources.GetBuiltinResource<Mesh>(null)));
                Row("skin/MonoBehaviour", Call(() => Resources.GetBuiltinResource<MonoBehaviour>("GameSkin/GameSkin.guiskin")));
                Row("skin/ScriptableObject", Call(() => Resources.GetBuiltinResource<ScriptableObject>("GameSkin/GameSkin.guiskin")));
                Row("crossed/R/Default-Material.mat", Call(() => Resources.GetBuiltinResource<Material>("Default-Material.mat")));
                Row("crossed/E/Cube.fbx", Call(() => AssetDatabase.GetBuiltinExtraResource<Mesh>("Cube.fbx")));
                Row("case/E/default-material.mat", Call(() => AssetDatabase.GetBuiltinExtraResource<Material>("default-material.mat")));
                Row("base/E/Default-Material.mat/Object", Call(() => AssetDatabase.GetBuiltinExtraResource(typeof(Object), "Default-Material.mat")));
                Row("missing/E/Nope.mat", Call(() => AssetDatabase.GetBuiltinExtraResource<Material>("Nope.mat")));
                Row("arial/E/Font", Call(() => AssetDatabase.GetBuiltinExtraResource<Font>("Arial.ttf")));

                // The same object on every call; the primitives' meshes.
                Row("same/pyramid.fbx", (Resources.GetBuiltinResource<Mesh>("pyramid.fbx") == Resources.GetBuiltinResource(typeof(Mesh), "pyramid.fbx")).ToString());
                Row("same/E/Default-Material.mat", (AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat") == AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat")).ToString());
                foreach (PrimitiveType primitive in Enum.GetValues(typeof(PrimitiveType)))
                {
                    var go = GameObject.CreatePrimitive(primitive);
                    var mesh = go.GetComponent<MeshFilter>().sharedMesh;
                    Row("primitive/" + primitive, Describe(mesh));
                    Object.DestroyImmediate(go);
                }
                output.Write("{\"k\":\"complete\",\"v\":true}\n");
            }
            output = null;
            Application.logMessageReceived -= OnLog;
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { output = null; Debug.LogException(exception); EditorApplication.Exit(1); }
    }
}
