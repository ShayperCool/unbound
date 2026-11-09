using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Object = UnityEngine.Object;

/// <summary>
/// Animation edge cases. <c>steps.*</c>: the Steps clip (segments whose ends carry infinite
/// tangents) sampled by an Animator's controller, by a manually evaluated AnimationClipPlayable and,
/// as the curves themselves, by AnimationCurve.Evaluate. <c>play.*</c>: AnimatorControllerPlayable.Play
/// issued before its graph's first evaluation (manual and game-time graphs, with and without a
/// normalized time, before and after connecting the output), after it, and Animator.Play before the
/// Animator's first update as a control: A.x (1 in state A, 10 in state B) and the current state.
/// </summary>
public class AnimEdgesProbe : MonoBehaviour
{
    public static bool Finished;
    static readonly StringBuilder s_Output = new StringBuilder();
    static int s_Log;

    static readonly string[] Paths = { "A", "B", "C", "D", "E", "L" };
    static readonly float[] Steps = { 0f, .25f, .24f, .02f, .48f, .01f };

    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string S(string s) => "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    static void Record(string key, string value) =>
        s_Output.Append("{\"k\":").Append(S(key)).Append(",\"v\":").Append(value).Append("}\n");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
    static void Boot()
    {
        if (Environment.GetEnvironmentVariable("ANIM_EDGES_OUT") == null) return;
        Application.runInBackground = true;
        DontDestroyOnLoad(new GameObject("AnimEdgesProbe").AddComponent<AnimEdgesProbe>().gameObject);
        Application.logMessageReceived += (message, stack, kind) =>
        {
            if (stack.Contains("UnityEditor.")) return;
            Record("log." + (s_Log++).ToString("D3"), "{\"type\":" + S(kind.ToString()) + ",\"message\":" + S(message) + "}");
        };
    }

    static GameObject Rig(string name, params string[] children)
    {
        var root = new GameObject(name);
        foreach (var child in children) new GameObject(child).transform.SetParent(root.transform, false);
        return root;
    }

    static string Xs(GameObject root) =>
        "{" + string.Join(",", Paths.Where(p => root.transform.Find(p) != null)
            .Select(p => S(p) + ":" + F(root.transform.Find(p).localPosition.x))) + "}";

    // ------------------------------------------------------------------ infinite tangents

    static void StepsByAnimator()
    {
        var root = Rig("Steps", Paths);
        var animator = root.AddComponent<Animator>();
        animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("C_Steps");
        float time = 0f;
        foreach (var step in Steps)
        {
            animator.Update(step);
            time += step;
            Record("steps.animator." + F(time), Xs(root));
        }
        Object.DestroyImmediate(root);
    }

    static void StepsByClipPlayable()
    {
        var root = Rig("StepsGraph", Paths);
        var animator = root.AddComponent<Animator>();
        var graph = PlayableGraph.Create("steps");
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var clip = AnimationClipPlayable.Create(graph, Resources.Load<AnimationClip>("Steps"));
        AnimationPlayableOutput.Create(graph, "out", animator).SetSourcePlayable(clip);
        float time = 0f;
        foreach (var step in Steps)
        {
            time += step;
            clip.SetTime(time);
            graph.Evaluate(0f);
            Record("steps.clipPlayable." + F(time), Xs(root));
        }
        graph.Destroy();
        Object.DestroyImmediate(root);
    }

    static void StepsByCurves()
    {
#if UNITY_EDITOR
        var clip = Resources.Load<AnimationClip>("Steps");
        float time = 0f;
        foreach (var step in Steps)
        {
            time += step;
            var fields = Paths.Select(p =>
            {
                var curve = UnityEditor.AnimationUtility.GetEditorCurve(clip,
                    UnityEditor.EditorCurveBinding.FloatCurve(p, typeof(Transform), "m_LocalPosition.x"));
                return S(p) + ":" + F(curve.Evaluate(time));
            });
            Record("steps.curve." + F(time), "{" + string.Join(",", fields) + "}");
        }
#endif
    }

    // ------------------------------------------------------------------ Play before the first evaluation

    static string State(GameObject root, AnimatorStateInfo info) =>
        "{\"x\":" + F(root.transform.Find("A").localPosition.x) + ",\"B\":" + (info.IsName("B") ? "true" : "false") +
        ",\"t\":" + F(info.normalizedTime) + "}";

    static void ManualGraph(string key, bool connectFirst, bool evaluateFirst, float normalizedTime)
    {
        var root = Rig(key, "A");
        var animator = root.AddComponent<Animator>();
        var graph = PlayableGraph.Create(key);
        graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
        var controller = AnimatorControllerPlayable.Create(graph, Resources.Load<RuntimeAnimatorController>("C_Two"));
        var output = AnimationPlayableOutput.Create(graph, "out", animator);
        if (connectFirst) output.SetSourcePlayable(controller);
        if (evaluateFirst) graph.Evaluate(0f);
        if (float.IsNegativeInfinity(normalizedTime)) controller.Play("B");
        else controller.Play("B", 0, normalizedTime);
        if (!connectFirst) output.SetSourcePlayable(controller);
        graph.Evaluate(0f);
        Record("play." + key + ".0", State(root, controller.GetCurrentAnimatorStateInfo(0)));
        graph.Evaluate(.1f);
        Record("play." + key + ".1", State(root, controller.GetCurrentAnimatorStateInfo(0)));
        graph.Destroy();
        Object.DestroyImmediate(root);
    }

    static void AnimatorControl()
    {
        var root = Rig("animator", "A");
        var animator = root.AddComponent<Animator>();
        animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("C_Two");
        animator.Play("B");
        animator.Update(0f);
        Record("play.animator.0", State(root, animator.GetCurrentAnimatorStateInfo(0)));
        animator.Update(.1f);
        Record("play.animator.1", State(root, animator.GetCurrentAnimatorStateInfo(0)));
        Object.DestroyImmediate(root);
    }

    // A game-time graph played by the player loop, Play issued in the frame it is created.
    GameObject m_GameRoot;
    PlayableGraph m_GameGraph;
    AnimatorControllerPlayable m_GameController;
    int m_Frame = -1;

    void Start()
    {
        try
        {
            StepsByAnimator();
            StepsByClipPlayable();
            StepsByCurves();
            ManualGraph("manual", connectFirst: true, evaluateFirst: false, float.NegativeInfinity);
            ManualGraph("manualTime", connectFirst: true, evaluateFirst: false, .5f);
            ManualGraph("manualUnconnected", connectFirst: false, evaluateFirst: false, float.NegativeInfinity);
            ManualGraph("manualAfterFirst", connectFirst: true, evaluateFirst: true, float.NegativeInfinity);
            AnimatorControl();
        }
        catch (Exception e)
        {
            Record("exception", S(e.ToString()));
        }
    }

    void Update()
    {
        if (Finished) return;
        m_Frame++;
        if (m_Frame == 1)
        {
            m_GameRoot = Rig("gameTime", "A");
            var animator = m_GameRoot.AddComponent<Animator>();
            m_GameGraph = PlayableGraph.Create("gameTime");
            m_GameController = AnimatorControllerPlayable.Create(m_GameGraph, Resources.Load<RuntimeAnimatorController>("C_Two"));
            AnimationPlayableOutput.Create(m_GameGraph, "out", animator).SetSourcePlayable(m_GameController);
            m_GameController.Play("B");
            m_GameGraph.Play();
        }
        if (m_Frame == 4)
        {
            m_GameGraph.Destroy();
            Object.DestroyImmediate(m_GameRoot);
            Record("complete", "true");
            File.WriteAllText(Environment.GetEnvironmentVariable("ANIM_EDGES_OUT"), s_Output.ToString());
            Finished = true;
        }
    }

    void LateUpdate()
    {
        if (Finished || m_Frame < 1 || m_Frame > 3) return;
        var info = m_GameController.GetCurrentAnimatorStateInfo(0);
        // B's clip holds A.x between 10 and 11 (A's between 1 and 2); the frame intervals vary.
        float x = m_GameRoot.transform.Find("A").localPosition.x;
        Record("play.gameTime." + (m_Frame - 1), "{\"xInB\":" + (x >= 10f && x <= 11f ? "true" : "false") + ",\"B\":" +
               (info.IsName("B") ? "true" : "false") + "}");
    }
}
