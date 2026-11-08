using System;

namespace UnityEngine
{
    /// <summary>Gizmo drawing state and callback-scoped Game-view drawing.</summary>
    public sealed partial class Gizmos
    {
        static Color32 s_Color = new Color32(255, 255, 255, 255);
        static Matrix4x4 s_Matrix;
        static Texture s_Exposure;

        [Unbound.EditorFaithful("The 6000.0.71f1 Play-mode constructor has no observable effect (renderapi).")]
        public Gizmos() { }
        // The drawing color is stored as 8-bit channels, with half values rounded up.
        static byte Channel(float value) => (byte)(Mathf.Clamp01(value) * 255f + .5f);
        public static Color color
        {
            get => s_Color;
            set => s_Color = new Color32(Channel(value.r), Channel(value.g), Channel(value.b), Channel(value.a));
        }
        public static Matrix4x4 matrix { get => s_Matrix; set => s_Matrix = value; }
        public static Texture exposure { get => s_Exposure; set => s_Exposure = value; }
        public static float probeSize => Unbound.Runtime.Compat.GizmoProbeSize;

        static void UnsupportedDrawing()
        {
            if (!s_Drawing) OutsideCallback();
            throw new NotSupportedException("Unbound: this Game-view Gizmos drawing operation is not supported.");
        }

        static void OutsideCallback() => throw new ArgumentException("Gizmo drawing functions can only be used in OnDrawGizmos and OnDrawGizmosSelected.");

        public static void DrawLine(Vector3 from, Vector3 to) => Line(from, to);
        public static void DrawLineStrip(ReadOnlySpan<Vector3> points, bool looped)
        {
            if (!s_Drawing) OutsideCallback();
            for (int i = 1; i < points.Length; i++) Line(points[i-1], points[i]);
            if (looped && points.Length > 1) Line(points[points.Length-1], points[0]);
        }
        public static void DrawLineList(ReadOnlySpan<Vector3> points)
        {
            if ((points.Length & 1) != 0)
                throw new UnityException("You cannot draw a line list from an odd number of points, with two points per line the number of points must be even");
            if (!s_Drawing) OutsideCallback();
            for (int i = 0; i < points.Length; i += 2) Line(points[i], points[i+1]);
        }
        public static void DrawWireSphere(Vector3 center, float radius)
        {
            if (!s_Drawing) OutsideCallback();
            const int segments = 64;
            for (int axis = 0; axis < 3; axis++)
                for (int n = 0; n < segments; n++)
                {
                    float a = n * (Mathf.PI * 2 / segments), b = (n + 1) * (Mathf.PI * 2 / segments);
                    Vector3 Point(float angle) => axis switch
                    {
                        0 => new Vector3(0, Mathf.Cos(angle), Mathf.Sin(angle)),
                        1 => new Vector3(Mathf.Cos(angle), 0, Mathf.Sin(angle)),
                        _ => new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0),
                    };
                    Line(center + radius * Point(a), center + radius * Point(b));
                }
        }
        public static void DrawSphere(Vector3 center, float radius)
        {
            if (!s_Drawing) OutsideCallback();
            Draw(Sphere, Matrix4x4.TRS(center, Quaternion.identity, Vector3.one * (radius * 2)));
        }
        public static void DrawWireCube(Vector3 center, Vector3 size)
        {
            if (!s_Drawing) OutsideCallback();
            Draw(EdgeCube, Matrix4x4.TRS(center, Quaternion.identity, size), true);
        }
        public static void DrawCube(Vector3 center, Vector3 size)
        {
            if (!s_Drawing) OutsideCallback();
            Draw(Cube, Matrix4x4.TRS(center, Quaternion.identity, size));
        }
        public static void DrawMesh(Mesh mesh, int submeshIndex, Vector3 position, Quaternion rotation, Vector3 scale) => Draw(mesh, Matrix4x4.TRS(position, rotation, scale), false, submeshIndex);
        public static void DrawWireMesh(Mesh mesh, int submeshIndex, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            if (!s_Drawing) OutsideCallback();
            bool previous = GL.wireframe; GL.wireframe = true;
            try { Draw(mesh, Matrix4x4.TRS(position, rotation, scale), true, submeshIndex); }
            finally { GL.wireframe = previous; }
        }
        public static void DrawIcon(Vector3 center, string name, bool allowScaling, Color tint) => UnsupportedDrawing();
        public static void DrawGUITexture(Rect screenRect, Texture texture, int leftBorder, int rightBorder, int topBorder, int bottomBorder, Material mat) => UnsupportedDrawing();
        [Unbound.EditorFaithful("Outside gizmo callbacks DrawFrustum does nothing in the 6000.0.71f1 Play-mode capture (renderapi).")]
        public static void DrawFrustum(Vector3 center, float fov, float maxRange, float minRange, float aspect)
        {
            if (s_Drawing) UnsupportedDrawing();
        }
        [Unbound.EditorFaithful("Without a current gizmo view, the 6000.0.71f1 Play-mode capture answers NaN (renderapi).")]
        public static float CalculateLOD(Vector3 position, float radius) => float.NaN;
        public static void DrawRay(Ray r) => DrawLine(r.origin, r.origin + r.direction);
        public static void DrawRay(Vector3 from, Vector3 direction) => DrawLine(from, from + direction);
        public static void DrawMesh(Mesh mesh, Vector3 position, Quaternion rotation) => DrawMesh(mesh, -1, position, rotation, Vector3.one);
        public static void DrawMesh(Mesh mesh, Vector3 position) => DrawMesh(mesh, position, Quaternion.identity);
        public static void DrawMesh(Mesh mesh) => DrawMesh(mesh, Vector3.zero);
        public static void DrawMesh(Mesh mesh, Vector3 position, Quaternion rotation, Vector3 scale) => DrawMesh(mesh, -1, position, rotation, scale);
        public static void DrawMesh(Mesh mesh, int submeshIndex, Vector3 position, Quaternion rotation) => DrawMesh(mesh, submeshIndex, position, rotation, Vector3.one);
        public static void DrawMesh(Mesh mesh, int submeshIndex, Vector3 position) => DrawMesh(mesh, submeshIndex, position, Quaternion.identity);
        public static void DrawMesh(Mesh mesh, int submeshIndex) => DrawMesh(mesh, submeshIndex, Vector3.zero);
        public static void DrawWireMesh(Mesh mesh, Vector3 position, Quaternion rotation) => DrawWireMesh(mesh, -1, position, rotation, Vector3.one);
        public static void DrawWireMesh(Mesh mesh, Vector3 position) => DrawWireMesh(mesh, position, Quaternion.identity);
        public static void DrawWireMesh(Mesh mesh) => DrawWireMesh(mesh, Vector3.zero);
        public static void DrawWireMesh(Mesh mesh, Vector3 position, Quaternion rotation, Vector3 scale) => DrawWireMesh(mesh, -1, position, rotation, scale);
        public static void DrawWireMesh(Mesh mesh, int submeshIndex, Vector3 position, Quaternion rotation) => DrawWireMesh(mesh, submeshIndex, position, rotation, Vector3.one);
        public static void DrawWireMesh(Mesh mesh, int submeshIndex, Vector3 position) => DrawWireMesh(mesh, submeshIndex, position, Quaternion.identity);
        public static void DrawWireMesh(Mesh mesh, int submeshIndex) => DrawWireMesh(mesh, submeshIndex, Vector3.zero);
        public static void DrawIcon(Vector3 center, string name, bool allowScaling) => DrawIcon(center, name, allowScaling, Color.white);
        public static void DrawIcon(Vector3 center, string name) => DrawIcon(center, name, true);
        public static void DrawGUITexture(Rect screenRect, Texture texture) => DrawGUITexture(screenRect, texture, null);
        public static void DrawGUITexture(Rect screenRect, Texture texture, Material mat) => DrawGUITexture(screenRect, texture, 0, 0, 0, 0, mat);
        public static void DrawGUITexture(Rect screenRect, Texture texture, int leftBorder, int rightBorder, int topBorder, int bottomBorder) => DrawGUITexture(screenRect, texture, leftBorder, rightBorder, topBorder, bottomBorder, null);
    }
}
