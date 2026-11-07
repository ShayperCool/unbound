using System;
using System.Collections.Generic;
using Unbound.Box3D;
using UnityEngine;
using W = Unbound.Runtime.PhysicsWorld;

namespace Unbound.Runtime
{
    /// <summary>The 3D queries behind UnityEngine.Physics: rays, shape casts and overlaps in the Box3D
    /// world, with Unity's conventions (layer masks, trigger interaction, distances, initial overlaps).</summary>
    internal static unsafe class PhysicsQueries
    {
        /// <summary>The longest cast: Unity's infinite distances become this many meters.</summary>
        internal const float MaxLength = 1e5f;

        // Shared with the native query flags. The first two bits select triggers/backfaces.
        const int MultipleRayFaces = 1 << 2;
        const int SkipInitialOverlap = 1 << 3;

        [ThreadStatic] static B3.Hit[] s_Hits;
        [ThreadStatic] static ulong[] s_Shapes;

        internal static float Length(float maxDistance) =>
            float.IsNaN(maxDistance) || maxDistance > MaxLength ? MaxLength : Mathf.Max(0f, maxDistance);

        static int HitTriggers(QueryTriggerInteraction q) => q switch
        {
            QueryTriggerInteraction.Ignore => 0,
            QueryTriggerInteraction.Collide => 1,
            _ => Physics.s_QueriesHitTriggers ? 1 : 0,
        };

        /// <summary>The ray and shape casts' query flags (the native hitTriggers argument): bit 0 hits
        /// triggers, bit 1 hits the back faces of mesh triangles. Documented (6000.0
        /// Physics.queriesHitBackfaces): "By default, all physics queries such as a raycast or a
        /// shape sweep (e.g. SphereCastAll) won't detect hits with back-face triangles."</summary>
        internal static int CastFlags(QueryTriggerInteraction q) => HitTriggers(q) | (HitsBackfaces ? 2 : 0);

        /// <summary>Whether the casts hit back faces now: Physics.queriesHitBackfaces, or the batch
        /// command's QueryParameters.hitBackfaces while it is answered.</summary>
        static bool HitsBackfaces => s_Backfaces ?? Physics.s_QueriesHitBackfaces;

        [ThreadStatic] static bool? s_Backfaces;

        internal readonly struct BackfacesScope : IDisposable
        {
            readonly bool? m_Previous;
            internal BackfacesScope(bool value)
            {
                m_Previous = s_Backfaces;
                s_Backfaces = value;
            }
            public void Dispose() => s_Backfaces = m_Previous;
        }

        /// <summary>Casts in the scope hit back faces as <paramref name="value"/> says, whatever
        /// Physics.queriesHitBackfaces is: a batch command's own QueryParameters.hitBackfaces (Unity 6000.0.71f1, physicsbatch and
        /// physicsccdhinge).</summary>
        internal static BackfacesScope Backfaces(bool value) => new BackfacesScope(value);

        static ulong Mask(int layerMask) => unchecked((uint)layerMask);

        /// <summary>A hit records the collider by instance ID only (RaycastHit is unmanaged); its
        /// collider-dependent properties look it up when read.</summary>
        internal static RaycastHit ToHit(B3.Hit hit, Collider collider, float length) => new RaycastHit
        {
            m_Point = hit.Point,
            m_Normal = hit.Normal,
            m_Distance = hit.Fraction * length,
            m_FaceID = unchecked((uint)hit.TriangleIndex),
            m_UV = hit.UV,
            m_Collider = collider != null ? collider.m_InstanceID : 0,
        };

        /// <summary>The physics scene the queries search: the default one (Physics), or the one a
        /// PhysicsScene query or a body's sweep names.</summary>
        [ThreadStatic] static W.Space s_Target;

        internal readonly struct Target : IDisposable
        {
            readonly W.Space m_Previous;
            internal Target(W.Space space)
            {
                m_Previous = s_Target;
                s_Target = space;
            }
            public void Dispose() => s_Target = m_Previous;
        }

        /// <summary>Queries in the scope search this physics scene (null: the default one).</summary>
        internal static Target In(W.Space space) => new Target(space);

        /// <summary>The world, ready for a query; 0 when there is nothing to hit.</summary>
        static uint ReadyWorld()
        {
            var space = s_Target ?? W.DefaultSpace;
            if (s_PreparedWorlds != null) return s_PreparedWorlds.TryGetValue(space, out uint prepared) ? prepared : 0;
            // Objects that just changed scene join their physics scene before it is searched.
            W.SceneMembershipChanged();
            W.PrepareQuery(space);
            return space.Colliders.Count == 0 ? 0 : space.World;
        }

        [ThreadStatic] static Dictionary<W.Space, uint> s_PreparedWorlds;

        internal readonly struct PreparedScope : IDisposable
        {
            readonly Dictionary<W.Space, uint> m_Previous;
            internal PreparedScope(Dictionary<W.Space, uint> worlds)
            {
                m_Previous = s_PreparedWorlds;
                s_PreparedWorlds = worlds;
            }
            public void Dispose() => s_PreparedWorlds = m_Previous;
        }
        internal static PreparedScope Prepared(Dictionary<W.Space, uint> worlds) => new PreparedScope(worlds);

        static void EnsureScratch(int capacity)
        {
            s_Hits ??= new B3.Hit[Math.Max(64, capacity)];
            s_Shapes ??= new ulong[Math.Max(64, capacity)];
            if (s_Hits.Length < capacity) Array.Resize(ref s_Hits, capacity);
            if (s_Shapes.Length < capacity) Array.Resize(ref s_Shapes, capacity);
        }

        internal static void Reserve(int capacity)
        {
            EnsureScratch(capacity);
            B3.ua3_query_reserve(capacity);
        }

        static RaycastHit ConvertedHit(int index, float length, Vector3 direction)
        {
            var raw = s_Hits[index];
            var hit = ToHit(raw, W.ColliderOf(raw.Shape), length);
            if (raw.Fraction <= 0f)
            {
                hit.m_Point = Vector3.zero;
                hit.m_Normal = -direction;
                hit.m_Distance = 0f;
            }
            return hit;
        }

        static List<RaycastHit> Collect(int count, float length, Vector3 direction)
        {
            var result = new List<RaycastHit>(count);
            for (int i = 0; i < count; i++)
            {
                var hit = ConvertedHit(i, length, direction);
                if (hit.m_Collider != 0) result.Add(hit);
            }
            return result;
        }

        // The shared native query path. Batch queries cap storage at maxHits; allocating APIs
        // repeat after growing scratch storage so their result count has no fixed ceiling.
        static int ReadCast(bool ray, B3.Proxy kind, Vector3 a, Vector3 b, Quaternion rotation, float radius,
            Vector3 direction, float maxDistance, int layerMask, QueryTriggerInteraction triggers,
            bool closest, bool bounded, int extraFlags = 0, int boundedCapacity = 0)
        {
            EnsureScratch(64);
            uint world = ReadyWorld();
            if (world == 0 || layerMask == 0 || direction.sqrMagnitude < 1e-12f || maxDistance < 0f) return 0;
            if (!ray && maxDistance == 0f)
            {
                if (closest || (extraFlags & SkipInitialOverlap) != 0) return 0;
                int overlaps = ReadOverlap(kind, a, b, rotation, radius, layerMask, triggers, bounded);
                if (s_Hits.Length < overlaps) Array.Resize(ref s_Hits, overlaps);
                for (int i = 0; i < overlaps; i++)
                    s_Hits[i] = new B3.Hit { Shape = s_Shapes[i], TriangleIndex = -1 };
                return overlaps;
            }
            var translation = direction.normalized * Length(maxDistance);
            int flags = CastFlags(triggers) | extraFlags;
            int count;
            while (true)
            {
                int capacity = bounded ? boundedCapacity : s_Hits.Length;
                fixed (B3.Hit* hits = s_Hits)
                    count = ray
                        ? B3.ua3_raycast(world, a, translation, Mask(layerMask), flags, closest ? 1 : 0, hits, capacity)
                        : B3.ua3_shapecast(world, (int)kind, a, b, Quaternion.Normalize(rotation), Mathf.Max(0f, radius), translation, Mask(layerMask), flags, closest ? 1 : 0, hits, capacity);
                if (bounded || count <= capacity) return Math.Min(count, capacity);
                Array.Resize(ref s_Hits, count);
            }
        }

        internal static List<RaycastHit> Raycast(Vector3 origin, Vector3 direction, float maxDistance, int layerMask, QueryTriggerInteraction triggers, bool closest) =>
            Collect(ReadCast(true, default, origin, default, Quaternion.identity, 0f, direction, maxDistance, layerMask, triggers, closest, false), Length(maxDistance), direction.normalized);

        internal static List<RaycastHit> Cast(B3.Proxy kind, Vector3 a, Vector3 b, Quaternion rotation, float radius, Vector3 direction, float maxDistance, int layerMask, QueryTriggerInteraction triggers, bool closest) =>
            Collect(ReadCast(false, kind, a, b, rotation, radius, direction, maxDistance, layerMask, triggers, closest, false), Length(maxDistance), direction.normalized);

        internal static int BatchCast(PhysicsScene scene, bool ray, B3.Proxy kind, Vector3 a, Vector3 b, Quaternion rotation, float radius,
            Vector3 direction, float distance, QueryParameters parameters, RaycastHit[] results, bool closest)
        {
            using var target = scene.Searching();
            using var backfaces = Backfaces(parameters.hitBackfaces);
            // Bit 2 requests multiple ray faces; bit 3 omits initial sweep overlaps at every
            // maxHits, as measured by tests/projects/physicsbatch in Unity 6000.0.71f1.
            int flags = ray ? (parameters.hitMultipleFaces && !closest ? MultipleRayFaces : 0) : SkipInitialOverlap;
            int count = ReadCast(ray, kind, a, b, rotation, radius, direction, distance,
                parameters.layerMask, parameters.hitTriggers, closest, true, flags, results.Length);
            int written = Math.Min(count, results.Length);
            for (int i = 0; i < written; i++) results[i] = ConvertedHit(i, Length(distance), direction.normalized);
            return written;
        }

        static int ReadOverlap(B3.Proxy kind, Vector3 a, Vector3 b, Quaternion rotation, float radius, int layerMask, QueryTriggerInteraction triggers, bool bounded)
        {
            EnsureScratch(64);
            uint world = ReadyWorld();
            if (world == 0 || layerMask == 0) return 0;
            int count;
            while (true)
            {
                fixed (ulong* shapes = s_Shapes)
                    count = B3.ua3_overlap(world, (int)kind, a, b, Quaternion.Normalize(rotation), Mathf.Max(0f, radius), Mask(layerMask), HitTriggers(triggers), shapes, s_Shapes.Length);
                if (bounded || count <= s_Shapes.Length) return Math.Min(count, s_Shapes.Length);
                Array.Resize(ref s_Shapes, count);
            }
        }

        internal static List<Collider> Overlap(B3.Proxy kind, Vector3 a, Vector3 b, Quaternion rotation, float radius, int layerMask, QueryTriggerInteraction triggers)
        {
            int count = ReadOverlap(kind, a, b, rotation, radius, layerMask, triggers, false);
            var result = new List<Collider>(count);
            for (int i = 0; i < count; i++)
            {
                var collider = W.ColliderOf(s_Shapes[i]);
                if (collider != null) result.Add(collider);
            }
            return result;
        }

        internal static int BatchOverlap(PhysicsScene scene, B3.Proxy kind, Vector3 a, Vector3 b, Quaternion rotation, float radius, QueryParameters parameters, Collider[] results)
        {
            using var target = scene.Searching();
            int count = ReadOverlap(kind, a, b, rotation, radius, parameters.layerMask, parameters.hitTriggers, true);
            int written = 0;
            for (int i = 0; i < count && written < results.Length; i++)
            {
                var collider = W.ColliderOf(s_Shapes[i]);
                if (collider != null) results[written++] = collider;
            }
            return written;
        }

        internal static Vector3 ClosestPoint(Collider collider, Vector3 point)
        {
            using var target = In(collider.m_Space);
            ReadyWorld();
            return collider.m_Shape == 0 ? point : B3.ua3_shape_closest_point(collider.m_Shape, point);
        }

        /// <summary>Casts a collider's own geometry, at its current pose, along a direction.</summary>
        internal static List<RaycastHit> CastCollider(Collider c, Vector3 direction, float maxDistance, int layerMask, QueryTriggerInteraction triggers, bool closest)
        {
            var t = c.transform;
            var m = t.localToWorldMatrix;
            switch (c)
            {
                case SphereCollider sphere:
                {
                    var s = Collider.AxisScales(m);
                    float r = Mathf.Abs(sphere.m_Radius) * Mathf.Max(s.x, Mathf.Max(s.y, s.z));
                    var center = m.MultiplyPoint3x4(sphere.m_Center);
                    return Cast(B3.Proxy.Sphere, center, center, Quaternion.identity, r, direction, maxDistance, layerMask, triggers, closest);
                }
                case CapsuleCollider capsule:
                {
                    capsule.Segment(m, out var a, out var b, out float r);
                    return Cast(B3.Proxy.Capsule, a, b, Quaternion.identity, r, direction, maxDistance, layerMask, triggers, closest);
                }
                case BoxCollider box:
                {
                    var half = Vector3.Scale(new Vector3(Mathf.Abs(box.m_Size.x), Mathf.Abs(box.m_Size.y), Mathf.Abs(box.m_Size.z)) * 0.5f, Collider.AxisScales(m));
                    return Cast(B3.Proxy.Box, m.MultiplyPoint3x4(box.m_Center), half, Collider.AxisRotation(m), 0f, direction, maxDistance, layerMask, triggers, closest);
                }
                default:
                {
                    var bounds = c.bounds;
                    return Cast(B3.Proxy.Box, bounds.center, bounds.extents, Quaternion.identity, 0f, direction, maxDistance, layerMask, triggers, closest);
                }
            }
        }

        // ------------------------------------------------------------------ result helpers

        internal static bool First(List<RaycastHit> hits, out RaycastHit hit)
        {
            hit = hits.Count > 0 ? hits[0] : default;
            return hits.Count > 0;
        }

        internal static int Fill(List<RaycastHit> hits, RaycastHit[] results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            int n = Math.Min(hits.Count, results.Length);
            for (int i = 0; i < n; i++) results[i] = hits[i];
            return n;
        }

        internal static int Fill(List<Collider> colliders, Collider[] results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            int n = Math.Min(colliders.Count, results.Length);
            for (int i = 0; i < n; i++) results[i] = colliders[i];
            return n;
        }
    }
}
