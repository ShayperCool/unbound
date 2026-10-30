using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

// Realtime reflection probes: ViaScripting with each time slicing mode (when IsFinishedRendering
// turns true, frame by frame), OnAwake, EveryFrame following a moving object, realtime probes off in
// the quality settings, BlendCubemap, blendDistance between two custom probes.
public partial class FxProbe
{
    /// <summary>A mirror sphere at (0, 1, 0) amid the cube stage.</summary>
    Camera MirrorScene()
    {
        CubeStage();
        Primitive(PrimitiveType.Sphere, new Vector3(0f, 1f, 0f), Standard(Color.white, 1f, 1f), 1.4f);
        var camera = MakeCamera();
        camera.transform.position = new Vector3(-1.6f, 1.5f, -1.6f);
        camera.transform.LookAt(new Vector3(0f, 1f, 0f));
        return camera;
    }

    ReflectionProbe RealtimeProbe(ReflectionProbeRefreshMode refresh, ReflectionProbeTimeSlicingMode slicing, bool active = true)
    {
        var go = Add(new GameObject("probe"));
        go.SetActive(active);
        go.transform.position = new Vector3(0f, 1f, 0f);
        var probe = go.AddComponent<ReflectionProbe>();
        probe.mode = ReflectionProbeMode.Realtime;
        probe.refreshMode = refresh;
        probe.timeSlicingMode = slicing;
        probe.size = new Vector3(4f, 4f, 4f);
        probe.resolution = 64;
        probe.clearFlags = ReflectionProbeClearFlags.SolidColor;
        probe.backgroundColor = new Color(0.2f, 0.2f, 0.2f, 1f);
        probe.nearClipPlane = 0.75f;
        return probe;
    }

    void ProbeValues(string prefix, ReflectionProbe probe)
    {
        Value(prefix + ".texture", probe.texture == null ? "null" : S(probe.texture.GetType().Name + " " + probe.texture.width));
        Value(prefix + ".realtimeTexture", probe.realtimeTexture == null ? "null" : S(probe.realtimeTexture.GetType().Name + " " + probe.realtimeTexture.width + " " + probe.realtimeTexture.format));
    }

    IEnumerator ReflectionProbes()
    {
        var camera = MirrorScene();
        RenderSettings.reflectionIntensity = 1f;
        Capture(camera, "probe_none");
        var defaults = Add(new GameObject("defaults")).AddComponent<ReflectionProbe>();
        Value("probe.defaults.mode", S(defaults.mode.ToString()));
        Value("probe.defaults.refreshMode", S(defaults.refreshMode.ToString()));
        Value("probe.defaults.timeSlicingMode", S(defaults.timeSlicingMode.ToString()));
        Value("probe.defaults.blendDistance", F(defaults.blendDistance));
        Value("probe.quality.realtimeReflectionProbes", B(QualitySettings.realtimeReflectionProbes));
        DestroyImmediate(defaults.gameObject);

        foreach (var slicing in new[] { ReflectionProbeTimeSlicingMode.NoTimeSlicing, ReflectionProbeTimeSlicingMode.AllFacesAtOnce, ReflectionProbeTimeSlicingMode.IndividualFaces })
        {
            var probe = RealtimeProbe(ReflectionProbeRefreshMode.ViaScripting, slicing);
            string prefix = "probe.scripted_" + slicing;
            ProbeValues(prefix + ".before", probe);
            int id = probe.RenderProbe();
            var finished = new StringBuilder("[");
            finished.Append(B(probe.IsFinishedRendering(id)));
            for (int frame = 0; frame < 12; frame++)
            {
                yield return null;
                finished.Append(",").Append(B(probe.IsFinishedRendering(id)));
            }
            Value(prefix + ".finished", finished.Append("]").ToString());
            Value(prefix + ".idPositive", B(id > 0));
            ProbeValues(prefix + ".after", probe);
            Capture(camera, "probe_scripted_" + slicing);
            DestroyImmediate(probe.gameObject);
        }

        // Into a texture of one's own.
        var own = RealtimeProbe(ReflectionProbeRefreshMode.ViaScripting, ReflectionProbeTimeSlicingMode.NoTimeSlicing);
        var target = Add(new RenderTexture(32, 32, 16) { dimension = TextureDimension.Cube });
        int ownId = own.RenderProbe(target);
        yield return null;
        yield return null;
        Value("probe.own_target.finished", B(own.IsFinishedRendering(ownId)));
        ProbeValues("probe.own_target", own);
        SaveFaces(target, "probe_own_target");
        DestroyImmediate(own.gameObject);

        // OnAwake: rendered once when it starts.
        var awake = RealtimeProbe(ReflectionProbeRefreshMode.OnAwake, ReflectionProbeTimeSlicingMode.NoTimeSlicing, false);
        awake.gameObject.SetActive(true);
        yield return null;
        yield return null;
        ProbeValues("probe.awake", awake);
        Capture(camera, "probe_awake");
        DestroyImmediate(awake.gameObject);

        // EveryFrame follows the scene: the red cube moves above the sphere.
        var every = RealtimeProbe(ReflectionProbeRefreshMode.EveryFrame, ReflectionProbeTimeSlicingMode.NoTimeSlicing);
        yield return null;
        yield return null;
        Capture(camera, "probe_every_frame");
        foreach (var r in FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
            if (r.sharedMaterial != null && r.sharedMaterial.color == Color.red) r.transform.position = new Vector3(-1.5f, 2.5f, -1.5f);
        yield return null;
        yield return null;
        Capture(camera, "probe_every_frame_moved");
        DestroyImmediate(every.gameObject);

        // Realtime probes off in the quality settings.
        bool realtime = QualitySettings.realtimeReflectionProbes;
        QualitySettings.realtimeReflectionProbes = false;
        var off = RealtimeProbe(ReflectionProbeRefreshMode.ViaScripting, ReflectionProbeTimeSlicingMode.NoTimeSlicing);
        int offId = off.RenderProbe();
        yield return null;
        yield return null;
        Value("probe.quality_off.finished", B(off.IsFinishedRendering(offId)));
        ProbeValues("probe.quality_off", off);
        Capture(camera, "probe_quality_off");
        QualitySettings.realtimeReflectionProbes = realtime;
        DestroyImmediate(off.gameObject);
        Clear();

        // BlendCubemap of a red and a blue cubemap.
        var red = Add(new Cubemap(16, TextureFormat.RGBA32, false));
        var blue = Add(new Cubemap(16, TextureFormat.RGBA32, false));
        for (int f = 0; f < 6; f++)
        {
            var reds = new Color[256];
            var blues = new Color[256];
            for (int i = 0; i < 256; i++)
            {
                reds[i] = new Color(1f, 0.2f * f / 5f, 0f, 1f);
                blues[i] = new Color(0f, 0.2f, 1f, 0.5f);
            }
            red.SetPixels(reds, (CubemapFace)f);
            blue.SetPixels(blues, (CubemapFace)f);
        }
        red.Apply();
        blue.Apply();
        var blended = ClearedCube(Color.black);
        Value("probe.blend.result", B(ReflectionProbe.BlendCubemap(red, blue, 0.25f, blended)));
        var face = Add(new RenderTexture(64, 64, 0, RenderTextureFormat.ARGB32));
        face.Create();
        var pixels = new StringBuilder("[");
        for (int f = 0; f < 6; f++)
        {
            Graphics.CopyTexture(blended, f, 0, face, 0, 0);
            pixels.Append(f == 0 ? "" : ",").Append(C(Pixel(face, 32, 32)));
        }
        Value("probe.blend.pixels", pixels.Append("]").ToString());

        // blendDistance: a red and a blue custom probe side by side over a row of mirror spheres.
        Directional(new Vector3(50f, -30f, 0f), Color.white, 1f);
        RenderSettings.reflectionIntensity = 1f;
        var redProbe = Add(new GameObject("red probe")).AddComponent<ReflectionProbe>();
        redProbe.mode = ReflectionProbeMode.Custom;
        redProbe.customBakedTexture = red;
        redProbe.transform.position = new Vector3(-2f, 1f, 0f);
        redProbe.size = new Vector3(4f, 4f, 4f);
        redProbe.blendDistance = 1.5f;
        var blueProbe = Add(new GameObject("blue probe")).AddComponent<ReflectionProbe>();
        blueProbe.mode = ReflectionProbeMode.Custom;
        blueProbe.customBakedTexture = blue;
        blueProbe.transform.position = new Vector3(2f, 1f, 0f);
        blueProbe.size = new Vector3(4f, 4f, 4f);
        blueProbe.blendDistance = 0.5f;
        for (int i = 0; i < 7; i++)
            Primitive(PrimitiveType.Sphere, new Vector3(-3f + i, 1f, 0f), Standard(Color.white, 1f, 1f), 0.8f);
        camera = MakeCamera();
        camera.transform.position = new Vector3(0f, 1.5f, -6f);
        camera.transform.LookAt(new Vector3(0f, 1f, 0f));
        Capture(camera, "probe_blend_distance_now");
        yield return null;
        Capture(camera, "probe_blend_distance");
        Clear();
    }
}
