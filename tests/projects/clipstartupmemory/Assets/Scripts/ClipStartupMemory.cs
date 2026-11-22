using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Profiling;
using Object = UnityEngine.Object;

public static class ClipStartupMemory
{
    [Serializable]
    sealed class Row
    {
        public string k;
        public string v;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        string output = Environment.GetEnvironmentVariable("CLIP_STARTUP_OUT");
        if (string.IsNullOrEmpty(output)) return;
        string profile = Environment.GetEnvironmentVariable("CLIP_STARTUP_PROFILE");
        var rows = new List<string>();
        void Put(string key, object value) =>
            rows.Add(JsonUtility.ToJson(new Row { k = key, v = value.ToString() }));
        long Size(AudioClip clip) => Profiler.GetRuntimeMemorySizeLong(clip);

        if (profile == "Reset")
        {
            void Changed(bool deviceWasChanged)
            {
                var clip = AudioClip.Create("callback", 1, 1, 44100, false);
                Put("callback.deviceChanged", deviceWasChanged);
                Put("callback.bytes", Size(clip));
                Object.DestroyImmediate(clip);
            }
            AudioSettings.OnAudioConfigurationChanged += Changed;
            try { Put("reset.success", AudioSettings.Reset(AudioSettings.GetConfiguration())); }
            finally { AudioSettings.OnAudioConfigurationChanged -= Changed; }
            var after = AudioClip.Create("after", 1, 1, 44100, false);
            Put("after.bytes", Size(after));
            Object.DestroyImmediate(after);
        }
        else if (profile == "ResetOwner")
        {
            var first = AudioClip.Create("first", 1, 1, 44100, false);
            var second = AudioClip.Create("second", 1, 1, 44100, false);
            Put("owner.before.delta", Size(first) - Size(second));
            Put("reset.success", AudioSettings.Reset(AudioSettings.GetConfiguration()));
            Put("owner.after.delta", Size(first) - Size(second));
            Object.DestroyImmediate(second);
            Object.DestroyImmediate(first);
        }
        else if (profile == "Disabled")
        {
            var first = AudioClip.Create("first", 1, 1, 44100, false);
            var second = AudioClip.Create("second", 1, 1, 44100, false);
            long firstBytes = Size(first), secondBytes = Size(second);
            Put("disabled.first.bytes", firstBytes);
            Put("disabled.second.bytes", secondBytes);
            Put("disabled.delta", firstBytes - secondBytes);
            Object.DestroyImmediate(second);
            Object.DestroyImmediate(first);
        }
        else
        {
            bool mono = profile == "PCM";
            bool stream = profile == "Stream";
            bool nestedControl = profile == "Callback";
            int frames = mono ? 1 : 41, channels = mono ? 1 : 2, rate = mono ? 44100 : 32003;
            if (mono)
            {
                AudioSettings.GetConfiguration();
                var imported = Resources.Load<AudioClip>("ClipStartupWarm");
                Put("imported", imported != null && imported.LoadAudioData());
            }
            AudioClip nested = null;
            void Read(float[] samples)
            {
                Array.Clear(samples, 0, samples.Length);
                if (nestedControl && nested == null)
                    nested = AudioClip.Create("nested", frames, channels, rate, false);
            }
            AudioClip Create() => AudioClip.Create("clip", frames, channels, rate, stream,
                mono ? null : (AudioClip.PCMReaderCallback)Read);
            var first = Create();
            Put("first.bytes", Size(first));
            if (!stream)
            {
                Put("first.write", first.SetData(new float[frames * channels], 0));
                Put("first.written.bytes", Size(first));
            }
            var second = Create();
            Put("second.bytes", Size(second));
            Put("first.with.second.bytes", Size(first));
            Object.DestroyImmediate(second);
            Object.DestroyImmediate(first);
            var after = Create();
            Put("after.destroy.bytes", Size(after));
            Object.DestroyImmediate(after);
            if (nestedControl)
            {
                Put("nested.bytes", Size(nested));
                Object.DestroyImmediate(nested);
            }
        }

        rows.Add("{\"k\":\"complete\",\"v\":true}");
        File.WriteAllLines(Path.Combine(output, "values.jsonl"), rows);
#if UNITY_EDITOR
        File.WriteAllLines(Path.Combine(output, "profile-release.txt"), new[] { profile, Application.unityVersion });
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit(0);
#endif
    }
}
