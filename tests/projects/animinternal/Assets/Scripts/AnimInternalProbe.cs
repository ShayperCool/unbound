using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Records, in Play mode, what internal editor animation helpers that packages call answer:
/// AnimationUtility's key overloads taking a Keyframe by reference (SetKeyLeftTangentMode,
/// SetKeyRightTangentMode, SetKeyBroken: the VFX Graph editor calls them on import), and
/// AnimationMode's recording state and events (the Timeline editor).
/// </summary>
static class AnimInternalProbe
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;
    const BindingFlags Internal = BindingFlags.NonPublic | BindingFlags.Static;

    static string F(float f) => f.ToString("R", Inv);
    static string V(Vector3 v) => F(v.x) + " " + F(v.y) + " " + F(v.z);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("ANIM_INTERNAL_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var output = new StringBuilder();
        void Put(string key, string value) => output.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"}\n");
        string Try(Func<string> read) { try { return read(); } catch (Exception e) { var inner = e is TargetInvocationException t && t.InnerException != null ? t.InnerException : e; return "throws " + inner.GetType().Name; } }

        var util = typeof(AnimationUtility);
        var byRef = typeof(Keyframe).MakeByRefType();
        var setLeft = util.GetMethod("SetKeyLeftTangentMode", Internal, null, new[] { byRef, typeof(AnimationUtility.TangentMode) }, null);
        var setRight = util.GetMethod("SetKeyRightTangentMode", Internal, null, new[] { byRef, typeof(AnimationUtility.TangentMode) }, null);
        var setBroken = util.GetMethod("SetKeyBroken", Internal, null, new[] { byRef, typeof(bool) }, null);
        Put("found", (setLeft != null) + " " + (setRight != null) + " " + (setBroken != null));

#pragma warning disable CS0618 // Keyframe.tangentMode: the raw mode bits
        string Key(Keyframe k) => k.tangentMode + " " + F(k.inTangent) + " " + F(k.outTangent) + " " + (int)k.weightedMode;
        // Starting keys: plain, with every mode bit set, broken, and with tangents and weights.
        var starts = new[]
        {
            new Keyframe(1f, 2f),
            new Keyframe(1f, 2f, 0.5f, -0.25f) { tangentMode = 0x1FF },
            new Keyframe(1f, 2f, 3f, 4f) { tangentMode = 1 },
            new Keyframe(1f, 2f, 3f, 4f, 0.2f, 0.7f) { weightedMode = WeightedMode.Both, tangentMode = 2 << 1 },
        };
#pragma warning restore CS0618
        var modes = (AnimationUtility.TangentMode[])Enum.GetValues(typeof(AnimationUtility.TangentMode));
        for (int s = 0; s < starts.Length; s++)
        {
            foreach (var mode in modes)
            {
                object[] left = { starts[s], mode };
                Put($"key.{s}.left.{mode}", Try(() => { setLeft.Invoke(null, left); return Key((Keyframe)left[0]); }));
                object[] right = { starts[s], mode };
                Put($"key.{s}.right.{mode}", Try(() => { setRight.Invoke(null, right); return Key((Keyframe)right[0]); }));
            }
            foreach (var broken in new[] { true, false })
            {
                object[] args = { starts[s], broken };
                Put($"key.{s}.broken.{broken}", Try(() => { setBroken.Invoke(null, args); return Key((Keyframe)args[0]); }));
            }
        }
        // An out-of-range mode value.
        object[] odd = { new Keyframe(0f, 0f), (AnimationUtility.TangentMode)31 };
        Put("key.left.31", Try(() => { setLeft.Invoke(null, odd); return Key((Keyframe)odd[0]); }));

        // AnimationMode's recording state and its events.
        var modeType = typeof(AnimationMode);
        string Call(string name) => Try(() => Convert.ToString(modeType.GetMethod(name, Internal).Invoke(null, null), Inv) ?? "void");
        int starts2 = 0, stops = 0;
        Action onStart = () => starts2++;
        Action onStop = () => stops++;
        var startEvent = modeType.GetEvent("onAnimationRecordingStart", Internal);
        var stopEvent = modeType.GetEvent("onAnimationRecordingStop", Internal);
        Put("record.events", Try(() => { startEvent.AddMethod.Invoke(null, new object[] { onStart }); stopEvent.AddMethod.Invoke(null, new object[] { onStop }); return "added"; }));
        Put("record.initial", Call("InAnimationRecording") + " mode " + AnimationMode.InAnimationMode());
        Put("record.start", Call("StartAnimationRecording") + " " + Call("InAnimationRecording") + " mode " + AnimationMode.InAnimationMode() + " events " + starts2 + "/" + stops);
        Put("record.startAgain", Call("StartAnimationRecording") + " " + Call("InAnimationRecording") + " events " + starts2 + "/" + stops);
        Put("record.stop", Call("StopAnimationRecording") + " " + Call("InAnimationRecording") + " events " + starts2 + "/" + stops);
        Put("record.stopAgain", Call("StopAnimationRecording") + " " + Call("InAnimationRecording") + " events " + starts2 + "/" + stops);
        AnimationMode.StartAnimationMode();
        Put("record.inMode", Call("StartAnimationRecording") + " " + Call("InAnimationRecording") + " mode " + AnimationMode.InAnimationMode() + " events " + starts2 + "/" + stops);
        AnimationMode.StopAnimationMode();
        Put("record.afterModeStop", Call("InAnimationRecording") + " mode " + AnimationMode.InAnimationMode() + " events " + starts2 + "/" + stops);
        Call("StopAnimationRecording");
        Put("record.removed", Try(() => { startEvent.RemoveMethod.Invoke(null, new object[] { onStart }); stopEvent.RemoveMethod.Invoke(null, new object[] { onStop }); Call("StartAnimationRecording"); Call("StopAnimationRecording"); return "events " + starts2 + "/" + stops; }));

        output.Append("{\"k\":\"complete\",\"v\":true}\n");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), output.ToString());
        EditorApplication.Exit(0);
    }
}
