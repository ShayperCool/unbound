using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Authoring (`bake_atlas.py create`, the Editor with <c>-executeMethod AtlasSetup.Create</c> in a
/// private copy): makes the scenes whose baked lightmap layout the probe measures, each with its
/// own Lighting Settings (fast bakes: one sample, no filtering, no compression), and the Lightmap
/// Parameters and meshes they use. The runner copies <c>Assets/Scenes</c> back.
/// </summary>
public static class AtlasSetup
{
    const string Folder = "Assets/Scenes";

    static LightingSettings Settings(string scene, float resolution, int maxSize, int padding)
    {
        var settings = new LightingSettings
        {
            name = scene,
            bakedGI = true,
            realtimeGI = false,
            lightmapper = LightingSettings.Lightmapper.ProgressiveCPU,
            lightmapResolution = resolution,
            lightmapMaxSize = maxSize,
            lightmapPadding = padding,
            directSampleCount = 1,
            indirectSampleCount = 1,
            environmentSampleCount = 8,
            maxBounces = 1,
            filteringMode = LightingSettings.FilterMode.None,
            lightmapCompression = LightmapCompression.None,
            directionalityMode = LightmapsMode.NonDirectional,
            ao = false,
            mixedBakeMode = MixedLightingMode.IndirectOnly,
            prioritizeView = false,
        };
        AssetDatabase.CreateAsset(settings, $"{Folder}/{scene}.lighting");
        return settings;
    }

    static Scene NewScene(string name, float resolution, int maxSize, int padding, Action<LightingSettings> extra = null)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var settings = Settings(name, resolution, maxSize, padding);
        extra?.Invoke(settings);
        Lightmapping.lightingSettings = settings;
        Lightmapping.bakeOnSceneLoad = Lightmapping.BakeOnSceneLoadMode.Never;
        return scene;
    }

    static void Save(Scene scene, string name)
    {
        EditorSceneManager.SaveScene(scene, $"{Folder}/{name}.unity");
    }

    static GameObject Static(GameObject go)
    {
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI);
        return go;
    }

    static GameObject Prim(PrimitiveType type, string name, Vector3 position, Vector3 euler, Vector3 scale, Transform parent = null)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        if (parent != null) go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.transform.localEulerAngles = euler;
        go.transform.localScale = scale;
        return Static(go);
    }

    static GameObject Cube(string name, Vector3 position, float scale = 1f) =>
        Prim(PrimitiveType.Cube, name, position, Vector3.zero, Vector3.one * scale);

    static void SetScaleInLightmap(GameObject go, float scale)
    {
        var so = new SerializedObject(go.GetComponent<MeshRenderer>());
        so.FindProperty("m_ScaleInLightmap").floatValue = scale;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void SetParameters(GameObject go, LightmapParameters parameters)
    {
        var so = new SerializedObject(go.GetComponent<MeshRenderer>());
        so.FindProperty("m_LightmapParameters").objectReferenceValue = parameters;
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>Primitives of every kind, placed, turned and scaled; Scale In Lightmap 0.5, 2 and 0;
    /// renderers the Editor leaves out (not static, disabled, inactive) and one lit by probes; children of
    /// a scaled, turned parent; a LOD Group with a renderer in two levels; positions far away and
    /// beyond -65536.</summary>
    static void Spread()
    {
        var scene = NewScene("Spread", 10f, 1024, 2);
        Cube("CubeA", new Vector3(0f, 0.5f, 0f));
        Prim(PrimitiveType.Cube, "CubeB", new Vector3(3f, 1f, 2f), new Vector3(0f, 30f, 0f), new Vector3(2f, 1f, 1f));
        Prim(PrimitiveType.Cube, "CubeNeg", new Vector3(-4.3f, 0.2f, -7.9f), new Vector3(10f, 20f, 30f), new Vector3(1f, 3f, 0.5f));
        Prim(PrimitiveType.Sphere, "Sphere", new Vector3(5f, 1f, -3f), Vector3.zero, Vector3.one * 1.5f);
        Prim(PrimitiveType.Cylinder, "Cylinder", new Vector3(-2f, 1f, 4f), Vector3.zero, new Vector3(1f, 2f, 1f));
        Prim(PrimitiveType.Capsule, "Capsule", new Vector3(7f, 1f, 6f), Vector3.zero, Vector3.one);
        Prim(PrimitiveType.Plane, "Plane", Vector3.zero, Vector3.zero, new Vector3(3f, 1f, 3f));
        Prim(PrimitiveType.Quad, "Quad", new Vector3(2f, 2f, -6f), new Vector3(0f, 0f, 45f), new Vector3(2f, 1f, 1f));
        SetScaleInLightmap(Cube("Half", new Vector3(9f, 0.5f, 0f)), 0.5f);
        SetScaleInLightmap(Cube("Double", new Vector3(11f, 0.5f, 0f)), 2f);
        SetScaleInLightmap(Cube("Zero", new Vector3(13f, 0.5f, 0f)), 0f);
        GameObjectUtility.SetStaticEditorFlags(Cube("NotStatic", new Vector3(15f, 0.5f, 0f)), 0);
        Cube("Probes", new Vector3(17f, 0.5f, 0f)).GetComponent<MeshRenderer>().receiveGI = ReceiveGI.LightProbes;
        Cube("Disabled", new Vector3(19f, 0.5f, 0f)).GetComponent<MeshRenderer>().enabled = false;
        Cube("Inactive", new Vector3(21f, 0.5f, 0f)).SetActive(false);
        var parent = new GameObject("Parent").transform;
        parent.position = new Vector3(-6f, 0f, -2f);
        parent.eulerAngles = new Vector3(0f, 45f, 0f);
        parent.localScale = new Vector3(2f, 0.5f, 1f);
        Prim(PrimitiveType.Cube, "Child1", new Vector3(0f, 1f, 0f), Vector3.zero, Vector3.one, parent);
        Prim(PrimitiveType.Sphere, "Child2", new Vector3(1f, 0f, 1f), Vector3.zero, Vector3.one * 0.5f, parent);
        var lod = new GameObject("Lod");
        lod.transform.position = new Vector3(4f, 0f, 8f);
        var lod0 = Prim(PrimitiveType.Sphere, "Lod0", Vector3.zero, Vector3.zero, Vector3.one, lod.transform);
        var lod1 = Prim(PrimitiveType.Cube, "Lod1", new Vector3(0f, 0f, 2f), Vector3.zero, Vector3.one, lod.transform);
        var lod2 = Prim(PrimitiveType.Cylinder, "Lod2", new Vector3(0f, 0f, 4f), Vector3.zero, Vector3.one, lod.transform);
        var shared = Prim(PrimitiveType.Cube, "LodShared", new Vector3(2f, 0f, 2f), Vector3.zero, Vector3.one, lod.transform);
        var group = lod.AddComponent<LODGroup>();
        group.SetLODs(new[]
        {
            new LOD(0.6f, new Renderer[] { lod0.GetComponent<Renderer>() }),
            new LOD(0.3f, new Renderer[] { lod1.GetComponent<Renderer>(), shared.GetComponent<Renderer>() }),
            new LOD(0.1f, new Renderer[] { lod2.GetComponent<Renderer>(), shared.GetComponent<Renderer>() }),
        });
        Cube("Far", new Vector3(150.37f, 3f, -220.1f));
        Cube("VeryFar", new Vector3(-70000f, 0f, 5f));
        Save(scene, "Spread");
    }

    /// <summary>Many cubes of many sizes at 20 texels per unit in 256-texel lightmaps: several
    /// atlases, the last one smaller.</summary>
    static void Crowded()
    {
        var scene = NewScene("Crowded", 20f, 256, 2);
        for (int i = 0; i < 48; i++)
            Cube("Cube" + i.ToString("00"), new Vector3((i % 8) * 4f - 14f, 0.5f, (i / 8) * 4f - 10f), 1f + (i % 5) * 0.37f);
        Save(scene, "Crowded");
    }

    /// <summary>A plane larger than a lightmap (clamped to its size) and small cubes, with a
    /// padding of 4 in 128-texel lightmaps.</summary>
    static void Oversize()
    {
        var scene = NewScene("Oversize", 40f, 128, 4);
        Prim(PrimitiveType.Plane, "BigPlane", Vector3.zero, Vector3.zero, new Vector3(10f, 1f, 10f));
        for (int i = 0; i < 6; i++) Cube("Small" + i, new Vector3(i * 2f - 5f, 0.5f, 3f), 0.3f + 0.1f * i);
        Save(scene, "Oversize");
    }

    /// <summary>Fixed Lightmap Size on: the lightmap stays at the Max Lightmap Size.</summary>
    static void Fixed()
    {
        var scene = NewScene("Fixed", 10f, 512, 2, settings =>
        {
            var so = new SerializedObject(settings);
            so.FindProperty("m_LightmapSizeFixed").boolValue = true;
            so.ApplyModifiedPropertiesWithoutUndo();
        });
        for (int i = 0; i < 5; i++) Cube("Cube" + i, new Vector3(i * 2f, 0.5f, 0f));
        Save(scene, "Fixed");
    }

    /// <summary>Renderers with Lightmap Parameters whose baked fields differ (each its own atlas),
    /// and one whose only differences are realtime fields (shares the default's).</summary>
    static void Tags()
    {
        var scene = NewScene("Tags", 10f, 256, 2);
        LightmapParameters Asset(string name, Action<LightmapParameters> set)
        {
            var p = new LightmapParameters { name = name };
            set(p);
            AssetDatabase.CreateAsset(p, $"{Folder}/Tags{name}.giparams");
            return p;
        }
        var assets = new (string, LightmapParameters)[]
        {
            ("Default", null),
            ("TagOne", Asset("TagOne", p => p.bakedLightmapTag = 1)),
            ("TagTwo", Asset("TagTwo", p => p.bakedLightmapTag = 2)),
            ("Samples", Asset("Samples", p => p.antiAliasingSamples = 4)),
            ("Pushed", Asset("Pushed", p => p.pushoff = 0.01f)),
            ("BackFace", Asset("BackFace", p => p.backFaceTolerance = 0.5f)),
            ("Realtime", Asset("Realtime", p => p.resolution = 2f)),
        };
        for (int a = 0; a < assets.Length; a++)
            for (int k = 0; k < 2; k++)
            {
                var go = Cube(assets[a].Item1 + k, new Vector3(a * 3f, 0.5f, k * 3f), 1f + 0.5f * k);
                if (assets[a].Item2 != null) SetParameters(go, assets[a].Item2);
            }
        Save(scene, "Tags");
    }

    static Mesh MeshAsset(string name, Vector3[] vertices, int[][] submeshes, Vector2[] uv, Vector2[] uv2)
    {
        var mesh = new Mesh { name = name, vertices = vertices, uv = uv };
        if (uv2 != null) mesh.uv2 = uv2;
        mesh.subMeshCount = submeshes.Length;
        for (int s = 0; s < submeshes.Length; s++) mesh.SetTriangles(submeshes[s], s);
        mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, $"{Folder}/Meshes{name}.asset");
        return mesh;
    }

    static GameObject WithMesh(string name, Mesh mesh, Vector3 position, Vector3 euler, Vector3 scale)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        go.transform.eulerAngles = euler;
        go.transform.localScale = scale;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
        return Static(go);
    }

    /// <summary>Meshes without lightmap UVs (their UVs taken), with lightmap UVs off the unit
    /// square, narrow ones, a vertex no triangle uses, and two submeshes.</summary>
    static void Meshes()
    {
        var scene = NewScene("Meshes", 10f, 512, 2);
        var quad = new[] { new Vector3(0f, 0f, 0f), new Vector3(2f, 0f, 0f), new Vector3(2f, 1f, 0f), new Vector3(0f, 1f, 0f) };
        var one = new[] { new[] { 0, 2, 1, 0, 3, 2 } };
        var unit = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 1f), new Vector2(0f, 1f) };
        var onlyUv = MeshAsset("OnlyUv", quad, one, unit, null);
        var offset = MeshAsset("Offset", quad, one, unit,
            new[] { new Vector2(0.25f, 0.1f), new Vector2(0.75f, 0.1f), new Vector2(0.75f, 0.3f), new Vector2(0.25f, 0.3f) });
        var tall = MeshAsset("Tall", quad, one, unit,
            new[] { new Vector2(0f, 0f), new Vector2(0.2f, 0f), new Vector2(0.2f, 1f), new Vector2(0f, 1f) });
        var loose = MeshAsset("Loose", new[] { quad[0], quad[1], quad[2], quad[3], new Vector3(5f, 5f, 5f) }, one,
            new[] { unit[0], unit[1], unit[2], unit[3], new Vector2(0.5f, 0.5f) },
            new[] { new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f), new Vector2(0.9f, 0.9f) });
        var box = new[]
        {
            new Vector3(0f, 0f, 0f), new Vector3(1f, 0f, 0f), new Vector3(1f, 1f, 0f), new Vector3(0f, 1f, 0f),
            new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 1f), new Vector3(1f, 1f, 1f), new Vector3(0f, 1f, 1f),
        };
        var boxUv = new[]
        {
            new Vector2(0f, 0f), new Vector2(0.5f, 0f), new Vector2(0.5f, 0.5f), new Vector2(0f, 0.5f),
            new Vector2(0.5f, 0.5f), new Vector2(1f, 0.5f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
        };
        var two = MeshAsset("TwoSubmeshes", box, new[] { new[] { 0, 2, 1, 0, 3, 2 }, new[] { 4, 5, 6, 4, 6, 7 } }, boxUv, boxUv);
        WithMesh("OnlyUv", onlyUv, new Vector3(0f, 0f, 0f), Vector3.zero, Vector3.one);
        WithMesh("Offset", offset, new Vector3(4f, 0f, 0f), new Vector3(0f, 20f, 0f), new Vector3(1.5f, 1f, 1f));
        WithMesh("Tall", tall, new Vector3(8f, 0f, 0f), Vector3.zero, new Vector3(1f, 3f, 1f));
        WithMesh("Loose", loose, new Vector3(0f, 4f, 0f), Vector3.zero, Vector3.one);
        WithMesh("TwoSubmeshes", two, new Vector3(4f, 4f, 0f), new Vector3(30f, 0f, 10f), Vector3.one * 2f);
        Save(scene, "Meshes");
    }

    /// <summary>Equal cubes whose positions give one Morton code (102.4 units apart along x), made
    /// in another order than their names, and a larger one there too.</summary>
    static void Ties()
    {
        var scene = NewScene("Ties", 10f, 256, 2);
        Cube("TieB", new Vector3(102.4f, 0.5f, 0f));
        Cube("TieA", new Vector3(0f, 0.5f, 0f));
        Cube("TieC", new Vector3(204.8f, 0.5f, 0f));
        Cube("TieBig", new Vector3(307.2f, 0.5f, 0f), 2f);
        Save(scene, "Ties");
    }

    /// <summary>One small cube, after a cube with Scale In Lightmap 0 whose one-texel slot comes
    /// first: how far a lone lightmap shrinks, and that the slot moves the cube.</summary>
    static void Small()
    {
        var scene = NewScene("Small", 10f, 1024, 2);
        SetScaleInLightmap(Cube("Zero", new Vector3(0f, 0.5f, 0f)), 0f);
        Cube("Little", new Vector3(0.1f, 0.5f, 0f), 0.3f);
        Save(scene, "Small");
    }

    /// <summary>Makes the scenes <c>BAKED_ATLAS_SCENES</c> names (comma-separated), else all.</summary>
    public static void Create()
    {
        try
        {
            var all = new (string Name, Action Make)[]
            {
                ("Spread", Spread), ("Crowded", Crowded), ("Oversize", Oversize), ("Fixed", Fixed),
                ("Tags", Tags), ("Meshes", Meshes), ("Ties", Ties), ("Small", Small),
            };
            var only = Environment.GetEnvironmentVariable("BAKED_ATLAS_SCENES");
            var wanted = string.IsNullOrEmpty(only) ? null : new HashSet<string>(only.Split(','));
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "Scenes");
            foreach (var (name, make) in all)
                if (wanted == null || wanted.Contains(name)) make();
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(0);
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }
}
