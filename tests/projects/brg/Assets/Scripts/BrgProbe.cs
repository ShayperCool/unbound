using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Collections;
using Unity.Collections.LowLevel.Unsafe;
using Unity.Jobs;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>
/// The BatchRendererGroup probe: in the shader graph probe's URP scene (its Lit sphere stays as an
/// ordinary renderer, its graph sphere is hidden) a BatchRendererGroup draws a row of URP Lit
/// cubes, each with its own matrix and colour, two URP Unlit spheres from the same batch and two
/// mirrored cubes (a negative scale), one drawn with FlipWinding and one without, as
/// the Manual's "Creating a renderer with BatchRendererGroup" builds it: a zeroed block at
/// address 0, packed float3x4 matrices and per-instance colours flagged 0x80000000. The buffer is
/// the device's <see cref="BatchRendererGroup.BufferTarget"/>: a raw buffer, or a constant
/// buffer given to AddBatch with its window. The main camera renders into a 320x180 texture;
/// the image goes to <c>$BRG_PROBE_OUT/&lt;scenario&gt;.png</c> and every OnPerformCulling call's
/// context, with the API's answers, to <c>$BRG_PROBE_OUT/values.jsonl</c>, then the Editor quits.
/// </summary>
public unsafe class BrgProbe : MonoBehaviour
{
    const int Width = 320, Height = 180;
    const int SizeOfPackedMatrix = 48, SizeOfFloat4 = 16;
    const uint PerInstance = 0x80000000;
    const int Cubes = 8, Spheres = 2, Mirrored = 2, Masked = 2, Instances = Cubes + Spheres + Mirrored + Masked, Commands = 4;
    /// <summary>The layer of the pillar the shadows and cameras scenarios draw in a second range:
    /// the sun's and the second camera's culling masks leave it out.</summary>
    const int MaskedLayer = 3;
    /// <summary>The layer of a second pillar, in a third range, that the main camera's culling
    /// mask leaves out in the shadows scenario.</summary>
    const int CameraMaskedLayer = 4;

    static readonly List<string> s_Rows = new List<string>();
    static string s_Out;

    BatchRendererGroup m_Brg;
    GraphicsBuffer m_Buffer;
    /// <summary>The instance data as uploaded, its colours' byte address and its size.</summary>
    float[] m_Data;
    int m_ColorsAt, m_Bytes, m_ObjectToWorldAt, m_WorldToObjectAt;
    Mesh m_CubeMesh;
    /// <summary>Run between a scenario's two renders (the lifetime scenario's buffer update).</summary>
    Action m_BetweenRenders;
    /// <summary>The errors scenario's extra commands: an unknown mesh, a material without DOTS instancing.</summary>
    BatchMaterialID m_NoDots;
    readonly List<string> m_Log = new List<string>();
    BatchID m_Batch;
    BatchMeshID m_Cube, m_Sphere;
    BatchMaterialID m_Lit, m_Unlit;
    int m_Calls;
    string m_Scenario = "";
    int m_Render;
    Camera m_Camera;
    /// <summary>The camera the scenario renders (the main one, else the cameras scenario's).</summary>
    Camera m_ViewCamera;
    Light m_Light;
    /// <summary>The shadows scenario: the range casts and receives shadows.</summary>
    bool m_Shadows;
    /// <summary>A second group that takes light views alone and draws nothing.</summary>
    BatchRendererGroup m_LightOnly;
    /// <summary>The forward_plus_plain scenario: the group draws nothing.</summary>
    bool m_NoCommands;
    readonly SortedDictionary<string, int> m_Views = new SortedDictionary<string, int>(StringComparer.Ordinal);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("BrgProbe").AddComponent<BrgProbe>();

    // Synchronous: the Editor in batch mode never reaches the end of a frame.
    void Start()
    {
        s_Out = Environment.GetEnvironmentVariable("BRG_PROBE_OUT");
        if (!string.IsNullOrEmpty(s_Out)) Directory.CreateDirectory(s_Out);
        var graph = GameObject.Find("Graph Sphere");
        if (graph != null) graph.SetActive(false);
        m_Camera = Camera.main;
        try
        {
            Setup();
            Scenario("basic");
            Shadows();
            Cameras();
            Lifetime();
            Errors();
            ForwardPlus();
        }
        catch (Exception e)
        {
            Row("exception", e.GetType().Name + ": " + e.Message);
        }
        Teardown();
        Row("complete", true);
        if (!string.IsNullOrEmpty(s_Out)) File.WriteAllText(Path.Combine(s_Out, "values.jsonl"), string.Join("\n", s_Rows) + "\n");
        Quit();
    }

    static void Quit()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    static Mesh PrimitiveMesh(PrimitiveType type)
    {
        var go = GameObject.CreatePrimitive(type);
        var mesh = go.GetComponent<MeshFilter>().sharedMesh;
        DestroyImmediate(go);
        return mesh;
    }

    void Setup()
    {
        var target = BatchRendererGroup.BufferTarget;
        Row("device", SystemInfo.graphicsDeviceType.ToString());
        Row("bufferTarget", target.ToString());
        Row("maxWindow", BatchRendererGroup.GetConstantBufferMaxWindowSize());
        m_Brg = new BatchRendererGroup(OnPerformCulling, IntPtr.Zero);
        var cube = m_CubeMesh = PrimitiveMesh(PrimitiveType.Cube);
        var sphere = PrimitiveMesh(PrimitiveType.Sphere);
        m_Cube = m_Brg.RegisterMesh(cube);
        m_Sphere = m_Brg.RegisterMesh(sphere);
        var lit = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        lit.SetColor("_BaseColor", Color.white);
        var unlit = new Material(Shader.Find("Universal Render Pipeline/Unlit"));
        unlit.SetColor("_BaseColor", Color.magenta);
        m_Lit = m_Brg.RegisterMaterial(lit);
        m_Unlit = m_Brg.RegisterMaterial(unlit);
        Row("ids.cubeMesh", m_Cube.value);
        Row("ids.sphereMesh", m_Sphere.value);
        Row("ids.lit", m_Lit.value);
        Row("ids.unlit", m_Unlit.value);
        Row("ids.cubeAgain", m_Brg.RegisterMesh(cube).value);
        Row("ids.registeredMesh", m_Brg.GetRegisteredMesh(m_Cube) == cube);
        Row("ids.registeredMaterial", m_Brg.GetRegisteredMaterial(m_Unlit) == unlit);

        // 0: a zeroed matrix (loads of unset metadata read zeros); 64: padding; then the
        // properties, each an array of one value per instance.
        int objectToWorld = SizeOfPackedMatrix * 2;
        int worldToObject = objectToWorld + SizeOfPackedMatrix * Instances;
        int colors = worldToObject + SizeOfPackedMatrix * Instances;
        int bytes = colors + SizeOfFloat4 * Instances;
        var data = m_Data = new float[bytes / 4];
        m_ColorsAt = colors;
        m_Bytes = bytes;
        m_ObjectToWorldAt = objectToWorld;
        m_WorldToObjectAt = worldToObject;
        for (int i = 0; i < Instances; i++)
        {
            var m = InstanceMatrix(i);
            Pack(data, objectToWorld / 4 + i * 12, m);
            Pack(data, worldToObject / 4 + i * 12, m.inverse);
            var c = InstanceColor(i);
            int o = colors / 4 + i * 4;
            data[o] = c.r; data[o + 1] = c.g; data[o + 2] = c.b; data[o + 3] = c.a;
        }
        m_Buffer = NewBuffer(data);
        m_Batch = AddInstanceBatch(m_Buffer);
        Row("ids.batch", m_Batch.value);
    }

    /// <summary>A buffer of the device's target holding <paramref name="data"/>.</summary>
    GraphicsBuffer NewBuffer(float[] data)
    {
        var buffer = BatchRendererGroup.BufferTarget == BatchBufferTarget.ConstantBuffer
            ? new GraphicsBuffer(GraphicsBuffer.Target.Constant, m_Bytes / 16, 16)
            : new GraphicsBuffer(GraphicsBuffer.Target.Raw, m_Bytes / 4, 4);
        buffer.SetData(data);
        return buffer;
    }

    NativeArray<MetadataValue> Metadata()
    {
        var metadata = new NativeArray<MetadataValue>(3, Allocator.Temp);
        metadata[0] = new MetadataValue { NameID = Shader.PropertyToID("unity_ObjectToWorld"), Value = PerInstance | (uint)m_ObjectToWorldAt };
        metadata[1] = new MetadataValue { NameID = Shader.PropertyToID("unity_WorldToObject"), Value = PerInstance | (uint)m_WorldToObjectAt };
        metadata[2] = new MetadataValue { NameID = Shader.PropertyToID("_BaseColor"), Value = PerInstance | (uint)m_ColorsAt };
        return metadata;
    }

    /// <summary>A batch of the instances in <paramref name="buffer"/> (a constant buffer through a
    /// window of the whole data).</summary>
    BatchID AddInstanceBatch(GraphicsBuffer buffer)
    {
        var metadata = Metadata();
        var id = BatchRendererGroup.BufferTarget == BatchBufferTarget.ConstantBuffer
            ? m_Brg.AddBatch(metadata, buffer.bufferHandle, 0, (uint)m_Bytes)
            : m_Brg.AddBatch(metadata, buffer.bufferHandle);
        metadata.Dispose();
        return id;
    }

    /// <summary>The instance data with every cube's colour replaced.</summary>
    float[] Recolored(Color color)
    {
        var data = (float[])m_Data.Clone();
        for (int i = 0; i < Cubes; i++)
        {
            int o = m_ColorsAt / 4 + i * 4;
            data[o] = color.r; data[o + 1] = color.g; data[o + 2] = color.b; data[o + 3] = color.a;
        }
        return data;
    }

    /// <summary>A batch removed and added again, a mesh unregistered by every owner and registered
    /// again, then the instance data changed between the two renders (the second draws the new
    /// colours), and another buffer swapped in for the batch.</summary>
    void Lifetime()
    {
        m_Brg.RemoveBatch(m_Batch);
        m_Batch = AddInstanceBatch(m_Buffer);
        Row("lifetime.batchAgain", m_Batch.value);
        m_Brg.UnregisterMesh(m_Cube);
        Row("lifetime.meshKept", m_Brg.GetRegisteredMesh(m_Cube) == m_CubeMesh);
        m_Brg.UnregisterMesh(m_Cube);
        Row("lifetime.meshGone", m_Brg.GetRegisteredMesh(m_Cube) == null);
        m_Cube = m_Brg.RegisterMesh(m_CubeMesh);
        Row("lifetime.meshAgain", m_Cube.value);
        m_BetweenRenders = () => m_Buffer.SetData(Recolored(new Color(0.9f, 0.1f, 0.1f, 1f)));
        Scenario("lifetime");
        RecordViews();
        m_BetweenRenders = null;
        var swap = NewBuffer(Recolored(new Color(0.1f, 0.2f, 0.9f, 1f)));
        m_Brg.SetBatchBuffer(m_Batch, swap.bufferHandle);
        Scenario("swap");
        RecordViews();
        m_Brg.SetBatchBuffer(m_Batch, m_Buffer.bufferHandle);
        swap.Dispose();
    }

    /// <summary>What the API refuses on the device, with the Editor's messages: AddBatch windows
    /// against the buffer target; then commands of an unknown mesh and of a material without DOTS
    /// instancing over two renders (not drawn).</summary>
    void Errors()
    {
        Application.logMessageReceived += Capture;
        try
        {
            var metadata = Metadata();
            if (BatchRendererGroup.BufferTarget == BatchBufferTarget.ConstantBuffer)
            {
                Row("errors.add.noWindow", m_Brg.AddBatch(metadata, m_Buffer.bufferHandle).value);
                Row("errors.add.unaligned", m_Brg.AddBatch(metadata, m_Buffer.bufferHandle, 16, 256).value);
                Row("errors.add.twiceMaxWindow", m_Brg.AddBatch(metadata, m_Buffer.bufferHandle, 0, 32768).value);
                Row("errors.add.overConstantBufferSize", m_Brg.AddBatch(metadata, m_Buffer.bufferHandle, 0, 65536 + 256).value);
            }
            else
            {
                Row("errors.add.window", m_Brg.AddBatch(metadata, m_Buffer.bufferHandle, 0, 256).value);
                Row("errors.add.offsetOnly", m_Brg.AddBatch(metadata, m_Buffer.bufferHandle, 256, 0).value);
            }
            metadata.Dispose();
            RecordLog("errors.add");
            var noDots = new Material(Shader.Find("Probe/NoDots"));
            m_NoDots = m_Brg.RegisterMaterial(noDots);
            // The Editor logs an error for each such command once per run, delivered after the
            // frame that drew them: a capture closed in the same frame sees them only by chance, so
            // nothing is recorded of them.
            Scenario("errors");
            RecordViews();
            m_Log.Clear();
        }
        finally
        {
            Application.logMessageReceived -= Capture;
        }
    }

    void Capture(string message, string stackTrace, LogType type)
    {
        // Unbound's own notes are not the Editor's.
        if (message.StartsWith("Unbound:", StringComparison.Ordinal)) return;
        m_Log.Add(type + ": " + message);
    }

    /// <summary>The messages logged since the last call, each with how many times.</summary>
    void RecordLog(string key)
    {
        var counts = new SortedDictionary<string, int>(StringComparer.Ordinal);
        foreach (var line in m_Log) counts[line] = counts.TryGetValue(line, out int n) ? n + 1 : 1;
        int i = 0;
        foreach (var pair in counts) Row(key + ".log" + i++, pair.Value + "x " + pair.Key);
        Row(key + ".logs", counts.Count);
        m_Log.Clear();
    }

    static Matrix4x4 InstanceMatrix(int i)
    {
        if (i < Cubes)
        {
            float x = -2.8f + i * 0.8f;
            var rotation = Quaternion.Euler(i * 11f, i * 23f, i * 7f);
            var scale = new Vector3(0.5f, 0.35f + 0.05f * i, 0.5f);
            return Matrix4x4.TRS(new Vector3(x, 0.2f, 1.5f), rotation, scale);
        }
        if (i < Cubes + Spheres)
        {
            int s = i - Cubes;
            return Matrix4x4.TRS(new Vector3(-0.6f + 1.2f * s, 1.9f, 0.5f), Quaternion.identity, Vector3.one * (0.4f + 0.2f * s));
        }
        if (i < Cubes + Spheres + Mirrored)
        {
            // Mirrored along x: front faces wind the other way.
            int k = i - Cubes - Spheres;
            return Matrix4x4.TRS(new Vector3(-1.8f + 3.6f * k, -0.9f, 0.5f), Quaternion.Euler(20f, 35f, 0f), new Vector3(-0.7f, 0.7f, 0.7f));
        }
        int m = i - Cubes - Spheres - Mirrored;
        return Matrix4x4.TRS(new Vector3(2.2f - 4.6f * m, 0.4f, 0.2f + 0.6f * m), Quaternion.identity, new Vector3(0.3f, 1.2f, 0.3f));
    }

    static Color InstanceColor(int i)
    {
        if (i >= Cubes + Spheres + Mirrored) return new Color(0.95f, 0.95f, 0.6f, 1f);
        if (i >= Cubes + Spheres) return new Color(0.9f, 0.3f, 0.8f, 1f);
        if (i >= Cubes) return i == Cubes ? new Color(0.1f, 0.9f, 0.3f, 1f) : new Color(1f, 0.8f, 0.1f, 1f);
        float t = i / (float)(Cubes - 1);
        return new Color(t, 0.3f + 0.4f * (1 - t), 1f - t, 1f);
    }

    static void Pack(float[] data, int at, Matrix4x4 m)
    {
        data[at + 0] = m.m00; data[at + 1] = m.m10; data[at + 2] = m.m20;
        data[at + 3] = m.m01; data[at + 4] = m.m11; data[at + 5] = m.m21;
        data[at + 6] = m.m02; data[at + 7] = m.m12; data[at + 8] = m.m22;
        data[at + 9] = m.m03; data[at + 10] = m.m13; data[at + 11] = m.m23;
    }

    /// <summary>The main light casts hard shadows in two cascades onto a ground plane; the range
    /// casts and receives shadows, and a second group takes light views alone. The sun's culling
    /// mask leaves out the pillar's layer (the sun does not light it), and the camera's leaves out a
    /// second pillar's.</summary>
    void Shadows()
    {
        m_Light = FindFirstObjectByType<Light>();
        var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        int cascades = urp.shadowCascadeCount;
        urp.shadowCascadeCount = 2;
        m_Light.shadows = LightShadows.Hard;
        int lightMask = m_Light.cullingMask;
        m_Light.cullingMask = ~(1 << MaskedLayer);
        int cameraMask = m_Camera.cullingMask;
        m_Camera.cullingMask = ~(1 << CameraMaskedLayer);
        var ground = GameObject.CreatePrimitive(PrimitiveType.Plane);
        ground.transform.position = new Vector3(0f, -0.45f, 1.5f);
        ground.transform.localScale = new Vector3(0.8f, 1f, 0.5f);
        m_LightOnly = new BatchRendererGroup(OnPerformCulling, (IntPtr)2);
        m_LightOnly.SetEnabledViewTypes(new[] { BatchCullingViewType.Light });
        m_Shadows = true;
        Scenario("shadows");
        RecordViews();
        m_Shadows = false;
        m_LightOnly.Dispose();
        m_LightOnly = null;
        DestroyImmediate(ground);
        m_Light.shadows = LightShadows.None;
        m_Light.cullingMask = lightMask;
        m_Camera.cullingMask = cameraMask;
        urp.shadowCascadeCount = cascades;
    }

    /// <summary>A second camera, elsewhere, renders the group: its own culling and callback. Its
    /// culling mask leaves out the pillar's layer.</summary>
    void Cameras()
    {
        var second = new GameObject("Second Camera").AddComponent<Camera>();
        second.CopyFrom(m_Camera);
        second.transform.SetPositionAndRotation(new Vector3(3f, 2f, -4f), Quaternion.Euler(18f, -35f, 0f));
        second.fieldOfView = 50f;
        second.cullingMask = ~(1 << MaskedLayer);
        Scenario("cameras", second);
        RecordViews();
        DestroyImmediate(second.gameObject);
    }

    /// <summary>The renderer's Forward+ path, whose clustered light data (tiles, z-bins, the
    /// additional lights and reflection probes) URP binds as constant buffers, under six point
    /// and two spot lights: the group's commands beside the scene's ordinary Lit sphere
    /// (`forward_plus`), then the group drawing nothing and ordinary Lit cubes where its cubes
    /// were (`forward_plus_plain`).</summary>
    void ForwardPlus()
    {
        var urp = GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
        var renderer = (UniversalRendererData)urp.rendererDataList[0];
        var mode = renderer.renderingMode;
        renderer.renderingMode = RenderingMode.ForwardPlus;
        var added = new List<GameObject>();
        for (int i = 0; i < 8; i++)
        {
            var go = new GameObject("Forward+ Light " + i);
            var light = go.AddComponent<Light>();
            float t = i / 7f;
            light.color = Color.HSVToRGB(t, 0.8f, 1f);
            if (i < 6)
            {
                light.type = LightType.Point;
                go.transform.position = new Vector3(-3f + 1.2f * i, 0.9f - 0.3f * (i % 2), 0.6f);
                light.range = 2.2f;
                light.intensity = 1.5f;
            }
            else
            {
                light.type = LightType.Spot;
                go.transform.SetPositionAndRotation(new Vector3(i == 6 ? -1.5f : 1.5f, 2.5f, 0.8f), Quaternion.Euler(80f, 0f, 0f));
                light.range = 5f;
                light.spotAngle = 45f;
                light.intensity = 3f;
            }
            added.Add(go);
        }
        Scenario("forward_plus");
        RecordViews();
        m_NoCommands = true;
        var lit = Shader.Find("Universal Render Pipeline/Lit");
        for (int i = 0; i < Cubes; i++)
        {
            var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            var m = InstanceMatrix(i);
            cube.transform.SetPositionAndRotation(m.GetPosition(), m.rotation);
            cube.transform.localScale = m.lossyScale;
            var material = new Material(lit);
            material.SetColor("_BaseColor", InstanceColor(i));
            cube.GetComponent<MeshRenderer>().sharedMaterial = material;
            added.Add(cube);
        }
        Scenario("forward_plus_plain");
        RecordViews();
        m_NoCommands = false;
        foreach (var go in added) DestroyImmediate(go);
        renderer.renderingMode = mode;
    }

    void RecordViews()
    {
        foreach (var pair in m_Views) Row(pair.Key, pair.Value);
        m_Views.Clear();
    }

    void Teardown()
    {
        if (m_Brg != null)
        {
            m_Brg.RemoveBatch(m_Batch);
            m_Brg.Dispose();
            m_Brg = null;
        }
        m_Buffer?.Dispose();
    }

    /// <summary>Renders the main camera twice into a 320x180 texture (the second render is the
    /// one recorded) and writes it as the scenario's image.</summary>
    void Scenario(string name, Camera camera = null)
    {
        m_Scenario = name;
        m_ViewCamera = camera ?? m_Camera;
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        m_ViewCamera.targetTexture = target;
        for (m_Render = 1; m_Render <= 2; m_Render++)
        {
            m_Calls = 0;
            m_ViewCamera.Render();
            Row(name + ".r" + m_Render + ".calls", m_Calls);
            if (m_Render == 1) m_BetweenRenders?.Invoke();
        }
        RenderTexture.active = target;
        var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        texture.Apply();
        RenderTexture.active = null;
        m_ViewCamera.targetTexture = null;
        target.Release();
        if (!string.IsNullOrEmpty(s_Out)) File.WriteAllBytes(Path.Combine(s_Out, name + ".png"), texture.EncodeToPNG());
    }

    JobHandle OnPerformCulling(BatchRendererGroup group, BatchCullingContext context, BatchCullingOutput output, IntPtr user)
    {
        int call = m_Calls++;
        string key = m_Scenario + ".r" + m_Render + ".cull" + call;
        if (call < (m_Scenario == "basic" ? 4 : 8)) RecordContext(key, group, context, user);
        if (m_Scenario != "basic")
        {
            string view = m_Scenario + ".views." + (group == m_Brg ? "drawing" : "lightOnly") + "." + context.viewType;
            m_Views[view] = m_Views.TryGetValue(view, out int n) ? n + 1 : 1;
        }
        if (group != m_Brg || m_NoCommands) return new JobHandle();
        // The pillar's range (the shadows and cameras scenarios).
        bool masked = m_Scenario != "basic";
        bool errors = m_Scenario == "errors";
        int commandCount = (masked ? Commands + Masked : Commands) + (errors ? 2 : 0);
        var commands = (BatchCullingOutputDrawCommands*)output.drawCommands.GetUnsafePtr();
        Row(key + ".outputCount", output.drawCommands.Length);
        Row(key + ".outputZeroed", commands->drawCommandCount == 0 && commands->drawCommands == null && commands->visibleInstances == null);
        int alignment = UnsafeUtility.AlignOf<long>();
        commands->drawCommands = (BatchDrawCommand*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<BatchDrawCommand>() * commandCount, alignment, Allocator.TempJob);
        commands->drawRanges = (BatchDrawRange*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<BatchDrawRange>() * (2 + Masked), alignment, Allocator.TempJob);
        commands->visibleInstances = (int*)UnsafeUtility.Malloc(Instances * sizeof(int), alignment, Allocator.TempJob);
        commands->drawCommandPickingInstanceIDs = null;
        commands->drawCommandCount = commandCount;
        commands->drawRangeCount = masked ? 1 + Masked : 1;
        commands->visibleInstanceCount = Instances;
        commands->instanceSortingPositions = null;
        commands->instanceSortingPositionFloatCount = 0;
        commands->drawCommands[0] = new BatchDrawCommand
        {
            visibleOffset = 0, visibleCount = Cubes, batchID = m_Batch, materialID = m_Lit, meshID = m_Cube,
            submeshIndex = 0, splitVisibilityMask = 0xff, flags = BatchDrawCommandFlags.None, sortingPosition = 0,
        };
        commands->drawCommands[1] = new BatchDrawCommand
        {
            visibleOffset = Cubes, visibleCount = Spheres, batchID = m_Batch, materialID = m_Unlit, meshID = m_Sphere,
            submeshIndex = 0, splitVisibilityMask = 0xff, flags = BatchDrawCommandFlags.None, sortingPosition = 0,
        };
        for (int k = 0; k < Mirrored; k++)
            commands->drawCommands[2 + k] = new BatchDrawCommand
            {
                visibleOffset = (uint)(Cubes + Spheres + k), visibleCount = 1, batchID = m_Batch, materialID = m_Lit, meshID = m_Cube,
                submeshIndex = 0, splitVisibilityMask = 0xff, flags = k == 0 ? BatchDrawCommandFlags.FlipWinding : BatchDrawCommandFlags.None, sortingPosition = 0,
            };
        commands->drawRanges[0] = new BatchDrawRange
        {
            drawCommandsType = BatchDrawCommandType.Direct, drawCommandsBegin = 0, drawCommandsCount = Commands,
            filterSettings = m_Shadows
                ? new BatchFilterSettings { renderingLayerMask = 0xffffffff, shadowCastingMode = ShadowCastingMode.On, receiveShadows = true }
                : new BatchFilterSettings { renderingLayerMask = 0xffffffff },
        };
        if (masked)
            for (int m = 0; m < Masked; m++)
            {
                commands->drawCommands[Commands + m] = new BatchDrawCommand
                {
                    visibleOffset = (uint)(Cubes + Spheres + Mirrored + m), visibleCount = 1, batchID = m_Batch, materialID = m_Lit, meshID = m_Cube,
                    submeshIndex = 0, splitVisibilityMask = 0xff, flags = BatchDrawCommandFlags.None, sortingPosition = 0,
                };
                commands->drawRanges[1 + m] = new BatchDrawRange
                {
                    drawCommandsType = BatchDrawCommandType.Direct, drawCommandsBegin = (uint)(Commands + m), drawCommandsCount = 1,
                    filterSettings = new BatchFilterSettings
                    {
                        renderingLayerMask = 0xffffffff, layer = (byte)(m == 0 ? MaskedLayer : CameraMaskedLayer),
                        shadowCastingMode = ShadowCastingMode.On, receiveShadows = true,
                    },
                };
            }
        if (errors)
        {
            // An unknown mesh, and a material without DOTS instancing, in a range of their own.
            int at = Commands + Masked;
            commands->drawCommands[at] = new BatchDrawCommand
            {
                visibleOffset = 0, visibleCount = 1, batchID = m_Batch, materialID = m_Lit, meshID = new BatchMeshID { value = 99 },
                submeshIndex = 0, splitVisibilityMask = 0xff,
            };
            commands->drawCommands[at + 1] = new BatchDrawCommand
            {
                visibleOffset = 1, visibleCount = 1, batchID = m_Batch, materialID = m_NoDots, meshID = m_Cube,
                submeshIndex = 0, splitVisibilityMask = 0xff,
            };
            commands->drawRanges[1 + Masked] = new BatchDrawRange
            {
                drawCommandsType = BatchDrawCommandType.Direct, drawCommandsBegin = (uint)at, drawCommandsCount = 2,
                filterSettings = new BatchFilterSettings { renderingLayerMask = 0xffffffff },
            };
            commands->drawRangeCount = 2 + Masked;
        }
        for (int i = 0; i < Instances; i++) commands->visibleInstances[i] = i;
        return new JobHandle();
    }

    void RecordContext(string key, BatchRendererGroup group, BatchCullingContext c, IntPtr user)
    {
        Row(key + ".group", group == m_Brg);
        Row(key + ".user", (long)user);
        Row(key + ".viewType", c.viewType.ToString());
        Row(key + ".projectionType", c.projectionType.ToString());
        Row(key + ".cullingFlags", c.cullingFlags.ToString());
        Row(key + ".cullingLayerMask", c.cullingLayerMask);
        Row(key + ".sceneCullingMask", c.sceneCullingMask.ToString(CultureInfo.InvariantCulture));
        Row(key + ".splitExclusionMask", c.splitExclusionMask);
        Row(key + ".receiverPlaneOffset", c.receiverPlaneOffset);
        Row(key + ".receiverPlaneCount", c.receiverPlaneCount);
        Row(key + ".viewIsCamera", c.viewID.GetInstanceID() == m_ViewCamera.GetInstanceID());
        if (m_Scenario != "basic") Row(key + ".viewIsLight", m_Light != null && c.viewID.GetInstanceID() == m_Light.GetInstanceID());
        Row(key + ".viewSlice", c.viewID.GetSliceIndex());
        Row(key + ".localToWorld", Floats(c.localToWorldMatrix));
        var lod = c.lodParameters;
        Row(key + ".lod.isOrthographic", lod.isOrthographic);
        Row(key + ".lod.cameraPosition", Floats(lod.cameraPosition));
        Row(key + ".lod.fieldOfView", Round(lod.fieldOfView));
        Row(key + ".lod.orthoSize", Round(lod.orthoSize));
        Row(key + ".lod.cameraPixelHeight", lod.cameraPixelHeight);
        Row(key + ".planes", c.cullingPlanes.Length);
        for (int i = 0; i < c.cullingPlanes.Length; i++)
        {
            var p = c.cullingPlanes[i];
            Row(key + ".plane" + i, Floats(new Vector4(p.normal.x, p.normal.y, p.normal.z, p.distance)));
        }
        if (m_Scenario != "basic")
            for (int i = 0; i < c.receiverPlaneCount; i++)
            {
                var p = c.cullingPlanes[c.receiverPlaneOffset + i];
                Row(key + ".receiver" + i, Floats(new Vector4(p.normal.x, p.normal.y, p.normal.z, p.distance)));
            }
        Row(key + ".splits", c.cullingSplits.Length);
        for (int i = 0; i < c.cullingSplits.Length; i++)
        {
            var s = c.cullingSplits[i];
            string k = key + ".split" + i;
            Row(k + ".sphere", Floats(new Vector4(s.sphereCenter.x, s.sphereCenter.y, s.sphereCenter.z, s.sphereRadius)));
            Row(k + ".planeOffset", s.cullingPlaneOffset);
            Row(k + ".planeCount", s.cullingPlaneCount);
            Row(k + ".blendFactor", Round(s.cascadeBlendCullingFactor));
            Row(k + ".nearPlane", Round(s.nearPlane));
            Row(k + ".cullingMatrix", Floats(s.cullingMatrix));
        }
    }

    // Values to four decimals: both sides compute them in their own float order.
    static double Round(float f) => Math.Round(f, 4);

    static double[] Floats(Vector3 v) => new[] { Round(v.x), Round(v.y), Round(v.z) };

    static double[] Floats(Vector4 v) => new[] { Round(v.x), Round(v.y), Round(v.z), Round(v.w) };

    static double[] Floats(Matrix4x4 m)
    {
        var values = new double[16];
        for (int i = 0; i < 16; i++) values[i] = Round(m[i]);
        return values;
    }

    static void Row(string key, object value)
    {
        var line = new StringBuilder("{\"k\":\"").Append(key).Append("\",\"v\":");
        Json(line, value);
        s_Rows.Add(line.Append('}').ToString());
    }

    static void Json(StringBuilder line, object value)
    {
        switch (value)
        {
            case null: line.Append("null"); break;
            case bool b: line.Append(b ? "true" : "false"); break;
            case string s: line.Append('"').Append(s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n")).Append('"'); break;
            case double d: line.Append(d.ToString("R", CultureInfo.InvariantCulture)); break;
            case double[] a:
                line.Append('[');
                for (int i = 0; i < a.Length; i++) { if (i > 0) line.Append(','); line.Append(a[i].ToString("R", CultureInfo.InvariantCulture)); }
                line.Append(']');
                break;
            default: line.Append(Convert.ToString(value, CultureInfo.InvariantCulture)); break;
        }
    }
}
