using System;
using System.Collections.Generic;
using Xunit;

namespace UnityEngine.Tests
{
    public class Physics2DMotionParityTests : IDisposable
    {
        readonly List<GameObject> objects = new List<GameObject>();
        public Physics2DMotionParityTests()
        {
            Physics2D.simulationMode = SimulationMode2D.Script;
            Physics2D.autoSyncTransforms = false;
            Physics2D.gravity = Vector2.zero;
        }
        public void Dispose()
        {
            foreach (var o in objects) if (o) Object.DestroyImmediate(o);
            Physics2D.defaultContactOffset = .01f;
            Physics2D.gravity = new Vector2(0, -9.81f);
            Physics2D.Simulate(.02f);
        }

        // Native phase/True/0/{speed}/{continuous} has two entering points with
        // different lever arms. The collision velocity is its first point's velocity;
        // each ContactPoint2D retains the velocity at its own evaluated position.
        [Theory]
        [InlineData(-3f, false, -1.14247024f, 3.242599f)]
        [InlineData(-3f, true, -1.14879191f, 3.243122f)]
        [InlineData(-12f, false, -1.126434f, 12.04712f)]
        [InlineData(-12f, true, -1.14894533f, 12.047658f)]
        public void EnteringContactPointsRetainTheirOwnRelativeVelocity(float speed, bool continuous, float x, float y)
        {
            Physics2D.gravity = new Vector2(0, -9.81f);
            var ground = new GameObject("ground"); objects.Add(ground);
            ground.transform.position = new Vector3(0, -.5f, 0);
            ground.AddComponent<BoxCollider2D>().size = new Vector2(20, 1);
            var moving = new GameObject("moving"); objects.Add(moving);
            moving.transform.position = new Vector3(0, .6f, 0);
            moving.AddComponent<BoxCollider2D>().sharedMaterial = new PhysicsMaterial2D { bounciness = .8f, friction = .4f };
            var body = moving.AddComponent<Rigidbody2D>();
            body.collisionDetectionMode = continuous ? CollisionDetectionMode2D.Continuous : CollisionDetectionMode2D.Discrete;
            body.linearVelocity = new Vector2(1, speed);
            body.angularVelocity = 17;
            var recorder = moving.AddComponent<ContactRecorder>();
            for (int i = 0; i < 20 && recorder.collisions.Count == 0; i++) Physics2D.Simulate(.02f);
            var collision = Assert.Single(recorder.collisions);
            Assert.Equal(2, collision.contactCount);
            Assert.Equal(collision.relativeVelocity, collision.GetContact(0).relativeVelocity);
            var second = collision.GetContact(1).relativeVelocity;
            Assert.InRange(Mathf.Abs(second.x - x), 0, .001f);
            Assert.InRange(Mathf.Abs(second.y - y), 0, .001f);
            Assert.True(Mathf.Abs(second.y - collision.relativeVelocity.y) > .29f);
        }
        public static IEnumerable<object[]> SkinTransitions()
        {
            foreach (float next in new[] { .001f, .1f })
            foreach (string action in new[] { "none", "sync-before", "sync-after", "enable", "activate",
                "trigger", "material", "density", "size-same", "size-edit", "offset-same", "offset-edit",
                "scale-same", "scale-edit", "rotation", "position", "body-static", "body-kinematic",
                "clone", "clone-inactive", "disabled-geometry", "inactive-geometry", "initial-inactive" })
                yield return new object[] { next, action };
        }
        // Root's two byte-identical 644-value recordings distinguish the retained
        // .01 skin from refreshed .001/.1 skins by public BoxCast observations.
        // Inspect the actual fixture here to keep unrelated query precision out of
        // this contact geometry test. All public query differences remain in evidence.
        [Theory]
        [MemberData(nameof(SkinTransitions))]
        public void BoxSkinFollowsMeasuredGeometryAndRegistrationTransitions(float next, string action)
        {
            Physics2D.defaultContactOffset = .01f;
            var source = new GameObject("box"); objects.Add(source);
            if (action == "initial-inactive") source.SetActive(false);
            var box = source.AddComponent<BoxCollider2D>(); box.size = new Vector2(4, 1);
            if (action == "sync-before") Physics2D.SyncTransforms();
            Physics2D.defaultContactOffset = next;
            switch (action)
            {
                case "sync-after": Physics2D.SyncTransforms(); break;
                case "enable": box.enabled = false; box.enabled = true; break;
                case "activate": source.SetActive(false); source.SetActive(true); break;
                case "trigger": box.isTrigger = true; break;
                case "material": box.sharedMaterial = new PhysicsMaterial2D { friction = .61f }; break;
                case "density": box.density = 2.5f; break;
                case "size-same": box.size = box.size; break;
                case "size-edit": box.size = new Vector2(4.2f, 1); break;
                case "offset-same": box.offset = box.offset; break;
                case "offset-edit": box.offset = new Vector2(0, .1f); break;
                case "scale-same": source.transform.localScale = Vector3.one; break;
                case "scale-edit": source.transform.localScale = new Vector3(1.1f, 1, 1); break;
                case "rotation": source.transform.rotation = Quaternion.Euler(0, 0, 5); break;
                case "position": source.transform.position = new Vector3(0, .1f, 0); break;
                case "body-static": source.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static; break;
                case "body-kinematic": source.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic; break;
                case "clone":
                case "clone-inactive":
                    if (action == "clone-inactive") source.SetActive(false);
                    var clone = Object.Instantiate(source); objects.Add(clone);
                    source.SetActive(false); clone.SetActive(true); box = clone.GetComponent<BoxCollider2D>(); break;
                case "disabled-geometry": box.enabled = false; box.size = new Vector2(4.2f, 1); box.enabled = true; break;
                case "inactive-geometry": source.SetActive(false); box.size = new Vector2(4.2f, 1); source.SetActive(true); break;
                case "initial-inactive": source.SetActive(true); break;
            }
            Physics2D.SyncTransforms();
            bool retained = action == "none" || action.StartsWith("sync-") || action == "material"
                || action == "density" || action == "size-same" || action == "offset-same" || action == "scale-same";
            Assert.Single(box.m_Fixtures);
            Assert.Equal(retained ? .01f : next, box.m_Fixtures[0].Shape.Radius);
        }
    }
}
