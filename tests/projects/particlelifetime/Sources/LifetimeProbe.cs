using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using Unity.Collections;
using UnityEngine;

public class LifetimeProbe : MonoBehaviour
{
    static readonly StringBuilder output = new StringBuilder();
    static string F(float x) => x.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
    static string B(bool x) => x ? "true" : "false";
    static void Value(string key, string value) => output.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(value).Append("}\n");
    static ParticleSystem.Particle Particle(int id, float life, float start = 10) => new ParticleSystem.Particle {
        randomSeed = (uint)id, position = new Vector3(id, id + 1, -id),
        velocity = new Vector3(1, 2, 3), startSize = 1, startColor = Color.white,
        startLifetime = start, remainingLifetime = life
    };
    static ParticleSystem Make()
    {
        var go = new GameObject("lifetime"); go.SetActive(false);
        var ps = go.AddComponent<ParticleSystem>();
        ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
        var main = ps.main; main.playOnAwake = false; main.startLifetime = 10; main.startSpeed = 0;
        var emission = ps.emission; emission.enabled = false;
        var shape = ps.shape; shape.enabled = false;
        ps.useAutoRandomSeed = false; ps.randomSeed = 12345;
        go.SetActive(true);
        return ps;
    }
    static string Particles(ParticleSystem.Particle[] particles, int count)
    {
        var b = new StringBuilder("[");
        for (int i = 0; i < count; i++) {
            if (i != 0) b.Append(','); var p = particles[i];
            b.Append("{\"seed\":").Append(p.randomSeed).Append(",\"life\":").Append(F(p.remainingLifetime))
                .Append(",\"start\":").Append(F(p.startLifetime)).Append(",\"position\":").Append(V(p.position))
                .Append(",\"velocity\":").Append(V(p.velocity)).Append('}');
        }
        return b.Append(']').ToString();
    }
    static void State(string key, ParticleSystem ps)
    {
        var p = new ParticleSystem.Particle[16]; int n = ps.GetParticles(p);
        Value(key, "{\"count\":" + ps.particleCount + ",\"read\":" + n + ",\"playing\":" + B(ps.isPlaying)
            + ",\"emitting\":" + B(ps.isEmitting) + ",\"stopped\":" + B(ps.isStopped) + ",\"paused\":" + B(ps.isPaused)
            + ",\"time\":" + F(ps.time) + ",\"particles\":" + Particles(p, n) + "}");
        foreach (var stream in new[] { ParticleSystemCustomData.Custom1, ParticleSystemCustomData.Custom2 }) {
            var data = new List<Vector4>(); int count = ps.GetCustomParticleData(data, stream);
            var b = new StringBuilder("{\"count\":").Append(count).Append(",\"values\":[");
            for (int i = 0; i < data.Count; i++) {
                if (i != 0) b.Append(','); var v = data[i];
                b.Append('[').Append(F(v.x)).Append(',').Append(F(v.y)).Append(',').Append(F(v.z)).Append(',').Append(F(v.w)).Append(']');
            }
            Value(key + "/" + stream, b.Append("]}").ToString());
        }
    }
    static void Set(ParticleSystem ps, ParticleSystem.Particle[] p, int overload)
    {
        if (overload == 0) ps.SetParticles(p);
        if (overload == 1) ps.SetParticles(p, 4);
        if (overload == 2) ps.SetParticles(p, 4, 1);
        if (overload >= 3) using (var a = new NativeArray<ParticleSystem.Particle>(p, Allocator.Temp)) {
            if (overload == 3) ps.SetParticles(a);
            if (overload == 4) ps.SetParticles(a, 4);
            if (overload == 5) ps.SetParticles(a, 4, 1);
        }
    }
    static void Case(int overload, int state, int mask)
    {
        string key = "compact/" + overload + "/" + state + "/" + mask;
        var ps = Make(); var initial = new ParticleSystem.Particle[6];
        for (int i = 0; i < 6; i++) initial[i] = Particle(100 + i, 8);
        ps.SetParticles(initial);
        foreach (var stream in new[] { ParticleSystemCustomData.Custom1, ParticleSystemCustomData.Custom2 }) {
            var data = new List<Vector4>();
            for (int i = 0; i < 6; i++) data.Add(new Vector4(100 + i, (int)stream + 1, i, -i));
            ps.SetCustomParticleData(data, stream);
        }
        if (state != 0) ps.Play(false);
        if (state == 2) ps.Pause(false);
        State(key + "/before", ps);
        var input = new ParticleSystem.Particle[6];
        int last = overload % 3 == 0 ? 5 : 3;
        for (int i = 0; i < 6; i++) {
            bool remove = mask == 1 && i == 0 || mask == 2 && i == 2 || mask == 3 && i == last
                || mask == 4 && (i == 0 || i == 2 || i == last);
            input[i] = Particle(200 + i, remove ? -.01f : i == 1 ? 0 : 5);
        }
        Value(key + "/input", Particles(input, input.Length));
        Set(ps, input, overload);
        Value(key + "/inputAfter", Particles(input, input.Length));
        State(key + "/after", ps);
        var slice = new ParticleSystem.Particle[3];
        int offset = ps.particleCount > 1 ? 1 : 0;
        int n = ps.GetParticles(slice, 2, offset);
        Value(key + "/slice", "{\"count\":" + n + ",\"particles\":" + Particles(slice, slice.Length) + "}");
        using (var a = new NativeArray<ParticleSystem.Particle>(3, Allocator.Temp)) {
            n = ps.GetParticles(a, 2, offset);
            Value(key + "/nativeSlice", "{\"count\":" + n + ",\"particles\":" + Particles(a.ToArray(), a.Length) + "}");
        }
        var roundtrip = new ParticleSystem.Particle[ps.particleCount]; ps.GetParticles(roundtrip);
        ps.SetParticles(roundtrip); State(key + "/roundtrip", ps);
        DestroyImmediate(ps.gameObject);
    }
    static void Lifetimes()
    {
        foreach (float start in new[] { .001f, 1f, 10f, 1000000f })
        foreach (float life in new[] { -1f, -.01f, -1e-7f, -float.Epsilon, -0f, 0f, float.Epsilon, 1e-7f, .01f, 1f }) {
            string key = "life/" + F(start) + "/" + F(life) + "/" + BitConverter.ToInt32(BitConverter.GetBytes(life), 0);
            var ps = Make(); var input = new[] { Particle(321, life, start) };
            Value(key + "/input", Particles(input, 1));
            ps.SetParticles(input); State(key + "/after", ps);
            var roundtrip = new ParticleSystem.Particle[ps.particleCount]; ps.GetParticles(roundtrip);
            ps.SetParticles(roundtrip); State(key + "/roundtrip", ps);
            DestroyImmediate(ps.gameObject);
        }
    }
    static void RetainedPrefix()
    {
        var ps = Make();
        ps.SetParticles(new[] { Particle(400, float.Epsilon), Particle(401, 5), Particle(402, 5) });
        State("prefix/before", ps);
        ps.SetParticles(new[] { Particle(403, 5) }, 1, 2);
        State("prefix/after", ps);
        ps.SetParticles(new ParticleSystem.Particle[0], 0, 1);
        State("prefix/truncated", ps);
        var roundtrip = new ParticleSystem.Particle[ps.particleCount]; ps.GetParticles(roundtrip);
        ps.SetParticles(roundtrip); State("prefix/roundtrip", ps);
        DestroyImmediate(ps.gameObject);
    }
    static void DeletionMasks()
    {
        for (int offset = 0; offset <= 3; offset++)
        for (int count = 1; count <= 9; count++)
        for (int mask = 0; mask < 1 << count; mask++) {
            string key = offset == 0 ? "mask/" + count + "/" + mask : "maskOffset/" + offset + "/" + count + "/" + mask;
            var ps = Make(); var input = new ParticleSystem.Particle[count];
            for (int i = 0; i < count; i++) input[i] = Particle(500+i, 5);
            var initial = new ParticleSystem.Particle[offset+count+(offset == 0 ? 0 : 1)];
            for (int i = 0; i < initial.Length; i++) initial[i] = Particle(500+i, 5);
            ps.SetParticles(initial);
            foreach (var stream in new[] { ParticleSystemCustomData.Custom1, ParticleSystemCustomData.Custom2 }) {
                var custom = new List<Vector4>();
                for (int i = 0; i < initial.Length; i++) custom.Add(new Vector4(500+i, (int)stream+1, i, -i));
                ps.SetCustomParticleData(custom, stream);
            }
            State(key + "/before", ps);
            for (int i = 0; i < count; i++) if ((mask & 1 << i) != 0) input[i].remainingLifetime = -.01f;
            Value(key + "/input", Particles(input, count));
            if (offset == 0) ps.SetParticles(input);
            else ps.SetParticles(input, count, offset);
            State(key + "/after", ps);
            DestroyImmediate(ps.gameObject);
        }
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        for (int overload = 0; overload < 6; overload++)
        for (int state = 0; state < 3; state++)
        for (int mask = 0; mask < 5; mask++) Case(overload, state, mask);
        Lifetimes();
        RetainedPrefix();
        DeletionMasks();
        // The exact Emit witness has randomized particle identities; record only deterministic flags.
        EmitFlags();
        File.WriteAllText(Environment.GetEnvironmentVariable("PARTICLE_LIFETIME_OUT"), output.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
    static void EmitFlags()
    {
        var go = new GameObject("particles"); go.SetActive(false);
        var ps = go.AddComponent<ParticleSystem>();
        var main = ps.main; main.startLifetime = 10f; main.startSpeed = 0f;
        var emission = ps.emission; emission.rateOverTime = 0f;
        main.playOnAwake = false; main.startSpeed = 2f;
        var shape = ps.shape; shape.enabled = false;
        go.SetActive(true);
        Action<string> record = step => Value("emitExact/" + step, "{\"playing\":" + B(ps.isPlaying)
            + ",\"emitting\":" + B(ps.isEmitting) + ",\"stopped\":" + B(ps.isStopped) + ",\"count\":" + ps.particleCount + "}");
        record("before"); ps.Emit(3); record("after"); DestroyImmediate(go);
    }
}
