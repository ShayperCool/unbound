using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

// Only the human profile runs this control. Existing entry/delayed records stay unchanged.
public class AnimStartupHumanProbe : MonoBehaviour
{
    sealed class Rig
    {
        public string name;
        public Animator animator;
        public Transform hips, arm;
        public StartupHumanReceiver receiver;
        public StartupHumanIKReceiver ikReceiver;
        public PlayableGraph graph;
        public AnimationClipPlayable clip;
    }
    readonly List<Rig> rigs = new List<Rig>();
    readonly StringBuilder rows = new StringBuilder();
    int update;
    static bool Active => Environment.GetEnvironmentVariable("ANIMSTARTUP_HUMAN") == "1";
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Clock() { if (Active) Time.captureDeltaTime = .02f; }
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot() { if (Active) new GameObject("HumanDriver").AddComponent<AnimStartupHumanProbe>(); }
    void Start()
    {
        var camera = new GameObject("Camera").AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = Color.black;
        Group("start");
    }
    void Group(string phase)
    {
        foreach (string control in new[] { "plain", "play", "graph" })
        {
            var model = Instantiate(Resources.Load<GameObject>("HumanA"));
            var a = model.GetComponent<Animator>();
            a.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            a.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("HumanAIK");
            a.applyRootMotion = control != "graph";
            var r = new Rig { name = phase + "/" + control, animator = a,
                hips = a.GetBoneTransform(HumanBodyBones.Hips), arm = a.GetBoneTransform(HumanBodyBones.LeftLowerArm),
                receiver = control == "graph" && Environment.GetEnvironmentVariable("ANIMSTARTUP_HUMAN_MOTION") == "0"
                    ? null : model.AddComponent<StartupHumanReceiver>() };
            if (r.receiver == null) r.ikReceiver = model.AddComponent<StartupHumanIKReceiver>();
            if (control == "play") a.Play("Base Layer.Scene", 0, .25f);
            if (control == "graph")
            {
                var clip = a.runtimeAnimatorController.animationClips[0];
                a.runtimeAnimatorController = null;
                r.graph = PlayableGraph.Create(r.name);
                r.graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
                r.clip = AnimationClipPlayable.Create(r.graph, clip);
                r.clip.SetApplyPlayableIK(true);
                r.clip.SetApplyFootIK(false);
                AnimationPlayableOutput.Create(r.graph, "Animation", a).SetSourcePlayable(r.clip);
                r.graph.Play();
            }
            rigs.Add(r);
        }
    }
    void Update()
    {
        if (update == 0) Group("firstUpdate");
        if (update == 1) Group("secondUpdate");
        if (update == 3) Group("laterUpdate");
        foreach (var r in rigs) Snapshot(r, "update/" + update);
    }
    void LateUpdate()
    {
        foreach (var r in rigs) Snapshot(r, "late/" + update);
        if (++update < 9) return;
        rows.Append("{\"k\":\"complete\",\"v\":true}\n");
        var directory = Environment.GetEnvironmentVariable("ANIMSTARTUP_PROBE_OUT");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), rows.ToString());
        if (AnimStartupProbe.finished != null) AnimStartupProbe.finished(0);
        else UnityEditor.EditorApplication.Exit(0);
    }
    static string F(double f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
    static string Q(Quaternion q) => "[" + F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w) + "]";
    void Snapshot(Rig r, string phase)
    {
        rows.Append("{\"k\":\"").Append(r.name).Append('/').Append(phase).Append("\",\"v\":{\"time\":")
            .Append(F(r.animator.GetCurrentAnimatorStateInfo(0).normalizedTime))
            .Append(",\"graph\":").Append(r.graph.IsValid() ? F(r.clip.GetTime()) : "-1")
            .Append(",\"root\":").Append(V(r.animator.transform.position))
            .Append(",\"delta\":").Append(V(r.animator.deltaPosition))
            .Append(",\"hips\":").Append(Q(r.hips.localRotation)).Append(",\"arm\":").Append(Q(r.arm.localRotation))
            .Append(",\"moves\":").Append(r.receiver == null ? 0 : r.receiver.moves).Append(",\"ik\":").Append(r.receiver == null ? r.ikReceiver.ik : r.receiver.ik).Append("}}\n");
    }
}
