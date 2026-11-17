using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>Reads imported meshes (readable and not) and a scripted mesh made no longer readable
/// through Mesh's script API in Play mode: their vertex layout and every read with what it returns,
/// throws and logs. Writes values.jsonl into MESH_READABLE_OUT, then exits. See README.md.</summary>
public class MeshReadableProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("MeshReadableProbe").AddComponent<MeshReadableProbe>();

    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Logs = new List<string>();

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string J(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";
    void Value(string key, string json) => m_Out.Append("{\"k\":").Append(J(key)).Append(",\"v\":").Append(json).Append("}\n");
    static string Vec(Vector2 v) => "[" + F(v.x) + "," + F(v.y) + "]";
    static string Vec(Vector3 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
    static string Vec(Vector4 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "," + F(v.w) + "]";
    static string Col(Color c) => "[" + F(c.r) + "," + F(c.g) + "," + F(c.b) + "," + F(c.a) + "]";
    static string Col(Color32 c) => "[" + c.r + "," + c.g + "," + c.b + "," + c.a + "]";

    void OnLog(string condition, string stack, LogType type) => m_Logs.Add(type + ": " + condition);

    /// <summary>One read: its result (a JSON value), or the exception it throws, and what it logs.</summary>
    void Read(string key, Func<string> read)
    {
        m_Logs.Clear();
        string result;
        try { result = read(); }
        catch (Exception e) { result = J("throws " + e.GetType().Name + ": " + e.Message); }
        Value(key, result);
        if (m_Logs.Count > 0) Value(key + ".log", "[" + string.Join(",", m_Logs.Select(J)) + "]");
    }

    static string Count<T>(T[] a, Func<T, string> first) => a == null ? "null" : a.Length == 0 ? "[0]" : "[" + a.Length + "," + first(a[0]) + "," + first(a[a.Length - 1]) + "]";
    static string Count<T>(List<T> a, Func<T, string> first) => a.Count == 0 ? "[0]" : "[" + a.Count + "," + first(a[0]) + "," + first(a[a.Count - 1]) + "]";

    void Probe(string name, Mesh mesh)
    {
        Value(name + ".readable", mesh.isReadable ? "true" : "false");
        Value(name + ".vertexCount", mesh.vertexCount.ToString());
        Value(name + ".subMeshCount", mesh.subMeshCount.ToString());
        Value(name + ".indexFormat", J(mesh.indexFormat.ToString()));
        Value(name + ".bounds", "[" + Vec(mesh.bounds.center) + "," + Vec(mesh.bounds.size) + "]");
        Value(name + ".attributes", "[" + string.Join(",", mesh.GetVertexAttributes().Select(a => J(a.attribute + ":" + a.format + "x" + a.dimension + "@" + a.stream))) + "]");
        Value(name + ".vertexAttributeCount", mesh.vertexAttributeCount.ToString());
        Value(name + ".vertexBufferCount", mesh.vertexBufferCount.ToString());
        Value(name + ".strides", "[" + string.Join(",", Enumerable.Range(0, mesh.vertexBufferCount).Select(s => mesh.GetVertexBufferStride(s).ToString())) + "]");
        foreach (VertexAttribute a in Enum.GetValues(typeof(VertexAttribute)))
            if (mesh.HasVertexAttribute(a))
                Value(name + ".attribute." + a, "[" + J(mesh.GetVertexAttributeFormat(a).ToString()) + "," + mesh.GetVertexAttributeDimension(a) + "," + mesh.GetVertexAttributeStream(a) + "," + mesh.GetVertexAttributeOffset(a) + "]");
        Value(name + ".indexCount0", mesh.GetIndexCount(0).ToString());
        var sub = mesh.GetSubMesh(0);
        Value(name + ".subMesh0", "[" + sub.indexStart + "," + sub.indexCount + "," + sub.baseVertex + "," + sub.firstVertex + "," + sub.vertexCount + "]");

        Read(name + ".vertices", () => Count(mesh.vertices, Vec));
        Read(name + ".normals", () => Count(mesh.normals, Vec));
        Read(name + ".tangents", () => Count(mesh.tangents, Vec));
        Read(name + ".colors", () => Count(mesh.colors, Col));
        Read(name + ".colors32", () => Count(mesh.colors32, Col));
        Read(name + ".uv", () => Count(mesh.uv, Vec));
        Read(name + ".uv2", () => Count(mesh.uv2, Vec));
        Read(name + ".uv5", () => Count(mesh.uv5, Vec));
        Read(name + ".uv8", () => Count(mesh.uv8, Vec));
        Read(name + ".triangles", () => Count(mesh.triangles, i => i.ToString()));
        Read(name + ".GetTriangles", () => Count(mesh.GetTriangles(0), i => i.ToString()));
        Read(name + ".GetIndices", () => Count(mesh.GetIndices(0), i => i.ToString()));
        Read(name + ".boneWeights", () => Count(mesh.boneWeights, w => F(w.weight0)));
        Read(name + ".GetVertices", () => { var l = new List<Vector3>(); mesh.GetVertices(l); return Count(l, Vec); });
        Read(name + ".GetNormals", () => { var l = new List<Vector3>(); mesh.GetNormals(l); return Count(l, Vec); });
        Read(name + ".GetColors", () => { var l = new List<Color>(); mesh.GetColors(l); return Count(l, Col); });
        Read(name + ".GetColors32", () => { var l = new List<Color32>(); mesh.GetColors(l); return Count(l, Col); });
        for (int channel = 0; channel < 8; channel++)
        {
            int c = channel;
            Read(name + ".GetUVs" + c, () => { var l = new List<Vector2>(); mesh.GetUVs(c, l); return Count(l, Vec); });
        }
        Read(name + ".GetUVs4d", () => { var l = new List<Vector4>(); mesh.GetUVs(4, l); return Count(l, Vec); });
        Read(name + ".GetTrianglesList", () => { var l = new List<int>(); mesh.GetTriangles(l, 0); return Count(l, i => i.ToString()); });
        Read(name + ".GetIndicesList", () => { var l = new List<int>(); mesh.GetIndices(l, 0); return Count(l, i => i.ToString()); });
        // The engine's own use of the data: a MeshCollider of the mesh, hit by a ray down its normal.
        Read(name + ".colliderHit", () =>
        {
            var go = new GameObject(name + " collider");
            var collider = go.AddComponent<MeshCollider>();
            collider.sharedMesh = mesh;
            Physics.SyncTransforms();
            var center = mesh.bounds.center;
            bool hit = Physics.Raycast(center + new Vector3(0.001f, 0.001f, 1f), Vector3.back, out var info, 10f);
            Destroy(go);
            return hit ? "[" + Vec(info.point) + "," + F(info.distance) + "]" : "false";
        });
        Read(name + ".meshData", () =>
        {
            using (var data = Mesh.AcquireReadOnlyMeshData(mesh))
            {
                var m = data[0];
                var vertices = new Unity.Collections.NativeArray<Vector3>(m.vertexCount, Unity.Collections.Allocator.Temp);
                m.GetVertices(vertices);
                string first = m.vertexCount > 0 ? Vec(vertices[0]) : "null";
                vertices.Dispose();
                return "[" + m.vertexCount + "," + m.vertexBufferCount + "," + m.subMeshCount + "," + first + "]";
            }
        });
    }

    void Start()
    {
        Application.logMessageReceived += OnLog;
        Probe("layers", Resources.Load<Mesh>("Layers"));
        Probe("layersLocked", Resources.Load<Mesh>("LayersLocked"));
        Probe("plainLocked", Resources.Load<Mesh>("Plain"));
        // A control outside the importer: a scripted mesh with byte colors and a UV in channel 4,
        // then made no longer readable.
        var scripted = new Mesh { name = "Scripted" };
        scripted.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
        scripted.colors32 = new[] { new Color32(10, 20, 30, 40), new Color32(50, 60, 70, 80), new Color32(90, 100, 110, 120) };
        scripted.SetUVs(4, new[] { Vector2.one, Vector2.zero, Vector2.up });
        scripted.triangles = new[] { 0, 1, 2 };
        Probe("scripted", scripted);
        scripted.UploadMeshData(true);
        Probe("scriptedLocked", scripted);
        Application.logMessageReceived -= OnLog;

        Value("complete", "true");
        var dir = Environment.GetEnvironmentVariable("MESH_READABLE_OUT");
        int code = 0;
        if (string.IsNullOrEmpty(dir)) { Debug.LogError("MESH_READABLE_OUT is not set"); code = 1; }
        else { Directory.CreateDirectory(dir); File.WriteAllText(Path.Combine(dir, "values.jsonl"), m_Out.ToString()); }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
}
