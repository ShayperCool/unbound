using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// `Unity -batchmode -projectPath &lt;copy&gt; -executeMethod ProbeSetup.Build -quit` made the probe's
/// prefabs and scenes: a regular prefab, one nesting it (with an override), a variant of it, and
/// scenes with instances (overrides, an added child and component, an instance under a plain
/// object, an unpacked instance, a model instance) and plain objects.
/// `ProbeSetup.Record` (with EDITOR_CALLBACKS_OUT set) opens Main and enters Play mode.
/// </summary>
public static class ProbeSetup
{
    public static void Build()
    {
        AssetDatabase.CreateFolder("Assets", "Prefabs");
        AssetDatabase.CreateFolder("Assets", "Scenes");

        var root = new GameObject("Plain");
        root.AddComponent<ProbeMarker>().value = 1;
        var child = new GameObject("Child");
        child.transform.SetParent(root.transform, false);
        child.AddComponent<BoxCollider>();
        var plain = PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/Plain.prefab");
        Object.DestroyImmediate(root);

        root = new GameObject("Outer");
        root.AddComponent<ProbeMarker>().value = 2;
        var slot = new GameObject("Slot");
        slot.transform.SetParent(root.transform, false);
        var inner = (GameObject)PrefabUtility.InstantiatePrefab(plain);
        inner.name = "Inner";
        inner.transform.SetParent(slot.transform, false);
        inner.GetComponent<ProbeMarker>().value = 5;
        PrefabUtility.RecordPrefabInstancePropertyModifications(inner.GetComponent<ProbeMarker>());
        var outer = PrefabUtility.SaveAsPrefabAsset(root, "Assets/Prefabs/Outer.prefab");
        Object.DestroyImmediate(root);

        var variantRoot = (GameObject)PrefabUtility.InstantiatePrefab(plain);
        variantRoot.name = "PlainVariant";
        variantRoot.GetComponent<ProbeMarker>().value = 7;
        PrefabUtility.RecordPrefabInstancePropertyModifications(variantRoot.GetComponent<ProbeMarker>());
        var variant = PrefabUtility.SaveAsPrefabAsset(variantRoot, "Assets/Prefabs/PlainVariant.prefab");
        Object.DestroyImmediate(variantRoot);

        var model = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Lods.fbx");

        NewScene();
        var a = Instance(plain, "PlainA", null);
        a.transform.position = new Vector3(1, 0, 0);
        a.GetComponent<ProbeMarker>().value = 3;
        PrefabUtility.RecordPrefabInstancePropertyModifications(a.GetComponent<ProbeMarker>());
        PrefabUtility.RecordPrefabInstancePropertyModifications(a.transform);
        var added = new GameObject("Added");
        added.transform.SetParent(a.transform, false);
        added.AddComponent<ProbeMarker>().value = 4;
        a.transform.Find("Child").gameObject.AddComponent<ProbeExtra>();
        Instance(plain, "Plain", null);
        Instance(outer, "OuterA", null);
        Instance(variant, "VariantA", null);
        Instance(model, "ModelA", null);
        var holder = new GameObject("Holder");
        Instance(plain, "Held", holder.transform);
        var unpacked = Instance(plain, "Unpacked", null);
        PrefabUtility.UnpackPrefabInstance(unpacked, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
        new GameObject("Lone").AddComponent<ProbeMarker>().value = 6;
        Save("Main");

        NewScene();
        Instance(plain, "PlainB", null);
        Instance(outer, "OuterB", null);
        Save("Second");

        NewScene();
        Instance(variant, "VariantC", null);
        new GameObject("AdditiveLone").AddComponent<ProbeMarker>().value = 8;
        Save("Additive");

        NewScene();
        Instance(plain, "PlainD", null);
        Save("Async");

        EditorBuildSettings.scenes = new[]
        {
            new EditorBuildSettingsScene("Assets/Scenes/Main.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/Second.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/Additive.unity", true),
            new EditorBuildSettingsScene("Assets/Scenes/Async.unity", true),
        };
        AssetDatabase.SaveAssets();
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }

    static void NewScene() => EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

    static void Save(string name) =>
        EditorSceneManager.SaveScene(EditorSceneManager.GetActiveScene(), "Assets/Scenes/" + name + ".unity");

    static GameObject Instance(GameObject prefab, string name, Transform parent)
    {
        var go = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        go.name = name;
        if (parent != null) go.transform.SetParent(parent, false);
        return go;
    }
}
