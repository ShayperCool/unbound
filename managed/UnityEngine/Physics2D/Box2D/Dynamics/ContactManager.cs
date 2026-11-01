using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using Unbound.Box2D.Collision;
using Unbound.Box2D.Collision.Shapes;
using Unbound.Box2D.Common;
using Unbound.Box2D.Dynamics.Contacts;
using Unbound.Box2D.Dynamics.Internal;

namespace Unbound.Box2D.Dynamics
{
    internal class ContactRegister
    {
        public readonly IContactFactory Factory;

        public readonly bool Primary;

        public ContactRegister(IContactFactory factory, bool primary)
        {
            Primary = primary;

            Factory = factory;
        }
    }

    // Delegate of b2World.
    internal class ContactManager : IAddPairCallback, IDisposable
    {
        public static readonly IContactFilter DefaultContactFilter = new DefaultContactFilter();

        public BroadPhase BroadPhase { get; private set; } = new BroadPhase();

        public IContactFilter ContactFilter = DefaultContactFilter;

        public LinkedList<Contact> ContactList { get; private set; } = new LinkedList<Contact>();
        private readonly List<Contact> _discreteContacts = new List<Contact>();
        private readonly List<Contact> _continuousContacts = new List<Contact>();
        private readonly Dictionary<Contact, (List<Contact> list, int index)> _contactSlots = new Dictionary<Contact, (List<Contact>, int)>();

        internal IEnumerable<Contact> ContactsInUpdateOrder
        {
            get
            {
                foreach (var contact in _discreteContacts) yield return contact;
                foreach (var contact in _continuousContacts) yield return contact;
            }
        }

        private static bool ContinuousContact(Contact contact) =>
            !contact.FixtureA.IsSensor && !contact.FixtureB.IsSensor &&
            (contact.FixtureA.Body.UnityContinuous || contact.FixtureB.Body.UnityContinuous);

        private void AddContactSlot(Contact contact)
        {
            var list = ContinuousContact(contact) ? _continuousContacts : _discreteContacts;
            _contactSlots.Add(contact, (list, list.Count));
            list.Add(contact);
        }

        private void RemoveContactSlot(Contact contact)
        {
            var (list, index) = _contactSlots[contact];
            var moved = list[list.Count - 1];
            list[index] = moved;
            _contactSlots[moved] = (list, index);
            list.RemoveAt(list.Count - 1);
            _contactSlots.Remove(contact);
        }

        public IContactListener ContactListener;

        public int ContactCount => ContactList.Count;

        private static readonly ContactRegister[,] _registers = new ContactRegister[(int)ShapeType.TypeCount, (int)ShapeType.TypeCount];

        static ContactManager()
        {
            Register(ShapeType.Circle, ShapeType.Circle, new CircleContactFactory());
            Register(ShapeType.Polygon, ShapeType.Circle, new PolygonAndCircleContactFactory());
            Register(ShapeType.Polygon, ShapeType.Polygon, new PolygonContactFactory());
            Register(ShapeType.Edge, ShapeType.Circle, new EdgeAndCircleContactFactory());
            Register(ShapeType.Edge, ShapeType.Polygon, new EdgeAndPolygonContactFactory());
            Register(ShapeType.Chain, ShapeType.Circle, new ChainAndCircleContactFactory());
            Register(ShapeType.Chain, ShapeType.Polygon, new ChainAndPolygonContactFactory());

            void Register(ShapeType type1, ShapeType type2, IContactFactory factory)
            {
                Debug.Assert(0 <= type1 && type1 < ShapeType.TypeCount);
                Debug.Assert(0 <= type2 && type2 < ShapeType.TypeCount);

                _registers[(int)type1, (int)type2] = new ContactRegister(factory, true);
                if (type1 != type2)
                {
                    _registers[(int)type2, (int)type1] = new ContactRegister(factory, false);
                }
            }
        }

        private const int DisposedFalse = 0;

        private const int DisposedTrue = 1;

        private int _disposed = DisposedFalse;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, DisposedTrue) == DisposedTrue)
            {
                return;
            }

            BroadPhase = null;
            ContactList?.Clear();
            ContactList = null;
            _discreteContacts.Clear();
            _continuousContacts.Clear();
            _contactSlots.Clear();

            ContactFilter = null;
            ContactListener = null;
        }

        private Contact CreateContact(
            Fixture fixtureA,
            int indexA,
            Fixture fixtureB,
            int indexB)
        {
            // Polygon/segment manifolds use the polygon as the first fixture.
            // Both views retain the same core clipping and contact ordering.
            if (fixtureA.Shape is PolygonShape a && a.Count == 2 &&
                fixtureB.Shape is PolygonShape b && b.Count > 2)
                (fixtureA, fixtureB, indexA, indexB) = (fixtureB, fixtureA, indexB, indexA);
            var type1 = fixtureA.ShapeType;
            var type2 = fixtureB.ShapeType;

            Debug.Assert(0 <= type1 && type1 < ShapeType.TypeCount);
            Debug.Assert(0 <= type2 && type2 < ShapeType.TypeCount);

            var reg = _registers[(int)type1, (int)type2]
                   ?? throw new NullReferenceException($"{type1.ToString()} can not contact to {type2.ToString()}");
            if (reg.Primary)
            {
                return reg.Factory.Create(fixtureA, indexA, fixtureB, indexB);
            }

            return reg.Factory.Create(fixtureB, indexB, fixtureA, indexA);
        }

        private void DestroyContact(Contact contact)
        {
            var fixtureA = contact.FixtureA;
            var fixtureB = contact.FixtureB;

            if (contact.Manifold.PointCount > 0 && fixtureA.IsSensor == false && fixtureB.IsSensor == false)
            {
                fixtureA.Body.IsAwake = true;
                fixtureB.Body.IsAwake = true;
            }

            var typeA = fixtureA.ShapeType;
            var typeB = fixtureB.ShapeType;

            Debug.Assert(0 <= typeA && typeB < ShapeType.TypeCount);
            Debug.Assert(0 <= typeA && typeB < ShapeType.TypeCount);
            var reg = _registers[(int)typeA, (int)typeB];
            reg.Factory.Destroy(contact);
        }

        public void AddPairCallback(object proxyUserDataA, object proxyUserDataB)
        {
            var proxyA = (FixtureProxy)proxyUserDataA;
            var proxyB = (FixtureProxy)proxyUserDataB;
            var fixtureA = proxyA.Fixture;
            var fixtureB = proxyB.Fixture;

            var indexA = proxyA.ChildIndex;
            var indexB = proxyB.ChildIndex;

            var bodyA = fixtureA.Body;
            var bodyB = fixtureB.Body;

            // Are the fixtures on the same body?
            if (bodyA == bodyB)
            {
                return;
            }

            // TODO_ERIN use a hash table to remove a potential bottleneck when both
            // bodies have a lot of contacts.
            // Does a contact already exist?
            var node1 = bodyB.ContactEdges.First;
            while (node1 != null)
            {
                var contactEdge = node1.Value;
                node1 = node1.Next;

                if ((contactEdge.Contact.FixtureA == fixtureA
                  && contactEdge.Contact.FixtureB == fixtureB
                  && contactEdge.Contact.ChildIndexA == indexA
                  && contactEdge.Contact.ChildIndexB == indexB)
                 || (contactEdge.Contact.FixtureA == fixtureB
                  && contactEdge.Contact.FixtureB == fixtureA
                  && contactEdge.Contact.ChildIndexA == indexB
                  && contactEdge.Contact.ChildIndexB == indexA))
                {
                    // A contact already exists.
                    return;
                }
            }

            if (bodyB.ShouldCollide(bodyA) == false                        // Does a joint override collision? Is at least one body dynamic?
             || ContactFilter?.ShouldCollide(fixtureA, fixtureB) == false) // Check user filtering.
            {
                return;
            }

            // Call the factory.
            var c = CreateContact(fixtureA, indexA, fixtureB, indexB);
            Debug.Assert(c != default, "Get null contact!");

            // Contact creation may swap fixtures.
            fixtureA = c.FixtureA;
            fixtureB = c.FixtureB;
            bodyA = fixtureA.Body;
            bodyB = fixtureB.Body;

            // Native wakes both nonstatic participants when a solid pair is
            // allocated, before its narrowphase first reports touching.
            if (!fixtureA.IsSensor && !fixtureB.IsSensor ||
                bodyA.BodyType == BodyType.StaticBody || bodyB.BodyType == BodyType.StaticBody)
            {
                if (bodyA.BodyType != BodyType.StaticBody) bodyA.IsAwake = true;
                if (bodyB.BodyType != BodyType.StaticBody) bodyB.IsAwake = true;
            }

            // Insert into the world.
            c.Node.Value = c;
            ContactList.AddFirst(c.Node);
            AddContactSlot(c);

            // Connect to island graph.

            // Connect to body A
            c.NodeA.Contact = c;
            c.NodeA.Other = bodyB;
            c.NodeA.Node.Value = c.NodeA;
            bodyA.ContactEdges.AddFirst(c.NodeA.Node);

            // Connect to body B
            c.NodeB.Contact = c;
            c.NodeB.Other = bodyA;
            c.NodeB.Node.Value = c.NodeB;
            bodyB.ContactEdges.AddFirst(c.NodeB.Node);
        }

        public void FindNewContacts()
        {
            BroadPhase.UpdatePairs(this);
        }

        public void Destroy(Contact c)
        {
            Debug.Assert(ContactCount > 0);
            var fixtureA = c.FixtureA;
            var fixtureB = c.FixtureB;
            var bodyA = fixtureA.Body;
            var bodyB = fixtureB.Body;

            if (c.IsTouching) // 存在接触监听器且当前接触点接触,则触发结束接触
            {
                ContactListener?.EndContact(c);
            }

            // Remove from the world.
            ContactList.Remove(c.Node);
            RemoveContactSlot(c);

            // Remove from body 1
            bodyA.ContactEdges.Remove(c.NodeA.Node);

            // Remove from body 2
            bodyB.ContactEdges.Remove(c.NodeB.Node);

            // Call the factory.
            DestroyContact(c);
        }

        public void Collide(bool sensorsOnly = false)
        {
            Collide(_discreteContacts, sensorsOnly);
            Collide(_continuousContacts, sensorsOnly);
        }

        private void Collide(List<Contact> contacts, bool sensorsOnly)
        {
            // Update awake contacts.
            for (int i = 0; i < contacts.Count; i++)
            {
                var c = contacts[i];
                var fixtureA = c.FixtureA;
                var fixtureB = c.FixtureB;
                if (sensorsOnly && !fixtureA.IsSensor && !fixtureB.IsSensor)
                {
                    continue;
                }
                var indexA = c.ChildIndexA;
                var indexB = c.ChildIndexB;
                var bodyA = fixtureA.Body;
                var bodyB = fixtureB.Body;

                // Is this contact flagged for filtering?
                if (c.Flags.IsSet(Contact.ContactFlag.FilterFlag))
                {
                    // Should these bodies collide?
                    if (bodyB.ShouldCollide(bodyA) == false)
                    {
                        Destroy(c);
                        --i;
                        continue;
                    }

                    // Check user filtering.
                    if (ContactFilter?.ShouldCollide(fixtureA, fixtureB) == false)
                    {
                        Destroy(c);
                        --i;
                        continue;
                    }

                    // Clear the filtering flag.
                    c.Flags &= ~Contact.ContactFlag.FilterFlag;
                }

                var activeA = bodyA.IsAwake && bodyA.BodyType != BodyType.StaticBody;
                var activeB = bodyB.IsAwake && bodyB.BodyType != BodyType.StaticBody;

                // At least one body must be awake and it must be dynamic or kinematic.
                if (activeA == false && activeB == false)
                {
                    continue;
                }

                var proxyIdA = fixtureA.Proxies[indexA].ProxyId;
                var proxyIdB = fixtureB.Proxies[indexB].ProxyId;
                var overlap = BroadPhase.TestOverlap(proxyIdA, proxyIdB);

                // Here we destroy contacts that cease to overlap in the broad-phase.
                if (overlap == false)
                {
                    Destroy(c);
                    --i;
                    continue;
                }

                // The contact persists.
                c.Update(ContactListener);
            }
        }
    }
}
