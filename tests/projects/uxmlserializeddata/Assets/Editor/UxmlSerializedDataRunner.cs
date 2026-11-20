using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>Re-imports the probe UXML document after scripts compile, opens the empty probe scene,
/// and enters Play mode so UxmlSerializedDataProbe records its values and exits.</summary>
public static class UxmlSerializedDataRunner
{
    public static void Record()
    {
        AssetDatabase.ImportAsset("Assets/Resources/UxmlSerializedDataProbe.uxml",
            ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
