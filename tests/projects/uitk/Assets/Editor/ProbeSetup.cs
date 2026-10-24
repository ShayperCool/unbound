using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UIElements;

/// <summary>
/// `Unity -batchmode -projectPath tests/projects/uitk -executeMethod ProbeSetup.Create -quit`: makes the
/// runtime theme (Unity's template: an import of the default theme), the panel settings, the textures'
/// import settings and the probe scene (a UIDocument with Probe.uxml and the probe script).
/// </summary>
public static class ProbeSetup
{
    public static void Create()
    {
        const string theme = "Assets/UI/UnityDefaultRuntimeTheme.tss";
        File.WriteAllText(theme, "@import url(\"unity-theme://default\");");
        AssetDatabase.ImportAsset(theme);

        foreach (var path in new[] { "Assets/UI/Checker.png", "Assets/UI/Picture.png" })
        {
            var importer = (TextureImporter)AssetImporter.GetAtPath(path);
            importer.textureType = TextureImporterType.Default;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.filterMode = FilterMode.Bilinear;
            importer.wrapMode = TextureWrapMode.Clamp;
            importer.alphaIsTransparency = true;
            importer.npotScale = TextureImporterNPOTScale.None;
            importer.SaveAndReimport();
        }

        AssetDatabase.DeleteAsset("Assets/UI/ProbePanelSettings.asset");
        var settings = ScriptableObject.CreateInstance<PanelSettings>();
        settings.themeStyleSheet = AssetDatabase.LoadAssetAtPath<ThemeStyleSheet>(theme);
        settings.scaleMode = PanelScaleMode.ConstantPixelSize;
        settings.scale = 1;
        AssetDatabase.CreateAsset(settings, "Assets/UI/ProbePanelSettings.asset");
        AssetDatabase.SaveAssets();

        // A new single scene unloads unused assets: load the settings again after it.
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        settings = AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/UI/ProbePanelSettings.asset");
        var go = new GameObject("UIDocument");
        var document = go.AddComponent<UIDocument>();
        var serialized = new SerializedObject(document);
        serialized.FindProperty("m_PanelSettings").objectReferenceValue = settings;
        serialized.FindProperty("sourceAsset").objectReferenceValue = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>("Assets/UI/Probe.uxml");
        serialized.ApplyModifiedPropertiesWithoutUndo();
        var probe = go.AddComponent<UitkProbe>();
        probe.document = document;
        probe.picture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/UI/Picture.png");
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }
}
