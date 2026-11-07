using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

public class MtlProbe : MonoBehaviour
{
    readonly StringBuilder output = new StringBuilder();
    string directory;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("MTL_OUT")))
            new GameObject("MtlProbe").AddComponent<MtlProbe>();
    }

    static string Q(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string L<T>(IEnumerable<T> values, Func<T, string> f) => "[" + string.Join(",", values.Select(f)) + "]";
    static string V(Vector3 v) => $"[{F(v.x)},{F(v.y)},{F(v.z)}]";
    static string C(Color c) => $"[{F(c.r)},{F(c.g)},{F(c.b)},{F(c.a)}]";
    void Add(string key, string value) => output.Append("{\"k\":").Append(Q(key)).Append(",\"v\":").Append(value).Append("}\n");
    void Start()
    {
        directory = Environment.GetEnvironmentVariable("MTL_OUT");
        Directory.CreateDirectory(directory);
        foreach (var name in new[] { "Description", "Legacy", "DescriptionLinearColor", "LegacyLinearColor", "Remap", "DescriptionSolid", "LegacySolid", "DescriptionLinearColorSolid", "LegacyLinearColorSolid", "RemapSolid" })
        {
            var model = Resources.Load<GameObject>("Mtl/" + name);
            if (model == null) { Add(name + "/missing", "true"); continue; }
            var instance = Instantiate(model);
            var renderer = instance.GetComponentInChildren<Renderer>();
            var mesh = renderer.GetComponent<MeshFilter>().sharedMesh;
            Add(name + "/subMeshCount", mesh.subMeshCount.ToString());
            Add(name + "/indices", L(Enumerable.Range(0, mesh.subMeshCount), i => L(mesh.GetIndices(i), x => x.ToString())));
            var bindings = renderer.sharedMaterials;
            Add(name + "/names", L(bindings, m => Q(m.name)));
            foreach (var m in bindings)
            {
                var key = name + "/" + m.name;
                Add(key + "/shader", Q(m.shader.name));
                Add(key + "/color", C(m.color));
                Add(key + "/queue", m.renderQueue.ToString());
                Add(key + "/keywords", L(m.shaderKeywords.OrderBy(x => x, StringComparer.Ordinal), Q));
                foreach (var prop in new[] { "_Mode", "_SrcBlend", "_DstBlend", "_ZWrite", "_Glossiness", "_Metallic" })
                    if (m.HasProperty(prop)) Add(key + "/" + prop, F(m.GetFloat(prop)));
                Add(key + "/texture", m.mainTexture == null ? "null" : Q(m.mainTexture.name));
            }
            // Mesh-local faces keep the separate imported-transform gap out of this probe.
            renderer.transform.position = Vector3.zero;
            renderer.transform.rotation = Quaternion.identity;
            renderer.transform.localScale = Vector3.one;
            // No environment lighting: imported Standard materials show their alpha and albedo
            // under a directional light, with reflections disabled on the renderer.
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            var lightObject = new GameObject("MtlLight");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1;
            light.transform.rotation = Quaternion.Euler(0, 180, 0);
            Image(name + "-assigned", new[] { renderer });
            DestroyImmediate(lightObject);
            var owned = bindings.Select(m => new Material(Shader.Find("Unlit/Color")) { color = m.color }).ToArray();
            renderer.sharedMaterials = owned;
            Image(name + "-colors", new[] { renderer });
            DestroyImmediate(instance);
            foreach (var m in owned) DestroyImmediate(m);
        }
        // A separate second pass leaves the original environment-lit captures untouched.
        // Flat black ambient isolates material conversion from the default sky's lighting data.
        RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;
        RenderSettings.reflectionIntensity = 0;
        foreach (var name in new[] { "Description", "Legacy", "DescriptionLinearColor", "LegacyLinearColor", "Remap",
                                    "DescriptionSolid", "LegacySolid", "DescriptionLinearColorSolid", "LegacyLinearColorSolid", "RemapSolid" })
        {
            var instance = Instantiate(Resources.Load<GameObject>("Mtl/" + name));
            var renderer = instance.GetComponentInChildren<Renderer>();
            renderer.transform.position = Vector3.zero;
            renderer.transform.rotation = Quaternion.identity;
            renderer.transform.localScale = Vector3.one;
            renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
            renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            var lightObject = new GameObject("MtlDirectLight");
            var light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1;
            light.transform.rotation = Quaternion.Euler(0, 180, 0);
            Image(name + "-direct", new[] { renderer });
            DestroyImmediate(lightObject);
            DestroyImmediate(instance);
        }
        File.WriteAllText(Path.Combine(directory, "mtl.jsonl"), output.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    void Image(string name, Renderer[] renderers)
    {
        var bounds = renderers[0].bounds;
        foreach (var renderer in renderers.Skip(1)) bounds.Encapsulate(renderer.bounds);
        var go = new GameObject("SlotCamera");
        var camera = go.AddComponent<Camera>();
        camera.orthographic = true;
        camera.orthographicSize = Mathf.Max(bounds.size.y * 0.6f, bounds.size.x * 0.6f / 2.5f);
        camera.transform.position = bounds.center + Vector3.forward * 10;
        camera.transform.rotation = Quaternion.Euler(0, 180, 0);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        camera.nearClipPlane = 0.01f;
        camera.farClipPlane = 100;
        var rt = new RenderTexture(160, 64, 24, RenderTextureFormat.ARGB32);
        rt.Create();
        camera.targetTexture = rt;
        camera.Render();
        RenderTexture.active = rt;
        var texture = new Texture2D(160, 64, TextureFormat.RGB24, false);
        texture.ReadPixels(new Rect(0, 0, 160, 64), 0, 0);
        texture.Apply();
        File.WriteAllBytes(Path.Combine(directory, name + ".png"), texture.EncodeToPNG());
        RenderTexture.active = null;
        camera.targetTexture = null;
        DestroyImmediate(texture);
        rt.Release();
        DestroyImmediate(rt);
        DestroyImmediate(go);
    }
}
