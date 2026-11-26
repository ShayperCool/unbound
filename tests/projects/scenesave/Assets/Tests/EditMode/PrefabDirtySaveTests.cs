using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

/// <summary>
/// Edit mode: prefab assets read from their files (copies of Thing.prefab run.py seeds into
/// Assets/PrefabSaves), changed through their objects and saved by AssetDatabase.SaveAssets and
/// SaveAssetIfDirty: fields set through the API and directly, with and without SetDirty; a root
/// marked dirty with nothing changed; a component added and a child parented; variants made in
/// the session and changed (their script marked dirty, their root alone, saved by
/// SavePrefabAsset unmarked); a variant and a nesting prefab written after their base and nested
/// prefab were saved; dependents of a saved prefab read and written: through a variant not loaded
/// (a chain of seeded variants), by two paths (a diamond), with an override written in the
/// session, with a component added to a nested instance, and after the saved prefab gained a
/// component (with an unsaved change outside the instance); one prefab left untouched; one left
/// dirty for the Editor's exit.
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

        // A variant whose root alone is marked dirty, its script's field set directly.
        var rootInstance = (GameObject)PrefabUtility.InstantiatePrefab(Load("Base"));
        var variantRoot = PrefabUtility.SaveAsPrefabAsset(rootInstance, Folder + "/VariantRoot.prefab");
        Object.DestroyImmediate(rootInstance);
        variantRoot.GetComponent<Holder>().count = 10;
        EditorUtility.SetDirty(variantRoot);
        Dirty("variantRoot.changed", variantRoot);

        // A variant saved by SavePrefabAsset, its script's field set directly, nothing marked.
        var apiInstance = (GameObject)PrefabUtility.InstantiatePrefab(Load("Base"));
        var variantApi = PrefabUtility.SaveAsPrefabAsset(apiInstance, Folder + "/VariantApi.prefab");
        Object.DestroyImmediate(apiInstance);
        variantApi.GetComponent<Holder>().count = 14;
        Dirty("variantApi.changed", variantApi);
        ProbeLog.Try("variantApi.save", () => { PrefabUtility.SavePrefabAsset(variantApi, out var saved); return saved; });
        ProbeLog.Snapshot("variantApi.file", Folder + "/VariantApi.prefab");
        Dirty("variantApi.saved", variantApi);

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
        foreach (var name in new[] { "Fields", "Unmarked", "Touched", "Untouched", "Structure", "Variant", "VariantRoot" })
            ProbeLog.Snapshot(name.ToLowerInvariant() + ".file", Folder + "/" + name + ".prefab");
        Dirty("fields.saved", fields);
        Dirty("unmarked.saved", unmarked);
        Dirty("touched.saved", touched);
        Dirty("structure.saved", structure);
        Dirty("variant.saved", variant);
        Dirty("variantRoot.saved", variantRoot);
        // A second SaveAssets with nothing dirty.
        ProbeLog.Try("saveAssets.again", () => { AssetDatabase.SaveAssets(); return "done"; });
        ProbeLog.Row("fields.fileAfterSecondSave", ProbeLog.Hash(Folder + "/Fields.prefab"));
        // The new GameObject, where it stayed in the scene.
        if (added != null && added.transform.parent == null) Object.DestroyImmediate(added);

        // A variant of Base2 and a prefab nesting Inner2, made in the session; a second variant of
        // Base2 with its own count, unsaved; then Base2 and Inner2 changed and saved by
        // SavePrefabAsset, and the dependents written by a later SaveAssets.
        var dependentInstance = (GameObject)PrefabUtility.InstantiatePrefab(Load("Base2"));
        var dependent = PrefabUtility.SaveAsPrefabAsset(dependentInstance, Folder + "/Dependent.prefab");
        Object.DestroyImmediate(dependentInstance);
        var ownInstance = (GameObject)PrefabUtility.InstantiatePrefab(Load("Base2"));
        var dependentOwn = PrefabUtility.SaveAsPrefabAsset(ownInstance, Folder + "/DependentOwn.prefab");
        Object.DestroyImmediate(ownInstance);
        var nestingRoot = new GameObject("Nesting");
        var nestedInner = (GameObject)PrefabUtility.InstantiatePrefab(Load("Inner2"));
        nestedInner.transform.SetParent(nestingRoot.transform, false);
        var nesting = PrefabUtility.SaveAsPrefabAsset(nestingRoot, Folder + "/Nesting.prefab");
        Object.DestroyImmediate(nestingRoot);
        ProbeLog.Snapshot("dependent.made", Folder + "/Dependent.prefab");
        ProbeLog.Snapshot("nesting.made", Folder + "/Nesting.prefab");
        dependentOwn.GetComponent<Holder>().count = 30;
        EditorUtility.SetDirty(dependentOwn.GetComponent<Holder>());
        var base2 = Load("Base2");
        base2.GetComponent<Holder>().count = 21;
        base2.GetComponent<Holder>().label = "base changed";
        ProbeLog.Try("base2.save", () => { PrefabUtility.SavePrefabAsset(base2, out var saved); return saved; });
        var inner2 = Load("Inner2");
        inner2.GetComponent<Holder>().count = 22;
        ProbeLog.Try("inner2.save", () => { PrefabUtility.SavePrefabAsset(inner2, out var saved); return saved; });
        ProbeLog.Row("dependent.afterBaseSave", dependent.GetComponent<Holder>().count + " " + dependent.GetComponent<Holder>().label);
        ProbeLog.Row("dependentOwn.afterBaseSave", dependentOwn.GetComponent<Holder>().count + " " + dependentOwn.GetComponent<Holder>().label);
        ProbeLog.Row("nesting.afterInnerSave", nesting.transform.GetChild(0).GetComponent<Holder>().count);
        Dirty("dependent.afterBaseSave", dependent);
        ProbeLog.Row("dependentOwn.afterBaseSave.holder", EditorUtility.IsDirty(dependentOwn.GetComponent<Holder>()));
        EditorUtility.SetDirty(dependent);
        EditorUtility.SetDirty(nesting);
        ProbeLog.Try("saveAssets.dependents", () => { AssetDatabase.SaveAssets(); return "done"; });
        foreach (var name in new[] { "Dependent", "DependentOwn", "Nesting", "Base2", "Inner2" })
            ProbeLog.Snapshot(name.ToLowerInvariant() + ".file", Folder + "/" + name + ".prefab");
        ProbeLog.Row("dependent.afterSave", dependent.GetComponent<Holder>().count + " " + dependent.GetComponent<Holder>().label);
        ProbeLog.Row("dependentOwn.afterSave", dependentOwn.GetComponent<Holder>().count + " " + dependentOwn.GetComponent<Holder>().label);
        ProbeLog.Row("nesting.afterSave", nesting.transform.GetChild(0).GetComponent<Holder>().count);

        // A chain through a prefab not loaded: Chain2 (a variant of Chain1, a variant of Base3,
        // both files run.py seeds) loaded alone, then Base3 changed and saved; Chain2 then marked
        // dirty and saved by SaveAssets.
        var chain2 = Load("Chain2");
        ProbeLog.Row("chain.loaded", chain2.GetComponent<Holder>().count + " " + chain2.GetComponent<Holder>().label);
        var base3 = Load("Base3");
        base3.GetComponent<Holder>().count = 24;
        base3.GetComponent<Holder>().label = "chain base";
        ProbeLog.Try("base3.save", () => { PrefabUtility.SavePrefabAsset(base3, out var saved); return saved; });
        ProbeLog.Row("chain.afterBaseSave", chain2.GetComponent<Holder>().count + " " + chain2.GetComponent<Holder>().label);
        Dirty("chain.afterBaseSave", chain2);
        EditorUtility.SetDirty(chain2);
        ProbeLog.Try("saveAssets.chain", () => { AssetDatabase.SaveAssets(); return "done"; });
        foreach (var name in new[] { "Chain1", "Chain2", "Base3" })
            ProbeLog.Snapshot(name.ToLowerInvariant() + ".file", Folder + "/" + name + ".prefab");
        ProbeLog.Row("chain.afterSave", chain2.GetComponent<Holder>().count + " " + chain2.GetComponent<Holder>().label);

        // A diamond: Top5, made in the session, nests Base5 and Middle5 (a variant of Base5 run.py
        // seeds); then Base5 changed and saved, Top5 and Middle5 marked dirty and saved by
        // SaveAssets, then Top5 deleted.
        var middle5 = Load("Middle5");
        var topRoot = new GameObject("Top5");
        ((GameObject)PrefabUtility.InstantiatePrefab(Load("Base5"))).transform.SetParent(topRoot.transform, false);
        ((GameObject)PrefabUtility.InstantiatePrefab(middle5)).transform.SetParent(topRoot.transform, false);
        var top5 = PrefabUtility.SaveAsPrefabAsset(topRoot, Folder + "/Top5.prefab");
        Object.DestroyImmediate(topRoot);
        var base5 = Load("Base5");
        base5.GetComponent<Holder>().count = 25;
        ProbeLog.Try("base5.save", () => { PrefabUtility.SavePrefabAsset(base5, out var saved); return saved; });
        ProbeLog.Row("diamond.direct", top5.transform.GetChild(0).GetComponent<Holder>().count);
        ProbeLog.Row("diamond.throughMiddle", top5.transform.GetChild(1).GetComponent<Holder>().count);
        ProbeLog.Row("diamond.middle", middle5.GetComponent<Holder>().count);
        EditorUtility.SetDirty(top5);
        EditorUtility.SetDirty(middle5);
        ProbeLog.Try("saveAssets.diamond", () => { AssetDatabase.SaveAssets(); return "done"; });
        ProbeLog.Snapshot("middle5.file", Folder + "/Middle5.prefab");
        // Top5's two PrefabInstance documents have random file ids, so its document order is not
        // one a capture repeats: its file is not kept.
        ProbeLog.Try("top5.delete", () => AssetDatabase.DeleteAsset(Folder + "/Top5.prefab"));

        // A variant written with its own count in the session, then its base changed and saved.
        var ownInstance6 = (GameObject)PrefabUtility.InstantiatePrefab(Load("Base6"));
        var own6 = PrefabUtility.SaveAsPrefabAsset(ownInstance6, Folder + "/Own6.prefab");
        Object.DestroyImmediate(ownInstance6);
        own6.GetComponent<Holder>().count = 31;
        ProbeLog.Try("own6.save", () => { PrefabUtility.SavePrefabAsset(own6, out var saved); return saved; });
        ProbeLog.Snapshot("own6.saved", Folder + "/Own6.prefab");
        var base6 = Load("Base6");
        base6.GetComponent<Holder>().count = 26;
        base6.GetComponent<Holder>().label = "own base";
        ProbeLog.Try("base6.save", () => { PrefabUtility.SavePrefabAsset(base6, out var saved); return saved; });
        ProbeLog.Row("own6.afterBaseSave", own6.GetComponent<Holder>().count + " " + own6.GetComponent<Holder>().label);

        // A component added to the nested instance in a prefab asset (unsaved), then the nested
        // prefab changed and saved.
        var outerRoot = new GameObject("Outer7");
        ((GameObject)PrefabUtility.InstantiatePrefab(Load("Inner7"))).transform.SetParent(outerRoot.transform, false);
        var outer7 = PrefabUtility.SaveAsPrefabAsset(outerRoot, Folder + "/Outer7.prefab");
        Object.DestroyImmediate(outerRoot);
        ProbeLog.Try("outer7.addComponent", () => outer7.transform.GetChild(0).gameObject.AddComponent<SphereCollider>() != null);
        ProbeLog.Row("outer7.child.dirty", EditorUtility.IsDirty(outer7.transform.GetChild(0).gameObject));
        var inner7 = Load("Inner7");
        inner7.GetComponent<Holder>().count = 27;
        ProbeLog.Try("inner7.save", () => { PrefabUtility.SavePrefabAsset(inner7, out var saved); return saved; });
        var outerChild = outer7.transform.GetChild(0);
        ProbeLog.Row("outer7.afterInnerSave", outerChild.GetComponent<Holder>().count + " " + (outerChild.GetComponent<SphereCollider>() != null));
        ProbeLog.Row("outer7.afterInnerSave.dirty", EditorUtility.IsDirty(outerChild.gameObject));

        // The saved prefab gains a component: its variant Variant8 (seeded) and Nest8, a prefab
        // nesting it made in the session whose own root then has an unsaved change, both loaded.
        var variant8 = Load("Variant8");
        var nestRoot = new GameObject("Nest8");
        ((GameObject)PrefabUtility.InstantiatePrefab(Load("Base8"))).transform.SetParent(nestRoot.transform, false);
        var nest8 = PrefabUtility.SaveAsPrefabAsset(nestRoot, Folder + "/Nest8.prefab");
        Object.DestroyImmediate(nestRoot);
        nest8.transform.localPosition = new Vector3(5f, 0f, 0f);
        EditorUtility.SetDirty(nest8.transform);
        var base8 = Load("Base8");
        ProbeLog.Try("base8.addComponent", () => base8.AddComponent<SphereCollider>() != null);
        base8.GetComponent<Holder>().count = 28;
        ProbeLog.Try("base8.save", () => { PrefabUtility.SavePrefabAsset(base8, out var saved); return saved; });
        ProbeLog.Row("variant8.afterBaseSave", variant8.GetComponent<Holder>().count + " " + (variant8.GetComponent<SphereCollider>() != null));
        var nested8 = nest8.transform.GetChild(0);
        ProbeLog.Row("nest8.afterBaseSave", nested8.GetComponent<Holder>().count + " " + (nested8.GetComponent<SphereCollider>() != null));
        ProbeLog.Row("nest8.rootAfterBaseSave", nest8.transform.localPosition.x);
        ProbeLog.Row("nest8.rootAfterBaseSave.dirty", EditorUtility.IsDirty(nest8.transform));

        // Left dirty for the Editor's exit, which run.py's file list shows.
        var exit = Load("Exit");
        exit.GetComponent<Holder>().count = 11;
        EditorUtility.SetDirty(exit.GetComponent<Holder>());
        Dirty("exit.marked", exit);
    }
}
