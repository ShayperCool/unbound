using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

/// <summary>
/// Animator.cullingMode and Animation.cullingType against renderer visibility. One camera looks
/// at the origin; each case is a holder that a schedule moves between the origin (on screen) and
/// x = 1000 (off screen) in Update, with a rig under it (Animator or Animation, an animated empty
/// "Marker" child, and the case's renderers). After every frame's animation (LateUpdate) the probe
/// records, per case, the renderers' isVisible, what the animation wrote, the state machine, the
/// event, OnAnimatorMove and OnAnimatorIK counts and the root's position. The rigs are built in
/// frame 1's Update (frame 5's for the late ones); frames 1-60 of 0.02 s are recorded.
/// </summary>
public class AnimCullingProbe : MonoBehaviour
{
    /// <summary>Set by the Editor runner: ends Play mode and quits with the code.</summary>
    public static Action<int> finished;

    const int Frames = 61;
    static readonly Vector3 On = Vector3.zero, Off = new Vector3(1000f, 0f, 0f);

    sealed class Case
    {
        public string name;
        public Transform holder, rig, marker;
        public Func<int, bool> visible;
        public Action<int> step;
        public readonly List<Renderer> renderers = new List<Renderer>();
        public Animator animator;
        public Animation animation;
        public CullReceiver receiver;
        public CullMoveReceiver move;
        public CullIK ik;
        public Transform hips, arm;
        public Quaternion lastHips, lastArm;
        public string atUpdate;
        public PlayableGraph graph;
        public AnimationClipPlayable clip;
        public Playable source;
        public bool explicitEvaluation;
    }

    readonly List<Case> cases = new List<Case>();
    readonly StringBuilder records = new StringBuilder();
    string directory;
    int frame;
    Material material;
    Mesh cube;
    bool failed;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("ANIMCULLING_PROBE_OUT") == null) return;
        Time.captureDeltaTime = 0.02f;
        new GameObject("AnimCullingProbe").AddComponent<AnimCullingProbe>();
    }

    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string B(bool b) => b ? "true" : "false";
    static string Q(Quaternion q) => "[" + F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w) + "]";
    static string V(Vector3 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
    static string S(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    void Put(string k, string json) => records.Append("{\"k\":").Append(S(k)).Append(",\"v\":").Append(json).Append("}\n");

    static bool ScheduleA(int f) => f < 12 || f >= 32;
    static bool ScheduleB(int f) => f >= 20;
    static bool Always(int f) => true;

    void Start()
    {
        directory = Environment.GetEnvironmentVariable("ANIMCULLING_PROBE_OUT");
        Directory.CreateDirectory(directory);
        try
        {
            var camera = new GameObject("Main Camera").AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.transform.position = new Vector3(0f, 0f, -10f);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            material = new Material(Shader.Find("Unlit/Color"));
            cube = CubeMesh();
            ExplicitCases("entry", entry: true);
        }
        catch (Exception e) { Put("fatal", S(e.ToString())); failed = true; }
    }

    void Build()
    {
        var culling = Resources.Load<RuntimeAnimatorController>("Culling");
        var rootMotion = Resources.Load<RuntimeAnimatorController>("RootMotion");
        var modes = new[] { ("always", AnimatorCullingMode.AlwaysAnimate), ("transforms", AnimatorCullingMode.CullUpdateTransforms),
            ("completely", AnimatorCullingMode.CullCompletely) };
        foreach (var (name, mode) in modes)
        {
            Animated("a/" + name + "/A", culling, mode, ScheduleA);
            Animated("a/" + name + "/B", culling, mode, ScheduleB);
            var rm = Animated("rm/" + name, rootMotion, mode, ScheduleA);
            rm.animator.applyRootMotion = true;
            foreach (var (suffix, schedule) in new[] { ("", (Func<int, bool>)ScheduleA), ("/B", ScheduleB) })
            {
                var rmm = Animated("rmm/" + name + suffix, rootMotion, mode, schedule);
                rmm.animator.applyRootMotion = true;
                rmm.move = rmm.rig.gameObject.AddComponent<CullMoveReceiver>();
            }
            Humanoid("h/" + name, mode);
        }
        foreach (var (name, mode) in new[] { modes[1], modes[2] })
        {
            // Two renderers: Body follows the holder, Far is placed so that it is on screen exactly
            // while the holder is off; Far's renderer is disabled from frame 20 on.
            var m = Animated("m/" + name, culling, mode, ScheduleA, far: true);
            m.step = f => { if (f == 20) m.renderers[1].enabled = false; };
            // On screen throughout; the only renderer disabled in frames 10-29.
            var d = Animated("d/" + name, culling, mode, Always);
            d.step = f => d.renderers[0].enabled = f < 10 || f >= 30;
            // On screen throughout; the renderer's GameObject inactive in frames 10-29.
            var i = Animated("i/" + name, culling, mode, Always);
            i.step = f => i.renderers[0].gameObject.SetActive(f < 10 || f >= 30);
            Animated("n/" + name, culling, mode, ScheduleA, body: false);
            foreach (var offscreen in new[] { false, true })
                Animated("s/" + name + "/" + (offscreen ? "on" : "off"), culling, mode, ScheduleA, body: false, skinned: true, updateWhenOffscreen: offscreen);
        }
        PlayableCases(modes);
        ExplicitCases("later", entry: false);
        ExplicitHumanCases();
        foreach (var (name, mode) in modes)
        foreach (bool controller in new[] { false, true })
        foreach (bool mixer in new[] { false, true })
        {
            var c = Humanoid("ph/" + name + "/" + (controller ? "controller" : "bare") + "/" + (mixer ? "mixer" : "clip"), mode);
            var clip = c.animator.runtimeAnimatorController.animationClips[0];
            if (!controller) c.animator.runtimeAnimatorController = null;
            c.animator.applyRootMotion = false;
            c.graph = PlayableGraph.Create(c.name);
            c.graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(c.graph, "Animation", c.animator);
            c.clip = AnimationClipPlayable.Create(c.graph, clip);
            c.clip.SetApplyPlayableIK(true);
            c.clip.SetApplyFootIK(false);
            c.source = c.clip;
            if (mixer)
            {
                var mix = AnimationMixerPlayable.Create(c.graph, 2);
                mix.ConnectInput(0, c.clip, 0, 0.25f);
                var second = AnimationClipPlayable.Create(c.graph, clip);
                second.SetApplyPlayableIK(true);
                second.SetApplyFootIK(false);
                mix.ConnectInput(1, second, 0, 0.75f);
                c.source = mix;
            }
            output.SetSourcePlayable(c.source);
            c.graph.Play();
        }
        Legacy("l/always/A", AnimationCullingType.AlwaysAnimate, ScheduleA);
        Legacy("l/based/A", AnimationCullingType.BasedOnRenderers, ScheduleA);
        Legacy("l/based/B", AnimationCullingType.BasedOnRenderers, ScheduleB);
        Legacy("ln/based", AnimationCullingType.BasedOnRenderers, ScheduleA, body: false);
        var lm = Legacy("lm/based", AnimationCullingType.BasedOnRenderers, ScheduleA, far: true);
        lm.step = f => { if (f == 20) lm.renderers[1].enabled = false; };
        var ld = Legacy("ld/based", AnimationCullingType.BasedOnRenderers, Always);
        ld.step = f => ld.renderers[0].enabled = f < 10 || f >= 30;
        foreach (var offscreen in new[] { false, true })
            Legacy("ls/based/" + (offscreen ? "on" : "off"), AnimationCullingType.BasedOnRenderers, ScheduleA, body: false, skinned: true, updateWhenOffscreen: offscreen);
    }

    void PlayableCases((string name, AnimatorCullingMode mode)[] modes)
    {
        foreach (var (name, mode) in modes)
        foreach (bool controller in new[] { false, true })
        foreach (bool mixer in new[] { false, true })
        foreach (string motion in new[] { "pose", "builtin", "move" })
        {
            var c = Animated("p/" + name + "/" + (controller ? "controller" : "bare") + "/"
                + (mixer ? "mixer" : "clip") + "/" + motion,
                controller ? Resources.Load<RuntimeAnimatorController>("Culling") : null, mode, ScheduleA);
            c.animator.applyRootMotion = motion != "pose";
            if (motion == "move") c.move = c.rig.gameObject.AddComponent<CullMoveReceiver>();
            c.graph = PlayableGraph.Create(c.name);
            c.graph.SetTimeUpdateMode(DirectorUpdateMode.GameTime);
            var output = AnimationPlayableOutput.Create(c.graph, "Animation", c.animator);
            c.clip = AnimationClipPlayable.Create(c.graph, Resources.Load<AnimationClip>("PlayablePose"));
            c.clip.SetApplyPlayableIK(true);
            c.source = c.clip;
            if (mixer)
            {
                var mix = AnimationMixerPlayable.Create(c.graph, 2);
                mix.ConnectInput(0, c.clip, 0, 0.25f);
                mix.ConnectInput(1, AnimationClipPlayable.Create(c.graph, Resources.Load<AnimationClip>("PlayableSecond")), 0, 0.75f);
                c.source = mix;
            }
            output.SetSourcePlayable(c.source);
            c.graph.Play();
        }
    }

    // Explicit calls are recorded immediately, separately from each following automatic frame.
    // The entry controls run before the first automatic animation interval; later controls run
    // with both positive and zero supplied deltas, including repeated calls within one Update.
    void ExplicitCases(string context, bool entry)
    {
        foreach (var mode in new[] { AnimatorCullingMode.AlwaysAnimate, AnimatorCullingMode.CullUpdateTransforms, AnimatorCullingMode.CullCompletely })
        foreach (var clock in new[] { DirectorUpdateMode.Manual, DirectorUpdateMode.GameTime })
        foreach (bool controller in new[] { false, true })
        foreach (bool mixer in new[] { false, true })
        foreach (string motion in new[] { "pose", "builtin", "move" })
        {
            if (entry && mixer) continue;
            var c = Animated("explicit/" + context + "/" + mode + "/" + clock + "/"
                + (controller ? "controller" : "bare") + "/" + (mixer ? "mixer" : "clip") + "/" + motion,
                controller ? Resources.Load<RuntimeAnimatorController>("Culling") : null, mode, ScheduleA);
            c.explicitEvaluation = true;
            c.animator.applyRootMotion = motion != "pose";
            if (motion == "move") c.move = c.rig.gameObject.AddComponent<CullMoveReceiver>();
            c.graph = PlayableGraph.Create(c.name);
            c.graph.SetTimeUpdateMode(clock);
            var output = AnimationPlayableOutput.Create(c.graph, "Animation", c.animator);
            c.clip = AnimationClipPlayable.Create(c.graph, Resources.Load<AnimationClip>("PlayablePose"));
            c.clip.SetApplyPlayableIK(true); // Generic rigs: no humanoid IK callback is expected.
            c.source = c.clip;
            if (mixer)
            {
                var mix = AnimationMixerPlayable.Create(c.graph, 2);
                mix.ConnectInput(0, c.clip, 0, 0.25f);
                mix.ConnectInput(1, AnimationClipPlayable.Create(c.graph, Resources.Load<AnimationClip>("PlayableSecond")), 0, 0.75f);
                c.source = mix;
            }
            output.SetSourcePlayable(c.source);
            c.graph.Play();
        }
    }

    void ExplicitHumanCases()
    {
        foreach (var mode in new[] { AnimatorCullingMode.AlwaysAnimate, AnimatorCullingMode.CullUpdateTransforms, AnimatorCullingMode.CullCompletely })
        foreach (var clock in new[] { DirectorUpdateMode.Manual, DirectorUpdateMode.GameTime })
        {
            var c = Humanoid("explicit/human/" + mode + "/" + clock, mode);
            var clip = c.animator.runtimeAnimatorController.animationClips[0];
            c.animator.runtimeAnimatorController = null;
            c.animator.applyRootMotion = false; // Humanoid output root motion is a separate gap.
            c.explicitEvaluation = true;
            c.graph = PlayableGraph.Create(c.name);
            c.graph.SetTimeUpdateMode(clock);
            c.clip = AnimationClipPlayable.Create(c.graph, clip);
            c.clip.SetApplyPlayableIK(true);
            c.clip.SetApplyFootIK(false);
            c.source = c.clip;
            AnimationPlayableOutput.Create(c.graph, "Animation", c.animator).SetSourcePlayable(c.source);
            c.graph.Play();
        }
    }

    void ExplicitStep(Case c)
    {
        if (!c.explicitEvaluation) return;
        if (frame != 0 && frame != 6 && frame != 16 && frame != 33) return;
        // Capture before/after every call. 0.07 + 0 + 0.04 crosses the clip's 0.1 event.
        Record(c, "before");
        int call = 0;
        foreach (float delta in new[] { 0.07f, 0f, 0.04f })
        {
            c.graph.Evaluate(delta);
            Record(c, "evaluate" + call++);
        }
    }

    /// <summary>Animators and an Animation created in frame 5's Update: their first update in a later frame.</summary>
    void Late()
    {
        Func<int, bool> later = f => f >= 20;
        Animated("late/always", Resources.Load<RuntimeAnimatorController>("Culling"), AnimatorCullingMode.AlwaysAnimate, Always);
        foreach (var (name, mode) in new[] { ("always", AnimatorCullingMode.AlwaysAnimate), ("transforms", AnimatorCullingMode.CullUpdateTransforms),
            ("completely", AnimatorCullingMode.CullCompletely) })
        {
            var c = Animated("late/rmm/" + name, Resources.Load<RuntimeAnimatorController>("RootMotion"), mode, later);
            c.animator.applyRootMotion = true;
            c.move = c.rig.gameObject.AddComponent<CullMoveReceiver>();
        }
        Legacy("late/l/always", AnimationCullingType.AlwaysAnimate, Always);
        Legacy("late/l/based", AnimationCullingType.BasedOnRenderers, later);
    }

    Case Rig(string name, Func<int, bool> visible, bool body, bool far, bool skinned, bool updateWhenOffscreen)
    {
        var c = new Case { name = name, visible = visible };
        c.holder = new GameObject(name).transform;
        c.holder.position = visible(0) ? On : Off;
        c.rig = new GameObject("Rig").transform;
        c.rig.SetParent(c.holder, false);
        c.marker = new GameObject("Marker").transform;
        c.marker.SetParent(c.rig, false);
        if (body) c.renderers.Add(Body("Body", c.rig, Vector3.zero));
        if (far) c.renderers.Add(Body("Far", c.rig, -Off));
        if (skinned) c.renderers.Add(Skin(c.rig, updateWhenOffscreen));
        c.receiver = c.rig.gameObject.AddComponent<CullReceiver>();
        cases.Add(c);
        return c;
    }

    Case Animated(string name, RuntimeAnimatorController controller, AnimatorCullingMode mode, Func<int, bool> visible,
        bool body = true, bool far = false, bool skinned = false, bool updateWhenOffscreen = false)
    {
        var c = Rig(name, visible, body, far, skinned, updateWhenOffscreen);
        c.animator = c.rig.gameObject.AddComponent<Animator>();
        c.animator.cullingMode = mode;
        c.animator.runtimeAnimatorController = controller;
        return c;
    }

    Case Legacy(string name, AnimationCullingType type, Func<int, bool> visible,
        bool body = true, bool far = false, bool skinned = false, bool updateWhenOffscreen = false)
    {
        var c = Rig(name, visible, body, far, skinned, updateWhenOffscreen);
        var clip = Resources.Load<AnimationClip>("LegacyLoop");
        c.animation = c.rig.gameObject.AddComponent<Animation>();
        c.animation.cullingType = type;
        c.animation.AddClip(clip, "LegacyLoop");
        c.animation.clip = clip;
        c.animation.Play("LegacyLoop");
        return c;
    }

    Case Humanoid(string name, AnimatorCullingMode mode)
    {
        var c = new Case { name = name, visible = ScheduleA };
        c.holder = new GameObject(name).transform;
        c.holder.position = On;
        var model = Instantiate(Resources.Load<GameObject>("HumanA"), c.holder, false);
        c.rig = model.transform;
        var body = Body("Body", c.rig, Vector3.zero);
        body.transform.position = new Vector3(0f, 1f, 0f);
        c.renderers.Add(body);
        c.animator = model.GetComponent<Animator>();
        c.animator.cullingMode = mode;
        c.animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("HumanAIK");
        c.ik = model.AddComponent<CullIK>();
        c.hips = c.animator.GetBoneTransform(HumanBodyBones.Hips);
        c.arm = c.animator.GetBoneTransform(HumanBodyBones.LeftLowerArm);
        c.lastHips = c.hips.localRotation;
        c.lastArm = c.arm.localRotation;
        Put(name + "/info", "{\"rigScale\":" + V(c.rig.lossyScale) + ",\"body\":" + V(body.transform.position) + ",\"bodyScale\":" + V(body.transform.lossyScale)
            + ",\"bounds\":" + V(body.bounds.center) + ",\"extents\":" + V(body.bounds.extents) + ",\"renderers\":" + model.GetComponentsInChildren<Renderer>(true).Length + "}");
        cases.Add(c);
        return c;
    }

    Renderer Body(string name, Transform parent, Vector3 position)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localPosition = position;
        go.AddComponent<MeshFilter>().sharedMesh = cube;
        var renderer = go.AddComponent<MeshRenderer>();
        renderer.sharedMaterial = material;
        return renderer;
    }

    Renderer Skin(Transform rig, bool updateWhenOffscreen)
    {
        var bone = new GameObject("Bone").transform;
        bone.SetParent(rig, false);
        var go = new GameObject("Skin");
        go.transform.SetParent(rig, false);
        var mesh = Instantiate(cube);
        var weights = new BoneWeight[mesh.vertexCount];
        for (int i = 0; i < weights.Length; i++) weights[i] = new BoneWeight { boneIndex0 = 0, weight0 = 1f };
        mesh.boneWeights = weights;
        mesh.bindposes = new[] { Matrix4x4.identity };
        var skin = go.AddComponent<SkinnedMeshRenderer>();
        skin.sharedMesh = mesh;
        skin.bones = new[] { bone };
        skin.rootBone = bone;
        skin.sharedMaterial = material;
        skin.updateWhenOffscreen = updateWhenOffscreen;
        return skin;
    }

    static Mesh CubeMesh()
    {
        var mesh = new Mesh { name = "ProbeCube" };
        var v = new List<Vector3>();
        for (int i = 0; i < 8; i++) v.Add(new Vector3((i & 1) - 0.5f, ((i >> 1) & 1) - 0.5f, ((i >> 2) & 1) - 0.5f) * 0.5f);
        mesh.SetVertices(v);
        mesh.triangles = new[] { 0, 2, 1, 1, 2, 3, 4, 5, 6, 5, 7, 6, 0, 1, 4, 1, 5, 4, 2, 6, 3, 3, 6, 7, 0, 4, 2, 2, 4, 6, 1, 3, 5, 3, 7, 5 };
        mesh.RecalculateBounds();
        return mesh;
    }

    void Update()
    {
        if (failed || frame >= Frames) return;
        try
        {
            // Built in frame 1: the first frame of Play mode evaluates Animators made in it without
            // advancing them, which is outside this probe.
            if (frame == 1) Build();
            if (frame == 5) Late();
            foreach (var c in cases)
            {
                c.atUpdate = c.move != null ? "[" + F(c.marker.localPosition.x) + "," + F(c.rig.localPosition.y) + "," + c.move.calls + "]" : null;
                c.holder.position = c.visible(frame) ? On : Off;
                c.step?.Invoke(frame);
                ExplicitStep(c);
            }
        }
        catch (Exception e) { Put("fatal", S(e.ToString())); failed = true; }
    }

    void LateUpdate()
    {
        if (frame >= Frames) return;
        try
        {
            if (!failed)
            {
                Put("frame/" + frame, "{\"dt\":" + F(Time.deltaTime) + "}");
                foreach (var c in cases) Record(c);
            }
        }
        catch (Exception e) { Put("fatal", S(e.ToString())); failed = true; }
        if (++frame == Frames || failed) Finish();
    }

    void Record(Case c, string phase = null)
    {
        var json = new StringBuilder("{\"vis\":[");
        for (int i = 0; i < c.renderers.Count; i++) json.Append(i > 0 ? "," : "").Append(B(c.renderers[i].isVisible));
        json.Append("]");
        if (c.graph.IsValid())
            json.Append(",\"gt\":").Append(c.source.GetTime().ToString("R", CultureInfo.InvariantCulture))
                .Append(",\"ct\":").Append(c.clip.GetTime().ToString("R", CultureInfo.InvariantCulture));
        if (c.ik != null)
        {
            var info = c.animator.GetCurrentAnimatorStateInfo(0);
            json.Append(",\"nt\":").Append(F(info.normalizedTime)).Append(",\"ik\":").Append(c.ik.calls)
                ;
            if (!c.explicitEvaluation)
                json.Append(",\"hips\":").Append(B(c.hips.localRotation != c.lastHips))
                    .Append(",\"arm\":").Append(B(c.arm.localRotation != c.lastArm));
            if (c.graph.IsValid())
                Put(c.name + "/pose" + (phase == null ? "" : "/" + phase) + "/" + frame, "{\"hips\":" + Q(c.hips.localRotation) + ",\"arm\":" + Q(c.arm.localRotation)
                    + ",\"hipsDot\":" + F(Quaternion.Dot(c.lastHips, c.hips.localRotation))
                    + ",\"armDot\":" + F(Quaternion.Dot(c.lastArm, c.arm.localRotation)) + "}");
            c.lastHips = c.hips.localRotation;
            c.lastArm = c.arm.localRotation;
        }
        else
        {
            json.Append(",\"m\":[").Append(F(c.marker.localPosition.x)).Append(",").Append(F(c.marker.localPosition.y)).Append("]")
                .Append(",\"root\":").Append(V(c.rig.localPosition)).Append(",\"ev\":").Append(c.receiver.events);
            if (c.graph.IsValid())
                json.Append(",\"ik\":").Append(c.receiver.ik).Append(",\"value\":").Append(F(c.receiver.value)).Append(",\"delta\":").Append(V(c.animator.deltaPosition));
            if (c.animator != null)
            {
                var info = c.animator.GetCurrentAnimatorStateInfo(0);
                json.Append(",\"st\":").Append(S(info.IsName("A") ? "A" : info.IsName("B") ? "B" : info.IsName("Move") ? "Move" : "?"))
                    .Append(",\"nt\":").Append(F(info.normalizedTime));
                if (c.move != null) json.Append(",\"mv\":").Append(c.move.calls).Append(",\"dp\":").Append(V(c.move.lastDelta))
                    .Append(",\"u\":").Append(c.atUpdate ?? "null");
            }
            else
            {
                var state = c.animation["LegacyLoop"];
                json.Append(",\"t\":").Append(F(state.time)).Append(",\"en\":").Append(B(state.enabled))
                    .Append(",\"pl\":").Append(B(c.animation.isPlaying));
            }
        }
        if (c.explicitEvaluation)
                json.Append(",\"velocity\":").Append(V(c.animator.velocity))
                    .Append(",\"rotationDelta\":").Append(Q(c.animator.deltaRotation))
                    .Append(",\"angularVelocity\":").Append(V(c.animator.angularVelocity));
        Put(c.name + (phase == null ? "" : "/" + phase) + "/" + frame, json.Append("}").ToString());
    }

    void Finish()
    {
        int code = failed ? 1 : 0;
        Put("complete", B(!failed));
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), records.ToString());
        Debug.Log("ANIMCULLING_PROBE_DONE " + directory + " " + code);
        foreach (var c in cases) if (c.graph.IsValid()) c.graph.Destroy();
        enabled = false;
        if (finished != null) finished(code);
        else
        {
#if UNITY_EDITOR
            UnityEditor.EditorApplication.Exit(code);
#else
            Application.Quit(code);
#endif
        }
    }
}
