using System;
using System.Collections;
using System.Reflection;
using UnityEngine;
using UnityEngine.Rendering;

public partial class LightingAssetsProbe
{
    IEnumerator RecordFallbackWarmup()
    {
        var root = Resources.Load<Shader>("WarmupFallbackRoot");
        var inherited = Resources.Load<Shader>("WarmupFallbackBase");
        var own = Resources.Load<Shader>("WarmupFallbackOwn");
        var image = new RenderTexture(4, 4, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
        image.Create();
        var previous = RenderTexture.active;
        Graphics.SetRenderTarget(image);
        GL.Clear(false, true, Color.clear);
        var mesh = WarmupMesh();
        var ownCollection = new ShaderVariantCollection();
        ownCollection.Add(new ShaderVariantCollection.ShaderVariant(own, PassType.ShadowCaster));
        var collection = new ShaderVariantCollection();
        collection.Add(new ShaderVariantCollection.ShaderVariant(root, PassType.ShadowCaster));
        collection.Add(new ShaderVariantCollection.ShaderVariant(root, PassType.ShadowCaster, "FALLBACK_ON"));
        void State(string step, ShaderVariantCollection c) => Record("warmup-fallback", "\"step\":" + S(step) + ",\"state\":" + Describe(c));
        var inheritedBeforeOwn = GpuWarmupStats(inherited);
        State("own before", ownCollection);
        ownCollection.WarmUp();
        State("own after", ownCollection);
        var ownWarm = GpuWarmupStats(own);
        var inheritedAfterOwn = GpuWarmupStats(inherited);
        if (ownWarm != null && inheritedBeforeOwn != null && inheritedAfterOwn != null)
            Debug.Log("WARMUP_FALLBACK_OWN " + ownWarm.programs + " " + ownWarm.pipelines + " "
                + inheritedBeforeOwn.programs + " " + inheritedAfterOwn.programs);
        void Draw(string step, Material original, Shader expected)
        {
            Material drawn = original;
            int pass = 0;
            // The engine's actual shadow source selection; the Editor draws that source directly.
            var frame = typeof(Shader).Assembly.GetType("Unbound.Runtime.FrameBuilder");
            if (frame != null)
            {
                var select = frame.GetMethod("ShadowCaster", BindingFlags.Static | BindingFlags.NonPublic,
                    null, new[] { typeof(Material), typeof(Material).MakeByRefType(), typeof(int).MakeByRefType() }, null);
                var args = new object[] { original, null, -1 };
                if (!(bool)select.Invoke(null, args)) throw new InvalidOperationException("fallback shadow source was not found");
                drawn = (Material)args[1]; pass = (int)args[2];
                if (drawn.shader.name != expected.name) throw new InvalidOperationException("incorrect shadow source");
            }
            else if (original.shader != expected)
            {
                drawn = new Material(expected) { shaderKeywords = original.shaderKeywords };
            }
            using (var cmd = new CommandBuffer())
            {
                cmd.SetRenderTarget(image);
                cmd.SetViewport(new Rect(0, 0, 4, 4));
                cmd.ClearRenderTarget(false, true, Color.clear);
                cmd.DrawMesh(mesh, Matrix4x4.identity, drawn, 0, pass);
                Graphics.ExecuteCommandBuffer(cmd);
            }
            Record("warmup-fallback", "\"step\":" + S(step) + ",\"pixels\":" + S(WarmupPixels(image)));
            if (frame == null && drawn != original) DestroyImmediate(drawn);
        }
        var ownMaterial = new Material(own);
        Draw("own draw", ownMaterial, own);
        var ownAfter = GpuWarmupStats(own);
        if (ownWarm != null && ownAfter != null && (ownWarm.programs != ownAfter.programs || ownWarm.pipelines != ownAfter.pipelines))
            throw new InvalidOperationException("own shadow pass compiled after warm-up");
        State("inherited before", collection);
        var before = GpuWarmupStats(inherited);
        collection.WarmUp();
        State("inherited after", collection);
        var warm = GpuWarmupStats(inherited);
        var material = new Material(root);
        for (int i = 0; i < 2; i++)
        {
            material.shaderKeywords = i == 0 ? new string[0] : new[] { "FALLBACK_ON" };
            Draw("inherited draw " + i, material, inherited);
            var after = GpuWarmupStats(inherited);
            if (before != null && warm != null && after != null)
                Debug.Log("WARMUP_GPU_PROOF fallback " + before.programs + " " + before.pipelines + " "
                    + warm.programs + " " + warm.pipelines + " " + after.programs + " " + after.pipelines);
        }
        collection.WarmUp();
        State("inherited repeat", collection);
        RenderTexture.active = previous;
        DestroyImmediate(ownMaterial); DestroyImmediate(material);
        DestroyImmediate(ownCollection); DestroyImmediate(collection);
        DestroyImmediate(mesh); DestroyImmediate(image);
        yield break;
    }
}
