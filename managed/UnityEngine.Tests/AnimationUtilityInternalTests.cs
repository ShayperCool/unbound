using System.Collections.Generic;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The internal key-by-reference overloads and AnimationMode recording, with values
    /// from tests/projects/animinternal.</summary>
    public class AnimationUtilityInternalTests
    {
#pragma warning disable CS0618 // Keyframe.tangentMode: the raw mode bits
        [Fact]
        public void KeyOverloadsSetOnlyTheirBits()
        {
            var full = new Keyframe(1f, 2f, 0.5f, -0.25f) { tangentMode = 0x1FF };
            var key = full;
            AnimationUtility.SetKeyLeftTangentMode(ref key, AnimationUtility.TangentMode.Free);
            Assert.Equal(481, key.tangentMode); // key.1.left.Free
            Assert.Equal(0.5f, key.inTangent);
            key = full;
            AnimationUtility.SetKeyRightTangentMode(ref key, AnimationUtility.TangentMode.Auto);
            Assert.Equal(63, key.tangentMode); // key.1.right.Auto
            key = full;
            AnimationUtility.SetKeyBroken(ref key, false);
            Assert.Equal(510, key.tangentMode); // key.1.broken.False
            key = new Keyframe(0f, 0f);
            AnimationUtility.SetKeyLeftTangentMode(ref key, (AnimationUtility.TangentMode)31);
            Assert.Equal(62, key.tangentMode); // key.left.31
        }
#pragma warning restore CS0618

        [Fact]
        public void RecordingRaisesItsEventOnEveryCall()
        {
            var calls = new List<string>();
            System.Action start = () => calls.Add("start"), stop = () => calls.Add("stop");
            AnimationMode.onAnimationRecordingStart += start;
            AnimationMode.onAnimationRecordingStop += stop;
            try
            {
                Assert.False(AnimationMode.InAnimationRecording());
                AnimationMode.StartAnimationRecording();
                AnimationMode.StartAnimationRecording();
                Assert.True(AnimationMode.InAnimationRecording());
                AnimationMode.StopAnimationRecording();
                AnimationMode.StopAnimationRecording();
                Assert.False(AnimationMode.InAnimationRecording());
            }
            finally
            {
                AnimationMode.onAnimationRecordingStart -= start;
                AnimationMode.onAnimationRecordingStop -= stop;
            }
            Assert.Equal(new[] { "start", "start", "stop", "stop" }, calls);
        }
    }
}
