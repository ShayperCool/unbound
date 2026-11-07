using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>`Unity -batchmode -projectPath tests/projects/particletriggerapi -executeMethod ProbeRunner.Record`
/// with <c>PARTICLE_TRIGGER_API_OUT</c> set (see run.py): the empty
/// scene in Play mode, where <see cref="TriggerApiProbe"/> records the values and exits the Editor.</summary>
public static class ProbeRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
