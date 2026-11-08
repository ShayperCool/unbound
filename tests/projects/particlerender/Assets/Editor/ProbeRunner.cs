using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>`Unity -batchmode -projectPath tests/projects/particlerender -executeMethod ProbeRunner.Record`
/// with <c>PARTICLE_RENDER_OUT</c> (and <c>PARTICLE_RENDER_IMAGES</c>) set (see run.py): the empty
/// scene in Play mode, where <see cref="ParticleRenderProbe"/> records the values and exits the Editor.</summary>
public static class ProbeRunner
{
    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
