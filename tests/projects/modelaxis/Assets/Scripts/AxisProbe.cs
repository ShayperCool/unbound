using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

public class AxisProbe : MonoBehaviour
{
    readonly StringBuilder output = new StringBuilder();
    static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string L<T>(IEnumerable<T> a, Func<T,string> f) => "[" + string.Join(",", a.Select(f)) + "]";
    static string V(Vector3 v) => $"[{F(v.x)},{F(v.y)},{F(v.z)}]";
    void Add(string k, string v) => output.Append("{\"k\":").Append(Q(k)).Append(",\"v\":").Append(v).Append("}\n");
    static string PathOf(Transform t, Transform root) => t == root ? "." :
        (t.parent == root ? t.name : PathOf(t.parent, root) + "/" + t.name);

    void Start()
    {
        foreach (var model in Resources.LoadAll<GameObject>("Axis").OrderBy(m => m.name, StringComparer.Ordinal))
        {
            var instance = Instantiate(model);
            Add(model.name + "/nodes", L(instance.GetComponentsInChildren<Transform>(true).Select(t => PathOf(t,instance.transform)), Q));
            foreach (var t in instance.GetComponentsInChildren<Transform>(true))
            {
                var k = model.name + "/" + PathOf(t,instance.transform);
                Add(k + "/localPosition", V(t.localPosition));
                Add(k + "/localScale", V(t.localScale));
                Add(k + "/localBasis", L(new[] { Vector3.right, Vector3.up, Vector3.forward }, v => V(t.localRotation * v)));
                Add(k + "/worldPosition", V(t.position));
                Add(k + "/worldBasis", L(new[] { Vector3.right, Vector3.up, Vector3.forward }, v => V(t.TransformVector(v))));
                var filter = t.GetComponent<MeshFilter>();
                if (filter == null) continue;
                var mesh = filter.sharedMesh;
                Add(k + "/vertices", L(mesh.vertices, V));
                Add(k + "/worldVertices", L(mesh.vertices, v => V(t.TransformPoint(v))));
                Add(k + "/indices", L(mesh.triangles, i => i.ToString(CultureInfo.InvariantCulture)));
                Add(k + "/meshBounds", "[" + V(mesh.bounds.center) + "," + V(mesh.bounds.size) + "]");
            }
            DestroyImmediate(instance);
        }
        var directory = Environment.GetEnvironmentVariable("AXIS_OUT");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "axis.jsonl"), output.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
