using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

sealed partial class MeshSkinningProbe
{
    static Mesh TwoVertexSkin(int width)
    {
        var mesh = Make(2);
        if (width == 2)
            Apply(mesh, new byte[] { 2, 1 }, new[] { Weight(7, .75f), Weight(9, .25f), Weight(5, 1) });
        else if (width == 5)
            Apply(mesh, new byte[] { 5, 1 }, new[] { Weight(2, .5f), Weight(3, .25f), Weight(4, .125f),
                Weight(5, .0625f), Weight(13, .0625f), Weight(1, 1) });
        return mesh;
    }

    static void WriteMeshData(Mesh.MeshData data, int count, int width)
    {
        var position = new VertexAttributeDescriptor(VertexAttribute.Position);
        if (width == 0) data.SetVertexBufferParams(count, position);
        else
        {
            var indices = new VertexAttributeDescriptor(VertexAttribute.BlendIndices, VertexAttributeFormat.UInt32, width, 1);
            if (width == 1) data.SetVertexBufferParams(count, position, indices);
            else data.SetVertexBufferParams(count, position,
                new VertexAttributeDescriptor(VertexAttribute.BlendWeight, VertexAttributeFormat.Float32, width, 1), indices);
        }
        var vertices = data.GetVertexData<Vector3>();
        for (int vertex = 0; vertex < count; vertex++) vertices[vertex] = new Vector3(vertex, 1, 0);
        if (width == 0) return;

        var words = data.GetVertexData<uint>(1);
        for (int i = 0; i < words.Length; i++) words[i] = 0;
        uint first = BitConverter.ToUInt32(BitConverter.GetBytes(.75f), 0);
        uint second = BitConverter.ToUInt32(BitConverter.GetBytes(.25f), 0);
        int stride = width == 1 ? 1 : 2 * width;
        for (int vertex = 0; vertex < count; vertex++)
        {
            int start = stride * vertex;
            if (width == 1) words[start] = 7;
            else
            {
                words[start] = first;
                words[start + 1] = second;
                words[start + width] = 8;
                words[start + width + 1] = 9;
            }
        }
    }

    static void CheckEditedCopyLayouts(Action<string, Func<string>> probe)
    {
        foreach (int priorWidth in new[] { 0, 2, 5 })
        {
            var source = TwoVertexSkin(priorWidth);
            foreach (int width in new[] { 0, 1, 2, 4 })
            {
                var target = new Mesh();
                probe("copy.editedLayout." + priorWidth + "." + width, () =>
                {
                    var data = Mesh.AllocateWritableMeshData(source);
                    WriteMeshData(data[0], 2, width);
                    Mesh.ApplyAndDisposeWritableMeshData(data, target);
                    return Shape(target) + "|" + AttributeShape(target);
                });
                Object.DestroyImmediate(target);
            }
            Object.DestroyImmediate(source);
        }
    }

    static void CheckCopyQueryCaches(Action<string, Func<string>> probe)
    {
        var fixedMesh = TwoVertexSkin(2);
        foreach (string prime in new[] { "cold", "gpu", "counts", "flat" })
        {
            var target = TwoVertexSkin(5);
            string key = "copy.query." + prime;
            if (prime == "counts") _ = target.GetBonesPerVertex();
            if (prime == "flat") _ = target.GetAllBoneWeights();
            if (prime == "gpu") probe(key + ".before", () =>
            {
                using var buffer = target.GetBoneWeightBuffer(SkinWeights.TwoBones);
                return Words(buffer);
            });
            probe(key + ".after", () =>
            {
                Mesh.ApplyAndDisposeWritableMeshData(Mesh.AllocateWritableMeshData(fixedMesh), target);
                using var buffer = target.GetBoneWeightBuffer(SkinWeights.TwoBones);
                return Shape(target) + "|" + Words(buffer);
            });
            if (prime is "cold" or "counts")
            {
                string name = "CopyCache-" + prime + ".asset";
                string path = "Assets/" + name;
                probe(key + ".save", () =>
                {
                    AssetDatabase.CreateAsset(target, path);
                    AssetDatabase.SaveAssets();
                    var lines = File.ReadAllLines(Path.Combine(Application.dataPath, name));
                    int at = Array.FindIndex(lines, line => line.Trim() == "m_VariableBoneCountWeights:");
                    return lines[at + 1].Trim();
                });
                probe(key + ".reload", () =>
                {
                    Apply(target, new byte[] { 1, 1 }, new[] { Weight(28, 1), Weight(29, 1) });
                    Resources.UnloadAsset(target);
                    return Shape(AssetDatabase.LoadAssetAtPath<Mesh>(path));
                });
            }
            else Object.DestroyImmediate(target);
        }
        Object.DestroyImmediate(fixedMesh);
    }

    static void CheckCopiedGpuBuffers(Action<string, Func<string>> probe)
    {
        foreach (bool fromMesh in new[] { false, true })
        foreach (int width in new[] { 0, 1 })
        {
            var source = new Mesh();
            var target = TwoVertexSkin(5);
            using var existing = target.GetBoneWeightBuffer(SkinWeights.TwoBones);
            string key = "copy.gpuResize." + (fromMesh ? "snapshot" : "newData") + "." + width;
            probe(key + ".apply", () =>
            {
                var data = fromMesh ? Mesh.AllocateWritableMeshData(source) : Mesh.AllocateWritableMeshData(1);
                WriteMeshData(data[0], 3, width);
                Mesh.ApplyAndDisposeWritableMeshData(data, target);
                return Shape(target);
            });
            probe(key + ".existing", () => Words(existing));
            probe(key + ".requested", () =>
            {
                using var buffer = target.GetBoneWeightBuffer(SkinWeights.TwoBones);
                return Words(buffer);
            });
            Object.DestroyImmediate(source);
            Object.DestroyImmediate(target);
        }
    }
}
