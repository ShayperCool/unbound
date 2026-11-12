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
    static void Playback(bool enabled, int savedState, int targetState)
    {
        string key = "playback/" + B(enabled) + "/" + savedState + "/" + targetState;
        var ps = Make(enabled, 0, false, 0, false);
        Action<int> setup = state => {
            ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (state > 0) { ps.Play(false); ps.time = .375f; }
            if (state == 2) ps.Pause(false);
            if (state == 3) { ps.Emit(1); ps.Stop(false, ParticleSystemStopBehavior.StopEmitting); }
        };
        setup(savedState); State(key + "/saved", ps);
        var saved = ps.GetPlaybackState();
        setup(targetState); State(key + "/target", ps);
        ps.SetPlaybackState(saved); State(key + "/restored", ps);
        ps.Play(false); State(key + "/play", ps);
        ps.Pause(false); State(key + "/pause", ps);
        ps.Stop(false, ParticleSystemStopBehavior.StopEmitting); State(key + "/stopPaused", ps);
        ps.Play(false); State(key + "/resumeStoppedPaused", ps);
        var emission = ps.emission;
        foreach (float rate in new[] { 0f, 4f, 4f, 0f }) {
            emission.rateOverTime = rate; State(key + "/rate/" + toggleIndex++, ps);
        }
        UnityEngine.Object.DestroyImmediate(ps.gameObject);
    }
    // Observe emitted ages as well as counts on both sides of a two-second lifetime.
    // Keep these controls separate so the original matrix remains byte-for-byte comparable.
    static void ParticleState(string key, ParticleSystem ps)
    {
        State(key, ps);
        var particles = new ParticleSystem.Particle[ps.particleCount];
        int count = ps.GetParticles(particles);
        output.Append("{\"k\":\"").Append(key).Append("/particles\",\"v\":[");
        for (int i = 0; i < count; i++) {
            if (i > 0) output.Append(',');
            output.Append("{\"seed\":").Append(particles[i].randomSeed)
                .Append(",\"life\":").Append(F(particles[i].remainingLifetime))
                .Append(",\"start\":").Append(F(particles[i].startLifetime)).Append('}');
        }
        output.Append("]}\n");
    }
    static void Boundaries()
    {
        foreach (int source in new[] { 1, 3 })
        foreach (bool loop in new[] { false, true })
        foreach (float delay in new[] { 0f, .25f })
        foreach (bool fixedStep in new[] { false, true })
        foreach (bool restore in new[] { false, true })
        foreach (bool split in new[] { false, true }) {
            string key = "boundary/" + source + "/" + B(loop) + "/" + F(delay) + "/" + B(fixedStep) + "/" + B(restore) + "/" + B(split);
            var ps = Make(true, source, loop, delay, false);
            ps.Play(false);
            int step = 0;
            foreach (float dt in split ? new[] { 0f, .125f, .125f, .25f, .5f, .25f, .9999f, .0001f, .0001f, .02f }
                                      : new[] { 0f, .125f, .125f, .25f, .5f, .25f, 1f, .0001f, .02f }) {
                ps.Simulate(dt, false, false, fixedStep);
                ParticleState(key + "/" + step++, ps);
                var saved = ps.GetPlaybackState();
                ps.Play(false);
                if (restore) ps.SetPlaybackState(saved);
            }
            UnityEngine.Object.DestroyImmediate(ps.gameObject);
        }
        // Identical setup to the inherited rate unit test and the broad long-call control.
        foreach (bool restart in new[] { false, true }) {
            var ps = Make(true, 1, true, 0, false);
            var main = ps.main; main.duration = 5; main.startLifetime = 10;
            var emission = ps.emission; emission.rateOverTime = 20;
            ps.Play(false);
            for (int step = 0; step < 75; step++) {
                ps.Simulate(.02f, false, false, false); ps.Play(false);
                ParticleState("rateSteps/" + B(restart) + "/" + step, ps);
            }
            UnityEngine.Object.DestroyImmediate(ps.gameObject);
            ps = Make(true, 1, true, 0, false);
            main = ps.main; main.duration = 5; main.startLifetime = 1.013f;
            emission = ps.emission; emission.rateOverTime = 20;
            foreach (float dt in new[] { .3f, .7f }) {
                ps.Simulate(dt, false, restart, false);
                ParticleState("longCall/" + B(restart) + "/" + F(dt), ps);
            }
            UnityEngine.Object.DestroyImmediate(ps.gameObject);
        }
    }
    static void RateUnitInputs()
    {
        // Match the inherited unit fixture, including its default shape and play-on-awake.
        var go = new GameObject("particles"); go.SetActive(false);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main; main.startLifetime = 10; main.startSpeed = 0;
        var emission = ps.emission; emission.rateOverTime = 20;
        go.SetActive(true);
        for (int step = 0; step < 75; step++) {
            ps.Simulate(.02f, false, false, false); ps.Play(false);
            State("rateUnit/" + step, ps);
        }
        UnityEngine.Object.DestroyImmediate(go);
    }
    static void SubdivisionControl(float maximum, float dt)
    {
        Time.maximumParticleDeltaTime = maximum;
        var ps = Make(false, 0, false, 0, false);
        var main = ps.main; main.duration = 10; main.startLifetime = 10; main.gravityModifier = 1;
        ps.Emit(new ParticleSystem.EmitParams { velocity = new Vector3(1, 0, 0), startLifetime = 10, randomSeed = 42 }, 1);
        ps.Simulate(dt, false, false, false);
        string key = "subdivision/" + F(maximum) + "/" + F(dt);
        State(key, ps);
        var particles = new ParticleSystem.Particle[1]; ps.GetParticles(particles);
        var p = particles[0]; var position = p.position; var velocity = p.velocity;
        output.Append("{\"k\":\"").Append(key).Append("/physical\",\"v\":{\"position\":[")
            .Append(F(position.x)).Append(',').Append(F(position.y)).Append(',').Append(F(position.z))
            .Append("],\"velocity\":[").Append(F(velocity.x)).Append(',').Append(F(velocity.y)).Append(',').Append(F(velocity.z))
            .Append("],\"life\":").Append(F(p.remainingLifetime)).Append("}}\n");
        UnityEngine.Object.DestroyImmediate(ps.gameObject);
    }
    static void SubdivisionControls()
    {
        float saved = Time.maximumParticleDeltaTime;
        foreach (float maximum in new[] { .02f, .03f, .05f }) {
            foreach (float dt in new[] { 0f, 1e-8f, 1e-7f, 5e-7f, 9.999999e-7f, 1e-6f, 1.0000001e-6f, 2e-6f, 5e-6f, 9.999999e-6f, 1e-5f, 1.0000001e-5f, 2e-5f, 5e-5f, 9.999999e-5f, 1e-4f, 1.0000001e-4f, .001f, .03f, .05f, .1f, .125f, .25f, .3f, .5f, .7f, 1f, 2f, 3f })
                SubdivisionControl(maximum, dt);
            foreach (int center in new[] { 0x3f800000, 0x40000000, 0x40400000 })
            for (int offset = -16; offset <= 16; offset++) {
                if (offset == 0) continue;
                float dt = BitConverter.ToSingle(BitConverter.GetBytes(center + offset), 0);
                SubdivisionControl(maximum, dt);
            }
        }
        Time.maximumParticleDeltaTime = saved;
    }
    static void RateBoundaries()
    {
        float saved = Time.maximumParticleDeltaTime;
        Time.maximumParticleDeltaTime = 1;
        foreach (float rate in new[] { 4f, 10f, 20f }) {
            var ps = Make(true, 1, true, 0, false);
            var main = ps.main; main.duration = 5; main.startLifetime = 10;
            var emission = ps.emission; emission.rateOverTime = rate;
            ps.Play(false);
            for (int i = 0; i < 12; i++) {
                ps.Simulate(1f / rate, false, false, false);
                ParticleState("rateBoundary/" + F(rate) + "/" + i, ps);
            }
            UnityEngine.Object.DestroyImmediate(ps.gameObject);
        }
        Time.maximumParticleDeltaTime = saved;
        foreach (bool write in new[] { false, true }) {
            var ps = Make(true, 1, true, 0, false);
            var main = ps.main; main.duration = 5; main.startLifetime = 1.013f; main.startSpeed = 2;
            var emission = ps.emission; emission.rateOverTime = 20;
            ps.Simulate(.3f, false, true, false);
            ParticleState("rateContinuation/" + B(write) + "/initial", ps);
            if (write) {
                var particles = new ParticleSystem.Particle[ps.particleCount]; int count = ps.GetParticles(particles);
                for (int i = 0; i < count; i++) {
                    particles[i].position += Vector3.up;
                    particles[i].velocity = Vector3.right;
                    if (i == 0) particles[i].remainingLifetime = -1;
                }
                ps.SetParticles(particles, count);
            }
            ParticleState("rateContinuation/" + B(write) + "/written", ps);
            for (int i = 0; i < 3; i++) {
                ps.Simulate(.1f, false, false, false);
                ParticleState("rateContinuation/" + B(write) + "/" + i, ps);
            }
            UnityEngine.Object.DestroyImmediate(ps.gameObject);
        }
    }
    static void RateAccumulation()
    {
        float saved = Time.maximumParticleDeltaTime;
        foreach (float maximum in new[] { .03f, 1f }) {
            Time.maximumParticleDeltaTime = maximum;
            foreach (float rate in new[] { 4f, 10f, 20f }) {
                var ps = Make(true, 1, true, 0, false);
                var main = ps.main; main.duration = 5; main.startLifetime = 10;
                var emission = ps.emission; emission.rateOverTime = rate;
                ps.Play(false);
                for (int i = 0; i < 12; i++) {
                    ps.Simulate(1f / rate, false, false, false);
                    ParticleState("rateStepping/" + F(maximum) + "/" + F(rate) + "/" + i, ps);
                }
                UnityEngine.Object.DestroyImmediate(ps.gameObject);
            }
            foreach (float duration in new[] { .3f, 1f })
            foreach (float rate in new[] { 4f, 10f, 20f, 9.7f }) {
                var ps = Make(true, 1, true, 0, false);
                var main = ps.main; main.duration = duration; main.startLifetime = 10;
                var emission = ps.emission; emission.rateOverTime = rate;
                ps.Play(false);
                for (int i = 0; i < 12; i++) {
                    ps.Simulate(.1f, false, false, false);
                    ParticleState("rateLoops/" + F(maximum) + "/" + F(duration) + "/" + F(rate) + "/" + i, ps);
                }
                UnityEngine.Object.DestroyImmediate(ps.gameObject);
            }
        }
        Time.maximumParticleDeltaTime = saved;
    }
    // Which earlier state makes a non-restarting Simulate advance without emitting: a fresh
    // system, Stop with either behaviour before or after it ever played, and Stop while the
    // GameObject is inactive or active. Play then shows what the stopped state kept.
    static void StoppedSimulation(int mode, int source, float delay)
    {
        string key = "stoppedSimulate/" + mode + "/" + source + "/" + F(delay);
        var go = new GameObject("emission-stopped"); go.SetActive(false);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main; main.playOnAwake = false; main.duration = 1; main.loop = true;
        main.startDelay = delay; main.startLifetime = 2; main.startSpeed = 0;
        var shape = ps.shape; shape.enabled = false;
        var emission = ps.emission; emission.enabled = true;
        emission.rateOverTime = source == 1 ? 10 : 0;
        if (source == 3) emission.SetBursts(new[] { new ParticleSystem.Burst(.25f, (short)2) });
        ps.useAutoRandomSeed = false; ps.randomSeed = 12345;
        if (mode == 1) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        go.SetActive(true);
        if (mode == 2) ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        if (mode == 3) ps.Stop(false, ParticleSystemStopBehavior.StopEmitting);
        if (mode == 4) { ps.Play(false); ps.Simulate(.1f, false, false, false); ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); }
        if (mode == 5) { ps.Play(false); ps.Stop(false, ParticleSystemStopBehavior.StopEmitting); }
        if (mode == 6) { ps.Play(false); ps.Simulate(.6f, false, false, false); ps.Play(false); ps.Stop(false, ParticleSystemStopBehavior.StopEmitting); }
        if (mode == 7) { ps.Play(false); ps.Pause(false); ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear); }
        ParticleState(key + "/before", ps);
        ps.Simulate(.3f, false, false, false); ParticleState(key + "/first", ps);
        ps.Simulate(.7f, false, false, false); ParticleState(key + "/second", ps);
        ps.Play(false); ParticleState(key + "/play", ps);
        ps.Simulate(.3f, false, false, false); ParticleState(key + "/afterPlay", ps);
        UnityEngine.Object.DestroyImmediate(go);
    }
    // Emission resumed by Play after a stopped clock, with and without crossing the loop end.
    static void StoppedResume(bool played, float stopped, float resume)
    {
        string key = "stoppedResume/" + B(played) + "/" + F(stopped) + "/" + F(resume);
        var go = new GameObject("emission-resume"); go.SetActive(false);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main; main.playOnAwake = false; main.duration = 1; main.loop = true;
        main.startLifetime = 5; main.startSpeed = 0;
        var shape = ps.shape; shape.enabled = false;
        var emission = ps.emission; emission.rateOverTime = 10;
        ps.useAutoRandomSeed = false; ps.randomSeed = 12345;
        go.SetActive(true);
        if (played) ps.Play(false);
        ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.Simulate(stopped, false, false, false);
        ps.Play(false);
        ps.Simulate(resume, false, false, false); ParticleState(key, ps);
        ps.Simulate(resume, false, false, false); ParticleState(key + "/again", ps);
        UnityEngine.Object.DestroyImmediate(go);
    }
    // A stopped step longer than the start delay left; a burst passed while the module is off.
    static void StoppedLongSteps(float maximum, float delay)
    {
        float saved = Time.maximumParticleDeltaTime;
        Time.maximumParticleDeltaTime = maximum;
        string key = "stoppedLongStep/" + F(maximum) + "/" + F(delay);
        var go = new GameObject("emission-long"); go.SetActive(false);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main; main.playOnAwake = false; main.duration = 1; main.loop = true;
        main.startDelay = delay; main.startLifetime = 5; main.startSpeed = 0;
        var shape = ps.shape; shape.enabled = false;
        var emission = ps.emission; emission.rateOverTime = 10;
        ps.useAutoRandomSeed = false; ps.randomSeed = 12345;
        go.SetActive(true);
        ps.Play(false); ps.Stop(false, ParticleSystemStopBehavior.StopEmittingAndClear);
        ps.Simulate(.3f, false, false, false); ParticleState(key + "/first", ps);
        ps.Simulate(.3f, false, false, false); ParticleState(key + "/second", ps);
        ps.Play(false);
        ps.Simulate(.3f, false, false, false); ParticleState(key + "/afterPlay", ps);
        ps.Simulate(.3f, false, false, false); ParticleState(key + "/afterPlay2", ps);
        ps.Simulate(.016f, false, false, false); ParticleState(key + "/afterPlay3", ps);
        for (int i = 0; i < 5; i++) ps.Simulate(.016f, false, false, false);
        ParticleState(key + "/afterPlay4", ps);
        UnityEngine.Object.DestroyImmediate(go);
        Time.maximumParticleDeltaTime = saved;
    }
    static void DisabledBurst(bool repeat)
    {
        string key = "disabledBurst/" + B(repeat);
        var go = new GameObject("emission-burst"); go.SetActive(false);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main; main.playOnAwake = false; main.duration = 1; main.loop = true;
        main.startLifetime = 5; main.startSpeed = 0;
        var shape = ps.shape; shape.enabled = false;
        var emission = ps.emission; emission.rateOverTime = 0;
        var burst = new ParticleSystem.Burst(.25f, (short)2);
        if (repeat) { burst.cycleCount = 3; burst.repeatInterval = .2f; }
        emission.SetBursts(new[] { burst });
        ps.useAutoRandomSeed = false; ps.randomSeed = 12345;
        go.SetActive(true);
        ps.Play(false);
        ps.Simulate(.1f, false, false, false); ParticleState(key + "/before", ps);
        emission.enabled = false;
        ps.Simulate(.3f, false, false, false); ParticleState(key + "/disabled", ps);
        emission.enabled = true;
        ps.Simulate(.3f, false, false, false); ParticleState(key + "/enabled", ps);
        ps.Simulate(.7f, false, false, false); ParticleState(key + "/nextLoop", ps);
        UnityEngine.Object.DestroyImmediate(go);
    }
    // Fixed-step and variable-step calls in either order: what the first leaves unstepped.
    static void MixedRemainder(bool fixedFirst, float first, float second, bool fresh)
    {
        string key = "mixedRemainder/" + B(fixedFirst) + "/" + F(first) + "/" + F(second) + "/" + B(fresh);
        var go = new GameObject("emission-mixed"); go.SetActive(false);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main; main.playOnAwake = false; main.duration = 1; main.loop = true;
        main.startDelay = fresh ? .1f : 0; main.startLifetime = 5; main.startSpeed = 0;
        var shape = ps.shape; shape.enabled = false;
        var emission = ps.emission; emission.rateOverTime = 10;
        ps.useAutoRandomSeed = false; ps.randomSeed = 12345;
        go.SetActive(true);
        if (fresh) {
            // A fresh playback whose births fall in the step that ends its start delay.
            float saved = Time.maximumParticleDeltaTime;
            Time.maximumParticleDeltaTime = .5f;
            ps.Play(false);
            ps.Simulate(first, false, false, false); ParticleState(key + "/first", ps);
            Time.maximumParticleDeltaTime = saved;
        } else {
            ps.Play(false);
            ps.Simulate(first, false, false, fixedFirst); ParticleState(key + "/first", ps);
            ps.Simulate(second, false, false, !fixedFirst); ParticleState(key + "/second", ps);
            ps.Play(false);
            ps.Simulate(second, false, false, fixedFirst); ParticleState(key + "/third", ps);
        }
        UnityEngine.Object.DestroyImmediate(go);
    }
    // A never-played Birth sub-emitter stopped with its parent, then a non-restarting Simulate.
    static void StoppedChild(bool withChildren, bool stopClear)
    {
        string key = "stoppedChild/" + B(withChildren) + "/" + B(stopClear);
        var parent = Make(true, 1, true, 0, false);
        var child = Make(true, 1, true, 0, false); child.transform.SetParent(parent.transform, false);
        var sub = parent.subEmitters; sub.enabled = true;
        sub.AddSubEmitter(child, ParticleSystemSubEmitterType.Birth, ParticleSystemSubEmitterProperties.InheritNothing);
        var childMain = child.main; childMain.startDelay = .1f;
        parent.Stop(true, stopClear ? ParticleSystemStopBehavior.StopEmittingAndClear : ParticleSystemStopBehavior.StopEmitting);
        parent.Simulate(.3f, withChildren, false, false);
        ParticleState(key + "/parent", parent); ParticleState(key + "/child", child);
        parent.Play(true);
        parent.Simulate(.3f, withChildren, false, false);
        ParticleState(key + "/playParent", parent); ParticleState(key + "/playChild", child);
        UnityEngine.Object.DestroyImmediate(parent.gameObject);
    }
    static void StoppedSimulations()
    {
        for (int mode = 0; mode < 8; mode++) {
            StoppedSimulation(mode, 1, 0);
            StoppedSimulation(mode, 1, .25f);
            StoppedSimulation(mode, 3, 0);
        }
        foreach (bool played in new[] { false, true })
        foreach (float stopped in new[] { .3f, .6f, .95f, 1f, 1.3f })
        foreach (float resume in new[] { .1f, .3f, .5f, .7f })
            StoppedResume(played, stopped, resume);
        foreach (float maximum in new[] { .03f, .5f })
        foreach (float delay in new[] { .1f, .25f, .4f })
            StoppedLongSteps(maximum, delay);
        DisabledBurst(false);
        DisabledBurst(true);
        foreach (bool fixedFirst in new[] { false, true })
        foreach (float first in new[] { .25f, .05f, .3f })
        foreach (float second in new[] { .3f, .016f })
            MixedRemainder(fixedFirst, first, second, false);
        foreach (float first in new[] { .3f, .25f, .5f })
            MixedRemainder(false, first, 0, true);
        foreach (bool withChildren in new[] { false, true })
        foreach (bool stopClear in new[] { false, true })
            StoppedChild(withChildren, stopClear);
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
        foreach (bool enabled in new[] { false, true })
        for (int saved = 0; saved < 4; saved++)
        for (int target = 0; target < 4; target++) Playback(enabled, saved, target);
        Boundaries();
        RateUnitInputs();
        SubdivisionControls();
        RateBoundaries();
        RateAccumulation();
        StoppedSimulations();
        File.WriteAllText(Environment.GetEnvironmentVariable("PARTICLE_EMISSION_OUT"), output.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
