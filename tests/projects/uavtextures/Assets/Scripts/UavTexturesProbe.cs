using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

/// <summary>
/// Measures fragment and compute writes to 4-channel random-write RenderTextures through
/// 1/2/3/4-channel RWTexture2D/3D declarations, fragment random-write texture binding lifecycle
/// (Graphics/CommandBuffer.SetRandomWriteTarget, ClearRandomWriteTargets, slot replacement,
/// mixed buffer+texture UAVs, read-modify-write), vertex switch default/case-0 fallthrough,
/// and tightly packed StructuredBuffer struct layouts.
/// </summary>
public class UavTexturesProbe : MonoBehaviour
{
    readonly StringBuilder records = new StringBuilder();
    string output;
    ComputeShader cs;
    Material mat;
    RenderTexture dummy;
    Mesh quad;
    ComputeBuffer readF, readU;
    readonly List<UnityEngine.Object> made = new List<UnityEngine.Object>();
    readonly List<IDisposable> disposables = new List<IDisposable>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("UAVTEXTURES_PROBE_OUT") == null) return;
        new GameObject("UavTexturesProbe").AddComponent<UavTexturesProbe>();
    }

    void Value(string k, string json) =>
        records.Append("{\"k\":\"").Append(k).Append("\",\"v\":").Append(json).Append("}\n");

    static string F4(float[] v)
    {
        var sb = new StringBuilder("[");
        for (int i = 0; i < v.Length; i++)
        {
            if (i > 0) sb.Append(',');
            sb.Append(Math.Round(v[i], 4).ToString("0.####", CultureInfo.InvariantCulture));
        }
        sb.Append(']');
        return sb.ToString();
    }

    static string U4(uint[] v) => "[" + string.Join(",", v) + "]";

    int frames;
    bool done;

    void Update()
    {
        if (done) return;
        if (++frames < 2) return;
        done = true;
        output = Environment.GetEnvironmentVariable("UAVTEXTURES_PROBE_OUT");
        try
        {
            Run();
            Value("complete", "true");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Value("fatal", "\"" + e.ToString().Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"");
        }
        finally
        {
            foreach (var d in disposables) d.Dispose();
            foreach (var o in made) if (o != null) Destroy(o);
            if (!string.IsNullOrEmpty(output))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(output));
                File.WriteAllText(output, records.ToString());
            }
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(0);
#else
            Application.Quit();
#endif
        }
    }

    RenderTexture MakeRT2D(GraphicsFormat fmt)
    {
        var desc = new RenderTextureDescriptor(4, 4, fmt, 0) { enableRandomWrite = true, msaaSamples = 1 };
        var rt = new RenderTexture(desc);
        rt.Create();
        made.Add(rt);
        return rt;
    }

    RenderTexture MakeRT3D(GraphicsFormat fmt)
    {
        var desc = new RenderTextureDescriptor
        {
            width = 4, height = 4, volumeDepth = 4,
            dimension = TextureDimension.Tex3D,
            graphicsFormat = fmt,
            enableRandomWrite = true,
            msaaSamples = 1,
        };
        var rt = new RenderTexture(desc);
        rt.Create();
        made.Add(rt);
        return rt;
    }

    Mesh MakeQuad()
    {
        var m = new Mesh();
        m.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(1, -1, 0), new Vector3(1, 1, 0), new Vector3(-1, 1, 0) };
        m.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        made.Add(m);
        return m;
    }

    void Clear2DF(RenderTexture rt)
    {
        int k = cs.FindKernel("Clear2DFloat4");
        cs.SetTexture(k, "_Clear2F", rt);
        cs.Dispatch(k, 1, 1, 1);
    }

    void Clear3DF(RenderTexture rt, int slice)
    {
        int k = cs.FindKernel("Clear3DFloat4");
        cs.SetTexture(k, "_Clear3F", rt);
        cs.SetInt("_Slice", slice);
        cs.Dispatch(k, 1, 1, 1);
    }

    void Clear2DU(RenderTexture rt)
    {
        int k = cs.FindKernel("Clear2DUint4");
        cs.SetTexture(k, "_Clear2U", rt);
        cs.Dispatch(k, 1, 1, 1);
    }

    float[] Read2DF(RenderTexture rt)
    {
        int k = cs.FindKernel("Read2DFloat4");
        cs.SetTexture(k, "_In2F", rt);
        cs.SetBuffer(k, "_ReadF", readF);
        cs.Dispatch(k, 1, 1, 1);
        var data = new float[4];
        readF.GetData(data, 0, 0, 4);
        return data;
    }

    float[] Read3DF(RenderTexture rt, int slice)
    {
        int k = cs.FindKernel("Read3DFloat4");
        cs.SetTexture(k, "_In3F", rt);
        cs.SetInt("_Slice", slice);
        cs.SetBuffer(k, "_ReadF", readF);
        cs.Dispatch(k, 1, 1, 1);
        var data = new float[4];
        readF.GetData(data, 0, 0, 4);
        return data;
    }

    uint[] Read2DU(RenderTexture rt)
    {
        int k = cs.FindKernel("Read2DUint4");
        cs.SetTexture(k, "_In2U", rt);
        cs.SetBuffer(k, "_ReadU", readU);
        cs.Dispatch(k, 1, 1, 1);
        var data = new uint[4];
        readU.GetData(data, 0, 0, 4);
        return data;
    }

    void DrawFragPass(int pass, Action<CommandBuffer> setup)
    {
        using var cmd = new CommandBuffer();
        setup(cmd);
        cmd.SetRenderTarget(dummy);
        cmd.SetViewport(new Rect(0, 0, dummy.width, dummy.height));
        cmd.DrawMesh(quad, Matrix4x4.identity, mat, 0, pass);
        cmd.ClearRandomWriteTargets();
        Graphics.ExecuteCommandBuffer(cmd);
    }

    void Run()
    {
        cs = Resources.Load<ComputeShader>("UavComputeOps");
        mat = new Material(Resources.Load<Shader>("UavFragWrite"));
        made.Add(mat);
        dummy = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32);
        dummy.Create();
        made.Add(dummy);
        quad = MakeQuad();
        readF = new ComputeBuffer(8, sizeof(float));
        readU = new ComputeBuffer(4, sizeof(uint));
        disposables.Add(readF);
        disposables.Add(readU);

        var half2d = MakeRT2D(GraphicsFormat.R16G16B16A16_SFloat);
        var unorm2d = MakeRT2D(GraphicsFormat.R8G8B8A8_UNorm);
        var uint2d = MakeRT2D(GraphicsFormat.R32G32B32A32_UInt);
        var half3d = MakeRT3D(GraphicsFormat.R16G16B16A16_SFloat);

        // 1. Fragment <4-channel RWTexture2D writes into 4-channel RGBAHalf and ARGB32 targets
        foreach (var (fmtName, rt) in new[] { ("rgba16f", half2d), ("rgba8", unorm2d) })
        {
            foreach (var (vName, val) in new[] { ("0", 0.0f), ("0.5", 0.5f), ("1", 1.0f) })
            {
                Clear2DF(rt);
                mat.SetFloat("_Val", val);
                DrawFragPass(0, cmd => cmd.SetRandomWriteTarget(1, rt));
                Value($"frag.2d.f1.{fmtName}.{vName}", F4(Read2DF(rt)));
            }
            Clear2DF(rt);
            mat.SetVector("_Val4", new Vector4(0.25f, 0.75f, 0.5f, 0.125f));
            DrawFragPass(1, cmd => cmd.SetRandomWriteTarget(1, rt));
            Value($"frag.2d.f2.{fmtName}", F4(Read2DF(rt)));

            Clear2DF(rt);
            DrawFragPass(2, cmd => cmd.SetRandomWriteTarget(1, rt));
            Value($"frag.2d.f3.{fmtName}", F4(Read2DF(rt)));

            Clear2DF(rt);
            DrawFragPass(3, cmd => cmd.SetRandomWriteTarget(1, rt));
            Value($"frag.2d.f4.{fmtName}", F4(Read2DF(rt)));
        }

        // Fragment RWTexture2D<uint> into R32G32B32A32_UInt
        foreach (uint u in new uint[] { 0u, 7u })
        {
            Clear2DU(uint2d);
            mat.SetInt("_UVal", (int)u);
            DrawFragPass(4, cmd => cmd.SetRandomWriteTarget(1, uint2d));
            Value($"frag.2d.u1.{u}", U4(Read2DU(uint2d)));
        }

        // Fragment RWTexture3D<float> at u4 into 3D RGBAHalf (VoxelizeScene pattern)
        for (int s = 0; s < 3; s++)
        {
            float val = s == 0 ? 0.0f : (s == 1 ? 0.5f : 1.0f);
            Clear3DF(half3d, s);
            mat.SetFloat("_Val", val);
            mat.SetInt("_Slice", s);
            DrawFragPass(5, cmd => cmd.SetRandomWriteTarget(4, half3d));
            Value($"frag.3d.f1.rgba16f.s{s}", F4(Read3DF(half3d, s)));
        }

        // 2. Compute <4-channel RWTexture2D/3D writes into the exact same 4-channel targets
        foreach (var (fmtName, rt) in new[] { ("rgba16f", half2d), ("rgba8", unorm2d) })
        {
            foreach (var (vName, val) in new[] { ("0", 0.0f), ("0.5", 0.5f), ("1", 1.0f) })
            {
                Clear2DF(rt);
                int k = cs.FindKernel("Write2DFloat");
                cs.SetTexture(k, "_OutF", rt);
                cs.SetFloat("_Val", val);
                cs.Dispatch(k, 1, 1, 1);
                Value($"comp.2d.f1.{fmtName}.{vName}", F4(Read2DF(rt)));
            }
            Clear2DF(rt);
            int k2 = cs.FindKernel("Write2DFloat2");
            cs.SetTexture(k2, "_OutF2", rt);
            cs.SetVector("_Val4", new Vector4(0.25f, 0.75f, 0.5f, 0.125f));
            cs.Dispatch(k2, 1, 1, 1);
            Value($"comp.2d.f2.{fmtName}", F4(Read2DF(rt)));

            Clear2DF(rt);
            int k3 = cs.FindKernel("Write2DFloat3");
            cs.SetTexture(k3, "_OutF3", rt);
            cs.SetVector("_Val4", new Vector4(0.25f, 0.75f, 0.5f, 0.125f));
            cs.Dispatch(k3, 1, 1, 1);
            Value($"comp.2d.f3.{fmtName}", F4(Read2DF(rt)));

            Clear2DF(rt);
            int k4 = cs.FindKernel("Write2DFloat4");
            cs.SetTexture(k4, "_OutF4", rt);
            cs.SetVector("_Val4", new Vector4(0.25f, 0.75f, 0.5f, 0.125f));
            cs.Dispatch(k4, 1, 1, 1);
            Value($"comp.2d.f4.{fmtName}", F4(Read2DF(rt)));
        }

        foreach (uint u in new uint[] { 0u, 7u })
        {
            Clear2DU(uint2d);
            int ku = cs.FindKernel("Write2DUint");
            cs.SetTexture(ku, "_OutU", uint2d);
            cs.SetInt("_UVal", (int)u);
            cs.Dispatch(ku, 1, 1, 1);
            Value($"comp.2d.u1.{u}", U4(Read2DU(uint2d)));
        }

        for (int s = 0; s < 3; s++)
        {
            float val = s == 0 ? 0.0f : (s == 1 ? 0.5f : 1.0f);
            Clear3DF(half3d, s);
            int k3d = cs.FindKernel("Write3DFloat");
            cs.SetTexture(k3d, "_Out3F", half3d);
            cs.SetFloat("_Val", val);
            cs.SetInt("_Slice", s);
            cs.Dispatch(k3d, 1, 1, 1);
            Value($"comp.3d.f1.rgba16f.s{s}", F4(Read3DF(half3d, s)));
        }

        // 3. Fragment binding lifecycle: Graphics.SetRandomWriteTarget, clear, replacement, multi-slot, RMW
        var rtA = MakeRT2D(GraphicsFormat.R16G16B16A16_SFloat);
        var rtB = MakeRT2D(GraphicsFormat.R16G16B16A16_SFloat);
        var buf = new ComputeBuffer(4, sizeof(uint));
        disposables.Add(buf);

        // Graphics.SetRandomWriteTarget + DrawMeshNow + ClearRandomWriteTargets
        Clear2DF(rtA);
        mat.SetVector("_Val4", new Vector4(0.75f, 0.5f, 0.25f, 1.0f));
        Graphics.SetRenderTarget(dummy);
        Graphics.SetRandomWriteTarget(1, rtA);
        mat.SetPass(3);
        Graphics.DrawMeshNow(quad, Matrix4x4.identity);
        Graphics.ClearRandomWriteTargets();
        Graphics.SetRenderTarget(null);
        Value("lifecycle.gfx.set", F4(Read2DF(rtA)));

        // After ClearRandomWriteTargets, drawing does not overwrite rtA
        mat.SetVector("_Val4", new Vector4(0.9f, 0.9f, 0.9f, 0.9f));
        Graphics.SetRenderTarget(dummy);
        mat.SetPass(3);
        Graphics.DrawMeshNow(quad, Matrix4x4.identity);
        Graphics.SetRenderTarget(null);
        Value("lifecycle.gfx.cleared", F4(Read2DF(rtA)));

        // Texture replaces texture on slot 1
        Clear2DF(rtA);
        Clear2DF(rtB);
        mat.SetVector("_Val4", new Vector4(0.5f, 0.25f, 0.75f, 1.0f));
        DrawFragPass(3, cmd =>
        {
            cmd.SetRandomWriteTarget(1, rtA);
            cmd.SetRandomWriteTarget(1, rtB);
        });
        Value("lifecycle.replace.tex_old", F4(Read2DF(rtA)));
        Value("lifecycle.replace.tex_new", F4(Read2DF(rtB)));

        // Texture replaces buffer on slot 1, then buffer replaces texture on slot 1
        buf.SetData(new uint[] { 99u, 0, 0, 0 });
        Clear2DF(rtA);
        mat.SetVector("_Val4", new Vector4(0.25f, 0.5f, 0.75f, 1.0f));
        DrawFragPass(3, cmd =>
        {
            cmd.SetRandomWriteTarget(1, buf);
            cmd.SetRandomWriteTarget(1, rtA);
        });
        var bOut = new uint[1];
        buf.GetData(bOut, 0, 0, 1);
        Value("lifecycle.replace.buf_to_tex.buf", bOut[0].ToString());
        Value("lifecycle.replace.buf_to_tex.tex", F4(Read2DF(rtA)));

        buf.SetData(new uint[] { 55u, 0, 0, 0 });
        Clear2DF(rtA);
        mat.SetInt("_UVal", 77);
        DrawFragPass(9, cmd =>
        {
            cmd.SetRandomWriteTarget(1, rtA);
            cmd.SetRandomWriteTarget(1, buf);
        });
        buf.GetData(bOut, 0, 0, 1);
        Value("lifecycle.replace.tex_to_buf.buf", bOut[0].ToString());
        Value("lifecycle.replace.tex_to_buf.tex", F4(Read2DF(rtA)));

        // Invalid (non-random-write) RenderTexture after a valid buffer on slot 1
        var nonRandomRt = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32);
        nonRandomRt.Create();
        made.Add(nonRandomRt);
        buf.SetData(new uint[] { 41u, 0, 0, 0 });
        mat.SetInt("_UVal", 88);
        DrawFragPass(9, cmd =>
        {
            cmd.SetRandomWriteTarget(1, buf);
            cmd.SetRandomWriteTarget(1, nonRandomRt);
        });
        buf.GetData(bOut, 0, 0, 1);
        Value("lifecycle.invalid_rt_over_buf", bOut[0].ToString());

        // Combined RWTexture2D (u1) + unnumbered RWStructuredBuffer (auto u2)
        buf.SetData(new uint[] { 0u, 0, 0, 0 });
        Clear2DF(rtA);
        mat.SetVector("_Val4", new Vector4(0.5f, 0.75f, 0.25f, 1.0f));
        mat.SetInt("_UVal", 123);
        DrawFragPass(6, cmd =>
        {
            cmd.SetRandomWriteTarget(1, rtA);
            cmd.SetRandomWriteTarget(2, buf);
        });
        buf.GetData(bOut, 0, 0, 1);
        Value("lifecycle.mixed.tex", F4(Read2DF(rtA)));
        Value("lifecycle.mixed.buf", bOut[0].ToString());

        // Read-modify-write on fragment RWTexture2D<float4>
        Clear2DF(rtA); // sentinel (0.125, 0.25, 0.375, 0.5)
        mat.SetVector("_Val4", new Vector4(0.25f, 0.5f, 0.125f, 0.25f));
        DrawFragPass(7, cmd => cmd.SetRandomWriteTarget(1, rtA));
        Value("lifecycle.rmw", F4(Read2DF(rtA)));

        // 4. Vertex switch with default falling through into case 0
        for (int axis = 0; axis < 3; axis++)
        {
            Clear2DF(rtA);
            mat.SetInt("_Axis", axis);
            DrawFragPass(8, cmd => cmd.SetRandomWriteTarget(1, rtA));
            Value($"switch.axis{axis}", F4(Read2DF(rtA)));
        }

        // 5. Tightly packed StructuredBuffer structs (trailing pad, mixed vec4, nested struct, aligned)
        TestPackedStructs();
    }

    [StructLayout(LayoutKind.Sequential)]
    struct TrailingPad { public Vector4 a; public float b; }
    [StructLayout(LayoutKind.Sequential)]
    struct MixedVec4 { public Vector3 p; public Vector4 a; public float s; public Vector4 b; }
    [StructLayout(LayoutKind.Sequential)]
    struct Inner { public Vector4 v; public float k; }
    [StructLayout(LayoutKind.Sequential)]
    struct Outer { public Inner inner; public float tail; }
    [StructLayout(LayoutKind.Sequential)]
    struct Aligned { public Vector4 x; }

    void TestPackedStructs()
    {
        using var bTrailing = new ComputeBuffer(2, Marshal.SizeOf<TrailingPad>());
        using var bMixed = new ComputeBuffer(2, Marshal.SizeOf<MixedVec4>());
        using var bNested = new ComputeBuffer(2, Marshal.SizeOf<Outer>());
        using var bAligned = new ComputeBuffer(2, Marshal.SizeOf<Aligned>());
        bTrailing.SetData(new[]
        {
            new TrailingPad { a = new Vector4(1, 2, 3, 4), b = 5 },
            new TrailingPad { a = new Vector4(10, 20, 30, 40), b = 50 },
        });
        bMixed.SetData(new[]
        {
            new MixedVec4 { p = new Vector3(1, 2, 3), a = new Vector4(4, 5, 6, 7), s = 8, b = new Vector4(9, 10, 11, 12) },
            new MixedVec4 { p = new Vector3(13, 14, 15), a = new Vector4(16, 17, 18, 19), s = 20, b = new Vector4(21, 22, 23, 24) },
        });
        bNested.SetData(new[]
        {
            new Outer { inner = new Inner { v = new Vector4(1, 2, 3, 4), k = 5 }, tail = 6 },
            new Outer { inner = new Inner { v = new Vector4(7, 8, 9, 10), k = 11 }, tail = 12 },
        });
        bAligned.SetData(new[]
        {
            new Aligned { x = new Vector4(1, 2, 3, 4) },
            new Aligned { x = new Vector4(5, 6, 7, 8) },
        });
        int k = cs.FindKernel("PackedStructs");
        cs.SetBuffer(k, "_Trailing", bTrailing);
        cs.SetBuffer(k, "_Mixed", bMixed);
        cs.SetBuffer(k, "_Nested", bNested);
        cs.SetBuffer(k, "_Aligned", bAligned);
        cs.SetBuffer(k, "_ReadF", readF);
        cs.Dispatch(k, 1, 1, 1);
        var got = new float[7];
        readF.GetData(got, 0, 0, 7);
        Value("packed.structs", F4(got));

        // Write path (emit_pack): whole struct, nested struct, vector field, and scalar field stores
        using var outTrailing = new ComputeBuffer(2, Marshal.SizeOf<TrailingPad>());
        using var outMixed = new ComputeBuffer(2, Marshal.SizeOf<MixedVec4>());
        using var outNested = new ComputeBuffer(2, Marshal.SizeOf<Outer>());
        int kw = cs.FindKernel("PackedStructsWrite");
        cs.SetBuffer(kw, "_OutTrailing", outTrailing);
        cs.SetBuffer(kw, "_OutMixed", outMixed);
        cs.SetBuffer(kw, "_OutNested", outNested);
        cs.Dispatch(kw, 1, 1, 1);
        var wT = new TrailingPad[2];
        var wM = new MixedVec4[2];
        var wN = new Outer[2];
        outTrailing.GetData(wT);
        outMixed.GetData(wM);
        outNested.GetData(wN);
        Value("packed.write.trailing", F4(new[] { wT[0].a.x, wT[0].a.w, wT[0].b, wT[1].a.x, wT[1].a.w, wT[1].b }));
        Value("packed.write.mixed", F4(new[] { wM[0].p.z, wM[0].a.y, wM[0].s, wM[0].b.w, wM[1].p.x, wM[1].a.z, wM[1].s, wM[1].b.y }));
        Value("packed.write.nested", F4(new[] { wN[0].inner.v.x, wN[0].inner.v.w, wN[0].inner.k, wN[0].tail, wN[1].inner.v.x, wN[1].inner.v.w, wN[1].inner.k, wN[1].tail }));
    }
}
