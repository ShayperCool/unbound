using System;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public class CompatReleaseTests : IDisposable
    {
        readonly string m_Release = ProjectInfo.UnityVersion;

        public void Dispose() => ProjectInfo.UnityVersion = m_Release;

        [Fact]
        public void PhysicsStateReleaseGatesAreCachedWithoutAllocations()
        {
            ProjectInfo.UnityVersion = "6000.0.71f1";
            _ = Compat.ConstrainedBodyStateMeasured;
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool measured = true;
            for (int i=0; i<1000; i++)
                measured &= Compat.ConstrainedBodyStateMeasured && Compat.ConvexColliderFrameMeasured && Compat.ColliderLifetimeMeasured;
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread()-before);
            Assert.True(measured);
            ProjectInfo.UnityVersion = "6000.0.72f1";
            Assert.False(Compat.ConstrainedBodyStateMeasured || Compat.ConvexColliderFrameMeasured || Compat.ColliderLifetimeMeasured);
        }

        [Fact]
        public void RepeatedAudioReleaseGatesDoNotAllocate()
        {
            // The audio filter chains and AudioMixer.GetFloat check these every frame.
            ProjectInfo.UnityVersion = "6000.0.71f1";
            _ = Compat.ModulatedDelayMeasured;
            long before = GC.GetAllocatedBytesForCurrentThread();
            bool all = true;
            for (int i = 0; i < 1000; i++)
                all &= Compat.ModulatedDelayMeasured && Compat.DistortionMeasured && Compat.MixerNormalizeMeasured
                    && Compat.MixerPitchShifterMeasured && Compat.MixerClearFloatSubmissionMeasured && Compat.MixerSidechainMeasured && Compat.MixerSnapshotCurvesMeasured && Compat.MixerTimedSidechainMeasured && Compat.MixerGraphStartupMeasured && Compat.MixerEffectTransitionsMeasured && Compat.MixerWetTransitionsMeasured;
            Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
            Assert.True(all);
            ProjectInfo.UnityVersion = "6000.0.72f1";
            Assert.False(Compat.ModulatedDelayMeasured || Compat.DistortionMeasured || Compat.MixerNormalizeMeasured
                || Compat.MixerPitchShifterMeasured || Compat.MixerClearFloatSubmissionMeasured || Compat.MixerSidechainMeasured || Compat.MixerSnapshotCurvesMeasured || Compat.MixerTimedSidechainMeasured || Compat.MixerGraphStartupMeasured || Compat.MixerEffectTransitionsMeasured || Compat.MixerWetTransitionsMeasured);
        }
    }
}
