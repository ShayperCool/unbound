using System;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>
    /// A Transform with a rectangle placed relative to its parent's: the anchors pick a region of the
    /// parent rect, sizeDelta grows it, anchoredPosition places the pivot relative to the anchor
    /// reference point. These are the state; the local position follows from them and the parent.
    /// </summary>
    [NativeClass("RectTransform")]
    public sealed partial class RectTransform : Transform
    {
        public delegate void ReapplyDrivenProperties(RectTransform driven);

        public static event ReapplyDrivenProperties reapplyDrivenProperties;

        internal Vector2 m_AnchorMin = new Vector2(0.5f, 0.5f);
        internal Vector2 m_AnchorMax = new Vector2(0.5f, 0.5f);
        internal Vector2 m_AnchoredPosition;
        internal Vector2 m_SizeDelta = new Vector2(100f, 100f);
        internal Vector2 m_Pivot = new Vector2(0.5f, 0.5f);
        Rect m_LastRect;
        bool m_HasLastRect;

        /// <summary>Set by a component that controls this rect (a root Canvas, a layout group).</summary>
        internal Object m_DrivenBy;
        internal DrivenTransformProperties m_DrivenProperties;

        RectTransform() { }

        public Rect rect
        {
            get
            {
                var size = ParentSize() * (m_AnchorMax - m_AnchorMin) + m_SizeDelta;
                return new Rect(-m_Pivot.x * size.x, -m_Pivot.y * size.y, size.x, size.y);
            }
        }

        public Vector2 anchorMin
        {
            get => m_AnchorMin;
            set { if (m_AnchorMin != value) { m_AnchorMin = value; DimensionsChanged(); } }
        }

        public Vector2 anchorMax
        {
            get => m_AnchorMax;
            set { if (m_AnchorMax != value) { m_AnchorMax = value; DimensionsChanged(); } }
        }

        public Vector2 anchoredPosition
        {
            get => m_AnchoredPosition;
            set { if (m_AnchoredPosition != value) { m_AnchoredPosition = value; MarkChanged(); } }
        }

        public Vector3 anchoredPosition3D
        {
            get => new Vector3(m_AnchoredPosition.x, m_AnchoredPosition.y, m_LocalPosition.z);
            set
            {
                m_AnchoredPosition = new Vector2(value.x, value.y);
                m_LocalPosition.z = value.z;
                MarkChanged();
            }
        }

        public Vector2 sizeDelta
        {
            get => m_SizeDelta;
            set { if (m_SizeDelta != value) { m_SizeDelta = value; DimensionsChanged(); } }
        }

        public Vector2 pivot
        {
            get => m_Pivot;
            set { if (m_Pivot != value) { m_Pivot = value; DimensionsChanged(); } }
        }

        public Vector2 offsetMin
        {
            get => m_AnchoredPosition - Vector2.Scale(m_SizeDelta, m_Pivot);
            set
            {
                var offset = value - (m_AnchoredPosition - Vector2.Scale(m_SizeDelta, m_Pivot));
                m_SizeDelta -= offset;
                m_AnchoredPosition += Vector2.Scale(offset, Vector2.one - m_Pivot);
                DimensionsChanged();
            }
        }

        public Vector2 offsetMax
        {
            get => m_AnchoredPosition + Vector2.Scale(m_SizeDelta, Vector2.one - m_Pivot);
            set
            {
                var offset = value - (m_AnchoredPosition + Vector2.Scale(m_SizeDelta, Vector2.one - m_Pivot));
                m_SizeDelta += offset;
                m_AnchoredPosition += Vector2.Scale(offset, m_Pivot);
                DimensionsChanged();
            }
        }

        public Object drivenByObject
        {
            get => m_DrivenBy;
            internal set => m_DrivenBy = value;
        }

        public DrivenTransformProperties drivenProperties
        {
            get => m_DrivenProperties;
            internal set => m_DrivenProperties = value;
        }

        Vector2 ParentSize() => m_Parent is RectTransform parentRect ? parentRect.rect.size : Vector2.zero;

        /// <summary>Where anchoredPosition is measured from, in the parent's space.</summary>
        Vector2 AnchorReference()
        {
            if (!(m_Parent is RectTransform parentRect)) return Vector2.zero;
            var r = parentRect.rect;
            var anchor = new Vector2(
                Mathf.LerpUnclamped(m_AnchorMin.x, m_AnchorMax.x, m_Pivot.x),
                Mathf.LerpUnclamped(m_AnchorMin.y, m_AnchorMax.y, m_Pivot.y));
            return r.min + Vector2.Scale(r.size, anchor);
        }

        internal override Vector3 GetLocalPosition()
        {
            var reference = AnchorReference();
            return new Vector3(reference.x + m_AnchoredPosition.x, reference.y + m_AnchoredPosition.y, m_LocalPosition.z);
        }

        internal override void SetLocalPosition(Vector3 value)
        {
            var reference = AnchorReference();
            m_AnchoredPosition = new Vector2(value.x - reference.x, value.y - reference.y);
            m_LocalPosition = value;
            MarkChanged();
        }

        public void ForceUpdateRectTransforms() => EnsureWorld();

        public void GetLocalCorners(Vector3[] fourCornersArray)
        {
            if (fourCornersArray == null || fourCornersArray.Length < 4)
            {
                Debug.LogError("Calling GetLocalCorners with an array that is null or has less than 4 elements.");
                return;
            }
            var r = rect;
            fourCornersArray[0] = new Vector3(r.xMin, r.yMin, 0f);
            fourCornersArray[1] = new Vector3(r.xMin, r.yMax, 0f);
            fourCornersArray[2] = new Vector3(r.xMax, r.yMax, 0f);
            fourCornersArray[3] = new Vector3(r.xMax, r.yMin, 0f);
        }

        public void GetWorldCorners(Vector3[] fourCornersArray)
        {
            if (fourCornersArray == null || fourCornersArray.Length < 4)
            {
                Debug.LogError("Calling GetWorldCorners with an array that is null or has less than 4 elements.");
                return;
            }
            GetLocalCorners(fourCornersArray);
            var m = localToWorldMatrix;
            for (int i = 0; i < 4; i++) fourCornersArray[i] = m.MultiplyPoint(fourCornersArray[i]);
        }

        public void SetInsetAndSizeFromParentEdge(Edge edge, float inset, float size)
        {
            int axis = edge == Edge.Top || edge == Edge.Bottom ? 1 : 0;
            bool end = edge == Edge.Top || edge == Edge.Right;
            float anchorValue = end ? 1f : 0f;
            var min = m_AnchorMin;
            var max = m_AnchorMax;
            min[axis] = anchorValue;
            max[axis] = anchorValue;
            m_AnchorMin = min;
            m_AnchorMax = max;
            var sd = m_SizeDelta;
            sd[axis] = size;
            m_SizeDelta = sd;
            var pos = m_AnchoredPosition;
            pos[axis] = end ? -inset - size * (1f - m_Pivot[axis]) : inset + size * m_Pivot[axis];
            m_AnchoredPosition = pos;
            DimensionsChanged();
        }

        public void SetSizeWithCurrentAnchors(Axis axis, float size)
        {
            int i = (int)axis;
            var sd = m_SizeDelta;
            sd[i] = size - ParentSize()[i] * (m_AnchorMax[i] - m_AnchorMin[i]);
            sizeDelta = sd;
        }

        internal static void SendReapplyDrivenProperties(RectTransform driven) => reapplyDrivenProperties?.Invoke(driven);

        /// <summary>The rect may have changed: move dependents and tell the scripts whose rect did.</summary>
        void DimensionsChanged()
        {
            MarkChanged();
        }

        internal override void OnLocalChanged() => NotifyIfRectChanged();

        internal override void OnParentChanged() => NotifyIfRectChanged();

        internal override void OnHierarchyChanged() => NotifyIfRectChanged();

        void NotifyIfRectChanged()
        {
            var r = rect;
            if (m_HasLastRect && r == m_LastRect) return;
            bool first = !m_HasLastRect;
            m_LastRect = r;
            m_HasLastRect = true;
            if (first || m_GameObject == null || m_GameObject.m_IsPrefabAsset) return;
            UiMessages.DimensionsChanged(this);
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (RectTransform)source;
            m_AnchorMin = s.m_AnchorMin;
            m_AnchorMax = s.m_AnchorMax;
            m_AnchoredPosition = s.m_AnchoredPosition;
            m_SizeDelta = s.m_SizeDelta;
            m_Pivot = s.m_Pivot;
            m_LocalPosition = s.m_LocalPosition;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            base.ReadSerialized(body, resolver);
            m_AnchorMin = body.Vector2("m_AnchorMin");
            m_AnchorMax = body.Vector2("m_AnchorMax");
            m_AnchoredPosition = body.Vector2("m_AnchoredPosition");
            m_SizeDelta = body.Vector2("m_SizeDelta");
            m_Pivot = body.Get("m_Pivot") != null ? body.Vector2("m_Pivot") : new Vector2(0.5f, 0.5f);
        }

        public enum Edge
        {
            Left = 0,
            Right = 1,
            Top = 2,
            Bottom = 3,
        }

        public enum Axis
        {
            Horizontal = 0,
            Vertical = 1,
        }
    }

    [Flags]
    public enum DrivenTransformProperties
    {
        None = 0,
        All = -1,
        AnchoredPositionX = 2,
        AnchoredPositionY = 4,
        AnchoredPositionZ = 8,
        Rotation = 16,
        ScaleX = 32,
        ScaleY = 64,
        ScaleZ = 128,
        AnchorMinX = 256,
        AnchorMinY = 512,
        AnchorMaxX = 1024,
        AnchorMaxY = 2048,
        SizeDeltaX = 4096,
        SizeDeltaY = 8192,
        PivotX = 16384,
        PivotY = 32768,
        AnchoredPosition = 6,
        AnchoredPosition3D = 14,
        Scale = 224,
        AnchorMin = 768,
        AnchorMax = 3072,
        Anchors = 3840,
        SizeDelta = 12288,
    }

    /// <summary>Records which RectTransform properties a component drives (an Editor concept; at
    /// runtime it only marks the driver).</summary>
    public partial struct DrivenRectTransformTracker
    {
        public void Add(Object driver, RectTransform rectTransform, DrivenTransformProperties drivenProperties)
        {
            if (rectTransform == null) return;
            rectTransform.m_DrivenBy = driver;
            rectTransform.m_DrivenProperties |= drivenProperties;
        }

        public void Clear() { }

        public void Clear(bool revertValues) { }

        public static void StopRecordingUndo() { }

        public static void StartRecordingUndo() { }
    }

    /// <summary>Marks a class as the managed side of a native engine class (Unity's binding attribute).</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct)]
    internal sealed partial class NativeClassAttribute : Attribute
    {
        internal NativeClassAttribute(string name) { }
    }
}

namespace Unbound.Runtime
{
    /// <summary>Engine messages of the UI: dimension changes travel down the RectTransform hierarchy.</summary>
    internal static class UiMessages
    {
        internal static void DimensionsChanged(UnityEngine.RectTransform t)
        {
            var go = t.m_GameObject;
            if (go == null || go.m_Destroyed) return;
            Messaging.SendFast(go, "OnRectTransformDimensionsChange");
        }
    }
}
