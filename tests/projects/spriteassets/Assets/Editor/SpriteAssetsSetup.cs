using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class SpriteAssetsSetup
{
    public static void Author()
    {
        try
        {
            EditorSettings.serializationMode = SerializationMode.ForceText;
            Directory.CreateDirectory("Assets/Resources");
            var tex = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            var pixels = new Color32[64 * 64];
            for (int y = 0; y < 64; y++) for (int x = 0; x < 64; x++)
                pixels[y * 64 + x] = new Color32((byte)(x < 32 ? 220 : 30), (byte)(y < 32 ? 65 : 190), (byte)(((x / 8 + y / 8) % 2) * 140 + 30), (byte)(x+y > 90 || x < y/3 ? 0 : 255));
            tex.SetPixels32(pixels);
            tex.Apply();
            File.WriteAllBytes("Assets/Resources/Imported.png", tex.EncodeToPNG());
            AssetDatabase.ImportAsset("Assets/Resources/Imported.png");
            var importer = (TextureImporter)AssetImporter.GetAtPath("Assets/Resources/Imported.png");
            importer.textureType = TextureImporterType.Sprite;
            importer.spriteImportMode = SpriteImportMode.Single;
            importer.spritePixelsPerUnit = 40;
            importer.spritePivot = new Vector2(.7f, .3f);
            var settings = new TextureImporterSettings();
            importer.ReadTextureSettings(settings);
            settings.spriteAlignment = (int)SpriteAlignment.Custom;
            settings.spritePivot = new Vector2(.7f, .3f);
            settings.spriteMeshType = SpriteMeshType.Tight;
            importer.SetTextureSettings(settings);
            importer.filterMode = FilterMode.Point;
            importer.mipmapEnabled = false;
            importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.isReadable = true;
            importer.SaveAndReimport();
            tex = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Imported.png");
            var rectangle = Sprite.Create(tex, new Rect(8, 12, 40, 32), new Vector2(.2f, .75f), 24, 0, SpriteMeshType.FullRect, new Vector4(3, 5, 7, 9), true);
            rectangle.name = "Rectangle";
            AssetDatabase.CreateAsset(rectangle, "Assets/Resources/Rectangle.asset");
            var polygon = UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Imported.png"));
            polygon.name = "Polygon";
            AssetDatabase.CreateAsset(polygon, "Assets/Resources/Polygon.asset");
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var runner = new GameObject("Probe").AddComponent<SpriteAssetsProbe>();
            runner.sceneSprite = rectangle;
            var camera = new GameObject("Camera").AddComponent<Camera>();
            camera.transform.position = new Vector3(0, 0, -10);
            camera.orthographic = true;
            camera.orthographicSize = 1.8f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color32(12, 18, 27, 255);
            camera.allowHDR = false;
            camera.allowMSAA = false;
            var left = new GameObject("RectangleRenderer").AddComponent<SpriteRenderer>();
            left.sprite = rectangle;
            left.transform.position = new Vector3(-1.6f, .3f, 0);
            var right = new GameObject("PolygonRenderer").AddComponent<SpriteRenderer>();
            right.sprite = polygon;
            right.transform.position = new Vector3(1.4f, -.3f, 0);
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
            GameObject.Find("Probe").GetComponent<SpriteAssetsProbe>().Record();
            EditorApplication.Exit(0);
        }
        catch (Exception e) { Debug.LogError(e); EditorApplication.Exit(1); }
    }
}
