using System;
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

public partial class LightingAssetsProbe
{
    static Shader s_MixedWarmupShader;
    static string s_StartupGlobalWarmup;
    static void PrepareStartupGlobalWarmup()
    {
        s_MixedWarmupShader = Resources.Load<Shader>("WarmupMixed");
        s_StartupGlobalWarmup = Try(() => { Shader.WarmupAllShaders(); return "\"ok\""; });
    }

    static ShaderVariantCollection s_StartupWarmup;
    static RenderTexture s_StartupWarmupTarget;
    static readonly string[][] s_WarmupKeywords = { new string[0], new[] { "WARM_RED" }, new[] { "WARM_BLUE" }, new[] { "WARM_RED", "WARM_BLUE" } };

    static ShaderVariantCollection WarmupCollection(Shader shader)
    {
        var result = new ShaderVariantCollection();
        foreach (var keys in s_WarmupKeywords) result.Add(new ShaderVariantCollection.ShaderVariant(shader, PassType.Normal, keys));
        result.Add(new ShaderVariantCollection.ShaderVariant(shader, PassType.Normal, "INSTANCING_ON"));
        return result;
    }

    static void PrepareStartupWarmup()
    {
        s_StartupWarmupTarget = new RenderTexture(4, 4, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        s_StartupWarmupTarget.Create();
        var previous = RenderTexture.active;
        Graphics.SetRenderTarget(s_StartupWarmupTarget);
        GL.Clear(false, true, Color.clear);
        s_StartupWarmup = WarmupCollection(Resources.Load<Shader>("WarmupFresh"));
        s_StartupWarmup.WarmUp();
        RenderTexture.active = previous;
    }

    [Serializable]
    sealed class WarmupStats { public int programs; public int pipelines; }

    static WarmupStats GpuWarmupStats(Shader shader) => GpuWarmupStats(shader.name);

    static WarmupStats GpuWarmupStats(string shaderName)
    {
        var native = typeof(Shader).Assembly.GetType("Unbound.Runtime.Native");
        if (native == null) return null;
        var call = native.GetMethod("Call", BindingFlags.Static | BindingFlags.NonPublic,
            null, new[] { typeof(string), typeof(string) }, null);
        return JsonUtility.FromJson<WarmupStats>((string)call.Invoke(null, new object[] { "shader-warmup", "{\"stats\":true,\"shader\":" + S(shaderName) + "}" }));
    }

    static Mesh WarmupMesh()
    {
        var mesh = new Mesh();
        mesh.vertices = new[] { new Vector3(-1,-1,0), new Vector3(1,-1,0), new Vector3(1,1,0), new Vector3(-1,1,0) };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
        return mesh;
    }

    static string WarmupPixels(RenderTexture target)
    {
        var request = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32);
        request.WaitForCompletion();
        if (request.hasError) throw new InvalidOperationException("warmup readback failed");
        var data = request.GetData<byte>();
        var hex = new StringBuilder(data.Length * 2);
        foreach (byte value in data) hex.Append(value.ToString("x2"));
        return hex.ToString();
    }

    IEnumerator RecordColdShaderWarmup(string mode)
    {
        var startupMixed = mode == "all" ? GpuWarmupStats(s_MixedWarmupShader) : null;
        var shader = Resources.Load<Shader>("WarmupFresh");
        var globalRoot = mode == "all" ? Resources.Load<Shader>("WarmupFallbackRoot") : null;
        var fallbackBefore = mode == "all" ? GpuWarmupStats("Probe/WarmupFallback/Base") : null;
        var material = new Material(shader);
        var target = mode == "startup" ? s_StartupWarmupTarget : new RenderTexture(4, 4, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        target.Create();
        var mesh = WarmupMesh();
        var collection = mode == "startup" ? s_StartupWarmup : WarmupCollection(shader);
        var keywords = s_WarmupKeywords;
        void State(string step, bool? result = null) => Record("warmup-cold", "\"step\":" + S(step) + ",\"mode\":" + S(mode)
            + ",\"result\":" + (result.HasValue ? B(result.Value) : "null") + ",\"state\":" + Describe(collection));
        var previous = RenderTexture.active;
        Graphics.SetRenderTarget(target);
        GL.Clear(false, true, Color.clear);
        var before = GpuWarmupStats(shader);
        State("before");
        if (mode == "collection" || mode == "startup") collection.WarmUp();
        else if (mode == "progressive")
        {
            State("partial", collection.WarmUpProgressively(2));
            var partial = GpuWarmupStats(shader);
            if (before != null && partial != null)
                Debug.Log("WARMUP_PARTIAL_GPU_PROOF " + before.programs + " " + before.pipelines + " " + partial.programs + " " + partial.pipelines);
            State("finished", collection.WarmUpProgressively(int.MaxValue));
        }
        else Record("warmup-cold", "\"step\":\"all call\",\"result\":" + Try(() => { Shader.WarmupAllShaders(); return "\"ok\""; }));
        State("after");
        var warm = GpuWarmupStats(shader);
        var fallbackWarm = mode == "all" ? GpuWarmupStats("Probe/WarmupFallback/Base") : null;
        void Proof(string step)
        {
            Record("warmup-cold", "\"step\":" + S(step) + ",\"pixels\":" + S(WarmupPixels(target)));
            var after = GpuWarmupStats(shader);
            if (before != null && warm != null && after != null)
                Debug.Log("WARMUP_GPU_PROOF " + mode + " " + before.programs + " " + before.pipelines + " " + warm.programs + " " + warm.pipelines + " " + after.programs + " " + after.pipelines);
        }
        for (int i = 0; i < keywords.Length; i++)
        {
            material.shaderKeywords = keywords[i];
            using (var draw = new CommandBuffer())
            {
                draw.SetRenderTarget(target);
                draw.SetViewport(new Rect(0, 0, 4, 4));
                draw.ClearRenderTarget(false, true, Color.clear);
                draw.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                draw.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0);
                Graphics.ExecuteCommandBuffer(draw);
            }
            Proof("draw " + i);
        }
        GL.Clear(false, true, Color.clear);
        material.shaderKeywords = new string[0];
        material.enableInstancing = true;
        using (var cmd = new CommandBuffer())
        {
            cmd.SetRenderTarget(target);
            cmd.SetViewport(new Rect(0, 0, 4, 4));
            cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
            cmd.DrawMeshInstanced(mesh, 0, material, 0, new[] { Matrix4x4.identity, Matrix4x4.identity });
            Graphics.ExecuteCommandBuffer(cmd);
        }
        Proof("draw instanced");
        var cameraObject = new GameObject("WarmupCamera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.allowHDR = false;
        camera.allowMSAA = false;
        camera.orthographic = true;
        camera.orthographicSize = 1;
        camera.aspect = 1;
        camera.transform.position = new Vector3(0, 0, -2);
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.clear;
        camera.targetTexture = target;
        Graphics.DrawMeshInstanced(mesh, 0, material, new[] { Matrix4x4.identity, Matrix4x4.identity }, 2, null,
            ShadowCastingMode.Off, false, 0, camera, LightProbeUsage.Off);
        camera.Render();
        Proof("draw standalone instanced");
        DestroyImmediate(cameraObject);
        if (mode == "all")
        {
            // This instancing combination is absent from the explicit collection.
            material.shaderKeywords = new[] { "WARM_RED", "WARM_BLUE" };
            using (var cmd = new CommandBuffer())
            {
                cmd.SetRenderTarget(target);
                cmd.SetViewport(new Rect(0, 0, 4, 4));
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                cmd.DrawMeshInstanced(mesh, 0, material, 0, new[] { Matrix4x4.identity, Matrix4x4.identity });
                Graphics.ExecuteCommandBuffer(cmd);
            }
            Proof("draw unlisted instancing combination");
            material.enableInstancing = false; material.shaderKeywords = new string[0];
            shader.maximumLOD = 100;
            using (var cmd = new CommandBuffer())
            {
                cmd.SetRenderTarget(target);
                cmd.SetViewport(new Rect(0, 0, 4, 4));
                cmd.SetViewProjectionMatrices(Matrix4x4.identity, Matrix4x4.identity);
                cmd.DrawMesh(mesh, Matrix4x4.identity, material, 0, 0);
                Graphics.ExecuteCommandBuffer(cmd);
            }
            Proof("draw lower LOD");
            var fallbackShader = Shader.Find("Probe/WarmupFallback/Base");
            var fallbackMaterial = new Material(fallbackShader);
            for (int i = 0; i < 2; i++)
            {
                fallbackMaterial.shaderKeywords = i == 0 ? new string[0] : new[] { "FALLBACK_ON" };
                using (var cmd = new CommandBuffer())
                {
                    cmd.SetRenderTarget(target);
                    cmd.SetViewport(new Rect(0, 0, 4, 4));
                    cmd.ClearRenderTarget(false, true, Color.clear);
                    cmd.DrawMesh(mesh, Matrix4x4.identity, fallbackMaterial, 0, 0);
                    Graphics.ExecuteCommandBuffer(cmd);
                }
                Record("warmup-cold", "\"step\":" + S("draw global fallback " + i) + ",\"pixels\":" + S(WarmupPixels(target)));
                var fallbackAfter = GpuWarmupStats(fallbackShader);
                if (fallbackBefore != null && fallbackWarm != null && fallbackAfter != null)
                    Debug.Log("WARMUP_GLOBAL_FALLBACK " + fallbackBefore.programs + " " + fallbackBefore.pipelines + " "
                        + fallbackWarm.programs + " " + fallbackWarm.pipelines + " " + fallbackAfter.programs + " " + fallbackAfter.pipelines);
            }
            DestroyImmediate(fallbackMaterial);
            var repeatedBefore = GpuWarmupStats(shader);
            Record("warmup-cold", "\"step\":\"all repeat call\",\"result\":" + Try(() => { Shader.WarmupAllShaders(); return "\"ok\""; }));
            State("global repeat");
            var repeatedAfter = GpuWarmupStats(shader);
            if (repeatedBefore != null && repeatedAfter != null)
                Debug.Log("WARMUP_GLOBAL_REPEAT " + repeatedBefore.programs + " " + repeatedBefore.pipelines + " " + repeatedAfter.programs + " " + repeatedAfter.pipelines);
        }
        if (mode != "all")
        {
            collection.WarmUp();
            State("repeat", collection.WarmUpProgressively(1));
            var repeated = GpuWarmupStats(shader);
            if (warm != null && (repeated.programs != warm.programs || repeated.pipelines != warm.pipelines))
                throw new InvalidOperationException("repeated warmup prepared new pipelines");
        }
        if (mode == "all") RecordMixedShaderWarmup(target, mesh, startupMixed);
        RenderTexture.active = previous;
        DestroyImmediate(collection);
        DestroyImmediate(material);
        DestroyImmediate(mesh);
        DestroyImmediate(target);
        yield break;
    }
    void RecordMixedShaderWarmup(RenderTexture floating, Mesh mesh, WarmupStats startup)
    {
        Record("warmup-cold", "\"step\":\"startup mixed global call\",\"result\":" + s_StartupGlobalWarmup);
        if (startup != null) Debug.Log("WARMUP_MIXED_STARTUP " + startup.programs + " " + startup.pipelines);
        var mixed = s_MixedWarmupShader;
        var material = new Material(mixed);
        var sampled = new RenderTexture(2, 1, 0, RenderTextureFormat.ARGBFloat, RenderTextureReadWrite.Linear);
        sampled.Create();
        var exact = new string[2];
        void Draw(RenderTexture target, int pass)
        {
            using (var cmd = new CommandBuffer())
            {
                cmd.SetRenderTarget(target);
                cmd.SetViewport(new Rect(0, 0, target.width, target.height));
                cmd.DrawMesh(mesh, Matrix4x4.identity, material, 0, pass);
                Graphics.ExecuteCommandBuffer(cmd);
            }
        }
        void Proof(string phase, WarmupStats before, WarmupStats warm)
        {
            var after = GpuWarmupStats(mixed);
            if (before != null && warm != null && after != null)
                Debug.Log("WARMUP_MIXED_PROOF " + phase + " " + before.programs + " " + before.pipelines + " "
                    + warm.programs + " " + warm.pipelines + " " + after.programs + " " + after.pipelines);
        }
        var initial = GpuWarmupStats(mixed);
        Draw(floating, 0);
        Record("warmup-cold", "\"step\":\"mixed float draw\",\"pixels\":" + S(WarmupPixels(floating)));
        Proof("float", initial, initial);
        foreach (bool signed in new[] { false, true })
        {
            var descriptor = new RenderTextureDescriptor(2, 1) {
                graphicsFormat = signed ? UnityEngine.Experimental.Rendering.GraphicsFormat.R32G32B32A32_SInt
                                        : UnityEngine.Experimental.Rendering.GraphicsFormat.R32G32B32A32_UInt,
                depthBufferBits = 24, msaaSamples = 1
            };
            var integer = new RenderTexture(descriptor); integer.Create();
            Graphics.SetRenderTarget(integer);
            string kind = signed ? "signed" : "unsigned";
            var before = GpuWarmupStats(mixed);
            Record("warmup-cold", "\"step\":" + S("all " + kind + " target call") + ",\"result\":"
                + Try(() => { Shader.WarmupAllShaders(); return "\"ok\""; }));
            if (!signed)
            {
                var collection = new ShaderVariantCollection();
                collection.Add(new ShaderVariantCollection.ShaderVariant(mixed, PassType.Normal));
                Record("warmup-cold", "\"step\":\"collection unsigned target call\",\"result\":"
                    + Try(() => { collection.WarmUp(); return "\"ok\""; }));
                DestroyImmediate(collection);
            }
            var warm = GpuWarmupStats(mixed);
            Draw(integer, signed ? 2 : 1);
            material.SetTexture(signed ? "_SignedTex" : "_UnsignedTex", integer);
            Draw(floating, signed ? 4 : 3);
            var read = AsyncGPUReadback.Request(floating, 0, TextureFormat.RGBAFloat); read.WaitForCompletion();
            if (read.hasError) throw new InvalidOperationException("mixed sampled readback failed");
            var values = new StringBuilder("[");
            var data = read.GetData<float>();
            for (int i = 0; i < 4; i++) { if (i > 0) values.Append(','); values.Append(F(data[i])); }
            values.Append(']');
            Record("warmup-cold", "\"step\":" + S("mixed " + kind + " draw") + ",\"values\":" + values);
            Proof(kind, before, warm);
            Draw(sampled, signed ? 4 : 3);
            var precise = AsyncGPUReadback.Request(sampled, 0, TextureFormat.RGBAFloat); precise.WaitForCompletion();
            if (precise.hasError) throw new InvalidOperationException("mixed float32 readback failed");
            var numbers = precise.GetData<float>();
            exact[signed ? 1 : 0] = "[" + F(numbers[0]) + "," + F(numbers[1]) + "," + F(numbers[2]) + "," + F(numbers[3]) + "]";
            material.SetTexture(signed ? "_SignedTex" : "_UnsignedTex", null);
            Graphics.SetRenderTarget(floating);
            DestroyImmediate(integer);
        }
        Record("warmup-cold", "\"step\":\"mixed exact unsigned\",\"values\":" + exact[0]);
        Record("warmup-cold", "\"step\":\"mixed exact signed\",\"values\":" + exact[1]);
        DestroyImmediate(sampled);
        DestroyImmediate(material);
    }

}
