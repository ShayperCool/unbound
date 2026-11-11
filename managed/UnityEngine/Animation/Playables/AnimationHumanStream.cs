using System;
using Unbound.Runtime;
using Unbound.Runtime.Humanoid;
using Unbound.Runtime.Playables;
using UnityEngine;
using HumanPose = Unbound.Runtime.Humanoid.HumanPose;

namespace Unbound.Runtime.Playables
{
    /// <summary>
    /// The human pose of a humanoid Animator's animation stream while an animation job runs
    /// (<c>AnimationStream.AsHuman</c>), as the Unity 6000.0.71f1 Editor keeps it
    /// (tests/projects/humanoid stream.*):
    /// <list type="bullet">
    /// <item>the pose the evaluation below gave: the body and the goals in the avatar root's frame, in
    /// human scale units (what the Animator retargets), muscles, goal weights, hints and look-at;</item>
    /// <item>the human skeleton pose it gives (world, node 0 the body), made on demand from the pose
    /// and the avatar root;</item>
    /// <item>goal positions and rotations a job set (kept beside the pose: only a solve, or a pose made
    /// again from the skeleton, takes them), and which hints it set.</item>
    /// </list>
    /// Changing the pose (body, muscles) makes the skeleton stale; solving the IK or resetting to the
    /// stance pose changes the skeleton and makes the pose stale, so the next read of the pose
    /// (and the end of the job) makes it again from the skeleton. The avatar root is where the
    /// update's root motion moves it, as the root motion pass runs before the animation pass.
    /// At the end of the job a changed pose goes back into the stream's humanoid curves.
    /// </summary>
    internal sealed class HumanStreamPose
    {
        readonly AvatarConstant m_Constant;
        readonly Human m_Human;
        internal readonly HumanPose Pose = new HumanPose();
        readonly HumanPose m_World = new HumanPose();
        readonly SkeletonPose m_Local, m_Global, m_Source, m_W4, m_W5, m_W6, m_W7;
        readonly float[] m_Values = new float[HumanClipImport.CurveCount];
        readonly Trs[] m_Saved = new Trs[GoalCount];
        readonly bool[] m_GoalSet = new bool[GoalCount], m_HintSet = new bool[GoalCount];
        readonly F4[] m_GoalT = new F4[GoalCount], m_GoalQ = new F4[GoalCount];
        F4[] m_TDoFBase;
        Trs m_LoadedBody, m_CurveRoot;
        readonly Trs[] m_LoadedGoals = new Trs[GoalCount];
        bool m_HumanValid, m_SkeletonValid, m_Changed;
        AnimStream m_Stream;

        const int GoalCount = 4;

        /// <summary>The avatar root's world transform the job reads and writes world values in.</summary>
        internal Trs Root;

        static readonly bool[] s_AllPresent = AllPresent();

        static bool[] AllPresent()
        {
            var present = new bool[HumanClipImport.CurveCount];
            Array.Fill(present, true);
            return present;
        }

        internal HumanStreamPose(AvatarConstant constant)
        {
            m_Constant = constant;
            m_Human = constant.Human;
            int n = m_Human.Skeleton.Count;
            m_Local = new SkeletonPose(n);
            m_Global = new SkeletonPose(n);
            m_Source = new SkeletonPose(n);
            m_W4 = new SkeletonPose(n);
            m_W5 = new SkeletonPose(n);
            m_W6 = new SkeletonPose(n);
            m_W7 = new SkeletonPose(n);
        }

        internal AvatarConstant Constant => m_Constant;

        internal float HumanScale => m_Human.Scale;

        /// <summary>The pose of a stream: its humanoid curves (goals taken relative to the body when
        /// they are in the motion's frame) placed by its body motion as the Animator places them
        /// (<see cref="HumanRootMotion.ApplyToPose"/>), and the IK values an earlier job left in it.</summary>
        internal void Load(AnimStream stream, in Trs root)
        {
            m_Stream = stream;
            Root = root;
            var b = stream.Bindings;
            for (int c = 0; c < m_Values.Length; c++)
            {
                int ch = b.HumanChannel(c);
                m_Values[c] = ch >= 0 && ch < stream.FloatCount ? stream.Values[ch] : AnimatorHuman.DefaultValue(c);
            }
            HumanClipImport.PoseFromCurves(m_Values, s_AllPresent, Pose);
            m_CurveRoot = Pose.RootX;
            var sample = stream.BodyMotion;
            if (!sample.HasMotion)
                Stubs.Ignored("AnimationHumanStream without humanoid body motion",
                    "takes the body and goals of a stream without clip body motion below it (a controller playable while no humanoid clip plays, the previous outputs' pose) as its curves hold them (not measured)");
            if (sample.HasMotion)
            {
                if (stream.GoalsInMotion)
                    for (int g = 0; g < GoalCount; g++) Pose.Goals[g].X = M.InvMul(sample.Body, Pose.Goals[g].X);
                HumanRootMotion.ApplyToPose(Pose, sample);
            }
            // The stream's buffer keeps the IK values from the job's last run until a job changes
            // them: the jobs of a chain share one buffer, each update starts from it (measured,
            // tests/projects/humanoid stream.ik, stream.chained).
            if (stream.HumanIK.Has) stream.HumanIK.LoadInto(Pose);
            else if (stream.Owner?.PersistentHumanIK is { } kept) kept.LoadInto(Pose);
            else StreamHumanIK.Clear(Pose);
            m_LoadedBody = Pose.RootX;
            for (int g = 0; g < GoalCount; g++) m_LoadedGoals[g] = Pose.Goals[g].X;
            m_HumanValid = true;
            m_SkeletonValid = false;
            m_Changed = false;
            Array.Clear(m_GoalSet);
            Array.Clear(m_HintSet);
        }

        /// <summary>The end of the job: the pose (made again from the skeleton when stale) goes back
        /// into the stream's humanoid curves when it changed, and its IK values stay in the stream
        /// for the jobs above.</summary>
        internal void Store()
        {
            var stream = m_Stream;
            if (stream == null) return;
            UpdateHumanPose();
            TakeSetGoals();
            stream.HumanIK.StoreFrom(Pose);
            if (stream.Owner != null) (stream.Owner.PersistentHumanIK ??= new StreamHumanIK()).StoreFrom(Pose);
            if (m_Changed) WriteCurves(stream);
            m_Stream = null;
        }

        void WriteCurves(AnimStream s)
        {
            var b = s.Bindings;
            bool placed = !Same(Pose.RootX, m_LoadedBody);
            for (int g = 0; g < GoalCount && !placed; g++) placed = !Same(Pose.Goals[g].X, m_LoadedGoals[g]);
            if (placed)
            {
                var body = Pose.RootX;
                var sample = s.BodyMotion;
                if (sample.HasMotion)
                {
                    // The body relative to the motion takes the change, so the Animator's placement
                    // (ApplyToPose: the sample's body plus what the root curves hold beyond the
                    // clips') gives this body, and mixers above blend it as a clip's; the goals stay
                    // in the stream's frame (the motion's, or relative to the body).
                    var root = m_CurveRoot;
                    var extra = M.QRotate(M.QConj(sample.Motion.q), root.t - sample.Root.t);
                    var turn = M.QMul(M.QConj(sample.Root.q), root.q);
                    bool curvesBeyond = !Same(root, sample.Root);
                    sample.Body = curvesBeyond
                        ? new Trs(body.t - extra, M.QNormalize(M.QMul(body.q, M.QConj(turn))), F4.One3)
                        : new Trs(body.t, M.QNormalize(body.q), F4.One3);
                    s.BodyMotion = sample;
                    // Goals relative to the body; in the motion's frame through the stream's body
                    // (the output takes them back relative to it, then places them on the body).
                    for (int g = 0; g < GoalCount; g++)
                    {
                        var relative = M.InvMul(body, Pose.Goals[g].X);
                        Write(s, b, HumanClipImport.GoalFirst + 7 * g, s.GoalsInMotion ? M.Mul(sample.Body, relative) : relative);
                    }
                }
                else
                {
                    Write(s, b, HumanClipImport.RootFirst, body);
                    for (int g = 0; g < GoalCount; g++) Write(s, b, HumanClipImport.GoalFirst + 7 * g, Pose.Goals[g].X);
                }
            }
            for (int i = 0; i < Pose.DoF.Length; i++) Write(s, b, HumanClipImport.MuscleFirst + i, Pose.DoF[i]);
            for (int i = 0; i < Pose.LeftHand.DoF.Length; i++)
            {
                Write(s, b, HumanClipImport.LeftHandFirst + i, Pose.LeftHand.DoF[i]);
                Write(s, b, HumanClipImport.RightHandFirst + i, Pose.RightHand.DoF[i]);
            }
            for (int i = 0; i < Pose.TDoF.Length; i++)
            {
                int c = HumanClipImport.TDoFFirst + 3 * i;
                Write(s, b, c, Pose.TDoF[i].x);
                Write(s, b, c + 1, Pose.TDoF[i].y);
                Write(s, b, c + 2, Pose.TDoF[i].z);
            }
        }

        static bool Same(in Trs a, in Trs b) =>
            a.t.x == b.t.x && a.t.y == b.t.y && a.t.z == b.t.z && a.t.w == b.t.w && a.q.x == b.q.x && a.q.y == b.q.y && a.q.z == b.q.z && a.q.w == b.q.w;

        static void Write(AnimStream s, AnimationBindings b, int curve, in Trs x)
        {
            Write(s, b, curve, x.t.x); Write(s, b, curve + 1, x.t.y); Write(s, b, curve + 2, x.t.z);
            Write(s, b, curve + 3, x.q.x); Write(s, b, curve + 4, x.q.y); Write(s, b, curve + 5, x.q.z); Write(s, b, curve + 6, x.q.w);
        }

        static void Write(AnimStream s, AnimationBindings b, int curve, float value)
        {
            int ch = b.HumanChannel(curve);
            if (ch < 0 || ch >= s.FloatCount) return;
            s.Values[ch] = value;
            s.Animated[ch] = true;
        }

        // ---------------------------------------------------------------- pose and skeleton

        /// <summary>The skeleton pose of the current human pose, placed at the avatar root (node 0 the
        /// world body), when the pose changed since it was made.</summary>
        void UpdateSkeletonPose()
        {
            if (m_SkeletonValid) return;
            m_SkeletonValid = true;
            HumanOps.RetargetTo(m_Human, Pose, null, Root, m_World, m_Local, m_Global, true);
        }

        /// <summary>The human pose of the current skeleton pose, when the skeleton changed since: the
        /// muscles and the body from the skeleton (its hips placed where its body puts them, under an
        /// identity root), the body taken into the avatar root's frame; the goals stay, or take the
        /// positions and rotations the job set.</summary>
        void UpdateHumanPose()
        {
            if (m_HumanValid) return;
            m_HumanValid = true;
            m_Changed = true;
            for (int g = 0; g < GoalCount; g++) m_Saved[g] = Pose.Goals[g].X;
            var s = m_Human.Skeleton;
            int hips = m_Human.BoneIndex[Bone.Hips];
            var hipsX = M.Mul(m_Local.X[s.Parent[hips]], m_Local.X[hips]);
            m_Source.CopyFrom(m_Local);
            m_Source.X[hips] = hipsX;
            m_Source.X[0] = Trs.Identity;
            if (m_Human.HasTDoF && m_TDoFBase == null) m_TDoFBase = HumanOps.TDoFBase(m_Human, m_W4);
            HumanOps.RetargetFrom(m_Human, m_Source, Pose, m_W4, m_W5, m_W6, m_W7, m_TDoFBase);
            float hs = m_Human.Scale;
            var body = Pose.RootX;
            body.t = body.t * hs;
            body = M.InvMul(Root, body);
            body.t = body.t / hs;
            body.s = F4.One3;
            Pose.RootX = body;
            // The skeleton keeps that identity root, its hips placed under the avatar root without
            // the root's scale: positions read from it later (GetGoalPositionFromPose, hints) are
            // the root's position plus the offsets in the root's frame unscaled
            // (tests/projects/humanoid stream.ik, stream.stance).
            m_Local.X[0] = Trs.Identity;
            m_Local.X[hips] = M.Mul(new Trs(Root.t, Root.q, F4.One3), M.InvMul(Root, hipsX));
            for (int g = 0; g < GoalCount; g++) Pose.Goals[g].X = m_Saved[g];
            TakeSetGoals();
        }

        /// <summary>The goals the job set go into the pose, back in the pose's goal frame (a foot's
        /// goal along its x axis by the foot's height from where the API places it), solved or not:
        /// the Animator's own goal step (retargeting, Foot IK) then works from them (measured,
        /// tests/projects/humanoid stream.ik, stream.localGoals, stream.ikNoSolve).</summary>
        void TakeSetGoals()
        {
            for (int g = 0; g < GoalCount; g++)
            {
                if (!m_GoalSet[g]) continue;
                ref var x = ref Pose.Goals[g].X;
                x.q = m_GoalQ[g];
                x.t = g < 2 ? m_GoalT[g] + M.QRotate(x.q, new F4(FootHeight(g == 0), 0f, 0f, 0f)) : m_GoalT[g];
                m_Changed = true;
            }
        }

        void PoseChanged()
        {
            m_Changed = true;
            m_SkeletonValid = false;
        }

        F4 ToLocalPoint(Vector3 world) => M.InvMulPoint(Root, V(world)) / m_Human.Scale;

        Vector3 ToWorldPoint(F4 local) => P(M.MulPoint(Root, local * m_Human.Scale));

        /// <summary>The foot goals' positions are kept at the ankle; the API gives them at the sole:
        /// the goal moved back along its x axis by the foot's height.</summary>
        F4 GoalPositionOf(int g, F4 t, F4 q)
        {
            if (g >= 2) return t;
            int foot = m_Human.BoneIndex[g == 0 ? Bone.LeftFoot : Bone.RightFoot];
            var s = m_Human.Skeleton;
            float height = s.AxesArray[s.AxesId[foot]].Length;
            return t - M.QRotate(q, new F4(height, 0f, 0f, 0f));
        }

        F4 GoalLocalPosition(int g) => m_GoalSet[g] ? m_GoalT[g] : GoalPositionOf(g, Pose.Goals[g].X.t, Pose.Goals[g].X.q);

        F4 GoalLocalRotation(int g) => m_GoalSet[g] ? m_GoalQ[g] : Pose.Goals[g].X.q;

        /// <summary>The first goal value a job sets keeps the other half of the goal from the pose.</summary>
        void SetGoal(int g)
        {
            if (m_GoalSet[g]) return;
            m_GoalT[g] = GoalPositionOf(g, Pose.Goals[g].X.t, Pose.Goals[g].X.q);
            m_GoalQ[g] = Pose.Goals[g].X.q;
            m_GoalSet[g] = true;
        }

        // ---------------------------------------------------------------- members

        internal float FootHeight(bool left)
        {
            int foot = m_Human.BoneIndex[left ? Bone.LeftFoot : Bone.RightFoot];
            var s = m_Human.Skeleton;
            return s.AxesArray[s.AxesId[foot]].Length;
        }

        internal Vector3 BodyLocalPosition
        {
            get { UpdateHumanPose(); return P(Pose.RootX.t); }
            set { UpdateHumanPose(); Pose.RootX.t = V(value); PoseChanged(); }
        }

        internal Quaternion BodyLocalRotation
        {
            get { UpdateHumanPose(); return Q(Pose.RootX.q); }
            set { UpdateHumanPose(); Pose.RootX.q = Q(value); PoseChanged(); }
        }

        internal Vector3 BodyPosition
        {
            get { UpdateHumanPose(); return ToWorldPoint(Pose.RootX.t); }
            set
            {
                var local = ToLocalPoint(value);
                UpdateHumanPose();
                Pose.RootX.t = local;
                PoseChanged();
            }
        }

        internal Quaternion BodyRotation
        {
            get { UpdateHumanPose(); return Q(M.QMul(Root.q, Pose.RootX.q)); }
            set
            {
                var local = M.QMul(M.QConj(Root.q), Q(value));
                UpdateHumanPose();
                Pose.RootX.q = local;
                PoseChanged();
            }
        }

        /// <summary>A muscle's value: body parts in the pose's muscles, fingers in the hands'.</summary>
        internal float GetMuscle(UnityEngine.Animations.MuscleHandle muscle)
        {
            UpdateHumanPose();
            var dofs = Muscles(muscle, out int index);
            return dofs != null && (uint)index < (uint)dofs.Length ? dofs[index] : 0f;
        }

        internal void SetMuscle(UnityEngine.Animations.MuscleHandle muscle, float value)
        {
            UpdateHumanPose();
            var dofs = Muscles(muscle, out int index);
            if (dofs != null && (uint)index < (uint)dofs.Length) dofs[index] = value;
            PoseChanged();
        }

        float[] Muscles(UnityEngine.Animations.MuscleHandle muscle, out int index)
        {
            index = muscle.Index;
            int part = (int)muscle.humanPartDof;
            if (part < (int)HumanPartDof.LeftThumb) return Pose.DoF;
            if (part <= (int)HumanPartDof.LeftLittle) return Pose.LeftHand.DoF;
            if (part <= (int)HumanPartDof.RightLittle) return Pose.RightHand.DoF;
            return null;
        }

        /// <summary>The skeleton made again from the avatar's stance (T-pose) skeleton, its root left
        /// where that skeleton has it.</summary>
        internal void ResetToStancePose()
        {
            m_Local.CopyFrom(m_Human.SkeletonPose);
            m_Changed = true;
            m_SkeletonValid = true;
            m_HumanValid = false;
        }

        internal Vector3 GetGoalPosition(int g) => ToWorldPoint(GoalLocalPosition(g));

        internal void SetGoalPosition(int g, Vector3 position)
        {
            var local = ToLocalPoint(position);
            SetGoal(g);
            m_GoalT[g] = local;
        }

        /// <summary>A goal's rotation in the world, with the goal's orientation offset.</summary>
        internal Quaternion GetGoalRotation(int g) =>
            Q(M.QMul(Root.q, M.QMul(GoalLocalRotation(g), HumanIK.GoalOrientationOffset[g])));

        internal void SetGoalRotation(int g, Quaternion rotation)
        {
            var local = M.QMul(M.QConj(Root.q), M.QMul(Q(rotation), M.QConj(HumanIK.GoalOrientationOffset[g])));
            SetGoal(g);
            m_GoalQ[g] = local;
        }

        internal Vector3 GetGoalLocalPosition(int g) => P(GoalLocalPosition(g));

        internal void SetGoalLocalPosition(int g, Vector3 position)
        {
            SetGoal(g);
            m_GoalT[g] = V(position);
        }

        internal Quaternion GetGoalLocalRotation(int g) => Q(GoalLocalRotation(g));

        internal void SetGoalLocalRotation(int g, Quaternion rotation)
        {
            SetGoal(g);
            m_GoalQ[g] = Q(rotation);
        }

        /// <summary>The goal's end bone (ankle, wrist) in the skeleton pose.</summary>
        internal Vector3 GetGoalPositionFromPose(int g)
        {
            UpdateSkeletonPose();
            Skel.ComputeGlobal(m_Human.Skeleton, m_Local, m_Global);
            return P(m_Global.X[m_Human.BoneIndex[HumanOps.GoalBoneOf(g)]].t);
        }

        internal Quaternion GetGoalRotationFromPose(int g)
        {
            UpdateSkeletonPose();
            Skel.ComputeGlobal(m_Human.Skeleton, m_Local, m_Global);
            var x = HumanOps.GetGoalXform(m_Human, m_Global, g);
            return Q(M.QMul(x.q, HumanIK.GoalOrientationOffset[g]));
        }

        internal ref HumanGoal Goal(int g) => ref Pose.Goals[g];

        /// <summary>A hint the job did not set is where the skeleton pose puts it (the elbow, the knee
        /// moved back); the first such read places every hint not set.</summary>
        internal Vector3 GetHintPosition(int g)
        {
            if (!m_HintSet[g])
            {
                UpdateSkeletonPose();
                Skel.ComputeGlobal(m_Human.Skeleton, m_Local, m_Global);
                for (int i = 0; i < GoalCount; i++)
                {
                    if (m_HintSet[i]) continue;
                    m_HintSet[i] = true;
                    Pose.Goals[i].HintT = HumanIK.HintPositionOf(m_Human, m_Global, i);
                }
            }
            return P(Pose.Goals[g].HintT);
        }

        internal void SetHintPosition(int g, Vector3 position)
        {
            m_HintSet[g] = true;
            Pose.Goals[g].HintT = V(position);
        }

        /// <summary>The full-body IK on the skeleton pose: the body and the goals (those the job set,
        /// else the pose's, the feet at the sole) in the world, the weights, hints and look-at as the
        /// job left them. The pose is made again from the solved skeleton when next read.</summary>
        internal void SolveIK()
        {
            UpdateSkeletonPose();
            var s = m_Human.Skeleton;
            Skel.ComputeGlobal(s, m_Local, m_Global);
            float hs = m_Human.Scale;
            m_World.CopyFrom(Pose);
            var body = Pose.RootX;
            body.t = body.t * hs;
            m_World.RootX = M.Mul(Root, body);
            for (int g = 0; g < GoalCount; g++)
            {
                var x = Pose.Goals[g].X;
                x.t = GoalLocalPosition(g) * hs;
                x.q = GoalLocalRotation(g);
                m_World.Goals[g].X = M.Mul(Root, x);
            }
            HumanIK.SolveFullBody(m_Human, m_World, m_Local, m_Global, m_W4);
            m_Changed = true;
            m_HumanValid = false;
        }

        static F4 V(Vector3 v) => new F4(v.x, v.y, v.z, 0f);

        static F4 Q(Quaternion q) => new F4(q.x, q.y, q.z, q.w);

        static Vector3 P(F4 v) => new Vector3(v.x, v.y, v.z);

        static Quaternion Q(F4 q) => new Quaternion(q.x, q.y, q.z, q.w);
    }

    /// <summary>The IK values of a humanoid stream's pose (goal weights, hints, look-at) a job left
    /// in it: the jobs above it read them, the Animator's own IK step does not (its retargeting
    /// clears the weights).</summary>
    internal sealed class StreamHumanIK
    {
        bool m_Has;

        internal bool Has => m_Has;

        readonly float[] m_WeightT = new float[4], m_WeightR = new float[4], m_HintWeight = new float[4];
        readonly F4[] m_Hint = new F4[4];
        F4 m_LookAt, m_LookAtWeight;

        internal void Clear() => m_Has = false;

        internal void LoadInto(HumanPose pose)
        {
            for (int g = 0; g < 4; g++)
            {
                ref var goal = ref pose.Goals[g];
                goal.WeightT = m_WeightT[g];
                goal.WeightR = m_WeightR[g];
                goal.HintT = m_Hint[g];
                goal.HintWeightT = m_HintWeight[g];
            }
            pose.LookAtPosition = m_LookAt;
            pose.LookAtWeight = m_LookAtWeight;
        }

        /// <summary>No IK values: weights 0, hints and look-at at the origin.</summary>
        internal static void Clear(HumanPose pose)
        {
            for (int g = 0; g < 4; g++)
            {
                ref var goal = ref pose.Goals[g];
                goal.WeightT = goal.WeightR = goal.HintWeightT = 0f;
                goal.HintT = F4.Zero;
            }
            pose.LookAtPosition = F4.Zero;
            pose.LookAtWeight = F4.Zero;
        }

        internal void StoreFrom(HumanPose pose)
        {
            m_Has = true;
            for (int g = 0; g < 4; g++)
            {
                var goal = pose.Goals[g];
                m_WeightT[g] = goal.WeightT;
                m_WeightR[g] = goal.WeightR;
                m_Hint[g] = goal.HintT;
                m_HintWeight[g] = goal.HintWeightT;
            }
            m_LookAt = pose.LookAtPosition;
            m_LookAtWeight = pose.LookAtWeight;
        }
    }
}

namespace UnityEngine.Animations
{
    /// <summary>A muscle of the human stream: a human part and its degree of freedom.</summary>
    public partial struct MuscleHandle
    {
        /// <summary>Each part's first muscle in its pose array (body parts in the body's muscles,
        /// fingers in their hand's).</summary>
        static readonly int[] s_PartOffsets = { 0, 9, 21, 29, 37, 46, 0, 4, 8, 12, 16, 0, 4, 8, 12, 16 };

        public HumanPartDof humanPartDof { get; private set; }

        public int dof { get; private set; }

        public MuscleHandle(BodyDof bodyDof)
        {
            humanPartDof = HumanPartDof.Body;
            dof = (int)bodyDof;
        }

        public MuscleHandle(HeadDof headDof)
        {
            humanPartDof = HumanPartDof.Head;
            dof = (int)headDof;
        }

        public MuscleHandle(HumanPartDof partDof, LegDof legDof)
        {
            if (partDof != HumanPartDof.LeftLeg && partDof != HumanPartDof.RightLeg)
                throw new InvalidOperationException("Invalid HumanPartDof for a leg, please use either HumanPartDof.LeftLeg or HumanPartDof.RightLeg.");
            humanPartDof = partDof;
            dof = (int)legDof;
        }

        public MuscleHandle(HumanPartDof partDof, ArmDof armDof)
        {
            if (partDof != HumanPartDof.LeftArm && partDof != HumanPartDof.RightArm)
                throw new InvalidOperationException("Invalid HumanPartDof for an arm, please use either HumanPartDof.LeftArm or HumanPartDof.RightArm.");
            humanPartDof = partDof;
            dof = (int)armDof;
        }

        public MuscleHandle(HumanPartDof partDof, FingerDof fingerDof)
        {
            if (partDof < HumanPartDof.LeftThumb || partDof > HumanPartDof.RightLittle)
                throw new InvalidOperationException("Invalid HumanPartDof for a finger.");
            humanPartDof = partDof;
            dof = (int)fingerDof;
        }

        /// <summary>The muscle's index in its part's pose array.</summary>
        internal int Index => dof + ((uint)humanPartDof < (uint)s_PartOffsets.Length ? s_PartOffsets[(int)humanPartDof] : 0);

        /// <summary>The muscle's humanoid curve (its name), -1 for a part outside HumanPartDof.</summary>
        int Curve
        {
            get
            {
                int part = (int)humanPartDof;
                if (part < 0 || part > (int)HumanPartDof.RightLittle) return -1;
                int first = part < (int)HumanPartDof.LeftThumb ? HumanClipImport.MuscleFirst
                    : part <= (int)HumanPartDof.LeftLittle ? HumanClipImport.LeftHandFirst : HumanClipImport.RightHandFirst;
                return first + Index;
            }
        }

        public string name
        {
            get
            {
                int curve = Curve;
                if (curve < HumanClipImport.MuscleFirst || curve >= HumanClipImport.TDoFFirst)
                    throw new IndexOutOfRangeException("The muscle handle does not name a muscle.");
                return HumanClipImport.CurveNames[curve];
            }
        }

        const int Count = 95;

        public static int muscleHandleCount => Count;

        /// <summary>Every muscle, part by part; an array shorter than <see cref="muscleHandleCount"/>
        /// is left as it is.</summary>
        public static void GetMuscleHandles(MuscleHandle[] muscleHandles)
        {
            if (muscleHandles == null) throw new ArgumentNullException(nameof(muscleHandles));
            if (muscleHandles.Length < Count) return;
            int i = 0;
            for (int part = 0; part < s_PartCounts.Length; part++)
                for (int d = 0; d < s_PartCounts[part]; d++)
                    muscleHandles[i++] = new MuscleHandle { humanPartDof = (HumanPartDof)part, dof = d };
        }

        /// <summary>Each part's number of muscles.</summary>
        static readonly int[] s_PartCounts = { 9, 12, 8, 8, 9, 9, 4, 4, 4, 4, 4, 4, 4, 4, 4, 4 };
    }

    /// <summary>The human pose of a humanoid Animator's animation stream, for animation jobs
    /// (<see cref="AnimationStream.AsHuman"/>); see <see cref="HumanStreamPose"/>.</summary>
    public partial struct AnimationHumanStream
    {
        internal int m_Context;

        public bool isValid => m_Context != 0;

        HumanStreamPose S
        {
            get
            {
                if (!isValid) throw new InvalidOperationException("The AnimationHumanStream is invalid.");
                return StreamContext.Get(m_Context)?.HumanPose() ?? throw new InvalidOperationException("The AnimationHumanStream is invalid.");
            }
        }

        static int Goal(AvatarIKGoal goal)
        {
            if ((uint)goal > (uint)AvatarIKGoal.RightHand) throw new ArgumentOutOfRangeException(nameof(goal));
            return (int)goal;
        }

        static int Hint(AvatarIKHint hint)
        {
            if ((uint)hint > (uint)AvatarIKHint.RightElbow) throw new ArgumentOutOfRangeException(nameof(hint));
            return (int)hint;
        }

        public float humanScale => S.HumanScale;

        public float leftFootHeight => S.FootHeight(true);

        public float rightFootHeight => S.FootHeight(false);

        public Vector3 bodyLocalPosition { get => S.BodyLocalPosition; set => S.BodyLocalPosition = value; }

        public Quaternion bodyLocalRotation { get => S.BodyLocalRotation; set => S.BodyLocalRotation = value; }

        public Vector3 bodyPosition { get => S.BodyPosition; set => S.BodyPosition = value; }

        public Quaternion bodyRotation { get => S.BodyRotation; set => S.BodyRotation = value; }

        public Vector3 leftFootVelocity => StreamContext.Get(m_Context)?.FootVelocity(true) ?? throw new InvalidOperationException("The AnimationHumanStream is invalid.");

        public Vector3 rightFootVelocity => StreamContext.Get(m_Context)?.FootVelocity(false) ?? throw new InvalidOperationException("The AnimationHumanStream is invalid.");

        public float GetMuscle(MuscleHandle muscle) => S.GetMuscle(muscle);

        public void SetMuscle(MuscleHandle muscle, float value) => S.SetMuscle(muscle, value);

        public void ResetToStancePose() => S.ResetToStancePose();

        public Vector3 GetGoalPositionFromPose(AvatarIKGoal index) => S.GetGoalPositionFromPose(Goal(index));

        public Quaternion GetGoalRotationFromPose(AvatarIKGoal index) => S.GetGoalRotationFromPose(Goal(index));

        public Vector3 GetGoalLocalPosition(AvatarIKGoal index) => S.GetGoalLocalPosition(Goal(index));

        public void SetGoalLocalPosition(AvatarIKGoal index, Vector3 pos) => S.SetGoalLocalPosition(Goal(index), pos);

        public Quaternion GetGoalLocalRotation(AvatarIKGoal index) => S.GetGoalLocalRotation(Goal(index));

        public void SetGoalLocalRotation(AvatarIKGoal index, Quaternion rot) => S.SetGoalLocalRotation(Goal(index), rot);

        public Vector3 GetGoalPosition(AvatarIKGoal index) => S.GetGoalPosition(Goal(index));

        public void SetGoalPosition(AvatarIKGoal index, Vector3 pos) => S.SetGoalPosition(Goal(index), pos);

        public Quaternion GetGoalRotation(AvatarIKGoal index) => S.GetGoalRotation(Goal(index));

        public void SetGoalRotation(AvatarIKGoal index, Quaternion rot) => S.SetGoalRotation(Goal(index), rot);

        public void SetGoalWeightPosition(AvatarIKGoal index, float value) => S.Goal(Goal(index)).WeightT = value;

        public void SetGoalWeightRotation(AvatarIKGoal index, float value) => S.Goal(Goal(index)).WeightR = value;

        public float GetGoalWeightPosition(AvatarIKGoal index) => S.Goal(Goal(index)).WeightT;

        public float GetGoalWeightRotation(AvatarIKGoal index) => S.Goal(Goal(index)).WeightR;

        public Vector3 GetHintPosition(AvatarIKHint index) => S.GetHintPosition(Hint(index));

        public void SetHintPosition(AvatarIKHint index, Vector3 pos) => S.SetHintPosition(Hint(index), pos);

        public void SetHintWeightPosition(AvatarIKHint index, float value) => S.Goal(Hint(index)).HintWeightT = value;

        public float GetHintWeightPosition(AvatarIKHint index) => S.Goal(Hint(index)).HintWeightT;

        public void SetLookAtPosition(Vector3 lookAtPosition) =>
            S.Pose.LookAtPosition = new F4(lookAtPosition.x, lookAtPosition.y, lookAtPosition.z, 0f);

        public void SetLookAtClampWeight(float weight) => S.Pose.LookAtWeight.x = weight;

        public void SetLookAtBodyWeight(float weight) => S.Pose.LookAtWeight.y = weight;

        public void SetLookAtHeadWeight(float weight) => S.Pose.LookAtWeight.z = weight;

        public void SetLookAtEyesWeight(float weight) => S.Pose.LookAtWeight.w = weight;

        public void SolveIK() => S.SolveIK();
    }

    public partial struct AnimationStream
    {
        public AnimationHumanStream AsHuman()
        {
            CheckIsValid();
            if (!isHumanStream) throw new InvalidOperationException("Cannot create an AnimationHumanStream for a generic rig.");
            return new AnimationHumanStream { m_Context = m_Context };
        }
    }
}
