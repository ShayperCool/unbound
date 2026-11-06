using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Rendering;

public partial class CmdBuffersProbe
{
    IEnumerator FrameProbes()
    {
        var indexed = RenderIndexedFrames();
        while (indexed.MoveNext()) yield return indexed.Current;
        var fences = Fences();
        while (fences.MoveNext()) yield return fences.Current;
    }

    IEnumerator RenderIndexedFrames()
    {
        var grid = Keep(new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
        grid.Create();
        var go = Keep(new GameObject("IndexedCamera"));
        var cam = go.AddComponent<Camera>();
        cam.transform.position = new Vector3(0, 0, -10);
        cam.enabled = false;
        cam.orthographic = true;
        cam.orthographicSize = 2;
        cam.clearFlags = CameraClearFlags.SolidColor;
        cam.backgroundColor = Color.clear;
        cam.targetTexture = grid;
        cam.allowHDR = false;
        cam.allowMSAA = false;
        var mat = Keep(new Material(Resources.Load<Shader>("IndexedGrid")));
        mat.SetFloat("_Tag", 10);
        var indirectMat = Keep(new Material(Resources.Load<Shader>("IndirectGrid")));
        GraphicsBuffer Buffer<T>(GraphicsBuffer.Target t, int stride, T[] data) where T : struct
        {
            var b = new GraphicsBuffer(t, data.Length, stride);
            disposables.Add(b);
            b.SetData(data);
            return b;
        }
        var words = new uint[] { 0, 1, 10, 0, 10, 9, 11, 12, 21, 11, 21, 20 };
        var idx16 = Buffer(GraphicsBuffer.Target.Index, 2, words.Select(x => (ushort)x).ToArray());
        var idx32 = Buffer(GraphicsBuffer.Target.Index, 4, words.Select(x => x + 81000).ToArray());
        // Exactly the documented Structured sizeof(int) buffer, without Target.Index.
        var structured = Buffer(GraphicsBuffer.Target.Structured, sizeof(int), words.Select(x => (int)x).ToArray());
        var positions = Buffer(GraphicsBuffer.Target.Structured, 12, Enumerable.Range(0, 81100)
            .Select(v => new Vector3(v % 81 % 9, v % 81 / 9, 0)).ToArray());
        indirectMat.SetBuffer("_Positions", positions);
        GraphicsBuffer.IndirectDrawIndexedArgs Args(uint count, uint instances, uint start, uint vertex, uint instance) =>
            new GraphicsBuffer.IndirectDrawIndexedArgs { indexCountPerInstance = count, instanceCount = instances,
                startIndex = start, baseVertexIndex = vertex, startInstance = instance };
        var commands = new[] { Args(6, 1, 0, 0, 0), Args(6, 2, 6, 9, 3), Args(6, 1, 0, 81001, 2) };
        var args = Buffer(GraphicsBuffer.Target.IndirectArguments, GraphicsBuffer.IndirectDrawIndexedArgs.size, commands);
        var rp = new RenderParams(mat) { camera = cam, worldBounds = new Bounds(Vector3.zero, Vector3.one * 100) };
        var ip = new RenderParams(indirectMat) { camera = cam, worldBounds = rp.worldBounds };
        var cases = new List<(string key, Action draw)>();
        void Case(string key, Action draw) => cases.Add(("render.indexed." + key, draw));
        void Direct(GraphicsBuffer b, int count = 6, int start = 0, int instances = 1) =>
            Graphics.RenderPrimitivesIndexed(rp, MeshTopology.Triangles, b, count, start, instances);
        void Indirect(GraphicsBuffer b, int count = 1, int start = 0) =>
            Graphics.RenderPrimitivesIndexedIndirect(ip, MeshTopology.Triangles, b, args, count, start);
        Case("u16", () => Direct(idx16, 12));
        Case("u16.offset", () => Direct(idx16, 6, 6));
        Case("u16.instances", () => Direct(idx16, 6, 0, 2));
        Case("u32", () => Direct(idx32, 12));
        Case("u32.offset", () => Direct(idx32, 6, 6));
        Case("structured.int", () => Direct(structured));
        Case("indirect.u16", () => Indirect(idx16));
        Case("indirect.u32", () => Indirect(idx32));
        Case("indirect.structured", () => Indirect(structured));
        Case("indirect.multiple", () => Indirect(structured, 2));
        Case("indirect.startcommand", () => Indirect(structured, 1, 1));
        Case("indirect.basevertex", () => Indirect(idx16, 1, 2));
        Case("indirect.rawids", () => Graphics.RenderPrimitivesIndexedIndirect(rp, MeshTopology.Triangles, idx16, args, 1, 1));
        Case("indirect.zero", () => Indirect(idx16, 0));
        Case("zero", () => Direct(idx16, 0));
        Case("instances.zero", () => Direct(idx16, 6, 0, 0));
        Case("arg.index.null", () => Direct(null));
        Case("arg.indirect.index.null", () => Indirect(null));
        Case("arg.args.null", () => Graphics.RenderPrimitivesIndexedIndirect(ip, MeshTopology.Triangles, idx16, null));
        Case("arg.args.target", () => Graphics.RenderPrimitivesIndexedIndirect(ip, MeshTopology.Triangles, idx16, structured));
        Case("arg.material.null", () => Graphics.RenderPrimitivesIndexed(new RenderParams(null), MeshTopology.Triangles, idx16, 6));
        Case("arg.indirect.material.null", () => Graphics.RenderPrimitivesIndexedIndirect(new RenderParams(null), MeshTopology.Triangles, idx16, args));
        var changed = Buffer(GraphicsBuffer.Target.Structured, 4, words);
        Case("write.before", () => { changed.SetData(words.Skip(6).Concat(words.Take(6)).ToArray()); Direct(changed); });
        Case("write.after", () => { Direct(changed); changed.SetData(words); });
        Case("args.write.after", () =>
        {
            Indirect(idx16);
            args.SetData(new[] { Args(6, 1, 6, 9, 0), commands[1], commands[2] });
        });
        // A command buffer writes both buffers on the GPU, after the RenderParams call.
        var gpuIndices = Buffer(GraphicsBuffer.Target.Structured, 4, words);
        var gpuArgs = Buffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, 4, new uint[5]);
        var fill = Resources.Load<ComputeShader>("IndexedFill");
        Case("gpu.write.after", () =>
        {
            Graphics.RenderPrimitivesIndexedIndirect(ip, MeshTopology.Triangles, gpuIndices, gpuArgs);
            Execute(cmd =>
            {
                cmd.SetComputeBufferParam(fill, 0, "_Indices", gpuIndices);
                cmd.SetComputeBufferParam(fill, 0, "_Args", gpuArgs);
                cmd.DispatchCompute(fill, 0, 1, 1, 1);
            });
        });
        foreach (var c in cases)
        {
            Try(c.key, c.draw);
            cam.Render();
            Value(c.key, Read(grid));
            yield return null; // A separate camera frame per queued draw, including in batchmode.
        }
        cam.enabled = false;
    }
}
