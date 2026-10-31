using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class LoadingEditor
{
    static GameObject Make(string name, bool active, bool kinematic, bool parented)
    {
        var body = new GameObject(name); body.SetActive(false);
        var root = body;
        if (parented)
        {
            root = new GameObject(name + "-parent");
            root.transform.SetPositionAndRotation(new Vector3(10,20,30),new Quaternion(0,1,0,0));
            body.transform.SetParent(root.transform,false);
        }
        body.transform.localPosition = new Vector3(3,4,5);
        body.transform.localRotation = new Quaternion(1,0,0,0);
        var rb = body.AddComponent<Rigidbody>(); rb.isKinematic = kinematic;
        // Gravity stays at its serialized default, but the manual world has zero gravity.
        body.SetActive(active);
        return root;
    }
    public static void Create()
    {
        System.IO.Directory.CreateDirectory("Assets/Resources");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        foreach (string kind in new[] { "scene","nested","asset","instance" })
        foreach (bool active in new[] { false,true })
        foreach (bool kinematic in new[] { false,true })
        foreach (bool parented in new[] { false,true })
        for (int sequence = 0; sequence < 6; sequence++)
        {
            string key = LoadingProbe.Name(kind,active,kinematic,parented,sequence);
            var root = Make(key,active,kinematic,parented);
            if (kind == "scene") continue;
            var prefab = PrefabUtility.SaveAsPrefabAsset(root,"Assets/Resources/" + key + ".prefab");
            UnityEngine.Object.DestroyImmediate(root);
            if (kind != "nested") continue;
            // A serialized instance containing a nested prefab and transform/property overrides.
            var wrapper = new GameObject(key + "-wrapper");
            wrapper.transform.SetPositionAndRotation(new Vector3(-7,8,9),new Quaternion(0,0,1,0));
            var nested = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            nested.transform.SetParent(wrapper.transform,false);
            nested.transform.localPosition = new Vector3(6,7,8);
            nested.transform.localRotation = new Quaternion(0,1,0,0);
            PrefabUtility.RecordPrefabInstancePropertyModifications(nested.transform);
            var body = parented ? nested.transform.GetChild(0).gameObject : nested;
            body.GetComponent<Rigidbody>().mass = 2;
            PrefabUtility.RecordPrefabInstancePropertyModifications(body.GetComponent<Rigidbody>());
            var outer = PrefabUtility.SaveAsPrefabAsset(wrapper,"Assets/Resources/" + key + "-outer.prefab");
            UnityEngine.Object.DestroyImmediate(wrapper);
            var instance = (GameObject)PrefabUtility.InstantiatePrefab(outer);
            instance.transform.position = new Vector3(-17,18,19);
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
        }
        EditorSceneManager.SaveScene(scene,"Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity",true) };
        AssetDatabase.SaveAssets(); EditorApplication.Exit(0);
    }
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
