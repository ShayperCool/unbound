using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>
    /// An animator controller as the runtime sees it. A `.controller` file's AnimatorController
    /// (class 91) loads as the Editor's UnityEditor.Animations.AnimatorController, which derives from
    /// it (Unity 6000.0.71f1, tests/projects/gameinterop editor_controller.type); its layers, state
    /// machines, states, transitions and blend trees are the file's other documents, compiled into a
    /// graph on first use.
    /// </summary>
    [ClassId(93)]
    [global::UnityEngine.@ExcludeFromObjectFactoryAttribute] public partial class RuntimeAnimatorController : Object
    {
        internal List<AnimatorControllerParameter> m_Parameters = new List<AnimatorControllerParameter>();
        internal List<ControllerLayerData> m_Layers = new List<ControllerLayerData>();
        internal ControllerGraph m_Graph;

        protected RuntimeAnimatorController() { }

        /// <summary>The clips the controller plays, after overrides. The measured profile keeps
        /// repeated state references; playback binding still uses the graph's unique clips.</summary>
        public AnimationClip[] animationClips
        {
            get
            {
                var graph = Graph;
                if (graph == null) return Array.Empty<AnimationClip>();
                var result = new List<AnimationClip>();
                bool occurrences = Compat.AssetCloneResourcesMeasured;
                foreach (var original in occurrences ? graph.ClipReferences : graph.Clips)
                {
                    var clip = MapClip(original);
                    if (clip != null && (occurrences || !result.Contains(clip))) result.Add(clip);
                }
                return result.ToArray();
            }
        }

        /// <summary>The compiled state machines (of the base controller, for an override controller).</summary>
        internal virtual ControllerGraph Graph => m_Graph ??= ControllerGraph.Build(this);

        /// <summary>Whether layer <paramref name="index"/> is a synced layer with "Timing" on
        /// (AnimatorControllerLayer.syncedLayerAffectsTiming). The flag of a layer that is not
        /// synced does nothing.</summary>
        internal virtual bool SyncedLayerAffectsTiming(int index) =>
            index >= 0 && index < m_Layers.Count && m_Layers[index].SyncedLayerIndex >= 0 && m_Layers[index].SyncedLayerAffectsTiming;

        /// <summary>The clip played in place of an original clip of the graph.</summary>
        internal virtual AnimationClip MapClip(AnimationClip original) => original;

        /// <summary>Changes when overrides change: Animators rebind their clips.</summary>
        internal virtual int OverrideVersion => 0;

        internal void ReadSerialized(YNode body, IReferenceResolver resolver) => ReadAsset(body, resolver);

        internal virtual void ReadAsset(YNode body, IReferenceResolver resolver)
        {
            if (body.Get("m_Controller") != null)
            {
                // Player form (an AssetBundle's): the compiled controller.
                CompiledController.Read(this, body, resolver);
                m_Graph = null;
                return;
            }
            m_Parameters.Clear();
            foreach (var p in body.Get("m_AnimatorParameters")?.Seq() ?? Array.Empty<YNode>())
            {
                m_Parameters.Add(new AnimatorControllerParameter
                {
                    m_Name = p.Str("m_Name"),
                    m_Type = (AnimatorControllerParameterType)p.Int("m_Type", 1),
                    m_DefaultFloat = p.Float("m_DefaultFloat"),
                    m_DefaultInt = p.Int("m_DefaultInt"),
                    m_DefaultBool = p.Bool("m_DefaultBool"),
                });
            }
            m_Layers.Clear();
            foreach (var l in body.Get("m_AnimatorLayers")?.Seq() ?? Array.Empty<YNode>())
            {
                var layer = new ControllerLayerData
                {
                    Name = l.Str("m_Name"),
                    StateMachine = Resolve<UnityEditor.Animations.AnimatorStateMachine>(resolver, l.Ref("m_StateMachine")),
                    Mask = Resolve<AvatarMask>(resolver, l.Ref("m_Mask")),
                    BlendingMode = l.Int("m_BlendingMode"),
                    SyncedLayerIndex = l.Int("m_SyncedLayerIndex", -1),
                    DefaultWeight = l.Float("m_DefaultWeight"),
                    SyncedLayerAffectsTiming = l.Bool("m_SyncedLayerAffectsTiming"),
                    IKPass = l.Bool("m_IKPass"),
                };
                foreach (var pair in l.Get("m_Motions")?.Seq() ?? Array.Empty<YNode>())
                {
                    var state = Resolve<UnityEditor.Animations.AnimatorState>(resolver, pair.Ref("m_State"));
                    if (state != null) layer.SyncedMotions.Add((state, Resolve<Motion>(resolver, pair.Ref("m_Motion"))));
                }
                foreach (var pair in l.Get("m_Behaviours")?.Seq() ?? Array.Empty<YNode>())
                {
                    var state = Resolve<UnityEditor.Animations.AnimatorState>(resolver, pair.Ref("m_State"));
                    if (state == null) continue;
                    var list = new List<StateMachineBehaviour>();
                    foreach (var b in pair.Get("m_StateMachineBehaviours")?.Seq() ?? Array.Empty<YNode>())
                        if (Resolve<StateMachineBehaviour>(resolver, b.AsRef()) is StateMachineBehaviour smb) list.Add(smb);
                    layer.SyncedBehaviours.Add((state, list));
                }
                m_Layers.Add(layer);
            }
            m_Graph = null;
        }

        /// <summary>Instantiate: a controller with the same layers and parameters (it compiles its own graph).</summary>
        internal virtual RuntimeAnimatorController CloneController() => new UnityEditor.Animations.AnimatorController
        {
            m_Parameters = new List<AnimatorControllerParameter>(m_Parameters),
            m_Layers = new List<ControllerLayerData>(m_Layers),
        };

        internal Object CloneObject() => CloneController();

        internal static T Resolve<T>(IReferenceResolver resolver, ObjectRef reference) where T : Object =>
            reference.IsNull ? null : resolver.Resolve(reference, typeof(T)) as T;
    }

    /// <summary>A controller that plays another controller's state machines with some clips replaced.</summary>
    [global::UnityEngine.@HelpURLAttribute("AnimatorOverrideController")] [ClassId(221)]
    public partial class AnimatorOverrideController : RuntimeAnimatorController
    {
        internal RuntimeAnimatorController m_Controller;
        internal readonly Dictionary<AnimationClip, AnimationClip> m_Overrides = new Dictionary<AnimationClip, AnimationClip>();
        int m_Version;

        public AnimatorOverrideController() { }

        public AnimatorOverrideController(RuntimeAnimatorController controller)
        {
            runtimeAnimatorController = controller;
        }

        public RuntimeAnimatorController runtimeAnimatorController
        {
            get => m_Controller;
            set
            {
                if (value == this)
                {
                    Debug.LogError("AnimatorOverrideController cannot override itself.");
                    return;
                }
                m_Controller = value;
                m_Version++;
            }
        }

        internal override ControllerGraph Graph => m_Controller != null ? m_Controller.Graph : null;

        /// <summary>The overridden controller's layers: an override replaces clips only.</summary>
        internal override bool SyncedLayerAffectsTiming(int index) => m_Controller != null && m_Controller.SyncedLayerAffectsTiming(index);

        internal override AnimationClip MapClip(AnimationClip original)
        {
            var mapped = m_Controller != null ? m_Controller.MapClip(original) : original;
            return original != null && m_Overrides.TryGetValue(original, out var replacement) && replacement != null ? replacement : mapped;
        }

        internal override int OverrideVersion => m_Version + (m_Controller != null ? m_Controller.OverrideVersion : 0);

        /// <summary>The original clips of the overridden controller.</summary>
        List<AnimationClip> Originals()
        {
            var result = new List<AnimationClip>();
            var graph = Graph;
            if (graph == null) return result;
            foreach (var c in graph.Clips)
                if (c != null && !result.Contains(c)) result.Add(c);
            return result;
        }

        public AnimationClip this[string name]
        {
            get
            {
                foreach (var c in Originals())
                    if (c.name == name) return Compat.AssetCloneResourcesMeasured ? MapClip(c) : RawOverride(c);
                return null;
            }
            set
            {
                foreach (var c in Originals())
                {
                    if (c.name != name) continue;
                    SetOverride(c, value);
                    return;
                }
            }
        }

        public AnimationClip this[AnimationClip clip]
        {
            get => Compat.AssetCloneResourcesMeasured
                ? clip != null && Graph != null && Graph.Clips.Contains(clip) ? MapClip(clip) : null
                : RawOverride(clip);
            set
            {
                if (clip == null) return;
                SetOverride(clip, value);
            }
        }

        void SetOverride(AnimationClip original, AnimationClip replacement)
        {
            if (replacement == null) m_Overrides.Remove(original);
            else m_Overrides[original] = replacement;
            m_Version++;
        }

        AnimationClip RawOverride(AnimationClip original) =>
            original != null && m_Overrides.TryGetValue(original, out var replacement) ? replacement : null;

        public int overridesCount => Originals().Count;

        [Obsolete("AnimatorOverrideController.clips property is deprecated. Use AnimatorOverrideController.GetOverrides and AnimatorOverrideController.ApplyOverrides instead.")]
        public AnimationClipPair[] clips
        {
            get
            {
                var originals = Originals();
                var result = new AnimationClipPair[originals.Count];
                for (int i = 0; i < result.Length; i++)
                    result[i] = new AnimationClipPair { originalClip = originals[i], overrideClip = RawOverride(originals[i]) };
                return result;
            }
            set
            {
                if (value == null) return;
                foreach (var pair in value)
                    if (pair?.originalClip != null) m_Overrides[pair.originalClip] = pair.overrideClip;
                m_Version++;
            }
        }

        public void GetOverrides(List<KeyValuePair<AnimationClip, AnimationClip>> overrides)
        {
            if (overrides == null) throw new ArgumentNullException(nameof(overrides));
            overrides.Clear();
            foreach (var c in Originals())
                overrides.Add(new KeyValuePair<AnimationClip, AnimationClip>(c, RawOverride(c)));
        }

        public void ApplyOverrides(IList<KeyValuePair<AnimationClip, AnimationClip>> overrides)
        {
            if (overrides == null) throw new ArgumentNullException(nameof(overrides));
            foreach (var pair in overrides)
            {
                if (pair.Key == null) continue;
                if (pair.Value == null) m_Overrides.Remove(pair.Key);
                else m_Overrides[pair.Key] = pair.Value;
            }
            m_Version++;
        }

        internal override RuntimeAnimatorController CloneController()
        {
            var clone = new AnimatorOverrideController { m_Controller = m_Controller };
            foreach (var pair in m_Overrides) clone.m_Overrides[pair.Key] = pair.Value;
            return clone;
        }

        internal override void ReadAsset(YNode body, IReferenceResolver resolver)
        {
            m_Controller = Resolve<RuntimeAnimatorController>(resolver, body.Ref("m_Controller"));
            m_Overrides.Clear();
            foreach (var pair in body.Get("m_Clips")?.Seq() ?? Array.Empty<YNode>())
            {
                var original = Resolve<AnimationClip>(resolver, pair.Ref("m_OriginalClip"));
                var replacement = Resolve<AnimationClip>(resolver, pair.Ref("m_OverrideClip"));
                if (original != null && replacement != null) m_Overrides[original] = replacement;
            }
            m_Version++;
        }
    }

    /// <summary>Which transforms an animator layer animates (the generic part of an AvatarMask).</summary>
    [ClassId(319)]
    public sealed partial class AvatarMask : Object
    {
        internal readonly List<(string Path, bool Active)> m_Elements = new List<(string, bool)>();

        public AvatarMask() { }

        public int transformCount
        {
            get => m_Elements.Count;
            set
            {
                while (m_Elements.Count > value) m_Elements.RemoveAt(m_Elements.Count - 1);
                while (m_Elements.Count < value) m_Elements.Add(("", true));
            }
        }

        public void AddTransformPath(Transform transform) => AddTransformPath(transform, true);

        public void AddTransformPath(Transform transform, [Internal.DefaultValue("true")] bool recursive)
        {
            if (transform == null) throw new ArgumentNullException(nameof(transform));
            AddPath(transform, transform, recursive);
        }

        void AddPath(Transform root, Transform t, bool recursive)
        {
            var path = PathOf(root, t);
            if (!m_Elements.Exists(e => e.Path == path)) m_Elements.Add((path, true));
            if (!recursive) return;
            foreach (var child in t.m_Children) AddPath(root, child, true);
        }

        public void RemoveTransformPath(Transform transform) => RemoveTransformPath(transform, true);

        public void RemoveTransformPath(Transform transform, [Internal.DefaultValue("true")] bool recursive)
        {
            if (transform == null) throw new ArgumentNullException(nameof(transform));
            var name = transform.name;
            m_Elements.RemoveAll(e => e.Path == name || (recursive && e.Path.StartsWith(name + "/", StringComparison.Ordinal)));
        }

        static string PathOf(Transform root, Transform t)
        {
            var parts = new List<string>();
            for (var p = t; p != null && p != root.m_Parent; p = p.m_Parent) parts.Insert(0, p.name);
            return string.Join("/", parts);
        }

        public string GetTransformPath(int index) => m_Elements[index].Path;

        public void SetTransformPath(int index, string path) => m_Elements[index] = (path ?? "", m_Elements[index].Active);

        public bool GetTransformActive(int index) => m_Elements[index].Active;

        public void SetTransformActive(int index, bool value) => m_Elements[index] = (m_Elements[index].Path, value);

        /// <summary>Whether the transform at a path below the animated root is animated by the layer.</summary>
        internal bool Allows(string path)
        {
            if (m_Elements.Count == 0) return true;
            foreach (var e in m_Elements)
                if (e.Path == path) return e.Active;
            return false;
        }

        internal void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_Elements.Clear();
            foreach (var e in body.Get("m_Elements")?.Seq() ?? Array.Empty<YNode>())
                m_Elements.Add((e.Str("m_Path"), e.Float("m_Weight", 1f) > 0.5f));
            ReadHumanoidMask(body.Str("m_Mask"));
        }
    }
}

namespace Unbound.Runtime
{
    using UnityEngine;
    using Object = UnityEngine.Object;

    /// <summary>A layer as the AnimatorController document lists it.</summary>
    internal sealed class ControllerLayerData
    {
        internal string Name;
        internal UnityEditor.Animations.AnimatorStateMachine StateMachine;
        internal AvatarMask Mask;
        internal int BlendingMode;
        internal int SyncedLayerIndex = -1;
        internal float DefaultWeight;
        internal bool SyncedLayerAffectsTiming;
        /// <summary>The layer's "IK Pass": OnAnimatorIK and the IK solve run for it (humanoids).</summary>
        internal bool IKPass;
        internal readonly List<(UnityEditor.Animations.AnimatorState State, Motion Motion)> SyncedMotions = new List<(UnityEditor.Animations.AnimatorState, Motion)>();
        internal readonly List<(UnityEditor.Animations.AnimatorState State, List<StateMachineBehaviour> Behaviours)> SyncedBehaviours = new List<(UnityEditor.Animations.AnimatorState, List<StateMachineBehaviour>)>();
    }

    /// <summary>AnimatorTransition (class 1109: entry and state machine transitions) and the base of
    /// AnimatorStateTransition.</summary>
    [ClassId(1109)]
    internal class ControllerTransitionAsset : Object, ISerializedAsset
    {
        internal readonly List<(int Mode, string Parameter, float Threshold)> Conditions = new List<(int, string, float)>();
        internal UnityEditor.Animations.AnimatorState DstState;
        internal UnityEditor.Animations.AnimatorStateMachine DstStateMachine;
        internal bool Solo;
        internal bool Mute;
        internal bool IsExit;
        internal float Duration;
        internal float Offset;
        internal float ExitTime;
        internal bool HasExitTime;
        internal bool HasFixedDuration;
        internal int InterruptionSource;
        internal bool OrderedInterruption = true;
        internal bool CanTransitionToSelf = true;

        public virtual void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            foreach (var c in body.Get("m_Conditions")?.Seq() ?? Array.Empty<YNode>())
                Conditions.Add((c.Int("m_ConditionMode"), c.Str("m_ConditionEvent"), c.Float("m_EventTreshold")));
            DstState = RuntimeAnimatorController.Resolve<UnityEditor.Animations.AnimatorState>(resolver, body.Ref("m_DstState"));
            DstStateMachine = RuntimeAnimatorController.Resolve<UnityEditor.Animations.AnimatorStateMachine>(resolver, body.Ref("m_DstStateMachine"));
            Solo = body.Bool("m_Solo");
            Mute = body.Bool("m_Mute");
            IsExit = body.Bool("m_IsExit");
        }
    }

    /// <summary>AnimatorStateTransition (class 1101): a transition out of a state (or Any State).</summary>
    [ClassId(1101)]
    internal sealed class ControllerStateTransitionAsset : ControllerTransitionAsset
    {
        public override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            base.ReadSerialized(body, resolver);
            Duration = body.Float("m_TransitionDuration");
            Offset = body.Float("m_TransitionOffset");
            ExitTime = body.Float("m_ExitTime", 0.75f);
            HasExitTime = body.Bool("m_HasExitTime");
            HasFixedDuration = body.Bool("m_HasFixedDuration");
            InterruptionSource = body.Int("m_InterruptionSource");
            OrderedInterruption = body.Bool("m_OrderedInterruption", true);
            CanTransitionToSelf = body.Bool("m_CanTransitionToSelf", true);
        }
    }

    /// <summary>BlendTree (class 206): a motion blending child motions by parameters.</summary>
    [ClassId(206)]
    internal sealed class BlendTreeMotion : Motion, ISerializedAsset
    {
        internal sealed class Child
        {
            internal Motion Motion;
            internal float Threshold;
            internal Vector2 Position;
            internal float TimeScale = 1f;
            internal float CycleOffset;
            /// <summary>ChildMotion.mirror: toggles the state's mirror for this child (humanoid clips).</summary>
            internal bool Mirror;
            internal string DirectParameter;
        }

        internal readonly List<Child> Children = new List<Child>();
        internal string Parameter = "";
        internal string ParameterY = "";
        /// <summary>0 Simple1D, 1 SimpleDirectional2D, 2 FreeformDirectional2D, 3 FreeformCartesian2D, 4 Direct.</summary>
        internal int BlendType;
        internal bool NormalizedBlendValues;

        public void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            foreach (var c in body.Get("m_Childs")?.Seq() ?? Array.Empty<YNode>())
            {
                var motion = c.Ref("m_Motion");
                var resolved = RuntimeAnimatorController.Resolve<Motion>(resolver, motion);
                if (resolved == null && !motion.IsNull) MissingMotion.Report(motion, $"blend tree '{m_Name}'");
                Children.Add(new Child
                {
                    Motion = resolved,
                    Threshold = c.Float("m_Threshold"),
                    Position = c.Vector2("m_Position"),
                    TimeScale = c.Float("m_TimeScale", 1f),
                    CycleOffset = c.Float("m_CycleOffset"),
                    Mirror = c.Bool("m_Mirror"),
                    DirectParameter = c.Str("m_DirectBlendParameter"),
                });
            }
            Parameter = body.Str("m_BlendParameter");
            ParameterY = body.Str("m_BlendParameterY");
            BlendType = body.Int("m_BlendType");
            NormalizedBlendValues = body.Bool("m_NormalizedBlendValues");
        }

        internal override bool GetIsLooping()
        {
            foreach (var c in Children)
                if (c.Motion != null && c.Motion.isLooping) return true;
            return false;
        }
    }

    /// <summary>A motion a controller or Animation component names that could not be loaded (a clip
    /// inside a model file, a missing asset): reported once, the state plays nothing.</summary>
    internal static class MissingMotion
    {
        static readonly HashSet<string> s_Reported = new HashSet<string>();

        internal static void Report(ObjectRef reference, string user)
        {
            var key = $"{reference.Guid}:{reference.FileId}";
            lock (s_Reported)
            {
                if (!s_Reported.Add(key)) return;
            }
            string why = "";
            try
            {
                var info = AssetDatabaseRuntime.Info(reference.Guid);
                why = info == null ? " (no such asset)" : $" ({info.Path} has no such clip)";
            }
            catch (Exception)
            {
                why = " (no such asset)";
            }
            Debug.LogWarning($"Unbound: the animation clip {{fileID: {reference.FileId}, guid: {reference.Guid}}} used by {user} could not be loaded{why}; it plays nothing.");
        }
    }
}

namespace UnityEditor.Animations
{
    using System;
    using System.Collections.Generic;
    using Unbound.Runtime;
    using UnityEngine;
    using Object = UnityEngine.Object;

    /// <summary>The Editor's class of a `.controller` file (class 91): its parameters and layers as
    /// the file holds them (Unity 6000.0.71f1, tests/projects/gameinterop editor_controller.*).
    /// Setting them rebuilds the controller's graph when it next plays.</summary>
    [ClassId(91)]
    public sealed partial class AnimatorController : RuntimeAnimatorController
    {
        public AnimatorController() { }

        public AnimatorControllerParameter[] parameters
        {
            get
            {
                var result = new AnimatorControllerParameter[m_Parameters.Count];
                for (int i = 0; i < result.Length; i++)
                {
                    var p = m_Parameters[i];
                    result[i] = new AnimatorControllerParameter { m_Name = p.m_Name, m_Type = p.m_Type, m_DefaultFloat = p.m_DefaultFloat, m_DefaultInt = p.m_DefaultInt, m_DefaultBool = p.m_DefaultBool };
                }
                return result;
            }
            set
            {
                m_Parameters = new List<AnimatorControllerParameter>(value ?? Array.Empty<AnimatorControllerParameter>());
                m_Graph = null;
            }
        }

        public AnimatorControllerLayer[] layers
        {
            get
            {
                var result = new AnimatorControllerLayer[m_Layers.Count];
                for (int i = 0; i < result.Length; i++)
                {
                    var l = m_Layers[i];
                    result[i] = new AnimatorControllerLayer
                    {
                        name = l.Name, stateMachine = l.StateMachine, avatarMask = l.Mask, blendingMode = (AnimatorLayerBlendingMode)l.BlendingMode,
                        syncedLayerIndex = l.SyncedLayerIndex, iKPass = l.IKPass, defaultWeight = l.DefaultWeight, syncedLayerAffectsTiming = l.SyncedLayerAffectsTiming,
                    };
                }
                return result;
            }
            set
            {
                var previous = m_Layers;
                m_Layers = new List<ControllerLayerData>();
                foreach (var l in value ?? Array.Empty<AnimatorControllerLayer>())
                {
                    var data = new ControllerLayerData
                    {
                        Name = l.name, StateMachine = l.stateMachine, Mask = l.avatarMask, BlendingMode = (int)l.blendingMode,
                        SyncedLayerIndex = l.syncedLayerIndex, IKPass = l.iKPass, DefaultWeight = l.defaultWeight, SyncedLayerAffectsTiming = l.syncedLayerAffectsTiming,
                    };
                    // A synced layer keeps the motions and behaviours it had for its states.
                    var same = previous.Find(p => p.Name == l.name);
                    if (same != null)
                    {
                        data.SyncedMotions.AddRange(same.SyncedMotions);
                        data.SyncedBehaviours.AddRange(same.SyncedBehaviours);
                    }
                    m_Layers.Add(data);
                }
                m_Graph = null;
            }
        }
    }

    public enum AnimatorLayerBlendingMode
    {
        Override = 0,
        Additive = 1,
    }

    /// <summary>A controller layer's settings (a copy: set <see cref="AnimatorController.layers"/> to apply).</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public partial class AnimatorControllerLayer
    {
        public AnimatorControllerLayer() { }
        public string name { get; set; }
        public AnimatorStateMachine stateMachine { get; set; }
        public AvatarMask avatarMask { get; set; }
        public AnimatorLayerBlendingMode blendingMode { get; set; }
        public int syncedLayerIndex { get; set; } = -1;
        public bool iKPass { get; set; }
        public float defaultWeight { get; set; }
        public bool syncedLayerAffectsTiming { get; set; }
    }

    public partial struct ChildAnimatorState
    {
        AnimatorState m_State;
        Vector3 m_Position;
        public AnimatorState state { get => m_State; set => m_State = value; }
        public Vector3 position { get => m_Position; set => m_Position = value; }
    }

    public partial struct ChildAnimatorStateMachine
    {
        AnimatorStateMachine m_StateMachine;
        Vector3 m_Position;
        public AnimatorStateMachine stateMachine { get => m_StateMachine; set => m_StateMachine = value; }
        public Vector3 position { get => m_Position; set => m_Position = value; }
    }

    /// <summary>AnimatorStateMachine (class 1107): an editor object of a controller file. The
    /// Editor's API reads what the file holds (Unity 6000.0.71f1, tests/projects/gameinterop
    /// editor_controller.*); editing it does not change a graph already built from it.</summary>
    [ClassId(1107)]
    [HelpURL("NestedStateMachines")]
    public sealed partial class AnimatorStateMachine : Object
    {
        internal readonly List<Vector3> StatePositions = new List<Vector3>();
        internal readonly List<Vector3> MachinePositions = new List<Vector3>();

        public AnimatorStateMachine() { }

        public ChildAnimatorState[] states
        {
            get
            {
                var result = new ChildAnimatorState[States.Count];
                for (int i = 0; i < result.Length; i++)
                    result[i] = new ChildAnimatorState { state = States[i], position = i < StatePositions.Count ? StatePositions[i] : default };
                return result;
            }
            set
            {
                States.Clear();
                StatePositions.Clear();
                foreach (var child in value ?? Array.Empty<ChildAnimatorState>())
                {
                    States.Add(child.state);
                    StatePositions.Add(child.position);
                }
            }
        }

        public ChildAnimatorStateMachine[] stateMachines
        {
            get
            {
                var result = new ChildAnimatorStateMachine[Machines.Count];
                for (int i = 0; i < result.Length; i++)
                    result[i] = new ChildAnimatorStateMachine { stateMachine = Machines[i], position = i < MachinePositions.Count ? MachinePositions[i] : default };
                return result;
            }
            set
            {
                Machines.Clear();
                MachinePositions.Clear();
                foreach (var child in value ?? Array.Empty<ChildAnimatorStateMachine>())
                {
                    Machines.Add(child.stateMachine);
                    MachinePositions.Add(child.position);
                }
            }
        }

        public AnimatorState defaultState { get => DefaultState; set => DefaultState = value; }

        internal readonly List<AnimatorState> States = new List<AnimatorState>();
        internal readonly List<AnimatorStateMachine> Machines = new List<AnimatorStateMachine>();
        internal readonly List<ControllerTransitionAsset> AnyStateTransitions = new List<ControllerTransitionAsset>();
        internal readonly List<ControllerTransitionAsset> EntryTransitions = new List<ControllerTransitionAsset>();
        internal readonly List<(AnimatorStateMachine Source, List<ControllerTransitionAsset> Transitions)> MachineTransitions =
            new List<(AnimatorStateMachine, List<ControllerTransitionAsset>)>();
        internal readonly List<StateMachineBehaviour> Behaviours = new List<StateMachineBehaviour>();
        internal AnimatorState DefaultState;

        internal void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            foreach (var child in body.Get("m_ChildStates")?.Seq() ?? Array.Empty<YNode>())
                if (UnityEngine.RuntimeAnimatorController.Resolve<AnimatorState>(resolver, child.Ref("m_State")) is AnimatorState s)
                {
                    States.Add(s);
                    StatePositions.Add(child.Vector3("m_Position"));
                }
            foreach (var child in body.Get("m_ChildStateMachines")?.Seq() ?? Array.Empty<YNode>())
                if (UnityEngine.RuntimeAnimatorController.Resolve<AnimatorStateMachine>(resolver, child.Ref("m_StateMachine")) is AnimatorStateMachine m)
                {
                    Machines.Add(m);
                    MachinePositions.Add(child.Vector3("m_Position"));
                }
            ReadTransitions(body.Get("m_AnyStateTransitions"), resolver, AnyStateTransitions);
            ReadTransitions(body.Get("m_EntryTransitions"), resolver, EntryTransitions);
            var machineTransitions = body.Get("m_StateMachineTransitions");
            foreach (var pair in machineTransitions?.Seq() ?? Array.Empty<YNode>())
            {
                var source = UnityEngine.RuntimeAnimatorController.Resolve<AnimatorStateMachine>(resolver, pair.Ref("first"));
                var list = new List<ControllerTransitionAsset>();
                ReadTransitions(pair.Get("second"), resolver, list);
                if (source != null) MachineTransitions.Add((source, list));
            }
            foreach (var b in body.Get("m_StateMachineBehaviours")?.Seq() ?? Array.Empty<YNode>())
                if (UnityEngine.RuntimeAnimatorController.Resolve<StateMachineBehaviour>(resolver, b.AsRef()) is StateMachineBehaviour smb) Behaviours.Add(smb);
            DefaultState = UnityEngine.RuntimeAnimatorController.Resolve<AnimatorState>(resolver, body.Ref("m_DefaultState"));
        }

        internal static void ReadTransitions(YNode list, IReferenceResolver resolver, List<ControllerTransitionAsset> into)
        {
            foreach (var item in list?.Seq() ?? Array.Empty<YNode>())
                if (UnityEngine.RuntimeAnimatorController.Resolve<ControllerTransitionAsset>(resolver, item.AsRef()) is ControllerTransitionAsset t) into.Add(t);
        }
    }

    /// <summary>AnimatorState (class 1102), read as <see cref="AnimatorStateMachine"/> is.</summary>
    [ClassId(1102)]
    [HelpURL("class-State")]
    public sealed partial class AnimatorState : Object
    {
        public AnimatorState() { }

        public int nameHash => Animator.StringToHash(name);
        public Motion motion { get => Motion; set => Motion = value; }
        public float speed { get => Speed; set => Speed = value; }
        public float cycleOffset { get => CycleOffset; set => CycleOffset = value; }
        public bool mirror { get => Mirror; set => Mirror = value; }
        public bool iKOnFeet { get => IKOnFeet; set => IKOnFeet = value; }
        public bool writeDefaultValues { get => WriteDefaults; set => WriteDefaults = value; }
        public string tag { get => Tag; set => Tag = value ?? ""; }

        internal float Speed = 1f;
        internal float CycleOffset;
        internal readonly List<ControllerTransitionAsset> Transitions = new List<ControllerTransitionAsset>();
        internal readonly List<StateMachineBehaviour> Behaviours = new List<StateMachineBehaviour>();
        internal bool WriteDefaults = true;
        internal bool IKOnFeet;
        internal bool Mirror;
        internal Motion Motion;
        internal string Tag = "";
        internal string SpeedParameter;
        internal string CycleOffsetParameter;
        internal string TimeParameter;
        internal string MirrorParameter;

        internal void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            Speed = body.Float("m_Speed", 1f);
            CycleOffset = body.Float("m_CycleOffset");
            AnimatorStateMachine.ReadTransitions(body.Get("m_Transitions"), resolver, Transitions);
            foreach (var b in body.Get("m_StateMachineBehaviours")?.Seq() ?? Array.Empty<YNode>())
                if (UnityEngine.RuntimeAnimatorController.Resolve<StateMachineBehaviour>(resolver, b.AsRef()) is StateMachineBehaviour smb) Behaviours.Add(smb);
            WriteDefaults = body.Bool("m_WriteDefaultValues", true);
            IKOnFeet = body.Bool("m_IKOnFeet");
            Mirror = body.Bool("m_Mirror");
            var motion = body.Ref("m_Motion");
            Motion = UnityEngine.RuntimeAnimatorController.Resolve<Motion>(resolver, motion);
            if (Motion == null && !motion.IsNull) MissingMotion.Report(motion, $"animator state '{m_Name}'");
            Tag = body.Str("m_Tag");
            SpeedParameter = body.Bool("m_SpeedParameterActive") ? body.Str("m_SpeedParameter") : null;
            CycleOffsetParameter = body.Bool("m_CycleOffsetParameterActive") ? body.Str("m_CycleOffsetParameter") : null;
            TimeParameter = body.Bool("m_TimeParameterActive") ? body.Str("m_TimeParameter") : null;
            MirrorParameter = body.Bool("m_MirrorParameterActive") ? body.Str("m_MirrorParameter") : null;
        }
    }

}
