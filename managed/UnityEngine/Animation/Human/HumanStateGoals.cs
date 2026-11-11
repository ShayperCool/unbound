namespace Unbound.Runtime.Humanoid
{
    /// <summary>
    /// The body and the IK goals of one state's humanoid clips while an Animator Controller
    /// samples them into a layer's pose. A clip gives both whole: its body (RootT, RootQ) as its
    /// root motion sample reads it, the identity's components for the rotation curves it lacks
    /// (<see cref="HumanRootMotion.ReadRoot"/>), so that bodies blend as positions and rotations
    /// and not curve by curve (matches the Unity 6000.0.71f1 Editor, tests/projects/humanoid
    /// body.*: a clip whose RootQ keys are negated blends as the same rotation, body.negTree and
    /// body.posTree; clips with RootQ.x and y only or RootQ.w only, body.partialTree, body.wTree,
    /// body.partialOverride; a clip without body curves has the identity body, body.noneTree).
    /// A clip's goal curves are relative to its body; a controller blends goals in
    /// the motion's frame, each clip's goals placed by that clip's own body (relative to its
    /// motion) before any weight applies, so the states of a transition, a blend tree's clips and
    /// override layers all blend placed goals (goals.*, footIKTransition). A goal the clip has no
    /// curve for is the identity, so it is placed at the clip's body.
    /// A state's clips (a blend tree's) blend among themselves first and the layer then blends its
    /// states: a goal's rotation is the normalized sum of the state's clips, each in the
    /// hemisphere of the ones before, before a transition blends it with another state's
    /// (goals.treeTransition, goals.treeSource). Where the humanoid clips do not fill the state's
    /// weight (a Direct blend tree whose weights do not sum to one, a tree with clips without
    /// humanoid curves), the state blends as an AnimationMixerPlayable's inputs do: below one,
    /// what the clips leave is the identity goal, its rotation (0, 0, 0, 1) as it is whatever
    /// hemisphere the clips' sum is in; above one, a goal's position is the clips' plain sum and
    /// its rotation their normalized sum (body.directLowFootIK, body.directHighFootIK, weights of
    /// 0.3 + 0.4 and 0.8 + 0.6; body.directEmptyChild).
    /// The sampler hands a clip's goal curves over instead of adding them (<see cref="Take"/>)
    /// and leaves its body curves out; <see cref="AddClip"/> collects the body and places and
    /// collects the goals, <see cref="EndState"/> adds the state's body and goals to the layer's
    /// pose, every curve of them with the state's weight, so that layers combine their rotations
    /// as rotations.
    /// </summary>
    internal sealed class HumanStateGoals
    {
        const int First = HumanClipImport.GoalFirst, Curves = HumanClipImport.MuscleFirst - HumanClipImport.GoalFirst;
        /// <summary>Curves per goal: a position, then a rotation.</summary>
        internal const int Width = 7;
        const int Goals = Curves / Width;

        readonly float[] m_Values = new float[Curves];
        readonly AnimationBindings m_Bindings;
        readonly F4[] m_Positions = new F4[Goals], m_Rotations = new F4[Goals];
        F4 m_BodyPosition, m_BodyRotation;
        float m_Weight;
        int m_Clips;
        Trs m_Body, m_Root;

        internal HumanStateGoals(AnimationBindings bindings) => m_Bindings = bindings;

        /// <summary>The goal curve a humanoid curve is, counted from the first goal curve (−1: none).</summary>
        internal static int Slot(int humanCurve) => humanCurve >= First && humanCurve < First + Curves ? humanCurve - First : -1;

        /// <summary>Per curve of a clip bound to these bindings, its goal curve (null: none).</summary>
        internal int[] Slots(BoundClip bound) => bound.GoalSlots(m_Bindings);

        /// <summary>Starts a state: no clip yet.</summary>
        internal void BeginState()
        {
            m_Weight = 0f;
            m_Clips = 0;
            for (int g = 0; g < Goals; g++) m_Positions[g] = m_Rotations[g] = F4.Zero;
            m_BodyPosition = m_BodyRotation = F4.Zero;
        }

        /// <summary>Starts a clip whose body is <paramref name="root"/> in the clip's space and
        /// <paramref name="body"/> relative to its motion: every goal the identity.</summary>
        internal void BeginClip(in Trs body, in Trs root)
        {
            m_Body = body;
            m_Root = root;
            for (int i = 0; i < Curves; i++) m_Values[i] = i % Width == Width - 1 ? 1f : 0f;
        }

        /// <summary>A goal curve's sampled value (<paramref name="slot"/> from <see cref="Slot"/>).</summary>
        internal void Take(int slot, float value) => m_Values[slot] = value;

        /// <summary>The clip's body and its goals, placed by its body, join the state's with the
        /// clip's weight; a rotation in the hemisphere of what the state holds already.</summary>
        internal void AddClip(float weight)
        {
            m_Weight += weight;
            m_Clips++;
            m_BodyPosition = m_BodyPosition + m_Root.t * weight;
            m_BodyRotation = m_BodyRotation + Aligned(m_BodyRotation, m_Root.q) * weight;
            for (int g = 0; g < Goals; g++)
            {
                int s = g * Width;
                var goal = new Trs(new F4(m_Values[s], m_Values[s + 1], m_Values[s + 2], 0f),
                    M.QNormalize(new F4(m_Values[s + 3], m_Values[s + 4], m_Values[s + 5], m_Values[s + 6])), F4.One3);
                var placed = M.Mul(m_Body, goal);
                m_Positions[g] = m_Positions[g] + placed.t * weight;
                m_Rotations[g] = m_Rotations[g] + Aligned(m_Rotations[g], placed.q) * weight;
            }
        }

        /// <summary>Adds the state's body and goals to a layer's pose with the state's weight, on
        /// their channels that are bound and that <paramref name="mask"/> lets through; a
        /// rotation in the hemisphere of what its channels hold already. Nothing for a state that
        /// sampled no humanoid clip, unless its motion has one (<paramref name="humanoid"/>: a
        /// Direct blend tree whose humanoid clips are all at weight 0), which gives the identity
        /// body and goals.</summary>
        internal void EndState(AnimationPose pose, bool[] mask, float weight, bool humanoid)
        {
            if (!(m_Weight > 0f) && !humanoid || !(weight > 0f)) return;
            // What the clips leave of the state's weight is the identity.
            float rest = weight - m_Weight;
            AddPart(pose, mask, weight, rest, HumanClipImport.RootFirst, m_BodyPosition, m_BodyRotation);
            for (int g = 0; g < Goals; g++) AddPart(pose, mask, weight, rest, First + g * Width, m_Positions[g], m_Rotations[g]);
        }

        /// <summary>One part (the body or a goal: a position's three curves from
        /// <paramref name="curve"/>, then a rotation's four) of the state.</summary>
        void AddPart(AnimationPose pose, bool[] mask, float weight, float rest, int curve, F4 t, F4 q)
        {
            var b = m_Bindings;
            Add(pose, mask, b.HumanChannel(curve), t.x, weight);
            Add(pose, mask, b.HumanChannel(curve + 1), t.y, weight);
            Add(pose, mask, b.HumanChannel(curve + 2), t.z, weight);
            // One clip that fills the state: its unit rotation times its weight, as it is.
            if (m_Clips != 1 || rest > 0f)
            {
                // The identity as it is, whatever hemisphere the clips' sum is in.
                if (rest > 0f) q.w += rest;
                q = M.QNormalize(q) * weight;
            }
            int cx = b.HumanChannel(curve + 3), cy = b.HumanChannel(curve + 4), cz = b.HumanChannel(curve + 5), cw = b.HumanChannel(curve + 6);
            q = Aligned(new F4(Held(pose, cx), Held(pose, cy), Held(pose, cz), Held(pose, cw)), q);
            Add(pose, mask, cx, q.x, weight);
            Add(pose, mask, cy, q.y, weight);
            Add(pose, mask, cz, q.z, weight);
            Add(pose, mask, cw, q.w, weight);
        }

        static F4 Aligned(F4 held, F4 q) => held.x * q.x + held.y * q.y + held.z * q.z + held.w * q.w < 0f ? -q : q;

        static float Held(AnimationPose pose, int channel) => channel >= 0 && channel < pose.Value.Length ? pose.Value[channel] : 0f;

        /// <summary>Adds an already weighted value and its weight to a channel.</summary>
        static void Add(AnimationPose pose, bool[] mask, int channel, float weighted, float weight)
        {
            if (channel < 0 || channel >= pose.Value.Length || (mask != null && !mask[channel])) return;
            pose.Value[channel] += weighted;
            pose.Weight[channel] += weight;
        }
    }
}
