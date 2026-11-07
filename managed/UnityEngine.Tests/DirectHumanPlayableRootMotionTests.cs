using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Unbound.Runtime.Humanoid;
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
            Add("RootQ.y", 0f, 0.5f);
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
                Assert.Contains(logs.Messages, message => message.Contains("only one direct, full-weight nonmirrored humanoid clip output"));
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
