using System;
using System.Collections;
using System.Reflection;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

public partial class LightingAssetsProbe
{
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

    static WarmupStats GpuWarmupStats(Shader shader)
    {
        var native = typeof(Shader).Assembly.GetType("Unbound.Runtime.Native");
        if (native == null) return null;
        var call = native.GetMethod("Call", BindingFlags.Static | BindingFlags.NonPublic,
            null, new[] { typeof(string), typeof(string) }, null);
        return JsonUtility.FromJson<WarmupStats>((string)call.Invoke(null, new object[] { "shader-warmup", "{\"stats\":true,\"shader\":" + S(shader.name) + "}" }));
    }

    IEnumerator RecordColdShaderWarmup(string mode)
    {
        var shader = Resources.Load<Shader>("WarmupFresh");
        var material = new Material(shader);
        var target = mode == "startup" ? s_StartupWarmupTarget : new RenderTexture(4, 4, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        target.Create();
        var mesh = new Mesh();
        mesh.vertices = new[] { new Vector3(-1,-1,0), new Vector3(1,-1,0), new Vector3(1,1,0), new Vector3(-1,1,0) };
        mesh.triangles = new[] { 0, 1, 2, 0, 2, 3 };
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
        string Pixels()
        {
            var request = AsyncGPUReadback.Request(target, 0, TextureFormat.RGBA32);
            request.WaitForCompletion();
            if (request.hasError) throw new InvalidOperationException("warmup readback failed");
            var data = request.GetData<byte>();
            var hex = new StringBuilder(data.Length * 2);
            foreach (byte value in data) hex.Append(value.ToString("x2"));
            return hex.ToString();
        }
        void Proof(string step)
        {
            Record("warmup-cold", "\"step\":" + S(step) + ",\"pixels\":" + S(Pixels()));
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
        if (mode != "all")
        {
            collection.WarmUp();
            State("repeat", collection.WarmUpProgressively(1));
            var repeated = GpuWarmupStats(shader);
            if (warm != null && (repeated.programs != warm.programs || repeated.pipelines != warm.pipelines))
                throw new InvalidOperationException("repeated warmup prepared new pipelines");
        }
        RenderTexture.active = previous;
        DestroyImmediate(collection);
        DestroyImmediate(material);
        DestroyImmediate(mesh);
        DestroyImmediate(target);
        yield break;
    }
}
