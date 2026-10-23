using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unbound.Box2D.Collision;
using Unbound.Box2D.Collision.Collider;
using Unbound.Box2D.Collision.Shapes;
using Unbound.Box2D.Common;

namespace Unbound.Box2D.Dynamics.Contacts
{
    internal class ChainAndCircleContact : Contact
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override void Evaluate(ref Manifold manifold, in Transform xfA, Transform xfB)
        {
            var chain = (ChainShape)FixtureA.Shape;

            chain.GetChildEdge(out var edge, ChildIndexA);
            CollisionUtils.CollideEdgeAndCircle(
                ref manifold,
                edge,
                xfA,
                (CircleShape)FixtureB.Shape,
                xfB);
        }
    }

    internal class ChainAndCircleContactFactory : IContactFactory
    {
        private readonly ContactPool<ChainAndCircleContact> _pool = new ContactPool<ChainAndCircleContact>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Contact Create(Fixture fixtureA, int indexA, Fixture fixtureB, int indexB)
        {
            Debug.Assert(fixtureA.ShapeType == ShapeType.Chain);
            Debug.Assert(fixtureB.ShapeType == ShapeType.Circle);
            var contact = _pool.Get();
            contact.Initialize(fixtureA, indexA, fixtureB, indexB);
            return contact;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Destroy(Contact contact)
        {
            _pool.Return((ChainAndCircleContact)contact);
        }
    }
}