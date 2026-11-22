using System;
using System.Collections.Generic;
using Unbound.Tests;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine.Animations;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// What a clip's curves do by the property's type and by what samples them, and what the Editor
    /// says of a compiled clip (measured in Unity 6000.0.71f1, tests/projects/animatorapi
    /// <c>stats.*</c>, <c>typed.*</c>, <c>modify.*</c>, <c>names.*</c>). Warnings and errors are
    /// captured through <see cref="Debug.unityLogger"/>; one a test does not expect fails it.
    /// </summary>
    [Collection("Animation")]
    public class AnimationCurveRulesTests : IDisposable
    {
        readonly List<string> m_Logs = new List<string>();
        readonly List<string> m_Expected = new List<string>();
        readonly List<Object> m_Created = new List<Object>();
        readonly ILogger m_Logger = Debug.unityLogger;
        readonly ILogHandler m_PreviousHandler;
        readonly bool m_PreviousLogEnabled;
        readonly LogType m_PreviousFilter;

        public AnimationCurveRulesTests()
        {
            m_PreviousHandler = m_Logger.logHandler;
            m_PreviousLogEnabled = m_Logger.logEnabled;
            m_PreviousFilter = m_Logger.filterLogType;
            m_Logger.logHandler = new CaptureHandler(m_PreviousHandler, m_Logs);
            m_Logger.logEnabled = true;
            m_Logger.filterLogType = LogType.Log;
        }

        sealed class CaptureHandler : ILogHandler
        {
            readonly ILogHandler m_Inner;
            readonly List<string> m_Logs;
            internal CaptureHandler(ILogHandler inner, List<string> logs) { m_Inner = inner; m_Logs = logs; }
            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type != LogType.Log)
                    m_Logs.Add($"{type}: {(args == null || args.Length == 0 ? format : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args))}");
                m_Inner.LogFormat(type, context, format, args);
            }
            public void LogException(Exception exception, Object context)
            {
                if (exception != null) m_Logs.Add($"Exception: {exception}");
                m_Inner.LogException(exception, context);
            }
        }

        public void Dispose()
        {
            try
            {
                while (AnimationMode.InAnimationMode()) AnimationMode.StopAnimationMode();
                foreach (var o in m_Created)
                    if (o != null) Object.DestroyImmediate(o);
            }
            finally
            {
                m_Logger.logHandler = m_PreviousHandler;
                m_Logger.logEnabled = m_PreviousLogEnabled;
                m_Logger.filterLogType = m_PreviousFilter;
            }
            var unexpected = m_Logs.FindAll(l => !m_Expected.Exists(e => l.Contains(e)));
            Assert.True(unexpected.Count == 0, string.Join("\n", unexpected));
        }

        T Keep<T>(T o) where T : Object { m_Created.Add(o); return o; }

        enum Mode { A = 0, B = 1, C = 2 }

        sealed class Typed : MonoBehaviour
        {
            public float f = 1.5f;
            public int i = 2;
            public bool b;
            public string label = "start";
            public Color color = Color.red;
            public Vector3 offset = new Vector3(1f, 2f, 3f);
            public Mode mode = Mode.C;
            [DiscreteEvaluation] public int counted = 4;
            public Object any;
            public AnimationClip clip;
            public Material material;
            public Component component;
            public MonoBehaviour script;
            public ScriptableObject data;
        }

        sealed class Data : ScriptableObject
        {
        }

        static AnimationCurve Keys(params float[] timesAndValues)
        {
            var keys = new Keyframe[timesAndValues.Length / 2];
            for (int i = 0; i < keys.Length; i++) keys[i] = new Keyframe(timesAndValues[2 * i], timesAndValues[2 * i + 1]);
            return new AnimationCurve(keys);
        }

        AnimationClip Clip(string name = "Clip", bool legacy = false) => Keep(new AnimationClip { name = name, frameRate = 60f, legacy = legacy });

        static void Float(AnimationClip clip, string attribute, AnimationCurve curve) =>
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Typed), attribute), curve);

        static void Moved(AnimationClip clip, string attribute, AnimationCurve curve) =>
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Transform), attribute), curve);

        static void Refs(AnimationClip clip, Type type, string attribute, params (float Time, Object Value)[] keys)
        {
            var frames = new ObjectReferenceKeyframe[keys.Length];
            for (int i = 0; i < keys.Length; i++) frames[i] = new ObjectReferenceKeyframe { time = keys[i].Time, value = keys[i].Value };
            AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve("", type, attribute), frames);
        }

        static AnimationClipStats Stats(AnimationClip clip) => AnimationUtility.GetAnimationClipStats(clip);

        GameObject Rig(string name)
        {
            var go = Keep(new GameObject(name));
            go.AddComponent<Typed>();
            go.AddComponent<MeshFilter>();
            go.AddComponent<MeshRenderer>().sharedMaterial = null;
            return go;
        }

        Animator Playing(GameObject go, AnimationClip clip)
        {
            var controller = Keep(new AnimatorController { name = "Controller" });
            controller.AddLayer("Base Layer");
            controller.AddMotion(clip);
            var animator = go.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            animator.Update(0f);
            return animator;
        }

        // ------------------------------------------------------------------ clip stats

        [Fact]
        public void AClipsStatsCountItsStreamedKeysAndTimes()
        {
            var two = Clip();
            Float(two, "f", Keys(0, 0, 1, 1));
            var stats = Stats(two);
            Assert.Equal((2628, 1, 1, 0, 1, 1), (stats.size, stats.clips, stats.totalCurves, stats.constantCurves, stats.streamCurves, stats.genericCurves));

            // Two curves share two of three key times.
            var shared = Clip();
            Float(shared, "f", Keys(0, 0, 1, 1));
            Float(shared, "i", Keys(0, 0, 0.5f, 2, 1, 1));
            Assert.Equal(2724, Stats(shared).size);

            // None shared: every time counts.
            var disjoint = Clip();
            Float(disjoint, "f", Keys(0, 0, 1, 1));
            Float(disjoint, "i", Keys(0.25f, 0, 0.75f, 1));
            Assert.Equal(2712, Stats(disjoint).size);

            // An object curve has no key before its first, and brings no time of its own here.
            var withObject = Clip();
            Refs(withObject, typeof(Typed), "any", (0f, two), (0.5f, shared));
            Assert.Equal((2600, 1, 1), (Stats(withObject).size, Stats(withObject).pptrCurves, Stats(withObject).streamCurves));
            Float(withObject, "f", Keys(0, 0, 1, 1));
            Assert.Equal(2684, Stats(withObject).size);
        }

        [Fact]
        public void AConstantCurveHasOneValueAndNoSlope()
        {
            var constant = Clip();
            Float(constant, "f", Keys(0, 3, 1, 3, 2, 3));
            var stats = Stats(constant);
            Assert.Equal((2548, 1, 0), (stats.size, stats.constantCurves, stats.streamCurves));

            foreach (var (key, incoming) in new[] { (0, true), (0, false), (1, true), (1, false) })
            {
                var sloped = Clip();
                var keys = Keys(0, 3, 1, 3).keys;
                if (incoming) keys[key].inTangent = 2f;
                else keys[key].outTangent = 2f;
                Float(sloped, "f", new AnimationCurve(keys));
                stats = Stats(sloped);
                Assert.Equal((2628, 0, 1), (stats.size, stats.constantCurves, stats.streamCurves));
            }
        }

        [Fact]
        public void ATransformVectorIsOneCurveAComponentKeyedAtAllItsTimes()
        {
            var position = Clip();
            Moved(position, "m_LocalPosition.x", Keys(0, 0, 1, 1));
            Moved(position, "m_LocalPosition.y", Keys(0, 0, 0.5f, 2, 1, 1));
            var stats = Stats(position);
            Assert.Equal((2832, 1, 3, 3, 0), (stats.size, stats.positionCurves, stats.totalCurves, stats.streamCurves, stats.genericCurves));

            // One constant component does not make the vector constant; all of them do.
            var mixed = Clip();
            Moved(mixed, "m_LocalPosition.x", Keys(0, 2, 1, 2));
            Moved(mixed, "m_LocalPosition.y", Keys(0, 0, 1, 1));
            Assert.Equal((2764, 0), (Stats(mixed).size, Stats(mixed).constantCurves));
            var still = Clip();
            Moved(still, "localEulerAnglesRaw.y", Keys(0, 20, 1.5f, 20));
            stats = Stats(still);
            Assert.Equal((2572, 1, 3, 3), (stats.size, stats.eulerCurves, stats.totalCurves, stats.constantCurves));

            var rotation = Clip();
            foreach (var axis in "xyzw") Moved(rotation, "m_LocalRotation." + axis, Keys(0, 0, 1, 0.5f));
            Assert.Equal((2832, 1, 4), (Stats(rotation).size, Stats(rotation).quaternionCurves, Stats(rotation).totalCurves));
        }

        [Fact]
        public void StatsDescribeTheClipAsLastCompiled()
        {
            // SetCurve alone compiles nothing, and undoes what was compiled.
            var written = Clip();
            written.SetCurve("", typeof(Typed), "f", Keys(0, 0, 1, 1));
            Assert.Equal((0, 0, 0), (Stats(written).size, Stats(written).clips, Stats(written).totalCurves));
            _ = written.length;
            written.SampleAnimation(Rig("Sampled"), 0.5f);
            Assert.Equal(0, Stats(written).size);
            AnimationUtility.SetAnimationEvents(written, Array.Empty<AnimationEvent>());
            Assert.Equal((2628, 1), (Stats(written).size, Stats(written).clips));
            written.SetCurve("", typeof(Typed), "i", Keys(0, 0, 1, 1));
            Assert.Equal(0, Stats(written).size);
            AnimationUtility.SetAnimationClipSettings(written, AnimationUtility.GetAnimationClipSettings(written));
            Assert.Equal(2696, Stats(written).size);
            written.ClearCurves();
            Assert.Equal((0, 0), (Stats(written).size, Stats(written).clips));

            // An Animator taking the clip compiles it.
            var played = Clip("Played");
            played.SetCurve("", typeof(Typed), "f", Keys(0, 0, 1, 1));
            Playing(Rig("Player"), played);
            Assert.Equal(2628, Stats(played).size);

            // A legacy clip is not compiled; one made legacy afterwards keeps what it had.
            var legacy = Clip("Legacy", legacy: true);
            Float(legacy, "f", Keys(0, 0, 1, 1));
            Assert.Equal((0, 0), (Stats(legacy).size, Stats(legacy).clips));
            var later = Clip("Later");
            Float(later, "f", Keys(0, 0, 1, 1));
            later.legacy = true;
            Assert.Equal(2628, Stats(later).size);

            // A copy starts without.
            var source = Clip("Source");
            Float(source, "f", Keys(0, 0, 1, 1));
            var copy = Keep(Object.Instantiate(source));
            Assert.Equal((2628, 0), (Stats(source).size, Stats(copy).size));
        }

        // ------------------------------------------------------------------ object curves

        AnimationClip ObjectClip(GameObject other, Material m1, Material m2, AnimationClip a, AnimationClip b, Data data, bool legacy = false)
        {
            var clip = Clip(legacy ? "ObjectsLegacy" : "Objects", legacy);
            Refs(clip, typeof(Typed), "any", (0f, a), (1f, b));
            Refs(clip, typeof(Typed), "clip", (0f, a), (1f, b));
            Refs(clip, typeof(Typed), "material", (0f, m1), (1f, m2));
            Refs(clip, typeof(Typed), "component", (0f, other.transform), (1f, other.GetComponent<Typed>()));
            Refs(clip, typeof(Typed), "script", (0f, other.GetComponent<Typed>()), (1f, other.GetComponent<Typed>()));
            Refs(clip, typeof(Typed), "data", (0f, data), (1f, data));
            Refs(clip, typeof(MeshRenderer), "m_Materials.Array.data[0]", (0f, m1), (1f, m2));
            Float(clip, "f", Keys(0, 10, 2, 30));
            return clip;
        }

        [Fact]
        public void AnAnimatorSetsObjectFieldsOfEngineClassesAndOnlyAnAnimatorSetsAny()
        {
            var other = Rig("Other");
            var shader = Shader.Create("GUI/Text Shader", 3000);
            var m1 = Keep(new Material(shader) { name = "M1" });
            var m2 = Keep(new Material(shader) { name = "M2" });
            var a = Clip("A");
            var b = Clip("B");
            var data = Keep(ScriptableObject.CreateInstance<Data>());

            var go = Rig("Animated");
            var typed = go.GetComponent<Typed>();
            var animator = Playing(go, ObjectClip(other, m1, m2, a, b, data));
            animator.Update(0.5f);
            Near.Equal(13.125f, typed.f, 1e-4f);
            Assert.Same(a, typed.clip);
            Assert.Same(m1, typed.material);
            Assert.Same(other.transform, typed.component);
            Assert.Same(m1, go.GetComponent<MeshRenderer>().sharedMaterial);
            // Fields typed Object, MonoBehaviour and ScriptableObject keep their values.
            Assert.Null(typed.any);
            Assert.Null(typed.script);
            Assert.Null(typed.data);
            animator.Update(1f);
            Assert.Same(b, typed.clip);
            Assert.Same(m2, typed.material);
            // A script is a value like another in a field typed Component.
            Assert.Same(other.GetComponent<Typed>(), typed.component);
            Assert.Null(typed.any);

            // The clip's curves sampled, and a legacy Animation, set numbers and no object.
            var sampled = Rig("Sampled");
            ObjectClip(other, m1, m2, a, b, data).SampleAnimation(sampled, 0.5f);
            var legacy = Rig("Legacy");
            var animation = legacy.AddComponent<Animation>();
            animation.AddClip(ObjectClip(other, m1, m2, a, b, data, legacy: true), "ObjectsLegacy");
            animation.Play("ObjectsLegacy");
            animation["ObjectsLegacy"].time = 0.5f;
            animation.Sample();
            foreach (var rig in new[] { sampled, legacy })
            {
                var t = rig.GetComponent<Typed>();
                Near.Equal(13.125f, t.f, 1e-4f);
                Assert.Null(t.clip);
                Assert.Null(t.material);
                Assert.Null(t.component);
                Assert.Null(rig.GetComponent<MeshRenderer>().sharedMaterial);
            }
        }

        // ------------------------------------------------------------------ discrete fields

        [Fact]
        public void ADiscreteFieldTakesItsCurvesBitsSteppedByAnAnimatorOnly()
        {
            AnimationClip Discrete()
            {
                var clip = Clip("Discrete");
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.DiscreteCurve("", typeof(Typed), "mode"),
                    Keys(0, BitConverter.Int32BitsToSingle(0), 1, BitConverter.Int32BitsToSingle(2)));
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.DiscreteCurve("", typeof(Typed), "counted"),
                    Keys(0, BitConverter.Int32BitsToSingle(3), 1, BitConverter.Int32BitsToSingle(7)));
                // A plain int is a number: it rounds.
                Float(clip, "i", Keys(0, 10, 2, 30));
                return clip;
            }
            var go = Rig("Animated");
            var typed = go.GetComponent<Typed>();
            var animator = Playing(go, Discrete());
            animator.Update(0.5f);
            Assert.Equal((Mode.A, 3, 13), (typed.mode, typed.counted, typed.i));
            animator.Update(1f);
            Assert.Equal((Mode.C, 7, 27), (typed.mode, typed.counted, typed.i));

            // Sampled as curves the bits are interpolated like a number's.
            var sampled = Rig("Sampled");
            Discrete().SampleAnimation(sampled, 0.5f);
            typed = sampled.GetComponent<Typed>();
            Assert.Equal((Mode.B, 5, 13), (typed.mode, typed.counted, typed.i));

            // A key given as a number is still read as bits.
            var number = Clip("Number");
            Float(number, "mode", Keys(0, 1, 2, 1));
            var numbered = Rig("Numbered");
            Playing(numbered, number).Update(0.5f);
            Assert.Equal(1065353216, (int)numbered.GetComponent<Typed>().mode);
        }

        [Fact]
        public void AStreamHandleReadsAndWritesADiscreteFieldAsItsInteger()
        {
            var go = Rig("Streamed");
            var typed = go.GetComponent<Typed>();
            var clip = Clip("Idle");
            Float(clip, "f", Keys(0, 1, 1, 1));
            var animator = Playing(go, clip);
            var counted = animator.BindStreamProperty(go.transform, typeof(Typed), "counted");
            var mode = animator.BindStreamProperty(go.transform, typeof(Typed), "mode");
            var plain = animator.BindStreamProperty(go.transform, typeof(Typed), "i");
            var stream = default(AnimationStream);
            Assert.True(animator.OpenAnimationStream(ref stream));
            Assert.Equal((4, 2, 2), (counted.GetInt(stream), mode.GetInt(stream), plain.GetInt(stream)));
            counted.SetInt(stream, 7);
            mode.SetInt(stream, 1);
            plain.SetInt(stream, 9);
            Assert.Equal((7, 1, 9), (counted.GetInt(stream), mode.GetInt(stream), plain.GetInt(stream)));
            animator.CloseAnimationStream(ref stream);
            Assert.Equal((7, Mode.B, 9), (typed.counted, typed.mode, typed.i));
        }

        // ------------------------------------------------------------------ AnimationMode

        [Fact]
        public void AnimationModeRecordsASampledClipsPropertiesAndLeavesItsObjectCurves()
        {
            var go = Rig("Mode");
            var typed = go.GetComponent<Typed>();
            var a = Clip("A");
            var clip = Clip("Sampled");
            Float(clip, "color.r", Keys(0, 0, 2, 1));
            Float(clip, "offset.x", Keys(0, 0, 2, 4));
            Moved(clip, "m_LocalPosition.x", Keys(0, 0, 2, 4));
            Refs(clip, typeof(Typed), "clip", (0f, a), (1f, a));
            Refs(clip, typeof(MeshRenderer), "m_Materials.Array.data[0]", (0f, null), (1f, null));

            AnimationMode.StartAnimationMode();
            AnimationMode.SampleAnimationClip(go, clip, 1f);
            Near.Equal(0.5f, typed.color.r, 1e-4f);
            Near.Equal(2f, typed.offset.x, 1e-4f);
            Assert.Null(typed.clip);
            Assert.True(AnimationMode.IsPropertyAnimated(typed, "clip"));
            Assert.True(AnimationMode.IsPropertyAnimated(go.GetComponent<MeshRenderer>(), "m_Materials.Array.data[0]"));
            // A script's struct is not animated with its member; a Transform's vector is.
            Assert.True(AnimationMode.IsPropertyAnimated(typed, "color.r"));
            Assert.False(AnimationMode.IsPropertyAnimated(typed, "color.g"));
            Assert.False(AnimationMode.IsPropertyAnimated(typed, "color"));
            Assert.False(AnimationMode.IsPropertyAnimated(typed, "offset"));
            Assert.True(AnimationMode.IsPropertyAnimated(go.transform, "m_LocalPosition"));
            Assert.False(AnimationMode.IsPropertyAnimated(typed, "label"));
            AnimationMode.StopAnimationMode();
            Near.Equal(1f, typed.color.r, 1e-4f);
            Near.Equal(1f, typed.offset.x, 1e-4f);
            Assert.Equal(Vector3.zero, go.transform.localPosition);
        }

        [Fact]
        public void AModificationRevertsToItsValueReadByThePropertysType()
        {
            var go = Rig("Modified");
            var typed = go.GetComponent<Typed>();
            var light = go.AddComponent<Light>();
            light.color = new Color(0.25f, 0.5f, 0.75f, 1f);
            AnimationMode.StartAnimationMode();
            void Add(Object target, string path, string value) => AnimationMode.AddPropertyModification(
                EditorCurveBinding.FloatCurve("", target.GetType(), path), new PropertyModification { target = target, propertyPath = path, value = value }, true);
            Add(typed, "f", "");
            Add(typed, "i", "x");
            Add(typed, "b", "true");
            Add(typed, "mode", "B");
            Add(typed, "label", "was");
            Add(typed, "counted", null);
            Add(typed, "nothing", "1");
            Add(typed, "offset.x", "6");
            Add(light, "m_Color.r", "0.5");
            Assert.True(AnimationMode.IsPropertyAnimated(typed, "label"));
            Assert.True(AnimationMode.IsPropertyAnimated(typed, "nothing"));
            Assert.True(AnimationMode.IsPropertyAnimated(light, "m_Color.r"));
            Assert.False(AnimationMode.IsPropertyAnimated(light, "m_Color.g"));
            Assert.False(AnimationMode.IsPropertyAnimated(light, "m_Color"));
            typed.f = 9f;
            typed.i = 9;
            typed.b = true;
            typed.mode = Mode.B;
            typed.label = "changed";
            typed.counted = 9;
            typed.offset = new Vector3(7f, 8f, 9f);
            light.color = Color.black;
            AnimationMode.StopAnimationMode();
            // Text that is no number is 0 for a number, a bool and an enum; a text property takes it.
            Assert.Equal((0f, 0, false, Mode.A, "was", 0), (typed.f, typed.i, typed.b, typed.mode, typed.label, typed.counted));
            Assert.Equal(new Vector3(6f, 8f, 9f), typed.offset);
            Assert.Equal(new Color(0.5f, 0f, 0f, 1f), light.color);

            // No text at all is the empty text.
            AnimationMode.StartAnimationMode();
            Add(typed, "label", null);
            typed.label = "changed";
            AnimationMode.StopAnimationMode();
            Assert.Equal("", typed.label);
        }

        // ------------------------------------------------------------------ state names

        [Fact]
        public void AStatesOrMachinesNameTakesUnderscoresForDots()
        {
            m_Expected.Add("Warning: '.' is not allowed in State name");
            var controller = Keep(new AnimatorController { name = "Names" });
            controller.AddLayer("Base Layer");
            var machine = controller.layers[0].stateMachine;
            Assert.Equal("a_b", machine.AddState("a.b").name);
            var renamed = machine.AddState("plain");
            renamed.name = "c.d";
            Assert.Equal("c_d", renamed.name);
            Assert.Equal("e_f", controller.AddMotion(Clip("e.f")).name);
            Assert.Equal("g_h", machine.AddStateMachine("g.h").name);
            Assert.Equal(4, m_Logs.Count);

            // The unique name helpers, parameters and a layer keep them; the layer's machine does not.
            Assert.Equal("a.b", machine.MakeUniqueStateName("a.b"));
            controller.AddParameter("o.p", AnimatorControllerParameterType.Float);
            Assert.Equal("o.p", controller.parameters[0].name);
            controller.AddLayer("m.n");
            Assert.Equal(("m.n", "m_n"), (controller.layers[1].name, controller.layers[1].stateMachine.name));
            Assert.Equal(5, m_Logs.Count);
            // A name without a dot says nothing.
            machine.AddState("plain again");
            Assert.Equal(5, m_Logs.Count);
        }
    }
}
