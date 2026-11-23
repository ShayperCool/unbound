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

    static Light NewLight(string name, LightType type, Vector3 position, Vector3 euler, Color color, float intensity, LightmapBakeType mode = LightmapBakeType.Baked)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        go.transform.eulerAngles = euler;
        var light = go.AddComponent<Light>();
        light.type = type;
        light.color = color;
        light.intensity = intensity;
        light.lightmapBakeType = mode;
        light.shadows = LightShadows.Hard;
        return light;
    }

    /// <summary>A scene to light: no sky and no ambient light, 16 direct samples (every sample
    /// of a texel weighs the same), with a ground, a cube and a sphere.</summary>
    static Scene Lit(string name, MixedLightingMode mixed = MixedLightingMode.IndirectOnly)
    {
        var scene = NewScene(name, 4f, 128);
        Lightmapping.lightingSettings.directSampleCount = 16;
        Lightmapping.lightingSettings.mixedBakeMode = mixed;
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;
        Prim(PrimitiveType.Plane, "Ground", Vector3.zero, Vector3.zero, Vector3.one);
        Prim(PrimitiveType.Cube, "Cube", new Vector3(0f, 0.5f, 0f), new Vector3(0f, 20f, 0f), Vector3.one);
        Prim(PrimitiveType.Sphere, "Sphere", new Vector3(2f, 0.5f, 1f), Vector3.zero, Vector3.one);
        return scene;
    }

    /// <summary>A coloured directional light with an Indirect Multiplier, over renderers that do
    /// not cast shadows, do not receive them, are lit by light probes (and cast them), are
    /// mirrored, and have Scale In Lightmap 0.</summary>
    static void Sun()
    {
        var scene = Lit("Sun");
        NewLight("Sun", LightType.Directional, Vector3.zero, new Vector3(50f, -30f, 0f), new Color(1f, 0.5f, 0.25f), 2f).bounceIntensity = 3f;
        Prim(PrimitiveType.Cube, "NoCast", new Vector3(-2f, 0.5f, 2f), Vector3.zero, Vector3.one).GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Prim(PrimitiveType.Cube, "NoReceive", new Vector3(-0.4f, 0.25f, -1.6f), Vector3.zero, new Vector3(1f, 0.5f, 1f)).GetComponent<MeshRenderer>().receiveShadows = false;
        Prim(PrimitiveType.Cube, "Probes", new Vector3(3f, 0.5f, -2f), new Vector3(0f, 45f, 0f), Vector3.one).GetComponent<MeshRenderer>().receiveGI = ReceiveGI.LightProbes;
        Prim(PrimitiveType.Capsule, "Mirrored", new Vector3(-3f, 1f, -2f), new Vector3(0f, 40f, 20f), new Vector3(-1f, 1f, 1f));
        Set(Prim(PrimitiveType.Cube, "Zero", new Vector3(1f, 0.5f, -3f), Vector3.zero, Vector3.one), "m_ScaleInLightmap", p => p.floatValue = 0f);
        Prim(PrimitiveType.Cube, "ShadowsOnly", new Vector3(-1f, 2.5f, 3f), Vector3.zero, new Vector3(1f, 0.2f, 1f)).GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.ShadowsOnly;
        Save(scene, "Sun");
    }

    /// <summary>Point and spot lights: ranges that end on the ground, wide and narrow cones, one
    /// without shadows, one inside a renderer, and lights that are disabled, inactive or beyond
    /// everything.</summary>
    static void Lamps()
    {
        var scene = Lit("Lamps");
        NewLight("Bulb", LightType.Point, new Vector3(1f, 2f, -1f), Vector3.zero, Color.white, 2f).range = 6f;
        NewLight("Small", LightType.Point, new Vector3(-3f, 0.5f, 3f), Vector3.zero, new Color(0.2f, 1f, 0.4f), 1.5f).range = 1.5f;
        var bare = NewLight("NoShadows", LightType.Point, new Vector3(3.5f, 1f, 3f), Vector3.zero, new Color(1f, 0.3f, 0.3f), 1f);
        bare.range = 4f;
        bare.shadows = LightShadows.None;
        var wide = NewLight("Wide", LightType.Spot, new Vector3(-2f, 3f, 0f), new Vector3(60f, 90f, 0f), Color.white, 3f);
        wide.range = 10f;
        wide.spotAngle = 60f;
        var narrow = NewLight("Narrow", LightType.Spot, new Vector3(4f, 4f, -4f), new Vector3(45f, -45f, 30f), new Color(0.4f, 0.6f, 1f), 8f);
        narrow.range = 12f;
        narrow.spotAngle = 20f;
        var broad = NewLight("Broad", LightType.Spot, new Vector3(-4f, 1f, -4f), new Vector3(10f, 45f, 0f), Color.white, 1f);
        broad.range = 5f;
        broad.spotAngle = 170f;
        // (Off the sphere's centre, so that no ray to it runs along an edge of the sphere's mesh.)
        NewLight("Inside", LightType.Point, new Vector3(2.013f, 0.517f, 0.991f), Vector3.zero, Color.white, 4f).range = 3f;
        NewLight("Disabled", LightType.Point, new Vector3(0f, 3f, 0f), Vector3.zero, Color.white, 5f).enabled = false;
        NewLight("Inactive", LightType.Point, new Vector3(0f, 3f, 0f), Vector3.zero, Color.white, 5f).gameObject.SetActive(false);
        NewLight("Away", LightType.Point, new Vector3(100f, 3f, 0f), Vector3.zero, Color.white, 5f).range = 2f;
        Save(scene, "Lamps");
    }

    /// <summary>A mixed sun, a baked bulb and a realtime spot, in each Lighting Mode.</summary>
    static void Modes(string name, MixedLightingMode mixed)
    {
        var scene = Lit(name, mixed);
        NewLight("MixedSun", LightType.Directional, Vector3.zero, new Vector3(50f, -30f, 0f), Color.white, 1f, LightmapBakeType.Mixed);
        NewLight("BakedBulb", LightType.Point, new Vector3(1f, 2f, -1f), Vector3.zero, new Color(1f, 0.8f, 0.6f), 2f).range = 6f;
        var spot = NewLight("RealtimeSpot", LightType.Spot, new Vector3(-2f, 3f, 0f), new Vector3(60f, 90f, 0f), Color.white, 3f, LightmapBakeType.Realtime);
        spot.range = 10f;
        var mixedSpot = NewLight("MixedSpot", LightType.Spot, new Vector3(3f, 3f, 3f), new Vector3(50f, 225f, 0f), new Color(0.5f, 0.7f, 1f), 4f, LightmapBakeType.Mixed);
        mixedSpot.range = 10f;
        mixedSpot.spotAngle = 50f;
        Save(scene, name);
    }

    /// <summary>A Standard material asset of a scene (its name starts with the scene's).</summary>
    static Material Mat(string name, Color color, Action<Material> more = null)
    {
        string path = $"{Folder}/{name}.mat";
        var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (existing != null) return existing;
        var material = new Material(Shader.Find("Standard")) { name = name, color = color };
        more?.Invoke(material);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    /// <summary>A texture asset of a scene, its texels given by column and row.</summary>
    static Texture2D Tex(string name, int width, int height, FilterMode filter, Func<int, int, Color32> texel)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false) { name = name, filterMode = filter, wrapMode = TextureWrapMode.Repeat };
        var texels = new Color32[width * height];
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++) texels[y * width + x] = texel(x, y);
        texture.SetPixels32(texels);
        texture.Apply();
        AssetDatabase.CreateAsset(texture, $"{Folder}/{name}.asset");
        return texture;
    }

    static GameObject With(GameObject go, Material material)
    {
        go.GetComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }

    static void Emit(Material material, Color color, MaterialGlobalIlluminationFlags flags, Texture2D map = null)
    {
        material.EnableKeyword("_EMISSION");
        material.SetColor("_EmissionColor", color);
        if (map != null) material.SetTexture("_EmissionMap", map);
        material.globalIlluminationFlags = flags;
    }

    /// <summary>A scene for indirect light: no sky and no ambient light, a grey ground, a white
    /// cube, a blue sphere and a red wall, with the Indirect Samples and bounces given.</summary>
    static Scene Coloured(string name, int samples, int maxBounces, int minBounces = 2)
    {
        var scene = NewScene(name, 4f, 128);
        var settings = Lightmapping.lightingSettings;
        settings.directSampleCount = 16;
        settings.indirectSampleCount = samples;
        settings.maxBounces = maxBounces;
        settings.minBounces = minBounces;
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;
        With(Prim(PrimitiveType.Plane, "Ground", Vector3.zero, Vector3.zero, Vector3.one), Mat("BounceGrey", new Color(0.5f, 0.5f, 0.5f)));
        With(Prim(PrimitiveType.Cube, "Cube", new Vector3(0f, 0.5f, 0f), new Vector3(0f, 20f, 0f), Vector3.one), Mat("BounceWhite", Color.white));
        With(Prim(PrimitiveType.Sphere, "Sphere", new Vector3(2f, 0.5f, 1f), Vector3.zero, Vector3.one), Mat("BounceBlue", new Color(0.2f, 0.3f, 0.9f)));
        With(Prim(PrimitiveType.Cube, "Wall", new Vector3(-2.5f, 1f, 0f), Vector3.zero, new Vector3(0.2f, 2f, 4f)), Mat("BounceRed", new Color(0.9f, 0.1f, 0.1f)));
        return scene;
    }

    static void WhiteSun() =>
        NewLight("Sun", LightType.Directional, Vector3.zero, new Vector3(50f, -30f, 0f), Color.white, 2f).bounceIntensity = 1.5f;

    /// <summary>A sun over the coloured scene: one bounce of 64 samples; four bounces, ended by
    /// chance from the second on; two bounces of 512 samples, ended by chance from the first on;
    /// 10 samples asked for; and one bounce of a point light's.</summary>
    static void Bounces()
    {
        var scene = Coloured("Bounce", 64, 1);
        WhiteSun();
        Save(scene, "Bounce");
        scene = Coloured("BounceFar", 64, 4);
        WhiteSun();
        Save(scene, "BounceFar");
        scene = Coloured("BounceMany", 512, 2, 1);
        WhiteSun();
        Save(scene, "BounceMany");
        scene = Coloured("BounceFew", 10, 1);
        WhiteSun();
        Save(scene, "BounceFew");
        scene = Coloured("BouncePoint", 64, 2);
        // (Off whole coordinates, so that no ray to it runs along an edge of a mesh.)
        NewLight("Bulb", LightType.Point, new Vector3(1.013f, 2.017f, -0.991f), Vector3.zero, new Color(1f, 0.9f, 0.7f), 3f).range = 8f;
        Save(scene, "BouncePoint");
    }

    /// <summary>Emission alone lights the coloured scene: a lamp facing down with an emission
    /// map, and panels whose materials are flagged for baked emission, for realtime emission
    /// alone, and as black.</summary>
    static void Glow()
    {
        var scene = Coloured("Glow", 64, 2);
        var dots = Tex("GlowDots", 4, 4, FilterMode.Bilinear, (x, y) => new Color32((byte)(x * 85), (byte)(y * 85), 128, 255));
        var lamp = Mat("GlowLamp", Color.black, m => Emit(m, new Color(1f, 0.8f, 0.4f) * 3f, MaterialGlobalIlluminationFlags.BakedEmissive, dots));
        With(Prim(PrimitiveType.Quad, "Lamp", new Vector3(0f, 2.5f, 0f), new Vector3(-90f, 0f, 0f), new Vector3(2f, 2f, 1f)), lamp);
        var baked = Mat("GlowBaked", new Color(0.3f, 0.3f, 0.3f), m => Emit(m, new Color(0.5f, 0.25f, 0.1f), MaterialGlobalIlluminationFlags.BakedEmissive));
        With(Prim(PrimitiveType.Quad, "Baked", new Vector3(3f, 1f, -3f), new Vector3(0f, 180f, 0f), Vector3.one), baked);
        var realtime = Mat("GlowRealtime", Color.grey, m => Emit(m, new Color(0.2f, 1f, 0.3f) * 2f, MaterialGlobalIlluminationFlags.RealtimeEmissive));
        With(Prim(PrimitiveType.Quad, "Realtime", new Vector3(-3f, 1f, -3f), new Vector3(0f, 180f, 0f), Vector3.one), realtime);
        var black = Mat("GlowBlack", Color.grey, m => Emit(m, new Color(1f, 0.2f, 1f) * 2f, MaterialGlobalIlluminationFlags.EmissiveIsBlack));
        With(Prim(PrimitiveType.Quad, "Black", new Vector3(0f, 1f, -3.5f), new Vector3(0f, 180f, 0f), Vector3.one), black);
        var none = Mat("GlowNone", Color.grey, m => Emit(m, new Color(0.3f, 0.3f, 1f) * 2f, MaterialGlobalIlluminationFlags.None));
        With(Prim(PrimitiveType.Quad, "None", new Vector3(1.5f, 1f, -3.5f), new Vector3(0f, 180f, 0f), Vector3.one), none);
        Save(scene, "Glow");
    }

    /// <summary>Surfaces met from behind: a roof whose back faces the ground under the sun, one
    /// with Double Sided Global Illumination beside it, and a mirrored cube.</summary>
    static void Roof()
    {
        var scene = Coloured("Roof", 64, 1);
        WhiteSun();
        With(Prim(PrimitiveType.Quad, "Roof", new Vector3(1f, 0.6f, -2.5f), new Vector3(90f, 0f, 0f), new Vector3(2f, 2f, 1f)), Mat("BounceWhite", Color.white));
        var both = Mat("RoofBoth", new Color(0.9f, 0.9f, 0.2f), m => m.doubleSidedGI = true);
        With(Prim(PrimitiveType.Quad, "Both", new Vector3(-1f, 0.6f, 3f), new Vector3(90f, 0f, 0f), new Vector3(2f, 2f, 1f)), both);
        With(Prim(PrimitiveType.Cube, "Mirrored", new Vector3(3.5f, 0.5f, -1f), new Vector3(0f, 30f, 0f), new Vector3(-1f, 1f, 1f)), Mat("BounceRed", new Color(0.9f, 0.1f, 0.1f)));
        Save(scene, "Roof");
    }

    /// <summary>Materials with textures, and Albedo Boost 2: a rough ground with a grey ramp,
    /// a cube with a tiled checker, a metal sphere and a smooth wall, under the sun.</summary>
    static void Textured()
    {
        var scene = NewScene("Textured", 4f, 128);
        var settings = Lightmapping.lightingSettings;
        settings.directSampleCount = 16;
        settings.indirectSampleCount = 64;
        settings.maxBounces = 2;
        settings.albedoBoost = 2f;
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;
        var ramp = Tex("TexturedRamp", 64, 1, FilterMode.Point, (x, y) => new Color32((byte)(x * 4), (byte)(255 - x * 4), (byte)(x * 2), 255));
        var checker = Tex("TexturedChecker", 8, 8, FilterMode.Point, (x, y) => ((x + y) & 1) == 0 ? new Color32(230, 40, 40, 255) : new Color32(40, 200, 60, 255));
        With(Prim(PrimitiveType.Plane, "Ground", Vector3.zero, Vector3.zero, Vector3.one), Mat("TexturedRampMat", Color.white, m => { m.mainTexture = ramp; m.SetFloat("_Glossiness", 0f); }));
        With(Prim(PrimitiveType.Cube, "Cube", new Vector3(0f, 0.5f, 0f), new Vector3(0f, 20f, 0f), Vector3.one), Mat("TexturedCheckerMat", Color.white, m => { m.mainTexture = checker; m.mainTextureScale = new Vector2(2f, 2f); }));
        With(Prim(PrimitiveType.Sphere, "Sphere", new Vector3(2f, 0.5f, 1f), Vector3.zero, Vector3.one), Mat("TexturedMetal", new Color(0.8f, 0.6f, 0.2f), m => { m.SetFloat("_Metallic", 1f); m.SetFloat("_Glossiness", 0.8f); }));
        With(Prim(PrimitiveType.Cube, "Wall", new Vector3(-2.5f, 1f, 0f), Vector3.zero, new Vector3(0.2f, 2f, 4f)), Mat("TexturedSmooth", new Color(0.5f, 0.5f, 0.5f), m => m.SetFloat("_Glossiness", 1f)));
        WhiteSun();
        Save(scene, "Textured");
    }

    /// <summary>A strip whose albedo runs through every 8-bit level, 300 texels long: the
    /// linear value the bake takes for each level.</summary>
    static void Levels()
    {
        var scene = NewScene("Levels", 300f, 512);
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;
        var ramp = Tex("LevelsRamp", 256, 1, FilterMode.Point, (x, y) => new Color32((byte)x, (byte)(255 - x), (byte)((x * 7) & 255), 255));
        ramp.wrapMode = TextureWrapMode.Clamp;
        With(Prim(PrimitiveType.Quad, "Strip", Vector3.zero, new Vector3(90f, 0f, 0f), new Vector3(1f, 0.02f, 1f)), Mat("LevelsMat", Color.white, m => m.mainTexture = ramp));
        Save(scene, "Levels");
    }

    /// <summary>A scene lit by its sky alone: the coloured scene's renderers and one more that
    /// casts no shadow, with the Environment Samples, bounces and environment importance
    /// sampling given; <paramref name="sky"/> sets the ambient source.</summary>
    static void SkyScene(string name, int samples, int maxBounces, bool picked, Action sky)
    {
        var scene = Coloured(name, 64, maxBounces);
        var settings = Lightmapping.lightingSettings;
        settings.environmentSampleCount = samples;
        settings.environmentImportanceSampling = picked;
        sky();
        With(Prim(PrimitiveType.Cube, "NoCast", new Vector3(-0.5f, 1.8f, 2.5f), Vector3.zero, new Vector3(2f, 0.2f, 2f)), Mat("BounceWhite", Color.white))
            .GetComponent<MeshRenderer>().shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
        Save(scene, name);
    }

    static void FlatSky()
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.4f, 0.6f, 0.9f);
    }

    static void GradientSky()
    {
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.3f, 0.5f, 1f);
        RenderSettings.ambientEquatorColor = new Color(0.6f, 0.5f, 0.4f);
        RenderSettings.ambientGroundColor = new Color(0.1f, 0.2f, 0.05f);
    }

    static void DrawnSky(string name, float intensity)
    {
        var material = new Material(Shader.Find("Skybox/Procedural")) { name = name };
        AssetDatabase.CreateAsset(material, $"{Folder}/{name}.mat");
        RenderSettings.skybox = material;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Skybox;
        RenderSettings.ambientIntensity = intensity;
    }

    /// <summary>Skies: an ambient colour, with and without directions picked by brightness; a
    /// gradient with 8 samples; a drawn sky (the procedural skybox), plain and at intensity 2.5
    /// with picked directions; and one whose paths never bounce (no sky light at all).</summary>
    static void Skies()
    {
        SkyScene("Sky", 64, 2, false, FlatSky);
        SkyScene("SkyPicked", 64, 2, true, FlatSky);
        SkyScene("SkyGradient", 8, 2, true, GradientSky);
        SkyScene("SkyDrawn", 64, 2, false, () => DrawnSky("SkyDrawnBox", 1f));
        SkyScene("SkyDrawnPicked", 512, 3, true, () => DrawnSky("SkyDrawnPickedBox", 2.5f));
        SkyScene("SkyNoBounce", 64, 0, false, FlatSky);
    }

    /// <summary>A sun and a sky over the coloured scene and a roof of two submeshes with one
    /// material: its second submesh, a quad beside the first, has none.</summary>
    static void Spare()
    {
        var scene = Coloured("Spare", 64, 2);
        Lightmapping.lightingSettings.environmentSampleCount = 64;
        FlatSky();
        WhiteSun();
        var corners = new[] { P(-1, 0, -1), P(-1, 0, 1), P(0, 0, 1), P(0, 0, -1), P(0.2f, 0, -1), P(0.2f, 0, 1), P(1.2f, 0, 1), P(1.2f, 0, -1) };
        var uvs = new[] { U(0, 0), U(0, 1), U(0.45f, 1), U(0.45f, 0), U(0.55f, 0), U(0.55f, 1), U(1, 1), U(1, 0) };
        WithMesh("Roof", MeshAsset("Spare", "Roof", corners, new[] { new[] { 0, 1, 2, 0, 2, 3 }, new[] { 4, 5, 6, 4, 6, 7 } }, uvs), P(0.3f, 1.7f, 2.2f), Vector3.zero);
        Save(scene, "Spare");
    }

    /// <summary>The ambient colour's scene with Realtime Global Illumination on as well, the
    /// environment's light kept in the realtime lightmaps (the Lighting Settings' default) or
    /// in the baked ones (LightingSettings.realtimeEnvironmentLighting).</summary>
    static void RealtimeSkies()
    {
        foreach (var (name, realtime) in new[] { ("SkyRealtime", true), ("SkyRealtimeBaked", false) })
            SkyScene(name, 64, 2, false, () =>
            {
                FlatSky();
                var settings = Lightmapping.lightingSettings;
                settings.realtimeGI = true;
                settings.realtimeEnvironmentLighting = realtime;
            });
    }

    /// <summary>Spare's sun and sky and a roof of three submeshes side by side whose renderer
    /// has three material slots, the middle one empty, between a blue and a red material.</summary>
    static void SpareEmpty()
    {
        var scene = Coloured("SpareEmpty", 64, 2);
        Lightmapping.lightingSettings.environmentSampleCount = 64;
        FlatSky();
        WhiteSun();
        var corners = new[]
        {
            P(-1, 0, -1), P(-1, 0, 1), P(-0.4f, 0, 1), P(-0.4f, 0, -1),
            P(-0.3f, 0, -1), P(-0.3f, 0, 1), P(0.3f, 0, 1), P(0.3f, 0, -1),
            P(0.4f, 0, -1), P(0.4f, 0, 1), P(1, 0, 1), P(1, 0, -1),
        };
        var uvs = new[]
        {
            U(0, 0), U(0, 1), U(0.3f, 1), U(0.3f, 0),
            U(0.35f, 0), U(0.35f, 1), U(0.65f, 1), U(0.65f, 0),
            U(0.7f, 0), U(0.7f, 1), U(1, 1), U(1, 0),
        };
        var submeshes = new[] { new[] { 0, 1, 2, 0, 2, 3 }, new[] { 4, 5, 6, 4, 6, 7 }, new[] { 8, 9, 10, 8, 10, 11 } };
        var roof = WithMesh("Roof", MeshAsset("SpareEmpty", "Roof", corners, submeshes, uvs), P(0.3f, 1.7f, 2.2f), Vector3.zero);
        roof.GetComponent<MeshRenderer>().sharedMaterials = new[] { Mat("BounceBlue", new Color(0.2f, 0.3f, 0.9f)), null, Mat("BounceRed", new Color(0.9f, 0.1f, 0.1f)) };
        Save(scene, "SpareEmpty");
    }

    /// <summary>Point and spot lights spread over a ground of 120 m with a pillar of 70 m: the
    /// light grid has several regions along each axis, and the paths' three bounces test fewer
    /// lights at each. (The boxes stand a little above the ground and off whole coordinates, so
    /// that no sample of a texel lies on one of their faces.)</summary>
    static void Spread()
    {
        var scene = NewScene("Spread", 0.5f, 128);
        var settings = Lightmapping.lightingSettings;
        settings.directSampleCount = 16;
        settings.indirectSampleCount = 16;
        settings.maxBounces = 3;
        settings.minBounces = 3;
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;
        With(Prim(PrimitiveType.Plane, "Ground", Vector3.zero, Vector3.zero, new Vector3(12f, 1f, 12f)), Mat("BounceGrey", new Color(0.5f, 0.5f, 0.5f)));
        With(Prim(PrimitiveType.Cube, "Pillar", new Vector3(50.13f, 35.07f, -49.91f), Vector3.zero, new Vector3(2f, 70f, 2f)), Mat("BounceWhite", Color.white));
        With(Prim(PrimitiveType.Cube, "Block", new Vector3(-25.17f, 2.03f, -20.11f), new Vector3(0f, 30f, 0f), new Vector3(6f, 4f, 6f)), Mat("BounceRed", new Color(0.9f, 0.1f, 0.1f)));
        With(Prim(PrimitiveType.Cube, "Wall", new Vector3(15.07f, 3.02f, 25.13f), Vector3.zero, new Vector3(20f, 6f, 1f)), Mat("BounceBlue", new Color(0.2f, 0.3f, 0.9f)));
        With(Prim(PrimitiveType.Cube, "Crate", new Vector3(-5.09f, 1.53f, 5.14f), new Vector3(0f, 10f, 0f), new Vector3(3f, 3f, 3f)), Mat("BounceWhite", Color.white));
        NewLight("P1", LightType.Point, new Vector3(-45.3f, 3f, -44.7f), Vector3.zero, Color.white, 3f).range = 20f;
        NewLight("P2", LightType.Point, new Vector3(-20.2f, 4f, -39.6f), Vector3.zero, new Color(1f, 0.8f, 0.5f), 2f).range = 15f;
        NewLight("P3", LightType.Point, new Vector3(5.1f, 2f, -30.3f), Vector3.zero, new Color(0.5f, 1f, 0.6f), 4f).range = 25f;
        NewLight("P4", LightType.Point, new Vector3(30.4f, 3f, -9.8f), Vector3.zero, new Color(0.6f, 0.7f, 1f), 1.5f).range = 12f;
        NewLight("P5", LightType.Point, new Vector3(44.6f, 5f, 40.2f), Vector3.zero, Color.white, 6f).range = 30f;
        NewLight("P6", LightType.Point, new Vector3(-39.9f, 2f, 30.1f), Vector3.zero, new Color(1f, 0.4f, 0.4f), 2.5f).range = 18f;
        NewLight("P7", LightType.Point, new Vector3(0.3f, 3f, 0.2f), Vector3.zero, new Color(0.9f, 0.9f, 0.6f), 1f).range = 10f;
        NewLight("P8", LightType.Point, new Vector3(20.1f, 2f, 19.7f), Vector3.zero, new Color(0.7f, 1f, 1f), 2f).range = 8f;
        NewLight("High", LightType.Point, new Vector3(49.7f, 60f, -45.2f), Vector3.zero, new Color(1f, 0.9f, 0.7f), 8f).range = 30f;
        var down = NewLight("Down", LightType.Spot, new Vector3(-30.2f, 8f, 0.3f), new Vector3(90f, 0f, 0f), Color.white, 5f);
        down.range = 20f;
        down.spotAngle = 50f;
        var side = NewLight("Side", LightType.Spot, new Vector3(35.3f, 6f, -34.8f), new Vector3(10f, 45f, 0f), new Color(1f, 0.7f, 0.3f), 8f);
        side.range = 40f;
        side.spotAngle = 30f;
        var wide = NewLight("Wide", LightType.Spot, new Vector3(0.2f, 10f, 39.7f), new Vector3(60f, 180f, 0f), new Color(0.5f, 0.6f, 1f), 4f);
        wide.range = 25f;
        wide.spotAngle = 90f;
        var along = NewLight("Along", LightType.Spot, new Vector3(-10.1f, 3f, -9.9f), new Vector3(0f, 90f, 0f), Color.white, 6f);
        along.range = 35f;
        along.spotAngle = 20f;
        Save(scene, "Spread");
    }

    /// <summary>A ground of 120 m 500 m from the scene's origin, with six point lights and a
    /// spot light near one of its ends: the light grid's box reaches from the origin, so the
    /// ground keeps five regions along x though the lights reach only its end, and the lights
    /// fall into two of them (from their own reach alone they would make one region); a
    /// ceiling over them sends their light back down.</summary>
    static void Far()
    {
        var scene = NewScene("Far", 0.5f, 128);
        var settings = Lightmapping.lightingSettings;
        settings.directSampleCount = 16;
        settings.indirectSampleCount = 16;
        settings.maxBounces = 2;
        settings.minBounces = 2;
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;
        With(Prim(PrimitiveType.Plane, "Ground", new Vector3(500f, 0f, 0f), Vector3.zero, new Vector3(12f, 1f, 2f)), Mat("BounceGrey", new Color(0.5f, 0.5f, 0.5f)));
        With(Prim(PrimitiveType.Plane, "Ceiling", new Vector3(537.13f, 6.5f, 0.07f), new Vector3(180f, 0f, 0f), new Vector3(4f, 1f, 2f)), Mat("BounceWhite", Color.white));
        With(Prim(PrimitiveType.Cube, "Crate", new Vector3(536.87f, 0.53f, 0.13f), new Vector3(0f, 10f, 0f), new Vector3(1f, 1f, 1f)), Mat("BounceWhite", Color.white));
        With(Prim(PrimitiveType.Cube, "Block", new Vector3(531.91f, 1.03f, 2.07f), new Vector3(0f, 30f, 0f), new Vector3(1f, 2f, 1f)), Mat("BounceRed", new Color(0.9f, 0.1f, 0.1f)));
        NewLight("F1", LightType.Point, new Vector3(528.2f, 2f, 1.1f), Vector3.zero, Color.white, 12f).range = 3f;
        NewLight("F2", LightType.Point, new Vector3(531.1f, 2.5f, -1.9f), Vector3.zero, new Color(1f, 0.8f, 0.5f), 8f).range = 3.5f;
        NewLight("F3", LightType.Point, new Vector3(533.9f, 1.5f, 2.2f), Vector3.zero, new Color(0.5f, 1f, 0.6f), 16f).range = 2.5f;
        NewLight("F4", LightType.Point, new Vector3(539.3f, 2f, -2.3f), Vector3.zero, new Color(0.6f, 0.7f, 1f), 6f).range = 3f;
        NewLight("F5", LightType.Point, new Vector3(542.4f, 3f, 0.8f), Vector3.zero, new Color(1f, 0.4f, 0.4f), 10f).range = 4f;
        NewLight("F6", LightType.Point, new Vector3(545.6f, 1.5f, -0.7f), Vector3.zero, new Color(0.7f, 1f, 1f), 8f).range = 3.5f;
        var spot = NewLight("FSpot", LightType.Spot, new Vector3(537.2f, 6f, 0.3f), new Vector3(80f, 20f, 0f), Color.white, 20f);
        spot.range = 8f;
        spot.spotAngle = 60f;
        Save(scene, "Far");
    }

    /// <summary>Directional lightmaps (Directional Mode Directional): a sun, a point light and a
    /// spot light over the lit scene, with one bounce; the coloured scene under a sun with two
    /// bounces; and an ambient colour alone.</summary>
    static void Directional()
    {
        void Directed() => Lightmapping.lightingSettings.directionalityMode = LightmapsMode.CombinedDirectional;
        var lamps = Lit("DirectionalLamps");
        Directed();
        Lightmapping.lightingSettings.indirectSampleCount = 16;
        NewLight("Sun", LightType.Directional, Vector3.zero, new Vector3(50f, -30f, 0f), new Color(1f, 0.9f, 0.8f), 1.5f);
        NewLight("Bulb", LightType.Point, new Vector3(1.03f, 2.01f, -1.02f), Vector3.zero, new Color(1f, 0.6f, 0.3f), 2f).range = 6f;
        var spot = NewLight("Spot", LightType.Spot, new Vector3(-1.97f, 3.02f, 0.03f), new Vector3(60f, 90f, 0f), new Color(0.4f, 0.6f, 1f), 3f);
        spot.range = 10f;
        spot.spotAngle = 60f;
        Save(lamps, "DirectionalLamps");
        var bounce = Coloured("DirectionalBounce", 64, 2);
        Directed();
        WhiteSun();
        Save(bounce, "DirectionalBounce");
        SkyScene("DirectionalSky", 64, 1, false, () => { FlatSky(); Directed(); });
    }

    /// <summary>Makes the scenes <c>BAKED_BUFFERS_SCENES</c> names (comma-separated), else all.</summary>
    public static void Create()
    {
        try
        {
            var all = new (string Name, Action Make)[]
            {
                ("Shapes", Shapes), ("Samples", Samples), ("Charts", Charts), ("Bare", Bare), ("Sun", Sun), ("Lamps", Lamps),
                ("ModeIndirect", () => Modes("ModeIndirect", MixedLightingMode.IndirectOnly)),
                ("ModeSubtractive", () => Modes("ModeSubtractive", MixedLightingMode.Subtractive)),
                ("ModeShadowmask", () => Modes("ModeShadowmask", MixedLightingMode.Shadowmask)),
                ("Bounce", Bounces), ("Glow", Glow), ("Roof", Roof), ("Textured", Textured), ("Levels", Levels), ("Sky", Skies), ("Spare", Spare),
                ("SkyRealtime", RealtimeSkies), ("SpareEmpty", SpareEmpty), ("Spread", Spread), ("Far", Far), ("Directional", Directional),
            };
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
