using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Writes values.jsonl in $BAKEMESH_OUT (tests/value_probe.py rows): Physics.BakeMesh. For each
/// case: what the call threw, the messages it logged, and whether a MeshCollider given the mesh
/// afterwards (convex as baked, the same cooking options) is hit by a ray.
/// </summary>
public class BakeMeshProbe : MonoBehaviour
{
    const string Variable = "BAKEMESH_OUT";
    static StreamWriter output;
    static readonly List<string> s_Logs = new List<string>();

    static string Escape(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t");

    static void Row(string key, string value) => output.Write("{\"k\":\"" + key + "\",\"v\":\"" + Escape(value) + "\"}\n");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(Variable))) return;
        new GameObject("BakeMeshProbe").AddComponent<BakeMeshProbe>();
    }

    static Mesh Cube()
    {
        var source = GameObject.CreatePrimitive(PrimitiveType.Cube);
        var mesh = Instantiate(source.GetComponent<MeshFilter>().sharedMesh);
        mesh.name = "baked cube";
        DestroyImmediate(source);
        return mesh;
    }

    void Start()
    {
        var dir = Environment.GetEnvironmentVariable(Variable);
        Directory.CreateDirectory(dir);
        output = new StreamWriter(Path.Combine(dir, "values.jsonl"), false, new UTF8Encoding(false));
        Application.logMessageReceivedThreaded += (message, stack, type) => { lock (s_Logs) s_Logs.Add(type + ": " + message); };
        Row("whiteTexture.name", Texture2D.whiteTexture.name);
        float x = 0f;
        var cases = new (string name, Func<Mesh> mesh, Func<Mesh, int> id, bool convex, MeshColliderCookingOptions? options, bool thread)[]
        {
            ("plain", Cube, m => m.GetInstanceID(), false, null, false),
            ("convex", Cube, m => m.GetInstanceID(), true, null, false),
            ("options", Cube, m => m.GetInstanceID(), false, MeshColliderCookingOptions.CookForFasterSimulation | MeshColliderCookingOptions.UseFastMidphase, false),
            ("nooptions", Cube, m => m.GetInstanceID(), false, MeshColliderCookingOptions.None, false),
            ("thread", Cube, m => m.GetInstanceID(), false, null, true),
            ("zero", () => null, m => 0, false, null, false),
            ("notmesh", () => null, m => Texture2D.whiteTexture.GetInstanceID(), false, null, false),
            ("notmeshnamed", () => null, m => new Material(Shader.Find("Standard")) { name = "named material" }.GetInstanceID(), false, null, false),
            ("destroyed", () => null, m => { var d = Cube(); int id = d.GetInstanceID(); DestroyImmediate(d); return id; }, false, null, false),
            ("empty", () => new Mesh { name = "empty" }, m => m.GetInstanceID(), false, null, false),
            ("emptyconvex", () => new Mesh { name = "empty" }, m => m.GetInstanceID(), true, null, false),
            ("unreadable", () => { var m = Cube(); m.UploadMeshData(true); return m; }, m => m.GetInstanceID(), false, null, false),
            ("unreadableconvex", () => { var m = Cube(); m.UploadMeshData(true); return m; }, m => m.GetInstanceID(), true, null, false),
        };
        foreach (var c in cases)
        {
            lock (s_Logs) s_Logs.Clear();
            var mesh = c.mesh();
            int id = c.id(mesh);
            string threw = "none";
            try
            {
                Action bake = () =>
                {
                    if (c.options.HasValue) Physics.BakeMesh(id, c.convex, c.options.Value);
                    else Physics.BakeMesh(id, c.convex);
                };
                if (c.thread) Task.Run(bake).Wait();
                else bake();
            }
            catch (Exception e) { threw = (e is AggregateException a ? a.InnerException : e).GetType().Name + ": " + (e is AggregateException b ? b.InnerException.Message : e.Message); }
            Row(c.name + ".threw", threw);
            lock (s_Logs) Row(c.name + ".logs", string.Join(" | ", s_Logs));
            if (mesh != null)
            {
                lock (s_Logs) s_Logs.Clear();
                var go = new GameObject("collider " + c.name);
                go.transform.position = new Vector3(x, 0f, 0f);
                var collider = go.AddComponent<MeshCollider>();
                if (c.options.HasValue) collider.cookingOptions = c.options.Value;
                collider.convex = c.convex;
                collider.sharedMesh = mesh;
                Physics.SyncTransforms();
                bool hit = Physics.Raycast(new Vector3(x, 5f, 0f), Vector3.down, out var info, 10f) && info.collider == collider;
                Row(c.name + ".hit", hit.ToString());
                lock (s_Logs) Row(c.name + ".colliderLogs", string.Join(" | ", s_Logs));
            }
            x += 10f;
        }
        output.Write("{\"k\":\"complete\",\"v\":true}\n");
        output.Dispose();
        EditorApplication.Exit(0);
    }
}
