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

        /// <summary>True if the point is inside or on the surface of the box.</summary>
        public bool Contains(Vector3 point)
        {
            Vector3 lo = min, hi = max;
            return point.x >= lo.x && point.x <= hi.x &&
                   point.y >= lo.y && point.y <= hi.y &&
                   point.z >= lo.z && point.z <= hi.z;
        }

        /// <summary>Squared distance from the point to the box; 0 inside.</summary>
        public float SqrDistance(Vector3 point)
        {
            float sqr = 0F;
            for (int i = 0; i < 3; i++)
            {
                float lo = m_Center[i] - m_Extents[i];
                float hi = m_Center[i] + m_Extents[i];
                float p = point[i];
                if (p < lo)
                {
                    float d = lo - p;
                    sqr += d * d;
                }
                else if (p > hi)
                {
                    float d = p - hi;
                    sqr += d * d;
                }
            }
            return sqr;
        }

        /// <summary>The closest point on or in the box; a point inside is returned unchanged.</summary>
        public Vector3 ClosestPoint(Vector3 point)
        {
            Vector3 lo = min, hi = max;
            return new Vector3(Mathf.Clamp(point.x, lo.x, hi.x), Mathf.Clamp(point.y, lo.y, hi.y), Mathf.Clamp(point.z, lo.z, hi.z));
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
