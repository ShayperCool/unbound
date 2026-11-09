using System;
using System.Collections.Generic;
using UnityEngine.SceneManagement;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>A static collider (no Rigidbody) moved through its Transform is found by queries at
    /// its new place once transforms are synced (6000.0 Scripting API, Physics.SyncTransforms:
    /// "Apply Transform changes to the physics engine"). Asserted on every frame of a scene whose
    /// query structures keep rebuilding while it moves, including the frames a rebuilt structure
    /// replaces the old one.</summary>
    public class PhysicsStaticMoveQueryTests : IDisposable
    {
        static int s_Created;
        readonly List<GameObject> m_Made = new List<GameObject>();
        readonly SimulationMode m_Mode = Physics.simulationMode;
        readonly bool m_AutoSync = Physics.autoSyncTransforms;
        readonly Scene m_Active = SceneManager.GetActiveScene();
        Scene m_Scene;

        public PhysicsStaticMoveQueryTests()
        {
            Physics.simulationMode = SimulationMode.Script;
            Physics.autoSyncTransforms = false;
        }

        public void Dispose()
        {
            if (m_Active.isLoaded && SceneManager.GetActiveScene() != m_Active) SceneManager.SetActiveScene(m_Active);
            foreach (var go in m_Made)
                if (go != null) Object.DestroyImmediate(go);
            if (m_Scene.isLoaded) SceneManager.UnloadSceneAsync(m_Scene);
            Physics.simulationMode = m_Mode;
            Physics.autoSyncTransforms = m_AutoSync;
        }

        Transform Box(Vector3 at)
        {
            var go = new GameObject("static box");
            go.AddComponent<BoxCollider>().size = new Vector3(2f, 0.2f, 2f);
            go.transform.position = at;
            m_Made.Add(go);
            return go.transform;
        }

        [Fact]
        public void AStaticMovedOnceIsSweptAtItsNewPlaceOnEveryFollowingFrame()
        {
            m_Scene = SceneManager.CreateScene("StaticMoveQueries" + s_Created++, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            SceneManager.SetActiveScene(m_Scene);
            Transform mover, other;
            try
            {
                // Enough other statics that a rebuild of the query structure spans several frames;
                // one of them moves a little every frame, so rebuilds follow one another.
                for (int i = 0; i < 64; i++) Box(new Vector3(4f * (i % 8), 0f, 4f * (i / 8)));
                other = Box(new Vector3(-10f, 0f, 0f));
                mover = Box(new Vector3(-100f, 0f, 0f));
            }
            finally { SceneManager.SetActiveScene(m_Active); }
            var physics = m_Scene.GetPhysicsScene();
            physics.Simulate(0.02f);

            var misses = new List<int>();
            var at = mover.position;
            for (int frame = 0; frame < 2000; frame++)
            {
                other.position = new Vector3(-10f, 0f, frame % 2 == 0 ? 0.01f : 0f);
                // Every seventh frame (so at every stage of a rebuild in turn) the mover jumps
                // between two places 100 units apart, away from every other collider; the sweep
                // reaches only its new place.
                if (frame % 7 == 0) mover.position = at = new Vector3(-100f, at.y == 0f ? 100f : 0f, 0f);
                Physics.SyncTransforms();
                if (!physics.SphereCast(at + Vector3.up, 0.1f, Vector3.down, out var hit, 2f) || hit.collider.transform != mover)
                    misses.Add(frame);
                physics.Simulate(0.02f);
            }
            Assert.Empty(misses);
        }
    }
}
