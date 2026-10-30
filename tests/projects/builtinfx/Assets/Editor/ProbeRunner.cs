using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>`Unity -batchmode -projectPath tests/projects/builtinfx -executeMethod ProbeRunner.Record`
/// with <c>BUILTINFX_PROBE_OUT</c> set: the probe scene in Play mode, where <see cref="FxProbe"/>
/// records and exits the Editor. <see cref="Create"/> made the scene and the assets.</summary>
public static class ProbeRunner
{
    static void Png(string path, int width, int height, Func<int, int, Color> pixel, Action<TextureImporter> settings)
    {
        var t = new Texture2D(width, height, TextureFormat.RGBA32, false);
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                t.SetPixel(x, y, pixel(x, y));
        System.IO.File.WriteAllBytes(path, t.EncodeToPNG());
        UnityEngine.Object.DestroyImmediate(t);
        AssetDatabase.ImportAsset(path);
        var importer = (TextureImporter)AssetImporter.GetAtPath(path);
        settings(importer);
        importer.SaveAndReimport();
    }

    /// <summary>Sets a serialized field, logging the names of the object's fields when it has none
    /// of that name.</summary>
    static SerializedProperty Find(SerializedObject so, string path)
    {
        var p = so.FindProperty(path);
        if (p == null)
        {
            var names = new System.Text.StringBuilder();
            var it = so.GetIterator();
            while (it.Next(true)) names.Append(it.propertyPath).Append(' ');
            Debug.LogError($"ProbeRunner: {so.targetObject.GetType().Name} has no {path}: {names}");
        }
        return p;
    }

    static GameObject Inactive(string name)
    {
        var go = new GameObject(name);
        go.SetActive(false);
        return go;
    }

    public static void Create()
    {
        System.IO.Directory.CreateDirectory("Assets/Scenes");
        System.IO.Directory.CreateDirectory("Assets/Resources");
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

        // Textures: a projector cookie, a flare texture, stripes whose first mip averages to gray.
        Png("Assets/Resources/Cookie.png", 64, 64, (x, y) =>
        {
            float u = (x + 0.5f) / 64f, v = (y + 0.5f) / 64f;
            return Vector2.Distance(new Vector2(u, v), new Vector2(0.5f, 0.5f)) < 0.4f ? new Color(0.2f, 0.2f, 0.2f, 1f) : Color.white;
        }, i =>
        {
            i.wrapMode = TextureWrapMode.Clamp;
            i.mipmapEnabled = false;
        });
        Png("Assets/Resources/FlareTex.png", 128, 128, (x, y) =>
        {
            float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(64f, 64f)) / 64f;
            float a = Mathf.Clamp01(1f - d);
            return new Color(a, a * a, a * 0.5f, a);
        }, i =>
        {
            i.wrapMode = TextureWrapMode.Clamp;
            i.alphaIsTransparency = true;
        });
        Png("Assets/Resources/Stripes.png", 64, 64, (x, y) => (x & 1) == 0 ? Color.white : Color.black, i =>
        {
            i.mipmapEnabled = true;
            i.filterMode = FilterMode.Point;
            i.sRGBTexture = false;
            i.textureCompression = TextureImporterCompression.Uncompressed;
        });
        var cookie = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/Cookie.png");
        var flareTexture = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Resources/FlareTex.png");

        // Materials: a projector's, a skybox, a parent material and its variant.
        var projectorMaterial = new Material(Shader.Find("Projector/Multiply"));
        projectorMaterial.SetTexture("_ShadowTex", cookie);
        AssetDatabase.CreateAsset(projectorMaterial, "Assets/Resources/ProjectorMat.mat");
        var sky = new Material(Shader.Find("Probe/Sky")) { color = new Color(0.4f, 1f, 0.4f) };
        AssetDatabase.CreateAsset(sky, "Assets/Resources/SceneSky.mat");
        var parent = new Material(Shader.Find("Standard"))
        {
            color = new Color(1f, 0f, 0f),
            enableInstancing = true,
            doubleSidedGI = true,
            globalIlluminationFlags = MaterialGlobalIlluminationFlags.EmissiveIsBlack,
            renderQueue = 2100,
        };
        parent.SetFloat("_Metallic", 0.3f);
        parent.SetFloat("_Glossiness", 0.7f);
        parent.mainTexture = cookie;
        parent.mainTextureScale = new Vector2(2f, 3f);
        parent.EnableKeyword("_EMISSION");
        AssetDatabase.CreateAsset(parent, "Assets/Resources/Parent.mat");
        var variant = new Material(parent) { parent = parent };
        variant.color = new Color(0f, 1f, 0f);
        variant.SetFloat("_Glossiness", 0.2f);
        AssetDatabase.CreateAsset(variant, "Assets/Resources/Variant.mat");

        // A flare: its texture in the 1 large + 4 small layout, three elements.
        var flare = new Flare();
        AssetDatabase.CreateAsset(flare, "Assets/Resources/Probe.flare");
        var so = new SerializedObject(flare);
        Find(so, "m_FlareTexture").objectReferenceValue = flareTexture;
        Find(so, "m_TextureLayout").intValue = 0;
        var elements = Find(so, "m_Elements");
        elements.arraySize = 3;
        for (int i = 0; i < 3; i++)
        {
            var e = elements.GetArrayElementAtIndex(i);
            e.FindPropertyRelative("m_ImageIndex").intValue = i;
            e.FindPropertyRelative("m_Position").floatValue = i * 0.5f - 0.2f;
            e.FindPropertyRelative("m_Size").floatValue = 40f - i * 10f;
            e.FindPropertyRelative("m_Color").colorValue = new Color(1f, 1f - 0.3f * i, 0.5f + 0.2f * i, 1f);
            e.FindPropertyRelative("m_UseLightColor").boolValue = i == 1;
            e.FindPropertyRelative("m_Rotate").boolValue = i == 2;
            e.FindPropertyRelative("m_Zoom").boolValue = false;
            e.FindPropertyRelative("m_Fade").boolValue = true;
        }
        so.ApplyModifiedPropertiesWithoutUndo();

        // A billboard asset.
        var billboard = new BillboardAsset { width = 1f, height = 1.5f, bottom = 0f };
        billboard.SetImageTexCoords(new[] { new Vector4(0f, 0f, 1f, 1f), new Vector4(0.5f, 0f, 0.5f, 1f) });
        billboard.SetVertices(new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) });
        billboard.SetIndices(new ushort[] { 0, 1, 2, 0, 2, 3 });
        AssetDatabase.CreateAsset(billboard, "Assets/Resources/Billboard.asset");

        // The scene: a camera with a skybox and a flare layer, a projector, lights with a halo and a
        // flare, a lens flare, a halo, occlusion components, a proxy volume (all inactive but the
        // camera, which is disabled).
        var cameraObject = new GameObject("SceneCamera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.renderingPath = RenderingPath.DeferredShading;
        camera.useOcclusionCulling = false;
        camera.allowDynamicResolution = true;
        camera.forceIntoRenderTexture = true;
        cameraObject.AddComponent<Skybox>().material = sky;
        cameraObject.AddComponent<FlareLayer>();

        var projector = Inactive("SceneProjector").AddComponent<Projector>();
        projector.nearClipPlane = 0.4f;
        projector.farClipPlane = 9f;
        projector.fieldOfView = 25f;
        projector.aspectRatio = 1.25f;
        projector.orthographic = true;
        projector.orthographicSize = 3f;
        projector.ignoreLayers = 1 << 5;
        projector.material = projectorMaterial;

        var haloLight = Inactive("HaloLight");
        haloLight.transform.position = new Vector3(0.3f, 1.4f, -1.2f);
        var l = haloLight.AddComponent<Light>();
        l.type = LightType.Point;
        l.range = 2f;
        l.color = new Color(1f, 0.6f, 0.2f);
        var lso = new SerializedObject(l);
        Find(lso, "m_DrawHalo").boolValue = true;
        lso.ApplyModifiedPropertiesWithoutUndo();

        var sun = Inactive("FlareSun");
        sun.transform.rotation = Quaternion.Euler(15f, 170f, 0f);
        var sunLight = sun.AddComponent<Light>();
        sunLight.type = LightType.Directional;
        sunLight.flare = flare;
        sunLight.color = new Color(1f, 0.9f, 0.7f);

        var lensFlare = Inactive("LensFlareObject");
        lensFlare.transform.position = new Vector3(0.6f, 1.2f, 0f);
        var lf = lensFlare.AddComponent<LensFlare>();
        lf.flare = flare;
        lf.brightness = 0.8f;
        lf.color = new Color(0.6f, 0.8f, 1f);
        lf.fadeSpeed = 4f;

        var halo = Inactive("HaloObject");
        halo.transform.position = new Vector3(-0.6f, 1.2f, 0f);
        var haloType = typeof(Light).Assembly.GetType("UnityEngine.Halo");
        var haloComponent = halo.AddComponent(haloType);
        var hso = new SerializedObject(haloComponent);
        Find(hso, "m_Color").colorValue = new Color(0.3f, 0.8f, 1f, 1f);
        Find(hso, "m_Size").floatValue = 0.8f;
        hso.ApplyModifiedPropertiesWithoutUndo();

        var occlusion = new GameObject("Occlusion");
        var area = occlusion.AddComponent<OcclusionArea>();
        area.center = new Vector3(0f, 1f, 0f);
        area.size = new Vector3(6f, 3f, 6f);
        var portal = occlusion.AddComponent<OcclusionPortal>();
        portal.open = false;

        var volume = Inactive("ProxyVolume").AddComponent<LightProbeProxyVolume>();
        volume.resolutionMode = LightProbeProxyVolume.ResolutionMode.Custom;
        volume.gridResolutionX = 4;
        volume.gridResolutionY = 8;
        volume.gridResolutionZ = 2;

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
