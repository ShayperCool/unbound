using System;
using System.Collections.Generic;
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
/// SetKeyRightTangentMode, SetKeyBroken: the VFX Graph editor calls them on import),
/// AnimationUtility.GetClosestEuler for rotations, hints and every rotation order (the Timeline
/// editor), and AnimationMode's recording state and events.
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

        // GetClosestEuler(Quaternion, Vector3 hint, RotationOrder) for every order.
        var orderType = typeof(Transform).Assembly.GetType("UnityEngine.RotationOrder");
        var closest = orderType == null ? null : util.GetMethod("GetClosestEuler", Internal, null, new[] { typeof(Quaternion), typeof(Vector3), orderType }, null);
        Put("euler.found", (closest != null).ToString());
        // Rotations as exact components (identity, Euler angles of edge cases, a non-unit one,
        // pseudo-random ones), so no platform's trigonometry changes the inputs.
        var list = new List<Quaternion>
        {
            new Quaternion(0f, 0f, 0f, 1f),
            new Quaternion(0.127679437f, 0.144878134f, 0.239298329f, 0.9515485f),
            new Quaternion(0.6560304f, 0.646379352f, 0.316920042f, -0.226670682f),
            new Quaternion(0.683012664f, 0.1830127f, -0.1830127f, 0.683012664f),
            new Quaternion(-0.65328145f, 0.270598054f, 0.270598054f, 0.65328145f),
            new Quaternion(0f, 0.707106769f, 0f, 0.707106769f),
            new Quaternion(0f, 0f, 0.707106769f, 0.707106769f),
            new Quaternion(0.958072f, -0.286525071f, 0.00108612224f, 0.00058605813f),
            new Quaternion(0.1f, 0.2f, 0.3f, 0.4f),
            new Quaternion(0.7007737f, -0.106495023f, -0.177249968f, -0.682757139f),
            new Quaternion(0.5335311f, 0.5078304f, -0.672893941f, -0.06831254f),
            new Quaternion(-0.368966371f, -0.0574904531f, 0.7277148f, -0.575317264f),
            new Quaternion(-0.6036935f, -0.485209584f, -0.466605663f, 0.427088857f),
            new Quaternion(-0.6007122f, 0.2706782f, -0.324937224f, 0.67844975f),
            new Quaternion(-0.560747862f, 0.5603556f, 0.514917254f, 0.326226383f),
            new Quaternion(0.0909436345f, 0.735790253f, 0.662983537f, -0.10389775f),
            new Quaternion(-0.06749598f, -0.833851039f, 0.409778774f, -0.363618016f),
            new Quaternion(-0.657098651f, -0.061826095f, -0.106742159f, 0.743643045f),
            new Quaternion(0.0893039f, 0.17536059f, -0.9650367f, -0.1731406f),
            new Quaternion(-0.303511679f, 0.08976113f, -0.6971715f, 0.643253863f),
            new Quaternion(-0.778239548f, 0.05234967f, 0.229577258f, -0.5821486f),
            // Rotations whose angles lie near a thousandth's half, where an arctangent's last bit
            // shows.
            new Quaternion(0.3075224f, 0.30193585f, -0.8854647f, 0.17383023f),
            new Quaternion(0.3049056f, 0.08317623f, 0.8655372f, -0.3885352f),
            new Quaternion(-0.5240452f, -0.35156822f, 0.75772625f, 0.16621462f),
            new Quaternion(-0.8252361f, -0.5242575f, 0.20936403f, -0.017498497f),
            new Quaternion(-0.6025253f, 0.47398466f, 0.62217283f, -0.15875377f),
            new Quaternion(-0.20402883f, -0.032547478f, 0.54919827f, -0.8097494f),
            new Quaternion(0.36556977f, -0.73564744f, 0.5324037f, -0.20427409f),
            new Quaternion(-0.988881f, 0.13455237f, 0.012223249f, 0.062134173f),
            new Quaternion(0.39514688f, -0.06854841f, -0.6218201f, 0.6726811f),
            new Quaternion(-0.6741339f, 0.7216502f, 0.030170722f, -0.15444821f),
            new Quaternion(-0.83793324f, -0.25894827f, 0.09062809f, -0.4718053f),
            new Quaternion(-0.9019102f, -0.04156772f, 0.39662582f, -0.16588597f),
            // Euler angles (89.8, 30, 20), (89.88, 30, 20) and (89.9, 30, 20): either side of gimbal lock.
            new Quaternion(0.7032964f, 0.06214989f, -0.061106756f, 0.70553344f),
            new Quaternion(0.70374453f, 0.061941322f, -0.061315443f, 0.70508677f),
            new Quaternion(0.7038565f, 0.061889175f, -0.06136761f, 0.704975f),
        };
        for (int r = 0; r < list.Count; r++)
            Put($"euler.q.{r}", F(list[r].x) + " " + F(list[r].y) + " " + F(list[r].z) + " " + F(list[r].w));
        var hints = new[] { Vector3.zero, new Vector3(360f, 0f, 0f), new Vector3(-720f, 400f, 10f), new Vector3(170f, 170f, 170f), new Vector3(1000f, -1000f, 0.5f) };
        foreach (var order in Enum.GetValues(orderType))
            for (int r = 0; r < list.Count; r++)
                for (int h = 0; h < hints.Length; h++)
                {
                    var q = list[r];
                    var hint = hints[h];
                    Put($"euler.{order}.{r}.{h}", Try(() => V((Vector3)closest.Invoke(null, new object[] { q, hint, order }))));
                }
        // Hints that are (or nearly are) the rotation itself (Euler angles 10 20 30, 90 30 20 and
        // 170 170 170), in gimbal lock too: 0.0004 and 0.002 degrees off, a tenth of a degree
        // off, the same angles in another order, then 0.0008 and 0.0012 degrees off.
        var near = new (Quaternion Rotation, Vector3 Hint, string Order)[]
        {
            (new Quaternion(0.127679437f, 0.144878134f, 0.239298329f, 0.9515485f), new Vector3(370f, -340f, 30.0004f), "OrderZXY"),
            (new Quaternion(0.127679437f, 0.144878134f, 0.239298329f, 0.9515485f), new Vector3(370f, -340f, 30.002f), "OrderZXY"),
            (new Quaternion(0.127679437f, 0.144878134f, 0.239298329f, 0.9515485f), new Vector3(10f, 20f, 30f), "OrderXYZ"),
            (new Quaternion(0.704416f, 0.06162841f, -0.06162841f, 0.704416f), new Vector3(90f, 30f, 20f), "OrderZXY"),
            (new Quaternion(0.704416f, 0.06162841f, -0.06162841f, 0.704416f), new Vector3(90f, 30.1f, 20f), "OrderZXY"),
            (new Quaternion(0.09406099f, -0.07892653f, -0.07892653f, 0.9892896f), new Vector3(170f, 170f, 170f), "OrderZXY"),
            (new Quaternion(0.127679437f, 0.144878134f, 0.239298329f, 0.9515485f), new Vector3(370f, -340f, 30.0008f), "OrderZXY"),
            (new Quaternion(0.127679437f, 0.144878134f, 0.239298329f, 0.9515485f), new Vector3(370f, -340f, 30.0012f), "OrderZXY"),
        };
        for (int n = 0; n < near.Length; n++)
        {
            var c = near[n];
            var q = c.Rotation;
            var order = Enum.Parse(orderType, c.Order);
            Put($"euler.near.{n}", F(q.x) + " " + F(q.y) + " " + F(q.z) + " " + F(q.w) + " -> "
                + Try(() => V((Vector3)closest.Invoke(null, new object[] { q, c.Hint, order }))));
        }

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
