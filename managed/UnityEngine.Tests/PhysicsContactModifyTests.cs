using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Unity.Collections;
using UnityEngine.SceneManagement;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Contact modification (Unity 6000.0 Scripting API: Physics.ContactModifyEvent,
    /// Physics.ContactModifyEventCCD, ModifiableContactPair, Collider.hasModifiableContacts).
    /// Documented and asserted: only pairs with a collider that has hasModifiableContacts reach the
    /// event, which gets the pair's physics scene; ignoring every contact lets the body fall through
    /// (the ContactModifyEvent example); CCD pairs go only to ContactModifyEventCCD.
    /// Measured against Unity 6000.0.71f1 by tests/projects/contactmodify3d (not asserted here):
    /// pair data, batching and the effect of every edit.
    /// Not asserted: the calling thread and the order of pairs (Unity leaves both unspecified).
    /// </summary>
    public class PhysicsContactModifyTests : IDisposable
    {
        const float Dt = 0.02f;
        // Far from the origin, away from anything other tests leave in the default physics scene.
        static readonly Vector3 Origin = new Vector3(0f, 0f, -900f);
        static int s_Lane;

        readonly List<GameObject> m_Made = new List<GameObject>();
        readonly List<Scene> m_Scenes = new List<Scene>();
        readonly HashSet<int> m_Mine = new HashSet<int>();
        readonly Vector3 m_Gravity;
        readonly SimulationMode m_Mode;
        Action<PhysicsScene, NativeArray<ModifiableContactPair>> m_Handler, m_CcdHandler;
        readonly List<(PhysicsScene Scene, int Collider, int Other, int Contacts)> m_Pairs = new List<(PhysicsScene, int, int, int)>();
        readonly List<(int Collider, int Other)> m_CcdPairs = new List<(int, int)>();
        bool m_Ignore, m_IgnoreCcd;

        public PhysicsContactModifyTests()
        {
            m_Gravity = Physics.gravity;
            m_Mode = Physics.simulationMode;
            Physics.simulationMode = SimulationMode.Script;
            Physics.gravity = new Vector3(0f, -9.81f, 0f);
            m_Handler = (scene, pairs) =>
            {
                foreach (var pair in pairs)
                {
                    if (!m_Mine.Contains(pair.colliderInstanceID) && !m_Mine.Contains(pair.otherColliderInstanceID)) continue;
                    m_Pairs.Add((scene, pair.colliderInstanceID, pair.otherColliderInstanceID, pair.contactCount));
                    if (m_Ignore)
                        for (int i = 0; i < pair.contactCount; i++) pair.IgnoreContact(i);
                }
            };
            m_CcdHandler = (scene, pairs) =>
            {
                foreach (var pair in pairs)
                {
                    if (!m_Mine.Contains(pair.colliderInstanceID) && !m_Mine.Contains(pair.otherColliderInstanceID)) continue;
                    m_CcdPairs.Add((pair.colliderInstanceID, pair.otherColliderInstanceID));
                    if (m_IgnoreCcd)
                        for (int i = 0; i < pair.contactCount; i++) pair.IgnoreContact(i);
                }
            };
            Physics.ContactModifyEvent += m_Handler;
            Physics.ContactModifyEventCCD += m_CcdHandler;
        }

        public void Dispose()
        {
            Physics.ContactModifyEvent -= m_Handler;
            Physics.ContactModifyEventCCD -= m_CcdHandler;
            foreach (var go in m_Made)
                if (go != null) Object.DestroyImmediate(go);
            foreach (var scene in m_Scenes)
                if (scene.isLoaded) SceneManager.UnloadSceneAsync(scene);
            Physics.gravity = m_Gravity;
            Physics.simulationMode = m_Mode;
        }

        Vector3 Lane() => Origin + new Vector3(30f * (s_Lane++ % 20), 0f, 0f);

        T Add<T>(GameObject go) where T : Collider
        {
            var c = go.AddComponent<T>();
            m_Mine.Add(c.GetInstanceID());
            return c;
        }

        GameObject Box(Vector3 position, Vector3 size, bool body)
        {
            var go = new GameObject("Box");
            m_Made.Add(go);
            go.transform.position = position;
            Add<BoxCollider>(go).size = size;
            if (body) go.AddComponent<Rigidbody>();
            return go;
        }

        static void Steps(int count) { for (int i = 0; i < count; i++) Physics.Simulate(Dt); }

        [Fact]
        public void ModifiableContactPairMirrorsThePhysXPairLayout()
        {
            // The buffers are PhysX's: PxContactModifyPair, PxModifiableContact, PxContactPatch.
            Assert.Equal(104, Unsafe.SizeOf<ModifiableContactPair>());
            Assert.Equal(64, Unsafe.SizeOf<ModifiableContact>());
            Assert.Equal(48, Unsafe.SizeOf<ModifiableContactPatch>());
            Assert.Equal(16, Unsafe.SizeOf<ModifiableMassProperties>());
        }

        [Fact]
        public void OnlyPairsWithAModifiableColliderAreReported()
        {
            var lane = Lane();
            var floor = Box(lane, new Vector3(10f, 1f, 10f), body: false);
            var box = Box(lane + new Vector3(0f, 1f, 0f), Vector3.one, body: true);
            Steps(3);
            Assert.Empty(m_Pairs);

            // A flagged collider's pairs are reported; the unflagged box's are not.
            var flagged = Box(lane + new Vector3(3f, 1f, 0f), Vector3.one, body: true);
            var collider = flagged.GetComponent<BoxCollider>();
            collider.hasModifiableContacts = true;
            Assert.True(collider.hasModifiableContacts);
            Assert.False(box.GetComponent<BoxCollider>().hasModifiableContacts);
            Steps(3);
            Assert.NotEmpty(m_Pairs);
            foreach (var pair in m_Pairs)
            {
                Assert.Equal(Physics.defaultPhysicsScene, pair.Scene);
                Assert.Contains(collider.GetInstanceID(), new[] { pair.Collider, pair.Other });
                Assert.Contains(floor.GetComponent<BoxCollider>().GetInstanceID(), new[] { pair.Collider, pair.Other });
                Assert.True(pair.Contacts > 0);
            }
        }

        [Fact]
        public void TriggerPairsAreNotReported()
        {
            var lane = Lane();
            var floor = Box(lane, new Vector3(10f, 1f, 10f), body: false);
            floor.GetComponent<BoxCollider>().isTrigger = true;
            floor.GetComponent<BoxCollider>().hasModifiableContacts = true;
            var box = Box(lane + new Vector3(0f, 1f, 0f), Vector3.one, body: true);
            box.GetComponent<BoxCollider>().hasModifiableContacts = true;
            Steps(3);
            Assert.Empty(m_Pairs);
        }

        [Fact]
        public void IgnoredContactsLetTheBodyFallThrough()
        {
            var lane = Lane();
            Box(lane, new Vector3(10f, 1f, 10f), body: false);
            var box = Box(lane + new Vector3(0f, 1.2f, 0f), Vector3.one, body: true);
            box.GetComponent<BoxCollider>().hasModifiableContacts = true;
            m_Ignore = true;
            Steps(50);
            Assert.NotEmpty(m_Pairs);
            Assert.True(box.transform.position.y < lane.y - 2f, $"fell to {box.transform.position.y}");
        }

        [Fact]
        public void SweptPairsGoOnlyToTheCcdEvent()
        {
            var lane = Lane();
            var gravity = Physics.gravity;
            Physics.gravity = Vector3.zero;
            Box(lane + new Vector3(5f, 0f, 0f), new Vector3(.1f, 4f, 4f), body: false);
            var go = new GameObject("Ball");
            m_Made.Add(go);
            go.transform.position = lane;
            Add<SphereCollider>(go).radius = .2f;
            go.GetComponent<SphereCollider>().hasModifiableContacts = true;
            var rb = go.AddComponent<Rigidbody>();
            rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            rb.linearVelocity = new Vector3(150f, 0f, 0f);
            m_IgnoreCcd = true;
            Steps(4);
            Physics.gravity = gravity;
            // The sweep never leaves a discrete contact: the ignored CCD contact lets it through.
            Assert.NotEmpty(m_CcdPairs);
            Assert.Empty(m_Pairs);
            Assert.True(go.transform.position.x > lane.x + 8f, $"stopped at {go.transform.position.x - lane.x}");
        }

        [Fact]
        public void LocalPhysicsSceneReportsItsOwnScene()
        {
            var scene = SceneManager.CreateScene("ContactModify3D" + s_Lane, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
            m_Scenes.Add(scene);
            var previous = SceneManager.GetActiveScene();
            SceneManager.SetActiveScene(scene);
            GameObject box;
            try
            {
                Box(Vector3.zero, new Vector3(10f, 1f, 10f), body: false);
                box = Box(new Vector3(0f, 1f, 0f), Vector3.one, body: true);
                box.GetComponent<BoxCollider>().hasModifiableContacts = true;
            }
            finally { SceneManager.SetActiveScene(previous); }
            var physics = scene.GetPhysicsScene();
            for (int i = 0; i < 3; i++) physics.Simulate(Dt);
            Assert.NotEmpty(m_Pairs);
            foreach (var pair in m_Pairs) Assert.Equal(physics, pair.Scene);
        }
    }
}
