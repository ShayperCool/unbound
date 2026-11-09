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
        foreach (var model in Resources.LoadAll<GameObject>(Environment.GetEnvironmentVariable("AXIS_SET") ?? "Axis").OrderBy(m => m.name, StringComparer.Ordinal))
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
                var skinned = t.GetComponent<SkinnedMeshRenderer>();
                var mesh = filter != null ? filter.sharedMesh : skinned != null ? skinned.sharedMesh : null;
                if (mesh == null) continue;
                Add(k + "/vertices", L(mesh.vertices, V));
                // Normals and skinning keys exist only for the Pivots and Skinned sets.
                if (mesh.normals.Length > 0) Add(k + "/normals", L(mesh.normals, V));
                if (filter != null) Add(k + "/worldVertices", L(mesh.vertices, v => V(t.TransformPoint(v))));
                Add(k + "/indices", L(mesh.triangles, i => i.ToString(CultureInfo.InvariantCulture)));
                Add(k + "/meshBounds", "[" + V(mesh.bounds.center) + "," + V(mesh.bounds.size) + "]");
                if (skinned == null) continue;
                var root = instance.transform;
                Add(k + "/rootBone", skinned.rootBone == null ? "null" : Q(PathOf(skinned.rootBone, root)));
                Add(k + "/bones", L(skinned.bones, b => b == null ? "null" : Q(PathOf(b, root))));
                Add(k + "/bindposes", L(mesh.bindposes, m => "[" + string.Join(",", Enumerable.Range(0, 16).Select(i => F(m[i]))) + "]"));
                Add(k + "/updateWhenOffscreen", skinned.updateWhenOffscreen ? "true" : "false");
                // With offscreen updates localBounds is the renderer's own update, not the import's.
                if (!skinned.updateWhenOffscreen)
                    Add(k + "/localBounds", "[" + V(skinned.localBounds.center) + "," + V(skinned.localBounds.extents) + "]");
                var baked = new Mesh();
                skinned.BakeMesh(baked);
                Add(k + "/bakedVertices", L(baked.vertices, V));
                DestroyImmediate(baked);
            }
            // Legacy clips (Pivots and Rigged sets): the local pose at frame-aligned times, in
            // the Rigged set also between frames. The Reduced set samples every clip of the
            // model, Legacy or not, every 1/60 s over its length.
            var animation = instance.GetComponent<Animation>();
            var set = Environment.GetEnvironmentVariable("AXIS_SET");
            var times = set == "Rigged" ? new[] { 0.25f, 0.5f, 0.55f, 0.95f, 1f } : new[] { 0.5f, 1f };
            var clips = set == "Reduced"
                ? Resources.LoadAll<AnimationClip>(set + "/" + model.name)
                    .Where(c => !c.name.StartsWith("__preview__")).OrderBy(c => c.name, StringComparer.Ordinal).ToArray()
                : animation != null ? animation.Cast<AnimationState>().Select(s => s.clip).ToArray() : new AnimationClip[0];
            foreach (var clip in clips)
                    foreach (var time in set == "Reduced"
                        ? Enumerable.Range(0, Mathf.RoundToInt(clip.length * 60) + 1).Select(i => i / 60f) : times)
                    {
                        clip.SampleAnimation(instance, time);
                        foreach (var t in instance.GetComponentsInChildren<Transform>(true))
                        {
                            var k = model.name + "/" + clip.name + "@" + F(time) + "/" + PathOf(t, instance.transform);
                            Add(k + "/localPosition", V(t.localPosition));
                            Add(k + "/localScale", V(t.localScale));
                            Add(k + "/localBasis", L(new[] { Vector3.right, Vector3.up, Vector3.forward }, v => V(t.localRotation * v)));
                        }
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
