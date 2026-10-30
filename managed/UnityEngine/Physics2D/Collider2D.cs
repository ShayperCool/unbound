using System;
using System.Collections.Generic;
using Unbound.Box2D.Collision.Shapes;
using Unbound.Box2D.Dynamics;
using Unbound.Runtime;
using B2Settings = Unbound.Box2D.Common.Settings;
using Shape = Unbound.Box2D.Collision.Shapes.Shape;
using W = Unbound.Runtime.Physics2DWorld;

namespace UnityEngine
{
    /// <summary>
    /// Base of the 2D colliders. While active and enabled, a collider adds Box2D shapes (fixtures) to
    /// the body of the Rigidbody2D it is attached to, or to a static body of its own.
    /// </summary>
    [RequireComponent(typeof(Transform))]
    public partial class Collider2D : Behaviour
    {
        // Serialized state.
        internal float m_Density = 1f;
        internal bool m_IsTrigger;
        internal bool m_UsedByEffector;
        internal CompositeOperation m_CompositeOperation;
        internal int m_CompositeOrder;
        internal Vector2 m_Offset;
        internal PhysicsMaterial2D m_Material;
        internal int m_LayerOverridePriority;
        internal int m_IncludeLayers;
        internal int m_ExcludeLayers;
        internal int m_ForceSendLayers = -1;
        internal int m_ForceReceiveLayers = -1;
        internal int m_ContactCaptureLayers = -1;
        internal int m_CallbackLayers = -1;

        // Runtime state.
        internal bool m_Registered;
        internal bool m_ShapeDirty;
        internal Rigidbody2D m_AttachedBody;
        internal Body m_StaticBody;
        internal readonly List<Fixture> m_Fixtures = new List<Fixture>();
        internal int m_TransformVersion = -1;
        internal Matrix4x4 m_ShapeMatrix;
        internal int m_Layer;
        internal PhysicsMaterial2D m_UsedMaterial;
        internal int m_MaterialVersion;
        internal ColliderErrorState2D m_ErrorState;

        public Collider2D() { }

        internal override void OnEngineEnable()
        {
            W.Add(this);
            CompositeMembershipChanged();
        }

        internal override void OnEngineDisable()
        {
            var composite = Composite();
            W.Remove(this);
            composite?.SourcesChanged();
            if (this is CompositeCollider2D) CompositeMembershipChanged();
        }

        /// <summary>Destroying it removes it too (components without enabled state get no disable).</summary>
        internal override void OnEngineDestroy() => OnEngineDisable();

        /// <summary>A collider joined or left the colliders a composite merges: the composite
        /// builds again, and a composite's sources drop or regain their own shapes.</summary>
        void CompositeMembershipChanged()
        {
            if (this is CompositeCollider2D composite)
            {
                var rb = W.FindRigidbody(this);
                foreach (var c in W.Colliders)
                    if (c != this && c.m_CompositeOperation != CompositeOperation.None && ReferenceEquals(W.FindRigidbody(c), rb))
                        W.MarkShapeDirty(c);
                if (m_Registered) composite.SourcesChanged();
                return;
            }
            Composite()?.SourcesChanged();
        }

        /// <summary>The shapes of the collider, in the frame of its body: <paramref name="toBody"/> maps
        /// the collider's transform space there (offset not applied).</summary>
        internal virtual void BuildShapes(Matrix4x4 toBody, List<Shape> shapes) { }

        /// <summary>The shapes must be rebuilt (a geometry or contact property changed).</summary>
        internal void ShapeChanged()
        {
            if (!m_Registered) return;
            W.MarkShapeDirty(this);
            Composite()?.SourcesChanged();
        }

        internal bool HasComposite() => Composite() != null;

        /// <summary>A setting that shapes the geometry (offset, extrusion) changed.</summary>
        internal virtual void GeometrySettingChanged() { }

        /// <summary>The scales a body-frame matrix applies along the collider's own X and Y axes.</summary>
        internal static Vector2 AxisScales(Matrix4x4 m) =>
            new Vector2(new Vector2(m.m00, m.m10).magnitude, new Vector2(m.m01, m.m11).magnitude);

        internal static System.Numerics.Vector2 ToBody(Matrix4x4 m, Vector2 p)
        {
            var q = m.MultiplyPoint3x4(new Vector3(p.x, p.y, 0f));
            return new System.Numerics.Vector2(q.x, q.y);
        }

        /// <summary>A convex polygon shape from points already in the body frame; null when it is too
        /// small for Box2D.</summary>
        internal static PolygonShape ConvexShape(IList<System.Numerics.Vector2> points)
        {
            if (points.Count < 3 || points.Count > B2Settings.MaxPolygonVertices) return null;
            float area = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                area += a.X * b.Y - a.Y * b.X;
            }
            float slop = B2Settings.LinearSlop;
            if (Math.Abs(area) * 0.5f <= slop * slop) return null;
            var array = new System.Numerics.Vector2[points.Count];
            points.CopyTo(array, 0);
            // Box2D welds points closer than half the linear slop; it needs three left.
            int distinct = 0;
            for (int i = 0; i < array.Length; i++)
            {
                bool unique = true;
                for (int j = 0; j < i; j++)
                    if (System.Numerics.Vector2.DistanceSquared(array[i], array[j]) < 0.25f * slop * slop) { unique = false; break; }
                if (unique) distinct++;
            }
            if (distinct < 3) return null;
            var shape = new PolygonShape();
            shape.Set(array);
            return shape.Count >= 3 ? shape : null;
        }

        internal static int ReadMask(YNode body, string key, int fallback)
        {
            var node = body.Get(key);
            if (node == null) return fallback;
            var bits = node.Get("m_Bits");
            return bits != null ? unchecked((int)(uint)bits.AsLong(fallback)) : unchecked((int)(uint)node.AsLong(fallback));
        }

        // ------------------------------------------------------------------ properties

        public float density
        {
            get => m_Density;
            set
            {
                // Unity takes a density only while the collider is on a body with auto mass;
                // otherwise the value is dropped (measured against Unity 6).
                var body = attachedRigidbody;
                if (body == null || !body.m_UseAutoMass) return;
                m_Density = Mathf.Max(0f, value);
                foreach (var f in m_Fixtures) f.Density = m_Density;
                body.m_MassDirty = true;
                W.MarkBodyDirty(body);
            }
        }

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

        public bool usedByEffector
        {
            get => m_UsedByEffector;
            set { if (m_UsedByEffector == value) return; m_UsedByEffector = value; W.Refilter(this); }
        }

        public CompositeOperation compositeOperation
        {
            get => m_CompositeOperation;
            set
            {
                var before = Composite();
                m_CompositeOperation = value;
                before?.SourcesChanged();
                ShapeChanged();
            }
        }

        public int compositeOrder
        {
            get => m_CompositeOrder;
            set => m_CompositeOrder = value;
        }

        public bool usedByComposite
        {
            get => m_CompositeOperation != CompositeOperation.None;
            set => compositeOperation = value ? CompositeOperation.Merge : CompositeOperation.None;
        }

        public Vector2 offset
        {
            get => m_Offset;
            set
            {
                m_Offset = value;
                GeometrySettingChanged();
                ShapeChanged();
            }
        }

        public Rigidbody2D attachedRigidbody
        {
            get
            {
                if (m_Registered)
                {
                    W.Flush();
                    return m_AttachedBody;
                }
                // Not in the simulation: the one it would attach to.
                for (var t = transform; t != null; t = t.m_Parent)
                {
                    var rb = t.m_GameObject.GetComponent<Rigidbody2D>();
                    if (rb != null) return rb;
                }
                return null;
            }
        }

        public Matrix4x4 localToWorldMatrix
        {
            get
            {
                var rb = attachedRigidbody;
                return rb != null ? rb.localToWorldMatrix : transform.localToWorldMatrix;
            }
        }

        public int shapeCount
        {
            get
            {
                W.Flush();
                return m_Fixtures.Count;
            }
        }

        public Bounds bounds
        {
            get
            {
                W.PrepareQuery();
                if (m_Fixtures.Count == 0) return new Bounds(transform.position, Vector3.zero);
                bool first = true;
                Vector2 min = default, max = default;
                foreach (var f in m_Fixtures)
                {
                    var xf = f.Body.GetTransform();
                    // Unity's bounds are the shapes' own: without the collision skin and edge
                    // radius polygons and edges are padded with.
                    float pad = f.Shape is Unbound.Box2D.Collision.Shapes.CapsuleShape ? 0f
                        : f.Shape is Unbound.Box2D.Collision.Shapes.PolygonShape
                        || f.Shape is Unbound.Box2D.Collision.Shapes.EdgeShape
                        || f.Shape is Unbound.Box2D.Collision.Shapes.ChainShape ? f.Shape.Radius : 0f;
                    for (int i = 0; i < f.Shape.GetChildCount(); i++)
                    {
                        f.Shape.ComputeAABB(out var aabb, xf, i);
                        var lo = W.ToUnity(aabb.LowerBound + new System.Numerics.Vector2(pad));
                        var hi = W.ToUnity(aabb.UpperBound - new System.Numerics.Vector2(pad));
                        min = first ? lo : Vector2.Min(min, lo);
                        max = first ? hi : Vector2.Max(max, hi);
                        first = false;
                    }
                }
                var center = (min + max) * 0.5f;
                return new Bounds(new Vector3(center.x, center.y, transform.position.z), new Vector3(max.x - min.x, max.y - min.y, 0f));
            }
        }

        public ColliderErrorState2D errorState
        {
            get
            {
                W.Flush();
                return m_ErrorState;
            }
        }

        /// <summary>Every collider but a composite can be merged into one (Unity 2023.1 extended
        /// composites to all collider types; Unbound's CompositeCollider2D merges them all).</summary>
        public bool compositeCapable => !(this is CompositeCollider2D);

        public PhysicsMaterial2D sharedMaterial
        {
            get => m_Material;
            set
            {
                m_Material = value;
                ShapeChanged();
            }
        }

        public int layerOverridePriority
        {
            get => m_LayerOverridePriority;
            set
            {
                m_LayerOverridePriority = value;
                RefilterIfRegistered();
            }
        }

        public LayerMask excludeLayers
        {
            get => m_ExcludeLayers;
            set
            {
                m_ExcludeLayers = value;
                RefilterIfRegistered();
            }
        }

        public LayerMask includeLayers
        {
            get => m_IncludeLayers;
            set
            {
                m_IncludeLayers = value;
                RefilterIfRegistered();
            }
        }

        public LayerMask forceSendLayers
        {
            get => m_ForceSendLayers;
            set => m_ForceSendLayers = value;
        }

        public LayerMask forceReceiveLayers
        {
            get => m_ForceReceiveLayers;
            set => m_ForceReceiveLayers = value;
        }

        public LayerMask contactCaptureLayers
        {
            get => m_ContactCaptureLayers;
            set => m_ContactCaptureLayers = value;
        }

        public LayerMask callbackLayers
        {
            get => m_CallbackLayers;
            set => m_CallbackLayers = value;
        }

        void RefilterIfRegistered()
        {
            if (!m_Registered) return;
            W.Flush();
            W.Refilter(this);
        }

        public float friction => Physics2D.MaterialOf(this)?.friction ?? 0.4f;

        public float bounciness => Physics2D.MaterialOf(this)?.bounciness ?? 0f;

        public PhysicsMaterialCombine2D frictionCombine => Physics2D.MaterialOf(this)?.frictionCombine ?? PhysicsMaterialCombine2D.Mean;

        public PhysicsMaterialCombine2D bounceCombine => Physics2D.MaterialOf(this)?.bounceCombine ?? PhysicsMaterialCombine2D.Maximum;

        // ------------------------------------------------------------------ contacts

        public bool IsTouching(Collider2D collider)
        {
            if (collider == null) throw new ArgumentNullException(nameof(collider));
            return Physics2D.IsTouching(this, collider);
        }

        public bool IsTouching(Collider2D collider, ContactFilter2D contactFilter) => Physics2D.IsTouching(this, collider, contactFilter);

        public bool IsTouching(ContactFilter2D contactFilter) => Physics2D.IsTouching(this, contactFilter);

        public bool IsTouchingLayers() => Physics2D.IsTouchingLayers(this, Physics2D.AllLayers);

        public bool IsTouchingLayers(int layerMask) => Physics2D.IsTouchingLayers(this, layerMask);

        public int GetContacts(ContactPoint2D[] contacts) => Physics2D.GetContacts(this, new ContactFilter2D().NoFilter(), contacts);

        public int GetContacts(List<ContactPoint2D> contacts) => Physics2D.GetContacts(this, new ContactFilter2D().NoFilter(), contacts);

        public int GetContacts(ContactFilter2D contactFilter, ContactPoint2D[] contacts) => Physics2D.GetContacts(this, contactFilter, contacts);

        public int GetContacts(ContactFilter2D contactFilter, List<ContactPoint2D> contacts) => Physics2D.GetContacts(this, contactFilter, contacts);

        public int GetContacts(Collider2D[] colliders) => Physics2D.GetContacts(this, new ContactFilter2D().NoFilter(), colliders);

        public int GetContacts(List<Collider2D> colliders) => Physics2D.GetContacts(this, new ContactFilter2D().NoFilter(), colliders);

        public int GetContacts(ContactFilter2D contactFilter, Collider2D[] colliders) => Physics2D.GetContacts(this, contactFilter, colliders);

        public int GetContacts(ContactFilter2D contactFilter, List<Collider2D> colliders) => Physics2D.GetContacts(this, contactFilter, colliders);

        // ------------------------------------------------------------------ queries

        public bool OverlapPoint(Vector2 point)
        {
            W.PrepareQuery();
            foreach (var f in m_Fixtures)
                if (f.TestPoint(W.ToB2(point))) return true;
            return false;
        }

        public ColliderDistance2D Distance(Collider2D collider) => Physics2D.Distance(this, collider);

        public ColliderDistance2D Distance(Vector2 thisPosition, float thisAngle, Collider2D collider, Vector2 position, float angle)
        {
            if (collider == null) throw new ArgumentNullException(nameof(collider));
            W.PrepareQuery();
            var a = new Unbound.Box2D.Common.Transform(W.ToB2(thisPosition), thisAngle * Mathf.Deg2Rad);
            var b = new Unbound.Box2D.Common.Transform(W.ToB2(position), angle * Mathf.Deg2Rad);
            return Physics2DQueries.Distance(this, a, collider, b, overrideB: true);
        }

        public Vector2 ClosestPoint(Vector2 position) => Physics2D.ClosestPoint(position, this);

        public int Overlap(ContactFilter2D contactFilter, Collider2D[] results) =>
            Physics2DQueries.CopyColliders(Physics2DQueries.OverlapColliders(new[] { this }, null, contactFilter, null), results);

        public int Overlap(List<Collider2D> results) => Overlap(new ContactFilter2D().NoFilter(), results);

        public int Overlap(ContactFilter2D contactFilter, List<Collider2D> results) =>
            Physics2DQueries.CopyColliders(Physics2DQueries.OverlapColliders(new[] { this }, null, contactFilter, null), results);

        public int Overlap(Vector2 position, float angle, List<Collider2D> results) => Overlap(position, angle, new ContactFilter2D().NoFilter(), results);

        public int Overlap(Vector2 position, float angle, ContactFilter2D contactFilter, List<Collider2D> results)
        {
            var pose = new Unbound.Box2D.Common.Transform(W.ToB2(position), angle * Mathf.Deg2Rad);
            return Physics2DQueries.CopyColliders(Physics2DQueries.OverlapColliders(new[] { this }, pose, contactFilter, null), results);
        }

        public int OverlapCollider(ContactFilter2D contactFilter, Collider2D[] results) => Overlap(contactFilter, results);

        public int OverlapCollider(ContactFilter2D contactFilter, List<Collider2D> results) => Overlap(contactFilter, results);

        public int Cast(Vector2 direction, RaycastHit2D[] results) => Cast(direction, Physics2D.DefaultCastFilter(m_GameObject), results, float.PositiveInfinity, true);

        public int Cast(Vector2 direction, RaycastHit2D[] results, float distance) => Cast(direction, Physics2D.DefaultCastFilter(m_GameObject), results, distance, true);

        public int Cast(Vector2 direction, RaycastHit2D[] results, float distance, bool ignoreSiblingColliders) =>
            Cast(direction, Physics2D.DefaultCastFilter(m_GameObject), results, distance, ignoreSiblingColliders);

        public int Cast(Vector2 direction, ContactFilter2D contactFilter, RaycastHit2D[] results) => Cast(direction, contactFilter, results, float.PositiveInfinity, true);

        public int Cast(Vector2 direction, ContactFilter2D contactFilter, RaycastHit2D[] results, float distance) => Cast(direction, contactFilter, results, distance, true);

        public int Cast(Vector2 direction, ContactFilter2D contactFilter, RaycastHit2D[] results, float distance, bool ignoreSiblingColliders)
        {
            var hits = Physics2DQueries.CastColliders(new[] { this }, null, direction, distance, contactFilter, ignoreSiblingColliders ? attachedRigidbody : null);
            return Physics2DQueries.CopyHits(hits, results);
        }

        public int Cast(Vector2 direction, List<RaycastHit2D> results, float distance = float.PositiveInfinity, bool ignoreSiblingColliders = true) =>
            Cast(direction, Physics2D.DefaultCastFilter(m_GameObject), results, distance, ignoreSiblingColliders);

        public int Cast(Vector2 direction, ContactFilter2D contactFilter, List<RaycastHit2D> results, float distance = float.PositiveInfinity, bool ignoreSiblingColliders = true)
        {
            var hits = Physics2DQueries.CastColliders(new[] { this }, null, direction, distance, contactFilter, ignoreSiblingColliders ? attachedRigidbody : null);
            return Physics2DQueries.CopyHits(hits, results);
        }

        public int Cast(Vector2 position, float angle, Vector2 direction, List<RaycastHit2D> results, float distance = float.PositiveInfinity, bool ignoreSiblingColliders = true) =>
            Cast(position, angle, direction, Physics2D.DefaultCastFilter(m_GameObject), results, distance, ignoreSiblingColliders);

        public int Cast(Vector2 position, float angle, Vector2 direction, ContactFilter2D contactFilter, List<RaycastHit2D> results, float distance = float.PositiveInfinity, bool ignoreSiblingColliders = true)
        {
            var pose = new Unbound.Box2D.Common.Transform(W.ToB2(position), angle * Mathf.Deg2Rad);
            var hits = Physics2DQueries.CastColliders(new[] { this }, pose, direction, distance, contactFilter, ignoreSiblingColliders ? attachedRigidbody : null);
            return Physics2DQueries.CopyHits(hits, results);
        }

        public int Raycast(Vector2 direction, RaycastHit2D[] results) => Raycast(direction, results, float.PositiveInfinity, Physics2D.AllLayers, float.NegativeInfinity, float.PositiveInfinity);

        public int Raycast(Vector2 direction, RaycastHit2D[] results, float distance) => Raycast(direction, results, distance, Physics2D.AllLayers, float.NegativeInfinity, float.PositiveInfinity);

        public int Raycast(Vector2 direction, RaycastHit2D[] results, float distance, int layerMask) => Raycast(direction, results, distance, layerMask, float.NegativeInfinity, float.PositiveInfinity);

        public int Raycast(Vector2 direction, RaycastHit2D[] results, float distance, int layerMask, float minDepth) => Raycast(direction, results, distance, layerMask, minDepth, float.PositiveInfinity);

        public int Raycast(Vector2 direction, RaycastHit2D[] results, float distance, int layerMask, float minDepth, float maxDepth) =>
            Raycast(direction, ContactFilter2D.CreateLegacyFilter(layerMask, minDepth, maxDepth), results, distance);

        public int Raycast(Vector2 direction, ContactFilter2D contactFilter, RaycastHit2D[] results) => Raycast(direction, contactFilter, results, float.PositiveInfinity);

        public int Raycast(Vector2 direction, ContactFilter2D contactFilter, RaycastHit2D[] results, float distance)
        {
            var hits = RaycastFromCollider(direction, contactFilter, distance);
            return Physics2DQueries.CopyHits(hits, results);
        }

        public int Raycast(Vector2 direction, ContactFilter2D contactFilter, List<RaycastHit2D> results, float distance = float.PositiveInfinity) =>
            Physics2DQueries.CopyHits(RaycastFromCollider(direction, contactFilter, distance), results);

        /// <summary>A ray from the collider's position (its offset in world space), ignoring the
        /// collider itself.</summary>
        List<RaycastHit2D> RaycastFromCollider(Vector2 direction, ContactFilter2D contactFilter, float distance)
        {
            var origin = (Vector2)transform.TransformPoint(m_Offset);
            var hits = Physics2DQueries.Raycast(origin, direction, distance, contactFilter);
            hits.RemoveAll(h => ReferenceEquals(h.collider, this));
            return hits;
        }

        // ------------------------------------------------------------------ serialization

        internal void ReadColliderData(YNode body, IReferenceResolver resolver)
        {
            m_Density = body.Float("m_Density", 1f);
            var material = body.Ref("m_Material");
            m_Material = material.IsNull ? null : resolver.Resolve(material, typeof(PhysicsMaterial2D)) as PhysicsMaterial2D;
            m_IncludeLayers = ReadMask(body, "m_IncludeLayers", 0);
            m_ExcludeLayers = ReadMask(body, "m_ExcludeLayers", 0);
            m_LayerOverridePriority = body.Int("m_LayerOverridePriority");
            m_ForceSendLayers = ReadMask(body, "m_ForceSendLayers", -1);
            m_ForceReceiveLayers = ReadMask(body, "m_ForceReceiveLayers", -1);
            m_ContactCaptureLayers = ReadMask(body, "m_ContactCaptureLayers", -1);
            m_CallbackLayers = ReadMask(body, "m_CallbackLayers", -1);
            m_IsTrigger = body.Bool("m_IsTrigger");
            m_UsedByEffector = body.Bool("m_UsedByEffector");
            if (body.Get("m_CompositeOperation") != null) m_CompositeOperation = (CompositeOperation)body.Int("m_CompositeOperation");
            else m_CompositeOperation = body.Bool("m_UsedByComposite") ? CompositeOperation.Merge : CompositeOperation.None;
            m_CompositeOrder = body.Int("m_CompositeOrder");
            m_Offset = body.Vector2("m_Offset");
        }

        internal void CopyColliderData(Collider2D s)
        {
            m_Enabled = s.m_Enabled;
            m_Density = s.m_Density;
            m_IsTrigger = s.m_IsTrigger;
            m_UsedByEffector = s.m_UsedByEffector;
            m_CompositeOperation = s.m_CompositeOperation;
            m_CompositeOrder = s.m_CompositeOrder;
            m_Offset = s.m_Offset;
            m_Material = s.m_Material;
            m_LayerOverridePriority = s.m_LayerOverridePriority;
            m_IncludeLayers = s.m_IncludeLayers;
            m_ExcludeLayers = s.m_ExcludeLayers;
            m_ForceSendLayers = s.m_ForceSendLayers;
            m_ForceReceiveLayers = s.m_ForceReceiveLayers;
            m_ContactCaptureLayers = s.m_ContactCaptureLayers;
            m_CallbackLayers = s.m_CallbackLayers;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver) => ReadColliderData(body, resolver);

        internal override void CopyFrom(Component source, Func<Object, Object> remap) => CopyColliderData((Collider2D)source);

        public enum CompositeOperation
        {
            None = 0,
            Merge = 1,
            Intersect = 2,
            Difference = 3,
            Flip = 4,
        }
    }

    [ClassId(58)]
    public sealed partial class CircleCollider2D : Collider2D
    {
        float m_Radius = 0.5f;

        public CircleCollider2D() { }

        public float radius
        {
            get => m_Radius;
            set
            {
                m_Radius = Mathf.Max(value, 0.0001f);
                ShapeChanged();
            }
        }

        public Vector2 center
        {
            get => offset;
            set => offset = value;
        }

        internal override void BuildShapes(Matrix4x4 toBody, List<Shape> shapes)
        {
            var scales = AxisScales(toBody);
            float r = m_Radius * Mathf.Max(scales.x, scales.y);
            if (!(r > B2Settings.LinearSlop * 0.5f))
            {
                m_ErrorState = ColliderErrorState2D.NoShapes;
                return;
            }
            shapes.Add(new CircleShape { Position = ToBody(toBody, m_Offset), Radius = r });
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            ReadColliderData(body, resolver);
            m_Radius = body.Float("m_Radius", 0.5f);
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (CircleCollider2D)source;
            CopyColliderData(s);
            m_Radius = s.m_Radius;
        }
    }

    [ClassId(61)]
    public sealed partial class BoxCollider2D : Collider2D
    {
        Vector2 m_Size = Vector2.one;
        float m_EdgeRadius;
        bool m_AutoTiling;

        public BoxCollider2D() { }

        public Vector2 size
        {
            get => m_Size;
            set
            {
                m_Size = value;
                ShapeChanged();
            }
        }

        public float edgeRadius
        {
            get => m_EdgeRadius;
            set
            {
                m_EdgeRadius = Mathf.Max(0f, value);
                ShapeChanged();
            }
        }

        public bool autoTiling
        {
            get => m_AutoTiling;
            set => m_AutoTiling = value;
        }

        public Vector2 center
        {
            get => offset;
            set => offset = value;
        }

        internal override void BuildShapes(Matrix4x4 toBody, List<Shape> shapes)
        {
            var h = new Vector2(Mathf.Abs(m_Size.x), Mathf.Abs(m_Size.y)) * 0.5f;
            var o = m_Offset;
            var points = new[]
            {
                ToBody(toBody, o + new Vector2(-h.x, -h.y)),
                ToBody(toBody, o + new Vector2(h.x, -h.y)),
                ToBody(toBody, o + new Vector2(h.x, h.y)),
                ToBody(toBody, o + new Vector2(-h.x, h.y)),
            };
            var shape = ConvexShape(points);
            if (shape == null)
            {
                m_ErrorState = ColliderErrorState2D.NoShapes;
                return;
            }
            // Unity does not scale the edge radius with the transform.
            if (m_EdgeRadius > 0f) shape.Radius = B2Settings.PolygonRadius + m_EdgeRadius;
            shapes.Add(shape);
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            ReadColliderData(body, resolver);
            m_Size = body.Get("m_Size") != null ? body.Vector2("m_Size") : Vector2.one;
            m_EdgeRadius = body.Float("m_EdgeRadius");
            m_AutoTiling = body.Bool("m_AutoTiling");
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (BoxCollider2D)source;
            CopyColliderData(s);
            m_Size = s.m_Size;
            m_EdgeRadius = s.m_EdgeRadius;
            m_AutoTiling = s.m_AutoTiling;
        }
    }

    [ClassId(70)]
    public sealed partial class CapsuleCollider2D : Collider2D
    {
        Vector2 m_Size = new Vector2(0.5f, 1f);
        CapsuleDirection2D m_Direction = CapsuleDirection2D.Vertical;

        public CapsuleCollider2D() { }

        public Vector2 size
        {
            get => m_Size;
            set
            {
                m_Size = value;
                ShapeChanged();
            }
        }

        public CapsuleDirection2D direction
        {
            get => m_Direction;
            set
            {
                m_Direction = value;
                ShapeChanged();
            }
        }

        internal override void BuildShapes(Matrix4x4 toBody, List<Shape> shapes)
        {
            var shape = CapsuleShapeOf(toBody, m_Offset, m_Size, m_Direction);
            if (shape == null)
            {
                m_ErrorState = ColliderErrorState2D.NoShapes;
                return;
            }
            shapes.Add(shape);
        }

        /// <summary>A capsule of the given size in the space <paramref name="toBody"/> maps from:
        /// a circle when it is no longer than wide.</summary>
        internal static Shape CapsuleShapeOf(Matrix4x4 toBody, Vector2 offset, Vector2 size, CapsuleDirection2D direction)
        {
            var scales = AxisScales(toBody);
            float w = Mathf.Abs(size.x) * scales.x;
            float h = Mathf.Abs(size.y) * scales.y;
            var center = ToBody(toBody, offset);
            var axisX = scales.x > 0f ? new System.Numerics.Vector2(toBody.m00, toBody.m10) / scales.x : new System.Numerics.Vector2(1f, 0f);
            var axisY = scales.y > 0f ? new System.Numerics.Vector2(toBody.m01, toBody.m11) / scales.y : new System.Numerics.Vector2(0f, 1f);
            float radius, half;
            System.Numerics.Vector2 axis;
            if (direction == CapsuleDirection2D.Vertical)
            {
                radius = w * 0.5f;
                half = h * 0.5f - radius;
                axis = axisY;
            }
            else
            {
                radius = h * 0.5f;
                half = w * 0.5f - radius;
                axis = axisX;
            }
            if (!(radius > B2Settings.LinearSlop * 0.5f)) return null;
            if (half <= B2Settings.LinearSlop * 0.5f)
                return new CircleShape { Position = center, Radius = Mathf.Max(w, h) * 0.5f };
            return new CapsuleShape(center - axis * half, center + axis * half, radius);
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            ReadColliderData(body, resolver);
            m_Size = body.Get("m_Size") != null ? body.Vector2("m_Size") : new Vector2(0.5f, 1f);
            m_Direction = (CapsuleDirection2D)body.Int("m_Direction");
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (CapsuleCollider2D)source;
            CopyColliderData(s);
            m_Size = s.m_Size;
            m_Direction = s.m_Direction;
        }
    }

    [ClassId(60)]
    public sealed partial class PolygonCollider2D : Collider2D
    {
        List<Vector2[]> m_Paths = new List<Vector2[]> { RegularPolygon(5, Vector2.one, Vector2.zero) };
        bool m_UseDelaunayMesh;
        bool m_AutoTiling;

        public PolygonCollider2D() { }

        public bool useDelaunayMesh
        {
            get => m_UseDelaunayMesh;
            set => m_UseDelaunayMesh = value;
        }

        public bool autoTiling
        {
            get => m_AutoTiling;
            set => m_AutoTiling = value;
        }

        public Vector2[] points
        {
            get => m_Paths.Count > 0 ? (Vector2[])m_Paths[0].Clone() : Array.Empty<Vector2>();
            set
            {
                if (value == null) throw new ArgumentNullException(nameof(value));
                if (m_Paths.Count == 0) m_Paths.Add(null);
                m_Paths[0] = (Vector2[])value.Clone();
                ShapeChanged();
            }
        }

        public int pathCount
        {
            get => m_Paths.Count;
            set
            {
                int count = Math.Max(0, value);
                while (m_Paths.Count > count) m_Paths.RemoveAt(m_Paths.Count - 1);
                while (m_Paths.Count < count) m_Paths.Add(Array.Empty<Vector2>());
                ShapeChanged();
            }
        }

        public int GetTotalPointCount()
        {
            int total = 0;
            foreach (var p in m_Paths) total += p.Length;
            return total;
        }

        public Vector2[] GetPath(int index)
        {
            if (index < 0 || index >= m_Paths.Count)
                throw new ArgumentOutOfRangeException(nameof(index), $"Path {index} does not exist.");
            return (Vector2[])m_Paths[index].Clone();
        }

        public int GetPath(int index, List<Vector2> points)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            if (index < 0 || index >= m_Paths.Count)
                throw new ArgumentOutOfRangeException(nameof(index), $"Path {index} does not exist.");
            points.Clear();
            points.AddRange(m_Paths[index]);
            return points.Count;
        }

        public void SetPath(int index, Vector2[] points)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            if (index < 0) throw new ArgumentOutOfRangeException(nameof(index));
            while (m_Paths.Count <= index) m_Paths.Add(Array.Empty<Vector2>());
            m_Paths[index] = (Vector2[])points.Clone();
            ShapeChanged();
        }

        public void SetPath(int index, List<Vector2> points)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            SetPath(index, points.ToArray());
        }

        public void CreatePrimitive(int sides) => CreatePrimitive(sides, Vector2.one, Vector2.zero);

        public void CreatePrimitive(int sides, Vector2 scale) => CreatePrimitive(sides, scale, Vector2.zero);

        public void CreatePrimitive(int sides, Vector2 scale, Vector2 offset)
        {
            if (sides < 3)
            {
                Debug.LogWarning("Cannot create a 2D polygon primitive collider with less than three sides.", this);
                return;
            }
            m_Paths = new List<Vector2[]> { RegularPolygon(sides, scale, offset) };
            ShapeChanged();
        }

        static Vector2[] RegularPolygon(int sides, Vector2 scale, Vector2 offset)
        {
            var points = new Vector2[sides];
            for (int i = 0; i < sides; i++)
            {
                float a = Mathf.PI * 0.5f + i * 2f * Mathf.PI / sides;
                points[i] = new Vector2(Mathf.Cos(a) * 0.5f * scale.x, Mathf.Sin(a) * 0.5f * scale.y) + offset;
            }
            return points;
        }

        internal override void BuildShapes(Matrix4x4 toBody, List<Shape> shapes)
        {
            int made = 0, dropped = 0;
            foreach (var path in m_Paths)
            {
                if (path == null || path.Length < 3) continue;
                var points = new List<System.Numerics.Vector2>(path.Length);
                foreach (var p in path) points.Add(ToBody(toBody, p + m_Offset));
                foreach (var piece in PolygonDecomposition.ConvexPieces(points))
                {
                    var shape = ConvexShape(piece);
                    if (shape == null) { dropped++; continue; }
                    shapes.Add(shape);
                    made++;
                }
            }
            m_ErrorState = made == 0 ? ColliderErrorState2D.NoShapes : dropped > 0 ? ColliderErrorState2D.RemovedShapes : ColliderErrorState2D.None;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            ReadColliderData(body, resolver);
            m_UseDelaunayMesh = body.Bool("m_UseDelaunayMesh");
            m_AutoTiling = body.Bool("m_AutoTiling");
            var paths = body.Get("m_Points")?.Get("m_Paths");
            if (paths == null) return;
            m_Paths = new List<Vector2[]>();
            foreach (var path in paths.Seq())
            {
                var list = new List<Vector2>();
                foreach (var p in path.Seq()) list.Add(new Vector2(p.Float("x"), p.Float("y")));
                m_Paths.Add(list.ToArray());
            }
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (PolygonCollider2D)source;
            CopyColliderData(s);
            m_UseDelaunayMesh = s.m_UseDelaunayMesh;
            m_AutoTiling = s.m_AutoTiling;
            m_Paths = new List<Vector2[]>();
            foreach (var p in s.m_Paths) m_Paths.Add((Vector2[])p.Clone());
        }
    }

    [ClassId(68)]
    public sealed partial class EdgeCollider2D : Collider2D
    {
        Vector2[] m_Points = { new Vector2(-0.5f, 0f), new Vector2(0.5f, 0f) };
        float m_EdgeRadius;
        bool m_UseAdjacentStartPoint, m_UseAdjacentEndPoint;
        Vector2 m_AdjacentStartPoint, m_AdjacentEndPoint;

        public EdgeCollider2D() { }

        public float edgeRadius
        {
            get => m_EdgeRadius;
            set
            {
                m_EdgeRadius = Mathf.Max(0f, value);
                ShapeChanged();
            }
        }

        public int edgeCount => Math.Max(0, m_Points.Length - 1);

        public int pointCount => m_Points.Length;

        public Vector2[] points
        {
            get => (Vector2[])m_Points.Clone();
            set
            {
                if (value == null || value.Length < 2)
                {
                    Debug.LogWarning("Cannot set the points of an EdgeCollider2D to fewer than two points.", this);
                    return;
                }
                m_Points = (Vector2[])value.Clone();
                ShapeChanged();
            }
        }

        public bool useAdjacentStartPoint
        {
            get => m_UseAdjacentStartPoint;
            set => m_UseAdjacentStartPoint = value;
        }

        public bool useAdjacentEndPoint
        {
            get => m_UseAdjacentEndPoint;
            set => m_UseAdjacentEndPoint = value;
        }

        public Vector2 adjacentStartPoint
        {
            get => m_AdjacentStartPoint;
            set => m_AdjacentStartPoint = value;
        }

        public Vector2 adjacentEndPoint
        {
            get => m_AdjacentEndPoint;
            set => m_AdjacentEndPoint = value;
        }

        public void Reset()
        {
            m_Points = new[] { new Vector2(-0.5f, 0f), new Vector2(0.5f, 0f) };
            m_EdgeRadius = 0f;
            ShapeChanged();
        }

        public int GetPoints(List<Vector2> points)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            points.Clear();
            points.AddRange(m_Points);
            return points.Count;
        }

        public bool SetPoints(List<Vector2> points)
        {
            if (points == null) throw new ArgumentNullException(nameof(points));
            if (points.Count < 2) return false;
            m_Points = points.ToArray();
            ShapeChanged();
            return true;
        }

        internal override void BuildShapes(Matrix4x4 toBody, List<Shape> shapes)
        {
            // Unity does not scale the edge radius with the transform.
            float radius = B2Settings.PolygonRadius + m_EdgeRadius;
            int dropped = 0, made = 0;
            for (int i = 0; i + 1 < m_Points.Length; i++)
            {
                var a = ToBody(toBody, m_Points[i] + m_Offset);
                var b = ToBody(toBody, m_Points[i + 1] + m_Offset);
                if (System.Numerics.Vector2.DistanceSquared(a, b) <= B2Settings.LinearSlop * B2Settings.LinearSlop * 0.25f)
                {
                    dropped++;
                    continue;
                }
                var edge = new EdgeShape();
                edge.SetTwoSided(a, b);
                edge.Radius = radius;
                shapes.Add(edge);
                made++;
            }
            m_ErrorState = made == 0 ? ColliderErrorState2D.NoShapes : dropped > 0 ? ColliderErrorState2D.RemovedShapes : ColliderErrorState2D.None;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            ReadColliderData(body, resolver);
            m_EdgeRadius = body.Float("m_EdgeRadius");
            var points = body.Get("m_Points");
            if (points != null)
            {
                var list = new List<Vector2>();
                foreach (var p in points.Seq()) list.Add(new Vector2(p.Float("x"), p.Float("y")));
                if (list.Count >= 2) m_Points = list.ToArray();
            }
            m_UseAdjacentStartPoint = body.Bool("m_UseAdjacentStartPoint");
            m_UseAdjacentEndPoint = body.Bool("m_UseAdjacentEndPoint");
            m_AdjacentStartPoint = body.Vector2("m_AdjacentStartPoint");
            m_AdjacentEndPoint = body.Vector2("m_AdjacentEndPoint");
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (EdgeCollider2D)source;
            CopyColliderData(s);
            m_Points = (Vector2[])s.m_Points.Clone();
            m_EdgeRadius = s.m_EdgeRadius;
            m_UseAdjacentStartPoint = s.m_UseAdjacentStartPoint;
            m_UseAdjacentEndPoint = s.m_UseAdjacentEndPoint;
            m_AdjacentStartPoint = s.m_AdjacentStartPoint;
            m_AdjacentEndPoint = s.m_AdjacentEndPoint;
        }
    }
}
