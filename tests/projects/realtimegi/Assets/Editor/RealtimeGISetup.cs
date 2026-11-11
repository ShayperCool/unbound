using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// `-executeMethod RealtimeGISetup.Create`: makes the probe's scenes and precomputes their realtime
/// global illumination. Each scene is an open-fronted room (white floor, ceiling, back wall and
/// block, red left and green right walls) with an emissive panel, a realtime point light, a
/// disabled realtime spot light, a light probe group and a dynamic sphere lit by the probes.
/// <list type="bullet">
/// <item><c>Room</c>: non-directional, Albedo Boost 1, Indirect Intensity 1.</item>
/// <item><c>RoomScaled</c>: the same with Albedo Boost 2 and Indirect Intensity 1.5.</item>
/// <item><c>RoomDirectional</c>: directional realtime lightmaps.</item>
/// <item><c>RoomSystems</c>: as <c>Room</c> with the left and right walls and the panel in a
/// second system: their Lightmap Parameters are the defaults with System Tag 1
/// (<c>-executeMethod RealtimeGISetup.CreateSystems</c> adds it alone).</item>
/// <item><c>RoomBakedEnvironment</c>: as <c>Room</c> with baked global illumination on too and
/// the environment lighting baked (<c>-executeMethod RealtimeGISetup.CreateBakedEnvironment</c>
/// adds it alone).</item>
/// <item><c>RoomCharts</c>: as <c>Room</c> with four flat panels whose lightmap UV charts leave
/// holes: a ring whose hole holds its two loose plates, the same ring with a third plate, a plate
/// with two holes of different sizes, and an arch; and a plate with realtime lightmap UVs of
/// its own in <c>Mesh.uv3</c> (<c>-executeMethod RealtimeGISetup.CreateCharts</c> adds it
/// alone).</item>
/// </list>
/// </summary>
public static class RealtimeGISetup
{
    const string Scenes = "Assets/Scenes";
    const string Settings = "Assets/Settings";

    public static void Create()
    {
        // An exception would leave a batchmode Editor waiting on a dialog: exit with an error.
        try { CreateAll(); }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    /// <summary>Adds the <c>RoomSystems</c> scene alone, leaving the others as they are.</summary>
    public static void CreateSystems()
    {
        try
        {
            CreateRoom("RoomSystems", LightmapsMode.NonDirectional, 1f, 1f, true);
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.Add(new EditorBuildSettingsScene($"{Scenes}/RoomSystems.unity", true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(0);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    /// <summary>Adds the <c>RoomBakedEnvironment</c> scene alone: the room with baked global
    /// illumination on as well and its environment lighting baked.</summary>
    public static void CreateBakedEnvironment()
    {
        try
        {
            CreateRoom("RoomBakedEnvironment", LightmapsMode.NonDirectional, 1f, 1f, bakedEnvironment: true);
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.Insert(4, new EditorBuildSettingsScene($"{Scenes}/RoomBakedEnvironment.unity", true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(0);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    /// <summary>Adds the <c>RoomCharts</c> scene alone: the room with the chart panels.</summary>
    public static void CreateCharts()
    {
        try
        {
            CreateRoom("RoomCharts", LightmapsMode.NonDirectional, 1f, 1f, charts: true);
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.Add(new EditorBuildSettingsScene($"{Scenes}/RoomCharts.unity", true));
            EditorBuildSettings.scenes = scenes.ToArray();
            AssetDatabase.SaveAssets();
            EditorApplication.Exit(0);
        }
        catch (System.Exception e)
        {
            Debug.LogException(e);
            EditorApplication.Exit(1);
        }
    }

    static void CreateAll()
    {
        AssetDatabase.CreateFolder("Assets", "Scenes");
        AssetDatabase.CreateFolder("Assets", "Settings");
        var scenes = new List<EditorBuildSettingsScene>();
        foreach (var (name, mode, boost, scale) in new[]
        {
            ("Room", LightmapsMode.NonDirectional, 1f, 1f),
            ("RoomScaled", LightmapsMode.NonDirectional, 2f, 1.5f),
            ("RoomDirectional", LightmapsMode.CombinedDirectional, 1f, 1f),
        })
        {
            CreateRoom(name, mode, boost, scale);
            scenes.Add(new EditorBuildSettingsScene($"{Scenes}/{name}.unity", true));
        }
        // The scenes without lighting data are written by hand (Assets/NoData): kept as they are.
        foreach (var name in new[]
        {
            "NoDataAuto", "NoDataOnDemand", "NoDataCurrent", "NoDataAutoOwnSky",
            "LegacyDefault", "LegacyAuto", "LegacyTurned", "LegacyNoLight", "LegacyOwnSky",
            "CurrentDefault", "LegacySettingsOnly", "LegacyLightmapsOnly", "LegacyChildSun", "LegacyDimSun",
            "LegacyTwoSuns", "LegacyTwoSunsTurned", "LegacyNamedSun",
        })
            scenes.Add(new EditorBuildSettingsScene($"Assets/NoData/{name}.unity", true));
        EditorBuildSettings.scenes = scenes.ToArray();
        AssetDatabase.SaveAssets();
        EditorApplication.Exit(0);
    }

    static Material Mat(string scene, string name, Color color)
    {
        var m = new Material(Shader.Find("Standard")) { color = color };
        m.SetFloat("_Glossiness", 0f);
        AssetDatabase.CreateAsset(m, $"{Scenes}/{scene}{name}.mat");
        return m;
    }

    static Renderer Static(PrimitiveType type, string name, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type);
        go.name = name;
        go.transform.position = position;
        go.transform.localScale = scale;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.receiveGI = ReceiveGI.Lightmaps;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI);
        return renderer;
    }

    /// <summary>The span of a panel's plane that its lightmap UVs map to 0..1, in metres.</summary>
    const float PanelUVSpan = 8f;

    /// <summary>
    /// A flat panel in its local XY plane, facing -z: the solid cells (<c>#</c>) of a grid over
    /// <paramref name="xs"/> x <paramref name="ys"/> (metres; <paramref name="rows"/> from the top
    /// one down), which share their corners and so make one lightmap UV chart, and loose plates
    /// (x, y, width, height), each a chart of its own. UVs are the positions over
    /// <see cref="PanelUVSpan"/>; with <paramref name="realtimeUVScale"/> the mesh also has a
    /// third UV channel, those UVs scaled by it.
    /// </summary>
    static Renderer Panel(string scene, string name, Vector3 position, Vector3 euler, Material material,
        float[] xs, float[] ys, string[] rows, Rect[] plates, Vector2? realtimeUVScale = null)
    {
        var vertices = new List<Vector3>();
        var triangles = new List<int>();
        var cornerOf = new Dictionary<(int, int), int>();
        int Vertex(float x, float y)
        {
            vertices.Add(new Vector3(x, y, 0f));
            return vertices.Count - 1;
        }
        int Corner(int c, int r)
        {
            if (!cornerOf.TryGetValue((c, r), out var index))
                cornerOf[(c, r)] = index = Vertex(xs[c], ys[r]);
            return index;
        }
        void Quad(int a, int b, int c, int d)
        {
            triangles.AddRange(new[] { a, c, b, a, d, c });
        }
        for (var r = 0; r < rows.Length; r++)
        {
            var row = rows[rows.Length - 1 - r];
            for (var c = 0; c < row.Length; c++)
                if (row[c] == '#')
                {
                    // In this order: each corner is made when first met.
                    var a = Corner(c, r);
                    var b = Corner(c + 1, r);
                    var d = Corner(c + 1, r + 1);
                    var e = Corner(c, r + 1);
                    Quad(a, b, d, e);
                }
        }
        foreach (var plate in plates)
        {
            var a = Vertex(plate.xMin, plate.yMin);
            var b = Vertex(plate.xMax, plate.yMin);
            var d = Vertex(plate.xMax, plate.yMax);
            var e = Vertex(plate.xMin, plate.yMax);
            Quad(a, b, d, e);
        }
        var uvs = vertices.ConvertAll(v => new Vector2(v.x / PanelUVSpan, v.y / PanelUVSpan));
        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetNormals(vertices.ConvertAll(_ => Vector3.back));
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, uvs);
        if (realtimeUVScale is Vector2 scale)
            mesh.SetUVs(2, uvs.ConvertAll(uv => Vector2.Scale(uv, scale)));
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, $"{Scenes}/{scene}{name}.asset");

        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.position = position;
        go.transform.rotation = Quaternion.Euler(euler);
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.receiveGI = ReceiveGI.Lightmaps;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI);
        var so = new SerializedObject(renderer);
        so.FindProperty("m_PreserveUVs").boolValue = true;
        so.ApplyModifiedPropertiesWithoutUndo();
        return renderer;
    }

    static void CreateRoom(string name, LightmapsMode mode, float albedoBoost, float indirectScale, bool twoSystems = false, bool bakedEnvironment = false, bool charts = false)
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("Main Camera", typeof(Camera));
        camera.tag = "MainCamera";
        camera.transform.position = new Vector3(0f, 2f, -6.5f);
        camera.transform.rotation = Quaternion.Euler(5f, 0f, 0f);
        var cam = camera.GetComponent<Camera>();
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.black;
        cam.fieldOfView = 60f;

        var white = Mat(name, "White", new Color(0.8f, 0.8f, 0.8f));
        var red = Mat(name, "Red", new Color(0.8f, 0.1f, 0.1f));
        var green = Mat(name, "Green", new Color(0.1f, 0.8f, 0.1f));
        var emissive = Mat(name, "Emissive", new Color(0.2f, 0.2f, 0.2f));
        emissive.EnableKeyword("_EMISSION");
        emissive.SetColor("_EmissionColor", new Color(1f, 0.5f, 0.1f) * 2f);
        emissive.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
        var sphereMat = Mat(name, "Sphere", new Color(0.8f, 0.8f, 0.8f));

        var lit = new List<Renderer>
        {
            Static(PrimitiveType.Cube, "Floor", new Vector3(0f, -0.1f, 0f), new Vector3(6f, 0.2f, 6f), white),
            Static(PrimitiveType.Cube, "Ceiling", new Vector3(0f, 4.1f, 0f), new Vector3(6f, 0.2f, 6f), white),
            Static(PrimitiveType.Cube, "Back", new Vector3(0f, 2f, 3.1f), new Vector3(6f, 4f, 0.2f), white),
            Static(PrimitiveType.Cube, "Left", new Vector3(-3.1f, 2f, 0f), new Vector3(0.2f, 4f, 6f), red),
            Static(PrimitiveType.Cube, "Right", new Vector3(3.1f, 2f, 0f), new Vector3(0.2f, 4f, 6f), green),
            Static(PrimitiveType.Cube, "Block", new Vector3(0.8f, 0.75f, 0.8f), new Vector3(1.2f, 1.5f, 1.2f), white),
            Static(PrimitiveType.Cube, "Panel", new Vector3(-1.2f, 0.6f, 1.6f), new Vector3(1f, 1.2f, 0.1f), emissive),
        };

        if (charts)
        {
            var ringXs = new[] { 0f, 0.3f, 4.7f, 5f };
            var ringYs = new[] { 0f, 0.3f, 3.1f, 3.4f };
            var ring = new[] { "###", "#.#", "###" };
            // On the back wall: a ring whose hole takes two plates exactly.
            lit.Add(Panel(name, "Ring", new Vector3(-2.5f, 0.3f, 2.95f), Vector3.zero, white, ringXs, ringYs, ring,
                new[] { new Rect(0.6f, 1.1f, 1.2f, 1.2f), new Rect(3.2f, 1.1f, 1.2f, 1.2f) }));
            // On the right wall: the same ring with a third plate.
            lit.Add(Panel(name, "RingGrown", new Vector3(2.95f, 0.3f, 2.5f), new Vector3(0f, 90f, 0f), white, ringXs, ringYs, ring,
                new[] { new Rect(0.6f, 1.1f, 1.2f, 1.2f), new Rect(1.9f, 1.1f, 1.2f, 1.2f), new Rect(3.2f, 1.1f, 1.2f, 1.2f) }));
            // Under the ceiling: two holes, the larger one first from the bottom left.
            lit.Add(Panel(name, "Holes", new Vector3(-2.8f, 3.95f, 2.2f), new Vector3(-90f, 0f, 0f), white,
                new[] { 0f, 0.3f, 2.65f, 2.95f, 5.3f, 5.6f }, new[] { 0f, 0.3f, 3.2f, 4.1f, 4.4f },
                new[] { "#####", "#.###", "#.#.#", "#####" },
                new[] { new Rect(0.9f, 1f, 1.2f, 1.2f), new Rect(3.5f, 1f, 1.2f, 1.2f) }));
            // On the left wall: an arch, its pocket open downwards.
            lit.Add(Panel(name, "Arch", new Vector3(-2.95f, 0.8f, -1.7f), new Vector3(0f, -90f, 0f), white,
                new[] { 0f, 0.3f, 3.1f, 3.4f }, new[] { 0f, 2.1f, 2.4f }, new[] { "###", "#.#" },
                new[] { new Rect(1.1f, 0.4f, 1.2f, 1.2f) }));
            // On the floor: a 2 x 1 m plate whose third UV channel is a square. Realtime lightmap
            // UVs come from Mesh.uv3 when a mesh has it (Manual, "Generate lightmap UVs").
            lit.Add(Panel(name, "Channels", new Vector3(-2.6f, 0.02f, -2.6f), new Vector3(90f, 0f, 0f), white,
                new[] { 0f, 2f }, new[] { 0f, 1f }, new[] { "#" }, new Rect[0], new Vector2(0.5f, 1f)));
        }

        if (twoSystems)
        {
            var tagged = new LightmapParameters { systemTag = 1 };
            AssetDatabase.CreateAsset(tagged, $"{Settings}/{name}SystemTag1.giparams");
            foreach (var r in new[] { lit[3], lit[4], lit[6] })
            {
                var so = new SerializedObject(r);
                so.FindProperty("m_LightmapParameters").objectReferenceValue = tagged;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        var point = new GameObject("Point Light", typeof(Light)).GetComponent<Light>();
        point.transform.position = new Vector3(-1f, 3f, -0.5f);
        point.type = LightType.Point;
        point.color = new Color(1f, 0.95f, 0.85f);
        point.intensity = 2f;
        point.range = 10f;
        point.shadows = LightShadows.Hard;
        point.lightmapBakeType = LightmapBakeType.Realtime;
        point.bounceIntensity = 1f;

        var spot = new GameObject("Spot Light", typeof(Light)).GetComponent<Light>();
        spot.transform.position = new Vector3(1.5f, 3.5f, -1f);
        spot.transform.rotation = Quaternion.LookRotation(new Vector3(-0.6f, -1f, 0.8f));
        spot.type = LightType.Spot;
        spot.color = new Color(0.6f, 0.8f, 1f);
        spot.intensity = 3f;
        spot.range = 9f;
        spot.spotAngle = 50f;
        spot.shadows = LightShadows.Hard;
        spot.lightmapBakeType = LightmapBakeType.Realtime;
        spot.enabled = false;

        var samples = new List<Vector3>();
        foreach (var x in new[] { -2f, 2f })
            foreach (var y in new[] { 0.5f, 3.5f })
                foreach (var z in new[] { -2f, 2f })
                    samples.Add(new Vector3(x, y, z));
        samples.Add(new Vector3(0f, 2f, 0f));
        var probes = new GameObject("Light Probes", typeof(LightProbeGroup)).GetComponent<LightProbeGroup>();
        probes.probePositions = samples.ToArray();

        var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        sphere.name = "Sphere";
        sphere.transform.position = new Vector3(-1.8f, 1f, -1f);
        var sphereRenderer = sphere.GetComponent<MeshRenderer>();
        sphereRenderer.sharedMaterial = sphereMat;

        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.1f, 0.12f, 0.2f);
        RenderSettings.skybox = null;

        var settings = new LightingSettings
        {
            name = name,
            bakedGI = bakedEnvironment,
            realtimeGI = true,
            realtimeEnvironmentLighting = !bakedEnvironment,
            // The baked lightmaps hold the environment alone (the lights are realtime): small.
            lightmapper = LightingSettings.Lightmapper.ProgressiveCPU,
            lightmapResolution = 4f,
            lightmapMaxSize = 256,
            lightmapPadding = 2,
            directSampleCount = 16,
            indirectSampleCount = 64,
            environmentSampleCount = 64,
            maxBounces = 1,
            indirectResolution = 2f,
            directionalityMode = mode,
            albedoBoost = albedoBoost,
            indirectScale = indirectScale,
        };
        AssetDatabase.CreateAsset(settings, $"{Settings}/{name}.lighting");
        Lightmapping.lightingSettings = settings;

        var holder = new GameObject("RealtimeGIScene").AddComponent<RealtimeGIScene>();
        holder.point = point;
        holder.spot = spot;
        holder.panel = lit[6];
        holder.leftWall = lit[3];
        holder.sphere = sphereRenderer;
        holder.lit = lit.ToArray();
        var queries = new List<Vector3>(samples) { new Vector3(-1.8f, 1f, -1f), new Vector3(1f, 2.5f, -1.5f) };
        holder.samples = queries.ToArray();

        EditorSceneManager.SaveScene(scene, $"{Scenes}/{name}.unity");
        if (!Lightmapping.Bake()) Debug.LogError("precomputing " + name + " failed");
        EditorSceneManager.SaveScene(scene);

    }
}
