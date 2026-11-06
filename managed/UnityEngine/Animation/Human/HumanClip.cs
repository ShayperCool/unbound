using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Unbound.Runtime.Humanoid;

namespace UnityEngine
{
    /// <summary>A Humanoid model's clip: its transform curves become muscle curves of the model's
    /// avatar the first time it is bound, as the model importer converts them.</summary>
    public sealed partial class AnimationClip
    {
        /// <summary>The avatar a Humanoid model's clip is converted with (its model's, or the one it
        /// copies).</summary>
        internal Avatar m_HumanAvatar;
        const int AnimatorClassId = 95;
        bool m_HumanConverted;

        /// <summary>Whether the clip animates a human (root, goal or muscle curves on the Animator);
        /// MotionT/MotionQ alone are a generic root node's motion.</summary>
        internal bool HasHumanCurves
        {
            get
            {
                EnsureHumanCurves();
                foreach (var c in m_Curves)
                    if (c.Binding.ClassId == AnimatorClassId && HumanClipImport.CurveIndex(c.Binding.Attribute) >= HumanClipImport.RootFirst) return true;
                return false;
            }
        }

        public bool humanMotion => HasHumanCurves;

        HumanClipMotion m_HumanClipMotion;
        int m_HumanClipMotionVersion = -1;
        bool[] m_HumanPresent;
        ClipCurve[] m_HumanCurves;

        /// <summary>Per muscle curve index, the clip's curve (null when it has none).</summary>
        internal ClipCurve[] HumanCurves
        {
            get
            {
                EnsureHumanCurves();
                if (m_HumanCurves != null && m_HumanClipMotionVersion == m_Version) return m_HumanCurves;
                m_HumanCurves = new ClipCurve[HumanClipImport.CurveCount];
                m_HumanPresent = new bool[HumanClipImport.CurveCount];
                foreach (var c in m_Curves)
                {
                    if (c.Binding.ClassId != AnimatorClassId) continue;
                    int i = HumanClipImport.CurveIndex(c.Binding.Attribute);
                    if (i < 0) continue;
                    m_HumanCurves[i] = c;
                    m_HumanPresent[i] = true;
                }
                m_HumanClipMotion = null;
                m_HumanClipMotionVersion = m_Version;
                return m_HumanCurves;
            }
        }

        internal bool[] HumanPresent
        {
            get
            {
                _ = HumanCurves;
                return m_HumanPresent;
            }
        }

        /// <summary>The muscle curves' values at a clip time.</summary>
        internal void SampleHuman(float time, float[] values)
        {
            var curves = HumanCurves;
            for (int i = 0; i < curves.Length; i++)
                values[i] = curves[i] != null ? curves[i].Evaluate(time) : Unbound.Runtime.Humanoid.AnimatorHuman.DefaultValue(i);
        }

        /// <summary>The clip's root motion constants (humanoid clips).</summary>
        internal HumanClipMotion HumanClipMotion
        {
            get
            {
                if (!HasHumanCurves) return null;
                var curves = HumanCurves;
                if (m_HumanClipMotion != null) return m_HumanClipMotion;
                var settings = HumanSettings();
                float start = StartTime, stop = StartTime + length;
                m_HumanClipMotion = HumanClipMotion.Build(SampleHuman, m_HumanPresent, start, stop, settings);
                return m_HumanClipMotion;
            }
        }

        /// <summary>The clip's settings (AnimationClipSettings, from the model's meta or the
        /// `.anim` file) as the humanoid conversion and root motion read them.</summary>
        HumanClipSettings HumanSettings()
        {
            var settings = HumanClipSettings.Default;
            settings.LoopTime = m_LoopTime;
            settings.LoopBlend = m_LoopBlend;
            settings.CycleOffset = m_CycleOffset;
            settings.LoopBlendOrientation = m_LoopBlendOrientation;
            settings.LoopBlendPositionY = m_LoopBlendPositionY;
            settings.LoopBlendPositionXZ = m_LoopBlendPositionXZ;
            settings.KeepOriginalOrientation = m_KeepOriginalOrientation;
            settings.KeepOriginalPositionY = m_KeepOriginalPositionY;
            settings.KeepOriginalPositionXZ = m_KeepOriginalPositionXZ;
            settings.HeightFromFeet = m_HeightFromFeet;
            settings.Mirror = m_Mirror;
            settings.OrientationOffsetY = m_OrientationOffsetY;
            settings.Level = m_Level;
            return settings;
        }

                internal override bool TryHumanMotion(out HumanClipMotion motion)
        {
            motion = HasHumanCurves ? HumanClipMotion : null;
            return motion != null;
        }

        internal override bool GetIsHumanMotion() => HasHumanCurves;

        /// <summary>The humanoid curves in m_Curves are mirrored.</summary>
        internal bool m_HumanMirrored;

        /// <summary>Converts a Humanoid model's transform curves into muscle curves (once), then
        /// mirrors the humanoid curves of a mirrored clip (again when the setting changes back:
        /// mirroring twice gives the curves back).</summary>
        internal void EnsureHumanCurves()
        {
            ConvertHumanCurves();
            if (m_HumanMirrored == m_Mirror || (m_HumanAvatar != null && !m_HumanConverted)) return;
            m_HumanMirrored = m_Mirror;
            MirrorHumanCurves();
        }

        AnimationClip m_StateMirrored;
        int m_StateMirroredVersion;
        /// <summary>The clip a <see cref="StateMirrored"/> copy plays for.</summary>
        AnimationClip m_StateMirrorSource;

        /// <summary>The clip as game code sees it: a mirrored state's playback copy stands for the
        /// clip it was made from (events, clip infos, parameter curves).</summary>
        internal AnimationClip Asset => m_StateMirrorSource ?? this;

        /// <summary>The <see cref="StateMirrored"/> copy if one was made, else null (without making one).</summary>
        internal AnimationClip StateMirroredIfMade => m_StateMirrored;

        /// <summary>The humanoid clip as a mirrored Animator state or blend tree child plays it: its
        /// Mirror setting toggled, so a mirrored clip plays unmirrored (measured,
        /// tests/projects/humanoid).</summary>
        internal AnimationClip StateMirrored
        {
            get
            {
                EnsureHumanCurves();
                if (m_StateMirrored != null && m_StateMirroredVersion == m_Version) return m_StateMirrored;
                var clip = Clone();
                // A playback copy, not an object of its own (FindObjectsOfTypeAll, instance IDs).
                Unbound.Runtime.ObjectRegistry.Unregister(clip);
                clip.m_StateMirrorSource = this;
                clip.m_Mirror = !m_Mirror;
                // On an additive layer it adds its difference from the clip's own reference pose,
                // unmirrored (measured for an unmirrored clip and its first frame).
                if (!(m_HasAdditiveReferencePose && m_AdditiveReferencePoseClip != null))
                {
                    clip.m_HasAdditiveReferencePose = true;
                    clip.m_AdditiveReferencePoseClip = this;
                    clip.m_AdditiveReferencePoseTime = m_HasAdditiveReferencePose ? m_AdditiveReferencePoseTime : 0f;
                }
                clip.EnsureHumanCurves();
                m_StateMirrored = clip;
                m_StateMirroredVersion = m_Version;
                return clip;
            }
        }

        /// <summary>Mirror: each humanoid curve plays on its <see cref="HumanClipImport.MirrorCurve"/>,
        /// as a new curve (negated where that says so; measured, tests/projects/humanoid).</summary>
        void MirrorHumanCurves()
        {
            bool changed = false;
            var goalRotations = new int[4];
            for (int k = 0; k < m_Curves.Count; k++)
            {
                var c = m_Curves[k];
                int i = c.Binding.ClassId == AnimatorClassId && c.Curve != null ? HumanClipImport.CurveIndex(c.Binding.Attribute) : -1;
                if (i < 0) continue;
                if (i >= HumanClipImport.TDoFFirst)
                    throw new NotSupportedException($"AnimationClip '{name}': mirrored humanoid clips with translation DoF curves are not supported");
                if (i >= HumanClipImport.GoalFirst && i < HumanClipImport.MuscleFirst && (i - HumanClipImport.GoalFirst) % 7 >= 3)
                    goalRotations[(i - HumanClipImport.GoalFirst) / 7]++;
                int j = HumanClipImport.MirrorCurve[i];
                if (j == i) continue;
                float sign = j < 0 ? -1f : 1f;
                if (j < 0) j = ~j;
                var keys = c.Curve.keys;
                for (int n = 0; n < keys.Length; n++)
                {
                    keys[n].value *= sign;
                    keys[n].inTangent *= sign;
                    keys[n].outTangent *= sign;
                }
                var b = c.Binding;
                m_Curves[k] = new ClipCurve
                {
                    Binding = new CurveBinding { Path = b.Path, Attribute = HumanClipImport.CurveNames[j], ClassId = b.ClassId },
                    Curve = new AnimationCurve(keys) { preWrapMode = c.Curve.preWrapMode, postWrapMode = c.Curve.postWrapMode },
                };
                changed = true;
            }
            if (changed) m_Version++;
            // A goal rotation's components trade places: one without all four curves mixes in defaults.
            if (Array.Exists(goalRotations, n => n != 0 && n != 4))
                Unbound.Runtime.Stubs.Ignored("AnimationClip mirrored humanoid goal rotation without all four curves",
                    "a mirrored humanoid clip's goal rotation with some of its curves takes the defaults for the others before mirroring (not measured)");
        }

        /// <summary>A humanoid body curve (RootT, RootQ): loop pose moves the body relative to the
        /// motion instead (<see cref="HumanClipMotion.LoopBlendX"/>), not the curve's values.</summary>
        static bool IsHumanBodyCurve(ClipCurve c)
        {
            if (c.Binding.ClassId != AnimatorClassId) return false;
            int i = HumanClipImport.CurveIndex(c.Binding.Attribute);
            return i >= HumanClipImport.RootFirst && i < HumanClipImport.GoalFirst;
        }

        /// <summary>Converts a Humanoid model's transform curves into muscle curves (once).</summary>
        void ConvertHumanCurves()
        {
            if (m_HumanConverted || m_HumanAvatar == null) return;
            m_HumanConverted = true;
            var constant = m_HumanAvatar.Constant;
            if (constant == null || !constant.IsHuman)
            {
                Debug.LogWarning($"AnimationClip '{name}': its avatar '{m_HumanAvatar.name}' is not a valid human; the clip keeps its transform curves");
                return;
            }
            var skel = constant.AvatarSkeleton;
            // The transform curves per avatar node: position (3), rotation (4), scale (3).
            var position = new Dictionary<int, AnimationCurve[]>();
            var rotation = new Dictionary<int, AnimationCurve[]>();
            var scale = new Dictionary<int, AnimationCurve[]>();
            float start = float.MaxValue, stop = float.MinValue;
            var kept = new List<ClipCurve>();
            foreach (var c in m_Curves)
            {
                var b = c.Binding;
                int node = b.ClassId == EngineTypes.TransformId && c.Curve != null ? skel.FindNode(HashedBindings.Crc32(b.Path)) : -1;
                Dictionary<int, AnimationCurve[]> group = null;
                int component = -1, width = 3;
                if (node >= 0)
                {
                    var a = b.Attribute;
                    if (a.StartsWith("m_LocalPosition.", StringComparison.Ordinal)) group = position;
                    else if (a.StartsWith("m_LocalRotation.", StringComparison.Ordinal)) { group = rotation; width = 4; }
                    else if (a.StartsWith("m_LocalScale.", StringComparison.Ordinal)) group = scale;
                    if (group != null) component = "xyzw".IndexOf(a[a.Length - 1]);
                }
                if (group == null || component < 0)
                {
                    kept.Add(c);
                    continue;
                }
                if (!group.TryGetValue(node, out var curves)) group[node] = curves = new AnimationCurve[width];
                curves[component] = c.Curve;
                int n = c.Curve.length;
                if (n > 0)
                {
                    start = Math.Min(start, c.Curve[0].time);
                    stop = Math.Max(stop, c.Curve[n - 1].time);
                }
                // Curves of the human skeleton's nodes become muscle curves; the others stay.
                if (constant.HumanSkeletonReverseIndexArray[node] < 0) kept.Add(c);
            }
            if (!(start < stop)) return;
            var times = HumanClipImport.SampleTimes(start, stop, m_SampleRate, 1);
            var settings = HumanSettings();
            var curvesOut = HumanClipImport.Convert(constant, times, (frame, time, pose) =>
            {
                foreach (var (node, c) in position) pose.X[node].t = Sample(c, time, pose.X[node].t);
                foreach (var (node, c) in scale) pose.X[node].s = Sample(c, time, pose.X[node].s);
                foreach (var (node, c) in rotation)
                {
                    var q = pose.X[node].q;
                    var v = new F4(c[0]?.Evaluate(time) ?? q.x, c[1]?.Evaluate(time) ?? q.y, c[2]?.Evaluate(time) ?? q.z, c[3]?.Evaluate(time) ?? q.w);
                    pose.X[node].q = HumanClipImport.NormalizeCurveQ(v);
                }
            }, settings);
            m_Curves = kept;
            for (int i = 0; i < HumanClipImport.CurveCount; i++)
            {
                var values = curvesOut.Values[i];
                if (values == null) continue;
                var slopes = curvesOut.Slopes[i];
                var keys = new Keyframe[values.Length];
                for (int k = 0; k < keys.Length; k++)
                    keys[k] = new Keyframe(curvesOut.Times[k], values[k], slopes[k], slopes[k], 1f / 3f, 1f / 3f);
                m_Curves.Add(new ClipCurve
                {
                    Binding = new CurveBinding { Path = "", Attribute = HumanClipImport.CurveNames[i], ClassId = AnimatorClassId },
                    Curve = new AnimationCurve(keys),
                });
            }
            m_Version++;
        }

        static F4 Sample(AnimationCurve[] c, float time, F4 rest) =>
            new F4(c[0]?.Evaluate(time) ?? rest.x, c[1]?.Evaluate(time) ?? rest.y, c[2]?.Evaluate(time) ?? rest.z, 0f);
    }
}
