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

    static void CreateRoom(string name, LightmapsMode mode, float albedoBoost, float indirectScale, bool twoSystems = false, bool bakedEnvironment = false, bool charts = false, bool autoCharts = false)
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
