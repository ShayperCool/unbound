using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>`-executeMethod ProbeRunner.Record` with <c>LIGHTMAP_UV_PROBE_OUT</c> set opens the
/// probe scene and enters Play mode, where <see cref="LightmapUvProbe"/> records and exits.</summary>
public static class ProbeRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
