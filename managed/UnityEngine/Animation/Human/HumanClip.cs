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
        /// <summary>A model clip's start on its take (seconds; 0 for other clips): its curves' key
        /// times are the take's less this.</summary>
        internal float m_TakeStart;
        const int AnimatorClassId = CurveBinding.AnimatorClassId;

        /// <summary>The index of a curve among the humanoid curves (-1: none): a curve on the
        /// Animator whose name is a humanoid curve's, at any path (a path below the root is
        /// unmeasured).</summary>
        static int HumanCurveIndexOf(CurveBinding b) =>
            b.ClassId == AnimatorClassId ? HumanClipImport.CurveIndex(b.Attribute) : -1;
        bool m_HumanConverted;

        /// <summary>Whether the clip animates a human (root, goal or muscle curves on the Animator);
        /// MotionT/MotionQ alone are a generic root node's motion.</summary>
        internal bool HasHumanCurves
        {
            get
            {
                EnsureHumanCurves();
                foreach (var c in m_Curves)
                    if (HumanCurveIndexOf(c.Binding) >= HumanClipImport.RootFirst) return true;
                return false;
            }
        }

        /// <summary>AnimationClip.humanMotion: the clip animates a human beyond its body's position
        /// and rotation (goal, muscle, finger or translation curves). Body curves (RootT/RootQ)
        /// alone, as Editor-authored generic clips with root motion carry them, do not count (Unity
        /// 6000.0.71f1, tests/projects/rootcurves stats.*).</summary>
        public bool humanMotion
        {
            get
            {
                EnsureHumanCurves();
                foreach (var c in m_Curves)
                    if (HumanCurveIndexOf(c.Binding) >= HumanClipImport.GoalFirst) return true;
                return false;
            }
        }

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
                    int i = HumanCurveIndexOf(c.Binding);
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

        /// <summary>Whether the clip has body curves (RootT, RootQ) and no root node motion
        /// (MotionT, MotionQ): the motion a generic Animator's root takes from it.</summary>
        internal bool HasBodyMotion
        {
            get
            {
                var present = HumanPresent;
                for (int c = 0; c < HumanClipImport.RootFirst; c++)
                    if (present[c]) return false;
                return HumanRootMotion.HasRoot(present);
            }
        }

        /// <summary>The clip's body curves move <paramref name="animator"/>'s root: it has
        /// <see cref="HasBodyMotion"/> and the Animator no human, whether it plays the clip through
        /// its controller, a controller playable or a clip playable (Unity 6000.0.71f1,
        /// tests/projects/rootcurves).</summary>
        internal bool BodyMovesRoot(Animator animator) => animator.Human == null && HasBodyMotion;

        float[] m_BodyValues;

        /// <summary>The body's motion at a clip time (<see cref="HumanRootMotion.MotionX"/>: its
        /// position and yaw with the clip's Bake Into Pose and Based Upon settings), in the clip's
        /// units. Only for clips with <see cref="HasBodyMotion"/>.</summary>
        internal (Vector3, Quaternion) BodyMotionPose(float time)
        {
            var values = m_BodyValues ??= new float[HumanClipImport.CurveCount];
            SampleHuman(time, values);
            var present = HumanPresent;
            // SampleHuman refreshed the curves for this version: the constants are current if built.
            var motion = m_HumanClipMotion ?? HumanClipMotion;
            var body = HumanRootMotion.ReadRoot(values, present);
            var x = HumanRootMotion.MotionX(motion, body.t, body.q, motion.HeightFromFeet ? HumanRootMotion.FeetY(values, present, body) : 0f);
            return (new Vector3(x.t.x, x.t.y, x.t.z), new Quaternion(x.q.x, x.q.y, x.q.z, x.q.w));
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
            settings.PartsOff = m_HumanPartsOff;
            return settings;
        }

                internal override bool TryHumanMotion(out HumanClipMotion motion)
        {
            motion = HasHumanCurves ? HumanClipMotion : null;
            return motion != null;
        }

        internal override bool GetIsHumanMotion() => humanMotion;

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
            if (m_Mirror) NoteMirroredWithoutFootGoals();
        }

        /// <summary>Says once, when a humanoid clip without a foot's goal curves is mirrored, that
        /// played with Foot IK it places its legs differently from the Editor (6000.0.71f1, an exploratory capture
        /// of tests/projects/humanoid apiCurves.mirror with foot IK, kept as the known class
        /// mirror-no-goals of apiCurves.mirrorFootIK: leg muscles and the body position differ,
        /// the unmirrored clip matches; the cause is not found).</summary>
        void NoteMirroredWithoutFootGoals()
        {
            bool any = false, left = false, right = false;
            foreach (var c in m_Curves)
            {
                int i = c.Curve != null ? HumanCurveIndexOf(c.Binding) : -1;
                if (i < 0) continue;
                any = true;
                left |= i >= HumanClipImport.GoalFirst && i < HumanClipImport.GoalFirst + 7;
                right |= i >= HumanClipImport.GoalFirst + 7 && i < HumanClipImport.GoalFirst + 14;
            }
            // Only the clip without either foot's goals is measured; one foot's goals alone is not.
            if (any && !(left && right))
                Unbound.Runtime.Stubs.Ignored("AnimationClip mirrored humanoid clip without foot goal curves",
                    "places its legs differently from the Editor if played with Foot IK (measured without either foot's goals, not matched)");
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
                int i = c.Curve != null ? HumanCurveIndexOf(c.Binding) : -1;
                if (i < 0) continue;
                if (i >= HumanClipImport.GoalFirst && i < HumanClipImport.MuscleFirst && (i - HumanClipImport.GoalFirst) % 7 >= 3)
                    goalRotations[(i - HumanClipImport.GoalFirst) / 7]++;
                var mirrored = MirroredHumanCurve(c, i);
                if (ReferenceEquals(mirrored, c)) continue;
                m_Curves[k] = mirrored;
                changed = true;
            }
            if (changed) m_Version++;
            // A goal rotation's components trade places: one without all four curves mixes in defaults.
            if (Array.Exists(goalRotations, n => n != 0 && n != 4))
                Unbound.Runtime.Stubs.Ignored("AnimationClip mirrored humanoid goal rotation without all four curves",
                    "a mirrored humanoid clip's goal rotation with some of its curves takes the defaults for the others before mirroring (not measured)");
        }

        /// <summary>A humanoid curve mirrored: the curve of the other side's name, negated where
        /// the mirror flips it; the curve itself when it mirrors onto itself unchanged.</summary>
        /// <param name="index">The curve's index among the humanoid curves.</param>
        static ClipCurve MirroredHumanCurve(ClipCurve c, int index)
        {
            int j = HumanClipImport.MirrorCurve[index];
            if (j == index) return c;
            float sign = j < 0 ? -1f : 1f;
            if (j < 0) j = ~j;
            var keys = c.Curve.keys;
            for (int n = 0; n < keys.Length; n++) keys[n] = AnimationCurve.Negated(keys[n], sign);
            return new ClipCurve
            {
                Binding = c.Binding.WithAttribute(HumanClipImport.CurveNames[j]),
                Curve = new AnimationCurve(keys) { preWrapMode = c.Curve.preWrapMode, postWrapMode = c.Curve.postWrapMode },
            };
        }

        /// <summary>The index of a stored curve among the humanoid curves, or -1.</summary>
        static int HumanCurveIndex(ClipCurve c) =>
            c.Curve != null ? HumanCurveIndexOf(c.Binding) : -1;

        /// <summary>The clip's float curves as the Editor's curve queries show them: a clip with
        /// the Mirror setting shows its curves as imported, not mirrored, after it has played
        /// mirrored (tests/projects/humanoid tdof.curves, the clip named Mirror). Mirroring a
        /// curve twice gives it back, so the stored mirrored curves are mirrored again for the
        /// view. Before the clip plays the stored curves are the imported ones.</summary>
        internal List<ClipCurve> CurvesAsImported()
        {
            ConvertHumanCurves();
            if (!m_HumanMirrored) return m_Curves;
            var view = new List<ClipCurve>(m_Curves.Count);
            foreach (var c in m_Curves)
            {
                int i = HumanCurveIndex(c);
                view.Add(i < 0 ? c : MirroredHumanCurve(c, i));
            }
            return view;
        }

        /// <summary>One curve of <see cref="CurvesAsImported"/> by its path, name and type, or
        /// null: a mirrored clip's is found under its mirror name and only that one is mirrored
        /// back.</summary>
        internal ClipCurve CurveAsImported(string path, string attribute, Type type)
        {
            ConvertHumanCurves();
            foreach (var c in m_Curves)
            {
                if (c.Binding.Path != path) continue;
                int i = m_HumanMirrored ? HumanCurveIndex(c) : -1;
                if (i < 0)
                {
                    if (c.Binding.Attribute == attribute && c.Binding.ResolveType() == type) return c;
                    continue;
                }
                int j = HumanClipImport.MirrorCurve[i];
                if (HumanClipImport.CurveNames[j < 0 ? ~j : j] == attribute && c.Binding.ResolveType() == type) return MirroredHumanCurve(c, i);
            }
            return null;
        }

        /// <summary>Puts the stored curves back as imported before they are edited: a clip that
        /// has played mirrored holds its curves mirrored, and an edit names the imported ones
        /// (what the curve queries show). The next use mirrors them again.</summary>
        internal void RestoreImportedCurves()
        {
            if (!m_HumanMirrored) return;
            MirrorHumanCurves();
            m_HumanMirrored = false;
        }

        /// <summary>A humanoid body curve (RootT, RootQ): loop pose moves the body relative to the
        /// motion instead (<see cref="HumanClipMotion.LoopBlendX"/>), not the curve's values.</summary>
        static bool IsHumanBodyCurve(ClipCurve c)
        {
            int i = HumanCurveIndexOf(c.Binding);
            return i >= HumanClipImport.RootFirst && i < HumanClipImport.GoalFirst;
        }

        /// <summary>Whether a Humanoid model clip keeps the curves of a transform that is not a
        /// bone of the human skeleton (<see cref="m_HumanTransformPaths"/>).</summary>
        bool KeepsTransformCurves(string path) => m_HumanTransformPaths == null || m_HumanTransformPaths.Contains(path);

        /// <summary>Converts a Humanoid model's transform curves into muscle curves (once).</summary>
        internal void ConvertHumanCurves()
        {
            if (m_HumanConverted || m_HumanAvatar == null) return;
            m_HumanConverted = true;
            ConvertHumanCurvesOnce();
        }

        // Separate from the per-sample guard above: its lambdas capture locals, whose closure
        // would otherwise be allocated on every call.
        void ConvertHumanCurvesOnce()
        {
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
                    // Curves of other kinds stay; a transform outside the avatar's skeleton
                    // keeps its curves like any transform that is not a human bone.
                    if (b.ClassId != EngineTypes.TransformId || KeepsTransformCurves(b.Path)) kept.Add(c);
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
                // Curves of the human skeleton's nodes become muscle curves; of the others, the
                // ones the take animates and the clip's mask leaves on stay.
                if (constant.HumanSkeletonReverseIndexArray[node] < 0 && KeepsTransformCurves(b.Path)) kept.Add(c);
            }
            if (!(start < stop)) return;
            // The samples span the curves' keys, stepping on the take's times from the clip's
            // start (see HumanClipImport.SampleTimes). For some counts the span, divided by the
            // frame length, rounds past the count, and there is one sample more: 31 frames at 30
            // a second are sampled 33 times (tests/projects/modelclips HumanLong; other lengths in
            // HumanClipSampleRangeTests).
            var times = HumanClipImport.SampleTimes(start, stop, m_SampleRate, 1, m_TakeStart);
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
            // A sample after the last key shapes that key's slope (the mean of the slopes on
            // both sides, the far one flat) and gives no key itself.
            int inRange = times.Length;
            while (inRange > 0 && times[inRange - 1] > stop) inRange--;
            curvesOut.Keep(inRange);
            m_Curves = kept;
            var keptKeys = m_HumanKeyReduction > 0f ? new List<int>() : null;
            for (int i = 0; i < HumanClipImport.CurveCount; i++)
            {
                var values = curvesOut.Values[i];
                if (values == null) continue;
                if (i >= HumanClipImport.TDoFFirst && !HumanClipImport.KeepsTDoFCurve(values)) continue;
                var keys = HumanClipReduction.Keys(curvesOut.Times, values, curvesOut.Slopes[i], 1f / m_SampleRate,
                    m_HumanKeyReduction, keptKeys);
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
