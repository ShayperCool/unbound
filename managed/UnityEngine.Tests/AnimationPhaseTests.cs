using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using Unbound.Runtime.Playables;
using Unbound.Tests;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The automatic Animator update in its two player-loop leaves and the state callback
    /// order of an update (Unity 6000.0.71f1, tests/projects/animcallbacks).</summary>
    [Collection("Animation")]
    public sealed class AnimationPhaseTests
    {
        static readonly List<string> s_Calls = new List<string>();

        public class SlotRecorder : StateMachineBehaviour
        {
            static string Name(AnimatorStateInfo info) => info.IsName("New State") ? "A" : info.IsName("PanelShowClip") ? "B" : "?";
            public override void OnStateEnter(Animator animator, AnimatorStateInfo info, int layer) => s_Calls.Add("enter " + Name(info));
            public override void OnStateUpdate(Animator animator, AnimatorStateInfo info, int layer) => s_Calls.Add("update " + Name(info));
            public override void OnStateExit(Animator animator, AnimatorStateInfo info, int layer) => s_Calls.Add("exit " + Name(info));
            public override void OnStateMove(Animator animator, AnimatorStateInfo info, int layer) => s_Calls.Add("move " + Name(info));
        }

        public class MoveObserver : MonoBehaviour
        {
            internal Transform Body;
            internal readonly List<float> SeenX = new List<float>();
            void OnAnimatorMove() => SeenX.Add(Body.localPosition.x);
        }

        [Fact]
        public void ATransitionsStartAndEndFollowSlotOrder()
        {
            // New State (A) leaves at 0.75 of its second for PanelShowClip (B) over 0.25 s.
            s_Calls.Clear();
            var file = ObjectGraphBuilder.Build(UnityYaml.Parse(AnimationFixtures.PanelController), "test.asset", null, ObjectGraphBuilder.Kind.Asset, null);
            var recorder = ScriptableObject.CreateInstance<SlotRecorder>();
            foreach (var state in file.Objects.Values.OfType<ControllerStateAsset>()) state.Behaviours.Add(recorder);
            var root = new GameObject("Panel", typeof(RectTransform));
            var animator = root.AddComponent<Animator>();
            animator.runtimeAnimatorController = (RuntimeAnimatorController)file.Objects[9100000];
            var updates = new List<string[]>();
            for (int i = 0; i < 14; i++)
            {
                s_Calls.Clear();
                animator.Update(0.1f);
                updates.Add(s_Calls.ToArray());
            }
            // The update that starts the transition: the source's Update before the destination's
            // Enter; both get OnStateMove.
            Assert.Contains(updates, u => u.SequenceEqual(new[] { "update A", "enter B", "move A", "move B" }));
            // The update that completes it: Exit(source) then Update(destination); the source still
            // gets OnStateMove in that update.
            Assert.Contains(updates, u => u.SequenceEqual(new[] { "exit A", "update B", "move A", "move B" }));
            // Afterwards only the destination.
            Assert.Equal(new[] { "update B", "move B" }, updates[^1]);
            Object.DestroyImmediate(root);
            Object.DestroyImmediate(recorder);
        }

        [Theory]
        [InlineData("6000.0.71f1", true)]
        // Unmeasured releases keep the single pass in the Begin leaf.
        [InlineData("6000.0.70f1", false)]
        public void TheAutomaticUpdateMovesInTheBeginLeafAndWritesInTheEndLeaf(string release, bool split)
        {
            string version = ProjectInfo.UnityVersion;
            ProjectInfo.UnityVersion = release;
            var root = new GameObject("Root");
            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);
            var animator = root.AddComponent<Animator>();
            var observer = root.AddComponent<MoveObserver>();
            observer.Body = body;
            var clip = new AnimationClip();
            clip.SetCurve("Body", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0f, 0.5f, 1f, 1f));
            var graph = PlayableGraph.Create("phases");
            try
            {
                var output = AnimationPlayableOutput.Create(graph, "Animation", animator);
                output.SetSourcePlayable(AnimationClipPlayable.Create(graph, clip));
                graph.Play();
                PlayableGraphData.UpdateAll();
                AnimationSystem.UpdateAnimators(fixedStep: false);
                // Movement ran before any pose was written.
                Assert.Equal(new[] { 0f }, observer.SeenX);
                Assert.Equal(split ? 0f : 0.5f, body.localPosition.x, 4);
                AnimationPhases.WritePoses();
                Assert.Equal(0.5f, body.localPosition.x, 4);

                // An explicit evaluation still writes at once, after its movement.
                graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                graph.Evaluate(0.5f);
                Assert.Equal(new[] { 0f, 0.5f }, observer.SeenX);
                Assert.Equal(0.75f, body.localPosition.x, 4);
            }
            finally
            {
                graph.Destroy();
                Object.DestroyImmediate(root);
                ProjectInfo.UnityVersion = version;
            }
        }

        [Fact]
        public void AnAnimatorDisabledBetweenTheLeavesIsNotWritten()
        {
            string version = ProjectInfo.UnityVersion;
            ProjectInfo.UnityVersion = "6000.0.71f1";
            var root = new GameObject("Root");
            var body = new GameObject("Body").transform;
            body.SetParent(root.transform, false);
            var animator = root.AddComponent<Animator>();
            var clip = new AnimationClip();
            clip.SetCurve("Body", typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0f, 0.5f, 1f, 1f));
            var graph = PlayableGraph.Create("disabled");
            try
            {
                var output = AnimationPlayableOutput.Create(graph, "Animation", animator);
                output.SetSourcePlayable(AnimationClipPlayable.Create(graph, clip));
                graph.Play();
                PlayableGraphData.UpdateAll();
                AnimationSystem.UpdateAnimators(fixedStep: false);
                animator.enabled = false;
                AnimationPhases.WritePoses();
                Assert.Equal(0f, body.localPosition.x);
            }
            finally
            {
                graph.Destroy();
                Object.DestroyImmediate(root);
                ProjectInfo.UnityVersion = version;
            }
        }
    }
}
