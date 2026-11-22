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

        /// <summary>Parts whose form in a file is not measured are refused: state machine
        /// transitions and a synced layer's override motions.</summary>
        [Fact]
        public void UnmeasuredPartsAreRefused()
        {
            var machine = new AnimatorStateMachine { name = "Root" };
            var controller = new AnimatorController();
            var clip = new AnimationClip();
            try
            {
                Assert.True(UnityYamlWriter.WritesNew(machine));
                var sub = machine.AddStateMachine("Sub");
                machine.AddStateMachineTransition(sub, machine);
                Assert.False(UnityYamlWriter.WritesNew(machine));

                controller.AddLayer("Base Layer");
                Assert.True(UnityYamlWriter.WritesNew(controller));
                var synced = new AnimatorControllerLayer { name = "Synced", syncedLayerIndex = 0 };
                controller.AddLayer(synced);
                var state = controller.layers[0].stateMachine.AddState("Idle");
                var layers = controller.layers;
                layers[1].SetOverrideMotion(state, clip);
                controller.layers = layers;
                Assert.False(UnityYamlWriter.WritesNew(controller));
            }
            finally
            {
                Object.DestroyImmediate(machine);
                Object.DestroyImmediate(controller);
                Object.DestroyImmediate(clip);
            }
        }
    }
}
