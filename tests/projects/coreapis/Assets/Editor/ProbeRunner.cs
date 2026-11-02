using System;
using Object = UnityEngine.Object;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>`Create` makes the assets and the scene once (the Editor writes them). A windowed Editor
/// started with CORE_APIS_OUT set opens the scene at a 320x240 Game view and enters Play mode, where
/// <see cref="CoreProbe"/> writes the record; the Editor then quits from Edit mode. A watchdog quits
/// with code 4 when Play mode does not finish in time. `Record` does the same in batch mode, where
/// the Game view does not render.</summary>
[InitializeOnLoad]
public static class ProbeRunner
{
    const double WatchdogSeconds = 600;

    static ProbeRunner()
    {
        string output = Environment.GetEnvironmentVariable("CORE_APIS_OUT");
        bool unbound = typeof(Camera).Assembly.GetType("Unbound.Runtime.FrameBuilder") != null;
        if (output == null || Application.isBatchMode || unbound) return;
        if (SessionState.GetBool("CoreProbe.finished", false))
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.delayCall += () => EditorApplication.Exit(0);
            return;
        }
        if (EditorApplication.isPlaying)
        {
            double start = EditorApplication.timeSinceStartup;
            EditorApplication.update += () =>
            {
                if (EditorApplication.timeSinceStartup - start < WatchdogSeconds) return;
                File.WriteAllText(output + ".watchdog", "Play mode did not finish\n");
                EditorApplication.Exit(4);
            };
            CoreProbe.finished = () =>
            {
                // Quit from Edit mode: the Editor does not always quit from inside Play mode.
                SessionState.SetBool("CoreProbe.finished", true);
                EditorApplication.playModeStateChanged += state =>
                {
                    if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.Exit(0);
                };
                EditorApplication.ExitPlaymode();
            };
            return;
        }
        if (!SessionState.GetBool("CoreProbe.started", false))
            EditorApplication.delayCall += Begin;
    }

    static void Begin()
    {
        SessionState.SetBool("CoreProbe.started", true);
        int ticks = 0;
        EditorApplication.CallbackFunction wait = null;
        wait = () =>
        {
            if (++ticks < 30) return;
            EditorApplication.update -= wait;
            try
            {
                EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
                PlayModeWindow.SetCustomRenderingResolution(320, 240, "CoreProbe");
                PlayModeWindow.SetPlayModeFocused(true);
                EditorApplication.EnterPlaymode();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                EditorApplication.Exit(6);
            }
        };
        EditorApplication.update += wait;
    }

    public static void Create()
    {
        Directory.CreateDirectory("Assets/Resources");
        Directory.CreateDirectory("Assets/Scenes");
        Directory.CreateDirectory("Assets/Data");

        var item = new GameObject("Item");
        item.transform.localPosition = new Vector3(1, 2, 3);
        item.AddComponent<Msg>();
        var itemChild = new GameObject("ItemChild");
        itemChild.transform.SetParent(item.transform, false);
        itemChild.AddComponent<Msg>();
        PrefabUtility.SaveAsPrefabAsset(item, "Assets/Resources/Item.prefab");
        Object.DestroyImmediate(item);

        var mat = new Material(Shader.Find("Unlit/Color")) { name = "LazyMat" };
        AssetDatabase.CreateAsset(mat, "Assets/Data/LazyMat.mat");
        var gone = new Material(Shader.Find("Unlit/Color")) { name = "Gone" };
        AssetDatabase.CreateAsset(gone, "Assets/Data/Gone.mat");
        var data = ScriptableObject.CreateInstance<LazyData>();
        data.value = 7;
        AssetDatabase.CreateAsset(data, "Assets/Data/LazyData.asset");
        var lazyPrefab = new GameObject("LazyPrefab");
        var prefabAsset = PrefabUtility.SaveAsPrefabAsset(lazyPrefab, "Assets/Data/LazyPrefab.prefab");
        Object.DestroyImmediate(lazyPrefab);
        AssetDatabase.SaveAssets();

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("CoreProbe").AddComponent<CoreProbe>();

        var holder = new GameObject("Holder").AddComponent<LazyHolder>();
        holder.mat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Data/LazyMat.mat");
        holder.data = AssetDatabase.LoadAssetAtPath<LazyData>("Assets/Data/LazyData.asset");
        holder.prefab = prefabAsset;
        holder.broken = AssetDatabase.LoadAssetAtPath<Material>("Assets/Data/Gone.mat");
        holder.mats = new LazyLoadReference<Material>[] { holder.mat, default };
        holder.pose = new Pose(new Vector3(0.1f, -2.5f, 1e-3f), new Quaternion(0.1f, 0.2f, 0.3f, 0.9273618f));
        holder.poses = new[] { Pose.identity, new Pose(new Vector3(1, 2, 3), new Quaternion(0, 0.70710677f, 0, 0.70710677f)) };

        var a = Make("A", null);
        Make("A1", a.transform);
        Make("B", null).SetActive(false);
        Make("C", null);
        var d = Make("D", null);
        Make("D1", d.transform);
        d.SetActive(false);
        var e = Make("E", null);
        var f = Make("F", null);
        e.GetComponent<Msg>().deactivateOnDisable = f;

        var srcParent = new GameObject("SrcParent");
        srcParent.transform.localPosition = new Vector3(1, 2, 3);
        srcParent.transform.localRotation = Quaternion.Euler(0, 30, 0);
        srcParent.transform.localScale = new Vector3(2, 2, 2);
        var src = Make("Src", srcParent.transform);
        src.transform.localPosition = new Vector3(1, 0, 0);
        src.transform.localRotation = Quaternion.Euler(10, 0, 0);
        src.transform.localScale = new Vector3(1, 1, 0.5f);
        var srcChild = Make("SrcChild", src.transform);
        src.GetComponent<Msg>().target = srcChild;
        Make("SrcHidden", src.transform).SetActive(false);
        Make("SrcOff", null).SetActive(false);

        var screenCam = new GameObject("ScreenCam").AddComponent<Camera>();
        screenCam.transform.position = new Vector3(0, 0, -10);
        screenCam.gameObject.AddComponent<RenderWatch>();
        var rtCam = new GameObject("RtCam").AddComponent<Camera>();
        rtCam.transform.position = new Vector3(0, 0, -10);
        rtCam.depth = -5;
        rtCam.gameObject.AddComponent<RenderWatch>();
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.name = "Cube";
        // The probe has no physics module.
        Object.DestroyImmediate(cube.GetComponent<Collider>());
        cube.AddComponent<RenderWatch>();
        new GameObject("Canvas").AddComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;

        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();

        // The scene keeps a reference to a deleted asset, and a texture field that names the
        // material (Unity's own YAML, edited as text).
        string matGuid = AssetDatabase.AssetPathToGUID("Assets/Data/LazyMat.mat");
        AssetDatabase.DeleteAsset("Assets/Data/Gone.mat");
        var path = "Assets/Scenes/Probe.unity";
        var text = File.ReadAllText(path);
        text = text.Replace("  wrongType: {fileID: 0}", "  wrongType: {fileID: 2100000, guid: " + matGuid + ", type: 2}");
        File.WriteAllText(path, text);
        AssetDatabase.ImportAsset(path);
    }

    static GameObject Make(string name, Transform parent)
    {
        var go = new GameObject(name);
        if (parent != null) go.transform.SetParent(parent, false);
        go.AddComponent<Msg>();
        return go;
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
