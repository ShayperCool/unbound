using System;
using System.Collections.Generic;
using Unbound.Runtime;
using UnityEngine.SceneManagement;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Unbound implementation controls, not Unity behaviour: what GetPhysicsScene, IsEmpty and
    /// Simulate do with invalid input. The 6000.0 Scripting API documents these methods only for
    /// valid input, so this is Unbound's choice (unmeasured): an invalid Scene is refused with
    /// ArgumentException, and a physics scene whose Scene was unloaded is refused with
    /// InvalidOperationException. The default physics scene, documented as always valid, is never
    /// refused.
    /// </summary>
    public class PhysicsSceneInvalidInputPolicyTests : IDisposable
    {
        const float Dt = 0.02f;
        static int s_Created;
        readonly List<Scene> m_Scenes = new List<Scene>();
        readonly SceneData m_PreviousActive = SceneRegistry.Active;
        readonly SceneData m_OwnedFallback;

        public PhysicsSceneInvalidInputPolicyTests()
        {
            Physics.simulationMode = SimulationMode.Script;
            // UnloadSceneAsync refuses the last loaded scene. Keep an active scene alive,
            // including when this fixture starts with no scenes at all.
            if (m_PreviousActive == null)
                m_OwnedFallback = SceneManager.GetActiveScene().Data;
        }

        public void Dispose()
        {
            try
            {
                foreach (var scene in m_Scenes)
                    if (scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
            }
            finally
            {
                // The empty fallback belongs to this fixture; forget it even if it is last.
                if (m_OwnedFallback != null) SceneRegistry.Forget(m_OwnedFallback);
                SceneRegistry.Active = m_PreviousActive;
            }
        }

        [Fact]
        public void InvalidSceneIsRefusedByGetPhysicsScene()
        {
            var scene = default(Scene);
            Assert.False(scene.IsValid());

            var error = Assert.Throws<ArgumentException>(() => scene.GetPhysicsScene());
            Assert.Equal("scene", error.ParamName);
            Assert.StartsWith("GetPhysicsScene: the Scene is not valid", error.Message);
        }

        [Fact]
        public void ReleasedPhysicsSceneIsRefusedByIsEmptyAndSimulate()
        {
            var local = SceneManager.CreateScene("InvalidInputPolicy" + s_Created++, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            m_Scenes.Add(local);
            var physics = local.GetPhysicsScene();
            SceneManager.UnloadSceneAsync(local);
            Assert.False(physics.IsValid());

            var empty = Assert.Throws<InvalidOperationException>(() => physics.IsEmpty());
            Assert.StartsWith("PhysicsScene.IsEmpty: ", empty.Message);
            var simulate = Assert.Throws<InvalidOperationException>(() => physics.Simulate(Dt));
            Assert.StartsWith("PhysicsScene.Simulate: ", simulate.Message);
        }

        [Fact]
        public void DefaultPhysicsSceneIsNeverRefused()
        {
            Assert.True(Physics.defaultPhysicsScene.IsValid());
            Assert.Null(Record.Exception(() => Physics.defaultPhysicsScene.IsEmpty()));
            Assert.Null(Record.Exception(() => Physics.defaultPhysicsScene.Simulate(Dt)));
            // A default-valued PhysicsScene is the default physics scene (measured: contactevents3d).
            Assert.Null(Record.Exception(() => default(PhysicsScene).IsEmpty()));
            Assert.Null(Record.Exception(() => SceneManager.GetActiveScene().GetPhysicsScene()));
        }
    }
}
