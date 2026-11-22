using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// `-executeMethod ProbeRunner.Create` makes the probe's inputs once: a checker texture, unlit
/// materials of it and the probe scene (a textured ground, a wall, posts and a camera).
/// `-executeMethod ProbeRunner.Record` plays the scene, where <see cref="StpMotionProbe"/> records.
/// </summary>
public static class ProbeRunner
{
    public static void Create()
    {
        foreach (var path in new[] { "Assets/Textures", "Assets/Materials", "Assets/Scenes/Probe.unity" })
            AssetDatabase.DeleteAsset(path);
        Directory.CreateDirectory("Assets/Textures");
        Directory.CreateDirectory("Assets/Materials");

        // A 64x64 checker of 4-texel squares with a thin diagonal line: edges in every direction.
        var pixels = new Color32[64 * 64];
        for (int y = 0; y < 64; y++)
            for (int x = 0; x < 64; x++)
            {
                bool dark = ((x >> 2) + (y >> 2) & 1) == 0;
                bool line = x == y || x == 63 - y;
                pixels[y * 64 + x] = line ? new Color32(230, 40, 30, 255) : dark ? new Color32(30, 40, 60, 255) : new Color32(235, 225, 200, 255);
            }
        var checker = new Texture2D(64, 64, TextureFormat.RGBA32, false);
        checker.SetPixels32(pixels);
        File.WriteAllBytes("Assets/Textures/Checker.png", checker.EncodeToPNG());
        AssetDatabase.ImportAsset("Assets/Textures/Checker.png");

        var unlit = Shader.Find("Universal Render Pipeline/Unlit");
        var texture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Textures/Checker.png");
        Material Make(string name, Color color, Vector2 tiling)
        {
            var material = new Material(unlit);
            material.SetTexture("_BaseMap", texture);
            material.SetTextureScale("_BaseMap", tiling);
            material.SetColor("_BaseColor", color);
            AssetDatabase.CreateAsset(material, "Assets/Materials/" + name + ".mat");
            return material;
        }
        var ground = Make("Ground", Color.white, new Vector2(6f, 6f));
        var wall = Make("Wall", new Color(0.8f, 0.9f, 1f), new Vector2(3f, 1f));
        var post = Make("Post", new Color(1f, 0.7f, 0.4f), new Vector2(1f, 4f));

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var plane = GameObject.CreatePrimitive(PrimitiveType.Plane);
        plane.name = "Ground";
        plane.transform.localScale = new Vector3(3f, 1f, 3f);
        plane.GetComponent<MeshRenderer>().sharedMaterial = ground;
        var back = GameObject.CreatePrimitive(PrimitiveType.Quad);
        back.name = "Wall";
        back.transform.SetPositionAndRotation(new Vector3(0f, 2f, 6f), Quaternion.identity);
        back.transform.localScale = new Vector3(12f, 4f, 1f);
        back.GetComponent<MeshRenderer>().sharedMaterial = wall;
        for (int i = 0; i < 7; i++)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = "Post" + i;
            cube.transform.SetPositionAndRotation(new Vector3(-3f + i, 0.75f, 1f + 0.6f * (i % 3)), Quaternion.Euler(0f, 20f * i, 0f));
            cube.transform.localScale = new Vector3(0.15f, 1.5f, 0.15f);
            cube.GetComponent<MeshRenderer>().sharedMaterial = post;
        }
        var camera = new GameObject("Main Camera") { tag = "MainCamera" }.AddComponent<Camera>();
        camera.transform.SetPositionAndRotation(new Vector3(0f, 1.6f, -4f), Quaternion.Euler(12f, 0f, 0f));
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.25f, 0.3f, 0.35f, 1f);
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = 100f;
        EditorSceneManager.SaveScene(scene, "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        AssetDatabase.SaveAssets();
    }

    public static void Record()
    {
        // The reference must use the real shader variants, not async compilation's placeholders.
        EditorSettings.asyncShaderCompilation = false;
        EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        // The Rendering Debugger scenarios render to the screen: a game view of the probe's size.
        PlayModeWindow.SetCustomRenderingResolution(320, 180, "StpMotionProbe");
        EditorApplication.EnterPlaymode();
    }
}
