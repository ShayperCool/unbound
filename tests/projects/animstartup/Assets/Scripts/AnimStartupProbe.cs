using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Per-rig snapshots do not compare ordering between different scripts or Animators.
public class AnimStartupProbe : MonoBehaviour
{
    public static Action<int> finished;
    public Animator sceneEnabled, sceneDisabled;
    sealed class Case
    {
        public string name;
        public Animator animator;
        public StartupReceiver receiver;
        public Transform marker;
        public PlayableGraph graph;
        public AnimationClipPlayable clip;
    }
    static readonly List<Case> cases = new List<Case>();
    static readonly StringBuilder records = new StringBuilder();
    static string Output => Environment.GetEnvironmentVariable("ANIMSTARTUP_PROBE_OUT");
    static bool Recording => Output != null && Application.isPlaying
        && Environment.GetEnvironmentVariable("ANIMSTARTUP_HUMAN") != "1";
    int updates;
    static bool Review => Environment.GetEnvironmentVariable("ANIMSTARTUP_REVIEW") == "1";
    static bool Delayed => Environment.GetEnvironmentVariable("ANIMSTARTUP_DELAYED") == "1";

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void BeforeScene()
    {
        if (!Recording) return;
        cases.Clear();
        records.Clear();
        Time.captureDeltaTime = 0.02f;
        if (Review) InitialScene("beforeScene");
        if (!Delayed) Group("beforeScene");
    }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AfterScene() { if (Recording) { if (Review) InitialScene("afterScene"); if (!Delayed) Group("afterScene"); } }

    static void InitialScene(string phase)
    {
        var loaded = Resources.FindObjectsOfTypeAll<AnimStartupProbe>();
        var active = UnityEngine.Object.FindObjectsByType<AnimStartupProbe>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        var go = loaded[0].gameObject;
        Put("initialScene/" + phase, "[" + loaded.Length + "," + active.Length + "," + (go.activeSelf ? "true" : "false") + "," + (go.activeInHierarchy ? "true" : "false") + "]");
    }

    void Awake()
    {
        if (!Recording || Delayed || Review) return;
        Add("scene/enabled", sceneEnabled);
        Add("scene/disabled", sceneDisabled);
        Group("awake");
        EnableGroup("awake");
    }
    void OnEnable() { if (Recording && !Delayed) { Group("onEnable"); EnableGroup("onEnable"); } }
    void Start()
    {
        if (!Recording) return;
        if (!Delayed) { Group("start"); EnableGroup("start"); }
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
    }

    static Case Add(string name, Animator animator)
    {
        var c = new Case { name = name, animator = animator,
            receiver = animator.GetComponent<StartupReceiver>(), marker = animator.transform.Find("Marker") };
        cases.Add(c);
        return c;
    }
    static Case Create(string name, bool disabled = false, bool builtin = false)
    {
        var go = new GameObject(name);
        new GameObject("Marker").transform.SetParent(go.transform, false);
        if (!builtin) go.AddComponent<StartupReceiver>();
        var animator = go.AddComponent<Animator>();
        animator.enabled = !disabled;
        animator.applyRootMotion = true;
        animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("Startup");
        return Add(name, animator);
    }
    static void Group(string phase)
    {
        foreach (string control in new[] { "plain", "zero", "positive", "play", "rebind", "unscaled", "builtin", "graph", "graphController", "fixed" })
        {
            var c = Create(phase + "/" + control, builtin: control == "builtin");
            Snapshot(c, "created");
            switch (control)
            {
                case "zero": c.animator.Update(0f); break;
                case "positive": c.animator.Update(0.03f); break;
                case "play": c.animator.Play("Base Layer.Run", 0, 0.25f); break;
                case "rebind": c.animator.Rebind(); break;
                case "unscaled": c.animator.updateMode = AnimatorUpdateMode.UnscaledTime; break;
                case "fixed": c.animator.updateMode = AnimatorUpdateMode.Fixed; break;
                case "graph":
                case "graphController":
                    if (control == "graph") c.animator.runtimeAnimatorController = null;
                    c.graph = PlayableGraph.Create(c.name);
                    c.graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
                    c.clip = AnimationClipPlayable.Create(c.graph, Resources.Load<AnimationClip>("Startup"));
                    var output = AnimationPlayableOutput.Create(c.graph, "Animation", c.animator);
                    output.SetSourcePlayable(c.clip);
                    c.graph.Play();
                    break;
            }
            Snapshot(c, "prepared");
        }
    }
    static void EnableGroup(string phase)
    {
        var c = Create(phase + "/enable", disabled: true);
        Snapshot(c, "disabled");
        c.animator.enabled = true;
        Snapshot(c, "enabled");
        var inactive = Create(phase + "/activate", disabled: true);
        inactive.animator.gameObject.SetActive(false);
        inactive.animator.enabled = true;
        inactive.animator.gameObject.SetActive(true);
        Snapshot(inactive, "enabled");
    }
    void Update()
    {
        if (!Recording) return;
        if (updates == 0 && !Delayed)
        {
            Group("firstUpdate");
            EnableGroup("firstUpdate");
            sceneDisabled.enabled = true;
        }
        if (updates == 1 && !Delayed) { Group("secondUpdate"); EnableGroup("secondUpdate"); }
        if (updates == 3 && !Review) { Group("laterUpdate"); EnableGroup("laterUpdate"); }
        if (updates == 2 && Review)
            foreach (bool keep in new[] { false, true })
                Create("reenable/" + keep + "/disabledActive").animator.keepAnimatorStateOnDisable = keep;
        if (updates == 2 && !Delayed)
        {
            foreach (bool keep in new[] { false, true })
            foreach (bool active in new[] { false, true })
            {
                var c = Create("reenable/" + keep + "/" + (active ? "active" : "component"));
                c.animator.keepAnimatorStateOnDisable = keep;
            }
        }
        if (updates == 4 || updates == 5 || (Review && updates == 6))
            foreach (var c in cases)
                if (c.name.StartsWith("reenable/"))
                {
                    if (c.name.EndsWith("disabledActive"))
                    {
                        if (updates == 4) { c.animator.enabled = false; c.animator.gameObject.SetActive(false); }
                        if (updates == 5) c.animator.gameObject.SetActive(true);
                        if (updates == 6) c.animator.enabled = true;
                    }
                    else
                    {
                        if (c.name.EndsWith("active")) c.animator.gameObject.SetActive(updates == 5);
                        else c.animator.enabled = updates == 5;
                    }
                }
        foreach (var c in cases)
        {
            // Normalize the unscaled control to 0.02 animation seconds despite wall-clock Editor pacing.
            if (c.animator.updateMode == AnimatorUpdateMode.UnscaledTime)
                c.animator.speed = 0.02f / Time.unscaledDeltaTime;
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
        if (finished != null) finished(0);
        else UnityEditor.EditorApplication.Exit(0);
    }
    static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);
    static void Put(string key, string value) => records.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(value).Append("}\n");
    static void Snapshot(Case c, string phase)
    {
        var a = c.animator;
        var state = a.GetCurrentAnimatorStateInfo(0);
        Put(c.name + "/" + phase, "{\"time\":" + F(state.normalizedTime)
            + ",\"graph\":" + (c.graph.IsValid() ? c.clip.GetTime().ToString("R", CultureInfo.InvariantCulture) : "-1")
            + ",\"state\":" + state.fullPathHash + ",\"pose\":" + F(c.marker.localPosition.x)
            + ",\"root\":" + F(a.transform.localPosition.y)
            + ",\"delta\":" + F(a.deltaPosition.y)
            + ",\"events\":[" + (c.receiver == null ? "0,0,0" : c.receiver.zero + "," + c.receiver.early + "," + c.receiver.later)
            + "],\"moves\":" + (c.receiver == null ? 0 : c.receiver.moves) + "}");
    }
}
