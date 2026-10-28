using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>
    /// Plays an animator controller on its GameObject's hierarchy. Evaluated in
    /// PreLateUpdate.DirectorUpdateAnimationBegin (or with physics for AnimatorUpdateMode.Fixed).
    /// Generic (non-humanoid) animation only: avatars, IK and root motion are not applied.
    /// </summary>
    [ClassId(95)]
    public partial class Animator : Behaviour
    {
        internal RuntimeAnimatorController m_Controller;
        internal AnimatorCullingMode m_CullingMode;
        internal AnimatorUpdateMode m_UpdateMode;
        internal bool m_ApplyRootMotion;
        internal bool m_KeepAnimatorStateOnDisable;
        internal bool m_WriteDefaultValuesOnDisable;
        internal float m_Speed = 1f;
        internal bool m_FireEvents = true;
        internal bool m_LogWarnings = true;
        internal AnimatorPlayer m_Player;
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
                return m_OutputBindings ??= new AnimationBindings(m_GameObject);
            }
        }

        /// <summary>The graph that plays the Animator's controller: an AnimatorControllerPlayable
        /// on an animation output. The Animator updates it itself (AnimationSystem).</summary>
        public UnityEngine.Playables.PlayableGraph playableGraph
        {
            get
            {
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

        public bool isOptimizable => false;

        public bool isHuman => false;

        public bool hasRootMotion => false;

        public bool isInitialized => m_Player != null;

        public bool applyRootMotion
        {
            get => m_ApplyRootMotion;
            set => m_ApplyRootMotion = value;
        }

        [Obsolete("Animator.animatePhysics has been deprecated. Use Animator.updateMode instead.")]
        public bool animatePhysics
        {
            get => m_UpdateMode == AnimatorUpdateMode.Fixed;
            set => m_UpdateMode = value ? AnimatorUpdateMode.Fixed : AnimatorUpdateMode.Normal;
        }

        public AnimatorUpdateMode updateMode
        {
            get => m_UpdateMode;
            set => m_UpdateMode = value;
        }

        public bool hasTransformHierarchy => true;

        public float speed
        {
            get => m_Speed;
            set => m_Speed = value;
        }

        /// <summary>Stored; Unbound does not cull animators (they always animate).</summary>
        public AnimatorCullingMode cullingMode
        {
            get => m_CullingMode;
            set => m_CullingMode = value;
        }

        public RuntimeAnimatorController runtimeAnimatorController
        {
            get => m_Controller;
            set
            {
                if (m_Controller == value) return;
                m_Controller = value;
                m_Player = null;
                if (isActiveAndEnabled) EnsurePlayer();
            }
        }

        public bool logWarnings
        {
            get => m_LogWarnings;
            set => m_LogWarnings = value;
        }

        public bool fireEvents
        {
            get => m_FireEvents;
            set => m_FireEvents = value;
        }

        [Obsolete("keepAnimatorControllerStateOnDisable is obsolete. Use keepAnimatorStateOnDisable instead (UnityUpgradable) -> keepAnimatorStateOnDisable", false)]
        public bool keepAnimatorControllerStateOnDisable
        {
            get => m_KeepAnimatorStateOnDisable;
            set => m_KeepAnimatorStateOnDisable = value;
        }

        public bool keepAnimatorStateOnDisable
        {
            get => m_KeepAnimatorStateOnDisable;
            set => m_KeepAnimatorStateOnDisable = value;
        }

        public bool writeDefaultValuesOnDisable
        {
            get => m_WriteDefaultValuesOnDisable;
            set => m_WriteDefaultValuesOnDisable = value;
        }

        public Transform avatarRoot => m_GameObject != null ? m_GameObject.m_Transform : null;

        public Vector3 deltaPosition => Vector3.zero;

        public Quaternion deltaRotation => Quaternion.identity;

        public Vector3 velocity => Vector3.zero;

        public Vector3 angularVelocity => Vector3.zero;

        public float humanScale => 1f;

        public float pivotWeight => 0.5f;

        // ------------------------------------------------------------------ player

        AnimatorPlayer EnsurePlayer()
        {
            if (m_Player != null) return m_Player;
            if (m_Controller == null || m_GameObject == null) return null;
            var graph = m_Controller.Graph;
            if (graph == null) return null;
            m_Player = new AnimatorPlayer(this, m_Controller, graph);
            m_Player.InstantiateBehaviours();
            return m_Player;
        }

        AnimatorPlayer PlayerOrWarn()
        {
            var player = EnsurePlayer();
            if (player == null && m_LogWarnings)
                Debug.LogWarning("Animator is not playing an AnimatorController", this);
            return player;
        }

        internal override void OnEngineEnable()
        {
            if (!m_Registered)
            {
                m_Registered = true;
                AnimationSystem.Add(this);
            }
            EnsurePlayer();
        }

        internal override void OnEngineDisable()
        {
            if (m_Registered)
            {
                m_Registered = false;
                AnimationSystem.Remove(this);
            }
            if (m_Player != null)
            {
                if (m_WriteDefaultValuesOnDisable) m_Player.Bindings.WriteDefaults();
                // Unless kept, the controller's state (and the captured defaults) start over on enable.
                if (!m_KeepAnimatorStateOnDisable) m_Player = null;
            }
        }

        internal override void OnEngineDestroy()
        {
            if (m_Registered)
            {
                m_Registered = false;
                AnimationSystem.Remove(this);
            }
            m_Player = null;
            base.OnEngineDestroy();
        }

        /// <summary>One update of the controller by the player loop.</summary>
        internal void Tick(float deltaTime)
        {
            var player = EnsurePlayer();
            player?.Tick(deltaTime);
        }

        /// <summary>Evaluates the animator now, advancing it by <paramref name="deltaTime"/>.</summary>
        public void Update(float deltaTime) => Tick(deltaTime);

        /// <summary>As Unity's: the animated properties go back to their defaults (the values the
        /// animator first bound, measured in the Editor's Play mode), then bind again.</summary>
        public void Rebind()
        {
            m_Player?.Bindings.WriteDefaults();
            m_Player = null;
            EnsurePlayer();
        }

        public void WriteDefaultValues() => m_Player?.Bindings.WriteDefaults();

        public void ApplyBuiltinRootMotion() { }

        // ------------------------------------------------------------------ parameters

        int Parameter(string name, AnimatorControllerParameterType? expected, out AnimatorPlayer player)
        {
            player = PlayerOrWarn();
            if (player == null) return -1;
            int index = player.ParameterIndex(name);
            if (index < 0)
            {
                if (m_LogWarnings) Debug.LogWarning($"Parameter '{name}' does not exist.", this);
                return -1;
            }
            return CheckType(player, index, expected, name) ? index : -1;
        }

        int Parameter(int id, AnimatorControllerParameterType? expected, out AnimatorPlayer player)
        {
            player = PlayerOrWarn();
            if (player == null) return -1;
            int index = player.ParameterIndex(id);
            if (index < 0)
            {
                if (m_LogWarnings) Debug.LogWarning($"Parameter '{id}' does not exist.", this);
                return -1;
            }
            return CheckType(player, index, expected, player.Graph.Parameters[index].Name) ? index : -1;
        }

        bool CheckType(AnimatorPlayer player, int index, AnimatorControllerParameterType? expected, string name)
        {
            if (expected == null) return true;
            var type = player.Graph.Parameters[index].Type;
            if (type == expected.Value) return true;
            // Bools and triggers are both boolean values.
            bool boolean = (type == AnimatorControllerParameterType.Bool || type == AnimatorControllerParameterType.Trigger) &&
                           (expected == AnimatorControllerParameterType.Bool || expected == AnimatorControllerParameterType.Trigger);
            if (boolean) return true;
            if (m_LogWarnings) Debug.LogWarning($"Parameter type '{expected.Value}' does not match parameter '{name}' of type '{type}'.", this);
            return false;
        }

        public float GetFloat(string name) => Parameter(name, AnimatorControllerParameterType.Float, out var p) is int i && i >= 0 ? p.Floats[i] : 0f;

        public float GetFloat(int id) => Parameter(id, AnimatorControllerParameterType.Float, out var p) is int i && i >= 0 ? p.Floats[i] : 0f;

        public void SetFloat(string name, float value)
        {
            int i = Parameter(name, AnimatorControllerParameterType.Float, out var p);
            if (i >= 0) p.Floats[i] = value;
        }

        public void SetFloat(string name, float value, float dampTime, float deltaTime)
        {
            int i = Parameter(name, AnimatorControllerParameterType.Float, out var p);
            if (i >= 0) p.SetFloatDamped(i, value, dampTime, deltaTime);
        }

        public void SetFloat(int id, float value)
        {
            int i = Parameter(id, AnimatorControllerParameterType.Float, out var p);
            if (i >= 0) p.Floats[i] = value;
        }

        public void SetFloat(int id, float value, float dampTime, float deltaTime)
        {
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

        public int parameterCount => EnsurePlayer()?.Graph.Parameters.Length ?? 0;

        public AnimatorControllerParameter GetParameter(int index)
        {
            var player = EnsurePlayer();
            if (player == null || index < 0 || index >= player.Graph.Parameters.Length)
                throw new IndexOutOfRangeException("Index must be between 0 and " + (player?.Graph.Parameters.Length ?? 0));
            return CopyParameter(player.Graph.Parameters[index]);
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

        // ------------------------------------------------------------------ layers

        public int layerCount => EnsurePlayer()?.Layers.Length ?? 0;

        bool ValidLayer(AnimatorPlayer player, int layerIndex)
        {
            if (player != null && layerIndex >= 0 && layerIndex < player.Layers.Length) return true;
            if (player != null) Debug.LogError($"Invalid Layer Index '{layerIndex}'", this);
            return false;
        }

        public string GetLayerName(int layerIndex)
        {
            var player = EnsurePlayer();
            return ValidLayer(player, layerIndex) ? player.Layers[layerIndex].Def.Name : null;
        }

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
            var player = EnsurePlayer();
            if (!ValidLayer(player, layerIndex)) return 0f;
            return layerIndex == 0 ? 1f : player.Layers[layerIndex].Weight;
        }

        public void SetLayerWeight(int layerIndex, float weight)
        {
            var player = EnsurePlayer();
            if (!ValidLayer(player, layerIndex)) return;
            if (layerIndex == 0) return; // the base layer always has full weight
            player.Layers[layerIndex].Weight = weight;
        }

        // ------------------------------------------------------------------ state queries

        public AnimatorStateInfo GetCurrentAnimatorStateInfo(int layerIndex)
        {
            var player = EnsurePlayer();
            if (!ValidLayer(player, layerIndex)) return default;
            var layer = player.Layers[layerIndex];
            var source = Source(player, layer);
            return player.StateInfo(layer, source.Current);
        }

        public AnimatorStateInfo GetNextAnimatorStateInfo(int layerIndex)
        {
            var player = EnsurePlayer();
            if (!ValidLayer(player, layerIndex)) return default;
            var layer = player.Layers[layerIndex];
            var source = Source(player, layer);
            return source.Transition != null ? player.StateInfo(layer, source.Next) : default;
        }

        public AnimatorTransitionInfo GetAnimatorTransitionInfo(int layerIndex)
        {
            var player = EnsurePlayer();
            if (!ValidLayer(player, layerIndex)) return default;
            return player.TransitionInfo(Source(player, player.Layers[layerIndex]));
        }

        public bool IsInTransition(int layerIndex)
        {
            var player = EnsurePlayer();
            if (!ValidLayer(player, layerIndex)) return false;
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
            var player = EnsurePlayer();
            if (!ValidLayer(player, layerIndex)) return result;
            var layer = player.Layers[layerIndex];
            var source = Source(player, layer);
            var run = next ? (source.Transition != null ? source.Next : null) : source.Current;
            if (run == null) return result;
            player.ClipWeights(player.MotionOf(layer.Def, run.Def), 1f, result);
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
            var player = EnsurePlayer();
            if (!ValidLayer(player, layerIndex)) return false;
            return player.FindLayer(stateID, layerIndex, out _) >= 0;
        }

        // ------------------------------------------------------------------ playing states

        bool Resolve(int stateNameHash, int layer, out AnimatorPlayer player, out int layerIndex, out StateDef state)
        {
            state = null;
            layerIndex = -1;
            player = PlayerOrWarn();
            if (player == null) return false;
            if (layer >= player.Layers.Length || layer < -1)
            {
                Debug.LogError($"Invalid Layer Index '{layer}'", this);
                return false;
            }
            layerIndex = player.FindLayer(stateNameHash, layer, out state);
            if (layerIndex < 0)
            {
                Debug.LogWarning($"Animator.GotoState: State could not be found", this);
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
            m_CullingMode = (AnimatorCullingMode)body.Int("m_CullingMode");
            m_UpdateMode = (AnimatorUpdateMode)body.Int("m_UpdateMode");
            m_ApplyRootMotion = body.Bool("m_ApplyRootMotion");
            m_KeepAnimatorStateOnDisable = body.Bool("m_KeepAnimatorStateOnDisable") || body.Bool("m_KeepAnimatorControllerStateOnDisable");
            m_WriteDefaultValuesOnDisable = body.Bool("m_WriteDefaultValuesOnDisable");
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (Animator)source;
            m_Enabled = s.m_Enabled;
            m_Controller = s.m_Controller;
            m_CullingMode = s.m_CullingMode;
            m_UpdateMode = s.m_UpdateMode;
            m_ApplyRootMotion = s.m_ApplyRootMotion;
            m_Avatar = s.m_Avatar;
            m_KeepAnimatorStateOnDisable = s.m_KeepAnimatorStateOnDisable;
            m_WriteDefaultValuesOnDisable = s.m_WriteDefaultValuesOnDisable;
            m_Speed = s.m_Speed;
            m_FireEvents = s.m_FireEvents;
            m_LogWarnings = s.m_LogWarnings;
        }
    }
}
