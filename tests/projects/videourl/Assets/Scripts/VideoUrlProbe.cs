using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// Records a VideoPlayer playing from http URLs in Editor Play mode. The runner serves the video
/// probe's clip.webm (VP8 64x48, 10 frames a second, 3 seconds, no audio) and the video audio
/// probe's tone.webm (one Vorbis track) at VIDEO_URL_BASE. Scenarios, each a fresh player rendering
/// into a render texture: each clip prepared (the player's answers once prepared, and before),
/// frame 5 of clip.webm sought and sampled; a missing file on the server and a refused connection
/// (the errors received, prepared or not after a wait). Error messages are recorded with the
/// server's address replaced by "BASE/" and the refused one by "REFUSED/".
/// </summary>
public sealed class VideoUrlProbe : MonoBehaviour
{
    StringBuilder m_Output;
    string m_Directory;
    string m_Base;
    const string Refused = "http://127.0.0.1:1/";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartProbe()
    {
        var directory = Environment.GetEnvironmentVariable("VIDEO_URL_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var probe = new GameObject("VideoUrlProbe").AddComponent<VideoUrlProbe>();
        probe.m_Directory = directory;
        probe.m_Base = Environment.GetEnvironmentVariable("VIDEO_URL_BASE") ?? "http://127.0.0.1:9/";
        probe.m_Output = new StringBuilder();
    }

    void Put(string key, params object[] values) =>
        m_Output.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(Json(values)).Append("}\n");

    string Clean(string text) => text?.Replace(m_Base, "BASE/").Replace(Refused, "REFUSED/");

    IEnumerator Start()
    {
        yield return Play("clip", m_Base + "clip.webm", true);
        yield return Play("tone", m_Base + "tone.webm", false);
        yield return Play("missing", m_Base + "missing.webm", false);
        yield return Play("refused", Refused + "clip.webm", false);
        m_Output.Append("{\"k\":\"complete\",\"v\":true}\n");
        Directory.CreateDirectory(m_Directory);
        File.WriteAllText(Path.Combine(m_Directory, "values.jsonl"), m_Output.ToString());
        EditorApplication.Exit(0);
    }

    IEnumerator Play(string name, string url, bool sample)
    {
        var target = new RenderTexture(64, 48, 0, RenderTextureFormat.ARGB32);
        target.Create();
        var go = new GameObject("Video " + name);
        var player = go.AddComponent<VideoPlayer>();
        player.playOnAwake = false;
        player.audioOutputMode = VideoAudioOutputMode.None;
        player.renderMode = VideoRenderMode.RenderTexture;
        player.targetTexture = target;
        player.url = url;
        var errors = new List<string>();
        int prepared = 0;
        player.errorReceived += (_, message) => errors.Add(Clean(message));
        player.prepareCompleted += _ => prepared++;
        Put(name + ".before", player.source.ToString(), Clean(player.url), player.isPrepared, (int)player.audioTrackCount, (long)player.frameCount, player.width, player.height);
        player.Prepare();
        Put(name + ".afterPrepare", player.isPrepared, prepared);
        float waited = 0;
        while (!player.isPrepared && errors.Count == 0 && waited < 8f) { waited += Time.unscaledDeltaTime; yield return null; }
        // Errors may come in more than once: wait a little longer.
        float extra = 0;
        while (extra < 0.5f) { extra += Time.unscaledDeltaTime; yield return null; }
        Put(name + ".prepared", player.isPrepared, prepared, errors.ToArray(), (int)player.audioTrackCount, (long)player.frameCount,
            player.width, player.height, Math.Round(player.length, 3), Math.Round(player.frameRate, 3), player.canSetTime, player.canStep);
        if (sample && player.isPrepared)
        {
            bool ready = false;
            player.sendFrameReadyEvents = true;
            player.frameReady += (_, frame) => { if (frame == 5) ready = true; };
            player.Pause();
            player.frame = 5;
            waited = 0;
            while (!ready && waited < 8f) { waited += Time.unscaledDeltaTime; yield return null; }
            yield return null;
            var read = new Texture2D(64, 48, TextureFormat.RGBA32, false);
            RenderTexture.active = target;
            read.ReadPixels(new Rect(0, 0, 64, 48), 0, 0);
            read.Apply();
            RenderTexture.active = null;
            var samples = new[] { (8, 8), (32, 24), (56, 40) }
                .Select(p => { Color32 c = read.GetPixel(p.Item1, p.Item2); return (object)new object[] { (int)c.r, (int)c.g, (int)c.b }; })
                .ToArray();
            Put(name + ".frame5", ready, (int)player.frame, samples);
            Destroy(read);
        }
        Destroy(go);
        target.Release();
        yield return null;
    }

    static string Json(object value) => value switch
    {
        null => "null",
        string text => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"",
        bool flag => flag ? "true" : "false",
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        IEnumerable items => "[" + string.Join(",", items.Cast<object>().Select(Json)) + "]",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };
}
