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
        }
        File.WriteAllText(Path.Combine(dir, "video.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
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
