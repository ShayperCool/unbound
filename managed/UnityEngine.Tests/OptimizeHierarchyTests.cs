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

        /// <summary>A prefab saved without its transform hierarchy (the Animator's
        /// m_HasTransformHierarchy: 0, as the Editor saves an optimized hierarchy): the loaded
        /// Animator animates its avatar's skeleton and poses the exposed child named as a node.
        /// Control: the same prefab saved with its hierarchy leaves that child alone, the curves
        /// finding no transform at their paths.</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void APrefabSavedWithoutItsHierarchyAnimatesItsAvatarsSkeleton(bool hasHierarchy)
        {
            var full = Rig(out var fullAnimator, out _);
            string yaml = """
                %YAML 1.1
                %TAG !u! tag:unity3d.com,2011:
                --- !u!1 &1
                GameObject:
                  m_Name: R
                  m_IsActive: 1
                  m_Component:
                  - component: {fileID: 2}
                  - component: {fileID: 3}
                --- !u!4 &2
                Transform:
                  m_GameObject: {fileID: 1}
                  m_LocalPosition: {x: 0, y: 0, z: 0}
                  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
                  m_LocalScale: {x: 1, y: 1, z: 1}
                  m_Children:
                  - {fileID: 5}
                  m_Father: {fileID: 0}
                --- !u!95 &3
                Animator:
                  m_GameObject: {fileID: 1}
                  m_Enabled: 1
                  m_Avatar: {fileID: 0}
                  m_Controller: {fileID: 0}
                  m_CullingMode: 0
                  m_HasTransformHierarchy: HIERARCHY
                --- !u!1 &4
                GameObject:
                  m_Name: Hand
                  m_IsActive: 1
                  m_Component:
                  - component: {fileID: 5}
                --- !u!4 &5
                Transform:
                  m_GameObject: {fileID: 4}
                  m_LocalPosition: {x: 9, y: 9, z: 9}
                  m_LocalRotation: {x: 0, y: 0, z: 0, w: 1}
                  m_LocalScale: {x: 1, y: 1, z: 1}
                  m_Father: {fileID: 2}
                """.Replace("HIERARCHY", hasHierarchy ? "1" : "0");
            var file = ObjectGraphBuilder.Build(UnityYaml.Parse(yaml), "optimized.prefab", null, ObjectGraphBuilder.Kind.PrefabAsset, null);
            var root = Object.Instantiate((GameObject)file.Objects[1]);
            var animator = root.GetComponent<Animator>();
            animator.avatar = fullAnimator.avatar;
            var hand = root.transform.Find("Hand");
            var graph = Play(animator, Clip());
            var fullGraph = Play(fullAnimator, Clip());
            try
            {
                Assert.Equal(hasHierarchy, animator.hasTransformHierarchy);
                graph.Evaluate(0.5f);
                fullGraph.Evaluate(0.5f);
                var expected = full.transform.Find("Rig/Hips/Spine/Hand");
                Assert.True((expected.position - new Vector3(9f, 9f, 9f)).magnitude > 1f);
                if (hasHierarchy) Near(new Vector3(9f, 9f, 9f), hand.localPosition);
                else
                {
                    Near(expected.position, hand.localPosition);
                    Assert.True(Quaternion.Angle(expected.rotation, hand.localRotation) < 0.01f);
                }
            }
            finally
            {
                graph.Destroy();
                fullGraph.Destroy();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate((GameObject)file.Objects[1]);
                Object.DestroyImmediate(full);
            }
        }

        /// <summary>A humanoid whose hierarchy is deoptimized plays on: its pose goes to the
        /// restored bones (6000.0.71f1, tests/projects/animoptimize h.deopt.played and
        /// h.rt.replayed). Control: the bone is at the avatar's default pose right after the
        /// deoptimization.</summary>
        [Fact]
        public void ADeoptimizedHumanoidPlaysOn()
        {
            var created = new List<Object>();
            var animator = HumanControllerGoalTests.Rig(HumanControllerGoalTests.TransitionController(HumanControllerGoalTests.s_B, false, false), created);
            try
            {
                var root = animator.gameObject;
                animator.Update(0f);
                animator.Update(0.1f);
                var arm = animator.GetBoneTransform(HumanBodyBones.LeftUpperArm);
                string path = HierarchyOptimizer.PathBelow(arm, root.transform);
                var posed = arm.localRotation;
                var order = root.GetComponentsInChildren<Transform>().Select(t => t.name).ToArray();
                AnimatorUtility.OptimizeTransformHierarchy(root, Array.Empty<string>());
                Assert.False(animator.hasTransformHierarchy);
                animator.Update(0.1f);
                AnimatorUtility.DeoptimizeTransformHierarchy(root);
                arm = root.transform.Find(path);
                Assert.NotNull(arm);
                // The bones come back in the order the hierarchy had, not in the human bones'.
                Assert.Equal(order, root.GetComponentsInChildren<Transform>().Select(t => t.name).ToArray());
                var rest = arm.localRotation;
                Assert.True(Quaternion.Angle(rest, posed) > 1f);
                animator.Update(0.1f);
                Assert.True(Quaternion.Angle(posed, arm.localRotation) < 0.1f, $"{arm.localRotation} != {posed}");
            }
            finally
            {
                foreach (var o in created)
                    if (o != null) Object.DestroyImmediate(o);
            }
        }

        struct HandJob : IAnimationJob
        {
            public TransformStreamHandle Hand;
            public PropertyStreamHandle Property;
            public TransformSceneHandle Scene;
            /// <summary>Write: the job turns the handle's transform; Idle: it reads nothing of
            /// it; Resolve: it resolves the handle; ReadProperty: it reads the property.</summary>
            public bool Write, Idle, Resolve, ReadProperty;
            public static bool Valid, SceneValid, ResolvedBefore, ResolvedAfter, PropertyBefore, PropertyAfter;
            public static Vector3 Read, World;

            public void ProcessRootMotion(AnimationStream stream) { }

            public void ProcessAnimation(AnimationStream stream)
            {
                Valid = Hand.IsValid(stream);
                SceneValid = Scene.IsValid(stream);
                if (!Valid) return;
                ResolvedBefore = Hand.IsResolved(stream);
                bool property = Property.IsValid(stream);
                PropertyBefore = property && Property.IsResolved(stream);
                if (property && ReadProperty) Property.GetFloat(stream);
                PropertyAfter = property && Property.IsResolved(stream);
                if (Resolve) Hand.Resolve(stream);
                if (!Idle)
                {
                    Read = Hand.GetLocalPosition(stream);
                    World = Hand.GetPosition(stream);
                }
                ResolvedAfter = Hand.IsResolved(stream);
                if (Write) Hand.SetLocalRotation(stream, Quaternion.Euler(0f, 0f, 30f));
            }
        }

        static PlayableGraph PlayJob(Animator animator, AnimationClip clip, HandJob job)
        {
            var graph = PlayableGraph.Create("handles");
            graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var playable = AnimationScriptPlayable.Create(graph, job, 1);
            playable.ConnectInput(0, AnimationClipPlayable.Create(graph, clip), 0, 1f);
            AnimationPlayableOutput.Create(graph, "out", animator).SetSourcePlayable(playable);
            return graph;
        }

        /// <summary>A stream handle is resolved at its first access (Scripting API,
        /// TransformStreamHandle.Resolve: "Handles are lazily resolved as they're accessed"):
        /// IsResolved is false in the first evaluation until the job reads it and true from then
        /// on (6000.0.71f1, tests/projects/animoptimize job.Rig.0 and job.Rig.1).</summary>
        [Fact]
        public void AStreamHandleIsResolvedAtItsFirstAccess()
        {
            var root = Rig(out var animator, out _);
            var job = new HandJob { Hand = animator.BindStreamTransform(root.transform.Find("Rig/Hips/Spine/Hand")) };
            var graph = PlayJob(animator, Clip(), job);
            try
            {
                graph.Evaluate(0f);
                Assert.False(HandJob.ResolvedBefore);
                Assert.True(HandJob.ResolvedAfter);
                graph.Evaluate(0.5f);
                Assert.True(HandJob.ResolvedBefore);
            }
            finally
            {
                graph.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>A handle the job never reads stays unresolved, also after
        /// Animator.ResolveAllStreamHandles between two evaluations of a manual graph
        /// (6000.0.71f1, tests/projects/animoptimize jobIdle.Rig, jobIdleResolveAll.Rig);
        /// TransformStreamHandle.Resolve resolves it in the job (jobResolve.Rig).</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ResolveResolvesAHandleTheJobDoesNotRead(bool resolve)
        {
            var root = Rig(out var animator, out _);
            var job = new HandJob { Hand = animator.BindStreamTransform(root.transform.Find("Rig/Hips/Spine/Hand")), Idle = true, Resolve = resolve };
            var graph = PlayJob(animator, Clip(), job);
            try
            {
                animator.ResolveAllStreamHandles();
                graph.Evaluate(0f);
                Assert.True(HandJob.Valid);
                Assert.False(HandJob.ResolvedBefore);
                Assert.Equal(resolve, HandJob.ResolvedAfter);
                animator.ResolveAllStreamHandles();
                graph.Evaluate(0.5f);
                Assert.Equal(resolve, HandJob.ResolvedBefore);
            }
            finally
            {
                graph.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>A stream property's handle is resolved at its first access too (6000.0.71f1,
        /// tests/projects/animoptimize jobProp.Rig: a custom property's handle). Control: not
        /// read, it stays unresolved (jobIdle.Rig).</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AStreamPropertysHandleIsResolvedAtItsFirstAccess(bool read)
        {
            var root = Rig(out var animator, out _);
            var job = new HandJob
            {
                Hand = animator.BindStreamTransform(root.transform.Find("Rig/Hips/Spine/Hand")),
                Property = animator.BindCustomStreamProperty("value", CustomStreamPropertyType.Float),
                Idle = true,
                ReadProperty = read,
            };
            var graph = PlayJob(animator, Clip(), job);
            try
            {
                graph.Evaluate(0f);
                Assert.False(HandJob.PropertyBefore);
                Assert.Equal(read, HandJob.PropertyAfter);
                graph.Evaluate(0.5f);
                Assert.Equal(read, HandJob.PropertyBefore);
            }
            finally
            {
                graph.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>A handle is unresolved again once the Animator's bindings change (Scripting
        /// API, TransformStreamHandle.IsResolved: a handle "can become unresolved if the animator
        /// bindings have changed or if the transform had been destroyed"; PropertyStreamHandle
        /// .IsResolved says the same of a property's): optimizing the hierarchy after the job
        /// read the handle leaves IsResolved false until the next access (6000.0.71f1,
        /// tests/projects/animoptimize jobReoptimize.Rig; the property's handle and the
        /// deoptimization are unmeasured). Control: without a change both stay resolved.</summary>
        [Fact]
        public void AHandleIsUnresolvedAgainWhenTheHierarchyChanges()
        {
            var root = Rig(out var animator, out _);
            var job = new HandJob
            {
                Hand = animator.BindStreamTransform(root.transform.Find("Rig/Hips/Spine/Hand")),
                Property = animator.BindCustomStreamProperty("value", CustomStreamPropertyType.Float),
                ReadProperty = true,
            };
            var graph = PlayJob(animator, Clip(), job);
            try
            {
                graph.Evaluate(0f);
                graph.Evaluate(0.1f);
                Assert.True(HandJob.ResolvedBefore);
                Assert.True(HandJob.PropertyBefore);
                AnimatorUtility.OptimizeTransformHierarchy(root, new[] { "Rig/Hips/Spine/Hand" });
                graph.Evaluate(0.1f);
                Assert.False(HandJob.ResolvedBefore);
                Assert.True(HandJob.ResolvedAfter);
                Assert.False(HandJob.PropertyBefore);
                Assert.True(HandJob.PropertyAfter);
                graph.Evaluate(0.1f);
                Assert.True(HandJob.ResolvedBefore);
                AnimatorUtility.DeoptimizeTransformHierarchy(root);
                graph.Evaluate(0.1f);
                Assert.False(HandJob.ResolvedBefore);
                Assert.True(HandJob.ResolvedAfter);
            }
            finally
            {
                graph.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>Destroying a handle's transform makes its scene handle invalid; the stream
        /// handle stays valid and resolved (6000.0.71f1, tests/projects/animoptimize
        /// jobDestroyed.Rig: the Scripting API says a stream handle can become unresolved if the
        /// transform had been destroyed, the Editor's stays resolved there). Control: the scene
        /// handle is valid before.</summary>
        [Fact]
        public void ADestroyedTransformsSceneHandleIsInvalidAndItsStreamHandleStaysResolved()
        {
            var root = Rig(out var animator, out _);
            var hand = root.transform.Find("Rig/Hips/Spine/Hand");
            var job = new HandJob { Hand = animator.BindStreamTransform(hand), Scene = animator.BindSceneTransform(hand) };
            var graph = PlayJob(animator, Clip(), job);
            try
            {
                graph.Evaluate(0f);
                Assert.True(HandJob.SceneValid);
                Assert.True(HandJob.ResolvedAfter);
                Object.DestroyImmediate(hand.gameObject);
                graph.Evaluate(0.5f);
                Assert.True(HandJob.Valid);
                Assert.True(HandJob.ResolvedBefore);
                Assert.False(HandJob.SceneValid);
            }
            finally
            {
                graph.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>After its transform is destroyed a stream handle goes on reading the
        /// stream's values of its path: the local values the clip gives it and the world values
        /// under the parent it had (6000.0.71f1, tests/projects/animoptimize
        /// jobDestroyedRead.Rig). Control: a second rig whose hand stays reads the same in every
        /// evaluation, and the values change between the evaluations. The parent destroyed with
        /// it is unmeasured: the handle then gives its local values.</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void ADestroyedTransformsStreamHandleReadsItsPathsValues(bool withItsParent)
        {
            var root = Rig(out var animator, out _);
            var whole = Rig(out var wholeAnimator, out _);
            root.transform.position = whole.transform.position = new Vector3(3f, 0f, -1f);
            root.transform.localScale = whole.transform.localScale = new Vector3(2f, 2f, 2f);
            var hand = root.transform.Find("Rig/Hips/Spine/Hand");
            var graph = PlayJob(animator, Clip(), new HandJob { Hand = animator.BindStreamTransform(hand) });
            var wholeGraph = PlayJob(wholeAnimator, Clip(), new HandJob { Hand = wholeAnimator.BindStreamTransform(whole.transform.Find("Rig/Hips/Spine/Hand")) });
            try
            {
                Vector3 before = default;
                for (int step = 0; step < 3; step++)
                {
                    if (step == 1) Object.DestroyImmediate((withItsParent ? hand.parent : hand).gameObject);
                    float time = step == 0 ? 0f : step == 1 ? 0.5f : 0.25f;
                    wholeGraph.Evaluate(time);
                    Vector3 local = HandJob.Read, world = HandJob.World;
                    HandJob.Read = HandJob.World = default;
                    graph.Evaluate(time);
                    Assert.True(HandJob.Valid);
                    Assert.Equal(local, HandJob.Read);
                    Assert.Equal(withItsParent && step > 0 ? local : world, HandJob.World);
                    Assert.NotEqual(local, world);
                    if (step == 0) before = local;
                    else Assert.NotEqual(before, local);
                }
            }
            finally
            {
                graph.Destroy();
                wholeGraph.Destroy();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(whole);
            }
        }

        /// <summary>A handle bound before the hierarchy is optimized goes on as its path's
        /// skeleton node: the job reads the node's stream values and its write turns the node,
        /// which the exposed transform of the path follows, as the bone of a whole hierarchy
        /// does (6000.0.71f1, tests/projects/animoptimize jobEarly.Rig). Deoptimized again, the
        /// handle is the restored bone's (unmeasured in the Editor).</summary>
        [Fact]
        public void AHandleBoundBeforeOptimizingAnimatesItsPathsNode()
        {
            var root = Rig(out var animator, out _);
            var whole = Rig(out var wholeAnimator, out _);
            var wholeHand = whole.transform.Find("Rig/Hips/Spine/Hand");
            var job = new HandJob { Hand = animator.BindStreamTransform(root.transform.Find("Rig/Hips/Spine/Hand")), Write = true };
            AnimatorUtility.OptimizeTransformHierarchy(root, new[] { "Rig/Hips/Spine/Hand" });
            var hand = root.transform.Find("Hand");
            var graph = PlayJob(animator, Clip(), job);
            var wholeGraph = PlayJob(wholeAnimator, Clip(), new HandJob { Hand = wholeAnimator.BindStreamTransform(wholeHand), Write = true });
            try
            {
                Vector3 first = default;
                foreach (float step in new[] { 0f, 0.5f })
                {
                    wholeGraph.Evaluate(step);
                    var read = HandJob.Read;
                    var world = HandJob.World;
                    HandJob.Valid = false;
                    graph.Evaluate(step);
                    Assert.True(HandJob.Valid);
                    Near(read, HandJob.Read);
                    Near(world, HandJob.World);
                    Near(wholeHand.position, hand.localPosition);
                    Assert.True(Quaternion.Angle(wholeHand.rotation, hand.localRotation) < 0.01f, $"{hand.localRotation} != {wholeHand.rotation}");
                    if (step == 0f) first = hand.localPosition;
                }
                // The take moved the node, and the exposed transform with it.
                Assert.True((hand.localPosition - first).magnitude > 0.1f, $"{hand.localPosition} stayed at {first}");

                AnimatorUtility.DeoptimizeTransformHierarchy(root);
                var restored = root.transform.Find("Rig/Hips/Spine/Hand");
                HandJob.Valid = false;
                graph.Evaluate(0.25f);
                Assert.True(HandJob.Valid);
                Near(restored.localPosition, HandJob.Read);
                Assert.True(Quaternion.Angle(Quaternion.Euler(0f, 0f, 30f), restored.localRotation) < 0.01f);
            }
            finally
            {
                graph.Destroy();
                wholeGraph.Destroy();
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(whole);
            }
        }

        /// <summary>An exposed top node has its node's path, so its handle animates the node: a
        /// job's write turns the skeleton below it, and the exposed transform follows its node
        /// (6000.0.71f1, tests/projects/animoptimize jobTop.Rig). Control: without the write
        /// the exposed hand is where the take puts it.</summary>
        [Fact]
        public void AnExposedTopNodesHandleAnimatesTheNode()
        {
            GameObject Optimized(bool write, out PlayableGraph graph)
            {
                var root = Rig(out var animator, out _);
                AnimatorUtility.OptimizeTransformHierarchy(root, new[] { "Rig", "Rig/Hips/Spine/Hand" });
                graph = PlayJob(animator, Clip(), new HandJob { Hand = animator.BindStreamTransform(root.transform.Find("Rig")), Write = write });
                return root;
            }
            var turned = Optimized(true, out var turnedGraph);
            var plain = Optimized(false, out var plainGraph);
            try
            {
                var turn = Quaternion.Euler(0f, 0f, 30f);
                foreach (float step in new[] { 0f, 0.5f })
                {
                    plainGraph.Evaluate(step);
                    turnedGraph.Evaluate(step);
                    Near(Vector3.zero, HandJob.Read);
                    Assert.True(Quaternion.Angle(Quaternion.identity, plain.transform.Find("Rig").localRotation) < 0.01f);
                    Assert.True(Quaternion.Angle(turn, turned.transform.Find("Rig").localRotation) < 0.01f);
                    var hand = plain.transform.Find("Hand");
                    Assert.True(hand.localPosition.magnitude > 1f);
                    Near(turn * hand.localPosition, turned.transform.Find("Hand").localPosition);
                    Assert.True(Quaternion.Angle(turn * hand.localRotation, turned.transform.Find("Hand").localRotation) < 0.01f);
                }
            }
            finally
            {
                turnedGraph.Destroy();
                plainGraph.Destroy();
                Object.DestroyImmediate(turned);
                Object.DestroyImmediate(plain);
            }
        }

        /// <summary>An exposed transform of an optimized hierarchy that is bound to the stream is
        /// the stream's own: it keeps the pose it was bound at, with a job's writes, while the
        /// skeleton's node moves (6000.0.71f1, tests/projects/animoptimize job.RigExposed,
        /// jobRead.RigExposed). Control: unbound, it follows the node (jobNone.RigExposed), and
        /// it does again once the stream handles are unbound (unmeasured).</summary>
        [Theory]
        [InlineData(true, true)]
        [InlineData(true, false)]
        [InlineData(false, false)]
        public void AnExposedTransformBoundToTheStreamIsTheStreamsOwn(bool bound, bool write)
        {
            var root = Rig(out var animator, out _);
            AnimatorUtility.OptimizeTransformHierarchy(root, new[] { "Rig/Hips/Spine/Hand" });
            var hand = root.transform.Find("Hand");
            var job = new HandJob { Write = write };
            if (bound) job.Hand = animator.BindStreamTransform(hand);
            var graph = bound ? PlayJob(animator, Clip(), job) : Play(animator, Clip());
            try
            {
                graph.Evaluate(0f);
                var first = hand.localPosition;
                graph.Evaluate(0.5f);
                if (bound)
                {
                    Near(first, hand.localPosition);
                    Near(first, HandJob.Read);
                    if (write) Assert.True(Quaternion.Angle(Quaternion.Euler(0f, 0f, 30f), hand.localRotation) < 0.01f);
                    animator.UnbindAllStreamHandles();
                    graph.Evaluate(0.25f);
                }
                Assert.True((hand.localPosition - first).magnitude > 0.1f, $"{hand.localPosition} stayed at {first}");
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

        /// <summary>A copy of an optimized instance is optimized too and skins from its own skeleton.</summary>
        [Fact]
        public void ACopyOfAnOptimizedInstanceAnimatesItsOwnSkeleton()
        {
            var clip = Clip();
            var original = Rig(out var animator, out _);
            AnimatorUtility.OptimizeTransformHierarchy(original, new[] { "Rig/Hips/Spine/Hand" });
            var copy = Object.Instantiate(original);
            var copyAnimator = copy.GetComponent<Animator>();
            var copySkin = copy.GetComponentInChildren<SkinnedMeshRenderer>();
            var plain = Rig(out var plainAnimator, out var plainSkin);
            var graph = Play(copyAnimator, clip);
            var plainGraph = Play(plainAnimator, clip);
            try
            {
                Assert.False(copyAnimator.hasTransformHierarchy);
                Assert.Equal(new[] { "Body", "Hand" }, Children(copy.transform));
                Assert.Empty(copySkin.bones);
                graph.Evaluate(0.5f);
                plainGraph.Evaluate(0.5f);
                var expected = WorldSkin(plainSkin);
                var actual = WorldSkin(copySkin);
                for (int i = 0; i < expected.Length; i++) Near(expected[i], actual[i]);
                Near(plain.transform.Find("Rig/Hips/Spine/Hand").position, copy.transform.Find("Hand").position);
            }
            finally
            {
                graph.Destroy();
                plainGraph.Destroy();
                Object.DestroyImmediate(original);
                Object.DestroyImmediate(copy);
                Object.DestroyImmediate(plain);
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
