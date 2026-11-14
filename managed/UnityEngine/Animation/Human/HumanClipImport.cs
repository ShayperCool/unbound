using System;

namespace Unbound.Runtime.Humanoid
{
    /// <summary>The settings of a humanoid clip import. The model importer's muscle conversion only
    /// depends on the oversampling, the root motion node, the clip's range in its take and the body
    /// mask; the root transform settings (keep original, bake into pose, offsets, mirror, loop blend)
    /// are stored in the clip and only change how it is played (see <see cref="HumanClipMotion.Build"/>).</summary>
    internal struct HumanClipSettings
    {
        /// <summary>ModelImporter.humanoidOversampling (1, 2, 4 or 8).</summary>
        public int Oversampling;
        /// <summary>The human description has a root motion node (motionNodeName).</summary>
        public bool HasRootMotionNode;
        /// <summary>The clip is a part of its take (firstFrame/lastFrame inside the take).</summary>
        public bool CroppedFromTake;
        /// <summary>The parts of the humanoid body the clip's body mask turns off, one bit per
        /// AvatarMaskBodyPart: the clip has no curves of them (<see cref="HumanClipImport.Convert"/>).</summary>
        public int PartsOff;

        public bool LoopTime, LoopBlend, LoopBlendOrientation, LoopBlendPositionY, LoopBlendPositionXZ;
        public bool KeepOriginalOrientation, KeepOriginalPositionY, KeepOriginalPositionXZ, HeightFromFeet, Mirror;
        public float OrientationOffsetY, Level, CycleOffset;

        /// <summary>The importer's defaults for a humanoid clip (root position Y based upon the
        /// original, everything else off).</summary>
        public static HumanClipSettings Default => new HumanClipSettings { Oversampling = 1, KeepOriginalPositionY = true };

        /// <summary>Throws for the import options the conversion does not reproduce.</summary>
        public void RequireSupportedImport()
        {
            if (Oversampling < 1)
                throw new NotSupportedException($"humanoid clip import: oversampling {Oversampling} is not valid");
            if (HasRootMotionNode)
                throw new NotSupportedException("humanoid clip import with a root motion node (motion curves) is not supported");
            if (CroppedFromTake)
                throw new NotSupportedException("humanoid clips that are only a part of their take (firstFrame/lastFrame) are not supported");
        }
    }

    /// <summary>A humanoid clip's muscle curves: the key times and, per curve of
    /// <see cref="HumanClipImport.CurveNames"/>, the key values and slopes (null for curves the
    /// clip does not have).</summary>
    internal sealed class HumanClipCurves
    {
        public float[] Times;
        public float[][] Values;
        /// <summary>The keys' in and out slope (they are equal); in and out weights are 1/3 and
        /// the keys are not weighted.</summary>
        public float[][] Slopes;

        /// <summary>Keeps the first <paramref name="count"/> samples of every curve.</summary>
        public void Keep(int count)
        {
            if (count >= Times.Length) return;
            Array.Resize(ref Times, count);
            for (int i = 0; i < Values.Length; i++)
            {
                if (Values[i] != null) Array.Resize(ref Values[i], count);
                if (Slopes[i] != null) Array.Resize(ref Slopes[i], count);
            }
        }
    }

    /// <summary>The model importer's conversion of a Humanoid model's clip (the transform curves
    /// of its avatar's hierarchy) into muscle curves, and the evaluation of muscle curve values into
    /// a human pose, as the Unity 6000.0.71f1 Editor does them (tests/projects/humanoid).</summary>
    internal static class HumanClipImport
    {
        public const int CurveCount = 200;
        /// <summary>Curve index ranges: motion (root motion node) 0..6, root 7..13, goals 14..41
        /// (left foot, right foot, left hand, right hand; T.xyz then Q.xyzw), body muscles 42..96,
        /// left fingers 97..116, right fingers 117..136, translation DoF 137..199.</summary>
        public const int RootFirst = 7, GoalFirst = 14, MuscleFirst = 42, LeftHandFirst = 97, RightHandFirst = 117,
            TDoFFirst = 137, HumanEnd = 137;

        static readonly string[] BodyBoneNames =
        {
            "Hips", "LeftUpperLeg", "RightUpperLeg", "LeftLowerLeg", "RightLowerLeg", "LeftFoot", "RightFoot", "Spine",
            "Chest", "UpperChest", "Neck", "Head", "LeftShoulder", "RightShoulder", "LeftUpperArm", "RightUpperArm",
            "LeftLowerArm", "RightLowerArm", "LeftHand", "RightHand", "LeftToes", "RightToes", "LeftEye", "RightEye", "Jaw",
        };

        static readonly string[] MuscleNames =
        {
            "Spine Front-Back", "Spine Left-Right", "Spine Twist Left-Right", "Chest Front-Back", "Chest Left-Right",
            "Chest Twist Left-Right", "UpperChest Front-Back", "UpperChest Left-Right", "UpperChest Twist Left-Right",
            "Neck Nod Down-Up", "Neck Tilt Left-Right", "Neck Turn Left-Right", "Head Nod Down-Up", "Head Tilt Left-Right",
            "Head Turn Left-Right", "Left Eye Down-Up", "Left Eye In-Out", "Right Eye Down-Up", "Right Eye In-Out", "Jaw Close",
            "Jaw Left-Right", "Left Upper Leg Front-Back", "Left Upper Leg In-Out", "Left Upper Leg Twist In-Out",
            "Left Lower Leg Stretch", "Left Lower Leg Twist In-Out", "Left Foot Up-Down", "Left Foot Twist In-Out",
            "Left Toes Up-Down", "Right Upper Leg Front-Back", "Right Upper Leg In-Out", "Right Upper Leg Twist In-Out",
            "Right Lower Leg Stretch", "Right Lower Leg Twist In-Out", "Right Foot Up-Down", "Right Foot Twist In-Out",
            "Right Toes Up-Down", "Left Shoulder Down-Up", "Left Shoulder Front-Back", "Left Arm Down-Up",
            "Left Arm Front-Back", "Left Arm Twist In-Out", "Left Forearm Stretch", "Left Forearm Twist In-Out",
            "Left Hand Down-Up", "Left Hand In-Out", "Right Shoulder Down-Up", "Right Shoulder Front-Back",
            "Right Arm Down-Up", "Right Arm Front-Back", "Right Arm Twist In-Out", "Right Forearm Stretch",
            "Right Forearm Twist In-Out", "Right Hand Down-Up", "Right Hand In-Out",
        };

        static readonly string[] FingerNames = { "Thumb", "Index", "Middle", "Ring", "Little" };
        static readonly string[] FingerDoFNames = { "1 Stretched", "Spread", "2 Stretched", "3 Stretched" };
        static readonly int[] GoalBones = { Bone.LeftFoot, Bone.RightFoot, Bone.LeftHand, Bone.RightHand };
        /// <summary>The bone of each translation DoF: the spine to the head, the left leg, the right
        /// leg, the left arm (shoulder to hand), the right arm.</summary>
        internal static readonly int[] TDoFBones = { 7, 8, 9, 10, 11, 1, 3, 5, 20, 2, 4, 6, 21, 12, 14, 16, 18, 13, 15, 17, 19 };

        /// <summary>Per body muscle: its bone and the axis (0 x, 1 y, 2 z) of the bone's limits.</summary>
        static readonly int[] MuscleBone =
        {
            7, 7, 7, 8, 8, 8, 9, 9, 9, 10, 10, 10, 11, 11, 11, 22, 22, 23, 23, 24, 24, 1, 1, 1, 3, 3, 5, 5, 20, 2, 2, 2, 4, 4,
            6, 6, 21, 12, 12, 14, 14, 14, 16, 16, 18, 18, 13, 13, 15, 15, 15, 17, 17, 19, 19,
        };
        static readonly int[] MuscleAxis =
        {
            2, 1, 0, 2, 1, 0, 2, 1, 0, 2, 1, 0, 2, 1, 0, 2, 1, 2, 1, 2, 1, 2, 1, 0, 2, 0, 2, 1, 2, 2, 1, 0, 2, 0, 2, 1, 2, 2,
            1, 2, 1, 0, 2, 0, 2, 1, 2, 1, 2, 1, 0, 2, 0, 2, 1,
        };

        /// <summary>The attribute names of the clip's muscle curves (bound to the Animator, path ""),
        /// in Unity's order.</summary>
        public static readonly string[] CurveNames = BuildNames();

        static string[] BuildNames()
        {
            var n = new string[CurveCount];
            int k = 0;
            foreach (var prefix in new[] { "MotionT", "MotionQ", "RootT", "RootQ" })
            {
                n[k++] = prefix + ".x"; n[k++] = prefix + ".y"; n[k++] = prefix + ".z";
                if (prefix[prefix.Length - 1] == 'Q') n[k++] = prefix + ".w";
            }
            foreach (int bone in GoalBones)
            {
                string b = BodyBoneNames[bone];
                n[k++] = b + "T.x"; n[k++] = b + "T.y"; n[k++] = b + "T.z";
                n[k++] = b + "Q.x"; n[k++] = b + "Q.y"; n[k++] = b + "Q.z"; n[k++] = b + "Q.w";
            }
            foreach (var m in MuscleNames) n[k++] = m;
            foreach (var hand in new[] { "LeftHand.", "RightHand." })
                foreach (var f in FingerNames)
                    foreach (var d in FingerDoFNames)
                        n[k++] = hand + f + "." + d;
            foreach (int bone in TDoFBones)
            {
                string b = BodyBoneNames[bone] + "TDOF";
                n[k++] = b + ".x"; n[k++] = b + ".y"; n[k++] = b + ".z";
            }
            return n;
        }

        /// <summary>The curve index of an attribute name (−1 when it is not a muscle curve).</summary>
        public static int CurveIndex(string attribute) => Array.IndexOf(CurveNames, attribute);

        /// <summary>Per curve index, the curve a mirrored clip plays it on, negated when the index is
        /// complemented (~j): the left and right goals, muscles and fingers swap, the x of positions
        /// and the y and z of the body's rotation flip, and so do the body's Left-Right muscles. A
        /// goal's rotation is mirrored as the API gives it (the stored rotation times the goal's
        /// orientation offset, <see cref="HumanIK.GetGoalRotation"/>): stored, (x, −y, −z, w) times
        /// a constant, the feet's (z, w, x, y) and the hands' (−y, −x, w, z) of the other side
        /// (measured, tests/projects/humanoid). Translation DoF swap sides with their x negated
        /// (tests/projects/humanoid tdof.TDoFMirror).</summary>
        public static readonly int[] MirrorCurve = BuildMirror();

        static int[] BuildMirror()
        {
            int[] GoalFootMirror = { 2, 3, 0, 1 }, GoalHandMirror = { 1, 0, 3, 2 };
            var m = new int[CurveCount];
            for (int i = 0; i < CurveCount; i++)
            {
                int j = i, c = i < GoalFirst ? i % 7 : (i - GoalFirst) % 7;
                bool negate = i < MuscleFirst && (c == 0 || c == 4 || c == 5);
                if (i >= GoalFirst && i < MuscleFirst)
                {
                    int g = (i - GoalFirst) / 7;
                    j = GoalFirst + 7 * (g ^ 1) + c;
                    if (c >= 3)
                    {
                        // The source component's place (x, y, z, w) in the other side's rotation.
                        int k = c - 3;
                        j = GoalFirst + 7 * (g ^ 1) + 3 + (g < 2 ? GoalFootMirror[k] : GoalHandMirror[k]);
                        negate = g >= 2 && k < 2;
                    }
                }
                else if (i >= MuscleFirst && i < LeftHandFirst)
                {
                    string n = CurveNames[i];
                    if (n.StartsWith("Left ", StringComparison.Ordinal)) j = CurveIndex("Right " + n.Substring(5));
                    else if (n.StartsWith("Right ", StringComparison.Ordinal)) j = CurveIndex("Left " + n.Substring(6));
                    else negate = n.Contains("Left-Right");
                }
                else if (i >= LeftHandFirst && i < TDoFFirst) j = i < RightHandFirst ? i + 20 : i - 20;
                else if (i >= TDoFFirst)
                {
                    // TDoFBones order: the body and head (0-4), the left leg (5-8), the right leg
                    // (9-12), the left arm (13-16), the right arm (17-20).
                    int t = (i - TDoFFirst) / 3, axis = (i - TDoFFirst) % 3;
                    int other = t < 5 ? t : t < 9 || t >= 13 && t < 17 ? t + 4 : t - 4;
                    j = TDoFFirst + 3 * other + axis;
                    negate = axis == 0;
                }
                m[i] = negate ? ~j : j;
            }
            return m;
        }

        /// <summary>Per bone (<see cref="BodyBoneNames"/> order): its AvatarMaskBodyPart (Body 1,
        /// Head 2, LeftLeg 3, RightLeg 4, LeftArm 5, RightArm 6).</summary>
        static readonly int[] BonePart = { 1, 3, 4, 3, 4, 3, 4, 1, 1, 1, 2, 2, 5, 6, 5, 6, 5, 6, 5, 6, 3, 4, 2, 2, 2 };

        /// <summary>The AvatarMaskBodyPart that masks a curve (Manual 6000.0, Avatar Mask window): the
        /// motion and body curves Root, the goals the IK parts, muscles and translation DoF their bone's
        /// part, fingers the hand's fingers.</summary>
        public static int BodyPart(int curve)
        {
            if (curve < GoalFirst) return 0;
            if (curve < MuscleFirst) return 9 + (curve - GoalFirst) / 7;
            if (curve < LeftHandFirst) return BonePart[MuscleBone[curve - MuscleFirst]];
            if (curve < RightHandFirst) return 7;
            if (curve < TDoFFirst) return 8;
            return BonePart[TDoFBones[(curve - TDoFFirst) / 3]];
        }

        /// <summary>The curves of the body parts a clip's body mask turns off are not in the clip
        /// (<see cref="BodyPart"/>: the motion curves with Root, a goal's with its IK part, a
        /// muscle's with its bone's part, fingers with the hand's); the others keep their values.
        /// The Editor takes the parts from the clip's own body mask (the model meta's, also with a
        /// mask copied from another asset) whatever the mask type (matches the Unity 6000.0.71f1
        /// Editor, tests/projects/modelclips HumanMask*, every part off once). Translation DoF
        /// curves follow their bone's part too: the neck's with the head, a shoulder's with its arm
        /// (tests/projects/modelclips HumanMaskTDoF, the body, the left leg and the right arm off).</summary>
        internal static void DropPartsOff(float[][] values, float[][] slopes, int partsOff)
        {
            if (partsOff == 0) return;
            for (int c = 0; c < CurveCount; c++)
            {
                if ((partsOff & (1 << BodyPart(c))) == 0) continue;
                values[c] = null;
                slopes[c] = null;
            }
        }

        // ---------------------------------------------------------------- import

        /// <summary>The magnitude one key of a translation DoF curve must reach for the clip to
        /// have the curve. The reference brackets it between 9.2e-5 (absent) and 1.6e-4
        /// (present); the value itself and a key equal to it counting are Unbound's choice
        /// inside that bracket.</summary>
        public const float TDoFCurveMinimum = 1e-4f;

        /// <summary>Whether a converted translation DoF curve stays in the clip: only when one of
        /// its keys reaches <see cref="TDoFCurveMinimum"/>. Each of a bone's three curves is
        /// tested on its own, so a bone moved along one axis of its parent's frame keeps that
        /// component and plays the others as zero (Unity 6000.0.71f1 Editor,
        /// tests/projects/humanoid HumanT: of 63 curves the seven with keys from 1.6e-4 to 4.3e-4
        /// stay and those up to 9.2e-5 are absent; tdof.curves, tdof.playT). The keys tested are
        /// the every-frame ones, before any keyframe reduction (unmeasured under a reducing
        /// compression). The other curves of a Humanoid clip always stay, whatever their
        /// values.</summary>
        public static bool KeepsTDoFCurve(float[] values)
        {
            foreach (float value in values)
                if (MathF.Abs(value) >= TDoFCurveMinimum) return true;
            return false;
        }

        /// <summary>The times the importer samples a clip whose transform curves span
        /// [<paramref name="start"/>, <paramref name="stop"/>]: steps of 1 / (sample rate ×
        /// oversampling), ceil(duration / step) + 1 of them (the last one may lie past the stop),
        /// taken on the take's times from the clip's start there (<paramref name="takeStart"/>) and
        /// moved back by it, in single precision: matches the Unity 6000.0.71f1 Editor's muscle
        /// key times, tests/projects/modelclips HumanLate (frames 1 to 31 of its take:
        /// 0.06666668, ..., 0.966666639, 0.99999994) and HumanLong (a take from 0).</summary>
        public static float[] SampleTimes(float start, float stop, float sampleRate, int oversampling, float takeStart = 0f)
        {
            if (!(start < stop))
                throw new NotSupportedException("a humanoid clip whose curves do not span any time (a single frame) gets no muscle curves; this is not supported");
            float dt = 1f / sampleRate;
            dt = dt / (float)oversampling;
            float duration = stop - start;
            int count = (int)(MathF.Ceiling(duration / dt) + 1f);
            var times = new float[count];
            float origin = start + takeStart;
            for (int i = 0; i < count; i++) times[i] = ((float)i * dt + origin) - takeStart;
            return times;
        }

        /// <summary>A quaternion read from rotation curves as the clip evaluation stores it:
        /// q / |q| (the norm's squares summed pairwise), identity when |q|² is at most 1e-30 (checked
        /// per component with that component's sum).</summary>
        public static F4 NormalizeCurveQ(F4 q)
        {
            float a0 = q.y * q.y + q.x * q.x, a1 = q.z * q.z + q.y * q.y, a2 = q.w * q.w + q.z * q.z, a3 = q.x * q.x + q.w * q.w;
            float s0 = a2 + a0, s1 = a3 + a1;
            const float Eps = 1.00000003e-30f;
            return new F4(
                s0 <= Eps ? 0f : q.x / MathF.Sqrt(s0),
                s1 <= Eps ? 0f : q.y / MathF.Sqrt(s1),
                s0 <= Eps ? 0f : q.z / MathF.Sqrt(s0),
                s1 <= Eps ? 1f : q.w / MathF.Sqrt(s1));
        }

        /// <summary>Whether a rotation has the sign the Editor's model importer gives a node's
        /// rotation and the first key of its rotation curve (the later keys follow in its
        /// hemisphere): the sign a rotation matrix gives its quaternion, w positive while the
        /// matrix's trace is positive (|w| above 1/2), else the component of the largest
        /// diagonal element positive. The muscles do not depend on it; the goal curves keep it,
        /// and a blend with the identity below a total weight of one shows it. Matches the
        /// Unity 6000.0.71f1 Editor, tests/projects/humanoid body.directLowB and
        /// body.directEmptyChild (model clips' goals completed by the identity), and a capture
        /// outside the reference of the probe's three models imported as Generic: all 105
        /// rotation curves and 108 node rotations follow it, eight of each with a negative w.
        /// A diagonal element decides only when it is larger than the later ones: on a tie the
        /// later axis decides (measured on an imported node whose y and z elements are equal,
        /// tests/projects/modelclips nodes.AxisSigned/U2S1B0H0; a tie with x and a trace of
        /// exactly zero are unmeasured). The model importer gives its node rotations and clip
        /// keys the same rule (crates/unbound-assets/src/model.rs `importer_sign`).</summary>
        internal static bool ImporterSign(F4 q)
        {
            float xx = q.x * q.x, yy = q.y * q.y, zz = q.z * q.z;
            // The diagonal of the rotation's matrix, for a unit rotation.
            float m00 = 1f - 2f * (yy + zz), m11 = 1f - 2f * (xx + zz), m22 = 1f - 2f * (xx + yy);
            if (m00 + m11 + m22 > 0f) return q.w > 0f;
            if (m00 > m11 && m00 > m22) return q.x > 0f;
            return m11 > m22 ? q.y > 0f : q.z > 0f;
        }

        /// <summary>Converts a clip into muscle curves.</summary>
        /// <param name="avatar">The model's humanoid avatar.</param>
        /// <param name="times">The sample times (<see cref="SampleTimes"/>).</param>
        /// <param name="sampleFrame">Fills the avatar's local pose at a sample (index, time). The
        /// pose it gets holds the avatar's default pose (the hierarchy as imported,
        /// <see cref="AvatarConstant.DefaultPose"/>); it must replace the position, rotation and scale
        /// of every transform the clip animates with the clip's values at that time (rotations through
        /// <see cref="NormalizeCurveQ"/>) and leave the others. Every node's rotations then take
        /// the sign of <see cref="ImporterSign"/> at the first sample, whatever sign the curves
        /// and the default pose hold them with.</param>
        /// <param name="settings">The clip's import settings.</param>
        public static HumanClipCurves Convert(AvatarConstant avatar, float[] times, Action<int, float, SkeletonPose> sampleFrame,
            in HumanClipSettings settings)
        {
            settings.RequireSupportedImport();
            if (avatar == null || !avatar.IsHuman)
                throw new NotSupportedException("humanoid clip import needs a human avatar");
            var h = avatar.Human;
            int frames = times.Length;
            var skel = avatar.AvatarSkeleton;
            int n = skel.Count, hn = h.Skeleton.Count;
            var local = new SkeletonPose(n);
            var global = new SkeletonPose(n);
            var humanLocal = new SkeletonPose(hn);
            var ws4 = new SkeletonPose(hn);
            var ws5 = new SkeletonPose(hn);
            var ws6 = new SkeletonPose(hn);
            var ws7 = new SkeletonPose(hn);
            // One pose for every sample: the retargeting writes every value the curves read.
            var pose = new HumanPose();
            // An avatar with translation DoF also writes their curves (the bones' placement is
            // recorded by tests/projects/humanoid tdof.playTOnA; the curve values are not recorded).
            int first = RootFirst, end = h.HasTDoF ? CurveCount : HumanEnd;
            var tdofBase = h.HasTDoF ? HumanOps.TDoFBase(h, ws5) : null;
            var values = new float[CurveCount][];
            for (int c = first; c < end; c++) values[c] = new float[frames];
            int hipsParent = avatar.HumanSkeletonIndexArray[0];
            // Per node, whether its rotations take the other sign (see ImporterSign).
            var negated = new bool[n];

            for (int i = 0; i < frames; i++)
            {
                local.CopyFrom(avatar.DefaultPose);
                sampleFrame(i, times[i], local);
                for (int k = 0; k < n; k++)
                {
                    if (i == 0) negated[k] = !ImporterSign(local.X[k].q);
                    if (negated[k]) local.X[k].q = -local.X[k].q;
                }
                Skel.ComputeGlobal(skel, local, global, hipsParent, 0);
                Skel.CopyById(skel, local, h.Skeleton, humanLocal);
                humanLocal.X[0] = global.X[hipsParent];
                HumanOps.RetargetFrom(h, humanLocal, pose, ws4, ws5, ws6, ws7, tdofBase);
                for (int c = first; c < end; c++)
                {
                    values[c][i] = CurveValue(pose, c);
                    if (i != 0 && c < MuscleFirst && c % 7 == 6) KeepHemisphere(values, c, i);
                }
            }

            var slopes = new float[CurveCount][];
            for (int c = first; c < end; c++)
            {
                if (c >= MuscleFirst && c < LeftHandFirst)
                {
                    MuscleRange(h, c - MuscleFirst, out float min, out float max);
                    Unroll(values[c], min, max);
                }
                slopes[c] = Slopes(times, values[c]);
            }
            // The clip taken out of the take: each body muscle curve moves by the whole turns that bring
            // its smallest angle into [-π, π].
            for (int c = MuscleFirst; c < LeftHandFirst; c++)
            {
                MuscleRange(h, c - MuscleFirst, out float min, out float max);
                Offset(values[c], min, max);
            }
            DropPartsOff(values, slopes, settings.PartsOff);
            return new HumanClipCurves { Times = times, Values = values, Slopes = slopes };
        }

        /// <summary>A curve's value in a retargeted pose (the root and goals as the retargeting
        /// stores them: divided by the human scale, the goals relative to the root).</summary>
        static float CurveValue(HumanPose pose, int c)
        {
            if (c < GoalFirst)
            {
                int k = c - RootFirst;
                return k < 3 ? pose.RootX.t[k] : pose.RootX.q[k - 3];
            }
            if (c < MuscleFirst)
            {
                int g = (c - GoalFirst) / 7, k = (c - GoalFirst) % 7;
                return k < 3 ? pose.Goals[g].X.t[k] : pose.Goals[g].X.q[k - 3];
            }
            if (c < LeftHandFirst) return pose.DoF[c - MuscleFirst];
            if (c < RightHandFirst) return pose.LeftHand.DoF[c - LeftHandFirst];
            if (c < TDoFFirst) return pose.RightHand.DoF[c - RightHandFirst];
            int t = c - TDoFFirst;
            return pose.TDoF[t / 3][t % 3];
        }

        /// <summary>Negates a sample's quaternion (curves c-3..c) when its dot product with the previous
        /// sample's is negative (or −0), so consecutive keys stay in one hemisphere.</summary>
        static void KeepHemisphere(float[][] v, int c, int i)
        {
            float px = v[c - 3][i - 1], py = v[c - 2][i - 1], pz = v[c - 1][i - 1], pw = v[c][i - 1];
            float cx = v[c - 3][i], cy = v[c - 2][i], cz = v[c - 1][i], cw = v[c][i];
            float mx = px * cx, my = py * cy, mz = pz * cz, mw = pw * cw;
            float s0 = (mw + mz) + (my + mx);
            float s1 = (mx + mw) + (mz + my);
            v[c - 3][i] = XorSign(cx, s0);
            v[c - 2][i] = XorSign(cy, s1);
            v[c - 1][i] = XorSign(cz, s0);
            v[c][i] = XorSign(cw, s1);
        }

        static float XorSign(float v, float s) =>
            BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(v) ^ (BitConverter.SingleToInt32Bits(s) & int.MinValue));

        /// <summary>A body muscle's limits along its bone's axis in radians; (1, −1), which leaves
        /// values as they are, when the avatar has no such bone.</summary>
        static void MuscleRange(Human h, int muscle, out float min, out float max)
        {
            min = 1f;
            max = -1f;
            int node = h.BoneIndex[MuscleBone[muscle]];
            if (node < 0) return;
            int id = h.Skeleton.AxesId[node];
            if (id < 0) return;
            var axes = h.Skeleton.AxesArray[id];
            min = axes.Min[MuscleAxis[muscle]];
            max = axes.Max[MuscleAxis[muscle]];
        }

        static float Unproject(float min, float max, float v)
        {
            if (v < 0f) return min < 0f ? -(v * min) : min > 0f ? v : 0f;
            return max > 0f ? v * max : max < 0f ? v : 0f;
        }

        static float Project(float min, float max, float v)
        {
            if (v < 0f) return min < 0f ? -v / min : min > 0f ? v : 0f;
            return max > 0f ? v / max : max < 0f ? v : 0f;
        }

        const float TwoPi = 6.28318548f;

        static float RoundF(float v) => MathF.Round(v, MidpointRounding.AwayFromZero);

        /// <summary>The first key with the smallest absolute value (−1 when none is below 1e6).</summary>
        static int SmallestKey(float[] v)
        {
            int index = -1;
            float best = 1000000f;
            for (int i = 0; i < v.Length; i++)
            {
                float a = MathF.Abs(v[i]);
                if (best > a) index = i;
                best = a < best ? a : best;
            }
            return index;
        }

        /// <summary>Makes a muscle curve continuous: in angles, every key moves by whole turns towards
        /// its neighbour, going out from the key with the smallest angle.</summary>
        static void Unroll(float[] v, float min, float max)
        {
            int n = v.Length;
            if (n == 0) return;
            int pivot = 0;
            float best = 1000000f;
            for (int i = 0; i < n; i++)
            {
                v[i] = Unproject(min, max, v[i]);
                float a = MathF.Abs(v[i]);
                if (best > a) pivot = i;
                best = a < best ? a : best;
            }
            float prev = v[pivot];
            for (int i = pivot + 1; i < n; i++)
            {
                float cur = v[i];
                float r = RoundF((prev - cur) / TwoPi);
                prev = cur + r * TwoPi;
                v[i] = prev;
            }
            float next = v[pivot];
            for (int i = pivot - 1; i >= 0; i--)
            {
                float cur = v[i];
                float r = RoundF((next - cur) / TwoPi);
                next = cur + r * TwoPi;
                v[i] = next;
            }
            for (int i = 0; i < n; i++) v[i] = Project(min, max, v[i]);
        }

        /// <summary>Moves a muscle curve so its key with the smallest value has an angle in
        /// [-π, π].</summary>
        static void Offset(float[] v, float min, float max)
        {
            int index = SmallestKey(v);
            if (index < 0) return;
            float a = Unproject(min, max, v[index]);
            a = a - RoundF(a / TwoPi) * TwoPi;
            float offset = Project(min, max, a) - v[index];
            for (int i = 0; i < v.Length; i++) v[i] = v[i] + offset;
        }

        /// <summary>The keys' slopes (in = out): the mean of the slopes to the neighbours, the slope
        /// to the only neighbour at the ends (0 across steps of at most 1e-5 s).</summary>
        public static float[] Slopes(float[] t, float[] v)
        {
            int n = v.Length;
            var s = new float[n];
            if (n < 2) return s;
            for (int i = 0; i < n; i++)
            {
                if (i == 0)
                {
                    s[0] = SafeSlope(t[1] - t[0], v[1] - v[0]);
                    continue;
                }
                float m1 = SafeSlope(t[i] - t[i - 1], v[i] - v[i - 1]);
                if (i == n - 1)
                {
                    s[i] = m1;
                    continue;
                }
                float m2 = SafeSlope(t[i + 1] - t[i], v[i + 1] - v[i]);
                s[i] = 0.5f * m2 + 0.5f * m1;
            }
            return s;
        }

        static float SafeSlope(float dx, float dy) => MathF.Abs(dx) > 9.99999975e-6f ? dy / dx : 0f;

        // ---------------------------------------------------------------- playback

        /// <summary>The human pose of a humanoid clip's sampled curve values: the root (body) position
        /// and normalized rotation (scale 1), the goals' positions and normalized rotations relative
        /// to it (their scale untouched), the muscles and the translation DoFs. Missing curves read 0
        /// (rotation w 1). Look-at, goal weights and hints are not changed. The root here is the
        /// clip's own; playback then replaces it with the body placed by the root motion and moves
        /// the goals with it.</summary>
        /// <param name="values">Per curve index of <see cref="CurveNames"/>, the curve's value.</param>
        /// <param name="present">Per curve index, whether the clip has the curve.</param>
        /// <param name="pose">Output.</param>
        public static void PoseFromCurves(float[] values, bool[] present, HumanPose pose)
        {
            float V(int c, float missing) => present[c] ? values[c] : missing;

            pose.RootX.t = new F4(V(7, 0f), V(8, 0f), V(9, 0f), 0f);
            pose.RootX.q = M.QNormalize(new F4(V(10, 0f), V(11, 0f), V(12, 0f), V(13, 1f)));
            pose.RootX.s = F4.One3;
            for (int g = 0; g < 4; g++)
            {
                int c = GoalFirst + 7 * g;
                pose.Goals[g].X.t = new F4(V(c, 0f), V(c + 1, 0f), V(c + 2, 0f), 0f);
                pose.Goals[g].X.q = M.QNormalize(new F4(V(c + 3, 0f), V(c + 4, 0f), V(c + 5, 0f), V(c + 6, 1f)));
            }
            for (int i = 0; i < Bone.DoFCount; i++) pose.DoF[i] = V(MuscleFirst + i, 0f);
            for (int i = 0; i < Bone.HandDoFCount; i++)
            {
                pose.LeftHand.DoF[i] = V(LeftHandFirst + i, 0f);
                pose.RightHand.DoF[i] = V(RightHandFirst + i, 0f);
            }
            for (int i = 0; i < Bone.TDoFCount; i++)
            {
                int c = TDoFFirst + 3 * i;
                pose.TDoF[i] = new F4(V(c, 0f), V(c + 1, 0f), V(c + 2, 0f), 0f);
            }
        }
    }
}
