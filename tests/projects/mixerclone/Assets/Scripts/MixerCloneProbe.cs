using System;
using System.Globalization;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Audio;

// Object.Instantiate of an audio mixer's groups and snapshots: whether a clone is made, what it is
// and what it belongs to, in Play mode.
public class MixerCloneProbe : MonoBehaviour
{
    [RuntimeInitializeOnLoadMethod] static void Install() { new GameObject("MixerCloneProbe").AddComponent<MixerCloneProbe>(); }
    [Serializable] class Row { public string key; public string value; }
    StreamWriter output;
    void Put(string key, object value) => output.WriteLine(JsonUtility.ToJson(new Row { key = key, value = value == null ? "<null>" : Convert.ToString(value, CultureInfo.InvariantCulture) }));

    void Start()
    {
        string path = Environment.GetEnvironmentVariable("MIXER_CLONE_OUT"); if (string.IsNullOrEmpty(path)) return;
        try
        {
            using (output = new StreamWriter(path)) { Record(); Put("complete", 1); }
            EditorApplication.Exit(0);
        }
        catch (Exception ex) { Debug.LogException(ex); EditorApplication.Exit(1); }
    }

    static string Interfaces(Type type) => string.Join(",", type.GetInterfaces().Select(i => i.FullName).OrderBy(n => n, StringComparer.Ordinal));

    void Clone(string key, UnityEngine.Object source, AudioMixer mixer)
    {
        UnityEngine.Object clone = null;
        try
        {
            clone = Instantiate(source);
            Put(key + ".result", clone == null ? "null" : clone is AudioMixerGroup ? "AudioMixerGroup" : clone is AudioMixerSnapshot ? "AudioMixerSnapshot" : clone.GetType().Name);
        }
        catch (Exception e)
        {
            Put(key + ".result", "threw " + e.GetType().Name + ": " + e.Message);
        }
        if (clone == null) return;
        Put(key + ".name", clone.name);
        Put(key + ".same", ReferenceEquals(clone, source));
        Put(key + ".newId", clone.GetInstanceID() != source.GetInstanceID());
        Put(key + ".hideFlags", clone.hideFlags);
        if (clone is AudioMixerGroup group)
        {
            Put(key + ".mixer", group.audioMixer == null ? "<null>" : group.audioMixer.name);
            Put(key + ".mixerSame", ReferenceEquals(group.audioMixer, mixer));
            Put(key + ".inMixer", mixer.FindMatchingGroups(string.Empty).Contains(group));
        }
        if (clone is AudioMixerSnapshot snapshot)
        {
            Put(key + ".mixer", snapshot.audioMixer == null ? "<null>" : snapshot.audioMixer.name);
            Put(key + ".mixerSame", ReferenceEquals(snapshot.audioMixer, mixer));
            Put(key + ".found", ReferenceEquals(mixer.FindSnapshot(snapshot.name), snapshot));
            try { snapshot.TransitionTo(0f); Put(key + ".transition", "ok"); }
            catch (Exception e) { Put(key + ".transition", "threw " + e.GetType().Name); }
        }
        Destroy(clone);
    }

    void Record()
    {
        Put("group.interfaces", Interfaces(typeof(AudioMixerGroup)));
        Put("snapshot.interfaces", Interfaces(typeof(AudioMixerSnapshot)));
        var mixer = Resources.Load<AudioMixer>("Mixer");
        Put("mixer.loaded", mixer != null);
        var groups = mixer.FindMatchingGroups(string.Empty);
        Put("groups", string.Join(",", groups.Select(g => g.name)));
        var snapshot = mixer.FindSnapshot("Snapshot");
        Put("snapshot", snapshot == null ? "<null>" : snapshot.name);
        Clone("master", groups.First(g => g.name == "Master"), mixer);
        Clone("music", groups.First(g => g.name == "Music"), mixer);
        Clone("snapshot", snapshot, mixer);
        // A source routed to a group, cloned with its object, keeps the original group.
        var go = new GameObject("Source");
        var source = go.AddComponent<AudioSource>();
        source.outputAudioMixerGroup = groups.First(g => g.name == "Music");
        var copy = Instantiate(go).GetComponent<AudioSource>();
        Put("source.group", copy.outputAudioMixerGroup == null ? "<null>" : copy.outputAudioMixerGroup.name);
        Put("source.groupSame", ReferenceEquals(copy.outputAudioMixerGroup, source.outputAudioMixerGroup));
    }
}
