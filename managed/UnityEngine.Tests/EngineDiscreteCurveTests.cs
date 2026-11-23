using UnityEditor;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A discrete curve of an engine component's property animates nothing; the same
    /// property's float curve does (tests/projects/enginediscrete measures the Editor).</summary>
    [Collection("Animation")]
    public sealed class EngineDiscreteCurveTests
    {
        static int Played(EditorCurveBinding binding)
        {
            var go = new GameObject("discrete");
            var renderer = go.AddComponent<SpriteRenderer>();
            var animator = go.AddComponent<Animator>();
            var clip = new AnimationClip();
            AnimationUtility.SetEditorCurve(clip, binding, new AnimationCurve(new Keyframe(0f, 1f), new Keyframe(1f, 2f)));
            var graph = PlayableGraph.Create("discrete");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            AnimationPlayableOutput.Create(graph, "out", animator).SetSourcePlayable(AnimationClipPlayable.Create(graph, clip));
            graph.Evaluate(0f);
            graph.Evaluate(1f);
            int order = renderer.sortingOrder;
            graph.Destroy();
            Object.DestroyImmediate(go);
            return order;
        }

        [Fact]
        public void AnEngineComponentsDiscreteCurveAnimatesNothing()
        {
            Assert.Equal(0, Played(EditorCurveBinding.DiscreteCurve("", typeof(SpriteRenderer), "m_SortingOrder")));
            // Control: the float curve of the same property sets it.
            Assert.Equal(2, Played(EditorCurveBinding.FloatCurve("", typeof(SpriteRenderer), "m_SortingOrder")));
        }
    }
}
