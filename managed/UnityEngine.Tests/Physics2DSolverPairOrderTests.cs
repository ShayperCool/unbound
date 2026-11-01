using System.Linq;
using Unbound.Box2D.Collision;
using Unbound.Box2D.Collision.Shapes;
using Unbound.Box2D.Dynamics;
using Xunit;
using B2Vector = System.Numerics.Vector2;

namespace UnityEngine.Tests
{
    // Native pair-order controls distinguish graph edges from public contact
    // enumeration. Preserve measured graph invariants independently of pair sort.
    public class Physics2DSolverPairOrderTests
    {
        static Fixture Fixture(World world, string name, BodyType type)
        {
            var body = world.CreateBody(new BodyDef { BodyType = type, UserData = name });
            var shape = new PolygonShape(); shape.SetAsBox(.5f, .5f);
            return body.CreateFixture(shape, 1);
        }
        static string[] Neighbours(Body body) =>
            body.ContactEdges.Select(edge => (string)edge.Other.UserData).ToArray();

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void SeparatePairCallbacksPrependEdgesInEitherOrder(bool reverse)
        {
            using var world = new World(B2Vector.Zero);
            var h = Fixture(world, "H", BodyType.DynamicBody);
            var l = Fixture(world, "L", BodyType.StaticBody);
            var r = Fixture(world, "R", BodyType.StaticBody);
            var first = reverse ? r : l; var second = reverse ? l : r;
            world.ContactManager.AddPairCallback(h.Proxies[0], first.Proxies[0]);
            var existing = h.Body.ContactEdges.First;
            world.ContactManager.AddPairCallback(h.Proxies[0], second.Proxies[0]);
            Assert.Equal(reverse ? new[] { "L", "R" } : new[] { "R", "L" }, Neighbours(h.Body));
            Assert.Same(existing, h.Body.ContactEdges.First.Next);
            world.ContactManager.AddPairCallback(first.Proxies[0], h.Proxies[0]);
            Assert.Equal(2, h.Body.ContactEdges.Count);
            Assert.Same(existing, h.Body.ContactEdges.Last);
        }

        [Fact]
        public void RecreatedMiddleContactPrependsWithoutMovingSurvivingEdgesOrProxies()
        {
            using var world = new World(B2Vector.Zero);
            var h = Fixture(world, "H", BodyType.DynamicBody);
            var l = Fixture(world, "L", BodyType.StaticBody);
            var r = Fixture(world, "R", BodyType.StaticBody);
            var m = Fixture(world, "M", BodyType.StaticBody);
            var fixtures = new[] { h, l, r, m };
            var ids = fixtures.Select(f => f.Proxies[0].ProxyId).ToArray();
            foreach (var neighbour in new[] { l, r, m })
                world.ContactManager.AddPairCallback(h.Proxies[0], neighbour.Proxies[0]);
            Assert.Equal(new[] { "M", "R", "L" }, Neighbours(h.Body));
            var head = h.Body.ContactEdges.First; var tail = h.Body.ContactEdges.Last;
            world.ContactManager.Destroy(head.Next.Value.Contact);
            Assert.Equal(new[] { "M", "L" }, Neighbours(h.Body));
            Assert.Same(tail, head.Next);
            world.ContactManager.AddPairCallback(h.Proxies[0], r.Proxies[0]);
            Assert.Equal(new[] { "R", "M", "L" }, Neighbours(h.Body));
            Assert.Same(head, h.Body.ContactEdges.First.Next);
            Assert.Same(tail, h.Body.ContactEdges.Last);
            Assert.Equal(ids, fixtures.Select(f => f.Proxies[0].ProxyId).ToArray());
        }

        [Fact]
        public void ContactUpdatesAppendAndSwapRemoveIndependentlyOfBodyEdges()
        {
            using var world = new World(B2Vector.Zero);
            var h = Fixture(world, "H", BodyType.DynamicBody);
            var neighbours = new[] { Fixture(world, "L", BodyType.StaticBody),
                Fixture(world, "R", BodyType.StaticBody), Fixture(world, "M", BodyType.StaticBody) };
            foreach (var neighbour in neighbours)
                world.ContactManager.AddPairCallback(h.Proxies[0], neighbour.Proxies[0]);
            string[] Updates() => world.ContactManager.ContactsInUpdateOrder.Select(c =>
                (string)(c.FixtureA.Body == h.Body ? c.FixtureB.Body : c.FixtureA.Body).UserData).ToArray();
            Assert.Equal(new[] { "L", "R", "M" }, Updates());
            world.ContactManager.Destroy(h.Body.ContactEdges.Last.Value.Contact);
            Assert.Equal(new[] { "M", "R" }, Updates());
            world.ContactManager.AddPairCallback(h.Proxies[0], neighbours[0].Proxies[0]);
            Assert.Equal(new[] { "M", "R", "L" }, Updates());
            Assert.Equal(new[] { "L", "M", "R" }, Neighbours(h.Body));
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        public void NewNonstaticSolidPairWakesBodiesBeforeItTouches(bool sensor)
        {
            using var world = new World(B2Vector.Zero);
            var a = Fixture(world, "dynamic", BodyType.DynamicBody);
            var b = Fixture(world, "kinematic", BodyType.KinematicBody);
            b.IsSensor = sensor;
            a.Body.IsAwake = b.Body.IsAwake = false;
            world.ContactManager.AddPairCallback(a.Proxies[0], b.Proxies[0]);
            Assert.Equal(!sensor, a.Body.IsAwake);
            Assert.Equal(!sensor, b.Body.IsAwake);
            Assert.False(a.Body.ContactEdges.First.Value.Contact.IsTouching);
        }
        [Fact]
        public void MovingProxyPredictsTwoDisplacementsAndRetainsContainingPadding()
        {
            var tree = new DynamicTree();
            var initial = new AABB { LowerBound = B2Vector.Zero, UpperBound = B2Vector.One };
            int id = tree.CreateProxy(initial, "proxy");
            var moved = new AABB { LowerBound = new B2Vector(3, 0), UpperBound = new B2Vector(4, 1) };
            Assert.True(tree.MoveProxy(id, moved, new B2Vector(3, 0)));
            var fat = tree.GetFatAABB(id);
            Assert.Equal(10.1f, fat.UpperBound.X);
            Assert.False(tree.MoveProxy(id, moved, B2Vector.Zero));
            Assert.Equal(fat.LowerBound, tree.GetFatAABB(id).LowerBound);
            Assert.Equal(fat.UpperBound, tree.GetFatAABB(id).UpperBound);
        }

        [Fact]
        public void ContinuousUpdatesFollowOrdinaryAndSensorContactsForTheirLifetime()
        {
            using var world = new World(B2Vector.Zero);
            var h = Fixture(world, "H", BodyType.DynamicBody); h.Body.IsBullet = true;
            var cc = Fixture(world, "continuous", BodyType.StaticBody);
            world.ContactManager.AddPairCallback(h.Proxies[0], cc.Proxies[0]);
            var ordinary = Fixture(world, "ordinary", BodyType.DynamicBody);
            var peer = Fixture(world, "peer", BodyType.StaticBody);
            world.ContactManager.AddPairCallback(ordinary.Proxies[0], peer.Proxies[0]);
            var sensor = Fixture(world, "sensor", BodyType.StaticBody); sensor.IsSensor = true;
            world.ContactManager.AddPairCallback(h.Proxies[0], sensor.Proxies[0]);
            var order = world.ContactManager.ContactsInUpdateOrder.ToArray();
            Assert.Contains(ordinary, new[] { order[0].FixtureA, order[0].FixtureB });
            Assert.Contains(sensor, new[] { order[1].FixtureA, order[1].FixtureB });
            Assert.Contains(cc, new[] { order[2].FixtureA, order[2].FixtureB });
            h.Body.IsBullet = false;
            Assert.Equal(order, world.ContactManager.ContactsInUpdateOrder.ToArray());
        }
    }
}
