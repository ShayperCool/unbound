using System;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>An axis-aligned bounding box, stored as a center and extents (half the size).</summary>
    [StructLayout(LayoutKind.Sequential)]
    public partial struct Bounds : IEquatable<Bounds>, IFormattable
    {
        private Vector3 m_Center;
        private Vector3 m_Extents;

        public Bounds(Vector3 center, Vector3 size)
        {
            m_Center = center;
            m_Extents = size * 0.5F;
        }

        public override int GetHashCode() => center.GetHashCode() ^ (extents.GetHashCode() << 2);

        public override bool Equals(object other) => other is Bounds b && Equals(b);

        public bool Equals(Bounds other) => center.Equals(other.center) && extents.Equals(other.extents);

        public Vector3 center
        {
            get => m_Center;
            set => m_Center = value;
        }

        public Vector3 size
        {
            get => m_Extents * 2.0F;
            set => m_Extents = value * 0.5F;
        }

        public Vector3 extents
        {
            get => m_Extents;
            set => m_Extents = value;
        }

        public Vector3 min
        {
            get => center - extents;
            set => SetMinMax(value, max);
        }

        public Vector3 max
        {
            get => center + extents;
            set => SetMinMax(min, value);
        }

        public static bool operator ==(Bounds lhs, Bounds rhs) => lhs.center == rhs.center && lhs.extents == rhs.extents;

        public static bool operator !=(Bounds lhs, Bounds rhs) => !(lhs == rhs);

        public void SetMinMax(Vector3 min, Vector3 max)
        {
            extents = (max - min) * 0.5F;
            center = min + extents;
        }

        public void Encapsulate(Vector3 point) => SetMinMax(Vector3.Min(min, point), Vector3.Max(max, point));

        public void Encapsulate(Bounds bounds)
        {
            Encapsulate(bounds.center - bounds.extents);
            Encapsulate(bounds.center + bounds.extents);
        }

        public void Expand(float amount)
        {
            amount *= .5f;
            extents += new Vector3(amount, amount, amount);
        }

        public void Expand(Vector3 amount) => extents += amount * .5f;

        /// <summary>True if the boxes overlap or touch.</summary>
        public bool Intersects(Bounds bounds)
        {
            Vector3 aMin = min, aMax = max, bMin = bounds.min, bMax = bounds.max;
            return aMin.x <= bMax.x && aMax.x >= bMin.x &&
                   aMin.y <= bMax.y && aMax.y >= bMin.y &&
                   aMin.z <= bMax.z && aMax.z >= bMin.z;
        }

        public bool IntersectRay(Ray ray) => IntersectRayAABB(ray, this, out _);

        /// <summary>
        /// Ray/box test. <paramref name="distance"/> receives the distance along the ray to where it
        /// enters the box (negative when the ray starts inside).
        /// </summary>
        public bool IntersectRay(Ray ray, out float distance) => IntersectRayAABB(ray, this, out distance);

        // Contains, SqrDistance and ClosestPoint compare and round in the order of the Unity
        // 6000.0.71f1 Editor (tests/projects/floatrounding, native/boundsPoint).

        /// <summary>True if the point is inside or on the surface of the box.</summary>
        public bool Contains(Vector3 point)
        {
            float x = m_Center.x, y = m_Center.y, z = m_Center.z;
            float ex = m_Extents.x, ey = m_Extents.y, ez = m_Extents.z;
            if (point.x < x - ex || point.x > x + ex || point.y < y - ey || point.y > y + ey || point.z < z - ez)
                return false;
            return !(point.z > z + ez);
        }

        /// <summary>Squared distance from the point to the box; 0 inside.</summary>
        public float SqrDistance(Vector3 point)
        {
            float dx = MathF.Max(MathF.Abs(point.x - m_Center.x), m_Extents.x) - m_Extents.x;
            float dy = MathF.Max(MathF.Abs(point.y - m_Center.y), m_Extents.y) - m_Extents.y;
            float dz = MathF.Max(MathF.Abs(point.z - m_Center.z), m_Extents.z) - m_Extents.z;
            return dx * dx + dy * dy + dz * dz;
        }

        /// <summary>The closest point on or in the box; a point inside is returned unchanged.</summary>
        public Vector3 ClosestPoint(Vector3 point)
        {
            float squared = 0F;
            float x = ClosestOffset(point.x - m_Center.x, m_Extents.x, ref squared);
            float y = ClosestOffset(point.y - m_Center.y, m_Extents.y, ref squared);
            float z = ClosestOffset(point.z - m_Center.z, m_Extents.z, ref squared);
            if (squared == 0F)
                return point;
            return new Vector3(m_Center.x + x, m_Center.y + y, m_Center.z + z);
        }

        // The offset from the centre clamped to [-extent, extent], adding the squared overshoot.
        private static float ClosestOffset(float offset, float extent, ref float squared)
        {
            if (offset < -extent)
            {
                float d = offset + extent;
                squared += d * d;
                return -extent;
            }
            if (offset <= extent)
                return offset;
            float over = offset - extent;
            squared += over * over;
            return extent;
        }

        // Slab test against the three axis-aligned slab pairs.
        private static bool IntersectRayAABB(Ray ray, Bounds bounds, out float dist)
        {
            float tMin = float.NegativeInfinity;
            float tMax = float.PositiveInfinity;
            Vector3 origin = ray.origin;
            Vector3 dir = ray.direction;
            Vector3 lo = bounds.min, hi = bounds.max;
            for (int i = 0; i < 3; i++)
            {
                float o = origin[i];
                float d = dir[i];
                if (Math.Abs(d) < 1e-20f)
                {
                    if (o < lo[i] || o > hi[i])
                    {
                        dist = 0F;
                        return false;
                    }
                    continue;
                }
                float inv = 1F / d;
                float t1 = (lo[i] - o) * inv;
                float t2 = (hi[i] - o) * inv;
                if (t1 > t2)
                {
                    float tmp = t1;
                    t1 = t2;
                    t2 = tmp;
                }
                if (t1 > tMin)
                    tMin = t1;
                if (t2 < tMax)
                    tMax = t2;
                if (tMin > tMax)
                {
                    dist = 0F;
                    return false;
                }
            }
            if (tMax < 0F)
            {
                dist = 0F;
                return false;
            }
            dist = tMin;
            return true;
        }

        public override string ToString() => ToString(null, null);

        public string ToString(string format) => ToString(format, null);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            format = MathFormat.Format(format, "F2");
            formatProvider = MathFormat.Provider(formatProvider);
            return "Center: " + m_Center.ToString(format, formatProvider) + ", Extents: " + m_Extents.ToString(format, formatProvider);
        }
    }
}
