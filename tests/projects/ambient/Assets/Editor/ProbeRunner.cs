using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>
/// `Unity -batchmode -quit -projectPath tests/projects/ambient -executeMethod ProbeRunner.Create`
/// makes the probe's assets: a cubemap whose faces are six plain colours and an empty scene with
/// no lighting data. `-executeMethod ProbeRunner.Record` with <c>AMBIENT_PROBE_OUT</c> set plays
/// it, where <see cref="AmbientProbe"/> records and exits.
/// </summary>
public static class ProbeRunner
{
    public static void Create()
    {
        foreach (var path in new[] { "Assets/Sky", "Assets/Resources", "Assets/Scenes" }) AssetDatabase.DeleteAsset(path);
        Directory.CreateDirectory("Assets/Resources");
        Directory.CreateDirectory("Assets/Scenes");
        var cube = new Cubemap(16, TextureFormat.RGBA32, false);
        var colours = new[]
        {
            new Color(1f, 0f, 0f), new Color(0f, 1f, 0f), new Color(0f, 0f, 1f),
            new Color(1f, 1f, 0f), new Color(0f, 1f, 1f), new Color(1f, 0f, 1f),
        };
        for (int face = 0; face < 6; face++)
        {
            var pixels = new Color[16 * 16];
            for (int i = 0; i < pixels.Length; i++) pixels[i] = colours[face];
            cube.SetPixels(pixels, (CubemapFace)face);
        }
        cube.Apply();
        AssetDatabase.CreateAsset(cube, "Assets/Resources/Faces.cubemap");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }

    public static void Record()
    {
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        EditorApplication.EnterPlaymode();
    }
}
