using System;
using Unbound.UIElements;
using Unbound.UIElements.Layout;

namespace UnityEngine.UIElements
{
    public partial class VisualElement
    {
        public float scaledPixelsPerPoint
        {
            get
            {
                if (elementPanel == null)
                {
                    Debug.LogWarning("Trying to access the DPI setting of a visual element that is not on a panel.");
                    return GUIUtility.pixelsPerPoint;
                }
                return elementPanel.scaledPixelsPerPoint;
            }
        }

        internal float scaledPixelsPerPointNoWarning => elementPanel?.scaledPixelsPerPoint ?? GUIUtility.pixelsPerPoint;

        public Rect layout
        {
            get
            {
                var r = m_ManualLayout;
                if (!m_LayoutManual)
                {
                    r.x = yogaNode.LayoutX;
                    r.y = yogaNode.LayoutY;
                    r.width = yogaNode.LayoutWidth;
                    r.height = yogaNode.LayoutHeight;
                }
                return r;
            }
            internal set
            {
                if (m_LayoutManual && m_ManualLayout == value) return;
                var before = layout;
                VersionChangeType change = 0;
                if (!Mathf.Approximately(before.x, value.x) || !Mathf.Approximately(before.y, value.y)) change |= VersionChangeType.Transform;
                if (!Mathf.Approximately(before.width, value.width) || !Mathf.Approximately(before.height, value.height)) change |= VersionChangeType.Size;
                m_ManualLayout = value;
                m_LayoutManual = true;
                var s = style;
                s.position = Position.Absolute;
                s.marginLeft = 0f;
                s.marginRight = 0f;
                s.marginBottom = 0f;
                s.marginTop = 0f;
                s.left = value.x;
                s.top = value.y;
                s.right = float.NaN;
                s.bottom = float.NaN;
                s.width = value.width;
                s.height = value.height;
                if (change != 0) IncrementVersion(change);
            }
        }

        internal Rect rect
        {
            get
            {
                var l = layout;
                return new Rect(0f, 0f, l.width, l.height);
            }
        }

        public Rect contentRect
        {
            get
            {
                var r = resolvedStyle;
                var p = paddingRect;
                return new Rect(p.x + r.paddingLeft, p.y + r.paddingTop, p.width - r.paddingLeft - r.paddingRight, p.height - r.paddingTop - r.paddingBottom);
            }
        }

        protected Rect paddingRect
        {
            get
            {
                var r = resolvedStyle;
                var b = rect;
                return new Rect(b.x + r.borderLeftWidth, b.y + r.borderTopWidth, b.width - r.borderLeftWidth - r.borderRightWidth, b.height - r.borderTopWidth - r.borderBottomWidth);
            }
        }

        internal Rect uaPaddingRect => paddingRect;

        internal bool hasDefaultRotationAndScale
        {
            get
            {
                var c = computedValues;
                return (c.rotate.IsNone() || c.rotate.angle.ToDegrees() == 0f) && (c.scale.IsNone() || c.scale.value == Vector3.one);
            }
        }

        Vector3 positionWithLayout => ResolveTranslate() + (Vector3)layout.min;

        internal Vector3 ResolveTranslate()
        {
            var t = computedValues.translate;
            float x, y;
            if (t.x.unit == LengthUnit.Percent)
            {
                float w = resolvedStyle.width;
                x = float.IsNaN(w) ? 0f : w * t.x.value / 100f;
            }
            else
            {
                x = t.x.value;
                if (float.IsNaN(x)) x = 0f;
            }
            if (t.y.unit == LengthUnit.Percent)
            {
                float h = resolvedStyle.height;
                y = float.IsNaN(h) ? 0f : h * t.y.value / 100f;
            }
            else
            {
                y = t.y.value;
                if (float.IsNaN(y)) y = 0f;
            }
            float z = t.z;
            if (float.IsNaN(z)) z = 0f;
            return new Vector3(x, y, z);
        }

        internal Vector3 ResolveTransformOrigin()
        {
            var o = computedValues.transformOrigin;
            float x, y;
            if (o.x.IsNone())
            {
                float w = resolvedStyle.width;
                x = float.IsNaN(w) ? 0f : w / 2f;
            }
            else if (o.x.unit == LengthUnit.Percent)
            {
                float w = resolvedStyle.width;
                x = float.IsNaN(w) ? 0f : w * o.x.value / 100f;
            }
            else x = o.x.value;
            if (o.y.IsNone())
            {
                float h = resolvedStyle.height;
                y = float.IsNaN(h) ? 0f : h / 2f;
            }
            else if (o.y.unit == LengthUnit.Percent)
            {
                float h = resolvedStyle.height;
                y = float.IsNaN(h) ? 0f : h * o.y.value / 100f;
            }
            else y = o.y.value;
            float z = o.z;
            return new Vector3(x, y, float.IsNaN(z) ? 0f : z);
        }

        internal Quaternion ResolveRotation() => computedValues.rotate.ToQuaternion();

        internal Vector3 ResolveScale()
        {
            var s = computedValues.scale.value;
            if (elementPanel == null || elementPanel.isFlat) s.z = 1f;
            return s;
        }

        internal void GetPivotedMatrixWithLayout(out Matrix4x4 result)
        {
            var origin = ResolveTransformOrigin();
            result = Matrix4x4.TRS(positionWithLayout + origin, ResolveRotation(), ResolveScale()) * Matrix4x4.Translate(-origin);
        }

        /// <summary>The element's transform in its parent's space (layout position, translate, rotate, scale).</summary>
        internal Matrix4x4 localTransformWithLayout
        {
            get
            {
                if (hasDefaultRotationAndScale) return Matrix4x4.Translate(positionWithLayout);
                GetPivotedMatrixWithLayout(out var m);
                return m;
            }
        }

        public Matrix4x4 worldTransform
        {
            get
            {
                var local = localTransformWithLayout;
                var p = hierarchy.parent;
                return p == null ? local : p.worldTransform * local;
            }
        }

        Matrix4x4 m_UaWorldTransformInverse;

        internal ref Matrix4x4 worldTransformInverse
        {
            get
            {
                m_UaWorldTransformInverse = worldTransform.inverse;
                return ref m_UaWorldTransformInverse;
            }
        }

        static Rect TransformRect(Matrix4x4 m, Rect r)
        {
            Vector3 a = m.MultiplyPoint3x4(new Vector3(r.xMin, r.yMin, 0f));
            Vector3 b = m.MultiplyPoint3x4(new Vector3(r.xMax, r.yMax, 0f));
            Vector3 c = m.MultiplyPoint3x4(new Vector3(r.xMax, r.yMin, 0f));
            Vector3 d = m.MultiplyPoint3x4(new Vector3(r.xMin, r.yMax, 0f));
            float minX = Mathf.Min(Mathf.Min(a.x, b.x), Mathf.Min(c.x, d.x));
            float minY = Mathf.Min(Mathf.Min(a.y, b.y), Mathf.Min(c.y, d.y));
            float maxX = Mathf.Max(Mathf.Max(a.x, b.x), Mathf.Max(c.x, d.x));
            float maxY = Mathf.Max(Mathf.Max(a.y, b.y), Mathf.Max(c.y, d.y));
            return new Rect(minX, minY, maxX - minX, maxY - minY);
        }

        public Rect worldBound
        {
            get
            {
                var r = rect;
                var m = worldTransform;
                if (IsTranslationOnly(m)) return new Rect(r.x + m.m03, r.y + m.m13, r.width, r.height);
                return TransformRect(m, r);
            }
        }

        public Rect localBound
        {
            get
            {
                var r = rect;
                if (hasDefaultRotationAndScale)
                {
                    var p = positionWithLayout;
                    return new Rect(r.x + p.x, r.y + p.y, r.width, r.height);
                }
                GetPivotedMatrixWithLayout(out var m);
                return TransformRect(m, r);
            }
        }

        static bool IsTranslationOnly(Matrix4x4 m) =>
            m.m00 == 1f && m.m11 == 1f && m.m22 == 1f && m.m01 == 0f && m.m02 == 0f && m.m10 == 0f && m.m12 == 0f && m.m20 == 0f && m.m21 == 0f;

        internal Vector2 WorldToLocal(Vector2 p)
        {
            var m = worldTransform;
            if (IsTranslationOnly(m)) return new Vector2(p.x - m.m03, p.y - m.m13);
            return m.inverse.MultiplyPoint3x4(new Vector3(p.x, p.y, 0f));
        }

        internal Vector2 LocalToWorld(Vector2 p) => worldTransform.MultiplyPoint3x4(new Vector3(p.x, p.y, 0f));

        public virtual bool ContainsPoint(Vector2 localPoint) => rect.Contains(localPoint);

        /// <summary>Content of elements with this flag is not clipped even with overflow hidden (scroll view content).</summary>
        internal bool disableClipping { get; set; }

        /// <summary>Whether geometry changes of descendants are reported to this element (unused: every
        /// element's GeometryChangedEvent is sent to it).</summary>
        internal bool receivesHierarchyGeometryChangedEvents { get; set; }

        internal bool ShouldClip() => computedValues.overflow != OverflowInternal.Visible && !disableClipping;

        /// <summary>The element's rect grown to the bounding boxes of its displayed children (unless it
        /// clips them), in its own space.</summary>
        internal Rect boundingBox
        {
            get
            {
                var r = rect;
                if (float.IsNaN(r.x) || float.IsNaN(r.y) || float.IsNaN(r.width) || float.IsNaN(r.height)) return Rect.zero;
                if (!ShouldClip() && computedValues.display == DisplayStyle.Flex)
                {
                    foreach (var child in m_Children)
                    {
                        if (!child.areAncestorsAndSelfDisplayed) continue;
                        var c = child.boundingBox;
                        if (child.hasDefaultRotationAndScale)
                        {
                            var p = child.positionWithLayout;
                            c.position += new Vector2(p.x, p.y);
                        }
                        else
                        {
                            child.GetPivotedMatrixWithLayout(out var m);
                            c = TransformRect(m, c);
                        }
                        r.xMin = Math.Min(r.xMin, c.xMin);
                        r.xMax = Math.Max(r.xMax, c.xMax);
                        r.yMin = Math.Min(r.yMin, c.yMin);
                        r.yMax = Math.Max(r.yMax, c.yMax);
                    }
                }
                return r;
            }
        }

        internal VisualElement GetRootVisualContainer(bool stopAtNearestRoot = false)
        {
            VisualElement found = null;
            for (var e = this; e != null; e = e.hierarchy.parent)
            {
                if (!e.isRootVisualContainer) continue;
                found = e;
                if (stopAtNearestRoot) return found;
            }
            return found;
        }

        internal VisualElement GetFirstAncestorWhere(Predicate<VisualElement> predicate)
        {
            for (var e = hierarchy.parent; e != null; e = e.hierarchy.parent)
                if (predicate(e)) return e;
            return null;
        }

        public virtual bool Overlaps(Rect rectangle) => rect.Overlaps(rectangle, true);

        // ---- measuring ----

        internal bool requireMeasureFunction
        {
            get => m_RequireMeasureFunction;
            set
            {
                m_RequireMeasureFunction = value;
                if (value && m_Children.Count == 0) AssignMeasureFunction();
                else if (!value) yogaNode.MeasureFunction = null;
            }
        }

        void AssignMeasureFunction() => yogaNode.MeasureFunction = UaLayout.Measure;

        protected internal virtual Vector2 DoMeasure(float desiredWidth, MeasureMode widthMode, float desiredHeight, MeasureMode heightMode) =>
            new Vector2(float.NaN, float.NaN);

        /// <summary>The element's content changed size (text, image): its layout must be measured again.</summary>
        internal void UaMarkMeasureDirty()
        {
            if (yogaNode.MeasureFunction != null) yogaNode.MarkDirty();
            IncrementVersion(VersionChangeType.Layout);
        }

        // ---- ITransform (the legacy transform maps to the translate/rotate/scale styles) ----

        public ITransform transform => this;

        Vector3 ITransform.position
        {
            get => resolvedStyle.translate;
            set => style.translate = new Translate(value.x, value.y, value.z);
        }

        Quaternion ITransform.rotation
        {
            get => resolvedStyle.rotate.ToQuaternion();
            set
            {
                value.ToAngleAxis(out float angle, out Vector3 axis);
                style.rotate = new Rotate(Angle.Degrees(angle), axis);
            }
        }

        Vector3 ITransform.scale
        {
            get
            {
                var v = resolvedStyle.scale.value;
                if (elementPanel != null && elementPanel.isFlat) v.z = 1f;
                return v;
            }
            set => style.scale = new Scale(value);
        }

        Matrix4x4 ITransform.matrix
        {
            get
            {
                var v = resolvedStyle.scale.value;
                if (elementPanel != null && elementPanel.isFlat) v.z = 1f;
                return Matrix4x4.TRS(resolvedStyle.translate, resolvedStyle.rotate.ToQuaternion(), v);
            }
        }
    }

    public static partial class VisualElementExtensions
    {
        public static void StretchToParentSize(this VisualElement elem)
        {
            if (elem == null) throw new ArgumentNullException(nameof(elem));
            var s = elem.style;
            s.position = Position.Absolute;
            s.left = 0f;
            s.top = 0f;
            s.right = 0f;
            s.bottom = 0f;
        }

        public static void StretchToParentWidth(this VisualElement elem)
        {
            if (elem == null) throw new ArgumentNullException(nameof(elem));
            var s = elem.style;
            s.position = Position.Absolute;
            s.left = 0f;
            s.right = 0f;
        }

        public static void AddManipulator(this VisualElement ele, IManipulator manipulator)
        {
            if (manipulator != null) manipulator.target = ele;
        }

        public static void RemoveManipulator(this VisualElement ele, IManipulator manipulator)
        {
            if (manipulator != null) manipulator.target = null;
        }

        public static Vector2 WorldToLocal(this VisualElement ele, Vector2 p)
        {
            if (ele == null) throw new ArgumentNullException(nameof(ele));
            return ele.WorldToLocal(p);
        }

        public static Vector2 LocalToWorld(this VisualElement ele, Vector2 p)
        {
            if (ele == null) throw new ArgumentNullException(nameof(ele));
            return ele.LocalToWorld(p);
        }

        public static Rect WorldToLocal(this VisualElement ele, Rect r)
        {
            if (ele == null) throw new ArgumentNullException(nameof(ele));
            var min = ele.WorldToLocal(r.min);
            var max = ele.WorldToLocal(r.max);
            return Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
        }

        public static Rect LocalToWorld(this VisualElement ele, Rect r)
        {
            if (ele == null) throw new ArgumentNullException(nameof(ele));
            var min = ele.LocalToWorld(r.min);
            var max = ele.LocalToWorld(r.max);
            return Rect.MinMaxRect(Mathf.Min(min.x, max.x), Mathf.Min(min.y, max.y), Mathf.Max(min.x, max.x), Mathf.Max(min.y, max.y));
        }

        public static Vector2 ChangeCoordinatesTo(this VisualElement src, VisualElement dest, Vector2 point) => dest.WorldToLocal(src.LocalToWorld(point));

        public static Rect ChangeCoordinatesTo(this VisualElement src, VisualElement dest, Rect rect) => dest.WorldToLocal(src.LocalToWorld(rect));
    }
}

namespace Unbound.UIElements
{
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>The bridge between computed styles and layout nodes.</summary>
    internal static class UaLayout
    {
        internal static readonly YogaConfig DefaultConfig = new YogaConfig { RoundEdgesToPixelGrid = true };

        static YogaValue Value(Length length)
        {
            if (length.IsAuto()) return YogaValue.Auto;
            if (length.IsNone()) return YogaValue.Undefined;
            return length.unit == LengthUnit.Percent ? YogaValue.Percent(length.value) : YogaValue.Point(length.value);
        }

        internal static void SyncStyle(VisualElement ve, ComputedValues s)
        {
            var n = ve.yogaNode;
            n.FlexGrow = s.flexGrow;
            n.FlexShrink = s.flexShrink;
            n.FlexBasis = Value(s.flexBasis);
            n.SetPosition(YogaEdge.Left, Value(s.left));
            n.SetPosition(YogaEdge.Top, Value(s.top));
            n.SetPosition(YogaEdge.Right, Value(s.right));
            n.SetPosition(YogaEdge.Bottom, Value(s.bottom));
            n.SetMargin(YogaEdge.Left, Value(s.marginLeft));
            n.SetMargin(YogaEdge.Top, Value(s.marginTop));
            n.SetMargin(YogaEdge.Right, Value(s.marginRight));
            n.SetMargin(YogaEdge.Bottom, Value(s.marginBottom));
            n.SetPadding(YogaEdge.Left, Value(s.paddingLeft));
            n.SetPadding(YogaEdge.Top, Value(s.paddingTop));
            n.SetPadding(YogaEdge.Right, Value(s.paddingRight));
            n.SetPadding(YogaEdge.Bottom, Value(s.paddingBottom));
            n.SetBorder(YogaEdge.Left, s.borderLeftWidth);
            n.SetBorder(YogaEdge.Top, s.borderTopWidth);
            n.SetBorder(YogaEdge.Right, s.borderRightWidth);
            n.SetBorder(YogaEdge.Bottom, s.borderBottomWidth);
            n.Width = Value(s.width);
            n.Height = Value(s.height);
            n.PositionType = (YogaPositionType)(int)s.position;
            n.Overflow = (YogaOverflow)(int)s.overflow;
            n.AlignSelf = (YogaAlign)(int)s.alignSelf;
            n.MaxWidth = Value(s.maxWidth);
            n.MaxHeight = Value(s.maxHeight);
            n.MinWidth = Value(s.minWidth);
            n.MinHeight = Value(s.minHeight);
            n.FlexDirection = (YogaFlexDirection)(int)s.flexDirection;
            n.AlignContent = (YogaAlign)(int)s.alignContent;
            n.AlignItems = (YogaAlign)(int)s.alignItems;
            n.JustifyContent = (YogaJustify)(int)s.justifyContent;
            n.Wrap = (YogaWrap)(int)s.flexWrap;
            n.Display = (YogaDisplay)(int)s.display;
        }

        internal static YogaSize Measure(YogaNode node, float width, YogaMeasureMode widthMode, float height, YogaMeasureMode heightMode)
        {
            var ve = (VisualElement)node.Owner;
            var size = ve.DoMeasure(width, (VisualElement.MeasureMode)(int)widthMode, height, (VisualElement.MeasureMode)(int)heightMode);
            float ppp = ve.scaledPixelsPerPointNoWarning;
            return new YogaSize { Width = RoundToPixelGrid(size.x, ppp), Height = RoundToPixelGrid(size.y, ppp) };
        }

        internal static float RoundToPixelGrid(float v, float pixelsPerPoint, float offset = 0.02f) => Mathf.Floor(v * pixelsPerPoint + 0.5f + offset) / pixelsPerPoint;
        internal static float CeilToPixelGrid(float v, float pixelsPerPoint, float offset = -0.02f) => Mathf.Ceil(v * pixelsPerPoint + offset) / pixelsPerPoint;
        internal static float FloorToPixelGrid(float v, float pixelsPerPoint, float offset = 0.02f) => Mathf.Floor(v * pixelsPerPoint + offset) / pixelsPerPoint;
    }
}
