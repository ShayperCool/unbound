using System;
using System.Collections;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

/// <summary>Renders the probe scene into a render texture through URP's upscaling filters while the
/// camera stays still or moves a fixed step every frame, and writes each scenario's last frame as a
/// PNG (and values.jsonl) into STP_MOTION_OUT, then exits. See README.md.</summary>
public class StpMotionProbe : MonoBehaviour
{
    const int Width = 320, Height = 180;
    /// <summary>Frames rendered in each scenario after its first, before the recorded one.</summary>
    const int Frames = 24;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("StpMotionProbe").AddComponent<StpMotionProbe>();

    readonly StringBuilder m_Out = new StringBuilder();
    string m_Dir;

    void Value(string key, string json) => m_Out.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(json).Append("}\n");

    IEnumerator Start()
    {
        m_Dir = Environment.GetEnvironmentVariable("STP_MOTION_OUT");
        // Every frame lasts 1/30 s, so STP's motion scaling (this frame's delta time over the last's) is 1.
        Time.captureFramerate = 30;
        var asset = (UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline;
        var camera = Camera.main;
        var data = camera.GetUniversalAdditionalCameraData();
        data.renderPostProcessing = true;
        data.antialiasing = AntialiasingMode.None;
        var small = new RenderTexture(Width, Height, 24, RenderTextureFormat.ARGB32);
        var large = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
        camera.targetTexture = small;
        var (homePosition, homeRotation) = (camera.transform.position, camera.transform.rotation);
        // UniversalRenderPipelineAsset exposes its renderers' data only internally.
        var rendererData = (UniversalRendererData)((ScriptableRendererData[])typeof(UniversalRenderPipelineAsset).GetField("m_RendererDataList",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(asset))[0];
        var renderers = FindObjectsByType<MeshRenderer>(FindObjectsSortMode.InstanceID);

        Action none = () => { };
        Action far = () => { camera.nearClipPlane = 1f; camera.farClipPlane = 1500f; };
        Action near = () => { camera.nearClipPlane = 0.3f; camera.farClipPlane = 100f; };
        Action prime = () => { rendererData.depthPrimingMode = DepthPrimingMode.Forced; rendererData.renderingMode = RenderingMode.ForwardPlus; };
        Action noPrime = () => { rendererData.depthPrimingMode = DepthPrimingMode.Disabled; rendererData.renderingMode = RenderingMode.Forward; };
        Texture2D noise = null;
        var checker = new Texture[renderers.Length];
        Action noisy = () =>
        {
            noise = NoiseTexture();
            for (int i = 0; i < renderers.Length; i++)
            {
                checker[i] = renderers[i].material.GetTexture("_BaseMap");
                renderers[i].material.SetTexture("_BaseMap", noise);
            }
        };
        Action plain = () =>
        {
            for (int i = 0; i < renderers.Length; i++) renderers[i].material.SetTexture("_BaseMap", checker[i]);
            Destroy(noise);
        };
        Action toLarge = () => camera.targetTexture = large;
        Action toSmall = () => camera.targetTexture = small;
        // The scene and the camera far from the world's origin.
        var offset = new Vector3(150f, 60f, 120f);
        Action away = () => { foreach (var r in renderers) r.transform.position += offset; homePosition += offset; camera.transform.position = homePosition; };
        Action back = () => { foreach (var r in renderers) r.transform.position -= offset; homePosition -= offset; };
        // URP's Rendering Debugger STP views (Rendering > Map Overlays > STP); the view index is internal.
        var rendering = UniversalRenderPipelineDebugDisplaySettings.Instance.renderingSettings;
        var viewIndex = rendering.GetType().GetProperty("stpDebugViewIndex",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        // The Rendering Debugger's full-screen views are composed by URP's final post-processing
        // pass (a renderer with post-processing data) into the screen: these scenarios render to
        // the 320x180 game view through the probe's second renderer.
        Action toScreen = () => { camera.targetTexture = null; data.SetRenderer(1); };
        Action fromScreen = () => { data.SetRenderer(0); camera.targetTexture = small; };
        Func<int, Action> debugView = view => () =>
        {
            toScreen();
            rendering.fullScreenDebugMode = DebugFullScreenMode.STP;
            rendering.fullScreenDebugModeOutputSizeScreenPercent = 100;
            viewIndex.SetValue(rendering, view);
        };
        Action noDebugView = () => { rendering.fullScreenDebugMode = DebugFullScreenMode.None; viewIndex.SetValue(rendering, 0); fromScreen(); };

        var pan = new Vector3(0.023f, 0f, 0f);
        var drift = new Vector3(0.023f, 0.004f, 0.011f);
        var forward = new Vector3(0.01f, 0f, 0.006f);
        var scenarios = new (string name, float scale, UpscalingFilterSelection filter, Vector3 step, float yaw, Action setup, Action teardown)[]
        {
            ("stp_static", 0.5f, UpscalingFilterSelection.STP, Vector3.zero, 0f, none, none),
            ("stp_pan", 0.5f, UpscalingFilterSelection.STP, pan, 0f, none, none),
            ("stp_turn", 0.5f, UpscalingFilterSelection.STP, Vector3.zero, 0.37f, none, none),
            ("stp_pan_08", 0.8f, UpscalingFilterSelection.STP, drift, 0.11f, none, none),
            ("linear_pan", 0.5f, UpscalingFilterSelection.Linear, pan, 0f, none, none),
            ("stp_pan_far", 0.5f, UpscalingFilterSelection.STP, pan, 0f, far, near),
            ("stp_pan_prime", 0.5f, UpscalingFilterSelection.STP, pan, 0f, prime, noPrime),
            ("stp_pan_noise", 0.8f, UpscalingFilterSelection.STP, drift, 0.11f, noisy, plain),
            ("stp_static_noise", 0.8f, UpscalingFilterSelection.STP, Vector3.zero, 0f, noisy, plain),
            ("stp_turn_fast", 0.8f, UpscalingFilterSelection.STP, forward, 1.6f, none, none),
            ("stp_turn_fast_noise", 0.8f, UpscalingFilterSelection.STP, forward, 1.6f, noisy, plain),
            ("stp_pan_fast", 0.8f, UpscalingFilterSelection.STP, new Vector3(0.11f, 0f, 0f), 0f, none, none),
            ("stp_pan_offset", 0.8f, UpscalingFilterSelection.STP, drift, 0.11f, away, back),
            ("stp_static_offset", 0.8f, UpscalingFilterSelection.STP, Vector3.zero, 0f, away, back),
            ("stp_turn_fast_720", 0.8f, UpscalingFilterSelection.STP, forward, 0.5f, toLarge, toSmall),
            ("stp_turn_fast_noise_720", 0.8f, UpscalingFilterSelection.STP, forward, 0.5f, () => { toLarge(); noisy(); }, () => { plain(); toSmall(); }),
            ("stp_screen", 0.5f, UpscalingFilterSelection.STP, drift, 0.11f, toScreen, fromScreen),
            ("stp_debug_input", 0.5f, UpscalingFilterSelection.STP, drift, 0.11f, debugView(0), noDebugView),
            ("stp_debug_depth", 0.5f, UpscalingFilterSelection.STP, drift, 0.11f, debugView(1), noDebugView),
            ("stp_debug_tonemapped", 0.5f, UpscalingFilterSelection.STP, drift, 0.11f, debugView(2), noDebugView),
            ("stp_debug_motion", 0.5f, UpscalingFilterSelection.STP, drift, 0.11f, debugView(3), noDebugView),
            ("stp_debug_reprojection", 0.5f, UpscalingFilterSelection.STP, drift, 0.11f, debugView(4), noDebugView),
            ("stp_debug_sensitivity", 0.5f, UpscalingFilterSelection.STP, drift, 0.11f, debugView(5), noDebugView),
            ("stp_debug_sensitivity_static", 0.5f, UpscalingFilterSelection.STP, Vector3.zero, 0f, debugView(5), noDebugView),
            // Controls that separate the two: the second renderer into the render texture, and the
            // first renderer to the screen.
            ("stp_post_texture", 0.5f, UpscalingFilterSelection.STP, drift, 0.11f, () => data.SetRenderer(1), () => data.SetRenderer(0)),
            ("stp_screen_plain", 0.5f, UpscalingFilterSelection.STP, drift, 0.11f, () => camera.targetTexture = null, toSmall),
        };
        foreach (var s in scenarios)
        {
            asset.renderScale = s.scale;
            asset.upscalingFilter = s.filter;
            camera.transform.SetPositionAndRotation(homePosition, homeRotation);
            s.setup();
            data.resetHistory = true;
            yield return null;
            bool onScreen = camera.targetTexture == null;
            for (int i = 0; i < Frames; i++)
            {
                camera.transform.position += s.step;
                camera.transform.rotation = Quaternion.Euler(0f, s.yaw, 0f) * camera.transform.rotation;
                // The screen is read at the end of the frame that rendered it.
                if (onScreen && i == Frames - 1) yield return new WaitForEndOfFrame();
                else yield return null;
            }
            if (onScreen)
            {
                Value(s.name + ".frame", Time.frameCount.ToString());
                Record(null, s.name);
            }
            else
            {
                // The last frame rendered; it is read in the next one.
                Value(s.name + ".frame", (Time.frameCount - 1).ToString());
                Record(camera.targetTexture, s.name);
            }
            s.teardown();
        }
        camera.targetTexture = null;
        small.Release();
        large.Release();
        Value("complete", "true");
        int code = 0;
        if (string.IsNullOrEmpty(m_Dir)) { Debug.LogError("STP_MOTION_OUT is not set"); code = 1; }
        else File.WriteAllText(Path.Combine(m_Dir, "values.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }

    /// <summary>A 256x256 texture of value noise over three octaves plus per-texel grain, from an
    /// integer hash (the same texels in every runtime).</summary>
    static Texture2D NoiseTexture()
    {
        var texture = new Texture2D(256, 256, TextureFormat.RGBA32, true);
        var texels = new Color32[256 * 256];
        for (int y = 0; y < 256; y++)
            for (int x = 0; x < 256; x++)
            {
                float v = 0f;
                for (int o = 0; o < 3; o++) v += Hash01(x >> (2 * o + 1), y >> (2 * o + 1), o) * (0.5f / (1 << o));
                float grain = Hash01(x, y, 7);
                texels[y * 256 + x] = new Color(v * 0.8f + grain * 0.4f, v * 0.7f + Hash01(x, y, 8) * 0.3f, v * 0.5f + grain * 0.2f, 1f);
            }
        texture.SetPixels32(texels);
        texture.Apply(true);
        return texture;
    }

    /// <summary>A value in [0, 1) from integer coordinates (a 32-bit integer hash).</summary>
    static float Hash01(int x, int y, int seed)
    {
        uint h = (uint)x * 374761393u + (uint)y * 668265263u + (uint)seed * 2246822519u;
        h = (h ^ (h >> 13)) * 1274126177u;
        h ^= h >> 16;
        return (h & 0xFFFFFF) / 16777216f;
    }

    /// <summary>Writes the target's pixels (the screen's when null, read at the end of a frame).</summary>
    void Record(RenderTexture target, string name)
    {
        if (string.IsNullOrEmpty(m_Dir)) return;
        int width = target != null ? target.width : Screen.width, height = target != null ? target.height : Screen.height;
        var read = new Texture2D(width, height, TextureFormat.RGBA32, false);
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        read.ReadPixels(new Rect(0, 0, width, height), 0, 0);
        read.Apply();
        RenderTexture.active = previous;
        Directory.CreateDirectory(m_Dir);
        File.WriteAllBytes(Path.Combine(m_Dir, name + ".png"), read.EncodeToPNG());
        Destroy(read);
    }
}
