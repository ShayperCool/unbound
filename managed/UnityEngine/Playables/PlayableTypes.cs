using System;
using System.Collections.Generic;

namespace UnityEngine
{
    /// <summary>A table of scene objects by name that assets resolve exposed references against.</summary>
    public partial interface IExposedPropertyTable
    {
        void SetReferenceValue(PropertyName id, Object value);

        Object GetReferenceValue(PropertyName id, out bool idValid);

        void ClearReferenceValue(PropertyName id);
    }
}

namespace UnityEngine.Playables
{
    public partial interface IPlayable
    {
        PlayableHandle GetHandle();
    }

    public partial interface IPlayableOutput
    {
        PlayableOutputHandle GetHandle();
    }

    public partial interface IPlayableBehaviour
    {
        void OnGraphStart(Playable playable);

        void OnGraphStop(Playable playable);

        void OnPlayableCreate(Playable playable);

        void OnPlayableDestroy(Playable playable);

        void OnBehaviourPlay(Playable playable, FrameData info);

        void OnBehaviourPause(Playable playable, FrameData info);

        void PrepareFrame(Playable playable, FrameData info);

        void ProcessFrame(Playable playable, FrameData info, object playerData);
    }

    public partial interface IPlayableAsset
    {
        double duration { get; }

        IEnumerable<PlayableBinding> outputs { get; }

        Playable CreatePlayable(PlayableGraph graph, GameObject owner);
    }

    public partial interface INotification
    {
        PropertyName id { get; }
    }

    public partial interface INotificationReceiver
    {
        void OnNotify(Playable origin, INotification notification, object context);
    }

    public enum PlayState
    {
        Paused,
        Playing,
        [Obsolete("Delayed is obsolete; use a custom ScriptPlayable to implement this feature", false)]
        Delayed,
    }

    public enum DirectorWrapMode
    {
        Hold,
        Loop,
        None,
    }

    public enum DirectorUpdateMode
    {
        DSPClock,
        GameTime,
        UnscaledGameTime,
        Manual,
    }

    public enum PlayableTraversalMode
    {
        Mix,
        Passthrough,
    }

    [Obsolete("DataStreamType is no longer required and will be removed in a future release.", false)]
    public enum DataStreamType
    {
        Animation,
        Audio,
        Texture,
        None,
    }
}

namespace UnityEngine.Experimental.Animations
{
    public enum AnimationStreamSource
    {
        DefaultValues,
        PreviousInputs,
    }
}
