using System.Collections.Generic;
using System.Linq;
using Unbound.Runtime;
using Unbound.Tests;
using UnityEditor.Animations;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// A `.controller` loads as the Editor's UnityEditor.Animations.AnimatorController, whose API reads
    /// its parameters, layers, state machines and states as the file holds them; an override
    /// controller with nothing to override cannot be assigned at run time (Unity 6000.0.71f1,
    /// tests/projects/gameinterop editor_controller.*, animator.runtime_invalid.*).
    /// </summary>
    public class EditorAnimatorControllerTests
    {
        static LoadedFile Load(string yaml) =>
            ObjectGraphBuilder.Build(UnityYaml.Parse(yaml), "test.controller", null, ObjectGraphBuilder.Kind.Asset, null);

        [Fact]
        public void AControllerFileLoadsAsTheEditorsControllerClass()
        {
            var file = Load(AnimationFixtures.Machines);
            var controller = Assert.IsType<AnimatorController>(file.Objects[9100000]);
            var parameter = Assert.Single(controller.parameters);
            Assert.Equal(("Jump", AnimatorControllerParameterType.Trigger), (parameter.name, parameter.type));
            var layer = Assert.Single(controller.layers);
            Assert.Equal(("Base Layer", 0f, AnimatorLayerBlendingMode.Override, -1, false), (layer.name, layer.defaultWeight, layer.blendingMode, layer.syncedLayerIndex, layer.iKPass));
            var root = layer.stateMachine;
            Assert.Same(file.Objects[1107000001], root);
            Assert.Equal("Base Layer", root.name);
            Assert.Equal(new[] { "Ground" }, root.states.Select(s => s.state.name));
            Assert.Equal("Ground", root.defaultState.name);
            Assert.Equal(Animator.StringToHash("Ground"), root.defaultState.nameHash);
            var air = Assert.Single(root.stateMachines).stateMachine;
            Assert.Equal(new[] { "Rise", "Fall" }.OrderBy(n => n), air.states.Select(s => s.state.name).OrderBy(n => n));
            Assert.Equal(1f, root.defaultState.speed);
            Assert.True(root.defaultState.writeDefaultValues);

            // The returned arrays are copies; setting them changes the controller.
            controller.parameters[0].name = "Changed";
            Assert.Equal("Jump", controller.parameters[0].name);
            controller.parameters = new[] { new AnimatorControllerParameter { name = "Speed", type = AnimatorControllerParameterType.Float } };
            Assert.Equal("Speed", Assert.Single(controller.parameters).name);
            Object.DestroyImmediate(controller, true);
        }

        [Fact]
        public void AnEmptyOverrideControllerIsRefusedAtRunTimeAndTheAnimatorKeepsItsController()
        {
            using var engine = MainThreadCheck.Engine();
            var file = Load(AnimationFixtures.Machines);
            var controller = (RuntimeAnimatorController)file.Objects[9100000];
            var empty = new AnimatorOverrideController { name = "Empty" };
            var go = new GameObject("Animated");
            var logs = new List<string>();
            Application.LogCallback capture = (message, stack, type) => logs.Add(type + ": " + message);
            Application.logMessageReceived += capture;
            try
            {
                var animator = go.AddComponent<Animator>();
                animator.runtimeAnimatorController = empty;
                Assert.Null(animator.runtimeAnimatorController);
                Assert.Equal(new[] { "Error: Could not set Runtime Animator Controller. The controller Empty is an AnimatorOverrideController with no AnimatorController to override." }, logs);
                animator.runtimeAnimatorController = controller;
                logs.Clear();
                animator.runtimeAnimatorController = empty;
                Assert.Same(controller, animator.runtimeAnimatorController);
                Assert.Single(logs);
                // Control: an override controller with a controller is taken.
                var filled = new AnimatorOverrideController(controller) { name = "Filled" };
                logs.Clear();
                animator.runtimeAnimatorController = filled;
                Assert.Same(filled, animator.runtimeAnimatorController);
                Assert.Empty(logs);
            }
            finally
            {
                Application.logMessageReceived -= capture;
                Object.DestroyImmediate(go);
                Object.DestroyImmediate(controller, true);
            }
        }
    }
}
