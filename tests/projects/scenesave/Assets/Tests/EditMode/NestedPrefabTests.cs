using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using Object = UnityEngine.Object;

/// <summary>
/// Edit mode: new prefabs that hold prefab instances and variants of instances with added and
/// removed objects. SaveAsPrefabAsset of a plain hierarchy holding an instance (moved, a recorded
/// script change, an added component and GameObject), SaveAsPrefabAssetAndConnect of one holding an
/// instance, SaveAsPrefabAsset of an instance root with an added component, an added GameObject
/// and a removed component (a variant), and of an instance of that variant (a variant of a
/// variant); the scene they are in saved. Each row records a return value, a flag or a file's
/// hash; run.py compares the files on disk after the run.
/// </summary>
public class NestedPrefabTests
{
    const string Thing = "Assets/Prefabs/Thing.prefab";

    static string Name(Object o) => o == null ? "null" : o.GetType().Name + ":" + o.name;

    static string Sorted(System.Collections.Generic.IEnumerable<string> items) => string.Join("; ", items.OrderBy(i => i, System.StringComparer.Ordinal));

    static void Overrides(string key, GameObject instance)
    {
        var mods = PrefabUtility.GetPropertyModifications(instance);
        ProbeLog.Row(key + ".mods", mods == null ? "null" : Sorted(mods.Select(m => $"{Name(m.target)}|{m.propertyPath}|{m.value}|{Name(m.objectReference)}")));
        ProbeLog.Row(key + ".added", Sorted(PrefabUtility.GetAddedComponents(instance).Select(o => Name(o.instanceComponent))));
        ProbeLog.Row(key + ".removed", Sorted(PrefabUtility.GetRemovedComponents(instance).Select(o => Name(o.assetComponent))));
        ProbeLog.Row(key + ".addedObjects", Sorted(PrefabUtility.GetAddedGameObjects(instance).Select(o => Name(o.instanceGameObject))));
    }

    static string Tree(GameObject root)
    {
        return string.Join(", ", root.GetComponentsInChildren<Transform>(true).Select(t =>
            t.name + "[" + PrefabUtility.GetPrefabInstanceStatus(t.gameObject) + "|" + PrefabUtility.IsAnyPrefabInstanceRoot(t.gameObject)
            + "|" + string.Join("+", t.GetComponents<Component>().Select(c => c.GetType().Name)) + "]"));
    }

    [Test]
    public void NestedPrefabs()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var thing = AssetDatabase.LoadAssetAtPath<GameObject>(Thing);

        // A new prefab holding an instance with overrides.
        var outer = new GameObject("Outer");
        var nested = (GameObject)PrefabUtility.InstantiatePrefab(thing, outer.transform);
        nested.transform.localPosition = new Vector3(1f, 2f, 3f);
        var holder = nested.GetComponent<Holder>();
        holder.count = 7;
        PrefabUtility.RecordPrefabInstancePropertyModifications(holder);
        nested.AddComponent<SphereCollider>().radius = 4f;
        var extra = new GameObject("Extra");
        extra.transform.SetParent(nested.transform, false);
        ProbeLog.Try("outer.result", () => Name(PrefabUtility.SaveAsPrefabAsset(outer, "Assets/Prefabs/Outer.prefab")));
        ProbeLog.Row("outer.isInstance", PrefabUtility.IsPartOfPrefabInstance(outer));
        ProbeLog.Row("outer.nestedSource", PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(nested));
        Overrides("outer.nested", nested);
        ProbeLog.Snapshot("outer.prefab", "Assets/Prefabs/Outer.prefab");
        var outerAsset = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/Outer.prefab");
        ProbeLog.Row("outer.asset", outerAsset == null ? "null" : Tree(outerAsset));
        ProbeLog.Row("outer.assetType", PrefabUtility.GetPrefabAssetType(outerAsset).ToString());

        // A hierarchy holding an instance, saved and connected.
        var connected = new GameObject("OuterConnected");
        var inner = (GameObject)PrefabUtility.InstantiatePrefab(thing, connected.transform);
        inner.transform.localPosition = new Vector3(0f, 0f, 4f);
        ProbeLog.Try("connect.result", () => Name(PrefabUtility.SaveAsPrefabAssetAndConnect(connected, "Assets/Prefabs/OuterConnected.prefab", InteractionMode.AutomatedAction)));
        ProbeLog.Row("connect.isRoot", PrefabUtility.IsOutermostPrefabInstanceRoot(connected));
        ProbeLog.Row("connect.innerSource", PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(inner));
        Overrides("connect", connected);
        ProbeLog.Row("connect.tree", Tree(connected));
        ProbeLog.Snapshot("connect.prefab", "Assets/Prefabs/OuterConnected.prefab");

        // A variant of an instance with added and removed objects.
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(thing);
        instance.transform.position = new Vector3(5f, 0f, 0f);
        instance.AddComponent<SphereCollider>().radius = 2f;
        var added = new GameObject("AddedChild");
        added.transform.SetParent(instance.transform, false);
        added.AddComponent<Holder>().label = "added";
        Object.DestroyImmediate(instance.transform.Find("Part").GetComponent<BoxCollider>());
        Overrides("variant.before", instance);
        ProbeLog.Try("variant.result", () => { var made = PrefabUtility.SaveAsPrefabAsset(instance, "Assets/Prefabs/RichVariant.prefab", out var ok); return Name(made) + "|" + ok; });
        ProbeLog.Row("variant.instanceSource", PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance));
        Overrides("variant.after", instance);
        ProbeLog.Snapshot("variant.prefab", "Assets/Prefabs/RichVariant.prefab");
        var variant = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/RichVariant.prefab");
        ProbeLog.Row("variant.type", PrefabUtility.GetPrefabAssetType(variant).ToString());
        ProbeLog.Row("variant.asset", variant == null ? "null" : Tree(variant));

        // A variant of that variant.
        var second = (GameObject)PrefabUtility.InstantiatePrefab(variant);
        second.transform.position = new Vector3(0f, 6f, 0f);
        ProbeLog.Try("variant2.result", () => Name(PrefabUtility.SaveAsPrefabAsset(second, "Assets/Prefabs/VariantOfVariant.prefab")));
        ProbeLog.Snapshot("variant2.prefab", "Assets/Prefabs/VariantOfVariant.prefab");
        var variant2 = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/VariantOfVariant.prefab");
        ProbeLog.Row("variant2.type", PrefabUtility.GetPrefabAssetType(variant2).ToString());
        ProbeLog.Row("variant2.base", PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(variant2));

        ProbeLog.Try("scene.result", () => EditorSceneManager.SaveScene(scene, "Assets/Prefabs/Nested.unity"));
        ProbeLog.Snapshot("scene.file", "Assets/Prefabs/Nested.unity");
    }
}
