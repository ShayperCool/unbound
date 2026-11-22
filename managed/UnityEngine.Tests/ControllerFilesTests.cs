using System.Collections.Generic;
using Unbound.Runtime;
using UnityEditor.Animations;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>The documents of a controller made through the Editor's controller API, in the
    /// Editor's field order (Unity 6000.0.71f1, tests/projects/scenesave controller-assets), and
    /// the parts Unbound refuses to write.</summary>
    public class ControllerFilesTests
    {
        static string Write(Object o, long id) =>
            UnityYamlWriter.Write(new[] { o }, new Dictionary<Object, long>(ReferenceEqualityComparer.Instance) { [o] = id });

        [Fact]
        public void AStateAndATransitionInTheEditorsFieldOrder()
        {
            var machine = new AnimatorStateMachine { name = "Base Layer" };
            try
            {
                var idle = machine.AddState("Idle");
                var run = machine.AddState("Run");
                var toRun = idle.AddTransition(run);
                toRun.AddCondition(AnimatorConditionMode.Greater, 0.1f, "Speed");
                var state = Write(idle, 1);
                Assert.StartsWith("%YAML 1.1\n%TAG !u! tag:unity3d.com,2011:\n--- !u!1102 &1\nAnimatorState:\n  serializedVersion: 6\n  m_ObjectHideFlags: 1\n", state);
                Assert.Contains("  m_Name: Idle\n  m_Speed: 1\n  m_CycleOffset: 0\n  m_Transitions:\n  - {fileID: 0}\n  m_StateMachineBehaviours: []\n  m_Position: {x: 50, y: 50, z: 0}\n", state);
                var transition = Write(toRun, 2);
                Assert.Contains("--- !u!1101 &2\nAnimatorStateTransition:\n", transition);
                Assert.Contains("  m_Conditions:\n  - m_ConditionMode: 3\n    m_ConditionEvent: Speed\n    m_EventTreshold: 0.1\n", transition);
                Assert.Contains("  m_IsExit: 0\n  serializedVersion: 3\n  m_TransitionDuration: ", transition);
                var entry = machine.AddEntryTransition(run);
                Assert.EndsWith("  m_IsExit: 0\n  serializedVersion: 1\n", Write(entry, 3));
            }
            finally { Object.DestroyImmediate(machine); }
        }

        /// <summary>Transitions out of sub-state machines, by source instance id, and a synced
        /// layer's motions and behaviours are written in the forms measured (controller-assets
        /// basic.saved, layered.saved; animatorapi model.file.C).</summary>
        [Fact]
        public void MachineTransitionsAndSyncedMotions()
        {
            var early = new AnimatorStateMachine { name = "Early" };
            var machine = new AnimatorStateMachine { name = "Root" };
            var controller = new AnimatorController();
            var clip = new AnimationClip();
            try
            {
                var sub = machine.AddStateMachine("Sub");
                var run = machine.AddState("Run");
                machine.AddStateMachineTransition(sub, run);
                Assert.True(UnityYamlWriter.WritesNew(machine));
                Assert.Contains("  m_StateMachineTransitions:\n  - first: {fileID: 0}\n    second:\n    - {fileID: 0}\n  m_StateMachineBehaviours: []\n", Write(machine, 1));
                // A second source, made later and given its transition later: written first, the
                // sources by instance id (controller-assets basic.saved).
                var other = machine.AddStateMachine("Other");
                machine.AddStateMachineTransition(other, run);
                // A third made before both but given its first transition last: by instance id it
                // comes after them (not by the order of first transitions).
                machine.AddStateMachine(early, Vector3.zero);
                machine.AddStateMachineTransition(early, run);
                Assert.Contains("  m_StateMachineTransitions:\n  - first: {fileID: 4}\n    second:\n    - {fileID: 0}\n  - first: {fileID: 3}\n    second:\n    - {fileID: 0}\n  - first: {fileID: 5}\n",
                    UnityYamlWriter.Write(new Object[] { machine, sub, other, early },
                    new Dictionary<Object, long>(ReferenceEqualityComparer.Instance) { [machine] = 1, [sub] = 3, [other] = 4, [early] = 5 }));

                controller.AddLayer("Base Layer");
                var synced = new AnimatorControllerLayer { name = "Synced", syncedLayerIndex = 0 };
                controller.AddLayer(synced);
                var state = controller.layers[0].stateMachine.AddState("Idle");
                var layers = controller.layers;
                layers[1].SetOverrideMotion(state, clip);
                controller.layers = layers;
                Assert.True(UnityYamlWriter.WritesNew(controller));
                Assert.Contains("    m_Motions:\n    - serializedVersion: 2\n      m_State: {fileID: 0}\n      m_Motion: {fileID: 0}\n    m_Behaviours: []\n", Write(controller, 9100000));

                // A synced layer's behaviours (tests/projects/animatorapi model.file.C).
                var behaviour = ScriptableObject.CreateInstance<ProbeStateBehaviour>();
                layers = controller.layers;
                layers[1].SetOverrideBehaviours(state, new StateMachineBehaviour[] { behaviour });
                controller.layers = layers;
                Assert.Contains("    m_Behaviours:\n    - m_State: {fileID: 0}\n      m_StateMachineBehaviours:\n      - {fileID: 0}\n    m_BlendingMode: ", Write(controller, 9100000));
                Object.DestroyImmediate(behaviour);
            }
            finally
            {
                Object.DestroyImmediate(machine);
                Object.DestroyImmediate(early);
                Object.DestroyImmediate(controller);
                Object.DestroyImmediate(clip);
            }
        }
    }

    public class ProbeStateBehaviour : StateMachineBehaviour
    {
    }
}
