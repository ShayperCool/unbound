using System.Linq;
using Unbound.Runtime;
using Unbound.Tests;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Root motion of a generic Animator (no Avatar) from its clip's body curves (RootT,
    /// RootQ on the root), as Editor-authored generic clips with root motion carry them (Unity
    /// 6000.0.71f1, tests/projects/rootcurves).</summary>
    [Collection("Animation")]
    public sealed class GenericBodyRootMotionTests
    {
        static readonly float s_Pitch = Mathf.Sin(10f * Mathf.Deg2Rad), s_PitchW = Mathf.Cos(10f * Mathf.Deg2Rad);

        /// <summary>AnimationFixtures.RootMotion with its Move clip replaced by body curves: 2 m
        /// forward over a second, the body pitched by 20 degrees.</summary>
        static RuntimeAnimatorController Controller()
        {
            var file = ObjectGraphBuilder.Build(UnityYaml.Parse(AnimationFixtures.RootMotion), "bodymotion.asset", null,
                ObjectGraphBuilder.Kind.Asset, null);
            var clip = (AnimationClip)file.Objects[7400001];
            clip.m_Curves.Clear();
            void Add(string attribute, AnimationCurve curve) => clip.m_Curves.Add(new ClipCurve
            {
                Binding = new CurveBinding { Path = "", Attribute = attribute, ClassId = 95 },
                Curve = curve,
            });
            Add("RootT.x", AnimationCurve.Constant(0f, 1f, 0f));
            Add("RootT.y", AnimationCurve.Constant(0f, 1f, 0f));
            Add("RootT.z", AnimationCurve.Linear(0f, 0f, 1f, 2f));
            Add("RootQ.x", AnimationCurve.Constant(0f, 1f, s_Pitch));
            Add("RootQ.y", AnimationCurve.Constant(0f, 1f, 0f));
            Add("RootQ.z", AnimationCurve.Constant(0f, 1f, 0f));
            Add("RootQ.w", AnimationCurve.Constant(0f, 1f, s_PitchW));
            clip.m_Version++;
            return (RuntimeAnimatorController)file.Objects[9100000];
        }

        static Animator Rig(bool applyRootMotion)
        {
            var root = new GameObject("Body");
            root.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            var animator = root.AddComponent<Animator>();
            animator.applyRootMotion = applyRootMotion;
            animator.runtimeAnimatorController = Controller();
            animator.Play("Move", 0, 0f);
            animator.Update(0f);
            return animator;
        }

        [Fact]
        public void BodyCurvesMoveTheRootInTheBodysYawFrame()
        {
            var animator = Rig(applyRootMotion: true);
            try
            {
                Assert.False(animator.hasRootMotion);
                animator.Update(0.25f);
                // 0.5 m along the root's forward (+x after the 90 degree turn); the pitch neither
                // tilts the step nor turns the root.
                Near.Equal(new Vector3(0.5f, 0f, 0f), animator.deltaPosition, 1e-4f);
                Near.Equal(new Vector3(2f, 0f, 0f), animator.velocity, 1e-3f);
                Assert.Equal(1f, Mathf.Abs(animator.deltaRotation.w), 5);
                Near.Equal(new Vector3(0.5f, 0f, 0f), animator.transform.position, 1e-4f);
                Near.Equal(90f, animator.transform.eulerAngles.y, 1e-3f);
            }
            finally { Object.DestroyImmediate(animator.gameObject); }
        }

        public class MoveReporter : MonoBehaviour
        {
            internal Vector3 Delta;
            void OnAnimatorMove() => Delta = GetComponent<Animator>().deltaPosition;
        }

        // rootcurves scriptOff: OnAnimatorMove gets the motion with root motion off; nothing
        // moves the root.
        [Fact]
        public void AMovementScriptGetsTheMotionWithRootMotionOff()
        {
            var animator = Rig(applyRootMotion: false);
            try
            {
                var reporter = animator.gameObject.AddComponent<MoveReporter>();
                animator.Update(0.25f);
                Near.Equal(new Vector3(0.5f, 0f, 0f), reporter.Delta, 1e-4f);
                Assert.Equal(Vector3.zero, animator.transform.position);
            }
            finally { Object.DestroyImmediate(animator.gameObject); }
        }

        // MatchTarget corrects the body motion like any generic root motion: the root reaches the
        // target at the match's end.
        [Fact]
        public void MatchTargetCorrectsTheBodyMotion()
        {
            var animator = Rig(applyRootMotion: true);
            try
            {
                animator.MatchTarget(new Vector3(3f, 0f, 1f), Quaternion.Euler(0f, 90f, 0f), AvatarTarget.Root,
                    new MatchTargetWeightMask(Vector3.one, 1f), 0f, 0.5f);
                animator.Update(0.25f);
                Assert.True(animator.isMatchingTarget);
                animator.Update(0.25f);
                Near.Equal(new Vector3(3f, 0f, 1f), animator.transform.position, 1e-3f);
                animator.Update(0.25f);
                Assert.False(animator.isMatchingTarget);
            }
            finally { Object.DestroyImmediate(animator.gameObject); }
        }

        /// <summary>The fixture's Move clip (body curves only).</summary>
        static AnimationClip MoveClip(RuntimeAnimatorController controller) => controller.animationClips[0];

        // rootcurves graph.*: graph outputs move a generic Animator by body curves as its
        // controller does, through a clip playable, a mixer (inputs summed by weight) and a
        // controller playable.
        [Theory]
        [InlineData("clip", 1f)]
        [InlineData("mixer", 0.5f)]
        [InlineData("controller", 1f)]
        public void GraphOutputsMoveTheRootByBodyCurves(string shape, float share)
        {
            var root = new GameObject("Body");
            root.transform.rotation = Quaternion.Euler(0f, 90f, 0f);
            var animator = root.AddComponent<Animator>();
            animator.applyRootMotion = true;
            var controller = Controller();
            var graph = PlayableGraph.Create("body-motion");
            try
            {
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                Playable source;
                if (shape == "controller")
                {
                    source = AnimatorControllerPlayable.Create(graph, controller);
                }
                else
                {
                    source = AnimationClipPlayable.Create(graph, MoveClip(controller));
                    if (shape == "mixer")
                    {
                        var mixer = AnimationMixerPlayable.Create(graph, 1);
                        graph.Connect(source, 0, mixer, 0);
                        mixer.SetInputWeight(0, 0.5f);
                        source = mixer;
                    }
                }
                AnimationPlayableOutput.Create(graph, "animation", animator).SetSourcePlayable(source);
                graph.Evaluate(0f);
                if (shape == "controller")
                {
                    // The fixture's default state has no motion.
                    ((AnimatorControllerPlayable)source).Play("Move", 0, 0f);
                    graph.Evaluate(0f);
                }
                graph.Evaluate(0.25f);
                Near.Equal(new Vector3(0.5f * share, 0f, 0f), animator.deltaPosition, 1e-4f);
                Near.Equal(new Vector3(0.5f * share, 0f, 0f), root.transform.position, 1e-4f);
            }
            finally
            {
                graph.Destroy();
                Object.DestroyImmediate(root);
            }
        }
    }
}
