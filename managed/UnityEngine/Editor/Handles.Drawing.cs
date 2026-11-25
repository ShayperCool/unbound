using System;
using Unbound.Runtime;
using UnityEngine;

namespace UnityEditor
{
    /// <summary>
    /// The Handles drawing helpers. Unity's draw only in a repaint and read the current event first,
    /// so outside OnGUI they throw NullReferenceException and in other events they do nothing
    /// (measured: tests/projects/editorwindowgui handles.*, also with null, empty and short point
    /// and color arrays). Unbound draws no handles: in a repaint each reports itself ignored.
    /// </summary>
    public sealed partial class Handles
    {
        static void Draw(string member)
        {
            if (Event.current.type != EventType.Repaint) return;
            Stubs.Ignored("Handles." + member, "draws nothing (Unbound draws no handles)");
        }

        public static void DrawAAPolyLine(params Vector3[] points) => Draw(nameof(DrawAAPolyLine));
        public static void DrawAAPolyLine(float width, params Vector3[] points) => Draw(nameof(DrawAAPolyLine));
        // Unity's reads the texture's height for the width first, in every event.
        public static void DrawAAPolyLine(Texture2D lineTex, params Vector3[] points)
        {
            _ = lineTex.height / 2;
            Draw(nameof(DrawAAPolyLine));
        }
        public static void DrawAAPolyLine(float width, int actualNumberOfPoints, params Vector3[] points) => Draw(nameof(DrawAAPolyLine));
        public static void DrawAAPolyLine(Texture2D lineTex, float width, params Vector3[] points) => Draw(nameof(DrawAAPolyLine));
        public static void DrawAAPolyLine(Color[] colors, Vector3[] points) => Draw(nameof(DrawAAPolyLine));
        public static void DrawAAPolyLine(float width, Color[] colors, Vector3[] points) => Draw(nameof(DrawAAPolyLine));
        public static void DrawAAConvexPolygon(params Vector3[] points) => Draw(nameof(DrawAAConvexPolygon));
        public static void DrawPolyLine(params Vector3[] points) => Draw(nameof(DrawPolyLine));
        public static void DrawLines(Vector3[] lineSegments) => Draw(nameof(DrawLines));
        public static void DrawLines(Vector3[] points, int[] segmentIndices) => Draw(nameof(DrawLines));
        public static void DrawDottedLine(Vector3 p1, Vector3 p2, float screenSpaceSize) => Draw(nameof(DrawDottedLine));
        public static void DrawDottedLines(Vector3[] lineSegments, float screenSpaceSize) => Draw(nameof(DrawDottedLines));
        public static void DrawDottedLines(Vector3[] points, int[] segmentIndices, float screenSpaceSize) => Draw(nameof(DrawDottedLines));

        public static void DrawBezier(Vector3 startPosition, Vector3 endPosition, Vector3 startTangent, Vector3 endTangent, Color color, Texture2D texture, float width) =>
            Draw(nameof(DrawBezier));

        public static void DrawWireCube(Vector3 center, Vector3 size) => Draw(nameof(DrawWireCube));
        public static void DrawWireDisc(Vector3 center, Vector3 normal, float radius) => Draw(nameof(DrawWireDisc));
        public static void DrawWireDisc(Vector3 center, Vector3 normal, float radius, float thickness) => Draw(nameof(DrawWireDisc));
        public static void DrawWireArc(Vector3 center, Vector3 normal, Vector3 from, float angle, float radius) => Draw(nameof(DrawWireArc));
        public static void DrawWireArc(Vector3 center, Vector3 normal, Vector3 from, float angle, float radius, float thickness) => Draw(nameof(DrawWireArc));
        public static void DrawSolidDisc(Vector3 center, Vector3 normal, float radius) => Draw(nameof(DrawSolidDisc));
        public static void DrawSolidArc(Vector3 center, Vector3 normal, Vector3 from, float angle, float radius) => Draw(nameof(DrawSolidArc));
        public static void DrawSolidRectangleWithOutline(Rect rectangle, Color faceColor, Color outlineColor) => Draw(nameof(DrawSolidRectangleWithOutline));
        public static void DrawSolidRectangleWithOutline(Vector3[] verts, Color faceColor, Color outlineColor) => Draw(nameof(DrawSolidRectangleWithOutline));

        /// <summary>Labels are GUI calls: outside OnGUI they throw ArgumentException (measured:
        /// tests/projects/editorwindowgui handles.outsideGui.labelAndRepaint); Unbound draws none.</summary>
        static void DrawLabel()
        {
            if (Event.current == null) throw new ArgumentException("You can only call GUI functions from inside OnGUI.");
            if (Event.current.type == EventType.Repaint) Stubs.Ignored("Handles.Label", "draws nothing (Unbound draws no handles)");
        }

        public static void Label(Vector3 position, Texture image) => DrawLabel();
        public static void Label(Vector3 position, GUIContent content) => DrawLabel();
        public static void Label(Vector3 position, string text, GUIStyle style) => DrawLabel();
        public static void Label(Vector3 position, GUIContent content, GUIStyle style) => DrawLabel();
    }

    public sealed partial class HandleUtility
    {
        /// <summary>Repaints the current view; also outside OnGUI it does not throw (measured:
        /// tests/projects/editorwindowgui handles.*.labelAndRepaint).</summary>
        [Unbound.EditorFaithful("Unbound draws no editor view to repaint")]
        public static void Repaint() { }
    }
}
