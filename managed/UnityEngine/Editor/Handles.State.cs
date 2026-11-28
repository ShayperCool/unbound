using UnityEngine;
using UnityEngine.Rendering;
using Unbound.Runtime;

namespace UnityEditor
{
    public sealed partial class Handles
    {
        static CompareFunction s_ZTest = CompareFunction.Always;
        static bool s_Lighting = true;

        public static CompareFunction zTest
        {
            [Unbound.ImplementedIn(Compat.EditorHandlesFamily)]
            get
            {
                if (!DrawingStateMeasured("UnityEditor.Handles.zTest")) return default;
                MainThreadCheck.Check("get_zTest");
                return s_ZTest;
            }
            [Unbound.ImplementedIn(Compat.EditorHandlesFamily)]
            set
            {
                if (!DrawingStateMeasured("UnityEditor.Handles.zTest")) return;
                MainThreadCheck.Check("set_zTest");
                s_ZTest = value;
            }
        }

        public static bool lighting
        {
            [Unbound.ImplementedIn(Compat.EditorHandlesFamily)]
            get
            {
                if (!DrawingStateMeasured("UnityEditor.Handles.lighting")) return default;
                MainThreadCheck.Check("get_lighting");
                return s_Lighting;
            }
            [Unbound.ImplementedIn(Compat.EditorHandlesFamily)]
            set
            {
                if (!DrawingStateMeasured("UnityEditor.Handles.lighting")) return;
                MainThreadCheck.Check("set_lighting");
                s_Lighting = value;
            }
        }

        static bool DrawingStateMeasured(string member)
        {
            if (Compat.EditorHandleDrawingStateMeasured) return true;
            Stubs.NotImplemented(member);
            return false;
        }
    }

    public sealed partial class HandleUtility
    {
        // Unity's line projection retains the raw direction below this length.
        const float MinNormalizedLineLength = 1e-6f;

        [Unbound.ImplementedIn(Compat.EditorHandlesFamily)]
        public static float PointOnLineParameter(Vector3 point, Vector3 linePoint, Vector3 lineDirection) =>
            LineGeometryMeasured("UnityEditor.HandleUtility.PointOnLineParameter")
                ? Vector3.Dot(point - linePoint, lineDirection) / lineDirection.sqrMagnitude : default;

        [Unbound.ImplementedIn(Compat.EditorHandlesFamily)]
        public static Vector3 ProjectPointLine(Vector3 point, Vector3 lineStart, Vector3 lineEnd) =>
            LineGeometryMeasured("UnityEditor.HandleUtility.ProjectPointLine")
                ? ProjectSegment(point, lineStart, lineEnd) : default;

        [Unbound.ImplementedIn(Compat.EditorHandlesFamily)]
        public static float DistancePointLine(Vector3 point, Vector3 lineStart, Vector3 lineEnd) =>
            LineGeometryMeasured("UnityEditor.HandleUtility.DistancePointLine")
                ? Vector3.Distance(point, ProjectSegment(point, lineStart, lineEnd)) : default;

        static bool LineGeometryMeasured(string member)
        {
            if (Compat.EditorHandleLineGeometryMeasured) return true;
            Stubs.NotImplemented(member);
            return false;
        }

        static Vector3 ProjectSegment(Vector3 point, Vector3 start, Vector3 end)
        {
            var segment = end - start;
            float length = segment.magnitude;
            var direction = length > MinNormalizedLineLength ? segment / length : segment;
            float along = Mathf.Clamp(Vector3.Dot(point - start, direction), 0, length);
            return start + along * direction;
        }
    }
}
