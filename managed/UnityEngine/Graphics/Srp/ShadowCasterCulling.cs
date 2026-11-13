using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace Unbound.Runtime
{
    /// <summary>
    /// A scriptable pipeline's light culling and per-light shadow casters, as the Editor gives them
    /// (measured, tests/projects/droppedparams shadow.*).
    /// <list type="bullet">
    /// <item>A point light is visible when its range sphere touches the camera's frustum, a spot
    /// light when its cone does (a spot whose sphere reaches into the view but whose cone points
    /// away is not visible); directional lights always are.</item>
    /// <item>A light's shadow casters are the renderers the camera's mask lets through that cast
    /// shadows (ShadowsOnly too), are on a layer of the light's culling mask, touch the light's
    /// volume (a point light's sphere, a spot light's cone) and can shade what the camera sees
    /// within the shadow distance: they lie in that frustum swept toward the light (along its
    /// direction for a directional light, toward its position for a local one).</item>
    /// <item>A light without shadows, or a local light whose volume does not reach the shadow
    /// distance's frustum, has none. The bounds are the union of the casters' world bounds.</item>
    /// </list>
    /// A spot's cone is taken as the square pyramid of its shadow projection.
    /// </summary>
    internal static class ShadowCasterCulling
    {
        /// <summary>Whether a light lights anything inside the camera's culling planes.</summary>
        internal static bool LightVisible(Light light, ref ScriptableCullingParameters parameters)
        {
            var planes = new Plane[parameters.cullingPlaneCount];
            for (int i = 0; i < planes.Length; i++) planes[i] = parameters.GetCullingPlane(i);
            switch (light.type)
            {
                case LightType.Point:
                    return !SphereOutside(planes, light.transform.position, light.range);
                case LightType.Spot:
                    return !PointsOutside(planes, SpotPoints(light));
                default:
                    return true;
            }
        }

        /// <summary>CullingResults.GetShadowCasterBounds for one visible light.</summary>
        internal static bool CasterBounds(CullingData data, VisibleLight visible, out Bounds bounds)
        {
            bounds = default;
            var light = visible.light;
            if (data == null || data.Camera == null || light == null || light.shadows == LightShadows.None) return false;
            if (!ShadowFrustum(data, out var corners)) return false;
            var faces = FacePlanes(corners, out var center);

            Plane[] volume = null;
            Vector3 position = light.transform.position;
            switch (light.type)
            {
                case LightType.Point:
                    if (SphereOutside(faces, position, light.range)) return false;
                    break;
                case LightType.Spot:
                    var points = SpotPoints(light);
                    if (PointsOutside(faces, points)) return false;
                    volume = PyramidPlanes(points);
                    break;
                case LightType.Directional:
                    break;
                default:
                    return false;
            }
            var hull = light.type == LightType.Directional
                ? SweptPlanes(corners, faces, center, light.transform.forward, false)
                : SweptPlanes(corners, faces, center, position, true);

            bool any = false;
            int mask = light.cullingMask;
            foreach (var renderer in data.Renderers)
            {
                if (!renderer || renderer.shadowCastingMode == ShadowCastingMode.Off) continue;
                if ((mask & (1 << renderer.gameObject.layer)) == 0) continue;
                var b = renderer.bounds;
                if (light.type == LightType.Point && b.SqrDistanceUnchecked(position) > light.range * light.range) continue;
                if (volume != null && BoxOutside(volume, b)) continue;
                if (BoxOutside(hull, b)) continue;
                if (!any) { bounds = b; any = true; }
                else bounds.Encapsulate(b);
            }
            return any;
        }

        /// <summary>The camera's culling frustum cut at the shadow distance: near corners
        /// (-x-y, +x-y, +x+y, -x+y), then the far ones.</summary>
        static bool ShadowFrustum(CullingData data, out Vector3[] corners)
        {
            corners = new Vector3[8];
            var camera = data.Camera;
            float near = camera.nearClipPlane;
            float distance = Mathf.Min(data.Parameters.shadowDistance, camera.farClipPlane);
            if (!(distance > near)) return false;
            var inverse = data.Parameters.cullingMatrix.inverse;
            var view = camera.worldToCameraMatrix;
            for (int i = 0; i < 4; i++)
            {
                float x = i == 1 || i == 2 ? 1f : -1f, y = i >= 2 ? 1f : -1f;
                var a = inverse.MultiplyPoint(new Vector3(x, y, -1f));
                var b = inverse.MultiplyPoint(new Vector3(x, y, 1f));
                float da = -view.MultiplyPoint(a).z, db = -view.MultiplyPoint(b).z;
                float t = db > da ? Mathf.Clamp01((distance - da) / (db - da)) : 1f;
                corners[i] = a;
                corners[i + 4] = Vector3.Lerp(a, b, t);
            }
            return true;
        }

        static readonly int[][] s_Faces =
        {
            new[] { 0, 1, 2, 3 }, new[] { 4, 5, 6, 7 }, new[] { 0, 1, 5, 4 },
            new[] { 3, 2, 6, 7 }, new[] { 0, 3, 7, 4 }, new[] { 1, 2, 6, 5 },
        };

        /// <summary>The frustum's edges: their two corners and the two faces meeting there, 4 bits
        /// each (see <see cref="SweepTowardLight"/>).</summary>
        static readonly int[] s_Edges =
        {
            0x2010, 0x5021, 0x3032, 0x4003, 0x2154, 0x5165, 0x3176, 0x4147, 0x4240, 0x5251, 0x3562, 0x4373,
        };

        /// <summary>The frustum's six faces as planes facing inward.</summary>
        static Plane[] FacePlanes(Vector3[] corners, out Vector3 center)
        {
            center = Vector3.zero;
            foreach (var c in corners) center += c;
            center /= corners.Length;
            var planes = new Plane[6];
            for (int f = 0; f < 6; f++)
            {
                var q = s_Faces[f];
                // A face's widest triangle: the near face of a narrow frustum is tiny.
                planes[f] = Inward(corners[q[0]], corners[q[1]], corners[q[2]], center) ?? Inward(corners[q[0]], corners[q[2]], corners[q[3]], center) ?? new Plane(Vector3.zero, 0f);
            }
            return planes;
        }

        static Plane[] SweptPlanes(Vector3[] corners, Plane[] faces, Vector3 center, Vector3 light, bool positional)
        {
            Span<Plane> planes = stackalloc Plane[6 + 12];
            return planes.Slice(0, SweepTowardLight(faces, corners, s_Edges, center, light, positional, planes)).ToArray();
        }

        /// <summary>
        /// The planes of a frustum swept toward a light, into <paramref name="result"/> (as many as
        /// fit; the count is returned): the faces turned away from the light (a directional light's
        /// direction of travel, or a local light's position: <paramref name="positional"/>), in
        /// their order, then for each edge between such a face and one turned toward it, in the
        /// edges' order, the plane through the edge along the light, facing the frustum's middle
        /// (none for an edge along the light). An edge is its two corners and its two faces, 4
        /// bits each from the lowest. The cascades' caster culling planes and the shadow casters'
        /// bounds take it.
        /// </summary>
        internal static int SweepTowardLight(ReadOnlySpan<Plane> faces, ReadOnlySpan<Vector3> corners, ReadOnlySpan<int> edges, Vector3 middle,
            Vector3 light, bool positional, Span<Plane> result)
        {
            Span<bool> away = stackalloc bool[6];
            int count = 0;
            for (int f = 0; f < faces.Length; f++)
            {
                away[f] = positional ? faces[f].GetDistanceToPoint(light) > 0f : Vector3.Dot(faces[f].normal, light) < 0f;
                if (away[f] && count < result.Length) result[count++] = faces[f];
            }
            for (int e = 0; e < edges.Length && count < result.Length; e++)
            {
                int edge = edges[e];
                if (away[(edge >> 8) & 0xf] == away[(edge >> 12) & 0xf]) continue;
                var a = corners[edge & 0xf];
                var normal = Vector3.Cross(corners[(edge >> 4) & 0xf] - a, positional ? light - a : light);
                float length = normal.sqrMagnitude;
                if (length <= 1.1920929e-07f) continue;
                normal *= 1f / Mathf.Sqrt(length);
                if (Vector3.Dot(middle - a, normal) < 0f) normal = -normal;
                result[count++] = new Plane(normal, -Vector3.Dot(normal, a));
            }
            return count;
        }

        /// <summary>A spot light's cone as a square pyramid: its apex, then the far corners.</summary>
        static Vector3[] SpotPoints(Light light)
        {
            var t = light.transform;
            Vector3 apex = t.position, forward = t.forward, right = t.right, up = t.up;
            float range = light.range;
            float half = range * Mathf.Tan(light.spotAngle * 0.5f * Mathf.Deg2Rad);
            var far = apex + forward * range;
            return new[]
            {
                apex,
                far - right * half - up * half, far + right * half - up * half,
                far + right * half + up * half, far - right * half + up * half,
            };
        }

        static Plane[] PyramidPlanes(Vector3[] p)
        {
            var center = (p[0] + p[1] + p[2] + p[3] + p[4]) / 5f;
            var planes = new List<Plane>();
            for (int i = 0; i < 4; i++)
                if (Inward(p[0], p[1 + i], p[1 + (i + 1) % 4], center) is Plane side) planes.Add(side);
            if (Inward(p[1], p[2], p[3], center) is Plane far) planes.Add(far);
            return planes.ToArray();
        }

        static Plane? Inward(Vector3 a, Vector3 b, Vector3 c, Vector3 inside)
        {
            var normal = Vector3.Cross(b - a, c - a);
            if (normal.sqrMagnitude < 1e-12f) return null;
            var plane = new Plane(normal.normalized, a);
            return plane.GetDistanceToPoint(inside) < 0f ? plane.flipped : plane;
        }

        static bool SphereOutside(Plane[] planes, Vector3 center, float radius)
        {
            foreach (var plane in planes)
                if (plane.GetDistanceToPoint(center) < -radius) return true;
            return false;
        }

        static bool PointsOutside(Plane[] planes, Vector3[] points)
        {
            foreach (var plane in planes)
            {
                bool all = true;
                foreach (var p in points)
                    if (plane.GetDistanceToPoint(p) >= 0f) { all = false; break; }
                if (all) return true;
            }
            return false;
        }

        static bool BoxOutside(Plane[] planes, Bounds bounds)
        {
            var c = bounds.center;
            var e = bounds.extents;
            foreach (var plane in planes)
            {
                var n = plane.normal;
                float radius = Mathf.Abs(n.x) * e.x + Mathf.Abs(n.y) * e.y + Mathf.Abs(n.z) * e.z;
                if (plane.GetDistanceToPoint(c) < -radius) return true;
            }
            return false;
        }
    }
}
