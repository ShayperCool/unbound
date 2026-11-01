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

        /// <summary>Whether the clip animates a human (muscle curves on the Animator).</summary>
        internal bool HasHumanCurves
        {
            get
            {
                EnsureHumanCurves();
                foreach (var c in m_Curves)
                    if (c.Binding.ClassId == AnimatorClassId && HumanClipImport.CurveIndex(c.Binding.Attribute) >= 0) return true;
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
                var settings = HumanClipSettings.Default;
                settings.LoopTime = m_LoopTime;
                settings.LoopBlend = m_LoopBlend;
                settings.CycleOffset = m_CycleOffset;
                float start = StartTime, stop = StartTime + length;
                m_HumanClipMotion = HumanClipMotion.Build(SampleHuman, m_HumanPresent, start, stop, settings);
                return m_HumanClipMotion;
            }
        }

        internal override bool TryHumanMotion(out HumanClipMotion motion)
        {
            motion = HasHumanCurves ? HumanClipMotion : null;
            return motion != null;
        }

        internal override bool GetIsHumanMotion() => HasHumanCurves;

        /// <summary>Converts a Humanoid model's transform curves into muscle curves (once).</summary>
        internal void EnsureHumanCurves()
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
            var settings = HumanClipSettings.Default;
            settings.LoopTime = m_LoopTime;
            settings.LoopBlend = m_LoopBlend;
            settings.CycleOffset = m_CycleOffset;
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
