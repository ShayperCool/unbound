using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;

/// <summary>Records the clips the model importer made from Rig.fbx's clip settings (Editor data:
/// settings, bindings, curve samples, events) and plays each in an Animator with fixed
/// Animator.Update steps, recording the animated transforms, the parameters the clip curves
/// drive, the state's time and the events received.</summary>
public class ClipProbe : MonoBehaviour
{
    static readonly StringBuilder s_Output = new StringBuilder();
    static readonly string[] s_Nodes = { "Hips", "Hips/Spine", "Hips/Spine/Head", "Prop", "Spin", "Still" };
    static string F(float f) => f.ToString("R", CultureInfo.InvariantCulture);
    static string V(Vector3 v) => "[" + F(v.x) + "," + F(v.y) + "," + F(v.z) + "]";
    static string Q(Quaternion q) => "[" + F(q.x) + "," + F(q.y) + "," + F(q.z) + "," + F(q.w) + "]";
    static string S(string s) => "\"" + (s ?? "").Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

    static void Record(string key, string value) =>
        s_Output.Append("{\"k\":").Append(S(key)).Append(",\"v\":").Append(value).Append("}\n");

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run() => new GameObject("ClipProbe").AddComponent<ClipProbe>();

    static IEnumerable<AnimationClip> Clips(string model) =>
        Resources.LoadAll<AnimationClip>(model).Where(c => !c.name.StartsWith("__preview__")).OrderBy(c => c.name, StringComparer.Ordinal);

    static string Events(AnimationEvent[] events) => "[" + string.Join(",", events.Select(e =>
        "[" + F(e.time) + "," + S(e.functionName) + "," + F(e.floatParameter) + "," + e.intParameter + "," + S(e.stringParameter) + "]")) + "]";

    /// <summary>The clips of a model; with <paramref name="samples"/> also their curves' values
    /// (not for reduced curves, whose keys depend on the reduction).</summary>
    static void Imported(string model, bool samples)
    {
        Record("clips." + model, "[" + string.Join(",", Resources.LoadAll<AnimationClip>(model).Select(c => S(c.name)).OrderBy(n => n, StringComparer.Ordinal)) + "]");
#if UNITY_EDITOR
        string path = "Assets/Resources/" + model + ".fbx";
        Record("assets." + model, "{\"all\":[" + string.Join(",", UnityEditor.AssetDatabase.LoadAllAssetsAtPath(path).OfType<AnimationClip>()
                .Select(c => S(c.name + "|" + (int)c.hideFlags)).OrderBy(n => n, StringComparer.Ordinal))
            + "],\"representations\":[" + string.Join(",", UnityEditor.AssetDatabase.LoadAllAssetRepresentationsAtPath(path).OfType<AnimationClip>()
                .Select(c => S(c.name)).OrderBy(n => n, StringComparer.Ordinal)) + "]}");
#endif
        foreach (var clip in Clips(model))
        {
            string key = "clip." + model + "." + clip.name;
            Record(key, "{\"length\":" + F(clip.length) + ",\"frameRate\":" + F(clip.frameRate) + ",\"isLooping\":" + (clip.isLooping ? 1 : 0)
                + ",\"wrapMode\":" + (int)clip.wrapMode + ",\"legacy\":" + (clip.legacy ? 1 : 0) + ",\"empty\":" + (clip.empty ? 1 : 0)
                + ",\"humanMotion\":" + (clip.humanMotion ? 1 : 0) + ",\"hasGenericRootTransform\":" + (clip.hasGenericRootTransform ? 1 : 0)
                + ",\"hasMotionCurves\":" + (clip.hasMotionCurves ? 1 : 0) + ",\"hasRootCurves\":" + (clip.hasRootCurves ? 1 : 0)
                + ",\"hasMotionFloatCurves\":" + (clip.hasMotionFloatCurves ? 1 : 0) + ",\"events\":" + Events(clip.events) + "}");
#if UNITY_EDITOR
            var s = UnityEditor.AnimationUtility.GetAnimationClipSettings(clip);
            Record(key + ".settings", "{\"startTime\":" + F(s.startTime) + ",\"stopTime\":" + F(s.stopTime) + ",\"loopTime\":" + (s.loopTime ? 1 : 0)
                + ",\"loopBlend\":" + (s.loopBlend ? 1 : 0) + ",\"cycleOffset\":" + F(s.cycleOffset) + ",\"mirror\":" + (s.mirror ? 1 : 0)
                + ",\"hasAdditiveReferencePose\":" + (s.hasAdditiveReferencePose ? 1 : 0) + ",\"additiveReferencePoseTime\":" + F(s.additiveReferencePoseTime)
                + ",\"additiveReferencePoseClip\":" + S(s.additiveReferencePoseClip != null ? s.additiveReferencePoseClip.name : "")
                + ",\"loopBlendOrientation\":" + (s.loopBlendOrientation ? 1 : 0) + ",\"loopBlendPositionY\":" + (s.loopBlendPositionY ? 1 : 0)
                + ",\"loopBlendPositionXZ\":" + (s.loopBlendPositionXZ ? 1 : 0) + ",\"keepOriginalOrientation\":" + (s.keepOriginalOrientation ? 1 : 0)
                + ",\"keepOriginalPositionY\":" + (s.keepOriginalPositionY ? 1 : 0) + ",\"keepOriginalPositionXZ\":" + (s.keepOriginalPositionXZ ? 1 : 0)
                + ",\"heightFromFeet\":" + (s.heightFromFeet ? 1 : 0) + ",\"orientationOffsetY\":" + F(s.orientationOffsetY) + ",\"level\":" + F(s.level) + "}");
            var bindings = UnityEditor.AnimationUtility.GetCurveBindings(clip)
                .OrderBy(b => b.path, StringComparer.Ordinal).ThenBy(b => b.propertyName, StringComparer.Ordinal).ToArray();
            Record(key + ".bindings", "[" + string.Join(",", bindings.Select(b => S(b.path + "|" + (b.type != null ? b.type.Name : "") + "|" + b.propertyName))) + "]");
            Record(key + ".objectBindings", "[" + string.Join(",", UnityEditor.AnimationUtility.GetObjectReferenceCurveBindings(clip).Select(b => S(b.path + "|" + b.propertyName))) + "]");
            // Curve values on every frame and between frames, from the clip's start.
            int frames = Mathf.CeilToInt(clip.length * clip.frameRate - 1e-3f);
            foreach (var b in samples ? bindings : new UnityEditor.EditorCurveBinding[0])
            {
                var curve = UnityEditor.AnimationUtility.GetEditorCurve(clip, b);
                var values = new List<string>();
                for (int i = 0; i <= 2 * frames; i++) values.Add(F(curve.Evaluate(i * 0.5f / clip.frameRate)));
                Record(key + ".curve." + b.path + "|" + b.propertyName, "{\"first\":" + F(curve.length > 0 ? curve[0].time : -1f)
                    + ",\"last\":" + F(curve.length > 0 ? curve[curve.length - 1].time : -1f) + ",\"values\":[" + string.Join(",", values) + "]}");
            }
#endif
        }
    }

    static string Snapshot(Animator animator, ClipReceiver receiver)
    {
        var parts = new List<string>();
        foreach (var path in s_Nodes)
        {
            var t = animator.transform.Find(path);
            string n = path.Substring(path.LastIndexOf('/') + 1);
            parts.Add("\"" + n + ".p\":" + V(t.localPosition));
            parts.Add("\"" + n + ".r\":" + Q(t.localRotation));
            parts.Add("\"" + n + ".s\":" + V(t.localScale));
        }
        parts.Add("\"Speed\":" + F(animator.GetFloat("Speed")));
        parts.Add("\"Count\":" + animator.GetInteger("Count"));
        parts.Add("\"On\":" + (animator.GetBool("On") ? 1 : 0));
        for (int layer = 0; layer < animator.layerCount; layer++)
        {
            var state = animator.GetCurrentAnimatorStateInfo(layer);
            parts.Add("\"t" + layer + "\":" + F(state.normalizedTime));
            parts.Add("\"len" + layer + "\":" + F(state.length));
            parts.Add("\"loop" + layer + "\":" + (state.loop ? 1 : 0));
        }
        parts.Add("\"events\":[" + string.Join(",", receiver.Received) + "]");
        receiver.Received.Clear();
        return "{" + string.Join(",", parts) + "}";
    }

    /// <summary>Update(0), then twenty of 0.125 s (three frames of the 24 fps take).</summary>
    static void Play(string key, string model, string controller)
    {
        var root = Instantiate(Resources.Load<GameObject>(model));
        var receiver = root.AddComponent<ClipReceiver>();
        var animator = root.GetComponent<Animator>();
        if (animator == null) animator = root.AddComponent<Animator>();
        animator.runtimeAnimatorController = Resources.Load<RuntimeAnimatorController>(controller);
        for (int step = 0; step <= 20; step++)
        {
            animator.Update(step == 0 ? 0f : 0.125f);
            Record(key + ".step" + step.ToString("00"), Snapshot(animator, receiver));
        }
        DestroyImmediate(root);
    }

    void Start()
    {
        string output = Environment.GetEnvironmentVariable("MODEL_CLIPS_OUT") ?? "/tmp/modelclips.jsonl";
        int code = 0;
        try
        {
            Imported("Rig", true);
            Imported("RigDefault", true);
            Imported("RigReduced", false);
            Imported("RigOptimal", false);
            // The rest pose of the model, for the masked and unanimated transforms.
            var rest = Instantiate(Resources.Load<GameObject>("Rig"));
            Record("rest", "{" + string.Join(",", s_Nodes.Select(p =>
            {
                var t = rest.transform.Find(p);
                string n = p.Substring(p.LastIndexOf('/') + 1);
                return "\"" + n + ".p\":" + V(t.localPosition) + ",\"" + n + ".r\":" + Q(t.localRotation) + ",\"" + n + ".s\":" + V(t.localScale);
            })) + ",\"animator\":" + (rest.GetComponent<Animator>() != null ? 1 : 0) + "}");
            DestroyImmediate(rest);
            foreach (var clip in Clips("Rig"))
                Play("play." + clip.name, "Rig", "C_" + clip.name);
            Play("play.Default", "RigDefault", "C_Default");
            foreach (var name in new[] { "AddRef", "AddRefRange", "AddRefOut", "AddRefOff", "Range" })
                Play("additive." + name, "Rig", "A_" + name);
            foreach (var name in new[] { "MaskModel", "MaskAsset", "MaskStale", "MaskNone" })
                Play("override." + name, "Rig", "O_" + name);
            foreach (var name in new[] { "Cycle", "CycleOnce", "Loop", "Full" })
                Play("stateoffset." + name, "Rig", "S_" + name);
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
