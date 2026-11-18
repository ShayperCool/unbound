using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Writes the probe's prefabs and the instances in its scene (run once in the Editor with
/// <c>-executeMethod ProbeScene.Make</c>; the results are committed): `Box` (a box collider and a
/// child), `Nest` (a Box instance and a child of its own) and `BoxVariant` (a variant of Box with
/// the box collider's centre changed), and one instance of each prefab and model in
/// Scenes/Probe.unity.
/// </summary>
public static class ProbeScene
{
    public static void Make()
    {
        AssetDatabase.CreateFolder("Assets", "Prefabs");
        var box = new GameObject("Box");
        box.AddComponent<BoxCollider>();
        new GameObject("Child").transform.SetParent(box.transform, false);
        var boxAsset = PrefabUtility.SaveAsPrefabAsset(box, "Assets/Prefabs/Box.prefab");
        Object.DestroyImmediate(box);

        var nest = new GameObject("Nest");
        var nested = (GameObject)PrefabUtility.InstantiatePrefab(boxAsset);
        nested.transform.SetParent(nest.transform, false);
        nested.transform.localPosition = new Vector3(0, 1, 0);
        var own = new GameObject("Own");
        own.transform.SetParent(nest.transform, false);
        own.AddComponent<BoxCollider>().size = new Vector3(3, 3, 3);
        PrefabUtility.SaveAsPrefabAsset(nest, "Assets/Prefabs/Nest.prefab");
        Object.DestroyImmediate(nest);

        var variant = (GameObject)PrefabUtility.InstantiatePrefab(boxAsset);
        variant.GetComponent<BoxCollider>().center = new Vector3(0, 1, 0);
        PrefabUtility.SaveAsPrefabAsset(variant, "Assets/Prefabs/BoxVariant.prefab");
        Object.DestroyImmediate(variant);

        var scene = EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        string[] assets =
        {
            "Assets/Models/Materials.fbx", "Assets/Models/Animated.fbx", "Assets/Models/UserProps.fbx",
            "Assets/Prefabs/Box.prefab", "Assets/Prefabs/Nest.prefab", "Assets/Prefabs/BoxVariant.prefab",
        };
        for (int i = 0; i < assets.Length; i++)
        {
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(assets[i]), scene);
            instance.transform.position = new Vector3(10 * i, 0, 0);
        }
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        EditorApplication.Exit(0);
    }
}
