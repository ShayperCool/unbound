using System;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// <c>Create</c> explicitly authors the serialized mesh and prefab fixture. With
/// <c>SKINNEDBOUNDS_PROBE_OUT</c> set, a windowed Editor (an X server of its own, the GPU through
/// VirtualGL) closes every Scene view (a Scene view camera also makes renderers visible), opens
/// the probe scene at a 192x108 Game view, enters Play mode and quits when
/// <see cref="SkinnedBoundsProbe"/> has written its record (nonzero on failure). A watchdog quits
/// with code 4 when Play mode does not finish in time.
/// </summary>
[InitializeOnLoad]
public static class ProbeRunner
{
    const double WatchdogSeconds = 600;

    public static void Create()
    {
        var rig = new GameObject("SerializedRig");
        var bone = new GameObject("Bone").transform;
        bone.SetParent(rig.transform,false);
        bone.localPosition = new Vector3(3,4,0);
        var mesh = new Mesh { name="SerializedSkin" };
        mesh.vertices = new[] { new Vector3(-.5f,-.5f,0), new Vector3(.5f,-.5f,0), new Vector3(-.5f,.5f,0) };
        mesh.triangles = new[] {0,2,1};
        mesh.boneWeights = new[] { new BoneWeight {weight0=1}, new BoneWeight {weight0=1}, new BoneWeight {weight0=1} };
        mesh.bindposes = new[] {Matrix4x4.identity};
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh,"Assets/Resources/SerializedSkin.asset");
        var skin = rig.AddComponent<SkinnedMeshRenderer>();
        skin.sharedMesh=mesh;
        skin.bones=new[] {bone};
        skin.rootBone=bone;
        skin.localBounds=new Bounds(new Vector3(1,2,0),new Vector3(4,6,2));
        skin.enabled=false;
        PrefabUtility.SaveAsPrefabAsset(rig,"Assets/Resources/SerializedRig.prefab");
        UnityEngine.Object.DestroyImmediate(rig);
        AssetDatabase.SaveAssets();
    }

    static ProbeRunner()
    {
        string output = Environment.GetEnvironmentVariable("SKINNEDBOUNDS_PROBE_OUT");
        // Unbound runs the probe by itself and quits through EditorApplication.Exit.
        bool unbound = typeof(Camera).Assembly.GetType("Unbound.Runtime.FrameBuilder") != null;
        if (output == null || Application.isBatchMode || unbound) return;
        if (SessionState.GetBool("SkinnedBounds.finished", false))
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.delayCall += () => EditorApplication.Exit(SessionState.GetInt("SkinnedBounds.code", 5));
            return;
        }
        if (EditorApplication.isPlaying)
        {
            double start = EditorApplication.timeSinceStartup;
            EditorApplication.update += () =>
            {
                if (EditorApplication.timeSinceStartup - start < WatchdogSeconds) return;
                File.WriteAllText(Path.Combine(output, "watchdog.txt"), "Play mode did not finish\n");
                EditorApplication.Exit(4);
            };
            SkinnedBoundsProbe.finished = code =>
            {
                // Quit from Edit mode: the Editor does not always quit from inside Play mode.
                SessionState.SetBool("SkinnedBounds.finished", true);
                SessionState.SetInt("SkinnedBounds.code", code);
                EditorApplication.playModeStateChanged += state =>
                {
                    if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.Exit(code);
                };
                EditorApplication.ExitPlaymode();
            };
            return;
        }
        if (!EditorApplication.isPlayingOrWillChangePlaymode && !SessionState.GetBool("SkinnedBounds.started", false))
            EditorApplication.delayCall += Begin;
    }

    static void Begin()
    {
        SessionState.SetBool("SkinnedBounds.started", true);
        int ticks = 0;
        EditorApplication.CallbackFunction wait = null;
        wait = () =>
        {
            if (++ticks < 30) return;
            EditorApplication.update -= wait;
            try
            {
                EditorSettings.asyncShaderCompilation = false;
                EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
                foreach (var view in Resources.FindObjectsOfTypeAll<SceneView>()) view.Close();
                PlayModeWindow.SetCustomRenderingResolution(192, 108, "SkinnedBounds");
                PlayModeWindow.SetPlayModeFocused(true);
                Debug.Log("[skinnedbounds] entering Play mode, scene views open: " + Resources.FindObjectsOfTypeAll<SceneView>().Length);
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
}
