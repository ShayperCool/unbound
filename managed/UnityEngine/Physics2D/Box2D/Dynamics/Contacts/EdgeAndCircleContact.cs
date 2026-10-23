using System.Diagnostics;
using System.Runtime.CompilerServices;
using Unbound.Box2D.Collision;
using Unbound.Box2D.Collision.Collider;
using Unbound.Box2D.Collision.Shapes;
using Unbound.Box2D.Common;

namespace Unbound.Box2D.Dynamics.Contacts
{
    /// <summary>
    ///     边缘与圆接触
    /// </summary>
    internal class EdgeAndCircleContact : Contact
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal override void Evaluate(ref Manifold manifold, in Transform xfA, Transform xfB)
        {
            CollisionUtils.CollideEdgeAndCircle(
                ref manifold,
                (EdgeShape)FixtureA.Shape,
                xfA,
                (CircleShape)FixtureB.Shape,
                xfB);
        }
    }

    internal class EdgeAndCircleContactFactory : IContactFactory
    {
        private readonly ContactPool<EdgeAndCircleContact> _pool = new ContactPool<EdgeAndCircleContact>();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Contact Create(Fixture fixtureA, int indexA, Fixture fixtureB, int indexB)
        {
            Debug.Assert(fixtureA.ShapeType == ShapeType.Edge);
            Debug.Assert(fixtureB.ShapeType == ShapeType.Circle);
            var contact = _pool.Get();
            contact.Initialize(fixtureA, 0, fixtureB, 0);
            return contact;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void Destroy(Contact contact)
        {
            _pool.Return((EdgeAndCircleContact)contact);
        }
    }
}