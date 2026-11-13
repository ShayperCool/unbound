using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Authors the probe's prefabs from the imported models (run once in the Editor):
/// a variant of Nested.fbx overriding objects below its root, and a prefab holding that variant
/// and Single.fbx with overrides of their objects in turn.</summary>
public static class ProbeSetup
{
    public static void Create()
    {
        var material = new Material(Shader.Find("Standard")) { name = "Override" };
        AssetDatabase.CreateAsset(material, "Assets/Materials/Override.mat");

        var nested = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Nested.fbx");
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(nested);
        Find(instance, "Body/Leaf 1").GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.ShadowsOnly;
        Find(instance, "Body/Arm/Hand").GetComponent<MeshRenderer>().sharedMaterial = material;
        Find(instance, "Tree_LOD1").GetComponent<MeshRenderer>().enabled = false;
        Find(instance, "Empty/Deep").SetActive(false);
        Find(instance, "Body/Arm").transform.localPosition = new Vector3(1, 2, 3);
        instance.GetComponent<LODGroup>().fadeMode = LODFadeMode.CrossFade;
        var variant = PrefabUtility.SaveAsPrefabAsset(instance, "Assets/Resources/NestedVariant.prefab");
        Object.DestroyImmediate(instance);

        var outer = new GameObject("Outer");
        var inner = (GameObject)PrefabUtility.InstantiatePrefab(variant, outer.transform);
        Find(inner, "Body").GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.Off;
        Find(inner, "Body/Leaf").GetComponent<MeshRenderer>().receiveShadows = false;
        Find(inner, "Body/Leaf 1").GetComponent<MeshRenderer>().sharedMaterial = material;
        Find(inner, "Empty").transform.localScale = new Vector3(2, 2, 2);
        var single = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Models/Single.fbx"), outer.transform);
        single.GetComponent<MeshRenderer>().shadowCastingMode = ShadowCastingMode.TwoSided;
        single.transform.localPosition = new Vector3(0, 5, 0);
        PrefabUtility.SaveAsPrefabAsset(outer, "Assets/Resources/Outer.prefab");
        Object.DestroyImmediate(outer);
        AssetDatabase.SaveAssets();
    }

    static GameObject Find(GameObject root, string path) => root.transform.Find(path).gameObject;
}
