using System;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// <c>-executeMethod ProbeRunner.Create</c> makes the probe scene: a <see cref="SpriteImportProbe"/>
/// holding every sprite of Assets/Sprites (by texture path, then sprite name), and exits the Editor
/// (1 on failure). <c>-executeMethod ProbeRunner.Record</c> with <c>SPRITE_IMPORT_PROBE_OUT</c> set
/// opens it and enters Play mode, where the probe records the reference and exits the Editor (1 when
/// recording fails).
/// </summary>
public static class ProbeRunner
{
    public static void Create()
    {
        try
        {
            CreateScene();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
            return;
        }
        EditorApplication.Exit(0);
    }

    static void CreateScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var probe = new GameObject("SpriteImportProbe").AddComponent<SpriteImportProbe>();
        probe.sprites = AssetDatabase.FindAssets("t:Texture2D", new[] { "Assets/Sprites" })
            .Select(AssetDatabase.GUIDToAssetPath)
            .OrderBy(p => p, StringComparer.Ordinal)
            .SelectMany(p => AssetDatabase.LoadAllAssetsAtPath(p).OfType<Sprite>().OrderBy(s => s.name, StringComparer.Ordinal))
            .ToArray();
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }

    public static void Record()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
            EditorApplication.EnterPlaymode();
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }
}
