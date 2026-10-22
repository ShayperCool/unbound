using System;
using System.Runtime.InteropServices;

namespace UnityEngine
{
    /// <summary>
    /// A plane: the points p with Dot(normal, p) + distance == 0. The normal is kept normalized.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    public partial struct Plane : IFormattable
    {
        private Vector3 m_Normal;
        private float m_Distance;

        public Vector3 normal
        {
            get => m_Normal;
            set => m_Normal = value;
        }

        public float distance
        {
            get => m_Distance;
            set => m_Distance = value;
        }

        public Plane(Vector3 inNormal, Vector3 inPoint)
        {
            m_Normal = Vector3.Normalize(inNormal);
            m_Distance = -Vector3.Dot(m_Normal, inPoint);
        }

        public Plane(Vector3 inNormal, float d)
        {
            m_Normal = Vector3.Normalize(inNormal);
            m_Distance = d;
        }

        /// <summary>The plane through three points; the normal follows the clockwise winding a, b, c.</summary>
        public Plane(Vector3 a, Vector3 b, Vector3 c)
        {
            m_Normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            m_Distance = -Vector3.Dot(m_Normal, a);
        }

        public void SetNormalAndPosition(Vector3 inNormal, Vector3 inPoint)
        {
            m_Normal = Vector3.Normalize(inNormal);
            m_Distance = -Vector3.Dot(m_Normal, inPoint);
        }

        public void Set3Points(Vector3 a, Vector3 b, Vector3 c)
        {
            m_Normal = Vector3.Normalize(Vector3.Cross(b - a, c - a));
            m_Distance = -Vector3.Dot(m_Normal, a);
        }

        public void Flip()
        {
            m_Normal = -m_Normal;
            m_Distance = -m_Distance;
        }

        public Plane flipped => new Plane(-m_Normal, -m_Distance);

        public void Translate(Vector3 translation) => m_Distance += Vector3.Dot(m_Normal, translation);

        public static Plane Translate(Plane plane, Vector3 translation) =>
            new Plane(plane.m_Normal, plane.m_Distance + Vector3.Dot(plane.m_Normal, translation));

        public Vector3 ClosestPointOnPlane(Vector3 point)
        {
            float pointToPlaneDistance = Vector3.Dot(m_Normal, point) + m_Distance;
            return point - m_Normal * pointToPlaneDistance;
        }

        /// <summary>Signed distance from the plane to the point (positive on the normal's side).</summary>
        public float GetDistanceToPoint(Vector3 point) => Vector3.Dot(m_Normal, point) + m_Distance;

        public bool GetSide(Vector3 point) => Vector3.Dot(m_Normal, point) + m_Distance > 0.0F;

        public bool SameSide(Vector3 inPt0, Vector3 inPt1)
        {
            float d0 = GetDistanceToPoint(inPt0);
            float d1 = GetDistanceToPoint(inPt1);
            return (d0 > 0.0f && d1 > 0.0f) || (d0 <= 0.0f && d1 <= 0.0f);
        }

        /// <summary>
        /// Intersects a ray with the plane. Returns false if the ray is parallel to the plane
        /// (<paramref name="enter"/> = 0) or points away from it (<paramref name="enter"/> is the
        /// negative distance behind the origin).
        /// </summary>
        public bool Raycast(Ray ray, out float enter)
        {
            float vdot = Vector3.Dot(ray.direction, m_Normal);
            float ndot = -Vector3.Dot(ray.origin, m_Normal) - m_Distance;

            if (Mathf.Approximately(vdot, 0.0f))
            {
                enter = 0.0F;
                return false;
            }

            enter = ndot / vdot;
            return enter > 0.0F;
        }

        public override string ToString() => ToString(null, null);

        public string ToString(string format) => ToString(format, null);

        public string ToString(string format, IFormatProvider formatProvider)
        {
            format = MathFormat.Format(format, "F2");
            formatProvider = MathFormat.Provider(formatProvider);
            return "(normal:(" + m_Normal.x.ToString(format, formatProvider) + ", " + m_Normal.y.ToString(format, formatProvider) +
                   ", " + m_Normal.z.ToString(format, formatProvider) + "), distance:" + m_Distance.ToString(format, formatProvider) + ")";
        }
    }
}
