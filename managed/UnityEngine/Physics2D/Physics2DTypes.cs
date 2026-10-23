using System;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>Friction and bounciness of 2D colliders (Box2D fixture properties).</summary>
    [ClassId(62)]
    public sealed partial class PhysicsMaterial2D : Object, ISerializedAsset, Instantiation.ICloneable
    {
        float m_Friction = 0.4f;
        float m_Bounciness;
        PhysicsMaterialCombine2D m_FrictionCombine = PhysicsMaterialCombine2D.Mean;
        PhysicsMaterialCombine2D m_BounceCombine = PhysicsMaterialCombine2D.Maximum;
        /// <summary>Bumped on every change, so colliders using the material update their shapes.</summary>
        internal int m_ChangeVersion;

        public PhysicsMaterial2D() { }

        public PhysicsMaterial2D(string name)
        {
            m_Name = name ?? "";
        }

        public float bounciness
        {
            get => m_Bounciness;
            set { m_Bounciness = value; m_ChangeVersion++; }
        }

        public float friction
        {
            get => m_Friction;
            set { m_Friction = value; m_ChangeVersion++; }
        }

        public PhysicsMaterialCombine2D frictionCombine
        {
            get => m_FrictionCombine;
            set { m_FrictionCombine = value; m_ChangeVersion++; }
        }

        public PhysicsMaterialCombine2D bounceCombine
        {
            get => m_BounceCombine;
            set { m_BounceCombine = value; m_ChangeVersion++; }
        }

        /// <summary>Combines two values as two materials ask: the mode with the higher value wins
        /// (Average &lt; Mean &lt; Multiply &lt; Minimum &lt; Maximum).</summary>
        public static float GetCombinedValues(float valueA, float valueB, PhysicsMaterialCombine2D materialCombineA, PhysicsMaterialCombine2D materialCombineB)
        {
            var mode = (PhysicsMaterialCombine2D)Math.Max((int)materialCombineA, (int)materialCombineB);
            switch (mode)
            {
                case PhysicsMaterialCombine2D.Average: return (valueA + valueB) * 0.5f;
                case PhysicsMaterialCombine2D.Mean: return Mathf.Sqrt(valueA * valueB);
                case PhysicsMaterialCombine2D.Multiply: return valueA * valueB;
                case PhysicsMaterialCombine2D.Minimum: return Mathf.Min(valueA, valueB);
                default: return Mathf.Max(valueA, valueB);
            }
        }

        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_Friction = body.Float("friction", body.Float("m_Friction", 0.4f));
            m_Bounciness = body.Float("bounciness", body.Float("m_Bounciness", 0f));
            m_FrictionCombine = (PhysicsMaterialCombine2D)body.Int("frictionCombine", body.Int("m_FrictionCombine", (int)PhysicsMaterialCombine2D.Mean));
            m_BounceCombine = (PhysicsMaterialCombine2D)body.Int("bounceCombine", body.Int("m_BounceCombine", (int)PhysicsMaterialCombine2D.Maximum));
            m_ChangeVersion++;
        }

        Object Instantiation.ICloneable.CloneObject() => new PhysicsMaterial2D
        {
            m_Friction = m_Friction,
            m_Bounciness = m_Bounciness,
            m_FrictionCombine = m_FrictionCombine,
            m_BounceCombine = m_BounceCombine,
        };
    }

    /// <summary>The result of a 2D ray or shape cast.</summary>
    public partial struct RaycastHit2D
    {
        Vector2 m_HitCentroid;
        Vector2 m_HitPoint;
        Vector2 m_HitNormal;
        float m_HitDistance;
        float m_HitFraction;
        Collider2D m_HitCollider;

        internal RaycastHit2D(Collider2D collider, Vector2 point, Vector2 normal, float distance, float fraction, Vector2 centroid)
        {
            m_HitCollider = collider;
            m_HitPoint = point;
            m_HitNormal = normal;
            m_HitDistance = distance;
            m_HitFraction = fraction;
            m_HitCentroid = centroid;
        }

        public Vector2 centroid { get => m_HitCentroid; set => m_HitCentroid = value; }
        public Vector2 point { get => m_HitPoint; set => m_HitPoint = value; }
        public Vector2 normal { get => m_HitNormal; set => m_HitNormal = value; }
        public float distance { get => m_HitDistance; set => m_HitDistance = value; }
        public float fraction { get => m_HitFraction; set => m_HitFraction = value; }
        public Collider2D collider => m_HitCollider;
        public Rigidbody2D rigidbody => collider != null ? collider.attachedRigidbody : null;

        public Transform transform
        {
            get
            {
                var body = rigidbody;
                if (body != null) return body.transform;
                return collider != null ? collider.transform : null;
            }
        }

        public static implicit operator bool(RaycastHit2D hit) => hit.collider != null;

        public int CompareTo(RaycastHit2D other)
        {
            if (collider == null) return 1;
            if (other.collider == null) return -1;
            return fraction.CompareTo(other.fraction);
        }
    }

    /// <summary>A contact point between two 2D colliders, seen from <see cref="otherCollider"/>.</summary>
    public partial struct ContactPoint2D
    {
        Vector2 m_ContactPoint;
        Vector2 m_ContactNormal;
        Vector2 m_ContactRelativeVelocity;
        float m_ContactFriction;
        float m_ContactBounciness;
        float m_ContactSeparation;
        float m_ContactNormalImpulse;
        float m_ContactTangentImpulse;
        Collider2D m_ContactCollider;
        Collider2D m_ContactOtherCollider;
        Rigidbody2D m_ContactRigidbody;
        Rigidbody2D m_ContactOtherRigidbody;
        bool m_ContactEnabled;

        internal ContactPoint2D(Vector2 point, Vector2 normal, Vector2 relativeVelocity, float friction, float bounciness, float separation,
            float normalImpulse, float tangentImpulse, Collider2D collider, Collider2D otherCollider, bool enabled)
        {
            m_ContactPoint = point;
            m_ContactNormal = normal;
            m_ContactRelativeVelocity = relativeVelocity;
            m_ContactFriction = friction;
            m_ContactBounciness = bounciness;
            m_ContactSeparation = separation;
            m_ContactNormalImpulse = normalImpulse;
            m_ContactTangentImpulse = tangentImpulse;
            m_ContactCollider = collider;
            m_ContactOtherCollider = otherCollider;
            m_ContactRigidbody = collider != null ? collider.m_AttachedBody : null;
            m_ContactOtherRigidbody = otherCollider != null ? otherCollider.m_AttachedBody : null;
            m_ContactEnabled = enabled;
        }

        public Vector2 point => m_ContactPoint;
        public Vector2 normal => m_ContactNormal;
        public float separation => m_ContactSeparation;
        public float normalImpulse => m_ContactNormalImpulse;
        public float tangentImpulse => m_ContactTangentImpulse;
        public Vector2 relativeVelocity => m_ContactRelativeVelocity;
        public float friction => m_ContactFriction;
        public float bounciness => m_ContactBounciness;
        public Collider2D collider => m_ContactCollider;
        public Collider2D otherCollider => m_ContactOtherCollider;
        public Rigidbody2D rigidbody => m_ContactRigidbody;
        public Rigidbody2D otherRigidbody => m_ContactOtherRigidbody;
        public bool enabled => m_ContactEnabled;
    }

    /// <summary>The details a collision message passes: <see cref="collider"/> is the other object's
    /// collider, <see cref="otherCollider"/> the receiving object's.</summary>
    public partial class Collision2D
    {
        internal Collider2D m_CollisionCollider;
        internal Collider2D m_CollisionOtherCollider;
        internal Rigidbody2D m_CollisionRigidbody;
        internal Rigidbody2D m_CollisionOtherRigidbody;
        internal Vector2 m_CollisionRelativeVelocity;
        internal bool m_CollisionEnabled = true;
        internal ContactPoint2D[] m_CollisionContacts = Array.Empty<ContactPoint2D>();
        internal int m_CollisionContactCount;

        public Collision2D() { }

        public Collider2D collider => m_CollisionCollider;
        public Collider2D otherCollider => m_CollisionOtherCollider;
        public Rigidbody2D rigidbody => m_CollisionRigidbody;
        public Rigidbody2D otherRigidbody => m_CollisionOtherRigidbody;
        public Transform transform => rigidbody != null ? rigidbody.transform : collider.transform;
        public GameObject gameObject => rigidbody != null ? rigidbody.gameObject : collider.gameObject;
        public Vector2 relativeVelocity => m_CollisionRelativeVelocity;
        public bool enabled => m_CollisionEnabled;
        public int contactCount => m_CollisionContactCount;

        public ContactPoint2D[] contacts
        {
            get
            {
                var copy = new ContactPoint2D[m_CollisionContactCount];
                Array.Copy(m_CollisionContacts, copy, m_CollisionContactCount);
                return copy;
            }
        }

        public ContactPoint2D GetContact(int index)
        {
            if (index < 0 || index >= m_CollisionContactCount)
                throw new ArgumentOutOfRangeException($"Cannot get contact at index {index}. There are {m_CollisionContactCount} contact(s).");
            return m_CollisionContacts[index];
        }

        public int GetContacts(ContactPoint2D[] contacts)
        {
            if (contacts == null) throw new NullReferenceException("Cannot get contacts as the provided array is NULL.");
            int count = Math.Min(m_CollisionContactCount, contacts.Length);
            Array.Copy(m_CollisionContacts, contacts, count);
            return count;
        }

        public int GetContacts(List<ContactPoint2D> contacts)
        {
            if (contacts == null) throw new NullReferenceException("Cannot get contacts as the provided list is NULL.");
            contacts.Clear();
            for (int i = 0; i < m_CollisionContactCount; i++) contacts.Add(m_CollisionContacts[i]);
            return m_CollisionContactCount;
        }
    }

    /// <summary>The minimum separation (negative: penetration) of two colliders.</summary>
    public partial struct ColliderDistance2D
    {
        Vector2 m_DistancePointA;
        Vector2 m_DistancePointB;
        Vector2 m_DistanceNormal;
        float m_DistanceValue;
        bool m_DistanceIsValid;

        internal ColliderDistance2D(Vector2 pointA, Vector2 pointB, Vector2 normal, float distance)
        {
            m_DistancePointA = pointA;
            m_DistancePointB = pointB;
            m_DistanceNormal = normal;
            m_DistanceValue = distance;
            m_DistanceIsValid = true;
        }

        public Vector2 pointA { get => m_DistancePointA; set => m_DistancePointA = value; }
        public Vector2 pointB { get => m_DistancePointB; set => m_DistancePointB = value; }
        /// <summary>Points from pointB to pointA.</summary>
        public Vector2 normal => m_DistanceNormal;
        public float distance { get => m_DistanceValue; set => m_DistanceValue = value; }
        public bool isOverlapped => m_DistanceValue < 0f;
        public bool isValid { get => m_DistanceIsValid; set => m_DistanceIsValid = value; }
    }

    /// <summary>Which contacts and query results to keep.</summary>
    [Serializable]
    public partial struct ContactFilter2D
    {
        public bool useTriggers;
        public bool useLayerMask;
        public bool useDepth;
        public bool useOutsideDepth;
        public bool useNormalAngle;
        public bool useOutsideNormalAngle;
        public LayerMask layerMask;
        public float minDepth;
        public float maxDepth;
        public float minNormalAngle;
        public float maxNormalAngle;

        public const float NormalAngleUpperLimit = 359.9999f;

        public bool isFiltering => !useTriggers || useLayerMask || useDepth || useNormalAngle;

        public ContactFilter2D NoFilter()
        {
            useTriggers = true;
            useLayerMask = false;
            layerMask = -1;
            useDepth = false;
            useOutsideDepth = false;
            minDepth = float.NegativeInfinity;
            maxDepth = float.PositiveInfinity;
            useNormalAngle = false;
            useOutsideNormalAngle = false;
            minNormalAngle = 0f;
            maxNormalAngle = NormalAngleUpperLimit;
            return this;
        }

        public void ClearLayerMask() => useLayerMask = false;

        public void SetLayerMask(LayerMask layerMask)
        {
            this.layerMask = layerMask;
            useLayerMask = true;
        }

        public void ClearDepth() => useDepth = false;

        public void SetDepth(float minDepth, float maxDepth)
        {
            this.minDepth = float.IsNaN(minDepth) ? float.NegativeInfinity : minDepth;
            this.maxDepth = float.IsNaN(maxDepth) ? float.PositiveInfinity : maxDepth;
            useDepth = true;
            if (this.minDepth > this.maxDepth) (this.minDepth, this.maxDepth) = (this.maxDepth, this.minDepth);
        }

        public void ClearNormalAngle() => useNormalAngle = false;

        public void SetNormalAngle(float minNormalAngle, float maxNormalAngle)
        {
            this.minNormalAngle = float.IsNaN(minNormalAngle) ? 0f : Mathf.Clamp(minNormalAngle, -NormalAngleUpperLimit, NormalAngleUpperLimit);
            this.maxNormalAngle = float.IsNaN(maxNormalAngle) ? NormalAngleUpperLimit : Mathf.Clamp(maxNormalAngle, -NormalAngleUpperLimit, NormalAngleUpperLimit);
            useNormalAngle = true;
            if (this.minNormalAngle > this.maxNormalAngle) (this.minNormalAngle, this.maxNormalAngle) = (this.maxNormalAngle, this.minNormalAngle);
        }

        public bool IsFilteringTrigger(Collider2D collider) => !useTriggers && collider != null && collider.isTrigger;

        public bool IsFilteringLayerMask(GameObject obj) => useLayerMask && obj != null && (layerMask.value & (1 << obj.layer)) == 0;

        public bool IsFilteringDepth(GameObject obj)
        {
            if (!useDepth || obj == null) return false;
            float min = minDepth, max = maxDepth;
            if (min > max) (min, max) = (max, min);
            float z = obj.transform.position.z;
            bool outside = z < min || z > max;
            return useOutsideDepth ? !outside : outside;
        }

        public bool IsFilteringNormalAngle(Vector2 normal) => IsFilteringNormalAngle(Mathf.Atan2(normal.y, normal.x) * Mathf.Rad2Deg);

        public bool IsFilteringNormalAngle(float angle)
        {
            if (!useNormalAngle) return false;
            float min = Mathf.Clamp(minNormalAngle, -NormalAngleUpperLimit, NormalAngleUpperLimit);
            float max = Mathf.Clamp(maxNormalAngle, -NormalAngleUpperLimit, NormalAngleUpperLimit);
            if (min > max) (min, max) = (max, min);
            // The angle, wrapped into [min, min + 360).
            float a = angle - min;
            a -= Mathf.Floor(a / 360f) * 360f;
            bool inside = a <= max - min;
            return useOutsideNormalAngle ? inside : !inside;
        }

        /// <summary>The filter of the legacy query overloads (layer mask and depth range).</summary>
        internal static ContactFilter2D CreateLegacyFilter(int layerMask, float minDepth, float maxDepth)
        {
            var filter = new ContactFilter2D().NoFilter();
            filter.useTriggers = Physics2D.queriesHitTriggers;
            filter.SetLayerMask(layerMask);
            if (!(float.IsNegativeInfinity(minDepth) && float.IsPositiveInfinity(maxDepth)))
                filter.SetDepth(minDepth, maxDepth);
            return filter;
        }

        /// <summary>True when a collider (and its hit normal) passes the filter.</summary>
        internal bool Accepts(Collider2D collider)
        {
            if (IsFilteringTrigger(collider)) return false;
            var go = collider.m_GameObject;
            return !IsFilteringLayerMask(go) && !IsFilteringDepth(go);
        }
    }
}
