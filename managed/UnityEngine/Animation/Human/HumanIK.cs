using System;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;

namespace Unbound.Runtime.Humanoid
{
    /// <summary>The Animator's humanoid IK pass: the full-body solve that runs after OnAnimatorIK
    /// (look-at for body, head and eyes with its clamp, the four limb goals with position and rotation
    /// weights, knee and elbow hints, limb stretch), the steps around it as the Animator runs them each
    /// update (retargeting into the IK pose, IK on feet, the IK step, writing the avatar pose) and the
    /// IK pose accessors behind the Animator's IK API. Matches the Unity 6000.0.71f1 Editor's results
    /// (tests/projects/humanoid), keeping its operation order and per-component reductions.
    ///
    /// The IK pose is a <see cref="HumanPose"/> in world space: root (body position and rotation),
    /// goals and hints are world positions and rotations, scaled by the human scale and placed by the
    /// avatar root's transform. The Animator's IK API reads and writes it without any conversion.
    /// Goal rotations are stored in the end bone's muscle frame; the API rotation is that rotation
    /// times the goal's orientation offset.</summary>
    internal static class HumanIK
    {
        // ---------------------------------------------------------------- goals

        /// <summary>Goal indices (the order of <c>AvatarIKGoal</c>; <c>AvatarIKHint</c> LeftKnee,
        /// RightKnee, LeftElbow, RightElbow index the same goals).</summary>
        public const int LeftFoot = 0, RightFoot = 1, LeftHand = 2, RightHand = 3, GoalCount = 4;

        /// <summary>The error the Animator logs (when its warnings are on) for a goal or hint index
        /// outside 0..3; getters then return zero, the identity or 0, setters do nothing.</summary>
        public const string InvalidGoalIndexMessage = "Invalid Goal Index";

        // Per goal: the limb's end, top and middle bone.
        static readonly int[] GoalEnd = { Bone.LeftFoot, Bone.RightFoot, Bone.LeftHand, Bone.RightHand };
        static readonly int[] GoalTop = { Bone.LeftUpperLeg, Bone.RightUpperLeg, Bone.LeftUpperArm, Bone.RightUpperArm };
        static readonly int[] GoalMid = { Bone.LeftLowerLeg, Bone.RightLowerLeg, Bone.LeftLowerArm, Bone.RightLowerArm };

        // Per goal: the rotation from the end bone's muscle frame to the API's goal rotation.
        static readonly F4[] GoalOrientationOffset =
        {
            new F4(0.5f, -0.5f, 0.5f, 0.5f),
            new F4(0.5f, -0.5f, 0.5f, 0.5f),
            new F4(0.707107f, 0f, 0.707107f, 0f),
            new F4(0f, 0.707107f, 0f, 0.707107f),
        };

        /// <summary>Whether an <c>AvatarIKGoal</c> or <c>AvatarIKHint</c> value is a goal index.</summary>
        public static bool IsValidGoal(int goal) => (uint)goal < GoalCount;

        /// <summary>The goal an <c>AvatarIKHint</c> belongs to (the same index).</summary>
        public static int GoalOfHint(int hint) => hint;

        // ---------------------------------------------------------------- IK pose accessors (Animator API)

        /// <summary>GetIKPosition: the goal's world position (xyz).</summary>
        public static F4 GetGoalPosition(HumanPose ikPose, int goal) => ikPose.Goals[goal].X.t;

        /// <summary>SetIKPosition: a world position, stored as is.</summary>
        public static void SetGoalPosition(HumanPose ikPose, int goal, F4 position) =>
            ikPose.Goals[goal].X.t = new F4(position.x, position.y, position.z, 0f);

        /// <summary>GetIKRotation: the stored rotation times the goal's orientation offset,
        /// normalized twice (the second time falling back to the identity for a null quaternion).</summary>
        public static F4 GetGoalRotation(HumanPose ikPose, int goal) =>
            NormalizeSafe(M.QNormalize(M.QMul(ikPose.Goals[goal].X.q, GoalOrientationOffset[goal])));

        /// <summary>SetIKRotation: a world rotation, stored in the muscle frame (times the inverse
        /// orientation offset, normalized).</summary>
        public static void SetGoalRotation(HumanPose ikPose, int goal, F4 rotation) =>
            ikPose.Goals[goal].X.q = M.QNormalize(M.QMul(rotation, M.QConj(GoalOrientationOffset[goal])));

        public static float GetGoalWeightPosition(HumanPose ikPose, int goal) => ikPose.Goals[goal].WeightT;

        /// <summary>SetIKPositionWeight: stored unclamped (the solve clamps it to 0..1).</summary>
        public static void SetGoalWeightPosition(HumanPose ikPose, int goal, float weight) => ikPose.Goals[goal].WeightT = weight;

        public static float GetGoalWeightRotation(HumanPose ikPose, int goal) => ikPose.Goals[goal].WeightR;

        public static void SetGoalWeightRotation(HumanPose ikPose, int goal, float weight) => ikPose.Goals[goal].WeightR = weight;

        /// <summary>GetIKHintPosition: the hint's world position (xyz).</summary>
        public static F4 GetHintPosition(HumanPose ikPose, int goal) => ikPose.Goals[goal].HintT;

        public static void SetHintPosition(HumanPose ikPose, int goal, F4 position) =>
            ikPose.Goals[goal].HintT = new F4(position.x, position.y, position.z, 0f);

        public static float GetHintWeightPosition(HumanPose ikPose, int goal) => ikPose.Goals[goal].HintWeightT;

        public static void SetHintWeightPosition(HumanPose ikPose, int goal, float weight) => ikPose.Goals[goal].HintWeightT = weight;

        /// <summary>SetLookAtPosition: a world position, stored as is.</summary>
        public static void SetLookAtPosition(HumanPose ikPose, F4 position) =>
            ikPose.LookAtPosition = new F4(position.x, position.y, position.z, 0f);

        /// <summary>SetLookAtWeight(weight, body, head, eyes, clamp): stored as (clamp, body * weight,
        /// head * weight, eyes * weight), unclamped.</summary>
        public static void SetLookAtWeight(HumanPose ikPose, float weight, float body, float head, float eyes, float clamp) =>
            ikPose.LookAtWeight = new F4(clamp, body * weight, head * weight, weight * eyes);

        /// <summary>bodyPosition: the IK pose's root position (the world mass center).</summary>
        public static F4 GetBodyPosition(HumanPose ikPose) => ikPose.RootX.t;

        public static void SetBodyPosition(HumanPose ikPose, F4 position) =>
            ikPose.RootX.t = new F4(position.x, position.y, position.z, 0f);

        /// <summary>bodyRotation: the root rotation, normalized (the identity for a null quaternion).</summary>
        public static F4 GetBodyRotation(HumanPose ikPose) => NormalizeSafe(ikPose.RootX.q);

        /// <summary>Stored as is (not normalized).</summary>
        public static void SetBodyRotation(HumanPose ikPose, F4 rotation) => ikPose.RootX.q = rotation;

        // ---------------------------------------------------------------- per-update steps

        /// <summary>The retarget step: the animated human pose to the world IK pose and the human
        /// skeleton pose, then the IK weights cleared. With <paramref name="rootFromPose"/> (the
        /// Animator's layersAffectMassCenter, or a single controller with at most one layer) the base
        /// pose takes all of <paramref name="pose"/> and places the body; otherwise the base pose places
        /// the body and <paramref name="pose"/> gives the goals and muscles.</summary>
        /// <param name="basePose">In/out: the base layer's human pose (avatar space, unscaled);
        /// overwritten by <paramref name="pose"/> with <paramref name="rootFromPose"/>.</param>
        /// <param name="pose">Input: the blended human pose (avatar space, unscaled).</param>
        /// <param name="avatarX">Input: the avatar root's world transform (position, rotation, lossy scale).</param>
        /// <param name="ikPose">Output: the world IK pose.</param>
        /// <param name="local">Output: the human skeleton pose, local (node 0 the world body root).</param>
        /// <param name="ws">Workspace: the human skeleton pose, global.</param>
        /// <param name="adjustMissingBones">Moves the muscles of missing spine, neck and shoulder bones
        /// onto existing ones (the Animator always does).</param>
        internal static void Retarget(Human h, HumanPose basePose, HumanPose pose, bool rootFromPose, Trs avatarX,
            HumanPose ikPose, SkeletonPose local, SkeletonPose ws, bool adjustMissingBones)
        {
            HumanPose prev;
            if (rootFromPose)
            {
                CopyPoseFields(basePose, pose);
                prev = null;
            }
            else prev = pose;
            HumanOps.RetargetTo(h, basePose, prev, avatarX, ikPose, local, ws, adjustMissingBones);
            ClearIKWeights(ikPose);
        }

        /// <summary>A state's "Foot IK" (run after the retarget step, left foot first, when the
        /// evaluated states ask for it): the foot goal's position and rotation weights become 1 and the
        /// goal moves sideways by the avatar's feet spacing (along the goal's z axis projected on the
        /// horizontal plane, scaled by the human and avatar scales). The Animator's stabilizeFeet
        /// (which limits the feet's motion between frames) is not supported and throws.</summary>
        /// <param name="ikPose">In/out: the world IK pose (the foot goal's weights and position).</param>
        /// <param name="avatarX">Input: the avatar root's world transform.</param>
        /// <param name="stabilizeFeet">The Animator's stabilizeFeet.</param>
        /// <param name="firstEvaluation">Whether this is the avatar's first evaluation (no previous
        /// feet to stabilize against).</param>
        internal static void SetIKOnFeet(bool left, Human h, HumanPose ikPose, Trs avatarX, bool stabilizeFeet, bool firstEvaluation)
        {
            int g = left ? LeftFoot : RightFoot;
            ref var goal = ref ikPose.Goals[g];
            goal.WeightT = 1f;
            goal.WeightR = 1f;
            if (stabilizeFeet && !firstEvaluation)
                throw new NotSupportedException("Animator.stabilizeFeet with a state's Foot IK is not supported");
            float k = (left ? -1f : 1f) * h.Scale * h.FeetSpacing;
            var q = goal.X.q;
            float zx = q.x * 2f * q.z - q.y * -2f * q.w;
            float zz = q.x * -2f * q.x - q.y * 2f * q.y;
            var offset = new F4(k * zx, 0f, k * zz + k, 0f);
            var s = avatarX.s;
            goal.X.t = new F4(s.x * offset.x + goal.X.t.x, s.y * offset.y + goal.X.t.y, s.z * offset.z + goal.X.t.z, goal.X.t.w);
        }

        /// <summary>Whether the IK pose asks for a solve: any look-at weight (clamp included), goal
        /// position or rotation weight or hint weight above 0.</summary>
        internal static bool NeedsSolve(HumanPose ikPose)
        {
            var w = ikPose.LookAtWeight;
            if (w.x > 0f || w.y > 0f || w.z > 0f || w.w > 0f) return true;
            foreach (var g in ikPose.Goals)
                if (g.WeightT > 0f || g.WeightR > 0f || g.HintWeightT > 0f) return true;
            return false;
        }

        /// <summary>Zeroes the goals' position, rotation and hint weights and the four look-at weights
        /// (positions, rotations, hints and the look-at position stay).</summary>
        internal static void ClearIKWeights(HumanPose ikPose)
        {
            for (int g = 0; g < GoalCount; g++)
            {
                ikPose.Goals[g].WeightT = 0f;
                ikPose.Goals[g].WeightR = 0f;
                ikPose.Goals[g].HintWeightT = 0f;
            }
            ikPose.LookAtWeight = F4.Zero;
        }

        /// <summary>The IK evaluation: the skeleton's root takes the IK pose's body transform; when
        /// the pose asks for a solve, or <paramref name="initHints"/> is set (the first IK step of an
        /// update, before OnAnimatorIK), the globals are computed, the hints initialised from them and
        /// the full-body solve run. Then the feet goals are kept for the next update and the weights
        /// cleared.</summary>
        /// <param name="ikPose">In/out: the world IK pose (hints written with <paramref name="initHints"/>,
        /// weights cleared).</param>
        /// <param name="local">In/out: the human skeleton pose, local (the previous IK step's result).</param>
        /// <param name="global">Output/workspace: the human skeleton pose, global.</param>
        /// <param name="ws">Workspace: the end bones' twist fix.</param>
        /// <param name="leftFootX">Output: the left foot goal as solved toward.</param>
        /// <param name="rightFootX">Output: the right foot goal.</param>
        internal static void EvaluateIK(Human h, HumanPose ikPose, SkeletonPose local, SkeletonPose global, SkeletonPose ws,
            bool initHints, out Trs leftFootX, out Trs rightFootX)
        {
            bool solve = NeedsSolve(ikPose);
            local.X[0] = ikPose.RootX;
            if (solve || initHints)
            {
                Skel.ComputeGlobal(h.Skeleton, local, global);
                if (initHints)
                    for (int g = 0; g < GoalCount; g++) ikPose.Goals[g].HintT = HintPositionOf(h, global, g);
                if (solve) SolveFullBody(h, ikPose, local, global, ws);
            }
            leftFootX = ikPose.Goals[LeftFoot].X;
            rightFootX = ikPose.Goals[RightFoot].X;
            ClearIKWeights(ikPose);
        }

        /// <summary>The end of the IK step: the twists spread over the limbs and the human skeleton
        /// pose written into the avatar's skeleton pose (unmapped nodes keep the avatar's T-pose, node 0
        /// the avatar root's transform, the hips relative to their avatar parent).</summary>
        /// <param name="local">Input: the human skeleton pose, local (node 0 the world body root).</param>
        /// <param name="humanWs">Workspace: the twist-solved copy of <paramref name="local"/>.</param>
        /// <param name="twistWs">Workspace: the twist fix's globals.</param>
        /// <param name="avatarPose">Output: the avatar skeleton pose, local.</param>
        internal static void EvaluateEnd(AvatarConstant c, Trs avatarX, SkeletonPose local, SkeletonPose humanWs,
            SkeletonPose twistWs, SkeletonPose avatarPose)
        {
            var human = c.Human;
            humanWs.CopyFrom(local);
            HumanOps.TwistSolve(human, humanWs, twistWs);
            int hipsParent = c.HumanSkeletonIndexArray[0];
            avatarPose.CopyFrom(c.AvatarSkeletonPose);
            avatarPose.X[0] = avatarX;
            Skel.ComputeGlobal(human.Skeleton, humanWs, humanWs, 1, 1);
            Skel.ComputeGlobal(c.AvatarSkeleton, avatarPose, avatarPose, hipsParent, 0);
            humanWs.X[0] = avatarPose.X[hipsParent];
            Skel.ComputeLocal(c.AvatarSkeleton, avatarPose, avatarPose, hipsParent, 0);
            Skel.ComputeLocal(human.Skeleton, humanWs, humanWs, 1, 1);
            humanWs.X[0] = avatarPose.X[hipsParent];
            for (int i = 0; i < c.HumanSkeletonIndexArray.Length; i++)
            {
                int a = c.HumanSkeletonIndexArray[i];
                if (a >= 0) avatarPose.X[a] = humanWs.X[i];
            }
        }

        /// <summary>A hint's default position in a global pose: the elbows at the lower arm; the knees
        /// at the lower leg, moved back by a quarter of the human scale along the foot's muscle-frame
        /// y axis.</summary>
        /// <param name="global">Input: the human skeleton pose, global.</param>
        internal static F4 HintPositionOf(Human h, SkeletonPose global, int goal)
        {
            int mid = h.BoneIndex[GoalMid[goal]];
            var t = global.X[mid].t;
            if (goal >= 2) return t;
            var s = h.Skeleton;
            int foot = h.BoneIndex[GoalEnd[goal]];
            var n = MuscleFrame(s, global.X[foot].q, foot);
            float yx = -(n.w * n.z) + n.y * n.x;
            float yy = (n.w * n.w + -0.5f) + n.y * n.y;
            float yz = n.w * n.x + n.y * n.z;
            float k = h.Scale * 0.25f;
            return new F4(t.x - k * (yx + yx), t.y - k * (yy + yy), t.z - k * (yz + yz), t.w);
        }

        // ---------------------------------------------------------------- full-body solve

        /// <summary>The full-body IK: look-at (clamp, then body, neck and head, eyes), then the four
        /// limbs' position goals with stretch and hints, then their rotation goals.</summary>
        /// <param name="ikPose">Input: the world IK pose (look-at, goals, hints and their weights).</param>
        /// <param name="local">In/out: the human skeleton pose, local (node 0 the world body root).</param>
        /// <param name="global">In/out (workspace): the human skeleton pose, global, consistent with
        /// <paramref name="local"/> on entry; only the chains the solve touched are current on exit.</param>
        /// <param name="ws">Workspace: the end bones' twist fix.</param>
        internal static void SolveFullBody(Human h, HumanPose ikPose, SkeletonPose local, SkeletonPose global, SkeletonPose ws)
        {
            var s = h.Skeleton;
            var b = h.BoneIndex;
            int hips = b[Bone.Hips], spine = b[Bone.Spine], chest = b[Bone.Chest], upperChest = b[Bone.UpperChest];
            int neck = b[Bone.Neck], head = b[Bone.Head], leftEye = b[Bone.LeftEye], rightEye = b[Bone.RightEye];
            var w = Clamp01(ikPose.LookAtWeight);
            var target = ikPose.LookAtPosition;

            // Look-at clamp (x): the target turned toward the head's forward axis.
            if (w.x > 0f) target = ClampLookAtTarget(h, global, target, w.x);

            // Body (y): spine, chest and upper chest each take half of the remaining turn, the last one all of it.
            if (w.y > 0f)
            {
                float wb = w.y, half = 0.5f * wb;
                LookAtBone(h, global, target, spine, chest != -1 ? half : wb, false);
                Skel.ComputeLocalQ(s, global, local, spine, spine);
                if (chest != -1)
                {
                    Skel.ComputeGlobal(s, local, global, head, spine);
                    LookAtBone(h, global, target, chest, upperChest != -1 ? half : wb, false);
                    Skel.ComputeLocalQ(s, global, local, chest, chest);
                    if (upperChest != -1)
                    {
                        Skel.ComputeGlobal(s, local, global, head, chest);
                        LookAtBone(h, global, target, upperChest, wb, false);
                        Skel.ComputeLocalQ(s, global, local, upperChest, upperChest);
                    }
                }
            }

            // Head (z): the neck with half the weight, then the head three times with the weight squared.
            // The head's globals are not recomputed after the neck turns, as the Editor does.
            if (w.z > 0f)
            {
                Skel.ComputeGlobal(s, local, global, head, spine);
                if (neck != -1)
                {
                    LookAtBone(h, global, target, neck, 0.5f * w.z, true);
                    Skel.ComputeLocalQ(s, global, local, neck, neck);
                }
                float wh = w.z * w.z;
                for (int i = 0; i < 3; i++)
                {
                    LookAtBone(h, global, target, head, wh, true);
                    Skel.ComputeLocalQ(s, global, local, head, head);
                    Skel.ComputeGlobal(s, local, global, head, head);
                }
            }

            // Eyes (w).
            if (w.w > 0f)
            {
                if (leftEye != -1) LookAtEye(h, local, global, target, leftEye, spine, w.w);
                if (rightEye != -1) LookAtEye(h, local, global, target, rightEye, spine, w.w);
            }

            // Position goals and hints.
            for (int g = 0; g < GoalCount; g++)
            {
                int top = b[GoalTop[g]], mid = b[GoalMid[g]], end = b[GoalEnd[g]];
                ref var goal = ref ikPose.Goals[g];
                if (goal.WeightT > 0f)
                {
                    float wt = Clamp01(goal.WeightT);
                    Skel.ComputeGlobal(s, local, global, end, hips);
                    var e = global.X[end].t;
                    var reach = (goal.X.t - e) * wt + e;
                    float stretch = (g < 2 ? h.LegStretch : h.ArmStretch) * wt;
                    TwoBoneAdjustLength(s, top, mid, end, reach, stretch, local, global);
                    Skel.ComputeGlobal(s, local, global, end, top);
                    TwoBoneIK(s, top, mid, end, goal.X.t, wt, local, global);
                }
                if (goal.HintWeightT > 0f)
                {
                    float wh = Clamp01(goal.HintWeightT);
                    Skel.ComputeGlobal(s, local, global, end, top);
                    TwoBoneAdjustHint(s, top, mid, end, goal.HintT, wh, local, global, 0.05f);
                }
            }

            // Rotation goals: the end bone's muscle frame turned toward the goal, its twist moved to the
            // middle bone.
            for (int g = 0; g < GoalCount; g++)
            {
                ref var goal = ref ikPose.Goals[g];
                if (!(goal.WeightR > 0f)) continue;
                float wr = Clamp01(goal.WeightR);
                int end = b[GoalEnd[g]];
                Skel.ComputeGlobal(s, local, global, end, hips);
                var postQ = AxesOf(s, end).PostQ;
                var cur = M.QNormalize(M.QMul(global.X[end].q, postQ));
                var n = M.QNormalize(SignedLerp(cur, goal.X.q, wr));
                var q = M.QNormalize(M.QMul(n, M.QConj(postQ)));
                local.X[end].q = M.QNormalize(M.QMul(M.QConj(global.X[s.Parent[end]].q), q));
                FixEndTwist(h, local, ws, GoalMid[g], GoalEnd[g], 0.05f, 1);
            }
        }

        /// <summary>The look-at target limited around the head's forward axis (from the eyes'
        /// midpoint, at the target's distance): beyond (1 - clamp) * 180 degrees from it, the turn is
        /// eased back toward none. The target is rebuilt from the turn even when it is not limited.</summary>
        static F4 ClampLookAtTarget(Human h, SkeletonPose global, F4 target, float clamp)
        {
            var s = h.Skeleton;
            int head = h.BoneIndex[Bone.Head];
            var headG = global.X[head];
            var headQ = MuscleFrame(s, headG.q, head);
            var eye = EyePosition(h, headG);
            var d = target - eye;
            float len = MathF.Sqrt(SumA(d));
            var forward = Rotate(headQ, new F4(0f, -len, 0f, 0f));
            var turn = M.QNormalize(Between(forward, d));
            float limit = Cos2Pi(((((1f - clamp) * 180f) * 0.017453292f) * 0.5f) * 0.15915494f);
            var q = M.QNormalize(turn);
            if (q.w < 0f) q = -q;
            if (limit > q.w)
            {
                float r = (turn.w * (1f - limit)) / ((1f - turn.w) * limit);
                float t = 1f - r;
                turn = M.QNormalize(SignedLerp(q, F4.QIdentity, t * t * t));
            }
            return eye + Rotate(turn, forward);
        }

        /// <summary>Turns a spine, neck or head bone so the point straight ahead of the head (at the
        /// target's distance) moves toward the target, by <paramref name="weight"/>. The forward point
        /// starts at the head, or at the eyes' midpoint with <paramref name="fromEyes"/>.</summary>
        /// <param name="global">In/out: the human skeleton pose, global (the bone's rotation).</param>
        static void LookAtBone(Human h, SkeletonPose global, F4 target, int bone, float weight, bool fromEyes)
        {
            var s = h.Skeleton;
            int head = h.BoneIndex[Bone.Head];
            var headG = global.X[head];
            var headQ = MuscleFrame(s, headG.q, head);
            var origin = fromEyes ? EyePosition(h, headG) : headG.t;
            float len = MathF.Sqrt(SumB(target - origin));
            var ahead = Rotate(headQ, new F4(0f, len, 0f, 0f));
            var bt = global.X[bone].t;
            var cur = (origin - ahead) - bt;
            var tgt = target - bt;
            TurnBy(global, bone, M.QNormalize(Between(cur, tgt)), weight);
        }

        /// <summary>Turns an eye so its forward axis (the muscle frame's x) points toward the target,
        /// by <paramref name="weight"/>.</summary>
        /// <param name="local">In/out: the pose, local (the eye's rotation).</param>
        /// <param name="global">In/out: the pose, global (eye..spine recomputed, the eye's rotation).</param>
        static void LookAtEye(Human h, SkeletonPose local, SkeletonPose global, F4 target, int eye, int spine, float weight)
        {
            Skel.ComputeGlobal(h.Skeleton, local, global, eye, spine);
            var eg = global.X[eye];
            var eq = MuscleFrame(h.Skeleton, eg.q, eye);
            float len = MathF.Sqrt(SumA(target - eg.t));
            var ahead = Rotate(eq, new F4(-1f * len, 0f, 0f, 0f));
            var cur = (eg.t - ahead) - eg.t;
            var tgt = target - eg.t;
            TurnBy(global, eye, M.QNormalize(Between(cur, tgt)), weight);
            Skel.ComputeLocalQ(h.Skeleton, global, local, eye, eye);
        }

        /// <summary>A node's global rotation turned by <paramref name="q"/> with its vector part scaled
        /// by <paramref name="weight"/>.</summary>
        static void TurnBy(SkeletonPose global, int node, F4 q, float weight)
        {
            var qs = M.QNormalize(new F4(weight * q.x, weight * q.y, weight * q.z, q.w));
            global.X[node].q = M.QNormalize(M.QMul(qs, global.X[node].q));
        }

        /// <summary>The eyes' midpoint from the head's global transform (the T-pose eye offsets), or
        /// the head's position when an eye is missing.</summary>
        static F4 EyePosition(Human h, in Trs headG)
        {
            int l = h.BoneIndex[Bone.LeftEye], r = h.BoneIndex[Bone.RightEye];
            if (l == -1 || r == -1) return headG.t;
            var tp = h.SkeletonPose.X;
            var mid = (tp[l].t + tp[r].t) * new F4(0.5f, 0.5f, 0.5f, 0f) * headG.s;
            return Rotate(headG.q, mid) + headG.t;
        }

        // ---------------------------------------------------------------- two-bone limb

        /// <summary>Stretches the limb (top-middle-end) toward an out-of-reach target: when the target
        /// is farther than (1 - stretch) of the limb's length, the middle and end bones' local
        /// translations scale by up to 1 + stretch (eased in quadratically).</summary>
        /// <param name="target">Input: the world position the end should reach.</param>
        /// <param name="local">In/out: the pose, local (the middle and end translations).</param>
        /// <param name="global">Input: the pose, global (top, middle, end current).</param>
        internal static void TwoBoneAdjustLength(Skeleton s, int top, int mid, int end, F4 target, float stretch,
            SkeletonPose local, SkeletonPose global)
        {
            var g = global.X;
            var a = g[mid].t - g[top].t;
            var b = g[end].t - g[mid].t;
            var d = target - g[top].t;
            float limit = 1f - stretch;
            float rA = MathF.Sqrt(SumA(d)) / (MathF.Sqrt(SumA(b)) + MathF.Sqrt(SumA(a)));
            if (!(rA > limit)) return;
            float rB = MathF.Sqrt(SumB(d)) / (MathF.Sqrt(SumB(b)) + MathF.Sqrt(SumB(a)));
            float kA = StretchScale(rA, limit, stretch), kB = StretchScale(rB, limit, stretch);
            var tm = local.X[mid].t;
            local.X[mid].t = new F4(tm.x * kA, tm.y * kB, tm.z * kA, tm.w * kB);
            var te = local.X[end].t;
            local.X[end].t = new F4(te.x * kA, te.y * kB, te.z * kA, te.w * kB);
        }

        static float StretchScale(float ratio, float limit, float stretch)
        {
            float x = (ratio - limit) / (stretch + stretch);
            x = 0f > x ? 0f : x;
            x = 1f < x ? 1f : x;
            return stretch * x * x + 1f;
        }

        /// <summary>The two-bone limb IK: the middle bone (knee, elbow) bends so the top-end distance
        /// matches the goal's (its bend muscle first relaxed to 90%, its side muscle zeroed), blended by
        /// <paramref name="weight"/>; then the top bone turns the end onto the goal, blended by
        /// 1 - (1 - weight)^4.</summary>
        /// <param name="goal">Input: the world goal position.</param>
        /// <param name="local">In/out: the pose, local (top and middle bones; the middle bone's side
        /// muscle).</param>
        /// <param name="global">In/out: the pose, global (top..end current on entry; top, middle and end
        /// current on exit).</param>
        internal static void TwoBoneIK(Skeleton s, int top, int mid, int end, F4 goal, float weight,
            SkeletonPose local, SkeletonPose global)
        {
            var qTop0 = local.X[top].q;
            var qMid0 = local.X[mid].q;
            var dof = Skel.GetDoF(s, local, mid);
            Skel.SetDoF(s, local, new F4(dof.x * 1f, dof.y * 0f, dof.z * 0.9f, dof.w * 0f), mid);
            Skel.ComputeGlobal(s, local, global, end, mid);
            var g = global.X;
            var a = g[mid].t - g[top].t;
            var b = g[end].t - g[mid].t;
            var c = g[end].t - g[top].t;
            var d = goal - g[top].t;

            // Half the bend change, per reduction lane.
            float hA = HalfBendChange(SumA(a), SumA(b), SumA(c), SumA(d));
            float hB = HalfBendChange(SumB(a), SumB(b), SumB(c), SumB(d));
            var n = M.Cross(a, b);
            float iA = 1f / MathF.Sqrt(SumA(n)), iB = 1f / MathF.Sqrt(SumB(n));
            var bend = new F4(
                Cos2Pi(hA * 0.15915494f + -0.25f) * (iA * n.x),
                Cos2Pi(hB * 0.15915494f + -0.25f) * (iB * n.y),
                Cos2Pi(hA * 0.15915494f + -0.25f) * (iA * n.z),
                Cos2Pi(hB * 0.15915494f + 0f) * 1f);
            g[mid].q = M.QNormalize(M.QMul(bend, g[mid].q));
            LocalOf(s, global, local, mid);
            local.X[mid].q = M.QNormalize(SignedLerp(qMid0, local.X[mid].q, weight));

            // The top bone turns the end onto the goal.
            Skel.ComputeGlobal(s, local, global, end, mid);
            var e = g[end].t - g[top].t;
            g[top].q = M.QMul(M.QNormalize(Between(e, d)), g[top].q);
            LocalOf(s, global, local, top);
            float back = Pow4(1f - weight);
            local.X[top].q = M.QNormalize(SignedLerp(local.X[top].q, qTop0, back));
        }

        /// <summary>Half the change of the middle joint's angle from the limb's current top-end length
        /// to the goal's (squared lengths of top-middle, middle-end, top-end and top-goal).</summary>
        static float HalfBendChange(float aa, float bb, float cc, float dd)
        {
            float la = MathF.Sqrt(aa), lb = MathF.Sqrt(bb), lc = MathF.Sqrt(cc), ld = MathF.Sqrt(dd);
            float lalb = la * lb;
            float sum = lb * lb + la * la;
            float now = Acos(ClampUnit((sum - lc * lc) / lalb * 0.5f));
            float wanted = Acos(ClampUnit((sum - ld * ld) / lalb * 0.5f));
            return (now - wanted) * 0.5f;
        }

        /// <summary>Turns the limb around its top-end axis so the middle bone moves toward the hint,
        /// by <paramref name="weight"/> (faded out as the limb straightens: no turn when the middle
        /// bone is within <paramref name="minLength"/> of the limb's length from the axis).</summary>
        /// <param name="hint">Input: the world hint position.</param>
        /// <param name="local">Output: the top bone's local.</param>
        /// <param name="global">In/out: the pose, global (top..end current; the top rotation).</param>
        internal static void TwoBoneAdjustHint(Skeleton s, int top, int mid, int end, F4 hint, float weight,
            SkeletonPose local, SkeletonPose global, float minLength)
        {
            var g = global.X;
            F4 tt = g[top].t, mt = g[mid].t, et = g[end].t;
            var axis = et - tt;
            float lA = MathF.Sqrt(SumA(axis));
            if (!(lA > 0f)) return;
            float lB = MathF.Sqrt(SumB(axis));
            var dir = new F4(axis.x / lA, axis.y / lA, axis.z / lA, axis.w / lB);
            var m = mt - tt;
            var pm = Reject(m, dir);
            float lenPm = MathF.Sqrt(SumA(pm));
            if (!(lenPm > 0f)) return;
            var ph = Reject(hint - tt, dir);
            float lenPh = MathF.Sqrt(SumA(ph));
            if (!(lenPh > 0f)) return;
            float lenM = MathF.Sqrt(SumA(m));
            float lenB = MathF.Sqrt(SumA(et - mt));
            float min = (lenB + lenM) * minLength;
            if (!(lenPm > min)) return;
            float min2 = min + min;
            float fade = lenPm > min2 ? 1f : 1f - (min2 - lenPm) / (min2 - min);
            var q = M.QNormalize(Between(pm, ph));
            float sw = fade * weight;
            var qs = new F4(sw * q.x, sw * q.y, sw * q.z, q.w);
            g[top].q = M.QNormalize(M.QMul(qs, g[top].q));
            LocalOf(s, global, local, top);
        }

        /// <summary>v without its component along dir (the dot product per reduction lane).</summary>
        static F4 Reject(F4 v, F4 dir)
        {
            float dA = Dot3A(v, dir), dB = Dot3B(v, dir);
            return new F4(v.x - dA * dir.x, v.y - dB * dir.y, v.z - dA * dir.z, v.w - dB * dir.w);
        }

        // ---------------------------------------------------------------- end twist

        /// <summary>The end bone's twist moved onto the middle bone (the same fix the retargeting uses
        /// after aligning hands and feet): the first step resets both to their zero muscle rotation,
        /// later steps drop the end bone's roll; the middle bone turns to keep the end bone's global
        /// rotation and its axis direction.</summary>
        /// <param name="local">In/out: the pose, local.</param>
        /// <param name="ws">Workspace: globals relative to the middle bone's parent.</param>
        static void FixEndTwist(Human h, SkeletonPose local, SkeletonPose ws, int midBone, int endBone, float minAngle, int iterations)
        {
            const float Rad2Deg = 57.2957802f;
            var s = h.Skeleton;
            int mid = h.BoneIndex[midBone], end = h.BoneIndex[endBone];
            ws.X[s.Parent[mid]].q = F4.QIdentity;
            Skel.ComputeGlobalQ(s, local, ws, end, mid);
            var refQ = local.X[mid].q;
            if (iterations <= 0) return;
            var midAxes = AxesOf(s, mid);
            var endAxes = AxesOf(s, end);
            var endGlobal = ws.X[end].q;
            var midZero = M.QNormalize(M.QMul(midAxes.PreQ, M.QConj(midAxes.PostQ)));
            var endZero = M.QNormalize(M.QMul(endAxes.PreQ, M.QConj(endAxes.PostQ)));
            float prev = 360f;
            for (int i = 0; ; )
            {
                var qp = M.QNormalize(M.QMul(M.QConj(endAxes.PreQ), M.QMul(local.X[end].q, endAxes.PostQ)));
                var t = M.Quat2Qtan(qp);
                float a = MathF.Atan(t.x);
                float angle = MathF.Abs((a + a) * Rad2Deg);
                if (minAngle > angle) return;
                if (i >= 2 && angle > prev) return;
                int stop;
                if (i == 0)
                {
                    local.X[mid].q = midZero;
                    local.X[end].q = endZero;
                    stop = mid;
                }
                else
                {
                    var zy = M.Quat2ZYRoll(qp);
                    var q = M.ZYRoll2Quat(new F4(0f, zy.y, zy.z, 0f));
                    local.X[end].q = M.QNormalize(M.QMul(endAxes.PreQ, M.QMul(q, M.QConj(endAxes.PostQ))));
                    stop = end;
                }
                Skel.ComputeGlobalQ(s, local, ws, end, stop);
                local.X[mid].q = M.QNormalize(M.QMul(M.QMul(endGlobal, M.QConj(ws.X[end].q)), local.X[mid].q));
                local.X[mid].q = Skel.Align(refQ, local.X[mid].q, midAxes.PostQ);
                Skel.ComputeGlobalQ(s, local, ws, end, mid);
                ws.X[end].q = endGlobal;
                Skel.ComputeLocalQ(s, ws, local, end, end);
                i++;
                if (i >= iterations) return;
                prev = angle;
            }
        }

        // ---------------------------------------------------------------- math as the IK computes it

        static Axes AxesOf(Skeleton s, int node)
        {
            int id = s.AxesId[node];
            if (id == -1) throw new InvalidOperationException($"humanoid IK: skeleton node {node} has no muscle axes");
            return s.AxesArray[id];
        }

        /// <summary>A node's global rotation in its muscle frame.</summary>
        static F4 MuscleFrame(Skeleton s, F4 q, int node) => M.QNormalize(M.QMul(q, AxesOf(s, node).PostQ));

        /// <summary>A node's full local transform from its parent's global and its own (the IK's own
        /// inverse: reciprocal scales refined from the hardware estimate, rotation not normalized).</summary>
        static void LocalOf(Skeleton s, SkeletonPose global, SkeletonPose local, int index)
        {
            int parent = s.Parent[index];
            if (parent == -1)
            {
                local.X[index] = global.X[index];
                return;
            }
            var p = global.X[parent];
            var x = global.X[index];
            var qc = M.QConj(p.q);
            var inv = new F4(RcpSafe(p.s.x, 1e-9f), RcpSafe(p.s.y, 1e-9f), RcpSafe(p.s.z, 1e-9f), RcpSafe(p.s.w, 0f));
            local.X[index] = new Trs(Rotate(qc, x.t - p.t) * inv, M.QMul(qc, x.q), inv * x.s);
        }

        static float RcpSafe(float v, float eps) => MathF.Abs(v) < eps ? 0f : Rcp(v);

        /// <summary>A reciprocal from the processor's estimate refined by two Newton steps (the
        /// estimate when the refinement is NaN). Without SSE the estimate is the exact reciprocal.</summary>
        static float Rcp(float v)
        {
            float r = Sse.IsSupported ? Sse.ReciprocalScalar(Vector128.CreateScalarUnsafe(v)).ToScalar() : 1f / v;
            float r1 = (2.0000005f - r * v) * r;
            float r2 = (2f - r1 * v) * r1;
            return float.IsNaN(r2) ? r : r2;
        }

        /// <summary>v rotated by q, the rotation's columns summed z and y first, then x with v.</summary>
        static F4 Rotate(F4 q, F4 v)
        {
            float x = q.x, y = q.y, z = q.z, w = q.w;
            float c0x = y * -2f * y - z * 2f * z, c0y = y * 2f * x - z * -2f * w, c0z = y * -2f * w - z * -2f * x;
            float c1x = z * -2f * w - x * -2f * y, c1y = z * -2f * z - x * 2f * x, c1z = z * 2f * y - x * -2f * w;
            float c2x = x * 2f * z - y * -2f * w, c2y = x * -2f * w - y * -2f * z, c2z = x * -2f * x - y * 2f * y;
            return new F4(
                (v.z * c2x + v.y * c1x) + (v.x * c0x + v.x),
                (v.z * c2y + v.y * c1y) + (v.x * c0y + v.y),
                (v.z * c2z + v.y * c1z) + (v.x * c0z + v.z), 0f);
        }

        /// <summary>The (unnormalized) rotation from one direction to another: (from x to,
        /// |from||to| + from.to).</summary>
        static F4 Between(F4 from, F4 to)
        {
            var c = M.Cross(from, to);
            return new F4(c.x, c.y, c.z, MathF.Sqrt(SumA(to) * SumA(from)) + Dot3A(to, from));
        }

        // The squared length and dot product of the xyz parts, grouped as the two reduction lanes give
        // them: lanes x and z sum z + (y + x), lanes y and w x + (z + y).
        static float SumA(F4 v) => v.z * v.z + (v.y * v.y + v.x * v.x);

        static float SumB(F4 v) => v.x * v.x + (v.z * v.z + v.y * v.y);

        static float Dot3A(F4 a, F4 b) => a.z * b.z + (a.y * b.y + a.x * b.x);

        static float Dot3B(F4 a, F4 b) => a.x * b.x + (a.z * b.z + a.y * b.y);

        /// <summary>from + (±to - from) * t, each component of to taking the sign of the quaternion dot
        /// product as its reduction lane gives it.</summary>
        static F4 SignedLerp(F4 from, F4 to, float t)
        {
            float dA = (from.w * to.w + from.z * to.z) + (from.y * to.y + from.x * to.x);
            float dB = (from.x * to.x + from.w * to.w) + (from.z * to.z + from.y * to.y);
            float sx = SignOf(to.x, dA), sy = SignOf(to.y, dB), sz = SignOf(to.z, dA), sw = SignOf(to.w, dB);
            return new F4((sx - from.x) * t + from.x, (sy - from.y) * t + from.y, (sz - from.z) * t + from.z, (sw - from.w) * t + from.w);
        }

        /// <summary>v with its sign flipped where s's sign bit is set.</summary>
        static float SignOf(float v, float s) =>
            BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(v) ^ (BitConverter.SingleToInt32Bits(s) & int.MinValue));

        /// <summary>q normalized, or the identity when its squared length is at most 1e-30 (per
        /// reduction lane).</summary>
        static F4 NormalizeSafe(F4 q)
        {
            float dA = (q.w * q.w + q.z * q.z) + (q.y * q.y + q.x * q.x);
            float dB = (q.x * q.x + q.w * q.w) + (q.z * q.z + q.y * q.y);
            bool okA = 1e-30f < dA, okB = 1e-30f < dB;
            return new F4(
                okA ? q.x / MathF.Sqrt(dA) : 0f,
                okB ? q.y / MathF.Sqrt(dB) : 0f,
                okA ? q.z / MathF.Sqrt(dA) : 0f,
                okB ? q.w / MathF.Sqrt(dB) : 1f);
        }

        /// <summary>Each component clamped to 0..1 (NaN passes through).</summary>
        static F4 Clamp01(F4 v) => new F4(Clamp01(v.x), Clamp01(v.y), Clamp01(v.z), Clamp01(v.w));

        static float Clamp01(float v)
        {
            v = 0f > v ? 0f : v;
            return 1f < v ? 1f : v;
        }

        /// <summary>Clamped to -1..1 (NaN becomes -1).</summary>
        static float ClampUnit(float v)
        {
            v = v > -1f ? v : -1f;
            return v < 1f ? v : 1f;
        }

        /// <summary>The arc cosine as the IK approximates it: the arc tangent of |sqrt(1 - x²) / x|
        /// (inverted above 1) by a degree-13 odd polynomial, mirrored for negative x.</summary>
        static float Acos(float x)
        {
            float t = MathF.Abs(MathF.Sqrt(1f - x * x) / x);
            bool small = !(1f < t);
            float u = small ? t : Rcp(t);
            float s = u * u, s2 = s * s, s3 = s * s2, s4 = s * s3, s5 = s * s4, s6 = s * s5;
            float p = s * -0.3332646f + 0.99999946f;
            p = s2 * 0.19881524f + p;
            p = s3 * -0.13487266f + p;
            p = s4 * 0.08387136f + p;
            p = s5 * -0.037013143f + p;
            p = s6 * 0.007862508f + p;
            p = p * u;
            float a = small ? p : 1.5707964f - p;
            return float.IsNegative(x) ? 3.1415927f - a : a;
        }

        /// <summary>cos(2πy) as the IK approximates it (a polynomial in the distance to the nearest
        /// quarter turn); sin(h) is Cos2Pi(h / 2π - 0.25).</summary>
        static float Cos2Pi(float y)
        {
            float z = 0.25f - MathF.Abs(y - MathF.Round(y));
            float z2 = z * z, z4 = z2 * z2;
            float t1 = -76.56862f * z2 + 81.60182f;
            float t2 = z2 * -41.34166f + 6.283185f;
            t1 = t1 * z4 + t2;
            float t3 = z4 * z4 * 39.657032f + t1;
            return t3 * z;
        }

        /// <summary>x⁴ for x in 0..1 as the IK approximates it (polynomial base-2 logarithm and
        /// exponential; exactly 0 at 0 and 1 at 1).</summary>
        static float Pow4(float x)
        {
            int bits = BitConverter.SingleToInt32Bits(x);
            const int ExponentField = 255 << 23, ExponentOne = 127 << 23;
            float m = BitConverter.Int32BitsToSingle((bits & ~ExponentField) | ExponentOne) + -1f;
            float e = (float)(bits >> 23) + -127f;
            float log = (m * 1.4379071f + e) + (0.19438113f * m + -0.6322882f) * (m * m);
            float y = log * 4f;
            y = y > -127f ? y : -127f;
            float r = MathF.Round(y);
            float fl = r - (y < r ? 1f : 0f);
            float f = y - fl;
            float p = f * f * 0.31772992f + (f * 0.6822701f + 1f);
            float pow2 = BitConverter.Int32BitsToSingle(((int)fl << 23) + ExponentOne);
            return pow2 * p;
        }

        /// <summary>The fields a full human pose copy takes: root, look-at, goals, body, finger and
        /// translation DoFs.</summary>
        static void CopyPoseFields(HumanPose dst, HumanPose src)
        {
            dst.RootX = src.RootX;
            dst.LookAtPosition = src.LookAtPosition;
            dst.LookAtWeight = src.LookAtWeight;
            Array.Copy(src.Goals, dst.Goals, dst.Goals.Length);
            Array.Copy(src.DoF, dst.DoF, dst.DoF.Length);
            Array.Copy(src.LeftHand.DoF, dst.LeftHand.DoF, dst.LeftHand.DoF.Length);
            Array.Copy(src.RightHand.DoF, dst.RightHand.DoF, dst.RightHand.DoF.Length);
            Array.Copy(src.TDoF, dst.TDoF, dst.TDoF.Length);
        }
    }
}
