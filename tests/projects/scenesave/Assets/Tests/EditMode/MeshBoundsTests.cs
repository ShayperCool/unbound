using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Edit mode: a mesh's bounds and sub-mesh bounds after each of its setters (vertices with the same
/// and another count, SetVertices with and without bounds flags, SetVertexBufferData, triangles,
/// SetTriangles and SetIndices with and without calculateBounds, SetSubMesh with and without
/// DontRecalculateBounds, assigned bounds, Clear, UploadMeshData) and for half-float positions.
/// Rows only: each is the mesh's bounds, then each sub-mesh's bounds and vertex range.
/// </summary>
public class MeshBoundsTests
{
    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

    static string V(Vector3 v) => F(v.x) + "," + F(v.y) + "," + F(v.z);

    static string B(Mesh mesh)
    {
        var text = V(mesh.bounds.center) + "/" + V(mesh.bounds.extents);
        for (int i = 0; i < mesh.subMeshCount; i++)
        {
            var s = mesh.GetSubMesh(i);
            text += " | " + V(s.bounds.center) + "/" + V(s.bounds.extents) + " v" + s.firstVertex + "+" + s.vertexCount;
        }
        return text;
    }

    static Vector3[] Tri(float scale) => new[] { Vector3.zero, Vector3.right * scale, Vector3.up * scale };

    static void Row(string key, Mesh mesh) => ProbeLog.Row(key, B(mesh));

    [Test]
    public void MeshBounds()
    {
        var mesh = new Mesh();
        mesh.vertices = Tri(1f);
        Row("verticesOnly", mesh);
        mesh.triangles = new[] { 0, 1, 2 };
        Row("triangles", mesh);
        mesh.vertices = Tri(2f);
        Row("verticesSameCount", mesh);
        mesh.vertices = new[] { Vector3.zero, Vector3.right * 3f, Vector3.up * 3f, Vector3.one * 3f };
        Row("verticesMoreCount", mesh);
        mesh.SetVertices(new List<Vector3>(Tri(5f)) { Vector3.one * 5f });
        Row("setVerticesList", mesh);
        using (var moved = new NativeArray<Vector3>(new[] { Vector3.zero, Vector3.right * 6f, Vector3.up * 6f, Vector3.one * 6f }, Allocator.Temp))
        {
            mesh.SetVertices(moved, 0, 4, MeshUpdateFlags.DontRecalculateBounds);
            Row("setVerticesNoBounds", mesh);
        }
        using (var moved = new NativeArray<Vector3>(new[] { Vector3.zero, Vector3.right * 7f, Vector3.up * 7f, Vector3.one * 7f }, Allocator.Temp))
        {
            mesh.SetVertices(moved, 0, 4, MeshUpdateFlags.Default);
            Row("setVerticesDefault", mesh);
        }
        mesh.SetTriangles(new[] { 0, 1, 2 }, 0, false);
        Row("setTrianglesNoBounds", mesh);
        mesh.SetTriangles(new[] { 0, 1, 3 }, 0, true);
        Row("setTrianglesBounds", mesh);
        mesh.vertices = new[] { Vector3.zero, Vector3.right * 8f, Vector3.up * 8f, Vector3.one * 8f };
        mesh.SetIndices(new[] { 0, 1, 2 }, MeshTopology.Triangles, 0, false);
        Row("setIndicesNoBounds", mesh);
        mesh.SetIndices(new[] { 0, 1, 3 }, MeshTopology.Triangles, 0, true);
        Row("setIndicesBounds", mesh);
        mesh.vertices = new[] { Vector3.zero, Vector3.right * 9f, Vector3.up * 9f, Vector3.one * 9f };
        mesh.SetSubMesh(0, new SubMeshDescriptor(0, 3));
        Row("setSubMeshDefault", mesh);
        mesh.SetSubMesh(0, new SubMeshDescriptor(0, 3) { bounds = new Bounds(Vector3.one, Vector3.one), firstVertex = 1, vertexCount = 2 }, MeshUpdateFlags.DontRecalculateBounds);
        Row("setSubMeshNoBounds", mesh);
        mesh.bounds = new Bounds(new Vector3(10f, 10f, 10f), Vector3.one);
        Row("boundsAssigned", mesh);
        mesh.triangles = new[] { 0, 1, 2 };
        Row("boundsAssignedTriangles", mesh);
        mesh.bounds = new Bounds(new Vector3(10f, 10f, 10f), Vector3.one);
        mesh.vertices = new[] { Vector3.zero, Vector3.right * 2f, Vector3.up * 2f, Vector3.one * 2f };
        Row("boundsAssignedVertices", mesh);
        mesh.vertices = new[] { Vector3.zero, Vector3.right * 11f, Vector3.up * 11f, Vector3.one * 11f };
        mesh.UploadMeshData(false);
        Row("upload", mesh);
        mesh.Clear(true);
        Row("clearKeepLayout", mesh);
        mesh.vertices = Tri(12f);
        Row("afterClearVertices", mesh);
        mesh.Clear(false);
        Row("clear", mesh);

        var buffer = new Mesh();
        buffer.SetVertexBufferParams(3, new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3));
        using (var positions = new NativeArray<Vector3>(Tri(2f), Allocator.Temp))
            buffer.SetVertexBufferData(positions, 0, 0, 3);
        Row("vertexBufferData", buffer);
        buffer.SetIndexBufferParams(3, IndexFormat.UInt16);
        using (var indices = new NativeArray<ushort>(new ushort[] { 0, 1, 2 }, Allocator.Temp))
            buffer.SetIndexBufferData(indices, 0, 0, 3);
        buffer.SetSubMesh(0, new SubMeshDescriptor(0, 3));
        Row("vertexBufferSubMesh", buffer);
        using (var positions = new NativeArray<Vector3>(Tri(4f), Allocator.Temp))
            buffer.SetVertexBufferData(positions, 0, 0, 3);
        Row("vertexBufferMoved", buffer);

        var half = new Mesh();
        half.SetVertexBufferParams(3, new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float16, 4));
        var h = new ushort[] { 0, 0, 0, 0x3c00, 0x4000, 0, 0, 0x3c00, 0, 0x4000, 0, 0x3c00 };
        using (var positions = new NativeArray<ushort>(h, Allocator.Temp))
            half.SetVertexBufferData(positions, 0, 0, 12);
        Row("halfData", half);
        half.SetTriangles(new[] { 0, 1, 2 }, 0, true);
        Row("halfSetTriangles", half);
        half.bounds = new Bounds(Vector3.one, Vector3.one);
        half.SetTriangles(new[] { 0, 1, 2 }, 0, true);
        Row("halfAssignedSetTriangles", half);
        half.bounds = new Bounds(Vector3.one, Vector3.one);
        half.triangles = new[] { 0, 1, 2 };
        Row("halfAssignedTriangles", half);
        Object.DestroyImmediate(mesh);
        Object.DestroyImmediate(buffer);
        Object.DestroyImmediate(half);
    }
}
