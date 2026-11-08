using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>The post-processing scenarios, each one Volume or camera setting on the Probe scene:
/// color grading (a Color Lookup texture, the grading effects URP bakes into its internal LUT,
/// the HDR grading mode), lens effects, motion blur, the camera's anti-aliasing modes and render
/// scale upscaling filters; and texture-samples.jsonl, what the Single Channel textures sample
/// as. The main camera is disabled throughout, so it renders only when the probe
/// renders it (one render per frame in the multi-frame scenarios).</summary>
public partial class UrpProbe
{
    /// <summary>The camera the post-processing scenarios record (disabled, so not Camera.main).</summary>
    static Camera s_Camera;

    IEnumerator PostFxScenarios(string dir, UniversalRenderPipelineAsset asset)
    {
        var assets = Resources.Load<PostFxAssets>("PostFxAssets");
        RecordTextureSamples(Path.Combine(dir, "texture-samples.jsonl"));
        s_ExtraKeywords = Array.Empty<string>();
        var camera = Camera.main;
        var cameraData = camera.GetComponent<UniversalAdditionalCameraData>();
        camera.enabled = false;
        s_Camera = camera;

        // Color grading (the asset's default Low Dynamic Range mode).
        Post(dir, "postfx_lut", p => { var l = p.Add<ColorLookup>(true); l.texture.Override(assets.lut); l.contribution.Override(0.8f); });
        Post(dir, "postfx_tonemap_neutral", p => p.Add<Tonemapping>(true).mode.Override(TonemappingMode.Neutral));
        Post(dir, "postfx_white_balance", p => { var w = p.Add<WhiteBalance>(true); w.temperature.Override(40f); w.tint.Override(-25f); });
        Post(dir, "postfx_channel_mixer", p =>
        {
            var m = p.Add<ChannelMixer>(true);
            m.redOutRedIn.Override(70f);
            m.redOutGreenIn.Override(40f);
            m.greenOutBlueIn.Override(30f);
            m.blueOutRedIn.Override(-20f);
            m.blueOutBlueIn.Override(140f);
        });
        Post(dir, "postfx_lift_gamma_gain", p =>
        {
            var l = p.Add<LiftGammaGain>(true);
            l.lift.Override(new Vector4(1f, 0.9f, 0.7f, 0.1f));
            l.gamma.Override(new Vector4(0.8f, 1f, 1.2f, -0.15f));
            l.gain.Override(new Vector4(1f, 1f, 0.9f, 0.2f));
        });
        Post(dir, "postfx_shadows_midtones_highlights", p =>
        {
            var s = p.Add<ShadowsMidtonesHighlights>(true);
            s.shadows.Override(new Vector4(0.6f, 0.8f, 1.2f, 0.2f));
            s.midtones.Override(new Vector4(1.1f, 1f, 0.9f, 0f));
            s.highlights.Override(new Vector4(1f, 0.9f, 0.8f, -0.2f));
            s.shadowsEnd.Override(0.4f);
            s.highlightsStart.Override(0.5f);
        });
        Post(dir, "postfx_split_toning", p =>
        {
            var s = p.Add<SplitToning>(true);
            s.shadows.Override(new Color(0.2f, 0.4f, 0.9f));
            s.highlights.Override(new Color(1f, 0.7f, 0.3f));
            s.balance.Override(-20f);
        });
        Post(dir, "postfx_curves", p =>
        {
            var c = p.Add<ColorCurves>(true);
            // An S-curve on the master curve, a hue-dependent saturation dip around green.
            c.master.Override(new TextureCurve(new[] { new Keyframe(0f, 0f, 0.5f, 0.5f), new Keyframe(0.3f, 0.2f), new Keyframe(0.7f, 0.85f), new Keyframe(1f, 1f, 0.5f, 0.5f) }, 0f, false, new Vector2(0f, 1f)));
            c.hueVsSat.Override(new TextureCurve(new[] { new Keyframe(0.2f, 0.5f), new Keyframe(0.33f, 0.1f), new Keyframe(0.5f, 0.5f) }, 0.5f, true, new Vector2(0f, 1f)));
        });
        var gradingMode = asset.colorGradingMode;
        Post(dir, "postfx_grading_hdr", p =>
        {
            var c = p.Add<ColorAdjustments>(true);
            c.postExposure.Override(0.7f);
            c.saturation.Override(20f);
            c.contrast.Override(10f);
            p.Add<Tonemapping>(true).mode.Override(TonemappingMode.ACES);
        }, () => asset.colorGradingMode = ColorGradingMode.HighDynamicRange, () => asset.colorGradingMode = gradingMode);
        Post(dir, "postfx_lut_hdr", p => { var l = p.Add<ColorLookup>(true); l.texture.Override(assets.lut); l.contribution.Override(0.8f); },
            () => asset.colorGradingMode = ColorGradingMode.HighDynamicRange, () => asset.colorGradingMode = gradingMode);

        // Lens effects.
        Post(dir, "postfx_dof_gaussian", p =>
        {
            var d = p.Add<DepthOfField>(true);
            d.mode.Override(DepthOfFieldMode.Gaussian);
            d.gaussianStart.Override(5f);
            d.gaussianEnd.Override(9f);
            d.gaussianMaxRadius.Override(1.5f);
        });
        Post(dir, "postfx_dof_gaussian_hq", p =>
        {
            var d = p.Add<DepthOfField>(true);
            d.mode.Override(DepthOfFieldMode.Gaussian);
            d.gaussianStart.Override(5f);
            d.gaussianEnd.Override(9f);
            d.gaussianMaxRadius.Override(1.5f);
            d.highQualitySampling.Override(true);
        });
        Post(dir, "postfx_dof_bokeh", p =>
        {
            var d = p.Add<DepthOfField>(true);
            d.mode.Override(DepthOfFieldMode.Bokeh);
            d.focusDistance.Override(6.2f);
            d.aperture.Override(2f);
            d.focalLength.Override(90f);
            d.bladeCount.Override(6);
            d.bladeCurvature.Override(0.5f);
            d.bladeRotation.Override(20f);
        });
        Post(dir, "postfx_lens_distortion", p =>
        {
            var l = p.Add<LensDistortion>(true);
            l.intensity.Override(0.5f);
            l.xMultiplier.Override(0.8f);
            l.center.Override(new Vector2(0.45f, 0.55f));
            l.scale.Override(0.9f);
        });
        Post(dir, "postfx_chromatic_aberration", p => p.Add<ChromaticAberration>(true).intensity.Override(1f));
        Post(dir, "postfx_panini", p => { var c = p.Add<PaniniProjection>(true); c.distance.Override(0.8f); c.cropToFit.Override(0.5f); });
        Post(dir, "postfx_film_grain", p =>
        {
            var g = p.Add<FilmGrain>(true);
            g.type.Override(FilmGrainLookup.Medium3);
            g.intensity.Override(0.8f);
            g.response.Override(0.6f);
        });

        // Motion blur: the camera (or the cube) moves between two frames.
        var cube = GameObject.Find("Cube").transform;
        Action<VolumeProfile, MotionBlurMode, MotionBlurQuality> blur = (p, mode, quality) =>
        {
            var m = p.Add<MotionBlur>(true);
            m.mode.Override(mode);
            m.quality.Override(quality);
            m.intensity.Override(1f);
            m.clamp.Override(0.1f);
        };
        var (cameraPosition, cameraRotation) = (camera.transform.position, camera.transform.rotation);
        Action moveCamera = () => camera.transform.SetPositionAndRotation(cameraPosition + new Vector3(0.25f, 0f, 0f), cameraRotation * Quaternion.Euler(0f, 4f, 0f));
        Action restoreCamera = () => camera.transform.SetPositionAndRotation(cameraPosition, cameraRotation);
        yield return Moving(dir, "motion_blur_camera", p => blur(p, MotionBlurMode.CameraOnly, MotionBlurQuality.Low), moveCamera, restoreCamera);
        yield return Moving(dir, "motion_blur_camera_high", p => blur(p, MotionBlurMode.CameraOnly, MotionBlurQuality.High), moveCamera, restoreCamera);
        var (cubePosition, cubeRotation) = (cube.position, cube.rotation);
        yield return Moving(dir, "motion_blur_objects", p => blur(p, MotionBlurMode.CameraAndObjects, MotionBlurQuality.Medium),
            () => cube.SetPositionAndRotation(cubePosition + new Vector3(0.3f, 0.1f, 0f), cubeRotation * Quaternion.Euler(0f, 15f, 0f)),
            () => cube.SetPositionAndRotation(cubePosition, cubeRotation));

        // Anti-aliasing (post-processing on, no Volume effects).
        Post(dir, "aa_fxaa", null, () => cameraData.antialiasing = AntialiasingMode.FastApproximateAntialiasing, () => cameraData.antialiasing = AntialiasingMode.None);
        foreach (var quality in new[] { AntialiasingQuality.Low, AntialiasingQuality.Medium, AntialiasingQuality.High })
            Post(dir, "aa_smaa_" + quality.ToString().ToLowerInvariant(), null, () =>
            {
                cameraData.antialiasing = AntialiasingMode.SubpixelMorphologicalAntiAliasing;
                cameraData.antialiasingQuality = quality;
            }, () => { cameraData.antialiasing = AntialiasingMode.None; cameraData.antialiasingQuality = AntialiasingQuality.High; });
        yield return Accumulated(dir, "aa_taa", () => cameraData.antialiasing = AntialiasingMode.TemporalAntiAliasing, () => cameraData.antialiasing = AntialiasingMode.None);

        // Render scale upscaling filters.
        var (renderScale, upscaling) = (asset.renderScale, asset.upscalingFilter);
        Action restoreScale = () => { asset.renderScale = renderScale; asset.upscalingFilter = upscaling; };
        Post(dir, "upscale_point", null, () => { asset.renderScale = 0.5f; asset.upscalingFilter = UpscalingFilterSelection.Point; }, restoreScale);
        Post(dir, "upscale_linear", null, () => { asset.renderScale = 0.5f; asset.upscalingFilter = UpscalingFilterSelection.Linear; }, restoreScale);
        // 0.7 scales 320x180 to 223x125 in the Editor's script runtime (its float products in
        // double precision); 0.75 is exact either way.
        Post(dir, "upscale_auto_fractional", null, () => { asset.renderScale = 0.7f; asset.upscalingFilter = UpscalingFilterSelection.Auto; }, restoreScale);
        Post(dir, "upscale_fsr", null, () => { asset.renderScale = 0.75f; asset.upscalingFilter = UpscalingFilterSelection.FSR; }, restoreScale);
        Post(dir, "upscale_fsr_sharpness", null, () =>
        {
            asset.renderScale = 0.75f;
            asset.upscalingFilter = UpscalingFilterSelection.FSR;
            asset.fsrOverrideSharpness = true;
            asset.fsrSharpness = 0.3f;
        }, () => { restoreScale(); asset.fsrOverrideSharpness = false; });
        yield return Accumulated(dir, "upscale_stp", () => { asset.renderScale = 0.5f; asset.upscalingFilter = UpscalingFilterSelection.STP; }, restoreScale);

        camera.enabled = true;
        s_Camera = null;
    }

    /// <summary>One single-frame scenario: post-processing on, a global Volume whose profile
    /// <paramref name="configure"/> fills (none when null), and an optional other setting.</summary>
    void Post(string dir, string name, Action<VolumeProfile> configure, Action setup = null, Action teardown = null)
    {
        GameObject volume = null;
        VolumeProfile profile = null;
        Scenario(dir, name, () =>
        {
            (volume, profile) = AddVolume(configure);
            setup?.Invoke();
        }, () =>
        {
            teardown?.Invoke();
            RemoveVolume(volume, profile);
        });
    }

    (GameObject, VolumeProfile) AddVolume(Action<VolumeProfile> configure)
    {
        s_Camera.GetComponent<UniversalAdditionalCameraData>().renderPostProcessing = true;
        var volume = new GameObject("Volume");
        var v = volume.AddComponent<Volume>();
        v.isGlobal = true;
        var profile = ScriptableObject.CreateInstance<VolumeProfile>();
        configure?.Invoke(profile);
        v.sharedProfile = profile;
        return (volume, profile);
    }

    void RemoveVolume(GameObject volume, VolumeProfile profile)
    {
        s_Camera.GetComponent<UniversalAdditionalCameraData>().renderPostProcessing = false;
        DestroyImmediate(volume);
        DestroyImmediate(profile);
    }

    /// <summary>A two-frame scenario: one render, then <paramref name="move"/> in the next frame
    /// and the recorded render (URP keeps the camera's previous matrices once per frame).</summary>
    IEnumerator Moving(string dir, string name, Action<VolumeProfile> configure, Action move, Action restore)
    {
        var (volume, profile) = AddVolume(configure);
        RenderOnce();
        yield return null;
        move();
        Record(Path.Combine(dir, name + ".png"), Path.Combine(dir, name + ".jsonl"));
        restore();
        RemoveVolume(volume, profile);
        // A frame without the effect, so the next scenario's previous frame is its own.
        yield return null;
    }

    /// <summary>Frames a temporal effect accumulates over before the recorded one.</summary>
    const int AccumulatedFrames = 8;

    /// <summary>A temporal scenario: one render per frame for <see cref="AccumulatedFrames"/>
    /// frames, then the recorded render.</summary>
    IEnumerator Accumulated(string dir, string name, Action setup, Action teardown)
    {
        var (volume, profile) = AddVolume(null);
        setup();
        for (int i = 0; i < AccumulatedFrames; i++)
        {
            RenderOnce();
            yield return null;
        }
        Record(Path.Combine(dir, name + ".png"), Path.Combine(dir, name + ".jsonl"));
        teardown();
        RemoveVolume(volume, profile);
        yield return null;
    }

    /// <summary>One render of the probe camera into a target like the recorded one's.</summary>
    static void RenderOnce()
    {
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32) { antiAliasing = s_Antialiasing };
        s_Camera.targetTexture = target;
        s_Camera.Render();
        s_Camera.targetTexture = null;
        target.Release();
        DestroyImmediate(target);
    }

    /// <summary>What the Single Channel textures sample as: each blitted into a float target, a
    /// few texels read back.</summary>
    static void RecordTextureSamples(string path)
    {
        var features = Resources.Load<FeatureAssets>("FeatureAssets");
        var postFx = Resources.Load<PostFxAssets>("PostFxAssets");
        var lines = new StringBuilder();
        foreach (var texture in new[] { features.singleAlpha, features.singleRed, postFx.singleAlphaCompressed, postFx.singleRedCompressed, postFx.singleRedSrgb })
        {
            var target = new RenderTexture(texture.width, texture.height, 0, RenderTextureFormat.ARGBFloat);
            Graphics.Blit(texture, target);
            var readback = new Texture2D(texture.width, texture.height, TextureFormat.RGBAFloat, false);
            RenderTexture.active = target;
            readback.ReadPixels(new Rect(0, 0, texture.width, texture.height), 0, 0);
            readback.Apply();
            RenderTexture.active = null;
            foreach (var (x, y) in new[] { (0, 0), (5, 3), (17, 9), (31, 30) })
            {
                var c = readback.GetPixel(x, y);
                lines.Append("{\"key\":\"").Append(texture.name).Append(" sample ").Append(x).Append(',').Append(y).Append("\",\"value\":[")
                    .Append(string.Join(",", new[] { c.r, c.g, c.b, c.a }.Select(f => f.ToString("R", CultureInfo.InvariantCulture)))).Append("]}\n");
            }
            target.Release();
            DestroyImmediate(target);
            DestroyImmediate(readback);
        }
        // The formats of the controls, but SingleRedCompressed's BC4: Unbound keeps compressed
        // imports' texels uncompressed (docs/COMPATIBILITY_GAPS.md, Q126 row).
        AppendTextureFormat(lines, postFx.singleAlphaCompressed);
        AppendTextureFormat(lines, postFx.singleRedSrgb);
        File.WriteAllText(path, lines.ToString());
    }
}
