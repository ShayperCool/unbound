using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

public partial class CmdBuffersProbe
{
    /// <summary>
    /// Device answers recorded on Metal only (`metal.*`): which bytes CopyTexture copies between the
    /// two 8-bit channel orders (B8G8R8A8 and R8G8B8A8 render textures, BGRA32 and RGBA32 textures,
    /// both directions, a same-order control, an immediate copy and the copied texture's CPU data),
    /// and what SV_VertexID reads where a draw starts at a vertex other than 0: a non-indexed
    /// indirect draw's start vertex (with a start instance), and a mesh draw's base vertex, direct
    /// and indirect, from a program that reads vertex inputs; and which arguments an indirect
    /// dispatch at a byte offset that is not a multiple of 4 reads.
    /// </summary>
    void MetalDeviceAnswers()
    {
        if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Metal) return;
        RenderTexture Rt(GraphicsFormat format)
        {
            var rt = Keep(new RenderTexture(2, 2, format, GraphicsFormat.None));
            rt.Create();
            return rt;
        }
        var source = Keep(Source(2, 2, linear: true));
        void Copy(string key, Texture from, Texture to, bool cpu = false, bool immediate = false)
        {
            Try(key, () =>
            {
                if (from is RenderTexture rt) Execute(cmd => cmd.Blit(source, rt));
                if (immediate) Graphics.CopyTexture(from, to);
                else Execute(cmd => cmd.CopyTexture(from, to));
            });
            Value(key, Read(to));
            if (cpu && to is Texture2D t) Value(key + ".cpu", Hex(t.GetPixelData<byte>(0)));
        }
        Texture2D Plain(TextureFormat format) => Keep(new Texture2D(2, 2, format, false, true));
        Copy("metal.copy.bgrart.rgba32", Rt(GraphicsFormat.B8G8R8A8_UNorm), Plain(TextureFormat.RGBA32));
        Copy("metal.copy.rgbart.bgra32", Rt(GraphicsFormat.R8G8B8A8_UNorm), Plain(TextureFormat.BGRA32));
        Copy("metal.copy.bgrart.bgra32", Rt(GraphicsFormat.B8G8R8A8_UNorm), Plain(TextureFormat.BGRA32));
        Copy("metal.copy.rgbart.bgrart", Rt(GraphicsFormat.R8G8B8A8_UNorm), Rt(GraphicsFormat.B8G8R8A8_UNorm));
        Copy("metal.copy.bgrart.rgba32.now", Rt(GraphicsFormat.B8G8R8A8_UNorm), Plain(TextureFormat.RGBA32), immediate: true);
        Copy("metal.copy.bgra32.rgba32", Keep(Source(2, 2, TextureFormat.BGRA32, linear: true)), Plain(TextureFormat.RGBA32), cpu: true);
        Value("metal.copy.bgrart.read", Read(Rt(GraphicsFormat.B8G8R8A8_UNorm)));

        var grid = Keep(new RenderTexture(8, 8, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear));
        grid.Create();
        void Grid(string key, Action<CommandBuffer> record)
        {
            Execute(cmd => { cmd.SetRenderTarget(grid); cmd.ClearRenderTarget(false, true, Color.clear); });
            Try(key, () => Execute(cmd => { cmd.SetRenderTarget(grid); record(cmd); }));
            Value(key, Read(grid));
        }
        GraphicsBuffer Args(params uint[] words)
        {
            var b = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments, words.Length, 4);
            disposables.Add(b);
            b.SetData(words);
            return b;
        }
        var procedural = Keep(new Material(Resources.Load<Shader>("ProceduralGrid")));
        procedural.SetFloat("_Tag", 50);
        // Vertex count, instances, start vertex, start instance: cell 2 (ids from the start vertex)
        // or 0, rows 1 and 2 (ids from the start instance) or 0 and 1.
        Grid("metal.vertexid.startvertex", cmd => cmd.DrawProceduralIndirect(Matrix4x4.identity, procedural, 0,
            MeshTopology.Triangles, Args(6, 2, 12, 1)));
        var mesh = Keep(new Mesh());
        mesh.vertices = new Vector3[90];
        mesh.SetIndices(new[] { 0, 1, 10, 0, 10, 9 }, MeshTopology.Triangles, 0);
        mesh.SetSubMesh(0, new SubMeshDescriptor(0, 6) { baseVertex = 9 }, MeshUpdateFlags.DontRecalculateBounds);
        mesh.bounds = new Bounds(Vector3.zero, Vector3.one * 100);
        var withInputs = Keep(new Material(Resources.Load<Shader>("VertexIdMesh")));
        withInputs.SetFloat("_Tag", 60);
        // Cell (0, 1) where SV_VertexID adds the base vertex, (0, 0) where it does not.
        Grid("metal.vertexid.mesh.basevertex", cmd => cmd.DrawMesh(mesh, Matrix4x4.identity, withInputs, 0, 0));
        var plainMesh = Keep(new Mesh());
        plainMesh.vertices = new Vector3[90];
        plainMesh.SetIndices(new[] { 0, 1, 10, 0, 10, 9 }, MeshTopology.Triangles, 0);
        plainMesh.bounds = mesh.bounds;
        // Index count, instances, start index, base vertex, start instance.
        Grid("metal.vertexid.mesh.indirect.basevertex", cmd => cmd.DrawMeshInstancedIndirect(plainMesh, 0, withInputs, 0,
            Args(6, 1, 0, 9, 0)));
        // The vertices the base vertex reaches: from vertex 9 on, positions half a clip unit to the
        // right (two pixels), so the cell moves when the attributes come from index + base vertex.
        var moved = Enumerable.Range(0, 90).Select(v => v < 9 ? Vector3.zero : new Vector3(0.5f, 0, 0)).ToArray();
        var fetched = Keep(new Mesh());
        fetched.vertices = moved;
        fetched.SetIndices(new[] { 0, 1, 10, 0, 10, 9 }, MeshTopology.Triangles, 0);
        fetched.SetSubMesh(0, new SubMeshDescriptor(0, 6) { baseVertex = 9 }, MeshUpdateFlags.DontRecalculateBounds);
        fetched.bounds = mesh.bounds;
        Grid("metal.vertexid.mesh.basevertex.fetch", cmd => cmd.DrawMesh(fetched, Matrix4x4.identity, withInputs, 0, 0));
        var fetchedPlain = Keep(new Mesh());
        fetchedPlain.vertices = moved;
        fetchedPlain.SetIndices(new[] { 0, 1, 10, 0, 10, 9 }, MeshTopology.Triangles, 0);
        fetchedPlain.bounds = mesh.bounds;
        Grid("metal.vertexid.mesh.indirect.basevertex.fetch", cmd => cmd.DrawMeshInstancedIndirect(fetchedPlain, 0, withInputs, 0,
            Args(6, 1, 0, 9, 0)));

        // Offsets 2 and 14 of { 2, 1, 1, 3, 2, 2 }: rounded down to 0 and 12 (2 and 12 groups), read
        // from the start (2 groups each), or nothing dispatched.
        var groups = Resources.Load<ComputeShader>("MetalGroups");
        var dispatchArgs = new ComputeBuffer(6, 4, ComputeBufferType.IndirectArguments);
        disposables.Add(dispatchArgs);
        dispatchArgs.SetData(new uint[] { 2, 1, 1, 3, 2, 2 });
        foreach (uint offset in new uint[] { 2, 14 })
        {
            var output = new ComputeBuffer(40, 4);
            disposables.Add(output);
            output.SetData(new uint[40]);
            string key = "metal.dispatch.offset" + offset;
            Try(key, () =>
            {
                groups.SetBuffer(0, "_Out", output);
                groups.DispatchIndirect(0, dispatchArgs, offset);
            });
            var words = new uint[40];
            output.GetData(words);
            Value(key, U(words));
        }
    }
}
