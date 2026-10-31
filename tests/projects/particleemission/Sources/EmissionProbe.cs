using System;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;

public class EmissionProbe : MonoBehaviour
{
    static readonly StringBuilder output = new StringBuilder();
    static string B(bool x) => x ? "true" : "false";
    static string F(float x) => x.ToString("R", CultureInfo.InvariantCulture);
    static void State(string key, ParticleSystem ps)
    {
        output.Append("{\"k\":\"").Append(key).Append("\",\"v\":{\"playing\":").Append(B(ps.isPlaying))
            .Append(",\"emitting\":").Append(B(ps.isEmitting)).Append(",\"paused\":").Append(B(ps.isPaused))
            .Append(",\"stopped\":").Append(B(ps.isStopped)).Append(",\"aliveSelf\":").Append(B(ps.IsAlive(false)))
            .Append(",\"aliveChildren\":").Append(B(ps.IsAlive(true))).Append(",\"enabled\":").Append(B(ps.emission.enabled))
            .Append(",\"count\":").Append(ps.particleCount).Append(",\"time\":").Append(F(ps.time)).Append("}}\n");
    }
    static ParticleSystem Make(bool enabled, int source, bool loop, float delay, bool live)
    {
        var go = new GameObject("emission-control"); go.SetActive(false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.playOnAwake = false; main.duration = 1; main.loop = loop;
        main.startDelay = delay; main.startLifetime = 2; main.startSpeed = 0;
        var shape = ps.shape; shape.enabled = false;
        var emission = ps.emission; emission.enabled = enabled;
        emission.rateOverTime = source == 1 ? 4 : 0; emission.rateOverDistance = source == 2 ? 4 : 0;
        if (source == 3) emission.SetBursts(new[] { new ParticleSystem.Burst(.25f, (short)2) });
        ps.useAutoRandomSeed = false; ps.randomSeed = 12345;
        go.SetActive(true);
        if (live) ps.SetParticles(new[] { new ParticleSystem.Particle { startLifetime = 2, remainingLifetime = 2, randomSeed = 42 } });
        return ps;
    }
    static void Toggle(string key, ParticleSystem ps)
    {
        var emission = ps.emission;
        foreach (bool enabled in new[] { false, false, true, true, false, true }) {
            emission.enabled = enabled;
            State(key + "/" + toggleIndex++, ps);
        }
    }
    static int toggleIndex;
    static void Controls(bool enabled, int source, bool loop, float delay, bool live)
    {
        string key = "matrix/" + B(enabled) + "/" + source + "/" + B(loop) + "/" + F(delay) + "/" + B(live);
        var ps = Make(enabled, source, loop, delay, live);
        State(key + "/initial", ps);
        Toggle(key + "/stoppedToggle", ps);
        var emission = ps.emission; emission.enabled = enabled;
        ps.Play(false); State(key + "/play", ps);
        ps.Play(false); State(key + "/playAgain", ps);
        Toggle(key + "/playingToggle", ps); emission.enabled = enabled;
        var playing = ps.GetPlaybackState();
        ps.Pause(false); State(key + "/pause", ps);
        ps.Pause(false); State(key + "/pauseAgain", ps);
        Toggle(key + "/pausedToggle", ps); emission.enabled = enabled;
        ps.Play(false); State(key + "/resume", ps);
        ps.Clear(false); State(key + "/clearPlaying", ps);
        ps.Emit(1); State(key + "/emitPlaying", ps);
        ps.SetPlaybackState(playing); State(key + "/restorePlaying", ps);
        // The module is not part of the opaque PlaybackState. Observe restoration through public getters.
        emission.enabled = !enabled;
        ps.SetPlaybackState(playing); State(key + "/restoreWithModuleChanged", ps);
        emission.enabled = enabled;
        ps.Stop(false, ParticleSystemStopBehavior.StopEmitting); State(key + "/stopLive", ps);
        ps.Stop(false, ParticleSystemStopBehavior.StopEmitting); State(key + "/stopLiveAgain", ps);
        Toggle(key + "/stopLiveToggle", ps); emission.enabled = enabled;
        ps.Clear(false); State(key + "/clearStoppedEmission", ps);
        ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); State(key + "/stopClear", ps);
        ps.Pause(false); State(key + "/pauseStopped", ps);
        ps.Play(false); State(key + "/playPausedStopped", ps);
        ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.Emit(0); State(key + "/emitZeroStopped", ps);
        ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.Emit(1); State(key + "/emitStopped", ps);
        Toggle(key + "/manualToggle", ps); emission.enabled = enabled;
        ps.Pause(false); State(key + "/pauseManual", ps);
        ps.Emit(new ParticleSystem.EmitParams { startLifetime = 2 }, 1); State(key + "/emitPaused", ps);
        ps.Clear(false); State(key + "/clearManual", ps);
        UnityEngine.Object.DestroyImmediate(ps.gameObject);
    }
    static void Simulation(bool enabled, int source, bool loop, float delay, bool live, bool fixedStep)
    {
        string key = "simulation/" + B(enabled) + "/" + source + "/" + B(loop) + "/" + F(delay) + "/" + B(live) + "/" + B(fixedStep);
        var ps = Make(enabled, source, loop, delay, live);
        ps.Play(false); State(key + "/play", ps);
        int i = 0;
        foreach (float dt in new[] { 0f, .125f, .125f, .25f, .5f, .25f, 1f, 2f }) {
            ps.Simulate(dt, false, false, fixedStep); State(key + "/" + i + "/simulate", ps);
            var paused = ps.GetPlaybackState();
            ps.Play(false); State(key + "/" + i + "/resume", ps);
            Toggle(key + "/" + i + "/toggle", ps);
            var emission = ps.emission; emission.enabled = enabled;
            ps.SetPlaybackState(paused); State(key + "/" + i + "/restorePaused", ps);
            i++;
        }
        ps.Simulate(.125f, false, true, fixedStep); State(key + "/restartSimulate", ps);
        ps.Play(false); State(key + "/restartResume", ps);
        ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        ps.Simulate(3f, false, false, fixedStep); State(key + "/drained", ps);
        ps.Play(false); State(key + "/playDrained", ps);
        UnityEngine.Object.DestroyImmediate(ps.gameObject);
    }
    static void Hierarchy(bool withChildren, bool enabled)
    {
        string key = "hierarchy/" + B(withChildren) + "/" + B(enabled);
        var root = Make(enabled, 0, true, 0, false);
        var child = Make(!enabled, 0, true, 0, false); child.transform.SetParent(root.transform, false);
        var grandchild = Make(enabled, 0, false, .25f, true); grandchild.transform.SetParent(child.transform, false);
        Action<string> record = step => { State(key + "/" + step + "/root", root); State(key + "/" + step + "/child", child); State(key + "/" + step + "/grandchild", grandchild); };
        record("initial"); root.Play(withChildren); record("play");
        var emission = root.emission; emission.enabled = !enabled; record("rootToggle");
        root.Pause(withChildren); record("pause"); root.Play(withChildren); record("resume");
        child.Emit(1); record("childEmit"); root.Clear(withChildren); record("clear");
        root.Simulate(.5f, withChildren, true, false); record("simulate"); root.Play(withChildren); record("resumeSimulate");
        root.Stop(withChildren, ParticleSystemStopBehavior.StopEmitting); record("stop");
        root.Stop(withChildren, ParticleSystemStopBehavior.StopEmittingAndClear); record("stopClear");
        UnityEngine.Object.DestroyImmediate(root.gameObject);
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        foreach (bool enabled in new[] { false, true })
        foreach (int source in new[] { 0, 1, 2, 3 })
        foreach (bool loop in new[] { false, true })
        foreach (float delay in new[] { 0f, .25f })
        foreach (bool live in new[] { false, true }) {
            Controls(enabled, source, loop, delay, live);
            Simulation(enabled, source, loop, delay, live, false);
            Simulation(enabled, source, loop, delay, live, true);
        }
        foreach (bool withChildren in new[] { false, true })
        foreach (bool enabled in new[] { false, true }) Hierarchy(withChildren, enabled);
        File.WriteAllText(Environment.GetEnvironmentVariable("PARTICLE_EMISSION_OUT"), output.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
