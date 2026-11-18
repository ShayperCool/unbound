using System;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

/// <summary>Authors the probe's mixer (Master and Music, attenuation only) and its serialized
/// filter prefabs, then enters play mode, where EffectProbe captures every case.</summary>
public static class ProbeRunner
{
    const BindingFlags F = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;
    static Type T(string name) => typeof(Editor).Assembly.GetType("UnityEditor.Audio." + name, true);
    static object Call(object o, string method, params object[] args) => o.GetType().GetMethod(method, F).Invoke(o, args);
    static object Get(object o, string property) => o.GetType().GetProperty(property, F).GetValue(o);

    public static void Record()
    {
        Directory.CreateDirectory("Assets/Resources");
        var mixer = T("AudioMixerController").GetMethod("CreateMixerControllerAtPath", F).Invoke(null, new object[] { "Assets/Resources/Probe.mixer" });
        // Keep the mixer processing through silent stretches.
        var serialized = new SerializedObject((UnityEngine.Object)mixer);
        var suspend = serialized.FindProperty("m_EnableSuspend");
        if (suspend == null) throw new Exception("Missing mixer suspend setting");
        suspend.boolValue = false; serialized.ApplyModifiedPropertiesWithoutUndo();
        var music = Call(mixer, "CreateNewGroup", "Music", true); Call(mixer, "AddChildToParent", music, Get(mixer, "masterGroup"));
        EditorUtility.SetDirty((UnityEngine.Object)mixer);

        // A low-pass filter with a curve of the cutoff over distance (normalized), at a source
        // whose maxDistance is 10.
        var low = new GameObject("SerializedLow");
        low.AddComponent<AudioSource>().maxDistance = 10f;
        var lowFilter = low.AddComponent<AudioLowPassFilter>();
        lowFilter.lowpassResonanceQ = 2f;
        lowFilter.customCutoffCurve = new AnimationCurve(new Keyframe(0f, 0.5f, -0.45f, -0.45f), new Keyframe(1f, 0.05f, -0.45f, -0.45f));
        PrefabUtility.SaveAsPrefabAsset(low, "Assets/Resources/SerializedLow.prefab");
        UnityEngine.Object.DestroyImmediate(low);
        var high = new GameObject("SerializedHigh");
        high.AddComponent<AudioSource>();
        var highFilter = high.AddComponent<AudioHighPassFilter>();
        highFilter.cutoffFrequency = 800f; highFilter.highpassResonanceQ = 2f;
        PrefabUtility.SaveAsPrefabAsset(high, "Assets/Resources/SerializedHigh.prefab");
        UnityEngine.Object.DestroyImmediate(high);
        AssetDatabase.SaveAssets();
        WriteLegacy("Assets/Resources/SerializedLow.prefab", "Assets/Resources/SerializedLegacy.prefab", 10f);

        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        new GameObject("EffectProbe").AddComponent<EffectProbe>(); Directory.CreateDirectory("Assets/Scenes");
        EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(), "Assets/Scenes/Probe.unity");
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene("Assets/Scenes/Probe.unity", true) };
        EditorApplication.EnterPlaymode();
    }

    /// <summary>The low-pass prefab as an older serialized version stores it: the filter's
    /// version 2, its curve's key times in world units (times maxDistance, slopes over it).</summary>
    static void WriteLegacy(string from, string to, float maxDistance)
    {
        var text = File.ReadAllText(from).Replace("m_Name: SerializedLow", "m_Name: SerializedLegacy");
        int filter = text.IndexOf("\nAudioLowPassFilter:", StringComparison.Ordinal);
        if (filter < 0) throw new Exception("No AudioLowPassFilter in " + from);
        int end = text.IndexOf("\n--- ", filter, StringComparison.Ordinal);
        if (end < 0) end = text.Length;
        var body = text.Substring(filter, end - filter);
        var version = new Regex(@"^(  serializedVersion: )3$", RegexOptions.Multiline);
        if (!version.IsMatch(body)) throw new Exception("No serialized version 3 in the filter of " + from);
        body = version.Replace(body, "${1}2", 1);
        string Scale(Match m, float factor) => m.Groups[1].Value + (float.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture) * factor).ToString("R", CultureInfo.InvariantCulture);
        body = Regex.Replace(body, @"^(      time: )(\S+)$", m => Scale(m, maxDistance), RegexOptions.Multiline);
        body = Regex.Replace(body, @"^(      (?:in|out)Slope: )(\S+)$", m => Scale(m, 1f / maxDistance), RegexOptions.Multiline);
        File.WriteAllText(to, text.Substring(0, filter) + body + text.Substring(end));
        AssetDatabase.ImportAsset(to, ImportAssetOptions.ForceUpdate);
    }
}
