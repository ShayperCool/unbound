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

    /// <summary>Adds the <c>RoomAuto</c> scene alone: the room and a set of other shapes, every
    /// lit renderer with Preserve UVs off (its realtime charts made by the precompute).</summary>
    public static void CreateAuto()
    {
        try
        {
            CreateRoom("RoomAuto", LightmapsMode.NonDirectional, 1f, 1f, autoCharts: true);
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.Add(new EditorBuildSettingsScene($"{Scenes}/RoomAuto.unity", true));
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

    /// <summary>Adds the <c>RoomTurned</c> scene alone: the room and shapes with Preserve UVs
    /// off that are turned, or whose limits or parts decide their charts.</summary>
    public static void CreateTurned()
    {
        try
        {
            CreateRoom("RoomTurned", LightmapsMode.NonDirectional, 1f, 1f, turned: true);
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.Add(new EditorBuildSettingsScene($"{Scenes}/RoomTurned.unity", true));
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

    /// <summary>Adds the <c>RoomHoles</c> scene alone: the room with panels of two holes of one
    /// size and a plate either holds.</summary>
    public static void CreateHoles()
    {
        try
        {
            CreateRoom("RoomHoles", LightmapsMode.NonDirectional, 1f, 1f, holes: true);
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            scenes.Add(new EditorBuildSettingsScene($"{Scenes}/RoomHoles.unity", true));
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

    /// <summary>Adds the <c>RoomBake</c> and <c>RoomBakeLoaded</c> scenes alone: the room
    /// without lighting data whose LightmapSettings ask for a bake when the Editor opens it
    /// (Lightmapping.bakeOnSceneLoad IfMissingLightingData), not precomputed.</summary>
    public static void CreateBake()
    {
        try
        {
            var scenes = new List<EditorBuildSettingsScene>(EditorBuildSettings.scenes);
            foreach (var name in new[] { "RoomBake", "RoomBakeLoaded" })
            {
                CreateRoom(name, LightmapsMode.NonDirectional, 1f, 1f, bakeOnLoad: true);
                scenes.Add(new EditorBuildSettingsScene($"{Scenes}/{name}.unity", true));
            }
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

    /// <summary>
    /// A mesh of flat faces: each face a polygon with vertices and a normal of its own, cut into
    /// a fan of triangles from its first corner. A face looks away from <paramref name="inside"/>
    /// (its corners are taken in the order that makes it so); with
    /// <paramref name="inverted"/> its triangles are wound the other way, against its normal.
    /// Its lightmap UVs lay the faces side by side in a row, each stretched over a square cell
    /// (so a face that is not square has UVs that are not a scaled copy of its shape).
    /// </summary>
    static Renderer Solid(string scene, string name, Vector3 position, Vector3 euler, Material material, Vector3 inside, bool inverted, params Vector3[][] faces)
    {
        var vertices = new List<Vector3>();
        var normals = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        for (var f = 0; f < faces.Length; f++)
        {
            var face = (Vector3[])faces[f].Clone();
            var normal = Vector3.Cross(face[1] - face[0], face[2] - face[0]).normalized;
            if (Vector3.Dot(normal, face[0] - inside) < 0f)
            {
                System.Array.Reverse(face);
                normal = -normal;
            }
            var across = (face[1] - face[0]).normalized;
            var up = Vector3.Cross(normal, across);
            var first = vertices.Count;
            var flat = new List<Vector2>();
            foreach (var corner in face)
                flat.Add(new Vector2(Vector3.Dot(corner - face[0], across), Vector3.Dot(corner - face[0], up)));
            var min = flat[0];
            var max = flat[0];
            foreach (var point in flat)
            {
                min = Vector2.Min(min, point);
                max = Vector2.Max(max, point);
            }
            for (var i = 0; i < face.Length; i++)
            {
                vertices.Add(face[i]);
                normals.Add(normal);
                // A cell of 1 / faces across, with a margin of a twentieth of it.
                var inCell = new Vector2((flat[i].x - min.x) / (max.x - min.x), (flat[i].y - min.y) / (max.y - min.y)) * 0.9f + new Vector2(0.05f, 0.05f);
                uvs.Add(new Vector2((f + inCell.x) / faces.Length, inCell.y / faces.Length));
            }
            for (var i = 1; i + 1 < face.Length; i++)
                triangles.AddRange(inverted ? new[] { first, first + i + 1, first + i } : new[] { first, first + i, first + i + 1 });
        }
        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, $"{Scenes}/{scene}{name}.asset");
        return Shape(name, mesh, position, euler, Vector3.one, material);
    }

    /// <summary>A flat grid of square cells in its local XY plane, facing -z, its vertices
    /// shared between the cells: one connected surface.</summary>
    static Renderer Grid(string scene, string name, Vector3 position, Vector3 euler, Material material, int columns, int rows, float cell)
    {
        var vertices = new List<Vector3>();
        var uvs = new List<Vector2>();
        var triangles = new List<int>();
        for (var r = 0; r <= rows; r++)
            for (var c = 0; c <= columns; c++)
            {
                vertices.Add(new Vector3(c * cell, r * cell, 0f));
                uvs.Add(new Vector2((c + 0.5f) / (columns + 1), (r + 0.5f) / (rows + 1)));
            }
        for (var r = 0; r < rows; r++)
            for (var c = 0; c < columns; c++)
            {
                int a = r * (columns + 1) + c, b = a + 1, d = a + columns + 1, e = d + 1;
                triangles.AddRange(new[] { a, d, b, b, d, e });
            }
        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetNormals(vertices.ConvertAll(_ => Vector3.back));
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, $"{Scenes}/{scene}{name}.asset");
        return Shape(name, mesh, position, euler, Vector3.one, material);
    }

    static Renderer Shape(string name, Mesh mesh, Vector3 position, Vector3 euler, Vector3 scale, Material material)
    {
        var go = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
        go.transform.position = position;
        go.transform.rotation = Quaternion.Euler(euler);
        go.transform.localScale = scale;
        go.GetComponent<MeshFilter>().sharedMesh = mesh;
        var renderer = go.GetComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        renderer.receiveGI = ReceiveGI.Lightmaps;
        GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.ContributeGI);
        return renderer;
    }

    /// <summary>A prism: the polygon <paramref name="section"/> (in XY) pushed
    /// <paramref name="depth"/> along z: two caps and a side per edge.</summary>
    static Vector3[][] Prism(Vector2[] section, float depth)
    {
        var faces = new List<Vector3[]>();
        var front = new Vector3[section.Length];
        var back = new Vector3[section.Length];
        for (var i = 0; i < section.Length; i++)
        {
            front[i] = new Vector3(section[i].x, section[i].y, 0f);
            back[section.Length - 1 - i] = new Vector3(section[i].x, section[i].y, depth);
        }
        faces.Add(front);
        faces.Add(back);
        for (var i = 0; i < section.Length; i++)
        {
            var a = section[i];
            var b = section[(i + 1) % section.Length];
            faces.Add(new[] { new Vector3(a.x, a.y, 0f), new Vector3(a.x, a.y, depth), new Vector3(b.x, b.y, depth), new Vector3(b.x, b.y, 0f) });
        }
        return faces.ToArray();
    }

    /// <summary>Two rectangles that meet along a ridge on the z axis, each falling away from it
    /// by <paramref name="degrees"/>: an open roof.</summary>
    static Vector3[][] Roof(float degrees, float width, float length)
    {
        var radians = degrees * Mathf.Deg2Rad;
        var side = new Vector3(Mathf.Cos(radians) * width, -Mathf.Sin(radians) * width, 0f);
        var other = new Vector3(-side.x, side.y, 0f);
        var along = new Vector3(0f, 0f, length);
        return new[]
        {
            new[] { Vector3.zero, along, side + along, side },
            new[] { Vector3.zero, other, other + along, along },
        };
    }

    /// <summary>A mesh of the given vertices (one normal and lightmap UV each) and
    /// triangles.</summary>
    static Renderer Custom(string scene, string name, Vector3 position, Vector3 euler, Vector3 scale, Material material,
        List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles)
    {
        var mesh = new Mesh { name = name };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetUVs(1, uvs);
        mesh.SetTriangles(triangles, 0);
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, $"{Scenes}/{scene}{name}.asset");
        return Shape(name, mesh, position, euler, scale, material);
    }

    /// <summary>Adds a quad of its own four vertices to the lists: corners in order, one
    /// normal, its lightmap UVs the rectangle <paramref name="cell"/>.</summary>
    static void AddQuad(List<Vector3> vertices, List<Vector3> normals, List<Vector2> uvs, List<int> triangles,
        Vector3 a, Vector3 b, Vector3 c, Vector3 d, Vector3 normal, Rect cell)
    {
        var first = vertices.Count;
        vertices.AddRange(new[] { a, b, c, d });
        normals.AddRange(new[] { normal, normal, normal, normal });
        uvs.AddRange(new[] { new Vector2(cell.xMin, cell.yMin), new Vector2(cell.xMax, cell.yMin), new Vector2(cell.xMax, cell.yMax), new Vector2(cell.xMin, cell.yMax) });
        triangles.AddRange(new[] { first, first + 1, first + 2, first, first + 2, first + 3 });
    }

    static void CreateRoom(string name, LightmapsMode mode, float albedoBoost, float indirectScale, bool twoSystems = false, bool bakedEnvironment = false, bool charts = false, bool autoCharts = false, bool turned = false, bool holes = false, bool bakeOnLoad = false)
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

        if (holes)
        {
            // On the back wall: two holes of one size side by side and a plate either holds; on
            // the right wall the same stacked.
            lit.Add(Panel(name, "TwinHoles", new Vector3(-2.5f, 0.3f, 2.95f), Vector3.zero, white,
                new[] { 0f, 0.3f, 2.65f, 2.95f, 5.3f, 5.6f }, new[] { 0f, 0.3f, 2.8f, 3.1f }, new[] { "#####", "#.#.#", "#####" },
                new[] { new Rect(0.9f, 0.4f, 1.2f, 1.2f) }));
            lit.Add(Panel(name, "TwinHolesStacked", new Vector3(2.95f, 0.3f, 2.5f), new Vector3(0f, 90f, 0f), white,
                new[] { 0f, 0.3f, 2.8f, 3.1f }, new[] { 0f, 0.3f, 2.65f, 2.95f, 5.3f, 5.6f }, new[] { "###", "#.#", "###", "#.#", "###" },
                new[] { new Rect(0.9f, 0.4f, 1.2f, 1.2f) }));
        }

        if (autoCharts)
        {
            // A wedge (a right-angled ramp), the same wedge with its triangles wound against
            // its normals, a step (an L-shaped bar), two roofs (one nearly flat, one steep), a
            // grid of shared vertices, a thin bar, and the built-in quad and plane; then four
            // shapes whose limits decide their charts. None is turned: the precompute works
            // with a turned instance's positions and normals as its own removal of the
            // rotation rounds them.
            var wedge = new[] { new Vector2(0f, 0f), new Vector2(1.2f, 0f), new Vector2(0f, 0.9f) };
            lit.Add(Solid(name, "Wedge", new Vector3(-2.6f, 0.01f, 0.2f), Vector3.zero, white, new Vector3(0.4f, 0.3f, 0.4f), false, Prism(wedge, 0.8f)));
            // The wedge's mesh again, taller and narrower.
            lit.Add(Shape("WedgeTall", lit[lit.Count - 1].GetComponent<MeshFilter>().sharedMesh, new Vector3(1.2f, 0.01f, 2f), Vector3.zero, new Vector3(1f, 1.4f, 0.75f), white));
            lit.Add(Solid(name, "WedgeInverted", new Vector3(-2.6f, 0.01f, -1.4f), Vector3.zero, white, new Vector3(0.4f, 0.3f, 0.4f), true, Prism(wedge, 0.8f)));
            lit.Add(Solid(name, "Step", new Vector3(1.7f, 0.01f, -2.4f), Vector3.zero, white, new Vector3(0.2f, 0.15f, 0.45f), false,
                Prism(new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0.3f), new Vector2(0.4f, 0.3f), new Vector2(0.4f, 0.7f), new Vector2(0f, 0.7f) }, 0.9f)));
            lit.Add(Solid(name, "RoofFlat", new Vector3(-1.2f, 2.6f, 1.2f), Vector3.zero, white, new Vector3(0f, -1f, 0.65f), false, Roof(10f, 0.7f, 1.3f)));
            lit.Add(Solid(name, "RoofSteep", new Vector3(1.6f, 2.8f, 1.2f), Vector3.zero, white, new Vector3(0f, -1f, 0.65f), false, Roof(50f, 0.7f, 1.3f)));
            lit.Add(Grid(name, "Grid", new Vector3(-2.2f, 1.9f, 2.95f), Vector3.zero, white, 3, 2, 0.5f));
            lit.Add(Static(PrimitiveType.Cube, "Bar", new Vector3(0f, 3.2f, 2.6f), new Vector3(2f, 0.05f, 0.3f), white));
            lit.Add(Static(PrimitiveType.Quad, "Quad", new Vector3(2.2f, 1.4f, 2.95f), new Vector3(1.5f, 1f, 1f), white));
            lit.Add(Static(PrimitiveType.Plane, "Plane", new Vector3(-1.6f, 0.02f, -2f), new Vector3(0.2f, 1f, 0.15f), white));
            // Max Angle: a roof whose sides are 20 degrees from level, 40 apart. With 30 each
            // side is within the limit of a plane through both and the two are further apart
            // than it; with 15 neither holds.
            var angles = new Dictionary<string, float> { { "RoofAngleWide", 30f }, { "RoofAngleTight", 15f } };
            lit.Add(Solid(name, "RoofAngleWide", new Vector3(1.6f, 2.6f, -1.2f), Vector3.zero, white, new Vector3(0f, -1f, 0.65f), false, Roof(20f, 0.7f, 1.3f)));
            lit.Add(Solid(name, "RoofAngleTight", new Vector3(-1.9f, 1.6f, -0.9f), Vector3.zero, white, new Vector3(0f, -1f, 0.65f), false, Roof(20f, 0.6f, 1.3f)));
            // Max Distance: the step's mesh again (other lengths, so nothing is shared). Its
            // two treads are 0.4 apart and their corners at most 0.2 from a plane through both,
            // as its two risers' are: 0.3 lies between the two measures, 0.15 below both.
            var distances = new Dictionary<string, float> { { "StepMid", 0.3f }, { "StepNear", 0.15f } };
            var step = lit.Find(r => r.name == "Step").GetComponent<MeshFilter>().sharedMesh;
            lit.Add(Shape("StepMid", step, new Vector3(1.7f, 0.01f, -1.2f), Vector3.zero, new Vector3(1f, 1f, 1.2f), white));
            lit.Add(Shape("StepNear", step, new Vector3(0.2f, 0.01f, -2.6f), Vector3.zero, new Vector3(1f, 1f, 0.8f), white));
            foreach (var r in lit)
            {
                var so = new SerializedObject(r);
                so.FindProperty("m_PreserveUVs").boolValue = false;
                if (angles.TryGetValue(r.name, out var angle)) so.FindProperty("m_AutoUVMaxAngle").floatValue = angle;
                if (distances.TryGetValue(r.name, out var distance)) so.FindProperty("m_AutoUVMaxDistance").floatValue = distance;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        if (turned)
        {
            var limits = new Dictionary<string, (float distance, float angle, bool ignoreNormals)>();
            Renderer Turn(Renderer r, Vector3 euler)
            {
                r.transform.rotation = Quaternion.Euler(euler);
                return r;
            }
            // Turned: a slab, a box turned about all three axes, the wedge, and a box under a
            // turned, scaled parent.
            lit.Add(Turn(Static(PrimitiveType.Cube, "SlabTurned", new Vector3(-1.8f, 0.3f, -2.2f), new Vector3(1.3f, 0.2f, 0.9f), white), new Vector3(0f, 30f, 0f)));
            lit.Add(Turn(Static(PrimitiveType.Cube, "BoxTurned", new Vector3(2.2f, 0.6f, -2f), new Vector3(0.7f, 0.5f, 0.6f), white), new Vector3(20f, 40f, 10f)));
            var wedge = new[] { new Vector2(0f, 0f), new Vector2(1.2f, 0f), new Vector2(0f, 0.9f) };
            var wedgeTurned = Solid(name, "WedgeTurned", new Vector3(-2.4f, 0.01f, 0.6f), new Vector3(0f, 30f, 0f), white, new Vector3(0.4f, 0.3f, 0.4f), false, Prism(wedge, 0.8f));
            wedgeTurned.transform.localScale = new Vector3(0.5f, 0.4f, 0.45f);
            lit.Add(wedgeTurned);
            var pivot = new GameObject("Pivot");
            pivot.transform.position = new Vector3(1.8f, 2.6f, 1.8f);
            pivot.transform.rotation = Quaternion.Euler(0f, 45f, 0f);
            pivot.transform.localScale = new Vector3(1.5f, 1.5f, 1.5f);
            var child = Static(PrimitiveType.Cube, "Child", Vector3.zero, new Vector3(0.5f, 0.3f, 0.4f), white);
            child.transform.SetParent(pivot.transform, false);
            child.transform.localRotation = Quaternion.Euler(10f, 0f, 0f);
            lit.Add(child);
            // One mesh and scale turned and not, the rotation taken out exactly: one geometry.
            lit.Add(Turn(Static(PrimitiveType.Cube, "SharedTurned", new Vector3(2.5f, 3.5f, -2.5f), new Vector3(0.35f, 0.35f, 0.35f), white), new Vector3(0f, 25f, 0f)));
            lit.Add(Static(PrimitiveType.Cube, "SharedFlat", new Vector3(1.9f, 3.5f, -2.5f), new Vector3(0.35f, 0.35f, 0.35f), white));
            // Max Distance alone: the sides of a roof 20 degrees from level, 0.7 m wide, are
            // 0.12 m at their eaves from the plane through both and their bounds meet.
            lit.Add(Solid(name, "RoofDistance", new Vector3(-1.2f, 2.6f, -1.6f), Vector3.zero, white, new Vector3(0f, -1f, 0.65f), false, Roof(20f, 0.7f, 1.3f)));
            limits["RoofDistance"] = (0.1f, 89f, false);
            // Ignore Normals: a floor and a wall at a right angle that share their edge's
            // positions and UVs, not its normals, and a loose quad beside them.
            for (var i = 0; i < 2; i++)
            {
                List<Vector3> v = new(), n = new();
                List<Vector2> uv = new();
                List<int> t = new();
                // The two unfolded side by side in UV: the edge's corners at the same UVs.
                AddQuad(v, n, uv, t, new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 1f), new Vector3(1f, 0f, 1f), new Vector3(1f, 0f, 0f), Vector3.up, new Rect(0.5f, 0.1f, 0.4f, 0.4f));
                uv.RemoveRange(0, 4);
                uv.AddRange(new[] { new Vector2(0.5f, 0.1f), new Vector2(0.5f, 0.5f), new Vector2(0.9f, 0.5f), new Vector2(0.9f, 0.1f) });
                AddQuad(v, n, uv, t, new Vector3(0f, 1f, 0f), new Vector3(0f, 1f, 1f), new Vector3(0f, 0f, 1f), new Vector3(0f, 0f, 0f), Vector3.right, new Rect(0.1f, 0.1f, 0.4f, 0.4f));
                uv.RemoveRange(4, 4);
                uv.AddRange(new[] { new Vector2(0.1f, 0.1f), new Vector2(0.1f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.1f) });
                AddQuad(v, n, uv, t, new Vector3(2f, 0f, 0f), new Vector3(2f, 0f, 0.5f), new Vector3(2.5f, 0f, 0.5f), new Vector3(2.5f, 0f, 0f), Vector3.up, new Rect(0.1f, 0.6f, 0.3f, 0.3f));
                var fold = i == 0 ? "FoldKept" : "FoldIgnored";
                lit.Add(Custom(name, fold, new Vector3(-2.7f, 2.9f + 0.6f * i, 1.2f), Vector3.zero, Vector3.one, white, v, n, uv, t));
                limits[fold] = (0.5f, 89f, i == 1);
            }
            // A Max Distance of zero: the step's section again.
            var section = new[] { new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(1f, 0.3f), new Vector2(0.4f, 0.3f), new Vector2(0.4f, 0.7f), new Vector2(0f, 0.7f) };
            lit.Add(Solid(name, "StepNoDistance", new Vector3(1.7f, 0.01f, -2.4f), Vector3.zero, white, new Vector3(0.2f, 0.15f, 0.45f), false, Prism(section, 0.9f)));
            limits["StepNoDistance"] = (0f, 89f, false);
            // And a Max Angle of zero.
            lit.Add(Solid(name, "StepNoAngle", new Vector3(1.7f, 0.01f, -1.2f), Vector3.zero, white, new Vector3(0.2f, 0.15f, 0.45f), false, Prism(section, 0.8f)));
            limits["StepNoAngle"] = (0.5f, 0f, false);
            // A box with a triangle of no area of its own a metre away: three points on a line,
            // and three at one point.
            for (var i = 0; i < 2; i++)
            {
                List<Vector3> v = new(), n = new();
                List<Vector2> uv = new();
                List<int> t = new();
                var cells = 0;
                Rect Cell() { var c = new Rect(0.05f + 0.15f * (cells % 6), 0.05f + 0.5f * (cells / 6), 0.12f, 0.4f); cells++; return c; }
                for (var axis = 0; axis < 3; axis++)
                    foreach (var side in new[] { -1f, 1f })
                    {
                        var normal = Vector3.zero;
                        normal[axis] = side;
                        Vector3 u = Vector3.zero, w = Vector3.zero;
                        u[(axis + 1) % 3] = 0.3f;
                        w[(axis + 2) % 3] = 0.3f;
                        var centre = normal * 0.3f;
                        if (side > 0)
                            AddQuad(v, n, uv, t, centre - u - w, centre + u - w, centre + u + w, centre - u + w, normal, Cell());
                        else
                            AddQuad(v, n, uv, t, centre - u - w, centre - u + w, centre + u + w, centre + u - w, normal, Cell());
                    }
                var first = v.Count;
                var at = new Vector3(1.3f, 0f, 0f);
                v.AddRange(i == 0 ? new[] { at, at + new Vector3(0f, 0.2f, 0f), at + new Vector3(0f, 0.4f, 0f) } : new[] { at, at, at });
                n.AddRange(new[] { Vector3.forward, Vector3.forward, Vector3.forward });
                uv.AddRange(new[] { new Vector2(0.95f, 0.9f), new Vector2(0.95f, 0.92f), new Vector2(0.95f, 0.94f) });
                t.AddRange(new[] { first, first + 1, first + 2 });
                lit.Add(Custom(name, i == 0 ? "BoxSliver" : "BoxPoint", new Vector3(-0.4f + 1.4f * i, 3.2f, 0.4f), Vector3.zero, Vector3.one, white, v, n, uv, t));
            }
            // More flat parts than rounds: 11 x 10 loose quads in one plane, apart by more than
            // their limit's reach.
            {
                List<Vector3> v = new(), n = new();
                List<Vector2> uv = new();
                List<int> t = new();
                for (var row = 0; row < 10; row++)
                    for (var column = 0; column < 11; column++)
                    {
                        var corner = new Vector3(0.25f * column, 0.25f * row, 0f);
                        AddQuad(v, n, uv, t, corner, corner + new Vector3(0f, 0.1f, 0f), corner + new Vector3(0.1f, 0.1f, 0f), corner + new Vector3(0.1f, 0f, 0f), Vector3.back,
                            new Rect((column + 0.1f) / 11f, (row + 0.1f) / 10f, 0.8f / 11f, 0.8f / 10f));
                    }
                lit.Add(Custom(name, "Many", new Vector3(-1.4f, 0.6f, 2.95f), Vector3.zero, Vector3.one, white, v, n, uv, t));
                limits["Many"] = (0.1f, 89f, false);
            }
            foreach (var r in lit)
            {
                var so = new SerializedObject(r);
                so.FindProperty("m_PreserveUVs").boolValue = false;
                if (limits.TryGetValue(r.name, out var limit))
                {
                    so.FindProperty("m_AutoUVMaxDistance").floatValue = limit.distance;
                    so.FindProperty("m_AutoUVMaxAngle").floatValue = limit.angle;
                    so.FindProperty("m_IgnoreNormalsForChartDetection").boolValue = limit.ignoreNormals;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
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

        if (bakeOnLoad)
        {
            // Saved without lighting data, asking for a bake when opened.
            Lightmapping.lightingDataAsset = null;
            Lightmapping.bakeOnSceneLoad = Lightmapping.BakeOnSceneLoadMode.IfMissingLightingData;
            EditorSceneManager.SaveScene(scene, $"{Scenes}/{name}.unity");
            // The emissive material with its keyword, as a precomputed scene's is saved (no
            // bake here writes it again).
            emissive.EnableKeyword("_EMISSION");
            EditorUtility.SetDirty(emissive);
            AssetDatabase.SaveAssetIfDirty(emissive);
            return;
        }
        EditorSceneManager.SaveScene(scene, $"{Scenes}/{name}.unity");
        if (!Lightmapping.Bake()) Debug.LogError("precomputing " + name + " failed");
        EditorSceneManager.SaveScene(scene);

    }
}
