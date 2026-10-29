using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Unbound.Runtime.Playables;
using UnityEngine.Playables;

namespace UnityEngine.Animations
{
    /// <summary>
    /// A playable running an animator controller's state machines. Its parameters, layers and
    /// states answer as the Animator's do; it animates the Animator of the animation output that
    /// plays it (made on first use).
    /// </summary>
    public partial struct AnimatorControllerPlayable : IPlayable, IEquatable<AnimatorControllerPlayable>
    {
        PlayableHandle m_Handle;

        static readonly AnimatorControllerPlayable m_NullPlayable = new AnimatorControllerPlayable(PlayableHandle.Null);

        public static AnimatorControllerPlayable Null => m_NullPlayable;

        public static AnimatorControllerPlayable Create(PlayableGraph graph, RuntimeAnimatorController controller)
        {
            var kind = new ControllerKind { Controller = controller };
            var handle = AnimationPlayableUtil.Create(graph, typeof(AnimatorControllerPlayable), kind, 1);
            kind.Node = PlayableGraphData.Get(handle);
            return new AnimatorControllerPlayable(handle);
        }

        internal AnimatorControllerPlayable(PlayableHandle handle)
        {
            m_Handle = PlayableHandle.Null;
            SetHandle(handle);
        }

        public PlayableHandle GetHandle() => m_Handle;

        public void SetHandle(PlayableHandle handle)
        {
            if (m_Handle.IsValid()) throw new InvalidOperationException("Cannot call IPlayable.SetHandle on an instance that already contains a valid handle.");
            if (handle.IsValid() && !handle.IsPlayableOfType<AnimatorControllerPlayable>())
                throw new InvalidCastException("Can't set handle: the playable is not an AnimatorControllerPlayable.");
            m_Handle = handle;
        }

        public static implicit operator Playable(AnimatorControllerPlayable playable) => new Playable(playable.GetHandle());

        public static explicit operator AnimatorControllerPlayable(Playable playable) => new AnimatorControllerPlayable(playable.GetHandle());

        public bool Equals(AnimatorControllerPlayable other) => GetHandle() == other.GetHandle();

        AnimatorPlayer Player => (PlayableGraphData.Get(m_Handle)?.Kind as ControllerKind)?.EnsurePlayer();

        // ------------------------------------------------------------------ parameters

        int Parameter(string name, AnimatorControllerParameterType expected, out AnimatorPlayer player)
        {
            player = Player;
            if (player == null) return -1;
            int i = player.ParameterIndex(name);
            if (i < 0)
            {
                Debug.LogWarning($"Parameter '{name}' does not exist.");
                return -1;
            }
            return Matches(player, i, expected) ? i : -1;
        }

        int Parameter(int id, AnimatorControllerParameterType expected, out AnimatorPlayer player)
        {
            player = Player;
            if (player == null) return -1;
            int i = player.ParameterIndex(id);
            if (i < 0)
            {
                Debug.LogWarning($"Parameter '{id}' does not exist.");
                return -1;
            }
            return Matches(player, i, expected) ? i : -1;
        }

        static bool Matches(AnimatorPlayer player, int index, AnimatorControllerParameterType expected)
        {
            var type = player.Graph.Parameters[index].Type;
            if (type == expected) return true;
            bool boolean = (type == AnimatorControllerParameterType.Bool || type == AnimatorControllerParameterType.Trigger) &&
                           (expected == AnimatorControllerParameterType.Bool || expected == AnimatorControllerParameterType.Trigger);
            if (!boolean) Debug.LogWarning($"Parameter type '{expected}' does not match parameter '{player.Graph.Parameters[index].Name}' of type '{type}'.");
            return boolean;
        }

        public float GetFloat(string name) => Parameter(name, AnimatorControllerParameterType.Float, out var p) is int i && i >= 0 ? p.Floats[i] : 0f;

        public float GetFloat(int id) => Parameter(id, AnimatorControllerParameterType.Float, out var p) is int i && i >= 0 ? p.Floats[i] : 0f;

        public void SetFloat(string name, float value)
        {
            int i = Parameter(name, AnimatorControllerParameterType.Float, out var p);
            if (i >= 0) p.Floats[i] = value;
        }

        public void SetFloat(int id, float value)
        {
            int i = Parameter(id, AnimatorControllerParameterType.Float, out var p);
            if (i >= 0) p.Floats[i] = value;
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
            Unbound.Runtime.Stubs.Ignored("AnimatorControllerPlayable.IsParameterControlledByCurve", "is always false for a playable");
            return false;
        }

        public bool IsParameterControlledByCurve(int id)
        {
            Unbound.Runtime.Stubs.Ignored("AnimatorControllerPlayable.IsParameterControlledByCurve", "is always false for a playable");
            return false;
        }

        public int GetParameterCount() => Player?.Graph.Parameters.Length ?? 0;

        public AnimatorControllerParameter GetParameter(int index)
        {
            var player = Player;
            if (player == null || index < 0 || index >= player.Graph.Parameters.Length)
                throw new IndexOutOfRangeException("Invalid parameter index.");
            var p = player.Graph.Parameters[index];
            return new AnimatorControllerParameter { m_Name = p.Name, m_Type = p.Type, m_DefaultFloat = p.DefaultFloat, m_DefaultInt = p.DefaultInt, m_DefaultBool = p.DefaultBool };
        }

        // ------------------------------------------------------------------ layers and states

        bool Layer(int layerIndex, out AnimatorPlayer player)
        {
            player = Player;
            if (player != null && layerIndex >= 0 && layerIndex < player.Layers.Length) return true;
            if (player != null) Debug.LogError($"Invalid Layer Index '{layerIndex}'");
            return false;
        }

        static LayerRun Source(AnimatorPlayer player, LayerRun layer)
        {
            int synced = layer.Def.SyncedLayerIndex;
            return synced >= 0 && synced < player.Layers.Length ? player.Layers[synced] : layer;
        }

        public int GetLayerCount() => Player?.Layers.Length ?? 0;

        public string GetLayerName(int layerIndex) => Layer(layerIndex, out var p) ? p.Layers[layerIndex].Def.Name : null;

        public int GetLayerIndex(string layerName)
        {
            var player = Player;
            if (player == null) return -1;
            for (int i = 0; i < player.Layers.Length; i++)
                if (player.Layers[i].Def.Name == layerName) return i;
            return -1;
        }

        public float GetLayerWeight(int layerIndex) => Layer(layerIndex, out var p) ? (layerIndex == 0 ? 1f : p.Layers[layerIndex].Weight) : 0f;

        public void SetLayerWeight(int layerIndex, float weight)
        {
            if (Layer(layerIndex, out var p) && layerIndex > 0) p.Layers[layerIndex].Weight = weight;
        }

        public AnimatorStateInfo GetCurrentAnimatorStateInfo(int layerIndex)
        {
            if (!Layer(layerIndex, out var p)) return default;
            var layer = p.Layers[layerIndex];
            return p.StateInfo(layer, Source(p, layer).Current);
        }

        public AnimatorStateInfo GetNextAnimatorStateInfo(int layerIndex)
        {
            if (!Layer(layerIndex, out var p)) return default;
            var layer = p.Layers[layerIndex];
            var source = Source(p, layer);
            return source.Transition != null ? p.StateInfo(layer, source.Next) : default;
        }

        public AnimatorTransitionInfo GetAnimatorTransitionInfo(int layerIndex) =>
            Layer(layerIndex, out var p) ? p.TransitionInfo(Source(p, p.Layers[layerIndex])) : default;

        public bool IsInTransition(int layerIndex) => Layer(layerIndex, out var p) && Source(p, p.Layers[layerIndex]).Transition != null;

        List<AnimatorClipInfo> ClipInfos(int layerIndex, bool next)
        {
            var result = new List<AnimatorClipInfo>();
            if (!Layer(layerIndex, out var p)) return result;
            var layer = p.Layers[layerIndex];
            var source = Source(p, layer);
            var run = next ? (source.Transition != null ? source.Next : null) : source.Current;
            if (run != null) p.ClipWeights(p.MotionOf(layer.Def, run.Def), 1f, result);
            return result;
        }

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

        public int GetCurrentAnimatorClipInfoCount(int layerIndex) => ClipInfos(layerIndex, false).Count;

        public int GetNextAnimatorClipInfoCount(int layerIndex) => ClipInfos(layerIndex, true).Count;

        public bool HasState(int layerIndex, int stateID) => Layer(layerIndex, out var p) && p.FindLayer(stateID, layerIndex, out _) >= 0;

        internal string ResolveHash(int hash) => "";

        // ------------------------------------------------------------------ playing states

        bool Resolve(int stateNameHash, int layer, out AnimatorPlayer player, out int layerIndex, out StateDef state)
        {
            state = null;
            layerIndex = -1;
            player = Player;
            if (player == null) return false;
            layerIndex = player.FindLayer(stateNameHash, layer, out state);
            if (layerIndex >= 0) return true;
            Debug.LogWarning("AnimatorControllerPlayable.GotoState: State could not be found");
            return false;
        }

        public void Play(string stateName) => Play(Animator.StringToHash(stateName), -1, float.NegativeInfinity);

        public void Play(string stateName, int layer) => Play(Animator.StringToHash(stateName), layer, float.NegativeInfinity);

        public void Play(string stateName, int layer, float normalizedTime) => Play(Animator.StringToHash(stateName), layer, normalizedTime);

        public void Play(int stateNameHash) => Play(stateNameHash, -1, float.NegativeInfinity);

        public void Play(int stateNameHash, int layer) => Play(stateNameHash, layer, float.NegativeInfinity);

        public void Play(int stateNameHash, int layer, float normalizedTime)
        {
            if (Resolve(stateNameHash, layer, out var p, out int index, out var state)) p.RequestPlay(index, state, normalizedTime, fixedTime: false);
        }

        public void PlayInFixedTime(string stateName) => PlayInFixedTime(Animator.StringToHash(stateName), -1, float.NegativeInfinity);

        public void PlayInFixedTime(string stateName, int layer) => PlayInFixedTime(Animator.StringToHash(stateName), layer, float.NegativeInfinity);

        public void PlayInFixedTime(string stateName, int layer, float fixedTime) => PlayInFixedTime(Animator.StringToHash(stateName), layer, fixedTime);

        public void PlayInFixedTime(int stateNameHash) => PlayInFixedTime(stateNameHash, -1, float.NegativeInfinity);

        public void PlayInFixedTime(int stateNameHash, int layer) => PlayInFixedTime(stateNameHash, layer, float.NegativeInfinity);

        public void PlayInFixedTime(int stateNameHash, int layer, float fixedTime)
        {
            if (Resolve(stateNameHash, layer, out var p, out int index, out var state)) p.RequestPlay(index, state, fixedTime, fixedTime: true);
        }

        public void CrossFade(string stateName, float transitionDuration) => CrossFade(Animator.StringToHash(stateName), transitionDuration, -1, float.NegativeInfinity);

        public void CrossFade(string stateName, float transitionDuration, int layer) => CrossFade(Animator.StringToHash(stateName), transitionDuration, layer, float.NegativeInfinity);

        public void CrossFade(string stateName, float transitionDuration, int layer, float normalizedTime) => CrossFade(Animator.StringToHash(stateName), transitionDuration, layer, normalizedTime);

        public void CrossFade(int stateNameHash, float transitionDuration) => CrossFade(stateNameHash, transitionDuration, -1, float.NegativeInfinity);

        public void CrossFade(int stateNameHash, float transitionDuration, int layer) => CrossFade(stateNameHash, transitionDuration, layer, float.NegativeInfinity);

        public void CrossFade(int stateNameHash, float transitionDuration, int layer, float normalizedTime)
        {
            if (Resolve(stateNameHash, layer, out var p, out int index, out var state))
                p.RequestCrossFade(index, state, transitionDuration, false, normalizedTime, false, 0f);
        }

        public void CrossFadeInFixedTime(string stateName, float transitionDuration) => CrossFadeInFixedTime(Animator.StringToHash(stateName), transitionDuration, -1, 0f);

        public void CrossFadeInFixedTime(string stateName, float transitionDuration, int layer) => CrossFadeInFixedTime(Animator.StringToHash(stateName), transitionDuration, layer, 0f);

        public void CrossFadeInFixedTime(string stateName, float transitionDuration, int layer, float fixedTime) => CrossFadeInFixedTime(Animator.StringToHash(stateName), transitionDuration, layer, fixedTime);

        public void CrossFadeInFixedTime(int stateNameHash, float transitionDuration) => CrossFadeInFixedTime(stateNameHash, transitionDuration, -1, 0f);

        public void CrossFadeInFixedTime(int stateNameHash, float transitionDuration, int layer) => CrossFadeInFixedTime(stateNameHash, transitionDuration, layer, 0f);

        public void CrossFadeInFixedTime(int stateNameHash, float transitionDuration, int layer, float fixedTime)
        {
            if (Resolve(stateNameHash, layer, out var p, out int index, out var state))
                p.RequestCrossFade(index, state, transitionDuration, true, fixedTime, true, 0f);
        }
    }
}
