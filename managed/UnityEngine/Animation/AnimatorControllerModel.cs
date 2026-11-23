using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>
    /// Edits of animator controllers and of the objects they play (the Editor's
    /// UnityEditor.Animations object model). Each edit takes the next version; a controller whose
    /// objects changed after its graph was built compiles again, and an Animator playing it starts
    /// over: its parameters go back to their defaults and its next update evaluates the default
    /// state at time 0 (matches the Unity 6000.0.71f1 Editor, tests/projects/animatorapi
    /// <c>play.*</c>: a parameter added, a state and transition added, a state's speed changed).
    /// </summary>
    internal static class ControllerEdits
    {
        /// <summary>The version of the latest edit (the object model is used on the main thread).</summary>
        internal static int Version;

        internal static int Next() => ++Version;
    }

    /// <summary>What the Editor's controller API does with the objects it makes and removes.</summary>
    internal static class ControllerObjects
    {
        /// <summary>The name a state or state machine takes: a dot separates the names of a state's
        /// path, so each becomes an underscore, with the Editor's warning (matches the Unity
        /// 6000.0.71f1 Editor, tests/projects/animatorapi <c>names.*</c>: AddState, AddMotion,
        /// AddStateMachine, a new layer's machine and the name setter; the unique name helpers,
        /// layers and parameters keep their dots).</summary>
        internal static string StateName(string name)
        {
            if (name == null || name.IndexOf('.') < 0) return name;
            Debug.LogWarning("'.' is not allowed in State name");
            return name.Replace('.', '_');
        }

        /// <summary>Whether two objects are in the same asset file (or both in none).</summary>
        internal static bool SameAsset(Object a, Object b) => AssetDatabase.GetAssetPath(a) == AssetDatabase.GetAssetPath(b);

        /// <summary>Destroys an object made for <paramref name="owner"/> when it is in the owner's
        /// asset (or both are in memory). An object already destroyed throws, as the Editor's
        /// undoable destroy does (tests/projects/animatorapi <c>memory.transitions.removeMachineAgain</c>).</summary>
        internal static void DestroyWith(Object owner, Object o)
        {
            if (ReferenceEquals(o, null) || !SameAsset(owner, o)) return;
            if (o == null) throw ObjectErrors.Receiver(o);
            Object.DestroyImmediate(o, true);
        }

        /// <summary>Adds an object made for <paramref name="owner"/> to the owner's asset file
        /// (AssetDatabase.AddObjectToAsset: written into it by the next SaveAssets, matches the Unity
        /// 6000.0.71f1 Editor, tests/projects/scenesave controller-assets, and controller-additions
        /// for a controller read from its file).</summary>
        internal static void AddToAssetOf(Object owner, Object o)
        {
            if (AssetDatabase.GetAssetPath(owner) == "") return;
            // A controller Unbound cannot write whole is kept as read: reported once.
            if (AssetDatabase.AddsObjectsTo(owner)) AssetDatabase.AddObjectToAsset(o, owner);
            else Unbound.Runtime.Stubs.NotImplemented("UnityEditor.AssetDatabase.AddObjectToAsset");
        }

        /// <summary>Makes an asset of an object at a path (AssetDatabase.CreateAsset: the controller's
        /// file is written at once, before its first layer is added: controller-assets basic.created).</summary>
        internal static void CreateAsset(Object o, string path) => AssetDatabase.CreateAsset(o, path);

        /// <summary>An edit of an object of a controller asset made this session, or of one read from
        /// its file that objects were added to (written whole from then on), dirties it, so that
        /// SaveAssets writes the file again (controller-assets basic.editedDirty, basic.saved); any
        /// other controller read from its file is kept as read (edits alone are not measured).</summary>
        internal static void Dirty(Object o)
        {
            if (o.IsPersistentAsset && AssetDatabase.CreatedThisSession(o)) EditorUtility.SetDirty(o);
        }

        /// <summary><paramref name="name"/>, else the first of "name 0", "name 1", ... that is free.</summary>
        internal static string Unique(string name, Func<string, bool> taken)
        {
            name ??= "";
            if (!taken(name)) return name;
            for (int i = 0; ; i++)
            {
                var candidate = name + " " + i;
                if (!taken(candidate)) return candidate;
            }
        }

        /// <summary>The objects a list holds, of one type, in order.</summary>
        internal static T[] OfType<T, TSource>(List<TSource> list) where T : class
        {
            int count = 0;
            foreach (var item in list) if (item is T) count++;
            var result = new T[count];
            int i = 0;
            foreach (var item in list) if (item is T t) result[i++] = t;
            return result;
        }

        internal static void Assign<T>(List<T> list, IEnumerable<T> items)
        {
            list.Clear();
            if (items != null) list.AddRange(items);
        }
    }

    /// <summary>
    /// The messages the Editor logs when it compiles a controller (Unity 6000.0.71f1,
    /// tests/projects/animatorapi <c>*.logs</c>, <c>extra.compile</c>): first an error for each
    /// parameter a state's blend tree uses (the trees nested in it included) that the controller
    /// does not have, then a warning for each transition of a state or of Any State that is left
    /// (not muted; of a state with solo transitions, a solo one) without an exit time or a
    /// condition. Entry and state machine transitions are not checked.
    /// </summary>
    internal static class ControllerValidation
    {
        internal static void Report(AnimatorController controller)
        {
            var layers = controller.m_Layers;
            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                if (layer.SyncedLayerIndex >= 0)
                {
                    foreach (var (state, motion) in layer.SyncedMotions)
                        if (motion is BlendTree tree && state != null) Parameters(controller, state, tree);
                }
                else if (layer.StateMachine != null) Trees(controller, layer.StateMachine, new HashSet<AnimatorStateMachine>());
            }
            string asset = Path.GetFileNameWithoutExtension(AssetDatabase.GetAssetPath(controller));
            foreach (var layer in layers)
                if (layer.SyncedLayerIndex < 0 && layer.StateMachine != null) Transitions(asset, layer.StateMachine, new HashSet<AnimatorStateMachine>());
        }

        static void Trees(AnimatorController controller, AnimatorStateMachine machine, HashSet<AnimatorStateMachine> seen)
        {
            if (!seen.Add(machine)) return;
            foreach (var state in machine.States)
                if (state != null && state.Motion is BlendTree tree) Parameters(controller, state, tree);
            foreach (var child in machine.Machines)
                if (child != null) Trees(controller, child, seen);
        }

        static void Parameters(AnimatorController controller, AnimatorState state, BlendTree tree)
        {
            var used = new List<string>();
            tree.CollectParameters(used, new HashSet<BlendTree>());
            foreach (var name in used)
                if (!controller.HasParameter(name))
                    Debug.LogError($"BlendTree \"{tree.m_Name}\" in state \"{state.m_Name}\" in Controller \"{controller.m_Name}\" uses parameter \"{name}\" which does not exist.");
        }

        static void Transitions(string asset, AnimatorStateMachine machine, HashSet<AnimatorStateMachine> seen)
        {
            if (!seen.Add(machine)) return;
            foreach (var state in machine.States)
            {
                if (state == null) continue;
                foreach (var t in Considered(state.Transitions))
                    Warn(asset, t.GetDisplayName(state), state.m_Name);
            }
            foreach (var t in Considered(machine.AnyStateTransitions))
                Warn(asset, AnimatorTransitionBase.BuildName("AnyState", t.DestinationName()), "AnyState");
            foreach (var child in machine.Machines)
                if (child != null) Transitions(asset, child, seen);
        }

        /// <summary>The transitions of a list that can fire but have neither an exit time nor a condition.</summary>
        static IEnumerable<AnimatorTransitionBase> Considered(List<AnimatorTransitionBase> transitions)
        {
            bool solo = transitions.Exists(t => t != null && t.Solo);
            foreach (var t in transitions)
                if (t != null && !t.Mute && (!solo || t.Solo) && !t.HasExitTime && t.Conditions.Count == 0) yield return t;
        }

        static void Warn(string asset, string transition, string state) =>
            Debug.LogWarning($"Asset '{asset}': Transition '{transition}' in state '{state}' doesn't have an Exit Time or any condition, transition will be ignored");
    }
}

namespace UnityEditor.Animations
{
    using System.Linq;
    using Unbound.Runtime;

    /// <summary>The Editor's class of a <c>.controller</c> file (class 91): its parameters and
    /// layers as the file holds them (Unity 6000.0.71f1, tests/projects/gameinterop
    /// editor_controller.*), and the Editor's API that builds and edits them
    /// (tests/projects/animatorapi). Getters answer copies; setting them compiles the controller
    /// again when it next plays.</summary>
    [ClassId(91)]
    public sealed partial class AnimatorController : RuntimeAnimatorController
    {
        public AnimatorController() { }

        public AnimatorControllerParameter[] parameters
        {
            get
            {
                var result = new AnimatorControllerParameter[m_Parameters.Count];
                for (int i = 0; i < result.Length; i++) result[i] = Copy(m_Parameters[i]);
                return result;
            }
            set
            {
                m_Parameters = new List<AnimatorControllerParameter>();
                foreach (var p in value ?? Array.Empty<AnimatorControllerParameter>())
                    if (p != null) m_Parameters.Add(Copy(p));
                Edited();
            }
        }

        static AnimatorControllerParameter Copy(AnimatorControllerParameter p) => new AnimatorControllerParameter
        {
            m_Name = p.m_Name, m_Type = p.m_Type, m_DefaultFloat = p.m_DefaultFloat, m_DefaultInt = p.m_DefaultInt, m_DefaultBool = p.m_DefaultBool,
        };

        public AnimatorControllerLayer[] layers
        {
            get
            {
                var result = new AnimatorControllerLayer[m_Layers.Count];
                for (int i = 0; i < result.Length; i++) result[i] = AnimatorControllerLayer.From(m_Layers[i]);
                return result;
            }
            set
            {
                m_Layers = new List<ControllerLayerData>();
                foreach (var l in value ?? Array.Empty<AnimatorControllerLayer>())
                    if (l != null) m_Layers.Add(l.ToData());
                Edited();
            }
        }

        internal bool HasParameter(string name)
        {
            foreach (var p in m_Parameters)
                if (p.m_Name == name) return true;
            return false;
        }

        public void AddLayer(string name)
        {
            var layer = new AnimatorControllerLayer { name = MakeUniqueLayerName(name) };
            layer.stateMachine = new AnimatorStateMachine { name = layer.name, hideFlags = HideFlags.HideInHierarchy };
            ControllerObjects.AddToAssetOf(this, layer.stateMachine);
            AddLayer(layer);
        }

        public void AddLayer(AnimatorControllerLayer layer)
        {
            var list = new List<AnimatorControllerLayer>(layers) { layer };
            layers = list.ToArray();
        }

        /// <summary>Removes a layer, clearing and destroying its state machine when it has its own
        /// in this asset. Other layers keep the synced layer indices they had (measured).</summary>
        public void RemoveLayer(int index)
        {
            var list = new List<AnimatorControllerLayer>(layers);
            var layer = list[index];
            if (layer.syncedLayerIndex == -1 && layer.stateMachine != null)
            {
                layer.stateMachine.Clear();
                ControllerObjects.DestroyWith(this, layer.stateMachine);
            }
            list.RemoveAt(index);
            layers = list.ToArray();
        }

        public void AddParameter(string name, AnimatorControllerParameterType type) =>
            AddParameter(new AnimatorControllerParameter { name = MakeUniqueParameterName(name), type = type });

        /// <summary>Adds the parameter as it is: its name is not made unique (measured).</summary>
        public void AddParameter(AnimatorControllerParameter paramater)
        {
            var list = new List<AnimatorControllerParameter>(parameters) { paramater };
            parameters = list.ToArray();
        }

        /// <summary>Removes the first parameter equal to the one at <paramref name="index"/>.</summary>
        public void RemoveParameter(int index) => RemoveParameter(parameters[index]);

        /// <summary>Removes the first parameter whose name, type and defaults all match (measured).</summary>
        public void RemoveParameter(AnimatorControllerParameter parameter)
        {
            var list = new List<AnimatorControllerParameter>(parameters);
            list.Remove(parameter);
            parameters = list.ToArray();
        }

        public AnimatorState AddMotion(Motion motion) => AddMotion(motion, 0);

        public AnimatorState AddMotion(Motion motion, int layerIndex)
        {
            var state = layers[layerIndex].stateMachine.AddState(motion.name);
            state.motion = motion;
            return state;
        }

        public AnimatorState CreateBlendTreeInController(string name, out BlendTree tree) => CreateBlendTreeInController(name, out tree, 0);

        /// <summary>A state playing a new blend tree named <paramref name="name"/>, whose parameters
        /// are the controller's first float parameter (a float "Blend" is added when there is none).</summary>
        public AnimatorState CreateBlendTreeInController(string name, out BlendTree tree, int layerIndex)
        {
            tree = new BlendTree { name = name };
            tree.blendParameter = tree.blendParameterY = GetDefaultBlendTreeParameter();
            ControllerObjects.AddToAssetOf(this, tree);
            var state = layers[layerIndex].stateMachine.AddState(tree.name);
            state.motion = tree;
            return state;
        }

        internal string GetDefaultBlendTreeParameter()
        {
            foreach (var p in m_Parameters)
                if (p.m_Type == AnimatorControllerParameterType.Float) return p.m_Name;
            AddParameter("Blend", AnimatorControllerParameterType.Float);
            return "Blend";
        }

        /// <summary>A controller asset at <paramref name="path"/> with a "Base Layer": made through
        /// <see cref="AssetDatabase.CreateAsset"/>, which names it after the file.</summary>
        public static AnimatorController CreateAnimatorControllerAtPath(string path)
        {
            var controller = new AnimatorController { name = Path.GetFileName(path) };
            ControllerObjects.CreateAsset(controller, path);
            controller.AddLayer("Base Layer");
            return controller;
        }

        /// <summary>A new clip set up as the Animation window makes one for an Animator: 60
        /// frames per second, looping (measured).</summary>
        public static AnimationClip AllocateAnimatorClip(string name)
        {
            var clip = new AnimationClip();
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            clip.name = name;
            return clip;
        }

        public static AnimatorController CreateAnimatorControllerAtPathWithClip(string path, AnimationClip clip)
        {
            var controller = CreateAnimatorControllerAtPath(path);
            var state = controller.layers[0].stateMachine.AddState(clip.name);
            state.motion = clip;
            return controller;
        }

        public void SetStateEffectiveMotion(AnimatorState state, Motion motion) => SetStateEffectiveMotion(state, motion, 0);

        /// <summary>Sets the motion a state plays in a layer: the state's own on a layer with its
        /// own state machine, the layer's override on a synced one. A blend tree it replaces that
        /// is a sub-asset is destroyed with its nested trees.</summary>
        public void SetStateEffectiveMotion(AnimatorState state, Motion motion, int layerIndex)
        {
            if (GetStateEffectiveMotion(state, layerIndex) is BlendTree tree && tree != null && !AssetDatabase.IsMainAsset(tree))
                tree.DestroyRecursive(this);
            var list = layers;
            if (list[layerIndex].syncedLayerIndex == -1)
            {
                state.motion = motion;
                return;
            }
            list[layerIndex].SetOverrideMotion(state, motion);
            layers = list;
        }

        public Motion GetStateEffectiveMotion(AnimatorState state) => GetStateEffectiveMotion(state, 0);

        public Motion GetStateEffectiveMotion(AnimatorState state, int layerIndex)
        {
            var layer = m_Layers[layerIndex];
            if (layer.SyncedLayerIndex == -1) return state.motion;
            foreach (var (s, m) in layer.SyncedMotions)
                if (s == state) return m;
            return null;
        }

        public void SetStateEffectiveBehaviours(AnimatorState state, int layerIndex, StateMachineBehaviour[] behaviours)
        {
            if (m_Layers[layerIndex].SyncedLayerIndex == -1)
            {
                state.behaviours = behaviours;
                return;
            }
            var list = layers;
            list[layerIndex].SetOverrideBehaviours(state, behaviours);
            layers = list;
        }

        public StateMachineBehaviour[] GetStateEffectiveBehaviours(AnimatorState state, int layerIndex)
        {
            var layer = m_Layers[layerIndex];
            if (layer.SyncedLayerIndex == -1) return state.behaviours;
            foreach (var (s, b) in layer.SyncedBehaviours)
                if (s == state) return b.ToArray();
            return Array.Empty<StateMachineBehaviour>();
        }

        public static void SetAnimatorController(Animator animator, AnimatorController controller) => animator.runtimeAnimatorController = controller;

        public string MakeUniqueParameterName(string name) => ControllerObjects.Unique(name, HasParameter);

        public string MakeUniqueLayerName(string name) => ControllerObjects.Unique(name, n => m_Layers.Exists(l => l.Name == n));

        /// <summary>A new behaviour of the type for a state in a layer: the state's own on a layer
        /// with its own state machine, the layer's override on a synced one.</summary>
        public StateMachineBehaviour AddEffectiveStateMachineBehaviour(Type stateMachineBehaviourType, AnimatorState state, int layerIndex)
        {
            if (m_Layers[layerIndex].SyncedLayerIndex == -1) return state.AddStateMachineBehaviour(stateMachineBehaviourType);
            var behaviour = StateMachineBehaviours.Create(stateMachineBehaviourType, this);
            if (behaviour == null) return null;
            var list = new List<StateMachineBehaviour>(GetStateEffectiveBehaviours(state, layerIndex)) { behaviour };
            SetStateEffectiveBehaviours(state, layerIndex, list.ToArray());
            return behaviour;
        }

        public T AddEffectiveStateMachineBehaviour<T>(AnimatorState state, int layerIndex) where T : StateMachineBehaviour =>
            AddEffectiveStateMachineBehaviour(typeof(T), state, layerIndex) as T;

        /// <summary>The behaviours of type <typeparamref name="T"/> the controller runs, layer by
        /// layer: each layer's state machine behaviours (machines in order, parents first), then
        /// its states' (a synced layer: its source's machine behaviours, then its own state
        /// overrides in its source's state order). Measured, tests/projects/animatorapi
        /// <c>model.behaviours.ofType</c>. The controller compiles first, as in the Editor.</summary>
        public T[] GetBehaviours<T>() where T : StateMachineBehaviour
        {
            _ = Graph;
            var result = new List<T>();
            foreach (var layer in m_Layers)
            {
                var source = layer.SyncedLayerIndex >= 0 && layer.SyncedLayerIndex < m_Layers.Count ? m_Layers[layer.SyncedLayerIndex] : layer;
                var machine = source.StateMachine;
                if (machine == null) continue;
                foreach (var m in machine.MachinesRecursive())
                    foreach (var b in m.Behaviours)
                        if (b is T t) result.Add(t);
                foreach (var state in machine.StatesRecursive())
                {
                    if (ReferenceEquals(source, layer))
                    {
                        foreach (var b in state.Behaviours)
                            if (b is T t) result.Add(t);
                    }
                    else
                    {
                        foreach (var (s, list) in layer.SyncedBehaviours)
                            if (s == state)
                                foreach (var b in list)
                                    if (b is T t) result.Add(t);
                    }
                }
            }
            return result.ToArray();
        }

        /// <summary>Where a behaviour is used, in every controller loaded: a state machine's
        /// behaviour gives the machine and the state of it whose full path hash is smallest
        /// (unsigned), a state's the state, a synced layer's override the state with that layer
        /// (Unity 6000.0.71f1, tests/projects/animatorapi <c>model.behaviours.context.*</c>).</summary>
        public static StateMachineBehaviourContext[] FindStateMachineBehaviourContext(StateMachineBehaviour behaviour)
        {
            var result = new List<StateMachineBehaviourContext>();
            if (behaviour == null) return result.ToArray();
            foreach (var controller in Resources.FindObjectsOfTypeAll<AnimatorController>())
            {
                for (int i = 0; i < controller.m_Layers.Count; i++)
                {
                    var layer = controller.m_Layers[i];
                    if (layer.SyncedLayerIndex >= 0)
                    {
                        foreach (var (state, list) in layer.SyncedBehaviours)
                            if (list.Contains(behaviour)) result.Add(new StateMachineBehaviourContext { animatorController = controller, animatorObject = state, layerIndex = i });
                        continue;
                    }
                    if (layer.StateMachine == null) continue;
                    foreach (var (machine, path) in layer.StateMachine.MachinesRecursive(layer.Name))
                    {
                        if (machine.Behaviours.Contains(behaviour))
                        {
                            result.Add(new StateMachineBehaviourContext { animatorController = controller, animatorObject = machine, layerIndex = i });
                            if (machine.FirstStateByPathHash(path) is AnimatorState first)
                                result.Add(new StateMachineBehaviourContext { animatorController = controller, animatorObject = first, layerIndex = i });
                        }
                        foreach (var state in machine.States)
                            if (state != null && state.Behaviours.Contains(behaviour))
                                result.Add(new StateMachineBehaviourContext { animatorController = controller, animatorObject = state, layerIndex = i });
                    }
                }
            }
            return result.ToArray();
        }

        internal override bool EditedSince(int version)
        {
            if (m_EditStamp > version) return true;
            var seen = new HashSet<Object>();
            foreach (var layer in m_Layers)
            {
                if (layer.StateMachine != null && layer.StateMachine.EditedSince(version, seen)) return true;
                foreach (var (_, motion) in layer.SyncedMotions)
                    if (motion is BlendTree tree && tree.EditedSince(version, seen)) return true;
            }
            return false;
        }

        internal override void Compiled() => ControllerValidation.Report(this);
    }

    /// <summary>Making the behaviours the controller API adds.</summary>
    internal static class StateMachineBehaviours
    {
        /// <summary>A new behaviour of the type, hidden in the hierarchy and added to the asset of
        /// <paramref name="owner"/>; null, with an error, for a type that is no concrete behaviour.</summary>
        internal static StateMachineBehaviour Create(Type type, Object owner)
        {
            if (type == null || !typeof(StateMachineBehaviour).IsAssignableFrom(type) || type.IsAbstract)
            {
                Debug.LogError($"Unbound: {type?.FullName ?? "null"} is not a StateMachineBehaviour type that can be added.");
                return null;
            }
            var behaviour = (StateMachineBehaviour)ScriptableObject.CreateInstance(type);
            behaviour.hideFlags = HideFlags.HideInHierarchy;
            ControllerObjects.AddToAssetOf(owner, behaviour);
            return behaviour;
        }
    }

    public enum AnimatorConditionMode
    {
        If = 1,
        IfNot = 2,
        Greater = 3,
        Less = 4,
        Equals = 6,
        NotEqual = 7,
    }

    public enum TransitionInterruptionSource
    {
        None = 0,
        Source = 1,
        Destination = 2,
        SourceThenDestination = 3,
        DestinationThenSource = 4,
    }

    public enum BlendTreeType
    {
        Simple1D = 0,
        SimpleDirectional2D = 1,
        FreeformDirectional2D = 2,
        FreeformCartesian2D = 3,
        Direct = 4,
    }

    public enum AnimatorLayerBlendingMode
    {
        Override = 0,
        Additive = 1,
    }

    /// <summary>A controller layer's settings (a copy: set <see cref="AnimatorController.layers"/>
    /// to apply), with a synced layer's motions and behaviours for its source's states.</summary>
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public partial class AnimatorControllerLayer
    {
        string m_Name;
        AnimatorStateMachine m_StateMachine;
        AvatarMask m_AvatarMask;
        StateMotionPair[] m_Motions;
        StateBehavioursPair[] m_Behaviours;
        AnimatorLayerBlendingMode m_BlendingMode;
        int m_SyncedLayerIndex = -1;
        bool m_IKPass;
        float m_DefaultWeight;
        bool m_SyncedLayerAffectsTiming;

        public AnimatorControllerLayer() { }
        public string name { get => m_Name; set => m_Name = value; }
        public AnimatorStateMachine stateMachine { get => m_StateMachine; set => m_StateMachine = value; }
        public AvatarMask avatarMask { get => m_AvatarMask; set => m_AvatarMask = value; }
        public AnimatorLayerBlendingMode blendingMode { get => m_BlendingMode; set => m_BlendingMode = value; }
        public int syncedLayerIndex { get => m_SyncedLayerIndex; set => m_SyncedLayerIndex = value; }
        public bool iKPass { get => m_IKPass; set => m_IKPass = value; }
        public float defaultWeight { get => m_DefaultWeight; set => m_DefaultWeight = value; }
        public bool syncedLayerAffectsTiming { get => m_SyncedLayerAffectsTiming; set => m_SyncedLayerAffectsTiming = value; }

        public Motion GetOverrideMotion(AnimatorState state)
        {
            if (m_Motions != null)
                foreach (var pair in m_Motions)
                    if (pair.m_State == state) return pair.m_Motion;
            return null;
        }

        public void SetOverrideMotion(AnimatorState state, Motion motion)
        {
            m_Motions ??= Array.Empty<StateMotionPair>();
            for (int i = 0; i < m_Motions.Length; i++)
                if (m_Motions[i].m_State == state)
                {
                    m_Motions[i].m_Motion = motion;
                    return;
                }
            m_Motions = m_Motions.Append(new StateMotionPair { m_State = state, m_Motion = motion }).ToArray();
        }

        public StateMachineBehaviour[] GetOverrideBehaviours(AnimatorState state)
        {
            if (m_Behaviours != null)
                foreach (var pair in m_Behaviours)
                    if (pair.m_State == state) return pair.m_Behaviours as StateMachineBehaviour[];
            return Array.Empty<StateMachineBehaviour>();
        }

        public void SetOverrideBehaviours(AnimatorState state, StateMachineBehaviour[] behaviours)
        {
            m_Behaviours ??= Array.Empty<StateBehavioursPair>();
            for (int i = 0; i < m_Behaviours.Length; i++)
                if (m_Behaviours[i].m_State == state)
                {
                    m_Behaviours[i].m_Behaviours = behaviours;
                    return;
                }
            m_Behaviours = m_Behaviours.Append(new StateBehavioursPair { m_State = state, m_Behaviours = behaviours }).ToArray();
        }

        internal static AnimatorControllerLayer From(ControllerLayerData data) => new AnimatorControllerLayer
        {
            m_Name = data.Name, m_StateMachine = data.StateMachine, m_AvatarMask = data.Mask, m_BlendingMode = (AnimatorLayerBlendingMode)data.BlendingMode,
            m_SyncedLayerIndex = data.SyncedLayerIndex, m_IKPass = data.IKPass, m_DefaultWeight = data.DefaultWeight, m_SyncedLayerAffectsTiming = data.SyncedLayerAffectsTiming,
            m_Motions = data.SyncedMotions.Select(p => new StateMotionPair { m_State = p.State, m_Motion = p.Motion }).ToArray(),
            m_Behaviours = data.SyncedBehaviours.Select(p => new StateBehavioursPair { m_State = p.State, m_Behaviours = p.Behaviours.ToArray() }).ToArray(),
        };

        internal ControllerLayerData ToData()
        {
            var data = new ControllerLayerData
            {
                Name = m_Name, StateMachine = m_StateMachine, Mask = m_AvatarMask, BlendingMode = (int)m_BlendingMode,
                SyncedLayerIndex = m_SyncedLayerIndex, IKPass = m_IKPass, DefaultWeight = m_DefaultWeight, SyncedLayerAffectsTiming = m_SyncedLayerAffectsTiming,
            };
            foreach (var pair in m_Motions ?? Array.Empty<StateMotionPair>())
                if (pair.m_State != null) data.SyncedMotions.Add((pair.m_State, pair.m_Motion));
            foreach (var pair in m_Behaviours ?? Array.Empty<StateBehavioursPair>())
            {
                if (pair.m_State == null) continue;
                var list = new List<StateMachineBehaviour>();
                foreach (var b in pair.m_Behaviours ?? Array.Empty<ScriptableObject>())
                    if (b is StateMachineBehaviour smb) list.Add(smb);
                data.SyncedBehaviours.Add((pair.m_State, list));
            }
            return data;
        }
    }

    /// <summary>A synced layer's motion for one of its source's states.</summary>
    internal partial struct StateMotionPair
    {
        public AnimatorState m_State;
        public Motion m_Motion;
    }

    /// <summary>A synced layer's behaviours for one of its source's states.</summary>
    internal partial struct StateBehavioursPair
    {
        public AnimatorState m_State;
        public ScriptableObject[] m_Behaviours;
    }

    /// <summary>Where <see cref="AnimatorController.FindStateMachineBehaviourContext"/> found a behaviour.</summary>
    [Serializable]
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public partial class StateMachineBehaviourContext
    {
        public AnimatorController animatorController;
        public Object animatorObject;
        public int layerIndex;

        public StateMachineBehaviourContext() { }
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

    /// <summary>AnimatorStateMachine (class 1107): states, sub-machines and their transitions. The
    /// Editor's API reads what the file holds (Unity 6000.0.71f1, tests/projects/gameinterop
    /// editor_controller.*) and builds and edits it (tests/projects/animatorapi).</summary>
    [ClassId(1107)]
    [HelpURL("NestedStateMachines")]
    public sealed partial class AnimatorStateMachine : Object
    {
        internal readonly List<Vector3> StatePositions = new List<Vector3>();
        internal readonly List<Vector3> MachinePositions = new List<Vector3>();
        internal int m_EditStamp;

        public AnimatorStateMachine() { }

        internal override void SetName(string value) => m_Name = ControllerObjects.StateName(value);

        // Where the graph view draws the Any State, Entry, Exit and parent nodes; a new state
        // machine's defaults match Unity 6000.0.71f1 (tests/projects/defaultstate).
        static readonly Vector3 k_AnyStatePosition = new Vector3(50, 20, 0), k_EntryPosition = new Vector3(50, 120, 0),
            k_ExitPosition = new Vector3(800, 120, 0), k_ParentStateMachinePosition = new Vector3(800, 20, 0);
        Vector3 m_AnyStatePosition = k_AnyStatePosition, m_EntryPosition = k_EntryPosition, m_ExitPosition = k_ExitPosition,
            m_ParentStateMachinePosition = k_ParentStateMachinePosition;
        // Where the graph view draws its nodes is layout only: it does not compile the controller
        // again (Unbound's choice: unmeasured whether the Editor restarts Animators for it).
        public Vector3 anyStatePosition { get => m_AnyStatePosition; set => m_AnyStatePosition = value; }
        public Vector3 entryPosition { get => m_EntryPosition; set => m_EntryPosition = value; }
        public Vector3 exitPosition { get => m_ExitPosition; set => m_ExitPosition = value; }
        public Vector3 parentStateMachinePosition { get => m_ParentStateMachinePosition; set => m_ParentStateMachinePosition = value; }

        // Where AddState puts a state: the first at (200, 0, 0), the next 35 right and 65 down
        // from the last one (measured, tests/projects/animatorapi model.states.*).
        static readonly Vector3 k_FirstStatePosition = new Vector3(200, 0, 0), k_NextStateOffset = new Vector3(35, 65, 0);

        internal void Edited()
        {
            m_EditStamp = ControllerEdits.Next();
            ControllerObjects.Dirty(this);
        }

        /// <summary>The machine's states. The first state set becomes the default state when the
        /// machine has none (measured).</summary>
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
                if (DefaultState == null && States.Count > 0) DefaultState = States[0];
                Edited();
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
                Edited();
            }
        }

        public AnimatorState defaultState { get => DefaultState; set { DefaultState = value; Edited(); } }

        public AnimatorStateTransition[] anyStateTransitions
        {
            get => ControllerObjects.OfType<AnimatorStateTransition, AnimatorTransitionBase>(AnyStateTransitions);
            set { ControllerObjects.Assign(AnyStateTransitions, value); Edited(); }
        }

        public AnimatorTransition[] entryTransitions
        {
            get => ControllerObjects.OfType<AnimatorTransition, AnimatorTransitionBase>(EntryTransitions);
            set { ControllerObjects.Assign(EntryTransitions, value); Edited(); }
        }

        public StateMachineBehaviour[] behaviours
        {
            get => Behaviours.ToArray();
            set { ControllerObjects.Assign(Behaviours, value); Edited(); }
        }

        internal readonly List<AnimatorState> States = new List<AnimatorState>();
        internal readonly List<AnimatorStateMachine> Machines = new List<AnimatorStateMachine>();
        internal readonly List<AnimatorTransitionBase> AnyStateTransitions = new List<AnimatorTransitionBase>();
        internal readonly List<AnimatorTransitionBase> EntryTransitions = new List<AnimatorTransitionBase>();
        internal readonly List<(AnimatorStateMachine Source, List<AnimatorTransitionBase> Transitions)> MachineTransitions =
            new List<(AnimatorStateMachine, List<AnimatorTransitionBase>)>();
        internal readonly List<StateMachineBehaviour> Behaviours = new List<StateMachineBehaviour>();
        internal AnimatorState DefaultState;

        public AnimatorState AddState(string name) =>
            AddState(name, StatePositions.Count > 0 ? StatePositions[StatePositions.Count - 1] + k_NextStateOffset : k_FirstStatePosition);

        public AnimatorState AddState(string name, Vector3 position)
        {
            var state = new AnimatorState { hideFlags = HideFlags.HideInHierarchy, name = MakeUniqueStateName(name) };
            ControllerObjects.AddToAssetOf(this, state);
            AddState(state, position);
            return state;
        }

        public void AddState(AnimatorState state, Vector3 position)
        {
            if (States.Contains(state))
            {
                Debug.LogWarning($"State '{state.name}' already exists in state machine '{name}', discarding new state.");
                return;
            }
            var list = new List<ChildAnimatorState>(states) { new ChildAnimatorState { state = state, position = position } };
            states = list.ToArray();
        }

        /// <summary>Removes a state and destroys it, with the transitions to it inside this
        /// machine (those of other machines stay, measured).</summary>
        public void RemoveState(AnimatorState state)
        {
            int index = States.IndexOf(state);
            if (index < 0) return;
            States.RemoveAt(index);
            if (index < StatePositions.Count) StatePositions.RemoveAt(index);
            RemoveTransitionsTo(new HashSet<Object> { state }, new HashSet<AnimatorStateMachine>());
            Edited();
            ControllerObjects.DestroyWith(this, state);
        }

        public AnimatorStateMachine AddStateMachine(string name) => AddStateMachine(name, Vector3.zero);

        public AnimatorStateMachine AddStateMachine(string name, Vector3 position)
        {
            var machine = new AnimatorStateMachine { hideFlags = HideFlags.HideInHierarchy, name = MakeUniqueStateMachineName(name) };
            AddStateMachine(machine, position);
            ControllerObjects.AddToAssetOf(this, machine);
            return machine;
        }

        public void AddStateMachine(AnimatorStateMachine stateMachine, Vector3 position)
        {
            if (Machines.Contains(stateMachine))
            {
                Debug.LogWarning($"Sub state machine '{stateMachine.name}' already exists in state machine '{name}', discarding new state machine.");
                return;
            }
            var list = new List<ChildAnimatorStateMachine>(stateMachines) { new ChildAnimatorStateMachine { stateMachine = stateMachine, position = position } };
            stateMachines = list.ToArray();
        }

        /// <summary>Removes a sub-machine and destroys it with everything in it, and the
        /// transitions inside this machine to it or to anything in it (measured).</summary>
        public void RemoveStateMachine(AnimatorStateMachine stateMachine)
        {
            int index = Machines.IndexOf(stateMachine);
            if (index < 0) return;
            Machines.RemoveAt(index);
            if (index < MachinePositions.Count) MachinePositions.RemoveAt(index);
            MachineTransitions.RemoveAll(p => p.Source == stateMachine);
            var removed = new HashSet<Object>();
            if (stateMachine != null)
            {
                foreach (var m in stateMachine.MachinesRecursive()) removed.Add(m);
                foreach (var s in stateMachine.StatesRecursive()) removed.Add(s);
            }
            RemoveTransitionsTo(removed, new HashSet<AnimatorStateMachine>());
            Edited();
            if (stateMachine != null)
            {
                stateMachine.Clear();
                ControllerObjects.DestroyWith(this, stateMachine);
            }
        }

        /// <summary>Drops (and destroys) the transitions inside this machine whose destination is
        /// one of <paramref name="targets"/>.</summary>
        void RemoveTransitionsTo(HashSet<Object> targets, HashSet<AnimatorStateMachine> seen)
        {
            if (!seen.Add(this)) return;
            bool Targets(AnimatorTransitionBase t) => t != null && (t.DstState is AnimatorState s && targets.Contains(s)
                || t.DstStateMachine is AnimatorStateMachine m && targets.Contains(m));
            void Drop(List<AnimatorTransitionBase> list, Object owner)
            {
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (!Targets(list[i])) continue;
                    var t = list[i];
                    list.RemoveAt(i);
                    ControllerObjects.DestroyWith(owner, t);
                }
            }
            foreach (var s in States)
            {
                if (s == null) continue;
                int before = s.Transitions.Count;
                Drop(s.Transitions, s);
                if (s.Transitions.Count != before) s.Edited();
            }
            Drop(AnyStateTransitions, this);
            Drop(EntryTransitions, this);
            foreach (var (_, list) in MachineTransitions) Drop(list, this);
            foreach (var child in Machines)
                if (child != null) child.RemoveTransitionsTo(targets, seen);
        }

        AnimatorStateTransition MakeAnyStateTransition()
        {
            // Any State transitions start with a 0.25 s fixed transition and no exit time (measured).
            var t = new AnimatorStateTransition { hasExitTime = false, hasFixedDuration = true, duration = 0.25f, exitTime = 0.75f, hideFlags = HideFlags.HideInHierarchy };
            ControllerObjects.AddToAssetOf(this, t);
            AnyStateTransitions.Add(t);
            Edited();
            return t;
        }

        public AnimatorStateTransition AddAnyStateTransition(AnimatorState destinationState)
        {
            var t = MakeAnyStateTransition();
            t.destinationState = destinationState;
            return t;
        }

        public AnimatorStateTransition AddAnyStateTransition(AnimatorStateMachine destinationStateMachine)
        {
            var t = MakeAnyStateTransition();
            t.destinationStateMachine = destinationStateMachine;
            return t;
        }

        public bool RemoveAnyStateTransition(AnimatorStateTransition transition)
        {
            if (ReferenceEquals(transition, null) || !AnyStateTransitions.Remove(transition)) return false;
            Edited();
            ControllerObjects.DestroyWith(this, transition);
            return true;
        }

        public AnimatorTransition AddStateMachineTransition(AnimatorStateMachine sourceStateMachine) =>
            Added(sourceStateMachine, new AnimatorTransition());

        public AnimatorTransition AddStateMachineTransition(AnimatorStateMachine sourceStateMachine, AnimatorStateMachine destinationStateMachine) =>
            Added(sourceStateMachine, new AnimatorTransition { destinationStateMachine = destinationStateMachine });

        public AnimatorTransition AddStateMachineTransition(AnimatorStateMachine sourceStateMachine, AnimatorState destinationState) =>
            Added(sourceStateMachine, new AnimatorTransition { destinationState = destinationState });

        public AnimatorTransition AddStateMachineExitTransition(AnimatorStateMachine sourceStateMachine) =>
            Added(sourceStateMachine, new AnimatorTransition { isExit = true });

        AnimatorTransition Added(AnimatorStateMachine sourceStateMachine, AnimatorTransition transition)
        {
            AddStateMachineTransition(sourceStateMachine, transition);
            return transition;
        }

        internal void AddStateMachineTransition(AnimatorStateMachine sourceStateMachine, AnimatorTransition transition)
        {
            transition.hideFlags = HideFlags.HideInHierarchy;
            ControllerObjects.AddToAssetOf(this, transition);
            var list = new List<AnimatorTransition>(GetStateMachineTransitions(sourceStateMachine)) { transition };
            SetStateMachineTransitions(sourceStateMachine, list.ToArray());
        }

        /// <summary>Removes a transition of a sub-machine and destroys it. Answers false whether or
        /// not it was there (measured).</summary>
        public bool RemoveStateMachineTransition(AnimatorStateMachine sourceStateMachine, AnimatorTransition transition)
        {
            var list = new List<AnimatorTransition>(GetStateMachineTransitions(sourceStateMachine));
            list.Remove(transition);
            SetStateMachineTransitions(sourceStateMachine, list.ToArray());
            ControllerObjects.DestroyWith(this, transition);
            return false;
        }

        AnimatorTransition MakeEntryTransition()
        {
            var t = new AnimatorTransition { hideFlags = HideFlags.HideInHierarchy };
            ControllerObjects.AddToAssetOf(this, t);
            EntryTransitions.Add(t);
            Edited();
            return t;
        }

        public AnimatorTransition AddEntryTransition(AnimatorState destinationState)
        {
            var t = MakeEntryTransition();
            t.destinationState = destinationState;
            return t;
        }

        public AnimatorTransition AddEntryTransition(AnimatorStateMachine destinationStateMachine)
        {
            var t = MakeEntryTransition();
            t.destinationStateMachine = destinationStateMachine;
            return t;
        }

        public bool RemoveEntryTransition(AnimatorTransition transition)
        {
            if (ReferenceEquals(transition, null) || !EntryTransitions.Remove(transition)) return false;
            Edited();
            ControllerObjects.DestroyWith(this, transition);
            return true;
        }

        public AnimatorTransition[] GetStateMachineTransitions(AnimatorStateMachine sourceStateMachine)
        {
            foreach (var (source, list) in MachineTransitions)
                if (source == sourceStateMachine) return ControllerObjects.OfType<AnimatorTransition, AnimatorTransitionBase>(list);
            return Array.Empty<AnimatorTransition>();
        }

        /// <summary>Sets a sub-machine's transitions. A transition with neither a destination nor
        /// an exit is left out (measured, tests/projects/animatorapi model.transitions.ofSub).</summary>
        public void SetStateMachineTransitions(AnimatorStateMachine sourceStateMachine, AnimatorTransition[] transitions)
        {
            var kept = new List<AnimatorTransitionBase>();
            foreach (var t in transitions ?? Array.Empty<AnimatorTransition>())
                if (t != null && (t.DstState != null || t.DstStateMachine != null || t.IsExit)) kept.Add(t);
            int index = MachineTransitions.FindIndex(p => p.Source == sourceStateMachine);
            if (index >= 0) MachineTransitions.RemoveAt(index);
            if (kept.Count > 0 && sourceStateMachine != null)
                MachineTransitions.Insert(index >= 0 ? index : MachineTransitions.Count, (sourceStateMachine, kept));
            Edited();
        }

        public StateMachineBehaviour AddStateMachineBehaviour(Type stateMachineBehaviourType)
        {
            var behaviour = StateMachineBehaviours.Create(stateMachineBehaviourType, this);
            if (behaviour == null) return null;
            Behaviours.Add(behaviour);
            Edited();
            return behaviour;
        }

        public T AddStateMachineBehaviour<T>() where T : StateMachineBehaviour => AddStateMachineBehaviour(typeof(T)) as T;

        /// <summary>A state name free among this machine's own states (machines do not count).</summary>
        public string MakeUniqueStateName(string name) => ControllerObjects.Unique(name, n => States.Exists(s => s != null && s.m_Name == n));

        /// <summary>A machine name free among this machine's own sub-machines.</summary>
        public string MakeUniqueStateMachineName(string name) => ControllerObjects.Unique(name, n => Machines.Exists(m => m != null && m.m_Name == n));

        /// <summary>Empties the machine, destroying what it holds in its asset.</summary>
        internal void Clear()
        {
            foreach (var child in Machines)
                if (child != null)
                {
                    child.Clear();
                    ControllerObjects.DestroyWith(this, child);
                }
            foreach (var state in States)
                if (state != null)
                {
                    foreach (var t in state.Transitions) ControllerObjects.DestroyWith(state, t);
                    ControllerObjects.DestroyWith(this, state);
                }
            foreach (var t in AnyStateTransitions) ControllerObjects.DestroyWith(this, t);
            foreach (var t in EntryTransitions) ControllerObjects.DestroyWith(this, t);
            foreach (var (_, list) in MachineTransitions)
                foreach (var t in list) ControllerObjects.DestroyWith(this, t);
            foreach (var b in Behaviours) ControllerObjects.DestroyWith(this, b);
            States.Clear();
            StatePositions.Clear();
            Machines.Clear();
            MachinePositions.Clear();
            AnyStateTransitions.Clear();
            EntryTransitions.Clear();
            MachineTransitions.Clear();
            Behaviours.Clear();
            DefaultState = null;
            Edited();
        }

        /// <summary>This machine and its sub-machines, parents first.</summary>
        internal IEnumerable<AnimatorStateMachine> MachinesRecursive()
        {
            foreach (var (machine, _) in MachinesRecursive(""))
                yield return machine;
        }

        /// <summary>This machine and its sub-machines with their full paths ("Layer.Sub").</summary>
        internal IEnumerable<(AnimatorStateMachine Machine, string Path)> MachinesRecursive(string path)
        {
            var seen = new HashSet<AnimatorStateMachine>();
            var pending = new Stack<(AnimatorStateMachine, string)>();
            pending.Push((this, path));
            while (pending.Count > 0)
            {
                var (machine, at) = pending.Pop();
                if (!seen.Add(machine)) continue;
                yield return (machine, at);
                for (int i = machine.Machines.Count - 1; i >= 0; i--)
                    if (machine.Machines[i] is AnimatorStateMachine child && child != null) pending.Push((child, at + "." + child.m_Name));
            }
        }

        /// <summary>The states of this machine, then those of its sub-machines (parents first).</summary>
        internal IEnumerable<AnimatorState> StatesRecursive()
        {
            foreach (var machine in MachinesRecursive())
                foreach (var state in machine.States)
                    if (state != null) yield return state;
        }

        /// <summary>The state in this machine or its sub-machines with the smallest full path hash
        /// (unsigned), the machine's path being <paramref name="path"/>; null without states.</summary>
        internal AnimatorState FirstStateByPathHash(string path)
        {
            AnimatorState best = null;
            uint bestHash = 0;
            foreach (var (machine, at) in MachinesRecursive(path))
                foreach (var state in machine.States)
                {
                    if (state == null) continue;
                    uint hash = unchecked((uint)Animator.StringToHash(at + "." + state.m_Name));
                    if (best == null || hash < bestHash)
                    {
                        best = state;
                        bestHash = hash;
                    }
                }
            return best;
        }

        internal bool EditedSince(int version, HashSet<Object> seen)
        {
            if (!seen.Add(this)) return false;
            if (m_EditStamp > version) return true;
            foreach (var t in AnyStateTransitions) if (t != null && t.m_EditStamp > version) return true;
            foreach (var t in EntryTransitions) if (t != null && t.m_EditStamp > version) return true;
            foreach (var (_, list) in MachineTransitions)
                foreach (var t in list) if (t != null && t.m_EditStamp > version) return true;
            foreach (var s in States) if (s != null && s.EditedSince(version, seen)) return true;
            foreach (var m in Machines) if (m != null && m.EditedSince(version, seen)) return true;
            return false;
        }

        internal void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            foreach (var child in body.Get("m_ChildStates")?.Seq() ?? Array.Empty<YNode>())
                if (RuntimeAnimatorController.Resolve<AnimatorState>(resolver, child.Ref("m_State")) is AnimatorState s)
                {
                    States.Add(s);
                    StatePositions.Add(child.Vector3("m_Position"));
                }
            foreach (var child in body.Get("m_ChildStateMachines")?.Seq() ?? Array.Empty<YNode>())
                if (RuntimeAnimatorController.Resolve<AnimatorStateMachine>(resolver, child.Ref("m_StateMachine")) is AnimatorStateMachine m)
                {
                    Machines.Add(m);
                    MachinePositions.Add(child.Vector3("m_Position"));
                }
            ReadTransitions(body.Get("m_AnyStateTransitions"), resolver, AnyStateTransitions);
            ReadTransitions(body.Get("m_EntryTransitions"), resolver, EntryTransitions);
            var machineTransitions = body.Get("m_StateMachineTransitions");
            foreach (var pair in machineTransitions?.Seq() ?? Array.Empty<YNode>())
            {
                var source = RuntimeAnimatorController.Resolve<AnimatorStateMachine>(resolver, pair.Ref("first"));
                var list = new List<AnimatorTransitionBase>();
                ReadTransitions(pair.Get("second"), resolver, list);
                if (source != null) MachineTransitions.Add((source, list));
            }
            foreach (var b in body.Get("m_StateMachineBehaviours")?.Seq() ?? Array.Empty<YNode>())
                if (RuntimeAnimatorController.Resolve<StateMachineBehaviour>(resolver, b.AsRef()) is StateMachineBehaviour smb) Behaviours.Add(smb);
            DefaultState = RuntimeAnimatorController.Resolve<AnimatorState>(resolver, body.Ref("m_DefaultState"));
            m_AnyStatePosition = body.Vector3("m_AnyStatePosition", k_AnyStatePosition);
            m_EntryPosition = body.Vector3("m_EntryPosition", k_EntryPosition);
            m_ExitPosition = body.Vector3("m_ExitPosition", k_ExitPosition);
            m_ParentStateMachinePosition = body.Vector3("m_ParentStateMachinePosition", k_ParentStateMachinePosition);
        }

        internal static void ReadTransitions(YNode list, IReferenceResolver resolver, List<AnimatorTransitionBase> into)
        {
            foreach (var item in list?.Seq() ?? Array.Empty<YNode>())
                if (RuntimeAnimatorController.Resolve<AnimatorTransitionBase>(resolver, item.AsRef()) is AnimatorTransitionBase t) into.Add(t);
        }
    }

    /// <summary>AnimatorState (class 1102), read and edited as <see cref="AnimatorStateMachine"/> is.</summary>
    [ClassId(1102)]
    [HelpURL("class-State")]
    public sealed partial class AnimatorState : Object
    {
        public AnimatorState() { }

        internal override void SetName(string value) => m_Name = ControllerObjects.StateName(value);

        public int nameHash => Animator.StringToHash(name);
        public Motion motion { get => Motion; set { Motion = value; Edited(); } }
        public float speed { get => Speed; set { Speed = value; Edited(); } }
        public float cycleOffset { get => CycleOffset; set { CycleOffset = value; Edited(); } }
        public bool mirror { get => Mirror; set { Mirror = value; Edited(); } }
        public bool iKOnFeet { get => IKOnFeet; set { IKOnFeet = value; Edited(); } }
        public bool writeDefaultValues { get => WriteDefaults; set { WriteDefaults = value; Edited(); } }
        public string tag { get => Tag; set { Tag = value ?? ""; Edited(); } }

        public AnimatorStateTransition[] transitions
        {
            get => ControllerObjects.OfType<AnimatorStateTransition, AnimatorTransitionBase>(Transitions);
            set { ControllerObjects.Assign(Transitions, value); Edited(); }
        }

        public StateMachineBehaviour[] behaviours
        {
            get => Behaviours.ToArray();
            set { ControllerObjects.Assign(Behaviours, value); Edited(); }
        }

        internal float Speed = 1f;
        internal float CycleOffset;
        internal readonly List<AnimatorTransitionBase> Transitions = new List<AnimatorTransitionBase>();
        internal readonly List<StateMachineBehaviour> Behaviours = new List<StateMachineBehaviour>();
        internal bool WriteDefaults = true;
        internal bool IKOnFeet;
        internal bool Mirror;
        internal Motion Motion;
        internal string Tag = "";
        internal int m_EditStamp;

        /// <summary>The state's own position field, which the file keeps apart from the machine's
        /// (50, 50, 0 for states the API makes, measured).</summary>
        internal Vector3 m_Position = new Vector3(50, 50, 0);

        internal void Edited()
        {
            m_EditStamp = ControllerEdits.Next();
            ControllerObjects.Dirty(this);
        }

        // A state's optional parameters keep their names whether or not they are active; a new
        // state has empty names and none active (Unity 6000.0.71f1, tests/projects/defaultstate).
        string m_SpeedParameter = "", m_CycleOffsetParameter = "", m_TimeParameter = "", m_MirrorParameter = "";
        bool m_SpeedParameterActive, m_CycleOffsetParameterActive, m_MirrorParameterActive, m_TimeParameterActive;
        public string speedParameter { get => m_SpeedParameter; set { m_SpeedParameter = value ?? ""; Edited(); } }
        public string cycleOffsetParameter { get => m_CycleOffsetParameter; set { m_CycleOffsetParameter = value ?? ""; Edited(); } }
        public string mirrorParameter { get => m_MirrorParameter; set { m_MirrorParameter = value ?? ""; Edited(); } }
        public string timeParameter { get => m_TimeParameter; set { m_TimeParameter = value ?? ""; Edited(); } }
        public bool speedParameterActive { get => m_SpeedParameterActive; set { m_SpeedParameterActive = value; Edited(); } }
        public bool cycleOffsetParameterActive { get => m_CycleOffsetParameterActive; set { m_CycleOffsetParameterActive = value; Edited(); } }
        public bool mirrorParameterActive { get => m_MirrorParameterActive; set { m_MirrorParameterActive = value; Edited(); } }
        public bool timeParameterActive { get => m_TimeParameterActive; set { m_TimeParameterActive = value; Edited(); } }

        /// <summary>The parameter each multiplier reads: its name when active, else null (setting
        /// null deactivates it).</summary>
        internal string SpeedParameter
        {
            get => speedParameterActive ? m_SpeedParameter : null;
            set { speedParameterActive = value != null; speedParameter = value; }
        }
        internal string CycleOffsetParameter
        {
            get => cycleOffsetParameterActive ? m_CycleOffsetParameter : null;
            set { cycleOffsetParameterActive = value != null; cycleOffsetParameter = value; }
        }
        internal string TimeParameter
        {
            get => timeParameterActive ? m_TimeParameter : null;
            set { timeParameterActive = value != null; timeParameter = value; }
        }
        internal string MirrorParameter
        {
            get => mirrorParameterActive ? m_MirrorParameter : null;
            set { mirrorParameterActive = value != null; mirrorParameter = value; }
        }

        [Obsolete("uniqueName does not exist anymore. Consider using .name instead.", true)]
        [Unbound.EditorFaithful("an obsolete member: empty, as a new state answers in Unity 6000.0.71f1 (tests/projects/defaultstate)")]
        public string uniqueName => string.Empty;
        [Obsolete("uniqueNameHash does not exist anymore.", true)]
        [Unbound.EditorFaithful("an obsolete member: -1, as a new state answers in Unity 6000.0.71f1 (tests/projects/defaultstate)")]
        public int uniqueNameHash => -1;

        [Obsolete("GetMotion() is obsolete. Use motion", true)]
        [Unbound.EditorFaithful("an obsolete member that answers null in Unity 6000.0.71f1")]
        public Motion GetMotion() => null;

        public void AddTransition(AnimatorStateTransition transition)
        {
            Transitions.Add(transition);
            Edited();
        }

        public void RemoveTransition(AnimatorStateTransition transition)
        {
            Transitions.Remove(transition);
            Edited();
            ControllerObjects.DestroyWith(this, transition);
        }

        /// <summary>A new transition out of this state: a fixed duration and no exit time; with
        /// <paramref name="defaultExitTime"/>, an exit time 0.25 s before the motion's end and a
        /// 0.25 s duration (0.75 without a motion that lasts), measured.</summary>
        AnimatorStateTransition CreateTransition(bool defaultExitTime)
        {
            var t = new AnimatorStateTransition { hasExitTime = false, hasFixedDuration = true };
            ControllerObjects.AddToAssetOf(this, t);
            t.hideFlags = HideFlags.HideInHierarchy;
            if (defaultExitTime)
            {
                const float defaultDuration = 0.25f;
                t.hasExitTime = true;
                t.duration = defaultDuration;
                t.exitTime = Motion != null && Motion.averageDuration > 0f ? 1f - defaultDuration / Motion.averageDuration : 0.75f;
            }
            return t;
        }

        public AnimatorStateTransition AddTransition(AnimatorState destinationState) => AddTransition(destinationState, false);

        public AnimatorStateTransition AddTransition(AnimatorStateMachine destinationStateMachine) => AddTransition(destinationStateMachine, false);

        public AnimatorStateTransition AddTransition(AnimatorState destinationState, bool defaultExitTime)
        {
            var t = CreateTransition(defaultExitTime);
            t.destinationState = destinationState;
            AddTransition(t);
            return t;
        }

        public AnimatorStateTransition AddTransition(AnimatorStateMachine destinationStateMachine, bool defaultExitTime)
        {
            var t = CreateTransition(defaultExitTime);
            t.destinationStateMachine = destinationStateMachine;
            AddTransition(t);
            return t;
        }

        public AnimatorStateTransition AddExitTransition() => AddExitTransition(false);

        public AnimatorStateTransition AddExitTransition(bool defaultExitTime)
        {
            var t = CreateTransition(defaultExitTime);
            t.isExit = true;
            AddTransition(t);
            return t;
        }

        public StateMachineBehaviour AddStateMachineBehaviour(Type stateMachineBehaviourType)
        {
            var behaviour = StateMachineBehaviours.Create(stateMachineBehaviourType, this);
            if (behaviour == null) return null;
            Behaviours.Add(behaviour);
            Edited();
            return behaviour;
        }

        public T AddStateMachineBehaviour<T>() where T : StateMachineBehaviour => AddStateMachineBehaviour(typeof(T)) as T;

        internal bool EditedSince(int version, HashSet<Object> seen)
        {
            if (!seen.Add(this)) return false;
            if (m_EditStamp > version) return true;
            foreach (var t in Transitions) if (t != null && t.m_EditStamp > version) return true;
            return Motion is BlendTree tree && tree.EditedSince(version, seen);
        }

        internal void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            Speed = body.Float("m_Speed", 1f);
            CycleOffset = body.Float("m_CycleOffset");
            AnimatorStateMachine.ReadTransitions(body.Get("m_Transitions"), resolver, Transitions);
            foreach (var b in body.Get("m_StateMachineBehaviours")?.Seq() ?? Array.Empty<YNode>())
                if (RuntimeAnimatorController.Resolve<StateMachineBehaviour>(resolver, b.AsRef()) is StateMachineBehaviour smb) Behaviours.Add(smb);
            m_Position = body.Vector3("m_Position", m_Position);
            WriteDefaults = body.Bool("m_WriteDefaultValues", true);
            IKOnFeet = body.Bool("m_IKOnFeet");
            Mirror = body.Bool("m_Mirror");
            var motion = body.Ref("m_Motion");
            Motion = RuntimeAnimatorController.Resolve<Motion>(resolver, motion);
            if (Motion == null && !motion.IsNull) MissingMotion.Report(motion, $"animator state '{m_Name}'");
            Tag = body.Str("m_Tag");
            m_SpeedParameter = body.Str("m_SpeedParameter");
            m_CycleOffsetParameter = body.Str("m_CycleOffsetParameter");
            m_TimeParameter = body.Str("m_TimeParameter");
            m_MirrorParameter = body.Str("m_MirrorParameter");
            m_SpeedParameterActive = body.Bool("m_SpeedParameterActive");
            m_CycleOffsetParameterActive = body.Bool("m_CycleOffsetParameterActive");
            m_TimeParameterActive = body.Bool("m_TimeParameterActive");
            m_MirrorParameterActive = body.Bool("m_MirrorParameterActive");
        }
    }

    public partial struct AnimatorCondition
    {
        AnimatorConditionMode m_ConditionMode;
        string m_ConditionEvent;
        float m_EventTreshold;

        public AnimatorConditionMode mode { get => m_ConditionMode; set => m_ConditionMode = value; }
        public string parameter { get => m_ConditionEvent; set => m_ConditionEvent = value; }
        public float threshold { get => m_EventTreshold; set => m_EventTreshold = value; }
    }

    /// <summary>The base of transitions (AnimatorTransition and AnimatorStateTransition): their
    /// destination and conditions. The state transition's timing fields live here too, at the
    /// defaults an entry or machine transition plays with.</summary>
    public partial class AnimatorTransitionBase : Object
    {
        protected AnimatorTransitionBase() { }

        internal readonly List<(int Mode, string Parameter, float Threshold)> Conditions = new List<(int, string, float)>();
        internal AnimatorState DstState;
        internal AnimatorStateMachine DstStateMachine;
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
        internal int m_EditStamp;

        internal void Edited()
        {
            m_EditStamp = ControllerEdits.Next();
            ControllerObjects.Dirty(this);
        }

        public bool solo { get => Solo; set { Solo = value; Edited(); } }
        public bool mute { get => Mute; set { Mute = value; Edited(); } }
        public bool isExit { get => IsExit; set { IsExit = value; Edited(); } }
        public AnimatorStateMachine destinationStateMachine { get => DstStateMachine; set { DstStateMachine = value; Edited(); } }
        public AnimatorState destinationState { get => DstState; set { DstState = value; Edited(); } }

        public AnimatorCondition[] conditions
        {
            get
            {
                var result = new AnimatorCondition[Conditions.Count];
                for (int i = 0; i < result.Length; i++)
                    result[i] = new AnimatorCondition { mode = (AnimatorConditionMode)Conditions[i].Mode, parameter = Conditions[i].Parameter, threshold = Conditions[i].Threshold };
                return result;
            }
            set
            {
                Conditions.Clear();
                foreach (var c in value ?? Array.Empty<AnimatorCondition>())
                    Conditions.Add(((int)c.mode, c.parameter, c.threshold));
                Edited();
            }
        }

        public void AddCondition(AnimatorConditionMode mode, float threshold, string parameter)
        {
            Conditions.Add(((int)mode, parameter, threshold));
            Edited();
        }

        /// <summary>Removes the first condition equal to <paramref name="condition"/> in every field.</summary>
        public void RemoveCondition(AnimatorCondition condition)
        {
            int index = Conditions.IndexOf(((int)condition.mode, condition.parameter, condition.threshold));
            if (index >= 0) Conditions.RemoveAt(index);
            Edited();
        }

        /// <summary>"Source -> Destination": a state's name, else a machine's (null: "Entry"); the
        /// destination state's name, else the destination machine's, else "Exit" for an exit,
        /// else "INVALID" (measured, tests/projects/animatorapi transitions.*, extra.*).</summary>
        public string GetDisplayName(Object source) =>
            BuildName(source is AnimatorState state ? state.name : source is AnimatorStateMachine machine ? machine.name : "Entry", DestinationName());

        internal string DestinationName() => DstState != null ? DstState.name : DstStateMachine != null ? DstStateMachine.name : IsExit ? "Exit" : "INVALID";

        internal static string BuildName(string source, string destination) => source + " -> " + destination;

        internal virtual void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            foreach (var c in body.Get("m_Conditions")?.Seq() ?? Array.Empty<YNode>())
                Conditions.Add((c.Int("m_ConditionMode"), c.Str("m_ConditionEvent"), c.Float("m_EventTreshold")));
            DstState = RuntimeAnimatorController.Resolve<AnimatorState>(resolver, body.Ref("m_DstState"));
            DstStateMachine = RuntimeAnimatorController.Resolve<AnimatorStateMachine>(resolver, body.Ref("m_DstStateMachine"));
            Solo = body.Bool("m_Solo");
            Mute = body.Bool("m_Mute");
            IsExit = body.Bool("m_IsExit");
        }
    }

    /// <summary>AnimatorTransition (class 1109): an entry or state machine transition.</summary>
    [ClassId(1109)]
    public partial class AnimatorTransition : AnimatorTransitionBase
    {
        public AnimatorTransition() { }
    }

    /// <summary>AnimatorStateTransition (class 1101): a transition out of a state or Any State. A
    /// new one lasts 0.1 s and leaves at 0.9 with an exit time (measured, tests/projects/animatorapi
    /// extra.newStateTransition).</summary>
    [ClassId(1101)]
    [HelpURL("class-Transition")]
    public partial class AnimatorStateTransition : AnimatorTransitionBase
    {
        // A new transition's timing (measured).
        const float k_NewDuration = 0.1f, k_NewExitTime = 0.9f;

        public AnimatorStateTransition()
        {
            Duration = k_NewDuration;
            ExitTime = k_NewExitTime;
            HasExitTime = true;
        }

        public float duration { get => Duration; set { Duration = value; Edited(); } }
        public float offset { get => Offset; set { Offset = value; Edited(); } }
        public TransitionInterruptionSource interruptionSource { get => (TransitionInterruptionSource)InterruptionSource; set { InterruptionSource = (int)value; Edited(); } }
        public bool orderedInterruption { get => OrderedInterruption; set { OrderedInterruption = value; Edited(); } }
        public float exitTime { get => ExitTime; set { ExitTime = value; Edited(); } }
        public bool hasExitTime { get => HasExitTime; set { HasExitTime = value; Edited(); } }
        public bool hasFixedDuration { get => HasFixedDuration; set { HasFixedDuration = value; Edited(); } }
        public bool canTransitionToSelf { get => CanTransitionToSelf; set { CanTransitionToSelf = value; Edited(); } }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
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

    public partial struct ChildMotion
    {
        Motion m_Motion;
        float m_Threshold;
        Vector2 m_Position;
        float m_TimeScale;
        float m_CycleOffset;
        string m_DirectBlendParameter;
        bool m_Mirror;

        public Motion motion { get => m_Motion; set => m_Motion = value; }
        public float threshold { get => m_Threshold; set => m_Threshold = value; }
        public Vector2 position { get => m_Position; set => m_Position = value; }
        public float timeScale { get => m_TimeScale; set => m_TimeScale = value; }
        public float cycleOffset { get => m_CycleOffset; set => m_CycleOffset = value; }
        public string directBlendParameter { get => m_DirectBlendParameter; set => m_DirectBlendParameter = value; }
        /// <summary>Toggles the state's mirror for this child (humanoid clips).</summary>
        public bool mirror { get => m_Mirror; set => m_Mirror = value; }
    }

    /// <summary>BlendTree (class 206): a motion blending its children by parameters. With
    /// automatic thresholds, setting the children or the range spreads the thresholds evenly from
    /// the minimum to the maximum (measured, tests/projects/animatorapi model.trees.*).</summary>
    [ClassId(206)]
    [ExcludeFromPreset]
    public partial class BlendTree : Motion
    {
        public BlendTree() { }

        internal readonly List<ChildMotion> Children = new List<ChildMotion>();
        internal string Parameter = k_DefaultParameter;
        internal string ParameterY = k_DefaultParameter;
        /// <summary>0 Simple1D, 1 SimpleDirectional2D, 2 FreeformDirectional2D, 3 FreeformCartesian2D, 4 Direct.</summary>
        internal int BlendType;
        internal bool NormalizedBlendValues;
        internal bool UseAutomaticThresholds = true;
        internal float MinThreshold;
        internal float MaxThreshold = 1f;
        internal int m_EditStamp;

        /// <summary>The parameter a new tree and a new child of a Direct tree read (measured).</summary>
        const string k_DefaultParameter = "Blend";

        void Edited()
        {
            m_EditStamp = ControllerEdits.Next();
            ControllerObjects.Dirty(this);
        }

        public string blendParameter { get => Parameter; set { Parameter = value ?? ""; Edited(); } }
        public string blendParameterY { get => ParameterY; set { ParameterY = value ?? ""; Edited(); } }
        public BlendTreeType blendType { get => (BlendTreeType)BlendType; set { BlendType = (int)value; Edited(); } }

        public ChildMotion[] children
        {
            get => Children.ToArray();
            set
            {
                ControllerObjects.Assign(Children, value);
                if (UseAutomaticThresholds) SpreadThresholds();
                Edited();
            }
        }

        public bool useAutomaticThresholds
        {
            get => UseAutomaticThresholds;
            set
            {
                UseAutomaticThresholds = value;
                if (value) SpreadThresholds();
                Edited();
            }
        }

        public float minThreshold
        {
            get => MinThreshold;
            set
            {
                MinThreshold = value;
                if (UseAutomaticThresholds) SpreadThresholds();
                Edited();
            }
        }

        public float maxThreshold
        {
            get => MaxThreshold;
            set
            {
                MaxThreshold = value;
                if (UseAutomaticThresholds) SpreadThresholds();
                Edited();
            }
        }

        /// <summary>Thresholds from the minimum to the maximum in equal steps, in single precision
        /// as the Editor computes them (its file holds 0.66666675 for the second of four from -1 to 4).</summary>
        void SpreadThresholds()
        {
            int n = Children.Count;
            for (int i = 0; i < n; i++)
            {
                var child = Children[i];
                child.threshold = n == 1 ? MinThreshold : MinThreshold + (MaxThreshold - MinThreshold) * ((float)i / (n - 1));
                Children[i] = child;
            }
        }

        public void AddChild(Motion motion) => AddChild(motion, Vector2.zero, 0f);

        public void AddChild(Motion motion, Vector2 position) => AddChild(motion, position, 0f);

        public void AddChild(Motion motion, float threshold) => AddChild(motion, Vector2.zero, threshold);

        internal void AddChild(Motion motion, Vector2 position, float threshold)
        {
            var list = new List<ChildMotion>(Children)
            {
                new ChildMotion { timeScale = 1f, motion = motion, position = position, threshold = threshold, directBlendParameter = k_DefaultParameter },
            };
            children = list.ToArray();
        }

        public void RemoveChild(int index)
        {
            var list = new List<ChildMotion>(Children);
            list.RemoveAt(index);
            children = list.ToArray();
        }

        public BlendTree CreateBlendTreeChild(float threshold) => CreateBlendTreeChild(Vector2.zero, threshold);

        public BlendTree CreateBlendTreeChild(Vector2 position) => CreateBlendTreeChild(position, 0f);

        internal BlendTree CreateBlendTreeChild(Vector2 position, float threshold)
        {
            var tree = new BlendTree { name = "BlendTree", hideFlags = HideFlags.HideInHierarchy };
            ControllerObjects.AddToAssetOf(this, tree);
            AddChild(tree, position, threshold);
            return tree;
        }

        /// <summary>The parameters the tree reads, its nested trees' included, each once: a 1D
        /// tree's parameter, a 2D tree's two, a Direct tree's children's.</summary>
        internal void CollectParameters(List<string> into, HashSet<BlendTree> seen)
        {
            if (!seen.Add(this)) return;
            void Add(string name)
            {
                if (!into.Contains(name)) into.Add(name);
            }
            switch ((BlendTreeType)BlendType)
            {
                case BlendTreeType.Simple1D:
                    Add(Parameter);
                    break;
                case BlendTreeType.Direct:
                    foreach (var child in Children) Add(child.directBlendParameter ?? "");
                    break;
                default:
                    Add(Parameter);
                    Add(ParameterY);
                    break;
            }
            foreach (var child in Children)
                if (child.motion is BlendTree tree && tree != null) tree.CollectParameters(into, seen);
        }

        /// <summary>The mean of the average durations of the clips the tree plays, nested trees
        /// flattened (measured, tests/projects/animatorapi model.trees.*).</summary>
        internal override float GetAverageDuration()
        {
            float sum = 0f;
            int count = 0;
            Clips(this, new HashSet<BlendTree>(), ref sum, ref count);
            return count == 0 ? 0f : sum / count;

            static void Clips(BlendTree tree, HashSet<BlendTree> seen, ref float sum, ref int count)
            {
                if (!seen.Add(tree)) return;
                foreach (var child in tree.Children)
                {
                    if (child.motion is BlendTree nested && nested != null) Clips(nested, seen, ref sum, ref count);
                    else if (child.motion is AnimationClip clip && clip != null && !clip.legacy)
                    {
                        sum += clip.averageDuration;
                        count++;
                    }
                }
            }
        }

        /// <summary>The unweighted arithmetic mean of averageSpeed, averageAngularSpeed and
        /// apparentSpeed across all flattened non-legacy clips in the tree (matches the Unity
        /// 6000.0.71f1 Editor, tests/projects/animrest <c>blendtree.*</c>).</summary>
        internal override (Vector3 Speed, float AngularSpeed, float ApparentSpeed) GetMotionStats()
        {
            var speedSum = Vector3.zero;
            float angularSum = 0f, apparentSum = 0f;
            int count = 0;
            Accumulate(this, new HashSet<BlendTree>(), ref speedSum, ref angularSum, ref apparentSum, ref count);
            if (count == 0) return (Vector3.zero, 0f, 0f);
            float inv = 1f / count;
            return (new Vector3(speedSum.x * inv, speedSum.y * inv, speedSum.z * inv), angularSum * inv, apparentSum * inv);

            static void Accumulate(BlendTree tree, HashSet<BlendTree> seen, ref Vector3 speedSum,
                ref float angularSum, ref float apparentSum, ref int count)
            {
                if (!seen.Add(tree)) return;
                foreach (var child in tree.Children)
                {
                    if (child.motion is BlendTree nested && nested != null)
                        Accumulate(nested, seen, ref speedSum, ref angularSum, ref apparentSum, ref count);
                    else if (child.motion is AnimationClip clip && clip != null && !clip.legacy)
                    {
                        var (s, ang, app) = clip.GetMotionStats();
                        speedSum += s;
                        angularSum += ang;
                        apparentSum += app;
                        count++;
                    }
                }
            }
        }

        /// <summary>Destroys the tree and the trees nested in it that are in <paramref name="owner"/>'s asset.</summary>
        internal void DestroyRecursive(Object owner)
        {
            foreach (var child in Children)
                if (child.motion is BlendTree tree && tree != null) tree.DestroyRecursive(owner);
            ControllerObjects.DestroyWith(owner, this);
        }

        internal bool EditedSince(int version, HashSet<Object> seen)
        {
            if (!seen.Add(this)) return false;
            if (m_EditStamp > version) return true;
            foreach (var child in Children)
                if (child.motion is BlendTree tree && tree != null && tree.EditedSince(version, seen)) return true;
            return false;
        }

        internal void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            foreach (var c in body.Get("m_Childs")?.Seq() ?? Array.Empty<YNode>())
            {
                var reference = c.Ref("m_Motion");
                var resolved = RuntimeAnimatorController.Resolve<Motion>(resolver, reference);
                if (resolved == null && !reference.IsNull) MissingMotion.Report(reference, $"blend tree '{m_Name}'");
                Children.Add(new ChildMotion
                {
                    motion = resolved,
                    threshold = c.Float("m_Threshold"),
                    position = c.Vector2("m_Position"),
                    timeScale = c.Float("m_TimeScale", 1f),
                    cycleOffset = c.Float("m_CycleOffset"),
                    mirror = c.Bool("m_Mirror"),
                    directBlendParameter = c.Str("m_DirectBlendParameter"),
                });
            }
            Parameter = body.Str("m_BlendParameter");
            ParameterY = body.Str("m_BlendParameterY");
            BlendType = body.Int("m_BlendType");
            NormalizedBlendValues = body.Bool("m_NormalizedBlendValues");
            UseAutomaticThresholds = body.Bool("m_UseAutomaticThresholds", true);
            MinThreshold = body.Float("m_MinThreshold");
            MaxThreshold = body.Float("m_MaxThreshold", 1f);
        }

        /// <summary>Whether every child loops (an empty tree does); the state playing the tree
        /// reports the same (Unity 6000.0.71f1, tests/projects/animatorapi extra.loop.*,
        /// extra.loopState.*: one non-looping child of two makes the tree not loop). A child
        /// without a motion counts as not looping (unmeasured).</summary>
        internal override bool GetIsLooping()
        {
            foreach (var c in Children)
                if (c.motion == null || !c.motion.isLooping) return false;
            return true;
        }

        /// <summary>Whether every child is a human motion: an empty tree is, trees of generic clips
        /// are not (Unity 6000.0.71f1, tests/projects/defaultstate BlendTree, animatorapi
        /// extra.loop.*); whether one human child is enough is unmeasured.</summary>
        internal override bool GetIsHumanMotion()
        {
            foreach (var c in Children)
                if (c.motion == null || !c.motion.isHumanMotion) return false;
            return true;
        }
    }
}

namespace UnityEngine
{
    /// <summary>A component that names clips it plays (AnimationUtility.GetAnimationClips lists them).</summary>
    public partial interface IAnimationClipSource
    {
        void GetAnimationClips(System.Collections.Generic.List<AnimationClip> results);
    }
}
