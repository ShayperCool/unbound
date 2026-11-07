using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>`Unity -batchmode -projectPath tests/projects/particleburstloop -executeMethod ProbeRunner.Record`
/// with <c>PARTICLE_BURST_LOOP_OUT</c> set (see run.py): the empty
/// scene in Play mode, where <see cref="BurstLoopProbe"/> records the values and exits the Editor.</summary>
public static class ProbeRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
