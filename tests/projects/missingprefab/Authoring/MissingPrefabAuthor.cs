using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Authors the probe's prefabs and scene (run once with -executeMethod MissingPrefabAuthor.Author).
public static class MissingPrefabAuthor
{
    public static void Author()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Probe")) AssetDatabase.CreateFolder("Assets", "Probe");
        var inner = new GameObject("Inner");
        inner.AddComponent<Marker>().value = 1;
        var wheel = new GameObject("Wheel");
        wheel.transform.SetParent(inner.transform, false);
        wheel.transform.localPosition = new Vector3(0, 1, 0);
        wheel.AddComponent<BoxCollider>().size = new Vector3(2, 2, 2);
        for (int i = 1; i <= 3; i++)
        {
            var extra = new GameObject("Wheel" + i);
            extra.transform.SetParent(inner.transform, false);
            extra.transform.localPosition = new Vector3(i, 0, 0);
            extra.AddComponent<BoxCollider>();
        }
        var innerAsset = PrefabUtility.SaveAsPrefabAsset(inner, "Assets/Probe/Inner.prefab");
        Object.DestroyImmediate(inner);

        var variant = (GameObject)PrefabUtility.InstantiatePrefab(innerAsset);
        variant.name = "InnerVariant";
        variant.GetComponent<Marker>().value = 7;
        var variantAsset = PrefabUtility.SaveAsPrefabAsset(variant, "Assets/Probe/InnerVariant.prefab");
        Object.DestroyImmediate(variant);

        var outer = new GameObject("Outer");
        var holder = outer.AddComponent<Holder>();
        var nested = (GameObject)PrefabUtility.InstantiatePrefab(innerAsset);
        nested.name = "InnerA";
        nested.transform.SetParent(outer.transform, false);
        nested.transform.localPosition = new Vector3(2, 0, 0);
        nested.GetComponent<Marker>().value = 5;
        var nestedWheel = nested.transform.Find("Wheel");
        var added = new GameObject("Added");
        added.transform.SetParent(nestedWheel, false);
        added.transform.localPosition = new Vector3(0, 0, 1);
        nested.AddComponent<SphereCollider>().radius = 0.25f;
        nested.transform.Find("Wheel2").gameObject.AddComponent<CapsuleCollider>().height = 3;
        var addedRoot = new GameObject("AddedRoot");
        addedRoot.transform.SetParent(nested.transform, false);
        var after = new GameObject("After");
        after.transform.SetParent(outer.transform, false);
        holder.objects = new[] { nested, nestedWheel.gameObject, added, after, addedRoot,
            nested.transform.Find("Wheel3").gameObject, nested.transform.Find("Wheel1").gameObject };
        var parts = new System.Collections.Generic.List<Component>();
        for (int i = 3; i >= 1; i--)
        {
            var extra = nested.transform.Find("Wheel" + i);
            parts.Add(extra);
            parts.Add(extra.GetComponent<BoxCollider>());
        }
        holder.parts = parts.ToArray();
        holder.childTransform = nestedWheel;
        holder.marker = nested.GetComponent<Marker>();
        holder.box = nestedWheel.GetComponent<BoxCollider>();
        holder.sphere = nested.GetComponent<SphereCollider>();
        var outerAsset = PrefabUtility.SaveAsPrefabAsset(outer, "Assets/Probe/Outer.prefab");
        Object.DestroyImmediate(outer);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var outerScene = (GameObject)PrefabUtility.InstantiatePrefab(outerAsset);
        outerScene.name = "OuterS";
        outerScene.transform.position = new Vector3(0, 0, -3);
        var innerScene = (GameObject)PrefabUtility.InstantiatePrefab(innerAsset);
        innerScene.name = "InnerS";
        innerScene.transform.position = new Vector3(0, 0, 3);
        var innerScene2 = (GameObject)PrefabUtility.InstantiatePrefab(innerAsset);
        innerScene2.name = "InnerS2";
        var variantScene = (GameObject)PrefabUtility.InstantiatePrefab(variantAsset);
        variantScene.name = "VariantS";
        variantScene.transform.position = new Vector3(1, 0, 0);
        var sceneChild = new GameObject("SceneChild");
        sceneChild.transform.SetParent(innerScene.transform.Find("Wheel"), false);
        var sceneHolder = new GameObject("SceneHolder").AddComponent<Holder>();
        sceneHolder.objects = new[]
        {
            innerScene, innerScene.transform.Find("Wheel").gameObject, sceneChild,
            outerScene.transform.Find("InnerA").gameObject, outerScene.transform.Find("InnerA/Wheel/Added").gameObject,
        };
        sceneHolder.childTransform = outerScene.transform.Find("InnerA/Wheel");
        sceneHolder.marker = innerScene.GetComponent<Marker>();
        sceneHolder.box = outerScene.transform.Find("InnerA/Wheel").GetComponent<BoxCollider>();
        sceneHolder.sphere = outerScene.transform.Find("InnerA").GetComponent<SphereCollider>();
        sceneHolder.spawn = outerAsset;
        EditorSceneManager.SaveScene(scene, "Assets/Probe/Probe.unity");
        AssetDatabase.SaveAssets();
        EditorApplication.Exit(0);
    }
}
