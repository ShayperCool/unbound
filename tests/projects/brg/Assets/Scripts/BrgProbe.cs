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
    const int Cubes = 8, Spheres = 2, Mirrored = 2, Instances = Cubes + Spheres + Mirrored, Commands = 4;

    static readonly List<string> s_Rows = new List<string>();
    static string s_Out;

    BatchRendererGroup m_Brg;
    GraphicsBuffer m_Buffer;
    BatchID m_Batch;
    BatchMeshID m_Cube, m_Sphere;
    BatchMaterialID m_Lit, m_Unlit;
    int m_Calls;
    string m_Scenario = "";
    int m_Render;
    Camera m_Camera;

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
        var cube = PrimitiveMesh(PrimitiveType.Cube);
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
        var data = new float[bytes / 4];
        for (int i = 0; i < Instances; i++)
        {
            var m = InstanceMatrix(i);
            Pack(data, objectToWorld / 4 + i * 12, m);
            Pack(data, worldToObject / 4 + i * 12, m.inverse);
            var c = InstanceColor(i);
            int o = colors / 4 + i * 4;
            data[o] = c.r; data[o + 1] = c.g; data[o + 2] = c.b; data[o + 3] = c.a;
        }
        if (target == BatchBufferTarget.ConstantBuffer)
        {
            m_Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Constant, bytes / 16, 16);
            m_Buffer.SetData(data);
        }
        else
        {
            m_Buffer = new GraphicsBuffer(GraphicsBuffer.Target.Raw, bytes / 4, 4);
            m_Buffer.SetData(data);
        }
        var metadata = new NativeArray<MetadataValue>(3, Allocator.Temp);
        metadata[0] = new MetadataValue { NameID = Shader.PropertyToID("unity_ObjectToWorld"), Value = PerInstance | (uint)objectToWorld };
        metadata[1] = new MetadataValue { NameID = Shader.PropertyToID("unity_WorldToObject"), Value = PerInstance | (uint)worldToObject };
        metadata[2] = new MetadataValue { NameID = Shader.PropertyToID("_BaseColor"), Value = PerInstance | (uint)colors };
        m_Batch = target == BatchBufferTarget.ConstantBuffer
            ? m_Brg.AddBatch(metadata, m_Buffer.bufferHandle, 0, (uint)bytes)
            : m_Brg.AddBatch(metadata, m_Buffer.bufferHandle);
        metadata.Dispose();
        Row("ids.batch", m_Batch.value);
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
        // Mirrored along x: front faces wind the other way.
        int k = i - Cubes - Spheres;
        return Matrix4x4.TRS(new Vector3(-1.8f + 3.6f * k, -0.9f, 0.5f), Quaternion.Euler(20f, 35f, 0f), new Vector3(-0.7f, 0.7f, 0.7f));
    }

    static Color InstanceColor(int i)
    {
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
    void Scenario(string name)
    {
        m_Scenario = name;
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        m_Camera.targetTexture = target;
        for (m_Render = 1; m_Render <= 2; m_Render++)
        {
            m_Calls = 0;
            m_Camera.Render();
            Row(name + ".r" + m_Render + ".calls", m_Calls);
        }
        RenderTexture.active = target;
        var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        texture.Apply();
        RenderTexture.active = null;
        m_Camera.targetTexture = null;
        target.Release();
        if (!string.IsNullOrEmpty(s_Out)) File.WriteAllBytes(Path.Combine(s_Out, name + ".png"), texture.EncodeToPNG());
    }

    JobHandle OnPerformCulling(BatchRendererGroup group, BatchCullingContext context, BatchCullingOutput output, IntPtr user)
    {
        int call = m_Calls++;
        string key = m_Scenario + ".r" + m_Render + ".cull" + call;
        if (call < 4) RecordContext(key, group, context, user);
        var commands = (BatchCullingOutputDrawCommands*)output.drawCommands.GetUnsafePtr();
        Row(key + ".outputCount", output.drawCommands.Length);
        Row(key + ".outputZeroed", commands->drawCommandCount == 0 && commands->drawCommands == null && commands->visibleInstances == null);
        int alignment = UnsafeUtility.AlignOf<long>();
        commands->drawCommands = (BatchDrawCommand*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<BatchDrawCommand>() * Commands, alignment, Allocator.TempJob);
        commands->drawRanges = (BatchDrawRange*)UnsafeUtility.Malloc(UnsafeUtility.SizeOf<BatchDrawRange>(), alignment, Allocator.TempJob);
        commands->visibleInstances = (int*)UnsafeUtility.Malloc(Instances * sizeof(int), alignment, Allocator.TempJob);
        commands->drawCommandPickingInstanceIDs = null;
        commands->drawCommandCount = Commands;
        commands->drawRangeCount = 1;
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
            filterSettings = new BatchFilterSettings { renderingLayerMask = 0xffffffff },
        };
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
        Row(key + ".viewIsCamera", c.viewID.GetInstanceID() == m_Camera.GetInstanceID());
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
