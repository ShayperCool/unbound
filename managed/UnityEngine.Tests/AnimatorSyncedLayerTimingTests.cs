using System.Collections.Generic;
using System.Globalization;
using Unbound.Runtime;
using Unbound.Tests;
using UnityEngine;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// A synced layer's "Timing" (AnimatorControllerLayer.syncedLayerAffectsTiming). Documented
    /// (6000.0 Manual, Animation Layers): without Timing, the synced layer's animations are
    /// stretched to the length of the original layer's, which stays the sole master; with Timing,
    /// the length is "a balance for both animations, based on weight", a compromise. The Scripting
    /// API: "When active, the layer will take control of the duration of the Synced Layer", and
    /// AnimatorStateInfo.length is the "current duration of the state". The endpoints (the
    /// source's length at weight 0, the synced motion's at weight 1, which the other Timing tests
    /// rely on) are documented inferences: Unbound's reading of those sentences, not stated
    /// outright and not measured. Between them only the documented compromise is asserted (a
    /// length strictly between both motions'), not Unbound's linear midpoint. What a synced layer
    /// without Timing reports as its length, and the 1 s a missing synced motion counts as, are
    /// not asserted (not measured; the latter warns).
    ///
    /// The fixture: the base layer plays A (1 s, Body x from 0 to 1) and B (1 s), going from A to
    /// B on "Go" over half of A's duration (a normalized duration). Layer 1 is synced to it, an
    /// override layer playing A with a 3 s clip (Body y from 0 to 3, so y is the clip time) and B
    /// with a 2 s one.
    /// </summary>
    [Collection("Animation")]
    public class AnimatorSyncedLayerTimingTests : System.IDisposable
    {
        readonly List<string> m_Logs = new List<string>();
        readonly List<string> m_Expected = new List<string>();
        readonly List<Object> m_Created = new List<Object>();
        readonly ILogger m_Logger = Debug.unityLogger;
        readonly ILogHandler m_PreviousHandler;
        readonly bool m_PreviousLogEnabled;
        readonly LogType m_PreviousFilter;
        const string SeveralTimingLayers = "with Timing on; their durations are combined in layer order";
        const string MissingSyncedMotion = "with Timing on but has no motion for state(s) 'B'";
        const float PoseTolerance = 1e-4f;

        public AnimatorSyncedLayerTimingTests()
        {
            m_PreviousHandler = m_Logger.logHandler;
            m_PreviousLogEnabled = m_Logger.logEnabled;
            m_PreviousFilter = m_Logger.filterLogType;
            // Unbound diagnostics bypass the game's Application log callbacks: observe them here.
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
                        : string.Format(CultureInfo.InvariantCulture, format, args);
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

        // ------------------------------------------------------------------ fixture

        static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>A looping clip moving Body's <paramref name="attribute"/> linearly.</summary>
        static string Clip(long id, string name, string attribute, float length, float from, float to)
        {
            float slope = (to - from) / length;
            return $$"""
--- !u!74 &{{id}}
AnimationClip:
  m_Name: {{name}}
  m_Legacy: 0
  m_FloatCurves:
  - serializedVersion: 2
    curve:
      serializedVersion: 2
      m_Curve:
      - serializedVersion: 3
        time: 0
        value: {{F(from)}}
        inSlope: {{F(slope)}}
        outSlope: {{F(slope)}}
        tangentMode: 0
        weightedMode: 0
      - serializedVersion: 3
        time: {{F(length)}}
        value: {{F(to)}}
        inSlope: {{F(slope)}}
        outSlope: {{F(slope)}}
        tangentMode: 0
        weightedMode: 0
      m_PreInfinity: 2
      m_PostInfinity: 2
    attribute: {{attribute}}
    path: Body
    classID: 4
    script: {fileID: 0}
  m_SampleRate: 60
  m_AnimationClipSettings:
    m_StartTime: 0
    m_StopTime: {{F(length)}}
    m_LoopTime: 1
  m_Events: []
""";
        }

        const string SyncedMotions = """
    m_Motions:
    - serializedVersion: 2
      m_State: {fileID: 1102000001}
      m_Motion: {fileID: 7400003}
    - serializedVersion: 2
      m_State: {fileID: 1102000002}
      m_Motion: {fileID: 7400004}
""";

        /// <summary>The synced layer's motions with none for B.</summary>
        const string SyncedMotionsWithoutB = """
    m_Motions:
    - serializedVersion: 2
      m_State: {fileID: 1102000001}
      m_Motion: {fileID: 7400003}
""";

        static string Layer(string name, long machine, string motions, int syncedIndex, bool timing) => $$"""
  - serializedVersion: 5
    m_Name: {{name}}
    m_StateMachine: {fileID: {{machine}}}
    m_Mask: {fileID: 0}
{{motions}}
    m_Behaviours: []
    m_BlendingMode: 0
    m_SyncedLayerIndex: {{syncedIndex}}
    m_DefaultWeight: 1
    m_IKPass: 0
    m_SyncedLayerAffectsTiming: {{(timing ? 1 : 0)}}
    m_Controller: {fileID: 9100000}
""";

        /// <summary>The fixture controller. <paramref name="syncedIndex"/> -1 makes layer 1 an
        /// ordinary layer without states (its Timing flag then means nothing);
        /// <paramref name="syncedLayers"/> 2 adds a second synced layer with the same Timing;
        /// <paramref name="syncedB"/> false leaves the synced layers without a motion for B.</summary>
        static string Controller(bool timing, float speedA = 1f, int syncedIndex = 0, int syncedLayers = 1, bool syncedB = true)
        {
            var layers = new List<string> { Layer("Base Layer", 1107000001, "    m_Motions: []", -1, false) };
            string synced = syncedB ? SyncedMotions : SyncedMotionsWithoutB;
            for (int i = 0; i < syncedLayers; i++)
                layers.Add(Layer("Synced " + i, 0, syncedIndex >= 0 ? synced : "    m_Motions: []", syncedIndex, timing));
            var parts = new List<string>
            {
                """
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!91 &9100000
AnimatorController:
  m_Name: SyncedTiming
  serializedVersion: 5
  m_AnimatorParameters:
  - m_Name: Go
    m_Type: 4
    m_DefaultFloat: 0
    m_DefaultInt: 0
    m_DefaultBool: 0
    m_Controller: {fileID: 9100000}
  m_AnimatorLayers:
""",
            };
            parts.AddRange(layers);
            parts.Add($$"""
--- !u!1107 &1107000001
AnimatorStateMachine:
  serializedVersion: 6
  m_Name: Base Layer
  m_ChildStates:
  - serializedVersion: 1
    m_State: {fileID: 1102000001}
  - serializedVersion: 1
    m_State: {fileID: 1102000002}
  m_ChildStateMachines: []
  m_AnyStateTransitions: []
  m_EntryTransitions: []
  m_StateMachineTransitions: {}
  m_StateMachineBehaviours: []
  m_DefaultState: {fileID: 1102000001}
--- !u!1102 &1102000001
AnimatorState:
  serializedVersion: 6
  m_Name: A
  m_Speed: {{F(speedA)}}
  m_CycleOffset: 0
  m_Transitions:
  - {fileID: 1101000001}
  m_StateMachineBehaviours: []
  m_WriteDefaultValues: 1
  m_Motion: {fileID: 7400001}
  m_Tag:
--- !u!1102 &1102000002
AnimatorState:
  serializedVersion: 6
  m_Name: B
  m_Speed: 1
  m_CycleOffset: 0
  m_Transitions: []
  m_StateMachineBehaviours: []
  m_WriteDefaultValues: 1
  m_Motion: {fileID: 7400002}
  m_Tag:
--- !u!1101 &1101000001
AnimatorStateTransition:
  m_Name:
  m_Conditions:
  - m_ConditionMode: 1
    m_ConditionEvent: Go
    m_EventTreshold: 0
  m_DstStateMachine: {fileID: 0}
  m_DstState: {fileID: 1102000002}
  m_Solo: 0
  m_Mute: 0
  m_IsExit: 0
  serializedVersion: 3
  m_TransitionDuration: 0.5
  m_TransitionOffset: 0
  m_ExitTime: 0
  m_HasExitTime: 0
  m_HasFixedDuration: 0
  m_InterruptionSource: 0
  m_OrderedInterruption: 1
  m_CanTransitionToSelf: 1
""");
            parts.Add(Clip(7400001, "SourceA", "m_LocalPosition.x", 1f, 0f, 1f));
            parts.Add(Clip(7400002, "SourceB", "m_LocalPosition.x", 1f, 5f, 5f));
            parts.Add(Clip(7400003, "SyncedA", "m_LocalPosition.y", 3f, 0f, 3f));
            parts.Add(Clip(7400004, "SyncedB", "m_LocalPosition.y", 2f, 7f, 7f));
            return string.Join("\n", parts) + "\n";
        }

        static RuntimeAnimatorController Load(string yaml)
        {
            var file = ObjectGraphBuilder.Build(UnityYaml.Parse(yaml), "test.asset", null, ObjectGraphBuilder.Kind.Asset, null);
            return (RuntimeAnimatorController)file.Objects[9100000];
        }

        Animator NewAnimator(RuntimeAnimatorController controller, out Transform body)
        {
            var root = new GameObject("Character");
            m_Created.Add(root);
            var child = new GameObject("Body");
            child.transform.SetParent(root.transform, false);
            body = child.transform;
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = controller;
            return animator;
        }

        static void AssertState(Animator animator, int layer, string name, float normalizedTime, float length)
        {
            var info = animator.GetCurrentAnimatorStateInfo(layer);
            Assert.True(info.IsName(name), $"layer {layer} plays {name}");
            Near.Equal(normalizedTime, info.normalizedTime);
            Near.Equal(length, info.length);
        }

        // ------------------------------------------------------------------ Timing off

        [Theory]
        [InlineData(0f)]
        [InlineData(0.5f)]
        [InlineData(1f)]
        public void WithoutTimingTheSourceLayerIsTheSoleMaster(float weight)
        {
            // Documented: the synced animation is stretched to the source's length, whatever the
            // weight. After 0.6 s both layers are 0.6 into A: the 3 s synced clip is at 1.8 s.
            var animator = NewAnimator(Load(Controller(timing: false)), out var body);
            animator.SetLayerWeight(1, weight);
            animator.Update(0.6f);
            AssertState(animator, 0, "A", 0.6f, 1f);
            Near.Equal(0.6f, animator.GetCurrentAnimatorStateInfo(1).normalizedTime);
            Near.Equal(new Vector3(0.6f, weight * 1.8f, 0f), body.localPosition, PoseTolerance);
        }

        [Fact]
        public void WithoutTimingANormalizedTransitionScalesTheSourceDuration()
        {
            // Half of A's 1 s: the transition started after 0.3 s is over 0.5 s later; B advanced
            // 0.6 s of its 1 s source clip meanwhile.
            var animator = NewAnimator(Load(Controller(timing: false)), out _);
            animator.SetBool("Go", true);
            animator.Update(0.3f);
            Assert.True(animator.IsInTransition(1));
            animator.Update(0.6f);
            Assert.False(animator.IsInTransition(0));
            Assert.False(animator.IsInTransition(1));
            Assert.True(animator.GetCurrentAnimatorStateInfo(1).IsName("B"));
            Near.Equal(0.6f, animator.GetCurrentAnimatorStateInfo(1).normalizedTime);
        }

        [Fact]
        public void TimingFlagOfALayerThatIsNotSyncedDoesNothing()
        {
            var animator = NewAnimator(Load(Controller(timing: true, syncedIndex: -1)), out var body);
            animator.Update(0.6f);
            AssertState(animator, 0, "A", 0.6f, 1f);
            Near.Equal(0.6f, body.localPosition.x, PoseTolerance);
        }

        // ------------------------------------------------------------------ Timing on

        [Theory]
        [InlineData(0f, 1f)] // documented inference (Unbound's reading of "based on weight"): the source's length alone
        [InlineData(1f, 3f)] // documented inference (Unbound's reading of "take control of the duration"): the synced motion's length
        public void WithTimingBothLayersPlayTheBalancedDuration(float weight, float duration)
        {
            var animator = NewAnimator(Load(Controller(timing: true)), out var body);
            animator.SetLayerWeight(1, weight);
            animator.Update(0.6f);
            float normalized = 0.6f / duration;
            // Documented: the synced layer also changes the source layer's timing.
            AssertState(animator, 0, "A", normalized, duration);
            AssertState(animator, 1, "A", normalized, duration);
            Near.Equal(new Vector3(normalized, weight * 3f * normalized, 0f), body.localPosition, PoseTolerance);
        }

        [Fact]
        public void WithTimingAtHalfWeightBothLayersPlayACompromise()
        {
            // Documented: "a balance for both animations, based on weight", a compromise. Only that
            // the length lies strictly between the 1 s source and the 3 s synced motion is asserted;
            // Unbound's linear midpoint is not.
            var animator = NewAnimator(Load(Controller(timing: true)), out _);
            animator.SetLayerWeight(1, 0.5f);
            animator.Update(0.6f);
            for (int layer = 0; layer < 2; layer++)
            {
                var info = animator.GetCurrentAnimatorStateInfo(layer);
                Assert.True(info.IsName("A"), $"layer {layer} plays A");
                Assert.True(info.length > 1f && info.length < 3f, $"layer {layer} length {info.length} between 1 and 3");
            }
        }

        [Fact]
        public void WithTimingTheStateSpeedScalesTheBalancedDuration()
        {
            // Speed 2 over the 3 s balanced duration (weight 1): 0.6 s go 0.4 into A.
            var animator = NewAnimator(Load(Controller(timing: true, speedA: 2f)), out var body);
            animator.Update(0.6f);
            for (int layer = 0; layer < 2; layer++)
            {
                AssertState(animator, layer, "A", 0.4f, 3f);
                Near.Equal(2f, animator.GetCurrentAnimatorStateInfo(layer).speed);
            }
            Near.Equal(new Vector3(0.4f, 1.2f, 0f), body.localPosition, PoseTolerance);
        }

        [Fact]
        public void WithTimingTheWeightOfEachUpdateBalancesIt()
        {
            var animator = NewAnimator(Load(Controller(timing: true)), out _);
            animator.Update(0.6f); // weight 1: 3 s
            Near.Equal(0.2f, animator.GetCurrentAnimatorStateInfo(0).normalizedTime);
            animator.SetLayerWeight(1, 0f);
            animator.Update(0.6f); // weight 0: the source's 1 s
            AssertState(animator, 0, "A", 0.8f, 1f);
            AssertState(animator, 1, "A", 0.8f, 1f);
        }

        [Fact]
        public void WithTimingANormalizedTransitionScalesTheBalancedDuration()
        {
            // Weight 1: A lasts 3 s, so half of it is 1.5 s; B lasts 2 s on both layers.
            var animator = NewAnimator(Load(Controller(timing: true)), out _);
            animator.SetBool("Go", true);
            animator.Update(0.3f); // A 0.1 in; the transition starts
            animator.Update(0.6f);
            for (int layer = 0; layer < 2; layer++)
            {
                Assert.True(animator.IsInTransition(layer), $"layer {layer} still in transition");
                Near.Equal(0.4f, animator.GetAnimatorTransitionInfo(layer).normalizedTime);
                AssertState(animator, layer, "A", 0.3f, 3f);
                var next = animator.GetNextAnimatorStateInfo(layer);
                Assert.True(next.IsName("B"));
                Near.Equal(0.3f, next.normalizedTime);
                Near.Equal(2f, next.length);
            }
            animator.Update(1f); // past 1.5 s: B, 0.8 into its 2 s
            Assert.False(animator.IsInTransition(0));
            Assert.False(animator.IsInTransition(1));
            AssertState(animator, 0, "B", 0.8f, 2f);
            AssertState(animator, 1, "B", 0.8f, 2f);
        }

        [Fact]
        public void AnOverrideControllerKeepsTheSyncedLayersTiming()
        {
            var controller = Load(Controller(timing: true));
            var over = new AnimatorOverrideController(controller);
            var animator = NewAnimator(over, out _);
            animator.Update(0.6f);
            AssertState(animator, 0, "A", 0.2f, 3f);
            AssertState(animator, 1, "A", 0.2f, 3f);
        }

        [Fact]
        public void SeveralTimingLayersOnOneSourceWarn()
        {
            // How several Timing layers combine is not measured: the combination is not asserted.
            m_Expected.Add(SeveralTimingLayers);
            var animator = NewAnimator(Load(Controller(timing: true, syncedLayers: 2)), out _);
            animator.Update(0.1f);
            Assert.Single(m_Logs.FindAll(l => l.StartsWith("Warning:") && l.Contains(SeveralTimingLayers)));
        }

        [Fact]
        public void TimingLayerWithoutAStatesMotionWarns()
        {
            // What a missing synced motion counts as in the balance is not measured: only the
            // warning is asserted, not the duration.
            m_Expected.Add(MissingSyncedMotion);
            var animator = NewAnimator(Load(Controller(timing: true, syncedB: false)), out _);
            animator.Update(0.1f);
            Assert.Single(m_Logs.FindAll(l => l.StartsWith("Warning:") && l.Contains(MissingSyncedMotion)));
        }
    }
}
