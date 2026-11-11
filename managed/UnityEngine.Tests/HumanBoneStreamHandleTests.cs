using System;
using System.Collections.Generic;
using UnityEngine.Animations;
using UnityEngine.Playables;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Stream handles of a humanoid's bones in an animation job (6000.0.71f1 Editor,
    /// tests/projects/humanoid bone.*). The Scripting API says of TransformStreamHandle that it
    /// is the "position, rotation and scale of an object in the AnimationStream"; for a bone of
    /// a humanoid that object is the bone of the stream's human pose: a handle reads the bone the
    /// pose gives, a rotation written through it (and the hips' position) reaches the final pose,
    /// and a position or scale written on a bone below the hips stays in the stream.
    /// </summary>
    [Collection("Animation")]
    public sealed class HumanBoneStreamHandleTests : IDisposable
    {
        readonly List<Object> m_Created = new List<Object>();
        PlayableGraph m_Graph;

        public void Dispose()
        {
            if (m_Graph.IsValid()) m_Graph.Destroy();
            foreach (var o in m_Created)
                if (o != null) Object.DestroyImmediate(o);
        }

        Animator Human(float scale = 1f)
        {
            var root = AnimationHumanStreamTests.Humanoid(out var animator, new Vector3(1f, 0f, 2f), scale);
            m_Created.Add(root);
            m_Created.Add(animator.avatar);
            return animator;
        }

        void Evaluate(Animator animator, AnimationHumanStreamTests.JobBody body)
        {
            if (m_Graph.IsValid()) m_Graph.Destroy();
            m_Graph = AnimationHumanStreamTests.Graph(animator, body);
            AnimationHumanStreamTests.Run(m_Graph, 1);
        }

        /// <summary>Makes the job's stream a changed one, so that its pose is written whether or
        /// not the job goes on to change a bone: the control evaluations and the ones with a
        /// write then differ by the write alone.</summary>
        static void Touch(AnimationStream stream) =>
            stream.AsHuman().SetMuscle(new MuscleHandle(BodyDof.SpineFrontBack), 0.1f);

        static void Near(Vector3 expected, Vector3 actual, float tolerance = 1e-4f) =>
            Assert.True((expected - actual).magnitude <= tolerance, $"expected {expected:F5}, got {actual:F5}");

        static void Near(Quaternion expected, Quaternion actual, float degrees = 0.05f) =>
            Assert.True(Quaternion.Angle(expected, actual) <= degrees, $"expected {expected:F5}, got {actual:F5} ({Quaternion.Angle(expected, actual)} degrees)");

        /// <summary>A handle reads the bone of the stream's pose, not the transform as the last
        /// update left it: after a muscle is set in the job the read has turned (control: the
        /// read before), and it is what the transform has once the update is done.</summary>
        [Fact]
        public void AHandleReadsTheBoneOfTheStreamsPose()
        {
            var animator = Human();
            var arm = animator.transform.Find("Hips/Spine/Chest/LeftUpperArm");
            var hips = animator.transform.Find("Hips");
            var armHandle = animator.BindStreamTransform(arm);
            var hipsHandle = animator.BindStreamTransform(hips);
            Quaternion before = default, after = default, world = default;
            Vector3 hipsLocal = default, hipsWorld = default, armLocal = default;
            Evaluate(animator, (stream, _) =>
            {
                before = armHandle.GetLocalRotation(stream);
                stream.AsHuman().SetMuscle(new MuscleHandle(HumanPartDof.LeftArm, ArmDof.ArmDownUp), 0.6f);
                after = armHandle.GetLocalRotation(stream);
                world = armHandle.GetRotation(stream);
                armLocal = armHandle.GetLocalPosition(stream);
                hipsLocal = hipsHandle.GetLocalPosition(stream);
                hipsWorld = hipsHandle.GetPosition(stream);
            });
            Assert.True(Quaternion.Angle(before, after) > 10f);
            Near(arm.localRotation, after);
            Near(arm.rotation, world);
            Near(arm.localPosition, armLocal);
            Near(hips.localPosition, hipsLocal);
            Near(hips.position, hipsWorld);
        }

        /// <summary>The combined accessors and the matrices give what the single ones give.</summary>
        [Fact]
        public void TheCombinedAccessorsAgreeWithTheSingleOnes()
        {
            var animator = Human(scale: 2f);
            var arm = animator.transform.Find("Hips/Spine/Chest/LeftUpperArm");
            var handle = animator.BindStreamTransform(arm);
            Evaluate(animator, (stream, _) =>
            {
                stream.AsHuman().SetMuscle(new MuscleHandle(HumanPartDof.LeftArm, ArmDof.ArmDownUp), 0.6f);
                handle.GetLocalTRS(stream, out var p, out var r, out var s);
                Near(handle.GetLocalPosition(stream), p);
                Near(handle.GetLocalRotation(stream), r);
                Near(handle.GetLocalScale(stream), s);
                handle.GetGlobalTR(stream, out var wp, out var wr);
                Near(handle.GetPosition(stream), wp);
                Near(handle.GetRotation(stream), wr);
                Near((Vector3)handle.GetLocalToParentMatrix(stream).GetColumn(3), p);
                Near((Vector3)handle.GetLocalToWorldMatrix(stream).GetColumn(3), wp);

                var turned = r * Quaternion.Euler(0f, 0f, 25f);
                handle.SetLocalTRS(stream, p, turned, s, false);
                Near(turned, handle.GetLocalRotation(stream));
                var world = Quaternion.Euler(10f, 20f, 30f);
                handle.SetGlobalTR(stream, wp, world, false);
                Near(world, handle.GetRotation(stream));
                Near(wp, handle.GetPosition(stream), 1e-3f);
            });
            // The final pose spreads a limb's twist over its bones, so the bone ends near, not
            // at, the rotation written (as with the Editor's lower arm, bone.rotateLocal).
            Near(Quaternion.Euler(10f, 20f, 30f), arm.rotation, 10f);
        }

        /// <summary>A handle is a bone of the avatar the Animator has when the job runs: one bound
        /// before the avatar was assigned, and one bound for an avatar that was then replaced by
        /// another of the same hierarchy with a bone fewer, read the pose's bone like a handle
        /// bound afterwards.</summary>
        [Fact]
        public void AHandleFollowsTheAnimatorsAvatar()
        {
            var animator = Human();
            var head = animator.transform.Find("Hips/Spine/Chest/Neck/Head");
            var foot = animator.transform.Find("Hips/RightUpperLeg/RightLowerLeg/RightFoot");
            var full = animator.avatar;

            animator.avatar = null;
            var early = animator.BindStreamTransform(head);
            animator.avatar = full;
            var earlyFoot = animator.BindStreamTransform(foot);
            Quaternion read = default;
            Evaluate(animator, (stream, _) =>
            {
                stream.AsHuman().SetMuscle(new MuscleHandle(HeadDof.HeadFrontBack), 0.7f);
                read = early.GetLocalRotation(stream);
            });
            Assert.True(Quaternion.Angle(Quaternion.identity, head.localRotation) > 5f);
            Near(head.localRotation, read);

            // Another avatar of the same hierarchy without the neck: the nodes are numbered anew.
            var description = full.humanDescription;
            var bones = new List<HumanBone>(description.human);
            bones.RemoveAll(b => b.humanName == "Neck");
            description.human = bones.ToArray();
            var fewer = AvatarBuilder.BuildHumanAvatar(animator.gameObject, description);
            m_Created.Add(fewer);
            Assert.True(fewer.isHuman);
            animator.avatar = fewer;
            Vector3 footRead = default;
            Evaluate(animator, (stream, _) =>
            {
                stream.AsHuman().SetMuscle(new MuscleHandle(HeadDof.HeadFrontBack), -0.7f);
                read = early.GetLocalRotation(stream);
                footRead = earlyFoot.GetPosition(stream);
            });
            Near(head.localRotation, read);
            Near(foot.position, footRead, 1e-3f);
        }

        /// <summary>After the job read the pose back, the bones are still the ones it changed: their
        /// local values stay, and a further bone write keeps the body where it was, also on a
        /// scaled root (control: the same job without the further write). World positions are
        /// then read under the root without its scale (tests/projects/humanoid
        /// bone.rotateThenHuman): on this rig, half as far from the root as before.</summary>
        [Fact]
        public void BonesStayAfterThePoseIsReadBack()
        {
            var animator = Human(scale: 2f);
            var forearm = animator.transform.Find("Hips/Spine/Chest/LeftUpperArm/LeftLowerArm");
            var head = animator.transform.Find("Hips/Spine/Chest/Neck/Head");
            var hips = animator.transform.Find("Hips");
            var forearmHandle = animator.BindStreamTransform(forearm);
            var headHandle = animator.BindStreamTransform(head);
            var hipsHandle = animator.BindStreamTransform(hips);
            void Turn(AnimationStream stream)
            {
                Touch(stream);
                forearmHandle.SetLocalRotation(stream, forearmHandle.GetLocalRotation(stream) * Quaternion.Euler(0f, 0f, 40f));
            }

            Evaluate(animator, (stream, _) => Turn(stream));
            Vector3 controlHips = hips.position, controlHead = head.position;
            var controlForearm = forearm.localRotation;

            Vector3 scaleBefore = default, scaleAfter = default, headBefore = default, headAfter = default;
            Evaluate(animator, (stream, _) =>
            {
                Turn(stream);
                scaleBefore = hipsHandle.GetLocalScale(stream);
                headBefore = headHandle.GetPosition(stream);
                stream.AsHuman().GetMuscle(new MuscleHandle(BodyDof.SpineFrontBack));
                var bodyRead = stream.AsHuman().bodyPosition;
                scaleAfter = hipsHandle.GetLocalScale(stream);
                headAfter = headHandle.GetPosition(stream);
                headHandle.SetRotation(stream, headHandle.GetRotation(stream));
            });
            Near(scaleBefore, scaleAfter);
            var root = animator.transform.position;
            Assert.True((headBefore - root).magnitude > 1f);
            Near(root + (headBefore - root) / 2f, headAfter, 1e-3f);
            Near(controlHips, hips.position, 1e-3f);
            Near(controlHead, head.position, 1e-3f);
            Near(controlForearm, forearm.localRotation, 0.5f);

            // The hips written back as read after the read-back, in the world and locally, stay too.
            foreach (int way in new[] { 0, 1, 2 })
            {
                Evaluate(animator, (stream, _) =>
                {
                    Turn(stream);
                    var body = stream.AsHuman().bodyPosition;
                    if (way == 0) hipsHandle.SetPosition(stream, hipsHandle.GetPosition(stream));
                    else if (way == 1)
                    {
                        hipsHandle.GetGlobalTR(stream, out var p, out var r);
                        hipsHandle.SetGlobalTR(stream, p, r, false);
                    }
                    else hipsHandle.SetLocalPosition(stream, hipsHandle.GetLocalPosition(stream));
                });
                Near(controlHips, hips.position, 1e-3f);
                Near(controlHead, head.position, 1e-3f);
            }
        }

        /// <summary>A zero rotation written on the hips is the identity in the frame it is written
        /// in: locally, the hips' local rotation, whatever the root's own turn.</summary>
        [Fact]
        public void AZeroRotationOnTheHipsIsTheIdentityWhereItIsWritten()
        {
            var animator = Human();
            animator.transform.rotation = Quaternion.Euler(0f, 30f, 0f);
            var handle = animator.BindStreamTransform(animator.transform.Find("Hips"));
            Quaternion local = default, world = default;
            Evaluate(animator, (stream, _) =>
            {
                Touch(stream);
                handle.SetLocalRotation(stream, default);
                local = handle.GetLocalRotation(stream);
                handle.SetRotation(stream, default);
                world = handle.GetRotation(stream);
            });
            Near(Quaternion.identity, local);
            Near(Quaternion.identity, world);
        }

        /// <summary>A rotation that is not of unit length is taken at unit length, a zero one as
        /// the identity, and one that is not a number is not taken.</summary>
        [Fact]
        public void ARotationIsTakenAtUnitLength()
        {
            var unit = Quaternion.Euler(0f, 0f, 30f);
            Animator animator = null;

            // Each on a rig of its own: an update starts from the pose the last one left.
            Vector3 Final(Quaternion? written)
            {
                animator = Human();
                var forearm = animator.transform.Find("Hips/Spine/Chest/LeftUpperArm/LeftLowerArm");
                var handle = animator.BindStreamTransform(forearm);
                Evaluate(animator, (stream, _) =>
                {
                    Touch(stream);
                    if (written.HasValue) handle.SetLocalRotation(stream, written.Value);
                });
                return animator.transform.Find("Hips/Spine/Chest/LeftUpperArm/LeftLowerArm/LeftHand").position;
            }

            var plain = Final(null);
            var turned = Final(unit);
            Assert.True((plain - turned).magnitude > 0.05f);
            Near(turned, Final(new Quaternion(unit.x * 2.5f, unit.y * 2.5f, unit.z * 2.5f, unit.w * 2.5f)), 1e-3f);
            Near(Final(Quaternion.identity), Final(new Quaternion(0f, 0f, 0f, 0f)), 1e-3f);
            Near(plain, Final(new Quaternion(float.NaN, 0f, 0f, 1f)), 1e-3f);
            Assert.False(float.IsNaN(animator.transform.Find("Hips").position.x));
        }

        /// <summary>A rotation written through a bone's handle stays in the final pose, local and
        /// world; the job reads it back at once. Control: without the write the bone is elsewhere.</summary>
        [Fact]
        public void ARotationWrittenThroughAHandleReachesTheFinalPose()
        {
            var animator = Human();
            var forearm = animator.transform.Find("Hips/Spine/Chest/LeftUpperArm/LeftLowerArm");
            var head = animator.transform.Find("Hips/Spine/Chest/Neck/Head");
            var forearmHandle = animator.BindStreamTransform(forearm);
            var headHandle = animator.BindStreamTransform(head);
            Evaluate(animator, (stream, _) => Touch(stream));
            var restForearm = forearm.localRotation;
            var restHead = head.rotation;

            var turned = Quaternion.Euler(0f, 30f, 0f);
            Quaternion readBack = default;
            Evaluate(animator, (stream, _) =>
            {
                Touch(stream);
                forearmHandle.SetLocalRotation(stream, forearmHandle.GetLocalRotation(stream) * Quaternion.Euler(0f, 0f, 40f));
                readBack = forearmHandle.GetLocalRotation(stream);
                headHandle.SetRotation(stream, turned);
            });
            Near(restForearm * Quaternion.Euler(0f, 0f, 40f), readBack);
            Assert.True(Quaternion.Angle(restForearm, forearm.localRotation) > 20f);
            Assert.True(Quaternion.Angle(restHead, head.rotation) > 20f);
            Near(turned, head.rotation, 1f);
        }

        /// <summary>A position or a scale written on a bone below the hips is read back by the job,
        /// and the bones below follow it in the stream, but the final pose is the one without
        /// the write.</summary>
        [Fact]
        public void APositionOrScaleBelowTheHipsStaysInTheStream()
        {
            var animator = Human();
            var forearm = animator.transform.Find("Hips/Spine/Chest/LeftUpperArm/LeftLowerArm");
            var hand = animator.transform.Find("Hips/Spine/Chest/LeftUpperArm/LeftLowerArm/LeftHand");
            var spine = animator.transform.Find("Hips/Spine");
            var forearmHandle = animator.BindStreamTransform(forearm);
            var handHandle = animator.BindStreamTransform(hand);
            var spineHandle = animator.BindStreamTransform(spine);
            Evaluate(animator, (stream, _) => Touch(stream));
            Vector3 restForearm = forearm.position, restHand = hand.position, restSpineScale = spine.localScale;

            Vector3 movedForearm = default, movedHand = default, readScale = default;
            Evaluate(animator, (stream, _) =>
            {
                Touch(stream);
                var handBefore = handHandle.GetPosition(stream);
                forearmHandle.SetPosition(stream, forearmHandle.GetPosition(stream) + new Vector3(0.1f, 0.2f, 0f));
                movedForearm = forearmHandle.GetPosition(stream);
                movedHand = handHandle.GetPosition(stream) - handBefore;
                spineHandle.SetLocalScale(stream, new Vector3(1.5f, 1.5f, 1.5f));
                readScale = spineHandle.GetLocalScale(stream);
            });
            Near(restForearm + new Vector3(0.1f, 0.2f, 0f), movedForearm);
            Near(new Vector3(0.1f, 0.2f, 0f), movedHand);
            Near(new Vector3(1.5f, 1.5f, 1.5f), readScale);
            Near(restForearm, forearm.position);
            Near(restHand, hand.position);
            Near(restSpineScale, spine.localScale);
        }

        /// <summary>The hips moved through their handle move the body: the whole pose follows.</summary>
        [Fact]
        public void TheHipsMovedThroughTheirHandleMoveTheBody()
        {
            var animator = Human();
            var hips = animator.transform.Find("Hips");
            var head = animator.transform.Find("Hips/Spine/Chest/Neck/Head");
            var handle = animator.BindStreamTransform(hips);
            Evaluate(animator, (stream, _) => Touch(stream));
            Vector3 restHips = hips.position, restHead = head.position, restBody = animator.bodyPosition;

            var by = new Vector3(0.1f, 0.2f, -0.05f);
            Evaluate(animator, (stream, _) =>
            {
                Touch(stream);
                handle.SetPosition(stream, handle.GetPosition(stream) + by);
            });
            Near(restHips + by, hips.position, 1e-3f);
            Near(restHead + by, head.position, 1e-3f);
            Near(restBody + by, animator.bodyPosition, 1e-3f);
        }

        /// <summary>A root scale the avatar does not hold itself is counted once: the handles read
        /// where the transforms are, the hips with a scale of one (a control beside the probe's
        /// rig, whose avatar holds its root's scale of 100 and whose handles read it twice below
        /// the hips, bone.read).</summary>
        [Fact]
        public void ARootScaleOutsideTheAvatarIsCountedOnce()
        {
            var animator = Human(scale: 2f);
            var hips = animator.transform.Find("Hips");
            var spine = animator.transform.Find("Hips/Spine");
            var hipsHandle = animator.BindStreamTransform(hips);
            var spineHandle = animator.BindStreamTransform(spine);
            Vector3 hipsScale = default, hipsRead = default, spineRead = default;
            Quaternion spineRotation = default;
            Evaluate(animator, (stream, _) =>
            {
                Touch(stream);
                hipsScale = hipsHandle.GetLocalScale(stream);
                hipsRead = hipsHandle.GetPosition(stream);
                spineRead = spineHandle.GetPosition(stream);
                spineRotation = spineHandle.GetRotation(stream);
            });
            Near(Vector3.one, hipsScale);
            Near(hips.position, hipsRead, 1e-3f);
            Near(spine.rotation, spineRotation);
            Near(spine.position, spineRead, 1e-3f);
            Assert.True((spine.position - hips.position).magnitude > 0.2f);
        }
    }
}
