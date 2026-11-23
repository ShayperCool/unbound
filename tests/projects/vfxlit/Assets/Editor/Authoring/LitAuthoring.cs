using System;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.VFX;
using UnityEditor.VFX.Block;
using UnityEditor.ShaderGraph;
using UnityEditor.ShaderGraph.Internal;
using UnityEditor.Rendering.Universal.ShaderGraph;
using UnityEditor.VFX.URP;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.VFX;

/// <summary>
/// Authors the probe's graphs, objects and scene in the Editor, through the graph package's
/// own models (this assembly carries the name of a friend assembly of the graph package and
/// of the render pipeline's editor assembly). The files it writes are kept under Authored/
/// (tests/authored_project.py) and replayed by the engine.
/// </summary>
public static class LitAuthoring
{
    public const string GraphFolder = "Assets/Graphs";
    const string ObjectFolder = "Assets/Objects";
    public const string Scene = "Assets/Scenes/Probe.unity";

    public static void Author()
    {
        Directory.CreateDirectory(GraphFolder);
        Directory.CreateDirectory(ObjectFolder);
        AssetDatabase.Refresh();
        Objects();
        LitQuads();
        Meshes();
        SortedAndCurves();
        ShaderGraphs();
        ListsMeshesAndLegacyGraphs();
        LegacyDefaultTexture();
        Decals();
        SingleMeshes();
        MovingVectors();
        PointsAndLines();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        BuildScene();
    }

    // ---------------------------------------------------------------------------- objects

    /// <summary>An image whose four quarters differ (a texture drawn turned or mirrored
    /// shows), and a mesh of the probe's own: a wedge whose faces all point differently, with
    /// normals and texture coordinates, in two submeshes.</summary>
    static void Objects()
    {
        var quarters = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                quarters.SetPixel(x, y, y < 4 ? (x < 4 ? Color.red : Color.green) : (x < 4 ? Color.blue : Color.yellow));
        File.WriteAllBytes(ObjectFolder + "/Quarters.png", quarters.EncodeToPNG());

        // A wedge: a right triangle in the plane z = -0.5 drawn out to z = 0.5.
        Vector3 a = new Vector3(-0.5f, -0.5f, -0.5f), b = new Vector3(0.5f, -0.5f, -0.5f), c = new Vector3(-0.5f, 0.5f, -0.5f);
        Vector3 back = new Vector3(0f, 0f, 1f);
        var corners = new[]
        {
            // The front and the back triangle, then the three sides as quads.
            new[] { a, c, b }, new[] { a + back, b + back, c + back },
            new[] { a, b, b + back, a + back }, new[] { b, c, c + back, b + back }, new[] { c, a, a + back, c + back },
        };
        var vertices = new System.Collections.Generic.List<Vector3>();
        var normals = new System.Collections.Generic.List<Vector3>();
        var uvs = new System.Collections.Generic.List<Vector2>();
        var first = new System.Collections.Generic.List<int>();
        var second = new System.Collections.Generic.List<int>();
        for (int face = 0; face < corners.Length; face++)
        {
            var points = corners[face];
            var normal = Vector3.Cross(points[1] - points[0], points[2] - points[0]).normalized;
            int at = vertices.Count;
            var triangles = face < 2 ? first : second;
            for (int i = 0; i < points.Length; i++)
            {
                vertices.Add(points[i]);
                normals.Add(normal);
                uvs.Add(new Vector2(i == 1 || i == 2 ? 1f : 0f, i >= 2 ? 1f : 0f));
            }
            triangles.AddRange(new[] { at, at + 1, at + 2 });
            if (points.Length == 4) triangles.AddRange(new[] { at, at + 2, at + 3 });
        }
        var mesh = new Mesh { subMeshCount = 2 };
        mesh.SetVertices(vertices);
        mesh.SetNormals(normals);
        mesh.SetUVs(0, uvs);
        mesh.SetTriangles(first, 0);
        mesh.SetTriangles(second, 1);
        mesh.RecalculateBounds();
        AssetDatabase.CreateAsset(mesh, ObjectFolder + "/Wedge.asset");

        // A spike: four faces from a square base up to a point, in one submesh, without a base.
        Vector3 top = new Vector3(0f, 0.6f, 0f);
        var foot = new[] { new Vector3(-0.5f, -0.4f, -0.5f), new Vector3(0.5f, -0.4f, -0.5f), new Vector3(0.5f, -0.4f, 0.5f), new Vector3(-0.5f, -0.4f, 0.5f) };
        vertices.Clear();
        normals.Clear();
        uvs.Clear();
        first.Clear();
        for (int face = 0; face < 4; face++)
        {
            Vector3 p = foot[face], q = foot[(face + 1) % 4];
            var normal = Vector3.Cross(top - p, q - p).normalized;
            int at = vertices.Count;
            vertices.AddRange(new[] { p, top, q });
            normals.AddRange(new[] { normal, normal, normal });
            uvs.AddRange(new[] { new Vector2(0f, 0f), new Vector2(0.5f, 1f), new Vector2(1f, 0f) });
            first.AddRange(new[] { at, at + 1, at + 2 });
        }
        var spike = new Mesh();
        spike.SetVertices(vertices);
        spike.SetNormals(normals);
        spike.SetUVs(0, uvs);
        spike.SetTriangles(first, 0);
        spike.RecalculateBounds();
        AssetDatabase.CreateAsset(spike, ObjectFolder + "/Spike.asset");
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    static Texture2D Quarters => AssetDatabase.LoadAssetAtPath<Texture2D>(ObjectFolder + "/Quarters.png");
    static Mesh Wedge => AssetDatabase.LoadAssetAtPath<Mesh>(ObjectFolder + "/Wedge.asset");
    static Mesh Spike => AssetDatabase.LoadAssetAtPath<Mesh>(ObjectFolder + "/Spike.asset");

    // ----------------------------------------------------------------------------- graphs

    /// <summary>Points (`Output Particle Point`) and lines (`Output Particle Line`, from each
    /// particle's position to its target position): sixty-four opaque ones at random places, each
    /// a colour of its own, drawn by the output itself or through an indirect buffer (the list
    /// the update makes); the same lines drawn as quads a pixel wide (the output's default, not
    /// native lines); points and lines of which some have died (lifetimes from 0.05 to 0.3
    /// seconds: the dead are drawn too, the program puts them nowhere); and lines to an offset
    /// from each particle (the output's default target, one unit up).</summary>
    static void PointsAndLines()
    {
        foreach (var (name, lines, native, listed) in new[]
        {
            ("PointStill", false, false, false), ("PointList", false, false, true),
            ("LineStill", true, true, false), ("LineList", true, true, true), ("LineSoft", true, false, false),
            ("PointDying", false, false, false), ("LineDying", true, true, false), ("LineOffset", true, true, false),
        })
        {
            var graph = NewGraph(name);
            VFXBasicSpawner spawn;
            VFXBasicInitialize init;
            VFXContext output;
            if (lines)
            {
                var (lineSpawn, lineInit, _, line) = System<VFXLineOutput>(graph, 64);
                line.SetSettingValue("useNativeLines", native);
                line.SetSettingValue("useTargetOffset", name == "LineOffset");
                (spawn, init, output) = (lineSpawn, lineInit, line);
            }
            else
            {
                var (pointSpawn, pointInit, _, point) = System<VFXPointOutput>(graph, 64);
                (spawn, init, output) = (pointSpawn, pointInit, point);
            }
            output.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
            if (listed) output.SetSettingValue("indirectDraw", true);
            Burst(spawn, 64f);
            SetRandom(init, "position", new Vector3(-1.4f, -1.4f, -0.5f), new Vector3(1.4f, 1.4f, 0.5f));
            if (lines && name != "LineOffset") SetRandom(init, "targetPosition", new Vector3(-1.4f, -1.4f, -0.5f), new Vector3(1.4f, 1.4f, 0.5f));
            if (name.EndsWith("Dying", StringComparison.Ordinal))
            {
                var lifetime = Make<SetAttribute>();
                lifetime.SetSettingValue("attribute", "lifetime");
                lifetime.SetSettingValue("Random", RandomMode.Uniform);
                init.AddChild(lifetime);
                lifetime.inputSlots[0].value = 0.05f;
                lifetime.inputSlots[1].value = 0.3f;
            }
            SetRandom(init, "color", new Vector3(0.3f, 0.3f, 0.3f), new Vector3(1f, 1f, 1f));
            Save(graph);
        }
    }

    /// <summary>Opaque particles moving sideways fast, writing motion vectors: quads lit by the
    /// render pipeline (no base colour map) and meshes (the package's default mesh, turned each
    /// its own way, unlit).</summary>
    static void MovingVectors()
    {
        foreach (var name in new[] { "MovingLit", "MovingMeshes" })
        {
            var graph = NewGraph(name);
            VFXBasicSpawner spawn;
            VFXBasicInitialize init;
            VFXContext output;
            if (name == "MovingLit")
            {
                var (litSpawn, litInit, _, lit) = System<VFXURPLitPlanarPrimitiveOutput>(graph, 64);
                lit.SetSettingValue("useBaseColorMap", VFXAbstractParticleURPLitOutput.BaseColorMapMode.None);
                (spawn, init, output) = (litSpawn, litInit, lit);
            }
            else
            {
                var (meshSpawn, meshInit, _, mesh) = System<VFXMeshOutput>(graph, 64);
                (spawn, init, output) = (meshSpawn, meshInit, mesh);
            }
            output.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
            output.SetSettingValue("generateMotionVector", true);
            Burst(spawn, 20f);
            SetRandom(init, "position", new Vector3(-1.4f, -1f, 0f), new Vector3(-0.8f, 1f, 0f));
            SetVector(init, "velocity", new Vector3(6f, 0f, 0f));
            Set(init, "size", 0.25f);
            if (name == "MovingMeshes") SetRandom(init, "angle", new Vector3(0f, 0f, 0f), new Vector3(90f, 90f, 90f));
            SetRandom(init, "color", new Vector3(0.3f, 0.3f, 0.3f), new Vector3(1f, 1f, 1f));
            Save(graph);
        }
    }

    /// <summary>Meshes an effect draws as they are (`Output Single Mesh`): the probe's wedge
    /// turned and scaled by the output's transform, through the render pipeline's unlit shader
    /// (both submeshes, a colour of the output's) and its lit shader (the first submesh only,
    /// another colour and smoothness of the output's).</summary>
    static void SingleMeshes()
    {
        var graph = NewGraph("SingleMesh");
        foreach (var (shader, x, mask, color) in new[]
        {
            ("Universal Render Pipeline/Unlit", -0.6f, ~0u, new Color(1f, 0.55f, 0.2f, 1f)),
            ("Universal Render Pipeline/Lit", 0.6f, 1u, new Color(0.3f, 0.5f, 1f, 1f)),
        })
        {
            var output = Make<VFXStaticMeshOutput>();
            graph.AddChild(output);
            output.SetSettingValue("shader", Shader.Find(shader));
            Input(output, "mesh").value = Wedge;
            Input(output, "transform").value = new UnityEditor.VFX.Transform
            {
                position = new Vector3(x, 0.1f, 0f),
                angles = new Vector3(25f, 40f, 10f),
                scale = new Vector3(0.9f, 1.1f, 0.8f),
            };
            Input(output, "subMeshMask").value = mask;
            Input(output, "_BaseColor").value = color;
            if (shader.EndsWith("/Lit", StringComparison.Ordinal)) Input(output, "_Smoothness").value = 0.7f;
        }
        Save(graph);
    }

    /// <summary>Decals of the render pipeline (`Output Particle URP Lit Decal`): boxes 0.9
    /// across centred on a wall's plane, each turned about the axis it projects along and of a colour
    /// of its own, the image whose quarters differ as their base colour map.</summary>
    static void Decals()
    {
        var graph = NewGraph("LitDecal");
        var (spawn, init, _, output) = System<VFXDecalURPOutput>(graph, 16);
        Burst(spawn, 5f);
        SetRandom(init, "position", new Vector3(-1f, -1f, 1.5f), new Vector3(1f, 1f, 1.5f));
        SetRandom(init, "angle", new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 90f));
        Set(init, "size", 0.9f);
        SetRandom(init, "color", new Vector3(0.3f, 0.3f, 0.3f), new Vector3(1f, 1f, 1f));
        Input(output, "baseColorMap").value = AssetDatabase.LoadAssetAtPath<Texture2D>(ObjectFolder + "/Quarters.png");
        Save(graph);
    }

    /// <summary>Lit quads, drawn by the output itself in the system's order (opaque, or
    /// added to what is behind): turned every way, so that each takes the lights
    /// differently.</summary>
    static void LitQuads()
    {
        (VFXGraph, VFXBasicInitialize, VFXURPLitPlanarPrimitiveOutput) Lit(string name, VFXAbstractRenderedOutput.BlendMode blend = VFXAbstractRenderedOutput.BlendMode.Opaque)
        {
            var graph = NewGraph(name);
            var (spawn, init, _, output) = System<VFXURPLitPlanarPrimitiveOutput>(graph, 16);
            output.SetSettingValue("blendMode", blend);
            // No base colour map: the default one is the package's.
            output.SetSettingValue("useBaseColorMap", VFXAbstractParticleURPLitOutput.BaseColorMapMode.None);
            Burst(spawn, 9f);
            SetRandom(init, "position", new Vector3(-1.1f, -1.1f, -0.5f), new Vector3(1.1f, 1.1f, 0.5f));
            SetRandom(init, "angle", new Vector3(-50f, -50f, 0f), new Vector3(50f, 50f, 90f));
            Set(init, "size", 0.7f);
            return (graph, init, output);
        }

        // Rough and not metallic, each quad a colour of its own.
        var (matte, matteInit, matteOutput) = Lit("LitMatte");
        SetRandom(matteInit, "color", new Vector3(0.3f, 0.3f, 0.3f), new Vector3(1f, 1f, 1f));
        Input(matteOutput, "smoothness").value = 0.1f;
        Input(matteOutput, "metallic").value = 0f;
        Save(matte);

        // Smooth: highlights of the lights.
        var (smooth, smoothInit, smoothOutput) = Lit("LitSmooth");
        SetVector(smoothInit, "color", new Vector3(0.8f, 0.3f, 0.2f));
        Input(smoothOutput, "smoothness").value = 0.8f;
        Input(smoothOutput, "metallic").value = 0f;
        Save(smooth);

        // Metallic.
        var (metal, metalInit, metalOutput) = Lit("LitMetal");
        SetVector(metalInit, "color", new Vector3(0.9f, 0.7f, 0.3f));
        Input(metalOutput, "smoothness").value = 0.6f;
        Input(metalOutput, "metallic").value = 1f;
        Save(metal);

        // Added to what is behind.
        var (added, addedInit, addedOutput) = Lit("LitAdditive", VFXAbstractRenderedOutput.BlendMode.Additive);
        SetVector(addedInit, "color", new Vector3(0.2f, 0.5f, 0.9f));
        Input(addedOutput, "smoothness").value = 0.4f;
        Input(addedOutput, "metallic").value = 0f;
        Save(added);

        // With a base colour map (the image whose quarters differ) and a colour of its own
        // that it emits.
        var mapped = NewGraph("LitMapped");
        var (mappedSpawn, mappedInit, _, mappedOutput) = System<VFXURPLitPlanarPrimitiveOutput>(mapped, 16);
        mappedOutput.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
        mappedOutput.SetSettingValue("useEmissive", true);
        Burst(mappedSpawn, 9f);
        SetRandom(mappedInit, "position", new Vector3(-1.1f, -1.1f, -0.5f), new Vector3(1.1f, 1.1f, 0.5f));
        SetRandom(mappedInit, "angle", new Vector3(-50f, -50f, 0f), new Vector3(50f, 50f, 90f));
        Set(mappedInit, "size", 0.7f);
        Input(mappedOutput, "baseColorMap").value = Quarters;
        Input(mappedOutput, "emissiveColor").value = new Color(0.05f, 0.2f, 0.3f, 1f);
        Input(mappedOutput, "smoothness").value = 0.3f;
        Input(mappedOutput, "metallic").value = 0f;
        Save(mapped);

        // Three outputs of one system, in this order in the graph: lit quads, small unlit
        // quads before them and smaller unlit meshes before those. The lit output's and the
        // mesh output's sort priority is 1, the unlit quads' 0: among the effect's outputs the
        // unlit quads come first although the graph lists them second, and the two of one
        // priority keep the graph's order.
        var (outputs, outputsInit, outputsLit) = Lit("LitOutputs");
        outputsLit.SetSettingValue("vfxSystemSortPriority", 1);
        SetVector(outputsInit, "color", new Vector3(0.4f, 0.8f, 0.5f));
        Input(outputsLit, "smoothness").value = 0.3f;
        Input(outputsLit, "metallic").value = 0f;
        var update = outputsLit.inputFlowSlot[0].link[0].context;
        var outputsQuads = Make<VFXPlanarPrimitiveOutput>();
        outputs.AddChild(outputsQuads);
        update.LinkTo(outputsQuads);
        outputsQuads.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
        Set(outputsQuads, "size", 0.25f);
        Nearer(outputsQuads, 0.8f);
        var outputsMeshes = Make<VFXMeshOutput>();
        outputs.AddChild(outputsMeshes);
        update.LinkTo(outputsMeshes);
        outputsMeshes.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
        outputsMeshes.SetSettingValue("vfxSystemSortPriority", 1);
        Input(outputsMeshes, "mesh").value = Wedge;
        Input(outputsMeshes, "mainTexture").value = Quarters;
        Set(outputsMeshes, "size", 0.12f);
        Nearer(outputsMeshes, 1.6f);
        Save(outputs);

        // Unlit quads that cast shadows.
        var unlitCaster = NewGraph("UnlitCaster");
        var (unlitSpawn, unlitInit, _, unlitOutput) = System<VFXPlanarPrimitiveOutput>(unlitCaster, 16);
        unlitOutput.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
        unlitOutput.SetSettingValue("castShadows", true);
        Burst(unlitSpawn, 9f);
        SetRandom(unlitInit, "position", new Vector3(-1.1f, -1.1f, -0.5f), new Vector3(1.1f, 1.1f, 0.5f));
        SetRandom(unlitInit, "angle", new Vector3(-50f, -50f, 0f), new Vector3(50f, 50f, 90f));
        Set(unlitInit, "size", 0.7f);
        SetVector(unlitInit, "color", new Vector3(0.9f, 0.5f, 0.2f));
        Save(unlitCaster);

        // An output that writes motion vectors: not drawn here, what its asset asks of the
        // effect's renderer is recorded.
        var (moving, movingInit, movingOutput) = Lit("LitMoving");
        movingOutput.SetSettingValue("generateMotionVector", true);
        SetVector(movingInit, "color", new Vector3(0.5f, 0.5f, 0.5f));
        Save(moving);

        // An asset saved with renderer settings of its own: it receives shadows, and the two
        // settings the graph's compilation decides are saved as what it does not decide.
        var (receiving, receivingInit, _) = Lit("LitReceiving");
        SetVector(receivingInit, "color", new Vector3(0.5f, 0.5f, 0.5f));
        var saved = receiving.GetResource().rendererSettings;
        saved.receiveShadows = true;
        saved.shadowCastingMode = ShadowCastingMode.TwoSided;
        saved.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
        receiving.GetResource().rendererSettings = saved;
        Save(receiving);

        // Casting shadows: on what is behind, and on each other.
        var (caster, casterInit, casterOutput) = Lit("LitCaster");
        casterOutput.SetSettingValue("castShadows", true);
        SetVector(casterInit, "color", new Vector3(0.9f, 0.9f, 0.5f));
        Input(casterOutput, "smoothness").value = 0.2f;
        Input(casterOutput, "metallic").value = 0f;
        Save(caster);
    }

    /// <summary>Particles drawn as a mesh of the probe's own, unlit and lit, turned every
    /// way.</summary>
    static void Meshes()
    {
        (VFXGraph, VFXBasicInitialize, TOutput) Shapes<TOutput>(string name) where TOutput : VFXAbstractParticleOutput
        {
            var graph = NewGraph(name);
            var (spawn, init, _, output) = System<TOutput>(graph, 16);
            output.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
            Input(output, "mesh").value = Wedge;
            Burst(spawn, 9f);
            SetRandom(init, "position", new Vector3(-1.1f, -1.1f, -0.5f), new Vector3(1.1f, 1.1f, 0.5f));
            SetRandom(init, "angle", new Vector3(0f, 0f, 0f), new Vector3(360f, 360f, 360f));
            Set(init, "size", 0.6f);
            return (graph, init, output);
        }

        // Unlit, textured with the image whose quarters differ.
        var (plain, plainInit, plainOutput) = Shapes<VFXMeshOutput>("MeshUnlit");
        SetRandom(plainInit, "color", new Vector3(0.5f, 0.5f, 0.5f), new Vector3(1f, 1f, 1f));
        Input(plainOutput, "mainTexture").value = Quarters;
        Save(plain);

        // Lit.
        var (lit, litInit, litOutput) = Shapes<VFXURPLitMeshOutput>("MeshLit");
        litOutput.SetSettingValue("useBaseColorMap", VFXAbstractParticleURPLitOutput.BaseColorMapMode.None);
        SetRandom(litInit, "color", new Vector3(0.3f, 0.3f, 0.3f), new Vector3(1f, 1f, 1f));
        Input(litOutput, "smoothness").value = 0.5f;
        Input(litOutput, "metallic").value = 0f;
        Save(lit);

        // The mesh and the mask of its submeshes drawn are exposed: the probe changes them.
        var (exposed, exposedInit, exposedOutput) = Shapes<VFXMeshOutput>("MeshExposed");
        SetRandom(exposedInit, "color", new Vector3(0.5f, 0.5f, 0.5f), new Vector3(1f, 1f, 1f));
        Input(exposedOutput, "mainTexture").value = Quarters;
        Link(Parameter(exposed, typeof(Mesh), "Shape", Wedge).outputSlots[0], Input(exposedOutput, "mesh"));
        Link(Parameter(exposed, typeof(uint), "Mask", 3u).outputSlots[0], Input(exposedOutput, "subMeshMask"));
        Save(exposed);

        // No mesh at all.
        var (none, noneInit, noneOutput) = Shapes<VFXMeshOutput>("MeshNone");
        SetRandom(noneInit, "color", new Vector3(0.5f, 0.5f, 0.5f), new Vector3(1f, 1f, 1f));
        Input(noneOutput, "mesh").value = null;
        Save(none);

        // Lit meshes that cast shadows.
        var (casting, castingInit, castingOutput) = Shapes<VFXURPLitMeshOutput>("MeshCaster");
        castingOutput.SetSettingValue("useBaseColorMap", VFXAbstractParticleURPLitOutput.BaseColorMapMode.None);
        castingOutput.SetSettingValue("castShadows", true);
        SetVector(castingInit, "color", new Vector3(0.6f, 0.9f, 0.6f));
        Input(castingOutput, "smoothness").value = 0.2f;
        Input(castingOutput, "metallic").value = 0f;
        Save(casting);

        // Ten a second rising and living two fifths of a second: particles die between ones
        // that live, in a system that draws every particle it has room for.
        var dying = NewGraph("MeshDying");
        var (dyingSpawn, dyingInit, _, dyingOutput) = System<VFXMeshOutput>(dying, 16);
        dyingOutput.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
        Input(dyingOutput, "mesh").value = Wedge;
        Input(dyingOutput, "mainTexture").value = Quarters;
        ConstantRate(dyingSpawn, 10f);
        SetRandom(dyingInit, "position", new Vector3(-1.2f, -1.2f, 0f), new Vector3(1.2f, -0.6f, 0f));
        SetVector(dyingInit, "velocity", new Vector3(0f, 4f, 0f));
        SetRandom(dyingInit, "angle", new Vector3(0f, 0f, 0f), new Vector3(360f, 360f, 360f));
        Set(dyingInit, "lifetime", 0.4f);
        Set(dyingInit, "size", 0.5f);
        Save(dying);

        // Through an indirect buffer: meshes blended over what is behind them, half transparent,
        // which such an output sorts by default (unlit, its mesh exposed as `Shape`, and lit);
        // and opaque meshes of an output told to draw through one, from the list the system's
        // update makes, not sorted. Spread over a depth of three units: the order shows.
        var (sorted, sortedInit, sortedOutput) = Shapes<VFXMeshOutput>("MeshSorted");
        sortedOutput.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Alpha);
        SetRandom(sortedInit, "position", new Vector3(-1.1f, -1.1f, -1.5f), new Vector3(1.1f, 1.1f, 1.5f));
        SetRandom(sortedInit, "color", new Vector3(0.3f, 0.3f, 0.3f), new Vector3(1f, 1f, 1f));
        Set(sortedInit, "alpha", 0.6f);
        Input(sortedOutput, "mainTexture").value = Quarters;
        Link(Parameter(sorted, typeof(Mesh), "Shape", Wedge).outputSlots[0], Input(sortedOutput, "mesh"));
        Save(sorted);

        var (litSorted, litSortedInit, litSortedOutput) = Shapes<VFXURPLitMeshOutput>("MeshLitSorted");
        litSortedOutput.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Alpha);
        litSortedOutput.SetSettingValue("useBaseColorMap", VFXAbstractParticleURPLitOutput.BaseColorMapMode.None);
        SetRandom(litSortedInit, "position", new Vector3(-1.1f, -1.1f, -1.5f), new Vector3(1.1f, 1.1f, 1.5f));
        SetRandom(litSortedInit, "color", new Vector3(0.3f, 0.3f, 0.3f), new Vector3(1f, 1f, 1f));
        Set(litSortedInit, "alpha", 0.6f);
        Input(litSortedOutput, "smoothness").value = 0.5f;
        Input(litSortedOutput, "metallic").value = 0f;
        Save(litSorted);

        var (listed, listedInit, listedOutput) = Shapes<VFXMeshOutput>("MeshListed");
        listedOutput.SetSettingValue("indirectDraw", true);
        SetRandom(listedInit, "color", new Vector3(0.3f, 0.3f, 0.3f), new Vector3(1f, 1f, 1f));
        Input(listedOutput, "mainTexture").value = Quarters;
        Save(listed);
    }

    /// <summary>Where the stages drawn through the 2D renderer stand: one row of their own,
    /// a hundred apart (LitProbe.cs places them the same way). The scene's 2D lights stand
    /// at these places.</summary>
    static Vector3 Place2D(int place) => new Vector3(1000f + 100f * place, 2000f, 0f);

    /// <summary>The render pipeline package's own graphs for particles: lit by 2D lights and
    /// unlit, as sprites.</summary>
    const string SpriteLitGraph = "Packages/com.unity.render-pipelines.universal/Shaders/2D/VFXSpriteLit.shadergraph";
    const string SpriteUnlitGraph = "Packages/com.unity.render-pipelines.universal/Shaders/2D/VFXSpriteUnlit.shadergraph";
    /// <summary>The graph package's default graph (URP Unlit, opaque), which does not draw
    /// particles itself: the probe's copy of it does.</summary>
    const string DefaultGraph = "Packages/com.unity.visualeffectgraph/ShaderGraph/VFXDefault.shadergraph";
    const string UnlitGraph = GraphFolder + "/Unlit.shadergraph";
    /// <summary>The render pipeline's lit graph (URP Lit, opaque, its default surface), its
    /// support for particles on, its base colour the probe's image through a texture property
    /// of its own.</summary>
    const string LitGraph = GraphFolder + "/Lit.shadergraph";

    static ShaderGraphVfxAsset ShaderGraph(string path) =>
        AssetDatabase.LoadAllAssetsAtPath(path).OfType<ShaderGraphVfxAsset>().FirstOrDefault()
        ?? throw new InvalidOperationException(path + " has no graph for particles");

    /// <summary>Outputs drawn by a Shader Graph: quads of `Output Particle ShaderGraph Quad`
    /// drawn by the render pipeline package's sprite graphs through the 2D renderer, and by
    /// an unlit graph (the graph package's default one, made to draw particles) through the
    /// forward renderer; and the lit sprite graph given to `Output Particle Quad`.</summary>
    static void ShaderGraphs()
    {
        File.WriteAllText(UnlitGraph, File.ReadAllText(Path.GetFullPath(DefaultGraph)).Replace("\"m_SupportVFX\": false", "\"m_SupportVFX\": true"));
        AssetDatabase.ImportAsset(UnlitGraph, ImportAssetOptions.ForceUpdate);
        (VFXGraph, VFXBasicInitialize, VFXContext) Drawn<TOutput>(string name, string shaderGraph) where TOutput : VFXContext
        {
            var graph = NewGraph(name);
            var (spawn, init, _, output) = System<TOutput>(graph, 16);
            if (output is VFXAbstractComposedParticleOutput) output.SetSettingValue("m_Topology", new ParticleTopologyPlanarPrimitive(VFXPrimitiveType.Quad));
            output.SetSettingValue("shaderGraph", ShaderGraph(shaderGraph));
            // The graphs' own texture: the image whose quarters differ.
            var texture = ((IVFXSlotContainer)output).inputSlots.FirstOrDefault(s => s.property.type == typeof(Texture2D))
                ?? throw new InvalidOperationException(name + ": the output of " + shaderGraph + " takes no texture");
            texture.value = Quarters;
            Burst(spawn, 9f);
            // Spread in depth too: through the 2D renderer what is drawn first does not follow
            // the distance to the camera alone.
            SetRandom(init, "position", new Vector3(-1.1f, -1.1f, -0.5f), new Vector3(1.1f, 1.1f, 0.5f));
            SetRandom(init, "angle", new Vector3(-50f, -50f, 0f), new Vector3(50f, 50f, 90f));
            SetRandom(init, "color", new Vector3(0.3f, 0.3f, 0.3f), new Vector3(1f, 1f, 1f));
            Set(init, "size", 0.7f);
            return (graph, init, output);
        }

        Save(Drawn<VFXComposedParticleOutput>("SpriteLit", SpriteLitGraph).Item1);
        Save(Drawn<VFXComposedParticleOutput>("SpriteUnlit", SpriteUnlitGraph).Item1);
        Save(Drawn<VFXComposedParticleOutput>("GraphUnlit", UnlitGraph).Item1);
        // A lit graph of the render pipeline: besides the passes cameras draw, it has the
        // lightmapper's (`Meta`).
        var lit = new UniversalTarget();
        lit.TrySetActiveSubTarget(typeof(UniversalLitSubTarget));
        typeof(UniversalTarget).GetField("m_SupportVFX", BindingFlags.NonPublic | BindingFlags.Instance).SetValue(lit, true);
        WriteImageGraph(LitGraph, lit, new[]
        {
            BlockFields.VertexDescription.Position, BlockFields.VertexDescription.Normal, BlockFields.VertexDescription.Tangent,
            BlockFields.SurfaceDescription.BaseColor, BlockFields.SurfaceDescription.NormalTS, BlockFields.SurfaceDescription.Metallic,
            BlockFields.SurfaceDescription.Smoothness, BlockFields.SurfaceDescription.Emission, BlockFields.SurfaceDescription.Occlusion,
        }, true);
        Save(Drawn<VFXComposedParticleOutput>("GraphLit", LitGraph).Item1);
        // The graph given to `Output Particle Quad` instead, whose own setting takes it.
        Save(Drawn<VFXPlanarPrimitiveOutput>("SpriteClassic", SpriteLitGraph).Item1);
        // The output of the probe's other unlit quads, without a Shader Graph, through the 2D
        // renderer.
        var plain = NewGraph("PlainUnlit");
        var (plainSpawn, plainInit, _, plainOutput) = System<VFXPlanarPrimitiveOutput>(plain, 16);
        plainOutput.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Alpha);
        Input(plainOutput, "mainTexture").value = Quarters;
        Burst(plainSpawn, 9f);
        SetRandom(plainInit, "position", new Vector3(-1.1f, -1.1f, -0.5f), new Vector3(1.1f, 1.1f, 0.5f));
        SetRandom(plainInit, "angle", new Vector3(-50f, -50f, 0f), new Vector3(50f, 50f, 90f));
        SetRandom(plainInit, "color", new Vector3(0.3f, 0.3f, 0.3f), new Vector3(1f, 1f, 1f));
        Set(plainInit, "alpha", 0.8f);
        Set(plainInit, "size", 0.7f);
        Save(plain);
    }

    /// <summary>Shader Graphs of the Visual Effect target the graph package keeps for older
    /// graphs (shown as deprecated): unlit, their colour the probe's image, sampled through a
    /// texture property of the graph's own (`Legacy`) or as the default texture of a sampling
    /// node whose texture slot nothing feeds (`LegacyDefault`).</summary>
    const string LegacyGraph = GraphFolder + "/Legacy.shadergraph";
    const string LegacyDefaultGraph = GraphFolder + "/LegacyDefault.shadergraph";

    static void WriteLegacyGraph(string path, bool property) =>
        WriteImageGraph(path, new VFXTarget(), new[] { BlockFields.SurfaceDescription.BaseColor, BlockFields.SurfaceDescription.Alpha }, property);

    /// <summary>A graph of the target with those blocks whose base colour is the probe's image,
    /// sampled through a texture property of the graph's own or as the default texture of the
    /// sampling node.</summary>
    static void WriteImageGraph(string path, Target target, BlockFieldDescriptor[] blocks, bool property)
    {
        var graph = new GraphData();
        graph.AddContexts();
        graph.InitializeOutputs(new[] { target }, blocks);
        graph.AddCategory(CategoryData.DefaultCategory());
        graph.path = "Shader Graphs";
        var sample = new SampleTexture2DNode();
        graph.AddNode(sample);
        if (property)
        {
            var texture = new Texture2DShaderProperty { displayName = "Image" };
            graph.AddGraphInput(texture);
            var read = new PropertyNode();
            graph.AddNode(read);
            read.property = texture;
            graph.Connect(read.GetSlotReference(PropertyNode.OutputSlotId), sample.GetSlotReference(SampleTexture2DNode.TextureInputId));
        }
        else sample.FindInputSlot<Texture2DInputMaterialSlot>(SampleTexture2DNode.TextureInputId).texture = Quarters;
        var baseColor = graph.fragmentContext.blocks.Select(b => b.value).First(b => b.descriptor == BlockFields.SurfaceDescription.BaseColor);
        graph.Connect(sample.GetSlotReference(SampleTexture2DNode.OutputSlotRGBAId), baseColor.GetSlotReference(0));
        FileUtilities.WriteShaderGraphToDisk(path, graph);
        AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
    }

    /// <summary>The follow-ups of the outputs above: lit outputs drawn from a list that is not
    /// sorted, by the pipeline's lights (opaque quads told to draw through an indirect buffer)
    /// and by 2D lights (the sprite graph's quads blended over what is behind them, their sort
    /// turned off, culled by each camera's frustum); meshes of an output of two meshes, chosen per particle by its mesh index or
    /// by the size it takes on screen; a quad output drawn by a graph of the older Visual
    /// Effect target; and outputs of the forward renderer's kinds (lit quads, meshes) drawn
    /// through the 2D renderer (LitProbe.cs: `lit listed` .. `mesh 2d`).</summary>
    static void ListsMeshesAndLegacyGraphs()
    {
        var listed = NewGraph("LitListed");
        var (listedSpawn, listedInit, _, listedOutput) = System<VFXURPLitPlanarPrimitiveOutput>(listed, 16);
        listedOutput.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
        listedOutput.SetSettingValue("useBaseColorMap", VFXAbstractParticleURPLitOutput.BaseColorMapMode.None);
        listedOutput.SetSettingValue("indirectDraw", true);
        Burst(listedSpawn, 9f);
        SetRandom(listedInit, "position", new Vector3(-1.1f, -1.1f, -0.5f), new Vector3(1.1f, 1.1f, 0.5f));
        SetRandom(listedInit, "angle", new Vector3(-50f, -50f, 0f), new Vector3(50f, 50f, 90f));
        Set(listedInit, "size", 0.7f);
        SetRandom(listedInit, "color", new Vector3(0.3f, 0.3f, 0.3f), new Vector3(1f, 1f, 1f));
        Input(listedOutput, "smoothness").value = 0.4f;
        Input(listedOutput, "metallic").value = 0f;
        Save(listed);

        var sprites = NewGraph("SpriteListed");
        var (spritesSpawn, spritesInit, _, spritesOutput) = System<VFXComposedParticleOutput>(sprites, 16);
        spritesOutput.SetSettingValue("m_Topology", new ParticleTopologyPlanarPrimitive(VFXPrimitiveType.Quad));
        spritesOutput.SetSettingValue("shaderGraph", ShaderGraph(SpriteLitGraph));
        spritesOutput.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Alpha);
        spritesOutput.SetSettingValue("sort", VFXAbstractParticleOutput.SortActivationMode.Off);
        // Culled by the camera's frustum: each camera draws a list of its own, not sorted.
        spritesOutput.SetSettingValue("frustumCulling", true);
        ((IVFXSlotContainer)spritesOutput).inputSlots.First(s => s.property.type == typeof(Texture2D)).value = Quarters;
        Burst(spritesSpawn, 9f);
        SetRandom(spritesInit, "position", new Vector3(-1.1f, -1.1f, -0.5f), new Vector3(1.1f, 1.1f, 0.5f));
        SetRandom(spritesInit, "angle", new Vector3(0f, 0f, 0f), new Vector3(0f, 0f, 90f));
        SetRandom(spritesInit, "color", new Vector3(0.3f, 0.3f, 0.3f), new Vector3(1f, 1f, 1f));
        Set(spritesInit, "alpha", 0.7f);
        Set(spritesInit, "size", 0.7f);
        Save(sprites);

        (VFXGraph, VFXBasicInitialize, VFXMeshOutput) Pair(string name, bool lod)
        {
            var graph = NewGraph(name);
            var (spawn, init, _, output) = System<VFXMeshOutput>(graph, 16);
            output.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
            output.SetSettingValue("MeshCount", 2u);
            output.SetSettingValue("lod", lod);
            Input(output, "mesh0").value = Wedge;
            Input(output, "mesh1").value = Spike;
            Input(output, "mainTexture").value = Quarters;
            Burst(spawn, 9f);
            SetRandom(init, "position", new Vector3(-1.1f, -1.1f, -0.5f), new Vector3(1.1f, 1.1f, 0.5f));
            SetRandom(init, "angle", new Vector3(0f, 0f, 0f), new Vector3(360f, 360f, 360f));
            SetRandom(init, "color", new Vector3(0.5f, 0.5f, 0.5f), new Vector3(1f, 1f, 1f));
            return (graph, init, output);
        }
        // Each particle's mesh by its index, drawn between 0 and 2 (0 or 1); the second mesh
        // shows only its first submesh. Then the same meshes chosen by the size each particle
        // takes on screen, the sizes on both sides of the first threshold.
        var (pair, pairInit, pairOutput) = Pair("MeshPair", false);
        var index = Make<SetAttribute>();
        index.SetSettingValue("attribute", "meshIndex");
        index.SetSettingValue("Random", RandomMode.Uniform);
        pairInit.AddChild(index);
        index.inputSlots[0].value = 0u;
        index.inputSlots[1].value = 2u;
        Set(pairInit, "size", 0.6f);
        Input(pairOutput, "subMeshMask0").value = 1u;
        Save(pair);
        var (lodded, loddedInit, loddedOutput) = Pair("MeshLod", true);
        Input(loddedOutput, "lodValues").value = new Vector4(30f, 0f, 0f, 0f);
        var sizes = Make<SetAttribute>();
        sizes.SetSettingValue("attribute", "size");
        sizes.SetSettingValue("Random", RandomMode.Uniform);
        loddedInit.AddChild(sizes);
        sizes.inputSlots[0].value = 0.3f;
        sizes.inputSlots[1].value = 1.2f;
        Save(lodded);

        WriteLegacyGraph(LegacyGraph, true);
        var legacy = NewGraph("GraphLegacy");
        var (legacySpawn, legacyInit, _, legacyOutput) = System<VFXPlanarPrimitiveOutput>(legacy, 16);
        legacyOutput.SetSettingValue("shaderGraph", ShaderGraph(LegacyGraph));
        var image = ((IVFXSlotContainer)legacyOutput).inputSlots.FirstOrDefault(s => s.property.type == typeof(Texture2D))
            ?? throw new InvalidOperationException("GraphLegacy: the output of " + LegacyGraph + " takes no texture");
        image.value = Quarters;
        Burst(legacySpawn, 9f);
        SetRandom(legacyInit, "position", new Vector3(-1.1f, -1.1f, -0.5f), new Vector3(1.1f, 1.1f, 0.5f));
        SetRandom(legacyInit, "angle", new Vector3(-50f, -50f, 0f), new Vector3(50f, 50f, 90f));
        Set(legacyInit, "size", 0.7f);
        Save(legacy);
    }

    /// <summary>Wedges of `Output Particle Mesh` drawn by a graph of the older Visual Effect
    /// target whose sampling node reads its slot's default texture (LitProbe.cs: `graph legacy
    /// mesh`).</summary>
    static void LegacyDefaultTexture()
    {
        WriteLegacyGraph(LegacyDefaultGraph, false);
        var graph = NewGraph("GraphLegacyMesh");
        var (spawn, init, _, output) = System<VFXMeshOutput>(graph, 16);
        output.SetSettingValue("shaderGraph", ShaderGraph(LegacyDefaultGraph));
        output.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
        Input(output, "mesh").value = Wedge;
        Burst(spawn, 9f);
        SetRandom(init, "position", new Vector3(-1.1f, -1.1f, -0.5f), new Vector3(1.1f, 1.1f, 0.5f));
        SetRandom(init, "angle", new Vector3(0f, 0f, 0f), new Vector3(360f, 360f, 360f));
        Set(init, "size", 0.6f);
        Save(graph);
    }

    // ---------------------------------------------------------------------------- helpers

    static T Make<T>() where T : VFXModel => ScriptableObject.CreateInstance<T>();

    static VFXGraph NewGraph(string name)
    {
        var asset = VisualEffectAssetEditorUtility.CreateNewAsset(GraphFolder + "/" + name + ".vfx");
        return asset.GetResource().GetOrCreateGraph();
    }

    static void Save(VFXGraph graph)
    {
        var resource = graph.GetResource();
        resource.WriteAssetWithSubAssets();
        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(resource), ImportAssetOptions.ForceUpdate);
    }

    /// <summary>A spawner, an initialize, an update and an output of the kind asked for,
    /// linked; returns them.</summary>
    static (VFXBasicSpawner Spawn, VFXBasicInitialize Init, VFXBasicUpdate Update, TOutput Output) System<TOutput>(VFXGraph graph, uint capacity) where TOutput : VFXContext
    {
        var spawn = Make<VFXBasicSpawner>();
        var init = Make<VFXBasicInitialize>();
        var update = Make<VFXBasicUpdate>();
        var output = Make<TOutput>();
        graph.AddChild(spawn);
        graph.AddChild(init);
        graph.AddChild(update);
        graph.AddChild(output);
        spawn.LinkTo(init);
        init.LinkTo(update);
        update.LinkTo(output);
        init.GetData().SetSettingValue("capacity", capacity);
        return (spawn, init, update, output);
    }

    /// <summary>An input of a model by its name; the names it has are said when it has
    /// none of that name.</summary>
    static VFXSlot Input(VFXModel model, string name)
    {
        var slots = ((IVFXSlotContainer)model).inputSlots;
        var slot = slots.FirstOrDefault(s => s.name == name);
        if (slot == null) throw new InvalidOperationException(model.GetType().Name + " has no input '" + name + "': " + string.Join(", ", slots.Select(s => s.name)));
        return slot;
    }

    static VFXParameter Parameter(VFXGraph graph, Type type, string name, object value)
    {
        var parameter = VFXLibrary.GetParameters().First(d => d.modelType == type).CreateInstance();
        parameter.SetSettingValue("m_ExposedName", name);
        parameter.SetSettingValue("m_Exposed", true);
        parameter.value = value;
        graph.AddChild(parameter);
        return parameter;
    }

    static void Link(VFXSlot from, VFXSlot to)
    {
        if (!from.Link(to)) throw new InvalidOperationException("cannot link " + from.property.type.Name + " '" + from.name + "' to " + to.property.type.Name + " '" + to.name + "'");
    }

    static void ConstantRate(VFXBasicSpawner spawn, float rate)
    {
        var block = Make<VFXSpawnerConstantRate>();
        spawn.AddChild(block);
        block.inputSlots[0].value = rate;
    }

    /// <summary>Moves what an output draws towards the camera (the systems are in the
    /// effect's space, the cameras look along its z).</summary>
    static void Nearer(VFXContext output, float by)
    {
        var block = Make<SetAttribute>();
        block.SetSettingValue("attribute", "position");
        block.SetSettingValue("Composition", AttributeCompositionMode.Add);
        output.AddChild(block);
        block.inputSlots[0].value = Spaced(block.inputSlots[0], new Vector3(0f, 0f, -by));
    }

    /// <summary>One burst at the start.</summary>
    static void Burst(VFXBasicSpawner spawn, float count)
    {
        var block = Make<VFXSpawnerBurst>();
        spawn.AddChild(block);
        block.SetSettingValue("repeat", VFXSpawnerBurst.RepeatMode.Single);
        Input(block, "Count").value = count;
        Input(block, "Delay").value = 0f;
    }

    static SetAttribute Set(VFXContext context, string attribute, object value)
    {
        var block = Make<SetAttribute>();
        block.SetSettingValue("attribute", attribute);
        context.AddChild(block);
        block.inputSlots[0].value = value;
        return block;
    }

    /// <summary>A vector as the type a slot holds it in (the package's own spaced types).</summary>
    static object Spaced(VFXSlot slot, Vector3 value)
    {
        var type = slot.property.type;
        if (type == typeof(Position)) return (Position)value;
        if (type == typeof(Vector)) return (Vector)value;
        if (type == typeof(DirectionType)) return (DirectionType)value;
        return value;
    }

    static SetAttribute SetVector(VFXContext context, string attribute, Vector3 value)
    {
        var block = Make<SetAttribute>();
        block.SetSettingValue("attribute", attribute);
        context.AddChild(block);
        block.inputSlots[0].value = Spaced(block.inputSlots[0], value);
        return block;
    }

    /// <summary>A block that draws an attribute for each particle, component by component,
    /// between two vectors.</summary>
    static SetAttribute SetRandom(VFXContext context, string attribute, Vector3 from, Vector3 to)
    {
        var block = Make<SetAttribute>();
        block.SetSettingValue("attribute", attribute);
        block.SetSettingValue("Random", RandomMode.PerComponent);
        context.AddChild(block);
        block.inputSlots[0].value = Spaced(block.inputSlots[0], from);
        block.inputSlots[1].value = Spaced(block.inputSlots[1], to);
        return block;
    }

    /// <summary>Lit quads that are blended over what is behind them, which such an output
    /// sorts by default and draws through an indirect buffer; and quads whose output context
    /// takes each particle's size and colour from a curve and a gradient over its life, which
    /// the output's own program then samples.</summary>
    static void SortedAndCurves()
    {
        // Half transparent, each quad a colour, a turn and a depth of its own: where two
        // overlap, the order they are drawn in shows.
        var sorted = NewGraph("LitSorted");
        var (sortedSpawn, sortedInit, _, sortedOutput) = System<VFXURPLitPlanarPrimitiveOutput>(sorted, 16);
        sortedOutput.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Alpha);
        sortedOutput.SetSettingValue("useBaseColorMap", VFXAbstractParticleURPLitOutput.BaseColorMapMode.None);
        Burst(sortedSpawn, 9f);
        SetRandom(sortedInit, "position", new Vector3(-0.9f, -0.9f, -1.5f), new Vector3(0.9f, 0.9f, 1.5f));
        SetRandom(sortedInit, "angle", new Vector3(-30f, -30f, 0f), new Vector3(30f, 30f, 90f));
        SetRandom(sortedInit, "color", new Vector3(0.2f, 0.2f, 0.2f), new Vector3(1f, 1f, 1f));
        Set(sortedInit, "alpha", 0.6f);
        Set(sortedInit, "size", 1f);
        Input(sortedOutput, "smoothness").value = 0.3f;
        Input(sortedOutput, "metallic").value = 0f;
        Save(sorted);

        // Unlit quads that grow and shrink and change colour over their life, in the output.
        var curved = NewGraph("CurveOutput");
        var (curvedSpawn, curvedInit, _, curvedOutput) = System<VFXPlanarPrimitiveOutput>(curved, 32);
        curvedOutput.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
        ConstantRate(curvedSpawn, 20f);
        SetRandom(curvedInit, "position", new Vector3(-1.2f, -1.2f, 0f), new Vector3(1.2f, 1.2f, 0f));
        Set(curvedInit, "lifetime", 0.8f);
        var size = Make<AttributeFromCurve>();
        size.SetSettingValue("attribute", "size");
        curvedOutput.AddChild(size);
        size.inputSlots[0].value = new AnimationCurve(new Keyframe(0f, 0.05f), new Keyframe(0.4f, 0.5f), new Keyframe(1f, 0.1f));
        var color = Make<AttributeFromCurve>();
        color.SetSettingValue("attribute", "color");
        curvedOutput.AddChild(color);
        var ramp = new Gradient();
        ramp.SetKeys(
            new[] { new GradientColorKey(new Color(1f, 0.9f, 0.2f), 0f), new GradientColorKey(new Color(1f, 0.2f, 0.1f), 0.5f), new GradientColorKey(new Color(0.2f, 0.3f, 1f), 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(1f, 1f) });
        color.inputSlots[0].value = ramp;
        Save(curved);
    }

    // ------------------------------------------------------------------------------ scene

    /// <summary>The probe scene: a camera, one flat ambient colour and no reflection, so
    /// that what lights the particles is the lights the probe makes.</summary>
    static void BuildScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("Main Camera") { tag = "MainCamera" };
        camera.AddComponent<Camera>();
        camera.transform.position = new Vector3(0f, 0f, -10f);
        RenderSettings.skybox = null;
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.15f, 0.2f, 0.25f, 1f);
        RenderSettings.defaultReflectionMode = UnityEngine.Rendering.DefaultReflectionMode.Custom;
        RenderSettings.customReflectionTexture = null;
        RenderSettings.reflectionIntensity = 0f;
        // The 2D lights: a dim white one over every place, a point light at the second
        // place (`sprite point`), one at the third that lights by normals (`sprite
        // normals`), and one at the fifth (`sprite classic`).
        AddLight2D("global", Light2D.LightType.Global, Vector3.zero, Color.white, 0.4f, false);
        AddLight2D("point", Light2D.LightType.Point, Place2D(1) + new Vector3(0.6f, 0.5f, 0f), new Color(1f, 0.6f, 0.3f), 1.5f, false);
        AddLight2D("normals", Light2D.LightType.Point, Place2D(2) + new Vector3(0.6f, 0.5f, 0f), new Color(1f, 0.6f, 0.3f), 1.5f, true);
        AddLight2D("classic", Light2D.LightType.Point, Place2D(4) + new Vector3(0.6f, 0.5f, 0f), new Color(0.3f, 0.6f, 1f), 1.5f, false);
        // And one at the eighth (`sprite listed`).
        AddLight2D("listed", Light2D.LightType.Point, Place2D(7) + new Vector3(0.6f, 0.5f, 0f), new Color(1f, 0.6f, 0.3f), 1.5f, false);
        EditorSceneManager.SaveScene(scene, Scene);
    }

    /// <summary>A 2D light of the scene lighting every sorting layer, with a radius of two
    /// (a point light), by normals or not.</summary>
    static void AddLight2D(string name, Light2D.LightType type, Vector3 position, Color color, float intensity, bool normals)
    {
        var go = new GameObject("light2d " + name);
        go.transform.position = position;
        var light = go.AddComponent<Light2D>();
        light.lightType = type;
        light.color = color;
        light.intensity = intensity;
        light.pointLightInnerRadius = 0.2f;
        light.pointLightOuterRadius = 2f;
        var so = new SerializedObject(light);
        var layers = so.FindProperty("m_ApplyToSortingLayers");
        var ids = SortingLayer.layers.Select(l => l.id).ToArray();
        layers.arraySize = ids.Length;
        for (int i = 0; i < ids.Length; i++) layers.GetArrayElementAtIndex(i).intValue = ids[i];
        if (normals)
        {
            so.FindProperty("m_NormalMapQuality").intValue = (int)Light2D.NormalMapQuality.Accurate;
            so.FindProperty("m_NormalMapDistance").floatValue = 1f;
        }
        so.ApplyModifiedPropertiesWithoutUndo();
    }
}
