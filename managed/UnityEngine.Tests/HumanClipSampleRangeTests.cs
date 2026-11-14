using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Unbound.Runtime.Humanoid;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// How a Humanoid model clip's conversion samples the clip (6000.0.71f1 Editor). The clip
    /// ends at its last key, the frame count times the frame length in single precision. The
    /// samples are a frame apart from the first key, ceil(span / frame length) + 1 of them; for
    /// some frame counts that division rounds past the count, and the sample after the last key
    /// halves the last key's slope (the mean of the slopes on both sides, the far one flat)
    /// without becoming a key. Measured: tests/projects/modelclips HumanLong (31 frames at 30 a
    /// second: 32 keys a curve, the last slope halved, length 1.03333342), and an import of the
    /// same rig with other clip lengths outside the reference (7 frames at 24 a second and 61
    /// and 62 at 30: one key per frame and one at the end, the last slope halved; 8 at 24 and
    /// 20 and 30 at 30: the same keys, the last slope whole). The Manual and the Scripting API
    /// do not describe the sampling.
    /// </summary>
    [Collection("Animation")]
    public sealed class HumanClipSampleRangeTests : IDisposable
    {
        readonly List<Object> m_Created = new List<Object>();

        public void Dispose()
        {
            foreach (var o in m_Created)
                if (o != null) Object.DestroyImmediate(o);
        }

        /// <summary>A model clip as the importer hands it over: rotation keys of one leg on every
        /// frame, at the frame count times the frame length; hips position keys ending at the
        /// take's end; the clip ending at its last key.</summary>
        AnimationClip ModelClip(float rate, int frames)
        {
            var root = AnimationHumanStreamTests.Humanoid(out var animator);
            m_Created.Add(root);
            m_Created.Add(animator.avatar);
            float frame = 1f / rate, end = frames * frame;
            var clip = new AnimationClip { frameRate = rate };
            m_Created.Add(clip);
            var rotation = new[] { new Keyframe[frames + 1], new Keyframe[frames + 1], new Keyframe[frames + 1], new Keyframe[frames + 1] };
            var height = new Keyframe[frames + 1];
            for (int i = 0; i <= frames; i++)
            {
                var q = Quaternion.Euler(40f * MathF.Sin(i * 0.3f), 0f, 0f);
                for (int c = 0; c < 4; c++) rotation[c][i] = new Keyframe(i * frame, q[c]);
                height[i] = new Keyframe(i == frames ? (float)(frames / (double)rate) : i * frame, 1f + 0.01f * i);
            }
            for (int c = 0; c < 4; c++)
                clip.m_Curves.Add(new ClipCurve
                {
                    Binding = new CurveBinding { Path = "Hips/LeftUpperLeg", Attribute = "m_LocalRotation." + "xyzw"[c], ClassId = EngineTypes.TransformId },
                    Curve = new AnimationCurve(rotation[c]),
                });
            clip.m_Curves.Add(new ClipCurve
            {
                Binding = new CurveBinding { Path = "Hips", Attribute = "m_LocalPosition.y", ClassId = EngineTypes.TransformId },
                Curve = new AnimationCurve(height),
            });
            clip.m_HumanAvatar = animator.avatar;
            clip.m_StartTime = 0f;
            clip.m_StopTime = end;
            clip.m_HasRange = true;
            return clip;
        }

        /// <summary>The lengths measured in the Editor. At 7 frames of 1/24 s and at 31, 61 and
        /// 62 frames of 1/30 s the clip's length divided by the frame length rounds past the
        /// frame count: one sample more, which halves the last key's slope. The other lengths
        /// are the controls with the last slope whole.</summary>
        [Theory]
        [InlineData(24f, 7, true)]
        [InlineData(24f, 8, false)]
        [InlineData(30f, 20, false)]
        [InlineData(30f, 30, false)]
        [InlineData(30f, 31, true)]
        [InlineData(30f, 61, true)]
        [InlineData(30f, 62, true)]
        public void AHumanoidClipKeepsOneKeyPerFrameOfItsRange(float rate, int frames, bool sampledPastTheEnd)
        {
            var clip = ModelClip(rate, frames);
            float frame = 1f / rate;
            Assert.Equal(sampledPastTheEnd, MathF.Ceiling(clip.m_StopTime / frame) > frames);

            clip.ConvertHumanCurves();

            int moving = 0;
            foreach (var c in clip.m_Curves)
            {
                Assert.NotEqual(EngineTypes.TransformId, c.Binding.ClassId);
                Assert.Equal(frames + 1, c.Curve.length);
                for (int i = 0; i <= frames; i++) Assert.Equal(i * frame, c.Curve[i].time);
                Keyframe last = c.Curve[frames], before = c.Curve[frames - 1];
                float slope = (last.value - before.value) / (last.time - before.time);
                if (MathF.Abs(slope) < 1e-3f) continue;
                moving++;
                Assert.Equal(sampledPastTheEnd ? 0.5f * slope : slope, last.inTangent);
                Assert.Equal(last.inTangent, last.outTangent);
            }
            Assert.True(moving > 0);
            Assert.Equal(frames * frame, clip.length);
        }

        /// <summary>A Humanoid clip with a range and no curve on the avatar's nodes (a take
        /// without keys in its span, or curves of another hierarchy) gains no muscle curves:
        /// the samples span the curves' keys, not the clip's range, so nothing is sampled from
        /// the avatar's default pose. Not measured in the Editor.</summary>
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void AClipWithoutCurvesOfTheAvatarsNodesIsLeftAsItIs(bool foreignCurve)
        {
            var clip = ModelClip(30f, 20);
            clip.m_Curves.Clear();
            if (foreignCurve)
                clip.m_Curves.Add(new ClipCurve
                {
                    Binding = new CurveBinding { Path = "Elsewhere/Bone", Attribute = "m_LocalPosition.y", ClassId = EngineTypes.TransformId },
                    Curve = AnimationCurve.Linear(0f, 0f, 20f / 30f, 1f),
                });

            clip.ConvertHumanCurves();

            Assert.Equal(foreignCurve ? 1 : 0, clip.m_Curves.Count);
            foreach (var c in clip.m_Curves) Assert.Equal(EngineTypes.TransformId, c.Binding.ClassId);
        }

        /// <summary>A clip on frames 1 to 31 of a take at 30 fps (tests/projects/modelclips
        /// HumanLate): the Editor steps from the clip's start on the take's times and moves the
        /// samples back, so the third is 0.06666668 (not 2 / 30 = 0.06666667) and the 31st,
        /// 0.99999994, is the last one inside the span; the 32nd lies past it. A take from 0
        /// keeps the plain steps.</summary>
        [Fact]
        public void SamplesStepOnTheTakesTimesFromTheClipsStart()
        {
            float frame = 1f / 30f;
            float stop = 31f * frame - frame;
            var times = HumanClipImport.SampleTimes(0f, stop, 30f, 1, frame);
            Assert.Equal(32, times.Length);
            Assert.Equal(0.06666668f, times[2]);
            Assert.Equal(0.99999994f, times[30]);
            Assert.True(times[31] > stop);
            Assert.Equal(2f * frame, HumanClipImport.SampleTimes(0f, stop, 30f, 1)[2]);
        }
    }
}
