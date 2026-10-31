using System.Linq;
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
    }
}
