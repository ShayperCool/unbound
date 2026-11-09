using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Unity.Jobs;
using UnityEngine;

/// <summary>Lifecycle controls and property access in distinct execution contexts.</summary>
public class ThreadApiFollowups : MonoBehaviour
{
    static readonly List<string> Rows = new List<string>();
    static Renderer renderer;
    static Animator animator;
    static AudioSource audio;
    static string context;
    static string Escape(string s) => s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "");
    static void Row(string key, object value) => Rows.Add("{\"thread\":\"" + context + "\",\"call\":\"" + Escape(key) + "\",\"result\":\"" + Escape(Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture)) + "\"}");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Create()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("THREAD_FOLLOWUP_OUT")))
            new GameObject("ThreadApiFollowups").AddComponent<ThreadApiFollowups>();
    }

    static void Properties(object target, string names)
    {
        foreach (string name in names.Split(' '))
        {
            var p = target.GetType().GetProperty(name);
            object value = null;
            try { value = p.GetValue(target); Row(target.GetType().Name + "." + name + " get", "ok"); }
            catch (Exception e) { Outcome(target.GetType().Name + "." + name + " get", e); }
            if (!p.CanWrite) continue;
            // Use a value read on the main thread, so the setter is exercised even when its getter throws.
            value = Values[target.GetType().Name + "." + name];
            try { p.SetValue(target, value); Row(target.GetType().Name + "." + name + " set", "ok"); }
            catch (Exception e) { Outcome(target.GetType().Name + "." + name + " set", e); }
        }
    }
    static readonly Dictionary<string, object> Values = new Dictionary<string, object>();
    const string RendererProperties = "forceRenderingOff shadowCastingMode receiveShadows staticShadowCaster motionVectorGenerationMode lightProbeUsage reflectionProbeUsage renderingLayerMask rendererPriority allowOcclusionWhenDynamic isPartOfStaticBatch lightmapIndex realtimeLightmapIndex lightmapScaleOffset realtimeLightmapScaleOffset probeAnchor lightProbeProxyVolumeOverride sortingLayerName localToWorldMatrix worldToLocalMatrix localBounds";
    const string AnimatorProperties = "layerCount hasTransformHierarchy hasRootMotion humanScale isHuman isOptimizable pivotWeight pivotPosition animatePhysics fireEvents keepAnimatorStateOnDisable writeDefaultValuesOnDisable logWarnings updateMode playableGraph keepAnimatorControllerStateOnDisable avatarRoot avatar hasBoundPlayables leftFeetBottomHeight rightFeetBottomHeight";
    const string AudioProperties = "priority minDistance maxDistance panStereo spatialBlend spread dopplerLevel reverbZoneMix bypassEffects bypassListenerEffects bypassReverbZones ignoreListenerVolume ignoreListenerPause spatialize spatializePostEffects velocityUpdateMode time resource outputAudioMixerGroup gamepadSpeakerOutputType isVirtual rolloffMode";
    static void Outcome(string key, Exception e)
    {
        while (e is TargetInvocationException && e.InnerException != null) e = e.InnerException;
        Row(key, "throws " + e.GetType().FullName + ": " + e.Message);
    }
    static void Matrix()
    {
        Properties(renderer, RendererProperties);
        Properties(animator, AnimatorProperties);
        Properties(audio, AudioProperties);
    }
    struct Job : IJob { public void Execute() => Matrix(); }
    static void Remember(object target, string names)
    {
        foreach (string name in names.Split(' '))
        {
            var p = target.GetType().GetProperty(name);
            if (p.CanWrite) Values[target.GetType().Name + "." + name] = p.GetValue(target);
        }
    }
    static void State(string name, Animator a) => Row(name, a.isInitialized);
    static void State(string name, AudioSource a) => Row(name, a.isPlaying);
    IEnumerator Start()
    {
        context = "state";
        var go = new GameObject("Defaults");
        renderer = go.AddComponent<MeshRenderer>();
        animator = go.AddComponent<Animator>();
        audio = go.AddComponent<AudioSource>();
        Row("renderer shared initially null", renderer.sharedMaterial == null);
        Row("renderer slots initially", renderer.sharedMaterials.Length);
        var material = renderer.material;
        Row("renderer default exists", material != null);
        Row("renderer default name", material.name);
        Row("renderer default shader", material.shader.name);
        Row("renderer repeated instance", renderer.material == material);
        renderer.sharedMaterials = new Material[0];
        Row("renderer empty materials length", renderer.materials.Length);
        Row("renderer empty material exists", renderer.material != null);
        Row("renderer empty material grows slots", renderer.sharedMaterials.Length);
        renderer.sharedMaterial = null;
        Row("renderer null slot material exists", renderer.material != null);
        var other = new GameObject("Other").AddComponent<MeshRenderer>();
        var shared = new Material(Shader.Find("Standard")); shared.color = Color.white;
        renderer.sharedMaterial = shared; other.sharedMaterial = shared;
        renderer.material.color = Color.red;
        Row("material instance separates renderers", renderer.material != other.sharedMaterial);
        Row("material instance mutation isolated", other.sharedMaterial.color == Color.white);
        State("animator active added", animator);
        animator.enabled = false; State("animator disabled", animator);
        animator.enabled = true; State("animator enabled", animator);
        go.SetActive(false); State("animator inactive", animator);
        animator.Rebind(); State("animator inactive rebind", animator);
        go.SetActive(true); State("animator reactivated", animator);
        var inactive = new GameObject("Inactive"); inactive.SetActive(false);
        var ai = inactive.AddComponent<Animator>(); State("animator added inactive", ai);
        ai.Rebind(); State("animator added inactive rebind", ai);
        inactive.SetActive(true); State("animator first activation", ai);
        ai.keepAnimatorStateOnDisable = true;
        inactive.SetActive(false); State("animator inactive keep state", ai);
        inactive.SetActive(true);
        ai.enabled = false; inactive.SetActive(false); State("animator disabled hierarchy keep state", ai);
        ai.keepAnimatorStateOnDisable = false;
        inactive.SetActive(true); ai.enabled = true;
        ai.enabled = false; inactive.SetActive(false); State("animator disabled hierarchy reset", ai);
        // New sources distinguish pending play-on-awake from an already completed audio update.
        var fresh = new GameObject("FreshAudio").AddComponent<AudioSource>();
        State("audio fresh clipless", fresh);
        fresh.Pause(); State("audio fresh paused", fresh);
        fresh.UnPause(); State("audio fresh unpaused", fresh);
        fresh.Stop(); State("audio fresh stopped", fresh);
        var explicitPlay = new GameObject("ExplicitCliplessAudio").AddComponent<AudioSource>();
        State("audio clipless before explicit play", explicitPlay);
        explicitPlay.Play(); State("audio clipless explicit play", explicitPlay);
        var disabled = new GameObject("DisabledAudio").AddComponent<AudioSource>();
        disabled.enabled = false; State("audio disabled pending", disabled);
        disabled.enabled = true; State("audio reenabled clipless", disabled);
        var delayed = new GameObject("DelayedAudio").AddComponent<AudioSource>();
        delayed.Stop(); delayed.clip = AudioClip.Create("silence", 44100, 1, 44100, false); delayed.loop = true;
        delayed.PlayDelayed(10); State("audio delayed", delayed);
        delayed.Pause(); State("audio delayed paused", delayed);
        delayed.Stop(); delayed.Play(); State("audio played", delayed);
        delayed.Pause(); State("audio played paused", delayed);
        delayed.UnPause(); State("audio played unpaused", delayed);
        delayed.Stop(); State("audio played stopped", delayed);
        var silentObject = new GameObject("SilentAudio"); silentObject.SetActive(false);
        var silent = silentObject.AddComponent<AudioSource>(); State("audio added inactive", silent);
        silent.playOnAwake = false; silentObject.SetActive(true); State("audio playOnAwake false", silent);
        var pending = new GameObject("PendingAudio").AddComponent<AudioSource>();
        State("audio before frame", pending);
        yield return null;
        context = "state";
        State("audio after frame", pending); State("animator after frame", animator);
        Remember(renderer, RendererProperties); Remember(animator, AnimatorProperties); Remember(audio, AudioProperties);
        context = "main"; Matrix();
        context = "Thread"; var thread = new Thread(Matrix); thread.Start(); thread.Join();
        context = "Task"; Task.Run((Action)Matrix).Wait();
        context = "IJob.Run"; new Job().Run();
        context = "IJob.Schedule"; new Job().Schedule().Complete();
        Rows.Add("{\"complete\":true}");
        var output = Environment.GetEnvironmentVariable("THREAD_FOLLOWUP_OUT");
        Directory.CreateDirectory(Path.GetDirectoryName(output)); File.WriteAllLines(output, Rows);
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
