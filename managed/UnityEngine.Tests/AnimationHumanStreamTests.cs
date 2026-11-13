using System;
using System.Collections.Generic;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// AnimationHumanStream (Scripting API 6000.0: AnimationStream.AsHuman "gets the same stream,
    /// but as an AnimationHumanStream", throwing for a generic rig; MuscleHandle and its 95 muscles)
    /// on a humanoid built at run time. The Editor's values are recorded by tests/projects/humanoid
    /// stream.*; these tests keep the mechanisms from drifting on another rig: the pose a job writes
    /// reaches the Animator, world values are in the avatar root's frame (its scale included), a
    /// solve moves a hand onto its goal, the IK values stay in the job's stream between updates, a
    /// stream outside a job is invalid.
    /// </summary>
    [Collection("Animation")]
    public sealed class AnimationHumanStreamTests
    {
        internal delegate void JobBody(AnimationStream stream, int frame);

        struct HumanJob : IAnimationJob
        {
            internal static JobBody Body;
            internal static int Frame;

            public void ProcessRootMotion(AnimationStream stream) { }

            public void ProcessAnimation(AnimationStream stream) => Body?.Invoke(stream, Frame);
        }

        static readonly (string Name, string Parent, Vector3 Position)[] Bones =
        {
            ("Hips", null, new Vector3(0f, 1f, 0f)),
            ("Spine", "Hips", new Vector3(0f, 0.12f, 0f)),
            ("Chest", "Spine", new Vector3(0f, 0.15f, 0f)),
            ("Neck", "Chest", new Vector3(0f, 0.22f, 0f)),
            ("Head", "Neck", new Vector3(0f, 0.1f, 0f)),
            ("LeftUpperArm", "Chest", new Vector3(-0.18f, 0.18f, 0f)),
            ("LeftLowerArm", "LeftUpperArm", new Vector3(-0.28f, 0f, 0f)),
            ("LeftHand", "LeftLowerArm", new Vector3(-0.25f, 0f, 0f)),
            ("RightUpperArm", "Chest", new Vector3(0.18f, 0.18f, 0f)),
            ("RightLowerArm", "RightUpperArm", new Vector3(0.28f, 0f, 0f)),
            ("RightHand", "RightLowerArm", new Vector3(0.25f, 0f, 0f)),
            ("LeftUpperLeg", "Hips", new Vector3(-0.1f, -0.05f, 0f)),
            ("LeftLowerLeg", "LeftUpperLeg", new Vector3(0f, -0.43f, 0.01f)),
            ("LeftFoot", "LeftLowerLeg", new Vector3(0f, -0.42f, -0.01f)),
            ("RightUpperLeg", "Hips", new Vector3(0.1f, -0.05f, 0f)),
            ("RightLowerLeg", "RightUpperLeg", new Vector3(0f, -0.43f, 0.01f)),
            ("RightFoot", "RightLowerLeg", new Vector3(0f, -0.42f, -0.01f)),
        };

        /// <summary>A humanoid in a T-pose: the required bones plus chest and neck, an avatar built
        /// from it with AvatarBuilder (with translation DoF when asked).</summary>
        internal static GameObject Humanoid(out Animator animator, Vector3 position = default, float scale = 1f, bool translationDoF = false)
        {
            var root = new GameObject("Human");
            root.transform.position = position;
            root.transform.localScale = new Vector3(scale, scale, scale);
            var made = new Dictionary<string, Transform>();
            var skeleton = new List<SkeletonBone> { new SkeletonBone { name = "Human", rotation = Quaternion.identity, scale = Vector3.one } };
            var human = new List<HumanBone>();
            foreach (var (name, parent, local) in Bones)
            {
                var t = new GameObject(name).transform;
                t.SetParent(parent == null ? root.transform : made[parent], false);
                t.localPosition = local;
                made[name] = t;
                skeleton.Add(new SkeletonBone { name = name, position = local, rotation = Quaternion.identity, scale = Vector3.one });
                human.Add(new HumanBone { boneName = name, humanName = name, limit = new HumanLimit { useDefaultValues = true } });
            }
            var description = new HumanDescription
            {
                human = human.ToArray(), skeleton = skeleton.ToArray(),
                upperArmTwist = 0.5f, lowerArmTwist = 0.5f, upperLegTwist = 0.5f, lowerLegTwist = 0.5f,
                armStretch = 0.05f, legStretch = 0.05f, feetSpacing = 0f, hasTranslationDoF = translationDoF,
            };
            animator = root.AddComponent<Animator>();
            animator.avatar = AvatarBuilder.BuildHumanAvatar(root, description);
            Assert.True(animator.isHuman);
            return root;
        }

        internal static PlayableGraph Graph(Animator animator, JobBody body)
        {
            HumanJob.Body = body;
            HumanJob.Frame = 0;
            var g = PlayableGraph.Create("human-stream");
            g.SetTimeUpdateMode(DirectorUpdateMode.Manual);
            var job = AnimationScriptPlayable.Create(g, new HumanJob());
            AnimationPlayableOutput.Create(g, "out", animator).SetSourcePlayable(job);
            g.Play();
            return g;
        }

        internal static void Run(PlayableGraph g, int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                HumanJob.Frame = i;
                g.Evaluate(1f / 30f);
            }
        }

        [Fact]
        public void MuscleHandlesListEveryMusclePartByPart()
        {
            Assert.Equal(95, MuscleHandle.muscleHandleCount);
            var handles = new MuscleHandle[MuscleHandle.muscleHandleCount];
            MuscleHandle.GetMuscleHandles(handles);
            Assert.Equal(HumanPartDof.Body, handles[0].humanPartDof);
            Assert.Equal("Spine Front-Back", handles[0].name);
            Assert.Equal((HumanPartDof.Head, 0, "Neck Nod Down-Up"), (handles[9].humanPartDof, handles[9].dof, handles[9].name));
            Assert.Equal((HumanPartDof.LeftLeg, 0), (handles[21].humanPartDof, handles[21].dof));
            Assert.Equal((HumanPartDof.RightLittle, 3, "RightHand.Little.3 Stretched"), (handles[94].humanPartDof, handles[94].dof, handles[94].name));
            for (int i = 0; i < 55; i++) Assert.Equal(HumanTrait.MuscleName[i], handles[i].name);
            Assert.Equal("Left Arm Down-Up", new MuscleHandle(HumanPartDof.LeftArm, ArmDof.ArmDownUp).name);

            // A shorter array is left as it is; the constructors reject a part of another kind.
            var shorter = new MuscleHandle[10];
            shorter[3] = new MuscleHandle(HeadDof.JawLeftRight);
            MuscleHandle.GetMuscleHandles(shorter);
            Assert.Equal(HumanPartDof.Head, shorter[3].humanPartDof);
            Assert.Throws<ArgumentNullException>(() => MuscleHandle.GetMuscleHandles(null));
            Assert.Throws<InvalidOperationException>(() => new MuscleHandle(HumanPartDof.LeftArm, LegDof.LegCloseOpen));
            Assert.Throws<InvalidOperationException>(() => new MuscleHandle(HumanPartDof.Body, ArmDof.ArmDownUp));
            Assert.Throws<InvalidOperationException>(() => new MuscleHandle(HumanPartDof.Head, FingerDof.ProximalDownUp));
        }

        [Fact]
        public void AsHumanThrowsForAGenericRigAndADefaultStreamIsInvalid()
        {
            var root = new GameObject("Generic");
            var animator = root.AddComponent<Animator>();
            Exception thrown = null;
            var g = Graph(animator, (stream, _) => thrown = Record.Exception(() => stream.AsHuman()));
            try
            {
                Run(g, 1);
                Assert.IsType<InvalidOperationException>(thrown);
                Assert.Equal("Cannot create an AnimationHumanStream for a generic rig.", thrown.Message);
                var none = default(AnimationHumanStream);
                Assert.False(none.isValid);
                Assert.Throws<InvalidOperationException>(() => none.humanScale);
                Assert.Throws<InvalidOperationException>(() => none.SolveIK());
            }
            finally
            {
                g.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        [Fact]
        public void TheMusclesAndBodyAJobWritesReachTheAnimatorsPose()
        {
            var root = Humanoid(out var animator, new Vector3(1f, 0f, 2f), 2f);
            var spine = new MuscleHandle(BodyDof.SpineFrontBack);
            float read = float.NaN;
            Vector3 world = default, local = default;
            var g = Graph(animator, (stream, frame) =>
            {
                var h = stream.AsHuman();
                h.SetMuscle(spine, 0.5f);
                read = h.GetMuscle(spine);
                h.bodyLocalPosition += new Vector3(0f, 0.05f, 0f);
                local = h.bodyLocalPosition;
                world = h.bodyPosition;
            });
            try
            {
                Run(g, 2);
                Assert.Equal(0.5f, read);
                // World values: the avatar root's frame, its scale included, times the human scale.
                var expected = root.transform.TransformPoint(local * animator.humanScale);
                Assert.True((world - expected).magnitude < 1e-4f, $"{world} vs {expected}");
                var handler = new HumanPoseHandler(animator.avatar, root.transform);
                var pose = new HumanPose();
                handler.GetHumanPose(ref pose);
                handler.Dispose();
                Assert.InRange(pose.muscles[0], 0.49f, 0.51f);
                Assert.True((animator.bodyPosition - world).magnitude < 1e-3f, $"{animator.bodyPosition} vs {world}");
            }
            finally
            {
                g.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        [Fact]
        public void SolveIKMovesAHandOntoItsGoal()
        {
            var root = Humanoid(out var animator);
            Vector3 goal = default, reached = default, before = default;
            var g = Graph(animator, (stream, frame) =>
            {
                var h = stream.AsHuman();
                before = h.GetGoalPositionFromPose(AvatarIKGoal.LeftHand);
                goal = before + new Vector3(0.03f, -0.04f, 0.05f);
                h.SetGoalPosition(AvatarIKGoal.LeftHand, goal);
                h.SetGoalWeightPosition(AvatarIKGoal.LeftHand, 1f);
                h.SolveIK();
                reached = h.GetGoalPositionFromPose(AvatarIKGoal.LeftHand);
            });
            try
            {
                Run(g, 1);
                Assert.True((before - goal).magnitude > 0.05f);
                Assert.True((reached - goal).magnitude < 0.01f, $"{reached} vs {goal}");
                Assert.True((animator.GetBoneTransform(HumanBodyBones.LeftHand).position - goal).magnitude < 0.01f);
            }
            finally
            {
                g.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        /// <summary>A solve after the job read the pose back works on the skeleton re-based under
        /// the root without its scale: on a root scaled by 2 the body ends half as far from the
        /// root as before the solve; on an unscaled root (the control) it stays where it was. The
        /// Editor displaces it the same way on the probe's root at the origin scaled by 100 with a
        /// goal set (tests/projects/humanoid bone.solveTwice); a root moved from the origin and a
        /// solve without goals, as here, are Unbound's rule, unmeasured.</summary>
        [Theory]
        [InlineData(1f)]
        [InlineData(2f)]
        public void ASolveAfterThePoseIsReadBackWorksOnTheRebasedSkeleton(float scale)
        {
            var root = Humanoid(out var animator, new Vector3(1f, 0f, 2f), scale);
            Vector3 between = default, after = default;
            var g = Graph(animator, (stream, frame) =>
            {
                var h = stream.AsHuman();
                // No clip plays: the body starts on the root; the job lifts it first.
                h.bodyPosition += new Vector3(0f, 1f, 0f);
                h.SolveIK();
                between = h.bodyPosition;
                h.SolveIK();
                after = h.bodyPosition;
            });
            try
            {
                Run(g, 1);
                var origin = root.transform.position;
                var expected = scale == 1f ? between : origin + (between - origin) / scale;
                Assert.True((between - origin).magnitude > 0.5f, $"{between}");
                Assert.True((after - expected).magnitude < 1e-3f, $"{after} vs {expected}");
            }
            finally
            {
                g.Destroy();
                Object.DestroyImmediate(root);
            }
        }

        [Fact]
        public void IKValuesStayInTheJobsStreamBetweenUpdates()
        {
            var root = Humanoid(out var animator);
            var seen = new List<float>();
            var g = Graph(animator, (stream, frame) =>
            {
                var h = stream.AsHuman();
                seen.Add(h.GetGoalWeightPosition(AvatarIKGoal.RightFoot));
                if (frame == 0) h.SetGoalWeightPosition(AvatarIKGoal.RightFoot, 0.75f);
            });
            try
            {
                Run(g, 3);
                Assert.Equal(new[] { 0f, 0.75f, 0.75f }, seen);
            }
            finally
            {
                g.Destroy();
                Object.DestroyImmediate(root);
            }
        }
    }
}
