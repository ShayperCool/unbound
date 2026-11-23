using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

sealed class MeshSkinningProbe : MonoBehaviour
{
    [Serializable] sealed class Row { public string k, v; }
    [Serializable] sealed class PositionRow { public string k; public Vector3[] v; }
    static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    static Mesh Make(int count) => new Mesh { vertices = new Vector3[count] };
    static BoneWeight1 Weight(int index, float weight) => new BoneWeight1 { boneIndex = index, weight = weight };

    static string Pack<T>(NativeArray<T> array, Func<T, string> format) where T : struct
    {
        var rows = new string[array.Length];
        for (int i = 0; i < rows.Length; i++) rows[i] = format(array[i]);
        return (array.IsCreated ? "created:" : "default:") + string.Join("/", rows);
    }

    static string Shape(Mesh mesh)
    {
        string counts = Pack(mesh.GetBonesPerVertex(), n => n.ToString(Invariant));
        string weights = Pack(mesh.GetAllBoneWeights(), n => n.boneIndex + "@" + n.weight.ToString("R", Invariant));
        return mesh.skinWeightBufferLayout + ":" + mesh.HasVertexAttribute(VertexAttribute.BlendWeight) + ":" +
            mesh.HasVertexAttribute(VertexAttribute.BlendIndices) + ":" + counts + ":" + weights;
    }

    static void Apply(Mesh mesh, byte[] counts, BoneWeight1[] weights)
    {
        using var perVertex = new NativeArray<byte>(counts, Allocator.Temp);
        using var influences = new NativeArray<BoneWeight1>(weights, Allocator.Temp);
        mesh.SetBoneWeights(perVertex, influences);
    }

    static string AttributeShape(Mesh mesh)
    {
        string Describe(VertexAttribute attribute) => mesh.HasVertexAttribute(attribute)
            ? mesh.GetVertexAttributeFormat(attribute) + ":" + mesh.GetVertexAttributeDimension(attribute) + ":" + mesh.GetVertexAttributeStream(attribute)
            : "none";
        return Describe(VertexAttribute.BlendWeight) + "/" + Describe(VertexAttribute.BlendIndices);
    }

    static string Words(GraphicsBuffer buffer)
    {
        var words = new uint[buffer.count * buffer.stride / sizeof(uint)];
        buffer.GetData(words);
        return buffer.count + ":" + buffer.stride + ":" + buffer.target + ":" +
            string.Join("/", Array.ConvertAll(words, word => word.ToString("X8", Invariant)));
    }

    static string OnWorkerThread(Action action)
    {
        string result = "ok";
        var thread = new Thread(() =>
        {
            try { action(); }
            catch (Exception error) { result = error.GetType().Name + ": " + error.Message; }
        });
        thread.Start();
        thread.Join();
        return result;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Begin()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MESH_SKINNING_OUT")))
            new GameObject("Mesh skinning probe").AddComponent<MeshSkinningProbe>().StartCoroutine(Capture());
    }

    static IEnumerator Capture()
    {
        string output = Environment.GetEnvironmentVariable("MESH_SKINNING_OUT");
        if (string.IsNullOrEmpty(output)) yield break;
        var rows = new List<string>();
        void Put(string key, object value) => rows.Add(JsonUtility.ToJson(new Row { k = key, v = Convert.ToString(value, Invariant) }));
        void Probe(string stage, Func<string> action)
        {
            var logs = new List<string>();
            void CaptureLog(string message, string _, LogType type) => logs.Add(type + ":" + message);
            Application.logMessageReceived += CaptureLog;
            string value;
            try { value = action(); }
            catch (Exception e) { value = e.GetType().Name + ": " + e.Message; }
            finally { Application.logMessageReceived -= CaptureLog; }
            Put(stage, value);
            Put(stage + ".log", string.Join("\n", logs));
        }

        void ProbePositions(string stage, Func<Vector3[]> action)
        {
            var logs = new List<string>();
            void CaptureLog(string message, string _, LogType type) => logs.Add(type + ":" + message);
            Application.logMessageReceived += CaptureLog;
            try { rows.Add(JsonUtility.ToJson(new PositionRow { k = stage, v = action() })); }
            catch (Exception error) { Put(stage, error.GetType().Name + ": " + error.Message); }
            finally { Application.logMessageReceived -= CaptureLog; }
            Put(stage + ".log", string.Join("\n", logs));
        }

        Put("unity", Application.unityVersion);
        var mesh = Make(3);
        Probe("empty.views", () => mesh.GetBonesPerVertex().ToArray().Length + ":" + mesh.GetAllBoneWeights().ToArray().Length);
        Probe("invalid.default", () => { mesh.SetBoneWeights(default, default); return Shape(mesh); });
        Probe("one.implicitWeights", () =>
        {
            Apply(mesh, new byte[] { 1, 1, 1 }, new[] { Weight(2, 0), Weight(5, .75f), Weight(4, 1) });
            return Shape(mesh) + "|" + AttributeShape(mesh);
        });
        Probe("invalid.countLength", () =>
        {
            Apply(mesh, new byte[] { 1, 1 }, new[] { Weight(1, 1), Weight(2, 1) });
            return Shape(mesh);
        });
        Probe("invalid.weightCount", () =>
        {
            Apply(mesh, new byte[] { 1, 1, 1 }, new[] { Weight(1, 1), Weight(2, 1) });
            return Shape(mesh);
        });
        Probe("invalid.zeroVertex", () =>
        {
            Apply(mesh, new byte[] { 1, 0, 1 }, new[] { Weight(1, 1), Weight(2, 1) });
            return Shape(mesh);
        });
        Probe("two.layout", () =>
        {
            Apply(mesh, new byte[] { 2, 2, 1 }, new[] { Weight(2, 0.85f), Weight(1, 0.15f), Weight(4, 0.5f), Weight(2, 0.5f), Weight(3, 1) });
            return Shape(mesh) + "|" + AttributeShape(mesh);
        });
        Probe("two.zeros", () =>
        {
            Apply(mesh, new byte[] { 2, 2, 1 }, new[] { Weight(2, 0), Weight(1, 0), Weight(4, 0.5f), Weight(2, 0), Weight(3, 0) });
            return Shape(mesh) + "|" + AttributeShape(mesh);
        });
        Probe("two.negative", () =>
        {
            Apply(mesh, new byte[] { 2, 2, 1 }, new[] { Weight(2, 0.5f), Weight(1, -0.25f), Weight(4, 0.5f), Weight(2, -0.1f), Weight(3, 1) });
            return Shape(mesh) + "|" + AttributeShape(mesh);
        });
        Probe("wide.original", () =>
        {
            Apply(mesh, new byte[] { 5, 1, 1 }, new[] {
                Weight(5, 0.5f), Weight(1, 0.25f), Weight(2, 0.125f), Weight(4, 0.0625f), Weight(3, 0.0625f), Weight(2, 1), Weight(6, 1)
            });
            return Shape(mesh) + "|" + AttributeShape(mesh);
        });
        Probe("wide.reapplyVertexDescriptors", () =>
        {
            mesh.SetVertexBufferParams(mesh.vertexCount, mesh.GetVertexAttributes());
            return Shape(mesh) + "|" + AttributeShape(mesh);
        });
        Probe("wide.rawIndicesWrite", () =>
        {
            int stream = mesh.GetVertexAttributeStream(VertexAttribute.BlendIndices);
            int stride = mesh.GetVertexBufferStride(stream);
            using var data = Mesh.AcquireReadOnlyMeshData(mesh);
            var bytes = data[0].GetVertexData<ushort>(stream).ToArray();
            bytes[mesh.GetVertexAttributeOffset(VertexAttribute.BlendIndices) / 2] = 19;
            mesh.SetVertexBufferData(bytes, 0, 0, stride / 2, stream);
            return Shape(mesh) + "|" + AttributeShape(mesh);
        });
        Probe("wide.legacyAssignment", () =>
        {
            mesh.boneWeights = new[] { new BoneWeight { weight0 = 1, boneIndex0 = 13 },
                new BoneWeight { weight0 = 1, boneIndex0 = 5 }, new BoneWeight { weight0 = 1, boneIndex0 = 11 } };
            return Shape(mesh) + "|" + AttributeShape(mesh);
        });
        using (var counts = new NativeArray<byte>(new byte[] {1, 1, 1}, Allocator.Persistent))
        using (var weights = new NativeArray<BoneWeight1>(new[] { Weight(2, 1), Weight(4, 1), Weight(5, 1) }, Allocator.Persistent))
            Probe("thread.assign", () => OnWorkerThread(() => mesh.SetBoneWeights(counts, weights)));
        Probe("thread.counts", () => OnWorkerThread(() => mesh.GetBonesPerVertex()));
        Probe("thread.weights", () => OnWorkerThread(() => mesh.GetAllBoneWeights()));
        Probe("thread.layout", () => OnWorkerThread(() => _ = mesh.skinWeightBufferLayout));
        Probe("legacy.grow", () => { mesh.vertices = new Vector3[4]; return Shape(mesh); });
        Probe("legacy.shrink", () => { mesh.vertices = new Vector3[2]; return Shape(mesh); });
        Probe("legacy.clearKeepingLayout", () => { mesh.Clear(); return Shape(mesh); });
        Probe("legacy.clearLayout", () => { mesh.Clear(false); return Shape(mesh); });
        Object.DestroyImmediate(mesh);
        var maximum = Make(1);
        Probe("unlimited.255", () =>
        {
            var inputs = new BoneWeight1[255];
            for (int i = 0; i < inputs.Length; i++) inputs[i] = Weight(i, 1f / inputs.Length);
            Apply(maximum, new byte[] { 255 }, inputs);
            var weights = maximum.GetAllBoneWeights();
            return maximum.GetBonesPerVertex()[0] + ":" + weights.Length + ":" + weights[254].boneIndex + "@" +
                weights[254].weight.ToString("R", Invariant);
        });
        Object.DestroyImmediate(maximum);
        var nan = Weight(13, float.NaN);
        var otherNan = Weight(13, float.NaN);
        Probe("weightValue.equality", () => nan.Equals(otherNan) + ":" + (nan == otherNan) + ":" + (nan != otherNan) +
            ":" + Weight(7, 0.25f).GetHashCode().ToString(Invariant));
        var root = new GameObject("Skin");
        var deforming = new Mesh
        {
            vertices = new[] { Vector3.zero, Vector3.right, Vector3.up },
            triangles = new[] { 0, 1, 2 }
        };
        var outputMesh = new Mesh();
        var bones = new Transform[6];
        var poses = new Matrix4x4[6];
        for (int i = 0; i < bones.Length; ++i)
        {
            bones[i] = new GameObject("Bone " + i).transform;
            bones[i].SetParent(root.transform, false);
            bones[i].localPosition = new Vector3(i * i, i * (7 - i), 0);
            poses[i] = Matrix4x4.identity;
        }
        deforming.bindposes = poses;
        Apply(deforming, new byte[] { 6, 2, 1 }, new[] {
            Weight(0, 0.35f), Weight(1, 0.23f), Weight(2, 0.16f), Weight(3, 0.12f), Weight(4, 0.09f), Weight(5, 0.05f),
            Weight(5, 0.7f), Weight(1, 0.3f), Weight(2, 1),
        });
        var renderer = root.AddComponent<SkinnedMeshRenderer>();
        renderer.bones = bones;
        renderer.sharedMesh = deforming;
        var originalQuality = QualitySettings.skinWeights;
        try
        {
            Probe("bake.weights", () => Shape(deforming));
            Vector3[] Bake()
            {
                renderer.BakeMesh(outputMesh);
                return outputMesh.vertices;
            }
            foreach (SkinWeights quality in new[] { SkinWeights.OneBone, SkinWeights.TwoBones, SkinWeights.FourBones, SkinWeights.Unlimited })
            {
                QualitySettings.skinWeights = quality;
                ProbePositions("bake.auto." + quality, Bake);
            }
            renderer.quality = SkinQuality.Bone2;
            ProbePositions("bake.overrideTwo", Bake);
        }
        finally
        {
            QualitySettings.skinWeights = originalQuality;
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(deforming);
            Object.DestroyImmediate(outputMesh);
        }
        // The buffer checks require the renderer to have completed its first frame.
        yield return null;
        var bufferMesh = Make(3);
        var narrow = new byte[] { 2, 2, 1 };
        var initial = new[] { Weight(2, 0.75f), Weight(0, 0.25f), Weight(3, 0.625f), Weight(1, 0.375f), Weight(4, 1) };
        var replacement = new[] { Weight(8, 0.75f), Weight(7, 0.25f), Weight(6, 0.5f), Weight(5, 0.5f), Weight(4, 1) };
        Probe("buffer.fresh.buffer", () =>
        {
            using var buffer = bufferMesh.GetBoneWeightBuffer(SkinWeights.TwoBones);
            return buffer == null ? "null" : Words(buffer);
        });
        Apply(bufferMesh, narrow, initial);
        using var first = bufferMesh.GetBoneWeightBuffer(SkinWeights.TwoBones);
        Probe("buffer.initial", () => Words(first));
        Probe("buffer.wrapper.shared", () =>
        {
            using var second = bufferMesh.GetBoneWeightBuffer(SkinWeights.TwoBones);
            return (first.GetNativeBufferPtr() == second.GetNativeBufferPtr()) + ":" + ReferenceEquals(first, second);
        });
        Probe("buffer.wrapper.afterDisposeOther", () => first.IsValid() + ":" + Words(first));
        Probe("buffer.storage.sameLayoutRewrite", () =>
        {
            Apply(bufferMesh, narrow, replacement);
            Put("buffer.storage.existingImmediatelyAfterSetter", Words(first));
            using var current = bufferMesh.GetBoneWeightBuffer(SkinWeights.TwoBones);
            return (first.GetNativeBufferPtr() == current.GetNativeBufferPtr()) + "|" + first.IsValid() + "|" + Words(current);
        });
        Probe("buffer.storage.newLayout", () =>
        {
            Apply(bufferMesh, new byte[] { 1, 1, 1 }, new[] { Weight(4, 1), Weight(5, 1), Weight(6, 1) });
            using var current = bufferMesh.GetBoneWeightBuffer(SkinWeights.TwoBones);
            return (first.GetNativeBufferPtr() == current.GetNativeBufferPtr()) + "|" + first.IsValid() + "|" + Words(current);
        });
        Probe("buffer.storage.afterUnreadable", () =>
        {
            bufferMesh.triangles = new[] { 0, 1, 2 };
            bufferMesh.UploadMeshData(true);
            using var current = bufferMesh.GetBoneWeightBuffer(SkinWeights.OneBone);
            return bufferMesh.isReadable + "|" + Words(current);
        });
        Probe("buffer.storage.ownerDestroyed", () =>
        {
            using var current = bufferMesh.GetBoneWeightBuffer(SkinWeights.TwoBones);
            Object.DestroyImmediate(bufferMesh);
            return first.IsValid() + "|" + current.IsValid();
        });

        var growing = Make(3);
        Apply(growing, narrow, initial);
        using var small = growing.GetBoneWeightBuffer(SkinWeights.TwoBones);
        Probe("buffer.storage.resize", () =>
        {
            growing.vertices = new Vector3[4];
            using var grown = growing.GetBoneWeightBuffer(SkinWeights.TwoBones);
            return small.IsValid() + "|" + (small.GetNativeBufferPtr() == grown.GetNativeBufferPtr()) + "|" + Words(grown);
        });
        Object.DestroyImmediate(growing);

        var packed = Make(3);
        Apply(packed, new byte[] { 6, 1, 2 }, new[] {
            Weight(4, 0.31f), Weight(1, 0.24f), Weight(8, 0.2f), Weight(3, 0.12f), Weight(2, 0.1f), Weight(9, 0.03f),
            Weight(8, 1), Weight(2, 0.8f), Weight(4, 0.2f)
        });
        foreach (SkinWeights layout in new[] { SkinWeights.None, SkinWeights.OneBone, SkinWeights.TwoBones,
            SkinWeights.FourBones, SkinWeights.Unlimited, (SkinWeights)3 })
            Probe("buffer.packed." + layout, () =>
            {
                using var view = packed.GetBoneWeightBuffer(layout);
                return view == null ? "null" : Words(view);
            });
        Probe("buffer.worker", () => OnWorkerThread(() => packed.GetBoneWeightBuffer(SkinWeights.OneBone)));
        Object.DestroyImmediate(packed);

        var diagnostics = Make(1);
        Apply(diagnostics, new byte[] { 2 }, new[] { Weight(5, 0.75f), Weight(7, 0.25f) });
        Probe("transition.callback", () =>
        {
            string observed = "none";
            void Observe(string message, string _, LogType type)
            {
                if (type == LogType.Error && message.Contains("Unsupported conversion"))
                    observed = Shape(diagnostics) + "|" + AttributeShape(diagnostics);
            }
            Application.logMessageReceived += Observe;
            try
            {
                Apply(diagnostics, new byte[] { 5 }, new[] { Weight(1, 0.5f), Weight(2, 0.25f),
                    Weight(3, 0.125f), Weight(4, 0.0625f), Weight(6, 0.0625f) });
            }
            finally { Application.logMessageReceived -= Observe; }
            return observed + "|" + Shape(diagnostics) + "|" + AttributeShape(diagnostics);
        });
        Probe("unlimited.unsorted", () =>
        {
            Apply(diagnostics, new byte[] { 5 }, new[] { Weight(1, 0.25f), Weight(2, 0.5f),
                Weight(3, 0.125f), Weight(4, 0.0625f), Weight(6, 0.0625f) });
            return Shape(diagnostics);
        });
        Probe("unlimited.infinity", () =>
        {
            Apply(diagnostics, new byte[] { 5 }, new[] { Weight(4, float.PositiveInfinity), Weight(1, 0.25f),
                Weight(2, 0.25f), Weight(3, 0.125f), Weight(5, 0.125f) });
            return Shape(diagnostics);
        });
        Probe("fixed.nan", () =>
        {
            Apply(diagnostics, new byte[] { 2 }, new[] { Weight(2, 1), Weight(3, float.NaN) });
            return Shape(diagnostics);
        });
        Probe("thread.legacyGet", () => OnWorkerThread(() => _ = diagnostics.boneWeights));
        Probe("thread.legacySet", () => OnWorkerThread(() => diagnostics.boneWeights = new[] { new BoneWeight { weight0 = 1 } }));
        Probe("thread.vertices", () => OnWorkerThread(() => diagnostics.vertices = new Vector3[2]));
        Probe("thread.clear", () => OnWorkerThread(() => diagnostics.Clear()));
        Object.DestroyImmediate(diagnostics);
        rows.Add("{\"k\":\"complete\",\"v\":true}");
        File.WriteAllLines(Path.Combine(output, "values.jsonl"), rows);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }

}
