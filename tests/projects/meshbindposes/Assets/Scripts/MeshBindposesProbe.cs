using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

static class MeshBindposesProbe
{
    [Serializable] sealed class Row { public string k, v; }

    static Matrix4x4 Pose(int x, int y) => Matrix4x4.Translate(new Vector3(x, y, 0));
    static string Pair(Matrix4x4 pose) => pose.m03.ToString("R", CultureInfo.InvariantCulture) + "," +
        pose.m13.ToString("R", CultureInfo.InvariantCulture);

    static string Describe(Mesh mesh)
    {
        var view = mesh.GetBindposes();
        string[] rows = new string[view.Length];
        for (int i = 0; i < view.Length; i++) rows[i] = Pair(view[i]);
        return (view.IsCreated ? "created:" : "default:") + string.Join("/", rows);
    }

    static string OnWorkerThread(Action action)
    {
        string result = "ok";
        var worker = new Thread(() =>
        {
            try { action(); }
            catch (Exception error) { result = error.GetType().Name + ": " + error.Message; }
        });
        worker.Start();
        worker.Join();
        return result;
    }

    static string BakeWithOrderedPoses()
    {
        var root = new GameObject("Skinned triangle");
        var first = new GameObject("First bone");
        var second = new GameObject("Second bone");
        var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
        var baked = new Mesh();
        using var poses = new NativeArray<Matrix4x4>(new[] { Pose(-2, 0), Pose(0, -5) }, Allocator.Temp);
        try
        {
            first.transform.SetParent(root.transform, false);
            second.transform.SetParent(root.transform, false);
            first.transform.localPosition = new Vector3(7, 0, 0);
            second.transform.localPosition = new Vector3(0, 13, 0);
            mesh.boneWeights = new[]
            {
                new BoneWeight { boneIndex0 = 0, weight0 = 1 },
                new BoneWeight { boneIndex0 = 1, weight0 = 1 },
                new BoneWeight { boneIndex0 = 0, weight0 = 1 }
            };
            mesh.SetBindposes(poses);
            var renderer = root.AddComponent<SkinnedMeshRenderer>();
            renderer.bones = new[] { first.transform, second.transform };
            renderer.sharedMesh = mesh;
            renderer.BakeMesh(baked);
            return string.Join("/", Array.ConvertAll(baked.vertices, v =>
                v.x.ToString("R", CultureInfo.InvariantCulture) + "," + v.y.ToString("R", CultureInfo.InvariantCulture)));
        }
        finally
        {
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(mesh);
            Object.DestroyImmediate(baked);
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Capture()
    {
        string output = Environment.GetEnvironmentVariable("MESH_BINDPOSES_OUT");
        if (string.IsNullOrEmpty(output)) return;
        var rows = new List<string>();
        void Put(string key, string value) => rows.Add(JsonUtility.ToJson(new Row { k = key, v = value }));
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

        Put("unity", Application.unityVersion);
        var mesh = new Mesh { vertices = new[] { Vector3.zero, Vector3.right, Vector3.up }, triangles = new[] { 0, 1, 2 } };
        Probe("fresh", () => Describe(mesh));
        Put("count.fresh", mesh.bindposeCount.ToString(CultureInfo.InvariantCulture));
        Probe("set.copy.order", () =>
        {
            var poses = new NativeArray<Matrix4x4>(new[] { Pose(2, 3), Pose(7, 11) }, Allocator.Temp);
            try
            {
                mesh.SetBindposes(poses);
                poses[1] = Pose(13, 17);
            }
            finally { poses.Dispose(); }
            var list = new List<Matrix4x4> { Pose(19, 23), Pose(29, 31), Pose(37, 41) };
            mesh.GetBindposes(list);
            return Describe(mesh) + "|" + string.Join("/", Array.ConvertAll(mesh.bindposes, Pair)) + "|" +
                string.Join("/", list.ConvertAll(Pair));
        });
        Put("count.assigned", mesh.bindposeCount.ToString(CultureInfo.InvariantCulture));
        NativeArray<Matrix4x4> original = mesh.GetBindposes();
        Probe("view.write", () => { original[1] = Pose(43, 47); return Pair(mesh.bindposes[1]); });
        Probe("view.dispose", () => { mesh.GetBindposes().Dispose(); return Describe(mesh); });
        Probe("view.afterDisposeOther", () => Pair(original[1]));
        Probe("set.default", () => { mesh.SetBindposes(default); return Describe(mesh); });
        Probe("set.emptyAllocated", () => { using var empty = new NativeArray<Matrix4x4>(0, Allocator.Temp); mesh.SetBindposes(empty); return Describe(mesh); });
        Probe("view.afterInvalidSetter", () => Pair(original[1]));
        Probe("set.selfView", () => { mesh.SetBindposes(mesh.GetBindposes()); return Describe(mesh); });
        Probe("view.afterSelfView", () => Pair(original[1]));
        var beforeManagedSet = mesh.GetBindposes();
        Probe("set.managed", () => { mesh.bindposes = new[] { Pose(53, 59), Pose(61, 67) }; return Describe(mesh); });
        Probe("view.afterManagedSet", () => Pair(beforeManagedSet[1]));
        var keep = mesh.GetBindposes();
        Probe("set.vertices", () => { mesh.vertices = new[] { Vector3.up, Vector3.right, Vector3.zero }; return Pair(keep[1]) + "|" + Describe(mesh); });
        Probe("set.clear", () => { mesh.Clear(); return Pair(keep[1]) + "|" + Describe(mesh); });
        Probe("clone.independent", () =>
        {
            var clone = Object.Instantiate(mesh);
            try
            {
                clone.bindposes = new[] { Pose(71, 73) };
                return Describe(clone) + "|" + Describe(mesh);
            }
            finally { Object.DestroyImmediate(clone); }
        });
        Probe("set.managedEmpty", () => { mesh.bindposes = Array.Empty<Matrix4x4>(); return Describe(mesh); });
        Put("count.cleared", mesh.bindposeCount.ToString(CultureInfo.InvariantCulture));
        Probe("view.afterManagedEmpty", () => Pair(keep[1]));
        mesh.bindposes = new[] { Pose(79, 83) };
        var beforeNull = mesh.GetBindposes();
        Probe("set.managedNull", () => { mesh.bindposes = null; return Describe(mesh); });
        Probe("view.afterManagedNull", () => Pair(beforeNull[0]));
        mesh.bindposes = new[] { Pose(89, 97) };
        var beforeUpload = mesh.GetBindposes();
        mesh.vertices = new[] { Vector3.up, Vector3.right, Vector3.zero };
        mesh.triangles = new[] { 0, 1, 2 };
        Probe("set.upload", () => { mesh.UploadMeshData(true); return mesh.isReadable + "|" + Describe(mesh); });
        Probe("view.afterUpload", () => Pair(beforeUpload[0]));
        Probe("set.unreadable", () => { using var poses = new NativeArray<Matrix4x4>(new[] { Pose(101, 103) }, Allocator.Temp); mesh.SetBindposes(poses); return Describe(mesh); });
        Object.DestroyImmediate(mesh);
        Probe("skinning.poseOrder", BakeWithOrderedPoses);
        var alias = new Mesh();
        alias.bindposes = new[] { Pose(2, 3), Pose(7, 11), Pose(13, 17) };
        Probe("alias.offset", () =>
        {
            alias.SetBindposes(alias.GetBindposes().GetSubArray(1, 2));
            return Describe(alias);
        });
        alias.bindposes = new[] { Pose(2, 3), Pose(7, 11), Pose(13, 17) };
        Probe("alias.reentrant", () =>
        {
            int calls = 0;
            void ReplacePoses(string message, string _, LogType type)
            {
                if (type != LogType.Assert || !message.Contains("assign data")) return;
                calls++;
                alias.bindposes = new[] { Pose(19, 23), Pose(29, 31), Pose(37, 41) };
            }
            Application.logMessageReceived += ReplacePoses;
            try { alias.SetBindposes(alias.GetBindposes().GetSubArray(1, 2)); }
            finally { Application.logMessageReceived -= ReplacePoses; }
            return calls.ToString(CultureInfo.InvariantCulture) + "|" + Describe(alias);
        });
        Object.DestroyImmediate(alias);
        var controls = new Mesh();
        Probe("fresh.copy", () =>
        {
            var view = controls.GetBindposes();
            return view.ToArray().Length + ":" + view.AsReadOnlySpan().Length;
        });
        Probe("fresh.index", () => Pair(controls.GetBindposes()[0]));
        controls.bindposes = new[] { Pose(5, 7) };
        Probe("set.disposed", () =>
        {
            var view = controls.GetBindposes();
            view.Dispose();
            controls.SetBindposes(view);
            return Describe(controls);
        });
        Probe("get.nullList", () => { controls.GetBindposes((List<Matrix4x4>)null); return Describe(controls); });
        Probe("thread.borrow", () => OnWorkerThread(() => controls.GetBindposes()));
        using (var poses = new NativeArray<Matrix4x4>(new[] { Pose(11, 13) }, Allocator.Persistent))
            Probe("thread.assign", () => OnWorkerThread(() => controls.SetBindposes(poses)));
        Probe("thread.arrayGet", () => OnWorkerThread(() => _ = controls.bindposes));
        Probe("thread.arraySet", () => OnWorkerThread(() => controls.bindposes = new[] { Pose(17, 19) }));
        Probe("thread.count", () => OnWorkerThread(() => _ = controls.bindposeCount));
        Probe("thread.list", () => OnWorkerThread(() => controls.GetBindposes(new List<Matrix4x4>())));
        Object.DestroyImmediate(controls);
        rows.Add("{\"k\":\"complete\",\"v\":true}");
        File.WriteAllLines(Path.Combine(output, "values.jsonl"), rows);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#endif
    }
}
