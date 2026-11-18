using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.VFX;
using UnityEditor.VFX.Block;
using UnityEditor.VFX.Operator;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.VFX;

/// <summary>
/// Authors the probe's graphs and scene in the Editor, through the graph package's own models
/// (this assembly carries the name of one of the package's friend assemblies). The files it
/// writes are kept under Authored/ (tests/authored_project.py) and replayed by the engine.
/// </summary>
public static class GraphAuthoring
{
    const string Folder = "Assets/Graphs";
    /// <summary>The simulation controls' graphs, apart from the graphs the import rows list.</summary>
    const string SimulationFolder = "Assets/SimGraphs";
    const string ObjectFolder = "Assets/Objects";
    public const string Scene = "Assets/Scenes/Probe.unity";

    public static void Author()
    {
        Directory.CreateDirectory(Folder);
        Directory.CreateDirectory(SimulationFolder);
        Directory.CreateDirectory(DrawFolder);
        Directory.CreateDirectory(ObjectFolder);
        AssetDatabase.Refresh();
        Objects();
        Empty();
        Basic();
        Exposed();
        Exposed2();
        Events();
        Attributes();
        Attributes2();
        Simulated();
        Expressions();
        Chained();
        Drawn();
        DrawnOverLife();
        Cameras();
        OutputEvents();
        Operations();
        Meshes();
        MeshOperations();
        DrawnFromMeshes();
        EventReadings();
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
        BuildScene();
        Bundled();
    }

    /// <summary>A block that sets an attribute over each particle's life from a curve (the
    /// colour and alpha from a gradient), given as a value or wired to a slot.</summary>
    static AttributeFromCurve OverLife(VFXContext context, string attribute, object value = null, VFXSlot source = null)
    {
        var block = Make<AttributeFromCurve>();
        block.SetSettingValue("attribute", attribute);
        context.AddChild(block);
        if (value != null) block.inputSlots[0].value = value;
        if (source != null) Link(source, block.inputSlots[0]);
        return block;
    }

    static Gradient Ramp(GradientMode mode, float alpha, params (float Time, Color Color)[] keys)
    {
        var gradient = new Gradient { mode = mode };
        gradient.SetKeys(keys.Select(k => new GradientColorKey(k.Color, k.Time)).ToArray(),
            new[] { new GradientAlphaKey(alpha, 0f), new GradientAlphaKey(alpha, 1f) });
        return gradient;
    }

    /// <summary>The graphs whose particles change over their lives by curves and gradients
    /// (README.md, "Drawing"): the programs sample them from a texture the effect bakes.</summary>
    static void DrawnOverLife()
    {
        // Twenty a second living a second: their size from an exposed curve, their colour
        // from an exposed gradient, their alpha from a curve that loops both ways.
        var graph = NewGraph("DrawOverLife", DrawFolder);
        var (spawn, init, update, output) = System(graph, 64);
        output.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Additive);
        ConstantRate(spawn, 20f);
        SetRandom(init, "position", new Vector3(-1.2f, -1f, 0f), new Vector3(1.2f, 1f, 0f));
        Set(init, "lifetime", 1f);
        var size = Parameter(graph, typeof(AnimationCurve), "Size", new AnimationCurve(new Keyframe(0f, 0.05f), new Keyframe(0.5f, 0.45f), new Keyframe(1f, 0.1f)));
        var tint = Parameter(graph, typeof(Gradient), "Tint", Ramp(GradientMode.Blend, 1f, (0f, Color.red), (0.5f, Color.green), (1f, Color.blue)));
        OverLife(update, "size", source: size.outputSlots[0]);
        OverLife(update, "color", source: tint.outputSlots[0]);
        OverLife(update, "alpha", new AnimationCurve(new Keyframe(0f, 0.2f), new Keyframe(0.5f, 1f), new Keyframe(1f, 0.2f)) { preWrapMode = WrapMode.Loop, postWrapMode = WrapMode.Loop });
        Save(graph);

        // Colours brighter than a half float holds, in a gradient that is fixed between its
        // keys: the texture keeps such a row scaled, with its scale. Twenty quads a second
        // living a second, so their ages span the key in the middle; each samples the
        // gradient at its age and takes the colour divided back into what an image shows, so
        // that a row stored unscaled, or its scale left out, would show in every channel.
        var hdr = NewGraph("DrawHdr", DrawFolder);
        var (hdrSpawn, hdrInit, hdrUpdate, hdrOutput) = System(hdr, 64);
        hdrOutput.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
        ConstantRate(hdrSpawn, 20f);
        SetRandom(hdrInit, "position", new Vector3(-1.2f, -1f, -1f), new Vector3(1.2f, 1f, 1f));
        Set(hdrInit, "size", 0.3f);
        Set(hdrInit, "lifetime", 1f);
        var age = Make<AgeOverLifetime>();
        hdr.AddChild(age);
        var bright = Make<SampleGradient>();
        hdr.AddChild(bright);
        bright.inputSlots[0].value = Ramp(GradientMode.Fixed, 1f, (0.5f, new Color(131008f, 65504f, 32752f)), (1f, new Color(32752f, 65504f, 131008f)));
        Link(age.outputSlots[0], bright.inputSlots[1]);
        var colour = Make<SetAttribute>();
        colour.SetSettingValue("attribute", "color");
        hdrUpdate.AddChild(colour);
        for (int channel = 0; channel < 3; channel++)
        {
            var dim = Make<Multiply>();
            hdr.AddChild(dim);
            dim.SetOperandType(0, typeof(float));
            dim.SetOperandType(1, typeof(float));
            Link(bright.outputSlots[0][channel], dim.inputSlots[0]);
            dim.inputSlots[1].value = 1f / 262016f;
            Link(dim.outputSlots[0], colour.inputSlots[0][channel]);
        }
        Save(hdr);

        // Curves that loop after their last key and not before their first, sampled just
        // below their end, where the last of the texture's samples shows: twelve quads added
        // to what is behind, their size from one curve and their alpha from another. The
        // first curve's last sample falls on its last key; the second's keys are such that
        // it falls just short of it.
        var end = NewGraph("DrawLoopEnd", DrawFolder);
        var (endSpawn, endInit, endUpdate, endOutput) = System(end, 16);
        endOutput.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Additive);
        Burst(endSpawn, false, 12f, 0f);
        SetRandom(endInit, "position", new Vector3(-1.2f, -1f, 0f), new Vector3(1.2f, 1f, 0f));
        SetVector(endInit, "color", new Vector3(0.3f, 0.6f, 1f));
        AtTime(endUpdate, "size", new AnimationCurve(new Keyframe(0f, 0.05f), new Keyframe(1f, 0.5f)) { postWrapMode = WrapMode.Loop }, 0.997f);
        AtTime(endUpdate, "alpha", new AnimationCurve(new Keyframe(0.3f, 0.2f), new Keyframe(1.4f, 1f)) { postWrapMode = WrapMode.Loop }, 0.3f + 0.997f * 1.1f);
        Save(end);
    }

    const string CameraFolder = "Assets/CameraGraphs";

    /// <summary>The camera controls' graphs (README.md, "Cameras").</summary>
    static void Cameras()
    {
        Directory.CreateDirectory(CameraFolder);
        AssetDatabase.Refresh();

        // What the main camera's values evaluate to, read by a script block of a spawn context.
        var values = NewGraph("CamValues", CameraFolder);
        var (valuesSpawn, _, _, _) = System(values, 8);
        ConstantRate(valuesSpawn, 1f);
        var block = Make<VFXSpawnerCustomWrapper>();
        block.SetSettingValue("m_customType", new SerializableType(typeof(CameraInputs)));
        valuesSpawn.AddChild(block);
        var main = Make<MainCamera>();
        values.AddChild(main);
        foreach (var (output, input) in new[]
                 {
                     ("transform", "m00"), ("fieldOfView", "f00"), ("nearPlane", "f01"), ("farPlane", "f02"), ("orthographicSize", "f03"),
                     ("aspectRatio", "f04"), ("orthographic", "b00"), ("pixelDimensions", "p00"), ("scaledPixelDimensions", "p01"), ("lensShift", "p02"),
                 })
            Link(main.outputSlots[0].children.First(s => s.name == output), Input(block, input));
        Save(values);

        // Particles whose colour is a texel of the main camera's depth buffer, read on the GPU
        // when they are made: the graph reads the main camera's depth.
        var depth = NewGraph("CamDepth", CameraFolder);
        var (depthSpawn, depthInit, _, _) = System(depth, 8);
        ConstantRate(depthSpawn, 2f);
        var buffer = Make<MainCamera>();
        depth.AddChild(buffer);
        var load = Make<LoadCameraBuffer>();
        depth.AddChild(load);
        Link(buffer.outputSlots[0].children.First(s => s.name == "depthBuffer"), load.inputSlots[0]);
        load.inputSlots[1].value = 32u;
        load.inputSlots[2].value = 24u;
        var colour = Make<SetAttribute>();
        colour.SetSettingValue("attribute", "color");
        depthInit.AddChild(colour);
        for (int channel = 0; channel < 3; channel++) Link(load.outputSlots[0][channel], colour.inputSlots[0][channel]);
        Save(depth);

        // Eight quads added to what is behind, fading over 0.6 where they near what is opaque
        // behind them (soft particles); nothing sorts them.
        var soft = NewGraph("SoftAdditive", CameraFolder);
        var (softSpawn, softInit, _, softOutput) = System(soft, 16);
        softOutput.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Additive);
        softOutput.SetSettingValue("useSoftParticle", true);
        Input(softOutput, "softParticleFadeDistance").value = 0.6f;
        Burst(softSpawn, false, 8f, 0f);
        SetRandom(softInit, "position", new Vector3(-1.2f, -0.8f, 0f), new Vector3(1.2f, 0.8f, 0f));
        Set(softInit, "size", 0.9f);
        SetVector(softInit, "color", new Vector3(0.6f, 0.4f, 0.2f));
        Save(soft);
    }

    const string EventFolder = "Assets/EventGraphs";

    /// <summary>A spawn context of its own in a graph.</summary>
    static VFXBasicSpawner Spawner(VFXGraph graph)
    {
        var spawner = Make<VFXBasicSpawner>();
        graph.AddChild(spawner);
        return spawner;
    }

    /// <summary>A block of a spawn context that sets an event attribute.</summary>
    static void SpawnSet(VFXBasicSpawner spawner, string attribute, object value)
    {
        var block = Make<VFXSpawnerSetAttribute>();
        block.SetSettingValue("attribute", attribute);
        spawner.AddChild(block);
        block.inputSlots[0].value = value;
    }

    /// <summary>An output event context of a name, fed by spawn contexts.</summary>
    static void OutputEvent(VFXGraph graph, string name, params VFXBasicSpawner[] from)
    {
        var output = Make<VFXOutputEvent>();
        output.SetSettingValue("eventName", name);
        graph.AddChild(output);
        foreach (var spawner in from) spawner.LinkTo(output);
    }

    /// <summary>The output event controls' graph (README.md, "Output events"): five spawn
    /// contexts, each step of a fixed time step (two a frame).</summary>
    static void OutputEvents()
    {
        Directory.CreateDirectory(EventFolder);
        AssetDatabase.Refresh();
        var graph = NewGraph("OutEvents", EventFolder);
        // Ten a second with three event attributes set, also spawning particles (so that a
        // camera sees the effect); three every 0.2 s; half a particle once.
        var (rate, _, _, _) = System(graph, 64);
        ConstantRate(rate, 10f);
        SpawnSet(rate, "color", new Vector3(0.25f, 0.5f, 0.75f));
        SpawnSet(rate, "lifetime", 2f);
        var burst = Spawner(graph);
        Burst(burst, true, 3f, 0.2f);
        SpawnSet(burst, "size", 0.125f);
        var half = Spawner(graph);
        Burst(half, false, 0.5f, 0.1f);
        // Another ten a second, firing in the same steps as the first, and sixty a second (in
        // every step, two a frame).
        var twin = Spawner(graph);
        ConstantRate(twin, 10f);
        SpawnSet(twin, "size", 0.25f);
        var fast = Spawner(graph);
        ConstantRate(fast, 60f);
        // Contexts of one name make one event fed by all of them.
        OutputEvent(graph, "Tick", rate);
        OutputEvent(graph, "Tick", burst);
        OutputEvent(graph, "Tick", twin);
        OutputEvent(graph, "Burst", burst);
        OutputEvent(graph, "Never", half);
        OutputEvent(graph, "Fast", fast);
        graph.GetResource().updateMode = VFXUpdateMode.ExactFixedTimeStep;
        Save(graph);
    }

    const string OperationFolder = "Assets/OpGraphs";

    /// <summary>The operation controls' graph (README.md, "Operations"): a spawn context whose
    /// script block reads matrices built from and split into rows and columns, a look-at frame
    /// with its position, angles and scale, colour spaces, a texture's and a buffer's sizes and a
    /// camera's viewport point in the world, all over exposed properties.</summary>
    static void Operations()
    {
        Directory.CreateDirectory(OperationFolder);
        AssetDatabase.Refresh();
        var graph = NewGraph("OpsValues", OperationFolder);
        var (spawn, _, _, _) = System(graph, 8);
        ConstantRate(spawn, 1f);
        var block = Make<VFXSpawnerCustomWrapper>();
        block.SetSettingValue("m_customType", new SerializableType(typeof(OpsInputs)));
        spawn.AddChild(block);
        VFXSlot Exposed(Type type, string name, object value) => Parameter(graph, type, name, value).outputSlots[0];
        void To(VFXSlot from, string input) => Link(from, Input(block, input));
        var a = Exposed(typeof(Vector4), "A", new Vector4(1f, 2f, 3f, 4f));
        var b = Exposed(typeof(Vector4), "B", new Vector4(5f, -6f, 7f, 8f));
        var c = Exposed(typeof(Vector4), "C", new Vector4(-1f, 0.5f, 0.25f, 2f));
        var d = Exposed(typeof(Vector4), "D", new Vector4(0f, 0.125f, -3f, 1f));
        foreach (var (order, input) in new[] { (ConstructMatrix.Order.Row, "m00"), (ConstructMatrix.Order.Column, "m01") })
        {
            var built = Make<ConstructMatrix>();
            graph.AddChild(built);
            built.SetSettingValue("order", order);
            Link(a, built.inputSlots[0]);
            Link(b, built.inputSlots[1]);
            Link(c, built.inputSlots[2]);
            Link(d, built.inputSlots[3]);
            To(built.outputSlots[0], input);
        }
        var m = Exposed(typeof(Matrix4x4), "M", new Matrix4x4(new Vector4(1f, 2f, 3f, 4f), new Vector4(-5f, 6f, 7f, 8f), new Vector4(9f, -10f, 11f, 12f), new Vector4(13f, 14f, -15f, 16f)));
        foreach (var (order, first, second) in new[] { (SplitMatrix.Order.Row, "q00", "q01"), (SplitMatrix.Order.Column, "q02", "q03") })
        {
            var split = Make<SplitMatrix>();
            graph.AddChild(split);
            split.SetSettingValue("order", order);
            Link(m, split.inputSlots[0]);
            To(split.outputSlots[1], first);
            To(split.outputSlots[2], second);
        }
        var look = Make<LookAt>();
        graph.AddChild(look);
        Link(Exposed(typeof(Position), "From", (Position)new Vector3(1f, 2f, 3f)), look.inputSlots[0]);
        Link(Exposed(typeof(Position), "Target", (Position)new Vector3(-4f, 5f, -1f)), look.inputSlots[1]);
        To(look.outputSlots[0], "m02");
        To(look.outputSlots[0].children.First(s => s.name == "position"), "v00");
        To(look.outputSlots[0].children.First(s => s.name == "angles"), "v01");
        To(look.outputSlots[0].children.First(s => s.name == "scale"), "v02");
        // A matrix of a translation, a rotation and an uneven scale, with a last row that is
        // not (0, 0, 0, 1), taken as a frame (its position, angles and scale).
        var n = Matrix4x4.TRS(new Vector3(10f, 20f, 30f), Quaternion.Euler(30f, -120f, 200f), new Vector3(2f, 0.5f, 3f));
        n.m30 = 0.25f;
        n.m33 = 2f;
        var frame = Make<Branch>();
        graph.AddChild(frame);
        frame.SetOperandType(typeof(UnityEditor.VFX.Transform));
        Link(Exposed(typeof(bool), "Pick", true), frame.inputSlots[0]);
        var nSlot = Exposed(typeof(Matrix4x4), "N", n);
        Link(nSlot, frame.inputSlots[1]);
        Link(nSlot, frame.inputSlots[2]);
        To(frame.outputSlots[0].children.First(s => s.name == "position"), "v04");
        To(frame.outputSlots[0].children.First(s => s.name == "angles"), "v05");
        To(frame.outputSlots[0].children.First(s => s.name == "scale"), "v06");
        var hsv = Make<RGBtoHSV>();
        graph.AddChild(hsv);
        Link(Exposed(typeof(Color), "Tint", new Color(0.8f, 0.3f, 0.6f, 1f)), hsv.inputSlots[0]);
        To(hsv.outputSlots[0], "v03");
        var rgb = Make<HSVtoRGB>();
        graph.AddChild(rgb);
        Link(Exposed(typeof(Vector3), "Hue", new Vector3(0.71f, 0.6f, 0.9f)), rgb.inputSlots[0]);
        To(rgb.outputSlots[0], "q04");
        var dims = Make<TextureDimensions>();
        graph.AddChild(dims);
        dims.SetOperandType(typeof(Texture2D));
        Link(Exposed(typeof(Texture2D), "Image", Object<Texture2D>("ImageC")), dims.inputSlots[0]);
        To(dims.outputSlots[0], "u00");
        To(dims.outputSlots[1], "u01");
        var count = Make<BufferCount>();
        graph.AddChild(count);
        Link(Exposed(typeof(GraphicsBuffer), "Buffer", null), count.inputSlots[0]);
        To(count.outputSlots[0], "u02");
        // A point of a camera's viewport in the world: the inverse of the camera's projection,
        // a camera whose values are each an exposed property.
        var point = Make<ViewportToWorldPoint>();
        graph.AddChild(point);
        point.SetSettingValue("camera", CameraMode.Custom);
        Link(Exposed(typeof(Vector3), "Viewport", new Vector3(0.25f, 0.75f, 6f)), point.inputSlots[0]);
        var eye = Input(point, "Camera");
        foreach (var (field, type, name, value) in new (string, Type, string, object)[]
                 {
                     ("transform", typeof(Matrix4x4), "EyeFrame", Matrix4x4.TRS(new Vector3(1f, 2f, -10f), Quaternion.Euler(10f, 20f, 5f), Vector3.one)),
                     ("orthographic", typeof(bool), "EyeOrtho", false), ("fieldOfView", typeof(float), "EyeFov", 1.1f),
                     ("nearPlane", typeof(float), "EyeNear", 0.3f), ("farPlane", typeof(float), "EyeFar", 1000f),
                     ("orthographicSize", typeof(float), "EyeSize", 5f), ("aspectRatio", typeof(float), "EyeAspect", 16f / 9f),
                     ("lensShift", typeof(Vector2), "EyeShift", new Vector2(0.1f, -0.2f)),
                 })
            Link(Exposed(type, name, value), eye.children.First(s => s.name == field));
        To(point.outputSlots[0], "v07");
        graph.GetResource().cullingFlags = VFXCullingFlags.CullNone;
        Save(graph);
    }

    /// <summary>The meshes of the mesh controls (README.md, "Mesh operations"). `MeshMixed`:
    /// five vertices in two streams, positions and normals as floats, tangents as signed 16-bit
    /// norms, colours as bytes, first texture coordinates as halves and second ones as unsigned
    /// 16-bit norms, three triangles of 16-bit indices; `MeshLocked`: the same without read/write
    /// access. `MeshWide`: four vertices in one stream of floats (position, colour, four texture
    /// coordinates), 32-bit indices in two submeshes.</summary>
    static void Meshes()
    {
        var mixed = new Mesh { name = "MeshMixed" };
        mixed.SetVertexBufferParams(5,
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
            new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3, 0),
            new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.SNorm16, 4, 0),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4, 1),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float16, 2, 1),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.UNorm16, 2, 1));
        var positions = new[] { new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0.5f), new Vector3(-1f, 1f, 0f), new Vector3(1f, 1f, -0.5f), new Vector3(0f, 0.3f, 1f) };
        var normals = new[] { new Vector3(0f, 0f, -2f), new Vector3(0.5f, 0.5f, -1f), new Vector3(1f, 0f, 0f), new Vector3(0f, 3f, 4f), new Vector3(-1f, -1f, -1f) };
        var tangents = new short[] { 32767, 0, -16384, 32767, -32768, 100, 200, -32767, 1, 2, 3, 4, 16000, -16000, 8000, -8000, 0, 0, 32767, -1 };
        var colours = new byte[] { 255, 0, 0, 255, 0, 255, 0, 128, 0, 0, 255, 64, 128, 64, 32, 255, 200, 150, 100, 50 };
        var uv0 = new[] { 0.5f, 0.25f, 1.5f, -2f, 0.1f, 0.2f, 65504f, -0.0001f, 3.14159f, 2.71828f };
        var uv1 = new ushort[] { 0, 65535, 32768, 1, 12345, 54321, 65535, 65535, 100, 200 };
        var first = new MemoryStream();
        var second = new MemoryStream();
        using (var a = new BinaryWriter(first))
        using (var b = new BinaryWriter(second))
        {
            for (int v = 0; v < 5; v++)
            {
                foreach (var f in new[] { positions[v].x, positions[v].y, positions[v].z, normals[v].x, normals[v].y, normals[v].z }) a.Write(f);
                for (int i = 0; i < 4; i++) a.Write(tangents[v * 4 + i]);
                for (int i = 0; i < 4; i++) b.Write(colours[v * 4 + i]);
                for (int i = 0; i < 2; i++) b.Write(Mathf.FloatToHalf(uv0[v * 2 + i]));
                for (int i = 0; i < 2; i++) b.Write(uv1[v * 2 + i]);
            }
            a.Flush();
            b.Flush();
            var bytes = first.ToArray();
            mixed.SetVertexBufferData(bytes, 0, 0, bytes.Length, 0);
            bytes = second.ToArray();
            mixed.SetVertexBufferData(bytes, 0, 0, bytes.Length, 1);
        }
        mixed.SetIndexBufferParams(9, IndexFormat.UInt16);
        mixed.SetIndexBufferData(new ushort[] { 0, 1, 2, 2, 1, 3, 3, 1, 4 }, 0, 0, 9);
        mixed.subMeshCount = 1;
        mixed.SetSubMesh(0, new SubMeshDescriptor(0, 9));
        mixed.RecalculateBounds();
        AssetDatabase.CreateAsset(mixed, ObjectFolder + "/MeshMixed.asset");
        // The same mesh saved without read/write access, as a model's mesh is imported by default.
        var locked = UnityEngine.Object.Instantiate(mixed);
        locked.name = "MeshLocked";
        AssetDatabase.CreateAsset(locked, ObjectFolder + "/MeshLocked.asset");
        var lockedState = new SerializedObject(locked);
        lockedState.FindProperty("m_IsReadable").boolValue = false;
        lockedState.ApplyModifiedPropertiesWithoutUndo();

        var wide = new Mesh { name = "MeshWide" };
        wide.SetVertexBufferParams(4,
            new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3, 0),
            new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.Float32, 4, 0),
            new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 4, 0));
        var floats = new[]
        {
            -1.2f, -1.2f, 0f, 0.2f, 0.4f, 0.6f, 1f, 1f, 2f, 3f, 4f,
            1.2f, -1.2f, 0f, 1f, 0.5f, 0.25f, 0.5f, -1f, -2f, -3f, -4f,
            -1.2f, 1.2f, 0f, 0.9f, 0.1f, 0.8f, 1f, 0.5f, 0.25f, 0.125f, 0f,
            1.2f, 1.2f, 0f, 0.04f, 0.5f, 1f, 1f, 7f, 8f, 9f, 10f,
        };
        wide.SetVertexBufferData(floats, 0, 0, floats.Length, 0);
        wide.SetIndexBufferParams(9, IndexFormat.UInt32);
        wide.SetIndexBufferData(new uint[] { 0, 1, 2, 2, 1, 3, 3, 2, 0 }, 0, 0, 9);
        wide.subMeshCount = 2;
        wide.SetSubMesh(0, new SubMeshDescriptor(0, 6));
        wide.SetSubMesh(1, new SubMeshDescriptor(6, 3));
        wide.RecalculateBounds();
        AssetDatabase.CreateAsset(wide, ObjectFolder + "/MeshWide.asset");
        AssetDatabase.SaveAssets();
    }

    /// <summary>The mesh controls' graph (README.md, "Mesh operations"): a spawn context whose
    /// script block reads a mesh's counts, an index of it, a vertex sampled with each addressing
    /// mode and a point on a triangle, and the depth or count of a volume and of arrays of
    /// textures and cubemaps, all over exposed properties.</summary>
    static void MeshOperations()
    {
        var graph = NewGraph("OpsMesh", OperationFolder);
        var (spawn, _, _, _) = System(graph, 8);
        ConstantRate(spawn, 1f);
        var block = Make<VFXSpawnerCustomWrapper>();
        block.SetSettingValue("m_customType", new SerializableType(typeof(MeshInputs)));
        spawn.AddChild(block);
        VFXSlot Exposed(Type type, string name, object value) => Parameter(graph, type, name, value).outputSlots[0];
        void To(VFXSlot from, string input) => Link(from, Input(block, input));
        T Op<T>() where T : VFXOperator
        {
            var op = Make<T>();
            graph.AddChild(op);
            return op;
        }
        // A spaceable output (a position, a direction, a vector) is read through its value.
        VFXSlot Value(VFXSlot slot) => slot.children.Any() ? slot.children.First() : slot;
        var mesh = Exposed(typeof(Mesh), "Mesh", Object<Mesh>("MeshMixed"));
        var vertex = Exposed(typeof(uint), "Vertex", 2u);
        var triangle = Exposed(typeof(uint), "Triangle", 1u);
        var bary = Exposed(typeof(Vector2), "Bary", new Vector2(0.25f, 0.5f));
        var index = Exposed(typeof(uint), "Index", 4u);
        var vertices = Op<MeshVertexCount>();
        Link(mesh, Input(vertices, "mesh"));
        To(vertices.outputSlots[0], "u00");
        var indices = Op<MeshIndexCount>();
        Link(mesh, Input(indices, "mesh"));
        To(indices.outputSlots[0], "u01");
        var triangles = Op<MeshTriangleCount>();
        Link(mesh, Input(triangles, "mesh"));
        To(triangles.outputSlots[0], "u02");
        var sampledIndex = Op<SampleIndex>();
        Link(mesh, Input(sampledIndex, "mesh"));
        Link(index, Input(sampledIndex, "index"));
        To(sampledIndex.outputSlots[0], "u03");
        const SampleMesh.VertexAttributeFlag Channels = SampleMesh.VertexAttributeFlag.Position | SampleMesh.VertexAttributeFlag.Normal
            | SampleMesh.VertexAttributeFlag.Tangent | SampleMesh.VertexAttributeFlag.Color
            | SampleMesh.VertexAttributeFlag.TexCoord0 | SampleMesh.VertexAttributeFlag.TexCoord1;
        SampleMesh Sample(SampleMesh.VertexAttributeFlag output, VFXOperatorUtility.SequentialAddressingMode mode, SampleMesh.PlacementMode placement)
        {
            var sample = Op<SampleMesh>();
            sample.SetSettingValue("output", output);
            sample.SetSettingValue("mode", mode);
            sample.SetSettingValue("placementMode", placement);
            if (placement == SampleMesh.PlacementMode.Surface) sample.SetSettingValue("surfaceCoordinates", SampleMesh.SurfaceCoordinates.Barycentric);
            Link(mesh, Input(sample, "mesh"));
            if (placement == SampleMesh.PlacementMode.Vertex) Link(vertex, Input(sample, "vertex"));
            else
            {
                Link(triangle, Input(sample, "triangle"));
                Link(bary, Input(sample, "barycentric"));
            }
            return sample;
        }
        // Outputs in the order of the channel flags: position, normal, tangent, colour, two
        // texture coordinates.
        var clamped = Sample(Channels, VFXOperatorUtility.SequentialAddressingMode.Clamp, SampleMesh.PlacementMode.Vertex);
        To(Value(clamped.outputSlots[0]), "v00");
        To(Value(clamped.outputSlots[1]), "v01");
        To(Value(clamped.outputSlots[2]), "v02");
        To(clamped.outputSlots[3], "q00");
        To(clamped.outputSlots[4], "q01");
        To(clamped.outputSlots[5], "q02");
        var wrapped = Sample(SampleMesh.VertexAttributeFlag.Position, VFXOperatorUtility.SequentialAddressingMode.Wrap, SampleMesh.PlacementMode.Vertex);
        To(Value(wrapped.outputSlots[0]), "v03");
        var mirrored = Sample(SampleMesh.VertexAttributeFlag.Position, VFXOperatorUtility.SequentialAddressingMode.Mirror, SampleMesh.PlacementMode.Vertex);
        To(Value(mirrored.outputSlots[0]), "v04");
        var surface = Sample(SampleMesh.VertexAttributeFlag.Position | SampleMesh.VertexAttributeFlag.Color, VFXOperatorUtility.SequentialAddressingMode.Clamp, SampleMesh.PlacementMode.Surface);
        To(Value(surface.outputSlots[0]), "v05");
        To(surface.outputSlots[1], "q03");
        foreach (var (type, name, inputs) in new[]
                 {
                     (typeof(Texture3D), "Volume", new[] { "u04", "u05", "u06" }),
                     (typeof(Texture2DArray), "Layers", new[] { null, null, "u07" }),
                     (typeof(CubemapArray), "Cubes", new[] { null, null, "u08" }),
                 })
        {
            var dims = Op<TextureDimensions>();
            dims.SetOperandType(type);
            Link(Exposed(type, name, null), dims.inputSlots[0]);
            for (int i = 0; i < 3; i++)
                if (inputs[i] != null) To(dims.outputSlots[i], inputs[i]);
        }
        graph.GetResource().cullingFlags = VFXCullingFlags.CullNone;
        Save(graph);
    }

    /// <summary>The graphs of the drawing controls of meshes and buffers (README.md, "Mesh
    /// operations"): `DrawMesh` puts a particle on each vertex of the exposed mesh in turn, its
    /// colour sampled from an exposed buffer of vectors; `DrawMeshSurface` puts one on each
    /// triangle, at a point and with the colour sampled there. Opaque quads, drawn as kept.</summary>
    static void DrawnFromMeshes()
    {
        foreach (var surface in new[] { false, true })
        {
            var graph = NewGraph(surface ? "DrawMeshSurface" : "DrawMesh", DrawFolder);
            var (spawn, init, _, output) = System(graph, 16);
            output.SetSettingValue("blendMode", VFXAbstractRenderedOutput.BlendMode.Opaque);
            Burst(spawn, false, 5f, 0f);
            var mesh = Parameter(graph, typeof(Mesh), "Mesh", Object<Mesh>("MeshMixed")).outputSlots[0];
            var id = Make<VFXAttributeParameter>();
            id.SetSettingValue("attribute", "particleId");
            graph.AddChild(id);
            var place = Make<PositionMesh>();
            place.SetSettingValue("spawnMode", PositionBase.SpawnMode.Custom);
            place.SetSettingValue("placementMode", surface ? SampleMesh.PlacementMode.Surface : SampleMesh.PlacementMode.Vertex);
            place.SetSettingValue("applyOrientation", PositionBase.Orientation.None);
            if (surface) place.SetSettingValue("surfaceCoordinates", SampleMesh.SurfaceCoordinates.Barycentric);
            init.AddChild(place);
            Link(mesh, Input(place, "mesh"));
            Link(id.outputSlots[0], Input(place, surface ? "triangle" : "vertex"));
            if (surface) Input(place, "barycentric").value = new Vector2(0.3f, 0.2f);
            Set(init, "size", 0.3f);
            var colour = Make<SetAttribute>();
            colour.SetSettingValue("attribute", "color");
            init.AddChild(colour);
            if (surface)
            {
                var sample = Make<SampleMesh>();
                graph.AddChild(sample);
                sample.SetSettingValue("output", SampleMesh.VertexAttributeFlag.Color);
                sample.SetSettingValue("placementMode", SampleMesh.PlacementMode.Surface);
                sample.SetSettingValue("surfaceCoordinates", SampleMesh.SurfaceCoordinates.Barycentric);
                Link(mesh, Input(sample, "mesh"));
                Link(id.outputSlots[0], Input(sample, "triangle"));
                Input(sample, "barycentric").value = new Vector2(0.3f, 0.2f);
                Link(sample.outputSlots[0], colour.inputSlots[0]);
            }
            else
            {
                var sample = Make<SampleBuffer>();
                graph.AddChild(sample);
                sample.SetOperandType(typeof(Vector4));
                Link(Parameter(graph, typeof(GraphicsBuffer), "Buffer", null).outputSlots[0], Input(sample, "buffer"));
                Link(id.outputSlots[0], Input(sample, "index"));
                Link(sample.outputSlots[0], colour.inputSlots[0]);
            }
            Save(graph);
        }
    }

    /// <summary>The graph of the controls of event attributes read in a spawn context (README.md,
    /// "Event attributes read in a spawn context"): a spawn context whose blocks, in order, are a
    /// script block reading its event attributes through Get Attribute (Current), blocks setting
    /// the lifetime (exposed), the colour (from the lifetime as read), the seed and alive
    /// (exposed), and a second script block reading them again; and a second spawn context,
    /// started by the event `Loop`, whose loop duration is its own lifetime as read there.</summary>
    static void EventReadings()
    {
        var graph = NewGraph("OpsEvent", OperationFolder);
        var (spawn, _, _, _) = System(graph, 8);
        ConstantRate(spawn, 1f);
        VFXSlot Current(string attribute)
        {
            var get = Make<VFXAttributeParameter>();
            get.SetSettingValue("attribute", attribute);
            get.SetSettingValue("location", VFXAttributeLocation.Current);
            graph.AddChild(get);
            return get.outputSlots[0];
        }
        void Reads(Type type)
        {
            var block = Make<VFXSpawnerCustomWrapper>();
            block.SetSettingValue("m_customType", new SerializableType(type));
            spawn.AddChild(block);
            foreach (var (attribute, input) in new[] { ("lifetime", "f00"), ("spawnCount", "f01"), ("color", "v00"), ("seed", "u00"), ("alive", "b00") })
                Link(Current(attribute), Input(block, input));
        }
        void SetFrom(string attribute, VFXSlot from)
        {
            var block = Make<VFXSpawnerSetAttribute>();
            block.SetSettingValue("attribute", attribute);
            spawn.AddChild(block);
            Link(from, block.inputSlots[0]);
        }
        Reads(typeof(EventReadsFirst));
        SetFrom("lifetime", Parameter(graph, typeof(float), "L", 2f).outputSlots[0]);
        SetFrom("color", Current("lifetime"));
        SetFrom("seed", Parameter(graph, typeof(uint), "S", 5u).outputSlots[0]);
        SetFrom("alive", Parameter(graph, typeof(bool), "A", true).outputSlots[0]);
        Reads(typeof(EventReadsSecond));
        // A second spawn context, started by the event `Loop`, whose loop lasts its own
        // lifetime as read there; a block sets the lifetime (the exposed `L2`) and a script
        // block writes the loop down.
        var (loop, _, _, _) = System(graph, 8);
        var start = Make<VFXBasicEvent>();
        start.SetSettingValue("eventName", "Loop");
        graph.AddChild(start);
        start.LinkTo(loop, 0, 0);
        loop.SetSettingValue("loopDuration", VFXBasicSpawner.LoopMode.Constant);
        Link(Current("lifetime"), Input(loop, "LoopDuration"));
        var setLifetime = Make<VFXSpawnerSetAttribute>();
        setLifetime.SetSettingValue("attribute", "lifetime");
        loop.AddChild(setLifetime);
        Link(Parameter(graph, typeof(float), "L2", 0.25f).outputSlots[0], setLifetime.inputSlots[0]);
        var loopReads = Make<VFXSpawnerCustomWrapper>();
        loopReads.SetSettingValue("m_customType", new SerializableType(typeof(LoopReads)));
        loop.AddChild(loopReads);
        graph.GetResource().cullingFlags = VFXCullingFlags.CullNone;
        Save(graph);
    }

    /// <summary>A block that sets an attribute from a curve sampled at a time given.</summary>
    static AttributeFromCurve AtTime(VFXContext context, string attribute, AnimationCurve curve, float time)
    {
        var block = Make<AttributeFromCurve>();
        block.SetSettingValue("attribute", attribute);
        block.SetSettingValue("SampleMode", AttributeFromCurve.CurveSampleMode.Custom);
        context.AddChild(block);
        block.inputSlots[0].value = curve;
        Input(block, "SampleTime").value = time;
        return block;
    }

    /// <summary>Where the probe finds the bundle (under StreamingAssets, so by a path of the
    /// project's in the Editor and in the engine alike).</summary>
    public const string BundleFolder = "Assets/StreamingAssets/VfxProbe", BundleName = "vfxgraphs";

    const string BundleGraphFolder = "Assets/BundleGraphs";

    /// <summary>An AssetBundle holding a graph's asset as the Editor builds it: its compiled
    /// graph, without the graph file. The graph is one of its own whose output is textured
    /// with an image of the probe's (an output left at its default would have the package's
    /// texture built into the bundle), with a rate that is exposed.</summary>
    static void Bundled()
    {
        Directory.CreateDirectory(BundleGraphFolder);
        AssetDatabase.Refresh();
        var graph = NewGraph("Bundled", BundleGraphFolder);
        var (spawn, _, _, output) = System(graph, 16);
        var rate = Make<VFXSpawnerConstantRate>();
        spawn.AddChild(rate);
        Link(Parameter(graph, typeof(float), "Rate", 5f).outputSlots[0], rate.inputSlots[0]);
        Input(output, "mainTexture").value = Object<Texture2D>("ImageA");
        Save(graph);
        AssetDatabase.SaveAssets();
        Directory.CreateDirectory(BundleFolder);
        var build = new AssetBundleBuild { assetBundleName = BundleName, assetNames = new[] { BundleGraphFolder + "/Bundled.vfx" } };
        BuildPipeline.BuildAssetBundles(BundleFolder, new[] { build }, BuildAssetBundleOptions.None, BuildTarget.StandaloneLinux64);
        AssetDatabase.Refresh();
    }

    /// <summary>Two textures and two meshes for object-typed properties: defaults and overrides.</summary>
    static void Objects()
    {
        foreach (var (name, color) in new[] { ("ImageA", Color.red), ("ImageB", Color.green) })
        {
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);
            texture.SetPixels(new[] { color, color, color, color });
            File.WriteAllBytes(ObjectFolder + "/" + name + ".png", texture.EncodeToPNG());
        }
        // An image whose four quarters differ, so that a texture drawn turned or mirrored
        // shows: red and green below, blue and yellow above (quarters of whole 4x4 blocks).
        var quarters = new Texture2D(8, 8, TextureFormat.RGBA32, false);
        for (int y = 0; y < 8; y++)
            for (int x = 0; x < 8; x++)
                quarters.SetPixel(x, y, y < 4 ? (x < 4 ? Color.red : Color.green) : (x < 4 ? Color.blue : Color.yellow));
        File.WriteAllBytes(ObjectFolder + "/ImageC.png", quarters.EncodeToPNG());
        foreach (var name in new[] { "ShapeA", "ShapeB" })
        {
            var mesh = new Mesh
            {
                vertices = new[] { Vector3.zero, Vector3.up, Vector3.right },
                triangles = new[] { 0, 1, 2 },
            };
            AssetDatabase.CreateAsset(mesh, ObjectFolder + "/" + name + ".asset");
        }
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();
    }

    static T Object<T>(string name) where T : UnityEngine.Object =>
        AssetDatabase.LoadAssetAtPath<T>(ObjectFolder + "/" + name + (typeof(T) == typeof(Mesh) ? ".asset" : ".png"));

    /// <summary>A block that initializes an attribute from the spawn event's value of it: the
    /// attribute becomes part of the graph's event attributes.</summary>
    static void Inherit(VFXContext context, string attribute)
    {
        var block = Make<SetAttribute>();
        block.SetSettingValue("attribute", attribute);
        block.SetSettingValue("Source", SetAttribute.ValueSource.Source);
        context.AddChild(block);
    }

    static T Make<T>() where T : VFXModel => ScriptableObject.CreateInstance<T>();

    static VFXGraph NewGraph(string name, string folder = Folder)
    {
        var asset = VisualEffectAssetEditorUtility.CreateNewAsset(folder + "/" + name + ".vfx");
        return asset.GetResource().GetOrCreateGraph();
    }

    static void Save(VFXGraph graph)
    {
        var resource = graph.GetResource();
        resource.WriteAssetWithSubAssets();
        AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(resource), ImportAssetOptions.ForceUpdate);
    }

    /// <summary>A spawner, an initialize, an update and a quad output, linked; returns them.</summary>
    static (VFXBasicSpawner Spawn, VFXBasicInitialize Init, VFXBasicUpdate Update, VFXPlanarPrimitiveOutput Output) System(VFXGraph graph, uint capacity)
    {
        var spawn = Make<VFXBasicSpawner>();
        var init = Make<VFXBasicInitialize>();
        var update = Make<VFXBasicUpdate>();
        var output = Make<VFXPlanarPrimitiveOutput>();
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

    static SetAttribute Set(VFXContext context, string attribute, object value)
    {
        var block = Make<SetAttribute>();
        block.SetSettingValue("attribute", attribute);
        context.AddChild(block);
        block.inputSlots[0].value = value;
        return block;
    }

    static VFXParameter Parameter(VFXGraph graph, Type type, string name, object value = null, bool exposed = true)
    {
        var parameter = VFXLibrary.GetParameters().First(d => d.modelType == type).CreateInstance();
        parameter.SetSettingValue("m_ExposedName", name);
        parameter.SetSettingValue("m_Exposed", exposed);
        if (value != null) parameter.value = value;
        graph.AddChild(parameter);
        return parameter;
    }

    /// <summary>A graph without contexts: it compiles to nothing.</summary>
    static void Empty() => Save(NewGraph("Empty"));

    /// <summary>One system at a constant rate with a lifetime and a velocity.</summary>
    static void Basic()
    {
        var graph = NewGraph("Basic");
        var (spawn, init, _, _) = System(graph, 32);
        var rate = Make<VFXSpawnerConstantRate>();
        spawn.AddChild(rate);
        rate.inputSlots[0].value = 8f;
        Set(init, "lifetime", 2f);
        Set(init, "velocity", (Vector)new Vector3(0f, 1f, 0f));
        Save(graph);
    }

    /// <summary>One system and an exposed property of every type the blackboard offers, some
    /// linked into the system, with names that sort differently by culture and by code point.</summary>
    static void Exposed()
    {
        var graph = NewGraph("Exposed");
        var (spawn, init, _, _) = System(graph, 16);
        var rate = Make<VFXSpawnerConstantRate>();
        spawn.AddChild(rate);
        var rateParameter = Parameter(graph, typeof(float), "Rate", 4f);
        rateParameter.outputSlots[0].Link(rate.inputSlots[0]);
        var lifetime = Set(init, "lifetime", 1f);
        Parameter(graph, typeof(float), "life time", 3f).outputSlots[0].Link(lifetime.inputSlots[0]);

        Parameter(graph, typeof(int), "Count", -7);
        Parameter(graph, typeof(uint), "uCount", 9u);
        Parameter(graph, typeof(bool), "Flag", true);
        Parameter(graph, typeof(Vector2), "Pair", new Vector2(1.5f, -2.5f));
        Parameter(graph, typeof(Vector3), "Triple", new Vector3(1f, 2f, 3f));
        Parameter(graph, typeof(Vector4), "Quad", new Vector4(1f, 2f, 3f, 4f));
        Parameter(graph, typeof(Color), "Tint", new Color(0.25f, 0.5f, 0.75f, 1f));
        Parameter(graph, typeof(Position), "Where", (Position)new Vector3(1f, 2f, 3f));
        var world = Parameter(graph, typeof(Position), "WhereWorld", (Position)new Vector3(4f, 5f, 6f));
        world.outputSlots[0].space = VFXSpace.World;
        Parameter(graph, typeof(DirectionType), "Heading", (DirectionType)new Vector3(0f, 0f, 1f));
        Parameter(graph, typeof(Vector), "Offset", (Vector)new Vector3(7f, 8f, 9f));
        Parameter(graph, typeof(AnimationCurve), "Curve", AnimationCurve.Linear(0f, 0f, 1f, 2f));
        var gradient = new Gradient();
        gradient.SetKeys(new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.blue, 1f) },
            new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0f, 1f) });
        Parameter(graph, typeof(Gradient), "Ramp", gradient);
        Parameter(graph, typeof(Texture2D), "Image", Object<Texture2D>("ImageA"));
        Parameter(graph, typeof(Texture3D), "Volume");
        Parameter(graph, typeof(Cubemap), "Sky");
        Parameter(graph, typeof(Texture2DArray), "Layers");
        Parameter(graph, typeof(CubemapArray), "Skies");
        Parameter(graph, typeof(Mesh), "Shape", Object<Mesh>("ShapeA"));
        Parameter(graph, typeof(SkinnedMeshRenderer), "Skin");
        Parameter(graph, typeof(GraphicsBuffer), "Data");
        Parameter(graph, typeof(TSphere), "Ball");
        Parameter(graph, typeof(AABox), "Box");
        Parameter(graph, typeof(UnityEditor.VFX.Transform), "Frame");
        // Names whose order differs between an ordinal and a culture comparison.
        Parameter(graph, typeof(float), "_under", 1f);
        Parameter(graph, typeof(float), "alpha", 2f);
        Parameter(graph, typeof(float), "Beta", 3f);
        Parameter(graph, typeof(float), "beta2", 4f);
        Parameter(graph, typeof(float), "Zeta", 5f);
        // Not exposed: no property of the asset.
        Parameter(graph, typeof(float), "Hidden", 6f, exposed: false);
        Save(graph);
    }

    /// <summary>A second graph sharing property names with Exposed: "Rate" with the same type,
    /// "Count" with another, and one of its own.</summary>
    static void Exposed2()
    {
        var graph = NewGraph("Exposed2");
        var (spawn, init, _, _) = System(graph, 16);
        var rate = Make<VFXSpawnerConstantRate>();
        spawn.AddChild(rate);
        Parameter(graph, typeof(float), "Rate", 6f).outputSlots[0].Link(rate.inputSlots[0]);
        Set(init, "lifetime", 1f);
        Parameter(graph, typeof(float), "Count", 1.5f);
        Parameter(graph, typeof(float), "Extra", 2.5f);
        Save(graph);
    }

    /// <summary>Custom events starting and stopping a spawner, and one starting a second system.</summary>
    static void Events()
    {
        var graph = NewGraph("Events");
        var (spawn, init, _, _) = System(graph, 8);
        var burst = Make<VFXSpawnerBurst>();
        spawn.AddChild(burst);
        burst.inputSlots[0].value = 4f;
        Set(init, "lifetime", 1f);
        var fire = Make<VFXBasicEvent>();
        fire.SetSettingValue("eventName", "Fire");
        graph.AddChild(fire);
        fire.LinkTo(spawn, 0, 0);
        var cease = Make<VFXBasicEvent>();
        cease.SetSettingValue("eventName", "Cease");
        graph.AddChild(cease);
        cease.LinkTo(spawn, 0, 1);

        var (spawn2, init2, _, _) = System(graph, 4);
        var rate = Make<VFXSpawnerConstantRate>();
        spawn2.AddChild(rate);
        rate.inputSlots[0].value = 2f;
        Set(init2, "lifetime", 1f);
        Set(init2, "color", new Vector3(1f, 0f, 0f));
        Save(graph);
    }

    /// <summary>A system whose particles take several attributes of different types from the
    /// spawn event: floats, vectors, a bool and an unsigned integer.</summary>
    static void Attributes()
    {
        var graph = NewGraph("Attributes");
        var (spawn, init, _, _) = System(graph, 8);
        var rate = Make<VFXSpawnerConstantRate>();
        spawn.AddChild(rate);
        rate.inputSlots[0].value = 2f;
        foreach (var attribute in new[] { "lifetime", "velocity", "color", "alive", "seed" }) Inherit(init, attribute);
        Save(graph);
    }

    /// <summary>Some of the same event attributes as Attributes in another order, and one of its own.</summary>
    static void Attributes2()
    {
        var graph = NewGraph("Attributes2");
        var (spawn, init, _, _) = System(graph, 8);
        var rate = Make<VFXSpawnerConstantRate>();
        spawn.AddChild(rate);
        rate.inputSlots[0].value = 2f;
        foreach (var attribute in new[] { "color", "size", "lifetime" }) Inherit(init, attribute);
        Save(graph);
    }

    static VFXSlot Input(VFXModel model, string name) => ((IVFXSlotContainer)model).inputSlots.First(s => s.name == name);

    /// <summary>A system whose particles never die (no lifetime): its alive count is the
    /// particles spawned so far, up to its capacity.</summary>
    static (VFXGraph Graph, VFXBasicSpawner Spawn, VFXBasicInitialize Init) Counting(string name, uint capacity)
    {
        var graph = NewGraph(name, SimulationFolder);
        var (spawn, init, _, _) = System(graph, capacity);
        return (graph, spawn, init);
    }

    static void ConstantRate(VFXBasicSpawner spawn, float rate)
    {
        var block = Make<VFXSpawnerConstantRate>();
        spawn.AddChild(block);
        block.inputSlots[0].value = rate;
    }

    static void Burst(VFXBasicSpawner spawn, bool periodic, object count, object delay)
    {
        var block = Make<VFXSpawnerBurst>();
        spawn.AddChild(block);
        block.SetSettingValue("repeat", periodic ? VFXSpawnerBurst.RepeatMode.Periodic : VFXSpawnerBurst.RepeatMode.Single);
        block.SetSettingValue("spawnMode", count is Vector2 ? VFXSpawnerBurst.RandomMode.Random : VFXSpawnerBurst.RandomMode.Constant);
        block.SetSettingValue("delayMode", delay is Vector2 ? VFXSpawnerBurst.RandomMode.Random : VFXSpawnerBurst.RandomMode.Constant);
        Input(block, "Count").value = count;
        Input(block, "Delay").value = delay;
    }

    /// <summary>The graphs the simulation controls run (README.md, "Simulation").</summary>
    static void Simulated()
    {
        // A constant rate that is no whole number per step, into a capacity it fills; the same
        // with each update mode, with the simulation not culled, and with a prewarm.
        foreach (var (name, mode, culling, prewarm) in new[]
        {
            ("SimRate", VFXUpdateMode.FixedDeltaTime, VFXCullingFlags.CullDefault, 0u),
            ("SimRateDelta", VFXUpdateMode.DeltaTime, VFXCullingFlags.CullDefault, 0u),
            ("SimRateExact", VFXUpdateMode.ExactFixedTimeStep, VFXCullingFlags.CullDefault, 0u),
            ("SimRateNoCull", VFXUpdateMode.FixedDeltaTime, VFXCullingFlags.CullNone, 0u),
            ("SimRateBoundsOnly", VFXUpdateMode.FixedDeltaTime, VFXCullingFlags.CullBoundsUpdate, 0u),
            ("SimRatePrewarm", VFXUpdateMode.FixedDeltaTime, VFXCullingFlags.CullDefault, 5u),
        })
        {
            var (graph, spawn, _) = Counting(name, 20);
            ConstantRate(spawn, 7.3f);
            var resource = graph.GetResource();
            resource.updateMode = mode;
            resource.cullingFlags = culling;
            resource.preWarmStepCount = prewarm;
            resource.preWarmDeltaTime = 0.1f;
            Save(graph);
        }

        // One burst after a delay and a periodic one, of fixed counts.
        {
            var (graph, spawn, _) = Counting("SimBurst", 64);
            Burst(spawn, false, 3f, 0.1f);
            Burst(spawn, true, 2f, 0.25f);
            Save(graph);
        }

        // Counts, delays, periods and rates drawn from ranges: the effect's random sequence.
        {
            var (graph, spawn, _) = Counting("SimRandom", 256);
            Burst(spawn, false, new Vector2(2f, 6f), new Vector2(0.05f, 0.3f));
            Burst(spawn, true, new Vector2(1f, 3f), new Vector2(0.1f, 0.2f));
            var variable = Make<VFXSpawnerVariableRate>();
            spawn.AddChild(variable);
            Input(variable, "Rate").value = new Vector2(5f, 15f);
            Input(variable, "Period").value = new Vector2(0.2f, 0.4f);
            Save(graph);
        }

        // A loop of a set duration run twice, with a delay before and after it.
        {
            var (graph, spawn, _) = Counting("SimLoop", 128);
            spawn.SetSettingValue("loopDuration", VFXBasicSpawner.LoopMode.Constant);
            spawn.SetSettingValue("loopCount", VFXBasicSpawner.LoopMode.Constant);
            spawn.SetSettingValue("delayBeforeLoop", VFXBasicSpawner.DelayMode.Constant);
            spawn.SetSettingValue("delayAfterLoop", VFXBasicSpawner.DelayMode.Constant);
            Input(spawn, "LoopDuration").value = 0.2f;
            Input(spawn, "LoopCount").value = 2;
            Input(spawn, "DelayBeforeLoop").value = 0.1f;
            Input(spawn, "DelayAfterLoop").value = 0.15f;
            ConstantRate(spawn, 30f);
            Burst(spawn, false, 5f, 0.05f);
            Save(graph);
        }

        // Loops of drawn durations and delays, without end.
        {
            var (graph, spawn, _) = Counting("SimLoopRandom", 256);
            spawn.SetSettingValue("loopDuration", VFXBasicSpawner.LoopMode.Random);
            spawn.SetSettingValue("delayBeforeLoop", VFXBasicSpawner.DelayMode.Random);
            spawn.SetSettingValue("delayAfterLoop", VFXBasicSpawner.DelayMode.Random);
            Input(spawn, "LoopDuration").value = new Vector2(0.1f, 0.3f);
            Input(spawn, "DelayBeforeLoop").value = new Vector2(0.05f, 0.1f);
            Input(spawn, "DelayAfterLoop").value = new Vector2(0.05f, 0.15f);
            ConstantRate(spawn, 20f);
            Save(graph);
        }

        // Particles that die: ten at once, living a short time or a long one.
        foreach (var (name, lifetime) in new[] { ("SimKill", 0.6f), ("SimKeep", 100f) })
        {
            var (graph, spawn, init) = Counting(name, 16);
            Burst(spawn, false, 10f, 0f);
            Set(init, "lifetime", lifetime);
            Save(graph);
        }
    }

    static void Link(VFXSlot from, VFXSlot to)
    {
        if (!from.Link(to)) throw new InvalidOperationException("cannot link " + from.property.type.Name + " '" + from.name + "' to " + to.property.type.Name + " '" + to.name + "'");
    }

    /// <summary>A graph whose spawn context has the probe's block, with operators over exposed
    /// properties (so nothing folds into a constant) connected to the block's inputs: what the
    /// block reads is what the effect's expressions evaluate to. Also a block that sets event
    /// attributes, and a particle lifetime taken from a texture.</summary>
    static void Expressions()
    {
        var (graph, spawn, init) = Counting("SimExpressions", 8);
        ConstantRate(spawn, 30f);
        var block = Make<VFXSpawnerCustomWrapper>();
        block.SetSettingValue("m_customType", new SerializableType(typeof(ProbeInputs)));
        spawn.AddChild(block);
        int[] next = new int[8];
        // An input a value cannot be connected to (no conversion between the types) keeps its default.
        void To(char kind, int index, VFXSlot value) => value.Link(Input(block, kind + (next[index]++).ToString("00")));
        void Floats(params VFXSlot[] values) { foreach (var value in values) To('f', 0, value); }
        void Ints(params VFXSlot[] values) { foreach (var value in values) To('i', 1, value); }
        void Uints(params VFXSlot[] values) { foreach (var value in values) To('u', 2, value); }
        void Bools(params VFXSlot[] values) { foreach (var value in values) To('b', 3, value); }
        void Vectors(params VFXSlot[] values) { foreach (var value in values) To('v', 4, value); }
        void Quads(params VFXSlot[] values) { foreach (var value in values) To('q', 5, value); }
        void GameTimes(params VFXSlot[] values) { foreach (var value in values) To('g', 6, value); }
        void Matrices(params VFXSlot[] values) { foreach (var value in values) To('m', 7, value); }

        VFXSlot Exposed(Type type, string name, object value) => Parameter(graph, type, name, value).outputSlots[0];
        VFXSlot A = Exposed(typeof(float), "A", 2.5f), B = Exposed(typeof(float), "B", -2.5f), C = Exposed(typeof(float), "C", 0.75f), Z = Exposed(typeof(float), "Z", 0f);
        VFXSlot I = Exposed(typeof(int), "I", -9), J = Exposed(typeof(int), "J", 0), K = Exposed(typeof(int), "K", 4);
        VFXSlot U = Exposed(typeof(uint), "U", 4294967280u), V = Exposed(typeof(uint), "V", 33u), W = Exposed(typeof(uint), "W", 0u);
        VFXSlot P = Exposed(typeof(bool), "P", true), Q = Exposed(typeof(bool), "Q", false);
        VFXSlot T = Exposed(typeof(Vector3), "T", new Vector3(1f, 2f, 3f)), S = Exposed(typeof(Vector3), "S", new Vector3(0.5f, -4f, 8f));
        VFXSlot R = Exposed(typeof(Vector4), "R", new Vector4(1f, 2f, 3f, 1f));
        var frame = Exposed(typeof(UnityEditor.VFX.Transform), "Frame", new UnityEditor.VFX.Transform { position = new Vector3(10f, 20f, 30f), angles = new Vector3(30f, 45f, 60f), scale = new Vector3(2f, 3f, 4f) });

        VFXSlot Uniform<O>(Type type, params VFXSlot[] inputs) where O : VFXOperatorNumericUniform
        {
            var op = Make<O>();
            graph.AddChild(op);
            op.SetOperandType(type);
            for (int k = 0; k < inputs.Length; k++) Link(inputs[k], op.inputSlots[k]);
            return op.outputSlots[0];
        }
        VFXSlot Cascaded<O>(Type type, VFXSlot a, VFXSlot b) where O : VFXOperatorNumericCascadedUnified
        {
            var op = Make<O>();
            graph.AddChild(op);
            op.SetOperandType(0, type);
            op.SetOperandType(1, type);
            Link(a, op.inputSlots[0]);
            Link(b, op.inputSlots[1]);
            return op.outputSlots[0];
        }
        VFXSlot Plain<O>(params VFXSlot[] inputs) where O : VFXOperator
        {
            var op = Make<O>();
            graph.AddChild(op);
            for (int k = 0; k < inputs.Length; k++) if (inputs[k] != null) Link(inputs[k], op.inputSlots[k]);
            return op.outputSlots[0];
        }
        VFXSlot Compare(Type type, VFXCondition condition, VFXSlot a, VFXSlot b)
        {
            var op = Make<Condition>();
            graph.AddChild(op);
            op.SetOperandType(type);
            op.SetSettingValue("condition", condition);
            Link(a, op.inputSlots[0]);
            Link(b, op.inputSlots[1]);
            return op.outputSlots[0];
        }
        VFXSlot Choose(Type type, VFXSlot predicate, VFXSlot a, VFXSlot b)
        {
            var op = Make<Branch>();
            graph.AddChild(op);
            op.SetOperandType(type);
            Link(predicate, op.inputSlots[0]);
            Link(a, op.inputSlots[1]);
            Link(b, op.inputSlots[2]);
            return op.outputSlots[0];
        }

        Type f = typeof(float), i = typeof(int), u = typeof(uint), v3 = typeof(Vector3);
        // Functions of one float, and of two.
        Floats(Uniform<Sine>(f, A), Uniform<Cosine>(f, A), Uniform<Tangent>(f, C), Uniform<Asin>(f, C), Uniform<Acos>(f, C), Uniform<Atan>(f, B),
            Uniform<Absolute>(f, B), Uniform<Sign>(f, B), Uniform<Sign>(f, Z), Uniform<Saturate>(f, A), Uniform<Saturate>(f, B), Uniform<Ceiling>(f, B),
            Uniform<Round>(f, A), Uniform<Round>(f, B), Uniform<Fractional>(f, B), Uniform<Floor>(f, B),
            Cascaded<Add>(f, A, C), Cascaded<Subtract>(f, A, C), Cascaded<Multiply>(f, A, B), Cascaded<Divide>(f, A, C), Cascaded<Divide>(f, A, Z),
            Cascaded<Minimum>(f, C, B), Cascaded<Maximum>(f, C, B), Cascaded<Power>(f, A, C), Uniform<Atan2>(f, A, B), Uniform<Modulo>(f, A, C), Uniform<Log>(f, A));
        // Values of other types converted where they meet a float input, and a branch each way.
        Floats(I, U, P, Choose(f, P, A, C), Choose(f, Q, A, C));
        // A number drawn once for the effect's seed, and one drawn at each evaluation.
        foreach (bool constant in new[] { true, false })
        {
            var random = Make<UnityEditor.VFX.Operator.Random>();
            graph.AddChild(random);
            random.SetSettingValue("seed", VFXSeedMode.PerVFXComponent);
            random.SetSettingValue("constant", constant);
            Link(C, random.inputSlots[0]);
            Link(A, random.inputSlots[1]);
            Floats(random.outputSlots[0]);
        }
        // Whole numbers.
        Ints(Cascaded<Add>(i, I, K), Cascaded<Subtract>(i, I, K), Cascaded<Multiply>(i, I, K), Cascaded<Divide>(i, I, K), Cascaded<Divide>(i, I, J),
            Cascaded<Minimum>(i, I, K), Cascaded<Maximum>(i, I, K), Uniform<Absolute>(i, I), Uniform<Sign>(i, I), B, U);
        Uints(Cascaded<Add>(u, U, V), Cascaded<Subtract>(u, W, V), Cascaded<Multiply>(u, U, V), Cascaded<Divide>(u, U, V), Cascaded<Divide>(u, U, W),
            Cascaded<Minimum>(u, U, V), Cascaded<Maximum>(u, U, V),
            Plain<BitwiseAnd>(U, V), Plain<BitwiseOr>(U, V), Plain<BitwiseXor>(U, V), Plain<BitwiseLeftShift>(U, V), Plain<BitwiseRightShift>(U, V), Plain<BitwiseComplement>(V),
            B, I, A);
        // Comparisons of each kind and type, and logic.
        Bools(Compare(f, VFXCondition.Equal, A, A), Compare(f, VFXCondition.NotEqual, A, C), Compare(f, VFXCondition.Less, A, C), Compare(f, VFXCondition.LessOrEqual, A, A),
            Compare(f, VFXCondition.Greater, A, C), Compare(f, VFXCondition.GreaterOrEqual, C, A), Compare(i, VFXCondition.Less, I, K), Compare(u, VFXCondition.Greater, U, V),
            Compare(f, VFXCondition.Less, Z, A), Compare(f, VFXCondition.Equal, Z, Z),
            Plain<LogicalAnd>(P, Q), Plain<LogicalOr>(P, Q), Plain<LogicalNot>(P), Plain<LogicalNand>(P, Q), Plain<LogicalNor>(P, Q), I);
        // Vectors, component by component, and through the frame's matrix and its inverse.
        var inverse = Plain<InverseTRSMatrix>(frame);
        Vectors(Cascaded<Add>(v3, T, S), Cascaded<Multiply>(v3, T, S), Uniform<Absolute>(v3, S),
            Plain<TransformPosition>(frame, T), Plain<TransformVector>(frame, T), Plain<TransformDirection>(frame, T));
        VFXSlot transposed = Plain<TransposeMatrix>(inverse), product = Plain<TransformMatrix>(frame, inverse);
        Quads(Plain<TransformVector4>(frame, R), Plain<TransformVector4>(inverse, R), Plain<TransformVector4>(transposed, R), Plain<TransformVector4>(product, R));
        Matrices(frame, inverse, transposed, product);
        // The effect's and the game's time, and the spawn context's own state.
        var time = Make<VFXDynamicBuiltInParameter>();
        graph.AddChild(time);
        time.SetSettingValue("m_BuiltInParameters", VFXDynamicBuiltInParameter.BuiltInFlag.VfxDeltaTime | VFXDynamicBuiltInParameter.BuiltInFlag.VfxUnscaledDeltaTime
            | VFXDynamicBuiltInParameter.BuiltInFlag.VfxTotalTime | VFXDynamicBuiltInParameter.BuiltInFlag.VfxPlayRate | VFXDynamicBuiltInParameter.BuiltInFlag.VfxManagerFixedTimeStep
            | VFXDynamicBuiltInParameter.BuiltInFlag.VfxManagerMaxDeltaTime);
        foreach (var output in time.outputSlots) Floats(output);
        var game = Make<VFXDynamicBuiltInParameter>();
        graph.AddChild(game);
        game.SetSettingValue("m_BuiltInParameters", VFXDynamicBuiltInParameter.BuiltInFlag.GameDeltaTime | VFXDynamicBuiltInParameter.BuiltInFlag.GameUnscaledDeltaTime
            | VFXDynamicBuiltInParameter.BuiltInFlag.GameSmoothDeltaTime | VFXDynamicBuiltInParameter.BuiltInFlag.GameTotalTime | VFXDynamicBuiltInParameter.BuiltInFlag.GameUnscaledTotalTime
            | VFXDynamicBuiltInParameter.BuiltInFlag.GameTotalTimeSinceSceneLoad | VFXDynamicBuiltInParameter.BuiltInFlag.GameTimeScale);
        foreach (var output in game.outputSlots) GameTimes(output);
        var seed = Make<VFXDynamicBuiltInParameter>();
        graph.AddChild(seed);
        seed.SetSettingValue("m_BuiltInParameters", VFXDynamicBuiltInParameter.BuiltInFlag.SystemSeed);
        Uints(seed.outputSlots[0]);
        var state = Make<SpawnState>();
        graph.AddChild(state);
        Bools(state.outputSlots.First(s => s.name == "NewLoop"));
        Uints(state.outputSlots.First(s => s.name == "LoopState"));
        // The manager's update count, in the input u19 (the probe records how it grows).
        var frameIndex = Make<VFXDynamicBuiltInParameter>();
        graph.AddChild(frameIndex);
        frameIndex.SetSettingValue("m_BuiltInParameters", VFXDynamicBuiltInParameter.BuiltInFlag.VfxFrameIndex);
        Link(frameIndex.outputSlots[0], Input(block, "u19"));
        Ints(state.outputSlots.First(s => s.name == "LoopIndex"), state.outputSlots.First(s => s.name == "LoopCount"));
        // The conversions not met above: to a bool from an unsigned number and from a float,
        // from a bool to whole numbers.
        Bools(U, A);
        Ints(P);
        Link(P, Input(block, "u20"));
        // A curve and a gradient sampled where the effect's expressions are evaluated (in a
        // spawn context: on the CPU), at a time the probe changes.
        var curve = Exposed(typeof(AnimationCurve), "Curve", new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 2f)));
        var ramp = Exposed(typeof(Gradient), "Ramp", Ramp(GradientMode.Blend, 0.5f, (0f, Color.red), (1f, Color.blue)));
        var sampledCurve = Make<SampleCurve>();
        graph.AddChild(sampledCurve);
        VFXSlot when = Exposed(typeof(float), "When", 0.75f);
        Link(curve, sampledCurve.inputSlots[0]);
        Link(when, sampledCurve.inputSlots[1]);
        Link(sampledCurve.outputSlots[0], Input(block, "c00"));
        var sampledRamp = Make<SampleGradient>();
        graph.AddChild(sampledRamp);
        Link(ramp, sampledRamp.inputSlots[0]);
        Link(when, sampledRamp.inputSlots[1]);
        Link(sampledRamp.outputSlots[0], Input(block, "r00"));
        // The same to a bool from numbers that are zero (W, and Z until the probe sets it).
        Bools(W);
        Link(Z, Input(block, "b20"));
        Floats(state.outputSlots.First(s => s.name == "SpawnCount"), state.outputSlots.First(s => s.name == "SpawnDeltaTime"), state.outputSlots.First(s => s.name == "SpawnTotalTime"),
            state.outputSlots.First(s => s.name == "LoopDuration"), state.outputSlots.First(s => s.name == "DelayBeforeLoop"), state.outputSlots.First(s => s.name == "DelayAfterLoop"));

        // A block that sets two event attributes; the particles take them from the event.
        foreach (var (attribute, value) in new[] { ("lifetime", Cascaded<Add>(f, A, C)), ("color", T) })
        {
            var set = Make<VFXSpawnerSetAttribute>();
            set.SetSettingValue("attribute", attribute);
            spawn.AddChild(set);
            Link(value, set.inputSlots[0]);
        }
        Inherit(init, "color");
        Save(graph);

        // Ten particles whose lifetime is a hundred times the red of a texture: with the
        // default image (red) they live, with a green one they die at once.
        var (textured, texturedSpawn, texturedInit) = Counting("SimTexture", 16);
        Burst(texturedSpawn, false, 10f, 0f);
        var image = Parameter(textured, typeof(Texture2D), "Image", Object<Texture2D>("ImageA"));
        var sample = Make<SampleTexture2D>();
        textured.AddChild(sample);
        Link(image.outputSlots[0], sample.inputSlots[0]);
        var hundredfold = Make<Multiply>();
        textured.AddChild(hundredfold);
        Link(sample.outputSlots[0][0], hundredfold.inputSlots[0]);
        hundredfold.inputSlots[1].value = 100f;
        var lifetime = Set(texturedInit, "lifetime", 1f);
        Link(hundredfold.outputSlots[0], lifetime.inputSlots[0]);
        Save(textured);
    }

    const string DrawFolder = "Assets/DrawGraphs";

    /// <summary>A vector as the slot takes it: a position, a direction and a vector have types
    /// of their own (a plain vector given to one of them is not taken).</summary>
    static object Spaced(VFXSlot slot, Vector3 value)
    {
        var type = slot.property.type;
        if (type == typeof(Position)) return (Position)value;
        if (type == typeof(Vector)) return (Vector)value;
        if (type == typeof(DirectionType)) return (DirectionType)value;
        return value;
    }

    /// <summary>A block that sets an attribute to a vector.</summary>
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

    /// <summary>The graphs the drawing controls show (README.md, "Drawing"): quads drawn
    /// without sorting, so in one draw of the particles in the order they are kept.</summary>
    static void Drawn()
    {
        (VFXGraph, VFXBasicSpawner, VFXBasicInitialize, VFXPlanarPrimitiveOutput) Quads(string name, uint capacity, VFXAbstractRenderedOutput.BlendMode blend)
        {
            var graph = NewGraph(name, DrawFolder);
            var (spawn, init, _, output) = System(graph, capacity);
            output.SetSettingValue("blendMode", blend);
            return (graph, spawn, init, output);
        }

        // Forty opaque quads at once, each with a place, a depth and a colour of its own.
        var (opaque, opaqueSpawn, opaqueInit, _) = Quads("DrawOpaque", 64, VFXAbstractRenderedOutput.BlendMode.Opaque);
        Burst(opaqueSpawn, false, 40f, 0f);
        SetRandom(opaqueInit, "position", new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f));
        Set(opaqueInit, "size", 0.4f);
        SetRandom(opaqueInit, "color", new Vector3(0.2f, 0.2f, 0.2f), new Vector3(1f, 1f, 1f));
        Save(opaque);

        // Thirty a second rising from a line and living half a second, added to what is behind.
        var (additive, additiveSpawn, additiveInit, _) = Quads("DrawAdditive", 64, VFXAbstractRenderedOutput.BlendMode.Additive);
        ConstantRate(additiveSpawn, 30f);
        SetRandom(additiveInit, "position", new Vector3(-1f, -1f, 0f), new Vector3(1f, -1f, 0f));
        SetVector(additiveInit, "velocity", new Vector3(0f, 2f, 0f));
        Set(additiveInit, "lifetime", 0.5f);
        Set(additiveInit, "size", 0.3f);
        SetVector(additiveInit, "color", new Vector3(1f, 0.5f, 0.2f));
        Save(additive);

        // More quads than one draw's indices reach.
        var (many, manySpawn, manyInit, _) = Quads("DrawMany", 20000, VFXAbstractRenderedOutput.BlendMode.Additive);
        Burst(manySpawn, false, 20000f, 0f);
        SetRandom(manyInit, "position", new Vector3(-1.4f, -1.4f, 0f), new Vector3(1.4f, 1.4f, 0f));
        Set(manyInit, "size", 0.05f);
        SetVector(manyInit, "color", new Vector3(0.3f, 0.6f, 1f));
        Save(many);

        // Nine opaque quads with an image of the project's for texture (its quarters differ).
        var (textured, texturedSpawn, texturedInit, texturedOutput) = Quads("DrawTextured", 16, VFXAbstractRenderedOutput.BlendMode.Opaque);
        Burst(texturedSpawn, false, 9f, 0f);
        SetRandom(texturedInit, "position", new Vector3(-1f, -1f, -1f), new Vector3(1f, 1f, 1f));
        Set(texturedInit, "size", 0.6f);
        Input(texturedOutput, "mainTexture").value = Object<Texture2D>("ImageC");
        Save(textured);
    }

    /// <summary>Spawn contexts wired to each other: the first's burst starts the second, a
    /// third's burst stops it.</summary>
    static void Chained()
    {
        var graph = NewGraph("SimChain", SimulationFolder);
        var (second, init, _, _) = System(graph, 64);
        ConstantRate(second, 30f);
        var first = Make<VFXBasicSpawner>();
        graph.AddChild(first);
        Burst(first, false, 2f, 0.1f);
        first.LinkTo(second, 0, 0);
        var third = Make<VFXBasicSpawner>();
        graph.AddChild(third);
        Burst(third, false, 1f, 0.3f);
        third.LinkTo(second, 0, 1);
        Save(graph);
    }

    /// <summary>The probe scene: effects saved with their serialized state, one with overrides.</summary>
    static void BuildScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var camera = new GameObject("Main Camera") { tag = "MainCamera" };
        camera.AddComponent<Camera>();
        camera.transform.position = new Vector3(0f, 0f, -10f);

        var plain = new GameObject("Plain").AddComponent<VisualEffect>();
        plain.visualEffectAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(Folder + "/Basic.vfx");

        var overridden = new GameObject("Overridden").AddComponent<VisualEffect>();
        overridden.visualEffectAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(Folder + "/Exposed.vfx");
        overridden.SetFloat("Rate", 12f);
        overridden.SetInt("Count", 5);
        overridden.SetUInt("uCount", 77u);
        overridden.SetBool("Flag", false);
        overridden.SetVector2("Pair", new Vector2(9f, 8f));
        overridden.SetVector3("Triple", new Vector3(7f, 6f, 5f));
        overridden.SetVector4("Quad", new Vector4(4f, 3f, 2f, 1f));
        overridden.SetVector4("Tint", new Vector4(1f, 0f, 0f, 0.5f));
        overridden.SetVector3("WhereWorld", new Vector3(-1f, -2f, -3f));
        overridden.SetAnimationCurve("Curve", AnimationCurve.Constant(0f, 1f, 5f));
        var ramp = new Gradient();
        ramp.SetKeys(new[] { new GradientColorKey(Color.yellow, 0.25f), new GradientColorKey(Color.cyan, 0.75f) },
            new[] { new GradientAlphaKey(0.25f, 0.125f), new GradientAlphaKey(0.75f, 0.5f) });
        overridden.SetGradient("Ramp", ramp);
        overridden.SetTexture("Image", Object<Texture2D>("ImageB"));
        overridden.SetMesh("Shape", Object<Mesh>("ShapeB"));
        overridden.startSeed = 1234;
        overridden.resetSeedOnPlay = false;
        overridden.initialEventName = "Custom";
        overridden.transform.position = new Vector3(3f, 0f, 0f);

        var events = new GameObject("Events").AddComponent<VisualEffect>();
        events.visualEffectAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(Folder + "/Events.vfx");
        events.transform.position = new Vector3(-3f, 0f, 0f);

        var expressions = new GameObject("Expressions").AddComponent<VisualEffect>();
        expressions.visualEffectAsset = AssetDatabase.LoadAssetAtPath<VisualEffectAsset>(SimulationFolder + "/SimExpressions.vfx");
        expressions.startSeed = 7;
        expressions.resetSeedOnPlay = false;
        expressions.SetFloat("C", 0.5f);

        var none = new GameObject("NoAsset").AddComponent<VisualEffect>();
        none.transform.position = new Vector3(0f, 3f, 0f);

        EditorSceneManager.SaveScene(scene, Scene);
    }
}
