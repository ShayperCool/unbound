using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Renders one small scenario per case — camera clears, renderers with property blocks, blits,
/// command buffers (immediate and at camera events), image effects, GL, DrawMeshNow, queued
/// DrawMesh, half-float targets — reads each result back with <c>Texture2D.ReadPixels</c> and
/// writes every pixel to <c>GRAPHICS_PROBE_OUT</c> (JSON), then quits. The same script runs in
/// the Editor (the reference) and in Unbound; <c>compare.py</c> compares the two files.
/// </summary>
public class GraphicsProbe : MonoBehaviour
{
    const int Size = 8;
    const int ProbeLayer = 30;

    readonly StringBuilder m_Json = new StringBuilder();
    int m_Cases;
    Mesh m_Quad;
    Texture2D m_Gradient;
    Camera m_Camera;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("GraphicsProbe").AddComponent<GraphicsProbe>();

    // Synchronous: the Editor in batch mode never reaches the end of a frame.
    void Start()
    {
        m_Quad = Quad();
        m_Gradient = Gradient();
        var cameraObject = new GameObject("ProbeCamera");
        cameraObject.transform.position = new Vector3(0, 0, -5);
        m_Camera = cameraObject.AddComponent<Camera>();
        m_Camera.enabled = false;
        m_Camera.orthographic = true;
        m_Camera.orthographicSize = 1f;
        m_Camera.cullingMask = 1 << ProbeLayer;
        m_Camera.clearFlags = CameraClearFlags.SolidColor;
        m_Camera.backgroundColor = new Color(0.2f, 0.4f, 0.6f, 1f);

        m_Json.Append("{\n  \"colorSpace\": \"").Append(QualitySettings.activeColorSpace).Append("\",\n  \"cases\": [\n");
        Case("cameraClear", rt =>
        {
            m_Camera.targetTexture = rt;
            m_Camera.Render();
        });
        Case("cameraQuad", rt =>
        {
            var quad = QuadObject(Color("Probe/Color", new Color(1f, 0.25f, 0f, 1f)), new Vector3(0.5f, 0.5f, 0f), 1f);
            m_Camera.targetTexture = rt;
            m_Camera.Render();
            DestroyImmediate(quad);
        });
        // GrabPass: two overlapping quads over four colors. An unnamed grab is taken for each
        // object (the second reads the first's result), a named one once (both read the same).
        Case("grabPass", rt => GrabCase(rt, "Probe/Grab"));
        Case("grabPassNamed", rt => GrabCase(rt, "Probe/GrabNamed"));
        Case("grabPassNamedAgain", rt => GrabCase(rt, "Probe/GrabNamed", 1));
        Case("rendererPropertyBlock", rt =>
        {
            var quad = QuadObject(Color("Probe/Color", UnityEngine.Color.red), Vector3.zero, 1f);
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", new Color(0f, 0.75f, 0.25f, 1f));
            quad.GetComponent<MeshRenderer>().SetPropertyBlock(block);
            m_Camera.targetTexture = rt;
            m_Camera.Render();
            DestroyImmediate(quad);
        });
        Case("blitCopy", rt => Graphics.Blit(m_Gradient, rt));
        Case("blitMaterial", rt => Graphics.Blit(m_Gradient, rt, new Material(Shader.Find("Probe/Invert"))));
        Case("blitPass1", rt => Graphics.Blit(null, rt, new Material(Shader.Find("Probe/TwoPass")), 1));
        Case("blitScaleOffset", rt => Graphics.Blit(m_Gradient, rt, new Vector2(0.5f, 0.5f), new Vector2(0.25f, 0.5f)));
        Case("commandBuffer", rt =>
        {
            var cb = new CommandBuffer { name = "probe" };
            cb.SetRenderTarget(rt);
            cb.ClearRenderTarget(true, true, new Color(1f, 1f, 0f, 1f));
            cb.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            cb.DrawMesh(m_Quad, Matrix4x4.TRS(new Vector3(0.25f, 0f, 0f), Quaternion.identity, new Vector3(1f, 2f, 1f)),
                Color("Probe/Color", new Color(0f, 0f, 1f, 1f)));
            Graphics.ExecuteCommandBuffer(cb);
        });
        Case("commandBufferPropertyBlock", rt =>
        {
            var cb = new CommandBuffer();
            var block = new MaterialPropertyBlock();
            block.SetColor("_Color", new Color(1f, 0f, 1f, 1f));
            cb.SetRenderTarget(rt);
            cb.ClearRenderTarget(true, true, UnityEngine.Color.black);
            cb.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            cb.DrawMesh(m_Quad, Matrix4x4.Scale(new Vector3(2f, 1f, 1f)), Color("Probe/Color", UnityEngine.Color.white), 0, 0, block);
            Graphics.ExecuteCommandBuffer(cb);
        });
        Case("commandBufferGlobal", rt =>
        {
            var cb = new CommandBuffer();
            cb.SetRenderTarget(rt);
            cb.ClearRenderTarget(true, true, UnityEngine.Color.black);
            cb.SetGlobalVector("_ProbeGlobal", new Vector4(0f, 1f, 1f, 1f));
            cb.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            cb.DrawMesh(m_Quad, Matrix4x4.Scale(new Vector3(1f, 2f, 1f)), new Material(Shader.Find("Probe/Global")));
            Graphics.ExecuteCommandBuffer(cb);
        });
        Case("cameraEvent", rt =>
        {
            var cb = new CommandBuffer { name = "after everything" };
            cb.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            cb.DrawMesh(m_Quad, Matrix4x4.TRS(new Vector3(-0.5f, 0f, 0f), Quaternion.identity, new Vector3(1f, 2f, 1f)),
                Color("Probe/Color", new Color(0.5f, 0f, 1f, 1f)));
            m_Camera.AddCommandBuffer(CameraEvent.AfterEverything, cb);
            m_Camera.targetTexture = rt;
            m_Camera.Render();
            m_Camera.RemoveCommandBuffer(CameraEvent.AfterEverything, cb);
        });
        Case("onRenderImage", rt =>
        {
            var effect = m_Camera.gameObject.AddComponent<ProbeImageEffect>();
            effect.material = new Material(Shader.Find("Probe/Invert"));
            m_Camera.targetTexture = rt;
            m_Camera.Render();
            DestroyImmediate(effect);
        });
        Case("gl", rt =>
        {
            RenderTexture.active = rt;
            GL.Clear(true, true, UnityEngine.Color.black);
            new Material(Shader.Find("Probe/VertexColor")).SetPass(0);
            GL.PushMatrix();
            GL.LoadOrtho();
            GL.Begin(GL.QUADS);
            GL.Color(UnityEngine.Color.red); GL.Vertex3(0f, 0f, 0f);
            GL.Color(UnityEngine.Color.green); GL.Vertex3(0f, 0.5f, 0f);
            GL.Color(UnityEngine.Color.blue); GL.Vertex3(0.75f, 0.5f, 0f);
            GL.Color(UnityEngine.Color.white); GL.Vertex3(0.75f, 0f, 0f);
            GL.End();
            GL.PopMatrix();
            RenderTexture.active = null;
        });
        Case("drawMeshNow", rt =>
        {
            RenderTexture.active = rt;
            GL.Clear(true, true, UnityEngine.Color.black);
            GL.PushMatrix();
            GL.LoadProjectionMatrix(Matrix4x4.identity);
            GL.modelview = Matrix4x4.identity;
            Color("Probe/Color", new Color(0.25f, 0.5f, 0.75f, 1f)).SetPass(0);
            Graphics.DrawMeshNow(m_Quad, Matrix4x4.TRS(new Vector3(0f, 0.5f, 0f), Quaternion.identity, new Vector3(2f, 1f, 1f)));
            GL.PopMatrix();
            RenderTexture.active = null;
        });
        Case("drawMeshQueued", rt =>
        {
            Graphics.DrawMesh(m_Quad, Matrix4x4.TRS(new Vector3(0f, -0.5f, 0f), Quaternion.identity, new Vector3(2f, 1f, 1f)),
                Color("Probe/Color", new Color(1f, 0.5f, 0f, 1f)), ProbeLayer, m_Camera);
            m_Camera.targetTexture = rt;
            m_Camera.Render();
        });
        Case("halfFloat", rt => Graphics.Blit(null, rt, new Material(Shader.Find("Probe/Hdr"))), RenderTextureFormat.ARGBHalf);
        Case("readPixelsOffset", rt =>
        {
            Graphics.Blit(m_Gradient, rt);
        }, RenderTextureFormat.ARGB32, true);
        ContentBoundsCase();
        PhysicalCameraCases();

        // Mesh buffers: a quad laid out by SetVertexBufferParams (float positions, UNorm8
        // colors), its vertices written as structs, a 16-bit index buffer, one submesh.
        var bufferQuad = BufferQuad();
        Case("meshVertexBuffer", rt => DrawNow(rt, bufferQuad, new Material(Shader.Find("Probe/VertexColor"))));
        var sub = bufferQuad.GetSubMesh(0);
        Floats("meshLayout", new float[]
        {
            bufferQuad.vertexCount, bufferQuad.vertexAttributeCount, bufferQuad.GetVertexBufferStride(0),
            bufferQuad.GetVertexAttributeOffset(VertexAttribute.Color), (int)bufferQuad.GetVertexAttributeFormat(VertexAttribute.Color),
            bufferQuad.GetVertexAttributeDimension(VertexAttribute.Color), bufferQuad.HasVertexAttribute(VertexAttribute.Normal) ? 1 : 0,
            (int)bufferQuad.indexFormat, sub.indexStart, sub.indexCount, sub.firstVertex, sub.vertexCount,
            sub.bounds.min.x, sub.bounds.min.y, sub.bounds.max.x, sub.bounds.max.y, bufferQuad.bounds.size.x, bufferQuad.bounds.size.y,
        });

        // MeshData: the same quad written through a writable mesh data array, read back through
        // a read-only one.
        var dataQuad = MeshDataQuad();
        Case("meshData", rt => DrawNow(rt, dataQuad, new Material(Shader.Find("Probe/VertexColor"))));
        using (var read = Mesh.AcquireReadOnlyMeshData(dataQuad))
        {
            var data = read[0];
            var positions = new NativeArray<Vector3>(data.vertexCount, Allocator.Temp);
            data.GetVertices(positions);
            var colors = new NativeArray<Color32>(data.vertexCount, Allocator.Temp);
            data.GetColors(colors);
            var indices = new NativeArray<int>(data.GetSubMesh(0).indexCount, Allocator.Temp);
            data.GetIndices(indices, 0);
            var values = new List<float> { read.Length, data.vertexCount, data.vertexBufferCount, data.subMeshCount, (int)data.indexFormat, data.GetVertexBufferStride(0) };
            foreach (var v in positions) { values.Add(v.x); values.Add(v.y); }
            foreach (var col in colors) values.Add(col.g);
            foreach (var i in indices) values.Add(i);
            Floats("meshDataRead", values.ToArray());
        }

        // Instancing: DrawMeshInstanced with per-instance colors, RenderMesh, RenderMeshInstanced,
        // a procedural draw.
        var instanced = new Material(Shader.Find("Probe/Instanced")) { enableInstancing = true };
        instanced.SetColor("_Color", new Color(0.9f, 0.9f, 0.1f, 1f));
        var three = new[]
        {
            Matrix4x4.TRS(new Vector3(-0.6f, 0.5f, 0f), Quaternion.identity, Vector3.one * 0.5f),
            Matrix4x4.TRS(new Vector3(0.6f, 0.5f, 0f), Quaternion.identity, Vector3.one * 0.5f),
            Matrix4x4.TRS(new Vector3(0f, -0.5f, 0f), Quaternion.identity, Vector3.one * 0.5f),
        };
        Case("drawMeshInstanced", rt =>
        {
            Graphics.DrawMeshInstanced(m_Quad, 0, instanced, three, 3, null, ShadowCastingMode.Off, false, ProbeLayer, m_Camera);
            m_Camera.targetTexture = rt;
            m_Camera.Render();
        });
        Case("drawMeshInstancedColors", rt =>
        {
            var block = new MaterialPropertyBlock();
            block.SetVectorArray("_Color", new[] { new Vector4(1f, 0f, 0f, 1f), new Vector4(0f, 1f, 0f, 1f), new Vector4(0f, 0f, 1f, 1f) });
            Graphics.DrawMeshInstanced(m_Quad, 0, instanced, three, 3, block, ShadowCastingMode.Off, false, ProbeLayer, m_Camera);
            m_Camera.targetTexture = rt;
            m_Camera.Render();
        });
        Case("renderMesh", rt =>
        {
            var rp = new RenderParams(Color("Probe/Color", new Color(0.3f, 0.9f, 0.6f, 1f))) { camera = m_Camera, layer = ProbeLayer };
            Graphics.RenderMesh(rp, m_Quad, 0, Matrix4x4.TRS(new Vector3(0.25f, 0.25f, 0f), Quaternion.identity, Vector3.one));
            m_Camera.targetTexture = rt;
            m_Camera.Render();
        });
        Case("renderMeshInstanced", rt =>
        {
            var rp = new RenderParams(instanced) { camera = m_Camera, layer = ProbeLayer };
            Graphics.RenderMeshInstanced(rp, m_Quad, 0, three);
            m_Camera.targetTexture = rt;
            m_Camera.Render();
        });
        Case("drawProcedural", rt =>
        {
            Graphics.DrawProcedural(Color("Probe/Procedural", new Color(0.8f, 0.3f, 0.9f, 1f)), new Bounds(Vector3.zero, Vector3.one * 4f),
                MeshTopology.Triangles, 3, 1, m_Camera, null, ShadowCastingMode.Off, false, ProbeLayer);
            m_Camera.targetTexture = rt;
            m_Camera.Render();
        });

        // Textures: an array's slice, a volume's depth, a region copy, a render target set
        // directly, a texture drawn in pixel coordinates, a readback.
        Case("texture2DArray", rt =>
        {
            var array = new Texture2DArray(2, 2, 3, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int slice = 0; slice < 3; slice++)
                array.SetPixels(new[] { new Color(slice / 2f, 0f, 0f, 1f), new Color(0f, 1f, 0f, 1f), new Color(0f, 0f, 1f, 1f), new Color(1f, 1f, slice / 2f, 1f) }, slice);
            array.Apply();
            var m = new Material(Shader.Find("Probe/Array"));
            m.SetTexture("_Array", array);
            m.SetFloat("_Slice", 1f);
            Graphics.Blit(null, rt, m);
        });
        Case("texture3D", rt =>
        {
            var volume = new Texture3D(2, 2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            var cells = new Color[8];
            for (int i = 0; i < 8; i++) cells[i] = new Color((i & 1), (i >> 1) & 1, (i >> 2) & 1, 1f);
            volume.SetPixels(cells);
            volume.Apply();
            var m = new Material(Shader.Find("Probe/Volume"));
            m.SetTexture("_Volume", volume);
            m.SetFloat("_Depth", 0.75f);
            Graphics.Blit(null, rt, m);
        });
        Case("copyTextureRegion", rt =>
        {
            RenderTexture.active = rt;
            GL.Clear(true, true, UnityEngine.Color.black);
            RenderTexture.active = null;
            Graphics.CopyTexture(m_Gradient, 0, 0, 1, 1, 3, 2, rt, 0, 0, 4, 5);
        });
        Case("setRenderTarget", rt =>
        {
            Graphics.SetRenderTarget(rt);
            GL.Clear(true, true, new Color(0.1f, 0.7f, 0.3f, 1f));
            Graphics.SetRenderTarget(null);
        });
        Case("drawTexture", rt =>
        {
            RenderTexture.active = rt;
            GL.Clear(true, true, UnityEngine.Color.black);
            GL.PushMatrix();
            GL.LoadPixelMatrix(0, Size, 0, Size);
            Graphics.DrawTexture(new Rect(2, 2, 4, 4), m_Gradient);
            GL.PopMatrix();
            RenderTexture.active = null;
        });
        {
            var source = new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32);
            source.Create();
            Graphics.Blit(m_Gradient, source);
            var request = AsyncGPUReadback.Request(source);
            request.WaitForCompletion();
            var values = new List<float> { request.done ? 1 : 0, request.hasError ? 1 : 0, request.width, request.height, request.layerCount };
            if (request.done && !request.hasError)
                foreach (var px in request.GetData<Color32>()) { values.Add(px.r); values.Add(px.g); }
            Floats("asyncReadback", values.ToArray());
            source.Release();
        }

        // Compute: a buffer doubled, a texture painted, array uniforms, a command buffer dispatch.
        var compute = Resources.Load<ComputeShader>("ProbeCompute");
        if (compute != null)
        {
            var values = new ComputeBuffer(8, 4);
            values.SetData(new float[] { 1, 2, 3, 4, 5, 6, 7, 8 });
            int doubleKernel = compute.FindKernel("Double");
            compute.SetBuffer(doubleKernel, "_Values", values);
            compute.SetFloat("_Scale", 1.5f);
            compute.SetInt("_Count", 6);
            compute.Dispatch(doubleKernel, 2, 1, 1);
            var read = new float[8];
            values.GetData(read);
            Floats("computeDouble", read);

            var cb = new CommandBuffer();
            cb.SetComputeFloatParam(compute, "_Scale", 0.5f);
            cb.SetComputeIntParam(compute, "_Count", 8);
            cb.SetComputeBufferParam(compute, doubleKernel, "_Values", values);
            cb.DispatchCompute(compute, doubleKernel, 2, 1, 1);
            Graphics.ExecuteCommandBuffer(cb);
            values.GetData(read);
            Floats("computeCommandBuffer", read);

            int weights = compute.FindKernel("Weights");
            var input = new ComputeBuffer(3, 4);
            input.SetData(new float[] { 0.25f, 0.5f, 0.75f });
            compute.SetVectorArray("_Weights", new[] { new Vector4(1, 2, 3, 4), new Vector4(5, 6, 7, 8), new Vector4(9, 0, 1, 2) });
            compute.SetBuffer(weights, "_Values", values);
            compute.SetBuffer(weights, "_Input", input);
            compute.Dispatch(weights, 1, 1, 1);
            values.GetData(read);
            Floats("computeArrays", read);

            compute.GetKernelThreadGroupSizes(compute.FindKernel("Paint"), out var tx, out var ty, out var tz);
            Floats("computeThreads", new float[] { tx, ty, tz });
            values.Release();
            input.Release();

            Case("computePaint", rt =>
            {
                var target = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGB32) { enableRandomWrite = true };
                target.Create();
                int paint = compute.FindKernel("Paint");
                compute.SetTexture(paint, "_Target", target);
                compute.SetTexture(paint, "_Source", m_Gradient);
                compute.SetVector("_Tint", new Vector4(1f, 0.5f, 1f, 1f));
                compute.Dispatch(paint, 1, 1, 1);
                Graphics.Blit(target, rt);
                target.Release();
            });

            // Indirect arguments written on the GPU: three instances of a procedural triangle.
            Case("indirectComputeArgs", rt =>
            {
                var args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Structured, 4, 4);
                int kernel = compute.FindKernel("Args");
                compute.SetBuffer(kernel, "_Args", args);
                compute.SetInt("_Count", 3);
                compute.Dispatch(kernel, 1, 1, 1);
                var cb = new CommandBuffer();
                cb.SetRenderTarget(rt);
                cb.ClearRenderTarget(true, true, UnityEngine.Color.black);
                cb.DrawProceduralIndirect(Matrix4x4.identity, Color("Probe/InstanceIds", new Color(1f, 0.5f, 0.25f, 1f)), 0, MeshTopology.Triangles, args);
                Graphics.ExecuteCommandBuffer(cb);
                args.Release();
            });
        }
        else Debug.LogError("ProbeCompute.compute is missing from Resources");

        // Several colors at once: the fragment program's two outputs, through render buffers and
        // through a command buffer; render buffers as one target.
        var mrt = new Material(Shader.Find("Probe/MRT"));
        Case("mrtFirst", rt =>
        {
            var other = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGB32);
            other.Create();
            Graphics.SetRenderTarget(new[] { rt.colorBuffer, other.colorBuffer }, rt.depthBuffer);
            GL.Clear(true, true, new Color(0.1f, 0.1f, 0.1f, 1f));
            mrt.SetPass(0);
            Graphics.DrawProceduralNow(MeshTopology.Triangles, 6);
            RenderTexture.active = null;
            other.Release();
        });
        Case("mrtSecond", rt =>
        {
            var other = new RenderTexture(Size, Size, 24, RenderTextureFormat.ARGB32);
            other.Create();
            Graphics.SetRenderTarget(new[] { other.colorBuffer, rt.colorBuffer }, other.depthBuffer);
            GL.Clear(true, true, new Color(0.2f, 0.2f, 0.2f, 1f));
            mrt.SetPass(0);
            Graphics.DrawProceduralNow(MeshTopology.Triangles, 6);
            RenderTexture.active = null;
            other.Release();
        });
        Case("mrtCommandBuffer", rt =>
        {
            var other = new RenderTexture(Size, Size, 0, RenderTextureFormat.ARGB32);
            other.Create();
            var cb = new CommandBuffer();
            cb.SetRenderTarget(new RenderTargetIdentifier[] { other, rt }, rt);
            cb.ClearRenderTarget(true, true, new Color(0.3f, 0f, 0.3f, 1f));
            cb.DrawProcedural(Matrix4x4.identity, mrt, 0, MeshTopology.Triangles, 6);
            Graphics.ExecuteCommandBuffer(cb);
            other.Release();
        });
        Case("renderBufferTarget", rt =>
        {
            Graphics.SetRenderTarget(rt.colorBuffer, rt.depthBuffer);
            GL.Clear(true, true, new Color(0f, 0.6f, 0.2f, 1f));
            mrt.SetPass(0);
            Graphics.DrawProceduralNow(MeshTopology.Triangles, 6);
            RenderTexture.active = null;
        });

        // A cubemap array: two cubes with a colour per face.
        Case("cubemapArray", rt =>
        {
            var cubes = new CubemapArray(2, 2, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
            for (int element = 0; element < 2; element++)
                for (int face = 0; face < 6; face++)
                {
                    var c = new Color(face / 5f, element, (5 - face) / 5f, 1f);
                    cubes.SetPixels(new[] { c, c, c, c }, (CubemapFace)face, element);
                }
            cubes.Apply();
            var m = new Material(Shader.Find("Probe/CubeArray"));
            m.SetTexture("_Cubes", cubes);
            m.SetFloat("_Element", 1f);
            Graphics.Blit(null, rt, m);
        });

        // Instances: a procedural draw's, and indirect draws' counts from a buffer.
        Case("proceduralInstanced", rt =>
        {
            Graphics.SetRenderTarget(rt);
            GL.Clear(true, true, UnityEngine.Color.black);
            Color("Probe/InstanceIds", new Color(0.2f, 1f, 0.6f, 1f)).SetPass(0);
            Graphics.DrawProceduralNow(MeshTopology.Triangles, 3, 3);
            RenderTexture.active = null;
        });
        Case("proceduralIndirect", rt =>
        {
            var args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, 16);
            args.SetData(new uint[] { 3, 2, 0, 0 });
            var cb = new CommandBuffer();
            cb.SetRenderTarget(rt);
            cb.ClearRenderTarget(true, true, UnityEngine.Color.black);
            cb.DrawProceduralIndirect(Matrix4x4.identity, Color("Probe/InstanceIds", new Color(1f, 1f, 0f, 1f)), 0, MeshTopology.Triangles, args);
            Graphics.ExecuteCommandBuffer(cb);
            args.Release();
        });
        Case("meshInstancedIndirect", rt =>
        {
            var quad = Resources.GetBuiltinResource<Mesh>("Quad.fbx");
            var args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, 1, 20);
            args.SetData(new uint[] { quad.GetIndexCount(0), 3, 0, 0, 0 });
            var cb = new CommandBuffer();
            cb.SetRenderTarget(rt);
            cb.ClearRenderTarget(true, true, UnityEngine.Color.black);
            cb.DrawMeshInstancedIndirect(quad, 0, Color("Probe/InstanceIds", new Color(0f, 1f, 1f, 0f)), 0, args);
            Graphics.ExecuteCommandBuffer(cb);
            args.Release();
        });

        // Temporaries: Unity hands a released one back for the same request.
        var a = RenderTexture.GetTemporary(16, 16, 0);
        RenderTexture.ReleaseTemporary(a);
        var b = RenderTexture.GetTemporary(16, 16, 0);
        var c = RenderTexture.GetTemporary(16, 16, 0);
        Value("temporaryReused", a == b);
        Value("temporarySecondDistinct", b != c);
        Value("temporaryCreated", a.IsCreated());
        RenderTexture.ReleaseTemporary(b);
        RenderTexture.ReleaseTemporary(c);

        m_Json.Append("\n  ]\n}\n");
        var output = Environment.GetEnvironmentVariable("GRAPHICS_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) Debug.Log(m_Json.ToString());
        else
        {
            File.WriteAllText(output, m_Json.ToString());
            Debug.Log("graphics probe written to " + output);
        }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    /// <summary>Runs a case into a fresh 8x8 target and records its pixels, read back.</summary>
    void Case(string name, Action<RenderTexture> draw, RenderTextureFormat format = RenderTextureFormat.ARGB32, bool offset = false)
    {
        var rt = new RenderTexture(Size, Size, 24, format);
        rt.filterMode = FilterMode.Point;
        rt.Create();
        m_Camera.targetTexture = null;
        try { draw(rt); }
        catch (Exception e) { Debug.LogException(e); }
        m_Camera.targetTexture = null;
        bool half = format == RenderTextureFormat.ARGBHalf;
        var texture = new Texture2D(Size, Size, half ? TextureFormat.RGBAHalf : TextureFormat.RGBA32, false);
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        if (offset)
        {
            var white = new Color[Size * Size];
            for (int i = 0; i < white.Length; i++) white[i] = UnityEngine.Color.white;
            texture.SetPixels(white);
            texture.ReadPixels(new Rect(2, 1, 4, 5), 3, 2);
        }
        else texture.ReadPixels(new Rect(0, 0, Size, Size), 0, 0);
        texture.Apply();
        RenderTexture.active = previous;
        rt.Release();

        Separator();
        m_Json.Append("    {\"name\": \"").Append(name).Append("\", \"pixels\": [");
        var pixels = texture.GetPixels();
        for (int i = 0; i < pixels.Length; i++)
        {
            if (i > 0) m_Json.Append(", ");
            var p = pixels[i];
            m_Json.Append('[').Append(F(p.r)).Append(", ").Append(F(p.g)).Append(", ").Append(F(p.b)).Append(", ").Append(F(p.a)).Append(']');
        }
        m_Json.Append("]}");
    }

    /// <summary>Scans a non-square target for the rows and columns holding alpha the way a
    /// texture-cropping helper does: one material, its loop bound set before each blit, blitted
    /// into a one-pixel-high and a one-pixel-wide temporary and read back.</summary>
    void ContentBoundsCase()
    {
        const int width = 12, height = 20;
        var content = new Texture2D(width, height, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < height; y++)
            for (int x = 0; x < width; x++)
                content.SetPixel(x, y, new Color(1f, 1f, 1f, x >= 3 && x <= 7 && y >= 5 && y <= 14 ? 1f : 0f));
        content.Apply();
        var source = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32);
        Graphics.Blit(content, source);
        var material = new Material(Shader.Find("Probe/ContentBounds"));

        var columns = RenderTexture.GetTemporary(width, 1, 0);
        material.SetFloat("_scanPixels", height / 2);
        Graphics.Blit(source, columns, material, 1);
        var columnAlpha = ReadAlpha(columns, width, 1);
        RenderTexture.ReleaseTemporary(columns);

        var rows = RenderTexture.GetTemporary(1, height, 0);
        material.SetFloat("_scanPixels", width / 2);
        Graphics.Blit(source, rows, material, 0);
        var rowAlpha = ReadAlpha(rows, 1, height);
        RenderTexture.ReleaseTemporary(rows);
        RenderTexture.ReleaseTemporary(source);

        Floats("contentBoundsColumns", columnAlpha);
        Floats("contentBoundsRows", rowAlpha);
    }

    /// <summary>Physical camera projections: every gate fit for a square, a wide and a tall
    /// target (sensor 36.5x24, focal length 221.6, a lens shift), the fitted field of view and
    /// lens shift, the field of view and focal length kept in step, and one render through it.</summary>
    void PhysicalCameraCases()
    {
        var cameraObject = new GameObject("PhysicalCamera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.usePhysicalProperties = true;
        camera.sensorSize = new Vector2(36.5f, 24f);
        camera.focalLength = 221.57365f;
        camera.lensShift = new Vector2(0.1f, -0.59f);
        camera.nearClipPlane = 0.3f;
        camera.farClipPlane = 20f;
        Floats("physicalFieldOfView", new[] { camera.fieldOfView, camera.focalLength });
        foreach (var size in new[] { new Vector2Int(8, 8), new Vector2Int(316, 312), new Vector2Int(10, 30), new Vector2Int(40, 10) })
        {
            var target = new RenderTexture(size.x, size.y, 16);
            camera.targetTexture = target;
            foreach (Camera.GateFitMode mode in new[] { Camera.GateFitMode.None, Camera.GateFitMode.Vertical, Camera.GateFitMode.Horizontal, Camera.GateFitMode.Fill, Camera.GateFitMode.Overscan })
            {
                camera.gateFit = mode;
                var values = new List<float>();
                var m = camera.projectionMatrix;
                for (int i = 0; i < 16; i++) values.Add(m[i]);
                values.Add(camera.fieldOfView);
                values.Add(camera.GetGateFittedFieldOfView());
                var shift = camera.GetGateFittedLensShift();
                values.Add(shift.x);
                values.Add(shift.y);
                Floats($"physicalProjection{size.x}x{size.y}{mode}", values.ToArray());
            }
            camera.targetTexture = null;
            target.Release();
        }
        Camera.CalculateProjectionMatrixFromPhysicalProperties(out var calculated, 50f, new Vector2(36f, 24f), new Vector2(0.2f, 0.1f), 0.5f, 100f,
            new Camera.GateFitParameters(Camera.GateFitMode.Fill, 2f));
        var calculatedValues = new float[16];
        for (int i = 0; i < 16; i++) calculatedValues[i] = calculated[i];
        Floats("physicalCalculatedProjection", calculatedValues);

        // The field of view and focal length follow each other through the sensor height.
        camera.fieldOfView = 40f;
        float focalFromFov = camera.focalLength;
        camera.sensorSize = new Vector2(36f, 30f);
        Floats("physicalFovFocal", new[] { focalFromFov, camera.fieldOfView, camera.focalLength });

        // Without physical properties the field of view stays as it was.
        camera.usePhysicalProperties = false;
        camera.targetTexture = new RenderTexture(8, 8, 16);
        var plain = camera.projectionMatrix;
        var plainValues = new float[17];
        for (int i = 0; i < 16; i++) plainValues[i] = plain[i];
        plainValues[16] = camera.fieldOfView;
        Floats("physicalOff", plainValues);

        // A camera without physical properties: what its focal length, sensor and field of view
        // do to each other, and what turning the properties on keeps.
        camera.fieldOfView = 60f;
        var steps = new List<float> { camera.focalLength };
        camera.focalLength = 100f;
        steps.Add(camera.fieldOfView);
        camera.sensorSize = new Vector2(36f, 24f);
        steps.Add(camera.fieldOfView);
        steps.Add(camera.focalLength);
        camera.fieldOfView = 30f;
        steps.Add(camera.focalLength);
        camera.usePhysicalProperties = true;
        steps.Add(camera.fieldOfView);
        steps.Add(camera.focalLength);
        Floats("physicalSync", steps.ToArray());
        DestroyImmediate(cameraObject);

        Case("physicalCameraRender", rt =>
        {
            var quad = QuadObject(Color("Probe/Color", new Color(1f, 0.5f, 0f, 1f)), Vector3.zero, 1f);
            var renderObject = new GameObject("PhysicalRenderCamera");
            renderObject.transform.position = new Vector3(0, 0, -5);
            var renderCamera = renderObject.AddComponent<Camera>();
            renderCamera.enabled = false;
            renderCamera.cullingMask = 1 << ProbeLayer;
            renderCamera.clearFlags = CameraClearFlags.SolidColor;
            renderCamera.backgroundColor = new Color(0.2f, 0.4f, 0.6f, 1f);
            renderCamera.usePhysicalProperties = true;
            renderCamera.sensorSize = new Vector2(36f, 24f);
            renderCamera.focalLength = 120f;
            renderCamera.gateFit = Camera.GateFitMode.Horizontal;
            renderCamera.lensShift = new Vector2(0.25f, -0.25f);
            renderCamera.targetTexture = rt;
            renderCamera.Render();
            renderCamera.targetTexture = null;
            DestroyImmediate(renderObject);
            DestroyImmediate(quad);
        });
    }

    static float[] ReadAlpha(RenderTexture rt, int width, int height)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = rt;
        var texture = new Texture2D(width, height, TextureFormat.ARGB32, false);
        texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        texture.Apply();
        RenderTexture.active = previous;
        var pixels = texture.GetPixels32();
        var alpha = new float[pixels.Length];
        for (int i = 0; i < pixels.Length; i++) alpha[i] = pixels[i].a / 255f;
        return alpha;
    }

    void Floats(string name, float[] values)
    {
        Separator();
        m_Json.Append("    {\"name\": \"").Append(name).Append("\", \"floats\": [");
        for (int i = 0; i < values.Length; i++) m_Json.Append(i > 0 ? ", " : "").Append(F(values[i]));
        m_Json.Append("]}");
    }

    void Value(string name, bool value)
    {
        Separator();
        m_Json.Append("    {\"name\": \"").Append(name).Append("\", \"value\": ").Append(value ? "true" : "false").Append('}');
    }

    void Separator()
    {
        if (m_Cases++ > 0) m_Json.Append(",\n");
    }

    static string F(float v) => v.ToString("0.####", CultureInfo.InvariantCulture);

    static Material Color(string shader, Color color)
    {
        var material = new Material(Shader.Find(shader));
        material.SetColor("_Color", color);
        return material;
    }

    GameObject QuadObject(Material material, Vector3 position, float size)
    {
        var go = new GameObject("ProbeQuad");
        go.layer = ProbeLayer;
        go.transform.position = position;
        go.transform.localScale = new Vector3(size, size, 1f);
        go.AddComponent<MeshFilter>().sharedMesh = m_Quad;
        go.AddComponent<MeshRenderer>().sharedMaterial = material;
        return go;
    }

    void GrabCase(RenderTexture rt, string shader, int shift = 0)
    {
        var colors = new[] { new Color(1f, 0f, 0f, 1f), new Color(0f, 1f, 0f, 1f), new Color(0f, 0f, 1f, 1f), new Color(1f, 1f, 0f, 1f) };
        var objects = new List<GameObject>();
        for (int q = 0; q < 4; q++)
            objects.Add(QuadObject(Color("Probe/Color", colors[(q + shift) % 4]),
                new Vector3(q % 2 == 0 ? -0.5f : 0.5f, q < 2 ? 0.5f : -0.5f, 0f), 1f));
        var grab = new Material(Shader.Find(shader));
        // The farther quad draws first.
        objects.Add(QuadObject(grab, new Vector3(0f, 0.25f, 0.1f), 1f));
        objects.Add(QuadObject(grab, new Vector3(0.25f, -0.25f, 0f), 1f));
        m_Camera.targetTexture = rt;
        m_Camera.Render();
        foreach (var o in objects) DestroyImmediate(o);
    }

    void DrawNow(RenderTexture rt, Mesh mesh, Material material)
    {
        RenderTexture.active = rt;
        GL.Clear(true, true, UnityEngine.Color.black);
        GL.PushMatrix();
        GL.LoadProjectionMatrix(Matrix4x4.identity);
        GL.modelview = Matrix4x4.identity;
        material.SetPass(0);
        Graphics.DrawMeshNow(mesh, Matrix4x4.identity);
        GL.PopMatrix();
        RenderTexture.active = null;
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    struct ColoredVertex
    {
        public Vector3 position;
        public Color32 color;
        public ColoredVertex(float x, float y, Color32 c) { position = new Vector3(x, y, 0f); color = c; }
    }

    static readonly ColoredVertex[] s_QuadVertices =
    {
        new ColoredVertex(-0.75f, -0.75f, new Color32(255, 0, 0, 255)),
        new ColoredVertex(-0.75f, 0.5f, new Color32(0, 255, 0, 255)),
        new ColoredVertex(0.5f, 0.5f, new Color32(0, 0, 255, 255)),
        new ColoredVertex(0.5f, -0.75f, new Color32(255, 255, 255, 255)),
    };
    static readonly ushort[] s_QuadIndices = { 0, 1, 2, 0, 2, 3 };
    static readonly VertexAttributeDescriptor[] s_ColoredLayout =
    {
        new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
        new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
    };

    static Mesh BufferQuad()
    {
        var mesh = new Mesh();
        mesh.SetVertexBufferParams(4, s_ColoredLayout);
        mesh.SetVertexBufferData(s_QuadVertices, 0, 0, 4);
        mesh.SetIndexBufferParams(6, IndexFormat.UInt16);
        mesh.SetIndexBufferData(s_QuadIndices, 0, 0, 6);
        mesh.subMeshCount = 1;
        mesh.SetSubMesh(0, new SubMeshDescriptor(0, 6));
        return mesh;
    }

    static Mesh MeshDataQuad()
    {
        var array = Mesh.AllocateWritableMeshData(1);
        var data = array[0];
        data.SetVertexBufferParams(4, s_ColoredLayout);
        var vertices = data.GetVertexData<ColoredVertex>();
        for (int i = 0; i < 4; i++) vertices[i] = s_QuadVertices[i];
        data.SetIndexBufferParams(6, IndexFormat.UInt16);
        var indices = data.GetIndexData<ushort>();
        for (int i = 0; i < 6; i++) indices[i] = s_QuadIndices[i];
        data.subMeshCount = 1;
        data.SetSubMesh(0, new SubMeshDescriptor(0, 6));
        var mesh = new Mesh();
        Mesh.ApplyAndDisposeWritableMeshData(array, mesh);
        mesh.RecalculateBounds();
        return mesh;
    }

    static Mesh Quad()
    {
        var mesh = new Mesh();
        mesh.vertices = new[] { new Vector3(-0.5f, -0.5f, 0), new Vector3(-0.5f, 0.5f, 0), new Vector3(0.5f, 0.5f, 0), new Vector3(0.5f, -0.5f, 0) };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(0, 1), new Vector2(1, 1), new Vector2(1, 0) };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        return mesh;
    }

    /// <summary>A 4x4 texture whose texels all differ, point-filtered.</summary>
    static Texture2D Gradient()
    {
        var texture = new Texture2D(4, 4, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point, wrapMode = TextureWrapMode.Clamp };
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 4; x++)
                texture.SetPixel(x, y, new Color(x / 3f, y / 3f, (x + y) / 6f, 1f));
        texture.Apply();
        return texture;
    }
}

/// <summary>An image effect: inverts the camera's image.</summary>
public class ProbeImageEffect : MonoBehaviour
{
    public Material material;

    void OnRenderImage(RenderTexture source, RenderTexture destination) => Graphics.Blit(source, destination, material);
}
