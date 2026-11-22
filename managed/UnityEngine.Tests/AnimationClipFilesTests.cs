using System.Collections.Generic;
using System.IO;
using Unbound.Runtime;
using UnityEditor;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Animation clips made by script and written as the Editor writes them when made assets
    /// (Unity 6000.0.71f1, tests/projects/scenesave clip-assets): the reference clips that name no
    /// project script or asset, made the same way, byte for byte, and the clips Unbound refuses.</summary>
    public class AnimationClipFilesTests
    {
        static string Repo([System.Runtime.CompilerServices.CallerFilePath] string source = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(source), "..", ".."));

        static string Reference(string name) => File.ReadAllText(Path.Combine(Repo(), "tests", "projects", "scenesave", "Reference",
            "unity-6000.0.71f1", "clip-assets", "files", "Assets", "Clips", name + ".anim"));

        static string Write(AnimationClip clip, string name)
        {
            clip.name = name;
            return UnityYamlWriter.Write(new Object[] { clip }, new Dictionary<Object, long>(ReferenceEqualityComparer.Instance) { [clip] = 7400000 });
        }

        [Fact]
        public void AnEmptyClip()
        {
            var clip = new AnimationClip();
            try { Assert.Equal(Reference("Empty"), Write(clip, "Empty")); }
            finally { Object.DestroyImmediate(clip); }
        }

        /// <summary>SetCurve alone: Transform vectors keyed together, no binding table.</summary>
        [Fact]
        public void ConstantCurvesSetByScript()
        {
            var clip = new AnimationClip();
            try
            {
                clip.SetCurve("", typeof(GameObject), "m_IsActive", AnimationCurve.Constant(0f, 1f, 1f));
                clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Constant(0f, 1f, 2f));
                clip.SetCurve("", typeof(Light), "m_Intensity", AnimationCurve.Constant(0f, 1f, 0.5f));
                clip.SetCurve("", typeof(Light), "m_Range", AnimationCurve.Linear(0f, 1f, 1f, 5f));
                Assert.Equal(Reference("Constants"), Write(clip, "Constants"));
            }
            finally { Object.DestroyImmediate(clip); }
        }

        /// <summary>Editor curves: a single component of a Transform vector filled with the others'
        /// defaults, blend shape and material bindings, a discrete curve without a binding.</summary>
        [Fact]
        public void CurvesSetThroughTheEditorCurveApi()
        {
            var clip = new AnimationClip();
            try
            {
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Transform), "localEulerAnglesRaw.y"), AnimationCurve.Linear(0f, 0f, 1f, 180f));
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Child", typeof(Transform), "m_LocalPosition.z"), AnimationCurve.Linear(0f, 0f, 1f, 4f));
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(MeshRenderer), "material._Color.r"), AnimationCurve.Linear(0f, 1f, 1f, 0f));
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Body", typeof(SkinnedMeshRenderer), "blendShape.Smile"), AnimationCurve.Linear(0f, 0f, 1f, 100f));
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.DiscreteCurve("", typeof(SpriteRenderer), "m_SortingOrder"), AnimationCurve.Constant(0f, 1f, 3f));
                Assert.Equal(Reference("Editor"), Write(clip, "Editor"));
            }
            finally { Object.DestroyImmediate(clip); }
        }

        /// <summary>Material properties' binding attributes by component (vector, color, float).</summary>
        [Fact]
        public void MaterialPropertyCurves()
        {
            var clip = new AnimationClip();
            try
            {
                foreach (var property in new[] { "_Color.g", "_Color.b", "_Color.a", "_Glossiness" })
                    clip.SetCurve("", typeof(MeshRenderer), "material." + property, AnimationCurve.Linear(0f, 0f, 1f, 1f));
                clip.SetCurve("", typeof(MeshRenderer), "material._MainTex_ST.x", AnimationCurve.Linear(0f, 1f, 1f, 2f));
                clip.SetCurve("", typeof(MeshRenderer), "material._MainTex_ST.y", AnimationCurve.Linear(0f, 1f, 1f, 2f));
                clip.SetCurve("", typeof(MeshRenderer), "material._MainTex_ST.z", AnimationCurve.Linear(0f, 0f, 1f, 1f));
                clip.SetCurve("", typeof(MeshRenderer), "material._MainTex_ST.w", AnimationCurve.Linear(0f, 0f, 1f, 1f));
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(MeshRenderer), "material._EmissionColor.r"), AnimationCurve.Linear(0f, 0f, 1f, 1f));
                Assert.Equal(Reference("Materials"), Write(clip, "Materials"));
            }
            finally { Object.DestroyImmediate(clip); }
        }

        /// <summary>What Unbound does not write yet is refused with its reason: a built clip naming a
        /// binding whose form is not measured (the same clip unbuilt is written), Animator curves,
        /// SetCurve Transform vectors beside editor curves.</summary>
        [Fact]
        public void UnmeasuredClipsAreRefused()
        {
            var clip = new AnimationClip();
            try
            {
                clip.SetCurve("", typeof(ParticleSystemRenderer), "m_Enabled", AnimationCurve.Linear(0f, 0f, 1f, 1f));
                Assert.Null(AnimationClipFiles.Unwritable(clip));
                AnimationUtility.SetAnimationEvents(clip, new[] { new AnimationEvent { time = 0.5f, functionName = "OnHit" } });
                Assert.Contains("not measured", AnimationClipFiles.Unwritable(clip));
                Assert.False(UnityYamlWriter.WritesNew(clip));

                clip.ClearCurves();
                clip.SetCurve("", typeof(Animator), "Speed", AnimationCurve.Linear(0f, 0f, 1f, 1f));
                Assert.Contains("Animator", AnimationClipFiles.Unwritable(clip));

                clip.ClearCurves();
                clip.SetCurve("", typeof(Transform), "localPosition.x", AnimationCurve.Linear(0f, 0f, 1f, 1f));
                AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("", typeof(Light), "m_Intensity"), AnimationCurve.Linear(0f, 0f, 1f, 1f));
                Assert.Contains("SetEditorCurve", AnimationClipFiles.Unwritable(clip));

                // A RectTransform's scale, a discrete curve other than the measured one in a built
                // clip, a built legacy clip.
                var rect = new AnimationClip();
                var discrete = new AnimationClip();
                var legacy = new AnimationClip { legacy = true };
                try
                {
                    rect.SetCurve("UI", typeof(RectTransform), "localScale.x", AnimationCurve.Linear(0f, 1f, 1f, 2f));
                    Assert.Contains("RectTransform", AnimationClipFiles.Unwritable(rect));
                    AnimationUtility.SetEditorCurve(discrete, EditorCurveBinding.DiscreteCurve("", typeof(SpriteRenderer), "m_SortingLayerID"), AnimationCurve.Constant(0f, 1f, 1f));
                    Assert.Contains("discrete", AnimationClipFiles.Unwritable(discrete));
                    legacy.SetCurve("", typeof(Transform), "localPosition.y", AnimationCurve.Linear(0f, 0f, 1f, 1f));
                    Assert.Null(AnimationClipFiles.Unwritable(legacy));
                    AnimationUtility.SetAnimationEvents(legacy, new[] { new AnimationEvent { time = 0.5f, functionName = "OnHit" } });
                    Assert.Contains("legacy", AnimationClipFiles.Unwritable(legacy));
                }
                finally
                {
                    Object.DestroyImmediate(rect);
                    Object.DestroyImmediate(discrete);
                    Object.DestroyImmediate(legacy);
                }
            }
            finally { Object.DestroyImmediate(clip); }
        }
    }
}
