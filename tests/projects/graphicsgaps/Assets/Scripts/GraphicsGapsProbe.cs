using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

/// <summary>
/// Graphics members render pipelines, packages and editor tools reach at run time (see README.md).
/// On the third frame of Play mode writes values.jsonl into $GRAPHICS_GAPS_OUT
/// (tests/value_probe.py), one section after the other; a section that throws records
/// `<section>.exception` and the next one runs.
/// </summary>
public class GraphicsGapsProbe : MonoBehaviour
{
    static string s_Directory;
    static StreamWriter s_Output;
    static readonly HashSet<string> s_Keys = new HashSet<string>();
    static readonly List<string> s_Logs = new List<string>();
    static readonly SortedSet<string> s_QueueErrors = new SortedSet<string>(StringComparer.Ordinal);
    static bool s_Capturing;

    static readonly string[] Selection =
    {
        "Probe/SelA", "Probe/SelB", "Probe/SelC", "Probe/SelD", "Probe/SelE", "Probe/SelF", "Probe/SelH",
    };

    /// <summary>Shaders whose first SubShader the device may not run: its only pass excludes
    /// OpenGL Core, one of its two passes does, its only pass runs on Vulkan alone, a pass has a
    /// geometry stage, a pass has tessellation stages (each with an untagged second SubShader).</summary>
    static readonly string[] Restricted =
    {
        "Probe/SelX", "Probe/SelY", "Probe/SelO", "Probe/SelG", "Probe/SelT",
    };

    static readonly string[] Builtin =
    {
        "Skybox/Procedural", "Skybox/6 Sided", "Standard", "Unlit/Transparent", "Unlit/Texture", "Sprites/Default", "UI/Default",
        "Legacy Shaders/Diffuse", "Legacy Shaders/Transparent/Diffuse", "Particles/Standard Unlit", "Hidden/InternalErrorShader",
        "Universal Render Pipeline/Lit", "Universal Render Pipeline/Unlit", "Universal Render Pipeline/Simple Lit",
        "Universal Render Pipeline/Baked Lit", "Universal Render Pipeline/Particles/Lit", "Universal Render Pipeline/Particles/Unlit",
        "Universal Render Pipeline/Terrain/Lit", "Universal Render Pipeline/Nature/SpeedTree8_PBRLit",
    };

    static string Escape(string s)
    {
        var b = new StringBuilder();
        foreach (var c in s)
        {
            if (c == '"' || c == '\\') b.Append('\\').Append(c);
            else if (c == '\n') b.Append("\\n");
            else if (c == '\r') b.Append("\\r");
            else if (c == '\t') b.Append("\\t");
            else if (c < ' ') b.Append("\\u").Append(((int)c).ToString("x4"));
            else b.Append(c);
        }
        return b.ToString();
    }

    static void Row(string key, object value)
    {
        if (!s_Keys.Add(key)) key += ".again" + s_Keys.Count;
        s_Output.Write("{\"k\":\"" + Escape(key) + "\",\"v\":\"" + Escape(Text(value)) + "\"}\n");
    }

    static string Text(object value)
    {
        switch (value)
        {
            case null: return "null";
            case float f: return f.ToString("R", CultureInfo.InvariantCulture);
            case double d: return d.ToString("R", CultureInfo.InvariantCulture);
            case bool b: return b ? "true" : "false";
            case Object o: return o != null ? o.name : "null";
            case Vector4 v: return Text(v.x) + "," + Text(v.y) + "," + Text(v.z) + "," + Text(v.w);
            case Vector2 v: return Text(v.x) + "," + Text(v.y);
            case Color c: return Text(c.r) + "," + Text(c.g) + "," + Text(c.b) + "," + Text(c.a);
            case Color32 c: return c.r + "," + c.g + "," + c.b + "," + c.a;
            case Matrix4x4 m:
                var parts = new string[16];
                for (int i = 0; i < 16; i++) parts[i] = Text(m[i]);
                return string.Join(",", parts);
            default: return Convert.ToString(value, CultureInfo.InvariantCulture);
        }
    }

    /// <summary>A read: its value, or the exception's type and message.</summary>
    static void Read(string key, Func<object> read)
    {
        try { Row(key, read()); }
        catch (Exception e) { Row(key + ".exception", e.GetType().Name + ": " + e.Message); }
    }

    static void Section(string name, Action body)
    {
        try { body(); }
        catch (Exception e) { Row(name + ".exception", e.GetType().Name + ": " + e.Message); }
    }

    /// <summary>The log lines a block of the probe caused (an Editor's own and Unbound's notes are
    /// left out: lines starting "Unbound:").</summary>
    static void Logs(string key)
    {
        var lines = s_Logs.Where(l => !l.Contains("Unbound:")).ToArray();
        Row(key, lines.Length + (lines.Length > 0 ? " " + string.Join(" | ", lines) : ""));
        s_Logs.Clear();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
    static void Listen()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("GRAPHICS_GAPS_OUT"))) return;
        s_Capturing = true;
        Application.logMessageReceived += (message, stack, type) =>
        {
            if (!s_Capturing) return;
            var line = type + ": " + message.Split('\n')[0];
            // Invalid Queue tags are reported as shaders load and again as the pipeline tag
            // changes: recorded once each, apart from the other lines (queue.errors).
            if (line.Contains(" uses undefined Queue: ")) s_QueueErrors.Add(line);
            else s_Logs.Add(line);
        };
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Launch()
    {
        s_Directory = Environment.GetEnvironmentVariable("GRAPHICS_GAPS_OUT");
        if (string.IsNullOrEmpty(s_Directory)) return;
        var camera = new GameObject("probe camera").AddComponent<Camera>();
        camera.gameObject.AddComponent<GraphicsGapsProbe>();
    }

    void Update()
    {
        if (Time.frameCount < 3) return;
        enabled = false;
        try
        {
            Directory.CreateDirectory(s_Directory);
            using (s_Output = new StreamWriter(Path.Combine(s_Directory, "values.jsonl")))
            {
                // The start-up log lines (the pipeline and its GPU Resident Drawer are made
                // meanwhile; the Game view may make them more than once): each line once, without
                // the Editor's note on its own objects of a disabled built-in package.
                Row("startup.logs", string.Join(" | ", new SortedSet<string>(s_Logs.Where(l => !l.Contains("Unbound:")
                    && !l.Contains("Component belongs to a disabled built-in package")), StringComparer.Ordinal)));
                s_Logs.Clear();
                Section("context", Context);
                Section("select", SelectionCases);
                Section("builtin", BuiltinShaders);
                Section("queue", QueueForms);
                Section("keywords", Keywords);
                Section("sparse", Sparse);
                Section("anamorphism", Anamorphism);
                Section("lightmapping", LightmappingMembers);
                Section("materialProperty", MaterialProperties);
                Section("meshUtility", MeshUtilityMembers);
                Section("objectId", ObjectIds);
                Section("layers", RenderingLayers);
                Section("rebind", Rebind);
                Section("cameraUtils", CameraUtils);
                Section("select.restricted", RestrictedSelection);
                Logs("end.logs");
                Row("queue.errors", string.Join(" | ", s_QueueErrors.Where(l => !l.Contains("Shader Unknown "))));
                Row("queue.errors.unknown", string.Join(" | ", s_QueueErrors.Where(l => l.Contains("Shader Unknown "))));
                s_Output.Write("{\"k\":\"complete\",\"v\":true}\n");
            }
            EditorApplication.Exit(0);
        }
        catch (Exception exception) { Debug.LogException(exception); EditorApplication.Exit(1); }
    }

    static void Context()
    {
        Row("context.device", SystemInfo.graphicsDeviceType);
        Row("context.pipeline", GraphicsSettings.currentRenderPipeline);
        Row("context.globalRenderPipeline", Shader.globalRenderPipeline);
        Row("context.globalMaximumLOD", Shader.globalMaximumLOD);
        Read("context.supportsSparseTextures", () => SystemInfo.supportsSparseTextures);
        Read("context.supportsTextureWrapMirrorOnce", () => SystemInfo.supportsTextureWrapMirrorOnce);
        Read("context.brgBufferTarget", () => BatchRendererGroup.BufferTarget);
        Logs("context.logs");
    }

    // ---- SubShader selection ----

    static RenderTexture s_Target;
    static Mesh s_Quad;

    /// <summary>The colour pass 0 of the material's selected SubShader draws (red: the SubShader's
    /// code; "none" when nothing is drawn).</summary>
    static string Drawn(Material material)
    {
        if (s_Target == null)
        {
            s_Target = new RenderTexture(4, 4, 0, GraphicsFormat.R8G8B8A8_UNorm) { name = "select" };
            s_Quad = new Mesh();
            s_Quad.vertices = new[] { new Vector3(-1, -1, 0), new Vector3(-1, 1, 0), new Vector3(1, 1, 0), new Vector3(1, -1, 0) };
            s_Quad.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        }
        var buffer = new CommandBuffer();
        buffer.SetRenderTarget(s_Target);
        buffer.ClearRenderTarget(true, true, new Color(1, 1, 1, 0));
        buffer.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
        buffer.DrawMesh(s_Quad, Matrix4x4.identity, material, 0, 0);
        Graphics.ExecuteCommandBuffer(buffer);
        buffer.Release();
        var previous = RenderTexture.active;
        RenderTexture.active = s_Target;
        var read = new Texture2D(4, 4, TextureFormat.RGBA32, false);
        read.ReadPixels(new Rect(0, 0, 4, 4), 0, 0);
        RenderTexture.active = previous;
        Color32 c = read.GetPixel(2, 2);
        Object.DestroyImmediate(read);
        return c.a == 0 ? "none" : c.r + "," + c.g;
    }

    static void SelectionRead(string prefix) => SelectionRead(prefix, Selection);

    static void SelectionRead(string prefix, string[] names)
    {
        foreach (var name in names)
        {
            var shader = Shader.Find(name);
            var key = prefix + "." + name.Substring(name.IndexOf('/') + 1);
            if (shader == null) { Row(key, "not found"); continue; }
            var material = new Material(shader);
            Read(key + ".queue", () => shader.renderQueue);
            Read(key + ".passCount", () => shader.passCount);
            Read(key + ".mat.queue", () => material.renderQueue);
            Read(key + ".mat.passCount", () => material.passCount);
            Read(key + ".tag", () => material.GetTag("SubId", false, "none"));
            Read(key + ".tagFallbacks", () => material.GetTag("SubId", true, "none"));
            Read(key + ".other", () => material.GetTag("Other", false, "none"));
            Read(key + ".otherFallbacks", () => material.GetTag("Other", true, "none"));
            Read(key + ".drawn", () => Drawn(material));
            Object.DestroyImmediate(material);
        }
        Logs(prefix + ".logs");
    }

    /// <summary>The SubShaders of shaders whose first SubShader the device may not run (read
    /// last, so that the other sections run as they were recorded before it was added).</summary>
    static void RestrictedSelection()
    {
        Row("select.restricted.subshaderCount", string.Join(",", Restricted.Select(n => Shader.Find(n) != null ? Shader.Find(n).subshaderCount.ToString() : "-")));
        SelectionRead("select.restricted", Restricted);
        foreach (var name in Restricted)
        {
            var shader = Shader.Find(name);
            if (shader == null) continue;
            var key = "select.restricted." + name.Substring(name.IndexOf('/') + 1);
            Read(key + ".passCounts", () => string.Join(",", Enumerable.Range(0, shader.subshaderCount).Select(i => shader.GetPassCountInSubshader(i))));
            var material = new Material(shader);
            Read(key + ".extraPass", () => material.FindPass("ProbeExtra"));
            Read(key + ".stagePass", () => material.FindPass("ProbeStage"));
            Object.DestroyImmediate(material);
        }
    }

    static void SelectionCases()
    {
        Row("select.subshaderCount", string.Join(",", Selection.Select(n => Shader.Find(n) != null ? Shader.Find(n).subshaderCount.ToString() : "-")));
        SelectionRead("select.default");
        var original = Shader.globalRenderPipeline;
        foreach (var (label, tag) in new[]
                 {
                     ("empty", ""), ("hd", "HDRenderPipeline"), ("list", "UniversalPipeline,Foo"), ("listSpaced", "Foo, UniversalPipeline"),
                     ("lower", "universalpipeline"), ("foo", "Foo"), ("both", "HDRenderPipeline,UniversalPipeline"),
                 })
        {
            Shader.globalRenderPipeline = tag;
            Row("select." + label + ".globalRenderPipeline", Shader.globalRenderPipeline);
            SelectionRead("select." + label);
        }
        Shader.globalRenderPipeline = original;
        // LODs under the pipeline: SelE's SubShaders are UniversalPipeline-tagged at 300 and 100,
        // its Fallback's untagged at 50.
        var selE = Shader.Find("Probe/SelE");
        foreach (var lod in new[] { 200, 75, 10 })
        {
            selE.maximumLOD = lod;
            var m = new Material(selE);
            Read("select.lod" + lod + ".queue", () => selE.renderQueue);
            Read("select.lod" + lod + ".mat.queue", () => m.renderQueue);
            Read("select.lod" + lod + ".tag", () => m.GetTag("SubId", false, "none"));
            Read("select.lod" + lod + ".drawn", () => Drawn(m));
            Object.DestroyImmediate(m);
        }
        selE.maximumLOD = -1;
        Shader.globalMaximumLOD = 150;
        Read("select.global150.queue", () => selE.renderQueue);
        Shader.globalMaximumLOD = int.MaxValue;
        Read("select.restored.queue", () => selE.renderQueue);
        Logs("select.lod.logs");
    }

    static void BuiltinShaders()
    {
        var original = Shader.globalRenderPipeline;
        foreach (var (label, tag) in new[] { ("urp", original), ("none", "") })
        {
            Shader.globalRenderPipeline = tag;
            foreach (var name in Builtin)
            {
                var shader = Shader.Find(name);
                var key = "builtin." + label + "." + name;
                if (shader == null) { Row(key, "not found"); continue; }
                var material = new Material(shader);
                Read(key + ".queue", () => shader.renderQueue);
                Read(key + ".passCount", () => shader.passCount);
                Read(key + ".subshaderCount", () => shader.subshaderCount);
                Read(key + ".mat.queue", () => material.renderQueue);
                Read(key + ".renderType", () => material.GetTag("RenderType", false, "none"));
                Read(key + ".renderTypeFallbacks", () => material.GetTag("RenderType", true, "none"));
                Read(key + ".queueTag", () => material.GetTag("Queue", false, "none"));
                Read(key + ".pipelineTag", () => material.GetTag("RenderPipeline", false, "none"));
                Object.DestroyImmediate(material);
            }
            Logs("builtin." + label + ".logs");
        }
        Shader.globalRenderPipeline = original;
    }

    static void QueueForms()
    {
        for (int i = 0; i < 13; i++)
        {
            var shader = Shader.Find("Probe/SelQ" + i);
            Read("queue.SelQ" + i, () => shader != null ? (object)shader.renderQueue : "not found");
        }
        Logs("queue.logs");
    }

    // ---- keyword spaces with Fallbacks ----

    static void Keywords()
    {
        foreach (var name in new[]
                 {
                     "Probe/KwOwn", "Probe/KwPlain", "Probe/KwFall", "Universal Render Pipeline/Particles/Lit",
                     "Hidden/Universal Render Pipeline/StencilDeferred", "Universal Render Pipeline/Unlit",
                 })
        {
            var shader = Shader.Find(name);
            var key = "keywords." + name;
            if (shader == null) { Row(key, "not found"); continue; }
            var space = shader.keywordSpace;
            Read(key + ".count", () => space.keywordCount);
            Read(key + ".names", () => string.Join(",", space.keywordNames));
            Read(key + ".flags", () => string.Join(",", space.keywords.Select(k =>
                k.name + ":" + k.type + (k.isOverridable ? ":o" : "") + (k.isDynamic ? ":d" : ""))));
        }
        foreach (var name in new[] { "Probe/KwOwn", "Probe/KwPlain" })
        {
            var shader = Shader.Find(name);
            var key = "keywords." + name;
            var material = new Material(shader);
            foreach (var keyword in new[] { "A_ON", "B_ON", "C_ON", "D_ON", "E_ON" })
            {
                material.EnableKeyword(keyword);
                Read(key + ".enabled." + keyword, () => material.IsKeywordEnabled(keyword));
                Read(key + ".valid." + keyword, () => new LocalKeyword(shader, keyword).isValid);
            }
            Read(key + ".enabledKeywords", () => string.Join(",", material.enabledKeywords.Select(k => k.name)));
            Read(key + ".shaderKeywords", () => string.Join(",", material.shaderKeywords));
            Object.DestroyImmediate(material);
        }
        Logs("keywords.logs");
    }

    // ---- sparse textures ----

    static void Sparse()
    {
        if (!SystemInfo.supportsSparseTextures) { Row("sparse.supported", false); return; }
        var texture = new SparseTexture(256, 256, TextureFormat.RGBA32, 1);
        Read("sparse.tileWidth", () => texture.tileWidth);
        Read("sparse.tileHeight", () => texture.tileHeight);
        Read("sparse.isCreated", () => texture.isCreated);
        Read("sparse.mipmapCount", () => texture.mipmapCount);
        Read("sparse.graphicsFormat", () => texture.graphicsFormat);
        int w = texture.tileWidth, h = texture.tileHeight;
        var red = Enumerable.Repeat(new Color32(255, 0, 0, 255), w * h).ToArray();
        Section("sparse.update", () => texture.UpdateTile(0, 0, 0, red));
        var target = new RenderTexture(256, 256, 0, GraphicsFormat.R8G8B8A8_UNorm);
        Graphics.Blit(texture, target);
        RenderTexture.active = target;
        var read = new Texture2D(256, 256, TextureFormat.RGBA32, false);
        read.ReadPixels(new Rect(0, 0, 256, 256), 0, 0);
        RenderTexture.active = null;
        Row("sparse.loaded", (Color32)read.GetPixel(w / 2, h / 2));
        Row("sparse.unloaded", (Color32)read.GetPixel(255 - w / 2, 255 - h / 2));
        Section("sparse.unload", () => texture.UnloadTile(0, 0, 0));
        Object.DestroyImmediate(read);
        target.Release();
        Object.DestroyImmediate(texture);
        Logs("sparse.logs");
    }

    // ---- Camera.anamorphism ----

    static void Anamorphism()
    {
        var go = new GameObject("anamorphic");
        var camera = go.AddComponent<Camera>();
        camera.enabled = false;
        Read("anamorphism.default", () => camera.anamorphism);
        foreach (var value in new[] { 0.5f, 2f, -2f, -0.25f, float.NaN })
        {
            camera.anamorphism = value;
            Read("anamorphism.set" + Text(value), () => camera.anamorphism);
        }
        camera.anamorphism = 0.5f;
        var copy = new GameObject("copy").AddComponent<Camera>();
        copy.enabled = false;
        copy.CopyFrom(camera);
        Read("anamorphism.copy", () => copy.anamorphism);
        camera.usePhysicalProperties = true;
        Read("anamorphism.projection", () => camera.projectionMatrix);
        camera.anamorphism = 0f;
        Read("anamorphism.projection0", () => camera.projectionMatrix);
        Object.DestroyImmediate(copy.gameObject);
        Object.DestroyImmediate(go);
        Logs("anamorphism.logs");
    }

    static void LightmappingMembers()
    {
#pragma warning disable 618
        Read("lightmapping.giWorkflowMode", () => Lightmapping.giWorkflowMode);
        Lightmapping.giWorkflowMode = Lightmapping.GIWorkflowMode.Iterative;
        Read("lightmapping.giWorkflowMode.set", () => Lightmapping.giWorkflowMode);
#pragma warning restore 618
        Logs("lightmapping.logs");
    }

    // ---- MaterialProperty ----

    static void MaterialProperties()
    {
        var shader = Shader.Find("Probe/Props");
        var a = new Material(shader) { name = "a" };
        var b = new Material(shader) { name = "b" };
        a.SetTextureScale("_Tex3", new Vector2(2, 3));
        b.SetFloat("_Flt", 0.75f);
        b.SetColor("_Color", Color.green);
        var properties = MaterialEditor.GetMaterialProperties(new Object[] { a });
        Row("materialProperty.count", properties.Length);
        foreach (var p in properties)
        {
            var key = "materialProperty." + p.name;
            Read(key + ".displayName", () => p.displayName);
            Read(key + ".type", () => p.type);
            Read(key + ".flags", () => p.flags);
            Read(key + ".float", () => p.floatValue);
            Read(key + ".int", () => p.intValue);
            Read(key + ".color", () => p.colorValue);
            Read(key + ".vector", () => p.vectorValue);
            Read(key + ".range", () => p.rangeLimits);
            Read(key + ".textureDimension", () => p.textureDimension);
            Read(key + ".texture", () => p.textureValue);
            Read(key + ".scaleOffset", () => p.textureScaleAndOffset);
            Read(key + ".mixed", () => p.hasMixedValue);
            Read(key + ".targets", () => p.targets.Length);
        }
        var both = MaterialEditor.GetMaterialProperties(new Object[] { a, b });
        Row("materialProperty.both", string.Join(",", both.Select(p => p.name + (p.hasMixedValue ? ":mixed" : ""))));
        Read("materialProperty.one", () => MaterialEditor.GetMaterialProperty(new Object[] { b }, "_Flt").floatValue);
        Read("materialProperty.oneMixed", () => MaterialEditor.GetMaterialProperty(new Object[] { a, b }, "_Flt").hasMixedValue);
        Read("materialProperty.missing", () => MaterialEditor.GetMaterialProperty(new Object[] { a }, "_None").name);
        var one = MaterialEditor.GetMaterialProperty(new Object[] { a }, "_Rng");
        one.floatValue = 1.5f;
        Read("materialProperty.write", () => a.GetFloat("_Rng"));
        var tex = MaterialEditor.GetMaterialProperty(new Object[] { a }, "_Tex");
        tex.textureValue = Texture2D.redTexture;
        Read("materialProperty.writeTexture", () => a.GetTexture("_Tex"));
        b.SetVector("_Vec", new Vector4(9, 8, 7, 6));
        Read("materialProperty.vectorRange", () => MaterialEditor.GetMaterialProperty(new Object[] { b }, "_Vec").rangeLimits);
        Read("materialProperty.vectorValue", () => MaterialEditor.GetMaterialProperty(new Object[] { b }, "_Vec").vectorValue);
        Read("materialProperty.material._Tex", () => b.GetTexture("_Tex"));
        Read("materialProperty.material._Per", () => b.GetTexture("_Per"));
        Read("materialProperty.material._Cube", () => b.GetTexture("_Cube"));
        Object.DestroyImmediate(a);
        Object.DestroyImmediate(b);
        Logs("materialProperty.logs");
    }

    // ---- MeshUtility ----

    static void MeshUtilityMembers()
    {
        var mesh = new Mesh { name = "probe" };
        mesh.vertices = new[]
        {
            new Vector3(0, 0, 0), new Vector3(5, 5, 0), new Vector3(1, 0, 0), new Vector3(0, 1, 0), new Vector3(4, 5, 0), new Vector3(5, 4, 0),
        };
        mesh.uv = new[] { new Vector2(0, 0), new Vector2(1, 1), new Vector2(1, 0), new Vector2(0, 1), new Vector2(0.8f, 1), new Vector2(1, 0.8f) };
        mesh.triangles = new[] { 1, 4, 5, 0, 3, 2 };
        Read("meshUtility.compression", () => MeshUtility.GetMeshCompression(mesh));
        MeshUtility.SetMeshCompression(mesh, ModelImporterMeshCompression.Medium);
        Read("meshUtility.compression.set", () => MeshUtility.GetMeshCompression(mesh));
        Read("meshUtility.vertices.set", () => string.Join(";", mesh.vertices.Select(v => Text(v.x) + "," + Text(v.y))));
        MeshUtility.Optimize(mesh);
        Read("meshUtility.optimized.vertices", () => string.Join(";", mesh.vertices.Select(v => Text(v.x) + "," + Text(v.y))));
        Read("meshUtility.optimized.uv", () => string.Join(";", mesh.uv.Select(v => Text(v.x) + "," + Text(v.y))));
        Read("meshUtility.optimized.triangles", () => string.Join(",", mesh.triangles));
        Object.DestroyImmediate(mesh);
        // A 4x3 grid of quads whose triangles are listed in a shuffled order.
        var grid = new Mesh { name = "grid" };
        var vertices = new List<Vector3>();
        for (int y = 0; y < 4; y++)
            for (int x = 0; x < 5; x++) vertices.Add(new Vector3(x, y, 0));
        var quads = new List<int[]>();
        for (int y = 0; y < 3; y++)
            for (int x = 0; x < 4; x++)
            {
                int i = y * 5 + x;
                quads.Add(new[] { i, i + 5, i + 6 });
                quads.Add(new[] { i, i + 6, i + 1 });
            }
        var order = new[] { 17, 3, 9, 0, 22, 14, 5, 11, 20, 8, 1, 15, 23, 6, 12, 19, 2, 10, 21, 4, 16, 7, 13, 18 };
        grid.SetVertices(vertices);
        grid.SetTriangles(order.SelectMany(o => quads[o]).ToArray(), 0);
        MeshUtility.Optimize(grid);
        Read("meshUtility.grid.vertices", () => string.Join(";", grid.vertices.Select(v => Text(v.x) + "," + Text(v.y))));
        Read("meshUtility.grid.triangles", () => string.Join(",", grid.triangles));
        Object.DestroyImmediate(grid);
        Logs("meshUtility.logs");
    }

    // ---- Camera.SubmitRenderRequest with ObjectIdRequest ----

    static void ObjectIds()
    {
        var root = new GameObject("objectIds");
        root.transform.position = new Vector3(0, 500, 0);
        var cameraObject = new GameObject("idCamera");
        cameraObject.transform.SetParent(root.transform, false);
        cameraObject.transform.localPosition = new Vector3(0, 0, -10);
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = 4;
        camera.nearClipPlane = 1;
        camera.farClipPlane = 20;
        var lit = new Material(Shader.Find("Universal Render Pipeline/Lit"));
        var names = new[] { "idA", "idB", "idC" };
        var cubes = new GameObject[3];
        for (int i = 0; i < 3; i++)
        {
            cubes[i] = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cubes[i].name = names[i];
            cubes[i].transform.SetParent(root.transform, false);
            // Distinct depths (A 0, B 2, C -1): the mapping lists renderers far to near.
            cubes[i].transform.localPosition = new Vector3(-2.5f + 2.5f * i, 0, i == 1 ? 2 : i == 2 ? -1 : 0);
            cubes[i].GetComponent<Renderer>().sharedMaterial = lit;
        }
        // A larger cube behind B: the mapping lists it, depth hides its middle.
        var back = GameObject.CreatePrimitive(PrimitiveType.Cube);
        back.name = "idBack";
        back.transform.SetParent(root.transform, false);
        back.transform.localPosition = new Vector3(0, 0, 6);
        back.transform.localScale = new Vector3(3, 3, 1);
        back.GetComponent<Renderer>().sharedMaterial = lit;
        // A legacy shader's cube above B (a control outside URP's own shaders).
        var unlit = new Material(Shader.Find("Unlit/Color"));
        var d = GameObject.CreatePrimitive(PrimitiveType.Cube);
        d.name = "idD";
        d.transform.SetParent(root.transform, false);
        d.transform.localPosition = new Vector3(0, 2.5f, 1);
        d.GetComponent<Renderer>().sharedMaterial = unlit;
        Read("objectId.supports", () => RenderPipeline.SupportsRenderRequest(camera, new ObjectIdRequest(null)));
        foreach (var (label, depth) in new[] { ("depth", 24), ("noDepth", 0) })
        {
            var target = new RenderTexture(64, 64, depth, RenderTextureFormat.ARGB32) { name = "ids" };
            target.Create();
            var request = new ObjectIdRequest(target);
            Read("objectId." + label + ".defaults", () => request.mipLevel + "," + request.face + "," + request.slice + "," + (request.result == null));
            Section("objectId." + label + ".submit", () => camera.SubmitRenderRequest(request));
            var key = "objectId." + label;
            Read(key + ".result", () => request.result == null ? "null" : request.result.idToObjectMapping.Length.ToString());
            if (request.result != null)
                Read(key + ".mapping", () => string.Join(",", request.result.idToObjectMapping.Select(o => o == null ? "null" : o.name + ":" + o.GetType().Name)));
            RenderTexture.active = target;
            var read = new Texture2D(64, 64, TextureFormat.RGBA32, false);
            read.ReadPixels(new Rect(0, 0, 64, 64), 0, 0);
            // Pixel centres of A, B, C, the back cube beside B, and the background.
            foreach (var (point, x, y) in new[] { ("a", 12, 32), ("b", 32, 32), ("c", 52, 32), ("back", 32, 42), ("d", 32, 54), ("background", 2, 2) })
            {
                Color pixel = read.GetPixel(x, y);
                int id = ObjectIdResult.DecodeIdFromColor(pixel);
                Read(key + ".pixel." + point, () => (Color32)pixel + " id " + id + " " + (request.result != null && id >= 0 && id < request.result.idToObjectMapping.Length
                    ? (request.result.idToObjectMapping[id] == null ? "null" : request.result.idToObjectMapping[id].name) : "-"));
            }
            var ids = new SortedDictionary<int, int>();
            foreach (var pixel in read.GetPixels32())
            {
                int id = pixel.r + (pixel.g << 8) + (pixel.b << 16) + (pixel.a << 24);
                ids[id] = ids.TryGetValue(id, out var n) ? n + 1 : 1;
            }
            Row(key + ".ids", string.Join(",", ids.Select(p => p.Key + ":" + p.Value)));
            RenderTexture.active = null;
            Object.DestroyImmediate(read);
            target.Release();
            Logs(key + ".logs");
        }
        // A standard request under URP draws the camera into its destination.
        var standard = new RenderPipeline.StandardRequest { destination = new RenderTexture(32, 32, 24) };
        Read("objectId.standard.supports", () => RenderPipeline.SupportsRenderRequest(camera, standard));
        Section("objectId.standard.submit", () => camera.SubmitRenderRequest(standard));
        Logs("objectId.standard.logs");
        Section("objectId.null", () => camera.SubmitRenderRequest<ObjectIdRequest>(null));
        Logs("objectId.null.logs");
        Object.DestroyImmediate(root);
        Object.DestroyImmediate(back);
        Object.DestroyImmediate(lit);
        Object.DestroyImmediate(unlit);
    }

    // ---- rendering layer names ----

    static string LayerState() =>
        RenderingLayerMask.GetRenderingLayerCount() + " " + string.Join(",", Enumerable.Range(0, RenderingLayerMask.GetRenderingLayerCount()).Select(RenderingLayerMask.RenderingLayerToName))
        + " defined " + string.Join(",", RenderingLayerMask.GetDefinedRenderingLayerNames());

    static void RenderingLayers()
    {
        Row("layers.start", LayerState());
        Read("layers.pipelineType", () => RenderPipelineEditorUtility.GetPipelineTypeFromPipelineAssetType(GraphicsSettings.currentRenderPipeline.GetType())?.FullName ?? "null");
        Read("layers.pipelineType.material", () => RenderPipelineEditorUtility.GetPipelineTypeFromPipelineAssetType(typeof(Material))?.FullName ?? "null");
        Read("layers.pipelineType.base", () => RenderPipelineEditorUtility.GetPipelineTypeFromPipelineAssetType(typeof(RenderPipelineAsset))?.FullName ?? "null");
        var max = typeof(RenderPipelineEditorUtility).GetMethod("GetActiveMaxRenderingLayers", BindingFlags.Static | BindingFlags.NonPublic);
        Read("layers.activeMax", () => max.Invoke(null, null));
        Read("layers.add", () => RenderPipelineEditorUtility.TryAddRenderingLayerName("ProbeAdded"));
        Row("layers.add.state", LayerState());
        Read("layers.addEmpty", () => RenderPipelineEditorUtility.TryAddRenderingLayerName(""));
        Read("layers.addDuplicate", () => RenderPipelineEditorUtility.TryAddRenderingLayerName("Layer 1"));
        Row("layers.addDuplicate.state", LayerState());
        Read("layers.set0", () => RenderPipelineEditorUtility.TrySetRenderingLayerName(0, "Renamed"));
        Read("layers.set1", () => RenderPipelineEditorUtility.TrySetRenderingLayerName(1, "Renamed"));
        Read("layers.setLast", () => RenderPipelineEditorUtility.TrySetRenderingLayerName(RenderingLayerMask.GetRenderingLayerCount() - 1, "Last"));
        Read("layers.setPast", () => RenderPipelineEditorUtility.TrySetRenderingLayerName(RenderingLayerMask.GetRenderingLayerCount(), "Past"));
        Read("layers.setNegative", () => RenderPipelineEditorUtility.TrySetRenderingLayerName(-1, "Negative"));
        Read("layers.setNull", () => RenderPipelineEditorUtility.TrySetRenderingLayerName(2, null));
        Row("layers.set.state", LayerState());
        Read("layers.mask", () => RenderingLayerMask.GetMask("Renamed", "ProbeAdded", "Layer 2"));
        Read("layers.nameToLayer", () => RenderingLayerMask.NameToRenderingLayer("ProbeAdded"));
        Logs("layers.set.logs");
        int added = 0;
        while (RenderingLayerMask.GetRenderingLayerCount() < 32 && added < 40)
        {
            RenderPipelineEditorUtility.TryAddRenderingLayerName("Fill" + added);
            added++;
        }
        Row("layers.filled", added + " " + RenderingLayerMask.GetRenderingLayerCount());
        Read("layers.addPastMax", () => RenderPipelineEditorUtility.TryAddRenderingLayerName("Past"));
        Logs("layers.fill.logs");
        while (RenderingLayerMask.GetRenderingLayerCount() > 1)
            if (!RenderPipelineEditorUtility.TryRemoveLastRenderingLayerName()) break;
        Row("layers.removed.state", LayerState());
        Read("layers.removeDefault", () => RenderPipelineEditorUtility.TryRemoveLastRenderingLayerName());
        Logs("layers.remove.logs");
    }

    // ---- RenderPipelineGraphicsSettingsEditorUtility.Rebind ----

    static void Rebind()
    {
        var settingsType = Type.GetType("UnityEngine.Rendering.Universal.URPDefaultVolumeProfileSettings, Unity.RenderPipelines.Universal.Runtime");
        var pipelineType = Type.GetType("UnityEngine.Rendering.Universal.UniversalRenderPipeline, Unity.RenderPipelines.Universal.Runtime");
        var get = typeof(GraphicsSettings).GetMethod(nameof(GraphicsSettings.GetRenderPipelineSettings)).MakeGenericMethod(settingsType);
        var profileProperty = settingsType.GetProperty("volumeProfile");
        object Current() => get.Invoke(null, null);
        Read("rebind.before", () => profileProperty.GetValue(Current()));
        var replacement = (IRenderPipelineGraphicsSettings)Activator.CreateInstance(settingsType);
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        profile.name = "ProbeProfile";
        profileProperty.SetValue(replacement, profile);
        Section("rebind.call", () => RenderPipelineGraphicsSettingsEditorUtility.Rebind(replacement, pipelineType));
        Read("rebind.after", () => profileProperty.GetValue(Current()));
        Read("rebind.same", () => ReferenceEquals(Current(), replacement));
        var other = (IRenderPipelineGraphicsSettings)Activator.CreateInstance(settingsType);
        Section("rebind.material", () => RenderPipelineGraphicsSettingsEditorUtility.Rebind(other, typeof(Material)));
        Section("rebind.null", () => RenderPipelineGraphicsSettingsEditorUtility.Rebind(null, pipelineType));
        Read("rebind.unchanged", () => ReferenceEquals(Current(), replacement));
        Logs("rebind.logs");
    }

    // ---- EditorCameraUtils ----

    static void CameraUtils()
    {
        var a = GameObject.CreatePrimitive(PrimitiveType.Cube);
        a.name = "filterA";
        var b = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        b.name = "filterB";
        // Renderers only: another instance ID crashes the Editor's native code.
        var ids = new[] { a.GetComponent<Renderer>().GetInstanceID(), b.GetComponent<Renderer>().GetInstanceID() };
        var results = new bool[ids.Length];
        Section("cameraUtils.filter", () => EditorCameraUtils.GetRenderersFilteringResults(ids, results));
        Row("cameraUtils.filter.results", string.Join(",", results));
        Section("cameraUtils.filterLength", () => EditorCameraUtils.GetRenderersFilteringResults(ids, new bool[2]));
        var bits = new ulong[1];
        Section("cameraUtils.hidden", () => EditorCameraUtils.GetRenderersHiddenResultBits(ids, bits));
        Row("cameraUtils.hidden.bits", bits[0]);
        SceneVisibilityManager.instance.Hide(b, false);
        Section("cameraUtils.hiddenB", () => EditorCameraUtils.GetRenderersHiddenResultBits(ids, bits));
        Row("cameraUtils.hiddenB.bits", bits[0]);
        Section("cameraUtils.filterHidden", () => EditorCameraUtils.GetRenderersFilteringResults(ids, results));
        Row("cameraUtils.filterHidden.results", string.Join(",", results));
        SceneVisibilityManager.instance.Show(b, false);
        Section("cameraUtils.hiddenLength", () => EditorCameraUtils.GetRenderersHiddenResultBits(ids, new ulong[2]));
        Object.DestroyImmediate(a);
        Object.DestroyImmediate(b);
        Logs("cameraUtils.logs");
        s_Capturing = false;
    }
}
