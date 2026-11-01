using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>`Create` makes the prefab (Resources/Item) and the scene once; `Record` opens the scene
/// and enters Play mode, where <see cref="AsyncProbe"/> writes INSTANTIATE_ASYNC_OUT and exits.</summary>
public static class ProbeRunner
{
    public static void Create()
    {
        System.IO.Directory.CreateDirectory("Assets/Resources");
        System.IO.Directory.CreateDirectory("Assets/Scenes");
        var item = new GameObject("Item");
        item.transform.localPosition = new Vector3(1, 2, 3);
        item.transform.localRotation = Quaternion.Euler(10, 20, 30);
        item.transform.localScale = new Vector3(0.5f, 0.5f, 0.5f);
        item.AddComponent<ItemProbe>();
        var child = new GameObject("Child");
        child.transform.SetParent(item.transform, false);
        child.transform.localPosition = new Vector3(0, 1, 0);
        child.AddComponent<ItemProbe>();
        PrefabUtility.SaveAsPrefabAsset(item, "Assets/Resources/Item.prefab");
        Object.DestroyImmediate(item);

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("AsyncProbe").AddComponent<AsyncProbe>();
        var parent = new GameObject("Parent").transform;
        parent.position = new Vector3(10, 0, 0);
        parent.rotation = Quaternion.Euler(0, 90, 0);
        parent.localScale = new Vector3(2, 2, 2);
        var template = new GameObject("Template");
        template.transform.position = new Vector3(0, 0, -5);
        template.AddComponent<ItemProbe>();
        var hidden = new GameObject("Hidden");
        hidden.AddComponent<ItemProbe>();
        hidden.SetActive(false);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
