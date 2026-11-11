using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// With <c>REALTIME_GI_OUT</c> set: plays each room scene with <c>DynamicGI.synchronousMode</c>
/// on, changes one input of the realtime global illumination at a time and records, per case, the
/// realtime lightmap atlas texels (<c>&lt;case&gt;.atlas</c>; directional scenes also
/// <c>&lt;case&gt;.dir</c>), light probe SH at fixed points and the API state
/// (<c>values.jsonl</c>), and a frame (<c>&lt;case&gt;.png</c>). Then quits.
/// Atlas files: int32 width, int32 height, then width*height RGBA half floats, row by row as the
/// render target reads back; alpha 0 marks texels no renderer covers.
/// </summary>
public class RealtimeGIProbe : MonoBehaviour
{
    const int Width = 192, Height = 108;
    // Unity updates realtime GI over several frames on worker threads, so the frame at which a
    // change shows is not reproducible; captures wait a fixed number of frames, then for
    // DynamicGI.isConverged, then a few more frames, so they record the converged lighting.
    const int BeforeConverged = 300, MaxFrames = 3000, AfterConverged = 60;
    readonly StringBuilder m_Out = new StringBuilder();
    string m_Dir;
    Material m_Dump;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("REALTIME_GI_OUT"))) return;
        var go = new GameObject("RealtimeGIProbe");
        DontDestroyOnLoad(go);
        go.AddComponent<RealtimeGIProbe>();
    }

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string B(bool v) => v ? "true" : "false";
    static string V(Vector4 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "," + F(v.w) + "]";

    IEnumerator Start()
    {
        m_Dir = Environment.GetEnvironmentVariable("REALTIME_GI_OUT");
        Directory.CreateDirectory(m_Dir);
        m_Dump = new Material(Shader.Find("Hidden/RealtimeGIDump"));
        DynamicGI.synchronousMode = true;
        if (GraphicsSettings.currentRenderPipeline != null)
        {
            yield return Pipeline();
            yield break;
        }

        yield return Load("Room");
        var s = Scene();
        yield return Converge();
        Capture("room-base", s);
        RecordBakedProbes("room-lightprobes");

        s.point.transform.position = new Vector3(1.5f, 2.5f, -1.5f);
        // DynamicGI.isConverged on the frame after the change: the update has to start again.
        yield return null;
        Record("room-moved-next-frame", "\"converged\":" + B(DynamicGI.isConverged));
        yield return Converge();
        Capture("room-moved", s);

        s.point.intensity = 4f;
        yield return Converge();
        Capture("room-intensity", s);

        s.point.bounceIntensity = 3f;
        yield return Converge();
        Capture("room-bounce", s);
        s.point.bounceIntensity = 1f;

        float indirect = DynamicGI.indirectScale;
        Record("room-indirectscale-default", "\"indirectScale\":" + F(indirect));
        DynamicGI.indirectScale = 2f;
        yield return Converge();
        Capture("room-indirectscale", s);
        DynamicGI.indirectScale = indirect;
        yield return Converge();

        // Emission: a material change with UpdateGIMaterials, then DynamicGI.SetEmissive and another
        // material change with UpdateGIMaterials after it. (Without the call the Editor picks the
        // material change up or not depending on timing, so there is no reference for that.)
        s.panel.sharedMaterial.SetColor("_EmissionColor", new Color(0.2f, 1f, 0.2f) * 2f);
        s.panel.UpdateGIMaterials();
        yield return Converge();
        Capture("room-emission-call", s);
        DynamicGI.SetEmissive(s.panel, new Color(0f, 1f, 1f) * 3f);
        yield return Converge();
        Capture("room-setemissive", s);
        s.panel.sharedMaterial.SetColor("_EmissionColor", new Color(1f, 0f, 1f) * 3f);
        s.panel.UpdateGIMaterials();
        yield return Converge();
        Capture("room-setemissive-then-call", s);

        // Albedo: the red wall turns blue, before and after UpdateGIMaterials.
        s.leftWall.sharedMaterial.color = new Color(0.1f, 0.1f, 0.8f);
        yield return Converge();
        Capture("room-albedo-nocall", s);
        s.leftWall.UpdateGIMaterials();
        yield return Converge();
        Capture("room-albedo-call", s);

        // Environment: the ambient color, before and after DynamicGI.UpdateEnvironment; then a
        // procedural skybox as the ambient source, its exposure changed before and after the call.
        RenderSettings.ambientLight = new Color(0.6f, 0.5f, 0.2f);
        yield return Converge();
        Capture("room-env-nocall", s);
        DynamicGI.UpdateEnvironment();
        yield return Converge();
        Capture("room-env-call", s);
        var sky = new Material(Shader.Find("Skybox/Procedural"));
        sky.SetFloat("_Exposure", 1f);
        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        DynamicGI.UpdateEnvironment();
        yield return Converge();
        Capture("room-sky", s);
        sky.SetFloat("_Exposure", 3f);
        yield return Converge();
        Capture("room-sky-nocall", s);
        DynamicGI.UpdateEnvironment();
        yield return Converge();
        Capture("room-sky-call", s);
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.skybox = null;
        DynamicGI.UpdateEnvironment();

        // updateThreshold above its default leaves output that depends on update timing (changes
        // below it wait for later ones); its default is recorded, and 0 in the other scenes.
        Record("room-threshold-default", "\"updateThreshold\":" + F(DynamicGI.updateThreshold));

        // The point light off and the spot light on.
        s.point.enabled = false;
        yield return Converge();
        Capture("room-pointoff", s);

        s.spot.enabled = true;
        yield return Converge();
        Capture("room-spot", s);

        // The spot light from the left at the block's side: the right wall behind the block is
        // in its shadow, so it is lit only if the realtime GI input ignores the light's shadows.
        s.spot.transform.SetPositionAndRotation(new Vector3(-1.5f, 1f, 0.8f), Quaternion.Euler(0f, 90f, 0f));
        s.spot.spotAngle = 60f;
        yield return Converge();
        Capture("room-spot-behind", s);
        s.spot.enabled = false;

        // A realtime directional light through the open front, with shadows, at two directions
        // between those the precomputed visibility holds: the block and the ceiling shade parts
        // of the room.
        var sun = Sun(LightShadows.Hard, 40f, 25f);
        yield return Converge();
        Capture("room-directional", s);
        sun.transform.rotation = Quaternion.Euler(57f, -33f, 0f);
        yield return Converge();
        Capture("room-directional-turned", s);
        // A new directional light without shadows, at the first direction.
        Destroy(sun.gameObject);
        sun = Sun(LightShadows.None, 40f, 25f);
        yield return Converge();
        Capture("room-directional-noshadows", s);
        Destroy(sun.gameObject);

        // The environment alone (no light on, the panel's emission set to black): a gradient
        // (Trilight) ambient, then the procedural skybox, then the skybox with ambient
        // intensity 2.
        DynamicGI.SetEmissive(s.panel, Color.black);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        RenderSettings.ambientSkyColor = new Color(0.5f, 0.6f, 0.9f);
        RenderSettings.ambientEquatorColor = new Color(0.4f, 0.35f, 0.3f);
        RenderSettings.ambientGroundColor = new Color(0.15f, 0.12f, 0.1f);
        DynamicGI.UpdateEnvironment();
        yield return Converge();
        Capture("room-gradient", s);
        var skyOnly = new Material(Shader.Find("Skybox/Procedural"));
        skyOnly.SetFloat("_Exposure", 1f);
        RenderSettings.skybox = skyOnly;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        DynamicGI.UpdateEnvironment();
        yield return Converge();
        Capture("room-sky-only", s);
        RenderSettings.ambientIntensity = 2f;
        DynamicGI.UpdateEnvironment();
        yield return Converge();
        Capture("room-sky-only-intensity", s);

        // RenderSettings.ambientProbe at another ambient intensity without
        // DynamicGI.UpdateEnvironment (within the frame: realtime GI does not see it).
        RenderSettings.ambientIntensity = 0.5f;
        Record("room-sky-only-intensity-half", "\"ambientProbe\":" + AmbientProbe());

        // The sky with a sun that adds no light of its own to realtime GI (bounce intensity 0):
        // the ambient intensity back at 1 and a directional light, then that light turned, both
        // without DynamicGI.UpdateEnvironment.
        RenderSettings.ambientIntensity = 1f;
        sun = Sun(LightShadows.None, 40f, 25f);
        sun.bounceIntensity = 0f;
        yield return Converge();
        Capture("room-sky-sun", s);
        sun.transform.rotation = Quaternion.Euler(15f, -120f, 0f);
        yield return Converge();
        Capture("room-sky-sun-turned", s);
        Destroy(sun.gameObject);
        yield return Converge();

        // DynamicGI.SetEnvironmentData, once the sky without the sun has settled: every face its
        // own colors, brighter along its columns (red) and rows (green). Then the ambient
        // intensity changed (documented: that replaces the data), and the data set again
        // followed by DynamicGI.UpdateEnvironment. Then the ambient source changed over the data
        // (documented: that replaces it too): the data set again and the ambient mode set to
        // the gradient; and the data set in that mode, followed by the ambient intensity, which
        // scales nothing there. What the call throws for an array of another length and for
        // none.
        var data = EnvironmentData();
        DynamicGI.SetEnvironmentData(data);
        yield return Converge();
        Capture("room-envdata", s);
        RenderSettings.ambientIntensity = 2f;
        yield return Converge();
        Capture("room-envdata-intensity", s);
        DynamicGI.SetEnvironmentData(data);
        yield return Converge();
        DynamicGI.UpdateEnvironment();
        yield return Converge();
        Capture("room-envdata-update", s);
        DynamicGI.SetEnvironmentData(data);
        yield return Converge();
        RenderSettings.ambientMode = AmbientMode.Trilight;
        yield return Converge();
        Capture("room-envdata-source", s);
        DynamicGI.SetEnvironmentData(data);
        yield return Converge();
        RenderSettings.ambientIntensity = 3f;
        yield return Converge();
        Capture("room-envdata-gradient-intensity", s);
        Record("room-envdata-errors", "\"wrongLength\":" + Thrown(new float[100], true) + ",\"noArray\":" + Thrown(null, false));

        // With the ambient intensity at 3 (documented: it scales only a skybox's light): a
        // skybox ambient mode without a skybox material, the gradient again, and a custom
        // ambient mode with another ambient color.
        RenderSettings.ambientMode = AmbientMode.Skybox;
        RenderSettings.skybox = null;
        yield return Converge();
        Capture("room-sky-none", s);
        RenderSettings.ambientMode = AmbientMode.Trilight;
        yield return Converge();
        Capture("room-gradient-intensity", s);
        RenderSettings.ambientMode = AmbientMode.Custom;
        RenderSettings.ambientLight = new Color(0.2f, 0.7f, 0.3f);
        yield return Converge();
        Capture("room-ambient-custom", s);

        // DynamicGI.SetEnvironmentData in the frame of a change of the ambient source: the
        // ambient color changed and the data set after it, then the data set and the color
        // changed after it; then, with the skybox as the source, its exposure changed and the
        // data set after it.
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.ambientMode = AmbientMode.Flat;
        yield return Converge();
        RenderSettings.ambientLight = new Color(0.7f, 0.2f, 0.2f);
        DynamicGI.SetEnvironmentData(data);
        yield return Converge();
        Capture("room-envdata-after-color", s);
        DynamicGI.SetEnvironmentData(data);
        RenderSettings.ambientLight = new Color(0.2f, 0.2f, 0.7f);
        yield return Converge();
        Capture("room-envdata-before-color", s);
        RenderSettings.skybox = skyOnly;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        yield return Converge();
        skyOnly.SetFloat("_Exposure", 2f);
        DynamicGI.SetEnvironmentData(data);
        yield return Converge();
        Capture("room-envdata-after-sky", s);
        RenderSettings.skybox = null;
        RenderSettings.ambientIntensity = 1f;
        RenderSettings.ambientMode = AmbientMode.Flat;
        DynamicGI.UpdateEnvironment();

        foreach (var name in new[] { "RoomScaled", "RoomDirectional", "RoomSystems" })
        {
            yield return Load(name);
            s = Scene();
            var label = name.ToLowerInvariant();
            Record(label + "-indirectscale-default", "\"indirectScale\":" + F(DynamicGI.indirectScale));
            yield return Converge();
            Capture(label + "-base", s);
            s.point.transform.position = new Vector3(1.5f, 2.5f, -1.5f);
            yield return Converge();
            Capture(label + "-moved", s);

            // DynamicGI.updateThreshold: from the same brighter bounce state, the indirect scale
            // 2 at the default threshold of 1 (parts whose inputs change by less than 1% keep
            // their earlier lighting) and at 0 (every part is updated until the output stops
            // changing).
            float sceneScale = DynamicGI.indirectScale;
            foreach (var threshold in new[] { 1f, 0f })
            {
                s.point.bounceIntensity = 3f;
                DynamicGI.indirectScale = sceneScale;
                yield return Converge();
                DynamicGI.updateThreshold = threshold;
                s.point.bounceIntensity = 1f;
                DynamicGI.indirectScale = 2f;
                yield return Converge();
                Capture(label + (threshold > 0f ? "-indirectscale" : "-threshold0"), s);
            }
            DynamicGI.updateThreshold = 1f;
            DynamicGI.indirectScale = sceneScale;

            // Directionality with hardly any light: no light on, the panel's emission black and
            // a dark ambient color, so that parts of the lightmap receive next to nothing.
            if (LightmapSettings.lightmapsMode == LightmapsMode.CombinedDirectional)
            {
                s.point.enabled = false;
                DynamicGI.SetEmissive(s.panel, Color.black);
                RenderSettings.ambientLight = new Color(0.02f, 0.025f, 0.03f);
                yield return Converge();
                Capture(label + "-dim", s);
            }
        }

        // What objects outside every reflection probe reflect in a scene without a sky.
        Record("end-reflection", DefaultReflection());

        // RenderSettings.ambientProbe in the custom ambient mode, within one frame: after a scene
        // load, then a probe set by script, the ambient intensity changed under it, the flat mode
        // and the custom one again, and the skybox mode (the scene has no skybox material).
        RenderSettings.ambientMode = AmbientMode.Custom;
        Record("end-custom-loaded", "\"ambientProbe\":" + AmbientProbe());
        var given = new SphericalHarmonicsL2();
        given.AddAmbientLight(new Color(0.3f, 0.2f, 0.1f));
        given.AddDirectionalLight(Vector3.up, new Color(0.1f, 0.4f, 0.2f), 1f);
        RenderSettings.ambientProbe = given;
        Record("end-custom-set", "\"ambientProbe\":" + AmbientProbe());
        RenderSettings.ambientIntensity = 2f;
        Record("end-custom-set-intensity", "\"ambientProbe\":" + AmbientProbe());
        RenderSettings.ambientMode = AmbientMode.Flat;
        Record("end-custom-set-flat", "\"ambientProbe\":" + AmbientProbe());
        RenderSettings.ambientMode = AmbientMode.Custom;
        Record("end-custom-again", "\"ambientProbe\":" + AmbientProbe());
        RenderSettings.ambientMode = AmbientMode.Skybox;
        Record("end-custom-skybox", "\"ambientProbe\":" + AmbientProbe());

        // The skybox mode with the probe a script gave: a skybox material assigned, then the
        // ambient intensity changed, another probe given and the intensity changed again.
        RenderSettings.skybox = new Material(Shader.Find("Skybox/Procedural"));
        Record("end-skybox-material", "\"ambientProbe\":" + AmbientProbe());
        RenderSettings.ambientIntensity = 1.5f;
        Record("end-skybox-intensity", "\"ambientProbe\":" + AmbientProbe());
        var other = new SphericalHarmonicsL2();
        other.AddAmbientLight(new Color(0.05f, 0.1f, 0.4f));
        other.AddDirectionalLight(Vector3.right, new Color(0.5f, 0.1f, 0.1f), 1f);
        RenderSettings.ambientProbe = other;
        Record("end-skybox-set", "\"ambientProbe\":" + AmbientProbe());
        RenderSettings.ambientIntensity = 0.5f;
        Record("end-skybox-set-intensity", "\"ambientProbe\":" + AmbientProbe());
        // The flat mode with a probe given, then another ambient color; and the skybox mode
        // entered without a skybox material.
        RenderSettings.ambientMode = AmbientMode.Flat;
        RenderSettings.ambientProbe = given;
        Record("end-flat-set", "\"ambientProbe\":" + AmbientProbe());
        RenderSettings.ambientLight = new Color(0.4f, 0.3f, 0.6f);
        Record("end-flat-set-color", "\"ambientProbe\":" + AmbientProbe());
        RenderSettings.skybox = null;
        Record("end-flat-noskybox", "\"ambientProbe\":" + AmbientProbe());
        RenderSettings.ambientMode = AmbientMode.Skybox;
        Record("end-skybox-none", "\"ambientProbe\":" + AmbientProbe());
        RenderSettings.ambientMode = AmbientMode.Custom;
        Record("end-custom-last", "\"ambientProbe\":" + AmbientProbe());
        // The gradient mode over the probe given, another one given in it, and the custom mode.
        RenderSettings.ambientMode = AmbientMode.Trilight;
        Record("end-gradient", "\"ambientProbe\":" + AmbientProbe());
        RenderSettings.ambientProbe = other;
        Record("end-gradient-set", "\"ambientProbe\":" + AmbientProbe());
        RenderSettings.ambientMode = AmbientMode.Custom;
        Record("end-gradient-custom", "\"ambientProbe\":" + AmbientProbe());

        // Scenes without lighting data (their files, in Assets/NoData, written by hand, each in
        // the skybox ambient mode). With this release's RenderSettings: one whose
        // LightmapSettings an earlier release saved with lighting generated automatically, one
        // with that off, one in this release's format, all with the default skybox, and the
        // first again with a sky material of its own. With an earlier release's RenderSettings:
        // the default scene (default skybox, the default directional light), the same with
        // lighting generated automatically, with the light turned, without a light, and with a
        // sky material of its own. What each reflects, its ambient probe as loaded and in the
        // custom mode.
        foreach (var name in NoData)
        {
            yield return Load(name);
            RecordEnvironment(name.ToLowerInvariant());
            RenderSettings.ambientMode = AmbientMode.Custom;
            Record(name.ToLowerInvariant() + "-custom", "\"ambientProbe\":" + AmbientProbe());
        }

        // A scene loaded beside another: the earlier release's default scene with a scene
        // without lighting data added, and the other way round; then the added scene made the
        // active one.
        foreach (var (first, added) in new[] { ("LegacyDefault", "NoDataCurrent"), ("NoDataCurrent", "LegacyDefault") })
        {
            yield return Load(first);
            SceneManager.LoadScene(added, LoadSceneMode.Additive);
            yield return null;
            yield return null;
            var label = first.ToLowerInvariant() + "-plus-" + added.ToLowerInvariant();
            RecordEnvironment(label);
            SceneManager.SetActiveScene(SceneManager.GetSceneByName(added));
            RecordEnvironment(label + "-active");
            yield return null;
            RecordEnvironment(label + "-active-next-frame");
        }

        // The room with baked global illumination on as well and its environment lighting
        // baked: as it loads, with a brighter ambient color, with the environment as its only
        // light (the point light off, the panel's emission black), and with
        // DynamicGI.SetEnvironmentData.
        yield return Load("RoomBakedEnvironment");
        s = Scene();
        yield return Converge();
        Capture("roombakedenvironment-base", s);
        // Its probes' baked coefficients, read while realtime GI lights the probes too. Then
        // baked coefficients given by the script (every probe's first coefficient of each color
        // raised by a half): the interpolated probes in that frame, with nothing else changed,
        // and once more after the usual wait; then the scene's own coefficients given back.
        RecordBakedProbes("roombakedenvironment-lightprobes");
        var lightProbes = LightmapSettings.lightProbes;
        var own = lightProbes.bakedProbes;
        var raised = lightProbes.bakedProbes;
        for (int i = 0; i < raised.Length; i++)
            for (int c = 0; c < 3; c++) raised[i][c, 0] += 0.5f;
        lightProbes.bakedProbes = raised;
        Record("roombakedenvironment-bakedset-now", "\"interpolated\":" + Interpolated(s));
        yield return Converge();
        Capture("roombakedenvironment-bakedset", s);
        RecordBakedProbes("roombakedenvironment-bakedset-lightprobes");
        lightProbes.bakedProbes = own;
        Record("roombakedenvironment-bakedrestored-now", "\"interpolated\":" + Interpolated(s));

        RenderSettings.ambientLight = new Color(0.6f, 0.5f, 0.2f);
        yield return Converge();
        Capture("roombakedenvironment-color", s);
        s.point.enabled = false;
        DynamicGI.SetEmissive(s.panel, Color.black);
        yield return Converge();
        Capture("roombakedenvironment-alone", s);
        var cube = EnvironmentData();
        DynamicGI.SetEnvironmentData(cube);
        yield return Converge();
        Capture("roombakedenvironment-envdata", s);
        // Over the data: the ambient color changed; the ambient source changed (a gradient);
        // a skybox as the source; its ambient intensity changed (each after the data was set
        // again); and DynamicGI.UpdateEnvironment.
        RenderSettings.ambientLight = new Color(0.2f, 0.6f, 0.5f);
        yield return Converge();
        Capture("roombakedenvironment-envdata-color", s);
        DynamicGI.SetEnvironmentData(cube);
        yield return Converge();
        RenderSettings.ambientMode = AmbientMode.Trilight;
        yield return Converge();
        Capture("roombakedenvironment-envdata-source", s);
        DynamicGI.SetEnvironmentData(cube);
        yield return Converge();
        RenderSettings.skybox = new Material(Shader.Find("Skybox/Procedural"));
        RenderSettings.ambientMode = AmbientMode.Skybox;
        yield return Converge();
        Capture("roombakedenvironment-envdata-sky", s);
        DynamicGI.SetEnvironmentData(cube);
        yield return Converge();
        RenderSettings.ambientIntensity = 2f;
        yield return Converge();
        Capture("roombakedenvironment-envdata-intensity", s);
        DynamicGI.SetEnvironmentData(cube);
        yield return Converge();
        DynamicGI.UpdateEnvironment();
        yield return Converge();
        Capture("roombakedenvironment-envdata-update", s);
        Finish();
    }

    static readonly string[] NoData =
    {
        "NoDataAuto", "NoDataOnDemand", "NoDataCurrent", "NoDataAutoOwnSky",
        "LegacyDefault", "LegacyAuto", "LegacyTurned", "LegacyNoLight", "LegacyOwnSky",
    };

    /// <summary>What the scene reflects by default and its ambient probe.</summary>
    void RecordEnvironment(string label)
    {
        Record(label + "-reflection", DefaultReflection());
        Record(label + "-ambient", "\"ambientProbe\":" + AmbientProbe());
    }

    /// <summary>ReflectionProbe.defaultTexture (its name, dimension and size, or "null") and its
    /// decode values.</summary>
    static string DefaultReflection()
    {
        var texture = ReflectionProbe.defaultTexture;
        return "\"defaultTexture\":\"" + (texture == null ? "null" : texture.name) + "\",\"dimension\":" + (texture == null ? -1 : (int)texture.dimension)
            + ",\"reflectionSize\":" + (texture == null ? 0 : texture.width) + ",\"decode\":" + V(ReflectionProbe.defaultTextureHDRDecodeValues);
    }

    /// <summary>Under a scriptable render pipeline (run.py's `--pipeline`): the Room's lights as
    /// the pipeline gives them to realtime GI, then the environment alone. Lightmaps and values
    /// only: the room's materials are the built-in pipeline's.</summary>
    IEnumerator Pipeline()
    {
        yield return Load("Room");
        var s = Scene();
        yield return Converge();
        Capture("pipeline-base", s, false);
        s.point.transform.position = new Vector3(1.5f, 2.5f, -1.5f);
        yield return Converge();
        Capture("pipeline-moved", s, false);
        s.point.intensity = 4f;
        yield return Converge();
        Capture("pipeline-intensity", s, false);
        // A range the room's far walls are beyond.
        s.point.range = 4f;
        yield return Converge();
        Capture("pipeline-range", s, false);
        s.point.range = 10f;
        s.point.intensity = 2f;
        s.point.bounceIntensity = 3f;
        yield return Converge();
        Capture("pipeline-bounce", s, false);
        s.point.bounceIntensity = 1f;

        // The spot light, then with a narrow inner angle.
        s.point.enabled = false;
        s.spot.enabled = true;
        yield return Converge();
        Capture("pipeline-spot", s, false);
        s.spot.innerSpotAngle = 5f;
        yield return Converge();
        Capture("pipeline-spot-inner", s, false);
        s.spot.enabled = false;

        var sun = Sun(LightShadows.Hard, 40f, 25f);
        yield return Converge();
        Capture("pipeline-directional", s, false);
        Destroy(sun.gameObject);

        // The environment alone: an ambient color, then a procedural skybox, then its ambient
        // intensity 2.
        DynamicGI.SetEmissive(s.panel, Color.black);
        RenderSettings.ambientLight = new Color(0.6f, 0.5f, 0.2f);
        yield return Converge();
        Capture("pipeline-color", s, false);
        var sky = new Material(Shader.Find("Skybox/Procedural"));
        sky.SetFloat("_Exposure", 1f);
        RenderSettings.skybox = sky;
        RenderSettings.ambientMode = AmbientMode.Skybox;
        DynamicGI.UpdateEnvironment();
        yield return Converge();
        Capture("pipeline-sky", s, false);
        RenderSettings.ambientIntensity = 2f;
        yield return Converge();
        Capture("pipeline-sky-intensity", s, false);
        Finish();
    }

    /// <summary>An environment for DynamicGI.SetEnvironmentData: six faces of 8 x 8 RGBA texels.</summary>
    static float[] EnvironmentData()
    {
        const int Size = 8;
        var data = new float[Size * Size * 6 * 4];
        for (int face = 0; face < 6; face++)
            for (int y = 0; y < Size; y++)
                for (int x = 0; x < Size; x++)
                {
                    int i = ((face * Size + y) * Size + x) * 4;
                    data[i] = (0.05f + 0.1f * face) * (x + 1) / 4f;
                    data[i + 1] = (0.6f - 0.1f * face) * (y + 1) / 4f;
                    data[i + 2] = 0.2f * (1 + face % 2);
                    data[i + 3] = 1f;
                }
        return data;
    }

    /// <summary>What DynamicGI.SetEnvironmentData throws for <paramref name="input"/>, as a JSON
    /// string: the exception's type, with its message or (the runtime words the message of a
    /// missing argument itself) the argument it names; empty when it throws nothing.</summary>
    static string Thrown(float[] input, bool message)
    {
        string thrown = "";
        try
        {
            DynamicGI.SetEnvironmentData(input);
        }
        catch (ArgumentException e)
        {
            thrown = e.GetType().Name + ": " + (message ? e.Message : e.ParamName);
        }
        catch (Exception e)
        {
            thrown = e.GetType().Name;
        }
        return "\"" + thrown.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", " ") + "\"";
    }

    static Light Sun(LightShadows shadows, float x, float y)
    {
        var sun = new GameObject("Sun").AddComponent<Light>();
        sun.type = LightType.Directional;
#if UNITY_EDITOR
        sun.lightmapBakeType = LightmapBakeType.Realtime;
#endif
        sun.shadows = shadows;
        sun.color = new Color(1f, 0.95f, 0.8f);
        sun.intensity = 1.2f;
        sun.transform.rotation = Quaternion.Euler(x, y, 0f);
        return sun;
    }

    IEnumerator Load(string name)
    {
        SceneManager.LoadScene(name);
        yield return null;
        yield return null;
    }

    static IEnumerator Converge()
    {
        for (int i = 0; i < BeforeConverged; i++) yield return null;
        for (int i = 0; i < MaxFrames && !DynamicGI.isConverged; i++) yield return null;
        for (int i = 0; i < AfterConverged; i++) yield return null;
    }

    static RealtimeGIScene Scene() => FindAnyObjectByType<RealtimeGIScene>();

    /// <summary>The scene's light probes as baked: count and every baked coefficient (realtime GI
    /// adds its own lighting at runtime; GetInterpolatedProbe in <see cref="Capture"/> reads both).</summary>
    void RecordBakedProbes(string name)
    {
        var probes = LightmapSettings.lightProbes;
        var body = new StringBuilder("\"count\":").Append(probes == null ? -1 : probes.count).Append(",\"baked\":[");
        if (probes != null)
        {
            var baked = probes.bakedProbes;
            for (int i = 0; i < baked.Length; i++)
                for (int c = 0; c < 3; c++)
                    for (int k = 0; k < 9; k++)
                        body.Append(i + c + k > 0 ? "," : "").Append(F(baked[i][c, k]));
        }
        Record(name, body.Append(']').ToString());
    }

    void Record(string kind, string body) =>
        m_Out.Append("{\"case\":\"").Append(kind).Append("\",").Append(body).Append("}\n");

    /// <summary>LightProbes.GetInterpolatedProbe at the scene's sample points: one array of 27
    /// coefficients each.</summary>
    static string Interpolated(RealtimeGIScene s)
    {
        var body = new StringBuilder("[");
        for (int i = 0; i < s.samples.Length; i++)
        {
            LightProbes.GetInterpolatedProbe(s.samples[i], null, out var sh);
            body.Append(i > 0 ? ",[" : "[");
            for (int c = 0; c < 3; c++)
                for (int k = 0; k < 9; k++)
                    body.Append(c + k > 0 ? "," : "").Append(F(sh[c, k]));
            body.Append(']');
        }
        return body.Append(']').ToString();
    }

    static string AmbientProbe()
    {
        var body = new StringBuilder("[");
        var ambient = RenderSettings.ambientProbe;
        for (int c = 0; c < 3; c++)
            for (int k = 0; k < 9; k++)
                body.Append(c + k > 0 ? "," : "").Append(F(ambient[c, k]));
        return body.Append(']').ToString();
    }

    void Capture(string name, RealtimeGIScene s, bool frame = true)
    {
        var body = new StringBuilder();
        body.Append("\"converged\":").Append(B(DynamicGI.isConverged));
        body.Append(",\"lightmapsMode\":").Append((int)LightmapSettings.lightmapsMode);
        body.Append(",\"renderers\":[");
        for (int i = 0; i < s.lit.Length; i++)
        {
            var r = s.lit[i];
            if (i > 0) body.Append(',');
            body.Append("{\"name\":\"").Append(r.name).Append("\",\"index\":").Append(r.realtimeLightmapIndex)
                .Append(",\"st\":").Append(V(r.realtimeLightmapScaleOffset)).Append('}');
        }
        body.Append("],\"probes\":[");
        for (int i = 0; i < s.samples.Length; i++)
        {
            LightProbes.GetInterpolatedProbe(s.samples[i], null, out var sh);
            if (i > 0) body.Append(',');
            body.Append('[');
            for (int c = 0; c < 3; c++)
                for (int k = 0; k < 9; k++)
                    body.Append(c + k > 0 ? "," : "").Append(F(sh[c, k]));
            body.Append(']');
        }
        body.Append("],\"ambientProbe\":").Append(AmbientProbe());
        Record(name, body.ToString());

        // One file per realtime lightmap atlas the renderers use: <case>.atlas for the first,
        // <case>-a<index>.atlas for the others.
        int atlases = 0;
        foreach (var r in s.lit) atlases = Mathf.Max(atlases, r.realtimeLightmapIndex + 1);
        int size = atlases > 0 ? AtlasSize(s, 0) : 0;
        if (size > 0)
        {
            for (int atlas = 0; atlas < atlases; atlas++)
            {
                var file = name + (atlas > 0 ? "-a" + atlas : "");
                int atlasSize = atlas > 0 ? AtlasSize(s, atlas) : size;
                DumpAtlas(file + ".atlas", s, atlasSize, 0, atlas);
                if (LightmapSettings.lightmapsMode == LightmapsMode.CombinedDirectional)
                    DumpAtlas(file + ".dir", s, atlasSize, 1, atlas);
            }
        }
        else Record(name + "-noatlas", "\"size\":" + size);
        if (frame) CaptureFrame(name + ".png");
    }

    /// <summary>The width of realtime lightmap atlas <paramref name="atlas"/>, read by the dump
    /// shader from its renderers; 0 when no renderer has one.</summary>
    int AtlasSize(RealtimeGIScene s, int atlas)
    {
        const int Probe = 16;
        var texture = RenderDump(s, Probe, 2, TextureFormat.RGBAFloat, RenderTextureFormat.ARGBFloat, atlas);
        var pixels = texture.GetPixels();
        Destroy(texture);
        float size = 0f;
        foreach (var p in pixels)
            if (p.a > 0f) size = Mathf.Max(size, p.r);
        return (int)size;
    }

    void DumpAtlas(string file, RealtimeGIScene s, int size, int channel, int atlas)
    {
        var texture = RenderDump(s, size, channel, TextureFormat.RGBAHalf, RenderTextureFormat.ARGBHalf, atlas);
        var raw = texture.GetRawTextureData();
        using (var stream = new BinaryWriter(File.Create(Path.Combine(m_Dir, file))))
        {
            stream.Write(size);
            stream.Write(size);
            stream.Write(raw);
        }
        Destroy(texture);
    }

    /// <summary>Draws every lit renderer of atlas <paramref name="atlas"/> with the dump shader into a size*size target through a
    /// camera that sees the whole room, and reads it back.</summary>
    Texture2D RenderDump(RealtimeGIScene s, int size, int channel, TextureFormat format, RenderTextureFormat targetFormat, int atlas)
    {
        var target = new RenderTexture(size, size, 0, targetFormat);
        target.filterMode = FilterMode.Point;
        var go = new GameObject("Dump Camera", typeof(Camera));
        var camera = go.GetComponent<Camera>();
        camera.enabled = false;
        camera.orthographic = true;
        camera.orthographicSize = 20f;
        camera.transform.position = new Vector3(0f, 2f, -30f);
        camera.nearClipPlane = 0.1f;
        camera.farClipPlane = 100f;
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0f, 0f, 0f, 0f);
        camera.targetTexture = target;
        camera.allowHDR = true;
        camera.allowMSAA = false;
        m_Dump.SetFloat("_Channel", channel);
        var saved = new Material[s.lit.Length];
        var others = FindObjectsByType<Renderer>(FindObjectsSortMode.None);
        var visible = new bool[others.Length];
        for (int i = 0; i < others.Length; i++)
        {
            visible[i] = others[i].enabled;
            if (Array.IndexOf(s.lit, others[i]) < 0 || others[i].realtimeLightmapIndex != atlas) others[i].enabled = false;
        }
        for (int i = 0; i < s.lit.Length; i++)
        {
            saved[i] = s.lit[i].sharedMaterial;
            s.lit[i].sharedMaterial = m_Dump;
        }
        camera.Render();
        for (int i = 0; i < s.lit.Length; i++) s.lit[i].sharedMaterial = saved[i];
        for (int i = 0; i < others.Length; i++) others[i].enabled = visible[i];

        RenderTexture.active = target;
        var texture = new Texture2D(size, size, format, false, true);
        texture.ReadPixels(new Rect(0, 0, size, size), 0, 0);
        texture.Apply();
        RenderTexture.active = null;
        camera.targetTexture = null;
        Destroy(go);
        target.Release();
        return texture;
    }

    void CaptureFrame(string file)
    {
        var camera = Camera.main;
        var target = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = target;
        camera.Render();
        RenderTexture.active = target;
        var texture = new Texture2D(Width, Height, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, Width, Height), 0, 0);
        texture.Apply();
        RenderTexture.active = null;
        camera.targetTexture = null;
        File.WriteAllBytes(Path.Combine(m_Dir, file), texture.EncodeToPNG());
        Destroy(texture);
        target.Release();
    }

    void Finish()
    {
        File.WriteAllText(Path.Combine(m_Dir, "values.jsonl"), m_Out.ToString() + "{\"case\":\"end\"}\n");
#if UNITY_EDITOR
        EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
