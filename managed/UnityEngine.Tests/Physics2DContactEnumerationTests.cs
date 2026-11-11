using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;

namespace UnityEngine.Tests
{
    public class Physics2DContactEnumerationTests : IDisposable
    {
        readonly List<GameObject> objects = new List<GameObject>();
        readonly Physics2DMeasuredScope release = new Physics2DMeasuredScope();
        public Physics2DContactEnumerationTests()
        {
            SharedPairObserver.Events.Clear();
            Physics2D.SyncTransforms(); Physics2D.simulationMode = SimulationMode2D.Script;
            Physics2D.gravity = Vector2.zero;
        }
        BoxCollider2D Box(string name, float x, float y = 0, bool dynamic = false)
        {
            var o = new GameObject(name); objects.Add(o); o.transform.position = new Vector3(1000+x, y, 0);
            var rb = o.AddComponent<Rigidbody2D>();
            rb.bodyType = dynamic ? RigidbodyType2D.Dynamic : RigidbodyType2D.Static;
            rb.constraints = RigidbodyConstraints2D.FreezeAll; rb.gravityScale = 0;
            rb.sleepMode = RigidbodySleepMode2D.NeverSleep;
            return o.AddComponent<BoxCollider2D>();
        }
        static void Step() { Physics2D.SyncTransforms(); Physics2D.Simulate(.02f); }
        static string[] Names(Rigidbody2D rb)
        {
            var list = new List<Collider2D>(); rb.GetContacts(list);
            return list.Select(c => c.name).ToArray();
        }
        [Fact]
        public void RemovingContactCanFillItsPublicSlotWithAnUnrelatedPair()
        {
            var h = Box("seed",0, dynamic:true); var body = h.GetComponent<Rigidbody2D>();
            var l = Box("Z",-.9f); Step();
            Box("A",.9f); Step(); Box("Q",0,.9f); Step();
            var trigger = Box("trigger",0); trigger.isTrigger = true; Step();
            Box("peer",20, dynamic:true); Box("peer-contact",20.9f); Step();
            Assert.Equal(new[] { "Z", "A", "Q", "trigger" }, Names(body));
            Physics2D.IgnoreCollision(h,l,true); Step();
            // The last scene pair belongs to peer, so filtering seed's view
            // leaves A first. A per-body swap would incorrectly put trigger first.
            Assert.Equal(new[] { "A", "Q", "trigger" }, Names(body));
            var colliders = new List<Collider2D> { l }; h.GetContacts(colliders);
            Assert.Equal(Names(body), colliders.Select(c => c.name).ToArray());
            Physics2D.IgnoreCollision(h,l,false); Step();
            Assert.Equal(new[] { "A", "Q", "trigger", "Z" }, Names(body));
        }
        [Fact]
        public void BodyViewsInterleaveAttachedCollidersAndKeepArrayPrefixes()
        {
            var h = Box("seed",0,dynamic:true); var body = h.GetComponent<Rigidbody2D>();
            var o = new GameObject("child"); objects.Add(o); o.transform.parent = h.transform;
            o.transform.localPosition = new Vector3(3,0,0); var child = o.AddComponent<BoxCollider2D>();
            Box("Z",-.9f); Step(); Box("A",3.9f); Step(); Box("Q",0,.9f); Step();
            Assert.Equal(new[] { "Z", "A", "Q" }, Names(body));
            var points = new List<ContactPoint2D> { default }; body.GetContacts(points);
            Assert.Equal(new[] { "Z", "Z", "A", "A", "Q", "Q" }, points.Select(c => c.collider.name).ToArray());
            var limited = new ContactPoint2D[3]; Assert.Equal(3,body.GetContacts(limited));
            Assert.Equal(points.Take(3).Select(c => c.collider),limited.Select(c => c.collider));
            var colliders = new Collider2D[2]; Assert.Equal(2,Physics2D.GetContacts(body,colliders));
            Assert.Equal(new[] { "Z", "A" },colliders.Select(c => c.name).ToArray());
            var childPoints = new List<ContactPoint2D>(); child.GetContacts(childPoints);
            Assert.All(childPoints,c=>Assert.Equal("A",c.collider.name));
            Assert.Equal(2,childPoints.Count);
        }
        [Fact]
        public void GetterAndDispatchShareTheSlotMovedByTriggerRemoval()
        {
            var h=Box("seed",0,dynamic:true);h.gameObject.AddComponent<SharedPairObserver>();
            var trigger=Box("trigger",0);trigger.isTrigger=true;Step();
            Box("Z",-.9f);Step();
            var peer=Box("peer",20,dynamic:true);peer.gameObject.AddComponent<SharedPairObserver>();
            Box("X",20.9f);Step();Box("A",.9f);Step();
            SharedPairObserver.Events.Clear();
            Physics2D.IgnoreCollision(h,trigger,true);Step();
            Assert.Equal(new[] { "exit:A,Z", "seed:A", "seed:Z", "peer:X" }, SharedPairObserver.Events);
        }
        [Fact]
        public void ContactPointDataSurvivesVelocityEditsUntilTheNextStep()
        {
            var a=Box("A",-.45f,dynamic:true).GetComponent<Rigidbody2D>();
            var b=Box("B",.45f,dynamic:true).GetComponent<Rigidbody2D>();
            a.constraints=b.constraints=RigidbodyConstraints2D.None;
            a.linearVelocity=new Vector2(2,.3f);b.linearVelocity=new Vector2(-1,-.1f);
            a.angularVelocity=33;b.angularVelocity=-17;Step();
            var before=new List<ContactPoint2D>();a.GetContacts(before);Assert.Equal(2,before.Count);
            a.linearVelocity=new Vector2(5,-3);b.linearVelocity=new Vector2(-7,2);
            var after=new List<ContactPoint2D>();a.GetContacts(after);Assert.Equal(before.Count,after.Count);
            for(int i=0;i<before.Count;i++)
            {
                Assert.Equal(before[i].point,after[i].point);
                Assert.Equal(before[i].separation,after[i].separation);
                Assert.Equal(before[i].relativeVelocity,after[i].relativeVelocity);
                Assert.Equal(before[i].normalImpulse,after[i].normalImpulse);
            }
        }
        [Theory]
        [InlineData(false, false)]
        [InlineData(true, false)]
        [InlineData(false, true)]
        [InlineData(true, true)]
        public void PublicImpulsesIncludeTheImpactSolve(bool continuous, bool dynamicGround)
        {
            var ground = Box("ground", 0, -.5f).GetComponent<Rigidbody2D>();
            ground.GetComponent<BoxCollider2D>().size = new Vector2(20, 1);
            ground.bodyType = dynamicGround ? RigidbodyType2D.Dynamic : RigidbodyType2D.Static;
            ground.mass = 3;
            ground.constraints = RigidbodyConstraints2D.FreezePositionX | RigidbodyConstraints2D.FreezeRotation;
            var o = new GameObject("moving"); objects.Add(o);
            o.transform.position = new Vector3(1000, .6f, 0);
            var collider = o.AddComponent<CircleCollider2D>();
            collider.sharedMaterial = new PhysicsMaterial2D { bounciness = .8f, friction = 0 };
            var body = o.AddComponent<Rigidbody2D>();
            body.gravityScale = 0;
            body.mass = 2;
            body.collisionDetectionMode = continuous ? CollisionDetectionMode2D.Continuous : CollisionDetectionMode2D.Discrete;
            var observer = o.AddComponent<ImpulseObserver>();
            body.linearVelocity = new Vector2(0, -8);
            for (int i = 0; i < 5 && observer.Collision == null; i++) Step();
            Assert.NotNull(observer.Collision);
            var contacts = new List<ContactPoint2D>(); body.GetContacts(contacts);
            var contact = Assert.Single(contacts);
            Assert.True(contact.normalImpulse > 0);
            Assert.InRange(Mathf.Abs(contact.normalImpulse - body.mass * (body.linearVelocity.y + 8)), 0, .001f);
            Assert.Equal(contact.normalImpulse, observer.Collision.GetContact(0).normalImpulse);
            Assert.Equal(contact.relativeVelocity, observer.Collision.relativeVelocity);
            body.linearVelocity = new Vector2(5, 7);
            contacts.Clear(); body.GetContacts(contacts);
            Assert.Equal(contact.normalImpulse, Assert.Single(contacts).normalImpulse);
            Assert.Equal(contact.relativeVelocity, contacts[0].relativeVelocity);
            Step();
            contacts.Clear(); body.GetContacts(contacts);
            Assert.Empty(contacts);
        }
        [Theory]
        [InlineData(false, false, false, -.418789566f)]
        [InlineData(true, false, false, -.385338962f)]
        [InlineData(false, true, false, -.418789566f)]
        [InlineData(true, true, false, -.385338962f)]
        [InlineData(false, false, true, -.7649468f)]
        [InlineData(true, false, true, 3.14987016f)]
        public void ContactSolvesRespectFrozenTranslationAxes(bool continuous, bool sideways, bool box, float tangentImpulse)
        {
            Physics2D.gravity = sideways ? new Vector2(-9.81f, 0) : new Vector2(0, -9.81f);
            var floor = new GameObject("ground"); objects.Add(floor);
            floor.transform.position = sideways ? new Vector3(-.5f, 0, 0) : new Vector3(0, -.5f, 0);
            floor.transform.rotation = Quaternion.Euler(0, 0, sideways ? -90 : 0);
            floor.AddComponent<BoxCollider2D>().size = new Vector2(20, 1);
            var ground = floor.AddComponent<Rigidbody2D>();
            ground.mass = 3; ground.gravityScale = 0;
            ground.constraints = (sideways ? RigidbodyConstraints2D.FreezePositionY : RigidbodyConstraints2D.FreezePositionX)
                | RigidbodyConstraints2D.FreezeRotation;
            var moving = new GameObject("moving"); objects.Add(moving);
            moving.transform.position = sideways ? new Vector3(.6f, 0, 0) : new Vector3(0, .6f, 0);
            moving.transform.rotation = Quaternion.Euler(0, 0, sideways ? -83 : 7);
            Collider2D collider = box ? (Collider2D)moving.AddComponent<BoxCollider2D>() : moving.AddComponent<CircleCollider2D>();
            collider.sharedMaterial = new PhysicsMaterial2D { bounciness = .8f, friction = .4f };
            var body = moving.AddComponent<Rigidbody2D>();
            body.collisionDetectionMode = continuous ? CollisionDetectionMode2D.Continuous : CollisionDetectionMode2D.Discrete;
            body.linearVelocity = sideways ? new Vector2(-8, -1) : new Vector2(1, -8);
            body.angularVelocity = 17;
            var observer = moving.AddComponent<ImpulseObserver>();
            for (int i = 0; i < 5 && observer.Collision == null; i++) Step();
            Assert.NotNull(observer.Collision);
            Assert.InRange(Mathf.Abs(observer.Collision.GetContact(0).tangentImpulse - tangentImpulse), 0, .001f);
            Assert.Equal(0f, sideways ? ground.linearVelocity.y : ground.linearVelocity.x);
            Assert.Equal(0f, sideways ? ground.position.y : ground.position.x);
        }
        [Theory]
        [InlineData(false, false)]
        [InlineData(false, true)]
        [InlineData(true, false)]
        [InlineData(true, true)]
        public void PublicImpulsesAccumulateAcrossOneSimulationCall(bool continuous, bool refresh)
        {
            bool oldSub = Physics2D.useSubStepping, oldRefresh = Physics2D.useSubStepContacts;
            float oldFps = Physics2D.minSubStepFPS; int oldCount = Physics2D.maxSubStepCount;
            try
            {
                Physics2D.useSubStepping = true; Physics2D.useSubStepContacts = refresh;
                Physics2D.minSubStepFPS = 200; Physics2D.maxSubStepCount = 4;
                Physics2D.gravity = new Vector2(0, -9.81f);
                var floor = Box("ground", 0, -.5f);
                floor.size = new Vector2(20, 1);
                var moving = new GameObject("moving"); objects.Add(moving);
                moving.transform.position = new Vector3(1000, .49f, 0);
                moving.AddComponent<CircleCollider2D>().sharedMaterial = new PhysicsMaterial2D { bounciness = 0, friction = 0 };
                var body = moving.AddComponent<Rigidbody2D>();
                body.sleepMode = RigidbodySleepMode2D.NeverSleep;
                body.collisionDetectionMode = continuous ? CollisionDetectionMode2D.Continuous : CollisionDetectionMode2D.Discrete;
                var contacts = new List<ContactPoint2D>();
                for (int i = 0; i < 3; i++)
                {
                    Step(); contacts.Clear(); body.GetContacts(contacts);
                    Assert.InRange(Mathf.Abs(Assert.Single(contacts).normalImpulse - .1962f), 0, .00001f);
                }
            }
            finally
            {
                Physics2D.useSubStepping = oldSub; Physics2D.useSubStepContacts = oldRefresh;
                Physics2D.minSubStepFPS = oldFps; Physics2D.maxSubStepCount = oldCount;
            }
        }
        [Theory]
        [InlineData("6000.0.71f1", true)]
        [InlineData("6000.0.72f1", false)]
        public void ContactPayloadSelectsTheMeasuredPolicyAndReportsItsFallback(string version, bool measured)
        {
            const string member = "Physics2D contact payload";
            var hits = (System.Collections.Concurrent.ConcurrentDictionary<string, int>)typeof(Unbound.Runtime.Stubs)
                .GetField("Hits", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static).GetValue(null);
            bool hadHit = hits.TryRemove(member, out int previousHit);
            var logger = Debug.unityLogger;
            var handler = logger.logHandler;
            bool enabled = logger.logEnabled; var filter = logger.filterLogType;
            var capture = new PayloadLogCapture(handler);
            try
            {
                logger.logHandler = capture; logger.logEnabled = true; logger.filterLogType = LogType.Log;
                ProjectInfo.UnityVersion = version;
                var circle = Impact(false);
                Assert.Equal(measured, circle.GetContact(0).normalImpulse > 0);
                var box = Impact(true);
                var pointVelocity = box.GetContact(0).relativeVelocity;
                if (measured) Assert.Equal(pointVelocity, box.relativeVelocity);
                else Assert.True((pointVelocity - box.relativeVelocity).magnitude > 1);
                Assert.Equal(!measured, capture.Warnings.Exists(message => message.Contains(member)));
            }
            finally
            {
                logger.logHandler = handler; logger.logEnabled = enabled; logger.filterLogType = filter;
                hits.TryRemove(member, out _);
                if (hadHit) hits[member] = previousHit;
            }
        }
        Collision2D Impact(bool box)
        {
            Unbound.Runtime.Physics2DWorld.Reset();
            Physics2D.gravity = new Vector2(0, -9.81f);
            var floor = new GameObject("ground"); objects.Add(floor);
            floor.transform.position = new Vector3(0, -.5f, 0);
            floor.AddComponent<BoxCollider2D>().size = new Vector2(20, 1);
            var moving = new GameObject("moving"); objects.Add(moving);
            moving.transform.position = new Vector3(0, .6f, 0);
            moving.transform.rotation = Quaternion.Euler(0, 0, box ? 13 : 0);
            Collider2D collider = box ? (Collider2D)moving.AddComponent<BoxCollider2D>() : moving.AddComponent<CircleCollider2D>();
            collider.sharedMaterial = new PhysicsMaterial2D { bounciness = .8f, friction = .4f };
            var body = moving.AddComponent<Rigidbody2D>();
            body.collisionDetectionMode = CollisionDetectionMode2D.Continuous;
            body.linearVelocity = new Vector2(1, -12); body.angularVelocity = 17;
            var observer = moving.AddComponent<ImpulseObserver>();
            for (int i = 0; i < 20 && observer.Collision == null; i++) Step();
            Assert.NotNull(observer.Collision);
            var collision = observer.Collision;
            foreach (var o in objects) if (o) Object.DestroyImmediate(o);
            objects.Clear();
            return collision;
        }
        sealed class PayloadLogCapture : ILogHandler
        {
            readonly ILogHandler inner;
            internal readonly List<string> Warnings = new List<string>();
            internal PayloadLogCapture(ILogHandler inner) => this.inner = inner;
            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type == LogType.Warning)
                    Warnings.Add(string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args));
                inner.LogFormat(type, context, format, args);
            }
            public void LogException(Exception exception, Object context) => inner.LogException(exception, context);
        }
        public sealed class ImpulseObserver : MonoBehaviour
        {
            public Collision2D Collision;
            void OnCollisionEnter2D(Collision2D collision) => Collision = collision;
        }
        [Fact]
        public void NewTouchingPairAppendsBeforeAnEndedPairReleasesItsSceneSlot()
        {
            var h = Box("seed", 0, dynamic: true);
            h.gameObject.AddComponent<PairLifetimeObserver>();
            var l = Box("L", -.9f); Step();
            Box("R", .9f); Step(); Box("M", 0, .9f); Step();
            PairLifetimeObserver.Events.Clear();
            Physics2D.IgnoreCollision(h, l, true);
            Box("N", 0, -.9f); Step();
            Assert.Equal(new[] { "N", "R", "M" }, Names(h.GetComponent<Rigidbody2D>()));
            Assert.Equal(new[] { "exit:L:N,R,M", "enter:N:N,R,M", "stay:R:N,R,M", "stay:M:N,R,M" },
                PairLifetimeObserver.Events);
        }
        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void CallbackRemovalOfAnEarlierPairStillDispatchesTheMovedTail(bool callbacksOnDisable)
        {
            bool previous = Physics2D.callbacksOnDisable;
            Physics2D.callbacksOnDisable = callbacksOnDisable;
            CallbackRemovalObserver.Events.Clear();
            try
            {
                var a = Box("A", 0, dynamic: true);
                a.gameObject.AddComponent<CallbackRemovalObserver>(); Box("A-peer", .9f); Step();
                var b = Box("B", 20, dynamic: true);
                var observer = b.gameObject.AddComponent<CallbackRemovalObserver>(); Box("B-peer", 20.9f); Step();
                var c = Box("C", 40, dynamic: true);
                c.gameObject.AddComponent<CallbackRemovalObserver>(); Box("C-peer", 40.9f); Step();
                CallbackRemovalObserver.Events.Clear();
                observer.Disable = a;
                Step();
                Assert.False(a.enabled);
                Assert.Equal(new[] { "A", "B", "C" }, CallbackRemovalObserver.Events);
            }
            finally { Physics2D.callbacksOnDisable = previous; }
        }
        public sealed class CallbackRemovalObserver : MonoBehaviour
        {
            public static readonly List<string> Events = new List<string>();
            public Collider2D Disable;
            void OnCollisionStay2D(Collision2D collision)
            {
                Events.Add(name);
                if (Disable != null) { var target = Disable; Disable = null; target.enabled = false; }
            }
        }
        public sealed class PairLifetimeObserver : MonoBehaviour
        {
            public static readonly List<string> Events = new List<string>();
            void Record(string phase, Collision2D collision) => Events.Add(phase + ":" + collision.collider.name + ":" +
                string.Join(",", Names(GetComponent<Rigidbody2D>())));
            void OnCollisionEnter2D(Collision2D collision) => Record("enter", collision);
            void OnCollisionStay2D(Collision2D collision) => Record("stay", collision);
            void OnCollisionExit2D(Collision2D collision) => Record("exit", collision);
        }
        [Theory]
        [InlineData(0f, 0f, 7f, 7f)]
        [InlineData(23f, 0f, 7f, 7.00000048f)]
        [InlineData(0f, 35f, 7f, 7.00000048f)]
        [InlineData(23f, 35f, 7f, -.34079054f)]
        [InlineData(0f, 0f, 181f, -179f)]
        public void BodyAndQuaternionSetterUseTheNativeTwistAngle(float x, float y, float z, float expected)
        {
            var o = new GameObject("rotation"); objects.Add(o);
            var q = Quaternion.Euler(x, y, z); o.transform.rotation = q;
            var rb = o.AddComponent<Rigidbody2D>();
            rb.constraints = RigidbodyConstraints2D.FreezeAll;
            o.AddComponent<BoxCollider2D>(); Step();
            Assert.InRange(Mathf.Abs(rb.rotation - expected), 0f, .0001f);
            rb.SetRotation(q);
            Assert.InRange(Mathf.Abs(rb.rotation - expected), 0f, .0001f);
        }
        [Fact]
        public void PendingRotationSubtractsTheCurrentRadianAngle()
        {
            var o = new GameObject("moving"); objects.Add(o);
            var rb = o.AddComponent<Rigidbody2D>(); rb.bodyType = RigidbodyType2D.Kinematic;
            o.AddComponent<BoxCollider2D>();
            for (int i = 0; i < 8; i++)
            {
                Step(); rb.MoveRotation(rb.rotation + 1f);
            }
            // The independent full-prefix Native captures both report exactly
            // seven degrees here, before the eighth pending move is applied.
            Assert.Equal(7f, rb.rotation);
        }
        [Fact]
        public void AttachedBoxKeepsItsNativeLocalCenterAwayFromTheOrigin()
        {
            var o = new GameObject("center"); objects.Add(o);
            o.transform.position = new Vector3(.3f, 3.53f, 0);
            o.transform.rotation = Quaternion.Euler(0, 0, 7);
            o.AddComponent<BoxCollider2D>(); var rb = o.AddComponent<Rigidbody2D>();
            Physics2D.SyncTransforms();
            Assert.Equal(Vector2.zero, rb.centerOfMass);
        }
        [Fact]
        public void RotatedBodyKeepsTheNativeLocalPolygonVertices()
        {
            var o = new GameObject("polygon"); objects.Add(o);
            o.transform.position = new Vector3(.3f, -.2f, 2f);
            o.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            o.transform.localScale = new Vector3(1f, .8f, 1f);
            o.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            var box = o.AddComponent<BoxCollider2D>(); box.size = new Vector2(2f, 1f);
            Physics2D.SyncTransforms();
            var shape = (Unbound.Box2D.Collision.Shapes.PolygonShape)box.m_Fixtures[0].Shape;
            Assert.Equal(4, shape.Count);
            // Complete local vertices read independently from Native's polygon input.
            var expected = new[] {
                new System.Numerics.Vector2(1.000000119f, -.399999857f),
                new System.Numerics.Vector2(.999999940f, .400000215f),
                new System.Numerics.Vector2(-1.000000119f, .399999857f),
                new System.Numerics.Vector2(-.999999940f, -.400000215f) };
            for (int i = 0; i < expected.Length; i++) Assert.Equal(expected[i], shape.Vertices[i]);
        }
        [Fact]
        public void TranslatedCapsulesKeepTheNativeTipInsetOrder()
        {
            var v = new GameObject("vertical"); objects.Add(v);
            v.transform.position = new Vector3(.3f, -.2f, 2f);
            v.transform.rotation = Quaternion.Euler(0f, 0f, 23f);
            v.transform.localScale = new Vector3(1f, .8f, 1f);
            var vertical = v.AddComponent<CapsuleCollider2D>(); vertical.size = new Vector2(1f, 3f);
            var h = new GameObject("horizontal"); objects.Add(h);
            h.transform.position = new Vector3(.3f, -.2f, 2f);
            h.transform.localScale = new Vector3(1f, .8f, 1f);
            var horizontal = h.AddComponent<CapsuleCollider2D>();
            horizontal.direction = CapsuleDirection2D.Horizontal; horizontal.size = new Vector2(3f, 1f);
            Physics2D.SyncTransforms();
            var a = (Unbound.Box2D.Collision.Shapes.CapsuleShape)vertical.m_Fixtures[0].Shape;
            var b = (Unbound.Box2D.Collision.Shapes.CapsuleShape)horizontal.m_Fixtures[0].Shape;
            Assert.Equal(new System.Numerics.Vector2(.573511779f, -.844353437f), a.P1);
            Assert.Equal(new System.Numerics.Vector2(.0264882594f, .444353372f), a.P2);
            Assert.Equal(new System.Numerics.Vector2(-.8000000715f, -.2f), b.P1);
            Assert.Equal(new System.Numerics.Vector2(1.399999976f, -.2f), b.P2);
        }
        [Theory]
        [InlineData(false, .7000001072883606f, 8.344650836988876e-8f, .5000000596046448f, -1.1920928244535389e-7f, 1f)]
        [InlineData(true, 1.2516976255483314e-7f, -.7000000476837158f, .5f, 1f, 1.7881393432617188e-7f)]
        public void QuarterTurnCapsulesKeepMeasuredTipsAndNormals(bool attached, float x, float y, float radius, float nx, float ny)
        {
            var o = new GameObject("capsule"); objects.Add(o);
            o.transform.position = new Vector3(0f, 0f, 2f);
            o.transform.rotation = Quaternion.Euler(0f, 0f, 90f);
            o.transform.localScale = new Vector3(1f, .8f, 1f);
            if (attached) o.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Kinematic;
            var c = o.AddComponent<CapsuleCollider2D>(); c.size = new Vector2(1f, 3f);
            Physics2D.SyncTransforms();
            var shape = (Unbound.Box2D.Collision.Shapes.CapsuleShape)c.m_Fixtures[0].Shape;
            Assert.Equal(new System.Numerics.Vector2(x, y), shape.P1);
            Assert.Equal(new System.Numerics.Vector2(-x, -y), shape.P2);
            Assert.Equal(radius, shape.Radius);
            Assert.Equal(new System.Numerics.Vector2(nx, ny), shape.Normals[0]);
            Assert.Equal(new System.Numerics.Vector2(-nx, -ny), shape.Normals[1]);
            var group = new PhysicsShapeGroup2D(); c.GetShapes(group);
            var points = new List<Vector2>(); group.GetShapeVertices(0,points);
            Assert.Equal(new Vector2(x,y),points[0]); Assert.Equal(new Vector2(-x,-y),points[1]);

        }
        [Fact]
        public void CapsuleCircleCoreCoincidenceUsesTheNativeEdgeNormal()
        {
            var capsule = new Unbound.Box2D.Collision.Shapes.CapsuleShape(
                new System.Numerics.Vector2(-.8000000715f, -.2f),
                new System.Numerics.Vector2(1.399999976f, -.2f), .4f);
            var circle = new Unbound.Box2D.Collision.Shapes.CircleShape {
                Position = new System.Numerics.Vector2(.3f, -.2f), Radius = .3f };
            var pose = new Unbound.Box2D.Common.Transform(System.Numerics.Vector2.Zero, new Unbound.Box2D.Common.Rotation(0f));
            var manifold = new Unbound.Box2D.Collision.Collider.Manifold();
            Unbound.Box2D.Collision.CollisionUtils.CollidePolygonAndCircle(ref manifold, capsule, pose, circle, pose);
            Assert.Equal(1, manifold.PointCount);
            Assert.Equal(new System.Numerics.Vector2(0f, 1f), manifold.LocalNormal);
        }
        [Fact]
        public void WheelConstraintKeepsTheNativeLocalAnchorAndMotion()
        {
            Physics2D.gravity = new Vector2(0f, -9.81f);
            var own = new GameObject("own"); objects.Add(own);
            own.transform.position = new Vector3(1f, -3f, 0f);
            own.transform.rotation = Quaternion.Euler(0f, 0f, 20f);
            var a = own.AddComponent<Rigidbody2D>(); a.mass = 2f;
            a.linearDamping = a.angularDamping = 0f; a.angularVelocity = 30f;
            own.AddComponent<BoxCollider2D>().size = new Vector2(1f, .5f);
            var peer = new GameObject("peer"); objects.Add(peer);
            peer.transform.position = new Vector3(-1f, 1f, 0f);
            peer.transform.rotation = Quaternion.Euler(0f, 0f, -15f);
            var b = peer.AddComponent<Rigidbody2D>(); b.mass = 3f;
            b.linearDamping = b.angularDamping = 0f;
            peer.AddComponent<BoxCollider2D>().size = new Vector2(1f, .5f);
            var joint = own.AddComponent<WheelJoint2D>(); joint.connectedBody = b;
            joint.anchor = new Vector2(.25f, .1f);
            joint.suspension = new JointSuspension2D { angle = 0f, frequency = 2f, dampingRatio = .7f };
            joint.motor = new JointMotor2D { motorSpeed = 45f, maxMotorTorque = 5f };
            Physics2D.SyncTransforms();
            var constraint = (Unbound.Box2D.Dynamics.Joints.WheelJoint)joint.CurrentJoint;
            Assert.Equal(new System.Numerics.Vector2(.25f, .1f), constraint.GetLocalAnchorA());
            Step(); Step();
            Assert.InRange(Mathf.Abs(a.angularVelocity - 7.38356924f), 0f, .0002f);
            Assert.InRange(Mathf.Abs(a.linearVelocity.y - 1.66361785f), 0f, .0002f);
        }
        public sealed class SharedPairObserver:MonoBehaviour
        {
            public static readonly List<string> Events=new List<string>();
            void OnCollisionStay2D(Collision2D collision)=>Events.Add(name+":"+collision.collider.name);
            void OnTriggerExit2D(Collider2D other)
            {
                var colliders=new List<Collider2D>();GetComponent<Rigidbody2D>().GetContacts(colliders);
                Events.Add("exit:"+string.Join(",",colliders.Select(c=>c.name)));
            }
        }
        public void Dispose()
        {
            foreach(var o in objects) if(o) Object.DestroyImmediate(o);
            Physics2D.gravity = new Vector2(0,-9.81f); Physics2D.Simulate(.02f);
            SharedPairObserver.Events.Clear();
            PairLifetimeObserver.Events.Clear();
            CallbackRemovalObserver.Events.Clear();
            release.Dispose();
        }
    }

    internal sealed class Physics2DMeasuredScope : IDisposable
    {
        readonly string previous = ProjectInfo.UnityVersion;
        internal Physics2DMeasuredScope()
        {
            Unbound.Runtime.Physics2DWorld.Reset();
            ProjectInfo.UnityVersion = "6000.0.71f1";
        }
        public void Dispose()
        {
            Unbound.Runtime.Physics2DWorld.Reset();
            ProjectInfo.UnityVersion = previous;
        }
    }
}
