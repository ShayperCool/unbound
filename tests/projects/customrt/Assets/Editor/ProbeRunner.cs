using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// Records the probe in a windowed Editor (an X server of its own, the GPU through VirtualGL):
/// with <c>CUSTOMRT_PROBE_OUT</c> set, the Editor opens the probe scene at a 64x32 game view,
/// enters Play mode and quits when <see cref="CustomRtProbe"/> has written its record (nonzero on
/// failure). A watchdog quits with code 4 when Play mode does not finish in time.
/// </summary>
[InitializeOnLoad]
public static class ProbeRunner
{
    const double WatchdogSeconds = 600;

    /// <summary>Makes the probe's empty scene, its materials and its `.asset` custom render
    /// textures (batchmode, -quit).</summary>
    public static void Create()
    {
        Directory.CreateDirectory("Assets/Scenes");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        var counterMaterial = new Material(Shader.Find("CustomRT/Self"));
        AssetDatabase.CreateAsset(counterMaterial, "Assets/Resources/CrtCounter.mat");
        var coordsMaterial = new Material(Shader.Find("CustomRT/Coords"));
        AssetDatabase.CreateAsset(coordsMaterial, "Assets/Resources/CrtCoords.mat");
        var counter = new CustomRenderTexture(4, 2, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
        {
            filterMode = FilterMode.Point,
            initializationColor = new Color(0.5f, 0, 0, 0),
            initializationMode = CustomRenderTextureUpdateMode.OnLoad,
            updateMode = CustomRenderTextureUpdateMode.Realtime,
            doubleBuffered = true,
            material = counterMaterial,
        };
        AssetDatabase.CreateAsset(counter, "Assets/Resources/crt.asset.counter.asset");
        var zoned = new CustomRenderTexture(8, 4, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear)
        {
            filterMode = FilterMode.Point,
            initializationColor = new Color(-1, -1, -1, -1),
            initializationMode = CustomRenderTextureUpdateMode.OnLoad,
            updateMode = CustomRenderTextureUpdateMode.OnLoad,
            updatePeriod = 0.5f,
            material = coordsMaterial,
            shaderPass = 1,
            wrapUpdateZones = true,
            updateZoneSpace = CustomRenderTextureUpdateZoneSpace.Pixel,
            cubemapFaceMask = 5,
        };
        zoned.SetUpdateZones(new[]
        {
            new CustomRenderTextureUpdateZone { updateZoneCenter = new Vector3(2, 2, 0), updateZoneSize = new Vector3(4, 4, 1), rotation = 0, passIndex = 0 },
            new CustomRenderTextureUpdateZone { updateZoneCenter = new Vector3(7, 1, 0), updateZoneSize = new Vector3(4, 2, 1), rotation = 20, passIndex = 1, needSwap = true },
        });
        AssetDatabase.CreateAsset(zoned, "Assets/Resources/crt.asset.zoned.asset");
        AssetDatabase.SaveAssets();
    }

    static ProbeRunner()
    {
        string output = Environment.GetEnvironmentVariable("CUSTOMRT_PROBE_OUT");
        // Unbound runs the probe by itself and quits through EditorApplication.Exit.
        bool unbound = typeof(Camera).Assembly.GetType("Unbound.Runtime.FrameBuilder") != null;
        if (output == null || Application.isBatchMode || unbound) return;
        if (SessionState.GetBool("CustomRtProbe.finished", false))
        {
            if (!EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.delayCall += () => EditorApplication.Exit(SessionState.GetInt("CustomRtProbe.code", 5));
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
            CustomRtProbe.finished = code =>
            {
                // Quit from Edit mode: the Editor does not always quit from inside Play mode.
                SessionState.SetBool("CustomRtProbe.finished", true);
                SessionState.SetInt("CustomRtProbe.code", code);
                EditorApplication.playModeStateChanged += state =>
                {
                    if (state == PlayModeStateChange.EnteredEditMode) EditorApplication.Exit(code);
                };
                EditorApplication.ExitPlaymode();
            };
            return;
        }
        if (!EditorApplication.isPlayingOrWillChangePlaymode && !SessionState.GetBool("CustomRtProbe.started", false))
            EditorApplication.delayCall += Begin;
    }

    static void Begin()
    {
        SessionState.SetBool("CustomRtProbe.started", true);
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
                PlayModeWindow.SetCustomRenderingResolution(64, 32, "CustomRtProbe");
                PlayModeWindow.SetPlayModeFocused(true);
                Debug.Log("[customrt] entering Play mode");
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
