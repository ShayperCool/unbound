using System;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.U2D;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.U2D;

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
            var layouts = AuthorLayouts(rectangle);
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var runner = new GameObject("Probe").AddComponent<SpriteAssetsProbe>();
            runner.sceneSprite = rectangle;
            // A Sprite made from a texture that is not saved: the scene keeps the Sprite with a
            // null texture reference.
            var unsaved = new Texture2D(8, 8) { hideFlags = HideFlags.DontSave };
            var embedded = Sprite.Create(unsaved, new Rect(0, 0, 8, 8), new Vector2(.5f, .5f), 10);
            embedded.name = "Embedded";
            runner.embeddedSprite = embedded;
            AuthorLayoutScene(layouts, embedded);
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

    static void ImportSprite(string path, int w, int h, Func<int, int, Color32> pixel, SpriteMeshType meshType,
        Action<TextureImporter> configure = null)
    {
        var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
        var pixels = new Color32[w * h];
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++) pixels[y * w + x] = pixel(x, y);
        tex.SetPixels32(pixels);
        tex.Apply();
        File.WriteAllBytes(path, tex.EncodeToPNG());
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        importer.textureType = TextureImporterType.Sprite;
        importer.spriteImportMode = SpriteImportMode.Single;
        importer.spritePixelsPerUnit = 40;
        var settings = new TextureImporterSettings();
        importer.ReadTextureSettings(settings);
        settings.spriteMeshType = meshType;
        importer.SetTextureSettings(settings);
        importer.filterMode = FilterMode.Point;
        importer.mipmapEnabled = false;
        importer.textureCompression = TextureImporterCompression.Uncompressed;
        importer.isReadable = true;
        configure?.Invoke(importer);
        importer.SaveAndReimport();
    }

    static Sprite Save(Sprite source, string name)
    {
        var clone = UnityEngine.Object.Instantiate(source);
        clone.name = name;
        AssetDatabase.CreateAsset(clone, "Assets/Resources/Layouts/" + name + ".asset");
        return clone;
    }

    /// <summary>Saved Sprite layouts beyond plain clones: clones of atlas-packed (tight, rotation
    /// allowed) sprites, a skinned clone, one with colour, normal and tangent channels, one of a
    /// downscaled texture, one with a secondary texture, and a vertex data version 2 copy.</summary>
    static Sprite[] AuthorLayouts(Sprite rectangle)
    {
        Directory.CreateDirectory("Assets/Resources/Layouts");
        Color32 Disc(int x, int y, int w, int h) => new Color32((byte)(x * 255 / w), (byte)(y * 255 / h), 150,
            (byte)((x - w / 2) * (x - w / 2) + (y - h / 2) * (y - h / 2) < w * w / 5 ? 255 : 0));
        ImportSprite("Assets/Resources/Atlased.png", 64, 64, (x, y) => Disc(x, y, 64, 64), SpriteMeshType.Tight);
        ImportSprite("Assets/Resources/Strip.png", 12, 48, (x, y) => new Color32(240, (byte)(y * 5), 60, 255), SpriteMeshType.FullRect);
        ImportSprite("Assets/Resources/Big.png", 128, 128, (x, y) => Disc(x, y, 128, 128), SpriteMeshType.Tight,
            importer => importer.maxTextureSize = 64);
        ImportSprite("Assets/Resources/LitNormal.png", 32, 32, (x, y) => new Color32(128, 128, 255, 255), SpriteMeshType.FullRect,
            importer => importer.textureType = TextureImporterType.Default);
        var normal = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/LitNormal.png");
        ImportSprite("Assets/Resources/Lit.png", 32, 32, (x, y) => new Color32(90, (byte)(x * 8), 200, 255), SpriteMeshType.FullRect,
            importer => importer.secondarySpriteTextures = new[] { new SecondarySpriteTexture { name = "_NormalMap", texture = normal } });

        var atlas = new SpriteAtlas();
        atlas.SetPackingSettings(new SpriteAtlasPackingSettings { enableRotation = true, enableTightPacking = true, padding = 2, blockOffset = 1 });
        atlas.SetTextureSettings(new SpriteAtlasTextureSettings { readable = true, generateMipMaps = false, filterMode = FilterMode.Point, sRGB = true });
        atlas.Add(new UnityEngine.Object[] { AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Atlased.png"),
            AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Strip.png") });
        AssetDatabase.CreateAsset(atlas, "Assets/Layouts.spriteatlas");
        var packerMode = EditorSettings.spritePackerMode;
        EditorSettings.spritePackerMode = SpritePackerMode.AlwaysOnAtlas;
        AssetDatabase.SaveAssets();
        SpriteAtlasUtility.PackAllAtlases(EditorUserBuildSettings.activeBuildTarget);
        var atlasTight = Save(AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Atlased.png"), "AtlasTight");
        var atlasStrip = Save(AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Strip.png"), "AtlasStrip");
        EditorSettings.spritePackerMode = packerMode;

        var imported = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Imported.png");
        var skinned = UnityEngine.Object.Instantiate(imported);
        skinned.name = "Skinned";
        skinned.SetBones(new[] {
            new SpriteBone { name = "root", guid = "b0", position = new Vector3(-.2f, -.1f, 0), rotation = Quaternion.Euler(0, 0, 10), length = .3f, parentId = -1, color = Color.red },
            new SpriteBone { name = "tip", guid = "b1", position = new Vector3(.3f, 0, 0), rotation = Quaternion.Euler(0, 0, 30), length = .2f, parentId = 0, color = Color.green } });
        var bind = new NativeArray<Matrix4x4>(2, Allocator.Temp);
        bind[0] = Matrix4x4.TRS(new Vector3(.2f, .1f, 0), Quaternion.Euler(0, 0, -10), Vector3.one);
        bind[1] = Matrix4x4.TRS(new Vector3(-.5f, .05f, 0), Quaternion.Euler(0, 0, -40), Vector3.one);
        skinned.SetBindPoses(bind);
        var weights = new NativeArray<BoneWeight>(skinned.GetVertexCount(), Allocator.Temp);
        for (int i = 0; i < weights.Length; i++)
            weights[i] = new BoneWeight { boneIndex0 = i % 2, weight0 = 1f - i / (float)weights.Length, boneIndex1 = 1 - i % 2, weight1 = i / (float)weights.Length };
        skinned.SetVertexAttribute(VertexAttribute.BlendWeight, weights);
        AssetDatabase.CreateAsset(skinned, "Assets/Resources/Layouts/Skinned.asset");

        var attributes = UnityEngine.Object.Instantiate(imported);
        attributes.name = "Attributes";
        int count = attributes.GetVertexCount();
        var colors = new NativeArray<Color32>(count, Allocator.Temp);
        var normals = new NativeArray<Vector3>(count, Allocator.Temp);
        var tangents = new NativeArray<Vector4>(count, Allocator.Temp);
        for (int i = 0; i < count; i++)
        {
            colors[i] = new Color32((byte)(40 + i * 21), (byte)(255 - i * 13), 90, (byte)(255 - i * 9));
            normals[i] = new Vector3(i * .1f, -.5f, -1f);
            tangents[i] = new Vector4(1f, i * .05f, 0f, i % 2 == 0 ? -1f : 1f);
        }
        attributes.SetVertexAttribute(VertexAttribute.Color, colors);
        attributes.SetVertexAttribute(VertexAttribute.Normal, normals);
        attributes.SetVertexAttribute(VertexAttribute.Tangent, tangents);
        AssetDatabase.CreateAsset(attributes, "Assets/Resources/Layouts/Attributes.asset");

        var downscaled = Save(AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Big.png"), "Downscaled");
        var secondary = Save(AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Lit.png"), "Secondary");

        // The vertex data version an earlier Editor wrote (serialized version 2), same layout.
        AssetDatabase.SaveAssets();
        var text = File.ReadAllText("Assets/Resources/Rectangle.asset")
            .Replace("m_Name: Rectangle", "m_Name: VertexV2")
            .Replace("m_VertexData:\n      serializedVersion: 3", "m_VertexData:\n      serializedVersion: 2");
        if (!text.Contains("m_VertexData:\n      serializedVersion: 2")) throw new Exception("no vertex data version to rewrite");
        File.WriteAllText("Assets/Resources/Layouts/VertexV2.asset", text);
        AssetDatabase.ImportAsset("Assets/Resources/Layouts/VertexV2.asset");
        var v2 = AssetDatabase.LoadAssetAtPath<Sprite>("Assets/Resources/Layouts/VertexV2.asset");
        return new[] { atlasTight, atlasStrip, skinned, attributes, downscaled, secondary, v2 };
    }

    /// <summary>The layout sprites' renderers, seen by a second camera away from the first one's view.</summary>
    static void AuthorLayoutScene(Sprite[] layouts, Sprite embedded)
    {
        var camera = new GameObject("LayoutCamera").AddComponent<Camera>();
        camera.transform.position = new Vector3(20, 0, -10);
        camera.orthographic = true;
        camera.orthographicSize = 2f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color32(40, 52, 70, 255);
        camera.allowHDR = false;
        camera.allowMSAA = false;
        var sprites = new Sprite[layouts.Length + 1];
        layouts.CopyTo(sprites, 0);
        sprites[layouts.Length] = embedded;
        for (int i = 0; i < sprites.Length; i++)
        {
            var renderer = new GameObject(sprites[i].name + "Renderer").AddComponent<SpriteRenderer>();
            renderer.sprite = sprites[i];
            renderer.transform.position = new Vector3(20 - 2.25f + 1.5f * (i % 4), i < 4 ? 1f : -1f, 0);
            renderer.transform.localScale = new Vector3(.8f, .8f, 1);
        }
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
