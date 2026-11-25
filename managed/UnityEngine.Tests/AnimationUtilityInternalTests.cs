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

        /// <summary>A curve with modes set and every tangent 9 (tests/projects/animinternal
        /// <c>tangents.*</c>).</summary>
        static AnimationCurve Moded()
        {
            float[] values = { 0f, 2f, 1f, 3f, 3f };
            var left = new[] { AnimationUtility.TangentMode.ClampedAuto, AnimationUtility.TangentMode.Auto, AnimationUtility.TangentMode.Linear, AnimationUtility.TangentMode.ClampedAuto, AnimationUtility.TangentMode.Free };
            var right = new[] { AnimationUtility.TangentMode.ClampedAuto, AnimationUtility.TangentMode.Auto, AnimationUtility.TangentMode.Constant, AnimationUtility.TangentMode.Linear, AnimationUtility.TangentMode.Auto };
            var keys = new Keyframe[5];
            for (int i = 0; i < 5; i++)
            {
                var key = new Keyframe(i, values[i], 9f, 9f);
                AnimationUtility.SetKeyLeftTangentMode(ref key, left[i]);
                AnimationUtility.SetKeyRightTangentMode(ref key, right[i]);
                keys[i] = key;
            }
            return new AnimationCurve(keys);
        }

        static string Tangents(AnimationCurve c) => string.Join(" | ", System.Linq.Enumerable.Select(c.keys,
            k => k.inTangent.ToString("R", System.Globalization.CultureInfo.InvariantCulture) + " " + k.outTangent.ToString("R", System.Globalization.CultureInfo.InvariantCulture)));

        [Fact]
        public void TangentsFromModesOfAKeyAndItsNeighbours()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var expected = new Dictionary<int, string>
            {
                [0] = "0 0 | 0.5 0.5 | 9 9 | 9 9 | 9 9",          // tangents.surrounding.0
                [2] = "9 9 | 0.5 0.5 | -1 Infinity | 0 0 | 9 9",   // tangents.surrounding.2
                [4] = "9 9 | 9 9 | 9 9 | 0 0 | 0 0",               // tangents.surrounding.4
            };
            foreach (var (index, tangents) in expected)
            {
                var curve = Moded();
                AnimationUtility.UpdateTangentsFromModeSurrounding(curve, index);
                Assert.Equal(tangents, Tangents(curve));
            }
            var all = Moded();
            AnimationUtility.UpdateTangentsFromMode(all);
            Assert.Equal("0 0 | 0.5 0.5 | -1 Infinity | 0 0 | 0 0", Tangents(all)); // tangents.all
            Assert.Throws<System.IndexOutOfRangeException>(() => AnimationUtility.UpdateTangentsFromModeSurrounding(Moded(), 5));
            Assert.Throws<System.IndexOutOfRangeException>(() => AnimationUtility.UpdateTangentsFromModeSurrounding(new AnimationCurve(), 0));
            Assert.Throws<System.ArgumentNullException>(() => AnimationUtility.UpdateTangentsFromModeSurrounding(null, 0));
            Assert.Throws<System.ArgumentNullException>(() => AnimationUtility.UpdateTangentsFromMode(null));
            Assert.Throws<System.ArgumentNullException>(() => AnimationUtility.SyncEditorCurves(null)); // sync.null
        }

        [Fact]
        public void InitializedPropertiesRevertWhenAnimationModeStops()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var root = new GameObject("initRoot");
            var child = new GameObject("c");
            child.transform.SetParent(root.transform, false);
            var light = root.AddComponent<Light>();
            var clip = new AnimationClip();
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Transform), "m_LocalPosition.x"), AnimationCurve.Constant(0f, 1f, 4f));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("c", typeof(Transform), "m_LocalScale.y"), AnimationCurve.Constant(0f, 1f, 4f));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Light), "m_Intensity"), AnimationCurve.Constant(0f, 1f, 4f));
            try
            {
                root.transform.localPosition = new Vector3(1f, 2f, 3f);
                light.intensity = 2f;
                // init.outside: outside animation mode it throws and records nothing.
                Assert.Throws<System.InvalidOperationException>(() => AnimationMode.InitializePropertyModificationForGameObject(root, clip));
                AnimationMode.StartAnimationMode();
                try
                {
                    AnimationMode.InitializePropertyModificationForGameObject(root, clip);
                    // init.gameObject: True True True True True False
                    Assert.True(AnimationMode.IsPropertyAnimated(root.transform, "m_LocalPosition.y"));
                    Assert.True(AnimationMode.IsPropertyAnimated(child.transform, "m_LocalScale.y"));
                    Assert.True(AnimationMode.IsPropertyAnimated(light, "m_Intensity"));
                    Assert.False(AnimationMode.IsPropertyAnimated(root.transform, "m_LocalScale.x"));
                    root.transform.localPosition = new Vector3(5f, 6f, 7f);
                    child.transform.localScale = new Vector3(8f, 8f, 8f);
                    light.intensity = 9f;
                }
                finally { AnimationMode.StopAnimationMode(); }
                // init.gameObject.stopped: 1 2 3 / 1 1 1 / 2
                Assert.Equal(new Vector3(1f, 2f, 3f), root.transform.localPosition);
                Assert.Equal(Vector3.one, child.transform.localScale);
                Assert.Equal(2f, light.intensity);

                // init.objectRootClip / init.object.stopped: only the object's own properties.
                AnimationMode.StartAnimationMode();
                try
                {
                    AnimationMode.InitializePropertyModificationForObject(light, clip);
                    Assert.True(AnimationMode.IsPropertyAnimated(light, "m_Intensity"));
                    Assert.False(AnimationMode.IsPropertyAnimated(root.transform, "m_LocalPosition.x"));
                    light.intensity = 9f;
                    root.transform.localPosition = new Vector3(5f, 6f, 7f);
                }
                finally { AnimationMode.StopAnimationMode(); }
                Assert.Equal(2f, light.intensity);
                Assert.Equal(new Vector3(5f, 6f, 7f), root.transform.localPosition);
                Assert.Throws<System.ArgumentNullException>(() => AnimationMode.InitializePropertyModificationForObject(null, clip));
            }
            finally { Object.DestroyImmediate(root); }
        }

        /// <summary>A release without the measurements keeps the earlier not-implemented report and
        /// changes nothing.</summary>
        [Fact]
        public void AnUnmeasuredReleaseLeavesTheTangents()
        {
            using var release = TestRelease.Pin(TestRelease.Unmeasured);
            var members = new[]
            {
                "UnityEditor.AnimationUtility.UpdateTangentsFromMode", "UnityEditor.AnimationUtility.UpdateTangentsFromModeSurrounding",
                "UnityEditor.AnimationUtility.SyncEditorCurves", "UnityEditor.AnimationMode.InitializePropertyModificationForGameObject",
                "UnityEditor.AnimationMode.InitializePropertyModificationForObject",
            };
            var before = System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(members, Hits));
            var curve = Moded();
            AnimationUtility.UpdateTangentsFromMode(curve);
            AnimationUtility.UpdateTangentsFromModeSurrounding(curve, 2);
            Assert.Equal("9 9 | 9 9 | 9 9 | 9 9 | 9 9", Tangents(curve)); // tangents.start
            var go = new GameObject("unmeasured");
            var clip = new AnimationClip();
            AnimationUtility.SyncEditorCurves(clip);
            // Outside animation mode the measured members throw; these only report.
            AnimationMode.InitializePropertyModificationForGameObject(go, clip);
            AnimationMode.InitializePropertyModificationForObject(go.transform, clip);
            Object.DestroyImmediate(go);
            for (int i = 0; i < members.Length; i++) Assert.Equal(before[i] + 1, Hits(members[i]));
        }

        static int Hits(string member) => System.Linq.Enumerable.FirstOrDefault(Unbound.Runtime.Stubs.HitReport(), h => h.Key == member).Value;

        const string KindNotice = "AnimationMode.InitializePropertyModification Animator, material and blend shape curves";
        const string UnreadNotice = "AnimationMode.InitializePropertyModification unread properties";

        /// <summary>An Animator's curves and material curves are reported and not recorded (their
        /// recording is unmeasured; Unbound's read of them is not the object's value).</summary>
        [Fact]
        public void AnimatorAndMaterialCurvesAreReportedNotRecorded()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var go = new GameObject("animated");
            var animator = go.AddComponent<Animator>();
            var renderer = go.AddComponent<MeshRenderer>();
            var clip = new AnimationClip();
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Animator), "Speed"), AnimationCurve.Constant(0f, 1f, 1f));
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(MeshRenderer), "material._Color.r"), AnimationCurve.Constant(0f, 1f, 1f));
            int before = Hits(KindNotice);
            AnimationMode.StartAnimationMode();
            try
            {
                AnimationMode.InitializePropertyModificationForGameObject(go, clip);
                Assert.False(AnimationMode.IsPropertyAnimated(animator, "Speed"));
                Assert.False(AnimationMode.IsPropertyAnimated(renderer, "material._Color.r"));
                Assert.Equal(before + 2, Hits(KindNotice));
                AnimationMode.InitializePropertyModificationForObject(renderer, clip);
                Assert.False(AnimationMode.IsPropertyAnimated(renderer, "material._Color.r"));
                Assert.Equal(before + 3, Hits(KindNotice));
            }
            finally
            {
                AnimationMode.StopAnimationMode();
                Object.DestroyImmediate(go);
            }
        }

        /// <summary>A property Unbound cannot read is reported, on both paths; a rotation authored as
        /// Euler angles records its rotation and is not reported for its hint.</summary>
        [Fact]
        public void UnreadPropertiesAreReported()
        {
            using var release = TestRelease.Pin(TestRelease.Measured);
            var go = new GameObject("animated");
            var light = go.AddComponent<Light>();
            var unread = new AnimationClip();
            AnimationUtility.SetEditorCurve(unread, EditorCurveBinding.FloatCurve("", typeof(Light), "m_Shadows.m_Type"), AnimationCurve.Constant(0f, 1f, 1f));
            var euler = new AnimationClip();
            AnimationUtility.SetEditorCurve(euler, EditorCurveBinding.FloatCurve("", typeof(Transform), "localEulerAnglesRaw.y"), AnimationCurve.Constant(0f, 1f, 30f));
            go.transform.localRotation = Quaternion.Euler(0f, 10f, 0f);
            var rotation = go.transform.localRotation;
            AnimationMode.StartAnimationMode();
            try
            {
                int before = Hits(UnreadNotice);
                AnimationMode.InitializePropertyModificationForGameObject(go, euler);
                Assert.Equal(before, Hits(UnreadNotice));
                Assert.True(AnimationMode.IsPropertyAnimated(go.transform, "m_LocalRotation.y"));
                AnimationMode.InitializePropertyModificationForGameObject(go, unread);
                Assert.Equal(before + 1, Hits(UnreadNotice));
                AnimationMode.InitializePropertyModificationForObject(light, unread);
                Assert.Equal(before + 2, Hits(UnreadNotice));
                go.transform.localRotation = Quaternion.Euler(0f, 50f, 0f);
            }
            finally
            {
                AnimationMode.StopAnimationMode();
            }
            Assert.Equal(rotation, go.transform.localRotation);
            Object.DestroyImmediate(go);
        }
    }
}
