using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Unity.Collections;
using Unity.Jobs;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

// Mesh.MeshDataArray in jobs: scheduling with the array as a job field, the parallel-for write
// range, [ReadOnly] and [NativeDisableParallelForRestriction] fields, and the array's safety while a
// job uses it. Results are the meshes' vertices after the jobs, and exception types and messages.
public class MeshDataJobsProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("MeshDataJobsProbe").AddComponent<MeshDataJobsProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    StreamWriter output;
    readonly List<string> logs = new List<string>();
    void OnLog(string message, string stack, LogType type) { lock (logs) logs.Add(type + ": " + message.Split('\n')[0]); }

    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = Convert.ToString(value, CultureInfo.InvariantCulture) }));

    void Start()
    {
        string path = Environment.GetEnvironmentVariable("MESH_DATA_JOBS_OUT"); if (string.IsNullOrEmpty(path)) return;
        try
        {
            Application.logMessageReceivedThreaded += OnLog;
            using (output = new StreamWriter(path)) { Record(); Put("complete", 1); }
            Application.logMessageReceivedThreaded -= OnLog;
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    // Each mesh i gets 3 vertices at (i, k, offset).
    struct WriteOwn : IJobParallelFor
    {
        public Mesh.MeshDataArray data;
        public float offset;
        public void Execute(int index)
        {
            var vertices = data[index].GetVertexData<Vector3>();
            for (int k = 0; k < vertices.Length; k++) vertices[k] = new Vector3(index, k, offset);
        }
    }

    struct WriteNext : IJobParallelFor
    {
        public Mesh.MeshDataArray data;
        public void Execute(int index)
        {
            var vertices = data[(index + 1) % data.Length].GetVertexData<Vector3>();
            vertices[0] = new Vector3(-1, -1, -1);
        }
    }

    struct WriteNextUnrestricted : IJobParallelFor
    {
        [NativeDisableParallelForRestriction] public Mesh.MeshDataArray data;
        public void Execute(int index)
        {
            var vertices = data[(index + 1) % data.Length].GetVertexData<Vector3>();
            vertices[1] = new Vector3(index, 9, 9);
        }
    }

    struct ReadOnlyWrite : IJob
    {
        [ReadOnly] public Mesh.MeshDataArray data;
        public NativeArray<float> sum;
        public void Execute()
        {
            float total = 0;
            for (int i = 0; i < data.Length; i++)
                foreach (var v in data[i].GetVertexData<Vector3>()) total += v.x + v.y + v.z;
            sum[0] = total;
            var vertices = data[0].GetVertexData<Vector3>();
            vertices[2] = Vector3.one;
        }
    }

    struct WholeArray : IJob
    {
        public Mesh.MeshDataArray data;
        public void Execute()
        {
            for (int i = 0; i < data.Length; i++)
            {
                var vertices = data[i].GetVertexData<Vector3>();
                vertices[2] = new Vector3(i, 7, 7);
            }
        }
    }

    struct OneMesh : IJob
    {
        public Mesh.MeshData mesh;
        public void Execute()
        {
            var vertices = mesh.GetVertexData<Vector3>();
            vertices[0] = new Vector3(5, 5, 5);
        }
    }

    struct Query : IJob
    {
        public LightProbesQuery query;
        public NativeArray<Vector3> positions;
        public NativeArray<int> tetrahedra;
        public NativeArray<SphericalHarmonicsL2> probes;
        public NativeArray<Vector4> occlusion;
        public void Execute() => query.CalculateInterpolatedLightAndOcclusionProbes(positions, tetrahedra, probes, occlusion);
    }

    struct ReadOnlyQuery : IJob
    {
        [ReadOnly] public LightProbesQuery query;
        public NativeArray<Vector3> positions;
        public NativeArray<int> tetrahedra;
        public NativeArray<SphericalHarmonicsL2> probes;
        public NativeArray<Vector4> occlusion;
        public void Execute() => query.CalculateInterpolatedLightAndOcclusionProbes(positions, tetrahedra, probes, occlusion);
    }

    static Mesh.MeshDataArray Allocate(int count)
    {
        var data = Mesh.AllocateWritableMeshData(count);
        for (int i = 0; i < count; i++)
        {
            var mesh = data[i];
            mesh.SetVertexBufferParams(3, new VertexAttributeDescriptor(VertexAttribute.Position));
            mesh.SetIndexBufferParams(3, IndexFormat.UInt16);
            var indices = mesh.GetIndexData<ushort>();
            for (ushort k = 0; k < 3; k++) indices[k] = k;
            mesh.subMeshCount = 1;
            mesh.SetSubMesh(0, new SubMeshDescriptor(0, 3));
        }
        return data;
    }

    static string Vertices(Mesh.MeshDataArray data) =>
        string.Join(";", Enumerable.Range(0, data.Length).Select(i => string.Join(",", data[i].GetVertexData<Vector3>().Select(v => v.x + "/" + v.y + "/" + v.z))));

    // Exceptions thrown inside jobs are logged from the worker threads: sorted, with duplicates.
    void Try(string key, Action action)
    {
        lock (logs) logs.Clear();
        try { action(); Put(key, "ok"); }
        catch (Exception e) { Put(key, e.GetType().Name + ": " + e.Message.Split('\n')[0]); }
        lock (logs) Put(key + ".logs", string.Join(" | ", logs.OrderBy(l => l, StringComparer.Ordinal)));
    }

    void Record()
    {
        // The array as a parallel-for job field: each index writes its own mesh.
        var data = Allocate(4);
        JobHandle handle = default;
        Try("own.schedule", () => handle = new WriteOwn { data = data, offset = 0.5f }.Schedule(4, 1));
        handle.Complete();
        Put("own.vertices", Vertices(data));

        // While a job uses the array: reading it on the main thread and disposing it.
        Try("running.schedule", () => handle = new WholeArray { data = data }.Schedule());
        Try("running.read", () => { var unused = data[0].vertexCount; });
        Try("running.index", () => { var unused = data[1]; });
        Try("running.dispose", () => data.Dispose());
        handle.Complete();
        Put("whole.vertices", Vertices(data));

        // Another index than the job's: restricted, then allowed with the attribute.
        Try("next.run", () => new WriteNext { data = data }.Schedule(4, 1).Complete());
        Put("next.vertices", Vertices(data));
        Try("nextUnrestricted.run", () => new WriteNextUnrestricted { data = data }.Schedule(4, 1).Complete());
        Put("nextUnrestricted.vertices", Vertices(data));

        // A read-only array field.
        var sum = new NativeArray<float>(1, Allocator.TempJob);
        Try("readOnly.run", () => new ReadOnlyWrite { data = data, sum = sum }.Schedule().Complete());
        Put("readOnly.sum", sum[0]);
        Put("readOnly.vertices", Vertices(data));
        sum.Dispose();

        // A single MeshData as a job field.
        Try("one.run", () => new OneMesh { mesh = data[3] }.Schedule().Complete());
        Put("one.vertices", Vertices(data));
        // While it runs, the job is no dependency of the array: the main thread reads and writes it.
        var lone = Allocate(2);
        Try("one.schedule", () => handle = new OneMesh { mesh = lone[1] }.Schedule());
        Try("one.running.read", () => { var unused = lone[0].vertexCount; });
        Try("one.running.write", () => { var vertices = lone[0].GetVertexData<Vector3>(); vertices[0] = Vector3.one; });
        handle.Complete();
        lone.Dispose();

        // Applied to meshes.
        var meshes = Enumerable.Range(0, 4).Select(i => new Mesh { name = "m" + i }).ToArray();
        Mesh.ApplyAndDisposeWritableMeshData(data, meshes, MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices);
        Put("applied", string.Join(";", meshes.Select(m => string.Join(",", m.vertices.Select(v => v.x + "/" + v.y + "/" + v.z)))));
        Try("disposed.index", () => { var unused = data[0]; });
        Try("disposed.schedule", () => new WholeArray { data = data }.Schedule().Complete());

        // A light probe query as a job field.
        var query = new LightProbesQuery(Allocator.Persistent);
        var positions = new NativeArray<Vector3>(2, Allocator.Persistent);
        var tetrahedra = new NativeArray<int>(2, Allocator.Persistent);
        var probes = new NativeArray<SphericalHarmonicsL2>(2, Allocator.Persistent);
        var occlusion = new NativeArray<Vector4>(2, Allocator.Persistent);
        Try("query.schedule", () => handle = new Query { query = query, positions = positions, tetrahedra = tetrahedra, probes = probes, occlusion = occlusion }.Schedule());
        Try("query.running.dispose", () => query.Dispose());
        handle.Complete();
        Try("query.readOnly.schedule", () => handle = new ReadOnlyQuery { query = query, positions = positions, tetrahedra = tetrahedra, probes = probes, occlusion = occlusion }.Schedule());
        Try("query.readOnly.running.dispose", () => query.Dispose());
        handle.Complete();
        Try("query.dispose", () => query.Dispose());
        Try("query.disposed.schedule", () => new Query { query = query, positions = positions, tetrahedra = tetrahedra, probes = probes, occlusion = occlusion }.Schedule().Complete());
        positions.Dispose(); tetrahedra.Dispose(); probes.Dispose(); occlusion.Dispose();
    }
}
