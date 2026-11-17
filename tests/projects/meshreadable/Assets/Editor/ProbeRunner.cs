using UnityEditor;
using UnityEditor.SceneManagement;

/// <summary>`-executeMethod ProbeRunner.Create` (batch mode, once) sets the models' Read/Write
/// Enabled import setting: on for Layers, off for LayersLocked (the same file) and Plain.
/// `-executeMethod ProbeRunner.Record` plays the scene, where <see cref="MeshReadableProbe"/>
/// records.</summary>
public static class ProbeRunner
{
    public static void Create()
    {
        foreach (var (path, readable) in new[] { ("Assets/Resources/Layers.fbx", true), ("Assets/Resources/LayersLocked.fbx", false), ("Assets/Resources/Plain.fbx", false) })
        {
            var importer = (ModelImporter)AssetImporter.GetAtPath(path);
            importer.isReadable = readable;
            importer.SaveAndReimport();
        }
        AssetDatabase.SaveAssets();
    }

    public static void Record()
    {
        EditorSettings.asyncShaderCompilation = false;
        EditorSceneManager.OpenScene("Assets/Scenes/Main.unity");
        EditorApplication.EnterPlaymode();
    }
}
