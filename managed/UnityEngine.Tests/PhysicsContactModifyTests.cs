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

        public sealed class Receiver : MonoBehaviour
        {
            void OnCollisionStay(Collision collision) { }
        }

        Scene ContactScene(bool measured = true)
        {
            string release = ProjectInfo.UnityVersion;
            ProjectInfo.UnityVersion = measured ? "6000.0.71f1" : "2022.3.0f1";
            try
            {
                var scene = SceneManager.CreateScene("LiveContacts" + s_Lane, new CreateSceneParameters(LocalPhysicsMode.Physics3D));
                m_Scenes.Add(scene);
                SceneManager.SetActiveScene(scene);
                var anchor = new GameObject("Anchor"); m_Made.Add(anchor);
                anchor.AddComponent<BoxCollider>();
                return scene;
            }
            finally { ProjectInfo.UnityVersion = release; }
        }

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(true, true, false)]
        [InlineData(false, false, true)]
        [InlineData(true, false, true)]
        public void LiveFlagChangesFollowThePairsContactPointRequest(bool initial, bool receiver, bool provides)
        {
            var active = SceneManager.GetActiveScene();
            var scene = ContactScene();
            var physics = scene.GetPhysicsScene();
            void Steps(int count) { for (int i = 0; i < count; i++) physics.Simulate(Dt); }
            var lane = Lane();
            Box(lane, new Vector3(10, 1, 10), false);
            var box = Box(lane + Vector3.up, Vector3.one, true);
            var col = box.GetComponent<BoxCollider>();
            col.hasModifiableContacts = initial;
            col.providesContacts = provides;
            if (receiver) box.AddComponent<Receiver>().enabled = false;
            box.GetComponent<Rigidbody>().sleepThreshold = 0;
            SceneManager.SetActiveScene(active);
            Steps(2);
            Assert.Equal(initial, m_Pairs.Count != 0);
            col.hasModifiableContacts = !initial;
            m_Pairs.Clear();
            Steps(2);
            Assert.Equal(receiver || provides ? initial : !initial, m_Pairs.Count != 0);
            // A new overlap uses the current flag even when reports keep points requested.
            var rb = box.GetComponent<Rigidbody>();
            rb.position = lane + Vector3.up * 5;
            Steps(1);
            rb.position = lane + Vector3.up;
            rb.linearVelocity = Vector3.zero;
            m_Pairs.Clear();
            Steps(2);
            Assert.Equal(!initial, m_Pairs.Count != 0);
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void CompoundReportRequestsBelongToTheBodyReceiver(bool initial, bool onRoot)
        {
            var active = SceneManager.GetActiveScene();
            var scene = ContactScene();
            var physics = scene.GetPhysicsScene();
            void Steps(int count) { for (int i = 0; i < count; i++) physics.Simulate(Dt); }
            var lane = Lane();
            Box(lane, new Vector3(10, 1, 10), false);
            var root = new GameObject("Compound"); m_Made.Add(root);
            root.transform.position = lane + Vector3.up;
            root.AddComponent<Rigidbody>().sleepThreshold = 0;
            var child = Box(Vector3.zero, Vector3.one, false);
            child.transform.SetParent(root.transform, false);
            (onRoot ? root : child).AddComponent<Receiver>();
            var collider = child.GetComponent<Collider>();
            collider.hasModifiableContacts = initial;
            SceneManager.SetActiveScene(active);
            int points = 0;
            Physics.ContactEventDelegate handler = (_, headers) =>
            {
                for (int h = 0; h < headers.Length; h++)
                    for (int p = 0; p < headers[h].pairCount; p++)
                    {
                        var pair = headers[h].GetContactPair(p);
                        if (pair.colliderInstanceID == collider.GetInstanceID() || pair.otherColliderInstanceID == collider.GetInstanceID())
                            points += pair.contactCount;
                    }
            };
            Physics.ContactEvent += handler;
            try
            {
                Steps(2); m_Pairs.Clear(); points = 0;
                collider.hasModifiableContacts = !initial;
                Steps(2);
                Assert.Equal(onRoot ? initial : !initial, m_Pairs.Count != 0);
                Assert.Equal(onRoot, points != 0);
            }
            finally { Physics.ContactEvent -= handler; }
        }

        public sealed class ReuseReceiver : MonoBehaviour
        {
            internal static readonly List<Collision> Seen = new List<Collision>();
            internal static readonly List<int> Counts = new List<int>();
            void OnCollisionEnter(Collision collision) { Seen.Add(collision); Counts.Add(collision.contactCount); }
            void OnCollisionStay(Collision collision) { Seen.Add(collision); Counts.Add(collision.contactCount); }
        }

        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void CollisionObjectReuseAppliesToBothDeliveryPaths(bool reuse, bool reports)
        {
            bool previous = Physics.reuseCollisionCallbacks;
            Physics.ContactEventDelegate handler = (_, __) => { };
            var lane = Lane();
            var floor = Box(lane, new Vector3(10, 1, 10), false);
            var box = Box(lane + Vector3.up, Vector3.one, true);
            floor.AddComponent<ReuseReceiver>();
            box.AddComponent<ReuseReceiver>();
            box.GetComponent<Rigidbody>().sleepThreshold = 0;
            ReuseReceiver.Seen.Clear(); ReuseReceiver.Counts.Clear();
            Physics.reuseCollisionCallbacks = reuse;
            if (reports) Physics.ContactEvent += handler;
            try
            {
                Steps(3);
                Assert.Equal(6, ReuseReceiver.Seen.Count);
                for (int i = 0; i < ReuseReceiver.Seen.Count; i++)
                {
                    Assert.Equal(i == 0 || reuse, ReferenceEquals(ReuseReceiver.Seen[0], ReuseReceiver.Seen[i]));
                    Assert.Equal(4, ReuseReceiver.Counts[i]);
                }
            }
            finally
            {
                if (reports) Physics.ContactEvent -= handler;
                Physics.reuseCollisionCallbacks = previous;
                ReuseReceiver.Seen.Clear(); ReuseReceiver.Counts.Clear();
            }
        }

        public sealed class TransitionReceiver : MonoBehaviour
        {
            internal static Collision Retained;
            internal static ContactPoint First;
            internal static int Count;
            internal static readonly List<GameObject> Routes = new List<GameObject>();
            internal bool Retain, DisableReuse;
            void OnCollisionEnter(Collision collision)
            {
                Routes.Add(gameObject);
                if (DisableReuse) Physics.reuseCollisionCallbacks = false;
                if (!Retain) return;
                Retained = collision; First = collision.GetContact(0); Count = collision.contactCount;
                Physics.reuseCollisionCallbacks = true;
            }
        }

        [Theory]
        [InlineData(false, false, false)]
        [InlineData(true, false, false)]
        [InlineData(false, true, false)]
        [InlineData(true, true, false)]
        [InlineData(false, false, true)]
        [InlineData(true, false, true)]
        public void ChangingReuseDuringDeliveryPreservesRetainedCollisions(bool reports, bool measured, bool startReused)
        {
            var active = SceneManager.GetActiveScene();
            var scene = ContactScene(measured);
            var physics = scene.GetPhysicsScene();
            void Steps(int count) { for (int i = 0; i < count; i++) physics.Simulate(Dt); }
            bool previous = Physics.reuseCollisionCallbacks;
            Physics.ContactEventDelegate handler = (_, __) => { };
            var lane = Lane();
            Box(lane, new Vector3(10, 1, 10), false).AddComponent<TransitionReceiver>();
            var root = new GameObject("Compound"); m_Made.Add(root);
            root.transform.position = lane + Vector3.up;
            root.AddComponent<Rigidbody>().sleepThreshold = 0;
            root.AddComponent<TransitionReceiver>().Retain = measured || startReused;
            var child = new GameObject("Child");
            child.transform.SetParent(root.transform, false);
            Add<BoxCollider>(child);
            var receiver = child.AddComponent<TransitionReceiver>();
            receiver.Retain = !measured && !startReused;
            receiver.DisableReuse = startReused;
            TransitionReceiver.Retained = null;
            TransitionReceiver.Routes.Clear();
            SceneManager.SetActiveScene(active);
            Physics.reuseCollisionCallbacks = startReused;
            if (reports) Physics.ContactEvent += handler;
            try
            {
                Steps(3);
                var retained = TransitionReceiver.Retained;
                Assert.NotNull(retained);
                if (measured)
                {
                    Assert.DoesNotContain(child, TransitionReceiver.Routes);
                    Assert.Contains(root, TransitionReceiver.Routes);
                }
                Assert.Equal(TransitionReceiver.Count, retained.contactCount);
                var current = retained.GetContact(0);
                Assert.Equal(TransitionReceiver.First.thisCollider, current.thisCollider);
                Assert.Equal(TransitionReceiver.First.otherCollider, current.otherCollider);
                Assert.Equal(TransitionReceiver.First.normal, current.normal);
            }
            finally
            {
                if (reports) Physics.ContactEvent -= handler;
                Physics.reuseCollisionCallbacks = previous;
                TransitionReceiver.Retained = null;
                TransitionReceiver.Routes.Clear();
            }
        }

        [Fact]
        public void CompoundActorIdentitiesSurviveReplacementAndAreSharedByAllChildren()
        {
            var lane = Lane();
            var floor = Box(lane, new Vector3(10, 1, 10), false).GetComponent<Collider>();
            for (int generation = 0; generation < 3; generation++)
            {
                var root = new GameObject("Compound"); m_Made.Add(root);
                root.transform.position = lane + Vector3.up;
                var rb = root.AddComponent<Rigidbody>();
                rb.sleepThreshold = 0;
                var children = new HashSet<int>();
                for (int i = 0; i < 2; i++)
                {
                    var child = new GameObject("Child");
                    child.transform.SetParent(root.transform, false);
                    child.transform.localPosition = new Vector3(i * 2 - 1, 0, 0);
                    var col = Add<SphereCollider>(child);
                    col.hasModifiableContacts = true;
                    children.Add(col.GetInstanceID());
                }
                int count = 0;
                Action<PhysicsScene, NativeArray<ModifiableContactPair>> handler = (scene, pairs) =>
                {
                    foreach (var pair in pairs)
                    {
                        bool first = children.Contains(pair.colliderInstanceID);
                        if (!first && !children.Contains(pair.otherColliderInstanceID)) continue;
                        Assert.Equal(rb.GetInstanceID(), first ? pair.bodyInstanceID : pair.otherBodyInstanceID);
                        Assert.Equal(0, first ? pair.otherBodyInstanceID : pair.bodyInstanceID);
                        Assert.Equal(floor.GetInstanceID(), first ? pair.otherColliderInstanceID : pair.colliderInstanceID);
                        count++;
                    }
                };
                Physics.ContactModifyEvent += handler;
                try { Steps(1); }
                finally { Physics.ContactModifyEvent -= handler; }
                Assert.Equal(2, count);
                Object.DestroyImmediate(root);
                Steps(1);
            }
        }

        [Fact]
        public void FlaggedControllerReportsSimulationContactsButNotItsMoves()
        {
            var lane = Lane();
            Box(lane, new Vector3(10, 1, 10), false);
            var go = new GameObject("Controller"); m_Made.Add(go);
            go.transform.position = lane + Vector3.up * 1.5f;
            var controller = Add<CharacterController>(go);
            Assert.False(controller.hasModifiableContacts);
            controller.hasModifiableContacts = true;
            Assert.True(controller.hasModifiableContacts);
            controller.Move(Vector3.down * .5f);
            Steps(1);
            Assert.Empty(m_Pairs);
            var box = Box(lane + Vector3.up * 2.5f, Vector3.one, true);
            box.GetComponent<Rigidbody>().sleepThreshold = 0;
            Steps(3);
            Assert.NotEmpty(m_Pairs);
            foreach (var pair in m_Pairs)
                Assert.Contains(controller.GetInstanceID(), new[] { pair.Collider, pair.Other });
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
