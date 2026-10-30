using System;
using System.Collections;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>Friction and bounciness of 3D colliders.</summary>
    [ClassId(134)]
    public partial class PhysicsMaterial : Object, ISerializedAsset, Instantiation.ICloneable
    {
        float m_DynamicFriction = 0.6f;
        float m_StaticFriction = 0.6f;
        float m_Bounciness;
        PhysicsMaterialCombine m_FrictionCombine = PhysicsMaterialCombine.Average;
        PhysicsMaterialCombine m_BounceCombine = PhysicsMaterialCombine.Average;
        /// <summary>Bumped on every change, so colliders using the material update their shapes.</summary>
        internal int m_ChangeVersion;
        /// <summary>A collider's own copy (Collider.material).</summary>
        internal bool m_IsInstance;

        public PhysicsMaterial() { }

        public PhysicsMaterial(string name)
        {
            m_Name = name ?? "";
        }

        public float bounciness
        {
            get => m_Bounciness;
            set { m_Bounciness = value; m_ChangeVersion++; }
        }

        public float dynamicFriction
        {
            get => m_DynamicFriction;
            set { m_DynamicFriction = value; m_ChangeVersion++; }
        }

        public float staticFriction
        {
            get => m_StaticFriction;
            set { m_StaticFriction = value; m_ChangeVersion++; }
        }

        public PhysicsMaterialCombine frictionCombine
        {
            get => m_FrictionCombine;
            set { m_FrictionCombine = value; m_ChangeVersion++; }
        }

        public PhysicsMaterialCombine bounceCombine
        {
            get => m_BounceCombine;
            set { m_BounceCombine = value; m_ChangeVersion++; }
        }

        internal void CopyValues(PhysicsMaterial s)
        {
            m_DynamicFriction = s.m_DynamicFriction;
            m_StaticFriction = s.m_StaticFriction;
            m_Bounciness = s.m_Bounciness;
            m_FrictionCombine = s.m_FrictionCombine;
            m_BounceCombine = s.m_BounceCombine;
            m_ChangeVersion++;
        }

        void ISerializedAsset.ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_DynamicFriction = body.Float("dynamicFriction", body.Float("m_DynamicFriction", 0.6f));
            m_StaticFriction = body.Float("staticFriction", body.Float("m_StaticFriction", 0.6f));
            m_Bounciness = body.Float("bounciness", body.Float("m_Bounciness", 0f));
            m_FrictionCombine = (PhysicsMaterialCombine)body.Int("frictionCombine", body.Int("m_FrictionCombine", 0));
            m_BounceCombine = (PhysicsMaterialCombine)body.Int("bounceCombine", body.Int("m_BounceCombine", 0));
            m_ChangeVersion++;
        }

        Object Instantiation.ICloneable.CloneObject()
        {
            var copy = new PhysicsMaterial();
            copy.CopyValues(this);
            return copy;
        }
    }

    /// <summary>The result of a 3D ray or shape cast.</summary>
    public partial struct RaycastHit
    {
        internal Vector3 m_Point;
        internal Vector3 m_Normal;
        internal uint m_FaceID;
        internal float m_Distance;
        internal Vector2 m_UV;
        internal int m_Collider;
        internal Collider m_ColliderRef;

        public Collider collider => m_ColliderRef;

        public int colliderInstanceID => m_ColliderRef != null ? m_ColliderRef.GetInstanceID() : m_Collider;

        public Vector3 point
        {
            get => m_Point;
            set => m_Point = value;
        }

        public Vector3 normal
        {
            get => m_Normal;
            set => m_Normal = value;
        }

        public Vector3 barycentricCoordinate
        {
            get => new Vector3(1f - (m_UV.y + m_UV.x), m_UV.x, m_UV.y);
            // Unity stores the setter's first two components; the getter reconstructs the first weight.
            set => m_UV = new Vector2(value.x, value.y);
        }

        public float distance
        {
            get => m_Distance;
            set => m_Distance = value;
        }

        public int triangleIndex => unchecked((int)m_FaceID);

        public Vector2 textureCoord => TextureCoordinate(0);
        public Vector2 textureCoord2 => TextureCoordinate(1);

        [Obsolete("Use textureCoord2 instead. (UnityUpgradable) -> textureCoord2")]
        public Vector2 textureCoord1 => textureCoord2;

        Vector2 TextureCoordinate(int channel)
        {
            if (!(m_ColliderRef is MeshCollider meshCollider) || meshCollider == null || meshCollider.convex) return Vector2.zero;
            var mesh = meshCollider.sharedMesh;
            if (mesh == null || m_FaceID == uint.MaxValue) return Vector2.zero;
            var uv = mesh.m_UVs[channel];
            if (channel == 1 && (uv == null || uv.Length == 0) && mesh.m_UVs[0] != null && mesh.m_UVs[0].Length > 0)
            {
                Debug.LogWarning($"Mesh \"{mesh.name}\" does not have a 2nd Texture coordinate channel. Defaulting to channel 1");
                uv = mesh.m_UVs[0];
            }
            if (uv == null || uv.Length == 0)
            {
                Debug.LogError($"Reading UV coordinates of mesh \"{mesh.name}\" failed. Texture coordinate channel \"{channel}\" not found.");
                return Vector2.zero;
            }
            uint triangle = m_FaceID;
            foreach (var sub in mesh.m_SubMeshes)
            {
                if (sub.Topology != MeshTopology.Triangles) continue;
                uint count = (uint)(sub.Indices.Length / 3);
                if (triangle >= count) { triangle -= count; continue; }
                int index = (int)triangle * 3;
                int a = sub.Indices[index] + sub.BaseVertex, b = sub.Indices[index + 1] + sub.BaseVertex, c = sub.Indices[index + 2] + sub.BaseVertex;
                if ((uint)a >= (uint)uv.Length || (uint)b >= (uint)uv.Length || (uint)c >= (uint)uv.Length) return Vector2.zero;
                var value = uv[a] * (1f - m_UV.x - m_UV.y) + uv[b] * m_UV.x + uv[c] * m_UV.y;
                return new Vector2(value.x, value.y);
            }
            return Vector2.zero;
        }

        public Vector2 lightmapCoord
        {
            get
            {
                var uv = TextureCoordinate(1);
                // The Editor applies the renderer's transform even with no baked lightmap index.
                // Its default hit throws when it attempts the collider's component lookup.
                if (m_ColliderRef == null) throw new NullReferenceException();
                var renderer = m_ColliderRef.GetComponent<Renderer>();
                if (renderer == null) return uv;
                var st = renderer.lightmapScaleOffset;
                return new Vector2(uv.x * st.x + st.z, uv.y * st.y + st.w);
            }
        }

        public Transform transform
        {
            get
            {
                var c = m_ColliderRef;
                if (c == null) return null;
                var rb = c.attachedRigidbody;
                return rb != null ? rb.transform : c.transform;
            }
        }

        public Rigidbody rigidbody => m_ColliderRef != null ? m_ColliderRef.attachedRigidbody : null;

    }

    /// <summary>A contact point of a collision, seen from one of the colliders: the normal points
    /// from the other collider towards it.</summary>
    public partial struct ContactPoint
    {
        internal Vector3 m_Point;
        internal Vector3 m_Normal;
        internal Vector3 m_Impulse;
        internal int m_ThisColliderInstanceID;
        internal int m_OtherColliderInstanceID;
        internal float m_Separation;

        internal ContactPoint(Vector3 point, Vector3 normal, Vector3 impulse, float separation, int thisInstanceID, int otherInstenceID)
        {
            m_Point = point;
            m_Normal = normal;
            m_Impulse = impulse;
            m_Separation = separation;
            m_ThisColliderInstanceID = thisInstanceID;
            m_OtherColliderInstanceID = otherInstenceID;
        }

        public Vector3 point => m_Point;

        public Vector3 normal => m_Normal;

        public Vector3 impulse => m_Impulse;

        public Collider thisCollider => ObjectRegistry.FindById(m_ThisColliderInstanceID) as Collider;

        public Collider otherCollider => ObjectRegistry.FindById(m_OtherColliderInstanceID) as Collider;

        public float separation => m_Separation;
    }

    /// <summary>What OnCollisionEnter/Stay/Exit get: the other collider and the contact points.</summary>
    public partial class Collision
    {
        internal Collider m_Collider;
        internal Rigidbody m_Rigidbody;
        internal Collider m_Self;
        internal Vector3 m_RelativeVelocity;
        internal Vector3 m_Impulse;
        internal ContactPoint[] m_Contacts = Array.Empty<ContactPoint>();

        public Collision() { }

        public Vector3 impulse => m_Impulse;

        public Vector3 relativeVelocity => m_RelativeVelocity;

        public Rigidbody rigidbody => m_Rigidbody;


        public Component body => m_Rigidbody;

        public Collider collider => m_Collider;

        public Transform transform => m_Rigidbody != null ? m_Rigidbody.transform : m_Collider != null ? m_Collider.transform : null;

        public GameObject gameObject => m_Rigidbody != null ? m_Rigidbody.gameObject : m_Collider != null ? m_Collider.gameObject : null;

        public int contactCount => m_Contacts.Length;

        /// <summary>A new array on every call, as in Unity.</summary>
        public ContactPoint[] contacts => (ContactPoint[])m_Contacts.Clone();

        public ContactPoint GetContact(int index)
        {
            if (index < 0 || index >= m_Contacts.Length)
                throw new ArgumentOutOfRangeException(nameof(index), $"Cannot get contact at index {index}. There are {m_Contacts.Length} contact(s).");
            return m_Contacts[index];
        }

        public int GetContacts(ContactPoint[] contacts)
        {
            if (contacts == null) throw new NullReferenceException("Cannot get contacts as the provided array is NULL.");
            int n = Math.Min(contacts.Length, m_Contacts.Length);
            Array.Copy(m_Contacts, contacts, n);
            return n;
        }

        public int GetContacts(List<ContactPoint> contacts)
        {
            if (contacts == null) throw new NullReferenceException("Cannot get contacts as the provided list is NULL.");
            contacts.Clear();
            contacts.AddRange(m_Contacts);
            return m_Contacts.Length;
        }

        [Obsolete("Do not use Collision.GetEnumerator(), enumerate using non-allocating array returned by Collision.GetContacts() or enumerate using Collision.GetContact(index) instead.", false)]
        public virtual IEnumerator GetEnumerator() => m_Contacts.GetEnumerator();

        [Obsolete("Use Collision.relativeVelocity instead. (UnityUpgradable) -> relativeVelocity", false)]
        public Vector3 impactForceSum => m_RelativeVelocity;

        [Obsolete("Will always return zero.", false)]
        [Unbound.EditorFaithful("obsolete: Unity always returns zero")]
        public Vector3 frictionForceSum => Vector3.zero;

        [Obsolete("Please use Collision.rigidbody, Collision.transform or Collision.collider instead", false)]
        public Component other => m_Rigidbody != null ? m_Rigidbody : m_Collider;
    }
}
