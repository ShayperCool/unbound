using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;

/// <summary>`Unity -batchmode -projectPath tests/projects/urp -executeMethod CapsProbe.Record` with
/// <c>CAPS_PROBE_OUT</c> set: writes the graphics device's SystemInfo answers the render pipelines
/// read (default formats, format support, MSAA, resolve support) as JSON lines and exits.</summary>
public static class CapsProbe
{
    static readonly StringBuilder s_Out = new StringBuilder();

    static void Line(string key, object value) => s_Out.Append("{\"key\":\"").Append(key).Append("\",\"value\":\"").Append(value).Append("\"}\n");

    static string Floats(params float[] values) => string.Join(" ", Array.ConvertAll(values, v => v.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));

    static void SH(string key, SphericalHarmonicsL2 sh)
    {
        var values = new float[27];
        for (int c = 0; c < 3; c++) for (int i = 0; i < 9; i++) values[c * 9 + i] = sh[c, i];
        Line(key, Floats(values));
    }

    public static void Record()
    {
        Line("graphicsDeviceType", SystemInfo.graphicsDeviceType);
        Line("graphicsDeviceVersion", SystemInfo.graphicsDeviceVersion);
        Line("usesReversedZBuffer", SystemInfo.usesReversedZBuffer);
        Line("graphicsUVStartsAtTop", SystemInfo.graphicsUVStartsAtTop);
        Line("supportsComputeShaders", SystemInfo.supportsComputeShaders);
        Line("usesLoadStoreActions", SystemInfo.usesLoadStoreActions);
        Line("supportsMultisampleResolveDepth", SystemInfo.supportsMultisampleResolveDepth);
        Line("supportsMultisampleResolveStencil", SystemInfo.supportsMultisampleResolveStencil);
        Line("supportsStoreAndResolveAction", SystemInfo.supportsStoreAndResolveAction);
        Line("supportsMultisampleAutoResolve", SystemInfo.supportsMultisampleAutoResolve);
        Line("supportsMultisampledBackBuffer", SystemInfo.supportsMultisampledBackBuffer);
        Line("hdrDisplaySupportFlags", SystemInfo.hdrDisplaySupportFlags);
        Line("supportsDepthFetchInRenderPass", SystemInfo.supportsDepthFetchInRenderPass);
        Line("supportsRenderTargetArrayIndexFromVertexShader", SystemInfo.supportsRenderTargetArrayIndexFromVertexShader);
        Line("supportsSetConstantBuffer", SystemInfo.supportsSetConstantBuffer);
        Line("constantBufferOffsetAlignment", SystemInfo.constantBufferOffsetAlignment);
        Line("maxConstantBufferSize", SystemInfo.maxConstantBufferSize);
        Line("supportedRandomWriteTargetCount", SystemInfo.supportedRandomWriteTargetCount);
        Line("supportsGraphicsFence", SystemInfo.supportsGraphicsFence);
        Line("supportsAsyncCompute", SystemInfo.supportsAsyncCompute);
        Line("supportsGeometryShaders", SystemInfo.supportsGeometryShaders);
        Line("supportsTessellationShaders", SystemInfo.supportsTessellationShaders);
        Line("supportsConservativeRaster", SystemInfo.supportsConservativeRaster);
        Line("supportsMultiview", SystemInfo.supportsMultiview);
        Line("supportsIndirectArgumentsBuffer", SystemInfo.supportsIndirectArgumentsBuffer);
        Line("supportsMultisampled2DArrayTextures", SystemInfo.supportsMultisampled2DArrayTextures);
        Line("maxTexture3DSize", SystemInfo.maxTexture3DSize);
        Line("maxTextureArraySlices", SystemInfo.maxTextureArraySlices);
        Line("maxComputeWorkGroupSize", SystemInfo.maxComputeWorkGroupSize);
        Line("computeSubGroupSize", SystemInfo.computeSubGroupSize);
        Line("renderingThreadingMode", SystemInfo.renderingThreadingMode);
        Line("hasMipMaxLevel", SystemInfo.hasMipMaxLevel);
        Line("supportsAnisotropicFilter", SystemInfo.supportsAnisotropicFilter);
        Line("maxAnisotropyLevel", SystemInfo.maxAnisotropyLevel);
        foreach (DefaultFormat f in Enum.GetValues(typeof(DefaultFormat)))
            Line("GetGraphicsFormat." + f, SystemInfo.GetGraphicsFormat(f));
        foreach (RenderTextureFormat f in new[] { RenderTextureFormat.Default, RenderTextureFormat.DefaultHDR, RenderTextureFormat.ARGB32, RenderTextureFormat.Depth, RenderTextureFormat.Shadowmap })
        {
            var d = new RenderTextureDescriptor(64, 64, f, 24);
            Line("RenderTextureDescriptor.graphicsFormat." + f, d.graphicsFormat);
            Line("RenderTextureDescriptor.depthStencilFormat." + f, d.depthStencilFormat);
            d.msaaSamples = 8;
            Line("GetRenderTextureSupportedMSAASampleCount.8." + f, SystemInfo.GetRenderTextureSupportedMSAASampleCount(d));
        }
        var formats = new[]
        {
            GraphicsFormat.R8G8B8A8_UNorm, GraphicsFormat.R8G8B8A8_SRGB, GraphicsFormat.B8G8R8A8_UNorm, GraphicsFormat.B8G8R8A8_SRGB,
            GraphicsFormat.B10G11R11_UFloatPack32, GraphicsFormat.R16G16B16A16_SFloat, GraphicsFormat.R32G32B32A32_SFloat,
            GraphicsFormat.A2B10G10R10_UNormPack32, GraphicsFormat.R16G16_SFloat, GraphicsFormat.R16_SFloat, GraphicsFormat.R32_SFloat,
            GraphicsFormat.R8_UNorm, GraphicsFormat.R8G8_UNorm, GraphicsFormat.R16_UNorm, GraphicsFormat.R32_UInt, GraphicsFormat.R16G16B16A16_UNorm,
            GraphicsFormat.R5G6B5_UNormPack16, GraphicsFormat.D16_UNorm, GraphicsFormat.D24_UNorm_S8_UInt, GraphicsFormat.D32_SFloat,
            GraphicsFormat.D32_SFloat_S8_UInt, GraphicsFormat.D24_UNorm, GraphicsFormat.S8_UInt, GraphicsFormat.RGBA_DXT5_UNorm,
            GraphicsFormat.RGBA_BC7_UNorm, GraphicsFormat.RGBA_ASTC4X4_UNorm, GraphicsFormat.RGB_ETC2_UNorm,
        };
        var usages = new[]
        {
            GraphicsFormatUsage.Sample, GraphicsFormatUsage.Linear, GraphicsFormatUsage.Render, GraphicsFormatUsage.Blend,
            GraphicsFormatUsage.LoadStore, GraphicsFormatUsage.MSAA2x, GraphicsFormatUsage.MSAA4x, GraphicsFormatUsage.MSAA8x,
            GraphicsFormatUsage.StencilSampling, GraphicsFormatUsage.GetPixels, GraphicsFormatUsage.SetPixels,
        };
        foreach (var f in formats)
        {
            foreach (var u in usages) Line("IsFormatSupported." + f + "." + u, SystemInfo.IsFormatSupported(f, u));
            Line("GetCompatibleFormat.Render." + f, SystemInfo.GetCompatibleFormat(f, GraphicsFormatUsage.Render));
            Line("GetCompatibleFormat.Sample." + f, SystemInfo.GetCompatibleFormat(f, GraphicsFormatUsage.Sample));
        }
        // Every format: the usages it supports (a GraphicsFormatUsage mask) and its compatible formats.
        var allUsages = (GraphicsFormatUsage[])Enum.GetValues(typeof(GraphicsFormatUsage));
        foreach (GraphicsFormat f in Enum.GetValues(typeof(GraphicsFormat)))
        {
            if (f == GraphicsFormat.None) continue;
            long mask = 0;
            foreach (var u in allUsages)
                if (u != GraphicsFormatUsage.None && SystemInfo.IsFormatSupported(f, u)) mask |= (long)u;
            Line("usages." + (int)f, mask);
            Line("compatible.render." + (int)f, (int)SystemInfo.GetCompatibleFormat(f, GraphicsFormatUsage.Render));
            Line("compatible.sample." + (int)f, (int)SystemInfo.GetCompatibleFormat(f, GraphicsFormatUsage.Sample));
            Line("compatible.blend." + (int)f, (int)SystemInfo.GetCompatibleFormat(f, GraphicsFormatUsage.Blend));
            Line("compatible.loadstore." + (int)f, (int)SystemInfo.GetCompatibleFormat(f, GraphicsFormatUsage.LoadStore));
        }
        foreach (RenderTextureFormat f in Enum.GetValues(typeof(RenderTextureFormat)))
        {
            try { Line("SupportsRenderTextureFormat." + f, SystemInfo.SupportsRenderTextureFormat(f)); } catch (Exception) { }
            try { Line("SupportsRandomWriteOnRenderTextureFormat." + f, SystemInfo.SupportsRandomWriteOnRenderTextureFormat(f)); } catch (Exception) { }
        }
        foreach (BuiltinShaderDefine d in Enum.GetValues(typeof(BuiltinShaderDefine)))
            Line("HasShaderDefine." + d, GraphicsSettings.HasShaderDefine(d));
        Line("enableLODCrossFade", QualitySettings.enableLODCrossFade);
        Line("preserveFramebufferAlpha", Graphics.preserveFramebufferAlpha);
        Line("activeTier", Graphics.activeTier);
        // Spherical harmonics (native in Unity): coefficients after adding lights, evaluations.
        var sh = new SphericalHarmonicsL2();
        sh.AddAmbientLight(new Color(1f, 0.5f, 0.25f));
        SH("sh.ambient", sh);
        sh.Clear();
        sh.AddDirectionalLight(new Vector3(0.36f, 0.48f, 0.8f), new Color(1f, 0.5f, 0.25f), 0.75f);
        SH("sh.directional", sh);
        var directions = new[] { Vector3.up, Vector3.down, Vector3.right, Vector3.forward, new Vector3(0.6f, 0f, 0.8f) };
        var results = new Color[directions.Length];
        sh.Evaluate(directions, results);
        for (int i = 0; i < results.Length; i++) Line("sh.directional.evaluate." + i, Floats(results[i].r, results[i].g, results[i].b));
        // The probe scene's ambient probe and lighting settings.
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/Probe.unity");
        Line("ambientMode", RenderSettings.ambientMode);
        Line("ambientSkyColor", RenderSettings.ambientSkyColor);
        Line("ambientIntensity", Floats(RenderSettings.ambientIntensity));
        SH("ambientProbe", RenderSettings.ambientProbe);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientLight = new Color(0.2f, 0.4f, 0.6f);
        SH("ambientProbe.flat", RenderSettings.ambientProbe);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.8f, 0.4f, 0.2f);
        RenderSettings.ambientEquatorColor = new Color(0.1f, 0.5f, 0.3f);
        RenderSettings.ambientGroundColor = new Color(0.05f, 0.1f, 0.6f);
        SH("ambientProbe.trilight", RenderSettings.ambientProbe);
        Line("BatchRendererGroup.BufferTarget", BatchRendererGroup.BufferTarget);
        Line("BatchRendererGroup.GetConstantBufferMaxWindowSize", BatchRendererGroup.GetConstantBufferMaxWindowSize());
        Line("BatchRendererGroup.GetConstantBufferOffsetAlignment", BatchRendererGroup.GetConstantBufferOffsetAlignment());
        foreach (var type in new[] { LightType.Directional, LightType.Point })
            foreach (var mode in new[] { LightmapBakeType.Realtime, LightmapBakeType.Mixed, LightmapBakeType.Baked })
            {
                var go = new GameObject("baking probe");
                var light = go.AddComponent<Light>();
                light.type = type;
                light.lightmapBakeType = mode;
                var o = light.bakingOutput;
                Line($"Light.bakingOutput.{type}.{mode}", $"{o.probeOcclusionLightIndex} {o.occlusionMaskChannel} {o.lightmapBakeType} {o.mixedLightingMode} {o.isBaked}");
                UnityEngine.Object.DestroyImmediate(go);
            }
        File.WriteAllText(Environment.GetEnvironmentVariable("CAPS_PROBE_OUT"), s_Out.ToString());
        EditorApplication.Exit(0);
    }
}
