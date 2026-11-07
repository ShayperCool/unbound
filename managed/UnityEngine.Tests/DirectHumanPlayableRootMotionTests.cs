using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Unbound.Runtime.Humanoid;
using Unbound.Runtime.Playables;
using Unbound.Tests;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Xunit;

namespace UnityEngine.Tests
{
    [Collection("Animation")]
    public class DirectHumanPlayableRootMotionTests
    {
        // A root-motion-only avatar isolates graph motion from retargeting. The offscreen
        // CullUpdateTransforms rig never requests a skeleton write or IK solve.
        static GameObject Rig(out Animator animator, out Avatar avatar)
        {
            var root = new GameObject("HumanMotion");
            avatar = Avatar.Create();
            avatar.SetConstant(new AvatarConstant
            {
                AvatarSkeleton = new Skeleton(1, 0),
                HumanSkeletonIndexArray = new[] { 0 },
                Human = new Human { Skeleton = new Skeleton(1, 0) },
            });
            animator = root.AddComponent<Animator>();
            animator.avatar = avatar;
            animator.applyRootMotion = true;
            animator.cullingMode = AnimatorCullingMode.CullUpdateTransforms;
            return root;
        }

        static AnimationClip Clip()
        {
            var clip = new AnimationClip();
            void Add(string attribute, float from, float to) => clip.m_Curves.Add(new ClipCurve
            {
                Binding = new CurveBinding { Path = "", Attribute = attribute, ClassId = 95 },
                Curve = AnimationCurve.Linear(0f, from, 1f, to),
            });
            Add("RootT.x", 0f, 2f);
            Add("RootT.y", 1f, 1f);
            Add("RootQ.x", 0f, 0f);
            Add("RootQ.y", 0f, 0.5f);
            Add("RootQ.z", 0f, 0f);
            Add("RootQ.w", 1f, 0.8660254f);
            clip.m_Version++;
            return clip;
        }

        static PlayableGraph Graph(Animator animator, AnimationClip clip, out AnimationClipPlayable playable)
        {
            var graph = PlayableGraph.Create("human-motion");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            playable = AnimationClipPlayable.Create(graph, clip);
            playable.SetApplyFootIK(false);
            AnimationPlayableOutput.Create(graph, "animation", animator).SetSourcePlayable(playable);
            return graph;
        }

        [Fact]
        public void PausedClipDoesNotRepeatItsLastMotionInterval()
        {
            var root = Rig(out var animator, out var avatar);
            var clip = Clip();
            var graph = Graph(animator, clip, out var playable);
            var logger = Debug.unityLogger;
            var previous = logger.logHandler;
            var logs = new Logs();
            logger.logHandler = logs;
            try
            {
                Assert.True(animator.isHuman);
                Assert.True(clip.HasHumanCurves);
                graph.Evaluate(0f);
                graph.Evaluate(0.25f);
                Assert.Equal(0.25, playable.GetTime());
                Assert.True(animator.deltaPosition.magnitude > 0.1f, string.Join("\n", logs.Messages));
                Assert.True(Quaternion.Angle(Quaternion.identity, animator.deltaRotation) > 1f);
                var position = root.transform.position;
                var rotation = root.transform.rotation;
                double time = playable.GetTime();
                playable.Pause();
                for (int i = 0; i < 3; i++)
                {
                    graph.Evaluate(0.25f);
                    Assert.Equal(time, playable.GetTime());
                    Near.Equal(position, root.transform.position, 1e-5f);
                    Near.SameRotation(rotation, root.transform.rotation, 1e-3f);
                    Near.Equal(Vector3.zero, animator.deltaPosition, 1e-5f);
                    Near.SameRotation(Quaternion.identity, animator.deltaRotation, 1e-3f);
                }
            }
            finally
            {
                logger.logHandler = previous;
                graph.Destroy();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(avatar);
                Object.DestroyImmediate(clip);
            }
        }

        [Theory]
        [InlineData(0f)]
        [InlineData(0.25f)]
        [InlineData(1f)]
        public void FixedTwoInputMixerDeliversTranslationAndRotation(float weight)
        {
            string version = ProjectInfo.UnityVersion;
            ProjectInfo.UnityVersion = "6000.0.71f1";
            var root = Rig(out var animator, out var avatar);
            var clip = Clip();
            var graph = PlayableGraph.Create("human-mixer");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            try
            {
                var first = AnimationClipPlayable.Create(graph, clip);
                var second = AnimationClipPlayable.Create(graph, clip);
                first.SetApplyFootIK(false);
                second.SetApplyFootIK(false);
                var mixer = AnimationMixerPlayable.Create(graph, 2);
                mixer.ConnectInput(0, first, 0, weight);
                mixer.ConnectInput(1, second, 0, 1f - weight);
                AnimationPlayableOutput.Create(graph, "animation", animator).SetSourcePlayable(mixer);
                graph.Evaluate(0f);
                graph.Evaluate(0.25f);
                Assert.True(animator.deltaPosition.magnitude > 0.1f);
                Assert.True(Quaternion.Angle(Quaternion.identity, animator.deltaRotation) > 1f);
                Near.Equal(animator.deltaPosition, root.transform.position, 1e-5f);
                Near.SameRotation(animator.deltaRotation, root.transform.rotation, 1e-3f);
            }
            finally
            {
                graph.Destroy();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(avatar);
                Object.DestroyImmediate(clip);
                ProjectInfo.UnityVersion = version;
            }
        }

        /// <summary>The source of a humanoid mixer shape over copies of one clip, so a supported
        /// shape must move exactly as the direct clip does.</summary>
        static Playable HumanShape(PlayableGraph graph, AnimationClip clip, string shape, out AnimationMixerPlayable top)
        {
            AnimationClipPlayable Input()
            {
                var p = AnimationClipPlayable.Create(graph, clip);
                p.SetApplyFootIK(shape == "footIK");
                return p;
            }
            if (shape == "nested")
            {
                var inner = AnimationMixerPlayable.Create(graph, 2);
                inner.ConnectInput(0, Input(), 0, 0.5f);
                inner.ConnectInput(1, Input(), 0, 0.5f);
                top = AnimationMixerPlayable.Create(graph, 2);
                top.ConnectInput(0, Input(), 0, 0.4f);
                top.ConnectInput(1, inner, 0, 0.6f);
                return top;
            }
            if (shape == "one")
            {
                top = AnimationMixerPlayable.Create(graph, 1);
                top.ConnectInput(0, Input(), 0, 1f);
                return top;
            }
            top = AnimationMixerPlayable.Create(graph, 3);
            top.ConnectInput(0, Input(), 0, 0.2f);
            top.ConnectInput(1, Input(), 0, 0.3f);
            top.ConnectInput(2, Input(), 0, 0.5f);
            return top;
        }

        static (Vector3 Position, Quaternion Rotation) Motion(string shape, AnimationClip clip)
        {
            var root = Rig(out var animator, out var avatar);
            var graph = PlayableGraph.Create("human-shape-" + shape);
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            try
            {
                Playable source;
                AnimationMixerPlayable top = default;
                if (shape == "direct")
                {
                    var p = AnimationClipPlayable.Create(graph, clip);
                    p.SetApplyFootIK(false);
                    source = p;
                }
                else source = HumanShape(graph, clip, shape, out top);
                AnimationPlayableOutput.Create(graph, "animation", animator).SetSourcePlayable(source);
                graph.Evaluate(0f);
                if (shape == "live")
                {
                    // Weights changed between evaluations, still summing to one.
                    top.SetInputWeight(0, 0.6f);
                    top.SetInputWeight(1, 0f);
                    top.SetInputWeight(2, 0.4f);
                }
                graph.Evaluate(0.25f);
                return (animator.deltaPosition, animator.deltaRotation);
            }
            finally
            {
                graph.Destroy();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(avatar);
            }
        }

        // Unity 6000.0.71f1 (animculling humanMixerShape): any input count, nesting, input weights
        // changed between evaluations and foot IK compose root motion through the humanoid blend.
        [Theory]
        [InlineData("three")]
        [InlineData("nested")]
        [InlineData("one")]
        [InlineData("live")]
        [InlineData("footIK")]
        public void UnitSumHumanMixerShapesMoveAsTheirClip(string shape)
        {
            string version = ProjectInfo.UnityVersion;
            ProjectInfo.UnityVersion = "6000.0.71f1";
            var clip = Clip();
            try
            {
                var direct = Motion("direct", clip);
                var mixed = Motion(shape, clip);
                Assert.True(direct.Position.magnitude > 0.1f);
                Near.Equal(direct.Position, mixed.Position, 1e-5f);
                Near.SameRotation(direct.Rotation, mixed.Rotation, 1e-3f);
            }
            finally
            {
                Object.DestroyImmediate(clip);
                ProjectInfo.UnityVersion = version;
            }
        }

        // A visibility return replays only the blended pose and step; Unity re-blends the clip
        // poses and steps with the current weights (animculling capture), so the return warns
        // when the mixer tree's weights hash differs from the one retained with them.
        [Fact]
        public void TreeWeightsChangeOnlyWithAWeightAtAnyDepth()
        {
            var clip = Clip();
            var graph = PlayableGraph.Create("human-weights");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            try
            {
                HumanShape(graph, clip, "nested", out var top);
                var node = PlayableGraphData.Get(top.GetHandle());
                var inner = PlayableGraphData.Get(top.GetInput(1).GetHandle());
                int original = MixerKind.TreeWeights(node);
                graph.Evaluate(0.25f);
                Assert.Equal(original, MixerKind.TreeWeights(node));
                top.SetInputWeight(0, 0.3f);
                Assert.NotEqual(original, MixerKind.TreeWeights(node));
                top.SetInputWeight(0, 0.4f);
                Assert.Equal(original, MixerKind.TreeWeights(node));
                top.GetInput(1).SetInputWeight(0, 0.25f);
                Assert.NotEqual(original, MixerKind.TreeWeights(node));
                Assert.NotEqual(MixerKind.TreeWeights(inner), MixerKind.TreeWeights(node));
            }
            finally
            {
                graph.Destroy();
                Object.DestroyImmediate(clip);
            }
        }

        [Theory]
        [InlineData("sum")]
        [InlineData("high")]
        [InlineData("release")]
        [InlineData("shared")]
        [InlineData("fractional")]
        [InlineData("sparse0")]
        [InlineData("sparse1")]
        public void UnmeasuredHumanMixerContextsStayDiagnostic(string context)
        {
            string version = ProjectInfo.UnityVersion;
            ProjectInfo.UnityVersion = context == "release" ? "6000.0.72f1" : "6000.0.71f1";
            var root = Rig(out var animator, out var avatar);
            var clip = Clip();
            if (context.StartsWith("sparse", StringComparison.Ordinal))
            {
                clip.m_Curves.RemoveAll(curve => curve.Binding.Attribute == "RootQ.w");
                clip.m_Version++;
            }
            var graph = PlayableGraph.Create("unsupported-human-mixer");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var logger = Debug.unityLogger;
            var previous = logger.logHandler;
            bool enabled = logger.logEnabled;
            var filter = logger.filterLogType;
            var logs = new Logs();
            logger.logHandler = logs;
            logger.logEnabled = true;
            logger.filterLogType = LogType.Log;
            try
            {
                var first = AnimationClipPlayable.Create(graph, clip);
                var second = AnimationClipPlayable.Create(graph, clip);
                first.SetApplyFootIK(false);
                second.SetApplyFootIK(false);
                var mixer = AnimationMixerPlayable.Create(graph, 2);
                // sum: weights summing to 0.5, high: to 1.5 (Unity blends them unnormalized).
                float firstWeight = context == "sparse0" ? 0f : context == "sparse1" ? 1f : context == "high" ? 0.75f : 0.25f;
                mixer.ConnectInput(0, first, 0, firstWeight);
                mixer.ConnectInput(1, second, 0, context == "sum" ? 0.25f : context == "high" ? 0.75f : 1f - firstWeight);
                var output = AnimationPlayableOutput.Create(graph, "animation", animator);
                output.SetSourcePlayable(mixer);
                if (context == "fractional") output.SetWeight(0.5f);
                if (context == "shared")
                {
                    var other = AnimationMixerPlayable.Create(graph, 1);
                    other.ConnectInput(0, first, 0, 1f);
                }
                const string diagnostic = "Animator root motion of unsupported humanoid mixer output";
                int hits = 0;
                foreach (var hit in Stubs.HitReport()) if (hit.Key == diagnostic) hits = hit.Value;
                graph.Evaluate(0f);
                graph.Evaluate(0.25f);
                int after = 0;
                foreach (var hit in Stubs.HitReport()) if (hit.Key == diagnostic) after = hit.Value;
                Assert.True(after > hits);
                if (hits == 0) Assert.Contains(logs.Messages, message => message.Contains("remain unsupported"));
                Near.Equal(Vector3.zero, root.transform.position, 1e-5f);
                Near.Equal(Vector3.zero, animator.deltaPosition, 1e-5f);
            }
            finally
            {
                logger.logHandler = previous;
                logger.logEnabled = enabled;
                logger.filterLogType = filter;
                graph.Destroy();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(avatar);
                Object.DestroyImmediate(clip);
                ProjectInfo.UnityVersion = version;
            }
        }

        sealed class Logs : ILogHandler
        {
            internal readonly List<string> Messages = new List<string>();
            public void LogFormat(LogType type, Object context, string format, params object[] args) =>
                Messages.Add(string.Format(format, args));
            public void LogException(Exception exception, Object context) => Messages.Add(exception.ToString());
        }

        [Fact]
        public void SeparateManualGraphsRetainTheMultipleOutputDiagnostic()
        {
            var root = Rig(out var animator, out var avatar);
            var clip = Clip();
            var first = Graph(animator, clip, out _);
            var second = Graph(animator, clip, out _);
            var logger = Debug.unityLogger;
            var previous = logger.logHandler;
            bool enabled = logger.logEnabled;
            var filter = logger.filterLogType;
            var logs = new Logs();
            logger.logHandler = logs;
            logger.logEnabled = true;
            logger.filterLogType = LogType.Log;
            try
            {
                first.Evaluate(0f);
                first.Evaluate(0.25f);
                second.Evaluate(0.25f);
                Assert.Contains(logs.Messages, message => message.Contains("one full-weight output of a nonmirrored humanoid clip"));
                Near.Equal(Vector3.zero, root.transform.position, 1e-5f);
                Near.Equal(Vector3.zero, animator.deltaPosition, 1e-5f);
            }
            finally
            {
                logger.logHandler = previous;
                logger.logEnabled = enabled;
                logger.filterLogType = filter;
                second.Destroy();
                first.Destroy();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(avatar);
                Object.DestroyImmediate(clip);
            }
        }
    }
}
