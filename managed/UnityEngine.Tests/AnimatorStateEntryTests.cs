using System.Collections.Generic;
using System.Globalization;
using Unbound.Runtime;
using Unbound.Tests;
using UnityEngine;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// The first update of an Animator state and transitions around it (6000.0.71f1,
    /// tests/projects/humanoid exit*, crossFade*, playState, emptyExit, emptyCond, events*,
    /// additiveMirrorClip and their generic.* counterparts). The Manual (Transitions, Animator
    /// Controller) and Animator.Play/CrossFade pages do not state these timings; they are measured.
    /// The fixture's clips move the root along x looping over 1 s: Walk at 1 unit/s, Run at 2.
    /// </summary>
    [Collection("Animation")]
    public sealed class AnimatorStateEntryTests : System.IDisposable
    {
        readonly List<Object> m_Created = new List<Object>();
        readonly List<string> m_Logs = new List<string>();
        readonly ILogger m_Logger = Debug.unityLogger;
        readonly ILogHandler m_PreviousHandler;
        readonly string m_Version = ProjectInfo.UnityVersion;

        public AnimatorStateEntryTests()
        {
            ProjectInfo.UnityVersion = "6000.0.71f1";
            m_PreviousHandler = m_Logger.logHandler;
            // Unbound diagnostics bypass Application.logMessageReceived: capture them here.
            m_Logger.logHandler = new CaptureHandler(m_PreviousHandler, m_Logs);
        }

        public void Dispose()
        {
            foreach (var o in m_Created)
                if (o != null) Object.DestroyImmediate(o);
            m_Logger.logHandler = m_PreviousHandler;
            ProjectInfo.UnityVersion = m_Version;
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
                    m_Logs.Add(type + ": " + (args == null || args.Length == 0 ? format : string.Format(CultureInfo.InvariantCulture, format, args)));
                m_Inner.LogFormat(type, context, format, args);
            }

            public void LogException(System.Exception exception, Object context)
            {
                m_Logs.Add("Exception: " + exception);
                m_Inner.LogException(exception, context);
            }
        }

        // ------------------------------------------------------------------ fixture

        static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);

        /// <summary>A looping clip moving the root along x at <paramref name="speed"/> units/s over
        /// 1 s; <paramref name="human"/> adds a muscle curve (a humanoid clip) and
        /// <paramref name="mirror"/> its Mirror setting.</summary>
        static string Clip(long id, string name, float speed, bool human = false, bool mirror = false) => $$"""
--- !u!74 &{{id}}
AnimationClip:
  m_Name: {{name}}
  m_Legacy: 0
  m_PositionCurves:
  - curve:
      m_Curve:
      - serializedVersion: 3
        time: 0
        value: {x: 0, y: 0, z: 0}
        inSlope: {x: {{F(speed)}}, y: 0, z: 0}
        outSlope: {x: {{F(speed)}}, y: 0, z: 0}
        tangentMode: 0
      - serializedVersion: 3
        time: 1
        value: {x: {{F(speed)}}, y: 0, z: 0}
        inSlope: {x: {{F(speed)}}, y: 0, z: 0}
        outSlope: {x: {{F(speed)}}, y: 0, z: 0}
        tangentMode: 0
      m_PreInfinity: 2
      m_PostInfinity: 2
    path:
  m_FloatCurves:{{(human ? """

  - curve:
      m_Curve:
      - serializedVersion: 3
        time: 0
        value: 0.2
        inSlope: -0.6
        outSlope: -0.6
        tangentMode: 0
      - serializedVersion: 3
        time: 1
        value: -0.4
        inSlope: -0.6
        outSlope: -0.6
        tangentMode: 0
      m_PreInfinity: 2
      m_PostInfinity: 2
    attribute: Left Arm Down-Up
    path:
    classID: 95
    script: {fileID: 0}
""" : " []")}}
  m_SampleRate: 60
  m_AnimationClipSettings:
    m_StartTime: 0
    m_StopTime: 1
    m_LoopTime: 1
    m_Mirror: {{(mirror ? 1 : 0)}}
  m_Events: []

""";

        /// <summary>A state with its transitions (fileIDs).</summary>
        static string State(long id, string name, long motion, bool mirror = false, params long[] transitions) =>
            State(id, name, motion, mirror, null, transitions);

        static string State(long id, string name, long motion, bool mirror, string timeParameter, params long[] transitions) => $$"""
--- !u!1102 &{{id}}
AnimatorState:
  serializedVersion: 6
  m_Name: {{name}}
  m_Speed: 1
  m_CycleOffset: 0
  m_Transitions:{{(transitions.Length == 0 ? " []" : "\n" + string.Join("\n", System.Array.ConvertAll(transitions, t => "  - {fileID: " + t + "}")))}}
  m_StateMachineBehaviours: []
  m_Mirror: {{(mirror ? 1 : 0)}}
  m_TimeParameterActive: {{(timeParameter != null ? 1 : 0)}}
  m_TimeParameter: {{timeParameter ?? ""}}
  m_WriteDefaultValues: 1
  m_Motion: {fileID: {{motion}}}
  m_Tag:

""";

        static string Transition(long id, long destination, float exitTime, float duration) => $$"""
--- !u!1101 &{{id}}
AnimatorStateTransition:
  m_Name:
  m_Conditions: []
  m_DstStateMachine: {fileID: 0}
  m_DstState: {fileID: {{destination}}}
  m_Solo: 0
  m_Mute: 0
  m_IsExit: 0
  serializedVersion: 3
  m_TransitionDuration: {{F(duration)}}
  m_TransitionOffset: 0
  m_ExitTime: {{F(exitTime)}}
  m_HasExitTime: 1
  m_HasFixedDuration: 1
  m_InterruptionSource: 0
  m_OrderedInterruption: 1
  m_CanTransitionToSelf: 1

""";

        static string Machine(long id, long defaultState, params long[] states) => $$"""
--- !u!1107 &{{id}}
AnimatorStateMachine:
  serializedVersion: 6
  m_Name: Machine{{id}}
  m_ChildStates:
{{string.Join("\n", System.Array.ConvertAll(states, s => "  - serializedVersion: 1\n    m_State: {fileID: " + s + "}"))}}
  m_ChildStateMachines: []
  m_AnyStateTransitions: []
  m_EntryTransitions: []
  m_StateMachineTransitions: {}
  m_StateMachineBehaviours: []
  m_DefaultState: {fileID: {{defaultState}}}

""";

        static string Layer(string name, long machine, int blending) => $$"""
  - serializedVersion: 5
    m_Name: {{name}}
    m_StateMachine: {fileID: {{machine}}}
    m_Mask: {fileID: 0}
    m_Motions: []
    m_Behaviours: []
    m_BlendingMode: {{blending}}
    m_SyncedLayerIndex: -1
    m_DefaultWeight: 1
    m_IKPass: 0
    m_SyncedLayerAffectsTiming: 0
    m_Controller: {fileID: 9100000}

""";

        const long Walk = 7400001, Run = 7400002;
        const long EmptyState = 1102000001, WalkState = 1102000002, RunState = 1102000003;

        /// <summary>One layer with Empty, Walk and Run, starting in <paramref name="first"/>, with
        /// a transition from it to <paramref name="second"/> when <paramref name="exitTime"/> is set;
        /// Empty writes defaults unless <paramref name="emptyWriteDefaults"/> is false.</summary>
        RuntimeAnimatorController Controller(long first, long second = 0, float exitTime = float.NaN, float duration = 0f,
            bool emptyWriteDefaults = true)
        {
            bool transition = !float.IsNaN(exitTime);
            long[] From(long state) => transition && state == first ? new long[] { 1101000001 } : System.Array.Empty<long>();
            var yaml = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!91 &9100000\nAnimatorController:\n  m_Name: Entry\n"
                + "  serializedVersion: 5\n  m_AnimatorParameters: []\n  m_AnimatorLayers:\n" + Layer("Base Layer", 1107000001, 0)
                + Machine(1107000001, first, EmptyState, WalkState, RunState)
                + State(EmptyState, "Empty", 0, false, From(EmptyState))
                    .Replace("m_WriteDefaultValues: 1", emptyWriteDefaults ? "m_WriteDefaultValues: 1" : "m_WriteDefaultValues: 0")
                + State(WalkState, "Walk", Walk, false, From(WalkState))
                + State(RunState, "Run", Run, false, From(RunState))
                + (transition ? Transition(1101000001, second, exitTime, duration) : "")
                + Clip(Walk, "Walk", 1f) + Clip(Run, "Run", 2f);
            return Load(yaml);
        }

        RuntimeAnimatorController Load(string yaml)
        {
            var file = ObjectGraphBuilder.Build(UnityYaml.Parse(yaml), "entry.asset", null, ObjectGraphBuilder.Kind.Asset, null);
            foreach (var o in file.Objects.Values) m_Created.Add(o);
            return (RuntimeAnimatorController)file.Objects[9100000];
        }

        Animator Rig(RuntimeAnimatorController controller)
        {
            var root = new GameObject("Rig");
            m_Created.Add(root);
            var animator = root.AddComponent<Animator>();
            animator.applyRootMotion = true;
            animator.runtimeAnimatorController = controller;
            return animator;
        }

        static void AssertState(Animator animator, string name, float normalizedTime)
        {
            var info = animator.GetCurrentAnimatorStateInfo(0);
            Assert.True(info.IsName(name), "plays " + name);
            Near.Equal(normalizedTime, info.normalizedTime, 1e-5f);
        }

        // ------------------------------------------------------------------ tests

        /// <summary>Play enters its state at the requested time; that update does not advance it, and
        /// its root motion covers the whole update up to there (Run from 0.9 to its end: 0.2).</summary>
        [Fact]
        public void PlayHoldsTheStateItEntersForThatUpdate()
        {
            var animator = Rig(Controller(WalkState));
            animator.Update(0f);
            animator.Update(0.1f);
            animator.Play("Run");
            animator.Update(0.1f);
            AssertState(animator, "Run", 0f);
            Near.Equal(0.2f, animator.deltaPosition.x, 1e-5f);
            animator.Update(0.1f);
            AssertState(animator, "Run", 0.1f);
            Near.Equal(0.2f, animator.deltaPosition.x, 1e-5f);

            // An offset is the time it holds: Walk from 0.4 to 0.5.
            animator.Play("Walk", 0, 0.5f);
            animator.Update(0.1f);
            AssertState(animator, "Walk", 0.5f);
            Near.Equal(0.1f, animator.deltaPosition.x, 1e-5f);
        }

        /// <summary>In the update a transition from a motion state into an empty state (write
        /// defaults) completes, the motion state still moves the root over the whole update; then
        /// nothing moves it (humanoid emptyBack, generic.emptyBack). Control: a transition into a
        /// motion state ends with the destination's step alone.</summary>
        [Fact]
        public void ATransitionIntoAnEmptyStateMovesTheRootUntilItCompletes()
        {
            var animator = Rig(Controller(WalkState, EmptyState, 0.2f, 0.1f));
            animator.Update(0f);
            bool completed = false;
            for (int step = 0; step < 12 && !completed; step++)
            {
                animator.Update(0.05f);
                completed = animator.GetCurrentAnimatorStateInfo(0).IsName("Empty") && !animator.IsInTransition(0);
                Near.Equal(0.05f, animator.deltaPosition.x, 1e-5f);
            }
            Assert.True(completed);
            animator.Update(0.05f);
            Near.Equal(0f, animator.deltaPosition.x, 1e-6f);

            var control = Rig(Controller(WalkState, RunState, 0.2f, 0.1f));
            control.Update(0f);
            for (int step = 0; step < 12; step++)
            {
                control.Update(0.05f);
                if (!control.IsInTransition(0) && control.GetCurrentAnimatorStateInfo(0).IsName("Run")) break;
            }
            Near.Equal(0.1f, control.deltaPosition.x, 1e-5f);
        }

        /// <summary>A state without a motion and without write defaults keeps the layer's last root
        /// motion: once Walk has left for it, each update moves the root by Walk's last step again,
        /// in the root's frame and whatever the delta time (6000.0.71f1, tests/projects/humanoid
        /// generic.noWD, noWDAlone). Controls: with write defaults it moves nothing (above), and as
        /// the first state it has no step to keep.</summary>
        [Fact]
        public void AnEmptyStateWithoutWriteDefaultsRepeatsTheLayersLastStep()
        {
            var animator = Rig(Controller(WalkState, EmptyState, 0.2f, 0f, emptyWriteDefaults: false));
            animator.Update(0f);
            for (int step = 0; step < 12 && !animator.GetCurrentAnimatorStateInfo(0).IsName("Empty"); step++)
                animator.Update(0.05f);
            Assert.True(animator.GetCurrentAnimatorStateInfo(0).IsName("Empty"));
            animator.Update(0.05f);
            Near.Equal(0.05f, animator.deltaPosition.x, 1e-5f);
            animator.Update(0.2f);
            Near.Equal(0.05f, animator.deltaPosition.x, 1e-5f);
            animator.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            animator.Update(0.05f);
            Near.Equal(0f, animator.deltaPosition.x, 1e-5f);
            Near.Equal(-0.05f, animator.deltaPosition.z, 1e-5f);

            // An explicit Update(0) moves it again too (humanoid generic.noWD, step 12).
            var before = animator.transform.position;
            animator.Update(0f);
            Near.Equal(-0.05f, animator.transform.position.z - before.z, 1e-5f);

            var first = Rig(Controller(EmptyState, emptyWriteDefaults: false));
            first.Update(0f);
            first.Update(0.05f);
            Near.Equal(0f, first.deltaPosition.magnitude, 1e-6f);
        }

        /// <summary>The kept step is the layer's last one also when root motion was off while the
        /// motion played: turned on during the hold, the root moves by Walk's last step.</summary>
        [Fact]
        public void AHoldKeepsTheLastStepWhileRootMotionIsOff()
        {
            var animator = Rig(Controller(WalkState, EmptyState, 0.2f, 0f, emptyWriteDefaults: false));
            animator.applyRootMotion = false;
            animator.Update(0f);
            for (int step = 0; step < 12 && !animator.GetCurrentAnimatorStateInfo(0).IsName("Empty"); step++)
                animator.Update(0.05f);
            Assert.True(animator.GetCurrentAnimatorStateInfo(0).IsName("Empty"));
            animator.applyRootMotion = true;
            animator.Update(0.05f);
            Near.Equal(0.05f, animator.deltaPosition.x, 1e-5f);
        }

        /// <summary>A state driven by a motion time parameter takes the parameter in the update Play
        /// enters it in (control for the hold above, which applies to states that advance).</summary>
        [Fact]
        public void AMotionTimeStateTakesItsParameterAtOnce()
        {
            var yaml = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!91 &9100000\nAnimatorController:\n  m_Name: Timed\n"
                + "  serializedVersion: 5\n  m_AnimatorParameters:\n  - m_Name: Aim\n    m_Type: 1\n    m_DefaultFloat: 0.7\n"
                + "    m_DefaultInt: 0\n    m_DefaultBool: 0\n    m_Controller: {fileID: 9100000}\n  m_AnimatorLayers:\n"
                + Layer("Base Layer", 1107000001, 0) + Machine(1107000001, WalkState, WalkState, RunState)
                + State(WalkState, "Walk", Walk) + State(RunState, "Run", Run, false, "Aim")
                + Clip(Walk, "Walk", 1f) + Clip(Run, "Run", 2f);
            var animator = Rig(Load(yaml));
            animator.Update(0f);
            animator.Update(0.1f);
            animator.Play("Run");
            animator.Update(0.1f);
            AssertState(animator, "Run", 0.7f);
        }

        /// <summary>Other releases keep advancing the state Play enters (not measured there).</summary>
        [Fact]
        public void OtherReleasesAdvanceTheStatePlayEnters()
        {
            ProjectInfo.UnityVersion = "2022.3.0f1";
            var animator = Rig(Controller(WalkState));
            animator.Update(0f);
            animator.Play("Run");
            animator.Update(0.1f);
            AssertState(animator, "Run", 0.1f);
        }

        /// <summary>A cross-fade's destination stays at its offset in the update it starts while the
        /// transition advances; the clips weigh by the transition's progress and the destination's
        /// motion is a whole update's (Walk 0.8·0.1 + Run 0.2·0.2).</summary>
        [Fact]
        public void CrossFadeHoldsTheDestinationWhileItsTransitionAdvances()
        {
            var animator = Rig(Controller(WalkState));
            animator.Update(0f);
            animator.Update(0.2f);
            animator.CrossFadeInFixedTime("Run", 0.5f);
            animator.Update(0.1f);
            Assert.True(animator.IsInTransition(0));
            AssertState(animator, "Walk", 0.3f);
            Near.Equal(0f, animator.GetNextAnimatorStateInfo(0).normalizedTime);
            Near.Equal(0.2f, animator.GetAnimatorTransitionInfo(0).normalizedTime);
            Near.Equal(0.8f, animator.GetCurrentAnimatorClipInfo(0)[0].weight);
            Near.Equal(0.2f, animator.GetNextAnimatorClipInfo(0)[0].weight);
            Near.Equal(0.12f, animator.deltaPosition.x, 1e-5f);
            animator.Update(0.1f);
            Near.Equal(0.1f, animator.GetNextAnimatorStateInfo(0).normalizedTime, 1e-5f);
        }

        /// <summary>An exit time of 0 is not crossed where the state starts: it is first reached at
        /// the end of the first loop. Control: an exit time inside the first loop.</summary>
        [Theory]
        [InlineData(0f, 0.5f, "Walk")]
        [InlineData(0.5f, 0.5f, "Run")]
        public void AStateDoesNotCrossTheExitTimeItStartsAt(float exitTime, float firstStep, string afterFirstStep)
        {
            var animator = Rig(Controller(WalkState, RunState, exitTime, 0f));
            animator.Update(0f);
            AssertState(animator, "Walk", 0f);
            animator.Update(firstStep);
            Assert.True(animator.GetCurrentAnimatorStateInfo(0).IsName(afterFirstStep));
            if (afterFirstStep == "Run") return;
            // Crossing 1 by 0.1: an instant transition leaves Run 0.1 in, its motion a whole
            // update's (Run from 0.5 to 1.1 cycles: 1.2).
            animator.Update(0.6f);
            AssertState(animator, "Run", 0.1f);
            Near.Equal(1.2f, animator.deltaPosition.x, 1e-4f);
        }

        /// <summary>In a base layer's transition from a state without a motion (write defaults on),
        /// the destination's clips are all the layer plays: clip infos at weight 1 and its root motion
        /// in full from the transition's start (a whole update's at the start: Walk from 0.5 to 1).</summary>
        [Fact]
        public void ClipsBesideAnEmptyStateTakeTheWholeWeight()
        {
            var animator = Rig(Controller(EmptyState, WalkState, 0.5f, 0.5f));
            animator.Update(0f);
            animator.Update(0.5f);
            Assert.True(animator.IsInTransition(0));
            Near.Equal(0f, animator.GetAnimatorTransitionInfo(0).normalizedTime);
            Assert.Empty(animator.GetCurrentAnimatorClipInfo(0));
            Near.Equal(1f, animator.GetNextAnimatorClipInfo(0)[0].weight);
            Near.Equal(0.5f, animator.deltaPosition.x, 1e-5f);
            animator.Update(0.1f);
            Near.Equal(0.2f, animator.GetAnimatorTransitionInfo(0).normalizedTime);
            Near.Equal(1f, animator.GetNextAnimatorClipInfo(0)[0].weight);
            Near.Equal(0.1f, animator.deltaPosition.x, 1e-5f);
        }

        public sealed class EventReceiver : MonoBehaviour
        {
            public readonly List<float> Times = new List<float>();
            void OnMark(AnimationEvent e) => Times.Add(e.animatorStateInfo.normalizedTime);
        }

        /// <summary>A mirrored state plays a looping humanoid clip half a cycle later, its events as
        /// well: an event at 0.8 fires as the state passes 0.3. Control: the same state unmirrored.</summary>
        [Theory]
        [InlineData(true, 0.3f)]
        [InlineData(false, 0.8f)]
        public void AMirroredStateFiresItsHumanoidClipsEventsHalfACycleLater(bool mirror, float stateTime)
        {
            var yaml = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!91 &9100000\nAnimatorController:\n  m_Name: Events\n"
                + "  serializedVersion: 5\n  m_AnimatorParameters: []\n  m_AnimatorLayers:\n" + Layer("Base Layer", 1107000001, 0)
                + Machine(1107000001, WalkState, WalkState) + State(WalkState, "Walk", Walk, mirror) + Clip(Walk, "Walk", 1f, human: true);
            var animator = Rig(Load(yaml));
            var clip = animator.runtimeAnimatorController.animationClips[0];
            Assert.True(clip.HasHumanCurves);
            clip.AddEvent(new AnimationEvent { time = 0.8f, functionName = "OnMark" });
            var receiver = animator.gameObject.AddComponent<EventReceiver>();
            animator.Update(0f);
            for (int i = 0; i < 10; i++) animator.Update(0.1f);
            Assert.Single(receiver.Times);
            Near.Equal(stateTime, receiver.Times[0], 1e-5f);
        }

        /// <summary>A mirrored state of a mirrored humanoid clip on an additive layer plays as the
        /// Editor plays it, which logs nothing (tests/projects/humanoid additiveMirrorClip.log).</summary>
        [Fact]
        public void AMirroredClipOnAMirroredAdditiveStateLogsNothing()
        {
            var yaml = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!91 &9100000\nAnimatorController:\n  m_Name: Additive\n"
                + "  serializedVersion: 5\n  m_AnimatorParameters: []\n  m_AnimatorLayers:\n"
                + Layer("Base Layer", 1107000001, 0) + Layer("Additive", 1107000002, 1)
                + Machine(1107000001, WalkState, WalkState) + Machine(1107000002, RunState, RunState)
                + State(WalkState, "Walk", Walk) + State(RunState, "Run", Run, mirror: true)
                + Clip(Walk, "Walk", 1f, human: true) + Clip(Run, "Run", 2f, human: true, mirror: true);
            var animator = Rig(Load(yaml));
            m_Logs.Clear();
            animator.Update(0f);
            animator.Update(0.1f);
            Assert.True(m_Logs.Count == 0, string.Join("\n", m_Logs));
        }
    }
}
