using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

// Public API observations only. The runner stages this beside the original fixture's prelude.
public static class HumanFirstPoseCapture
{
    static readonly StringBuilder Output = new StringBuilder();
    static int CurveCount, CallbackCount;
    static string Text(string value) => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r") + "\"";
    static string Bool(bool value) => value ? "true" : "false";
    static string Float(float value)
    {
        string number = value.ToString("R", CultureInfo.InvariantCulture);
        if (float.IsNaN(value) || float.IsInfinity(value)) number = Text(number);
        uint bits = BitConverter.ToUInt32(BitConverter.GetBytes(value), 0);
        return "{\"f\":" + number + ",\"bits\":\"0x" + bits.ToString("x8") + "\"}";
    }
    static string Vector(Vector3 v) => "[" + Float(v.x) + "," + Float(v.y) + "," + Float(v.z) + "]";
    static string Rotation(Quaternion q) => "[" + Float(q.x) + "," + Float(q.y) + "," + Float(q.z) + "," + Float(q.w) + "]";
    static void Put(string key, string value) => Output.Append("{\"k\":").Append(Text(key)).Append(",\"v\":").Append(value).Append("}\n");
    public static void Curves()
    {
        var clip = Resources.LoadAll<AnimationClip>("HumanA").Single(c => c.name == "Scene");
        if (!clip.humanMotion || clip.length <= 0) throw new Exception("HumanA Scene is not an active human clip");
        Put("clip", "{\"name\":" + Text(clip.name) + ",\"length\":" + Float(clip.length) + ",\"frameRate\":" + Float(clip.frameRate) + ",\"humanMotion\":" + Bool(clip.humanMotion) + "}");
        foreach (var binding in AnimationUtility.GetCurveBindings(clip).OrderBy(b => b.propertyName, StringComparer.Ordinal))
        {
            if (!binding.propertyName.StartsWith("RootT.", StringComparison.Ordinal) && !binding.propertyName.StartsWith("RootQ.", StringComparison.Ordinal)) continue;
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (binding.path != "" || binding.type != typeof(Animator) || curve == null || curve.length < 2 || curve.keys[0].time != 0)
                throw new Exception("Incomplete root curve: " + binding.propertyName);
            var keys = new List<string>();
            foreach (var k in curve.keys)
                keys.Add("{\"time\":" + Float(k.time) + ",\"value\":" + Float(k.value)
                    + ",\"inTangent\":" + Float(k.inTangent) + ",\"outTangent\":" + Float(k.outTangent)
                    + ",\"inWeight\":" + Float(k.inWeight) + ",\"outWeight\":" + Float(k.outWeight)
                    + ",\"weightedMode\":" + (int)k.weightedMode + "}");
            Put("curve." + binding.propertyName, "{\"path\":" + Text(binding.path) + ",\"type\":" + Text(binding.type.FullName)
                + ",\"evaluate0\":" + Float(curve.Evaluate(0)) + ",\"keys\":[" + string.Join(",", keys) + "]}");
            CurveCount++;
        }
        if (CurveCount != 7) throw new Exception("Expected all seven RootT/RootQ curves, got " + CurveCount);
    }
    public static void Snapshot(string key, Animator animator)
    {
        var t = animator.transform;
        var m = t.localToWorldMatrix;
        var rows = new List<string>();
        for (int r = 0; r < 4; r++)
        {
            var row = new List<string>();
            for (int c = 0; c < 4; c++) row.Add(Float(m[r, c]));
            rows.Add("[" + string.Join(",", row) + "]");
        }
        Put(key, "{\"position\":" + Vector(t.position) + ",\"localPosition\":" + Vector(t.localPosition)
            + ",\"rotation\":" + Rotation(t.rotation) + ",\"localRotation\":" + Rotation(t.localRotation)
            + ",\"localScale\":" + Vector(t.localScale) + ",\"lossyScale\":" + Vector(t.lossyScale)
            + ",\"localToWorldMatrix\":[" + string.Join(",", rows) + "],\"hasParent\":" + Bool(t.parent != null)
            + ",\"humanScale\":" + Float(animator.humanScale) + ",\"bodyPosition\":" + Vector(animator.bodyPosition)
            + ",\"bodyRotation\":" + Rotation(animator.bodyRotation) + ",\"rootPosition\":" + Vector(animator.rootPosition)
            + ",\"rootRotation\":" + Rotation(animator.rootRotation) + "}");
        if (!animator.isHuman || !animator.avatar.isValid || animator.humanScale <= 0 || animator.GetBoneTransform(HumanBodyBones.Hips) == null)
            throw new Exception("HumanA rig inactive");
        if (key.EndsWith(".after", StringComparison.Ordinal))
        {
            var clips = animator.GetCurrentAnimatorClipInfo(0);
            if (clips.Length != 1 || clips[0].clip.name != "Scene" || clips[0].weight <= 0)
                throw new Exception("Expected the active Scene clip on layer zero");
            Put(key + ".active", "{\"clip\":" + Text(clips[0].clip.name) + ",\"weight\":" + Float(clips[0].weight)
                + ",\"normalizedTime\":" + Float(animator.GetCurrentAnimatorStateInfo(0).normalizedTime)
                + ",\"applyRootMotion\":" + Bool(animator.applyRootMotion) + ",\"enabled\":" + Bool(animator.enabled) + "}");
        }
    }
    public static void Callback(Animator animator, int layer)
    {
        Put("ik.callback." + CallbackCount++, "{\"layer\":" + layer + ",\"bodyPosition\":" + Vector(animator.bodyPosition) + ",\"bodyRotation\":" + Rotation(animator.bodyRotation) + "}");
    }
    public static void Finish()
    {
        if (CurveCount != 7 || CallbackCount != 1) throw new Exception("Incomplete curves or zero-time IK callback");
        Put("complete", "{\"curves\":" + CurveCount + ",\"callbacks\":" + CallbackCount + ",\"updates\":2,\"elapsedTime\":0}");
        Save();
    }
    public static void Fail(Exception error) { Put("exception", Text(error.ToString())); Save(); }
    static void Save()
    {
        string path = Environment.GetEnvironmentVariable("HUMAN_FIRST_POSE_OUT");
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path)));
        File.WriteAllText(path, Output.ToString());
    }
}
