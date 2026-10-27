using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>
    /// An AnimatorController in player form (built into an AssetBundle or a player): one compiled
    /// ControllerConstant instead of the editor's state machine, state, transition and blend tree
    /// objects. It is turned back into those objects (ControllerGraph compiles them as an editor
    /// controller's): names come from the controller's table of strings by their CRC32 hashes
    /// (m_TOS), sub-state machines from the states' full paths, entry and exit from the selector
    /// states (destinations from 30000 on), clips by index into m_AnimationClips.
    /// </summary>
    internal static class CompiledController
    {
        const uint Selector = 30000;
        const uint None = 0xFFFFFFFFu;

        sealed class Context
        {
            internal readonly Dictionary<uint, string> Names = new Dictionary<uint, string>();
            internal readonly List<AnimationClip> Clips = new List<AnimationClip>();
            internal string Name(uint hash) => Names.TryGetValue(hash, out var name) ? name : null;
        }

        static uint U(YNode node, string key) => unchecked((uint)(node.Get(key)?.AsLong(0) ?? 0));

        /// <summary>A node's `data` (an OffsetPtr's target), or the node itself.</summary>
        static YNode Data(YNode node) => node?.Get("data") ?? node;

        static IEnumerable<YNode> Items(YNode node, string key) => node?.Get(key)?.Seq() ?? Array.Empty<YNode>();

        static float Float(YNode node) => float.Parse(node.AsString(), NumberStyles.Float, CultureInfo.InvariantCulture);

        internal static void Read(RuntimeAnimatorController controller, YNode body, IReferenceResolver resolver)
        {
            var ctx = new Context();
            foreach (var pair in Items(body, "m_TOS"))
                ctx.Names[U(pair, "first")] = pair.Str("second");
            foreach (var clip in Items(body, "m_AnimationClips"))
            {
                var reference = clip.AsRef();
                ctx.Clips.Add(reference.IsNull ? null : resolver.Resolve(reference, typeof(AnimationClip)) as AnimationClip);
            }
            var constant = body.Get("m_Controller");

            // Parameters: their names, types and defaults (trigger and bool values share a list).
            controller.m_Parameters.Clear();
            var defaults = Data(constant.Get("m_DefaultValues"));
            var floats = new List<float>();
            foreach (var v in Items(defaults, "m_FloatValues")) floats.Add(Float(v));
            var ints = new List<int>();
            foreach (var v in Items(defaults, "m_IntValues")) ints.Add((int)v.AsLong(0));
            var bools = new List<bool>();
            foreach (var v in Items(defaults, "m_BoolValues")) bools.Add(v.AsLong(0) != 0 || v.AsString() == "true");
            foreach (var value in Items(Data(constant.Get("m_Values")), "m_ValueArray"))
            {
                uint id = U(value, "m_ID");
                int type = value.Int("m_Type");
                int index = value.Int("m_Index");
                var name = ctx.Name(id);
                if (name == null) continue;
                var p = new AnimatorControllerParameter { m_Name = name, m_Type = (AnimatorControllerParameterType)type };
                if (type == 1 && index < floats.Count) p.m_DefaultFloat = floats[index];
                if (type == 3 && index < ints.Count) p.m_DefaultInt = ints[index];
                if ((type == 4 || type == 9) && index < bools.Count) p.m_DefaultBool = bools[index];
                controller.m_Parameters.Add(p);
            }

            var behaviours = Behaviours(body, resolver);
            var machines = new List<YNode>();
            foreach (var m in Items(constant, "m_StateMachineArray")) machines.Add(Data(m));
            controller.m_Layers.Clear();
            var firstLayerOfMachine = new Dictionary<int, int>();
            int layerIndex = 0;
            foreach (var l in Items(constant, "m_LayerArray"))
            {
                var layer = Data(l);
                int machineIndex = layer.Int("m_StateMachineIndex");
                int syncIndex = layer.Int("m_StateMachineSynchronizedLayerIndex");
                var data = new ControllerLayerData
                {
                    Name = ctx.Name(U(layer, "m_Binding")) ?? "Layer " + layerIndex,
                    BlendingMode = layer.Int("(int&)m_LayerBlendingMode"),
                    DefaultWeight = layer.Float("m_DefaultWeight"),
                    SyncedLayerAffectsTiming = layer.Bool("m_SyncedLayerAffectsTiming"),
                };
                if (Items(Data(layer.Get("m_SkeletonMask")), "m_Data") is var mask && System.Linq.Enumerable.Any(mask))
                    Debug.LogWarning($"Unbound: animator controller '{controller.name}' layer '{data.Name}' has a mask built into its bundle; masks of compiled controllers are not supported yet, the layer animates everything.");
                if (machineIndex >= 0 && machineIndex < machines.Count)
                {
                    if (firstLayerOfMachine.TryGetValue(machineIndex, out int source))
                    {
                        // A synchronized layer: the source layer's states with its own motions.
                        data.SyncedLayerIndex = source;
                        var built = s_Built[controller.m_Layers[source]];
                        int i = 0;
                        foreach (var s in Items(machines[machineIndex], "m_StateConstantArray"))
                        {
                            if (i < built.Count) data.SyncedMotions.Add((built[i], StateMotion(Data(s), syncIndex, ctx)));
                            i++;
                        }
                    }
                    else
                    {
                        firstLayerOfMachine[machineIndex] = layerIndex;
                        data.StateMachine = BuildMachine(machines[machineIndex], data.Name, layerIndex, ctx, behaviours, out var states);
                        s_Built[data] = states;
                    }
                }
                controller.m_Layers.Add(data);
                layerIndex++;
            }
            s_Built.Clear();
        }

        static readonly Dictionary<ControllerLayerData, List<ControllerStateAsset>> s_Built = new Dictionary<ControllerLayerData, List<ControllerStateAsset>>();

        /// <summary>State machine behaviours by the full path hash of their state or machine.</summary>
        static Dictionary<(uint, int), List<StateMachineBehaviour>> Behaviours(YNode body, IReferenceResolver resolver)
        {
            var result = new Dictionary<(uint, int), List<StateMachineBehaviour>>();
            var all = new List<StateMachineBehaviour>();
            foreach (var b in Items(body, "m_StateMachineBehaviours"))
            {
                var reference = b.AsRef();
                all.Add(reference.IsNull ? null : resolver.Resolve(reference, typeof(StateMachineBehaviour)) as StateMachineBehaviour);
            }
            var description = body.Get("m_StateMachineBehaviourVectorDescription");
            var indices = new List<int>();
            foreach (var i in Items(description, "m_StateMachineBehaviourIndices")) indices.Add((int)i.AsLong(0));
            foreach (var pair in Items(description, "m_StateMachineBehaviourRanges"))
            {
                var key = pair.Get("first");
                var range = pair.Get("second");
                var list = new List<StateMachineBehaviour>();
                int start = range.Int("m_StartIndex"), count = range.Int("m_Count");
                for (int k = start; k < start + count && k < indices.Count; k++)
                    if (indices[k] >= 0 && indices[k] < all.Count && all[indices[k]] != null) list.Add(all[indices[k]]);
                result[(U(key, "m_StateID"), key.Int("m_LayerIndex"))] = list;
            }
            return result;
        }

        static T Make<T>(string name) where T : Object, new()
        {
            var o = new T();
            o.m_Name = name;
            return o;
        }

        /// <summary>A layer's state machine with its sub-machines, rebuilt from the states' full paths.</summary>
        static ControllerStateMachineAsset BuildMachine(YNode machine, string layerName, int layerIndex, Context ctx,
            Dictionary<(uint, int), List<StateMachineBehaviour>> behaviours, out List<ControllerStateAsset> states)
        {
            var root = Make<ControllerStateMachineAsset>(layerName);
            var byPath = new Dictionary<string, ControllerStateMachineAsset> { [layerName] = root };
            ControllerStateMachineAsset MachineAt(string path)
            {
                if (byPath.TryGetValue(path, out var m)) return m;
                int dot = path.LastIndexOf('.');
                var parent = dot > 0 ? MachineAt(path.Substring(0, dot)) : root;
                m = Make<ControllerStateMachineAsset>(dot > 0 ? path.Substring(dot + 1) : path);
                parent.Machines.Add(m);
                byPath[path] = m;
                return m;
            }

            var constants = new List<YNode>();
            foreach (var s in Items(machine, "m_StateConstantArray")) constants.Add(Data(s));
            var built = new List<ControllerStateAsset>();
            states = built;
            foreach (var s in constants)
            {
                var name = ctx.Name(U(s, "m_NameID")) ?? "State";
                var full = ctx.Name(U(s, "m_FullPathID")) ?? layerName + "." + name;
                var state = Make<ControllerStateAsset>(name);
                state.Speed = s.Float("m_Speed", 1f);
                state.CycleOffset = s.Float("m_CycleOffset");
                state.WriteDefaults = s.Bool("m_WriteDefaultValues", true);
                state.Mirror = s.Bool("m_Mirror");
                state.Tag = ctx.Name(U(s, "m_TagID")) ?? "";
                state.SpeedParameter = Parameter(ctx, U(s, "m_SpeedParamID"));
                state.CycleOffsetParameter = Parameter(ctx, U(s, "m_CycleOffsetParamID"));
                state.TimeParameter = Parameter(ctx, U(s, "m_TimeParamID"));
                state.Motion = StateMotion(s, 0, ctx);
                if (behaviours.TryGetValue((U(s, "m_FullPathID"), layerIndex), out var list)) state.Behaviours.AddRange(list);
                int dot = full.LastIndexOf('.');
                MachineAt(dot > 0 ? full.Substring(0, dot) : layerName).States.Add(state);
                built.Add(state);
            }

            // Selectors: a machine's entry (its entry transitions, its default) and exit.
            var selectors = new List<YNode>();
            foreach (var sel in Items(machine, "m_SelectorStateConstantArray")) selectors.Add(Data(sel));
            ControllerStateMachineAsset SelectorMachine(YNode sel)
            {
                var path = ctx.Name(U(sel, "m_FullPathID"));
                return path != null && byPath.TryGetValue(path, out var m) ? m : root;
            }
            ControllerTransitionAsset Transition(YNode t, bool state)
            {
                var asset = state ? Make<ControllerStateTransitionAsset>(ctx.Name(U(t, "m_ID")) ?? "") : Make<ControllerTransitionAsset>("");
                foreach (var c in Items(t, "m_ConditionConstantArray"))
                {
                    var cond = Data(c);
                    asset.Conditions.Add((cond.Int("m_ConditionMode"), ctx.Name(U(cond, "m_EventID")), cond.Float("m_EventThreshold")));
                }
                uint destination = state ? U(t, "m_DestinationState") : U(t, "m_Destination");
                if (destination >= Selector && destination != None)
                {
                    int index = (int)(destination - Selector);
                    if (index < selectors.Count)
                    {
                        var target = selectors[index];
                        if (target.Bool("m_IsEntry")) asset.DstStateMachine = SelectorMachine(target);
                        else asset.IsExit = true;
                    }
                }
                else if (destination < built.Count)
                {
                    asset.DstState = built[(int)destination];
                }
                if (state)
                {
                    asset.Duration = t.Float("m_TransitionDuration");
                    asset.Offset = t.Float("m_TransitionOffset");
                    asset.ExitTime = t.Float("m_ExitTime", 0.75f);
                    asset.HasExitTime = t.Bool("m_HasExitTime");
                    asset.HasFixedDuration = t.Bool("m_HasFixedDuration");
                    asset.InterruptionSource = t.Int("m_InterruptionSource");
                    asset.OrderedInterruption = t.Bool("m_OrderedInterruption", true);
                    asset.CanTransitionToSelf = t.Bool("m_CanTransitionToSelf", true);
                }
                return asset;
            }

            for (int i = 0; i < constants.Count; i++)
                foreach (var t in Items(constants[i], "m_TransitionConstantArray"))
                    built[i].Transitions.Add(Transition(Data(t), state: true));
            foreach (var t in Items(machine, "m_AnyStateTransitionConstantArray"))
                root.AnyStateTransitions.Add(Transition(Data(t), state: true));
            foreach (var sel in selectors)
            {
                var m = SelectorMachine(sel);
                var transitions = new List<ControllerTransitionAsset>();
                foreach (var t in Items(sel, "m_TransitionConstantArray")) transitions.Add(Transition(Data(t), state: false));
                if (sel.Bool("m_IsEntry"))
                {
                    // Conditional transitions are the machine's entry transitions; the last,
                    // unconditional one leads to its default state.
                    foreach (var t in transitions)
                    {
                        if (t.Conditions.Count > 0) m.EntryTransitions.Add(t);
                        else if (t.DstState != null && m.DefaultState == null && m != root) m.DefaultState = t.DstState;
                    }
                }
                else if (m != root)
                {
                    // A sub-machine's exit: the parent's transitions from it.
                    var parent = ParentOf(root, m);
                    if (parent != null) parent.MachineTransitions.Add((m, transitions));
                }
            }
            int defaultState = (int)U(machine, "m_DefaultState");
            if (defaultState < built.Count) root.DefaultState = built[defaultState];
            foreach (var pair in byPath)
                if (behaviours.TryGetValue((Animator.StringToHash(pair.Key) is int h ? unchecked((uint)h) : 0, layerIndex), out var list))
                    pair.Value.Behaviours.AddRange(list);
            return root;
        }

        static ControllerStateMachineAsset ParentOf(ControllerStateMachineAsset node, ControllerStateMachineAsset child)
        {
            foreach (var m in node.Machines)
            {
                if (m == child) return node;
                var found = ParentOf(m, child);
                if (found != null) return found;
            }
            return null;
        }

        static string Parameter(Context ctx, uint id) => id == 0 ? null : ctx.Name(id);

        /// <summary>A state's motion in a layer (by synchronized layer index): a clip, or a blend tree.</summary>
        static Motion StateMotion(YNode state, int syncIndex, Context ctx)
        {
            var indices = new List<int>();
            foreach (var i in Items(state, "m_BlendTreeConstantIndexArray")) indices.Add((int)i.AsLong(-1));
            if (syncIndex >= indices.Count || indices[syncIndex] < 0) return null;
            var trees = new List<YNode>();
            foreach (var t in Items(state, "m_BlendTreeConstantArray")) trees.Add(Data(t));
            if (indices[syncIndex] >= trees.Count) return null;
            var nodes = new List<YNode>();
            foreach (var n in Items(trees[indices[syncIndex]], "m_NodeArray")) nodes.Add(Data(n));
            return nodes.Count > 0 ? NodeMotion(nodes, 0, ctx) : null;
        }

        static Motion NodeMotion(List<YNode> nodes, int index, Context ctx)
        {
            var node = nodes[index];
            var children = new List<int>();
            foreach (var c in Items(node, "m_ChildIndices")) children.Add((int)c.AsLong(0));
            if (children.Count == 0)
            {
                uint clip = U(node, "m_ClipID");
                return clip != None && clip < ctx.Clips.Count ? ctx.Clips[(int)clip] : null;
            }
            var tree = Make<BlendTreeMotion>("Blend Tree");
            tree.BlendType = node.Int("m_BlendType");
            tree.Parameter = ctx.Name(U(node, "m_BlendEventID")) ?? "";
            tree.ParameterY = ctx.Name(U(node, "m_BlendEventYID")) ?? "";
            var thresholds = new List<float>();
            foreach (var v in Items(Data(node.Get("m_Blend1dData")), "m_ChildThresholdArray")) thresholds.Add(Float(v));
            var positions = new List<Vector2>();
            foreach (var v in Items(Data(node.Get("m_Blend2dData")), "m_ChildPositionArray")) positions.Add(new Vector2(v.Float("x"), v.Float("y")));
            var direct = new List<uint>();
            foreach (var v in Items(Data(node.Get("m_BlendDirectData")), "m_ChildBlendEventIDArray")) direct.Add(unchecked((uint)v.AsLong(0)));
            for (int i = 0; i < children.Count; i++)
            {
                var child = nodes[children[i]];
                tree.Children.Add(new BlendTreeMotion.Child
                {
                    Motion = NodeMotion(nodes, children[i], ctx),
                    Threshold = i < thresholds.Count ? thresholds[i] : 0f,
                    Position = i < positions.Count ? positions[i] : Vector2.zero,
                    CycleOffset = child.Float("m_CycleOffset"),
                    DirectParameter = i < direct.Count ? ctx.Name(direct[i]) : null,
                });
            }
            return tree;
        }
    }
}
