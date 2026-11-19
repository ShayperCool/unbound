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

        /// <summary>The <see cref="ControllerEdits.Version"/> the graph was last known current at.</summary>
        int m_GraphVersion;

        /// <summary>When the controller's own settings (parameters, layers) were last edited
        /// (<see cref="ControllerEdits"/>).</summary>
        internal int m_EditStamp;

        /// <summary>The controller is the player form an AssetBundle holds (already compiled).</summary>
        internal bool m_PlayerForm;

        internal void Edited() => m_EditStamp = ControllerEdits.Next();

        /// <summary>The compiled state machines (of the base controller, for an override
        /// controller). An edit of the controller or of an object it plays makes the next read
        /// compile it again.</summary>
        internal virtual ControllerGraph Graph
        {
            get
            {
                if (m_Graph != null && m_GraphVersion != ControllerEdits.Version)
                {
                    if (EditedSince(m_GraphVersion)) m_Graph = null;
                    m_GraphVersion = ControllerEdits.Version;
                }
                if (m_Graph == null)
                {
                    m_GraphVersion = ControllerEdits.Version;
                    m_Graph = ControllerGraph.Build(this);
                    if (!m_PlayerForm) Compiled();
                }
                return m_Graph;
            }
        }

        /// <summary>Whether the controller or an object of its layers was edited after
        /// <paramref name="version"/>.</summary>
        internal virtual bool EditedSince(int version) => m_EditStamp > version;

        /// <summary>Called when the controller's graph is built (the Editor compiles it then).</summary>
        internal virtual void Compiled() { }

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
                m_PlayerForm = true;
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
            m_PlayerForm = m_PlayerForm,
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
