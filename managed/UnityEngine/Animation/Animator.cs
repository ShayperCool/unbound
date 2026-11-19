using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>
    /// Plays an animator controller on its GameObject's hierarchy. Evaluated in
    /// PreLateUpdate.DirectorUpdateAnimationBegin (or with physics for AnimatorUpdateMode.Fixed).
    /// Generic animation and root motion. Humanoid avatars and IK are not applied.
    /// </summary>
    [ClassId(95)]
    public partial class Animator : Behaviour
    {
        internal RuntimeAnimatorController m_Controller;
        internal AnimatorCullingMode m_CullingMode;
        internal AnimatorUpdateMode m_UpdateMode;
        internal bool m_ApplyRootMotion;
        internal bool m_AnimatePhysics;
        internal bool m_KeepAnimatorStateOnDisable;
        internal bool m_WriteDefaultValuesOnDisable;
        internal float m_Speed = 1f;
        internal bool m_FireEvents = true;
        internal bool m_LogWarnings = true;
        internal AnimatorPlayer m_Player;
        bool m_Initialized;
        bool m_Registered;
        /// <summary>Bindings of an Animator animated by playable outputs alone (no controller).</summary>
        AnimationBindings m_OutputBindings;
        internal Unbound.Runtime.Playables.AnimatorHandles m_StreamHandles;
        Unbound.Runtime.Playables.PlayableGraphData m_ControllerGraph;

        /// <summary>The channels playable outputs and animation jobs animate on this Animator: its
        /// controller's when it has one (so they share default values), its own otherwise.</summary>
        internal AnimationBindings StreamBindings
        {
            get
            {
                if (m_GameObject == null) return null;
                var player = EnsurePlayer();
                if (player != null) return player.Bindings;
                return m_OutputBindings ??= new AnimationBindings(m_GameObject) { Owner = this };
            }
        }

        /// <summary>The graph that plays the Animator's controller: an AnimatorControllerPlayable
        /// on an animation output. The Animator updates it itself (AnimationSystem).</summary>
        public UnityEngine.Playables.PlayableGraph playableGraph
        {
            get
            { MainThreadCheck.Check("GetCurrentGraph");
                var player = EnsurePlayer();
                if (player == null) return default;
                if (m_ControllerGraph == null || m_ControllerGraph.Destroyed || m_ControllerGraph.ControllerPlayer != player)
                    m_ControllerGraph = Unbound.Runtime.Playables.PlayableGraphData.CreateForAnimator(this, player);
                return m_ControllerGraph.ToGraph();
            }
        }

        public Animator() { }

        // ------------------------------------------------------------------ hashing

        static readonly uint[] s_Crc = BuildCrcTable();

        static uint[] BuildCrcTable()
        {
            var table = new uint[256];
            for (uint i = 0; i < 256; i++)
            {
                uint c = i;
                for (int k = 0; k < 8; k++) c = (c & 1) != 0 ? 0xEDB88320u ^ (c >> 1) : c >> 1;
                table[i] = c;
            }
            return table;
        }

        /// <summary>Unity's name hash: the CRC-32 of the UTF-8 string.</summary>
        public static int StringToHash(string name)
        {
            if (string.IsNullOrEmpty(name)) return 0;
            uint crc = 0xFFFFFFFFu;
            foreach (var b in System.Text.Encoding.UTF8.GetBytes(name))
                crc = s_Crc[(crc ^ b) & 0xFF] ^ (crc >> 8);
            return unchecked((int)(crc ^ 0xFFFFFFFFu));
        }

        // ------------------------------------------------------------------ settings

        /// <summary>Whether the avatar has a root motion transform, whatever the controller or
        /// <see cref="applyRootMotion"/> (see <see cref="Avatar.HasRootMotion"/>).</summary>
        public bool hasRootMotion
        { get { MainThreadCheck.Check("get_hasRootMotion"); return m_Avatar != null && m_Avatar.HasRootMotion; } }

        public bool isInitialized { get { MainThreadCheck.Check("get_isInitialized"); return Compat.MainThreadContextsMeasured ? m_Initialized : m_Player != null; } }

        public bool applyRootMotion
        {
            get { MainThreadCheck.Check("get_applyRootMotion"); return m_ApplyRootMotion; }
            set { MainThreadCheck.Check("set_applyRootMotion"); m_ApplyRootMotion = value; }
        }

        /// <summary>Whether the pose this Animator writes moves the kinematic bodies on the written
        /// transforms (and below them) as kinematic moves, which carry other bodies along, instead
        /// of teleporting them; independent of <see cref="updateMode"/> (matches the Unity
        /// 6000.0.71f1 Editor, tests/projects/animatephysics).</summary>
        public bool animatePhysics
        {
            get { MainThreadCheck.Check("get_animatePhysics"); return m_AnimatePhysics; }
            set { MainThreadCheck.Check("set_animatePhysics"); m_AnimatePhysics = value; }
        }

        public AnimatorUpdateMode updateMode
        {
            get { MainThreadCheck.Check("get_updateMode"); return m_UpdateMode; }
            set { MainThreadCheck.Check("set_updateMode"); m_UpdateMode = value; }
        }

        public bool hasTransformHierarchy
        { get { MainThreadCheck.Check("get_hasTransformHierarchy"); return !m_NoHierarchy; } }

        /// <summary>The Animator animates its avatar's skeleton instead of a transform hierarchy
        /// (Optimize Game Objects, AnimatorUtility.OptimizeTransformHierarchy).</summary>
        internal bool m_NoHierarchy;
        Unbound.Runtime.AnimatorSkeleton m_Skeleton;
        /// <summary>A model's Optimize Game Objects: the extra exposed paths the loader optimizes
        /// the loaded hierarchy with (<see cref="Unbound.Runtime.HierarchyOptimizer"/>).</summary>
        internal string[] m_OptimizeOnLoad;

        /// <summary>The skeleton an Animator without its transform hierarchy animates (made on first
        /// use, after a copy's hierarchy exists); null with a hierarchy or without a valid avatar.</summary>
        internal Unbound.Runtime.AnimatorSkeleton Skeleton
        {
            get
            {
                if (!m_NoHierarchy) return null;
                var avatar = m_Avatar != null ? m_Avatar.Skeleton : null;
                if (avatar == null) return null;
                if (m_Skeleton == null || m_Skeleton.Avatar != avatar)
                {
                    m_Skeleton = new Unbound.Runtime.AnimatorSkeleton(this, avatar);
                    m_Skeleton.BindExposed();
                }
                return m_Skeleton;
            }
        }

        /// <summary>The bindings this Animator's own updates and its playable outputs write.</summary>
        IEnumerable<Unbound.Runtime.AnimationBindings> OwnBindings()
        {
            if (m_Player != null) yield return m_Player.Bindings;
            if (m_OutputBindings != null) yield return m_OutputBindings;
        }

        /// <summary>The bound transform properties below the root that are skeleton nodes, while the
        /// hierarchy is whole (their path names the node).</summary>
        internal List<(Unbound.Runtime.AnimationBindings Bindings, Unbound.Runtime.PropertyGroup Group, int Node, Unbound.Runtime.TransformProperty Property)>
            SkeletonBindings(Unbound.Runtime.Humanoid.AvatarConstant avatar)
        {
            var moves = new List<(Unbound.Runtime.AnimationBindings, Unbound.Runtime.PropertyGroup, int, Unbound.Runtime.TransformProperty)>();
            var root = transform;
            foreach (var bindings in OwnBindings())
                foreach (var ((target, key), group) in bindings.Index)
                {
                    if (!(target is Transform t) || t == root || !Unbound.Runtime.PropertyBinder.TransformPropertyOf(key, out var property)) continue;
                    int node = avatar.NodeOfPath(Unbound.Runtime.HashedBindings.Crc32(Unbound.Runtime.HierarchyOptimizer.PathBelow(t, root)));
                    if (node > 0) moves.Add((bindings, group, node, property));
                }
            return moves;
        }

        /// <summary>The hierarchy was optimized: the Animator animates <paramref name="skeleton"/>,
        /// and the bound transform properties of its nodes move to it (channels and state kept).</summary>
        internal void SetSkeleton(Unbound.Runtime.AnimatorSkeleton skeleton,
            List<(Unbound.Runtime.AnimationBindings Bindings, Unbound.Runtime.PropertyGroup Group, int Node, Unbound.Runtime.TransformProperty Property)> moves)
        {
            m_NoHierarchy = true;
            m_Skeleton = skeleton;
            skeleton.BindExposed();
            m_Human?.Rebind();
            if (moves != null)
                foreach (var (bindings, group, node, property) in moves)
                    bindings.Replace(group, (this, Unbound.Runtime.PropertyBinder.SkeletonKey(node, property)),
                        new Unbound.Runtime.SkeletonGroup(skeleton, node, property));
            m_StreamHandles?.HierarchyChanged();
        }

        /// <summary>How often the Editor logs that an optimized humanoid has no avatar (see
        /// <see cref="AvatarLostToStreamHandles"/>) in one evaluation of a manual graph and in
        /// one Animator.Update (6000.0.71f1, tests/projects/animoptimize graphJobHand.HumanOpt
        /// and updateHand.HumanOpt), and in one update of the player loop (unmeasured: once
        /// here).</summary>
        internal const int AvatarLostLogsPerEvaluation = 2, AvatarLostLogsPerUpdateCall = 3, AvatarLostLogsPerFrame = 1;

        /// <summary>Whether the Animator's stream handles leave it without an avatar; logs the
        /// Editor's messages <paramref name="logs"/> times. Measured (6000.0.71f1,
        /// tests/projects/animoptimize): an optimized humanoid with a stream handle of a path
        /// its avatar's skeleton does not hold, the exposed hand bound under its own path
        /// (graphJobHand.HumanOpt, updateHand.HumanOpt) or an object added below the Animator
        /// (graphJobAdded.HumanOpt), plays nothing: each evaluation of a manual graph and each
        /// Animator.Update logs the error "Transform 'Hips' for human bone 'Hips' not found"
        /// and the warning "Object is set to Optimize Game Object but does not have an Avatar,
        /// Animation will not play.", no clip, controller or job runs and the exposed
        /// transforms stay as they are. With a handle of the Animator's own transform, or none,
        /// it plays (graphJobRoot.HumanOpt, graphJob.HumanOpt, update.HumanOpt). The Scripting
        /// API does not describe it. Unbound's reading of it, not measured beyond those
        /// controls: the error is the one AvatarBuilder.BuildHumanAvatar gives for the
        /// avatar's human description on the Animator's hierarchy as it is, so a humanoid
        /// whose hierarchy still holds every mapped bone goes on playing, and the hierarchy is
        /// looked through in every evaluation while such a handle is bound. A generic rig goes
        /// on as before; there the Editor logs "The input bones do not match the skeleton of
        /// the Avatar" and its skin differs between two runs (an Editor capture outside the
        /// reference).</summary>
        internal bool AvatarLostToStreamHandles(int logs)
        {
            if (!m_NoHierarchy || m_StreamHandles == null || !IsHuman || !m_StreamHandles.AnyOffSkeleton()) return false;
            string error = Unbound.Runtime.Humanoid.AvatarBuild.HierarchyError(m_GameObject, m_Avatar.m_Description);
            if (error == null)
            {
                Stubs.Ignored("stream handle off the skeleton of an optimized humanoid that still holds every mapped bone's transform",
                    "plays as without the handle (not measured)");
                return false;
            }
            for (int i = 0; i < logs; i++)
            {
                Debug.LogError(error);
                Debug.LogWarning("Object is set to Optimize Game Object but does not have an Avatar, Animation will not play.");
            }
            return true;
        }

        /// <summary>The hierarchy was deoptimized: the skeleton's bound properties move back to the
        /// transforms of their paths.</summary>
        internal void ClearSkeleton()
        {
            var root = transform;
            foreach (var bindings in OwnBindings())
            {
                var entries = new List<((Object, string) Key, Unbound.Runtime.PropertyGroup Group)>();
                foreach (var entry in bindings.Index)
                    if (entry.Value is Unbound.Runtime.SkeletonGroup) entries.Add((entry.Key, entry.Value));
                foreach (var (_, group) in entries)
                {
                    var g = (Unbound.Runtime.SkeletonGroup)group;
                    var t = root.Find(g.SkeletonPath);
                    if (t == null)
                    {
                        g.Target = null;
                        continue;
                    }
                    bindings.Replace(g, (t, Unbound.Runtime.PropertyBinder.TransformKey(g.Property)), Unbound.Runtime.PropertyBinder.TransformGroup(t, g.Property));
                }
            }
            m_NoHierarchy = false;
            m_Skeleton = null;
            m_Human?.Rebind();
            m_StreamHandles?.HierarchyChanged();
        }

        public float speed
        {
            get { MainThreadCheck.Check("get_speed"); return m_Speed; }
            set { MainThreadCheck.Check("set_speed"); m_Speed = value; }
        }

        /// <summary>Applied by the player loop's update (<see cref="Tick"/>) while no renderer of the
        /// hierarchy is visible: CullUpdateTransforms does not write the pose or run IK,
        /// CullCompletely skips the update.</summary>
        public AnimatorCullingMode cullingMode
        {
            get { MainThreadCheck.Check("get_cullingMode"); return m_CullingMode; }
            set { MainThreadCheck.Check("set_cullingMode"); m_CullingMode = value; }
        }

        public RuntimeAnimatorController runtimeAnimatorController
        {
            get { MainThreadCheck.Check("get_runtimeAnimatorController"); return m_Controller; }
            set
            { MainThreadCheck.Check("set_runtimeAnimatorController");
                if (m_Controller == value) return;
                // An override controller with nothing to override is refused; the Animator keeps
                // its controller (Unity 6000.0.71f1, tests/projects/gameinterop animator.runtime_invalid.*).
                if (value is AnimatorOverrideController empty && empty.runtimeAnimatorController == null)
                {
                    Debug.LogError(AnimatorErrors.EmptyOverrideAssigned(empty.name), this);
                    return;
                }
                ResetBindings();
                m_Controller = value;
                m_Player = null;
                if (isActiveAndEnabled) EnsurePlayer();
            }
        }

        public bool logWarnings
        {
            get { MainThreadCheck.Check("get_logWarnings"); return m_LogWarnings; }
            set { MainThreadCheck.Check("set_logWarnings"); m_LogWarnings = value; }
        }

        public bool fireEvents
        {
            get { MainThreadCheck.Check("get_fireEvents"); return m_FireEvents; }
            set { MainThreadCheck.Check("set_fireEvents"); m_FireEvents = value; }
        }

        [global::System.@ObsoleteAttribute("keepAnimatorControllerStateOnDisable is deprecated, use keepAnimatorStateOnDisable instead. (UnityUpgradable) -> keepAnimatorStateOnDisable", false)]
        public bool keepAnimatorControllerStateOnDisable
        {
            get { MainThreadCheck.Check("get_keepAnimatorStateOnDisable"); return m_KeepAnimatorStateOnDisable; }
            set { MainThreadCheck.Check("set_keepAnimatorStateOnDisable"); m_KeepAnimatorStateOnDisable = value; }
        }

        public bool keepAnimatorStateOnDisable
        {
            get { MainThreadCheck.Check("get_keepAnimatorStateOnDisable"); return m_KeepAnimatorStateOnDisable; }
            set { MainThreadCheck.Check("set_keepAnimatorStateOnDisable"); m_KeepAnimatorStateOnDisable = value; }
        }

        public bool writeDefaultValuesOnDisable
        {
            get { MainThreadCheck.Check("get_writeDefaultValuesOnDisable"); return m_WriteDefaultValuesOnDisable; }
            set { MainThreadCheck.Check("set_writeDefaultValuesOnDisable"); m_WriteDefaultValuesOnDisable = value; }
        }

        public Transform avatarRoot { get { MainThreadCheck.Check("get_avatarRoot"); return m_GameObject != null ? m_GameObject.m_Transform : null; } }

        /// <summary>The root motion of the last update, in world space (see AnimatorPlayer.ApplyRootMotion).</summary>
        internal Vector3 m_DeltaPosition;
        internal Quaternion m_DeltaRotation = Quaternion.identity;

        public Vector3 deltaPosition { get { MainThreadCheck.Check("get_deltaPosition"); return m_DeltaPosition; } }

        public Quaternion deltaRotation { get { MainThreadCheck.Check("get_deltaRotation"); return m_DeltaRotation; } }


        /// <summary>How much the feet pivot follows the left foot (humanoids; 0.5 otherwise).</summary>
        public float pivotWeight
        { get { MainThreadCheck.Check("get_pivotWeight"); return m_Human != null ? m_Human.PivotWeight : 0.5f; } }

        /// <summary>The feet pivot point in world space (humanoids; zero otherwise).</summary>
        public Vector3 pivotPosition
        {
            get
            { MainThreadCheck.Check("get_pivotPosition");
                if (m_Human == null) return Vector3.zero;
                var p = m_Human.PivotPosition;
                return new Vector3(p.x, p.y, p.z);
            }
        }

        // ------------------------------------------------------------------ player

        AnimatorPlayer EnsurePlayer()
        {
            if (m_Player != null)
            {
                // An edit of the controller (the Editor's object model) compiled it again: the
                // Animator starts over as with a newly assigned controller (Unity 6000.0.71f1,
                // tests/projects/animatorapi play.*).
                if (m_Controller == null || ReferenceEquals(m_Player.Graph, m_Controller.Graph)) return m_Player;
                ResetBindings();
                m_Player = null;
                var restarted = EnsurePlayer();
                if (restarted != null) restarted.Restarted = true;
                return restarted;
            }
            if (m_Controller == null || m_GameObject == null) return null;
            // An inactive hierarchy whose controller was cleared has no current state.
            // A disabled component on an active hierarchy retains its controller (animstartup).
            if (Compat.AnimatorStartupMeasured && !m_GameObject.activeInHierarchy) return null;
            var graph = m_Controller.Graph;
            if (graph == null) return null;
            m_Player = new AnimatorPlayer(this, m_Controller, graph);
            m_Player.InstantiateBehaviours();
            return m_Player;
        }

        /// <summary>What a member needs the playing controller for: a parameter's value, the
        /// controller's parameter definitions, a layer (its count, name or state), or playing a
        /// state.</summary>
        enum Use { Parameter, Definition, State, Play }

        /// <summary>The playing controller, or null after the warning the member gives without one
        /// (Unity 6000.0.71f1, tests/projects/apierrors `animator.bare_*` and gameinterop
        /// `animator.*`): without a controller every member warns that none is playing, and playing
        /// a state that there is none; an invalid controller (an override controller with nothing
        /// to override) leaves the Animator uninitialized, which only the parameters report; on an
        /// inactive object the parameter definitions are still read (the layers are not), and
        /// playing a state names the inactive object.</summary>
        AnimatorPlayer PlayerOrWarn(Use use)
        {
            var player = EnsurePlayer();
            if (player != null || !m_LogWarnings) return player;
            string warning;
            if (m_Controller == null) warning = use == Use.Play ? AnimatorErrors.NoController : AnimatorErrors.NotPlaying;
            else if (m_Controller.Graph == null) warning = use == Use.Parameter ? AnimatorErrors.NotInitialized : null;
            else if (m_GameObject != null && !m_GameObject.activeInHierarchy)
                warning = use == Use.Play ? AnimatorErrors.Inactive : use == Use.Definition ? null : AnimatorErrors.NotPlaying;
            else warning = AnimatorErrors.NotPlaying;
            if (warning != null) Debug.LogWarning(warning, this);
            return null;
        }

        /// <summary>The controller's definition while nothing plays it: that of a valid controller
        /// on an inactive object (gameinterop `animator.Inactive.parameter_count`).</summary>
        ControllerGraph Definition(out AnimatorPlayer player)
        {
            player = PlayerOrWarn(Use.Definition);
            return player?.Graph ?? (m_Controller != null && m_GameObject != null && !m_GameObject.activeInHierarchy ? m_Controller.Graph : null);
        }

        // Initialization follows hierarchy activation, including disabled Animators
        // (Unity 6000.0.71f1, tests/projects/threadapis); an invalid controller is not
        // initialized and is reported once (tests/projects/gameinterop animator.OverrideEmpty).
        internal override void OnEngineHierarchyActivated()
        {
            m_Initialized = m_Controller == null || m_Controller.Graph != null;
            if (!m_Initialized && m_LogWarnings)
                Debug.LogWarning($"The Animator Controller ({m_Controller.m_Name}) you have used is not valid. Animations will not play", this);
        }

        internal override void OnEngineEnable()
        {
            // A humanoid's initial API root pose predates game Transform edits, and stays
            // there until evaluation (6000.0.71f1, animculling humanMotion/initial).
            if (isHuman) EnsureRootPose();
            if (!m_Registered)
            {
                m_Registered = true;
                AnimationSystem.Add(this);
            }
            EnsurePlayer();
        }

        internal override void OnEngineDisable()
        {
            if (!m_GameObject.activeInHierarchy) m_Initialized = false;
            Unbound.Runtime.Playables.AnimationOutputs.ForgetCulled(this);
            if (m_Registered)
            {
                m_Registered = false;
                AnimationSystem.Remove(this);
            }
            if (m_Player != null)
            {
                if (m_WriteDefaultValuesOnDisable) m_Player.Bindings.WriteDefaults();
                // 6000.0.71f1 (animstartup): enabled=false only suspends evaluation. The
                // keep-state setting controls hierarchy deactivation, as its API documents.
                if (!m_KeepAnimatorStateOnDisable && (!Compat.AnimatorStartupMeasured || !m_GameObject.activeInHierarchy))
                    m_Player = null;
            }
        }

        internal override void OnEngineDestroy()
        {
            Unbound.Runtime.Playables.AnimationOutputs.ForgetCulled(this);
            if (m_Registered)
            {
                m_Registered = false;
                AnimationSystem.Remove(this);
            }
            m_Player = null;
            base.OnEngineDestroy();
        }

        // Hierarchy deactivation also clears a controller retained by enabled=false.
        // It does not deliver a second component-disable notification.
        internal void DisabledHierarchyDeactivated()
        {
            if (Compat.AnimatorStartupMeasured && !m_KeepAnimatorStateOnDisable) m_Player = null;
            m_Initialized = false;
        }

        /// <summary>
        /// One update of the controller by the player loop, culled by renderer visibility.
        /// Documented (6000.0 AnimatorCullingMode, Manual "Animator component"): CullUpdateTransforms,
        /// "Retarget, IK and write of Transforms are disabled when renderers are not visible";
        /// CullCompletely, "Animation is completely disabled when renderers are not visible".
        /// Measured (tests/projects/animculling, Unity 6000.0.71f1, a Game view camera only), while
        /// no renderer of the hierarchy is visible (<see cref="AnyRendererVisible"/>, read at the
        /// update):
        /// <list type="bullet">
        /// <item>CullUpdateTransforms: time, the state machines, events, OnAnimatorMove and root motion
        /// advance as usual; the pose is not written (the transforms keep what they last had) and
        /// OnAnimatorIK does not run.</item>
        /// <item>CullCompletely: the update is skipped; nothing catches up later. The Animator's first
        /// update still runs, without advancing time and without writes or IK (OnAnimatorMove
        /// runs).</item>
        /// <item>When one of its renderers becomes visible after such an update, during the camera's
        /// culling, the pose is written and the IK pass runs once more; after a skipped update (or
        /// that first one) the movement callbacks and builtin root motion also run again with the
        /// last evaluation's deltas (<see cref="RendererBecameVisible"/>).</item>
        /// </list>
        /// Following the documentation (unmeasured): under CullUpdateTransforms only the
        /// Transforms and the humanoid pose are not written; parameter curves, materials and script
        /// fields are for controller-only updates. Pending playable outputs defer the final write and
        /// movement to their combined evaluation, which holds script fields while culled (measured).
        /// </summary>
        internal void Tick(float deltaTime)
        {
            // Pending outputs log it in their evaluation.
            if (m_NoHierarchy && m_StreamHandles != null && m_Controller != null
                && AvatarLostToStreamHandles(Unbound.Runtime.Playables.AnimationOutputs.HasPending(this) ? 0 : AvatarLostLogsPerFrame)) return;
            if (m_CullingMode == AnimatorCullingMode.AlwaysAnimate || AnyRendererVisible(m_GameObject))
            {
                m_Culled = CulledUpdate.None;
                Advance(deltaTime, deferOutput: true);
                return;
            }
            m_Culled = m_CullingMode == AnimatorCullingMode.CullCompletely ? CulledUpdate.Skipped : CulledUpdate.NotWritten;
            var player = EnsurePlayer();
            if (player == null) return;
            player.DeferOutput = Unbound.Runtime.Playables.AnimationOutputs.HasPending(this);
            if (m_CullingMode == AnimatorCullingMode.CullCompletely)
            {
                bool first = !player.Ticked;
                m_Culled = CulledUpdate.Skipped;
                if (!first) return;
                deltaTime = 0f;
            }
            else m_Culled = CulledUpdate.NotWritten;
            player.SkipWrites = true;
            try
            {
                player.Tick(deltaTime, automatic: true);
            }
            finally
            {
                player.SkipWrites = false;
            }
        }

        /// <summary>How many times this Animator's playable outputs were evaluated (deferred writes
        /// of an older evaluation are dropped).</summary>
        internal int m_OutputEvaluations;

        enum CulledUpdate : byte { None, NotWritten, Skipped }

        CulledUpdate m_Culled;

        /// <summary>A renderer became visible in a camera's culling: each culled Animator above it (its
        /// GameObject included) writes its pose and runs its IK pass, and after a skipped update its
        /// movement too (see <see cref="Tick"/>; measured, tests/projects/animculling).</summary>
        internal static void RendererBecameVisible(GameObject go)
        {
            for (var t = go != null ? go.m_Transform : null; t != null; t = t.parent)
            {
                var components = t.m_GameObject.m_Components;
                for (int i = 0; i < components.Count; i++)
                {
                    if (!(components[i] is Animator animator) || animator.m_Culled == CulledUpdate.None) continue;
                    var culled = animator.m_Culled;
                    animator.m_Culled = CulledUpdate.None;
                    if (!animator.isActiveAndEnabled) continue;
                    try
                    {
                        if (!Unbound.Runtime.Playables.AnimationOutputs.EvaluateOnVisible(animator, culled == CulledUpdate.Skipped))
                            animator.m_Player?.EvaluateOnVisible(move: culled == CulledUpdate.Skipped);
                    }
                    catch (Exception e)
                    {
                        Debug.LogException(e, animator);
                    }
                }
            }
        }

        void Advance(float deltaTime, bool deferOutput = false)
        {
            var player = EnsurePlayer();
            if (player != null) player.DeferOutput = deferOutput && Unbound.Runtime.Playables.AnimationOutputs.HasPending(this);
            player?.Tick(deltaTime, automatic: deferOutput);
        }

        /// <summary>Evaluates the animator now, advancing it by <paramref name="deltaTime"/>.
        /// Unbound's choice (unmeasured): an explicit call is not culled, whatever the culling mode
        /// and the renderers' visibility.</summary>
        public void Update(float deltaTime)
        {
            if (m_Controller != null && AvatarLostToStreamHandles(AvatarLostLogsPerUpdateCall)) return;
            m_Culled = CulledUpdate.None;
            Advance(deltaTime);
        }

        /// <summary>
        /// Whether any Renderer of <paramref name="root"/>'s hierarchy (the GameObject and all of its
        /// descendants) is visible (<see cref="Renderer.isVisible"/>), for animation culling.
        /// <list type="bullet">
        /// <item>Visibility is read when the animation update runs: what the previous frame's camera
        /// culling (and shadow casting) left, after this frame's EarlyUpdate made the renderers no
        /// camera took invisible; a renderer disabled earlier in the frame no longer counts
        /// (measured, tests/projects/animculling, the default update mode).</item>
        /// <item>A hierarchy without any Renderer is never visible, so a culling animator there is
        /// culled (measured). A uGUI hierarchy has CanvasRenderers, which are not Renderers.</item>
        /// <item>Unbound's choice (unmeasured): the hierarchy is every descendant, including the
        /// renderers under a nested Animator or Animation; renderers outside it (another hierarchy
        /// driven by its bindings) do not count.</item>
        /// <item>MeshRenderer, SpriteRenderer and SkinnedMeshRenderer decide by isVisible; a
        /// skinned renderer's updateWhenOffscreen does not change that (measured,
        /// tests/projects/animculling: a disabled or inactive renderer and one off screen do not
        /// count, with updateWhenOffscreen on or off; one visible renderer of several is enough).
        /// Root-space skinned bounds and their remaining limitations are handled by the renderer
        /// (measured, tests/projects/skinnedbounds).</item>
        /// <item>Line and trail renderers, and particle renderers whose culling bounds cover what
        /// they draw, decide by isVisible too: cameras cull them by the bounds Unity gives them
        /// (<see cref="FrameBuilder.Culler.TrustsBounds"/>; line and trail bounds measured,
        /// tests/projects/animatorapi bounds.*).</item>
        /// <item>Every other enabled renderer in an active hierarchy (a particle renderer whose
        /// bounds do not cover its trails or minimum particle size, billboard, tilemap, sprite
        /// shape) counts as visible: cameras draw them whether culled or not, so their
        /// visibility is not Unity's. Reported once (stub report "Animation culling with
        /// approximate renderer bounds").</item>
        /// </list>
        /// </summary>
        internal static bool AnyRendererVisible(GameObject root)
        {
            if (root == null) return false;
            var components = root.m_Components;
            for (int i = 0; i < components.Count; i++)
            {
                if (!(components[i] is Renderer renderer)) continue;
                if (!Unbound.Runtime.Compat.SkinnedBoundsMeasured && renderer is SkinnedMeshRenderer skinned && skinned.rootBone != null && skinned.rootBone != skinned.transform)
                    Unbound.Runtime.Stubs.Ignored("Animation culling by a skinned renderer with a root bone",
                        "judges its visibility by the mesh bounds around the renderer, not Unity's bounds around the root bone: a character whose bones move far from the renderer can be culled on screen");
                if (Unbound.Runtime.Compat.SkinnedBoundsMeasured && renderer is SkinnedMeshRenderer measuredSkin)
                    measuredSkin.ReportBoundsLimitation();
                if (renderer.isVisible) return true;
                if (!(renderer is SkinnedMeshRenderer) && !FrameBuilder.Culler.TrustsBounds(renderer) && renderer.enabled && root.activeInHierarchy)
                {
                    Unbound.Runtime.Stubs.Ignored("Animation culling with approximate renderer bounds",
                        "counts particle renderers with trails or a minimum size, billboard, tilemap and sprite shape renderers as visible while they are enabled (cameras draw them whether culled or not)");
                    return true;
                }
            }
            var children = root.m_Transform != null ? root.m_Transform.m_Children : null;
            if (children == null) return false;
            for (int i = 0; i < children.Count; i++)
            {
                var child = children[i];
                if (child != null && AnyRendererVisible(child.m_GameObject)) return true;
            }
            return false;
        }

        /// <summary>As Unity's: the animated properties go back to their defaults (the values the
        /// animator first bound, measured in the Editor's Play mode), then bind again.</summary>
        public void Rebind()
        {
            ResetBindings();
            m_Player = null;
            Skeleton?.BindExposed();
            EnsurePlayer();
            if (m_Match != null && m_Player != null && m_Player.Layers.Length > 0)
            {
                // Rebinding keeps the target and start but resets its end to one state cycle.
                // Measured with several original target times in tests/projects/modelroot.
                m_Match.Player = m_Player;
                m_Match.Run = m_Player.Layers[0].Current;
                m_Match.End = 1.0;
                m_Match.Finish = false;
                m_Match.PredictCurrentPose = true;
            }
        }

        void ResetBindings()
        {
            Unbound.Runtime.Playables.AnimationOutputs.ForgetCulled(this);
            if (m_Player == null) return;
            var root = transform;
            var position = root.localPosition;
            var rotation = root.localRotation;
            m_Player.Bindings.WriteDefaults();
            if (m_ApplyRootMotion) root.SetLocalPositionAndRotation(position, rotation);
        }

        public void WriteDefaultValues() => m_Player?.Bindings.WriteDefaults();

        // ------------------------------------------------------------------ parameters

        int Parameter(string name, AnimatorControllerParameterType? expected, out AnimatorPlayer player)
        {
            player = PlayerOrWarn(Use.Parameter);
            if (player == null) return -1;
            int index = player.ParameterIndex(name);
            if (index < 0)
            {
                if (m_LogWarnings) Debug.LogWarning(AnimatorErrors.MissingParameter(name), this);
                return -1;
            }
            return CheckType(player, index, expected, name, 0) ? index : -1;
        }

        int Parameter(int id, AnimatorControllerParameterType? expected, out AnimatorPlayer player)
        {
            player = PlayerOrWarn(Use.Parameter);
            if (player == null) return -1;
            int index = player.ParameterIndex(id);
            if (index < 0)
            {
                if (m_LogWarnings) Debug.LogWarning(AnimatorErrors.MissingParameter(AnimatorErrors.HashLabel(id)), this);
                return -1;
            }
            return CheckType(player, index, expected, null, id) ? index : -1;
        }

        /// <summary>The parameter's type check, named as the call named it (its name, or its id).</summary>
        bool CheckType(AnimatorPlayer player, int index, AnimatorControllerParameterType? expected, string name, int id)
        {
            if (expected == null || !AnimatorErrors.Mismatched(player.Graph.Parameters[index].Type, expected.Value)) return true;
            if (m_LogWarnings) Debug.LogWarning(AnimatorErrors.TypeMismatch(name ?? AnimatorErrors.HashLabel(id)), this);
            return false;
        }

        public float GetFloat(string name) { MainThreadCheck.Check("GetFloatString"); return Parameter(name, AnimatorControllerParameterType.Float, out var p) is int i && i >= 0 ? p.Floats[i] : 0f; }

        public float GetFloat(int id) { MainThreadCheck.Check("GetFloatID"); return Parameter(id, AnimatorControllerParameterType.Float, out var p) is int i && i >= 0 ? p.Floats[i] : 0f; }

        public void SetFloat(string name, float value)
        {
            MainThreadCheck.Check("SetFloatString");
            int i = Parameter(name, AnimatorControllerParameterType.Float, out var p);
            if (i >= 0) p.Floats[i] = value;
        }

        public void SetFloat(string name, float value, float dampTime, float deltaTime)
        {
            MainThreadCheck.Check("SetFloatStringDamp");
            int i = Parameter(name, AnimatorControllerParameterType.Float, out var p);
            if (i >= 0) p.SetFloatDamped(i, value, dampTime, deltaTime);
        }

        public void SetFloat(int id, float value)
        {
            MainThreadCheck.Check("SetFloatID");
            int i = Parameter(id, AnimatorControllerParameterType.Float, out var p);
            if (i >= 0) p.Floats[i] = value;
        }

        public void SetFloat(int id, float value, float dampTime, float deltaTime)
        {
            MainThreadCheck.Check("SetFloatIDDamp");
            int i = Parameter(id, AnimatorControllerParameterType.Float, out var p);
            if (i >= 0) p.SetFloatDamped(i, value, dampTime, deltaTime);
        }

        public bool GetBool(string name) => Parameter(name, AnimatorControllerParameterType.Bool, out var p) is int i && i >= 0 && p.Bools[i];

        public bool GetBool(int id) => Parameter(id, AnimatorControllerParameterType.Bool, out var p) is int i && i >= 0 && p.Bools[i];

        public void SetBool(string name, bool value)
        {
            int i = Parameter(name, AnimatorControllerParameterType.Bool, out var p);
            if (i >= 0) p.Bools[i] = value;
        }

        public void SetBool(int id, bool value)
        {
            int i = Parameter(id, AnimatorControllerParameterType.Bool, out var p);
            if (i >= 0) p.Bools[i] = value;
        }

        public int GetInteger(string name) => Parameter(name, AnimatorControllerParameterType.Int, out var p) is int i && i >= 0 ? p.Ints[i] : 0;

        public int GetInteger(int id) => Parameter(id, AnimatorControllerParameterType.Int, out var p) is int i && i >= 0 ? p.Ints[i] : 0;

        public void SetInteger(string name, int value)
        {
            int i = Parameter(name, AnimatorControllerParameterType.Int, out var p);
            if (i >= 0) p.Ints[i] = value;
        }

        public void SetInteger(int id, int value)
        {
            int i = Parameter(id, AnimatorControllerParameterType.Int, out var p);
            if (i >= 0) p.Ints[i] = value;
        }

        public void SetTrigger(string name)
        {
            int i = Parameter(name, AnimatorControllerParameterType.Trigger, out var p);
            if (i >= 0) p.Bools[i] = true;
        }

        public void SetTrigger(int id)
        {
            int i = Parameter(id, AnimatorControllerParameterType.Trigger, out var p);
            if (i >= 0) p.Bools[i] = true;
        }

        public void ResetTrigger(string name)
        {
            int i = Parameter(name, AnimatorControllerParameterType.Trigger, out var p);
            if (i >= 0) p.Bools[i] = false;
        }

        public void ResetTrigger(int id)
        {
            int i = Parameter(id, AnimatorControllerParameterType.Trigger, out var p);
            if (i >= 0) p.Bools[i] = false;
        }

        public bool IsParameterControlledByCurve(string name)
        {
            var player = EnsurePlayer();
            return player != null && player.Bindings.AnimatesParameter(this, name);
        }

        public bool IsParameterControlledByCurve(int id)
        {
            var player = EnsurePlayer();
            if (player == null) return false;
            int i = player.ParameterIndex(id);
            return i >= 0 && player.Bindings.AnimatesParameter(this, player.Graph.Parameters[i].Name);
        }

        public AnimatorControllerParameter[] parameters
        {
            get
            {
                var player = EnsurePlayer();
                if (player == null) return Array.Empty<AnimatorControllerParameter>();
                var result = new AnimatorControllerParameter[player.Graph.Parameters.Length];
                for (int i = 0; i < result.Length; i++) result[i] = CopyParameter(player.Graph.Parameters[i]);
                return result;
            }
        }

        public int parameterCount { get { MainThreadCheck.Check("get_parameterCount"); return Definition(out _)?.Parameters.Length ?? 0; } }

        public AnimatorControllerParameter GetParameter(int index)
        {
            // The parameter's lookup and then the count for the message, each warning without a
            // controller (the release's reference assemblies).
            var graph = Definition(out _);
            if (graph == null || index < 0 || index >= graph.Parameters.Length)
                throw new IndexOutOfRangeException("Index must be between 0 and " + parameterCount);
            return CopyParameter(graph.Parameters[index]);
        }

        static AnimatorControllerParameter CopyParameter(ParameterDef p) => new AnimatorControllerParameter
        {
            m_Name = p.Name,
            m_Type = p.Type,
            m_DefaultFloat = p.DefaultFloat,
            m_DefaultInt = p.DefaultInt,
            m_DefaultBool = p.DefaultBool,
        };

        // Animator parameters animated by clips (curves on the Animator with a parameter's name).
        internal bool HasParameterNamed(string name) => EnsurePlayer() is AnimatorPlayer p && p.ParameterIndex(name) >= 0;

        internal bool HasFloatParameterNamed(string name) =>
            EnsurePlayer() is AnimatorPlayer p && p.ParameterIndex(name) is int i && i >= 0
            && p.Graph.Parameters[i].Type == AnimatorControllerParameterType.Float;

        internal float GetParameterAsFloat(string name)
        {
            var p = EnsurePlayer();
            int i = p != null ? p.ParameterIndex(name) : -1;
            return i >= 0 ? p.GetAsFloat(i) : 0f;
        }

        internal void SetParameterFromCurve(string name, float value)
        {
            var p = EnsurePlayer();
            int i = p != null ? p.ParameterIndex(name) : -1;
            if (i >= 0) p.SetFromCurve(i, value);
        }

        /// <summary>Whether animation outputs write through <paramref name="bindings"/> now (they
        /// are <see cref="StreamBindings"/>), without making a player. A controller playable keeps
        /// the bindings it was first played with; once the Animator's own player was replaced
        /// (a controller swap, a disable that drops its state) they are stale. Unbound's
        /// lifetime choice (unmeasured).</summary>
        internal bool StreamsThrough(AnimationBindings bindings)
        {
            if (bindings == null || m_GameObject == null) return false;
            if (m_Player != null) return ReferenceEquals(m_Player.Bindings, bindings);
            // Without a player the next one (if the controller has a graph) brings new bindings.
            if (m_Controller != null && m_Controller.Graph != null) return false;
            return ReferenceEquals(m_OutputBindings, bindings);
        }

        // ------------------------------------------------------------------ layers

        public int layerCount
        { get { MainThreadCheck.Check("get_layerCount"); return PlayerOrWarn(Use.State)?.Layers.Length ?? 0; } }

        /// <summary>The layer members' checks: a controller playing, then the layer's index (the
        /// Unity 6000.0.71f1 Editor warns for each, tests/projects/apierrors `animator.*`).</summary>
        bool Layer(int layerIndex, out AnimatorPlayer player)
        {
            player = PlayerOrWarn(Use.State);
            if (player == null) return false;
            if (layerIndex >= 0 && layerIndex < player.Layers.Length) return true;
            Debug.LogWarning(AnimatorErrors.InvalidLayer(layerIndex), this);
            return false;
        }

        public string GetLayerName(int layerIndex) => Layer(layerIndex, out var player) ? player.Layers[layerIndex].Def.Name : "";

        public int GetLayerIndex(string layerName)
        {
            var player = EnsurePlayer();
            if (player == null) return -1;
            for (int i = 0; i < player.Layers.Length; i++)
                if (player.Layers[i].Def.Name == layerName) return i;
            return -1;
        }

        public float GetLayerWeight(int layerIndex)
        {
            // Without a controller the weight reads -1 (tests/projects/apierrors `animator.bare_layer_weight`).
            if (!Layer(layerIndex, out var player)) return player == null ? -1f : 0f;
            return layerIndex == 0 ? 1f : player.Layers[layerIndex].Weight;
        }

        public void SetLayerWeight(int layerIndex, float weight)
        {
            if (!Layer(layerIndex, out var player)) return;
            if (layerIndex == 0) return; // the base layer always has full weight
            player.Layers[layerIndex].Weight = weight;
        }

        // ------------------------------------------------------------------ state queries

        public AnimatorStateInfo GetCurrentAnimatorStateInfo(int layerIndex)
        {
            MainThreadCheck.Check("GetAnimatorStateInfo");
            if (!Layer(layerIndex, out var player) || player.Restarted && !player.Ticked) return default;
            var layer = player.Layers[layerIndex];
            var source = Source(player, layer);
            return player.StateInfo(layer, source.Current);
        }

        public AnimatorStateInfo GetNextAnimatorStateInfo(int layerIndex)
        {
            if (!Layer(layerIndex, out var player)) return default;
            var layer = player.Layers[layerIndex];
            var source = Source(player, layer);
            return source.Transition != null ? player.StateInfo(layer, source.Next) : default;
        }

        public AnimatorTransitionInfo GetAnimatorTransitionInfo(int layerIndex)
        {
            if (!Layer(layerIndex, out var player)) return default;
            return player.TransitionInfo(Source(player, player.Layers[layerIndex]));
        }

        public bool IsInTransition(int layerIndex)
        {
            if (!Layer(layerIndex, out var player)) return false;
            return Source(player, player.Layers[layerIndex]).Transition != null;
        }

        static LayerRun Source(AnimatorPlayer player, LayerRun layer)
        {
            int synced = layer.Def.SyncedLayerIndex;
            return synced >= 0 && synced < player.Layers.Length ? player.Layers[synced] : layer;
        }

        List<AnimatorClipInfo> ClipInfos(int layerIndex, bool next)
        {
            var result = new List<AnimatorClipInfo>();
            if (!Layer(layerIndex, out var player)) return result;
            var layer = player.Layers[layerIndex];
            var source = Source(player, layer);
            var run = next ? (source.Transition != null ? source.Next : null) : source.Current;
            if (run == null) return result;
            // The clips' blending weights: a transition weighs the states by its progress, unless
            // the other state is an empty one the clips play beside alone (measured,
            // tests/projects/humanoid detail records).
            float weight = 1f;
            if (source.Transition != null && source.Next != null && !player.WholeBesideEmpty(layer, run, next ? source.Current : source.Next))
                weight = next ? source.Transition.Progress : 1f - source.Transition.Progress;
            player.ClipWeights(player.MotionOf(layer.Def, run.Def), weight, result);
            return result;
        }

        public int GetCurrentAnimatorClipInfoCount(int layerIndex) => ClipInfos(layerIndex, false).Count;

        public int GetNextAnimatorClipInfoCount(int layerIndex) => ClipInfos(layerIndex, true).Count;

        public AnimatorClipInfo[] GetCurrentAnimatorClipInfo(int layerIndex) => ClipInfos(layerIndex, false).ToArray();

        public AnimatorClipInfo[] GetNextAnimatorClipInfo(int layerIndex) => ClipInfos(layerIndex, true).ToArray();

        public void GetCurrentAnimatorClipInfo(int layerIndex, List<AnimatorClipInfo> clips)
        {
            if (clips == null) throw new ArgumentNullException(nameof(clips));
            clips.Clear();
            clips.AddRange(ClipInfos(layerIndex, false));
        }

        public void GetNextAnimatorClipInfo(int layerIndex, List<AnimatorClipInfo> clips)
        {
            if (clips == null) throw new ArgumentNullException(nameof(clips));
            clips.Clear();
            clips.AddRange(ClipInfos(layerIndex, true));
        }

        public bool HasState(int layerIndex, int stateID)
        {
            if (!Layer(layerIndex, out var player)) return false;
            return player.FindLayer(stateID, layerIndex, out _) >= 0;
        }

        // ------------------------------------------------------------------ playing states

        bool Resolve(int stateNameHash, int layer, out AnimatorPlayer player, out int layerIndex, out StateDef state)
        {
            state = null;
            layerIndex = -1;
            player = PlayerOrWarn(Use.Play);
            if (player == null) return false;
            if (layer >= player.Layers.Length || layer < -1)
            {
                Debug.LogWarning(AnimatorErrors.InvalidLayer(layer), this);
                return false;
            }
            layerIndex = player.FindLayer(stateNameHash, layer, out state);
            if (layerIndex < 0)
            {
                AnimatorErrors.StateMissing(layer, this);
                return false;
            }
            return true;
        }

        public void Play(string stateName) => Play(StringToHash(stateName), -1, float.NegativeInfinity);

        public void Play(string stateName, int layer) => Play(StringToHash(stateName), layer, float.NegativeInfinity);

        public void Play(string stateName, [Internal.DefaultValue("-1")] int layer, [Internal.DefaultValue("float.NegativeInfinity")] float normalizedTime) =>
            Play(StringToHash(stateName), layer, normalizedTime);

        public void Play(int stateNameHash) => Play(stateNameHash, -1, float.NegativeInfinity);

        public void Play(int stateNameHash, int layer) => Play(stateNameHash, layer, float.NegativeInfinity);

        public void Play(int stateNameHash, [Internal.DefaultValue("-1")] int layer, [Internal.DefaultValue("float.NegativeInfinity")] float normalizedTime)
        {
            MainThreadCheck.Check("Play");
            if (Resolve(stateNameHash, layer, out var player, out int index, out var state))
                player.RequestPlay(index, state, normalizedTime, fixedTime: false);
        }

        public void PlayInFixedTime(string stateName) => PlayInFixedTime(StringToHash(stateName), -1, float.NegativeInfinity);

        public void PlayInFixedTime(string stateName, int layer) => PlayInFixedTime(StringToHash(stateName), layer, float.NegativeInfinity);

        public void PlayInFixedTime(string stateName, [Internal.DefaultValue("-1")] int layer, [Internal.DefaultValue("float.NegativeInfinity")] float fixedTime) =>
            PlayInFixedTime(StringToHash(stateName), layer, fixedTime);

        public void PlayInFixedTime(int stateNameHash) => PlayInFixedTime(stateNameHash, -1, float.NegativeInfinity);

        public void PlayInFixedTime(int stateNameHash, int layer) => PlayInFixedTime(stateNameHash, layer, float.NegativeInfinity);

        public void PlayInFixedTime(int stateNameHash, [Internal.DefaultValue("-1")] int layer, [Internal.DefaultValue("float.NegativeInfinity")] float fixedTime)
        {
            if (Resolve(stateNameHash, layer, out var player, out int index, out var state))
                player.RequestPlay(index, state, fixedTime, fixedTime: true);
        }

        public void CrossFade(string stateName, float normalizedTransitionDuration) =>
            CrossFade(StringToHash(stateName), normalizedTransitionDuration, -1, float.NegativeInfinity, 0f);

        public void CrossFade(string stateName, float normalizedTransitionDuration, int layer) =>
            CrossFade(StringToHash(stateName), normalizedTransitionDuration, layer, float.NegativeInfinity, 0f);

        public void CrossFade(string stateName, float normalizedTransitionDuration, int layer, float normalizedTimeOffset) =>
            CrossFade(StringToHash(stateName), normalizedTransitionDuration, layer, normalizedTimeOffset, 0f);

        public void CrossFade(string stateName, float normalizedTransitionDuration, [Internal.DefaultValue("-1")] int layer,
            [Internal.DefaultValue("float.NegativeInfinity")] float normalizedTimeOffset, [Internal.DefaultValue("0.0f")] float normalizedTransitionTime) =>
            CrossFade(StringToHash(stateName), normalizedTransitionDuration, layer, normalizedTimeOffset, normalizedTransitionTime);

        public void CrossFade(int stateHashName, float normalizedTransitionDuration) =>
            CrossFade(stateHashName, normalizedTransitionDuration, -1, 0f, 0f);

        public void CrossFade(int stateHashName, float normalizedTransitionDuration, int layer) =>
            CrossFade(stateHashName, normalizedTransitionDuration, layer, 0f, 0f);

        public void CrossFade(int stateHashName, float normalizedTransitionDuration, int layer, float normalizedTimeOffset) =>
            CrossFade(stateHashName, normalizedTransitionDuration, layer, normalizedTimeOffset, 0f);

        public void CrossFade(int stateHashName, float normalizedTransitionDuration, [Internal.DefaultValue("-1")] int layer,
            [Internal.DefaultValue("0.0f")] float normalizedTimeOffset, [Internal.DefaultValue("0.0f")] float normalizedTransitionTime)
        {
            if (Resolve(stateHashName, layer, out var player, out int index, out var state))
                player.RequestCrossFade(index, state, normalizedTransitionDuration, false, normalizedTimeOffset, false, normalizedTransitionTime);
        }

        public void CrossFadeInFixedTime(string stateName, float fixedTransitionDuration) =>
            CrossFadeInFixedTime(StringToHash(stateName), fixedTransitionDuration, -1, 0f, 0f);

        public void CrossFadeInFixedTime(string stateName, float fixedTransitionDuration, int layer) =>
            CrossFadeInFixedTime(StringToHash(stateName), fixedTransitionDuration, layer, 0f, 0f);

        public void CrossFadeInFixedTime(string stateName, float fixedTransitionDuration, int layer, float fixedTimeOffset) =>
            CrossFadeInFixedTime(StringToHash(stateName), fixedTransitionDuration, layer, fixedTimeOffset, 0f);

        public void CrossFadeInFixedTime(string stateName, float fixedTransitionDuration, [Internal.DefaultValue("-1")] int layer,
            [Internal.DefaultValue("0.0f")] float fixedTimeOffset, [Internal.DefaultValue("0.0f")] float normalizedTransitionTime) =>
            CrossFadeInFixedTime(StringToHash(stateName), fixedTransitionDuration, layer, fixedTimeOffset, normalizedTransitionTime);

        public void CrossFadeInFixedTime(int stateHashName, float fixedTransitionDuration) =>
            CrossFadeInFixedTime(stateHashName, fixedTransitionDuration, -1, 0f, 0f);

        public void CrossFadeInFixedTime(int stateHashName, float fixedTransitionDuration, int layer) =>
            CrossFadeInFixedTime(stateHashName, fixedTransitionDuration, layer, 0f, 0f);

        public void CrossFadeInFixedTime(int stateHashName, float fixedTransitionDuration, int layer, float fixedTimeOffset) =>
            CrossFadeInFixedTime(stateHashName, fixedTransitionDuration, layer, fixedTimeOffset, 0f);

        public void CrossFadeInFixedTime(int stateHashName, float fixedTransitionDuration, [Internal.DefaultValue("-1")] int layer,
            [Internal.DefaultValue("0.0f")] float fixedTimeOffset, [Internal.DefaultValue("0.0f")] float normalizedTransitionTime)
        {
            if (Resolve(stateHashName, layer, out var player, out int index, out var state))
                player.RequestCrossFade(index, state, fixedTransitionDuration, true, fixedTimeOffset, true, normalizedTransitionTime);
        }

        // ------------------------------------------------------------------ behaviours

        public T GetBehaviour<T>() where T : StateMachineBehaviour
        {
            var player = EnsurePlayer();
            if (player == null) return null;
            foreach (var b in player.AllBehaviours)
                if (b is T t) return t;
            return null;
        }

        public T[] GetBehaviours<T>() where T : StateMachineBehaviour
        {
            var result = new List<T>();
            var player = EnsurePlayer();
            if (player != null)
                foreach (var b in player.AllBehaviours)
                    if (b is T t) result.Add(t);
            return result.ToArray();
        }

        public StateMachineBehaviour[] GetBehaviours(int fullPathHash, int layerIndex) =>
            EnsurePlayer()?.Behaviours(fullPathHash, layerIndex) ?? Array.Empty<StateMachineBehaviour>();

        // ------------------------------------------------------------------ serialized data

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            var controller = body.Ref("m_Controller");
            m_Controller = controller.IsNull ? null : resolver.Resolve(controller, typeof(RuntimeAnimatorController)) as RuntimeAnimatorController;
            var avatar = body.Ref("m_Avatar");
            m_Avatar = avatar.IsNull ? null : resolver.Resolve(avatar, typeof(Avatar)) as Avatar;
            m_CullingMode = (AnimatorCullingMode)body.Int("m_CullingMode");
            m_UpdateMode = (AnimatorUpdateMode)body.Int("m_UpdateMode");
            m_ApplyRootMotion = body.Bool("m_ApplyRootMotion");
            // Animators saved before the field existed animated physics through the old
            // AnimatePhysics update mode (now Fixed), and still do.
            m_AnimatePhysics = body.Bool("m_AnimatePhysics", m_UpdateMode == AnimatorUpdateMode.Fixed);
            m_KeepAnimatorStateOnDisable = body.Bool("m_KeepAnimatorStateOnDisable") || body.Bool("m_KeepAnimatorControllerStateOnDisable");
            m_WriteDefaultValuesOnDisable = body.Bool("m_WriteDefaultValuesOnDisable");
            m_NoHierarchy = !body.Bool("m_HasTransformHierarchy", true);
            if (body.Bool("m_UnboundOptimizeGameObjects"))
            {
                var paths = new List<string>();
                if (body.Get("m_UnboundExtraExposedTransformPaths") is YNode exposed)
                    foreach (var item in exposed.Seq()) paths.Add(item.AsString());
                m_OptimizeOnLoad = paths.ToArray();
            }
        }

        /// <summary>What <see cref="ReadSerialized"/> reads (an imported prefab's documents).</summary>
        internal override void WriteSerialized(Unbound.Runtime.UnityYamlWriter.Writer w)
        {
            w.Bool("m_Enabled", m_Enabled);
            w.Ref("m_Avatar", m_Avatar);
            w.Ref("m_Controller", m_Controller);
            w.Int("m_CullingMode", (int)m_CullingMode);
            w.Int("m_UpdateMode", (int)m_UpdateMode);
            w.Bool("m_ApplyRootMotion", m_ApplyRootMotion);
            w.Bool("m_AnimatePhysics", m_AnimatePhysics);
            w.Bool("m_KeepAnimatorStateOnDisable", m_KeepAnimatorStateOnDisable);
            w.Bool("m_WriteDefaultValuesOnDisable", m_WriteDefaultValuesOnDisable);
            w.Bool("m_HasTransformHierarchy", !m_NoHierarchy);
            if (m_OptimizeOnLoad != null)
            {
                w.Bool("m_UnboundOptimizeGameObjects", true);
                w.Scalars("m_UnboundExtraExposedTransformPaths", Array.ConvertAll(m_OptimizeOnLoad, Unbound.Runtime.UnityYamlWriter.Scalar));
            }
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (Animator)source;
            m_Enabled = s.m_Enabled;
            m_Controller = s.m_Controller;
            m_CullingMode = s.m_CullingMode;
            m_UpdateMode = s.m_UpdateMode;
            m_ApplyRootMotion = s.m_ApplyRootMotion;
            m_AnimatePhysics = s.m_AnimatePhysics;
            m_Avatar = s.m_Avatar;
            m_KeepAnimatorStateOnDisable = s.m_KeepAnimatorStateOnDisable;
            m_WriteDefaultValuesOnDisable = s.m_WriteDefaultValuesOnDisable;
            m_NoHierarchy = s.m_NoHierarchy;
            m_Speed = s.m_Speed;
            m_FireEvents = s.m_FireEvents;
            m_LogWarnings = s.m_LogWarnings;
        }
    }
}
