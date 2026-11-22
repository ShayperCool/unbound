using System;
using System.Globalization;
using UnityEditor.Animations;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>
    /// The documents of an animator controller made through the Editor's controller API, written
    /// as the Editor writes them (matches the Unity 6000.0.71f1 Editor, tests/projects/scenesave
    /// controller-assets; a synced layer's behaviours and state machine behaviours as
    /// tests/projects/animatorapi model.file.* holds them): the controller (its parameters and
    /// layers), its state machines, states, transitions and blend trees, each object's fields in the
    /// Editor's order.
    /// </summary>
    internal static class ControllerFiles
    {
        /// <summary>The objects of a controller file Unbound writes.</summary>
        internal static bool Writes(Object o) =>
            o is AnimatorController or AnimatorStateMachine or AnimatorState or AnimatorTransitionBase or BlendTree;
    }

    internal static partial class UnityYamlWriter
    {
        internal sealed partial class Writer
        {
            static string Position(Vector3 p) => "{x: " + F(p.x) + ", y: " + F(p.y) + ", z: " + F(p.z) + "}";

            static string Position(Vector2 p) => "{x: " + F(p.x) + ", y: " + F(p.y) + "}";

            void References<T>(string key, System.Collections.Generic.IList<T> objects, int level) where T : Object
            {
                if (objects.Count == 0)
                {
                    Line(level, key, "[]");
                    return;
                }
                Key(key, level);
                foreach (var o in objects) SequenceValue(level, Reference(o));
            }

            /// <summary>An AnimatorController's body (class 91).</summary>
            internal void AnimatorControllerBody(AnimatorController c)
            {
                ObjectHeader(c);
                String("m_Name", c.name);
                Int("serializedVersion", 5);
                if (c.m_Parameters.Count == 0) Line(1, "m_AnimatorParameters", "[]");
                else
                {
                    Key("m_AnimatorParameters", 1);
                    foreach (var p in c.m_Parameters)
                    {
                        Item("m_Name", Scalar(p.m_Name ?? ""), 1);
                        Int("m_Type", (int)p.m_Type, 2);
                        Float("m_DefaultFloat", p.m_DefaultFloat, 2);
                        Int("m_DefaultInt", p.m_DefaultInt, 2);
                        Bool("m_DefaultBool", p.m_DefaultBool, 2);
                        Line(2, "m_Controller", Reference(c));
                    }
                }
                if (c.m_Layers.Count == 0) Line(1, "m_AnimatorLayers", "[]");
                else
                {
                    Key("m_AnimatorLayers", 1);
                    foreach (var l in c.m_Layers)
                    {
                        Item("serializedVersion", "5", 1);
                        String("m_Name", l.Name ?? "", 2);
                        Line(2, "m_StateMachine", Reference(l.StateMachine));
                        Line(2, "m_Mask", Reference(l.Mask));
                        if (l.SyncedMotions.Count == 0) Line(2, "m_Motions", "[]");
                        else
                        {
                            // A synced layer's motions (controller-assets layered.saved).
                            Key("m_Motions", 2);
                            foreach (var (state, motion) in l.SyncedMotions)
                            {
                                Item("serializedVersion", "2", 2);
                                Line(3, "m_State", Reference(state));
                                Line(3, "m_Motion", Reference(motion));
                            }
                        }
                        if (l.SyncedBehaviours.Count == 0) Line(2, "m_Behaviours", "[]");
                        else
                        {
                            // A synced layer's behaviours (tests/projects/animatorapi model.file.C).
                            Key("m_Behaviours", 2);
                            foreach (var (state, behaviours) in l.SyncedBehaviours)
                            {
                                Item("m_State", Reference(state), 2);
                                References("m_StateMachineBehaviours", behaviours, 3);
                            }
                        }
                        Int("m_BlendingMode", l.BlendingMode, 2);
                        Int("m_SyncedLayerIndex", l.SyncedLayerIndex, 2);
                        Float("m_DefaultWeight", l.DefaultWeight, 2);
                        Bool("m_IKPass", l.IKPass, 2);
                        Bool("m_SyncedLayerAffectsTiming", l.SyncedLayerAffectsTiming, 2);
                        Line(2, "m_Controller", Reference(c));
                    }
                }
            }

            /// <summary>An AnimatorStateMachine's body (class 1107).</summary>
            internal void AnimatorStateMachineBody(AnimatorStateMachine m)
            {
                Int("serializedVersion", 6);
                ObjectHeader(m);
                String("m_Name", m.name);
                var states = m.states;
                if (states.Length == 0) Line(1, "m_ChildStates", "[]");
                else
                {
                    Key("m_ChildStates", 1);
                    foreach (var s in states)
                    {
                        Item("serializedVersion", "1", 1);
                        Line(2, "m_State", Reference(s.state));
                        Line(2, "m_Position", Position(s.position));
                    }
                }
                var machines = m.stateMachines;
                if (machines.Length == 0) Line(1, "m_ChildStateMachines", "[]");
                else
                {
                    Key("m_ChildStateMachines", 1);
                    foreach (var s in machines)
                    {
                        Item("serializedVersion", "1", 1);
                        Line(2, "m_StateMachine", Reference(s.stateMachine));
                        Line(2, "m_Position", Position(s.position));
                    }
                }
                References("m_AnyStateTransitions", m.AnyStateTransitions, 1);
                References("m_EntryTransitions", m.EntryTransitions, 1);
                if (m.MachineTransitions.Count == 0) Line(1, "m_StateMachineTransitions", "{}");
                else
                {
                    // Transitions out of sub-state machines, by their source's instance id (newest made
                    // first among machines made in the session), each source's in the order made
                    // (controller-assets basic.saved: three sources made and given transitions in
                    // different orders).
                    Key("m_StateMachineTransitions", 1);
                    var sources = new System.Collections.Generic.List<(AnimatorStateMachine Source, System.Collections.Generic.List<AnimatorTransitionBase> Transitions)>(m.MachineTransitions);
                    // A destroyed source keeps its instance id (GetInstanceID answers it after destruction).
                    sources.Sort((a, b) => (a.Source is null ? 0 : a.Source.GetInstanceID()).CompareTo(b.Source is null ? 0 : b.Source.GetInstanceID()));
                    foreach (var (source, transitions) in sources)
                    {
                        Item("first", Reference(source), 1);
                        References("second", transitions, 2);
                    }
                }
                References("m_StateMachineBehaviours", m.Behaviours, 1);
                Line(1, "m_AnyStatePosition", Position(m.anyStatePosition));
                Line(1, "m_EntryPosition", Position(m.entryPosition));
                Line(1, "m_ExitPosition", Position(m.exitPosition));
                Line(1, "m_ParentStateMachinePosition", Position(m.parentStateMachinePosition));
                Line(1, "m_DefaultState", Reference(m.DefaultState));
            }

            /// <summary>An AnimatorState's body (class 1102).</summary>
            internal void AnimatorStateBody(AnimatorState s)
            {
                Int("serializedVersion", 6);
                ObjectHeader(s);
                String("m_Name", s.name);
                Float("m_Speed", s.Speed);
                Float("m_CycleOffset", s.CycleOffset);
                References("m_Transitions", s.Transitions, 1);
                References("m_StateMachineBehaviours", s.Behaviours, 1);
                Line(1, "m_Position", Position(s.m_Position));
                Bool("m_IKOnFeet", s.IKOnFeet);
                Bool("m_WriteDefaultValues", s.WriteDefaults);
                Bool("m_Mirror", s.Mirror);
                Bool("m_SpeedParameterActive", s.speedParameterActive);
                Bool("m_MirrorParameterActive", s.mirrorParameterActive);
                Bool("m_CycleOffsetParameterActive", s.cycleOffsetParameterActive);
                Bool("m_TimeParameterActive", s.timeParameterActive);
                Line(1, "m_Motion", Reference(s.Motion));
                String("m_Tag", s.Tag ?? "");
                String("m_SpeedParameter", s.speedParameter ?? "");
                String("m_MirrorParameter", s.mirrorParameter ?? "");
                String("m_CycleOffsetParameter", s.cycleOffsetParameter ?? "");
                String("m_TimeParameter", s.timeParameter ?? "");
            }

            /// <summary>A transition's body: an AnimatorStateTransition (class 1101) or an
            /// AnimatorTransition (class 1109, entry and state machine transitions).</summary>
            internal void AnimatorTransitionBody(AnimatorTransitionBase t)
            {
                ObjectHeader(t);
                String("m_Name", t.name);
                if (t.Conditions.Count == 0) Line(1, "m_Conditions", "[]");
                else
                {
                    Key("m_Conditions", 1);
                    foreach (var c in t.Conditions)
                    {
                        Item("m_ConditionMode", c.Mode.ToString(CultureInfo.InvariantCulture), 1);
                        String("m_ConditionEvent", c.Parameter ?? "", 2);
                        Float("m_EventTreshold", c.Threshold, 2);
                    }
                }
                Line(1, "m_DstStateMachine", Reference(t.DstStateMachine));
                Line(1, "m_DstState", Reference(t.DstState));
                Bool("m_Solo", t.Solo);
                Bool("m_Mute", t.Mute);
                Bool("m_IsExit", t.IsExit);
                if (t is not AnimatorStateTransition)
                {
                    Int("serializedVersion", 1);
                    return;
                }
                Int("serializedVersion", 3);
                Float("m_TransitionDuration", t.Duration);
                Float("m_TransitionOffset", t.Offset);
                Float("m_ExitTime", t.ExitTime);
                Bool("m_HasExitTime", t.HasExitTime);
                Bool("m_HasFixedDuration", t.HasFixedDuration);
                Int("m_InterruptionSource", t.InterruptionSource);
                Bool("m_OrderedInterruption", t.OrderedInterruption);
                Bool("m_CanTransitionToSelf", t.CanTransitionToSelf);
            }

            /// <summary>A BlendTree's body (class 206).</summary>
            internal void BlendTreeBody(BlendTree b)
            {
                ObjectHeader(b);
                String("m_Name", b.name);
                if (b.Children.Count == 0) Line(1, "m_Childs", "[]");
                else
                {
                    Key("m_Childs", 1);
                    foreach (var c in b.Children)
                    {
                        Item("serializedVersion", "2", 1);
                        Line(2, "m_Motion", Reference(c.motion));
                        Float("m_Threshold", c.threshold, 2);
                        Line(2, "m_Position", Position(c.position));
                        Float("m_TimeScale", c.timeScale, 2);
                        Float("m_CycleOffset", c.cycleOffset, 2);
                        String("m_DirectBlendParameter", c.directBlendParameter ?? "", 2);
                        Bool("m_Mirror", c.mirror, 2);
                    }
                }
                String("m_BlendParameter", b.Parameter ?? "");
                String("m_BlendParameterY", b.ParameterY ?? "");
                Float("m_MinThreshold", b.MinThreshold);
                Float("m_MaxThreshold", b.MaxThreshold);
                Bool("m_UseAutomaticThresholds", b.UseAutomaticThresholds);
                Bool("m_NormalizedBlendValues", b.NormalizedBlendValues);
                Int("m_BlendType", b.BlendType);
            }
        }
    }
}
