using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class ProbeRunner
{
    public static void Record()
    {
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        new GameObject("AssetCloneProbe").AddComponent<AssetCloneProbe>();
        EditorSceneManager.SaveScene(scene,"Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/Scenes/Probe.unity",true)};
        AssetDatabase.SaveAssets(); EditorApplication.EnterPlaymode();
    }
}
