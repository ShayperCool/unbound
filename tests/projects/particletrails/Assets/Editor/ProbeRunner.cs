using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>`Unity -batchmode -projectPath tests/projects/particletrails -executeMethod ProbeRunner.Record`
/// with <c>PARTICLE_TRAILS_OUT</c> (and <c>PARTICLE_TRAILS_IMAGES</c>) set (see run.py): the empty
/// scene in Play mode, where <see cref="ParticleTrailProbe"/> records the values and exits the Editor.</summary>
public static class ProbeRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
