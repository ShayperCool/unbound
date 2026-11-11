using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Playables;

/// <summary>
/// Profile "director" (ANIMSTARTUP_DIRECTOR=1): the clock of a PlayableDirector at Play entry.
/// Directors over <see cref="StartupDirectorAsset"/> are created in BeforeSceneLoad,
/// AfterSceneLoad, Start, the first, the second and a later Update: played, playing on awake,
/// played from an initial time, played and moved to a time, played and evaluated, on unscaled
/// game time and on a manual clock; beside them a plain game-time graph of the same playable
/// ("graph", played before the evaluated director of its group). Each is recorded before the director update (Update) and
/// after it (LateUpdate) for nine Updates: the director's time and state, its root playable's
/// time, the behaviour's callback counts and its last prepared frame. No order between
/// different directors is asserted.
/// </summary>
public class AnimStartupDirectorProbe : MonoBehaviour
{
    sealed class Case
    {
        public string name;
        public PlayableDirector director;
        public PlayableGraph graph;
    }
    static readonly List<Case> cases = new List<Case>();
    static readonly StringBuilder records = new StringBuilder();
    static string Output => Environment.GetEnvironmentVariable("ANIMSTARTUP_PROBE_OUT");
    static bool Recording => Output != null && Application.isPlaying
        && Environment.GetEnvironmentVariable("ANIMSTARTUP_DIRECTOR") == "1";
    int updates;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void BeforeScene()
    {
        if (!Recording) return;
        cases.Clear();
        records.Clear();
        Time.captureDeltaTime = 0.02f;
        Group("beforeScene");
        new GameObject("DirectorProbe").AddComponent<AnimStartupDirectorProbe>();
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AfterScene() { if (Recording) Group("afterScene"); }

    void Start()
    {
        if (!Recording) return;
        Group("start");
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
    }

    static Case Create(string name, bool active)
    {
        var go = new GameObject(name);
        go.SetActive(active);
        var director = go.AddComponent<PlayableDirector>();
        director.playOnAwake = false;
        director.extrapolationMode = DirectorWrapMode.Hold;
        director.playableAsset = ScriptableObject.CreateInstance<StartupDirectorAsset>();
        var c = new Case { name = name, director = director };
        cases.Add(c);
        return c;
    }

    static void Group(string phase)
    {
        foreach (string control in new[] { "play", "awake", "initial", "seek", "graph", "evaluate", "unscaled", "manual" })
        {
            if (control == "graph")
            {
                var plain = new Case { name = phase + "/graph", graph = PlayableGraph.Create(phase + "/graph") };
                cases.Add(plain);
                plain.graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
                var playable = ScriptPlayable<StartupDirectorBehaviour>.Create(plain.graph);
                ScriptPlayableOutput.Create(plain.graph, "probe").SetSourcePlayable(playable);
                Snapshot(plain, "created");
                plain.graph.Play();
                Snapshot(plain, "prepared");
                continue;
            }
            var c = Create(phase + "/" + control, control != "awake");
            var d = c.director;
            Snapshot(c, "created");
            switch (control)
            {
                case "play": d.Play(); break;
                case "awake": d.playOnAwake = true; d.gameObject.SetActive(true); break;
                case "initial": d.initialTime = 0.25; d.Play(); break;
                case "seek": d.Play(); d.time = 0.25; break;
                case "evaluate": d.Play(); d.Evaluate(); break;
                case "unscaled": d.timeUpdateMode = DirectorUpdateMode.UnscaledGameTime; d.Play(); break;
                case "manual": d.timeUpdateMode = DirectorUpdateMode.Manual; d.Play(); break;
            }
            Snapshot(c, "prepared");
        }
    }

    void Update()
    {
        if (!Recording) return;
        if (updates == 0) Group("firstUpdate");
        if (updates == 1) Group("secondUpdate");
        if (updates == 3) Group("laterUpdate");
        foreach (var c in cases)
        {
            // Normalize the unscaled control to 0.02 seconds a frame despite wall-clock Editor pacing.
            var graph = c.director != null ? c.director.playableGraph : c.graph;
            if (c.director != null && c.director.timeUpdateMode == DirectorUpdateMode.UnscaledGameTime && Time.unscaledDeltaTime > 0f && graph.IsValid())
                graph.GetRootPlayable(0).SetSpeed(0.02 / Time.unscaledDeltaTime);
            Snapshot(c, "update/" + updates);
        }
    }

    void LateUpdate()
    {
        if (!Recording) return;
        foreach (var c in cases) Snapshot(c, "late/" + updates);
        if (++updates < 9) return;
        Put("complete", "true");
        Directory.CreateDirectory(Output);
        File.WriteAllText(Path.Combine(Output, "values.jsonl"), records.ToString());
        if (AnimStartupProbe.finished != null) AnimStartupProbe.finished(0);
        else UnityEditor.EditorApplication.Exit(0);
    }

    // Seven decimals: the unscaled control's products of a wall-clock delta and its inverse
    // differ in their last digits between runs.
    static string D(double v) => Math.Round(v, 7).ToString("R", CultureInfo.InvariantCulture);
    static void Put(string key, string value) => records.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(value).Append("}\n");

    static void Snapshot(Case c, string phase)
    {
        var d = c.director;
        var graph = d != null ? d.playableGraph : c.graph;
        double root = -1.0;
        // The unscaled control's frame delta is the wall clock's: only its sign is recorded.
        bool unscaled = d != null && d.timeUpdateMode == DirectorUpdateMode.UnscaledGameTime;
        StartupDirectorBehaviour b = null;
        if (graph.IsValid() && graph.GetRootPlayableCount() > 0)
        {
            var playable = graph.GetRootPlayable(0);
            root = playable.GetTime();
            b = ((ScriptPlayable<StartupDirectorBehaviour>)playable).GetBehaviour();
        }
        Put(c.name + "/" + phase, "{\"time\":" + D(d != null ? d.time : root) + ",\"state\":\""
            + (d != null ? d.state : graph.IsPlaying() ? PlayState.Playing : PlayState.Paused) + "\",\"root\":" + D(root)
            + ",\"starts\":" + (b == null ? -1 : b.starts) + ",\"plays\":" + (b == null ? -1 : b.plays)
            + ",\"prepares\":" + (b == null ? -1 : b.prepares) + ",\"processes\":" + (b == null ? -1 : b.processes)
            + ",\"delta\":" + D(b == null ? -1.0 : unscaled ? Math.Sign(b.delta) : b.delta) + ",\"at\":" + D(b == null ? -1.0 : b.time)
            + ",\"evaluation\":" + (b == null ? -1 : b.evaluation) + "}");
    }
}
