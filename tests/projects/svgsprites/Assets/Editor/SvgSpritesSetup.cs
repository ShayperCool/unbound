using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SvgSpritesSetup
{
    public static void Author()
    {
        try
        {
            EditorSettings.serializationMode = SerializationMode.ForceText;
            AssetDatabase.ImportAsset("Assets/Resources/Svg", ImportAssetOptions.ImportRecursive | ImportAssetOptions.ForceUpdate);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            new GameObject("Probe").AddComponent<SvgSpritesProbe>();
            EditorSceneManager.SaveScene(scene, "Assets/Probe.unity");
            EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Probe.unity", true) };
            PlayerSettings.colorSpace = ColorSpace.Gamma;
            QualitySettings.antiAliasing = 0;
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogError(e); EditorApplication.Exit(1); }
    }

    public static void Record()
    {
        try
        {
            EditorSceneManager.OpenScene("Assets/Probe.unity");
            GameObject.Find("Probe").GetComponent<SvgSpritesProbe>().Record();
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogError(e); EditorApplication.Exit(1); }
    }
}
