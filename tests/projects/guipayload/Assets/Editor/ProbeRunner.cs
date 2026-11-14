using UnityEditor;
using UnityEditor.SceneManagement;
public static class ProbeRunner
{
    public static void Record()
    {
        var skin = UnityEngine.ScriptableObject.CreateInstance<UnityEngine.GUISkin>();
        skin.name = "yaml-skin";
        skin.box = DeclarationGuiPayloadControls.Make();
        skin.customStyles = new[] { DeclarationGuiPayloadControls.Make() };
        AssetDatabase.CreateAsset(skin, "Assets/Payload.guiskin");
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
