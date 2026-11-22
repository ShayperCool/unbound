using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// What ObjectFactory makes and what it changes besides: names, flags, components (required ones
/// included), Reset, the scene's dirty state, the Undo group, componentWasAdded, the log, primitives,
/// PlaceGameObject. With <c>OBJECTFACTORY_PROBE_OUT</c> set, it records in Play mode, in the loaded
/// probe scene, and quits.
/// </summary>
public static class ObjectFactoryProbe
{
    static readonly StringBuilder s_Rows = new StringBuilder();
    static readonly List<string> s_Log = new List<string>();
    static readonly List<string> s_Added = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        var output = Environment.GetEnvironmentVariable("OBJECTFACTORY_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) return;
        try { Record(); }
        catch (Exception e) { Row("fatal", e.GetType().Name + ": " + e.Message); }
        s_Rows.Append("{\"k\":\"complete\",\"v\":true}\n");
        Directory.CreateDirectory(output);
        File.WriteAllText(Path.Combine(output, "values.jsonl"), s_Rows.ToString());
        EditorApplication.Exit(0);
    }

    static void Record()
    {
        var scene = UnityEngine.SceneManagement.SceneManager.GetActiveScene();
        Row("scene.dirty.open", scene.isDirty.ToString());
        Application.logMessageReceived += (message, _, type) => s_Log.Add(type + ": " + message);
        ObjectFactory.componentWasAdded += c => s_Added.Add(c.GetType().Name + "@" + c.gameObject.name);
        Undo.IncrementCurrentGroup();
        int group = Undo.GetCurrentGroup();

        var a = ObjectFactory.CreateGameObject("A");
        Object("a", a);
        Row("a.undoGroupName", Undo.GetCurrentGroupName());
        Row("a.undoGroupMoved", (Undo.GetCurrentGroup() != group).ToString());
        Row("a.sceneDirty", scene.isDirty.ToString());
        Events("a");

        var b = ObjectFactory.CreateGameObject("B", typeof(BoxCollider), typeof(Rigidbody), typeof(NeedsBody));
        Object("b", b);
        Row("b.needsBody.value", b.GetComponent<NeedsBody>().value.ToString(CultureInfo.InvariantCulture));
        Events("b");

        var c = ObjectFactory.CreateGameObject(scene, HideFlags.DontSave, "C", typeof(Light));
        Object("c", c);
        Events("c");

        var empty = ObjectFactory.CreateGameObject(null);
        Object("nullName", empty);
        Events("nullName");

        var body = ObjectFactory.AddComponent<Rigidbody>(a);
        Row("add.rigidbody", Describe(body));
        Events("add.rigidbody");
        Row("add.rigidbody.again", Try(() => Describe(ObjectFactory.AddComponent<Rigidbody>(a))));
        Events("add.rigidbody.again");
        Row("add.audio", Try(() => Describe(ObjectFactory.AddComponent(a, typeof(AudioSource)))));
        Events("add.audio");
        Row("add.needsBody", Try(() => Describe(ObjectFactory.AddComponent<NeedsBody>(c))));
        Row("add.needsBody.value", c.GetComponent<NeedsBody>() is NeedsBody nb ? nb.value.ToString(CultureInfo.InvariantCulture) : "none");
        Events("add.needsBody");
        Row("add.onlyOne", Try(() => Describe(ObjectFactory.AddComponent<OnlyOne>(a))));
        Row("add.onlyOne.again", Try(() => Describe(ObjectFactory.AddComponent<OnlyOne>(a))));
        Events("add.onlyOne");
        Row("add.transform", Try(() => Describe(ObjectFactory.AddComponent<Transform>(a))));
        Events("add.transform");
        Row("add.null", Try(() => Describe(ObjectFactory.AddComponent(null, typeof(AudioSource)))));
        Row("add.nullType", Try(() => Describe(ObjectFactory.AddComponent(a, null))));
        Row("add.notComponent", Try(() => Describe(ObjectFactory.AddComponent(a, typeof(string)))));
        Events("add.invalid");
        Object("a.after", a);

        foreach (PrimitiveType type in Enum.GetValues(typeof(PrimitiveType)))
        {
            var p = ObjectFactory.CreatePrimitive(type);
            Object("primitive." + type, p);
            var filter = p.GetComponent<MeshFilter>();
            Row("primitive." + type + ".mesh", filter != null && filter.sharedMesh != null ? filter.sharedMesh.name : "none");
            var renderer = p.GetComponent<MeshRenderer>();
            Row("primitive." + type + ".material", renderer != null && renderer.sharedMaterial != null ? renderer.sharedMaterial.name : "none");
            Events("primitive." + type);
        }

        var asset = ObjectFactory.CreateInstance<FactoryAsset>();
        Row("instance.asset", asset.name + "|" + (int)asset.hideFlags + "|" + asset.value.ToString(CultureInfo.InvariantCulture) + "|" + EditorUtility.IsPersistent(asset));
        var material = Try(() => { var m = (Material)ObjectFactory.CreateInstance(typeof(Material)); return m == null ? "null" : m.name + "|" + (m.shader != null ? m.shader.name : "no shader"); });
        Row("instance.material", material);
        Row("instance.gameObject", Try(() => { var g = (GameObject)ObjectFactory.CreateInstance(typeof(GameObject)); return g == null ? "null" : g.name + "|" + g.scene.name + "|" + (int)g.hideFlags; }));
        Row("instance.component", Try(() => Describe(ObjectFactory.CreateInstance(typeof(Rigidbody)))));
        Events("instance");

        var parent = new GameObject("Parent");
        parent.transform.position = new Vector3(1, 2, 3);
        var child = ObjectFactory.CreateGameObject("Placed");
        child.transform.position = new Vector3(10, 0, 0);
        ObjectFactory.PlaceGameObject(child, parent);
        Object("placed.withParent", child);
        var loose = ObjectFactory.CreateGameObject("Loose");
        loose.transform.position = new Vector3(10, 0, 0);
        ObjectFactory.PlaceGameObject(loose);
        Object("placed.alone", loose);
        Row("place.null", Try(() => { ObjectFactory.PlaceGameObject(null); return "ok"; }));
        Events("place");

        Row("roots", string.Join(",", scene.GetRootGameObjects().Select(r => r.name)));
        Row("sceneDirty.end", scene.isDirty.ToString());
        Row("undoGroupName.end", Undo.GetCurrentGroupName());
        for (int i = 0; i < s_Log.Count; i++) Row("log/" + i.ToString("D2", CultureInfo.InvariantCulture), s_Log[i]);
        Undo.PerformUndo();
        Row("afterUndo.roots", string.Join(",", scene.GetRootGameObjects().Select(r => r.name)));
        Row("afterUndo.a.components", a == null ? "destroyed" : string.Join(",", a.GetComponents<Component>().Select(x => x.GetType().Name)));
    }

    static void Object(string key, GameObject go)
    {
        if (go == null) { Row(key, "null"); return; }
        Row(key + ".name", go.name);
        Row(key + ".hideFlags", ((int)go.hideFlags).ToString(CultureInfo.InvariantCulture));
        Row(key + ".scene", go.scene.IsValid() ? go.scene.name : "invalid");
        Row(key + ".active", go.activeSelf.ToString());
        Row(key + ".layer", go.layer.ToString(CultureInfo.InvariantCulture));
        Row(key + ".parent", go.transform.parent == null ? "none" : go.transform.parent.name);
        Row(key + ".position", V(go.transform.position));
        Row(key + ".localPosition", V(go.transform.localPosition));
        Row(key + ".components", string.Join(",", go.GetComponents<Component>().Select(x => x.GetType().Name)));
        Row(key + ".componentFlags", string.Join(",", go.GetComponents<Component>().Select(x => ((int)x.hideFlags).ToString(CultureInfo.InvariantCulture))));
    }

    static void Events(string key)
    {
        Row(key + ".events", string.Join(",", s_Added));
        s_Added.Clear();
    }

    static string Describe(UnityEngine.Object o) => o == null ? "null" : o.GetType().Name + "|" + o.name + "|" + (int)o.hideFlags;

    static string Try(Func<string> f)
    {
        try { return f(); }
        catch (Exception e) { return "throws " + e.GetType().Name + ": " + e.Message; }
    }

    static string V(Vector3 v) => v.x.ToString("R", CultureInfo.InvariantCulture) + "," + v.y.ToString("R", CultureInfo.InvariantCulture) + "," + v.z.ToString("R", CultureInfo.InvariantCulture);

    static void Row(string key, string value)
    {
        s_Rows.Append("{\"k\":").Append(J(key)).Append(",\"v\":").Append(J(value)).Append("}\n");
    }

    static string J(string s)
    {
        var b = new StringBuilder("\"");
        foreach (var c in s ?? "null")
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.Append('"').ToString();
    }
}
