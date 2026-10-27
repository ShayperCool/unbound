using System.Collections;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// Audio from an AssetBundle: each clip of the `audio` bundle, its properties, its samples as
/// GetData reads them once loaded (every 37th sample of every channel, and the sum of squares), and
/// whether an AudioSource plays it; then the same for each clip as the project's asset (the Editor's
/// import of the same file and settings). Writes <c>audio.jsonl</c> into <c>AUDIO_PROBE_OUT</c>, then quits.
/// </summary>
public class AudioProbe : MonoBehaviour
{
    static readonly StringBuilder s_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("AudioProbe").AddComponent<AudioProbe>();

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static void Record(string kind, string body) => s_Out.Append("{\"case\":\"").Append(kind).Append("\",").Append(body).Append("}\n");

    IEnumerator Start()
    {
        var dir = System.Environment.GetEnvironmentVariable("AUDIO_PROBE_OUT") ?? "/tmp/audio";
        Directory.CreateDirectory(dir);
        yield return null;
        var bundle = AssetBundle.LoadFromFile(Path.Combine(Application.dataPath, "..", "Reference", "bundles", "audio"));
        var paths = bundle.GetAllAssetNames().OrderBy(n => n).ToArray();
        foreach (var path in paths)
            yield return Clip("clip", path, bundle.LoadAsset<AudioClip>(path));
        bundle.Unload(true);
#if UNITY_EDITOR
        foreach (var path in paths)
            yield return Clip("asset", path, UnityEditor.AssetDatabase.LoadAssetAtPath<AudioClip>(path));
#endif
        File.WriteAllText(Path.Combine(dir, "audio.jsonl"), s_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    IEnumerator Clip(string kind, string path, AudioClip clip)
    {
        if (clip.loadState != AudioDataLoadState.Loaded) clip.LoadAudioData();
        for (int i = 0; i < 100 && clip.loadState == AudioDataLoadState.Loading; i++) yield return null;
        var samples = new float[clip.samples * clip.channels];
        bool read = clip.GetData(samples, 0);
        double energy = samples.Sum(s => (double)s * s);
        var picked = string.Join(",", Enumerable.Range(0, samples.Length / 37).Select(i => F(samples[i * 37])));
        var source = gameObject.AddComponent<AudioSource>();
        source.clip = clip;
        source.Play();
        yield return null;
        bool playing = source.isPlaying;
        Destroy(source);
        Record(kind, $"\"path\":\"{path}\",\"name\":\"{clip.name}\",\"length\":{F(clip.length)},\"samples\":{clip.samples},\"channels\":{clip.channels},\"frequency\":{clip.frequency}," +
            $"\"loadType\":{(int)clip.loadType},\"preload\":{(clip.preloadAudioData ? "true" : "false")},\"loadState\":{(int)clip.loadState},\"read\":{(read ? "true" : "false")},\"playing\":{(playing ? "true" : "false")}," +
            $"\"energy\":{energy.ToString("R", CultureInfo.InvariantCulture)},\"picked\":[{picked}]");
    }
}
