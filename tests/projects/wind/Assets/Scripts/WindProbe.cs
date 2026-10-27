using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>
/// How wind zones move particles (the External Forces module): for each scenario a particle
/// system (no gravity, no emission of its own) gets particles at rest at known places, a wind
/// zone, and is simulated in fixed steps; the particles' velocities and positions are recorded
/// after each step. Also the WindZone's defaults. Writes <c>wind.jsonl</c> into
/// <c>WIND_PROBE_OUT</c>, then quits.
/// </summary>
public class WindProbe : MonoBehaviour
{
    readonly StringBuilder m_Out = new StringBuilder();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("WindProbe").AddComponent<WindProbe>();

    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => $"[{F(v.x)},{F(v.y)},{F(v.z)}]";
    void Record(string kind, string body) => m_Out.Append("{\"case\":\"").Append(kind).Append("\",").Append(body).Append("}\n");

    struct Scenario
    {
        public string Name;
        public WindZoneMode Mode;
        public float Main, Turbulence, PulseMagnitude, PulseFrequency, Radius;
        public Vector3 ZonePosition, ZoneEuler;
        public float Multiplier;
        public bool Enabled;
        public ParticleSystemGameObjectFilter Filter;
        public bool WorldSpace;
    }

    IEnumerator Start()
    {
        var dir = System.Environment.GetEnvironmentVariable("WIND_PROBE_OUT") ?? "/tmp/wind";
        Directory.CreateDirectory(dir);
        var defaults = new GameObject("defaults").AddComponent<WindZone>();
        Record("defaults", $"\"mode\":\"{defaults.mode}\",\"radius\":{F(defaults.radius)},\"main\":{F(defaults.windMain)},\"turbulence\":{F(defaults.windTurbulence)},\"pulseMagnitude\":{F(defaults.windPulseMagnitude)},\"pulseFrequency\":{F(defaults.windPulseFrequency)}");
        Destroy(defaults.gameObject);
        yield return null;

        var basic = new Scenario { Mode = WindZoneMode.Directional, Main = 1f, Radius = 20f, Multiplier = 1f, Enabled = true, Filter = ParticleSystemGameObjectFilter.LayerMask, WorldSpace = true };
        var scenarios = new List<Scenario>();
        void Add(string name, System.Func<Scenario, Scenario> change) { var s = change(basic); s.Name = name; scenarios.Add(s); }
        Add("directional", s => s);
        Add("directional main 2.5", s => { s.Main = 2.5f; return s; });
        Add("directional turned", s => { s.ZoneEuler = new Vector3(30f, 60f, 0f); return s; });
        Add("directional far away", s => { s.ZonePosition = new Vector3(100f, 0f, 0f); return s; });
        Add("multiplier 3", s => { s.Multiplier = 3f; return s; });
        Add("module off", s => { s.Enabled = false; return s; });
        Add("filter list", s => { s.Filter = ParticleSystemGameObjectFilter.List; return s; });
        Add("local space", s => { s.WorldSpace = false; s.ZoneEuler = new Vector3(0f, 90f, 0f); return s; });
        Add("spherical", s => { s.Mode = WindZoneMode.Spherical; s.Radius = 5f; s.ZonePosition = new Vector3(-1f, 0f, 0f); return s; });
        Add("spherical main 2", s => { s.Mode = WindZoneMode.Spherical; s.Radius = 8f; s.Main = 2f; s.ZonePosition = new Vector3(0f, -2f, 0f); return s; });
        Add("turbulence", s => { s.Main = 0f; s.Turbulence = 1f; return s; });
        Add("turbulence and main", s => { s.Main = 1f; s.Turbulence = 0.5f; return s; });
        Add("pulse", s => { s.PulseMagnitude = 0.5f; s.PulseFrequency = 0.25f; return s; });
        Add("pulse fast", s => { s.PulseMagnitude = 1f; s.PulseFrequency = 2f; return s; });

        var places = new[] { new Vector3(0f, 0f, 0f), new Vector3(2f, 0f, 0f), new Vector3(0f, 3f, 1f), new Vector3(-4f, 1f, 6f), new Vector3(6f, 0f, 0f) };
        foreach (var s in scenarios)
        {
            var zoneObject = new GameObject("zone");
            zoneObject.transform.position = s.ZonePosition;
            zoneObject.transform.eulerAngles = s.ZoneEuler;
            var zone = zoneObject.AddComponent<WindZone>();
            zone.mode = s.Mode;
            zone.windMain = s.Main;
            zone.windTurbulence = s.Turbulence;
            zone.windPulseMagnitude = s.PulseMagnitude;
            zone.windPulseFrequency = s.PulseFrequency;
            zone.radius = s.Radius;

            var go = new GameObject("system");
            go.transform.eulerAngles = s.WorldSpace ? Vector3.zero : new Vector3(0f, 45f, 0f);
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.startLifetime = 100f;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            main.simulationSpace = s.WorldSpace ? ParticleSystemSimulationSpace.World : ParticleSystemSimulationSpace.Local;
            main.maxParticles = 100;
            var emission = ps.emission;
            emission.enabled = false;
            var external = ps.externalForces;
            external.enabled = s.Enabled;
            external.multiplier = s.Multiplier;
            external.influenceFilter = s.Filter;
            foreach (var place in places)
                ps.Emit(new ParticleSystem.EmitParams { position = place, velocity = Vector3.zero, applyShapeToPosition = false }, 1);
            var steps = new List<string>();
            var buffer = new ParticleSystem.Particle[16];
            for (int step = 0; step < 5; step++)
            {
                ps.Simulate(0.1f, true, false, false);
                int n = ps.GetParticles(buffer);
                steps.Add("[" + string.Join(",", Enumerable.Range(0, n).Select(i => $"[{V(buffer[i].velocity)},{V(buffer[i].position)}]")) + "]");
            }
            Record("scenario", $"\"name\":\"{s.Name}\",\"steps\":[{string.Join(",", steps)}]");
            Destroy(go);
            Destroy(zoneObject);
            yield return null;
        }

        // The pulse over time: a directional zone (main 1) and one particle, sampled once Time.time
        // passes each mark.
        var zones = new List<(WindZone zone, ParticleSystem ps, List<string> samples)>();
        foreach (var (magnitude, frequency) in new[] { (1f, 0.25f), (1f, 1f), (1f, 0.05f) })
        {
            var zoneObject = new GameObject("pulse zone");
            var zone = zoneObject.AddComponent<WindZone>();
            zone.windMain = 1f;
            zone.windTurbulence = 0f;
            zone.windPulseMagnitude = magnitude;
            zone.windPulseFrequency = frequency;
            var go = new GameObject("pulse system");
            var ps = go.AddComponent<ParticleSystem>();
            ps.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            var main = ps.main;
            main.playOnAwake = false;
            main.startLifetime = 1000f;
            main.startSpeed = 0f;
            main.gravityModifier = 0f;
            main.simulationSpace = ParticleSystemSimulationSpace.World;
            var emission = ps.emission;
            emission.enabled = false;
            var external = ps.externalForces;
            external.enabled = true;
            // Each system feels its own zone alone (layers 8, 9, 10).
            zoneObject.layer = 8 + zones.Count;
            external.influenceMask = 1 << zoneObject.layer;
            zones.Add((zone, ps, new List<string>()));
        }
        var one = new ParticleSystem.Particle[4];
        float next = Time.time;
        while (Time.time < 10f)
        {
            yield return null;
            if (Time.time < next) continue;
            next = Time.time + 0.1f;
            foreach (var (zone, ps, samples) in zones)
            {
                ps.Clear();
                ps.Emit(new ParticleSystem.EmitParams { position = Vector3.zero, velocity = Vector3.zero, applyShapeToPosition = false }, 1);
                ps.Simulate(0.1f, true, false, false);
                ps.GetParticles(one);
                samples.Add($"[{F(Time.time)},{F(one[0].velocity.z * 10f)}]");
            }
        }
        foreach (var (zone, ps, samples) in zones)
            Record("pulse", $"\"magnitude\":{F(zone.windPulseMagnitude)},\"frequency\":{F(zone.windPulseFrequency)},\"samples\":[{string.Join(",", samples)}]");

        File.WriteAllText(Path.Combine(dir, "wind.jsonl"), m_Out.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }
}
