using System;
using System.Collections.Generic;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>
    /// An animator controller compiled for playback: parameters by index, layers with their state
    /// machines, states with name hashes as Unity computes them (CRC32 of "Layer.Sub.State"),
    /// transitions with conditions resolved to parameter indices.
    /// </summary>
    internal sealed class ControllerGraph
    {
        internal ParameterDef[] Parameters = Array.Empty<ParameterDef>();
        internal readonly Dictionary<int, int> ParameterByHash = new Dictionary<int, int>();
        internal LayerDef[] Layers = Array.Empty<LayerDef>();
        /// <summary>Every clip the states (and blend trees) play, before overrides.</summary>
        internal readonly List<AnimationClip> Clips = new List<AnimationClip>();

        internal int ParameterIndex(string name) =>
            name != null && ParameterByHash.TryGetValue(Animator.StringToHash(name), out var i) ? i : -1;

        internal static ControllerGraph Build(RuntimeAnimatorController controller)
        {
            var g = new ControllerGraph();
            var parameters = new List<ParameterDef>();
            foreach (var p in controller.m_Parameters)
            {
                var def = new ParameterDef
                {
                    Name = p.m_Name,
                    Hash = Animator.StringToHash(p.m_Name),
                    Type = p.m_Type,
                    DefaultFloat = p.m_DefaultFloat,
                    DefaultInt = p.m_DefaultInt,
                    DefaultBool = p.m_DefaultBool,
                    Source = p,
                };
                if (g.ParameterByHash.ContainsKey(def.Hash)) continue;
                g.ParameterByHash[def.Hash] = parameters.Count;
                parameters.Add(def);
            }
            g.Parameters = parameters.ToArray();

            var layers = new List<LayerDef>();
            var assetMaps = new List<Dictionary<ControllerStateAsset, StateDef>>();
            for (int i = 0; i < controller.m_Layers.Count; i++)
            {
                var data = controller.m_Layers[i];
                var layer = new LayerDef
                {
                    Index = i,
                    Name = data.Name ?? "",
                    Mask = data.Mask,
                    Additive = data.BlendingMode == 1,
                    DefaultWeight = i == 0 ? 1f : data.DefaultWeight,
                    SyncedLayerIndex = data.SyncedLayerIndex,
                };
                var map = new Dictionary<ControllerStateAsset, StateDef>();
                if (data.SyncedLayerIndex < 0 && data.StateMachine != null)
                {
                    var builder = new Builder(g, layer, map);
                    layer.Root = builder.BuildMachine(data.StateMachine, null, layer.Name);
                    builder.Link();
                }
                layers.Add(layer);
                assetMaps.Add(map);
            }
            // Synced layers play their source layer's states with their own motions and behaviours.
            for (int i = 0; i < layers.Count; i++)
            {
                var layer = layers[i];
                if (layer.SyncedLayerIndex < 0 || layer.SyncedLayerIndex >= layers.Count || layer.SyncedLayerIndex == i) continue;
                var data = controller.m_Layers[i];
                var sourceMap = assetMaps[layer.SyncedLayerIndex];
                foreach (var (state, motion) in data.SyncedMotions)
                {
                    if (!sourceMap.TryGetValue(state, out var def)) continue;
                    layer.SyncedMotions[def] = motion;
                    CollectClips(g, motion);
                }
                foreach (var (state, behaviours) in data.SyncedBehaviours)
                    if (sourceMap.TryGetValue(state, out var def)) layer.SyncedBehaviours[def] = behaviours.ToArray();
            }
            g.Layers = layers.ToArray();
            return g;
        }

        internal static void CollectClips(ControllerGraph g, Motion motion)
        {
            switch (motion)
            {
                case AnimationClip clip:
                    if (!g.Clips.Contains(clip)) g.Clips.Add(clip);
                    break;
                case BlendTreeMotion tree:
                    foreach (var c in tree.Children) CollectClips(g, c.Motion);
                    break;
            }
        }

        sealed class Builder
        {
            readonly ControllerGraph m_Graph;
            readonly LayerDef m_Layer;
            readonly Dictionary<ControllerStateAsset, StateDef> m_States;
            readonly Dictionary<ControllerStateMachineAsset, MachineDef> m_Machines = new Dictionary<ControllerStateMachineAsset, MachineDef>();
            readonly List<(ControllerStateMachineAsset Asset, MachineDef Def)> m_PendingMachines = new List<(ControllerStateMachineAsset, MachineDef)>();
            readonly List<(ControllerStateAsset Asset, StateDef Def)> m_PendingStates = new List<(ControllerStateAsset, StateDef)>();

            internal Builder(ControllerGraph graph, LayerDef layer, Dictionary<ControllerStateAsset, StateDef> states)
            {
                m_Graph = graph;
                m_Layer = layer;
                m_States = states;
            }

            internal MachineDef BuildMachine(ControllerStateMachineAsset asset, MachineDef parent, string path)
            {
                if (m_Machines.TryGetValue(asset, out var existing)) return existing;
                var m = new MachineDef
                {
                    Name = parent == null ? m_Layer.Name : asset.m_Name,
                    Path = path,
                    FullPathHash = Animator.StringToHash(path),
                    Parent = parent,
                    Behaviours = asset.Behaviours.ToArray(),
                };
                m_Machines[asset] = m;
                foreach (var s in asset.States)
                {
                    if (s == null || m_States.ContainsKey(s)) continue;
                    var def = new StateDef
                    {
                        Name = s.m_Name,
                        Machine = m,
                        Layer = m_Layer,
                        NameHash = Animator.StringToHash(s.m_Name),
                        FullPathHash = Animator.StringToHash(path + "." + s.m_Name),
                        LayerPathHash = Animator.StringToHash(m_Layer.Name + "." + s.m_Name),
                        TagHash = Animator.StringToHash(s.Tag ?? ""),
                        Speed = s.Speed,
                        CycleOffset = s.CycleOffset,
                        WriteDefaults = s.WriteDefaults,
                        Motion = s.Motion,
                        SpeedParameter = m_Graph.ParameterIndex(s.SpeedParameter),
                        CycleOffsetParameter = m_Graph.ParameterIndex(s.CycleOffsetParameter),
                        TimeParameter = m_Graph.ParameterIndex(s.TimeParameter),
                        Behaviours = s.Behaviours.ToArray(),
                        Index = m_Layer.States.Count,
                    };
                    m_States[s] = def;
                    m.States.Add(def);
                    m_Layer.States.Add(def);
                    m_Layer.StateByHash.TryAdd(def.FullPathHash, def);
                    m_Layer.StateByHash.TryAdd(def.NameHash, def);
                    m_Layer.StateByHash.TryAdd(def.LayerPathHash, def);
                    CollectClips(m_Graph, s.Motion);
                    m_PendingStates.Add((s, def));
                }
                foreach (var child in asset.Machines)
                    if (child != null) m.Children.Add(BuildMachine(child, m, path + "." + child.m_Name));
                m_PendingMachines.Add((asset, m));
                return m;
            }

            /// <summary>Transitions, once every state and machine exists.</summary>
            internal void Link()
            {
                foreach (var (asset, def) in m_PendingStates)
                    def.Transitions = Transitions(asset.Transitions, def, isAny: false);
                foreach (var (asset, m) in m_PendingMachines)
                {
                    m.AnyState = Transitions(asset.AnyStateTransitions, null, isAny: true);
                    m.Entry = Transitions(asset.EntryTransitions, null, isAny: false);
                    foreach (var (source, list) in asset.MachineTransitions)
                        if (m_Machines.TryGetValue(source, out var sourceDef))
                            m.MachineTransitions[sourceDef] = Transitions(list, null, isAny: false);
                    m.Default = asset.DefaultState != null && m_States.TryGetValue(asset.DefaultState, out var d) ? d : null;
                    m_Layer.AnyState.AddRange(m.AnyState);
                }
            }

            TransitionDef[] Transitions(List<ControllerTransitionAsset> assets, StateDef source, bool isAny)
            {
                var result = new List<TransitionDef>();
                bool solo = assets.Exists(t => t != null && t.Solo);
                foreach (var t in assets)
                {
                    if (t == null || t.Mute || (solo && !t.Solo)) continue;
                    var conditions = new List<ConditionDef>();
                    foreach (var (mode, parameter, threshold) in t.Conditions)
                        conditions.Add(new ConditionDef { Mode = mode, Parameter = m_Graph.ParameterIndex(parameter), Threshold = threshold, Name = parameter });
                    var def = new TransitionDef
                    {
                        Name = t.m_Name ?? "",
                        Conditions = conditions.ToArray(),
                        DstState = t.DstState != null && m_States.TryGetValue(t.DstState, out var ds) ? ds : null,
                        DstMachine = t.DstStateMachine != null && m_Machines.TryGetValue(t.DstStateMachine, out var dm) ? dm : null,
                        IsExit = t.IsExit,
                        Duration = t.Duration,
                        Offset = t.Offset,
                        ExitTime = t.ExitTime,
                        HasExitTime = t.HasExitTime,
                        HasFixedDuration = t.HasFixedDuration,
                        InterruptionSource = t.InterruptionSource,
                        OrderedInterruption = t.OrderedInterruption,
                        CanTransitionToSelf = t.CanTransitionToSelf,
                        IsAnyState = isAny,
                        Source = source,
                        Index = result.Count,
                    };
                    var src = isAny ? "AnyState" : source != null ? m_Layer.Name + "." + source.Name : "";
                    var dst = def.DstState != null ? m_Layer.Name + "." + def.DstState.Name : def.DstMachine?.Path ?? "Exit";
                    def.NameHash = Animator.StringToHash((source?.Name ?? "AnyState") + " -> " + (def.DstState?.Name ?? def.DstMachine?.Name ?? "Exit"));
                    def.FullPathHash = Animator.StringToHash(src + " -> " + dst);
                    def.UserNameHash = Animator.StringToHash(def.Name);
                    result.Add(def);
                }
                return result.ToArray();
            }
        }
    }

    internal sealed class ParameterDef
    {
        internal string Name;
        internal int Hash;
        internal AnimatorControllerParameterType Type;
        internal float DefaultFloat;
        internal int DefaultInt;
        internal bool DefaultBool;
        internal AnimatorControllerParameter Source;
    }

    internal sealed class LayerDef
    {
        internal int Index;
        internal string Name;
        internal MachineDef Root;
        internal AvatarMask Mask;
        internal bool Additive;
        internal float DefaultWeight;
        internal int SyncedLayerIndex = -1;
        internal readonly List<StateDef> States = new List<StateDef>();
        internal readonly Dictionary<int, StateDef> StateByHash = new Dictionary<int, StateDef>();
        /// <summary>Any State transitions of every machine of the layer, root first.</summary>
        internal readonly List<TransitionDef> AnyState = new List<TransitionDef>();
        internal readonly Dictionary<StateDef, Motion> SyncedMotions = new Dictionary<StateDef, Motion>();
        internal readonly Dictionary<StateDef, StateMachineBehaviour[]> SyncedBehaviours = new Dictionary<StateDef, StateMachineBehaviour[]>();
    }

    internal sealed class MachineDef
    {
        internal string Name;
        internal string Path;
        internal int FullPathHash;
        internal MachineDef Parent;
        internal readonly List<StateDef> States = new List<StateDef>();
        internal readonly List<MachineDef> Children = new List<MachineDef>();
        internal TransitionDef[] AnyState = Array.Empty<TransitionDef>();
        internal TransitionDef[] Entry = Array.Empty<TransitionDef>();
        internal readonly Dictionary<MachineDef, TransitionDef[]> MachineTransitions = new Dictionary<MachineDef, TransitionDef[]>();
        internal StateDef Default;
        internal StateMachineBehaviour[] Behaviours = Array.Empty<StateMachineBehaviour>();
    }

    internal sealed class StateDef
    {
        internal string Name;
        internal MachineDef Machine;
        internal LayerDef Layer;
        internal int Index;
        internal int NameHash;
        internal int FullPathHash;
        internal int LayerPathHash;
        internal int TagHash;
        internal float Speed = 1f;
        internal float CycleOffset;
        internal bool WriteDefaults = true;
        internal Motion Motion;
        internal int SpeedParameter = -1;
        internal int CycleOffsetParameter = -1;
        internal int TimeParameter = -1;
        internal TransitionDef[] Transitions = Array.Empty<TransitionDef>();
        internal StateMachineBehaviour[] Behaviours = Array.Empty<StateMachineBehaviour>();
    }

    internal sealed class TransitionDef
    {
        internal string Name;
        internal ConditionDef[] Conditions;
        internal StateDef DstState;
        internal MachineDef DstMachine;
        internal bool IsExit;
        internal float Duration;
        internal float Offset;
        internal float ExitTime;
        internal bool HasExitTime;
        internal bool HasFixedDuration;
        internal int InterruptionSource;
        internal bool OrderedInterruption;
        internal bool CanTransitionToSelf;
        internal bool IsAnyState;
        internal StateDef Source;
        internal int Index;
        internal int NameHash;
        internal int FullPathHash;
        internal int UserNameHash;
    }

    internal struct ConditionDef
    {
        /// <summary>1 If, 2 IfNot, 3 Greater, 4 Less, 6 Equals, 7 NotEqual.</summary>
        internal int Mode;
        internal int Parameter;
        internal float Threshold;
        internal string Name;
    }
}
