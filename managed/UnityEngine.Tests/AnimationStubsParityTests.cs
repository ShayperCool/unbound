using System.Collections.Generic;
using Unbound.Runtime;
using Unbound.Tests;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// AnimationClip.EnsureQuaternionContinuity, Animation.SyncLayer and
    /// AnimatorControllerPlayable.IsParameterControlledByCurve. Documented (6000.0 Scripting API):
    /// quaternion keys are realigned for the shortest interpolation path; the playable's query
    /// returns whether the parameter is controlled by a curve. Not documented in 6000.0 (no page):
    /// SyncLayer; that it synchronizes the playback speed of all animations in the layer is
    /// Unbound's reading of its purpose, unmeasured. EnsureQuaternionContinuity is measured
    /// (tests/projects/editorui EnsureQuaternionContinuity.*); the other two are not: expected values
    /// beyond their contracts are Unbound's unmeasured choices and are marked so. Created
    /// objects are destroyed in <see cref="Dispose"/>, which also fails on unexpected warnings.
    /// </summary>
    [Collection("Animation")]
    public class AnimationStubsParityTests : System.IDisposable
    {
        readonly List<string> m_Logs = new List<string>();
        readonly List<string> m_Expected = new List<string>();
        readonly List<Object> m_Created = new List<Object>();
        readonly ILogger m_Logger = Debug.unityLogger;
        readonly ILogHandler m_PreviousHandler;
        readonly bool m_PreviousLogEnabled;
        readonly LogType m_PreviousFilter;
        const string KeptKeys = "AnimationClip.EnsureQuaternionContinuity on";

        public AnimationStubsParityTests()
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

            internal CaptureHandler(ILogHandler inner, List<string> logs)
            {
                m_Inner = inner;
                m_Logs = logs;
            }

            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type != LogType.Log)
                {
                    string message = args == null || args.Length == 0 ? format
                        : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args);
                    m_Logs.Add($"{type}: {message}");
                }
                m_Inner.LogFormat(type, context, format, args);
            }

            public void LogException(System.Exception exception, Object context)
            {
                if (exception != null) m_Logs.Add($"Exception: {exception}");
                m_Inner.LogException(exception, context);
            }
        }

        public void Dispose()
        {
            try
            {
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

        T Keep<T>(T o) where T : Object
        {
            m_Created.Add(o);
            return o;
        }

        // ------------------------------------------------------------------ EnsureQuaternionContinuity

        /// <summary>A rotation about z by <paramref name="degrees"/>, as (x, y, z, w).</summary>
        static Quaternion AboutZ(float degrees)
        {
            float h = degrees * Mathf.Deg2Rad * 0.5f;
            return new Quaternion(0f, 0f, Mathf.Sin(h), Mathf.Cos(h));
        }

        static Quaternion Negated(Quaternion q) => new Quaternion(-q.x, -q.y, -q.z, -q.w);

        static float Component(Quaternion q, int axis) => axis == 0 ? q.x : axis == 1 ? q.y : axis == 2 ? q.z : q.w;

        static readonly string[] s_Axes = { "x", "y", "z", "w" };

        /// <summary>A legacy clip whose localRotation keys are <paramref name="keys"/> at times 0, 1,
        /// 2, ... with flat tangents.</summary>
        AnimationClip RotationClip(params Quaternion[] keys)
        {
            var clip = Keep(new AnimationClip { legacy = true, name = "turn" });
            for (int a = 0; a < 4; a++)
            {
                var frames = new Keyframe[keys.Length];
                for (int i = 0; i < keys.Length; i++) frames[i] = new Keyframe(i, Component(keys[i], a));
                clip.SetCurve("", typeof(Transform), "localRotation." + s_Axes[a], new AnimationCurve(frames));
            }
            return clip;
        }

        static Keyframe[] Keys(AnimationClip clip, string attribute) =>
            clip.m_Curves.Find(c => c.Binding.Attribute == attribute).Curve.keys;

        static Quaternion KeyAt(AnimationClip clip, int index)
        {
            var v = new float[4];
            for (int a = 0; a < 4; a++) v[a] = Keys(clip, "m_LocalRotation." + s_Axes[a])[index].value;
            return new Quaternion(v[0], v[1], v[2], v[3]);
        }

        static void SameComponents(Quaternion expected, Quaternion actual, float tolerance = 1e-6f)
        {
            for (int a = 0; a < 4; a++) Near.Equal(Component(expected, a), Component(actual, a), tolerance);
        }

        /// <summary>Documented: a key in the opposite hemisphere from the key before it is negated.
        /// Each key is compared with the key before it as realigned (key 2 stays, key 3 flips).
        /// RED at b988: the call changes nothing, so key 1 keeps −q1.</summary>
        [Fact]
        public void KeysInTheOppositeHemisphereAreNegatedFromTheFirstKeyOn()
        {
            Quaternion q0 = Quaternion.identity, q1 = AboutZ(60f), q2 = AboutZ(120f), q3 = AboutZ(180f);
            var clip = RotationClip(q0, Negated(q1), q2, Negated(q3));
            clip.EnsureQuaternionContinuity();
            SameComponents(q0, KeyAt(clip, 0));
            SameComponents(q1, KeyAt(clip, 1));
            SameComponents(q2, KeyAt(clip, 2));
            SameComponents(q3, KeyAt(clip, 3));
            for (int a = 0; a < 4; a++)
            {
                var keys = Keys(clip, "m_LocalRotation." + s_Axes[a]);
                Assert.Equal(4, keys.Length);
                for (int i = 0; i < 4; i++) Assert.Equal((float)i, keys[i].time);
            }
        }

        /// <summary>Documented outcome: the shortest path. Halfway between identity and a key
        /// stored as −(60° about z) the clip samples 30° about z. RED at b988: the components go
        /// through the long way, about 150° the other way (180° from the expected rotation).</summary>
        [Fact]
        public void TheRealignedClipSamplesTheShortestPath()
        {
            var clip = RotationClip(Quaternion.identity, Negated(AboutZ(60f)));
            var go = Keep(new GameObject("Turned"));
            clip.EnsureQuaternionContinuity();
            clip.SampleAnimation(go, 0.5f);
            Assert.True(Quaternion.Angle(AboutZ(30f), go.transform.localRotation) < 0.05f,
                $"got {go.transform.localRotation.ToString("R")}");
        }

        /// <summary>Every key of a realigned rotation takes the slope between its neighbours as both
        /// tangents (one-sided at the ends), whatever tangents it had (matches the Unity 6000.0.71f1
        /// Editor, tests/projects/editorui EnsureQuaternionContinuity.*; that weights are reset with
        /// them is Unbound's choice, unmeasured).</summary>
        [Fact]
        public void EveryKeyTakesTheSlopeBetweenItsNeighbours()
        {
            var q1 = AboutZ(60f);
            var clip = Keep(new AnimationClip { legacy = true, name = "shaped" });
            for (int a = 0; a < 4; a++)
            {
                var k1 = a == 3
                    ? new Keyframe(1f, -q1.w, 0.1f, 0.2f, 0.25f, 0.4f) { weightedMode = WeightedMode.Both }
                    : new Keyframe(1f, -Component(q1, a), a == 2 ? 0.3f : 0f, a == 2 ? -0.2f : 0f);
                clip.SetCurve("", typeof(Transform), "localRotation." + s_Axes[a],
                    new AnimationCurve(new Keyframe(0f, Component(Quaternion.identity, a), 5f, 5f), k1, new Keyframe(3f, Component(q1, a))));
            }
            clip.EnsureQuaternionContinuity();
            var z = Keys(clip, "m_LocalRotation.z");
            Near.Equal(q1.z, z[1].value, 1e-6f);
            Assert.Equal(q1.z / 1f, z[0].inTangent);
            Assert.Equal(z[0].inTangent, z[0].outTangent);
            Assert.Equal((q1.z - 0f) / 3f, z[1].inTangent);
            Assert.Equal(z[1].inTangent, z[1].outTangent);
            Assert.Equal(0f, z[2].outTangent);
            var w = Keys(clip, "m_LocalRotation.w");
            Near.Equal(q1.w, w[1].value, 1e-6f);
            Assert.Equal((q1.w - 1f) / 3f, w[1].outTangent);
            Assert.Equal(WeightedMode.None, w[1].weightedMode);
        }

        /// <summary>Unbound's choice (unmeasured): realigned curves redo the clip's bindings (its
        /// version is bumped), so an Animation playing the clip samples the new keys. RED at b988:
        /// the sampled rotation stays on the long way.</summary>
        [Fact]
        public void APlayingAnimationSamplesTheRealignedKeys()
        {
            var clip = RotationClip(Quaternion.identity, Negated(AboutZ(60f)));
            var go = Keep(new GameObject("Legacy"));
            var animation = go.AddComponent<Animation>();
            animation.AddClip(clip, "turn");
            animation.Play("turn");
            animation.Step(0f); // a state's first update samples its start
            animation.Step(0.5f);
            int version = clip.m_Version;
            clip.EnsureQuaternionContinuity();
            Assert.Equal(version + 1, clip.m_Version);
            animation.Sample();
            Assert.True(Quaternion.Angle(AboutZ(30f), go.transform.localRotation) < 0.05f,
                $"got {go.transform.localRotation.ToString("R")}");
        }

        /// <summary>A rotation whose x, y, z, w curves have keys at different times keeps its keys,
        /// silently (matches the Unity 6000.0.71f1 Editor, tests/projects/editorui
        /// EnsureQuaternionContinuity.*, the second clip).</summary>
        [Fact]
        public void ARotationWithKeysAtDifferentTimesKeepsItsKeys()
        {
            var q1 = Negated(AboutZ(60f));
            var clip = Keep(new AnimationClip { legacy = true, name = "uneven" });
            for (int a = 0; a < 3; a++)
                clip.SetCurve("", typeof(Transform), "localRotation." + s_Axes[a],
                    new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, Component(q1, a))));
            // SetCurve would key all four at the union of their times; SetEditorCurve keeps w's own.
            UnityEditor.AnimationUtility.SetEditorCurve(clip, UnityEditor.EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalRotation.w"),
                new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(0.5f, 0f), new Keyframe(1f, q1.w)));
            int version = clip.m_Version;
            clip.EnsureQuaternionContinuity();
            Assert.DoesNotContain(m_Logs, l => l.Contains(KeptKeys));
            Assert.Equal(version, clip.m_Version);
            Near.Equal(q1.z, Keys(clip, "m_LocalRotation.z")[1].value, 0f);
            Near.Equal(q1.w, Keys(clip, "m_LocalRotation.w")[2].value, 0f);
        }

        /// <summary>A continuous rotation keeps its key values and takes the neighbours' slopes as its
        /// tangents; Euler angle curves and other float curves keep their keys (matches the Unity
        /// 6000.0.71f1 Editor, tests/projects/editorui EnsureQuaternionContinuity.*, the control).</summary>
        [Fact]
        public void ContinuousRotationsAndEulerCurvesAreKeptAsTheyAre()
        {
            var clip = RotationClip(Quaternion.identity, AboutZ(60f), AboutZ(120f));
            clip.SetCurve("", typeof(Transform), "localEulerAngles.y", new AnimationCurve(new Keyframe(0f, 350f), new Keyframe(1f, -10f)));
            clip.SetCurve("", typeof(Transform), "localPosition.x", new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, -1f)));
            int version = clip.m_Version;
            m_Expected.Add("does not flip quaternion keys yet"); // b988's stub warning, once per process
            clip.EnsureQuaternionContinuity();
            // Its tangents are the neighbours' slopes now (the editorui control), so the clip changed.
            Assert.Equal(version + 1, clip.m_Version);
            SameComponents(AboutZ(60f), KeyAt(clip, 1), 0f);
            Assert.Equal((AboutZ(120f).z - 0f) / 2f, Keys(clip, "m_LocalRotation.z")[1].outTangent);
            var euler = Keys(clip, "localEulerAnglesRaw.y");
            Assert.Equal(350f, euler[0].value);
            Assert.Equal(-10f, euler[1].value);
            Assert.Equal(-1f, Keys(clip, "m_LocalPosition.x")[1].value);
        }

        /// <summary>Unbound's choice (unmeasured): a clip made from the clip before the call
        /// (Animation.AddClip with frames) keeps its keys. RED at b988 only through the source
        /// clip's key 1 (not negated); the sub-clip assertion alone is a guard, green at b988 too.</summary>
        [Fact]
        public void ClipsMadeFromTheClipBeforeKeepTheirKeys()
        {
            var clip = RotationClip(Quaternion.identity, Negated(AboutZ(60f)), Quaternion.identity);
            clip.frameRate = 10f;
            var go = Keep(new GameObject("Legacy"));
            var animation = go.AddComponent<Animation>();
            animation.AddClip(clip, "part", 0, 20);
            var part = animation.GetClip("part");
            Assert.NotSame(clip, part);
            clip.EnsureQuaternionContinuity();
            SameComponents(AboutZ(60f), KeyAt(clip, 1));
            SameComponents(Negated(AboutZ(60f)), KeyAt(part, 1), 0f);
        }

        // ------------------------------------------------------------------ SyncLayer

        /// <summary>A looping legacy clip of <paramref name="length"/> seconds.</summary>
        AnimationClip LoopClip(string name, float length)
        {
            var clip = Keep(new AnimationClip { legacy = true, wrapMode = WrapMode.Loop, name = name });
            clip.SetCurve("", typeof(Transform), "localPosition.x", new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(length, 1f)));
            return clip;
        }

        Animation LayeredAnimation(params (string Name, float Length, int Layer)[] states)
        {
            var go = Keep(new GameObject("Legacy"));
            var animation = go.AddComponent<Animation>();
            foreach (var (name, length, layer) in states)
            {
                animation.AddClip(LoopClip(name, length), name);
                animation[name].layer = layer;
            }
            return animation;
        }

        /// <summary>Enables a state at a weight at once (a Blend's weight takes an update).</summary>
        static void Enable(Animation animation, string name, float weight)
        {
            animation[name].enabled = true;
            animation[name].weight = weight;
        }

        /// <summary>Not documented in 6000.0; Unbound's reading (unmeasured): the playback speed of
        /// the layer's animations is synchronized. Hypotheses (unmeasured): the layer stays synchronized from a call made before its states
        /// play; its states advance by the weight-weighted mean normalizedSpeed (0.5·1/1 + 0.5·1/2
        /// = 0.75 per second), a weight-0 state follows without contributing, their speed
        /// properties keep their values, and other layers advance as before. RED at b988: A reaches
        /// normalized time 0.2, B 0.1, D 0.05.</summary>
        [Fact]
        public void SynchronizedStatesAdvanceByTheirWeightedMeanNormalizedSpeed()
        {
            var animation = LayeredAnimation(("A", 1f, 1), ("B", 2f, 1), ("D", 4f, 1), ("C", 1f, 0));
            m_Expected.Add("Animation.SyncLayer"); // the once-per-process unmeasured-semantics notice
            animation.SyncLayer(1);
            animation.Play("C");
            Enable(animation, "A", 0.5f);
            Enable(animation, "B", 0.5f);
            Enable(animation, "D", 0f);
            animation.Step(0f); // started states are sampled where they start
            animation.Step(0.2f);
            Near.Equal(0.15f, animation["A"].normalizedTime);
            Near.Equal(0.15f, animation["B"].normalizedTime);
            Near.Equal(0.15f, animation["D"].normalizedTime);
            Near.Equal(0.2f, animation["C"].normalizedTime);
            Assert.Equal(1f, animation["A"].speed);
            Assert.Equal(1f, animation["B"].speed);
            Near.Equal(0.5f, animation["B"].normalizedSpeed);
        }

        /// <summary>Hypothesis (unmeasured): weights and speeds both enter the mean, and normalized
        /// times keep their offsets. Mean = (0.25·2/1 + 0.75·1/2) = 0.875 per second. RED at b988:
        /// A advances by 0.8, B by 0.2.</summary>
        [Fact]
        public void SynchronizedStatesKeepTheirNormalizedTimeOffsets()
        {
            var animation = LayeredAnimation(("A", 1f, 2), ("B", 2f, 2));
            m_Expected.Add("Animation.SyncLayer"); // the once-per-process unmeasured-semantics notice
            animation.SyncLayer(2);
            animation["A"].speed = 2f;
            Enable(animation, "A", 0.25f);
            Enable(animation, "B", 0.75f);
            animation.Step(0f);
            animation["B"].normalizedTime = 0.5f;
            animation.Step(0.4f);
            Near.Equal(0.35f, animation["A"].normalizedTime);
            Near.Equal(0.85f, animation["B"].normalizedTime);
            Assert.Equal(2f, animation["A"].speed);
        }

        /// <summary>Guard, not a red (green at b988 too): a synchronized layer without weight, and a
        /// layer without states, change nothing (Unbound's choice, unmeasured).</summary>
        [Fact]
        public void LayersWithoutWeightOrStatesAdvanceAsBefore()
        {
            var animation = LayeredAnimation(("A", 1f, 3), ("B", 2f, 3));
            m_Expected.Add("Animation.SyncLayer"); // b988's stub warning, once per process
            animation.SyncLayer(3);
            animation.SyncLayer(42);
            animation.Blend("A", 0f, 0f);
            animation.Blend("B", 0f, 0f);
            animation.Step(0f);
            animation.Step(0.2f);
            Near.Equal(0.2f, animation["A"].normalizedTime);
            Near.Equal(0.1f, animation["B"].normalizedTime);
        }

        // ------------------------------------------------------------------ IsParameterControlledByCurve

        static LoadedFile Load(string yaml) =>
            ObjectGraphBuilder.Build(UnityYaml.Parse(yaml), "test.asset", null, ObjectGraphBuilder.Kind.Asset, null);

        /// <summary>The locomotion controller with its Idle clip also driving the float Speed.</summary>
        static RuntimeAnimatorController SpeedCurveController()
        {
            var file = Load(AnimationFixtures.Locomotion);
            var idle = (AnimationClip)file.Objects[7400001];
            idle.m_Curves.Add(new ClipCurve
            {
                Binding = new CurveBinding { Path = "", Attribute = "Speed", ClassId = 95 },
                Curve = AnimationCurve.Constant(0f, 1f, 0.8f),
            });
            idle.m_Version++;
            return (RuntimeAnimatorController)file.Objects[9100000];
        }

        /// <summary>Documented: true when a curve controls the parameter. The playable answers as
        /// the Animator does for the same controller (Speed by a clip curve; Run is a bool, Missing
        /// unknown). RED at b988: always false.</summary>
        [Fact]
        public void AControllerPlayableAnswersWhetherACurveDrivesItsParameter()
        {
            var controller = SpeedCurveController();
            var go = Keep(new GameObject("Character"));
            new GameObject("Body").transform.SetParent(go.transform, false);
            var animator = go.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            var g = PlayableGraph.Create("curves");
            try
            {
                var output = AnimationPlayableOutput.Create(g, "anim", animator);
                var playable = AnimatorControllerPlayable.Create(g, controller);
                output.SetSourcePlayable(playable);
                Assert.True(animator.IsParameterControlledByCurve("Speed"));
                Assert.True(playable.IsParameterControlledByCurve("Speed"));
                Assert.True(playable.IsParameterControlledByCurve(Animator.StringToHash("Speed")));
                Assert.False(playable.IsParameterControlledByCurve("Run"));
                Assert.False(playable.IsParameterControlledByCurve(Animator.StringToHash("Run")));
                Assert.False(playable.IsParameterControlledByCurve("Missing"));
                Assert.False(playable.IsParameterControlledByCurve(Animator.StringToHash("Missing")));
            }
            finally
            {
                g.Destroy();
            }
        }

        /// <summary>Guard, not a red (green at b988 too): the null playable and a playable no output
        /// plays answer false without throwing (existing convention of the playable's queries).</summary>
        [Fact]
        public void PlayablesWithoutAPlayerAnswerFalse()
        {
            m_Expected.Add("AnimatorControllerPlayable.IsParameterControlledByCurve"); // b988's stub warning
            Assert.False(AnimatorControllerPlayable.Null.IsParameterControlledByCurve("Speed"));
            Assert.False(AnimatorControllerPlayable.Null.IsParameterControlledByCurve(Animator.StringToHash("Speed")));
            var g = PlayableGraph.Create("unplayed");
            try
            {
                var playable = AnimatorControllerPlayable.Create(g, SpeedCurveController());
                Assert.False(playable.IsParameterControlledByCurve("Speed"));
            }
            finally
            {
                g.Destroy();
            }
        }
    }
}
