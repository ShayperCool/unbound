using System;

namespace UnityEngine
{
    /// <summary>Play-mode state and argument checks outside gizmo callbacks. Unbound has no
    /// Game-view gizmo toggle and does not send the Editor's gizmo repaint callbacks.</summary>
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

        static void OutsideCallback() => throw new ArgumentException("Gizmo drawing functions can only be used in OnDrawGizmos and OnDrawGizmosSelected.");

        public static void DrawLine(Vector3 from, Vector3 to) => OutsideCallback();
        public static void DrawLineStrip(ReadOnlySpan<Vector3> points, bool looped) => OutsideCallback();
        public static void DrawLineList(ReadOnlySpan<Vector3> points)
        {
            if ((points.Length & 1) != 0)
                throw new UnityException("You cannot draw a line list from an odd number of points, with two points per line the number of points must be even");
            OutsideCallback();
        }
        public static void DrawWireSphere(Vector3 center, float radius) => OutsideCallback();
        public static void DrawSphere(Vector3 center, float radius) => OutsideCallback();
        public static void DrawWireCube(Vector3 center, Vector3 size) => OutsideCallback();
        public static void DrawCube(Vector3 center, Vector3 size) => OutsideCallback();
        public static void DrawMesh(Mesh mesh, int submeshIndex, Vector3 position, Quaternion rotation, Vector3 scale) => OutsideCallback();
        public static void DrawWireMesh(Mesh mesh, int submeshIndex, Vector3 position, Quaternion rotation, Vector3 scale) => OutsideCallback();
        public static void DrawIcon(Vector3 center, string name, bool allowScaling, Color tint) => OutsideCallback();
        public static void DrawGUITexture(Rect screenRect, Texture texture, int leftBorder, int rightBorder, int topBorder, int bottomBorder, Material mat) => OutsideCallback();
        [Unbound.EditorFaithful("Outside gizmo callbacks DrawFrustum does nothing in the 6000.0.71f1 Play-mode capture (renderapi).")]
        public static void DrawFrustum(Vector3 center, float fov, float maxRange, float minRange, float aspect) { }
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
