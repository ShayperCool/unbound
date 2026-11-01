using System;

namespace Unbound.Runtime.Humanoid
{
    /// <summary>The settings of a humanoid clip import. The model importer's muscle conversion only
    /// depends on the oversampling, the root motion node, the clip's range in its take and the body
    /// mask; the root transform settings (keep original, bake into pose, offsets, mirror, loop blend)
    /// are stored in the clip and only change how it is played (see <see cref="RequireDefaultPlayback"/>).</summary>
    internal struct HumanClipSettings
    {
        /// <summary>ModelImporter.humanoidOversampling (1, 2, 4 or 8).</summary>
        public int Oversampling;
        /// <summary>The human description has a root motion node (motionNodeName).</summary>
        public bool HasRootMotionNode;
        /// <summary>The clip is a part of its take (firstFrame/lastFrame inside the take).</summary>
        public bool CroppedFromTake;
        /// <summary>The clip's body mask leaves out a body part.</summary>
        public bool MasksBodyParts;

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
            if (MasksBodyParts)
                throw new NotSupportedException("humanoid clips with a body mask that leaves out body parts are not supported");
        }

        /// <summary>Throws unless the root transform settings are the defaults, the only ones whose
        /// playback is reproduced.</summary>
        public void RequireDefaultPlayback()
        {
            string what = null;
            if (LoopBlend || LoopBlendOrientation || LoopBlendPositionY || LoopBlendPositionXZ) what = "loop pose / bake into pose";
            else if (KeepOriginalOrientation) what = "root rotation based upon the original";
            else if (!KeepOriginalPositionY) what = "root position Y based upon the center of mass";
            else if (KeepOriginalPositionXZ) what = "root position XZ based upon the original";
            else if (HeightFromFeet) what = "root position Y based upon the feet";
            else if (Mirror) what = "mirror";
            else if (OrientationOffsetY != 0f) what = "root rotation offset";
            else if (Level != 0f) what = "root position Y offset";
            else if (CycleOffset != 0f) what = "cycle offset";
            if (what != null)
                throw new NotSupportedException($"humanoid clip setting '{what}' is not supported");
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
        static readonly int[] TDoFBones = { 7, 8, 9, 10, 11, 1, 3, 5, 20, 2, 4, 6, 21, 12, 14, 16, 18, 13, 15, 17, 19 };

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

        // ---------------------------------------------------------------- import

        /// <summary>The times the importer samples a clip whose transform curves span
        /// [<paramref name="start"/>, <paramref name="stop"/>]: steps of 1 / (sample rate ×
        /// oversampling) from the start, ceil(duration / step) + 1 of them (the last one may lie past
        /// the stop).</summary>
        public static float[] SampleTimes(float start, float stop, float sampleRate, int oversampling)
        {
            if (!(start < stop))
                throw new NotSupportedException("a humanoid clip whose curves do not span any time (a single frame) gets no muscle curves; this is not supported");
            float dt = 1f / sampleRate;
            dt = dt / (float)oversampling;
            float duration = stop - start;
            int count = (int)(MathF.Ceiling(duration / dt) + 1f);
            var times = new float[count];
            for (int i = 0; i < count; i++) times[i] = (float)i * dt + start;
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

        /// <summary>Converts a clip into muscle curves.</summary>
        /// <param name="avatar">The model's humanoid avatar.</param>
        /// <param name="times">The sample times (<see cref="SampleTimes"/>).</param>
        /// <param name="sampleFrame">Fills the avatar's local pose at a sample (index, time). The
        /// pose it gets holds the avatar's default pose (the hierarchy as imported,
        /// <see cref="AvatarConstant.DefaultPose"/>); it must replace the position, rotation and scale
        /// of every transform the clip animates with the clip's values at that time (rotations through
        /// <see cref="NormalizeCurveQ"/>) and leave the others.</param>
        /// <param name="settings">The clip's import settings.</param>
        public static HumanClipCurves Convert(AvatarConstant avatar, float[] times, Action<int, float, SkeletonPose> sampleFrame,
            in HumanClipSettings settings)
        {
            settings.RequireSupportedImport();
            if (avatar == null || !avatar.IsHuman)
                throw new NotSupportedException("humanoid clip import needs a human avatar");
            var h = avatar.Human;
            if (h.HasTDoF)
                throw new NotSupportedException("humanoid clip import for avatars with translation DoF is not supported");
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
            int first = RootFirst, end = HumanEnd;
            var values = new float[CurveCount][];
            for (int c = first; c < end; c++) values[c] = new float[frames];
            int hipsParent = avatar.HumanSkeletonIndexArray[0];

            for (int i = 0; i < frames; i++)
            {
                local.CopyFrom(avatar.DefaultPose);
                sampleFrame(i, times[i], local);
                Skel.ComputeGlobal(skel, local, global, hipsParent, 0);
                Skel.CopyById(skel, local, h.Skeleton, humanLocal);
                humanLocal.X[0] = global.X[hipsParent];
                HumanOps.RetargetFrom(h, humanLocal, pose, ws4, ws5, ws6, ws7, null);
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
