using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor;
using UnityEditor.Animations;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>GameObjectRecorder, the keyframe reduction it saves with and the clip lengths around
    /// it, with values from tests/projects/animrecorder and tests/projects/animinternal (Unity
    /// 6000.0.71f1).</summary>
    public class GameObjectRecorderTests
    {
        static readonly EditorCurveBinding s_X = EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.x");

        [Fact]
        public void AnUnsyncedCurveKeepsTheSyncedLength()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var clip = new AnimationClip();
            AnimationUtility.SetEditorCurveNoSync(clip, s_X, AnimationCurve.Linear(0f, 0f, 2f, 1f));
            Assert.Equal(2f, clip.length); // sync.length: before the first sync the length follows the curves
            AnimationUtility.SyncEditorCurves(clip);
            AnimationUtility.SetEditorCurveNoSync(clip, s_X, AnimationCurve.Linear(0f, 0f, 3f, 1f));
            AnimationUtility.SetEditorCurveNoSync(clip, s_X, AnimationCurve.Linear(0f, 0f, 4f, 1f));
            Assert.Equal(2f, clip.length); // sync.length: the synced length until the sync
            AnimationUtility.SyncEditorCurves(clip);
            Assert.Equal(4f, clip.length);
            // The synced setters end the held length; so does another edit of the curves.
            AnimationUtility.SetEditorCurveNoSync(clip, s_X, AnimationCurve.Linear(0f, 0f, 5f, 1f));
            Assert.Equal(4f, clip.length);
            AnimationUtility.SetEditorCurve(clip, s_X, AnimationCurve.Linear(0f, 0f, 6f, 1f));
            Assert.Equal(6f, clip.length);
            AnimationUtility.SetEditorCurveNoSync(clip, s_X, AnimationCurve.Linear(0f, 0f, 7f, 1f));
            Assert.Equal(6f, clip.length);
            clip.ClearCurves();
            Assert.Equal(1f, clip.length);
        }

        [Fact]
        public void AnObjectReferenceCurvesLastKeyLastsAFrame()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var a = new GameObject("a");
            var b = new GameObject("b");
            try
            {
                var clip = new AnimationClip { frameRate = 20f };
                AnimationUtility.SetObjectReferenceCurve(clip, EditorCurveBinding.PPtrCurve("", typeof(SpriteRenderer), "m_Sprite"),
                    new[] { new ObjectReferenceKeyframe { time = 0f, value = a }, new ObjectReferenceKeyframe { time = 0.5f, value = b } });
                Assert.Equal(0.55f, clip.length); // objectLength.only
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Light), "m_Range"), AnimationCurve.Linear(0f, 1f, 0.52f, 2f));
                Assert.Equal(0.55f, clip.length); // objectLength.withFloat
                clip.frameRate = 4f;
                Assert.Equal(0.75f, clip.length); // objectLength.rate4
            }
            finally
            {
                Object.DestroyImmediate(a);
                Object.DestroyImmediate(b);
            }
        }

        [Fact]
        public void ARecordingSavesOnFramesAndReducesWhatStays()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var root = new GameObject("recorderRoot");
            var light = root.AddComponent<Light>();
            try
            {
                var recorder = new GameObjectRecorder(root);
                var range = EditorCurveBinding.FloatCurve("", typeof(Light), "m_Range");
                var intensity = EditorCurveBinding.FloatCurve("", typeof(Light), "m_Intensity");
                recorder.Bind(range);
                recorder.Bind(intensity);
                recorder.Bind(range);
                Assert.Equal(2, recorder.GetBindings().Length);
                Assert.Throws<InvalidOperationException>(() => recorder.SaveToClip(new AnimationClip())); // api.save.unrecorded
                for (int i = 0; i < 10; i++)
                {
                    light.range = 3f;
                    light.intensity = i < 5 ? 1f : 1f + (i - 4);
                    recorder.TakeSnapshot(0.1f);
                }
                Assert.True(recorder.isRecording);

                var reduced = new AnimationClip();
                recorder.SaveToClip(reduced, 10f);
                Assert.Equal(10f, reduced.frameRate);
                var constant = AnimationUtility.GetEditorCurve(reduced, range);
                Assert.Equal(2, constant.length);
                Assert.Equal(3f, constant[1].value);
                var ramp = AnimationUtility.GetEditorCurve(reduced, intensity);
                Assert.InRange(ramp.length, 3, 9);
                Assert.Equal(1f, ramp[0].value);
                Assert.Equal(6f, ramp[ramp.length - 1].value);

                var full = new AnimationClip();
                recorder.SaveToClip(full, 10f, new CurveFilterOptions { keyframeReduction = false, floatError = -1f });
                Assert.Equal(10, AnimationUtility.GetEditorCurve(full, range).length);
                Assert.Equal(10, AnimationUtility.GetEditorCurve(full, intensity).length);
                Assert.Throws<ArgumentException>(() => recorder.SaveToClip(full, 0f)); // api.save.fps0
                Assert.Throws<ArgumentException>(() => recorder.SaveToClip(full, 10f, new CurveFilterOptions { keyframeReduction = true, floatError = -1f })); // api.save.negativeError
                recorder.ResetRecording();
                Assert.False(recorder.isRecording);
                Assert.Empty(recorder.GetBindings());
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Fact]
        public void AnUnmeasuredReleaseReportsTheRecorder()
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            var go = new GameObject("unmeasuredRecorder");
            try
            {
                int before = Hits("UnityEditor.Animations.GameObjectRecorder.ctor");
                _ = new GameObjectRecorder(go);
                Assert.Equal(before + 1, Hits("UnityEditor.Animations.GameObjectRecorder.ctor"));
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }

        static int Hits(string member) => System.Linq.Enumerable.FirstOrDefault(Stubs.HitReport(), h => h.Key == member).Value;

        static Keyframe Stepped(float time, float value) => new Keyframe(time, value, float.PositiveInfinity, float.PositiveInfinity);

        readonly struct CurveSource : KeyframeReduction.ISource
        {
            readonly AnimationCurve m_Curve;

            internal CurveSource(AnimationCurve curve) => m_Curve = curve;

            public float Evaluate(float time) => m_Curve.Evaluate(time);

            public float Interpolate(in Keyframe from, in Keyframe to, float time) => AnimationCurve.EvaluateSegment(from, to, time);
        }

        /// <summary>Stepped keys (both slopes infinite, as an integer's or a flag's): a key within the
        /// error of the next is dropped, any other step keeps both its keys.</summary>
        [Fact]
        public void ReductionDropsAStepKeyWithinTheErrorOfTheNext()
        {
            var keys = new[] { Stepped(0f, 0f), Stepped(0.1f, 0f), Stepped(0.2f, 1f), Stepped(0.3f, 1.001f), Stepped(0.4f, 1.001f), Stepped(0.5f, 3f) };
            var kept = new List<int>();
            bool flat = KeyframeReduction.Reduce(new CurveSource(new AnimationCurve(keys)), keys, 0.1f, 0.005f, kept);
            Assert.False(flat);
            Assert.Equal(new[] { 0, 1, 2, 4, 5 }, kept);
        }

        /// <summary>A ClampedAuto key whose interval to the key before is under 1e-5 seconds has no
        /// slope over it, so it is flat.</summary>
        [Fact]
        public void AClampedAutoKeyAfterATinyIntervalIsFlat()
        {
            var curve = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1e-6f, 0.5f), new Keyframe(1f, 1f));
            var control = new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(0.5f, 0.5f), new Keyframe(1f, 1f));
            foreach (var c in new[] { curve, control })
                for (int i = 0; i < c.length; i++)
                {
                    AnimationUtility.SetKeyLeftTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto);
                    AnimationUtility.SetKeyRightTangentMode(c, i, AnimationUtility.TangentMode.ClampedAuto);
                }
            Assert.Equal(0f, curve[1].inTangent);
            Assert.Equal(1f, control[1].inTangent);
        }
    }
}
