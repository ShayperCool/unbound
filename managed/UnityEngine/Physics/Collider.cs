using System;
using System.Collections.Generic;
using Unbound.Box3D;
using Unbound.Runtime;
using W = Unbound.Runtime.PhysicsWorld;

namespace UnityEngine
{
    /// <summary>
    /// Base of the 3D colliders. While its GameObject is active and it is enabled, a collider adds
    /// one Box3D shape to the body of the Rigidbody it is attached to, or to a static body of its own.
    /// Unlike Behaviours, colliders are Components with their own enabled flag.
    /// </summary>
    [ClassId(56)]
    [RequireComponent(typeof(Transform))]
    public partial class Collider : Component
    {
        // Serialized state.
        internal bool m_Enabled = true;
        internal bool m_IsTrigger;
        internal PhysicsMaterial m_Material;
        internal int m_IncludeLayers;
        internal int m_ExcludeLayers;
        internal int m_LayerOverridePriority;
        internal bool m_ProvidesContacts;
        float m_ContactOffset = -1f;
        bool m_HasModifiableContacts;

        // Runtime state.
        /// <summary>Its GameObject is active in the hierarchy (the engine enabled it).</summary>
        internal bool m_Active;
        internal bool m_Registered;
        internal bool m_ShapeDirty;
        internal Rigidbody m_AttachedBody;
        internal ulong m_StaticBody;
        internal ulong m_Shape;
        internal IntPtr m_MeshData;
        /// <summary>A TerrainCollider's height field (Box3D data its shape uses).</summary>
        internal IntPtr m_HeightFieldData;
        /// <summary>More shapes of the collider on its body (a terrain's tree colliders).</summary>
        internal readonly System.Collections.Generic.List<ulong> m_ExtraShapes = new System.Collections.Generic.List<ulong>();
        internal int m_TransformVersion = -1;
        /// <summary>The pose the collider was added to the physics scene with: its shape is built
        /// there, and only a transform sync moves it to where its transform has gone since (Unity
        /// creates the shape at once; queries see the move once synced).</summary>
        internal bool m_UseAddedPose;
        internal Vector3 m_AddedPosition;
        internal Quaternion m_AddedRotation;
        internal Matrix4x4 m_AddedMatrix;
        internal int m_AddedVersion;
        internal Matrix4x4 m_ShapeMatrix;
        internal int m_Layer;
        internal PhysicsMaterial m_UsedMaterial;
        internal int m_MaterialVersion;

        public Collider() { }

        internal override void OnEngineEnable()
        {
            m_Active = true;
            if (m_Enabled) W.Add(this);
        }

        internal override void OnEngineDisable()
        {
            m_Active = false;
            W.Remove(this);
        }

        internal override void OnEngineDestroy()
        {
            m_Active = false;
            W.Remove(this);
        }

        public bool enabled
        {
            get => m_Enabled;
            set
            {
                if (m_Enabled == value) return;
                m_Enabled = value;
                if (!m_Active) return;
                if (value) W.Add(this);
                else W.Remove(this);
            }
        }

        /// <summary>The collider's shape on a body, from <paramref name="toBody"/> (its transform space
        /// to the body frame); 0 when it has none.</summary>
        internal virtual ulong CreateShape(ulong body, in B3.ShapeDef def, Matrix4x4 toBody, Rigidbody rigidbody) => 0;

        /// <summary>The frame of the static body a collider without a Rigidbody gets (its
        /// transform's position and rotation unless the shape needs another).</summary>
        internal virtual void StaticFrame(ref Vector3 position, ref Quaternion rotation) { }

        /// <summary>The bounds of the collider in its transform's space (before scale), for the
        /// bounds of a collider that is not in the world.</summary>
        internal virtual Bounds LocalBounds() => new Bounds(Vector3.zero, Vector3.zero);

        /// <summary>The shape must be rebuilt (a geometry or contact property changed).</summary>
        internal void ShapeChanged()
        {
            if (m_Registered) W.MarkShapeDirty(this);
        }

        /// <summary>The scales a body-frame matrix applies along the collider's own axes.</summary>
        internal static Vector3 AxisScales(Matrix4x4 m) => new Vector3(
            new Vector3(m.m00, m.m10, m.m20).magnitude,
            new Vector3(m.m01, m.m11, m.m21).magnitude,
            new Vector3(m.m02, m.m12, m.m22).magnitude);

        /// <summary>The rotation of a body-frame matrix (its axes without scale).</summary>
        internal static Quaternion AxisRotation(Matrix4x4 m)
        {
            var forward = new Vector3(m.m02, m.m12, m.m22);
            var up = new Vector3(m.m01, m.m11, m.m21);
            if (forward.sqrMagnitude < 1e-20f || up.sqrMagnitude < 1e-20f) return Quaternion.identity;
            return Quaternion.LookRotation(forward, up);
        }

        internal static int ReadMask(YNode body, string key, int fallback)
        {
            var node = body.Get(key);
            if (node == null) return fallback;
            var bits = node.Get("m_Bits");
            return bits != null ? unchecked((int)(uint)bits.AsLong(fallback)) : unchecked((int)(uint)node.AsLong(fallback));
        }

        // ------------------------------------------------------------------ properties

        public bool isTrigger
        {
            get => m_IsTrigger;
            set
            {
                if (m_IsTrigger == value) return;
                m_IsTrigger = value;
                ShapeChanged();
            }
        }

        public Rigidbody attachedRigidbody
        {
            get
            {
                if (m_Registered)
                {
                    W.Flush();
                    return m_AttachedBody;
                }
                // Not simulated: the rigidbody it would attach to.
                for (var t = transform; t != null; t = t.parent)
                {
                    var rb = t.GetComponent<Rigidbody>();
                    if (rb != null) return rb;
                }
                return null;
            }
        }


        public PhysicsMaterial sharedMaterial
        {
            get => m_Material;
            set
            {
                m_Material = value;
                ShapeChanged();
            }
        }

        /// <summary>The collider's own copy of its material, made on first use as in Unity.</summary>
        public PhysicsMaterial material
        {
            get
            {
                if (m_Material == null || !m_Material.m_IsInstance)
                {
                    var source = m_Material;
                    var copy = new PhysicsMaterial((source != null ? source.name : "Default") + " (Instance)");
                    if (source != null) copy.CopyValues(source);
                    copy.m_IsInstance = true;
                    m_Material = copy;
                    ShapeChanged();
                }
                return m_Material;
            }
            set => sharedMaterial = value;
        }

        public float contactOffset
        {
            get => m_ContactOffset >= 0f ? m_ContactOffset : Physics.s_DefaultContactOffset;
            set => m_ContactOffset = Mathf.Max(0f, value);
        }

        public bool hasModifiableContacts
        {
            get => m_HasModifiableContacts;
            set => m_HasModifiableContacts = value;
        }

        public bool providesContacts
        {
            get => m_ProvidesContacts;
            set => m_ProvidesContacts = value;
        }

        public int layerOverridePriority
        {
            get => m_LayerOverridePriority;
            set => m_LayerOverridePriority = value;
        }

        public LayerMask includeLayers
        {
            get => m_IncludeLayers;
            set
            {
                m_IncludeLayers = value;
                W.Refilter(this);
            }
        }

        public LayerMask excludeLayers
        {
            get => m_ExcludeLayers;
            set
            {
                m_ExcludeLayers = value;
                W.Refilter(this);
            }
        }

        /// <summary>The world-space bounds of the collider's shape.</summary>
        public Bounds bounds
        {
            get
            {
                if (this is CharacterController controller)
                {
                    if (m_Registered) W.PrepareQuery();
                    return controller.ControllerBounds();
                }
                if (m_Registered)
                {
                    W.PrepareQuery();
                    if (m_Shape != 0)
                    {
                        B3.ua3_shape_aabb(m_Shape, out var box);
                        var b = new Bounds();
                        b.SetMinMax(box.Lower, box.Upper);
                        return b;
                    }
                }
                var local = LocalBounds();
                var m = transform.localToWorldMatrix;
                var center = m.MultiplyPoint3x4(local.center);
                var e = local.extents;
                var extents = new Vector3(
                    Mathf.Abs(m.m00) * e.x + Mathf.Abs(m.m01) * e.y + Mathf.Abs(m.m02) * e.z,
                    Mathf.Abs(m.m10) * e.x + Mathf.Abs(m.m11) * e.y + Mathf.Abs(m.m12) * e.z,
                    Mathf.Abs(m.m20) * e.x + Mathf.Abs(m.m21) * e.y + Mathf.Abs(m.m22) * e.z);
                return new Bounds(center, extents * 2f);
            }
        }

        /// <summary>The closest point of the collider to a position: the position itself when it is inside.</summary>
        public Vector3 ClosestPoint(Vector3 position)
        {
            W.PrepareQuery();
            if (m_Shape == 0) return position;
            return B3.ua3_shape_closest_point(m_Shape, position);
        }

        public Vector3 ClosestPointOnBounds(Vector3 position) => bounds.ClosestPoint(position);

        public bool Raycast(Ray ray, out RaycastHit hitInfo, float maxDistance)
        {
            hitInfo = default;
            W.PrepareQuery();
            if (m_Shape == 0) return false;
            var direction = ray.direction;
            if (direction.sqrMagnitude < 1e-12f) return false;
            float length = PhysicsQueries.Length(maxDistance);
            if (B3.ua3_shape_raycast(m_Shape, ray.origin, direction.normalized * length, out var hit) == 0) return false;
            hitInfo = PhysicsQueries.ToHit(hit, this, length);
            return true;
        }

        // ------------------------------------------------------------------ serialization

        internal void ReadColliderData(YNode body, IReferenceResolver resolver)
        {
            var material = body.Ref("m_Material");
            m_Material = material.IsNull ? null : resolver.Resolve(material, typeof(PhysicsMaterial)) as PhysicsMaterial;
            m_IncludeLayers = ReadMask(body, "m_IncludeLayers", 0);
            m_ExcludeLayers = ReadMask(body, "m_ExcludeLayers", 0);
            m_LayerOverridePriority = body.Int("m_LayerOverridePriority");
            m_IsTrigger = body.Bool("m_IsTrigger");
            m_ProvidesContacts = body.Bool("m_ProvidesContacts");
            m_Enabled = body.Bool("m_Enabled", true);
        }

        internal void CopyColliderData(Collider s)
        {
            m_Enabled = s.m_Enabled;
            m_IsTrigger = s.m_IsTrigger;
            m_Material = s.m_Material;
            m_IncludeLayers = s.m_IncludeLayers;
            m_ExcludeLayers = s.m_ExcludeLayers;
            m_LayerOverridePriority = s.m_LayerOverridePriority;
            m_ProvidesContacts = s.m_ProvidesContacts;
            m_ContactOffset = s.m_ContactOffset;
            m_HasModifiableContacts = s.m_HasModifiableContacts;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver) => ReadColliderData(body, resolver);

        internal override void CopyFrom(Component source, Func<Object, Object> remap) => CopyColliderData((Collider)source);
    }

    [ClassId(65)]
    public partial class BoxCollider : Collider
    {
        internal Vector3 m_Center;
        internal Vector3 m_Size = Vector3.one;

        public BoxCollider() { }

        public Vector3 center
        {
            get => m_Center;
            set { m_Center = value; ShapeChanged(); }
        }

        public Vector3 size
        {
            get => m_Size;
            set { m_Size = value; ShapeChanged(); }
        }

        [Obsolete("Use BoxCollider.size instead. (UnityUpgradable) -> size")]
        public Vector3 extents
        {
            get => m_Size * 0.5f;
            set => size = value * 2f;
        }

        internal override ulong CreateShape(ulong body, in B3.ShapeDef def, Matrix4x4 toBody, Rigidbody rigidbody)
        {
            var scales = AxisScales(toBody);
            var half = Vector3.Scale(new Vector3(Mathf.Abs(m_Size.x), Mathf.Abs(m_Size.y), Mathf.Abs(m_Size.z)) * 0.5f, scales);
            half = Vector3.Max(half, new Vector3(1e-4f, 1e-4f, 1e-4f));
            return B3.ua3_shape_box(body, def, toBody.MultiplyPoint3x4(m_Center), AxisRotation(toBody), half);
        }

        internal override Bounds LocalBounds() => new Bounds(m_Center, m_Size);

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            ReadColliderData(body, resolver);
            m_Size = body.Vector3("m_Size", Vector3.one);
            m_Center = body.Vector3("m_Center");
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (BoxCollider)source;
            CopyColliderData(s);
            m_Center = s.m_Center;
            m_Size = s.m_Size;
        }
    }

    [ClassId(135)]
    public partial class SphereCollider : Collider
    {
        internal Vector3 m_Center;
        internal float m_Radius = 0.5f;

        public SphereCollider() { }

        public Vector3 center
        {
            get => m_Center;
            set { m_Center = value; ShapeChanged(); }
        }

        public float radius
        {
            get => m_Radius;
            set { m_Radius = value; ShapeChanged(); }
        }

        internal override ulong CreateShape(ulong body, in B3.ShapeDef def, Matrix4x4 toBody, Rigidbody rigidbody)
        {
            var s = AxisScales(toBody);
            float r = Mathf.Max(1e-4f, Mathf.Abs(m_Radius) * Mathf.Max(s.x, Mathf.Max(s.y, s.z)));
            return B3.ua3_shape_sphere(body, def, toBody.MultiplyPoint3x4(m_Center), r);
        }

        internal override Bounds LocalBounds() => new Bounds(m_Center, Vector3.one * (2f * Mathf.Abs(m_Radius)));

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            ReadColliderData(body, resolver);
            m_Radius = body.Float("m_Radius", 0.5f);
            m_Center = body.Vector3("m_Center");
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (SphereCollider)source;
            CopyColliderData(s);
            m_Center = s.m_Center;
            m_Radius = s.m_Radius;
        }
    }

    [ClassId(136)]
    public partial class CapsuleCollider : Collider
    {
        internal Vector3 m_Center;
        internal float m_Radius = 0.5f;
        internal float m_Height = 2f;
        internal int m_Direction = 1;

        public CapsuleCollider() { }

        public Vector3 center
        {
            get => m_Center;
            set { m_Center = value; ShapeChanged(); }
        }

        public float radius
        {
            get => m_Radius;
            set { m_Radius = value; ShapeChanged(); }
        }

        public float height
        {
            get => m_Height;
            set { m_Height = value; ShapeChanged(); }
        }

        /// <summary>The axis of the capsule: 0 X, 1 Y, 2 Z.</summary>
        public int direction
        {
            get => m_Direction;
            set { m_Direction = value; ShapeChanged(); }
        }

        /// <summary>The capsule's end centers and radius in a frame (see <see cref="Collider.CreateShape"/>).</summary>
        internal void Segment(Matrix4x4 toFrame, out Vector3 a, out Vector3 b, out float radius)
        {
            int axis = Mathf.Clamp(m_Direction, 0, 2);
            var s = AxisScales(toFrame);
            float axisScale = s[axis];
            float radial = axis == 0 ? Mathf.Max(s.y, s.z) : axis == 1 ? Mathf.Max(s.x, s.z) : Mathf.Max(s.x, s.y);
            radius = Mathf.Max(1e-4f, Mathf.Abs(m_Radius) * radial);
            float half = Mathf.Max(0f, Mathf.Abs(m_Height) * axisScale * 0.5f - radius);
            var center = toFrame.MultiplyPoint3x4(m_Center);
            var column = new Vector3(toFrame[0, axis], toFrame[1, axis], toFrame[2, axis]);
            var dir = column.sqrMagnitude > 1e-20f ? column.normalized : Vector3.up;
            a = center - dir * half;
            b = center + dir * half;
        }

        internal override ulong CreateShape(ulong body, in B3.ShapeDef def, Matrix4x4 toBody, Rigidbody rigidbody)
        {
            Segment(toBody, out var a, out var b, out float r);
            if ((b - a).sqrMagnitude < 1e-10f) return B3.ua3_shape_sphere(body, def, (a + b) * 0.5f, r);
            return B3.ua3_shape_capsule(body, def, a, b, r);
        }

        internal override Bounds LocalBounds()
        {
            int axis = Mathf.Clamp(m_Direction, 0, 2);
            var size = Vector3.one * (2f * Mathf.Abs(m_Radius));
            size[axis] = Mathf.Max(size[axis], Mathf.Abs(m_Height));
            return new Bounds(m_Center, size);
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            ReadColliderData(body, resolver);
            m_Radius = body.Float("m_Radius", 0.5f);
            m_Height = body.Float("m_Height", 2f);
            m_Direction = body.Int("m_Direction", 1);
            m_Center = body.Vector3("m_Center");
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (CapsuleCollider)source;
            CopyColliderData(s);
            m_Center = s.m_Center;
            m_Radius = s.m_Radius;
            m_Height = s.m_Height;
            m_Direction = s.m_Direction;
        }
    }

    /// <summary>A collider from a mesh: its convex hull (convex), or its triangles, which Box3D (like
    /// Unity since 5) only collides on static and kinematic bodies.</summary>
    [ClassId(64)]
    public partial class MeshCollider : Collider
    {
        internal Mesh m_Mesh;
        internal bool m_Convex;
        internal MeshColliderCookingOptions m_CookingOptions = (MeshColliderCookingOptions)30;
        bool m_WarnedDynamic;

        public MeshCollider() { }

        public Mesh sharedMesh
        {
            get => m_Mesh;
            set { m_Mesh = value; ShapeChanged(); }
        }

        public bool convex
        {
            get => m_Convex;
            set { m_Convex = value; ShapeChanged(); }
        }

        public MeshColliderCookingOptions cookingOptions
        {
            get => m_CookingOptions;
            set => m_CookingOptions = value;
        }

        internal override unsafe ulong CreateShape(ulong body, in B3.ShapeDef def, Matrix4x4 toBody, Rigidbody rigidbody)
        {
            var mesh = m_Mesh;
            if (mesh == null) return 0;
            var source = mesh.m_Vertices;
            if (source == null || source.Length == 0) return 0;
            var vertices = new Vector3[source.Length];
            for (int i = 0; i < source.Length; i++) vertices[i] = toBody.MultiplyPoint3x4(source[i]);
            if (m_Convex)
            {
                fixed (Vector3* points = vertices)
                    return B3.ua3_shape_hull(body, def, points, vertices.Length);
            }
            if (rigidbody != null && !rigidbody.m_IsKinematic)
            {
                if (!m_WarnedDynamic)
                {
                    m_WarnedDynamic = true;
                    Debug.LogError($"Non-convex MeshCollider with non-kinematic Rigidbody is no longer supported since Unity 5. If you want to use a non-convex mesh either make the Rigidbody kinematic or remove the Rigidbody component. Scene hierarchy path \"{gameObject.name}\", Mesh asset path \"\" Mesh name \"{mesh.name}\"", this);
                }
                return 0;
            }
            var indices = new List<int>();
            for (int s = 0; s < mesh.subMeshCount; s++)
            {
                if (mesh.GetTopology(s) != MeshTopology.Triangles) continue;
                indices.AddRange(mesh.GetTriangles(s));
            }
            if (indices.Count < 3) return 0;
            var triangles = indices.ToArray();
            fixed (Vector3* v = vertices)
            fixed (int* t = triangles)
                m_MeshData = B3.ua3_mesh_create(v, vertices.Length, t, triangles.Length / 3);
            if (m_MeshData == IntPtr.Zero) return 0;
            return B3.ua3_shape_mesh(body, def, m_MeshData, Vector3.one);
        }

        internal override Bounds LocalBounds() => m_Mesh != null ? m_Mesh.bounds : new Bounds(Vector3.zero, Vector3.zero);

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            ReadColliderData(body, resolver);
            m_Convex = body.Bool("m_Convex");
            m_CookingOptions = (MeshColliderCookingOptions)body.Int("m_CookingOptions", 30);
            var mesh = body.Ref("m_Mesh");
            m_Mesh = mesh.IsNull ? null : resolver.Resolve(mesh, typeof(Mesh)) as Mesh;
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (MeshCollider)source;
            CopyColliderData(s);
            m_Mesh = s.m_Mesh;
            m_Convex = s.m_Convex;
            m_CookingOptions = s.m_CookingOptions;
        }
    }
}
