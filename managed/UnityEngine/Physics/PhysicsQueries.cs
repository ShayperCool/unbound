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

        static B3.Hit[] s_Hits = new B3.Hit[64];
        static ulong[] s_Shapes = new ulong[64];

        internal static float Length(float maxDistance) =>
            float.IsNaN(maxDistance) || maxDistance > MaxLength ? MaxLength : Mathf.Max(0f, maxDistance);

        static int HitTriggers(QueryTriggerInteraction q) => q switch
        {
            QueryTriggerInteraction.Ignore => 0,
            QueryTriggerInteraction.Collide => 1,
            _ => Physics.s_QueriesHitTriggers ? 1 : 0,
        };

        static ulong Mask(int layerMask) => unchecked((uint)layerMask);

        internal static RaycastHit ToHit(B3.Hit hit, Collider collider, float length) => new RaycastHit
        {
            m_Point = hit.Point,
            m_Normal = hit.Normal,
            m_Distance = hit.Fraction * length,
            m_FaceID = unchecked((uint)Math.Max(0, hit.TriangleIndex)),
            m_ColliderRef = collider,
            m_Collider = collider != null ? collider.GetInstanceID() : 0,
        };

        /// <summary>The world, ready for a query; 0 when there is nothing to hit.</summary>
        static uint ReadyWorld()
        {
            if (W.Colliders.Count == 0) return 0;
            W.PrepareQuery();
            return W.World;
        }

        delegate int HitQuery(B3.Hit* hits, int capacity);

        static List<RaycastHit> Collect(HitQuery query, float length, Vector3 direction, bool closest)
        {
            var result = new List<RaycastHit>();
            int count;
            while (true)
            {
                fixed (B3.Hit* hits = s_Hits)
                    count = query(hits, s_Hits.Length);
                if (count <= s_Hits.Length) break;
                s_Hits = new B3.Hit[count * 2];
            }
            for (int i = 0; i < count; i++)
            {
                var collider = W.ColliderOf(s_Hits[i].Shape);
                if (collider == null) continue;
                var hit = ToHit(s_Hits[i], collider, length);
                if (s_Hits[i].Fraction <= 0f)
                {
                    // Unity's casts that start overlapping a collider report it at distance 0
                    // without a point.
                    hit.m_Point = Vector3.zero;
                    hit.m_Normal = -direction;
                    hit.m_Distance = 0f;
                }
                result.Add(hit);
            }
            if (closest && result.Count > 1) result.RemoveRange(1, result.Count - 1);
            return result;
        }

        internal static List<RaycastHit> Raycast(Vector3 origin, Vector3 direction, float maxDistance, int layerMask, QueryTriggerInteraction triggers, bool closest)
        {
            uint world = ReadyWorld();
            if (world == 0 || direction.sqrMagnitude < 1e-12f) return new List<RaycastHit>();
            var dir = direction.normalized;
            float length = Length(maxDistance);
            var translation = dir * length;
            int hitTriggers = HitTriggers(triggers);
            return Collect((hits, capacity) => B3.ua3_raycast(world, origin, translation, Mask(layerMask), hitTriggers, closest ? 1 : 0, hits, capacity),
                length, dir, closest);
        }

        internal static List<RaycastHit> Cast(B3.Proxy kind, Vector3 a, Vector3 b, Quaternion rotation, float radius, Vector3 direction, float maxDistance, int layerMask, QueryTriggerInteraction triggers, bool closest)
        {
            uint world = ReadyWorld();
            if (world == 0 || direction.sqrMagnitude < 1e-12f) return new List<RaycastHit>();
            var dir = direction.normalized;
            float length = Length(maxDistance);
            var translation = dir * length;
            int hitTriggers = HitTriggers(triggers);
            var q = Quaternion.Normalize(rotation);
            return Collect((hits, capacity) => B3.ua3_shapecast(world, (int)kind, a, b, q, Mathf.Max(0f, radius), translation, Mask(layerMask), hitTriggers, closest ? 1 : 0, hits, capacity),
                length, dir, closest);
        }

        internal static List<Collider> Overlap(B3.Proxy kind, Vector3 a, Vector3 b, Quaternion rotation, float radius, int layerMask, QueryTriggerInteraction triggers)
        {
            var result = new List<Collider>();
            uint world = ReadyWorld();
            if (world == 0) return result;
            int hitTriggers = HitTriggers(triggers);
            var q = Quaternion.Normalize(rotation);
            int count;
            while (true)
            {
                fixed (ulong* shapes = s_Shapes)
                    count = B3.ua3_overlap(world, (int)kind, a, b, q, Mathf.Max(0f, radius), Mask(layerMask), hitTriggers, shapes, s_Shapes.Length);
                if (count <= s_Shapes.Length) break;
                s_Shapes = new ulong[count * 2];
            }
            for (int i = 0; i < count; i++)
            {
                var collider = W.ColliderOf(s_Shapes[i]);
                if (collider != null) result.Add(collider);
            }
            return result;
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
