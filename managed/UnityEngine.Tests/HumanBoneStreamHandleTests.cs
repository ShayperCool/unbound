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
