using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>Records the root motion of imported generic clips: each clip's statistics, settings
/// and curves (Editor data), and each clip played by an Animator with fixed Animator.Update steps,
/// with root motion on and off: the root's pose, the motion's deltas and velocities, the animated
/// nodes; and MatchTarget on generic rigs.</summary>
public class RootProbe : MonoBehaviour
{
    static readonly StringBuilder s_Output = new StringBuilder();
    static readonly string[] s_Models = { "Walker", "WalkerTilt", "WalkerNoNode", "WalkerNoAvatar", "Mover", "MoverAvatar" };
    static readonly string[] s_Samples = { "Full", "BakeAll", "OffsetY", "Range" };
    static readonly List<string> s_Log = new List<string>();
    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
    static string Q(Quaternion q) => "[" + F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w) + "]";
    static string S(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n") + "\"";

    internal static void Record(string key, string value) =>
        s_Output.Append("{\"k\":").Append(S(key)).Append(",\"v\":").Append(value).Append("}\n");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("RootProbe").AddComponent<RootProbe>();

    static IEnumerable<AnimationClip> Clips(string model) =>
        Resources.LoadAll<AnimationClip>(model).OrderBy(c => c.name, StringComparer.Ordinal);

    static void Imported(string model)
    {
        foreach (var clip in Clips(model))
        {
            string key = "clip." + model + "." + clip.name;
            Record(key, "{\"length\":" + F(clip.length) + ",\"isLooping\":" + (clip.isLooping ? 1 : 0)
                + ",\"averageSpeed\":" + V(clip.averageSpeed) + ",\"averageAngularSpeed\":" + F(clip.averageAngularSpeed)
                + ",\"apparentSpeed\":" + F(clip.apparentSpeed) + ",\"averageDuration\":" + F(clip.averageDuration)
                + ",\"humanMotion\":" + (clip.humanMotion ? 1 : 0) + ",\"hasGenericRootTransform\":" + (clip.hasGenericRootTransform ? 1 : 0)
                + ",\"hasMotionCurves\":" + (clip.hasMotionCurves ? 1 : 0) + ",\"hasRootCurves\":" + (clip.hasRootCurves ? 1 : 0)
                + ",\"hasMotionFloatCurves\":" + (clip.hasMotionFloatCurves ? 1 : 0) + "}");
#if UNITY_EDITOR
            var s = UnityEditor.AnimationUtility.GetAnimationClipSettings(clip);
            Record(key + ".settings", "{\"startTime\":" + F(s.startTime) + ",\"stopTime\":" + F(s.stopTime) + ",\"loopTime\":" + (s.loopTime ? 1 : 0)
                + ",\"loopBlend\":" + (s.loopBlend ? 1 : 0) + ",\"cycleOffset\":" + F(s.cycleOffset)
                + ",\"loopBlendOrientation\":" + (s.loopBlendOrientation ? 1 : 0) + ",\"loopBlendPositionY\":" + (s.loopBlendPositionY ? 1 : 0)
                + ",\"loopBlendPositionXZ\":" + (s.loopBlendPositionXZ ? 1 : 0) + ",\"keepOriginalOrientation\":" + (s.keepOriginalOrientation ? 1 : 0)
                + ",\"keepOriginalPositionY\":" + (s.keepOriginalPositionY ? 1 : 0) + ",\"keepOriginalPositionXZ\":" + (s.keepOriginalPositionXZ ? 1 : 0)
                + ",\"heightFromFeet\":" + (s.heightFromFeet ? 1 : 0) + ",\"orientationOffsetY\":" + F(s.orientationOffsetY) + ",\"level\":" + F(s.level) + "}");
            var bindings = UnityEditor.AnimationUtility.GetCurveBindings(clip)
                .OrderBy(b => b.path, StringComparer.Ordinal).ThenBy(b => b.propertyName, StringComparer.Ordinal).ToArray();
            Record(key + ".bindings", "[" + string.Join(",", bindings.Select(b => S(b.path + "|" + (b.type != null ? b.type.Name : "") + "|" + b.propertyName))) + "]");
            if (Array.IndexOf(s_Samples, clip.name) < 0) continue;
            int frames = Mathf.CeilToInt(clip.length * clip.frameRate - 1e-3f);
            foreach (var b in bindings)
            {
                var curve = UnityEditor.AnimationUtility.GetEditorCurve(clip, b);
                var values = new List<string>();
                for (int i = 0; i <= frames; i++) values.Add(F(curve.Evaluate(i / clip.frameRate)));
                Record(key + ".curve." + b.path + "|" + b.propertyName, "[" + string.Join(",", values) + "]");
            }
#endif
        }
    }

    internal static string Snapshot(Animator animator, string body, bool log)
    {
        var root = animator.transform;
        var parts = new List<string>
        {
            "\"root.p\":" + V(root.position), "\"root.r\":" + Q(root.rotation),
            "\"dp\":" + V(animator.deltaPosition), "\"dr\":" + Q(animator.deltaRotation),
            "\"v\":" + V(animator.velocity), "\"av\":" + V(animator.angularVelocity),
            "\"rootPosition\":" + V(animator.rootPosition), "\"rootRotation\":" + Q(animator.rootRotation),
            "\"t0\":" + F(animator.GetCurrentAnimatorStateInfo(0).normalizedTime),
            "\"matching\":" + (animator.isMatchingTarget ? 1 : 0),
        };
        if (body != null)
        {
            var b = root.Find(body);
            parts.Add("\"body.p\":" + V(b.localPosition));
            parts.Add("\"body.r\":" + Q(b.localRotation));
        }
        var leg = root.Find(body != null ? body + "/Leg" : "Leg");
        parts.Add("\"leg.r\":" + Q(leg.localRotation));
        if (log)
        {
            parts.Add("\"log\":[" + string.Join(",", s_Log.Select(S)) + "]");
            s_Log.Clear();
        }
        return "{" + string.Join(",", parts) + "}";
    }

    /// <summary>Update(0), then twenty of 0.125 s (three frames of the 24 fps take).</summary>
    static void Play(string key, string model, string clip, bool apply, Action<Animator, int> before = null)
    {
        var root = Instantiate(Resources.Load<GameObject>(model));
        root.transform.SetPositionAndRotation(new Vector3(1f, 0f, -2f), Quaternion.Euler(0f, 20f, 0f));
        var animator = root.GetComponent<Animator>();
        if (animator == null) animator = root.AddComponent<Animator>();
        animator.applyRootMotion = apply;
        animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>("C_" + model + "_" + clip);
        string body = model.StartsWith("Walker") ? "Hips" : null;
        bool log = before != null;
        s_Log.Clear();
        Record(key + ".animator", "{\"hasRootMotion\":" + (animator.hasRootMotion ? 1 : 0) + ",\"isHuman\":" + (animator.isHuman ? 1 : 0)
            + ",\"avatar\":" + S(animator.avatar != null ? animator.avatar.name : "") + ",\"humanScale\":" + F(animator.humanScale) + "}");
        for (int step = 0; step <= 20; step++)
        {
            before?.Invoke(animator, step);
            animator.Update(step == 0 ? 0f : 0.125f);
            Record(key + ".step" + step.ToString("00"), Snapshot(animator, body, log));
        }
        DestroyImmediate(root);
    }

    void Start()
    {
        string output = Environment.GetEnvironmentVariable("MODEL_ROOT_OUT") ?? "/tmp/modelroot.jsonl";
        int code = 0;
        Application.logMessageReceived += (message, stack, type) => s_Log.Add(type + ": " + message.Split('\n')[0]);
        try
        {
            foreach (var model in s_Models)
            {
                var rest = Instantiate(Resources.Load<GameObject>(model));
                var node = rest.transform.Find(model.StartsWith("Walker") ? "Hips" : "Leg");
                Record("rest." + model, "{\"root.p\":" + V(rest.transform.localPosition) + ",\"root.r\":" + Q(rest.transform.localRotation)
                    + ",\"root.s\":" + V(rest.transform.localScale) + ",\"node.p\":" + V(node.localPosition) + ",\"node.r\":" + Q(node.localRotation)
                    + ",\"node.s\":" + V(node.localScale) + "}");
                DestroyImmediate(rest);
                Imported(model);
                foreach (var clip in Clips(model))
                    Play("play." + model + "." + clip.name, model, clip.name, apply: true);
                foreach (var clip in new[] { "Full", "BakeAll", "Loop" })
                    Play("still." + model + "." + clip, model, clip, apply: false);
            }
            // Matching on generic rigs: a target for the root, during the clip's first loop.
            foreach (var model in new[] { "Walker", "WalkerNoAvatar", "Mover" })
                Play("match." + model, model, "Loop", apply: true, (animator, step) =>
                {
                    if (step == 2)
                        animator.MatchTarget(new Vector3(3f, 0.5f, 4f), Quaternion.Euler(0f, 90f, 0f), AvatarTarget.Root,
                            new MatchTargetWeightMask(Vector3.one, 1f), 0.3f, 0.8f);
                });
            MatchReviewProbe.Run();
            Record("complete", "true");
        }
        catch (Exception e)
        {
            Debug.LogException(e);
            Record("error", S(e.ToString()));
            code = 1;
        }
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(output)));
        File.WriteAllText(output, s_Output.ToString());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(code);
#else
        Application.Quit(code);
#endif
    }
}
