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
    /// A humanoid clip is a whole human pose wherever it blends (6000.0.71f1, tests/projects/humanoid
    /// body.*, bodyGraph.*; the Manual and the Scripting API say nothing about curves a clip lacks):
    /// its body is read whole, the identity's components for the rotation curves it has not, and
    /// blends as a position and a rotation; the muscles it has no curve for are at rest; a Direct
    /// blend tree blends its clips as a mixer does, whatever its weights sum to. The Editor's values
    /// are in the probe; these tests keep the rules on another rig and other clips.
    /// </summary>
    [Collection("Animation")]
    public sealed class HumanBodyBlendTests : IDisposable
    {
        readonly List<Object> m_Created = new List<Object>();
        readonly string m_Version = ProjectInfo.UnityVersion;

        public HumanBodyBlendTests() => ProjectInfo.UnityVersion = "6000.0.71f1";

        public void Dispose()
        {
            foreach (var o in m_Created)
                if (o != null) Object.DestroyImmediate(o);
            ProjectInfo.UnityVersion = m_Version;
        }

        const int RootT = HumanClipImport.RootFirst, RootQ = HumanClipImport.RootFirst + 3, LeftFoot = HumanClipImport.GoalFirst;
        static readonly int s_Spine = HumanClipImport.CurveIndex("Spine Front-Back");

        static F4 Q(float degreesX, float degreesY)
        {
            var q = Quaternion.Euler(degreesX, degreesY, 0f);
            return new F4(q.x, q.y, q.z, q.w);
        }

        /// <summary>The fixture's goals under a body at a height with a rotation (some of its four
        /// curves with <paramref name="rotationCurves"/>, e.g. "xw"); no body curve at all without
        /// a height.</summary>
        static (string, float)[] Clip(float? height, F4 rotation, string rotationCurves = "xyzw", params (string, float)[] more)
        {
            var curves = new List<(string, float)>();
            if (height is { } y)
            {
                curves.Add(("RootT.x", 0f));
                curves.Add(("RootT.y", y));
                curves.Add(("RootT.z", 0f));
                foreach (char c in rotationCurves)
                    curves.Add(("RootQ." + c, c == 'x' ? rotation.x : c == 'y' ? rotation.y : c == 'z' ? rotation.z : rotation.w));
            }
            curves.AddRange(Fixture.s_A.Where(c => !c.Item1.StartsWith("Root", StringComparison.Ordinal)));
            curves.AddRange(more);
            return curves.ToArray();
        }

        Animator Play(string yaml, float step = 0.1f)
        {
            var animator = Fixture.Rig(yaml, m_Created);
            animator.Update(0f);
            animator.Update(step);
            return animator;
        }

        static F4 BodyPosition(Animator animator) =>
            new F4(animator.Human.Value(RootT), animator.Human.Value(RootT + 1), animator.Human.Value(RootT + 2), 0f);

        static F4 BodyRotation(Animator animator) => M.QNormalize(new F4(animator.Human.Value(RootQ), animator.Human.Value(RootQ + 1),
            animator.Human.Value(RootQ + 2), animator.Human.Value(RootQ + 3)));

        static F4 Blend(F4 a, F4 b, float t)
        {
            if (a.x * b.x + a.y * b.y + a.z * b.z + a.w * b.w < 0f) b = -b;
            return M.QNormalize(a * (1f - t) + b * t);
        }

        // ------------------------------------------------------------------ the body, read whole

        /// <summary>A clip whose RootQ curves are negated is the same rotation: a blend tree gives
        /// the same body with it, the normalized blend of the two rotations. Control: curve by
        /// curve the two rotations would nearly cancel.</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ANegatedBodyRotationBlendsAsTheSameRotation(bool direct)
        {
            F4 leaning = Q(30f, 0f), turned = Q(0f, 50f);
            F4 Body(F4 first) => BodyRotation(Play(Fixture.TreeController(direct, null, b: Clip(1f, first), c: Clip(1f, turned))));
            var expected = Blend(leaning, turned, 0.5f);
            Assert.True(Fixture.Angle(expected, Body(leaning)) < 0.1f);
            Assert.True(Fixture.Angle(expected, Body(-leaning)) < 0.1f);
            Assert.True(Fixture.Angle(expected, M.QNormalize(-leaning * 0.5f + turned * 0.5f)) > 20f);
        }

        /// <summary>A transition and an override layer blend negated body rotations alike.</summary>
        [Fact]
        public void ANegatedBodyRotationBlendsAcrossStatesAndLayers()
        {
            F4 leaning = Q(30f, 0f), turned = Q(0f, 50f);
            var layered = Play(Fixture.LayerController(0, 0.25f, a: Clip(1f, -leaning), b: Clip(1f, turned)));
            Assert.True(Fixture.Angle(Blend(leaning, turned, 0.25f), BodyRotation(layered)) < 0.1f);

            var animator = Fixture.Rig(Fixture.TransitionController(Clip(1f, turned), false, false), m_Created);
            // The fixture's first state is the upright A: take its transition's progress as it is.
            animator.Update(0f);
            animator.Update(0.25f);
            animator.Update(0.2f);
            Assert.True(animator.IsInTransition(0));
            float p = animator.GetAnimatorTransitionInfo(0).normalizedTime;
            Assert.True(Fixture.Angle(Blend(F4.QIdentity, turned, p), BodyRotation(animator)) < 0.1f);
        }

        /// <summary>A clip with only some of the body's rotation curves has the identity's
        /// components for the others, normalized, on an override layer too: the layer blends the
        /// body below towards that rotation, not curve by curve towards the curves it has.
        /// Control: keeping the lower layer's value for the missing curves gives another rotation.</summary>
        [Theory]
        [InlineData("x")]
        [InlineData("w")]
        [InlineData("yz")]
        public void ABodyRotationWithSomeCurvesTakesTheIdentitysForTheRest(string curves)
        {
            var below = Q(0f, 70f);
            var values = new F4(0.5f, 0.3f, 0.2f, -1f);
            var whole = M.QNormalize(new F4(curves.Contains('x') ? values.x : 0f, curves.Contains('y') ? values.y : 0f,
                curves.Contains('z') ? values.z : 0f, curves.Contains('w') ? values.w : 1f));
            var animator = Play(Fixture.LayerController(0, 0.5f, a: Clip(1f, below), b: Clip(1.4f, values, curves)));
            var expected = Blend(below, whole, 0.5f);
            Assert.True(Fixture.Angle(expected, BodyRotation(animator)) < 0.1f);
            Near.Equal(1.2f, BodyPosition(animator).y, 1e-5f);
            var byCurve = M.QNormalize(new F4(curves.Contains('x') ? (below.x + values.x) / 2f : below.x, curves.Contains('y') ? (below.y + values.y) / 2f : below.y,
                curves.Contains('z') ? (below.z + values.z) / 2f : below.z, curves.Contains('w') ? (below.w + values.w) / 2f : below.w));
            Assert.True(Fixture.Angle(expected, byCurve) > 1f);
        }

        /// <summary>A clip without body curves has the identity body and blends with it: beside a
        /// clip whose body is 1.2 high and rises, the body is at its weight's share of that height
        /// and the root rises by that share. Control: the rising clip alone.</summary>
        [Fact]
        public void AClipWithoutBodyCurvesBlendsWithTheIdentityBody()
        {
            var rising = Clip(1.2f, Q(20f, 0f)).Select(c => c.Item1 == "RootT.y" ? ("RootT.y+", c.Item2) : c).ToArray();
            var none = Clip(null, default);
            var alone = Play(Fixture.TreeController(false, null, b: rising, c: rising), 0.25f);
            var mixed = Play(Fixture.TreeController(false, null, b: rising, c: none), 0.25f);
            Assert.True(alone.deltaPosition.y > 0.05f);
            Near.Equal(alone.deltaPosition.y / 2f, mixed.deltaPosition.y, 1e-4f);
            Near.Equal(BodyPosition(alone).y / 2f, BodyPosition(mixed).y, 1e-4f);
            Assert.True(Fixture.Angle(Blend(Q(20f, 0f), F4.QIdentity, 0.5f), BodyRotation(mixed)) < 0.1f);

            // Its own sample: a humanoid clip's, the identity body, and no hold on the feet pivot.
            HumanRootMotionSample Sample(string name)
            {
                var clip = Fixture.ClipOf(mixed, name);
                mixed.Human.SampleMotion(clip, clip.HumanClipMotion, new HumanClipMotionInput { Time = 0.5f, PreviousTime = 0.25f, Speed = 1f }, out var sample);
                return sample;
            }
            var without = Sample("C");
            Assert.True(without.HasMotion);
            Near.Equal(0f, without.Body.t.y, 1e-6f);
            Near.Equal(0f, without.Delta.t.y, 1e-6f);
            Near.Equal(0f, without.GravityWeight);
            Near.Equal(1f, Sample("B").GravityWeight);
        }

        /// <summary>A state that gives no humanoid pose leaves the body to what the pose's other
        /// values take there (unmeasured in the Editor; as before): in a transition from an empty
        /// state on an override layer, the body moves from the layer below's to the clip's with
        /// the transition; in a transition to a state whose clip has no humanoid curve, it moves
        /// to the identity with it. Neither jumps when the transition starts or ends.</summary>
        [Fact]
        public void TheBodyFollowsATransitionWithAStateWithoutAHumanoidPose()
        {
            const long empty = 1102000009, transition = 1101000009;
            F4 leaning = Q(30f, 0f);
            string layered = Fixture.Header + Fixture.Layer("Base Layer", 1107000001, 0, 1f) + Fixture.Layer("Above", 1107000002, 0, 1f)
                + Fixture.Machine(1107000001, Fixture.StateA, Fixture.StateA) + Fixture.Machine(1107000002, empty, empty, Fixture.StateB)
                + Fixture.State(Fixture.StateA, "A", Fixture.ClipA, false) + Fixture.State(empty, "Empty", 0, false, transition)
                + Fixture.State(Fixture.StateB, "B", Fixture.ClipB, false) + Fixture.Transition(transition, Fixture.StateB, 0.2f, 0.5f)
                + Fixture.Clip(Fixture.ClipA, "A", Clip(1f, F4.QIdentity)) + Fixture.Clip(Fixture.ClipB, "B", Clip(1.3f, leaning));
            var above = Fixture.Rig(layered, m_Created);
            above.Update(0f);
            above.Update(0.25f);
            above.Update(0.2f);
            Assert.True(above.IsInTransition(1));
            float p = above.GetAnimatorTransitionInfo(1).normalizedTime;
            Assert.InRange(p, 0.2f, 0.8f);
            Near.Equal(1f + 0.3f * p, BodyPosition(above).y, 1e-4f);
            Assert.True(Fixture.Angle(Blend(F4.QIdentity, leaning, p), BodyRotation(above)) < 0.1f);

            var generic = Fixture.Rig(Fixture.TransitionController(new[] { ("NotAHumanCurve", 1f) }, false, false), m_Created);
            generic.Update(0f);
            generic.Update(0.25f);
            generic.Update(0.2f);
            Assert.True(generic.IsInTransition(0));
            p = generic.GetAnimatorTransitionInfo(0).normalizedTime;
            Assert.InRange(p, 0.2f, 0.8f);
            Near.Equal(1f - p, BodyPosition(generic).y, 1e-4f);
        }

        // ------------------------------------------------------------------ muscles at rest

        /// <summary>An override layer's humanoid clip takes the muscles it has no curve for to
        /// rest by the layer's weight, as it blends the ones it has. Controls: an additive layer
        /// leaves them, a clip with the curve blends towards its value, and a clip with body
        /// curves alone (no AnimationClip.humanMotion) leaves the muscles too (unmeasured, as
        /// before).</summary>
        [Fact]
        public void AnOverrideLayersClipTakesTheMusclesItLacksToRest()
        {
            var a = Clip(1f, F4.QIdentity, more: ("Spine Front-Back", 0.4f));
            var without = Clip(1f, F4.QIdentity);
            var with = Clip(1f, F4.QIdentity, more: ("Spine Front-Back", 0.8f));
            var bodyOnly = without.Where(c => c.Item1.StartsWith("Root", StringComparison.Ordinal)).ToArray();
            Near.Equal(0.3f, Play(Fixture.LayerController(0, 0.25f, a: a, b: without)).Human.Value(s_Spine), 1e-5f);
            Near.Equal(0.5f, Play(Fixture.LayerController(0, 0.25f, a: a, b: with)).Human.Value(s_Spine), 1e-5f);
            Near.Equal(0.4f, Play(Fixture.LayerController(1, 0.25f, a: a, b: without)).Human.Value(s_Spine), 1e-5f);
            Near.Equal(0.4f, Play(Fixture.LayerController(0, 0.25f, a: a, b: bodyOnly)).Human.Value(s_Spine), 1e-5f);
        }

        // ------------------------------------------------------------------ Direct blend trees

        /// <summary>A Direct blend tree blends its clips as a mixer: the body is the sum of the
        /// clips' bodies by their weights, the identity taking what they leave of one, and the
        /// state lasts the clips' lengths summed by weight once the weights reach one. Control:
        /// weights that sum to one give the 1D tree's body.</summary>
        [Theory]
        [InlineData(0.3f, 0.4f, 0.3f * 1.2f + 0.4f * 0.8f, 0.1f)]
        [InlineData(0.8f, 0.6f, 0.8f * 1.2f + 0.6f * 0.8f, 0.1f / 1.4f)]
        [InlineData(0.5f, 0.5f, 1f, 0.1f)]
        public void ADirectBlendTreeSumsItsClipsBodies(float wb, float wc, float height, float time)
        {
            var animator = Play(Fixture.TreeController(true, null, wb, wc, Clip(1.2f, F4.QIdentity), Clip(0.8f, F4.QIdentity)));
            Near.Equal(height, BodyPosition(animator).y, 1e-5f);
            Near.Equal(time, animator.GetCurrentAnimatorStateInfo(0).normalizedTime, 1e-5f);
            if (wb + wc == 1f)
            {
                var tree = Play(Fixture.TreeController(false, null, b: Clip(1.2f, F4.QIdentity), c: Clip(0.8f, F4.QIdentity)));
                Near.Equal(BodyPosition(tree).y, BodyPosition(animator).y, 1e-6f);
            }
        }

        /// <summary>What a Direct blend tree's clips leave of one is the identity as it is
        /// (tests/projects/humanoid body.directEmptyChild, measured for a foot goal): a goal's
        /// rotation with a negative w at 0.5 blends with (0, 0, 0, 1), not with the identity of
        /// its own hemisphere; the body's follows the same rule (unmeasured: the probe's bodies
        /// below a total of one have a positive w). Control: the two differ.</summary>
        [Fact]
        public void ADirectBlendTreeBelowOneCompletesRotationsWithThePlainIdentity()
        {
            var leaning = -Q(100f, 0f);
            Assert.True(leaning.w < -0.1f);
            var plain = M.QNormalize(leaning * 0.5f + F4.QIdentity * 0.5f);
            var aligned = M.QNormalize(leaning * 0.5f - F4.QIdentity * 0.5f);
            Assert.True(Fixture.Angle(plain, aligned) > 20f);

            var body = Play(Fixture.TreeController(true, null, 0.5f, 0f, Clip(1f, leaning), Clip(1f, F4.QIdentity)));
            Assert.True(Fixture.Angle(plain, BodyRotation(body)) < 0.1f);

            // The goal under an identity body, so that it is placed as its curves give it.
            var goal = Clip(null, default).Select(c => c.Item1 switch
            {
                "LeftFootQ.x" => (c.Item1, leaning.x), "LeftFootQ.y" => (c.Item1, leaning.y),
                "LeftFootQ.z" => (c.Item1, leaning.z), "LeftFootQ.w" => (c.Item1, leaning.w), _ => c,
            }).ToArray();
            var goals = Play(Fixture.TreeController(true, null, 0.5f, 0f, goal, Clip(1f, F4.QIdentity)));
            var written = M.QNormalize(new F4(goals.Human.Value(LeftFoot + 3), goals.Human.Value(LeftFoot + 4), goals.Human.Value(LeftFoot + 5),
                goals.Human.Value(LeftFoot + 6)));
            Assert.True(Fixture.Angle(plain, written) < 0.1f);
        }

        /// <summary>A Direct blend tree whose humanoid clips are all at weight 0 gives the identity
        /// pose once: the goals at the motion's origin with the state's weight, the body on the
        /// root. A clip at weight 0 beside one at weight 1 leaves that clip's goals as it gives
        /// them alone.</summary>
        [Fact]
        public void ADirectBlendTreeAtWeightZeroGivesTheIdentityPose()
        {
            var b = Clip(1.2f, Q(20f, 0f));
            var c = Clip(0.8f, F4.QIdentity);
            var none = Play(Fixture.TreeController(true, null, 0f, 0f, b, c));
            for (int k = 0; k < HumanStateGoals.Width; k++)
                Near.Equal(k == HumanStateGoals.Width - 1 ? 1f : 0f, none.Human.Value(LeftFoot + k), 1e-6f);
            Near.Equal(0f, BodyPosition(none).y, 1e-6f);

            var one = Play(Fixture.TreeController(true, null, 0f, 1f, b, c));
            var alone = Play(Fixture.TreeController(false, null, b: c, c: c));
            for (int k = 0; k < HumanStateGoals.Width; k++)
                Near.Equal(alone.Human.Value(LeftFoot + k), one.Human.Value(LeftFoot + k), 1e-6f);
        }

        // ------------------------------------------------------------------ playables

        AnimationClip Asset((string Attribute, float Value)[] curves)
        {
            var clip = new AnimationClip();
            m_Created.Add(clip);
            foreach (var (attribute, value) in curves)
                clip.m_Curves.Add(new ClipCurve
                {
                    Binding = new CurveBinding { Path = "", Attribute = attribute, ClassId = 95 },
                    Curve = AnimationCurve.Constant(0f, 1f, value),
                });
            clip.m_Version++;
            return clip;
        }

        Animator Graph(Func<PlayableGraph, Playable> build)
        {
            var root = AnimationHumanStreamTests.Humanoid(out var animator);
            m_Created.Add(root);
            m_Created.Add(animator.avatar);
            var graph = PlayableGraph.Create("body-blend");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            try
            {
                AnimationPlayableOutput.Create(graph, "animation", animator).SetSourcePlayable(build(graph));
                graph.Evaluate(0f);
                graph.Evaluate(0.1f);
            }
            finally { graph.Destroy(); }
            return animator;
        }

        static AnimationClipPlayable Input(PlayableGraph graph, AnimationClip clip)
        {
            var playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            return playable;
        }

        /// <summary>An AnimationMixerPlayable blends its clips' bodies as rotations: a negated
        /// RootQ, or one with only some of its curves, gives the body of the whole rotations'
        /// normalized blend.</summary>
        [Theory]
        [InlineData("xyzw", -1f)]
        [InlineData("x", 1f)]
        [InlineData("w", 1f)]
        public void AMixerBlendsItsClipsBodiesAsRotations(string curves, float sign)
        {
            var first = Q(40f, 0f) * sign;
            var whole = M.QNormalize(new F4(curves.Contains('x') ? first.x : 0f, curves.Contains('y') ? first.y : 0f,
                curves.Contains('z') ? first.z : 0f, curves.Contains('w') ? first.w : 1f));
            var turned = Q(0f, 50f);
            AnimationClip a = Asset(Clip(1.2f, first, curves)), b = Asset(Clip(0.8f, turned));
            var animator = Graph(graph =>
            {
                var mixer = AnimationMixerPlayable.Create(graph, 2);
                mixer.ConnectInput(0, Input(graph, a), 0, 0.25f);
                mixer.ConnectInput(1, Input(graph, b), 0, 0.75f);
                return mixer;
            });
            Assert.True(Fixture.Angle(Blend(whole, turned, 0.75f), BodyRotation(animator)) < 0.1f);
            Near.Equal(0.25f * 1.2f + 0.75f * 0.8f, BodyPosition(animator).y, 1e-5f);
        }

        /// <summary>An AnimationLayerMixerPlayable's layer takes the muscles its clip lacks to
        /// rest and the body towards the clip's whole rotation by its weight, as a controller's
        /// override layer does.</summary>
        [Fact]
        public void ALayerMixersClipIsAWholePose()
        {
            var below = Q(0f, 70f);
            AnimationClip a = Asset(Clip(1f, below, more: ("Spine Front-Back", 0.4f))), b = Asset(Clip(1.4f, new F4(0.5f, 0f, 0f, 0f), "x"));
            var animator = Graph(graph =>
            {
                var mixer = AnimationLayerMixerPlayable.Create(graph, 2);
                mixer.ConnectInput(0, Input(graph, a), 0, 1f);
                mixer.ConnectInput(1, Input(graph, b), 0, 0.25f);
                return mixer;
            });
            Near.Equal(0.3f, animator.Human.Value(s_Spine), 1e-5f);
            Assert.True(Fixture.Angle(Blend(below, M.QNormalize(new F4(0.5f, 0f, 0f, 1f)), 0.25f), BodyRotation(animator)) < 0.1f);
            Near.Equal(1.1f, BodyPosition(animator).y, 1e-5f);
        }
    }
}
