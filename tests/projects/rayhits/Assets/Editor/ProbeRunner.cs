using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
public static class ProbeRunner
{
    public static void Create()
    {
        var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene,"Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes=new[]{new EditorBuildSettingsScene("Assets/Scenes/Probe.unity",true)};
        AssetDatabase.CreateAsset(RayHitProbe.MakeMesh(2),"Assets/Resources/Mesh.asset");
        AssetDatabase.SaveAssets();
    }
    public static void Record(){EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");EditorApplication.EnterPlaymode();}
}
