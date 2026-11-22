using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Authoring (`bake_buffers.py create`, the Editor with <c>-executeMethod BuffersSetup.Create</c>
/// in a private copy): makes the scenes whose baked lightmaps' buffers the probe measures, each
/// with its own Lighting Settings, and the Lightmap Parameters and meshes they use. The runner
/// copies <c>Assets/Scenes</c> back.
/// </summary>
public static class BuffersSetup
{
    const string Folder = "Assets/Scenes";

    static Scene NewScene(string name, float resolution, int maxSize)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var settings = new LightingSettings
        {
            name = name,
            bakedGI = true,
            realtimeGI = false,
            lightmapper = LightingSettings.Lightmapper.ProgressiveCPU,
            lightmapResolution = resolution,
            lightmapMaxSize = maxSize,
            lightmapPadding = 2,
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
        AssetDatabase.CreateAsset(settings, $"{Folder}/{name}.lighting");
        Lightmapping.lightingSettings = settings;
        Lightmapping.bakeOnSceneLoad = Lightmapping.BakeOnSceneLoadMode.Never;
        return scene;
    }

    static void Save(Scene scene, string name) => EditorSceneManager.SaveScene(scene, $"{Folder}/{name}.unity");

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

    static void Set(GameObject go, string property, Action<SerializedProperty> set)
    {
        var so = new SerializedObject(go.GetComponent<MeshRenderer>());
        set(so.FindProperty(property));
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    /// <summary>Primitives of every kind, placed, turned and scaled (one mirrored, one under a
    /// scaled and turned parent), a cube with Scale In Lightmap 0, one lit by light probes
    /// between the others, and a LOD Group.</summary>
    static void Shapes()
    {
        var scene = NewScene("Shapes", 5f, 256);
        Prim(PrimitiveType.Cube, "Cube", new Vector3(0f, 0.5f, 0f), Vector3.zero, Vector3.one);
        Prim(PrimitiveType.Cube, "Turned", new Vector3(3f, 1f, 2f), new Vector3(10f, 30f, 50f), new Vector3(2f, 1f, 0.5f));
        Prim(PrimitiveType.Sphere, "Sphere", new Vector3(5f, 1f, -3f), new Vector3(0f, 25f, 0f), Vector3.one * 1.5f);
        Prim(PrimitiveType.Sphere, "Squashed", new Vector3(8f, 1f, -3f), new Vector3(20f, 0f, 70f), new Vector3(2f, 0.5f, 1f));
        Prim(PrimitiveType.Cube, "Probes", new Vector3(-3f, 0.5f, 0f), Vector3.zero, Vector3.one).GetComponent<MeshRenderer>().receiveGI = ReceiveGI.LightProbes;
        Prim(PrimitiveType.Cylinder, "Cylinder", new Vector3(-2f, 1f, 4f), new Vector3(90f, 0f, 0f), new Vector3(1f, 2f, 1f));
        Prim(PrimitiveType.Capsule, "Capsule", new Vector3(7f, 1f, 6f), new Vector3(0f, 0f, 30f), Vector3.one);
        Prim(PrimitiveType.Plane, "Plane", Vector3.zero, Vector3.zero, Vector3.one);
        Prim(PrimitiveType.Quad, "Quad", new Vector3(2f, 2f, -6f), new Vector3(0f, 0f, 45f), new Vector3(2f, 1f, 1f));
        Prim(PrimitiveType.Capsule, "Mirrored", new Vector3(-5f, 1f, -5f), new Vector3(0f, 40f, 0f), new Vector3(-1f, 1.5f, 1f));
        Set(Prim(PrimitiveType.Cube, "Zero", new Vector3(13f, 0.5f, 0f), Vector3.zero, Vector3.one), "m_ScaleInLightmap", p => p.floatValue = 0f);
        var parent = new GameObject("Parent").transform;
        parent.position = new Vector3(-6f, 0f, -2f);
        parent.eulerAngles = new Vector3(0f, 45f, 20f);
        parent.localScale = new Vector3(2f, 0.5f, 1f);
        Prim(PrimitiveType.Sphere, "Child", new Vector3(1f, 0f, 1f), new Vector3(0f, 0f, 60f), Vector3.one * 0.5f, parent);
        var lod = new GameObject("Lod");
        lod.transform.position = new Vector3(4f, 0f, 8f);
        var lod0 = Prim(PrimitiveType.Sphere, "Lod0", Vector3.zero, Vector3.zero, Vector3.one, lod.transform);
        var lod1 = Prim(PrimitiveType.Cube, "Lod1", new Vector3(0f, 0f, 2f), Vector3.zero, Vector3.one, lod.transform);
        lod.AddComponent<LODGroup>().SetLODs(new[]
        {
            new LOD(0.6f, new Renderer[] { lod0.GetComponent<Renderer>() }),
            new LOD(0.3f, new Renderer[] { lod1.GetComponent<Renderer>() }),
        });
        Save(scene, "Shapes");
    }

    /// <summary>A cube and a sphere for each of several Anti-aliasing Samples of Lightmap
    /// Parameters: how many samples a texel gets along each side, and the lightmaps' order.</summary>
    static void Samples()
    {
        var scene = NewScene("Samples", 6f, 64);
        int[] counts = { 1, 3, 4, 5, 8, 9, 16, 256 };
        for (int k = 0; k < counts.Length; k++)
        {
            var parameters = new LightmapParameters { name = "Samples" + counts[k] };
            var so = new SerializedObject(parameters);
            so.FindProperty("antiAliasingSamples").intValue = counts[k];
            so.ApplyModifiedPropertiesWithoutUndo();
            AssetDatabase.CreateAsset(parameters, $"{Folder}/Samples{counts[k]}.giparams");
            foreach (var go in new[]
            {
                Prim(PrimitiveType.Cube, "Cube" + counts[k], new Vector3(k * 3f, 0.5f, 0f), new Vector3(0f, 10f * k, 0f), Vector3.one),
                Prim(PrimitiveType.Sphere, "Sphere" + counts[k], new Vector3(k * 3f, 0.5f, 3f), Vector3.zero, Vector3.one),
            })
                Set(go, "m_LightmapParameters", p => p.objectReferenceValue = parameters);
        }
        Save(scene, "Samples");
    }

    static Mesh MeshAsset(string scene, string name, Vector3[] vertices, int[][] submeshes, Vector2[] uv2, Vector3[] normals = null, bool noNormals = false)
    {
        var mesh = new Mesh { name = name, vertices = vertices, uv = uv2, uv2 = uv2 };
        mesh.subMeshCount = submeshes.Length;
        for (int s = 0; s < submeshes.Length; s++) mesh.SetTriangles(submeshes[s], s);
        if (normals != null) mesh.normals = normals;
        else if (!noNormals) mesh.RecalculateNormals();
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, $"{Folder}/{scene}{name}.asset");
        return mesh;
    }

    static GameObject WithMesh(string name, Mesh mesh, Vector3 position, Vector3 euler)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        go.transform.eulerAngles = euler;
        go.AddComponent<MeshFilter>().sharedMesh = mesh;
        go.AddComponent<MeshRenderer>().sharedMaterial = AssetDatabase.GetBuiltinExtraResource<Material>("Default-Material.mat");
        return Static(go);
    }

    static Vector3 P(float x, float y, float z = 0f) => new Vector3(x, y, z);
    static Vector2 U(float x, float y) => new Vector2(x, y);

    /// <summary>Meshes whose lightmap UVs meet the texels in particular ways: triangles drawn
    /// over one another, slivers thinner than a texel, triangles without area, charts that join,
    /// submeshes, vertices that are the same but for their place in the mesh, normals without
    /// length.</summary>
    static void Charts()
    {
        var scene = NewScene("Charts", 12f, 128);
        var quad = new[] { P(0, 0), P(1, 0), P(1, 1), P(0, 1) };
        var unit = new[] { U(0, 0), U(1, 0), U(1, 1), U(0, 1) };
        // Two quads over the same UVs, facing opposite ways; the second is drawn later.
        WithMesh("Stacked", MeshAsset("Charts", "Stacked",
            new[] { quad[0], quad[1], quad[2], quad[3], P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1) },
            new[] { new[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 4, 6, 7 } },
            new[] { unit[0], unit[1], unit[2], unit[3], unit[0], unit[1], unit[2], unit[3] }), P(0, 0), Vector3.zero);
        // A quad, then a triangle over a part of it that ends within texels.
        WithMesh("Partial", MeshAsset("Charts", "Partial",
            new[] { quad[0], quad[1], quad[2], quad[3], P(0, 0, 1), P(0, 1, 1), P(0, 0, 2) },
            new[] { new[] { 0, 2, 1, 0, 3, 2, 4, 5, 6 } },
            new[] { unit[0], unit[1], unit[2], unit[3], U(0.13f, 0.21f), U(0.58f, 0.27f), U(0.31f, 0.77f) }), P(2, 0), Vector3.zero);
        // The same in the other order: the triangle first.
        WithMesh("PartialFirst", MeshAsset("Charts", "PartialFirst",
            new[] { quad[0], quad[1], quad[2], quad[3], P(0, 0, 1), P(0, 1, 1), P(0, 0, 2) },
            new[] { new[] { 4, 5, 6, 0, 2, 1, 0, 3, 2 } },
            new[] { unit[0], unit[1], unit[2], unit[3], U(0.13f, 0.21f), U(0.58f, 0.27f), U(0.31f, 0.77f) }), P(4, 0), Vector3.zero);
        // A fan of slivers around a point: a small mesh, each triangle thinner than a texel.
        {
            const int n = 24;
            var vertices = new List<Vector3> { P(0.1f, 0.1f) };
            var uvs = new List<Vector2> { U(0.5f, 0.5f) };
            var triangles = new List<int>();
            for (int k = 0; k < n; k++)
            {
                float a = 2f * Mathf.PI * k / n;
                vertices.Add(P(0.1f + 0.1f * Mathf.Cos(a), 0.1f + 0.1f * Mathf.Sin(a)));
                uvs.Add(U(0.5f + 0.5f * Mathf.Cos(a), 0.5f + 0.5f * Mathf.Sin(a)));
                triangles.AddRange(new[] { 0, 1 + (k + 1) % n, 1 + k });
            }
            WithMesh("Slivers", MeshAsset("Charts", "Slivers", vertices.ToArray(), new[] { triangles.ToArray() }, uvs.ToArray()), P(6, 0), Vector3.zero);
        }
        // Long thin triangles across many texels.
        WithMesh("Needles", MeshAsset("Charts", "Needles",
            new[] { P(0, 0), P(2, 0.02f), P(2, 0), P(0, 0.5f), P(2, 0.52f), P(2, 0.5f), P(0, 1), P(2, 1.001f), P(2, 1) },
            new[] { new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8 } },
            new[] { U(0, 0), U(1, 0.02f), U(1, 0), U(0, 0.5f), U(1, 0.52f), U(1, 0.5f), U(0, 1), U(1, 1.001f), U(1, 1) }), P(8, 0), Vector3.zero);
        // Triangles without area in the lightmap, and one without area in space.
        WithMesh("Flat", MeshAsset("Charts", "Flat",
            new[] { quad[0], quad[1], quad[2], quad[3], P(0, 0, 1), P(1, 0, 1), P(0, 1, 1), P(0, 0, 2), P(0.5f, 0, 2), P(1, 0, 2) },
            new[] { new[] { 0, 2, 1, 0, 3, 2, 4, 5, 6, 7, 8, 9 } },
            new[] { U(0, 0), U(0.5f, 0), U(0.5f, 0.5f), U(0, 0.5f), U(0.75f, 0.75f), U(0.75f, 0.75f), U(0.75f, 0.75f), U(0.6f, 0.1f), U(1f, 0.1f), U(0.8f, 0.4f) },
            new[] { Vector3.back, Vector3.back, Vector3.back, Vector3.back, Vector3.back, Vector3.back, Vector3.back, Vector3.up, Vector3.up, Vector3.right }), P(11, 0), Vector3.zero);
        // Two submeshes of their own vertices, and two that share an edge's vertices.
        var boxUv = new[] { U(0, 0), U(0.45f, 0), U(0.45f, 0.45f), U(0, 0.45f), U(0.55f, 0.55f), U(1, 0.55f), U(1, 1), U(0.55f, 1) };
        WithMesh("Apart", MeshAsset("Charts", "Apart",
            new[] { quad[0], quad[1], quad[2], quad[3], P(0, 0, 1), P(1, 0, 1), P(1, 1, 1), P(0, 1, 1) },
            new[] { new[] { 0, 2, 1, 0, 3, 2 }, new[] { 4, 5, 6, 4, 6, 7 } }, boxUv), P(0, 2), new Vector3(30f, 0f, 10f));
        WithMesh("Sharing", MeshAsset("Charts", "Sharing", quad, new[] { new[] { 0, 2, 1 }, new[] { 0, 3, 2 } }, unit), P(2, 2), Vector3.zero);
        // Three triangles in a row given first, third, second: the second joins two charts; a
        // fourth apart from them comes after.
        WithMesh("Joining", MeshAsset("Charts", "Joining",
            new[] { P(0, 0), P(1, 0), P(0, 1), P(1, 1), P(2, 0), P(2, 1), P(3, 0), P(4, 0), P(3, 1) },
            new[] { new[] { 0, 2, 1, 3, 5, 4, 1, 2, 3, 6, 8, 7 } },
            new[] { U(0, 0), U(0.2f, 0), U(0, 1), U(0.2f, 1), U(0.4f, 0), U(0.4f, 1), U(0.6f, 0), U(1, 0), U(0.6f, 1) }), P(4, 2), Vector3.zero);
        // Two triangles of a quad with their own vertices along the shared edge: the same
        // there (one chart), and with other normals there (two).
        var six = new[] { quad[0], quad[2], quad[1], quad[0], quad[3], quad[2] };
        var sixUv = new[] { unit[0], unit[2], unit[1], unit[0], unit[3], unit[2] };
        var back = Vector3.back;
        var tilted = new Vector3(0.6f, 0f, -0.8f);
        WithMesh("Same", MeshAsset("Charts", "Same", six, new[] { new[] { 0, 1, 2, 3, 4, 5 } }, sixUv, new[] { back, back, back, back, back, back }), P(9, 2), Vector3.zero);
        WithMesh("Split", MeshAsset("Charts", "Split", six, new[] { new[] { 0, 1, 2, 3, 4, 5 } }, sixUv, new[] { back, back, back, tilted, tilted, tilted }), P(11, 2), Vector3.zero);
        // Normals without length, and of other lengths.
        WithMesh("Lengths", MeshAsset("Charts", "Lengths", quad, new[] { new[] { 0, 2, 1, 0, 3, 2 } }, unit,
            new[] { Vector3.zero, new Vector3(0f, 0f, -3f), new Vector3(0f, 0.000004f, 0f), new Vector3(1f, 1f, -1f) }), P(13, 2), new Vector3(0f, 40f, 0f));
        // Lightmap UVs wound the other way.
        WithMesh("Wound", MeshAsset("Charts", "Wound", quad, new[] { new[] { 0, 2, 1, 0, 3, 2 } }, new[] { U(1, 0), U(0, 0), U(0, 1), U(1, 1) }), P(15, 2), Vector3.zero);
        Save(scene, "Charts");
    }

    /// <summary>A mesh without normals beside an ordinary cube.</summary>
    static void Bare()
    {
        var scene = NewScene("Bare", 12f, 64);
        Prim(PrimitiveType.Cube, "Cube", new Vector3(0f, 0.5f, 0f), Vector3.zero, Vector3.one);
        var quad = new[] { P(0, 0), P(1, 0), P(1, 1), P(0, 1) };
        WithMesh("NoNormals", MeshAsset("Bare", "NoNormals", quad, new[] { new[] { 0, 2, 1, 0, 3, 2 } }, new[] { U(0, 0), U(1, 0), U(1, 1), U(0, 1) }, noNormals: true), P(3, 0), Vector3.zero);
        Save(scene, "Bare");
    }

    /// <summary>Makes the scenes <c>BAKED_BUFFERS_SCENES</c> names (comma-separated), else all.</summary>
    public static void Create()
    {
        try
        {
            var all = new (string Name, Action Make)[] { ("Shapes", Shapes), ("Samples", Samples), ("Charts", Charts), ("Bare", Bare) };
            var only = Environment.GetEnvironmentVariable("BAKED_BUFFERS_SCENES");
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
