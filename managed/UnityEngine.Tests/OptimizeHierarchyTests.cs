using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using Unbound.Tests;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>AnimatorUtility.OptimizeTransformHierarchy / DeoptimizeTransformHierarchy on a rig
    /// built at run time (an AvatarBuilder generic avatar, a skinned body): the hierarchy that
    /// remains, the skin and exposed transforms following the skeleton as the bones did, the way
    /// back, and the cases that do nothing (tests/projects/animoptimize measures the Editor).</summary>
    [Collection("Animation")]
    public sealed class OptimizeHierarchyTests
    {
        static readonly string[] BonePaths = { "Rig/Hips", "Rig/Hips/Spine", "Rig/Hips/Spine/Hand" };

        /// <summary>R (Animator) / Body (skinned to the three bones), Rig / Hips / Spine / Hand, and
        /// Hips / Leg; the hips turned and moved so that globals are not locals.</summary>
        static GameObject Rig(out Animator animator, out SkinnedMeshRenderer skin)
        {
            var root = new GameObject("R");
            var body = new GameObject("Body");
            body.transform.SetParent(root.transform, false);
            var rig = new GameObject("Rig").transform;
            rig.SetParent(root.transform, false);
            var hips = new GameObject("Hips").transform;
            hips.SetParent(rig, false);
            hips.localPosition = new Vector3(0f, 1f, 0f);
            hips.localRotation = Quaternion.Euler(0f, 0f, 10f);
            var spine = new GameObject("Spine").transform;
            spine.SetParent(hips, false);
            spine.localPosition = new Vector3(0f, 2f, 0f);
            var hand = new GameObject("Hand").transform;
            hand.SetParent(spine, false);
            hand.localPosition = new Vector3(0f, 2f, 0f);
            new GameObject("Leg").transform.SetParent(hips, false);
            var bones = new[] { hips, spine, hand };
            var mesh = new Mesh
            {
                vertices = new[] { new Vector3(0f, 1f, 0f), new Vector3(0.5f, 3f, 0f), new Vector3(0f, 5f, 0.5f), new Vector3(1f, 4f, 0f) },
                triangles = new[] { 0, 1, 2, 1, 3, 2 },
                boneWeights = new[]
                {
                    new BoneWeight { boneIndex0 = 0, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 0, weight0 = 0.5f, boneIndex1 = 1, weight1 = 0.5f },
                    new BoneWeight { boneIndex0 = 2, weight0 = 1f },
                    new BoneWeight { boneIndex0 = 1, weight0 = 0.25f, boneIndex1 = 2, weight1 = 0.75f },
                },
                bindposes = bones.Select(b => b.worldToLocalMatrix * root.transform.localToWorldMatrix).ToArray(),
            };
            mesh.m_BoneNameHashes = BonePaths.Select(HashedBindings.Crc32).ToArray();
            mesh.m_RootBoneNameHash = HashedBindings.Crc32("Rig/Hips");
            skin = body.AddComponent<SkinnedMeshRenderer>();
            skin.sharedMesh = mesh;
            skin.bones = bones;
            skin.rootBone = hips;
            animator = root.AddComponent<Animator>();
            animator.avatar = AvatarBuilder.BuildGenericAvatar(root, "");
            return root;
        }

        static AnimationClip Clip()
        {
            var clip = new AnimationClip();
            void Position(string path, Vector3 from, Vector3 to)
            {
                clip.SetCurve(path, typeof(Transform), "m_LocalPosition.x", AnimationCurve.Linear(0f, from.x, 1f, to.x));
                clip.SetCurve(path, typeof(Transform), "m_LocalPosition.y", AnimationCurve.Linear(0f, from.y, 1f, to.y));
                clip.SetCurve(path, typeof(Transform), "m_LocalPosition.z", AnimationCurve.Linear(0f, from.z, 1f, to.z));
            }
            Position("Rig/Hips", new Vector3(0f, 1f, 0f), new Vector3(2f, 1f, 0f));
            Position("Rig/Hips/Spine/Hand", new Vector3(0f, 2f, 0f), new Vector3(0f, 3f, 0f));
            clip.SetCurve("Rig/Hips/Spine", typeof(Transform), "localEulerAnglesRaw.z", AnimationCurve.Linear(0f, 0f, 1f, 60f));
            return clip;
        }

        static PlayableGraph Play(Animator animator, AnimationClip clip)
        {
            var graph = PlayableGraph.Create("optimize");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            AnimationPlayableOutput.Create(graph, "out", animator).SetSourcePlayable(AnimationClipPlayable.Create(graph, clip));
            return graph;
        }

        static Vector3[] WorldSkin(SkinnedMeshRenderer skin)
        {
            Time.s_FrameCount++;
            var baked = new Mesh();
            skin.BakeMesh(baked);
            var world = skin.transform.localToWorldMatrix;
            return baked.vertices.Select(v => world.MultiplyPoint3x4(v)).ToArray();
        }

        static void Near(Vector3 expected, Vector3 actual) => Assert.True((expected - actual).magnitude < 1e-4f, $"{expected} != {actual}");

        static string[] Children(Transform t) => Enumerable.Range(0, t.childCount).Select(i => t.GetChild(i).name).ToArray();

        [Fact]
        public void OptimizedRigAnimatesItsSkeletonAsTheBonesDid()
        {
            var clip = Clip();
            var plain = Rig(out var plainAnimator, out var plainSkin);
            var optimized = Rig(out var animator, out var skin);
            // Placed away from the origin: the exposed locals stay relative to the Animator.
            plain.transform.SetPositionAndRotation(new Vector3(3f, 4f, 5f), Quaternion.Euler(0f, 30f, 0f));
            optimized.transform.SetPositionAndRotation(new Vector3(3f, 4f, 5f), Quaternion.Euler(0f, 30f, 0f));
            var plainGraph = Play(plainAnimator, clip);
            var graph = Play(animator, clip);
            try
            {
                AnimatorUtility.OptimizeTransformHierarchy(optimized, new[] { "Rig/Hips/Spine/Hand" });
                Assert.False(animator.hasTransformHierarchy);
                Assert.Equal(new[] { "Body", "Hand" }, Children(optimized.transform));
                Assert.Empty(skin.bones);
                Assert.Null(skin.rootBone);
                // The skin moved to its root bone's pose.
                Near(plain.transform.Find("Rig/Hips").position, skin.transform.position);

                foreach (float time in new[] { 0.25f, 0.5f })
                {
                    plainGraph.Evaluate(time);
                    graph.Evaluate(time);
                    var expected = WorldSkin(plainSkin);
                    var actual = WorldSkin(skin);
                    for (int i = 0; i < expected.Length; i++) Near(expected[i], actual[i]);
                    Near(plain.transform.Find("Rig/Hips/Spine/Hand").position, optimized.transform.Find("Hand").position);
                    Near(plain.transform.Find("Rig/Hips").position, skin.transform.position);
                }
            }
            finally
            {
                plainGraph.Destroy();
                graph.Destroy();
                Object.DestroyImmediate(plain);
                Object.DestroyImmediate(optimized);
            }
        }

        [Fact]
        public void DeoptimizingRestoresTheBonesAtTheDefaultPoseAndTheirCurves()
        {
            var root = Rig(out var animator, out var skin);
            var graph = Play(animator, Clip());
            try
            {
                AnimatorUtility.OptimizeTransformHierarchy(root, Array.Empty<string>());
                graph.Evaluate(0.5f);
                Assert.Equal(new[] { "Body" }, Children(root.transform));

                AnimatorUtility.DeoptimizeTransformHierarchy(root);
                Assert.True(animator.hasTransformHierarchy);
                var hips = root.transform.Find("Rig/Hips");
                Assert.NotNull(hips);
                Assert.NotNull(root.transform.Find("Rig/Hips/Leg"));
                // The avatar's default pose, not the animated one.
                Near(new Vector3(0f, 1f, 0f), hips.localPosition);
                Assert.Equal(new[] { "Hips", "Spine", "Hand" }, skin.bones.Select(b => b.name).ToArray());
                Assert.Same(hips, skin.rootBone);
                Near(Vector3.zero, skin.transform.localPosition);

                // The curves animate the transforms again.
                graph.Evaluate(0.25f);
                Near(new Vector3(1.5f, 1f, 0f), hips.localPosition);
            }
            finally
            {
                graph.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        [Fact]
        public void OptimizingWithoutAvatarOrTwiceDoesNothingAndAMissingPathWarns()
        {
            var plain = new GameObject("Plain");
            new GameObject("Child").transform.SetParent(plain.transform, false);
            plain.AddComponent<Animator>();
            AnimatorUtility.OptimizeTransformHierarchy(plain, Array.Empty<string>());
            Assert.Equal(new[] { "Child" }, Children(plain.transform));
            Assert.True(plain.GetComponent<Animator>().hasTransformHierarchy);

            var root = Rig(out var animator, out _);
            var logs = new List<(LogType, string)>();
            var logger = Debug.unityLogger;
            var previous = logger.logHandler;
            logger.logHandler = new Capture(logs);
            try
            {
                AnimatorUtility.OptimizeTransformHierarchy(root, new[] { "Rig/Nope", "Rig/Hips/Leg" });
                Assert.Equal(new[] { "Body", "Leg" }, Children(root.transform));
                Assert.Contains((LogType.Warning, "Unable to optimize missing transform on GameObject 'R'"), logs);
                AnimatorUtility.OptimizeTransformHierarchy(root, new[] { "Rig/Hips/Spine" });
                Assert.Equal(new[] { "Body", "Leg" }, Children(root.transform));
                Assert.Throws<ArgumentNullException>(() => AnimatorUtility.OptimizeTransformHierarchy(null, Array.Empty<string>()));
            }
            finally
            {
                logger.logHandler = previous;
                Object.DestroyImmediate(plain);
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>A transform outside the avatar's skeleton stays under its bone with what it
        /// holds; the bone then remains (flattened under the Animator) and follows its node.</summary>
        [Fact]
        public void ObjectsAddedUnderBonesKeepTheirBone()
        {
            var root = Rig(out var animator, out _);
            var sword = new GameObject("Sword", typeof(BoxCollider)).transform;
            sword.SetParent(root.transform.Find("Rig/Hips/Spine/Hand"), false);
            new GameObject("Empty").transform.SetParent(root.transform.Find("Rig/Hips/Leg"), false);
            var graph = Play(animator, Clip());
            try
            {
                AnimatorUtility.OptimizeTransformHierarchy(root, Array.Empty<string>());
                Assert.Equal(new[] { "Body", "Hand" }, Children(root.transform));
                Assert.Same(sword, root.transform.Find("Hand/Sword"));
                graph.Evaluate(0.5f);
                Near(new Vector3(1f, 1f, 0f) + Quaternion.Euler(0f, 0f, 10f) * new Vector3(0f, 2f, 0f)
                     + Quaternion.Euler(0f, 0f, 40f) * new Vector3(0f, 2.5f, 0f), root.transform.Find("Hand").localPosition);
            }
            finally
            {
                graph.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        [Fact]
        public void PlayingAnOptimizedRigAllocatesNothing()
        {
            var root = Rig(out var animator, out _);
            var graph = Play(animator, Clip());
            try
            {
                AnimatorUtility.OptimizeTransformHierarchy(root, new[] { "Rig/Hips/Spine/Hand" });
                for (int i = 0; i < 500; i++) graph.Evaluate(0.001f);
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < 2000; i++) graph.Evaluate(0.001f);
                Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            }
            finally
            {
                graph.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        sealed class Capture : ILogHandler
        {
            readonly List<(LogType, string)> m_Logs;

            internal Capture(List<(LogType, string)> logs) => m_Logs = logs;

            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                m_Logs.Add((logType, args.Length == 0 ? format : string.Format(format, args)));

            public void LogException(Exception exception, Object context) => m_Logs.Add((LogType.Exception, exception.Message));
        }
    }
}
