using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Records, in Play mode, the material.* bindings AnimationUtility.GetAnimatableBindings offers for
/// a renderer whose material uses one of three probe shaders: Q758/Forward and Q758/Reverse declare
/// every property type (attributed ones too) in opposite orders under names of their own (Unity
/// numbers a shader's names when it loads it, in an order the probe cannot set, so no two probe
/// shaders share a name), Q758/Builtin mixes built-in names (_MainTex, _Color, _Cutoff) with new ones. For each shader on a SpriteRenderer and
/// on a MeshRenderer, the float and object reference bindings in the Editor's order; then a material
/// with a value set for a name its shader lacks, a MeshRenderer with two materials, and the renderers'
/// default material. Shader.PropertyToID of every name involved goes to ids.txt (session ids).
/// </summary>
static class AnimMaterialBindingsProbe
{
    static readonly StringBuilder s_Out = new StringBuilder();

    static void Put(string key, string value) =>
        s_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"}\n");

    static string Bindings(GameObject go, Type type)
    {
        var bindings = AnimationUtility.GetAnimatableBindings(go, go).Where(b => b.type == type).ToList();
        string List(IEnumerable<EditorCurveBinding> list) => string.Join(",", list.Select(b => b.propertyName));
        var floats = bindings.Where(b => !b.isDiscreteCurve && !b.isPPtrCurve).ToList();
        var discrete = bindings.Where(b => b.isDiscreteCurve && !b.isPPtrCurve).ToList();
        var pptr = bindings.Where(b => b.isPPtrCurve).ToList();
        return $"float({floats.Count})={List(floats)}|discrete({discrete.Count})={List(discrete)}|pptr({pptr.Count})={List(pptr)}";
    }

    static void Record(string key, Type type, Action<Renderer> setup)
    {
        var go = new GameObject("probe");
        try
        {
            var renderer = (Renderer)go.AddComponent(type);
            setup?.Invoke(renderer);
            Put(key, Bindings(go, type));
        }
        catch (Exception e) { Put(key, "throws " + e.GetType().Name + ": " + e.Message); }
        finally { UnityEngine.Object.DestroyImmediate(go); }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var directory = Environment.GetEnvironmentVariable("ANIM_MATERIAL_BINDINGS_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var order = Environment.GetEnvironmentVariable("ANIM_MATERIAL_BINDINGS_FIND_ORDER");
        if (!string.IsNullOrEmpty(order))
            foreach (var n in order.Split(',')) Shader.Find("Q758/" + n);
        var shaders = new[] { "Forward", "Reverse", "Builtin" }.Select(n => Shader.Find("Q758/" + n)).ToArray();
        Put("shaders", string.Join(",", shaders.Select(s => s == null ? "null" : s.name + ":" + s.GetPropertyCount())));
        Record("default/SpriteRenderer", typeof(SpriteRenderer), null);
        Record("default/MeshRenderer", typeof(MeshRenderer), null);
        // A built-in shader whose names the Editor registers at startup.
        var standard = Shader.Find("Standard");
        if (standard != null) Record("standard/MeshRenderer", typeof(MeshRenderer), r => r.sharedMaterial = new Material(standard));
        foreach (var shader in shaders.Where(s => s != null))
        {
            var name = shader.name.Substring(5);
            Record($"{name}/SpriteRenderer", typeof(SpriteRenderer), r => r.sharedMaterial = new Material(shader));
            Record($"{name}/MeshRenderer", typeof(MeshRenderer), r => r.sharedMaterial = new Material(shader));
        }
        if (shaders[0] != null && shaders[1] != null)
        {
            Record("extra/MeshRenderer", typeof(MeshRenderer), r =>
            {
                var m = new Material(shaders[0]);
                m.SetFloat("_QExtraFloat", 2f);
                m.SetVector("_QExtraVector", Vector4.one);
                r.sharedMaterial = m;
            });
            Record("two/MeshRenderer", typeof(MeshRenderer), r => r.sharedMaterials = new[] { new Material(shaders[2] ?? shaders[1]), new Material(shaders[0]) });
            Record("swapped/MeshRenderer", typeof(MeshRenderer), r =>
            {
                var m = new Material(shaders[0]);
                m.shader = shaders[1];
                r.sharedMaterial = m;
            });
        }
        var names = new List<string>();
        foreach (var shader in shaders.Where(s => s != null))
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                var n = shader.GetPropertyName(i);
                names.Add(n);
                if (shader.GetPropertyType(i) == UnityEngine.Rendering.ShaderPropertyType.Texture)
                    names.AddRange(new[] { n + "_ST", n + "_TexelSize", n + "_HDR" });
            }
        names.AddRange(new[] { "_QExtraFloat", "_QExtraVector", "_EnableExternalAlpha", "PixelSnap", "_RendererColor", "_Flip", "_AlphaTex", "_AlphaTex_ST" });
        // The ids are the session's (not a reference value): ids.txt beside the values, for study.
        var ids = string.Join("\n", names.Distinct().Select(n => n + " " + Shader.PropertyToID(n)));
        var candidates = Environment.GetEnvironmentVariable("ANIM_MATERIAL_BINDINGS_CANDIDATES");
        if (!string.IsNullOrEmpty(candidates) && File.Exists(candidates))
            ids += "\n# candidates\n" + string.Join("\n", File.ReadAllLines(candidates).Where(l => l.Length > 0).Select(n => n + " " + Shader.PropertyToID(n)));
        var loaded = new SortedDictionary<int, string>();
        foreach (var shader in Resources.FindObjectsOfTypeAll<Shader>())
            for (int i = 0; i < shader.GetPropertyCount(); i++)
            {
                loaded[shader.GetPropertyNameId(i)] = shader.GetPropertyName(i);
                if (shader.GetPropertyType(i) != UnityEngine.Rendering.ShaderPropertyType.Texture) continue;
                foreach (var suffix in new[] { "_ST", "_TexelSize", "_HDR" })
                    loaded[Shader.PropertyToID(shader.GetPropertyName(i) + suffix)] = shader.GetPropertyName(i) + suffix;
            }
        ids += "\n# loaded shaders' properties by id\n" + string.Join("\n", loaded.Select(e => e.Key + " " + e.Value));
        s_Out.Append("{\"k\":\"complete\",\"v\":true}\n");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "ids.txt"), ids + "\n");
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), s_Out.ToString());
        EditorApplication.Exit(0);
    }
}
