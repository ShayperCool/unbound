using System;
using System.IO;
using System.Collections;
using UnityEngine;
using UnityEngine.Audio;
using Unity.Collections;

public class ParamsProbe : MonoBehaviour
{
    public static readonly string[] Cases = { "lp_render", "dist_render", "lp_idle", "dist_idle" };
    static bool Engine => Environment.GetEnvironmentVariable("PARAMS_ENGINE") == "1";
    StreamWriter output;
    string key;
    void Record(string step, object value) { output.WriteLine("\"" + key + "." + step + "\":\"" + value + "\","); output.Flush(); }
    void Read(AudioMixer mixer, string step, string name = "Value")
    {
        bool ok = mixer.GetFloat(name, out float value);
        Record(step, ok + ":" + value.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
    }
    IEnumerator Start()
    {
        var routine = Capture();
        for (;;) {
            object next;
            try { if (!routine.MoveNext()) yield break; next = routine.Current; }
            catch (Exception error) { Debug.LogException(error); Exit(1); yield break; }
            yield return next;
        }
    }
    static void Exit(int code)
    {
        if (Engine) { Application.Quit(code); return; }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
    IEnumerator Capture()
    {
        var config = AudioSettings.GetConfiguration();
        config.sampleRate = int.Parse(Environment.GetEnvironmentVariable("PARAMS_RATE"));
        config.speakerMode = AudioSpeakerMode.Stereo; config.dspBufferSize = 1024;
        if (!AudioSettings.Reset(config)) throw new Exception("Reset failed");
        Time.captureDeltaTime = 1024f / config.sampleRate;
        gameObject.AddComponent<AudioListener>();
        if (!AudioRenderer.Start()) throw new Exception("Start failed");
        string path = Environment.GetEnvironmentVariable("PARAMS_OUT"); Directory.CreateDirectory(path);
        output = new StreamWriter(Path.Combine(path, "capture.json"));
        output.WriteLine("{");
        output.WriteLine("\"release\":\"" + Application.unityVersion + "\",\"rate\":" + AudioSettings.outputSampleRate + ",");
        using (var buffer = new NativeArray<float>(2048, Allocator.Persistent))
        foreach (string item in Cases)
        {
            key = item; bool lp = key.StartsWith("lp"), render = key.EndsWith("render");
            float a = lp ? 4000f : 0.9f, b = lp ? 6000f : 0.75f;
            var mixer = Resources.Load<AudioMixer>(key);
            Read(mixer, "initial");
            Record("pre.set", mixer.SetFloat("Value", a)); Read(mixer, "pre.set.get");
            Record("pre.clear", mixer.ClearFloat("Value")); Read(mixer, "pre.clear.get.0"); Read(mixer, "pre.clear.get.1");
            Record("pre.clear.again", mixer.ClearFloat("Value")); Read(mixer, "pre.clear.again.get");
            Read(mixer, "missing.get", "Missing"); Record("missing.set", mixer.SetFloat("Missing", 1)); Record("missing.clear", mixer.ClearFloat("Missing"));
            Read(mixer, "nonexposed.get", "Volume"); Record("nonexposed.set", mixer.SetFloat("Volume", 1)); Record("nonexposed.clear", mixer.ClearFloat("Volume"));
            mixer.SetFloat("Value", a); Read(mixer, "pre.setafterclear");
            var go = new GameObject("Signal"); var source = go.AddComponent<AudioSource>();
            var clip = AudioClip.Create("Signal", 32768, 2, config.sampleRate, false);
            var data = new float[65536]; for (int i = 0; i < data.Length; i++) data[i] = 0.1f;
            clip.SetData(data, 0); source.clip = clip; source.loop = true; source.outputAudioMixerGroup = mixer.FindMatchingGroups("Master")[0]; source.Play();
            for (int block = 0; block < 4; block++) {
                yield return null;
                if (render && !AudioRenderer.Render(buffer)) throw new Exception("Render failed");
                Read(mixer, "active.block." + block);
                }
            Read(mixer, "active.beforeclear");
            Record("active.clear", mixer.ClearFloat("Value")); Read(mixer, "active.clear.get.0"); Read(mixer, "active.clear.get.1");
            Record("active.clear.again", mixer.ClearFloat("Value")); Read(mixer, "active.clear.again.get");
            mixer.SetFloat("Value", b); Read(mixer, "active.setafterclear");
            mixer.ClearFloat("Value"); Read(mixer, "active.unapplied.clear");
            if (render && !AudioRenderer.Render(buffer)) throw new Exception("Second render failed");
            Read(mixer, "active.sameframe.render");
            yield return null; Read(mixer, "active.nextframe");
            if (render && !AudioRenderer.Render(buffer)) throw new Exception("Next render failed");
            Read(mixer, "active.nextframe.render");
            mixer.SetFloat("Value", a);
            yield return null;
            if (render && !AudioRenderer.Render(buffer)) throw new Exception("Override render failed");
            mixer.FindSnapshot("Other").TransitionTo(0); Read(mixer, "snapshot.overridden.immediate");
            yield return null;
            if (render && !AudioRenderer.Render(buffer)) throw new Exception("Snapshot render failed");
            Read(mixer, "snapshot.overridden.applied");
            mixer.ClearFloat("Value"); Read(mixer, "snapshot.clear.immediate");
            yield return null;
            if (render && !AudioRenderer.Render(buffer)) throw new Exception("Clear render failed");
            Read(mixer, "snapshot.clear.applied");
            mixer.FindSnapshot("Base").TransitionTo(0); Read(mixer, "snapshot.base.immediate");
            yield return null;
            if (render && !AudioRenderer.Render(buffer)) throw new Exception("Base render failed");
            Read(mixer, "snapshot.base.applied");
            source.Stop(); DestroyImmediate(go); DestroyImmediate(clip);
        }
        output.WriteLine("\"complete\":true}"); output.Dispose(); AudioRenderer.Stop(); Exit(0);
    }
}
