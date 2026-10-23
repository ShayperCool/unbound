using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unbound.Box2D.Collision;
using Unbound.Box2D.Collision.Collider;
using Unbound.Box2D.Collision.Shapes;
using Unbound.Box2D.Common;

namespace Unbound.Box2D.Dynamics.Contacts
{
    internal class PolygonContact : Contact
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override void Evaluate(ref Manifold manifold, in Transform xfA, Transform xfB)
        {
            CollisionUtils.CollidePolygons(
                ref manifold,
                (PolygonShape)FixtureA.Shape,
                xfA,
                (PolygonShape)FixtureB.Shape,
                xfB);
        }
    }

    internal class PolygonContactFactory : IContactFactory
    {
        private readonly ContactPool<PolygonContact> _pool = new ContactPool<PolygonContact>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Contact Create(Fixture fixtureA, int indexA, Fixture fixtureB, int indexB)
        {
            Debug.Assert(fixtureA.ShapeType == ShapeType.Polygon);
            Debug.Assert(fixtureB.ShapeType == ShapeType.Polygon);
            var contact = _pool.Get();
            contact.Initialize(fixtureA, 0, fixtureB, 0);
            return contact;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Destroy(Contact contact)
        {
            _pool.Return((PolygonContact)contact);
        }
    }
}