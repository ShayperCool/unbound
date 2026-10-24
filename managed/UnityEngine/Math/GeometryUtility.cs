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

        public static Plane[] CalculateFrustumPlanes(Camera camera)
        {
            var planes = new Plane[6];
            CalculateFrustumPlanes(camera, planes);
            return planes;
        }

        public static Plane[] CalculateFrustumPlanes(Matrix4x4 worldToProjectionMatrix)
        {
            var planes = new Plane[6];
            CalculateFrustumPlanes(worldToProjectionMatrix, planes);
            return planes;
        }

        public static void CalculateFrustumPlanes(Camera camera, Plane[] planes)
        {
            if (camera == null) throw new ArgumentNullException(nameof(camera));
            CalculateFrustumPlanes(camera.projectionMatrix * camera.worldToCameraMatrix, planes);
        }

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

        /// <summary>The plane of a polygon: Newell's normal through the vertices' centroid; false for
        /// fewer than three vertices or a degenerate polygon. The normal's side is assumed (not
        /// measured): for a triangle a, b, c it is Cross(b - a, c - a), as <see cref="Plane.Set3Points"/>.</summary>
        public static bool TryCreatePlaneFromPolygon(Vector3[] vertices, out Plane plane)
        {
            plane = default;
            if (vertices == null || vertices.Length < 3) return false;
            var normal = Vector3.zero;
            var centroid = Vector3.zero;
            for (int i = 0; i < vertices.Length; i++)
            {
                var a = vertices[i];
                var b = vertices[(i + 1) % vertices.Length];
                normal.x += (a.y - b.y) * (a.z + b.z);
                normal.y += (a.z - b.z) * (a.x + b.x);
                normal.z += (a.x - b.x) * (a.y + b.y);
                centroid += a;
            }
            float length = normal.magnitude;
            if (length < 1e-12f || float.IsNaN(length)) return false;
            normal /= length;
            centroid /= vertices.Length;
            plane = new Plane { normal = normal, distance = -Vector3.Dot(normal, centroid) };
            return true;
        }
    }
}
