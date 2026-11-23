using System;
using System.IO;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

sealed partial class MeshSkinningProbe
{
    sealed class ThrowingLogHandler : ILogHandler
    {
        public int Calls;
        public void LogFormat(LogType type, Object context, string format, params object[] args)
        {
            Calls++;
            throw new InvalidOperationException("handler failed");
        }
        public void LogException(Exception exception, Object context)
        {
            Calls++;
            throw new InvalidOperationException("handler failed");
        }
    }

    static BoneWeight1[] EqualWeights(int count)
    {
        var weights = new BoneWeight1[count];
        for (int i = 0; i < weights.Length; i++) weights[i] = Weight(i + 2, 1f / count);
        return weights;
    }

    static void CheckRoundingAndDiagnostics(Action<string, Func<string>> probe)
    {
        foreach (int width in new[] { 7, 11, 19 })
        {
            var mesh = Make(1);
            probe("rounding." + width, () =>
            {
                Apply(mesh, new[] { (byte)width }, EqualWeights(width));
                return Shape(mesh);
            });
            probe("rounding." + width + ".self", () =>
            {
                mesh.SetBoneWeights(mesh.GetBonesPerVertex(), mesh.GetAllBoneWeights());
                return Shape(mesh);
            });
            Object.DestroyImmediate(mesh);
        }

        var rejected = Make(1);
        var weights = new[] { Weight(2, .5f), Weight(3, .25f), Weight(4, .125f), Weight(5, .0625f), Weight(6, .0625f) };
        Apply(rejected, new byte[] { 5 }, weights);
        (weights[0], weights[1]) = (weights[1], weights[0]);
        probe("diagnostics.throwingHandler", () =>
        {
            var handler = new ThrowingLogHandler();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = handler;
            try { Apply(rejected, new byte[] { 5 }, weights); }
            finally { Debug.unityLogger.logHandler = previous; }
            return handler.Calls + "|" + Shape(rejected);
        });
        Object.DestroyImmediate(rejected);
    }

    static void CheckBufferLayouts(Action<string, Func<string>> probe)
    {
        var mesh = Make(1);
        Apply(mesh, new byte[] { 1 }, new[] { Weight(5, 1) });
        probe("descriptors.one.before", () => AttributeShape(mesh) + "|" + Shape(mesh));
        probe("descriptors.one.twoRaw", () =>
        {
            mesh.SetVertexBufferParams(1, new VertexAttributeDescriptor(VertexAttribute.Position),
                new VertexAttributeDescriptor(VertexAttribute.BlendWeight, VertexAttributeFormat.Float32, 2, 1),
                new VertexAttributeDescriptor(VertexAttribute.BlendIndices, VertexAttributeFormat.UInt32, 2, 1));
            return mesh.GetVertexBufferStride(1) + "|" + AttributeShape(mesh) + "|" + Shape(mesh);
        });
        Object.DestroyImmediate(mesh);

        foreach (int width in new[] { 2, 5 })
        {
            var sized = Make(1);
            Apply(sized, new[] { (byte)width }, EqualWeights(width));
            foreach (int raw in new[] { -1, 3, 5, 256, int.MaxValue })
                probe("buffer.layoutValue." + width + "." + raw, () =>
                {
                    using var buffer = sized.GetBoneWeightBuffer((SkinWeights)raw);
                    return buffer == null ? "null" : buffer.count + "|" + buffer.stride;
                });
            Object.DestroyImmediate(sized);
        }
    }

    static string RewriteSkinStream(Mesh mesh)
    {
        int stream = mesh.GetVertexAttributeStream(VertexAttribute.BlendWeight);
        byte[] bytes;
        using (var read = Mesh.AcquireReadOnlyMeshData(mesh)) bytes = read[0].GetVertexData<byte>(stream).ToArray();
        int weightOffset = mesh.GetVertexAttributeOffset(VertexAttribute.BlendWeight);
        int indexOffset = mesh.GetVertexAttributeOffset(VertexAttribute.BlendIndices);
        int width = mesh.GetVertexAttributeDimension(VertexAttribute.BlendWeight);
        bool packed = mesh.GetVertexAttributeFormat(VertexAttribute.BlendWeight) == VertexAttributeFormat.UNorm16;
        for (int i = 0; i < width; i++)
        {
            var weight = packed
                ? BitConverter.GetBytes((ushort)(i == 0 ? 16384 : i == 1 ? 49151 : 0))
                : BitConverter.GetBytes(i == 0 ? .25f : i == 1 ? .75f : 0f);
            var index = packed ? BitConverter.GetBytes((ushort)(8 + i)) : BitConverter.GetBytes(8 + i);
            weight.CopyTo(bytes, weightOffset + i * weight.Length);
            index.CopyTo(bytes, indexOffset + i * index.Length);
        }
        mesh.SetVertexBufferData(bytes, 0, 0, mesh.GetVertexBufferStride(stream), stream);
        using var result = Mesh.AcquireReadOnlyMeshData(mesh);
        return BitConverter.ToString(result[0].GetVertexData<byte>(stream).ToArray(), 0, mesh.GetVertexBufferStride(stream));
    }

    static void CheckRawChannels(Action<string, Func<string>> probe, Action<string, Func<Vector3[]>> positions)
    {
        var root = new GameObject("Raw skin");
        var bones = new Transform[12];
        var poses = new Matrix4x4[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            bones[i] = new GameObject("Bone " + i).transform;
            bones[i].SetParent(root.transform, false);
            bones[i].localPosition = new Vector3(i * 2, 0, 0);
            poses[i] = Matrix4x4.identity;
        }
        var renderer = root.AddComponent<SkinnedMeshRenderer>();
        renderer.bones = bones;
        var baked = new Mesh();
        Vector3[] Bake()
        {
            renderer.BakeMesh(baked);
            return baked.vertices;
        }
        var quality = QualitySettings.skinWeights;
        QualitySettings.skinWeights = SkinWeights.Unlimited;
        try
        {
            foreach (int width in new[] { 0, 2, 5 })
            {
                var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                    triangles = new[] { 0, 1, 2 }, bindposes = poses };
                if (width == 0)
                    mesh.boneWeights = new[] { new BoneWeight { boneIndex0 = 2, boneIndex1 = 3, weight0 = .75f, weight1 = .25f },
                        new BoneWeight { boneIndex0 = 1, weight0 = 1 }, new BoneWeight { boneIndex0 = 1, weight0 = 1 } };
                else if (width == 2)
                    Apply(mesh, new byte[] { 2, 1, 1 }, new[] { Weight(2, .75f), Weight(3, .25f), Weight(1, 1), Weight(1, 1) });
                else
                    Apply(mesh, new byte[] { 5, 1, 1 }, new[] { Weight(2, .5f), Weight(3, .25f), Weight(4, .125f), Weight(5, .0625f),
                        Weight(6, .0625f), Weight(1, 1), Weight(1, 1) });
                renderer.sharedMesh = mesh;
                string key = "raw." + width;
                positions(key + ".beforeBake", Bake);
                using var buffer = mesh.GetBoneWeightBuffer(SkinWeights.TwoBones);
                probe(key + ".before", () => Shape(mesh) + "|" + Words(buffer));
                var oldCounts = mesh.GetBonesPerVertex();
                var oldWeights = mesh.GetAllBoneWeights();
                probe(key + ".write", () =>
                {
                    string raw = RewriteSkinStream(mesh);
                    using var refreshed = mesh.GetBoneWeightBuffer(SkinWeights.TwoBones);
                    var legacy = mesh.boneWeights[0];
                    return raw + "|" + Shape(mesh) + "|" + legacy.boneIndex0 + "@" + legacy.weight0.ToString("R", Invariant) +
                        "|" + Words(buffer) + "|" + Words(refreshed);
                });
                positions(key + ".afterBake", Bake);
                probe(key + ".staleCounts", () => oldCounts[0].ToString(Invariant));
                probe(key + ".staleWeights", () => oldWeights[0].boneIndex.ToString(Invariant));
                Object.DestroyImmediate(mesh);
            }

            var dominant = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
                triangles = new[] { 0, 1, 2 }, bindposes = poses };
            Apply(dominant, new byte[] { 2, 2, 2 }, new[] { Weight(2, .9995f), Weight(3, .0005f),
                Weight(2, .999f), Weight(3, .001f), Weight(2, .9985f), Weight(3, .0015f) });
            renderer.sharedMesh = dominant;
            QualitySettings.skinWeights = SkinWeights.OneBone;
            positions("bake.dominant.OneBone", Bake);
            QualitySettings.skinWeights = SkinWeights.TwoBones;
            positions("bake.dominant.TwoBones", Bake);
            Object.DestroyImmediate(dominant);
        }
        finally
        {
            QualitySettings.skinWeights = quality;
            Object.DestroyImmediate(baked);
            Object.DestroyImmediate(root);
        }
    }

    static void CheckAssetReload(Action<string, Func<string>> probe)
    {
        foreach (int width in new[] { 1, 2, 5, 11 })
        {
            var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
            var weights = new BoneWeight1[width + (width == 1 ? 2 : 3)];
            float sum = width * (width + 1) / 2f;
            for (int i = 0; i < width; i++) weights[i] = Weight(i + 2, (width - i) / sum);
            weights[width] = Weight(1, 1f);
            weights[width + 1] = Weight(2, width == 1 ? 1f : .75f);
            if (width > 1) weights[width + 2] = Weight(1, .25f);
            byte lastCount = (byte)(width == 1 ? 1 : 2);
            Apply(mesh, new[] { (byte)width, (byte)1, lastCount }, weights);
            string key = "asset." + width;
            string file = "SkinWeights" + width + ".asset";
            string path = "Assets/" + file;
            probe(key + ".before", () => Shape(mesh) + "|" + AttributeShape(mesh));
            probe(key + ".save", () =>
            {
                AssetDatabase.CreateAsset(mesh, path);
                AssetDatabase.SaveAssets();
                var lines = File.ReadAllLines(Path.Combine(Application.dataPath, file));
                int at = Array.FindIndex(lines, line => line.Trim() == "m_VariableBoneCountWeights:");
                return at >= 0 ? lines[at + 1].Trim() : "absent";
            });
            probe(key + ".reload", () =>
            {
                Apply(mesh, new byte[] { 1, 1, 1 }, new[] { Weight(12, 1), Weight(13, 1), Weight(14, 1) });
                Resources.UnloadAsset(mesh);
                var loaded = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                return Shape(loaded) + "|" + AttributeShape(loaded);
            });
        }
    }
}
