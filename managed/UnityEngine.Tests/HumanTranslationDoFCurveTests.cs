using System;
using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using Unbound.Runtime.Humanoid;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Which translation DoF curves a Humanoid model clip has (6000.0.71f1 Editor,
    /// tests/projects/humanoid tdof.curves and tdof.playT): a curve stays only when one of its
    /// keys reaches a minimum (between 9.2e-5 and 1.6e-4 by the reference; 1e-4 here), each of a
    /// bone's three curves on its own, and a curve that is absent plays as zero. The Manual's Avatar Muscle and Settings page describes
    /// Translation DoF as enabling translation animations for the humanoid and does not mention
    /// a minimum.
    /// </summary>
    [Collection("Animation")]
    public sealed class HumanTranslationDoFCurveTests : IDisposable
    {
        readonly List<Object> m_Created = new List<Object>();

        public void Dispose()
        {
            foreach (var o in m_Created)
                if (o != null) Object.DestroyImmediate(o);
        }

        [Fact]
        public void ACurveStaysWhenOneKeyReachesTheMinimum()
        {
            Assert.False(HumanClipImport.KeepsTDoFCurve(new float[0]));
            Assert.False(HumanClipImport.KeepsTDoFCurve(new[] { 0f, 9.9e-5f, -9.9e-5f }));
            // A key equal to the minimum counts (Unbound's choice inside the measured bracket).
            Assert.True(HumanClipImport.KeepsTDoFCurve(new[] { 0f, HumanClipImport.TDoFCurveMinimum, 0f }));
            Assert.True(HumanClipImport.KeepsTDoFCurve(new[] { 0f, 0f, -2e-4f }));
            // The largest absent and the smallest present curve of the probe's clip.
            Assert.False(HumanClipImport.KeepsTDoFCurve(new[] { 0f, 9.223e-5f }));
            Assert.True(HumanClipImport.KeepsTDoFCurve(new[] { 0f, 1.576e-4f }));
        }

        /// <summary>A model clip on a rig with translation DoF: the spine moves up by
        /// <paramref name="up"/> and sideways by <paramref name="aside"/> over ten frames.</summary>
        AnimationClip SpineClip(float up, float aside, bool translationDoF)
        {
            var root = AnimationHumanStreamTests.Humanoid(out var animator, translationDoF: translationDoF);
            m_Created.Add(root);
            m_Created.Add(animator.avatar);
            var spine = root.transform.Find("Hips/Spine").localPosition;
            const float Frame = 1f / 30f;
            var clip = new AnimationClip { frameRate = 30f };
            m_Created.Add(clip);
            for (int axis = 0; axis < 3; axis++)
            {
                var keys = new Keyframe[11];
                for (int i = 0; i <= 10; i++)
                    keys[i] = new Keyframe(i * Frame, spine[axis] + (axis == 1 ? up : axis == 0 ? aside : 0f) * i / 10f);
                clip.m_Curves.Add(new ClipCurve
                {
                    Binding = new CurveBinding { Path = "Hips/Spine", Attribute = "m_LocalPosition." + "xyz"[axis], ClassId = EngineTypes.TransformId },
                    Curve = new AnimationCurve(keys),
                });
            }
            clip.m_HumanAvatar = animator.avatar;
            clip.m_StartTime = 0f;
            clip.m_StopTime = 10 * Frame;
            clip.m_HasRange = true;
            clip.ConvertHumanCurves();
            return clip;
        }

        static string[] TranslationCurves(AnimationClip clip) =>
            clip.m_Curves.Select(c => c.Binding.Attribute).Where(a => a.Contains("TDOF")).OrderBy(a => a, StringComparer.Ordinal).ToArray();

        /// <summary>A bone moved along one axis keeps that component's curve; a second component
        /// stays with it once it reaches the minimum too. The muscle, goal and body curves stay
        /// whatever their values, and without translation DoF the clip has no such curve.</summary>
        [Fact]
        public void EachComponentOfABoneIsKeptOnItsOwn()
        {
            var oneAxis = TranslationCurves(SpineClip(up: 0.02f, aside: 0f, true));
            Assert.Single(oneAxis);
            Assert.StartsWith("SpineTDOF.", oneAxis[0]);

            var belowTheMinimum = SpineClip(up: 0.02f, aside: 5e-5f, true);
            Assert.Equal(oneAxis, TranslationCurves(belowTheMinimum));

            var twoAxes = TranslationCurves(SpineClip(up: 0.02f, aside: 5e-4f, true));
            Assert.Equal(2, twoAxes.Length);
            Assert.Contains(oneAxis[0], twoAxes);
            Assert.All(twoAxes, name => Assert.StartsWith("SpineTDOF.", name));

            // Nothing moves: no translation curve, and still every other curve of the clip.
            var still = SpineClip(up: 0f, aside: 0f, true);
            Assert.Empty(TranslationCurves(still));
            int others = belowTheMinimum.m_Curves.Count - 1;
            Assert.True(others > 100);
            Assert.Equal(others, still.m_Curves.Count);

            Assert.Empty(TranslationCurves(SpineClip(up: 0.02f, aside: 5e-4f, false)));
        }

        /// <summary>The Editor's curve queries show a clip with the Mirror setting as imported
        /// (tests/projects/humanoid tdof.curves, the clip named Mirror, after it has played;
        /// before it has played the Editor is unmeasured): here the same names and values before
        /// and after the mirrored curves exist. Control: the curves
        /// the clip plays have traded sides and signs by then.</summary>
        [Fact]
        public void AMirroredClipsCurvesAreQueriedAsImported()
        {
            var clip = new AnimationClip { frameRate = 30f };
            m_Created.Add(clip);
            (string Name, float Value)[] curves = { ("Left Arm Down-Up", 0.25f), ("Spine Left-Right", 0.5f), ("LeftUpperLegTDOF.x", 3e-4f), ("NeckTDOF.y", 2e-4f) };
            foreach (var (name, value) in curves)
                clip.m_Curves.Add(new ClipCurve
                {
                    Binding = new CurveBinding { Path = "", Attribute = name, ClassId = 95 },
                    Curve = AnimationCurve.Linear(0f, 0f, 1f, value),
                });
            clip.m_Mirror = true;

            void QueriesShowTheImportedCurves()
            {
                var bindings = UnityEditor.AnimationUtility.GetCurveBindings(clip);
                Assert.Equal(curves.Select(c => c.Name), bindings.Select(b => b.propertyName));
                for (int i = 0; i < curves.Length; i++)
                    Assert.Equal(curves[i].Value, UnityEditor.AnimationUtility.GetEditorCurve(clip, bindings[i])[1].value);
            }

            QueriesShowTheImportedCurves();
            clip.EnsureHumanCurves();
            Assert.Equal(new[] { "Right Arm Down-Up", "Spine Left-Right", "RightUpperLegTDOF.x", "NeckTDOF.y" }, clip.m_Curves.Select(c => c.Binding.Attribute));
            Assert.Equal(new[] { 0.25f, -0.5f, -3e-4f, 2e-4f }, clip.m_Curves.Select(c => c.Curve[1].value));
            QueriesShowTheImportedCurves();
        }

        static float Queried(AnimationClip clip, string name)
        {
            foreach (var b in UnityEditor.AnimationUtility.GetCurveBindings(clip))
                if (b.propertyName == name) return UnityEditor.AnimationUtility.GetEditorCurve(clip, b)[1].value;
            return float.NaN;
        }

        static AnimationClip Arms(List<Object> created, bool played)
        {
            var clip = new AnimationClip { frameRate = 30f };
            created.Add(clip);
            foreach (var (name, value) in new[] { ("Left Arm Down-Up", 0.25f), ("Right Arm Down-Up", 0.75f) })
                clip.m_Curves.Add(new ClipCurve
                {
                    Binding = new CurveBinding { Path = "", Attribute = name, ClassId = 95 },
                    Curve = AnimationCurve.Linear(0f, 0f, 1f, value),
                });
            clip.m_Mirror = true;
            if (played) clip.EnsureHumanCurves();
            return clip;
        }

        /// <summary>An edit of a clip with the Mirror setting names the curves the queries show,
        /// whether the clip has played or not: what is set is read back, the other curve stays,
        /// there is no second curve of the name, and the queries say the same after the next use
        /// and after another edit. Through SetEditorCurve and through SetCurve, with and without
        /// a query before the edit. Unmeasured in the Editor; the rule keeps a query, an edit and
        /// the next query consistent. Control: once played the stored curves are the mirrored
        /// ones, so a write under the stored names would replace the other arm's curve.</summary>
        [Theory]
        [InlineData(false, false, true)]
        [InlineData(false, true, true)]
        [InlineData(true, false, true)]
        [InlineData(true, true, true)]
        [InlineData(true, false, false)]
        [InlineData(true, true, false)]
        public void AnEditOfAMirroredClipIsReadBack(bool throughSetCurve, bool played, bool queriedFirst)
        {
            var clip = Arms(m_Created, played);
            if (played) Assert.Equal(new[] { 0.25f, 0.75f }, new[] { "Right Arm Down-Up", "Left Arm Down-Up" }.Select(n => clip.m_Curves.Single(c => c.Binding.Attribute == n).Curve[1].value));
            if (queriedFirst) Assert.Equal(0.25f, Queried(clip, "Left Arm Down-Up"));

            void Set(float value)
            {
                var curve = AnimationCurve.Linear(0f, 0f, 1f, value);
                if (throughSetCurve) clip.SetCurve("", typeof(Animator), "Left Arm Down-Up", curve);
                else UnityEditor.AnimationUtility.SetEditorCurve(clip, UnityEditor.EditorCurveBinding.FloatCurve("", typeof(Animator), "Left Arm Down-Up"), curve);
            }

            void QueriesShow(float left)
            {
                Assert.Equal(new[] { "Left Arm Down-Up", "Right Arm Down-Up" },
                    UnityEditor.AnimationUtility.GetCurveBindings(clip).Select(b => b.propertyName).OrderBy(n => n, StringComparer.Ordinal));
                Assert.Equal(left, Queried(clip, "Left Arm Down-Up"));
                Assert.Equal(0.75f, Queried(clip, "Right Arm Down-Up"));
            }

            Set(0.5f);
            QueriesShow(0.5f);
            clip.EnsureHumanCurves();
            QueriesShow(0.5f);
            Set(0.6f);
            QueriesShow(0.6f);
        }

        /// <summary>Removing a curve of a played mirrored clip removes the one the queries show
        /// under that name.</summary>
        [Fact]
        public void ARemovalFromAMirroredClipTakesTheQueriedCurve()
        {
            var clip = Arms(m_Created, played: true);
            var left = UnityEditor.AnimationUtility.GetCurveBindings(clip).Single(b => b.propertyName == "Left Arm Down-Up");
            UnityEditor.AnimationUtility.SetEditorCurve(clip, left, null);
            Assert.Equal(new[] { "Right Arm Down-Up" }, UnityEditor.AnimationUtility.GetCurveBindings(clip).Select(b => b.propertyName));
            Assert.Equal(0.75f, Queried(clip, "Right Arm Down-Up"));
        }
    }
}
