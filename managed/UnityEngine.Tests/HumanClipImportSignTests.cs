using System;
using System.Collections.Generic;
using Unbound.Runtime.Humanoid;
using Unbound.Tests;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// The sign a humanoid clip's conversion takes its nodes' rotations with: the Editor's model
    /// importer gives a node's rotation, and the first key of its rotation curve, the sign a
    /// rotation matrix gives its quaternion (6000.0.71f1; the Manual and the Scripting API say
    /// nothing about it). The muscle curves do not show it; the IK goal curves do, and a blend
    /// with the identity below a total weight of one (tests/projects/humanoid body.directLowB,
    /// body.directEmptyChild) depends on it.
    /// </summary>
    [Collection("Animation")]
    public sealed class HumanClipImportSignTests : IDisposable
    {
        readonly List<Object> m_Created = new List<Object>();

        public void Dispose()
        {
            foreach (var o in m_Created)
                if (o != null) Object.DestroyImmediate(o);
        }

        /// <summary>First rotation keys of the probe's models as the Editor imports them (a
        /// capture outside the reference): each has the importer's sign, its negation has not.
        /// The first five have a negative w; the last two a positive one.</summary>
        [Theory]
        [InlineData(-0.0295306686f, 0.875484765f, -0.463585f, -0.133204371f)]
        [InlineData(0.9673199f, 0.000962821534f, 0.0573583543f, -0.246984333f)]
        [InlineData(0.8492892f, -0.4019245f, 0.3161406f, -0.131223962f)]
        [InlineData(0.0173410885f, 0.9229305f, -0.0418650843f, -0.3822905f)]
        [InlineData(-0.173610568f, -0.00361531437f, 0.9845943f, -0.0205034558f)]
        [InlineData(-0.58f, -0.13f, 0.10f, 0.80f)]
        [InlineData(0.31f, 0.69f, -0.34f, 0.56f)]
        public void TheImportersSignIsARotationMatrixsQuaternionSign(float x, float y, float z, float w)
        {
            var q = M.QNormalize(new F4(x, y, z, w));
            Assert.True(HumanClipImport.ImporterSign(q));
            Assert.False(HumanClipImport.ImporterSign(-q));
        }

        /// <summary>The rule itself: w positive while the rotation's matrix has a positive trace
        /// (|w| above 1/2), else the component of the largest diagonal element positive.</summary>
        [Fact]
        public void TheSignFollowsTheTraceThenTheLargestDiagonalElement()
        {
            // |w| above 1/2: w decides, whatever the largest component is.
            Assert.True(HumanClipImport.ImporterSign(M.QNormalize(new F4(-0.8f, 0.1f, 0.1f, 0.55f))));
            Assert.False(HumanClipImport.ImporterSign(M.QNormalize(new F4(0.8f, 0.1f, 0.1f, -0.55f))));
            // |w| below 1/2: the largest of x, y and z decides, whatever w is.
            Assert.True(HumanClipImport.ImporterSign(M.QNormalize(new F4(0.9f, -0.2f, -0.2f, -0.3f))));
            Assert.True(HumanClipImport.ImporterSign(M.QNormalize(new F4(-0.2f, 0.9f, -0.2f, -0.3f))));
            Assert.True(HumanClipImport.ImporterSign(M.QNormalize(new F4(-0.2f, -0.2f, 0.9f, -0.3f))));
            Assert.False(HumanClipImport.ImporterSign(M.QNormalize(new F4(-0.9f, 0.2f, 0.2f, 0.3f))));
            Assert.True(HumanClipImport.ImporterSign(F4.QIdentity));
        }

        /// <summary>A conversion's curves do not depend on the sign the samples hold a node's
        /// rotation with: the node takes the importer's sign either way. Control: another
        /// rotation changes the curves. Which sign the goal curves then have is the probe's to
        /// say (body.directLowB, body.directEmptyChild).</summary>
        [Fact]
        public void AConversionTakesItsNodesRotationsWithTheImportersSign()
        {
            var root = AnimationHumanStreamTests.Humanoid(out var animator);
            m_Created.Add(root);
            m_Created.Add(animator.avatar);
            var avatar = animator.avatar.Constant;
            var h = avatar.Human;
            int leg = avatar.HumanSkeletonIndexArray[h.BoneIndex[Bone.RightUpperLeg]];
            var times = HumanClipImport.SampleTimes(0f, 2f / 60f, 60f, 1);
            // Nearly half a turn about x, a little more with time: w stays below 1/2.
            F4 Turn(int frame) => M.QNormalize(new F4(0.96f, 0.02f, 0.05f, 0.25f - 0.01f * frame));

            HumanClipCurves Convert(Func<int, F4> rotation) => HumanClipImport.Convert(avatar, times,
                (frame, time, pose) => pose.X[leg].q = M.QNormalize(M.QMul(avatar.DefaultPose.X[leg].q, rotation(frame))),
                new HumanClipSettings { Oversampling = 1 });
            var plain = Convert(Turn);
            var negated = Convert(frame => -Turn(frame));
            var other = Convert(frame => M.QNormalize(new F4(0.5f, 0.02f, 0.05f, 0.86f)));
            const int rightFootQ = HumanClipImport.GoalFirst + 7 + 3;
            bool differs = false;
            for (int c = HumanClipImport.RootFirst; c < HumanClipImport.HumanEnd; c++)
            {
                if (plain.Values[c] == null) continue;
                for (int i = 0; i < times.Length; i++)
                {
                    Assert.Equal(plain.Values[c][i], negated.Values[c][i]);
                    differs |= MathF.Abs(plain.Values[c][i] - other.Values[c][i]) > 1e-3f;
                }
            }
            Assert.True(differs);
            // The goal's rotation is a unit rotation in every sample, in one hemisphere.
            for (int i = 0; i < times.Length; i++)
            {
                var q = new F4(plain.Values[rightFootQ][i], plain.Values[rightFootQ + 1][i], plain.Values[rightFootQ + 2][i], plain.Values[rightFootQ + 3][i]);
                Near.Equal(1f, MathF.Sqrt(q.x * q.x + q.y * q.y + q.z * q.z + q.w * q.w), 1e-5f);
                if (i == 0) continue;
                var p = new F4(plain.Values[rightFootQ][i - 1], plain.Values[rightFootQ + 1][i - 1], plain.Values[rightFootQ + 2][i - 1],
                    plain.Values[rightFootQ + 3][i - 1]);
                Assert.True(p.x * q.x + p.y * q.y + p.z * q.z + p.w * q.w > 0.9f);
            }
        }
    }
}
