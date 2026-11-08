using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
[InitializeOnLoad]
public static class DependencyRunner
{
    static DependencyRunner()
    {
        if (Environment.GetEnvironmentVariable("FLARE_DEPENDENCY_OUT") == null ||
            Environment.GetEnvironmentVariable("FLARE_DEPENDENCY_ENGINE") == "1") return;
        EditorApplication.playModeStateChanged += state => {
            if (state != PlayModeStateChange.EnteredPlayMode) return;
            DependencyProbe.Finished = () => EditorApplication.Exit(0);
            DependencyProbe.ManualRenderOverride = camera => {
                bool previous = ShaderUtil.allowAsyncCompilation;
                bool synchronous = Environment.GetEnvironmentVariable("FLARE_DEPENDENCY_SYNC_SHADERS") == "1";
                if (synchronous) ShaderUtil.allowAsyncCompilation = false;
                string trace = Path.Combine(Environment.GetEnvironmentVariable("FLARE_DEPENDENCY_OUT"), "shader-trace.txt");
                File.AppendAllText(trace, "before=" + ShaderUtil.anythingCompiling + ", sync=" + synchronous);
                try { camera.Render(); }
                finally { ShaderUtil.allowAsyncCompilation = previous; }
                File.AppendAllText(trace, ", after=" + ShaderUtil.anythingCompiling + "\n");
            };
        };
    }
    public static void Run()
    {
        EditorSceneManager.OpenScene("Assets/Probe.unity", OpenSceneMode.Single);
        EditorApplication.isPlaying = true;
    }
}
