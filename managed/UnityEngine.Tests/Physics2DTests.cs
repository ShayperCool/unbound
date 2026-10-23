using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Records the 2D physics messages its GameObject gets, in order.</summary>
    public class ContactRecorder : MonoBehaviour
    {
        public readonly List<string> log = new List<string>();
        public readonly List<Collision2D> collisions = new List<Collision2D>();
        public readonly List<Collider2D> triggers = new List<Collider2D>();

        void OnCollisionEnter2D(Collision2D c) { log.Add("CollisionEnter:" + c.collider.name); collisions.Add(c); }
        void OnCollisionStay2D(Collision2D c) { log.Add("CollisionStay:" + c.collider.name); }
        void OnCollisionExit2D(Collision2D c) { log.Add("CollisionExit:" + c.collider.name); collisions.Add(c); }
        void OnTriggerEnter2D(Collider2D other) { log.Add("TriggerEnter:" + other.name); triggers.Add(other); }
        void OnTriggerStay2D(Collider2D other) { log.Add("TriggerStay:" + other.name); }
        void OnTriggerExit2D(Collider2D other) { log.Add("TriggerExit:" + other.name); }
    }

    /// <summary>A collision message without the parameter.</summary>
    public class BareCollisionRecorder : MonoBehaviour
    {
        public int enters;
        void OnCollisionEnter2D() => enters++;
    }

    /// <summary>
    /// 2D physics: Box2D semantics behind Unity's components. The world is global, so every test
    /// of the class runs alone (xunit runs a class's tests one after another) and destroys what it made.
    /// </summary>
    public class Physics2DTests : IDisposable
    {
        const float Dt = 0.02f;
        readonly List<GameObject> m_Made = new List<GameObject>();

        public Physics2DTests()
        {
            Physics2D.gravity = new Vector2(0f, -9.81f);
            Physics2D.queriesHitTriggers = true;
            Physics2D.queriesStartInColliders = true;
            Physics2D.callbacksOnDisable = true;
            Physics2D.autoSyncTransforms = true;
            Physics2D.defaultContactOffset = 0.01f;
            for (int i = 0; i < 32; i++) Physics2D.SetLayerCollisionMask(i, -1);
            Physics2D.simulationMode = SimulationMode2D.Script;
        }

        public void Dispose()
        {
            foreach (var go in m_Made)
                if (go != null) Object.DestroyImmediate(go);
            Physics2D.Simulate(Dt); // flush the exits of what was destroyed
        }

        GameObject Make(string name, Vector2 position)
        {
            var go = new GameObject(name);
            go.transform.position = new Vector3(position.x, position.y, 0f);
            m_Made.Add(go);
            return go;
        }

        static void Run(int steps)
        {
            for (int i = 0; i < steps; i++) Physics2D.Simulate(Dt);
        }

        GameObject Ground(string name = "ground", float y = 0f, float width = 20f)
        {
            var go = Make(name, new Vector2(0f, y));
            var box = go.AddComponent<BoxCollider2D>();
            box.size = new Vector2(width, 1f);
            return go;
        }

        GameObject Ball(string name, Vector2 position, float radius = 0.5f)
        {
            var go = Make(name, position);
            go.AddComponent<Rigidbody2D>();
            go.AddComponent<CircleCollider2D>().radius = radius;
            return go;
        }

        [Fact]
        public void FallingBodyFollowsBox2DSemiImplicitEuler()
        {
            var ball = Ball("ball", new Vector2(0f, 100f));
            var rb = ball.GetComponent<Rigidbody2D>();
            rb.angularDamping = 0f;
            const int steps = 50;
            Run(steps);
            // v(n) = n g dt; y(n) = y0 + g dt^2 n (n + 1) / 2 (velocity first, then position).
            float g = -9.81f;
            Assert.Equal(steps * g * Dt, rb.linearVelocity.y, 3);
            Assert.Equal(100f + g * Dt * Dt * steps * (steps + 1) / 2f, ball.transform.position.y, 3);
            Assert.Equal(0f, ball.transform.position.x, 5);
            Assert.Equal(rb.position.y, ball.transform.position.y, 5);
        }

        [Fact]
        public void GravityScaleDampingAndKinematicBodies()
        {
            var half = Ball("half", new Vector2(-5f, 100f));
            half.GetComponent<Rigidbody2D>().gravityScale = 0.5f;
            var full = Ball("full", new Vector2(5f, 100f));
            var kinematic = Ball("kinematic", new Vector2(0f, 50f));
            var krb = kinematic.GetComponent<Rigidbody2D>();
            krb.bodyType = RigidbodyType2D.Kinematic;
            krb.linearVelocity = new Vector2(1f, 0f);
            Run(10);
            float dropHalf = 100f - half.transform.position.y;
            float dropFull = 100f - full.transform.position.y;
            Assert.Equal(dropFull * 0.5f, dropHalf, 3);
            // Kinematic: no gravity, keeps its velocity.
            Assert.Equal(50f, kinematic.transform.position.y, 4);
            Assert.Equal(10 * Dt, kinematic.transform.position.x, 4);
            Assert.Equal(1f, krb.mass); // the mass that was set, not Box2D's zero
        }

        [Fact]
        public void StaticRigidbodyDoesNotMoveAndKinematicIsNotPushed()
        {
            var wall = Make("wall", new Vector2(0f, 0f));
            var wrb = wall.AddComponent<Rigidbody2D>();
            wrb.bodyType = RigidbodyType2D.Kinematic;
            wall.AddComponent<BoxCollider2D>().size = new Vector2(1f, 10f);
            var ball = Ball("ball", new Vector2(-3f, 0f));
            var rb = ball.GetComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.linearVelocity = new Vector2(10f, 0f);
            Run(40);
            Assert.Equal(0f, wall.transform.position.x, 5);
            Assert.True(ball.transform.position.x < -0.9f, $"the ball went into the wall: {ball.transform.position.x}");
            var stat = Make("static", new Vector2(3f, 3f));
            var srb = stat.AddComponent<Rigidbody2D>();
            srb.bodyType = RigidbodyType2D.Static;
            stat.AddComponent<CircleCollider2D>();
            Run(10);
            Assert.Equal(3f, stat.transform.position.y, 5);
        }

        [Fact]
        public void BallRestsOnGround()
        {
            Ground();
            var ball = Ball("ball", new Vector2(0f, 3f));
            Run(150);
            // Resting on the box top (0.5) with the polygon skin (contact offset) in between.
            Assert.InRange(ball.transform.position.y, 0.99f, 1.02f);
            Assert.True(ball.GetComponent<Rigidbody2D>().IsSleeping() || Mathf.Abs(ball.GetComponent<Rigidbody2D>().linearVelocity.y) < 0.05f);
        }

        [Fact]
        public void BouncinessOfThePhysicsMaterialMakesTheBallBounce()
        {
            Ground();
            var ball = Ball("ball", new Vector2(0f, 5f));
            var material = new PhysicsMaterial2D("bouncy") { bounciness = 1f, friction = 0f };
            ball.GetComponent<CircleCollider2D>().sharedMaterial = material;
            var rb = ball.GetComponent<Rigidbody2D>();
            float lowestVelocity = 0f, highestAfter = 0f;
            bool bounced = false;
            for (int i = 0; i < 200; i++)
            {
                Physics2D.Simulate(Dt);
                var v = rb.linearVelocity.y;
                if (!bounced && v < lowestVelocity) lowestVelocity = v;
                if (v > 0.1f) bounced = true;
                if (bounced) highestAfter = Mathf.Max(highestAfter, ball.transform.position.y);
            }
            Assert.True(bounced);
            // A perfectly elastic bounce gets back to about the drop height (Box2D's position
            // correction can add a little energy at restitution 1, as in Unity).
            Assert.InRange(highestAfter, 4.6f, 5.5f);

            // Without bounciness it stays down.
            var dull = Ball("dull", new Vector2(4f, 5f));
            for (int i = 0; i < 200; i++) Physics2D.Simulate(Dt);
            Assert.InRange(dull.transform.position.y, 0.99f, 1.02f);
        }

        [Fact]
        public void CollisionMessagesEnterStayExitOnBothObjects()
        {
            var ground = Ground();
            var groundLog = ground.AddComponent<ContactRecorder>();
            var ball = Ball("ball", new Vector2(0f, 1.5f));
            var ballLog = ball.AddComponent<ContactRecorder>();
            var bare = ball.AddComponent<BareCollisionRecorder>();
            bare.enabled = false; // messages reach disabled behaviours too
            Run(60);
            Assert.Equal("CollisionEnter:ground", ballLog.log[0]);
            Assert.Equal("CollisionEnter:ball", groundLog.log[0]);
            Assert.Contains("CollisionStay:ground", ballLog.log);
            Assert.Equal(1, bare.enters);
            Assert.Single(ballLog.log, s => s.StartsWith("CollisionEnter"));

            // The ball's view: the ground is the collider, the normal points from it to the ball.
            var c = ballLog.collisions[0];
            Assert.Same(ground.GetComponent<BoxCollider2D>(), c.collider);
            Assert.Same(ball.GetComponent<CircleCollider2D>(), c.otherCollider);
            Assert.Same(ball.GetComponent<Rigidbody2D>(), c.otherRigidbody);
            Assert.Null(c.rigidbody);
            Assert.True(c.contactCount >= 1);
            var contact = c.GetContact(0);
            Assert.True(contact.normal.y > 0.99f, $"normal {contact.normal}");
            Assert.InRange(contact.point.y, 0.4f, 0.6f);
            Assert.True(c.relativeVelocity.y > 0.5f, $"relative velocity {c.relativeVelocity}");
            // The ground's view mirrors it.
            Assert.True(groundLog.collisions[0].GetContact(0).normal.y < -0.99f);

            Assert.True(ball.GetComponent<Collider2D>().IsTouching(ground.GetComponent<Collider2D>()));
            Assert.True(ball.GetComponent<Rigidbody2D>().IsTouchingLayers());

            // Lift the ball away: one exit each, then silence.
            ball.GetComponent<Rigidbody2D>().position = new Vector2(0f, 10f);
            ballLog.log.Clear();
            groundLog.log.Clear();
            Run(1);
            Assert.Equal(new[] { "CollisionExit:ground" }, ballLog.log.ToArray());
            Assert.Equal(new[] { "CollisionExit:ball" }, groundLog.log.ToArray());
            Assert.Equal(0, ballLog.collisions[ballLog.collisions.Count - 1].contactCount);
            Assert.False(ball.GetComponent<Collider2D>().IsTouching(ground.GetComponent<Collider2D>()));
        }

        [Fact]
        public void TriggersReportEnterStayExitAndNeedARigidbody()
        {
            var zone = Make("zone", new Vector2(0f, 0f));
            var box = zone.AddComponent<BoxCollider2D>();
            box.size = new Vector2(2f, 2f);
            box.isTrigger = true;
            var zoneLog = zone.AddComponent<ContactRecorder>();

            // A static collider in a static trigger: nothing.
            var still = Make("still", new Vector2(0f, 0f));
            still.AddComponent<CircleCollider2D>();
            var stillLog = still.AddComponent<ContactRecorder>();

            // A kinematic body moving through it.
            var mover = Make("mover", new Vector2(-3f, 0f));
            var rb = mover.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            mover.AddComponent<CircleCollider2D>().radius = 0.25f;
            var moverLog = mover.AddComponent<ContactRecorder>();
            rb.linearVelocity = new Vector2(10f, 0f);
            Run(60);
            Assert.Empty(stillLog.log);
            Assert.Equal("TriggerEnter:zone", moverLog.log[0]);
            Assert.Contains("TriggerEnter:mover", zoneLog.log);
            Assert.Contains("TriggerStay:mover", zoneLog.log);
            Assert.Contains("TriggerExit:zone", moverLog.log);
            int enter = moverLog.log.IndexOf("TriggerEnter:zone"), exit = moverLog.log.IndexOf("TriggerExit:zone");
            Assert.True(enter < exit);
            Assert.DoesNotContain(moverLog.log, s => s.StartsWith("Collision"));
        }

        [Fact]
        public void DisablingAColliderSendsExitWhenCallbacksOnDisable()
        {
            var zone = Make("zone", Vector2.zero);
            var box = zone.AddComponent<BoxCollider2D>();
            box.isTrigger = true;
            box.size = new Vector2(4f, 4f);
            var body = Ball("body", Vector2.zero);
            body.GetComponent<Rigidbody2D>().gravityScale = 0f;
            var log = body.AddComponent<ContactRecorder>();
            Run(2);
            Assert.Contains("TriggerEnter:zone", log.log);
            log.log.Clear();
            box.enabled = false;
            Run(1);
            Assert.Equal(new[] { "TriggerExit:zone" }, log.log.ToArray());

            Physics2D.callbacksOnDisable = false;
            box.enabled = true;
            Run(1);
            log.log.Clear();
            box.enabled = false;
            Run(1);
            Assert.Empty(log.log);
        }

        [Fact]
        public void LayerCollisionMatrixAndIgnoreCollision()
        {
            var ground = Ground();
            ground.layer = 8;
            var ball = Ball("ball", new Vector2(0f, 2f));
            ball.layer = 9;
            Physics2D.IgnoreLayerCollision(8, 9);
            Assert.True(Physics2D.GetIgnoreLayerCollision(9, 8));
            Assert.Equal(-1 & ~(1 << 9), Physics2D.GetLayerCollisionMask(8));
            Run(60);
            Assert.True(ball.transform.position.y < -1f, "the ball should fall through the ground");

            Physics2D.IgnoreLayerCollision(8, 9, false);
            var other = Ball("other", new Vector2(3f, 2f));
            other.layer = 9;
            Physics2D.IgnoreCollision(other.GetComponent<Collider2D>(), ground.GetComponent<Collider2D>());
            Assert.True(Physics2D.GetIgnoreCollision(ground.GetComponent<Collider2D>(), other.GetComponent<Collider2D>()));
            var third = Ball("third", new Vector2(-3f, 2f));
            third.layer = 9;
            Run(60);
            Assert.True(other.transform.position.y < -1f);
            Assert.InRange(third.transform.position.y, 0.99f, 1.02f);
        }

        [Fact]
        public void RaycastsHitTheNearestColliderWithItsSurface()
        {
            var near = Make("near", new Vector2(5f, 0f));
            near.AddComponent<BoxCollider2D>().size = new Vector2(2f, 2f);
            var far = Make("far", new Vector2(10f, 0f));
            far.AddComponent<CircleCollider2D>().radius = 1f;
            far.layer = 3;

            var hit = Physics2D.Raycast(Vector2.zero, Vector2.right);
            Assert.True(hit);
            Assert.Same(near.GetComponent<BoxCollider2D>(), hit.collider);
            Assert.Equal(4f, hit.distance, 3);
            Assert.Equal(new Vector2(4f, 0f), hit.point);
            Assert.Equal(Vector2.left, hit.normal);
            Assert.Equal(hit.point, hit.centroid);

            var all = Physics2D.RaycastAll(Vector2.zero, Vector2.right, 20f);
            Assert.Equal(2, all.Length);
            Assert.Same(far.GetComponent<CircleCollider2D>(), all[1].collider);
            Assert.Equal(9f, all[1].distance, 3);
            Assert.Equal(9f / 20f, all[1].fraction, 4);

            // Layer mask, distance and NonAlloc.
            Assert.Same(far.GetComponent<CircleCollider2D>(), Physics2D.Raycast(Vector2.zero, Vector2.right, 20f, 1 << 3).collider);
            Assert.False(Physics2D.Raycast(Vector2.zero, Vector2.right, 3.9f));
            var results = new RaycastHit2D[1];
            Assert.Equal(1, Physics2D.RaycastNonAlloc(Vector2.zero, Vector2.right, results, 20f));
            Assert.Same(near.GetComponent<BoxCollider2D>(), results[0].collider);

            // Linecast and a ContactFilter2D with a normal angle range.
            Assert.Same(near.GetComponent<BoxCollider2D>(), Physics2D.Linecast(Vector2.zero, new Vector2(20f, 0f)).collider);
            var filter = new ContactFilter2D().NoFilter();
            filter.SetNormalAngle(-10f, 10f); // normals pointing right only
            var list = new List<RaycastHit2D>();
            Assert.Equal(0, Physics2D.Raycast(Vector2.zero, Vector2.right, filter, list, 20f));
            // Going left, both the circle and the box face the ray with normals pointing right.
            Assert.Equal(2, Physics2D.Raycast(new Vector2(20f, 0f), Vector2.left, filter, list, 20f));
            Assert.Same(far.GetComponent<CircleCollider2D>(), list[0].collider);
        }

        [Fact]
        public void QueriesStartingInsideAColliderAndTriggers()
        {
            var box = Make("box", Vector2.zero);
            box.AddComponent<BoxCollider2D>().size = new Vector2(2f, 2f);
            var trigger = Make("trigger", new Vector2(5f, 0f));
            var tc = trigger.AddComponent<CircleCollider2D>();
            tc.isTrigger = true;

            var inside = Physics2D.Raycast(Vector2.zero, Vector2.right, 10f);
            Assert.Same(box.GetComponent<BoxCollider2D>(), inside.collider);
            Assert.Equal(0f, inside.distance);
            Assert.Equal(0f, inside.fraction);
            Assert.Equal(Vector2.left, inside.normal);

            Physics2D.queriesStartInColliders = false;
            var past = Physics2D.Raycast(Vector2.zero, Vector2.right, 10f);
            Assert.Same(tc, past.collider);
            Assert.Equal(4.5f, past.distance, 3);

            Physics2D.queriesHitTriggers = false;
            Assert.False(Physics2D.Raycast(Vector2.zero, Vector2.right, 10f));
            Physics2D.queriesHitTriggers = true;
            Physics2D.queriesStartInColliders = true;
        }

        [Fact]
        public void ShapeCastsAndOverlaps()
        {
            var wall = Make("wall", new Vector2(5f, 0f));
            wall.AddComponent<BoxCollider2D>().size = new Vector2(2f, 2f);

            var circle = Physics2D.CircleCast(Vector2.zero, 0.5f, Vector2.right, 10f);
            Assert.Same(wall.GetComponent<BoxCollider2D>(), circle.collider);
            // The circle touches the box's face at x = 4 (within the contact offset).
            Assert.InRange(circle.centroid.x, 3.48f, 3.51f);
            Assert.InRange(circle.point.x, 3.98f, 4.01f);
            Assert.True(circle.normal.x < -0.99f);
            Assert.Equal(circle.distance / 10f, circle.fraction, 4);

            var box = Physics2D.BoxCast(Vector2.zero, new Vector2(1f, 1f), 0f, Vector2.right, 10f);
            Assert.InRange(box.centroid.x, 3.48f, 3.51f);

            // The game's call: CapsuleCastNonAlloc with a movement and its length.
            var results = new RaycastHit2D[4];
            int count = Physics2D.CapsuleCastNonAlloc(new Vector2(0f, 0f), new Vector2(1f, 2f), CapsuleDirection2D.Vertical, 0f,
                new Vector2(8f, 0f), results, 8f, Physics2D.AllLayers);
            Assert.Equal(1, count);
            Assert.InRange(results[0].fraction, 3.48f / 8f, 3.51f / 8f);
            Assert.InRange(results[0].point.x, 3.98f, 4.01f);
            // Starting inside: distance zero.
            count = Physics2D.CapsuleCastNonAlloc(new Vector2(4.5f, 0f), new Vector2(1f, 2f), CapsuleDirection2D.Vertical, 0f,
                new Vector2(1f, 0f), results, 1f, Physics2D.AllLayers);
            Assert.Equal(1, count);
            Assert.Equal(0f, results[0].fraction);

            Assert.Same(wall.GetComponent<BoxCollider2D>(), Physics2D.OverlapCircle(new Vector2(3.6f, 0f), 0.5f));
            Assert.Null(Physics2D.OverlapCircle(new Vector2(3.4f, 0f), 0.5f));
            Assert.Same(wall.GetComponent<BoxCollider2D>(), Physics2D.OverlapPoint(new Vector2(5.5f, 0.5f)));
            Assert.Null(Physics2D.OverlapPoint(new Vector2(6.5f, 0.5f)));
            Assert.Single(Physics2D.OverlapBoxAll(new Vector2(5f, 0f), new Vector2(0.1f, 0.1f), 45f));
            Assert.Single(Physics2D.OverlapAreaAll(new Vector2(0f, -1f), new Vector2(4.5f, 1f)));
            Assert.NotNull(Physics2D.OverlapCapsule(new Vector2(3.8f, 0f), new Vector2(1f, 2f), CapsuleDirection2D.Vertical, 0f));
        }

        [Fact]
        public void ColliderDistanceAndClosestPoint()
        {
            var a = Make("a", Vector2.zero);
            var ca = a.AddComponent<CircleCollider2D>();
            var b = Make("b", new Vector2(3f, 0f));
            var cb = b.AddComponent<BoxCollider2D>();

            var d = ca.Distance(cb);
            Assert.True(d.isValid);
            Assert.False(d.isOverlapped);
            // Circle radius 0.5; box face at 2.5 plus its skin.
            Assert.InRange(d.distance, 1.98f, 2.01f);
            Assert.InRange(d.pointA.x, 0.49f, 0.51f);
            Assert.True(d.normal.x < -0.99f, "the normal points from pointB to pointA");

            b.transform.position = new Vector3(0.8f, 0f, 0f);
            d = Physics2D.Distance(ca, cb);
            Assert.True(d.isOverlapped);
            Assert.InRange(d.distance, -0.22f, -0.18f);
            Assert.True(Vector2.Distance(d.pointA - d.pointB, d.normal * d.distance) < 1e-3f);

            var closest = cb.ClosestPoint(new Vector2(5f, 0f));
            Assert.InRange(closest.x, 1.29f, 1.32f);
            Assert.Equal(new Vector2(0.9f, 0.1f), cb.ClosestPoint(new Vector2(0.9f, 0.1f)));
        }

        [Fact]
        public void CollidersOnChildrenAttachToTheParentBodyAndScale()
        {
            var root = Make("root", new Vector2(0f, 5f));
            var rb = root.AddComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            var child = new GameObject("child");
            child.transform.SetParent(root.transform, false);
            child.transform.localPosition = new Vector3(2f, 0f, 0f);
            child.transform.localScale = new Vector3(2f, 2f, 1f);
            var circle = child.AddComponent<CircleCollider2D>();
            Assert.Same(rb, circle.attachedRigidbody);
            Assert.Equal(1, rb.attachedColliderCount);

            var hit = Physics2D.Raycast(new Vector2(2f, 0f), Vector2.up, 10f);
            Assert.Same(circle, hit.collider);
            Assert.Equal(4f, hit.distance, 3); // radius 0.5 scaled by 2 below y = 5

            // Mass from the collider: auto mass is density times area.
            rb.useAutoMass = true;
            Assert.Equal(Mathf.PI * 1f * 1f, rb.mass, 3);
            Assert.Equal(new Vector2(2f, 0f), rb.centerOfMass);
            rb.useAutoMass = false;
            rb.mass = 2f;
            Assert.Equal(2f, rb.mass);

            // Removing the body leaves the collider static where it is.
            Object.DestroyImmediate(rb);
            Assert.True(circle.attachedRigidbody == null); // Unity's null: destroyed
            Assert.Same(circle, Physics2D.OverlapPoint(new Vector2(2f, 5f)));
        }

        [Fact]
        public void ForcesImpulsesAndTorque()
        {
            var go = Ball("ball", Vector2.zero);
            var rb = go.GetComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.angularDamping = 0f;
            rb.mass = 2f;
            rb.AddForce(new Vector2(4f, 0f), ForceMode2D.Impulse);
            Assert.Equal(2f, rb.linearVelocity.x, 4);
            rb.AddForce(new Vector2(100f, 0f));
            Assert.Equal(new Vector2(100f, 0f), rb.totalForce);
            Physics2D.Simulate(Dt);
            Assert.Equal(2f + 100f / 2f * Dt, rb.linearVelocity.x, 3);
            Assert.Equal(Vector2.zero, rb.totalForce);
            rb.AddTorque(1f, ForceMode2D.Impulse);
            Assert.True(rb.angularVelocity > 0f);
            rb.constraints = RigidbodyConstraints2D.FreezePositionX;
            float x = go.transform.position.x;
            rb.AddForce(new Vector2(0f, 10f), ForceMode2D.Impulse);
            Run(5);
            Assert.Equal(x, go.transform.position.x, 5);
            Assert.True(go.transform.position.y > 0.1f);
        }

        [Fact]
        public void MovePositionOnKinematicBodiesAndTransformSync()
        {
            var go = Make("mover", Vector2.zero);
            var rb = go.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            go.AddComponent<BoxCollider2D>();
            rb.MovePosition(new Vector2(3f, 1f));
            rb.MoveRotation(90f);
            Physics2D.Simulate(Dt);
            Assert.Equal(3f, go.transform.position.x, 3);
            Assert.Equal(1f, go.transform.position.y, 3);
            Assert.Equal(90f, go.transform.eulerAngles.z, 1);
            Assert.Equal(Vector2.zero, rb.linearVelocity);

            // Moving the transform moves the body at the next sync (queries sync with autoSyncTransforms).
            go.transform.position = new Vector3(-5f, 0f, 0f);
            Assert.Same(go.GetComponent<BoxCollider2D>(), Physics2D.OverlapPoint(new Vector2(-5f, 0f)));
            Physics2D.autoSyncTransforms = false;
            go.transform.position = new Vector3(5f, 0f, 0f);
            Assert.Null(Physics2D.OverlapPoint(new Vector2(5f, 0f)));
            Physics2D.SyncTransforms();
            Assert.NotNull(Physics2D.OverlapPoint(new Vector2(5f, 0f)));
        }

        [Fact]
        public void InstantiateCopiesTheBodyAndColliders()
        {
            var original = Make("original", Vector2.zero);
            original.SetActive(false);
            var rb = original.AddComponent<Rigidbody2D>();
            rb.bodyType = RigidbodyType2D.Kinematic;
            rb.gravityScale = 3f;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.constraints = RigidbodyConstraints2D.FreezeRotation;
            var capsule = original.AddComponent<CapsuleCollider2D>();
            capsule.size = new Vector2(1f, 3f);
            capsule.direction = CapsuleDirection2D.Horizontal;
            capsule.offset = new Vector2(0.5f, 0.25f);
            capsule.isTrigger = true;
            var poly = original.AddComponent<PolygonCollider2D>();
            poly.SetPath(0, new[] { new Vector2(0f, 0f), new Vector2(2f, 0f), new Vector2(2f, 2f), new Vector2(1f, 1f), new Vector2(0f, 2f) });

            var clone = Object.Instantiate(original);
            m_Made.Add(clone);
            var crb = clone.GetComponent<Rigidbody2D>();
            Assert.Equal(RigidbodyType2D.Kinematic, crb.bodyType);
            Assert.Equal(3f, crb.gravityScale);
            Assert.Equal(RigidbodyInterpolation2D.Interpolate, crb.interpolation);
            Assert.Equal(RigidbodyConstraints2D.FreezeRotation, crb.constraints);
            var ccap = clone.GetComponent<CapsuleCollider2D>();
            Assert.Equal(new Vector2(1f, 3f), ccap.size);
            Assert.Equal(CapsuleDirection2D.Horizontal, ccap.direction);
            Assert.Equal(new Vector2(0.5f, 0.25f), ccap.offset);
            Assert.True(ccap.isTrigger);
            Assert.Equal(5, clone.GetComponent<PolygonCollider2D>().GetTotalPointCount());

            clone.SetActive(true);
            // The concave polygon is split into convex pieces: its notch is empty.
            var cpoly = clone.GetComponent<PolygonCollider2D>();
            Assert.True(cpoly.shapeCount >= 2);
            Assert.True(cpoly.OverlapPoint(new Vector2(0.5f, 1.2f)));
            Assert.False(cpoly.OverlapPoint(new Vector2(1f, 1.6f)));
        }

        [Fact]
        public void ReadsSerializedComponents()
        {
            var body = TestYaml.DocumentBody(@"
Rigidbody2D:
  serializedVersion: 4
  m_BodyType: 1
  m_Simulated: 1
  m_UseFullKinematicContacts: 0
  m_UseAutoMass: 0
  m_Mass: 1
  m_LinearDrag: 0.5
  m_AngularDrag: 0.05
  m_GravityScale: 2
  m_Material: {fileID: 0}
  m_IncludeLayers:
    serializedVersion: 2
    m_Bits: 0
  m_ExcludeLayers:
    serializedVersion: 2
    m_Bits: 8
  m_Interpolate: 1
  m_SleepingMode: 2
  m_CollisionDetection: 1
  m_Constraints: 4
");
            var go = Make("serialized", Vector2.zero);
            go.SetActive(false);
            var rb = go.AddComponent<Rigidbody2D>();
            rb.ReadSerialized(body, null);
            Assert.Equal(RigidbodyType2D.Kinematic, rb.bodyType);
            Assert.Equal(0.5f, rb.linearDamping);
            Assert.Equal(2f, rb.gravityScale);
            Assert.Equal(8, rb.excludeLayers.value);
            Assert.Equal(RigidbodyInterpolation2D.Interpolate, rb.interpolation);
            Assert.Equal(RigidbodySleepMode2D.StartAsleep, rb.sleepMode);
            Assert.Equal(CollisionDetectionMode2D.Continuous, rb.collisionDetectionMode);
            Assert.True(rb.freezeRotation);

            var capsule = go.AddComponent<CapsuleCollider2D>();
            capsule.ReadSerialized(TestYaml.DocumentBody(@"
CapsuleCollider2D:
  m_Enabled: 1
  m_Density: 1
  m_Material: {fileID: 0}
  m_IncludeLayers:
    serializedVersion: 2
    m_Bits: 0
  m_ExcludeLayers:
    serializedVersion: 2
    m_Bits: 0
  m_LayerOverridePriority: 0
  m_ForceSendLayers:
    serializedVersion: 2
    m_Bits: 4294967295
  m_ForceReceiveLayers:
    serializedVersion: 2
    m_Bits: 4294967295
  m_ContactCaptureLayers:
    serializedVersion: 2
    m_Bits: 4294967295
  m_CallbackLayers:
    serializedVersion: 2
    m_Bits: 4294967295
  m_IsTrigger: 1
  m_UsedByEffector: 0
  m_UsedByComposite: 0
  m_Offset: {x: 0, y: 150}
  m_Size: {x: 400, y: 400}
  m_Direction: 1
"), null);
            Assert.True(capsule.isTrigger);
            Assert.Equal(new Vector2(0f, 150f), capsule.offset);
            Assert.Equal(new Vector2(400f, 400f), capsule.size);
            Assert.Equal(CapsuleDirection2D.Horizontal, capsule.direction);
            Assert.Equal(-1, capsule.callbackLayers.value);

            var settings = TestYaml.DocumentBody(@"
Physics2DSettings:
  serializedVersion: 3
  m_Gravity: {x: 0, y: -20}
  m_VelocityIterations: 6
  m_PositionIterations: 2
  m_VelocityThreshold: 1
  m_QueriesHitTriggers: 0
  m_QueriesStartInColliders: 1
  m_CallbacksOnDisable: 1
  m_LayerCollisionMatrix: fffffffffeffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffffff
");
            try
            {
                Physics2D.ReadSettings(settings);
                Assert.Equal(new Vector2(0f, -20f), Physics2D.gravity);
                Assert.Equal(6, Physics2D.velocityIterations);
                Assert.False(Physics2D.queriesHitTriggers);
                Assert.True(Physics2D.autoSyncTransforms); // absent from such an old asset
                Assert.True(Physics2D.GetIgnoreLayerCollision(1, 0));
                Assert.False(Physics2D.GetIgnoreLayerCollision(1, 1));
            }
            finally
            {
                Physics2D.velocityIterations = 8;
                Physics2D.positionIterations = 3;
            }
        }

        [Fact]
        public void InterpolationShowsThePoseBetweenSteps()
        {
            var go = Ball("ball", Vector2.zero);
            var rb = go.GetComponent<Rigidbody2D>();
            rb.gravityScale = 0f;
            rb.interpolation = RigidbodyInterpolation2D.Interpolate;
            rb.linearVelocity = new Vector2(1f, 0f);
            Physics2D.Simulate(Dt);
            Assert.Equal(Dt, go.transform.position.x, 4);
            Assert.Equal(Dt, rb.position.x, 4);
        }
    }
}
