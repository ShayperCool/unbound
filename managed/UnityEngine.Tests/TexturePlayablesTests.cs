using System;
using UnityEngine.Experimental.Playables;
using UnityEngine.Playables;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The experimental texture playables' answers (tests/projects/texplayables).</summary>
    public sealed class TexturePlayablesTests
    {
        [Fact]
        public void ThePlayablesKeepWhatTheyAreGivenWithTheirInputs()
        {
            var graph = PlayableGraph.Create("Texture");
            try
            {
                var camera = new GameObject("Camera").AddComponent<Camera>();
                var cam = CameraPlayable.Create(graph, camera);
                Assert.Same(camera, cam.GetCamera());
                Assert.Equal(0, ((Playable)cam).GetInputCount());
                Assert.Equal(typeof(CameraPlayable), ((Playable)cam).GetPlayableType());
                cam.SetCamera(null);
                Assert.Null(cam.GetCamera());
                var effect = MaterialEffectPlayable.Create(graph, null);
                Assert.Equal(-1, effect.GetPass());
                effect.SetPass(5);
                Assert.Equal(5, effect.GetPass());
                Assert.Equal(2, ((Playable)TextureMixerPlayable.Create(graph)).GetInputCount());
                var ex = Assert.Throws<InvalidCastException>(() => (MaterialEffectPlayable)(Playable)cam);
                Assert.Equal("Can't set handle: the playable is not an MaterialEffectPlayable.", ex.Message);
                Object.DestroyImmediate(camera.gameObject);
            }
            finally { graph.Destroy(); }
        }

        static int Hits(string member) => System.Linq.Enumerable.FirstOrDefault(Unbound.Runtime.Stubs.HitReport(), h => h.Key == member).Value;

        [Fact]
        public void EvaluatingATextureOutputWithASourceSaysItDoesNotDraw()
        {
            var graph = PlayableGraph.Create("Texture");
            try
            {
                var key = Unbound.Runtime.Playables.TextureOutputs.DrawingKey;
                Unbound.Runtime.Stubs.Forget(key);
                // A director's binding gives the output no target: it still says so.
                var output = TexturePlayableOutput.Create(graph, "Out", null);
                graph.Evaluate();
                Assert.Equal(0, Hits(key));
                ((PlayableOutput)output).SetSourcePlayable(CameraPlayable.Create(graph, null));
                graph.Evaluate();
                Assert.Equal(1, Hits(key));
            }
            finally { graph.Destroy(); }
        }

        [Fact]
        public void TheTextureOutputHoldsItsTargetAndAnInvalidGraphThrows()
        {
            var graph = PlayableGraph.Create("Texture");
            try
            {
                var target = new RenderTexture(4, 4, 0);
                var output = TexturePlayableOutput.Create(graph, "Out", target);
                Assert.True(output.IsOutputValid());
                Assert.Same(target, output.GetTarget());
                Assert.Equal(1, graph.GetOutputCountByType<TexturePlayableOutput>());
                Assert.Equal(typeof(RenderTexture), TexturePlayableBinding.Create("s", null).outputTargetType);
                var ex = Assert.Throws<NullReferenceException>(() => TexturePlayableOutput.Create(default, "x", target));
                Assert.Equal("The PlayableGraph is null.", ex.Message);
                Assert.Throws<NullReferenceException>(() => CameraPlayable.Create(default, null));
                Object.DestroyImmediate(target);
            }
            finally { graph.Destroy(); }
        }
    }
}
