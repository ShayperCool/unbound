using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

sealed partial class MeshSkinningProbe
{
    static string FirstStreamVertex(Mesh mesh, int stream)
    {
        using var data = Mesh.AcquireReadOnlyMeshData(mesh);
        return BitConverter.ToString(data[0].GetVertexData<byte>(stream).ToArray(), 0, mesh.GetVertexBufferStride(stream));
    }

    static BoneWeight1[] WideWeights(int lastBone) => new[] {
        Weight(2, .5f), Weight(3, .25f), Weight(4, .125f), Weight(5, .0625f), Weight(lastBone, .0625f),
        Weight(1, 1), Weight(1, 1),
    };

    static Mesh SkinTriangle() => new Mesh
    {
        vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
        triangles = new[] { 0, 1, 2 },
    };

    static void CheckBorrowedUploads(Action<string, Func<string>> probe)
    {
        foreach (bool flat in new[] { false, true })
        {
            var mesh = Make(4);
            Apply(mesh, new byte[] { 1, 1, 1, 1 }, new[] { Weight(2, 1), Weight(7, 1), Weight(13, 1), Weight(17, 1) });
            mesh.SetVertexBufferParams(4, new VertexAttributeDescriptor(VertexAttribute.Position),
                flat ? new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2, 2)
                     : new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4, 2),
                new VertexAttributeDescriptor(VertexAttribute.BlendIndices, VertexAttributeFormat.UInt32, 1, 1));
            var counts = mesh.GetBonesPerVertex();
            var weights = mesh.GetAllBoneWeights();
            string key = "alias." + (flat ? "weights" : "counts");
            probe(key + ".write", () =>
            {
                if (flat) mesh.SetVertexBufferData(weights, 1, 0, 1, 2);
                else mesh.SetVertexBufferData(counts, 0, 0, 4, 2);
                return FirstStreamVertex(mesh, 2);
            });
            probe(key + ".oldCounts", () => counts[0].ToString(Invariant));
            probe(key + ".oldWeights", () => weights[0].boneIndex.ToString(Invariant));
            probe(key + ".refreshed", () => Shape(mesh));
            Object.DestroyImmediate(mesh);
        }
    }

    static void CheckMeshCopies(Action<string, Func<string>> probe)
    {
        Mesh MakeWide(int lastBone)
        {
            var mesh = Make(3);
            Apply(mesh, new byte[] { 5, 1, 1 }, WideWeights(lastBone));
            return mesh;
        }
        string State(Mesh mesh)
        {
            int stream = mesh.GetVertexAttributeStream(VertexAttribute.BlendWeight);
            return Shape(mesh) + "|" + AttributeShape(mesh) + "|" + (stream < 0 ? "-" : FirstStreamVertex(mesh, stream));
        }
        string ApplyCopy(Mesh source, Mesh destination)
        {
            var copy = Mesh.AllocateWritableMeshData(source);
            Mesh.ApplyAndDisposeWritableMeshData(copy, destination);
            return State(destination);
        }
        var source = MakeWide(11);
        RewriteSkinStream(source);
        probe("copy.source", () => State(source));
        var empty = new Mesh();
        probe("copy.toEmpty", () => ApplyCopy(source, empty));
        var target = MakeWide(17);
        probe("copy.toWide", () => ApplyCopy(source, target));
        var fixedMesh = Make(3);
        Apply(fixedMesh, new byte[] { 2, 1, 1 }, new[] { Weight(7, .75f), Weight(9, .25f), Weight(3, 1), Weight(5, 1) });
        probe("copy.fixedIntoWide", () => ApplyCopy(fixedMesh, target));

        var freshTarget = MakeWide(19);
        probe("copy.freshIntoWide", () =>
        {
            var array = Mesh.AllocateWritableMeshData(1);
            var data = array[0];
            data.SetVertexBufferParams(3, fixedMesh.GetVertexAttributes());
            using (var original = Mesh.AcquireReadOnlyMeshData(fixedMesh))
                for (int stream = 0; stream < original[0].vertexBufferCount; stream++)
                    data.GetVertexData<byte>(stream).CopyFrom(original[0].GetVertexData<byte>(stream));
            Mesh.ApplyAndDisposeWritableMeshData(array, freshTarget);
            return State(freshTarget);
        });
        var stripped = new Mesh();
        probe("copy.wideStripLayout", () =>
        {
            var copy = Mesh.AllocateWritableMeshData(source);
            copy[0].SetVertexBufferParams(3, new VertexAttributeDescriptor(VertexAttribute.Position));
            Mesh.ApplyAndDisposeWritableMeshData(copy, stripped);
            return State(stripped);
        });
        foreach (var mesh in new[] { source, empty, target, fixedMesh, freshTarget, stripped })
            Object.DestroyImmediate(mesh);
    }

    static void CheckStoredSkinStreams(Action<string, Func<string>> probe, Action<string, Func<Vector3[]>> positions)
    {
        var root = new GameObject("Saved skin");
        var renderer = root.AddComponent<SkinnedMeshRenderer>();
        var bones = new Transform[8];
        var poses = new Matrix4x4[bones.Length];
        for (int i = 0; i < bones.Length; i++)
        {
            bones[i] = new GameObject("Bone " + i).transform;
            bones[i].SetParent(root.transform, false);
            bones[i].localPosition = new Vector3(i * 40, 0, 0);
            poses[i] = Matrix4x4.identity;
        }
        renderer.bones = bones;
        var baked = new Mesh();
        var initialQuality = QualitySettings.skinWeights;
        Vector3[] Bake(Mesh mesh, SkinWeights quality)
        {
            QualitySettings.skinWeights = quality;
            renderer.sharedMesh = mesh;
            renderer.BakeMesh(baked);
            return baked.vertices;
        }
        Mesh WithBones()
        {
            var mesh = SkinTriangle();
            mesh.bindposes = poses;
            return mesh;
        }
        string State(Mesh mesh, VertexAttribute attribute) =>
            FirstStreamVertex(mesh, mesh.GetVertexAttributeStream(attribute)) + "|" + Shape(mesh);

        try
        {
            var wide = WithBones();
            Apply(wide, new byte[] { 5, 1, 1 }, WideWeights(6));
            probe("stored.wide.before", () =>
            {
                RewriteSkinStream(wide);
                return State(wide, VertexAttribute.BlendWeight);
            });
            positions("stored.wide.beforeBake", () => Bake(wide, SkinWeights.Unlimited));
            Mesh wideReloaded = null;
            probe("stored.wide.reload", () =>
            {
                const string path = "Assets/RawSkin.asset";
                AssetDatabase.CreateAsset(wide, path);
                AssetDatabase.SaveAssets();
                wide.SetVertexBufferData(new ushort[8], 0, 0, 8, wide.GetVertexAttributeStream(VertexAttribute.BlendWeight));
                Resources.UnloadAsset(wide);
                wideReloaded = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                return State(wideReloaded, VertexAttribute.BlendWeight);
            });
            positions("stored.wide.reloadBake", () => Bake(wideReloaded, SkinWeights.Unlimited));

            var dominant = WithBones();
            var values = new[] { Weight(2, .9995f), Weight(3, .0005f), Weight(2, .999f), Weight(3, .001f),
                Weight(2, .9985f), Weight(3, .0015f) };
            Apply(dominant, new byte[] { 2, 2, 2 }, values);
            positions("stored.dominant.before", () => Bake(dominant, SkinWeights.OneBone));
            Mesh fixedReloaded = null;
            positions("stored.dominant.reload", () =>
            {
                const string path = "Assets/DominantSkin.asset";
                AssetDatabase.CreateAsset(dominant, path);
                AssetDatabase.SaveAssets();
                Apply(dominant, new byte[] { 1, 1, 1 }, new[] { Weight(5, 1), Weight(5, 1), Weight(5, 1) });
                Resources.UnloadAsset(dominant);
                fixedReloaded = AssetDatabase.LoadAssetAtPath<Mesh>(path);
                return Bake(fixedReloaded, SkinWeights.OneBone);
            });
            positions("stored.dominant.reloadTwo", () => Bake(fixedReloaded, SkinWeights.TwoBones));
            var legacy = WithBones();
            legacy.boneWeights = new[] {
                new BoneWeight { boneIndex0 = 2, boneIndex1 = 3, weight0 = .9995f, weight1 = .0005f },
                new BoneWeight { boneIndex0 = 2, boneIndex1 = 3, weight0 = .999f, weight1 = .001f },
                new BoneWeight { boneIndex0 = 2, boneIndex1 = 3, weight0 = .9985f, weight1 = .0015f },
            };
            positions("stored.dominant.legacy", () => Bake(legacy, SkinWeights.OneBone));

            var raw = WithBones();
            raw.SetVertexBufferParams(3, new VertexAttributeDescriptor(VertexAttribute.Position),
                new VertexAttributeDescriptor(VertexAttribute.BlendIndices, VertexAttributeFormat.UInt32, 1, 1));
            raw.SetVertexBufferData(new uint[] { 2, 3, 4 }, 0, 0, 3, 1);
            probe("raw.indexOnly", () => State(raw, VertexAttribute.BlendIndices) + "|" +
                string.Join("/", Array.ConvertAll(raw.boneWeights, v => v.boneIndex0 + "@" + v.weight0.ToString("R", Invariant))));
            positions("raw.indexOnly.bake", () => Bake(raw, SkinWeights.Unlimited));
            Object.DestroyImmediate(legacy);
            Object.DestroyImmediate(raw);
        }
        finally
        {
            QualitySettings.skinWeights = initialQuality;
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(baked);
        }
    }

    static void CheckEmptyCachesAndBounds(Action<string, Func<string>> probe)
    {
        foreach (var mode in new[] { "cold", "counts", "flat" })
        {
            var mesh = Make(1);
            Apply(mesh, new byte[] { 2 }, new[] { Weight(2, 0), Weight(3, 0) });
            int stream = mesh.GetVertexAttributeStream(VertexAttribute.BlendWeight);
            int width = mesh.GetVertexBufferStride(stream);
            mesh.SetVertexBufferData(new byte[width], 0, 0, width, stream);
            string key = "zero." + mode;
            if (mode == "counts") probe(key + ".prime", () => Pack(mesh.GetBonesPerVertex(), n => n.ToString(Invariant)));
            if (mode == "flat") probe(key + ".prime", () => Pack(mesh.GetAllBoneWeights(), n => n.boneIndex.ToString(Invariant)));
            probe(key + ".write", () =>
            {
                RewriteSkinStream(mesh);
                return Shape(mesh);
            });
            Object.DestroyImmediate(mesh);
        }

        var fixedMesh = SkinTriangle();
        fixedMesh.vertices = new[] { Vector3.right, Vector3.up, Vector3.forward };
        var poses = new Matrix4x4[12];
        for (int i = 0; i < poses.Length; i++) poses[i] = Matrix4x4.identity;
        fixedMesh.bindposes = poses;
        Apply(fixedMesh, new byte[] { 2, 1, 1 }, new[] { Weight(2, .75f), Weight(3, .25f), Weight(1, 1), Weight(1, 1) });
        _ = fixedMesh.GetAllBoneWeights();
        probe("stored.fixedBounds.write", () => RewriteSkinStream(fixedMesh));
        probe("stored.fixedBounds.save", () =>
        {
            const string file = "RawBounds.asset";
            AssetDatabase.CreateAsset(fixedMesh, "Assets/" + file);
            AssetDatabase.SaveAssets();
            var lines = File.ReadAllLines(Path.Combine(Application.dataPath, file));
            int first = Array.FindIndex(lines, line => line.Trim() == "m_BonesAABB:");
            int last = Array.FindIndex(lines, line => line.Trim() == "m_VariableBoneCountWeights:");
            return string.Join("/", Array.FindAll(lines[first..last], line => line.Contains("m_Min:")));
        });
    }

    static void CheckUnweightedCopies(Action<string, Func<string>> probe)
    {
        var source = Make(2);
        foreach (bool fromMesh in new[] { false, true })
        {
            var target = Make(2);
            Apply(target, new byte[] { 5, 1 }, new[] { Weight(2, .5f), Weight(3, .25f), Weight(4, .125f),
                Weight(5, .0625f), Weight(13, .0625f), Weight(1, 1) });
            probe("copy.unweighted." + (fromMesh ? "snapshot" : "newData"), () =>
            {
                var data = fromMesh ? Mesh.AllocateWritableMeshData(source) : Mesh.AllocateWritableMeshData(1);
                if (!fromMesh)
                {
                    data[0].SetVertexBufferParams(2, new VertexAttributeDescriptor(VertexAttribute.Position));
                    var vertices = data[0].GetVertexData<Vector3>();
                    vertices[0] = Vector3.right;
                    vertices[1] = Vector3.up;
                }
                Mesh.ApplyAndDisposeWritableMeshData(data, target);
                return Shape(target);
            });
            Object.DestroyImmediate(target);
        }
        Object.DestroyImmediate(source);
    }
}
