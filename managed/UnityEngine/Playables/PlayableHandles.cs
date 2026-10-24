using System;
using System.Globalization;
using Unbound.Runtime.Playables;
using UnityEngine.Internal;

namespace UnityEngine.Playables
{
    /// <summary>A playable graph: creates, connects, evaluates and destroys playables and outputs.</summary>
    public partial struct PlayableGraph
    {
        internal IntPtr m_Handle;
        internal uint m_Version;

        PlayableGraphData Data => PlayableGraphData.Get(this) ?? throw new InvalidOperationException("The PlayableGraph is invalid. It has either been Destroyed or was never created.");

        public static PlayableGraph Create() => Create(null);

        public static PlayableGraph Create(string name) => PlayableGraphData.Create(name).ToGraph();

        public void Destroy() => Data.Destroy();

        public bool IsValid() => PlayableGraphData.Get(this) != null;

        public bool IsPlaying() => PlayableGraphData.Get(this)?.Playing ?? false;

        public bool IsDone() => Data.IsDone();

        public void Play() => Data.Play();

        public void Stop() => Data.Stop();

        public void Evaluate() => Evaluate(0f);

        public void Evaluate([DefaultValue("0")] float deltaTime) => Data.Evaluate(deltaTime);

        public DirectorUpdateMode GetTimeUpdateMode() => Data.UpdateMode;

        public void SetTimeUpdateMode(DirectorUpdateMode value) => Data.UpdateMode = value;

        public IExposedPropertyTable GetResolver() => Data.Resolver;

        public void SetResolver(IExposedPropertyTable value) => Data.Resolver = value;

        public int GetPlayableCount() => Data.Nodes.Count;

        public int GetRootPlayableCount() => Data.Roots.Count;

        public int GetOutputCount() => Data.Outputs.Count;

        public string GetEditorName() => Data.Name;

        public Playable GetRootPlayable(int index) => new Playable(GetRootPlayableInternal(index));

        internal PlayableHandle GetRootPlayableInternal(int index)
        {
            var roots = Data.Roots;
            return index >= 0 && index < roots.Count ? roots[index].ToHandle() : PlayableHandle.Null;
        }

        public bool Connect<U, V>(U source, int sourceOutputPort, V destination, int destinationInputPort)
            where U : struct, IPlayable
            where V : struct, IPlayable =>
            Data.Connect(PlayableGraphData.Get(source.GetHandle()), sourceOutputPort, PlayableGraphData.Get(destination.GetHandle()), destinationInputPort);

        public void Disconnect<U>(U input, int inputPort) where U : struct, IPlayable =>
            Data.DisconnectInput(PlayableGraphData.Get(input.GetHandle()), inputPort, updateRoots: true);

        public void DestroyPlayable<U>(U playable) where U : struct, IPlayable => Data.DestroyNode(PlayableGraphData.Get(playable.GetHandle()));

        public void DestroySubgraph<U>(U playable) where U : struct, IPlayable => Data.DestroySubgraph(PlayableGraphData.Get(playable.GetHandle()));

        public void DestroyOutput<U>(U output) where U : struct, IPlayableOutput => DestroyOutputInternal(output.GetHandle());

        internal void DestroyOutputInternal(PlayableOutputHandle handle) => Data.DestroyOutput(PlayableGraphData.Get(handle));

        public int GetOutputCountByType<T>() where T : struct, IPlayableOutput
        {
            int n = 0;
            foreach (var o in Data.Outputs)
                if (o.OutputType == typeof(T)) n++;
            return n;
        }

        public PlayableOutput GetOutput(int index)
        {
            var outputs = Data.Outputs;
            return index >= 0 && index < outputs.Count ? new PlayableOutput(outputs[index].ToHandle()) : PlayableOutput.Null;
        }

        public PlayableOutput GetOutputByType<T>(int index) where T : struct, IPlayableOutput
        {
            int n = 0;
            foreach (var o in Data.Outputs)
            {
                if (o.OutputType != typeof(T)) continue;
                if (n++ == index) return new PlayableOutput(o.ToHandle());
            }
            return PlayableOutput.Null;
        }

        internal PlayableHandle CreatePlayableHandle() => Data.CreateNode(typeof(Playable)).ToHandle();

        internal bool CreateScriptOutputInternal(string name, out PlayableOutputHandle handle)
        {
            handle = Data.CreateOutput(typeof(ScriptPlayableOutput), name).ToHandle();
            return true;
        }

        /// <summary>Makes this graph evaluate with another one's time (control tracks use it); the
        /// graphs here keep their own clocks.</summary>
        internal void SynchronizeEvaluation(PlayableGraph playable) { }

        internal bool IsMatchFrameRateEnabled() => Data.MatchFrameRate.HasValue;

        internal void EnableMatchFrameRate(FrameRate frameRate) => Data.MatchFrameRate = frameRate;

        internal void DisableMatchFrameRate() => Data.MatchFrameRate = null;

        internal FrameRate GetFrameRate() => Data.MatchFrameRate ?? default;
    }

    /// <summary>A handle to a playable of a graph.</summary>
    public partial struct PlayableHandle : IEquatable<PlayableHandle>
    {
        internal IntPtr m_Handle;
        internal uint m_Version;

        static readonly PlayableHandle m_Null = default;

        public static PlayableHandle Null => m_Null;

        PlayableNode Node => PlayableGraphData.Get(this) ?? throw new InvalidOperationException("The Playable is invalid. It has either been Disposed or was never created.");

        internal T GetObject<T>() where T : class, IPlayableBehaviour => IsValid() ? GetScriptInstance() as T : null;

        internal T GetPayload<T>() where T : struct => IsValid() && GetScriptInstance() is T t ? t : default;

        internal void SetPayload<T>(T payload) where T : struct
        {
            if (IsValid()) Node.Script = payload;
        }

        internal bool IsPlayableOfType<T>() => GetPlayableType() == typeof(T);

        internal Playable GetInput(int inputPort) => new Playable(GetInputHandle(inputPort));

        internal Playable GetOutput(int outputPort) => new Playable(GetOutputHandle(outputPort));

        internal int GetOutputPortFromInputConnection(int inputPort) => GetOutputPortFromInputIndex(inputPort);

        internal int GetInputPortFromOutputConnection(int inputPort) => GetInputPortFromOutputIndex(inputPort);

        internal bool SetInputWeight(int inputIndex, float weight)
        {
            if (!CheckInputBounds(inputIndex)) return false;
            SetInputWeightFromIndex(inputIndex, weight);
            return true;
        }

        internal float GetInputWeight(int inputIndex) => CheckInputBounds(inputIndex) ? GetInputWeightFromIndex(inputIndex) : 0f;

        internal void Destroy() => GetGraph().DestroyPlayable(new Playable(this));

        public static bool operator ==(PlayableHandle x, PlayableHandle y) => CompareVersion(x, y);

        public static bool operator !=(PlayableHandle x, PlayableHandle y) => !CompareVersion(x, y);

        public override bool Equals(object p) => p is PlayableHandle h && Equals(h);

        public bool Equals(PlayableHandle other) => CompareVersion(this, other);

        public override int GetHashCode() => m_Handle.GetHashCode() ^ m_Version.GetHashCode();

        internal static bool CompareVersion(PlayableHandle lhs, PlayableHandle rhs) => lhs.m_Handle == rhs.m_Handle && lhs.m_Version == rhs.m_Version;

        internal bool CheckInputBounds(int inputIndex) => CheckInputBounds(inputIndex, false);

        internal bool CheckInputBounds(int inputIndex, bool acceptAny)
        {
            if (inputIndex == -1 && acceptAny) return true;
            if (inputIndex < 0) throw new IndexOutOfRangeException("Index must be greater than 0");
            if (GetInputCount() <= inputIndex)
                throw new IndexOutOfRangeException("inputIndex " + inputIndex + " is greater than the number of available inputs (" + GetInputCount() + ").");
            return true;
        }

        internal bool IsNull() => m_Handle == IntPtr.Zero;

        internal bool IsValid() => PlayableGraphData.Get(this) != null;

        internal Type GetPlayableType() => PlayableGraphData.Get(this)?.PlayableType;

        internal Type GetJobType() => PlayableGraphData.Get(this)?.JobType;

        internal void SetScriptInstance(object scriptInstance)
        {
            var n = Node;
            n.Script = scriptInstance;
            if (scriptInstance != null) n.PlayableType = scriptInstance.GetType();
            if (scriptInstance is IPlayableBehaviour b)
            {
                var p = new Playable(this);
                PlayableGraphData.Invoke(() => b.OnPlayableCreate(p));
            }
        }

        internal bool CanChangeInputs() => Node.Kind?.CanChangeInputs ?? true;

        internal bool CanSetWeights() => Node.Kind?.CanSetWeights ?? true;

        internal bool CanDestroy() => Node.Kind?.CanDestroy ?? true;

        internal PlayState GetPlayState() => Node.State;

        internal void Play() => Node.State = PlayState.Playing;

        internal void Pause() => Node.State = PlayState.Paused;

        internal double GetSpeed() => Node.Speed;

        internal void SetSpeed(double value) => Node.Speed = value;

        internal double GetTime() => Node.Time;

        internal void SetTime(double value) => SetTimeOf(Node, value, 0);

        static void SetTimeOf(PlayableNode n, double value, int depth)
        {
            n.PreviousTime = n.Time;
            n.Time = value;
            n.TimeSet = true;
            if (!n.PropagateSetTime || depth > 256) return;
            foreach (var i in n.Inputs)
                if (i.Node != null) SetTimeOf(i.Node, value, depth + 1);
        }

        internal bool IsDone() => Node.Done;

        internal void SetDone(bool value) => Node.Done = value;

        internal double GetDuration() => Node.Duration;

        internal void SetDuration(double value) => Node.Duration = value;

        internal bool GetPropagateSetTime() => Node.PropagateSetTime;

        internal void SetPropagateSetTime(bool value) => Node.PropagateSetTime = value;

        internal PlayableGraph GetGraph() => Node.Graph.ToGraph();

        internal int GetInputCount() => Node.Inputs.Count;

        internal int GetOutputPortFromInputIndex(int index)
        {
            var n = Node;
            return index >= 0 && index < n.Inputs.Count && n.Inputs[index].Node != null ? n.Inputs[index].SourcePort : -1;
        }

        internal int GetInputPortFromOutputIndex(int index)
        {
            var n = Node;
            return index >= 0 && index < n.Outputs.Count && n.Outputs[index].Node != null ? n.Outputs[index].InputPort : -1;
        }

        internal void SetInputCount(int value)
        {
            var n = Node;
            n.Graph.SetInputCount(n, value);
        }

        internal int GetOutputCount() => Node.Outputs.Count;

        internal void SetOutputCount(int value)
        {
            var n = Node;
            n.Graph.SetOutputCount(n, value);
        }

        internal void SetInputWeight(PlayableHandle input, float weight)
        {
            var n = Node;
            var source = PlayableGraphData.Get(input);
            for (int i = 0; i < n.Inputs.Count; i++)
            {
                if (n.Inputs[i].Node != source || source == null) continue;
                var port = n.Inputs[i];
                port.Weight = weight;
                n.Inputs[i] = port;
            }
        }

        internal void SetDelay(double delay) => Node.Delay = delay;

        internal double GetDelay() => Node.Delay;

        internal bool IsDelayed() => false;

        internal double GetPreviousTime() => Node.PreviousTime;

        internal void SetLeadTime(float value) => Node.LeadTime = value;

        internal float GetLeadTime() => Node.LeadTime;

        internal PlayableTraversalMode GetTraversalMode() => Node.Traversal;

        internal void SetTraversalMode(PlayableTraversalMode mode) => Node.Traversal = mode;

        internal IntPtr GetJobData() => IntPtr.Zero;

        internal DirectorWrapMode GetTimeWrapMode() => Node.WrapMode;

        internal void SetTimeWrapMode(DirectorWrapMode mode) => Node.WrapMode = mode;

        object GetScriptInstance() => Node.Script;

        PlayableHandle GetInputHandle(int index)
        {
            var n = Node;
            return index >= 0 && index < n.Inputs.Count && n.Inputs[index].Node != null ? n.Inputs[index].Node.ToHandle() : Null;
        }

        PlayableHandle GetOutputHandle(int index)
        {
            var n = Node;
            return index >= 0 && index < n.Outputs.Count && n.Outputs[index].Node != null ? n.Outputs[index].Node.ToHandle() : Null;
        }

        void SetInputWeightFromIndex(int index, float weight)
        {
            var n = Node;
            var port = n.Inputs[index];
            port.Weight = weight;
            n.Inputs[index] = port;
        }

        float GetInputWeightFromIndex(int index) => Node.Inputs[index].Weight;
    }

    /// <summary>A handle to an output of a graph.</summary>
    public partial struct PlayableOutputHandle : IEquatable<PlayableOutputHandle>
    {
        internal IntPtr m_Handle;
        internal uint m_Version;

        static readonly PlayableOutputHandle m_Null = default;

        public static PlayableOutputHandle Null => m_Null;

        PlayableOutputData Output => PlayableGraphData.Get(this) ?? throw new InvalidOperationException("The PlayableOutput is invalid. It has either been Disposed or was never created.");

        internal bool IsPlayableOutputOfType<T>() => GetPlayableOutputType() == typeof(T);

        public override int GetHashCode() => m_Handle.GetHashCode() ^ m_Version.GetHashCode();

        public static bool operator ==(PlayableOutputHandle lhs, PlayableOutputHandle rhs) => CompareVersion(lhs, rhs);

        public static bool operator !=(PlayableOutputHandle lhs, PlayableOutputHandle rhs) => !CompareVersion(lhs, rhs);

        public override bool Equals(object p) => p is PlayableOutputHandle h && Equals(h);

        public bool Equals(PlayableOutputHandle other) => CompareVersion(this, other);

        internal static bool CompareVersion(PlayableOutputHandle lhs, PlayableOutputHandle rhs) => lhs.m_Handle == rhs.m_Handle && lhs.m_Version == rhs.m_Version;

        internal bool IsNull() => m_Handle == IntPtr.Zero;

        internal bool IsValid() => PlayableGraphData.Get(this) != null;

        internal Type GetPlayableOutputType() => PlayableGraphData.Get(this)?.OutputType;

        internal Object GetReferenceObject() => Output.ReferenceObject;

        internal void SetReferenceObject(Object target) => Output.ReferenceObject = target;

        internal Object GetUserData() => Output.UserData;

        internal void SetUserData(Object target) => Output.UserData = target;

        internal PlayableHandle GetSourcePlayable() => Output.Source?.ToHandle() ?? PlayableHandle.Null;

        internal void SetSourcePlayable(PlayableHandle target, int port)
        {
            var o = Output;
            var n = PlayableGraphData.Get(target);
            if (n != null && n.Graph != o.Graph)
            {
                Debug.LogError("Cannot set the source of an output to a playable of another graph.");
                return;
            }
            o.Source = n;
            o.SourcePort = port;
        }

        internal int GetSourceOutputPort() => Output.SourcePort;

        internal float GetWeight() => Output.Weight;

        internal void SetWeight(float weight) => Output.Weight = weight;

        internal void PushNotification(PlayableHandle origin, INotification notification, object context) =>
            Output.Pending.Add((origin, notification, context));

        internal INotificationReceiver[] GetNotificationReceivers() => Output.Receivers.ToArray();

        internal void AddNotificationReceiver(INotificationReceiver receiver)
        {
            var o = Output;
            if (receiver != null && !o.Receivers.Contains(receiver)) o.Receivers.Add(receiver);
        }

        internal void RemoveNotificationReceiver(INotificationReceiver receiver) => Output.Receivers.Remove(receiver);

        internal string GetEditorName() => Output.Name;
    }

    /// <summary>A playable: the generic handle wrapper.</summary>
    public partial struct Playable : IPlayable, IEquatable<Playable>
    {
        PlayableHandle m_Handle;

        static readonly Playable m_NullPlayable = new Playable(PlayableHandle.Null);

        public static Playable Null => m_NullPlayable;

        public static Playable Create(PlayableGraph graph, int inputCount = 0)
        {
            var playable = new Playable(graph.CreatePlayableHandle());
            playable.SetInputCount(inputCount);
            return playable;
        }

        internal Playable(PlayableHandle handle)
        {
            m_Handle = handle;
        }

        public PlayableHandle GetHandle() => m_Handle;

        public bool IsPlayableOfType<T>() where T : struct, IPlayable => GetHandle().IsPlayableOfType<T>();

        public Type GetPlayableType() => GetHandle().GetPlayableType();

        public bool Equals(Playable other) => GetHandle() == other.GetHandle();
    }

    /// <summary>An output: the generic handle wrapper.</summary>
    public partial struct PlayableOutput : IPlayableOutput, IEquatable<PlayableOutput>
    {
        PlayableOutputHandle m_Handle;

        static readonly PlayableOutput m_NullPlayableOutput = new PlayableOutput(PlayableOutputHandle.Null);

        public static PlayableOutput Null => m_NullPlayableOutput;

        internal PlayableOutput(PlayableOutputHandle handle)
        {
            m_Handle = handle;
        }

        public PlayableOutputHandle GetHandle() => m_Handle;

        public bool IsPlayableOutputOfType<T>() where T : struct, IPlayableOutput => GetHandle().IsPlayableOutputOfType<T>();

        public Type GetPlayableOutputType() => GetHandle().GetPlayableOutputType();

        public bool Equals(PlayableOutput other) => GetHandle() == other.GetHandle();
    }

    /// <summary>A playable whose behaviour is a PlayableBehaviour script.</summary>
    public partial struct ScriptPlayable<T> : IPlayable, IEquatable<ScriptPlayable<T>> where T : class, IPlayableBehaviour, new()
    {
        PlayableHandle m_Handle;

        static readonly ScriptPlayable<T> m_NullPlayable = new ScriptPlayable<T>(PlayableHandle.Null);

        public static ScriptPlayable<T> Null => m_NullPlayable;

        public static ScriptPlayable<T> Create(PlayableGraph graph, int inputCount = 0) => new ScriptPlayable<T>(CreateHandle(graph, null, inputCount));

        public static ScriptPlayable<T> Create(PlayableGraph graph, T template, int inputCount = 0) => new ScriptPlayable<T>(CreateHandle(graph, template, inputCount));

        static PlayableHandle CreateHandle(PlayableGraph graph, T template, int inputCount)
        {
            object instance = template != null ? CloneScriptInstance(template) : CreateScriptInstance();
            if (instance == null)
            {
                Debug.LogError("Could not create a ScriptPlayable of Type " + typeof(T));
                return PlayableHandle.Null;
            }
            var handle = graph.CreatePlayableHandle();
            if (!handle.IsValid()) return PlayableHandle.Null;
            handle.SetInputCount(inputCount);
            handle.SetScriptInstance(instance);
            return handle;
        }

        static object CreateScriptInstance()
        {
            if (typeof(ScriptableObject).IsAssignableFrom(typeof(T))) return ScriptableObject.CreateInstance(typeof(T)) as T;
            return new T();
        }

        static object CloneScriptInstance(IPlayableBehaviour source)
        {
            if (source is Object o && o != null)
            {
                var clone = Object.Instantiate(o);
                if (clone != null) clone.hideFlags |= HideFlags.DontSave;
                return clone;
            }
            if (source is ICloneable cloneable) return cloneable.Clone();
            return null;
        }

        internal ScriptPlayable(PlayableHandle handle)
        {
            if (handle.IsValid() && !typeof(T).IsAssignableFrom(handle.GetPlayableType()))
                throw new InvalidCastException($"Incompatible handle: Trying to assign a playable data of type `{handle.GetPlayableType()}` that is not compatible with the PlayableBehaviour of type `{typeof(T)}`.");
            m_Handle = handle;
        }

        public PlayableHandle GetHandle() => m_Handle;

        public T GetBehaviour() => m_Handle.GetObject<T>();

        public static implicit operator Playable(ScriptPlayable<T> playable) => new Playable(playable.GetHandle());

        public static explicit operator ScriptPlayable<T>(Playable playable) => new ScriptPlayable<T>(playable.GetHandle());

        public bool Equals(ScriptPlayable<T> other) => GetHandle() == other.GetHandle();
    }

    /// <summary>An output whose playables' ProcessFrame receives its user data.</summary>
    public partial struct ScriptPlayableOutput : IPlayableOutput
    {
        PlayableOutputHandle m_Handle;

        public static ScriptPlayableOutput Null => new ScriptPlayableOutput(PlayableOutputHandle.Null);

        public static ScriptPlayableOutput Create(PlayableGraph graph, string name)
        {
            if (!graph.CreateScriptOutputInternal(name, out var handle)) return Null;
            return new ScriptPlayableOutput(handle);
        }

        internal ScriptPlayableOutput(PlayableOutputHandle handle)
        {
            if (handle.IsValid() && !handle.IsPlayableOutputOfType<ScriptPlayableOutput>())
                throw new InvalidCastException("Can't set handle: the playable is not a ScriptPlayableOutput.");
            m_Handle = handle;
        }

        public PlayableOutputHandle GetHandle() => m_Handle;

        public static implicit operator PlayableOutput(ScriptPlayableOutput output) => new PlayableOutput(output.GetHandle());

        public static explicit operator ScriptPlayableOutput(PlayableOutput output) => new ScriptPlayableOutput(output.GetHandle());
    }

    public static partial class ScriptPlayableBinding
    {
        public static PlayableBinding Create(string name, Object key, Type type) => PlayableBinding.CreateInternal(name, key, type, CreateScriptOutput);

        static PlayableOutput CreateScriptOutput(PlayableGraph graph, string name) => ScriptPlayableOutput.Create(graph, name);
    }

    /// <summary>A stream a playable asset outputs: its name, key object, target type and how to make its output.</summary>
    public partial struct PlayableBinding
    {
        internal delegate PlayableOutput CreateOutputMethod(PlayableGraph graph, string name);

        string m_StreamName;
        Object m_SourceObject;
        Type m_SourceBindingType;
        CreateOutputMethod m_CreateOutputMethod;

        public static readonly PlayableBinding[] None = new PlayableBinding[0];
        public static readonly double DefaultDuration = double.PositiveInfinity;

        public string streamName
        {
            get => m_StreamName;
            set => m_StreamName = value;
        }

        public Object sourceObject
        {
            get => m_SourceObject;
            set => m_SourceObject = value;
        }

        public Type outputTargetType => m_SourceBindingType;

        [Obsolete("sourceBindingType is no longer supported on PlayableBinding. Use outputBindingType instead to get the required output target type, and the appropriate binding create method (e.g. AnimationPlayableBinding.Create(name, key)) to create PlayableBindings")]
        public Type sourceBindingType
        {
            get => m_SourceBindingType;
            set { }
        }

        [Obsolete("streamType is no longer supported on PlayableBinding. Use the appropriate binding create method (e.g. AnimationPlayableBinding.Create(name, key)) instead.")]
        public DataStreamType streamType
        {
            get => DataStreamType.None;
            set { }
        }

        internal PlayableOutput CreateOutput(PlayableGraph graph) => m_CreateOutputMethod != null ? m_CreateOutputMethod(graph, m_StreamName) : PlayableOutput.Null;

        internal static PlayableBinding CreateInternal(string name, Object sourceObject, Type sourceType, CreateOutputMethod createFunction) =>
            new PlayableBinding { m_StreamName = name, m_SourceObject = sourceObject, m_SourceBindingType = sourceType, m_CreateOutputMethod = createFunction };
    }

    /// <summary>The frame information a playable receives in its callbacks.</summary>
    public partial struct FrameData
    {
        [Flags]
        internal enum Flags
        {
            Evaluate = 1,
            SeekOccured = 2,
            Loop = 4,
            Hold = 8,
            EffectivePlayStateDelayed = 0x10,
            EffectivePlayStatePlaying = 0x20,
        }

        public enum EvaluationType
        {
            Evaluate,
            Playback,
        }

        internal ulong m_FrameID;
        internal double m_DeltaTime;
        internal float m_Weight;
        internal float m_EffectiveWeight;
        internal double m_EffectiveParentDelay;
        internal float m_EffectiveParentSpeed;
        internal float m_EffectiveSpeed;
        internal Flags m_Flags;
        internal PlayableOutput m_Output;

        public ulong frameId => m_FrameID;

        public float deltaTime => (float)m_DeltaTime;

        public float weight => m_Weight;

        public float effectiveWeight => m_EffectiveWeight;

        [Obsolete("effectiveParentDelay is obsolete; use a custom ScriptPlayable to implement this feature")]
        public double effectiveParentDelay => m_EffectiveParentDelay;

        public float effectiveParentSpeed => m_EffectiveParentSpeed;

        public float effectiveSpeed => m_EffectiveSpeed;

        public EvaluationType evaluationType => HasFlags(Flags.Evaluate) ? EvaluationType.Evaluate : EvaluationType.Playback;

        public bool seekOccurred => HasFlags(Flags.SeekOccured);

        public bool timeLooped => HasFlags(Flags.Loop);

        public bool timeHeld => HasFlags(Flags.Hold);

        public PlayableOutput output => m_Output;

        public PlayState effectivePlayState =>
            HasFlags(Flags.EffectivePlayStateDelayed) ? PlayState.Delayed : HasFlags(Flags.EffectivePlayStatePlaying) ? PlayState.Playing : PlayState.Paused;

        bool HasFlags(Flags flag) => (m_Flags & flag) == flag;
    }

    /// <summary>A frame rate, negative for the NTSC drop-frame rates.</summary>
    internal partial struct FrameRate : IEquatable<FrameRate>
    {
        public static readonly FrameRate k_24Fps = new FrameRate(24u);
        public static readonly FrameRate k_23_976Fps = new FrameRate(24u, drop: true);
        public static readonly FrameRate k_25Fps = new FrameRate(25u);
        public static readonly FrameRate k_30Fps = new FrameRate(30u);
        public static readonly FrameRate k_29_97Fps = new FrameRate(30u, drop: true);
        public static readonly FrameRate k_50Fps = new FrameRate(50u);
        public static readonly FrameRate k_60Fps = new FrameRate(60u);
        public static readonly FrameRate k_59_94Fps = new FrameRate(60u, drop: true);

        int m_Rate;

        public bool dropFrame => m_Rate < 0;

        public double rate => dropFrame ? -m_Rate * 0.999000999000999 : m_Rate;

        public FrameRate(uint frameRate = 0u, bool drop = false)
        {
            m_Rate = (drop ? -1 : 1) * (int)frameRate;
        }

        public bool IsValid() => m_Rate != 0;

        public bool Equals(FrameRate other) => m_Rate == other.m_Rate;

        public override bool Equals(object obj) => obj is FrameRate f && Equals(f);

        public static bool operator ==(FrameRate a, FrameRate b) => a.Equals(b);

        public static bool operator !=(FrameRate a, FrameRate b) => !a.Equals(b);

        public static bool operator <(FrameRate a, FrameRate b) => a.rate < b.rate;

        public static bool operator <=(FrameRate a, FrameRate b) => a.rate <= b.rate;

        public static bool operator >(FrameRate a, FrameRate b) => a.rate > b.rate;

        // Unity's own comparison (sic).
        public static bool operator >=(FrameRate a, FrameRate b) => a.rate <= b.rate;

        public override int GetHashCode() => m_Rate;

        public override string ToString() => ToString(null, null);

        public string ToString(string format) => ToString(format, null);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            if (string.IsNullOrEmpty(format)) format = dropFrame ? "F2" : "F0";
            formatProvider ??= CultureInfo.InvariantCulture.NumberFormat;
            return string.Format(CultureInfo.InvariantCulture, "{0} Fps", rate.ToString(format, formatProvider));
        }

        internal static int FrameRateToInt(FrameRate framerate) => framerate.m_Rate;

        internal static FrameRate DoubleToFrameRate(double framerate)
        {
            uint n = (uint)Math.Ceiling(framerate);
            if (n == 0) return new FrameRate(1u);
            var drop = new FrameRate(n, drop: true);
            if (Math.Abs(framerate - drop.rate) < Math.Abs(framerate - n)) return drop;
            return new FrameRate(n);
        }
    }

    /// <summary>A notification with a name.</summary>
    public partial class Notification : INotification
    {
        public PropertyName id { get; }

        public Notification(string name)
        {
            id = new PropertyName(name);
        }
    }
}
