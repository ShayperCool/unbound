using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using Unbound.Runtime.Humanoid;
using Unbound.Tests;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Xunit;
using Fixture = UnityEngine.Tests.HumanControllerGoalTests;

namespace UnityEngine.Tests
{
    /// <summary>
    /// An Animator Controller played through an AnimatorControllerPlayable poses and moves a
    /// humanoid Animator as the same controller on the Animator does (6000.0.71f1,
    /// tests/projects/humanoid controllerGraph.*: a controller as the source of the Animator's
    /// only output records the rows of the controller on the Animator itself; the Scripting API's
    /// AnimatorControllerPlayable page calls it a playable that controls a
    /// RuntimeAnimatorController and promises nothing about its pose). The Editor's values are in
    /// the probe; these tests keep the rule on another rig and other clips.
    /// </summary>
    [Collection("Animation")]
    public sealed class HumanControllerPlayableTests : IDisposable
    {
        readonly List<Object> m_Created = new List<Object>();
        readonly List<PlayableGraph> m_Graphs = new List<PlayableGraph>();
        readonly string m_Version = ProjectInfo.UnityVersion;

        public HumanControllerPlayableTests() => ProjectInfo.UnityVersion = "6000.0.71f1";

        public void Dispose()
        {
            foreach (var graph in m_Graphs)
                if (graph.IsValid()) graph.Destroy();
            foreach (var o in m_Created)
                if (o != null) Object.DestroyImmediate(o);
            ProjectInfo.UnityVersion = m_Version;
        }

        static readonly float[] s_Steps = { 0f, 0.25f, 0.2f };

        static (string, float)[] Moving((string, float)[] clip, string axis) =>
            clip.Select(c => c.Item1 == "RootT." + axis ? (c.Item1 + "+", c.Item2) : c).ToArray();

        /// <summary>A leaving for B after an exit time of 0.2 over 0.5 s, the clips moving along z and x.</summary>
        static string MovingTransition(bool footIK) =>
            Fixture.Header + Fixture.Layer("Base Layer", 1107000001, 0, 1f) + Fixture.Machine(1107000001, Fixture.StateA, Fixture.StateA, Fixture.StateB)
            + Fixture.State(Fixture.StateA, "A", Fixture.ClipA, footIK, 1101000001) + Fixture.State(Fixture.StateB, "B", Fixture.ClipB, false)
            + Fixture.Transition(1101000001, Fixture.StateB, 0.2f, 0.5f)
            + Fixture.Clip(Fixture.ClipA, "A", Moving(Fixture.s_A, "z")) + Fixture.Clip(Fixture.ClipB, "B", Moving(Fixture.s_B, "x"));

        static string Controller(string kind) => kind switch
        {
            "transition" => MovingTransition(false),
            "footIK" => MovingTransition(true),
            "tree" => Fixture.TreeController(false, null),
            "direct" => Fixture.TreeController(true, null, 0.3f, 0.4f),
            "override" => Fixture.LayerController(0, 0.5f),
            "additive" => Fixture.LayerController(1, 0.5f),
            _ => throw new ArgumentException(kind),
        };

        /// <summary>The controller on the Animator, stepped by <see cref="s_Steps"/>.</summary>
        Animator Own(string yaml)
        {
            var animator = Fixture.Rig(yaml, m_Created);
            foreach (float step in s_Steps) animator.Update(step);
            return animator;
        }

        /// <summary>The controller as the source of the Animator's only output, evaluated by the
        /// same steps.</summary>
        Animator Played(string yaml, Action<Animator> before = null)
        {
            var own = Fixture.Rig(yaml, m_Created);
            var controller = own.runtimeAnimatorController;
            own.runtimeAnimatorController = null;
            before?.Invoke(own);
            var graph = PlayableGraph.Create("controller");
            m_Graphs.Add(graph);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            AnimationPlayableOutput.Create(graph, "animation", own).SetSourcePlayable(AnimatorControllerPlayable.Create(graph, controller));
            foreach (float step in s_Steps) graph.Evaluate(step);
            return own;
        }

        /// <summary>The body and muscle curves the two poses hold. The goal curves are left out:
        /// an output's pose holds them relative to the body, a controller's own placed by it,
        /// and the bones show what both solve to.</summary>
        static void SameCurves(Animator expected, Animator actual)
        {
            for (int curve = HumanClipImport.RootFirst; curve < HumanClipImport.LeftHandFirst; curve++)
                if (curve < HumanClipImport.GoalFirst || curve >= HumanClipImport.MuscleFirst)
                    Near.Equal(expected.Human.Value(curve), actual.Human.Value(curve), 1e-6f);
        }

        static void SamePose(Animator expected, Animator actual)
        {
            SameCurves(expected, actual);
            var a = expected.GetComponentsInChildren<Transform>();
            var b = actual.GetComponentsInChildren<Transform>();
            Assert.Equal(a.Length, b.Length);
            for (int i = 0; i < a.Length; i++)
            {
                Assert.True((a[i].localPosition - b[i].localPosition).magnitude < 1e-5f, a[i].name);
                Assert.True(Quaternion.Angle(a[i].localRotation, b[i].localRotation) < 0.01f, a[i].name);
            }
            Assert.True((expected.deltaPosition - actual.deltaPosition).magnitude < 1e-6f);
            Assert.True(Quaternion.Angle(expected.deltaRotation, actual.deltaRotation) < 0.01f);
            Assert.True((expected.pivotPosition - actual.pivotPosition).magnitude < 1e-5f);
            Near.Equal(expected.pivotWeight, actual.pivotWeight, 1e-6f);
        }

        /// <summary>The playable's controller gives the pose, the root's step and the feet pivot
        /// of the Animator's own: in a transition (also with a state's Foot IK), a blend tree, a
        /// Direct blend tree below one and under an override and an additive layer.</summary>
        [Theory]
        [InlineData("transition")]
        [InlineData("footIK")]
        [InlineData("tree")]
        [InlineData("direct")]
        [InlineData("override")]
        [InlineData("additive")]
        public void AControllerPlayablePosesAndMovesAHumanoidAsItsOwnController(string kind)
        {
            string yaml = Controller(kind);
            var own = Own(yaml);
            var played = Played(yaml);
            SamePose(own, played);
        }

        /// <summary>Controls of the comparison: the clips move the root and pose the muscles, a
        /// state's Foot IK changes the pose, and the root moves by the blend of the two clips'
        /// steps (both axes) while the transition runs.</summary>
        [Fact]
        public void TheComparedControllersMoveTheRootAndTheFeet()
        {
            var plain = Played(MovingTransition(false));
            Assert.True(plain.deltaPosition.z > 0.02f && plain.deltaPosition.x > 0.02f, plain.deltaPosition.ToString());
            Assert.True(plain.transform.position.magnitude > 0.1f);
            var footIK = Played(MovingTransition(true));
            var a = plain.GetComponentsInChildren<Transform>();
            var b = footIK.GetComponentsInChildren<Transform>();
            Assert.Contains(Enumerable.Range(0, a.Length), i => Quaternion.Angle(a[i].localRotation, b[i].localRotation) > 0.5f);
        }

        /// <summary>The playable asks for an IK pass when its controller's base layer does (the
        /// probe's controllers have one, their goals are read in it); layers above the first are
        /// unmeasured and get none. Control: without the setting there is no pass.</summary>
        [Theory]
        [InlineData(true)]
        [InlineData(false)]
        public void AControllerPlayableAsksForItsBaseLayersIKPass(bool pass)
        {
            string yaml = MovingTransition(false);
            if (pass) yaml = yaml.Replace("m_IKPass: 0", "m_IKPass: 1");
            Assert.Equal(pass, yaml.Contains("m_IKPass: 1"));
            var played = Played(yaml);
            Assert.Equal(pass, played.Human.HasIKPass(0));
        }

        /// <summary>Without applyRootMotion the playable's controller poses the body and leaves
        /// the root where it is, as the Animator's own does.</summary>
        [Fact]
        public void AControllerPlayableLeavesTheRootWithoutRootMotion()
        {
            string yaml = MovingTransition(false);
            var played = Played(yaml, animator => animator.applyRootMotion = false);
            Assert.Equal(Vector3.zero, played.transform.position);
            Assert.Equal(Vector3.zero, played.deltaPosition);
            var own = Fixture.Rig(yaml, m_Created);
            own.applyRootMotion = false;
            foreach (float step in s_Steps) own.Update(step);
            SameCurves(own, played);
        }
    }
}
