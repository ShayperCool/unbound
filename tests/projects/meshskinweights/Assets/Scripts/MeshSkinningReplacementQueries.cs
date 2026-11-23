using System;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

sealed partial class MeshSkinningProbe
{
    static void CheckReplacementQueries(Action<string, Func<string>> probe)
    {
        foreach (int originalWidth in new[] { 0, 2, 5 })
        foreach (string prime in new[] { "cold", "counts", "flat", "gpu" })
        foreach (string sourceKind in new[] { "fresh", "none", "fixed", "wide" })
        foreach (int width in new[] { 0, 2 })
        foreach (int vertexCount in new[] { 1, 2, 3 })
        {
            var target = TwoVertexSkin(originalWidth == 0 ? 2 : originalWidth);
            if (originalWidth == 0)
            {
                int stream = target.GetVertexAttributeStream(VertexAttribute.BlendWeight);
                int bytes = target.GetVertexBufferStride(stream) * target.vertexCount;
                target.SetVertexBufferData(new byte[bytes], 0, 0, bytes, stream);
            }
            string key = $"replacement.{originalWidth}.{prime}.{sourceKind}.{width}.{vertexCount}";
            if (prime == "gpu") probe(key + ".prime", () =>
            {
                using var buffer = target.GetBoneWeightBuffer(SkinWeights.TwoBones);
                return Words(buffer);
            });
            if (prime == "counts") _ = target.GetBonesPerVertex();
            if (prime == "flat") _ = target.GetAllBoneWeights();

            Mesh source = sourceKind switch
            {
                "fresh" => null,
                "wide" => Make(vertexCount),
                "fixed" => TwoVertexSkin(2),
                _ => TwoVertexSkin(0),
            };
            if (sourceKind == "wide")
            {
                var counts = new byte[vertexCount];
                var weights = new BoneWeight1[vertexCount + 4];
                counts[0] = 5;
                weights[0] = Weight(14, .5f);
                weights[1] = Weight(15, .25f);
                weights[2] = Weight(16, .125f);
                weights[3] = Weight(17, .0625f);
                weights[4] = Weight(18, .0625f);
                for (int vertex = 1; vertex < vertexCount; vertex++)
                {
                    counts[vertex] = 1;
                    weights[vertex + 4] = Weight(19 + vertex, 1);
                }
                Apply(source, counts, weights);
            }

            Mesh clone = null;
            probe(key + ".target", () =>
            {
                var data = source == null ? Mesh.AllocateWritableMeshData(1) : Mesh.AllocateWritableMeshData(source);
                WriteMeshData(data[0], vertexCount, width);
                Mesh.ApplyAndDisposeWritableMeshData(data, target);
                clone = Object.Instantiate(target);
                // A retained count array can report the new vertex count while its initialized
                // data still covers only the original two vertices.
                return (prime is "counts" or "flat") ? Shape(target, 2) : Shape(target);
            });
            probe(key + ".clone", () => Shape(clone));
            Object.DestroyImmediate(clone);
            Object.DestroyImmediate(target);
            Object.DestroyImmediate(source);
        }
        foreach (int originalWidth in new[] { 2, 5 })
        foreach (bool readUnweighted in new[] { false, true })
        {
            var target = TwoVertexSkin(originalWidth);
            _ = target.GetBonesPerVertex();
            var strip = Mesh.AllocateWritableMeshData(1);
            WriteMeshData(strip[0], 2, 0);
            Mesh.ApplyAndDisposeWritableMeshData(strip, target);
            string key = $"replacement.chain.{originalWidth}.{readUnweighted}";
            if (readUnweighted) probe(key + ".unweighted", () => Shape(target));
            var clone = Object.Instantiate(target);
            string Reskin(Mesh mesh)
            {
                var write = Mesh.AllocateWritableMeshData(1);
                WriteMeshData(write[0], 2, 2);
                Mesh.ApplyAndDisposeWritableMeshData(write, mesh);
                return Shape(mesh);
            }
            probe(key + ".target", () => Reskin(target));
            probe(key + ".clone", () => Reskin(clone));
            Object.DestroyImmediate(clone);
            Object.DestroyImmediate(target);
        }
        string WithLegacy(Mesh mesh) => Shape(mesh) + "|legacy=" + string.Join("/",
            Array.ConvertAll(mesh.boneWeights, weight => weight.boneIndex0 + "@" + weight.weight0.ToString("R", Invariant)));

        foreach (int width in new[] { 2, 5 })
        foreach (string prime in new[] { "cold", "typed", "legacy" })
        {
            var target = TwoVertexSkin(width);
            if (prime == "typed") _ = target.GetBonesPerVertex();
            if (prime == "legacy") _ = target.boneWeights;
            RewriteSkinStream(target);
            var clone = Object.Instantiate(target);
            string key = $"replacement.rawClone.{width}.{prime}";
            probe(key + ".source", () => WithLegacy(target));
            probe(key + ".copy", () => WithLegacy(clone));
            Apply(target, new byte[] { 1, 1 }, new[] { Weight(28, 1), Weight(29, 1) });
            probe(key + ".copyAfterSourceWrite", () => WithLegacy(clone));
            Object.DestroyImmediate(clone);
            Object.DestroyImmediate(target);
        }
        foreach (bool warm in new[] { false, true })
        {
            var target = Make(2);
            target.boneWeights = new[] { new BoneWeight { boneIndex0 = 4, weight0 = 1 }, new BoneWeight { boneIndex0 = 6, weight0 = 1 } };
            if (warm) _ = target.GetAllBoneWeights();
            target.vertices = new Vector3[1];
            var clone = Object.Instantiate(target);
            string key = $"replacement.resizedLegacyClone.{warm}";
            probe(key + ".source", () => WithLegacy(target));
            probe(key + ".copy", () => WithLegacy(clone));
            Object.DestroyImmediate(clone);
            Object.DestroyImmediate(target);
        }
    }
}
