using System;
using System.Collections.Generic;

namespace UnityEngine.Playables
{
    public static partial class PlayableExtensions
    {
        public static bool IsNull<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().IsNull();

        public static bool IsValid<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().IsValid();

        public static void Destroy<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().Destroy();

        public static PlayableGraph GetGraph<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().GetGraph();

        [Obsolete("SetPlayState() has been deprecated. Use Play(), Pause() or SetDelay() instead")]
        public static void SetPlayState<U>(this U playable, PlayState value) where U : struct, IPlayable
        {
            if (value == PlayState.Delayed) throw new ArgumentException("Can't set Delayed: use SetDelay() instead");
            if (value == PlayState.Playing) playable.GetHandle().Play();
            else playable.GetHandle().Pause();
        }

        public static PlayState GetPlayState<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().GetPlayState();

        public static void Play<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().Play();

        public static void Pause<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().Pause();

        public static void SetSpeed<U>(this U playable, double value) where U : struct, IPlayable => playable.GetHandle().SetSpeed(value);

        public static double GetSpeed<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().GetSpeed();

        public static void SetDuration<U>(this U playable, double value) where U : struct, IPlayable => playable.GetHandle().SetDuration(value);

        public static double GetDuration<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().GetDuration();

        public static void SetTime<U>(this U playable, double value) where U : struct, IPlayable => playable.GetHandle().SetTime(value);

        public static double GetTime<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().GetTime();

        public static double GetPreviousTime<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().GetPreviousTime();

        public static void SetDone<U>(this U playable, bool value) where U : struct, IPlayable => playable.GetHandle().SetDone(value);

        public static bool IsDone<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().IsDone();

        public static void SetPropagateSetTime<U>(this U playable, bool value) where U : struct, IPlayable => playable.GetHandle().SetPropagateSetTime(value);

        public static bool GetPropagateSetTime<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().GetPropagateSetTime();

        public static bool CanChangeInputs<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().CanChangeInputs();

        public static bool CanSetWeights<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().CanSetWeights();

        public static bool CanDestroy<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().CanDestroy();

        public static void SetInputCount<U>(this U playable, int value) where U : struct, IPlayable => playable.GetHandle().SetInputCount(value);

        public static int GetInputCount<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().GetInputCount();

        public static void SetOutputCount<U>(this U playable, int value) where U : struct, IPlayable => playable.GetHandle().SetOutputCount(value);

        public static int GetOutputCount<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().GetOutputCount();

        public static Playable GetInput<U>(this U playable, int inputPort) where U : struct, IPlayable => playable.GetHandle().GetInput(inputPort);

        public static Playable GetOutput<U>(this U playable, int outputPort) where U : struct, IPlayable => playable.GetHandle().GetOutput(outputPort);

        public static void SetInputWeight<U>(this U playable, int inputIndex, float weight) where U : struct, IPlayable => playable.GetHandle().SetInputWeight(inputIndex, weight);

        public static void SetInputWeight<U, V>(this U playable, V input, float weight)
            where U : struct, IPlayable
            where V : struct, IPlayable =>
            playable.GetHandle().SetInputWeight(input.GetHandle(), weight);

        public static float GetInputWeight<U>(this U playable, int inputIndex) where U : struct, IPlayable => playable.GetHandle().GetInputWeight(inputIndex);

        public static void ConnectInput<U, V>(this U playable, int inputIndex, V sourcePlayable, int sourceOutputIndex)
            where U : struct, IPlayable
            where V : struct, IPlayable =>
            playable.ConnectInput(inputIndex, sourcePlayable, sourceOutputIndex, 0f);

        public static void ConnectInput<U, V>(this U playable, int inputIndex, V sourcePlayable, int sourceOutputIndex, float weight)
            where U : struct, IPlayable
            where V : struct, IPlayable
        {
            playable.GetGraph().Connect(sourcePlayable, sourceOutputIndex, playable, inputIndex);
            playable.SetInputWeight(inputIndex, weight);
        }

        public static void DisconnectInput<U>(this U playable, int inputPort) where U : struct, IPlayable => playable.GetGraph().Disconnect(playable, inputPort);

        public static int AddInput<U, V>(this U playable, V sourcePlayable, int sourceOutputIndex, float weight = 0f)
            where U : struct, IPlayable
            where V : struct, IPlayable
        {
            int inputCount = playable.GetInputCount();
            playable.SetInputCount(inputCount + 1);
            playable.ConnectInput(inputCount, sourcePlayable, sourceOutputIndex, weight);
            return inputCount;
        }

        [Obsolete("SetDelay is obsolete; use a custom ScriptPlayable to implement this feature")]
        public static void SetDelay<U>(this U playable, double delay) where U : struct, IPlayable => playable.GetHandle().SetDelay(delay);

        [Obsolete("GetDelay is obsolete; use a custom ScriptPlayable to implement this feature")]
        public static double GetDelay<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().GetDelay();

        [Obsolete("IsDelayed is obsolete; use a custom ScriptPlayable to implement this feature")]
        public static bool IsDelayed<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().IsDelayed();

        public static void SetLeadTime<U>(this U playable, float value) where U : struct, IPlayable => playable.GetHandle().SetLeadTime(value);

        public static float GetLeadTime<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().GetLeadTime();

        public static PlayableTraversalMode GetTraversalMode<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().GetTraversalMode();

        public static void SetTraversalMode<U>(this U playable, PlayableTraversalMode mode) where U : struct, IPlayable => playable.GetHandle().SetTraversalMode(mode);

        internal static DirectorWrapMode GetTimeWrapMode<U>(this U playable) where U : struct, IPlayable => playable.GetHandle().GetTimeWrapMode();

        internal static void SetTimeWrapMode<U>(this U playable, DirectorWrapMode value) where U : struct, IPlayable => playable.GetHandle().SetTimeWrapMode(value);

        internal static int GetOutputPortFromInputConnection<U>(this U playable, int inputIndex) where U : struct, IPlayable =>
            playable.GetHandle().GetOutputPortFromInputConnection(inputIndex);

        internal static int GetInputPortFromOutputConnection<U>(this U playable, int outputIndex) where U : struct, IPlayable =>
            playable.GetHandle().GetInputPortFromOutputConnection(outputIndex);
    }

    public static partial class PlayableOutputExtensions
    {
        public static bool IsOutputNull<U>(this U output) where U : struct, IPlayableOutput => output.GetHandle().IsNull();

        public static bool IsOutputValid<U>(this U output) where U : struct, IPlayableOutput => output.GetHandle().IsValid();

        public static Object GetReferenceObject<U>(this U output) where U : struct, IPlayableOutput => output.GetHandle().GetReferenceObject();

        public static void SetReferenceObject<U>(this U output, Object value) where U : struct, IPlayableOutput => output.GetHandle().SetReferenceObject(value);

        public static Object GetUserData<U>(this U output) where U : struct, IPlayableOutput => output.GetHandle().GetUserData();

        public static void SetUserData<U>(this U output, Object value) where U : struct, IPlayableOutput => output.GetHandle().SetUserData(value);

        public static Playable GetSourcePlayable<U>(this U output) where U : struct, IPlayableOutput => new Playable(output.GetHandle().GetSourcePlayable());

        public static void SetSourcePlayable<U, V>(this U output, V value)
            where U : struct, IPlayableOutput
            where V : struct, IPlayable =>
            output.GetHandle().SetSourcePlayable(value.GetHandle(), output.GetSourceOutputPort());

        public static void SetSourcePlayable<U, V>(this U output, V value, int port)
            where U : struct, IPlayableOutput
            where V : struct, IPlayable =>
            output.GetHandle().SetSourcePlayable(value.GetHandle(), port);

        public static int GetSourceOutputPort<U>(this U output) where U : struct, IPlayableOutput => output.GetHandle().GetSourceOutputPort();

        public static float GetWeight<U>(this U output) where U : struct, IPlayableOutput => output.GetHandle().GetWeight();

        public static void SetWeight<U>(this U output, float value) where U : struct, IPlayableOutput => output.GetHandle().SetWeight(value);

        public static void PushNotification<U>(this U output, Playable origin, INotification notification, object context = null) where U : struct, IPlayableOutput =>
            output.GetHandle().PushNotification(origin.GetHandle(), notification, context);

        public static INotificationReceiver[] GetNotificationReceivers<U>(this U output) where U : struct, IPlayableOutput => output.GetHandle().GetNotificationReceivers();

        public static void AddNotificationReceiver<U>(this U output, INotificationReceiver receiver) where U : struct, IPlayableOutput => output.GetHandle().AddNotificationReceiver(receiver);

        public static void RemoveNotificationReceiver<U>(this U output, INotificationReceiver receiver) where U : struct, IPlayableOutput => output.GetHandle().RemoveNotificationReceiver(receiver);

        [Obsolete("Method GetSourceInputPort has been renamed to GetSourceOutputPort (UnityUpgradable) -> GetSourceOutputPort<U>(*)")]
        public static int GetSourceInputPort<U>(this U output) where U : struct, IPlayableOutput => output.GetHandle().GetSourceOutputPort();

        [Obsolete("Method SetSourceInputPort has been deprecated. Use SetSourcePlayable(Playable, Port) instead.")]
        public static void SetSourceInputPort<U>(this U output, int value) where U : struct, IPlayableOutput => output.SetSourcePlayable(output.GetSourcePlayable(), value);

        [Obsolete("Method SetSourceOutputPort has been deprecated. Use SetSourcePlayable(Playable, Port) instead.")]
        public static void SetSourceOutputPort<U>(this U output, int value) where U : struct, IPlayableOutput => output.SetSourcePlayable(output.GetSourcePlayable(), value);
    }

    /// <summary>The base of playable scripts; the graph calls these as it plays.</summary>
    [Serializable]
    [Unbound.EditorFaithful("overridable callbacks: Unity's base implementations are empty too")]
    public abstract partial class PlayableBehaviour : IPlayableBehaviour, ICloneable
    {
        public PlayableBehaviour() { }

        public virtual void OnGraphStart(Playable playable) { }

        public virtual void OnGraphStop(Playable playable) { }

        public virtual void OnPlayableCreate(Playable playable) { }

        public virtual void OnPlayableDestroy(Playable playable) { }

        [Obsolete("OnBehaviourDelay is obsolete; use a custom ScriptPlayable to implement this feature")]
        public virtual void OnBehaviourDelay(Playable playable, FrameData info) { }

        public virtual void OnBehaviourPlay(Playable playable, FrameData info) { }

        public virtual void OnBehaviourPause(Playable playable, FrameData info) { }

        public virtual void PrepareData(Playable playable, FrameData info) { }

        public virtual void PrepareFrame(Playable playable, FrameData info) { }

        public virtual void ProcessFrame(Playable playable, FrameData info, object playerData) { }

        public virtual object Clone() => MemberwiseClone();
    }

    /// <summary>An asset that makes playables (a timeline).</summary>
    [Serializable]
    public abstract partial class PlayableAsset : ScriptableObject, IPlayableAsset
    {
        protected PlayableAsset() { }

        public virtual double duration => PlayableBinding.DefaultDuration;

        [Unbound.EditorFaithful("Unity's base answer: an asset declares no outputs unless it overrides this")]
        public virtual IEnumerable<PlayableBinding> outputs => PlayableBinding.None;

        public abstract Playable CreatePlayable(PlayableGraph graph, GameObject owner);

        internal static unsafe void Internal_CreatePlayable(PlayableAsset asset, PlayableGraph graph, GameObject go, IntPtr ptr)
        {
            var playable = asset == null ? Playable.Null : asset.CreatePlayable(graph, go);
            *(Playable*)ptr.ToPointer() = playable;
        }

        internal static unsafe void Internal_GetPlayableAssetDuration(PlayableAsset asset, IntPtr ptrToDouble)
        {
            *(double*)ptrToDouble.ToPointer() = asset.duration;
        }
    }
}
