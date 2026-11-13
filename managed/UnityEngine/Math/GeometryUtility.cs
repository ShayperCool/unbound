using System;

namespace UnityEngine
{
    /// <summary>
    /// Frustum planes, bounds of points and polygon planes. Frustum planes are extracted from the
    /// rows of a world-to-projection matrix in Unity's order (left, right, down, up, near, far),
    /// normalized, with normals pointing into the frustum (measured in tests/projects/urp,
    /// local-shadow-matrices.jsonl: the shadow culling planes are these).
    /// </summary>
    public sealed partial class GeometryUtility
    {
        public GeometryUtility() { }

        public static Plane[] CalculateFrustumPlanes(Matrix4x4 worldToProjectionMatrix)
        {
            var planes = new Plane[6];
            CalculateFrustumPlanes(worldToProjectionMatrix, planes);
            return planes;
        }

#if !UNBOUND_SCRIPT_MATH // the Camera overloads use the engine's math types
        public static void CalculateFrustumPlanes(Camera camera, Plane[] planes)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            CalculateFrustumPlanes(camera.projectionMatrix * camera.worldToCameraMatrix, planes);
        }

        public static Plane[] CalculateFrustumPlanes(Camera camera)
        {
            var planes = new Plane[6];
            CalculateFrustumPlanes(camera, planes);
            return planes;
        }
#endif

        public static void CalculateFrustumPlanes(Matrix4x4 worldToProjectionMatrix, Plane[] planes)
        {
            if (planes == null) throw new ArgumentNullException(nameof(planes));
            if (planes.Length != 6) throw new ArgumentException("Planes array must be of length 6.", nameof(planes));
            var m = worldToProjectionMatrix;
            var w = m.GetRow(3);
            for (int i = 0; i < 3; i++)
            {
                var row = m.GetRow(i);
                planes[2 * i] = FromRow(w + row);
                planes[2 * i + 1] = FromRow(w - row);
            }
        }

        static Plane FromRow(Vector4 v)
        {
            var normal = new Vector3(v.x, v.y, v.z);
            float length = normal.magnitude;
            if (length == 0f) return new Plane { normal = Vector3.zero, distance = v.w };
            return new Plane { normal = normal / length, distance = v.w / length };
        }

        /// <summary>Whether the box is inside every plane or crosses it (false as soon as it lies
        /// wholly behind one).</summary>
        public static bool TestPlanesAABB(Plane[] planes, Bounds bounds)
        {
            if (planes == null) throw new ArgumentNullException(nameof(planes));
            var center = bounds.center;
            var extents = bounds.extents;
            foreach (var plane in planes)
            {
                var n = plane.normal;
                float radius = Mathf.Abs(n.x) * extents.x + Mathf.Abs(n.y) * extents.y + Mathf.Abs(n.z) * extents.z;
                if (Vector3.Dot(n, center) + plane.distance + radius < 0f) return false;
            }
            return true;
        }

        public static Bounds CalculateBounds(Vector3[] positions, Matrix4x4 transform)
        {
            if (positions == null) throw new ArgumentNullException(nameof(positions));
            if (positions.Length == 0) throw new ArgumentException("Zero-sized array is not allowed.", nameof(positions));
            var first = transform.MultiplyPoint3x4(positions[0]);
            var bounds = new Bounds(first, Vector3.zero);
            for (int i = 1; i < positions.Length; i++) bounds.Encapsulate(transform.MultiplyPoint3x4(positions[i]));
            return bounds;
        }

        /// <summary>
        /// A plane through a polygon: fewer than three vertices give no plane (up, through the origin);
        /// three, the plane of the triangle; more, the normal of Newell's method (the sum over the edges
        /// of the cross terms), normalized, at the vertices' mean distance. True when the normal is not
        /// zero.
        /// </summary>
        public static bool TryCreatePlaneFromPolygon(Vector3[] vertices, out Plane plane)
        {
            if (vertices == null || vertices.Length < 3)
            {
                plane = new Plane(Vector3.up, 0f);
                return false;
            }
            if (vertices.Length == 3)
            {
                Vector3 a = vertices[0];
                Vector3 b = vertices[1];
                Vector3 c = vertices[2];
                plane = new Plane(a, b, c);
                return plane.normal.sqrMagnitude > 0f;
            }
            Vector3 normal = Vector3.zero;
            int last = vertices.Length - 1;
            Vector3 previous = vertices[last];
            float distance;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 current = vertices[i];
                normal.x = normal.x + (previous.y - current.y) * (previous.z + current.z);
                normal.y = normal.y + (previous.z - current.z) * (previous.x + current.x);
                normal.z = normal.z + (previous.x - current.x) * (previous.y + current.y);
                previous = current;
            }
            normal.Normalize();
            distance = 0f;
            for (int i = 0; i < vertices.Length; i++)
            {
                Vector3 vertex = vertices[i];
                distance -= Vector3.Dot(normal, vertex);
            }
            distance /= vertices.Length;
            plane = new Plane(normal, distance);
            return plane.normal.sqrMagnitude > 0f;
        }
    }
}
