using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Records, in Play mode, what the Recorder editor's GameObjectRecorder answers: its bindings
/// (BindComponent, BindAll, BindComponentsOfType, Bind), its recording state (TakeSnapshot,
/// currentTime, isRecording, ResetRecording), its argument checks, and the clips SaveToClip makes
/// of recorded Transform, script, Light, active-state and sprite values: the curves' bindings and
/// every key (time, value, tangents, tangent mode, weights), at several frame rates and with the
/// CurveFilterOptions on and off. Keys are JSON numbers: the gate compares them within its
/// tolerance and each curve's key count exactly.
/// </summary>
static class AnimRecorderProbe
{
    static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    static string F(float f) => f.ToString("R", Inv);

    /// <summary>A number as JSON (Infinity and NaN as the comparator reads them).</summary>
    static string N(float f) => f.ToString("R", Inv);

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Record()
    {
        var directory = Environment.GetEnvironmentVariable("ANIM_RECORDER_OUT");
        if (string.IsNullOrEmpty(directory)) return;
        var output = new StringBuilder();
        void Put(string key, string value) => output.Append("{\"k\":\"").Append(key).Append("\",\"v\":\"").Append(value.Replace("\\", "\\\\").Replace("\"", "\\\"")).Append("\"}\n");
        // Numbers compare within the run's tolerance; a list's length (a curve's key count) exactly.
        void PutJson(string key, string json) => output.Append("{\"k\":\"").Append(key).Append("\",\"v\":").Append(json).Append("}\n");
        string Try(Func<string> read) { try { return read(); } catch (Exception e) { var inner = e is TargetInvocationException t && t.InnerException != null ? t.InnerException : e; return "throws " + inner.GetType().Name; } }

        string B(EditorCurveBinding b) => b.path + "|" + (b.type == null ? "null" : b.type.Name) + "|" + b.propertyName + "|" + (b.isPPtrCurve ? "pptr" : b.isDiscreteCurve ? "discrete" : "float");
        string Bindings(GameObjectRecorder r) => r.GetBindings().Length + ":" + string.Join(",", r.GetBindings().Select(B));
#pragma warning disable CS0618 // Keyframe.tangentMode: the raw mode bits
        string Key(Keyframe k) => "[" + N(k.time) + "," + N(k.value) + "," + N(k.inTangent) + "," + N(k.outTangent) + "," + k.tangentMode + "," + (int)k.weightedMode + "," + N(k.inWeight) + "," + N(k.outWeight) + "]";
#pragma warning restore CS0618
        int modified = 0;
        AnimationUtility.onCurveWasModified += (clip, binding, type) => modified++;

        // Every curve of a clip: its bindings in order, then each curve's keys.
        void Clip(string name, AnimationClip clip)
        {
            PutJson(name + ".frameRate", N(clip.frameRate));
            PutJson(name + ".length", N(clip.length));
            var floats = AnimationUtility.GetCurveBindings(clip);
            Put(name + ".curves", floats.Length + ":" + string.Join(",", floats.Select(B)));
            foreach (var b in floats)
            {
                var curve = AnimationUtility.GetEditorCurve(clip, b);
                PutJson(name + ".curve." + b.path + ":" + b.propertyName, curve == null ? "null" : "[" + string.Join(",", curve.keys.Select(Key)) + "]");
            }
            var objects = AnimationUtility.GetObjectReferenceCurveBindings(clip);
            Put(name + ".objectCurves", objects.Length + ":" + string.Join(",", objects.Select(B)));
            foreach (var b in objects)
            {
                var keys = AnimationUtility.GetObjectReferenceCurve(clip, b);
                Put(name + ".objectCurve." + b.path + ":" + b.propertyName, keys.Length + ": " + string.Join(" | ", keys.Select(k => F(k.time) + " " + (k.value == null ? "null" : k.value.name))));
            }
        }
        void Save(string name, GameObjectRecorder recorder, Func<AnimationClip, int> save)
        {
            var clip = new AnimationClip();
            int before = modified;
            Put(name + ".save", Try(() => { save(clip); return "void"; }) + " events " + (modified - before));
            Clip(name, clip);
        }
        CurveFilterOptions Options(bool reduce, float position = 0.5f, float rotation = 0.5f, float scale = 0.5f, float value = 0.5f, bool unroll = true) =>
            new CurveFilterOptions { keyframeReduction = reduce, positionError = position, rotationError = rotation, scaleError = scale, floatError = value, unrollRotation = unroll };

        // ---- Construction and argument checks.
        Put("api.ctor.null", Try(() => { new GameObjectRecorder(null); return "void"; }));
        var apiRoot = new GameObject("apiRoot");
        var api = new GameObjectRecorder(apiRoot);
        Put("api.root", api.root == null ? "null" : api.root.name);
        Put("api.fresh", api.isRecording + " " + F(api.currentTime) + " " + Bindings(api));
        Put("api.snapshot.unbound", Try(() => { api.TakeSnapshot(0.5f); return api.isRecording + " " + F(api.currentTime); }));
        Put("api.save.unrecorded", Try(() => { api.SaveToClip(new AnimationClip()); return "void"; }));
        Put("api.save.fps0", Try(() => { api.SaveToClip(new AnimationClip(), 0f); return "void"; }));
        Put("api.save.fpsNegative", Try(() => { api.SaveToClip(new AnimationClip(), -1f, Options(true)); return "void"; }));
        Put("api.save.negativeError", Try(() => { api.SaveToClip(new AnimationClip(), 30f, Options(true, value: -1f)); return "void"; }));
        Put("api.save.negativeErrorUnreduced", Try(() => { api.SaveToClip(new AnimationClip(), 30f, Options(false, value: -1f)); return "void"; }));
        Put("api.bind.nonObject", Try(() => { api.Bind(EditorCurveBinding.FloatCurve("", typeof(int), "value")); return Bindings(api); }));
        Put("api.bind.nullType", Try(() => { api.Bind(new EditorCurveBinding { path = "", propertyName = "m_LocalPosition.x" }); return Bindings(api); }));
        Put("api.bindComponent.null", Try(() => { api.BindComponent(null); return Bindings(api); }));
        Put("api.bindAll.null", Try(() => { api.BindAll(null, true); return Bindings(api); }));
        Put("api.bindOfType.null", Try(() => { api.BindComponentsOfType<Transform>(null, true); return Bindings(api); }));
        Put("api.bind.animatorEnabled", Try(() => { api.Bind(EditorCurveBinding.FloatCurve("", typeof(Animator), "m_Enabled")); return Bindings(api); }));
        Put("api.bind.transformEnabled", Try(() => { api.Bind(EditorCurveBinding.FloatCurve("", typeof(Transform), "m_Enabled")); return Bindings(api); }));
        Put("api.bind.lightEnabled", Try(() => { api.Bind(EditorCurveBinding.FloatCurve("", typeof(Light), "m_Enabled")); return Bindings(api); }));
        Put("api.bind.twice", Try(() => { api.Bind(EditorCurveBinding.FloatCurve("", typeof(Light), "m_Enabled")); return Bindings(api); }));
        Put("api.bind.pptr", Try(() => { api.Bind(EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite")); return Bindings(api); }));
        Put("api.bind.floatBeforePptr", Try(() => { api.Bind(EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.x")); return Bindings(api); }));
        Put("api.save.null", Try(() => { api.TakeSnapshot(0f); int before = modified; api.SaveToClip(null); return "void events " + (modified - before); }));
        Put("api.reset", Try(() => { api.TakeSnapshot(0.25f); var time = F(api.currentTime); api.ResetRecording(); return time + " " + api.isRecording + " " + F(api.currentTime) + " " + Bindings(api); }));
        Put("api.reset.snapshot", Try(() => { api.TakeSnapshot(0.25f); return api.isRecording + " " + F(api.currentTime); }));
        UnityEngine.Object.DestroyImmediate(apiRoot);

        // ---- Bindings of a hierarchy: root (script, Light, Animator), child (SpriteRenderer),
        // grandchild.
        var root = new GameObject("root");
        var fields = root.AddComponent<AnimRecorderFields>();
        var light = root.AddComponent<Light>();
        root.AddComponent<Animator>();
        var child = new GameObject("child");
        child.transform.SetParent(root.transform, false);
        var sprites = child.AddComponent<SpriteRenderer>();
        var grandchild = new GameObject("grandchild");
        grandchild.transform.SetParent(child.transform, false);
        var outside = new GameObject("outside");
        {
            var r = new GameObjectRecorder(root);
            r.BindComponent(root.transform);
            Put("bind.transform", Bindings(r));
            r.BindComponent(root.transform);
            Put("bind.transformTwice", Bindings(r));
            r.BindComponent(child.transform);
            Put("bind.childTransform", Bindings(r));
            r = new GameObjectRecorder(root);
            r.BindComponent(fields);
            Put("bind.script", Bindings(r));
            r = new GameObjectRecorder(root);
            r.BindComponent(light);
            Put("bind.light", Bindings(r));
            r = new GameObjectRecorder(root);
            r.BindComponent(root.GetComponent<Animator>());
            Put("bind.animator", Bindings(r));
            r = new GameObjectRecorder(root);
            r.BindComponent(sprites);
            Put("bind.sprite", Bindings(r));
            r = new GameObjectRecorder(root);
            r.BindAll(root, false);
            Put("bind.all", Bindings(r));
            r = new GameObjectRecorder(root);
            r.BindAll(child, true);
            Put("bind.allChild", Bindings(r));
            r = new GameObjectRecorder(root);
            r.BindAll(root, true);
            Put("bind.allRecursive", Bindings(r));
            r = new GameObjectRecorder(root);
            r.BindComponentsOfType<Transform>(root, true);
            Put("bind.ofTypeTransform", Bindings(r));
            r = new GameObjectRecorder(root);
            r.BindComponentsOfType(root, typeof(Renderer), true);
            Put("bind.ofTypeRenderer", Bindings(r));
            r = new GameObjectRecorder(root);
            r.BindComponentsOfType<Transform>(root, false);
            Put("bind.ofTypeShallow", Bindings(r));
            r = new GameObjectRecorder(child);
            r.BindComponent(root.transform);
            Put("bind.aboveRoot", Bindings(r));
            r = new GameObjectRecorder(root);
            r.BindComponent(outside.transform);
            Put("bind.outside", Bindings(r));
        }

        // ---- Bind beside BindComponent: which bindings count as one already bound.
        {
            var r = new GameObjectRecorder(root);
            r.BindComponent(light);
            r.Bind(EditorCurveBinding.FloatCurve("", typeof(Light), "m_Intensity"));
            r.Bind(EditorCurveBinding.FloatCurve("", typeof(Light), "m_Enabled"));
            Put("same.light", Bindings(r));
            r = new GameObjectRecorder(root);
            r.BindComponent(root.transform);
            r.Bind(EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalRotation.x"));
            r.Bind(EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalScale.z"));
            r.Bind(EditorCurveBinding.FloatCurve("child", typeof(Transform), "m_LocalPosition.x"));
            r.BindComponent(child.transform);
            Put("same.transform", Bindings(r));
            r = new GameObjectRecorder(root);
            r.Bind(EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.y"));
            r.BindComponent(root.transform);
            Put("same.bindFirst", Bindings(r));
            r = new GameObjectRecorder(root);
            r.BindComponent(fields);
            r.Bind(EditorCurveBinding.FloatCurve("", typeof(AnimRecorderFields), "speed"));
            r.Bind(EditorCurveBinding.FloatCurve("", typeof(AnimRecorderFields), "m_Enabled"));
            r.Bind(EditorCurveBinding.DiscreteCurve("", typeof(AnimRecorderFields), "mode"));
            r.Bind(EditorCurveBinding.FloatCurve("", typeof(AnimRecorderFields), "mode"));
            Put("same.script", Bindings(r));
            r = new GameObjectRecorder(root);
            r.BindComponent(sprites);
            r.Bind(EditorCurveBinding.PPtrCurve("child", typeof(SpriteRenderer), "m_Sprite"));
            r.Bind(EditorCurveBinding.FloatCurve("child", typeof(SpriteRenderer), "m_Color.r"));
            Put("same.sprite", Bindings(r).Length.ToString(Inv) + " " + r.GetBindings().Length);
        }

        // ---- Recording: snapshots and their times.
        {
            var r = new GameObjectRecorder(root);
            r.BindComponent(root.transform);
            var times = new List<string>();
            foreach (var dt in new[] { 0.5f, 0.1f, 0f, 0.2f })
            {
                r.TakeSnapshot(dt);
                times.Add(r.isRecording + " " + F(r.currentTime));
            }
            Put("snap.times", string.Join(" | ", times));
            Save("snap.zero", r, c => { r.SaveToClip(c, 10f, Options(false)); return 0; });
        }

        // ---- Transform motion on regular frames: a position along a smooth path, a rotation
        // turning past a whole turn, a scale that stays.
        void Pose(int i, float t)
        {
            root.transform.localPosition = new Vector3(Mathf.Sin(t * 3f) * 2f, t * t, 1f - t * 0.5f);
            root.transform.localEulerAngles = new Vector3(10f, 25f * i, 5f + Mathf.Sin(t * 2f));
            root.transform.localScale = new Vector3(1f, 1f, 1f);
            child.transform.localPosition = new Vector3(0.5f, 0f, i < 12 ? 0f : 0.01f * (i - 12));
        }
        var dts = new[] { 0.013f, 0.021f, 0.034f, 0.017f, 0.04f, 0.009f, 0.028f, 0.033f, 0.019f, 0.045f, 0.012f, 0.03f,
            0.022f, 0.016f, 0.037f, 0.025f, 0.011f, 0.042f, 0.02f, 0.031f, 0.018f, 0.027f, 0.035f, 0.014f, 0.029f };
        GameObjectRecorder Motion(bool regular)
        {
            var r = new GameObjectRecorder(root);
            r.BindComponent(root.transform);
            r.BindComponent(child.transform);
            float t = 0f;
            for (int i = 0; i < 31; i++)
            {
                float dt = regular ? 1f / 30f : dts[i % dts.Length];
                if (i > 0) t += dt;
                Pose(i, t);
                r.TakeSnapshot(dt);
            }
            Put((regular ? "motion" : "irregular") + ".currentTime", F(r.currentTime));
            return r;
        }
        {
            var r = Motion(true);
            Save("motion.default", r, c => { r.SaveToClip(c); return 0; });
            Save("motion.fps30", r, c => { r.SaveToClip(c, 30f); return 0; });
            Save("motion.unreduced", r, c => { r.SaveToClip(c, 30f, Options(false)); return 0; });
            Save("motion.noUnroll", r, c => { r.SaveToClip(c, 30f, Options(true, unroll: false)); return 0; });
            Save("motion.noUnrollUnreduced", r, c => { r.SaveToClip(c, 30f, Options(false, unroll: false)); return 0; });
            Save("motion.valueError0", r, c => { r.SaveToClip(c, 30f, Options(true, position: 100f, rotation: 100f, scale: 100f, value: 0f)); return 0; });
            Save("motion.valueError5", r, c => { r.SaveToClip(c, 30f, Options(true, position: 0f, rotation: 0f, scale: 0f, value: 5f)); return 0; });
            Save("motion.fps24", r, c => { r.SaveToClip(c, 24f); return 0; });
            Save("motion.fpsHalf", r, c => { r.SaveToClip(c, 0.5f); return 0; });
            Save("motion.fps1", r, c => { r.SaveToClip(c, 1f); return 0; });
            var r2 = Motion(false);
            Save("irregular.default", r2, c => { r2.SaveToClip(c, 30f); return 0; });
            Save("irregular.unreduced", r2, c => { r2.SaveToClip(c, 30f, Options(false)); return 0; });
            Save("irregular.fps60", r2, c => { r2.SaveToClip(c, 60f, Options(false)); return 0; });
        }

        // ---- A rotation set through the quaternion, turning past a half turn.
        {
            var r = new GameObjectRecorder(grandchild);
            r.BindComponent(grandchild.transform);
            for (int i = 0; i < 21; i++)
            {
                grandchild.transform.localRotation = Quaternion.Euler(30f, 20f * i, 0f);
                r.TakeSnapshot(0.05f);
            }
            Save("quat.default", r, c => { r.SaveToClip(c, 20f); return 0; });
            Save("quat.noUnroll", r, c => { r.SaveToClip(c, 20f, Options(false, unroll: false)); return 0; });
        }

        // ---- Values that are not positions: script fields, a Light, the active state, a sprite.
        var texture = new Texture2D(4, 4);
        var spriteA = Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.zero);
        spriteA.name = "A";
        var spriteB = Sprite.Create(texture, new Rect(0, 0, 2, 2), Vector2.zero);
        spriteB.name = "B";
        var spriteC = Sprite.Create(texture, new Rect(0, 0, 1, 1), Vector2.zero);
        spriteC.name = "C";
        {
            var r = new GameObjectRecorder(root);
            r.BindComponent(fields);
            r.Bind(EditorCurveBinding.FloatCurve("", typeof(Light), "m_Intensity"));
            r.Bind(EditorCurveBinding.FloatCurve("child", typeof(GameObject), "m_IsActive"));
            r.Bind(EditorCurveBinding.PPtrCurve("child", typeof(SpriteRenderer), "m_Sprite"));
            Put("values.bindings", Bindings(r));
            var spriteOrder = new[] { spriteA, spriteA, spriteB, spriteB, spriteB, spriteA, spriteC, spriteC, null, spriteA };
            for (int i = 0; i < 20; i++)
            {
                fields.speed = 1f + 0.25f * i - (i > 10 ? 0.4f * (i - 10) : 0f);
                fields.count = i / 3;
                fields.flag = i % 7 < 3;
                fields.mode = (AnimRecorderFields.Mode)(i / 7);
                light.intensity = i < 8 ? 1f : 1f + 0.5f * (i - 8);
                child.SetActive(i < 5 || i > 12);
                sprites.sprite = spriteOrder[i / 2];
                r.TakeSnapshot(i % 3 == 0 ? 0.04f : 0.06f);
            }
            Save("values.default", r, c => { r.SaveToClip(c, 30f); return 0; });
            Save("values.unreduced", r, c => { r.SaveToClip(c, 30f, Options(false)); return 0; });
            Save("values.fps10", r, c => { r.SaveToClip(c, 10f); return 0; });
        }

        // ---- Euler angle bindings (the Transform offers its quaternion): unrolled over the
        // recording when the x angle is bound, as recorded otherwise.
        {
            var spinner = new GameObject("spinner");
            var whole = new GameObjectRecorder(spinner);
            var onlyY = new GameObjectRecorder(spinner);
            foreach (var axis in new[] { "x", "y", "z" })
                whole.Bind(EditorCurveBinding.FloatCurve("", typeof(Transform), "localEulerAnglesRaw." + axis));
            onlyY.Bind(EditorCurveBinding.FloatCurve("", typeof(Transform), "localEulerAnglesRaw.y"));
            Put("euler.bindings", Bindings(whole));
            var raw = new List<string>();
            for (int i = 0; i < 25; i++)
            {
                if (i < 12) spinner.transform.localEulerAngles = new Vector3(10f, 40f * i, 5f);
                else spinner.transform.localRotation = Quaternion.Euler(10f + 3f * i, 40f * i, 5f);
                raw.Add(N(spinner.transform.localEulerAngles.y));
                whole.TakeSnapshot(0.05f);
                onlyY.TakeSnapshot(0.05f);
            }
            PutJson("euler.read", "[" + string.Join(",", raw) + "]");
            Save("euler.default", whole, c => { whole.SaveToClip(c, 20f); return 0; });
            Save("euler.unreduced", whole, c => { whole.SaveToClip(c, 20f, Options(false)); return 0; });
            Save("euler.noUnroll", whole, c => { whole.SaveToClip(c, 20f, Options(false, unroll: false)); return 0; });
            Save("euler.onlyY", onlyY, c => { onlyY.SaveToClip(c, 20f, Options(false)); return 0; });
            UnityEngine.Object.DestroyImmediate(spinner);
        }

        // ---- What AnimationUtility offers for the same objects, beside the recorder's bindings.
        string Offered(GameObject target, GameObject of) => string.Join(",", AnimationUtility.GetAnimatableBindings(target, of).Select(B));
        Put("animatable.root", Offered(root, root));
        Put("animatable.child", Offered(child, root));
        // The values and value types the recorder reads through.
        fields.mode = AnimRecorderFields.Mode.Fast;
        fields.count = 7;
        root.transform.localEulerAngles = new Vector3(10f, 200f, 5f);
        foreach (var b in new[]
        {
            EditorCurveBinding.FloatCurve("", typeof(AnimRecorderFields), "speed"),
            EditorCurveBinding.FloatCurve("", typeof(AnimRecorderFields), "count"),
            EditorCurveBinding.FloatCurve("", typeof(AnimRecorderFields), "flag"),
            EditorCurveBinding.DiscreteCurve("", typeof(AnimRecorderFields), "mode"),
            EditorCurveBinding.FloatCurve("", typeof(AnimRecorderFields), "m_Enabled"),
            EditorCurveBinding.FloatCurve("", typeof(Light), "m_Intensity"),
            EditorCurveBinding.FloatCurve("child", typeof(GameObject), "m_IsActive"),
            EditorCurveBinding.FloatCurve("", typeof(Transform), "localEulerAnglesRaw.y"),
            EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalRotation.w"),
        })
        {
            Put("read." + b.propertyName, Try(() => { var type = AnimationUtility.GetEditorCurveValueType(root, b); bool got = AnimationUtility.GetFloatValue(root, b, out float v); return (type == null ? "null" : type.Name) + " " + got + " " + F(v); }));
        }

        // ---- Long regular recordings: the last frame of a time summed snapshot by snapshot.
        {
            var range = EditorCurveBinding.FloatCurve("", typeof(Light), "m_Range");
            foreach (var (count, perSecond, fps) in new[] { (64, 30, 30f), (81, 30, 30f), (301, 30, 30f), (301, 30, 60f), (601, 60, 60f), (1201, 60, 30f), (101, 10, 10f) })
            {
                var r = new GameObjectRecorder(root);
                r.Bind(range);
                for (int i = 0; i < count; i++)
                {
                    light.range = 1f + 0.01f * i;
                    r.TakeSnapshot(1f / perSecond);
                }
                var clip = new AnimationClip();
                r.SaveToClip(clip, fps, Options(false));
                var curve = AnimationUtility.GetEditorCurve(clip, range);
                PutJson("long." + count + "at" + perSecond + ".fps" + fps.ToString(Inv),
                    "[" + N(r.currentTime) + "," + curve.length + "," + N(curve[0].time) + "," + N(curve[curve.length - 1].time) + "," + N(clip.length) + "]");
            }
        }

        // ---- ClampedAuto tangents (the recorder's) of keys a tiny interval or rise apart.
        {
            var cases = new (string Name, Vector2[] Keys)[]
            {
                ("tinyInterval", new[] { new Vector2(0f, 0f), new Vector2(5e-6f, 1f), new Vector2(1f, 2f) }),
                ("tinyRise", new[] { new Vector2(0f, 1f), new Vector2(1f, 1.000005f), new Vector2(2f, 1.000039f) }),
                ("tinyRise2", new[] { new Vector2(0f, 1f), new Vector2(1f, 1.00003f), new Vector2(2f, 1.000039f) }),
                ("uneven", new[] { new Vector2(0f, 0f), new Vector2(0.1f, 0.8f), new Vector2(2f, 1f) }),
            };
            foreach (var (name, points) in cases)
            {
                var curve = new AnimationCurve(points.Select(v => new Keyframe(v.x, v.y)).ToArray());
                for (int i = 0; i < curve.length; i++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                    AnimationUtility.SetKeyRightTangentMode(curve, i, AnimationUtility.TangentMode.ClampedAuto);
                }
                PutJson("clampedAuto." + name, "[" + string.Join(",", curve.keys.Select(Key)) + "]");
            }
            var weighted = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 0.5f, 0f, 0f, 0.2f, 0.7f) { weightedMode = WeightedMode.Both }, new Keyframe(2f, 2f, 0f, 0f, 0.1f, 0.9f) { weightedMode = WeightedMode.In });
            for (int i = 0; i < weighted.length; i++)
            {
                AnimationUtility.SetKeyLeftTangentMode(weighted, i, AnimationUtility.TangentMode.ClampedAuto);
                AnimationUtility.SetKeyRightTangentMode(weighted, i, AnimationUtility.TangentMode.ClampedAuto);
            }
            PutJson("clampedAuto.weighted", "[" + string.Join(",", weighted.keys.Select(Key)) + "]");
            var single = new AnimationCurve(new Keyframe(1f, 2f, 0.5f, 0.5f, 0.2f, 0.7f));
            AnimationUtility.SetKeyLeftTangentMode(single, 0, AnimationUtility.TangentMode.ClampedAuto);
            AnimationUtility.SetKeyRightTangentMode(single, 0, AnimationUtility.TangentMode.ClampedAuto);
            PutJson("clampedAuto.single", "[" + string.Join(",", single.keys.Select(Key)) + "]");
        }

        // ---- An Animator with a controller: what animation offers for it and the recorder binds.
        {
            var go = new GameObject("animated");
            var animator = go.AddComponent<Animator>();
            var controller = new UnityEditor.Animations.AnimatorController();
            controller.AddLayer("Base");
            controller.AddParameter("speed", AnimatorControllerParameterType.Float);
            controller.AddParameter("count", AnimatorControllerParameterType.Int);
            controller.AddParameter("on", AnimatorControllerParameterType.Bool);
            controller.AddParameter("hit", AnimatorControllerParameterType.Trigger);
            controller.AddParameter("blend", AnimatorControllerParameterType.Float);
            animator.runtimeAnimatorController = controller;
            Put("animator.offered", Offered(go, go));
            var r = new GameObjectRecorder(go);
            r.BindComponent(animator);
            Put("animator.bound", Bindings(r));
            // The same Animator below the recorder's root.
            var top = new GameObject("top");
            go.transform.SetParent(top.transform, false);
            Put("animator.childOffered", Offered(go, top));
            r = new GameObjectRecorder(top);
            r.BindAll(top, true);
            Put("animator.childBound", Bindings(r));
            r = new GameObjectRecorder(top);
            r.BindComponent(animator);
            Put("animator.childComponent", Bindings(r));
            UnityEngine.Object.DestroyImmediate(top);
        }

        // ---- A recording shorter than a frame: reduced all the same (the clip's length is one
        // second while its curves are not synced).
        {
            var r = new GameObjectRecorder(root);
            r.Bind(EditorCurveBinding.FloatCurve("", typeof(Light), "m_SpotAngle"));
            for (int i = 0; i < 6; i++)
            {
                light.spotAngle = 30f + (i == 3 ? 0.01f : 0f);
                r.TakeSnapshot(0.01f);
            }
            Save("short.fps10", r, c => { r.SaveToClip(c, 10f); return 0; });
            Save("short.fps100", r, c => { r.SaveToClip(c, 100f); return 0; });
        }

        // ---- A clip's length with object reference curves: their last key lasts a frame.
        {
            var clip = new AnimationClip { frameRate = 20f };
            AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"),
                new[] { new ObjectReferenceKeyframe { time = 0f, value = spriteA }, new ObjectReferenceKeyframe { time = 0.5f, value = spriteB } });
            PutJson("objectLength.only", N(clip.length));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Light), "m_Range"), AnimationCurve.Linear(0f, 1f, 0.52f, 2f));
            PutJson("objectLength.withFloat", N(clip.length));
            clip.frameRate = 4f;
            PutJson("objectLength.rate4", N(clip.length));
        }

        // ---- A clip that held curves: the recording replaces them.
        {
            var r = new GameObjectRecorder(root);
            r.Bind(EditorCurveBinding.FloatCurve("", typeof(Light), "m_Range"));
            light.range = 3f;
            r.TakeSnapshot(0f);
            light.range = 5f;
            r.TakeSnapshot(0.5f);
            var clip = new AnimationClip();
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("other", typeof(Transform), "m_LocalPosition.y"), AnimationCurve.Linear(0f, 0f, 2f, 1f));
            clip.frameRate = 12f;
            Put("reuse.save", Try(() => { r.SaveToClip(clip, 4f); return "void"; }));
            Clip("reuse", clip);
            Put("reuse.again", Try(() => { r.SaveToClip(clip, 8f, Options(false)); return "void"; }));
            Clip("reuse.again", clip);
        }

        output.Append("{\"k\":\"complete\",\"v\":true}\n");
        Directory.CreateDirectory(directory);
        File.WriteAllText(Path.Combine(directory, "values.jsonl"), output.ToString());
        EditorApplication.Exit(0);
    }
}
