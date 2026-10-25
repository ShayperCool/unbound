using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// Plays the project's clip (Assets/Video: a VP8 WebM of ffmpeg's testsrc2, 64x48,
/// 10 frames a second, 3 seconds) with a VideoPlayer into a RenderTexture and records: the clips'
/// properties, the player's once prepared, the pixels of frames it seeks to (frame by frame, so
/// the record does not depend on timing), stepping, playing to the end (events, state). Writes
/// <c>video.jsonl</c> and one PNG per sought frame into <c>VIDEO_PROBE_OUT</c>, then quits.
/// </summary>
public class VideoProbe : MonoBehaviour
{
    readonly StringBuilder m_Out = new StringBuilder();
    readonly List<string> m_Events = new List<string>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("VideoProbe").AddComponent<VideoProbe>();

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string D(double v) => v.ToString("R", CultureInfo.InvariantCulture);
    void Record(string kind, string body) => m_Out.Append("{\"case\":\"").Append(kind).Append("\",").Append(body).Append("}\n");

    IEnumerator Start()
    {
        var dir = System.Environment.GetEnvironmentVariable("VIDEO_PROBE_OUT") ?? "/tmp/video";
        Directory.CreateDirectory(dir);
#if UNITY_EDITOR
        // The Linux Editor imports VP8 WebM (H.264 MP4 does not import: "Error while reading movie").
        var clips = new[] { UnityEditor.AssetDatabase.LoadAssetAtPath<VideoClip>("Assets/Video/clip.webm") };
#else
        var clips = new VideoClip[0];
#endif
        foreach (var clip in clips)
        {
            Record("clip", $"\"name\":\"{clip.name}\",\"length\":{D(clip.length)},\"frameCount\":{clip.frameCount},\"frameRate\":{D(clip.frameRate)},\"width\":{clip.width},\"height\":{clip.height},\"audioTracks\":{clip.audioTrackCount},\"aspect\":[{clip.pixelAspectRatioNumerator},{clip.pixelAspectRatioDenominator}]");
            var target = new RenderTexture(64, 48, 0, RenderTextureFormat.ARGB32);
            var go = new GameObject("player " + clip.name);
            var player = go.AddComponent<VideoPlayer>();
            player.playOnAwake = false;
            player.audioOutputMode = VideoAudioOutputMode.None;
            player.renderMode = VideoRenderMode.RenderTexture;
            player.targetTexture = target;
            player.source = VideoSource.VideoClip;
            player.clip = clip;
            player.isLooping = false;
            player.sendFrameReadyEvents = true;
            m_Events.Clear();
            player.prepareCompleted += p => m_Events.Add("prepared");
            player.started += p => m_Events.Add("started");
            player.loopPointReached += p => m_Events.Add("loopPoint");
            player.seekCompleted += p => m_Events.Add("seek");
            long lastReady = -2;
            player.frameReady += (p, f) => lastReady = f;
            player.errorReceived += (p, e) => m_Events.Add("error:" + e);

            player.Prepare();
            for (int i = 0; i < 300 && !player.isPrepared; i++) yield return null;
            Record("prepared", $"\"clip\":\"{clip.name}\",\"isPrepared\":{(player.isPrepared ? "true" : "false")},\"frameCount\":{player.frameCount},\"length\":{D(player.length)},\"frameRate\":{F(player.frameRate)},\"width\":{player.width},\"height\":{player.height},\"canStep\":{(player.canStep ? "true" : "false")},\"canSetTime\":{(player.canSetTime ? "true" : "false")},\"frame\":{player.frame},\"time\":{D(player.time)},\"events\":[{string.Join(",", m_Events.ConvertAll(e => "\"" + e + "\""))}]");

            // Paused, the player seeks frame by frame (a prepared player that is neither playing nor
            // paused does not).
            player.Pause();
            for (int i = 0; i < 300 && lastReady == -2; i++) yield return null;
            Record("paused", $"\"clip\":\"{clip.name}\",\"ready\":{lastReady},\"frame\":{player.frame},\"isPaused\":{(player.isPaused ? "true" : "false")},\"isPlaying\":{(player.isPlaying ? "true" : "false")}");
            foreach (var n in new long[] { 0, 1, 5, 14, 29 })
            {
                lastReady = -2;
                player.frame = n;
                for (int i = 0; i < 300 && lastReady != n; i++) yield return null;
                yield return null;
                Record("frame", $"\"clip\":\"{clip.name}\",\"asked\":{n},\"ready\":{lastReady},\"frame\":{player.frame},\"time\":{D(player.time)},\"pixels\":{Samples(target)}");
                Save(target, Path.Combine(dir, $"{clip.name}_{n}.png"));
            }

            // One frame forward.
            lastReady = -2;
            player.StepForward();
            for (int i = 0; i < 300 && lastReady == -2; i++) yield return null;
            Record("step", $"\"clip\":\"{clip.name}\",\"ready\":{lastReady},\"frame\":{player.frame}");

            // From the start to the end.
            m_Events.Clear();
            player.frame = 0;
            player.Play();
            yield return null;
            bool playing = player.isPlaying;
            float started = Time.realtimeSinceStartup;
            while (Time.realtimeSinceStartup - started < 6f && !m_Events.Contains("loopPoint")) yield return null;
            yield return null;
            Record("played", $"\"clip\":\"{clip.name}\",\"wasPlaying\":{(playing ? "true" : "false")},\"isPlaying\":{(player.isPlaying ? "true" : "false")},\"frame\":{player.frame},\"events\":[{string.Join(",", m_Events.ConvertAll(e => "\"" + e + "\""))}]");
            player.Stop();
            Destroy(go);
            target.Release();

            // The camera plane modes: a camera (rendering into a 160x90 texture) with a red cube in
            // the middle, the video's frame 5 on its far or near plane.
            foreach (var c in s_CameraCases)
                yield return CameraCase(clip, dir, c.name, c.mode, c.alpha, c.aspect, c.sky);
        }

        // The light variables the sky (which the far plane cases show) and objects see with no
        // light in the scene.
        Record("sky", $"\"sky\":{Globals("Probe/SkyGlobals", true)},\"object\":{Globals("Probe/ObjectGlobals", false)}");
        File.WriteAllText(Path.Combine(dir, "video.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    static readonly (string name, VideoRenderMode mode, float alpha, VideoAspectRatio aspect, bool sky)[] s_CameraCases =
    {
        ("far_opaque", VideoRenderMode.CameraFarPlane, 1f, VideoAspectRatio.FitHorizontally, false),
        ("far_half", VideoRenderMode.CameraFarPlane, 0.5f, VideoAspectRatio.FitHorizontally, false),
        ("far_sky", VideoRenderMode.CameraFarPlane, 0.5f, VideoAspectRatio.FitHorizontally, true),
        ("far_inside", VideoRenderMode.CameraFarPlane, 1f, VideoAspectRatio.FitInside, false),
        ("near_half", VideoRenderMode.CameraNearPlane, 0.5f, VideoAspectRatio.FitHorizontally, false),
        ("near_inside", VideoRenderMode.CameraNearPlane, 1f, VideoAspectRatio.FitInside, false),
        ("near_stretch", VideoRenderMode.CameraNearPlane, 0.75f, VideoAspectRatio.Stretch, true),
    };

    IEnumerator CameraCase(VideoClip clip, string dir, string name, VideoRenderMode mode, float alpha, VideoAspectRatio aspect, bool sky)
    {
        var target = new RenderTexture(160, 90, 24, RenderTextureFormat.ARGB32);
        var cameraObject = new GameObject("camera " + name);
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.clearFlags = sky ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0f, 0.5f, 0f, 1f);
        camera.targetTexture = target;
        camera.transform.position = new Vector3(0f, 0f, -5f);
        var cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
        cube.GetComponent<Renderer>().sharedMaterial = new Material(Shader.Find("Unlit/Color")) { color = Color.red };
        cube.transform.rotation = Quaternion.Euler(20f, 30f, 0f);

        var go = new GameObject("player " + name);
        var player = go.AddComponent<VideoPlayer>();
        player.playOnAwake = false;
        player.audioOutputMode = VideoAudioOutputMode.None;
        player.renderMode = mode;
        player.targetCamera = camera;
        player.targetCameraAlpha = alpha;
        player.aspectRatio = aspect;
        player.clip = clip;
        long lastReady = -2;
        player.sendFrameReadyEvents = true;
        player.frameReady += (p, f) => lastReady = f;
        player.Prepare();
        for (int i = 0; i < 300 && !player.isPrepared; i++) yield return null;
        player.Pause();
        for (int i = 0; i < 300 && lastReady == -2; i++) yield return null;
        player.frame = 5;
        for (int i = 0; i < 300 && lastReady != 5; i++) yield return null;
        for (int i = 0; i < 3; i++) yield return null;
        camera.Render();
        Save(target, Path.Combine(dir, $"cam_{name}.png"));
        player.Stop();
        Destroy(go);
        Destroy(cube);
        Destroy(cameraObject);
        yield return null;
        target.Release();
    }

    /// <summary>The two vectors a dump shader writes, drawn as the sky or as a quad.</summary>
    static string Globals(string shader, bool sky)
    {
        var target = new RenderTexture(4, 1, 24, RenderTextureFormat.ARGBFloat);
        var cameraObject = new GameObject("globals camera");
        var camera = cameraObject.AddComponent<Camera>();
        camera.enabled = false;
        camera.targetTexture = target;
        camera.clearFlags = sky ? CameraClearFlags.Skybox : CameraClearFlags.SolidColor;
        var previousSky = RenderSettings.skybox;
        var material = new Material(Shader.Find(shader));
        GameObject quad = null;
        if (sky) RenderSettings.skybox = material;
        else
        {
            quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.transform.position = new Vector3(0f, 0f, 5f);
            quad.GetComponent<Renderer>().sharedMaterial = material;
        }
        camera.Render();
        RenderSettings.skybox = previousSky;
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var texture = new Texture2D(4, 1, TextureFormat.RGBAFloat, false);
        texture.ReadPixels(new Rect(0, 0, 4, 1), 0, 0);
        RenderTexture.active = previous;
        var parts = new List<string>();
        for (int x = 0; x < 2; x++)
        {
            var c = texture.GetPixel(x, 0);
            parts.Add($"[{F(c.r)},{F(c.g)},{F(c.b)},{F(c.a)}]");
        }
        Object.Destroy(texture);
        if (quad != null) Object.Destroy(quad);
        Object.Destroy(cameraObject);
        target.Release();
        return "[" + string.Join(",", parts) + "]";
    }

    /// <summary>Nine pixels of the target (a 3x3 grid), as bytes.</summary>
    static string Samples(RenderTexture target)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        RenderTexture.active = previous;
        var parts = new List<string>();
        foreach (var y in new[] { 8, 24, 40 })
            foreach (var x in new[] { 10, 32, 54 })
            {
                Color32 c = texture.GetPixel(x, y);
                parts.Add($"[{c.r},{c.g},{c.b}]");
            }
        Object.Destroy(texture);
        return "[" + string.Join(",", parts) + "]";
    }

    static void Save(RenderTexture target, string path)
    {
        var previous = RenderTexture.active;
        RenderTexture.active = target;
        var texture = new Texture2D(target.width, target.height, TextureFormat.RGBA32, false);
        texture.ReadPixels(new Rect(0, 0, target.width, target.height), 0, 0);
        RenderTexture.active = previous;
        File.WriteAllBytes(path, texture.EncodeToPNG());
        Object.Destroy(texture);
    }
}
