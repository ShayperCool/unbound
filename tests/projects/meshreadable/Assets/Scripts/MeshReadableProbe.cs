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

    /// <summary>Values the editor class <c>MeshReadableEditMode</c> records outside Play mode,
    /// as the editor domain loads (JSON lines, written out with the rest).</summary>
    public static string EditModeValues = "";

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
            bool hit = Physics.Raycast(Inside(mesh) + new Vector3(0f, 0f, 1f), Vector3.back, out var info, 10f);
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

    /// <summary>A point a quarter in from the bounds' minimum corner, on the mesh's plane: inside
    /// the grid and inside the scripted triangle (its positions need not be readable).</summary>
    static Vector3 Inside(Mesh mesh)
    {
        var b = mesh.bounds;
        return new Vector3(b.min.x + b.size.x * 0.25f, b.min.y + b.size.y * 0.25f, b.center.z);
    }

    /// <summary>The engine's other uses of meshes scripts cannot read.</summary>
    void Uses(Mesh locked, Mesh readable, Mesh lockedScripted)
    {
        Read("combine.lockedSource", () =>
        {
            var into = new Mesh { name = "Combined" };
            into.CombineMeshes(new[] { new CombineInstance { mesh = locked, transform = Matrix4x4.identity }, new CombineInstance { mesh = readable, transform = Matrix4x4.identity } });
            return "[" + into.vertexCount + "," + into.subMeshCount + "," + into.GetIndexCount(0) + "]";
        });
        Read("combine.lockedTarget", () =>
        {
            var into = new Mesh { name = "LockedTarget" };
            into.vertices = new[] { Vector3.zero, Vector3.right, Vector3.up };
            into.triangles = new[] { 0, 1, 2 };
            into.UploadMeshData(true);
            into.CombineMeshes(new[] { new CombineInstance { mesh = readable, transform = Matrix4x4.identity } });
            return "[" + into.vertexCount + "," + into.subMeshCount + "," + into.GetIndexCount(0) + "]";
        });
        Read("bake.locked", () =>
        {
            var go = new GameObject("bake");
            var skin = go.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = locked;
            var baked = new Mesh { name = "Baked" };
            skin.BakeMesh(baked);
            var result = "[" + baked.vertexCount + "," + baked.isReadable.ToString().ToLowerInvariant() + "," + Count(baked.vertices, Vec) + "]";
            Destroy(go);
            return result;
        });
        Read("set.lockedVertices", () =>
        {
            var target = Instantiate(lockedScripted);
            target.name = "SetTarget";
            target.vertices = new[] { Vector3.one, Vector3.one, Vector3.one };
            return "[" + target.vertexCount + "," + target.isReadable.ToString().ToLowerInvariant() + "]";
        });
        // Writes to a mesh scripts cannot read: each on a fresh copy, then what is left of it
        // (counts, bounds, channels and the indices, read through the list getter, which reads them).
        Write("lockedScripted", lockedScripted);
        Write("lockedImported", locked);
        Read("canvasRenderer.setMesh", () =>
        {
            var go = new GameObject("canvas renderer", typeof(RectTransform));
            var renderer = go.AddComponent<CanvasRenderer>();
            renderer.SetMesh(locked);
            Destroy(go);
            return "true";
        });
    }

    /// <summary>One write to a fresh copy of <paramref name="source"/>: what it logs or throws,
    /// and the copy's state after it.</summary>
    void Write(string name, Mesh source)
    {
        void One(string what, Action<Mesh> write) => Read("write." + name + "." + what, () =>
        {
            var m = Instantiate(source);
            m.name = name + " " + what;
            write(m);
            var indices = new List<int>();
            m.GetTriangles(indices, 0);
            var state = "[" + m.isReadable.ToString().ToLowerInvariant() + "," + m.vertexCount + "," + m.subMeshCount + "," + m.GetIndexCount(0) + ","
                + Vec(m.bounds.center) + "," + Vec(m.bounds.size) + ","
                + J(string.Join(" ", m.GetVertexAttributes().Select(a => a.attribute + ":" + a.format + "x" + a.dimension))) + ","
                + Count(indices, i => i.ToString()) + "]";
            Destroy(m);
            return state;
        });
        int n = source.vertexCount;
        Vector3[] V3(Vector3 v) => Enumerable.Repeat(v, n).ToArray();
        One("normals", m => m.normals = V3(Vector3.up));
        One("tangents", m => m.tangents = Enumerable.Repeat(new Vector4(1f, 0f, 0f, 1f), n).ToArray());
        One("uv", m => m.uv = Enumerable.Repeat(Vector2.one, n).ToArray());
        One("uv3", m => m.uv3 = Enumerable.Repeat(Vector2.one, n).ToArray());
        One("colors32", m => m.colors32 = Enumerable.Repeat(new Color32(1, 2, 3, 4), n).ToArray());
        One("colors", m => m.colors = Enumerable.Repeat(Color.red, n).ToArray());
        One("SetVertices", m => m.SetVertices(V3(Vector3.one).ToList()));
        One("SetUVs4", m => m.SetUVs(4, Enumerable.Repeat(Vector2.one, n).ToList()));
        One("triangles", m => m.triangles = new[] { 0, 1, 2 });
        One("SetTriangles", m => m.SetTriangles(new[] { 0, 2, 1 }, 0));
        One("SetIndices", m => m.SetIndices(new[] { 0, 1 }, MeshTopology.Lines, 0));
        One("RecalculateNormals", m => m.RecalculateNormals());
        One("RecalculateBounds", m => m.RecalculateBounds());
        One("RecalculateTangents", m => m.RecalculateTangents());
        One("Clear", m => m.Clear());
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
        // The same writes to the readable mesh: the control for what a refused write leaves.
        Write("readableScripted", scripted);
        scripted.UploadMeshData(true);
        Probe("scriptedLocked", scripted);
        Uses(Resources.Load<Mesh>("LayersLocked"), Resources.Load<Mesh>("Layers"), scripted);
        Application.logMessageReceived -= OnLog;

        m_Out.Append(EditModeValues);
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
