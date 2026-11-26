using System;
using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using UnityEngine.Video;

/// <summary>
/// Records the pictures of a VP8 clip with alpha in Editor Play mode. The clip
/// (Assets/Video/alpha.webm: 64x48, 10 frames a second, 1 second, one colour 0x4080C0, its alpha
/// a horizontal ramp from 0 at the left column to 255 at the right) is shown paused on frame 0 into
/// an ARGB32 render texture and an ARGBHalf one; each
/// row records the colour and alpha (0..255) at columns 0, 8, 16, 24, 32, 40, 48, 56 and 63 of
/// its middle row, and the render texture's own size.
/// </summary>
public sealed class VideoAlphaProbe : MonoBehaviour
{
    StringBuilder m_Output;
    string m_Directory;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void StartProbe()
    {
        var directory = Environment.GetEnvironmentVariable("VIDEO_ALPHA_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var probe = new GameObject("VideoAlphaProbe").AddComponent<VideoAlphaProbe>();
        probe.m_Directory = directory;
        probe.m_Output = new StringBuilder();
    }

    void Put(string key, params object[] values) =>
        m_Output.Append("{\"k\":").Append(Json(key)).Append(",\"v\":").Append(Json(values)).Append("}\n");

    IEnumerator Start()
    {
        var clip = AssetDatabase.LoadAssetAtPath<VideoClip>("Assets/Video/alpha.webm");
        Put("clip", clip.width, clip.height, clip.frameCount, clip.audioTrackCount);
        yield return Show("rt", clip, RenderTextureFormat.ARGB32);
        yield return Show("rtHalf", clip, RenderTextureFormat.ARGBHalf);
        m_Output.Append("{\"k\":\"complete\",\"v\":true}\n");
        Directory.CreateDirectory(m_Directory);
        File.WriteAllText(Path.Combine(m_Directory, "values.jsonl"), m_Output.ToString());
        EditorApplication.Exit(0);
    }

    IEnumerator Show(string name, VideoClip clip, RenderTextureFormat format)
    {
        var target = new RenderTexture(64, 48, 0, format);
        target.Create();
        // A known colour before the frame, to see what the video writes over it.
        RenderTexture.active = target;
        GL.Clear(false, true, new Color(1f, 0f, 1f, 0.5f));
        RenderTexture.active = null;
        var go = new GameObject("Video " + name);
        var player = go.AddComponent<VideoPlayer>();
        player.playOnAwake = false;
        player.audioOutputMode = VideoAudioOutputMode.None;
        player.renderMode = VideoRenderMode.RenderTexture;
        player.targetTexture = target;
        player.clip = clip;
        player.sendFrameReadyEvents = true;
        bool ready = false;
        player.frameReady += (_, frame) => { if (frame == 0) ready = true; };
        player.Prepare();
        float waited = 0;
        while (!player.isPrepared && waited < 10f) { waited += Time.unscaledDeltaTime; yield return null; }
        player.Pause();
        waited = 0;
        while (!ready && waited < 10f) { waited += Time.unscaledDeltaTime; yield return null; }
        yield return null;
        yield return null;
        var read = new Texture2D(64, 48, TextureFormat.RGBA32, false);
        RenderTexture.active = target;
        read.ReadPixels(new Rect(0, 0, 64, 48), 0, 0);
        read.Apply();
        RenderTexture.active = null;
        var samples = new[] { 0, 8, 16, 24, 32, 40, 48, 56, 63 }
            .Select(x => { Color32 c = read.GetPixel(x, 24); return (object)new object[] { (int)c.r, (int)c.g, (int)c.b, (int)c.a }; })
            .ToArray();
        Put(name + ".prepared", player.isPrepared, ready, (int)player.frame, player.width, player.height);
        Put(name + ".samples", samples);
        Destroy(go);
        Destroy(read);
        target.Release();
        yield return null;
    }

    static string Json(object value) => value switch
    {
        null => "null",
        string text => "\"" + text.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"",
        bool flag => flag ? "true" : "false",
        float f => f.ToString("R", CultureInfo.InvariantCulture),
        double d => d.ToString("R", CultureInfo.InvariantCulture),
        IEnumerable items => "[" + string.Join(",", items.Cast<object>().Select(Json)) + "]",
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };
}
