using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Reflection and light probe behaviour of the built-in pipeline, written to
/// <c>REFLECTION_PROBES_OUT</c> (a directory): the mips of imported glossy-convolved cubemaps and of a
/// realtime probe's cube (binary dumps), the probes and weights renderers get
/// (GetClosestReflectionProbes), forward and deferred frames of blended probes, glossy spheres in
/// a realtime and an imported probe, and the light probe constants plain and lightmapped
/// renderers draw with. Ends with <c>complete: true</c> in values.jsonl and exits.
/// </summary>
public class ReflectionProbesProbe : MonoBehaviour
{
    public Cubemap[] imported;
    public Cubemap environment;
    public Cubemap sky;
    public Shader constants;

    const int Width = 320, Height = 180;
    const int SphereLayer = 8;
    string output;
    readonly StringBuilder values = new StringBuilder();

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    void Value(string key, string value) => values.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(value).Append("}\n");

    IEnumerator Start()
    {
        output = Environment.GetEnvironmentVariable("REFLECTION_PROBES_OUT");
        if (string.IsNullOrEmpty(output)) yield break;
        Directory.CreateDirectory(output);
        for (int i = 0; i < 3; i++) yield return null;
        // First: later sections change the sky and the ambient mode, after which the Editor
        // recomputes the ambient probe.
        try { ProbeConstants(); } catch (Exception e) { Value("sh.fatal", Quote(e.ToString())); }
        try { ImportedMips(); } catch (Exception e) { Value("import.fatal", Quote(e.ToString())); }
        yield return RealtimeMips("realtime", sky, 1f);
        // Exposure 4: the skybox draws values above 1 (an HDR probe keeps them).
        yield return RealtimeMips("realtime_hdr", sky, 4f);
        // An RGBAFloat cubemap made at run time with texels from 0.5 to 8.
        var hot = new Cubemap(16, TextureFormat.RGBAFloat, false);
        for (int face = 0; face < 6; face++)
        {
            var texels = new Color[16 * 16];
            for (int i = 0; i < texels.Length; i++)
                texels[i] = new Color(0.5f + face * 1.5f, 0.5f + (i % 16) * 0.5f, 0.5f + (i / 16) * 0.25f, 1f);
            hot.SetPixels(texels, (CubemapFace)face);
        }
        hot.Apply(false);
        yield return RealtimeMips("realtime_texels", hot, 1f);
        Destroy(hot);
        yield return Weights();
        yield return Frames();
        yield return GlossySpheres();
        Value("complete", "true");
        File.WriteAllText(Path.Combine(output, "values.jsonl"), values.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    static string Quote(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

    // ---- Mips -------------------------------------------------------------------------------

    /// <summary>Each imported cube's mips as RGB: float32 for float cubes, bytes for byte ones
    /// (header: size, mip count, bytes per channel).</summary>
    void ImportedMips()
    {
        foreach (var cube in imported)
        {
            bool bytes = cube.format == TextureFormat.RGBA32;
            Value("import." + cube.name + ".format", Quote(cube.format + " " + cube.width + " mips " + cube.mipmapCount));
            using var w = new BinaryWriter(File.Create(Path.Combine(output, "import_" + cube.name + ".bin")));
            w.Write(cube.width); w.Write(cube.mipmapCount); w.Write(bytes ? 1 : 4);
            for (int face = 0; face < 6; face++)
                for (int mip = 0; mip < cube.mipmapCount; mip++)
                    foreach (var p in cube.GetPixels((CubemapFace)face, mip))
                        if (bytes) { w.Write(Byte(p.r)); w.Write(Byte(p.g)); w.Write(Byte(p.b)); }
                        else { w.Write(p.r); w.Write(p.g); w.Write(p.b); }
        }
    }

    static byte Byte(float v) => (byte)Mathf.RoundToInt(Mathf.Clamp01(v) * 255f);

    /// <summary>A realtime probe of a Skybox/Cubemap skybox of <paramref name="source"/> at an exposure
    /// (nothing else drawn), its cube's mips read back as half floats (<c>&lt;name&gt;_sky32.bin</c>).</summary>
    IEnumerator RealtimeMips(string name, Cubemap source, float exposure)
    {
        var skybox = new Material(Shader.Find("Skybox/Cubemap"));
        skybox.SetTexture("_Tex", source);
        skybox.SetColor("_Tint", new Color(0.5f, 0.5f, 0.5f, 0.5f));
        skybox.SetFloat("_Exposure", exposure);
        RenderSettings.skybox = skybox;
        var probe = new GameObject("realtime").AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Realtime;
        probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
        probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
        probe.resolution = 32;
        probe.hdr = true;
        probe.clearFlags = ReflectionProbeClearFlags.Skybox;
        probe.cullingMask = 0;
        int id = probe.RenderProbe();
        for (int i = 0; i < 10 && !probe.IsFinishedRendering(id); i++) yield return null;
        var cube = probe.realtimeTexture;
        Value(name + ".texture", cube == null ? "null" : Quote(cube.format + " " + cube.width + " mips " + cube.mipmapCount));
        if (cube != null)
        {
            using var w = new BinaryWriter(File.Create(Path.Combine(output, name + "_sky32.bin")));
            w.Write(cube.width); w.Write(cube.mipmapCount); w.Write(2);
            for (int face = 0; face < 6; face++)
                for (int mip = 0; mip < cube.mipmapCount; mip++)
                {
                    int s = Math.Max(1, cube.width >> mip);
                    var read = new Texture2D(s, s, TextureFormat.RGBAFloat, false, true);
                    Graphics.SetRenderTarget(cube, mip, (CubemapFace)face);
                    read.ReadPixels(new Rect(0, 0, s, s), 0, 0);
                    read.Apply();
                    foreach (var p in read.GetPixels()) { w.Write(Mathf.FloatToHalf(p.r)); w.Write(Mathf.FloatToHalf(p.g)); w.Write(Mathf.FloatToHalf(p.b)); }
                    Destroy(read);
                }
            Graphics.SetRenderTarget(null);
        }
        Destroy(probe.gameObject);
        RenderSettings.skybox = null;
        yield return null;
    }

    // ---- Probe blending -----------------------------------------------------------------------

    static Cubemap Tinted(Color top, Color side, Color bottom)
    {
        const int size = 16;
        var cube = new Cubemap(size, TextureFormat.RGBAHalf, true);
        var pixels = new Color[size * size];
        for (int face = 0; face < 6; face++)
        {
            for (int y = 0; y < size; y++)
                for (int x = 0; x < size; x++)
                    pixels[y * size + x] = face == 2 ? top : face == 3 ? bottom
                        : Color.Lerp(top, bottom, (y + 0.5f) / size) * 0.5f + side * 0.5f;
            cube.SetPixels(pixels, (CubemapFace)face);
        }
        cube.Apply(true);
        return cube;
    }

    static ReflectionProbe Custom(string name, Vector3 position, Vector3 size, Texture cube, int importance, float blend)
    {
        var go = new GameObject(name);
        go.transform.position = position;
        var probe = go.AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Custom;
        probe.customBakedTexture = cube;
        probe.size = size;
        probe.importance = importance;
        probe.blendDistance = blend;
        return probe;
    }

    static GameObject Primitive(PrimitiveType type, Vector3 position, Vector3 scale, Material material)
    {
        var go = GameObject.CreatePrimitive(type);
        go.transform.position = position;
        go.transform.localScale = scale;
        go.GetComponent<Renderer>().sharedMaterial = material;
        return go;
    }

    static Material Metal(float smoothness)
    {
        var m = new Material(Shader.Find("Standard"));
        m.SetFloat("_Metallic", 1f);
        m.SetFloat("_Glossiness", smoothness);
        return m;
    }

    ReflectionProbe red, blue;
    readonly List<GameObject> scene = new List<GameObject>();

    void ListProbes(string key, Renderer renderer)
    {
        var list = new List<ReflectionProbeBlendInfo>();
        renderer.GetClosestReflectionProbes(list);
        var sb = new StringBuilder("[");
        for (int i = 0; i < list.Count; i++)
            sb.Append(i > 0 ? "," : "").Append("[\"").Append(list[i].probe.name).Append("\",").Append(F(list[i].weight)).Append(']');
        Value(key, sb.Append(']').ToString());
    }

    /// <summary>Two probes of equal size overlapping by half (A from x -2 to 2, B from 0 to 4),
    /// cubes along x and one across both: equal importance, B smaller, no blend distance, B more
    /// important, a third probe C between them.</summary>
    IEnumerator Weights()
    {
        var metal = Metal(0.9f);
        red = Custom("A", Vector3.zero, new Vector3(4f, 4f, 4f), Tinted(new Color(1f, 0.2f, 0.1f), new Color(0.6f, 0.1f, 0.1f), new Color(0.2f, 0.05f, 0.02f)), 1, 1f);
        blue = Custom("B", new Vector3(2f, 0f, 0f), new Vector3(4f, 4f, 4f), Tinted(new Color(0.1f, 0.3f, 1f), new Color(0.1f, 0.1f, 0.6f), new Color(0.02f, 0.05f, 0.2f)), 1, 1f);
        var renderers = new List<(string, Renderer)>();
        foreach (float x in new[] { -1.5f, 0f, 0.5f, 1f, 1.25f, 1.5f, 2f, 3.5f, 4.2f })
            renderers.Add(("x" + F(x), Primitive(PrimitiveType.Cube, new Vector3(x, 0f, 0f), Vector3.one * 0.5f, metal).GetComponent<Renderer>()));
        renderers.Add(("wide", Primitive(PrimitiveType.Cube, new Vector3(1f, 0f, 0f), new Vector3(6f, 0.5f, 0.5f), metal).GetComponent<Renderer>()));
        renderers.Add(("straddle", Primitive(PrimitiveType.Cube, new Vector3(-2f, 0f, 0f), new Vector3(1f, 0.5f, 0.5f), metal).GetComponent<Renderer>()));
        void All(string label) { foreach (var (n, r) in renderers) ListProbes("weights." + label + "." + n, r); }
        yield return null;
        All("equal");
        blue.size = new Vector3(2f, 2f, 2f);
        yield return null;
        All("smallB");
        blue.size = new Vector3(4f, 4f, 4f);
        red.blendDistance = blue.blendDistance = 0f;
        yield return null;
        All("noblend");
        red.blendDistance = blue.blendDistance = 1f;
        blue.importance = 2;
        yield return null;
        All("importantB");
        blue.importance = 1;
        var green = Custom("C", new Vector3(1f, 0f, 0f), new Vector3(3f, 3f, 3f), Tinted(new Color(0.2f, 1f, 0.2f), new Color(0.1f, 0.6f, 0.1f), new Color(0.02f, 0.2f, 0.05f)), 1, 0.5f);
        yield return null;
        All("three");
        Destroy(green.gameObject);
        foreach (var (_, r) in renderers) Destroy(r.gameObject);
        yield return null;
    }

    void Capture(Camera camera, string name)
    {
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var read = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        read.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        read.Apply();
        File.WriteAllBytes(Path.Combine(output, name + ".png"), read.EncodeToPNG());
        RenderTexture.active = null;
        camera.targetTexture = null;
        Destroy(target);
        Destroy(read);
    }

    Camera MakeCamera(Vector3 position, Vector3 target)
    {
        var camera = new GameObject("camera").AddComponent<Camera>();
        camera.transform.position = position;
        camera.transform.LookAt(target);
        camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.gray;
        camera.allowMSAA = false;
        scene.Add(camera.gameObject);
        return camera;
    }

    /// <summary>A glossy floor and spheres across the two probes, forward and deferred: equal
    /// importance, B more important, no blend distance, box projection, B smaller; B made more
    /// important and rendered in the same frame and in the next (lag_same, lag_next).</summary>
    IEnumerator Frames()
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = Color.black;
        scene.Add(Primitive(PrimitiveType.Plane, new Vector3(1f, -1f, 0f), Vector3.one, Metal(0.8f)));
        var metal = Metal(0.9f);
        Renderer overlap = null;
        foreach (float x in new[] { -1f, 0.5f, 1f, 1.5f, 3f })
        {
            var sphere = Primitive(PrimitiveType.Sphere, new Vector3(x, -0.4f, 0f), Vector3.one * 0.8f, metal);
            scene.Add(sphere);
            if (x == 1f) overlap = sphere.GetComponent<Renderer>();
        }
        var light = new GameObject("light").AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 0.3f;
        light.transform.rotation = Quaternion.Euler(50f, -30f, 0f);
        scene.Add(light.gameObject);
        var camera = MakeCamera(new Vector3(1f, 1.5f, -5f), new Vector3(1f, -0.5f, 0f));
        foreach (var path in new[] { RenderingPath.Forward, RenderingPath.DeferredShading })
        {
            camera.renderingPath = path;
            string prefix = path == RenderingPath.Forward ? "forward" : "deferred";
            // A frame after each change: forward draws take probe changes from the next frame on.
            yield return null;
            Capture(camera, prefix + "_equal");
            blue.importance = 2;
            yield return null;
            Capture(camera, prefix + "_importantB");
            blue.importance = 1;
            red.blendDistance = blue.blendDistance = 0f;
            yield return null;
            Capture(camera, prefix + "_noblend");
            red.blendDistance = blue.blendDistance = 1f;
            red.boxProjection = blue.boxProjection = true;
            yield return null;
            Capture(camera, prefix + "_box");
            red.boxProjection = blue.boxProjection = false;
            blue.size = new Vector3(2f, 2f, 2f);
            yield return null;
            Capture(camera, prefix + "_smallB");
            blue.size = new Vector3(4f, 4f, 4f);
            // A change and a render in the same frame (lag_same), then the next frame (lag_next);
            // the probes the sphere in both gets (GetClosestReflectionProbes) at each.
            yield return null;
            blue.importance = 2;
            ListProbes("lag." + prefix + ".same", overlap);
            Capture(camera, prefix + "_lag_same");
            yield return null;
            ListProbes("lag." + prefix + ".next", overlap);
            Capture(camera, prefix + "_lag_next");
            blue.importance = 1;
        }
        Destroy(red.gameObject);
        Destroy(blue.gameObject);
        foreach (var go in scene) Destroy(go);
        scene.Clear();
        yield return null;
    }

    /// <summary>Metal spheres of falling smoothness reflecting a realtime probe of coloured boxes,
    /// then a custom probe of the glossy-convolved <see cref="environment"/>.</summary>
    IEnumerator GlossySpheres()
    {
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.05f, 0.05f, 0.05f);
        var colours = new[] { Color.red, Color.green, Color.blue, Color.yellow, Color.white, Color.cyan };
        var directions = new[] { Vector3.right, Vector3.left, Vector3.up, Vector3.down, Vector3.forward, Vector3.back };
        for (int i = 0; i < 6; i++)
        {
            var m = new Material(Shader.Find("Unlit/Color")) { color = colours[i] };
            var box = Primitive(PrimitiveType.Cube, directions[i] * 3f + new Vector3(0.5f * i - 1f, 0.3f * i, 0f), Vector3.one * 1.5f, m);
            scene.Add(box);
        }
        float[] smoothness = { 1f, 0.9f, 0.75f, 0.5f };
        for (int i = 0; i < smoothness.Length; i++)
        {
            var sphere = Primitive(PrimitiveType.Sphere, new Vector3(-1.5f + i, 0f, 0f), Vector3.one * 0.9f, Metal(smoothness[i]));
            sphere.layer = SphereLayer;
            scene.Add(sphere);
        }
        var probe = new GameObject("realtime").AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Realtime;
        probe.refreshMode = ReflectionProbeRefreshMode.ViaScripting;
        probe.timeSlicingMode = ReflectionProbeTimeSlicingMode.NoTimeSlicing;
        probe.resolution = 64;
        probe.size = new Vector3(20f, 20f, 20f);
        probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
        probe.backgroundColor = new Color(0.2f, 0.2f, 0.25f);
        probe.cullingMask = ~(1 << SphereLayer);
        int id = probe.RenderProbe();
        for (int i = 0; i < 10 && !probe.IsFinishedRendering(id); i++) yield return null;
        var camera = MakeCamera(new Vector3(0f, 0.6f, -3.2f), Vector3.zero);
        camera.cullingMask = 1 << SphereLayer;
        Capture(camera, "glossy_realtime");
        Destroy(probe.gameObject);
        var custom = Custom("environment", Vector3.zero, new Vector3(20f, 20f, 20f), environment, 1, 0f);
        yield return null;
        Capture(camera, "glossy_imported");
        Destroy(custom.gameObject);
        foreach (var go in scene) Destroy(go);
        scene.Clear();
        yield return null;
    }

    // ---- Light probe constants ---------------------------------------------------------------

    /// <summary>The probe constants (<see cref="constants"/>, read into a float pixel) of a quad
    /// that is not lightmapped and of one that is (index 0 of a runtime lightmap), with probes
    /// blended or off, in forward and deferred, under a set ambient probe and no light probes.</summary>
    void ProbeConstants()
    {
        RenderSettings.ambientMode = AmbientMode.Skybox;
        var ambient = new SphericalHarmonicsL2();
        ambient.AddAmbientLight(new Color(0.2f, 0.3f, 0.4f));
        ambient.AddDirectionalLight(new Vector3(0.3f, 0.8f, 0.5f).normalized, new Color(0.8f, 0.5f, 0.3f), 1f);
        RenderSettings.ambientProbe = ambient;
        LightmapSettings.lightmaps = new[] { new LightmapData { lightmapColor = new Texture2D(4, 4, TextureFormat.RGBAHalf, false) } };
        var material = new Material(constants);
        var quad = Primitive(PrimitiveType.Quad, new Vector3(0f, 0f, 10f), Vector3.one * 50f, material);
        var renderer = quad.GetComponent<MeshRenderer>();
        var camera = new GameObject("constants").AddComponent<Camera>();
        camera.enabled = false;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        var target = new RenderTexture(1, 1, 24, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        var read = new Texture2D(1, 1, TextureFormat.RGBAFloat, false, true);
        var cases = new[] { ("plain", -1, LightProbeUsage.BlendProbes), ("lightmapped", 0, LightProbeUsage.BlendProbes), ("probesoff", -1, LightProbeUsage.Off), ("lightmapped_probesoff", 0, LightProbeUsage.Off) };
        foreach (var (label, index, usage) in cases)
        {
            renderer.lightmapIndex = index;
            renderer.lightmapScaleOffset = new Vector4(0.5f, 0.25f, 0.125f, 0.0625f);
            renderer.lightProbeUsage = usage;
            foreach (var path in new[] { RenderingPath.Forward, RenderingPath.DeferredShading })
            {
                camera.renderingPath = path;
                // unity_SHAr..unity_SHC, unity_LightmapST (unity_ProbesOcclusion is not recorded).
                for (int which = 0; which < 8; which++)
                {
                    material.SetFloat("_Which", which);
                    camera.targetTexture = target;
                    camera.Render();
                    RenderTexture.active = target;
                    read.ReadPixels(new Rect(0, 0, 1, 1), 0, 0);
                    read.Apply();
                    var v = read.GetPixel(0, 0);
                    Value("sh." + label + "." + (path == RenderingPath.Forward ? "forward" : "deferred") + "." + which,
                        "[" + F(v.r) + "," + F(v.g) + "," + F(v.b) + "," + F(v.a) + "]");
                }
            }
        }
        RenderTexture.active = null;
        camera.targetTexture = null;
        Destroy(quad);
        Destroy(camera.gameObject);
    }
}
