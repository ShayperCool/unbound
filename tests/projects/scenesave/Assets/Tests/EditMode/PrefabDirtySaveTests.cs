using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Edit mode: prefab assets read from their files (copies of Thing.prefab run.py seeds into
/// Assets/PrefabSaves), changed through their objects and saved by AssetDatabase.SaveAssets and
/// SaveAssetIfDirty: fields set through the API and directly, with and without SetDirty; a root
/// marked dirty with nothing changed; a component added and a child parented; a variant made
/// in the session and changed; one prefab left untouched; one left dirty for the Editor's exit.
/// Each row records a dirty flag, a step's result or a file's hash (with a snapshot); run.py
/// keeps the files on disk after the run.
/// </summary>
public class PrefabDirtySaveTests
{
    const string Folder = "Assets/PrefabSaves";

    static GameObject Load(string name) => AssetDatabase.LoadAssetAtPath<GameObject>(Folder + "/" + name + ".prefab");

    /// <summary>The dirty flags of a Thing copy's objects: the root GameObject, its Transform
    /// and Holder, the child Part and its BoxCollider.</summary>
    static void Dirty(string key, GameObject root)
    {
        ProbeLog.Row(key + ".root", EditorUtility.IsDirty(root));
        ProbeLog.Row(key + ".transform", EditorUtility.IsDirty(root.transform));
        ProbeLog.Row(key + ".holder", EditorUtility.IsDirty(root.GetComponent<Holder>()));
        var part = root.transform.GetChild(0);
        ProbeLog.Row(key + ".part", EditorUtility.IsDirty(part.gameObject));
        ProbeLog.Row(key + ".collider", EditorUtility.IsDirty(part.GetComponent<BoxCollider>()));
    }

    [Test]
    public void PrefabDirtySaves()
    {
        Application.LogCallback log = (message, stack, type) => ProbeLog.Row("save.log", type + ": " + message);
        Application.logMessageReceived += log;
        LogAssert.ignoreFailingMessages = true;
        try { Run(); }
        finally
        {
            LogAssert.ignoreFailingMessages = false;
            Application.logMessageReceived -= log;
        }
    }

    static void Run()
    {
        // Fields set through the API (a Transform, a BoxCollider) and directly (a script's).
        var fields = Load("Fields");
        Dirty("fields.loaded", fields);
        fields.transform.localPosition = new Vector3(1f, 2f, 3f);
        fields.transform.GetChild(0).GetComponent<BoxCollider>().size = new Vector3(2f, 2f, 2f);
        var holder = fields.GetComponent<Holder>();
        holder.count = 7;
        holder.label = "saved";
        Dirty("fields.changed", fields);
        EditorUtility.SetDirty(holder);
        Dirty("fields.marked", fields);

        // A script's field set directly, with no SetDirty.
        var unmarked = Load("Unmarked");
        unmarked.GetComponent<Holder>().count = 8;
        Dirty("unmarked.changed", unmarked);

        // A root marked dirty with nothing changed.
        var touched = Load("Touched");
        EditorUtility.SetDirty(touched);
        Dirty("touched.marked", touched);

        // Loaded, left as it is.
        var untouched = Load("Untouched");
        Dirty("untouched.loaded", untouched);

        // A component added to the root, a new GameObject parented under it.
        var structure = Load("Structure");
        ProbeLog.Try("structure.addComponent", () => structure.AddComponent<SphereCollider>() != null);
        var added = new GameObject("Added");
        ProbeLog.Try("structure.setParent", () =>
        {
            added.transform.SetParent(structure.transform, false);
            return added.transform.parent == structure.transform;
        });
        ProbeLog.Row("structure.childCount", structure.transform.childCount);
        Dirty("structure.changed", structure);

        // A variant of Base made in the session, its Holder changed.
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(Load("Base"));
        var variant = PrefabUtility.SaveAsPrefabAsset(instance, Folder + "/Variant.prefab");
        Object.DestroyImmediate(instance);
        ProbeLog.Snapshot("variant.made", Folder + "/Variant.prefab");
        ProbeLog.Row("variant.isVariant", PrefabUtility.GetPrefabAssetType(variant).ToString());
        variant.GetComponent<Holder>().count = 9;
        EditorUtility.SetDirty(variant.GetComponent<Holder>());
        Dirty("variant.changed", variant);

        // One prefab saved alone: the others stay as they are.
        var single = Load("Single");
        single.GetComponent<Holder>().count = 12;
        EditorUtility.SetDirty(single.GetComponent<Holder>());
        ProbeLog.Try("single.saveIfDirty", () => { AssetDatabase.SaveAssetIfDirty(single); return "done"; });
        ProbeLog.Snapshot("single.file", Folder + "/Single.prefab");
        Dirty("single.saved", single);
        ProbeLog.Row("fields.fileBeforeSaveAssets", ProbeLog.Hash(Folder + "/Fields.prefab"));
        Dirty("fields.afterSingle", fields);

        ProbeLog.Try("saveAssets", () => { AssetDatabase.SaveAssets(); return "done"; });
        foreach (var name in new[] { "Fields", "Unmarked", "Touched", "Untouched", "Structure", "Variant" })
            ProbeLog.Snapshot(name.ToLowerInvariant() + ".file", Folder + "/" + name + ".prefab");
        Dirty("fields.saved", fields);
        Dirty("unmarked.saved", unmarked);
        Dirty("touched.saved", touched);
        Dirty("structure.saved", structure);
        Dirty("variant.saved", variant);
        // A second SaveAssets with nothing dirty.
        ProbeLog.Try("saveAssets.again", () => { AssetDatabase.SaveAssets(); return "done"; });
        ProbeLog.Row("fields.fileAfterSecondSave", ProbeLog.Hash(Folder + "/Fields.prefab"));
        // The new GameObject, where it stayed in the scene.
        if (added != null && added.transform.parent == null) Object.DestroyImmediate(added);

        // Left dirty for the Editor's exit, which run.py's file list shows.
        var exit = Load("Exit");
        exit.GetComponent<Holder>().count = 11;
        EditorUtility.SetDirty(exit.GetComponent<Holder>());
        Dirty("exit.marked", exit);
    }
}
