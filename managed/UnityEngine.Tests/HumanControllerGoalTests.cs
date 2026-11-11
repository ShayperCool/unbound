using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Unbound.Runtime;
using Unbound.Runtime.Humanoid;
using Unbound.Runtime.Playables;
using Unbound.Tests;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// The IK goals an Animator Controller blends on a humanoid (6000.0.71f1, tests/projects/humanoid
    /// goals.*, footIKTransition; the Manual and the Scripting API only say that a state's Foot IK
    /// is respected and that additive layers add to the layers below): every clip's goals are
    /// placed by that clip's own body before they blend, an additive layer leaves the goals alone,
    /// and a state's Foot IK applies while its clip blends with others. The Editor's values are in
    /// the probe; these tests keep the rule on another rig and other clips.
    /// </summary>
    [Collection("Animation")]
    public sealed class HumanControllerGoalTests : IDisposable
    {
        readonly List<Object> m_Created = new List<Object>();
        readonly List<string> m_Logs = new List<string>();
        readonly ILogger m_Logger = Debug.unityLogger;
        readonly ILogHandler m_PreviousHandler;
        readonly string m_Version = ProjectInfo.UnityVersion;

        public HumanControllerGoalTests()
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

            public void LogException(Exception exception, Object context)
            {
                m_Logs.Add("Exception: " + exception);
                m_Inner.LogException(exception, context);
            }
        }

        // ------------------------------------------------------------------ fixture

        const long ClipA = 7400001, ClipB = 7400002, ClipNoGoals = 7400003;
        const long StateA = 1102000001, StateB = 1102000002;
        const int LeftFoot = HumanClipImport.GoalFirst, LeftHand = HumanClipImport.GoalFirst + 2 * HumanStateGoals.Width;

        static string F(float value) => value.ToString("R", CultureInfo.InvariantCulture);

        // A stands upright; B and NoGoals lean forward 30 degrees with the body higher, so a goal
        // placed by B's body lands elsewhere than the same goal under A's.
        static readonly float s_LeanX = MathF.Sin(15f * MathF.PI / 180f), s_LeanW = MathF.Cos(15f * MathF.PI / 180f);

        static (string, float)[] Body(float height, bool lean) => new[]
        {
            ("RootT.x", 0f), ("RootT.y", height), ("RootT.z", 0f),
            ("RootQ.x", lean ? s_LeanX : 0f), ("RootQ.y", 0f), ("RootQ.z", 0f), ("RootQ.w", lean ? s_LeanW : 1f),
        };

        static (string, float)[] Goal(string name, float x, float y, float z) => new[]
        {
            (name + "T.x", x), (name + "T.y", y), (name + "T.z", z),
            (name + "Q.x", 0f), (name + "Q.y", 0f), (name + "Q.z", 0f), (name + "Q.w", 1f),
        };

        static readonly (string, float)[] s_A = Body(1f, false).Concat(Goal("LeftFoot", -0.1f, -0.9f, 0.05f)).Concat(Goal("RightFoot", 0.1f, -0.9f, 0f))
            .Concat(Goal("LeftHand", -0.4f, 0.3f, 0.1f)).Concat(Goal("RightHand", 0.4f, 0.3f, 0.1f)).ToArray();
        static readonly (string, float)[] s_B = Body(1.3f, true).Concat(Goal("LeftFoot", -0.12f, -1f, 0.2f)).Concat(Goal("RightFoot", 0.12f, -1f, -0.1f))
            .Concat(Goal("LeftHand", -0.2f, 0.5f, 0.3f)).Concat(Goal("RightHand", 0.2f, 0.5f, 0.3f)).ToArray();

        /// <summary>A looping humanoid clip of 1 s holding each curve at one value; a curve named
        /// with a trailing '+' rises by one over the second.</summary>
        static string Clip(long id, string name, (string Attribute, float Value)[] curves) => $$"""
--- !u!74 &{{id}}
AnimationClip:
  m_Name: {{name}}
  m_Legacy: 0
  m_FloatCurves:
{{string.Join("", curves.Select(c => $$"""
  - curve:
      m_Curve:
      - serializedVersion: 3
        time: 0
        value: {{F(c.Value)}}
        inSlope: 0
        outSlope: 0
        tangentMode: 0
      - serializedVersion: 3
        time: 1
        value: {{F(c.Value + (c.Attribute.EndsWith("+") ? 1f : 0f))}}
        inSlope: 0
        outSlope: 0
        tangentMode: 0
      m_PreInfinity: 2
      m_PostInfinity: 2
    attribute: {{c.Attribute.TrimEnd('+')}}
    path:
    classID: 95
    script: {fileID: 0}

"""))}}  m_SampleRate: 60
  m_AnimationClipSettings:
    m_StartTime: 0
    m_StopTime: 1
    m_LoopTime: 1
  m_Events: []

""";

        static string State(long id, string name, long motion, bool footIK, long transition = 0) => $$"""
--- !u!1102 &{{id}}
AnimatorState:
  serializedVersion: 6
  m_Name: {{name}}
  m_Speed: 1
  m_CycleOffset: 0
  m_Transitions:{{(transition == 0 ? " []" : "\n  - {fileID: " + transition + "}")}}
  m_StateMachineBehaviours: []
  m_IKOnFeet: {{(footIK ? 1 : 0)}}
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
{{string.Join("\n", states.Select(s => "  - serializedVersion: 1\n    m_State: {fileID: " + s + "}"))}}
  m_ChildStateMachines: []
  m_AnyStateTransitions: []
  m_EntryTransitions: []
  m_StateMachineTransitions: {}
  m_StateMachineBehaviours: []
  m_DefaultState: {fileID: {{defaultState}}}

""";

        static string Layer(string name, long machine, int blending, float weight) => $$"""
  - serializedVersion: 5
    m_Name: {{name}}
    m_StateMachine: {fileID: {{machine}}}
    m_Mask: {fileID: 0}
    m_Motions: []
    m_Behaviours: []
    m_BlendingMode: {{blending}}
    m_SyncedLayerIndex: -1
    m_DefaultWeight: {{F(weight)}}
    m_IKPass: 0
    m_SyncedLayerAffectsTiming: 0
    m_Controller: {fileID: 9100000}

""";

        const string Header = "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!91 &9100000\nAnimatorController:\n  m_Name: Goals\n"
            + "  serializedVersion: 5\n  m_AnimatorParameters: []\n  m_AnimatorLayers:\n";

        /// <summary>A, then <paramref name="second"/> after an exit time of 0.2 over 0.5 s.</summary>
        static string TransitionController((string, float)[] second, bool footA, bool footB) =>
            Header + Layer("Base Layer", 1107000001, 0, 1f) + Machine(1107000001, StateA, StateA, StateB)
            + State(StateA, "A", ClipA, footA, 1101000001) + State(StateB, "B", ClipB, footB) + Transition(1101000001, StateB, 0.2f, 0.5f)
            + Clip(ClipA, "A", s_A) + Clip(ClipB, "B", second);

        /// <summary>A with B on a second layer (blending mode 0 override, 1 additive).</summary>
        static string LayerController(int blending, float weight, bool footA = false, bool footB = false) =>
            Header + Layer("Base Layer", 1107000001, 0, 1f) + Layer("Above", 1107000002, blending, weight)
            + Machine(1107000001, StateA, StateA) + Machine(1107000002, StateB, StateB)
            + State(StateA, "A", ClipA, footA) + State(StateB, "B", ClipB, footB) + Clip(ClipA, "A", s_A) + Clip(ClipB, "B", s_B);

        const long ClipC = 7400003, Tree = 20600001, StateTree = 1102000003;

        static string Parameter(string name, float value) => $$"""
  - m_Name: {{name}}
    m_Type: 1
    m_DefaultFloat: {{F(value)}}
    m_DefaultInt: 0
    m_DefaultBool: 0
    m_Controller: {fileID: 9100000}

""";

        /// <summary>A blend tree of B and C: 1D on Blend (thresholds 0 and 1), or Direct on WB and WC.</summary>
        static string BlendTree(bool direct) => $$"""
--- !u!206 &{{Tree}}
BlendTree:
  m_Name: Tree
  m_Childs:
  - serializedVersion: 2
    m_Motion: {fileID: {{ClipB}}}
    m_Threshold: 0
    m_Position: {x: 0, y: 0}
    m_TimeScale: 1
    m_CycleOffset: 0
    m_DirectBlendParameter: WB
    m_Mirror: 0
  - serializedVersion: 2
    m_Motion: {fileID: {{ClipC}}}
    m_Threshold: 1
    m_Position: {x: 0, y: 0}
    m_TimeScale: 1
    m_CycleOffset: 0
    m_DirectBlendParameter: WC
    m_Mirror: 0
  m_BlendParameter: Blend
  m_BlendParameterY: Blend
  m_MinThreshold: 0
  m_MaxThreshold: 1
  m_UseAutomaticThresholds: 0
  m_NormalizedBlendValues: 0
  m_BlendType: {{(direct ? 4 : 0)}}

""";

        // The tree's clips: A's body, the left foot goal turned 80 degrees about y either way, so
        // their blend at equal weights is the unturned goal, a sum well short of a unit rotation.
        static (string, float)[] Turned(float degrees)
        {
            float half = degrees * MathF.PI / 360f;
            var curves = s_A.ToArray();
            for (int i = 0; i < curves.Length; i++)
            {
                if (curves[i].Item1 == "LeftFootQ.y") curves[i].Item2 = MathF.Sin(half);
                if (curves[i].Item1 == "LeftFootQ.w") curves[i].Item2 = MathF.Cos(half);
            }
            return curves;
        }

        /// <summary>The tree of B and C alone (<paramref name="from"/> null), or after
        /// <paramref name="from"/> on state A, left after an exit time of 0.2 over 0.5 s.</summary>
        static string TreeController(bool direct, (string, float)[] from, float wb = 0.5f, float wc = 0.5f, (string, float)[] b = null) =>
            "%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!91 &9100000\nAnimatorController:\n  m_Name: Goals\n  serializedVersion: 5\n"
            + "  m_AnimatorParameters:\n" + Parameter("Blend", 0.5f) + Parameter("WB", wb) + Parameter("WC", wc) + "  m_AnimatorLayers:\n"
            + Layer("Base Layer", 1107000001, 0, 1f)
            + (from != null
                ? Machine(1107000001, StateA, StateA, StateTree) + State(StateA, "A", ClipA, false, 1101000001)
                    + Transition(1101000001, StateTree, 0.2f, 0.5f) + Clip(ClipA, "A", from)
                : Machine(1107000001, StateTree, StateTree))
            + State(StateTree, "Tree", Tree, false) + BlendTree(direct) + Clip(ClipB, "B", b ?? Turned(80f)) + Clip(ClipC, "C", Turned(-80f));

        Animator Rig(string yaml)
        {
            var file = ObjectGraphBuilder.Build(UnityYaml.Parse(yaml), "goals.asset", null, ObjectGraphBuilder.Kind.Asset, null);
            foreach (var o in file.Objects.Values) m_Created.Add(o);
            var root = AnimationHumanStreamTests.Humanoid(out var animator);
            m_Created.Add(root);
            m_Created.Add(animator.avatar);
            animator.applyRootMotion = true;
            animator.runtimeAnimatorController = (RuntimeAnimatorController)file.Objects[9100000];
            return animator;
        }

        static AnimationClip ClipOf(Animator animator, string name) => animator.runtimeAnimatorController.animationClips.First(c => c.name == name);

        /// <summary>A goal of a clip as the clip alone gives it: placed by the clip's body relative
        /// to its motion (the fixture's curves are constant, so any time will do).</summary>
        static Trs Placed(Animator animator, AnimationClip clip, (string Attribute, float Value)[] curves, string goal)
        {
            animator.Human.SampleMotion(clip, clip.HumanClipMotion, new HumanClipMotionInput { Time = 0.5f, PreviousTime = 0.5f, Speed = 1f }, out var sample);
            float V(string attribute, float missing)
            {
                foreach (var (a, v) in curves) if (a == goal + attribute) return v;
                return missing;
            }
            var local = new Trs(new F4(V("T.x", 0f), V("T.y", 0f), V("T.z", 0f), 0f),
                M.QNormalize(new F4(V("Q.x", 0f), V("Q.y", 0f), V("Q.z", 0f), V("Q.w", 1f))), F4.One3);
            return M.Mul(sample.Body, local);
        }

        /// <summary>The position the Animator's pose holds for a goal (its first curve), in the
        /// motion's frame.</summary>
        static F4 Written(Animator animator, int first) =>
            new F4(animator.Human.Value(first), animator.Human.Value(first + 1), animator.Human.Value(first + 2), 0f);

        /// <summary>The rotation the Animator's pose holds for a goal (its first curve).</summary>
        static F4 WrittenRotation(Animator animator, int first) => M.QNormalize(new F4(animator.Human.Value(first + 3),
            animator.Human.Value(first + 4), animator.Human.Value(first + 5), animator.Human.Value(first + 6)));

        /// <summary>The angle between two rotations, in degrees.</summary>
        static float Angle(F4 a, F4 b)
        {
            float dot = MathF.Abs(a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w);
            return 2f * MathF.Acos(MathF.Min(1f, dot)) * 180f / MathF.PI;
        }

        static F4 Lerp(F4 a, F4 b, float t) => a * (1f - t) + b * t;

        static void AssertNear(F4 expected, F4 actual, float tolerance = 2e-5f)
        {
            Near.Equal(expected.x, actual.x, tolerance);
            Near.Equal(expected.y, actual.y, tolerance);
            Near.Equal(expected.z, actual.z, tolerance);
        }

        static float Distance(F4 a, F4 b) => MathF.Sqrt((a.x - b.x) * (a.x - b.x) + (a.y - b.y) * (a.y - b.y) + (a.z - b.z) * (a.z - b.z));

        /// <summary>Steps into the fixture's transition and returns its progress.</summary>
        static float IntoTransition(Animator animator)
        {
            animator.Update(0f);
            animator.Update(0.25f);
            animator.Update(0.2f);
            Assert.True(animator.IsInTransition(0));
            float progress = animator.GetAnimatorTransitionInfo(0).normalizedTime;
            Assert.InRange(progress, 0.2f, 0.8f);
            return progress;
        }

        // ------------------------------------------------------------------ tests

        /// <summary>The states of a transition blend goals that each clip's own body placed, feet
        /// and hands alike. Control: the blend of the goals relative to the bodies under the
        /// blended body lands elsewhere, so the fixture tells the two rules apart.</summary>
        [Fact]
        public void ATransitionBlendsGoalsPlacedByEachClipsBody()
        {
            var animator = Rig(TransitionController(s_B, false, false));
            float p = IntoTransition(animator);
            var a = ClipOf(animator, "A");
            var b = ClipOf(animator, "B");
            foreach (var (name, first) in new[] { ("LeftFoot", LeftFoot), ("LeftHand", LeftHand) })
            {
                var placedA = Placed(animator, a, s_A, name);
                var placedB = Placed(animator, b, s_B, name);
                AssertNear(Lerp(placedA.t, placedB.t, p), Written(animator, first));
            }
            // The other rule: the goals blended relative to their bodies, then placed by the blended body.
            animator.Human.SampleMotion(a, a.HumanClipMotion, new HumanClipMotionInput { Time = 0.5f, PreviousTime = 0.5f, Speed = 1f }, out var sampleA);
            animator.Human.SampleMotion(b, b.HumanClipMotion, new HumanClipMotionInput { Time = 0.5f, PreviousTime = 0.5f, Speed = 1f }, out var sampleB);
            var body = HumanRootMotion.Blend(new[] { sampleA, sampleB }, new[] { 1f - p, p }).Body;
            var relative = new Trs(Lerp(new F4(-0.1f, -0.9f, 0.05f, 0f), new F4(-0.12f, -1f, 0.2f, 0f), p), F4.QIdentity, F4.One3);
            Assert.True(Distance(M.Mul(body, relative).t, Written(animator, LeftFoot)) > 0.01f);
        }

        /// <summary>A clip without goal curves gives the identity goals, which its body places: in
        /// a blend its goals are at its body.</summary>
        [Fact]
        public void AClipWithoutGoalCurvesBlendsItsBodyAsItsGoals()
        {
            var noGoals = Body(1.3f, true);
            var animator = Rig(TransitionController(noGoals, false, false));
            float p = IntoTransition(animator);
            var placedA = Placed(animator, ClipOf(animator, "A"), s_A, "LeftFoot");
            var bodyB = Placed(animator, ClipOf(animator, "B"), noGoals, "LeftFoot");
            Assert.True(bodyB.t.y > 0.1f);
            AssertNear(Lerp(placedA.t, bodyB.t, p), Written(animator, LeftFoot));
        }

        /// <summary>An additive layer leaves the goals of the layers below as they are, whatever
        /// its clip's goal curves hold. Control: the same clip on an override layer moves them.</summary>
        [Theory]
        [InlineData(1, false)]
        [InlineData(0, true)]
        public void AnAdditiveLayerLeavesTheGoalsAlone(int blending, bool moved)
        {
            var animator = Rig(LayerController(blending, 0.6f));
            animator.Update(0f);
            animator.Update(0.1f);
            var a = ClipOf(animator, "A");
            foreach (var (name, first) in new[] { ("LeftFoot", LeftFoot), ("LeftHand", LeftHand) })
            {
                var alone = Placed(animator, a, s_A, name).t;
                if (!moved)
                {
                    AssertNear(alone, Written(animator, first));
                    continue;
                }
                var above = Placed(animator, ClipOf(animator, "B"), s_B, name).t;
                AssertNear(Lerp(alone, above, 0.6f), Written(animator, first));
                Assert.True(Distance(alone, Written(animator, first)) > 0.01f);
            }
        }

        /// <summary>Manual 6000.0, Animation States: "Enable to respect Foot IK for this state". A
        /// state with it asks for Foot IK while a transition blends its clip with another's, on
        /// either side, and on a layer, without a diagnostic. Controls: no state with it asks for
        /// none, and the legs then end elsewhere.</summary>
        [Theory]
        [InlineData(true, false, true)]
        [InlineData(false, true, true)]
        [InlineData(false, false, false)]
        public void FootIKAppliesWhileStatesBlend(bool footA, bool footB, bool expected)
        {
            var animator = Rig(TransitionController(s_B, footA, footB));
            m_Logs.Clear();
            IntoTransition(animator);
            Assert.Equal(expected, animator.m_Player.FootIK());
            Assert.True(m_Logs.Count == 0, string.Join("\n", m_Logs));

            var layered = Rig(LayerController(0, 0.6f, footA, footB));
            layered.Update(0f);
            layered.Update(0.1f);
            Assert.Equal(expected, layered.m_Player.FootIK());
            Assert.True(m_Logs.Count == 0, string.Join("\n", m_Logs));
        }

        [Fact]
        public void FootIKMovesTheLegsOntoTheBlendedGoals()
        {
            Quaternion Knee(bool foot)
            {
                var animator = Rig(TransitionController(s_B, foot, foot));
                IntoTransition(animator);
                return animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg).localRotation;
            }
            Assert.True(Quaternion.Angle(Knee(true), Knee(false)) > 1f);
        }

        /// <summary>A blend tree's clips blend their goals among themselves before a transition
        /// blends the tree with another state: the tree's goal rotation is a unit rotation there,
        /// whatever its clips' sum is short of one. Control: all three clips blended at once by
        /// their final weights give another rotation, so the fixture tells the two apart.</summary>
        [Fact]
        public void ABlendTreesClipsBlendBeforeATransitionBlendsItsStates()
        {
            // A's left foot goal pitched 60 degrees; the tree's blend is the unturned goal.
            var from = s_A.ToArray();
            float half = 30f * MathF.PI / 180f;
            for (int i = 0; i < from.Length; i++)
            {
                if (from[i].Item1 == "LeftFootQ.x") from[i].Item2 = MathF.Sin(half);
                if (from[i].Item1 == "LeftFootQ.w") from[i].Item2 = MathF.Cos(half);
            }
            var animator = Rig(TreeController(false, from));
            float p = IntoTransition(animator);
            var pitched = new F4(MathF.Sin(half), 0f, 0f, MathF.Cos(half));
            var states = M.QNormalize(pitched * (1f - p) + F4.QIdentity * p);
            float treeSum = MathF.Cos(40f * MathF.PI / 180f);
            var atOnce = M.QNormalize(pitched * (1f - p) + F4.QIdentity * (p * treeSum));
            Assert.True(Angle(states, atOnce) > 1f);
            Assert.True(Angle(states, WrittenRotation(animator, LeftFoot)) < 0.01f);
            // The positions are the same in every clip: the blend leaves them.
            AssertNear(Placed(animator, ClipOf(animator, "A"), from, "LeftFoot").t, Written(animator, LeftFoot));
        }

        /// <summary>A Direct blend tree's clips give the state's goals once: with weights that sum
        /// to one, the goals of the 1D tree at the same weights. Control: with other weights the
        /// goal turns towards the heavier clip's.</summary>
        [Fact]
        public void ADirectBlendTreeBlendsItsClipsGoalsAsATreeOfTheSameWeights()
        {
            (F4 Position, F4 Rotation) Goal(string yaml)
            {
                var animator = Rig(yaml);
                animator.Update(0f);
                animator.Update(0.1f);
                return (Written(animator, LeftFoot), WrittenRotation(animator, LeftFoot));
            }
            var tree = Goal(TreeController(false, null));
            var direct = Goal(TreeController(true, null));
            AssertNear(tree.Position, direct.Position);
            Assert.True(Angle(tree.Rotation, direct.Rotation) < 0.01f);
            Assert.True(Angle(F4.QIdentity, direct.Rotation) < 0.01f);
            var uneven = Goal(TreeController(true, null, 0.75f, 0.25f));
            AssertNear(tree.Position, uneven.Position);
            Assert.True(Angle(F4.QIdentity, uneven.Rotation) > 20f);
        }

        /// <summary>A Direct blend tree whose weights sum above one moves the root by each clip's
        /// own weight: a rising clip at weight 1 beside a level one at weight 1 raises it as the
        /// rising clip alone does (unmeasured in the Editor; Unbound's rule, kept as it was). The
        /// height is used because the feet pivot only corrects the step on the ground plane.</summary>
        [Fact]
        public void ADirectBlendTreeAboveOneMovesTheRootByEachClipsWeight()
        {
            var moving = Turned(80f);
            for (int i = 0; i < moving.Length; i++)
                if (moving[i].Item1 == "RootT.y") moving[i].Item1 = "RootT.y+";
            float Step(float wc)
            {
                var animator = Rig(TreeController(true, null, 1f, wc, moving));
                animator.Update(0f);
                animator.Update(0.25f);
                animator.Update(0.25f);
                return animator.deltaPosition.y;
            }
            float alone = Step(0f);
            Assert.True(alone > 0.05f);
            Assert.InRange(Step(1f) / alone, 0.9f, 1.1f);
        }

        /// <summary>A playable output on the same Animator takes the controller's pose on with its
        /// goals relative to the body and writes it: the feet goals end where the controller alone
        /// puts them. Controls: the output's clip does reach the Animator, and the goal curves it
        /// wrote are not the placed ones the controller alone writes.</summary>
        [Fact]
        public void APlayableOutputOverTheControllerKeepsItsGoals()
        {
            (F4 Foot, F4 Curves, float Progress, float Marker) Play(bool withOutput)
            {
                var animator = Rig(TransitionController(s_B, true, true));
                // Root motion of playable outputs beside a playing controller is another matter
                // (unsupported on humanoids, with its own diagnostic): not requested here.
                animator.applyRootMotion = false;
                var extra = new GameObject("Extra");
                extra.transform.SetParent(animator.transform, false);
                var graph = default(PlayableGraph);
                if (withOutput)
                {
                    var clip = new AnimationClip();
                    m_Created.Add(clip);
                    clip.SetCurve("Extra", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Constant(0f, 1f, 3f));
                    graph = PlayableGraph.Create("over-controller");
                    AnimationPlayableOutput.Create(graph, "animation", animator).SetSourcePlayable(AnimationClipPlayable.Create(graph, clip));
                    graph.Play();
                }
                float oldDelta = Time.s_DeltaTime;
                try
                {
                    Time.s_DeltaTime = 0.15f;
                    for (int i = 0; i < 3; i++)
                    {
                        PlayableGraphData.UpdateAll();
                        AnimationTestLoop.Update(fixedStep: false);
                    }
                    Assert.True(animator.IsInTransition(0));
                    return (animator.Human.Output.Goals[0].X.t, Written(animator, LeftFoot), animator.GetAnimatorTransitionInfo(0).normalizedTime,
                        extra.transform.localPosition.x);
                }
                finally
                {
                    Time.s_DeltaTime = oldDelta;
                    if (withOutput) graph.Destroy();
                }
            }
            var alone = Play(false);
            var over = Play(true);
            Near.Equal(0f, alone.Marker, 1e-6f);
            Near.Equal(3f, over.Marker, 1e-6f);
            Near.Equal(alone.Progress, over.Progress, 1e-6f);
            Assert.True(Distance(alone.Curves, over.Curves) > 0.01f);
            AssertNear(alone.Foot, over.Foot, 1e-5f);
        }
    }
}
