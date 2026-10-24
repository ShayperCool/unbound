// A C# port of the Yoga 1.9.0 flexbox layout engine.
//
// Yoga is Copyright (c) 2014-present, Facebook, Inc., and is licensed under the MIT license:
// https://github.com/facebook/yoga/blob/1.9.0/LICENSE
//
// UI Toolkit lays out with a Yoga derivative, so this is a faithful translation of Yoga 1.9.0's
// algorithm: the same float arithmetic in the same order, the same measurement caching and the
// same pixel-grid rounding. Internally an undefined value is Yoga's 10E20 sentinel (anything outside
// (-1e9, 1e9) counts as undefined); the public API uses NaN for undefined.

using System;
using System.Collections.Generic;
using System.Threading;

namespace Unbound.UIElements.Layout
{
    internal enum YogaUnit { Undefined, Point, Percent, Auto }

    internal enum YogaDirection { Inherit, LTR, RTL }

    internal enum YogaFlexDirection { Column, ColumnReverse, Row, RowReverse }

    internal enum YogaJustify { FlexStart, Center, FlexEnd, SpaceBetween, SpaceAround, SpaceEvenly }

    internal enum YogaAlign { Auto, FlexStart, Center, FlexEnd, Stretch, Baseline, SpaceBetween, SpaceAround }

    internal enum YogaPositionType { Relative, Absolute }

    internal enum YogaWrap { NoWrap, Wrap, WrapReverse }

    internal enum YogaOverflow { Visible, Hidden, Scroll }

    internal enum YogaDisplay { Flex, None }

    internal enum YogaMeasureMode { Undefined, Exactly, AtMost }

    internal enum YogaEdge { Left, Top, Right, Bottom, Start, End, Horizontal, Vertical, All }

    /// <summary>A length with its unit. Undefined and auto values report NaN as their value.</summary>
    internal struct YogaValue : IEquatable<YogaValue>
    {
        public float Value;
        public YogaUnit Unit;

        public YogaValue(float value, YogaUnit unit)
        {
            Value = value;
            Unit = unit;
        }

        public static YogaValue Point(float value) =>
            float.IsNaN(value) ? Undefined : new YogaValue(value, YogaUnit.Point);

        public static YogaValue Percent(float value) =>
            float.IsNaN(value) ? Undefined : new YogaValue(value, YogaUnit.Percent);

        public static YogaValue Auto => new YogaValue(float.NaN, YogaUnit.Auto);

        public static YogaValue Undefined => new YogaValue(float.NaN, YogaUnit.Undefined);

        // Same units, and either both undefined or values within 0.0001.
        public bool Equals(YogaValue other)
        {
            if (Unit != other.Unit)
                return false;
            if (Unit == YogaUnit.Undefined || (IsUndefinedOrNaN(Value) && IsUndefinedOrNaN(other.Value)))
                return true;
            return MathF.Abs(Value - other.Value) < 0.0001f;
        }

        public override bool Equals(object obj) => obj is YogaValue other && Equals(other);

        public override int GetHashCode() => (int)Unit;

        public static bool operator ==(YogaValue a, YogaValue b) => a.Equals(b);

        public static bool operator !=(YogaValue a, YogaValue b) => !a.Equals(b);

        public override string ToString() => Unit switch
        {
            YogaUnit.Point => Value.ToString(System.Globalization.CultureInfo.InvariantCulture),
            YogaUnit.Percent => Value.ToString(System.Globalization.CultureInfo.InvariantCulture) + "%",
            YogaUnit.Auto => "auto",
            _ => "undefined",
        };

        static bool IsUndefinedOrNaN(float value) => float.IsNaN(value) || Yoga.IsUndefined(value);
    }

    internal struct YogaSize
    {
        public float Width;
        public float Height;

        public YogaSize(float width, float height)
        {
            Width = width;
            Height = height;
        }
    }

    /// <summary>Measures a leaf node; undefined available sizes are passed as NaN.</summary>
    internal delegate YogaSize YogaMeasureFunc(YogaNode node, float width, YogaMeasureMode widthMode, float height, YogaMeasureMode heightMode);

    internal delegate float YogaBaselineFunc(YogaNode node, float width, float height);

    internal sealed class YogaConfig
    {
        internal static readonly YogaConfig Default = new YogaConfig();

        float pointScaleFactor = 1f;

        /// <summary>Pixels per point used to round layouts to the pixel grid; 0 disables rounding.</summary>
        public float PointScaleFactor
        {
            get => pointScaleFactor;
            set
            {
                if (!(value >= 0f))
                    throw new ArgumentOutOfRangeException(nameof(value), "Scale factor should not be less than zero");
                pointScaleFactor = value;
            }
        }

        public bool UseWebDefaults { get; set; }

        public bool UseLegacyStretchBehaviour { get; set; }

        /// <summary>Margins, paddings and borders snap to the pixel grid before layout, as UI Toolkit's
        /// layout engine does (measured: at 1.25 pixels per point a 2pt margin lays out as 2.4pt).</summary>
        public bool RoundEdgesToPixelGrid { get; set; }

        /// <summary>A percentage flex basis resolves only within a container of exact main size; otherwise
        /// the item sizes to its content, as UI Toolkit's layout measures it.</summary>
        public bool PercentFlexBasisNeedsExactContainer { get; set; }

        /// <summary>Containers with overflow hidden measure their items along the main axis without
        /// constraint, as overflow scroll does (UI Toolkit's layout).</summary>
        public bool OverflowHiddenMeasuresContent { get; set; }

        /// <summary>Positions round as the difference of the rounded absolute edges of the node and its
        /// parent (as widths do), not as rounded relative offsets, as UI Toolkit's layout engine does
        /// (measured at 1.25 pixels per point: a child at 10.4 in a parent at 8 lies at 10.4f - 8f).</summary>
        public bool RoundPositionsFromAbsolute { get; set; }

        /// <summary>Nodes with a measure function round to the pixel grid as the others do (no flooring
        /// of their position nor ceiling of their size), as in UI Toolkit's layout engine, whose nodes
        /// keep the default node type (measured at 1.25 pixels per point: a centered label at 111.2,
        /// not floored to 110.4).</summary>
        public bool MeasuredNodesRoundAsOthers { get; set; }

        /// <summary>To the nearest pixel, halves up (measured: a -3pt margin at 1.25
        /// pixels per point, -3.75 pixels, lays out as -4 pixels).</summary>
        internal float RoundEdge(float value) =>
            RoundEdgesToPixelGrid && pointScaleFactor != 0f && !float.IsNaN(value) ? (float)Math.Floor(value * pointScaleFactor + 0.5f) / pointScaleFactor : value;

        internal YogaFloatOptional RoundEdge(YogaFloatOptional value) =>
            value.IsUndefined || !RoundEdgesToPixelGrid || pointScaleFactor == 0f ? value : new YogaFloatOptional(RoundEdge(value.Value));
    }

    /// <summary>An optional float: undefined inputs make it empty.</summary>
    internal readonly struct YogaFloatOptional
    {
        readonly float value;
        readonly bool defined;

        public YogaFloatOptional(float value)
        {
            if (Yoga.IsUndefined(value))
            {
                this.value = 0;
                defined = false;
            }
            else
            {
                this.value = value;
                defined = true;
            }
        }

        public static YogaFloatOptional Undefined => default;

        public bool IsUndefined => !defined;

        public float Value
        {
            get
            {
                if (!defined)
                    throw new InvalidOperationException("Tried to get value of an undefined YogaFloatOptional");
                return value;
            }
        }

        public static YogaFloatOptional operator +(YogaFloatOptional a, YogaFloatOptional b) =>
            a.defined && b.defined ? new YogaFloatOptional(a.value + b.value) : default;

        // Both undefined, or both defined and equal (an undefined float matches an empty optional).
        public bool EqualsFloat(float other)
        {
            if (Yoga.IsUndefined(other) == !defined)
                return !defined || other == value;
            return false;
        }
    }

    /// <summary>A cached result of laying out or measuring a node under given constraints.</summary>
    internal sealed class YogaCachedMeasurement
    {
        public float AvailableWidth;
        public float AvailableHeight;
        public YogaMeasureMode WidthMeasureMode;
        public YogaMeasureMode HeightMeasureMode;
        public float ComputedWidth;
        public float ComputedHeight;

        public YogaCachedMeasurement() => Reset();

        public void Reset()
        {
            AvailableWidth = 0;
            AvailableHeight = 0;
            WidthMeasureMode = (YogaMeasureMode)(-1);
            HeightMeasureMode = (YogaMeasureMode)(-1);
            ComputedWidth = -1;
            ComputedHeight = -1;
        }

        public void Zero()
        {
            AvailableWidth = 0;
            AvailableHeight = 0;
            WidthMeasureMode = 0;
            HeightMeasureMode = 0;
            ComputedWidth = 0;
            ComputedHeight = 0;
        }
    }

    /// <summary>A node's layout results and its layout caches.</summary>
    internal sealed class YogaLayoutData
    {
        public const int MaxCachedResultCount = 16;

        public readonly float[] Position = new float[4];
        public readonly float[] Dimensions = new float[2];
        public readonly float[] Margin = new float[6];
        public readonly float[] Border = new float[6];
        public readonly float[] Padding = new float[6];
        public YogaDirection Direction;

        public uint ComputedFlexBasisGeneration;
        public YogaFloatOptional ComputedFlexBasis;
        public bool HadOverflow;

        public uint GenerationCount;
        public YogaDirection LastOwnerDirection;

        public uint NextCachedMeasurementsIndex;
        public readonly YogaCachedMeasurement[] CachedMeasurements = new YogaCachedMeasurement[MaxCachedResultCount];
        public readonly float[] MeasuredDimensions = new float[2];

        public readonly YogaCachedMeasurement CachedLayout = new YogaCachedMeasurement();
        public bool DidUseLegacyFlag;
        public bool DoesLegacyStretchFlagAffectsLayout;

        public YogaLayoutData()
        {
            for (int i = 0; i < MaxCachedResultCount; i++)
                CachedMeasurements[i] = new YogaCachedMeasurement();
            Reset();
        }

        /// <summary>Back to the state of a freshly created node.</summary>
        public void Reset()
        {
            Array.Clear(Position);
            Dimensions[0] = Yoga.Undefined;
            Dimensions[1] = Yoga.Undefined;
            Array.Clear(Margin);
            Array.Clear(Border);
            Array.Clear(Padding);
            Direction = YogaDirection.Inherit;
            ComputedFlexBasisGeneration = 0;
            ComputedFlexBasis = YogaFloatOptional.Undefined;
            HadOverflow = false;
            GenerationCount = 0;
            LastOwnerDirection = (YogaDirection)(-1);
            NextCachedMeasurementsIndex = 0;
            foreach (var entry in CachedMeasurements)
                entry.Reset();
            MeasuredDimensions[0] = Yoga.Undefined;
            MeasuredDimensions[1] = Yoga.Undefined;
            CachedLayout.Reset();
            DidUseLegacyFlag = false;
            DoesLegacyStretchFlagAffectsLayout = false;
        }

        /// <summary>Every field set to zero (the layout of a node with display: none).</summary>
        public void Zero()
        {
            Array.Clear(Position);
            Array.Clear(Dimensions);
            Array.Clear(Margin);
            Array.Clear(Border);
            Array.Clear(Padding);
            Direction = 0;
            ComputedFlexBasisGeneration = 0;
            ComputedFlexBasis = new YogaFloatOptional(0f);
            HadOverflow = false;
            GenerationCount = 0;
            LastOwnerDirection = 0;
            NextCachedMeasurementsIndex = 0;
            foreach (var entry in CachedMeasurements)
                entry.Zero();
            Array.Clear(MeasuredDimensions);
            CachedLayout.Zero();
            DidUseLegacyFlag = false;
            DoesLegacyStretchFlagAffectsLayout = false;
        }
    }

    /// <summary>A node's style, with Yoga's defaults.</summary>
    internal sealed class YogaStyle
    {
        public const int EdgeCount = 9;

        public YogaDirection Direction = YogaDirection.Inherit;
        public YogaFlexDirection FlexDirection = YogaFlexDirection.Column;
        public YogaJustify JustifyContent = YogaJustify.FlexStart;
        public YogaAlign AlignContent = YogaAlign.FlexStart;
        public YogaAlign AlignItems = YogaAlign.Stretch;
        public YogaAlign AlignSelf = YogaAlign.Auto;
        public YogaPositionType PositionType = YogaPositionType.Relative;
        public YogaWrap FlexWrap = YogaWrap.NoWrap;
        public YogaOverflow Overflow = YogaOverflow.Visible;
        public YogaDisplay Display = YogaDisplay.Flex;
        public YogaFloatOptional Flex;
        public YogaFloatOptional FlexGrow;
        public YogaFloatOptional FlexShrink;
        public YogaValue FlexBasis = new YogaValue(0, YogaUnit.Auto);
        public readonly YogaValue[] Margin = DefaultEdges();
        public readonly YogaValue[] Position = DefaultEdges();
        public readonly YogaValue[] Padding = DefaultEdges();
        public readonly YogaValue[] Border = DefaultEdges();
        public readonly YogaValue[] Dimensions = { new YogaValue(0, YogaUnit.Auto), new YogaValue(0, YogaUnit.Auto) };
        public readonly YogaValue[] MinDimensions = { new YogaValue(0, YogaUnit.Undefined), new YogaValue(0, YogaUnit.Undefined) };
        public readonly YogaValue[] MaxDimensions = { new YogaValue(0, YogaUnit.Undefined), new YogaValue(0, YogaUnit.Undefined) };
        public YogaFloatOptional AspectRatio;

        static YogaValue[] DefaultEdges()
        {
            var edges = new YogaValue[EdgeCount];
            for (int i = 0; i < EdgeCount; i++)
                edges[i] = new YogaValue(0, YogaUnit.Undefined);
            return edges;
        }
    }

    /// <summary>A flexbox node: style in, layout out.</summary>
    internal sealed class YogaNode
    {
        internal readonly YogaStyle style = new YogaStyle();
        internal readonly YogaLayoutData layout = new YogaLayoutData();
        internal readonly YogaValue[] resolvedDimensions = { Yoga.ValueUndefined, Yoga.ValueUndefined };
        internal YogaConfig config;
        internal uint lineIndex;
        internal bool isTextNode;

        YogaNode owner;
        readonly List<YogaNode> children = new List<YogaNode>();
        YogaMeasureFunc measure;
        YogaBaselineFunc baseline;
        bool hasNewLayout = true;
        bool isDirty;

        public YogaNode(YogaConfig config = null)
        {
            this.config = config ?? YogaConfig.Default;
            if (this.config.UseWebDefaults)
            {
                style.FlexDirection = YogaFlexDirection.Row;
                style.AlignContent = YogaAlign.Stretch;
            }
        }

        /// <summary>Free user data.</summary>
        public object Owner { get; set; }

        public YogaConfig Config
        {
            get => config;
            set
            {
                value ??= YogaConfig.Default;
                if (config == value) return;
                config = value;
                MarkDirtyInternal();
            }
        }

        // ---- Tree ----

        public int Count => children.Count;

        public YogaNode this[int index] => children[index];

        public YogaNode Parent => owner;

        internal List<YogaNode> Children => children;

        public void Insert(int index, YogaNode child)
        {
            if (child == null)
                throw new ArgumentNullException(nameof(child));
            if (child.owner != null)
                throw new InvalidOperationException("Child already has a owner, it must be removed first.");
            if (measure != null)
                throw new InvalidOperationException("Cannot add child: Nodes with measure functions cannot have children.");
            children.Insert(index, child);
            child.owner = this;
            MarkDirtyInternal();
        }

        public void RemoveAt(int index)
        {
            var child = children[index];
            children.RemoveAt(index);
            Detach(child);
        }

        public void Remove(YogaNode child)
        {
            if (children.Remove(child))
                Detach(child);
        }

        void Detach(YogaNode child)
        {
            // The layout is no longer valid.
            child.layout.Reset();
            child.owner = null;
            MarkDirtyInternal();
        }

        public void Clear()
        {
            if (children.Count == 0)
                return;
            foreach (var child in children)
            {
                child.layout.Reset();
                child.owner = null;
            }
            children.Clear();
            MarkDirtyInternal();
        }

        // ---- Functions ----

        public YogaMeasureFunc MeasureFunction
        {
            get => measure;
            set
            {
                if (value == null)
                {
                    isTextNode = false;
                }
                else
                {
                    if (children.Count != 0)
                        throw new InvalidOperationException("Cannot set measure function: Nodes with measure functions cannot have children.");
                    isTextNode = true;
                }
                measure = value;
            }
        }

        public YogaBaselineFunc BaselineFunction
        {
            get => baseline;
            set => baseline = value;
        }

        // ---- Dirty state ----

        public bool IsDirty => isDirty;

        internal void SetDirty(bool dirty) => isDirty = dirty;

        /// <summary>Marks a leaf with a measure function dirty, so that it is measured again.</summary>
        public void MarkDirty()
        {
            if (measure == null)
                throw new InvalidOperationException("Only leaf nodes with custom measure functions should manually mark themselves as dirty");
            MarkDirtyInternal();
        }

        /// <summary>Marks this node and its ancestors dirty.</summary>
        internal void MarkDirtyInternal()
        {
            if (!isDirty)
            {
                isDirty = true;
                layout.ComputedFlexBasis = YogaFloatOptional.Undefined;
                owner?.MarkDirtyInternal();
            }
        }

        public bool HasNewLayout
        {
            get => hasNewLayout;
            set => hasNewLayout = value;
        }

        public void MarkLayoutSeen() => hasNewLayout = false;

        // ---- Style ----

        public YogaDirection StyleDirection
        {
            get => style.Direction;
            set { if (style.Direction != value) { style.Direction = value; MarkDirtyInternal(); } }
        }

        public YogaFlexDirection FlexDirection
        {
            get => style.FlexDirection;
            set { if (style.FlexDirection != value) { style.FlexDirection = value; MarkDirtyInternal(); } }
        }

        public YogaJustify JustifyContent
        {
            get => style.JustifyContent;
            set { if (style.JustifyContent != value) { style.JustifyContent = value; MarkDirtyInternal(); } }
        }

        public YogaAlign AlignContent
        {
            get => style.AlignContent;
            set { if (style.AlignContent != value) { style.AlignContent = value; MarkDirtyInternal(); } }
        }

        public YogaAlign AlignItems
        {
            get => style.AlignItems;
            set { if (style.AlignItems != value) { style.AlignItems = value; MarkDirtyInternal(); } }
        }

        public YogaAlign AlignSelf
        {
            get => style.AlignSelf;
            set { if (style.AlignSelf != value) { style.AlignSelf = value; MarkDirtyInternal(); } }
        }

        public YogaPositionType PositionType
        {
            get => style.PositionType;
            set { if (style.PositionType != value) { style.PositionType = value; MarkDirtyInternal(); } }
        }

        public YogaWrap Wrap
        {
            get => style.FlexWrap;
            set { if (style.FlexWrap != value) { style.FlexWrap = value; MarkDirtyInternal(); } }
        }

        public YogaOverflow Overflow
        {
            get => style.Overflow;
            set { if (style.Overflow != value) { style.Overflow = value; MarkDirtyInternal(); } }
        }

        public YogaDisplay Display
        {
            get => style.Display;
            set { if (style.Display != value) { style.Display = value; MarkDirtyInternal(); } }
        }

        /// <summary>The flex shorthand; NaN when undefined.</summary>
        public float Flex
        {
            get => Yoga.ToPublic(style.Flex);
            set => SetOptional(ref style.Flex, value);
        }

        /// <summary>The flex-grow factor; NaN when undefined (resolved as 0, or from Flex).</summary>
        public float FlexGrow
        {
            get => Yoga.ToPublic(style.FlexGrow);
            set => SetOptional(ref style.FlexGrow, value);
        }

        /// <summary>The flex-shrink factor; NaN when undefined (resolved as 0, 1 with web defaults).</summary>
        public float FlexShrink
        {
            get => Yoga.ToPublic(style.FlexShrink);
            set => SetOptional(ref style.FlexShrink, value);
        }

        void SetOptional(ref YogaFloatOptional field, float value)
        {
            value = Yoga.FromPublic(value);
            if (!field.EqualsFloat(value))
            {
                field = Yoga.IsUndefined(value) ? YogaFloatOptional.Undefined : new YogaFloatOptional(value);
                MarkDirtyInternal();
            }
        }

        public YogaValue FlexBasis
        {
            get => Yoga.ToPublic(style.FlexBasis);
            set
            {
                if (value.Unit == YogaUnit.Percent)
                    SetPercentOrAuto(ref style.FlexBasis, value.Value);
                else if (value.Unit == YogaUnit.Auto)
                    SetAuto(ref style.FlexBasis);
                else
                    SetUnit(ref style.FlexBasis, value.Value, YogaUnit.Point);
            }
        }

        public YogaValue Width
        {
            get => Yoga.ToPublic(style.Dimensions[Yoga.DimWidth]);
            set => SetDimension(style.Dimensions, Yoga.DimWidth, value, true);
        }

        public YogaValue Height
        {
            get => Yoga.ToPublic(style.Dimensions[Yoga.DimHeight]);
            set => SetDimension(style.Dimensions, Yoga.DimHeight, value, true);
        }

        public YogaValue MinWidth
        {
            get => Yoga.ToPublic(style.MinDimensions[Yoga.DimWidth]);
            set => SetDimension(style.MinDimensions, Yoga.DimWidth, value, false);
        }

        public YogaValue MinHeight
        {
            get => Yoga.ToPublic(style.MinDimensions[Yoga.DimHeight]);
            set => SetDimension(style.MinDimensions, Yoga.DimHeight, value, false);
        }

        public YogaValue MaxWidth
        {
            get => Yoga.ToPublic(style.MaxDimensions[Yoga.DimWidth]);
            set => SetDimension(style.MaxDimensions, Yoga.DimWidth, value, false);
        }

        public YogaValue MaxHeight
        {
            get => Yoga.ToPublic(style.MaxDimensions[Yoga.DimHeight]);
            set => SetDimension(style.MaxDimensions, Yoga.DimHeight, value, false);
        }

        void SetDimension(YogaValue[] values, int index, YogaValue value, bool allowsAuto)
        {
            if (value.Unit == YogaUnit.Percent)
            {
                if (allowsAuto)
                    SetPercentOrAuto(ref values[index], value.Value);
                else
                    SetUnit(ref values[index], value.Value, YogaUnit.Percent);
            }
            else if (allowsAuto && value.Unit == YogaUnit.Auto)
            {
                SetAuto(ref values[index]);
            }
            else
            {
                SetUnit(ref values[index], value.Value, YogaUnit.Point);
            }
        }

        /// <summary>The aspect ratio (width / height); NaN when undefined.</summary>
        public float AspectRatio
        {
            get => Yoga.ToPublic(style.AspectRatio);
            set
            {
                value = Yoga.FromPublic(value);
                if (!style.AspectRatio.EqualsFloat(value))
                {
                    style.AspectRatio = new YogaFloatOptional(value);
                    MarkDirtyInternal();
                }
            }
        }

        public void SetPosition(YogaEdge edge, YogaValue value) =>
            SetUnit(ref style.Position[(int)edge], value.Value, value.Unit == YogaUnit.Percent ? YogaUnit.Percent : YogaUnit.Point);

        public YogaValue GetPosition(YogaEdge edge) => Yoga.ToPublic(style.Position[(int)edge]);

        public void SetMargin(YogaEdge edge, YogaValue value)
        {
            if (value.Unit == YogaUnit.Auto)
                SetAuto(ref style.Margin[(int)edge]);
            else
                SetUnit(ref style.Margin[(int)edge], value.Value, value.Unit == YogaUnit.Percent ? YogaUnit.Percent : YogaUnit.Point);
        }

        public YogaValue GetMargin(YogaEdge edge) => Yoga.ToPublic(style.Margin[(int)edge]);

        public void SetPadding(YogaEdge edge, YogaValue value) =>
            SetUnit(ref style.Padding[(int)edge], value.Value, value.Unit == YogaUnit.Percent ? YogaUnit.Percent : YogaUnit.Point);

        public YogaValue GetPadding(YogaEdge edge) => Yoga.ToPublic(style.Padding[(int)edge]);

        public void SetBorder(YogaEdge edge, float value) =>
            SetUnit(ref style.Border[(int)edge], value, YogaUnit.Point);

        public float GetBorder(YogaEdge edge)
        {
            var border = style.Border[(int)edge];
            if (border.Unit == YogaUnit.Undefined || border.Unit == YogaUnit.Auto)
                return float.NaN;
            return border.Value;
        }

        // Sets a point or percent value; an undefined input clears the unit.
        void SetUnit(ref YogaValue field, float input, YogaUnit unit)
        {
            input = Yoga.FromPublic(input);
            var value = new YogaValue(Yoga.Sanitize(input), Yoga.IsUndefined(input) ? YogaUnit.Undefined : unit);
            if ((field.Value != value.Value && value.Unit != YogaUnit.Undefined) || field.Unit != value.Unit)
            {
                field = value;
                MarkDirtyInternal();
            }
        }

        // Sets a percent value on a property that accepts auto; an undefined input makes it auto.
        void SetPercentOrAuto(ref YogaValue field, float input)
        {
            input = Yoga.FromPublic(input);
            if (field.Value != Yoga.Sanitize(input) || field.Unit != YogaUnit.Percent)
            {
                field.Value = Yoga.Sanitize(input);
                field.Unit = Yoga.IsUndefined(input) ? YogaUnit.Auto : YogaUnit.Percent;
                MarkDirtyInternal();
            }
        }

        void SetAuto(ref YogaValue field)
        {
            if (field.Unit != YogaUnit.Auto)
            {
                field.Value = 0;
                field.Unit = YogaUnit.Auto;
                MarkDirtyInternal();
            }
        }

        // ---- Layout ----

        public void CalculateLayout(float ownerWidth = float.NaN, float ownerHeight = float.NaN, YogaDirection direction = YogaDirection.LTR) =>
            Yoga.CalculateLayout(this, Yoga.FromPublic(ownerWidth), Yoga.FromPublic(ownerHeight), direction);

        public float LayoutX => Yoga.ToPublic(layout.Position[(int)YogaEdge.Left]);
        public float LayoutY => Yoga.ToPublic(layout.Position[(int)YogaEdge.Top]);
        public float LayoutRight => Yoga.ToPublic(layout.Position[(int)YogaEdge.Right]);
        public float LayoutBottom => Yoga.ToPublic(layout.Position[(int)YogaEdge.Bottom]);
        public float LayoutWidth => Yoga.ToPublic(layout.Dimensions[Yoga.DimWidth]);
        public float LayoutHeight => Yoga.ToPublic(layout.Dimensions[Yoga.DimHeight]);

        public float LayoutMarginLeft => ResolvedEdge(layout.Margin, YogaEdge.Left);
        public float LayoutMarginTop => ResolvedEdge(layout.Margin, YogaEdge.Top);
        public float LayoutMarginRight => ResolvedEdge(layout.Margin, YogaEdge.Right);
        public float LayoutMarginBottom => ResolvedEdge(layout.Margin, YogaEdge.Bottom);
        public float LayoutPaddingLeft => ResolvedEdge(layout.Padding, YogaEdge.Left);
        public float LayoutPaddingTop => ResolvedEdge(layout.Padding, YogaEdge.Top);
        public float LayoutPaddingRight => ResolvedEdge(layout.Padding, YogaEdge.Right);
        public float LayoutPaddingBottom => ResolvedEdge(layout.Padding, YogaEdge.Bottom);
        public float LayoutBorderLeft => ResolvedEdge(layout.Border, YogaEdge.Left);
        public float LayoutBorderTop => ResolvedEdge(layout.Border, YogaEdge.Top);
        public float LayoutBorderRight => ResolvedEdge(layout.Border, YogaEdge.Right);
        public float LayoutBorderBottom => ResolvedEdge(layout.Border, YogaEdge.Bottom);

        public YogaDirection LayoutDirection => layout.Direction;

        public bool HadOverflow => layout.HadOverflow;

        /// <summary>The flex basis computed by the last layout; NaN when undefined.</summary>
        public float ComputedFlexBasis => Yoga.ToPublic(layout.ComputedFlexBasis);

        // Left and right map to start and end, swapped in right-to-left layouts.
        float ResolvedEdge(float[] edges, YogaEdge edge)
        {
            if (edge == YogaEdge.Left)
                return Yoga.ToPublic(layout.Direction == YogaDirection.RTL ? edges[(int)YogaEdge.End] : edges[(int)YogaEdge.Start]);
            if (edge == YogaEdge.Right)
                return Yoga.ToPublic(layout.Direction == YogaDirection.RTL ? edges[(int)YogaEdge.Start] : edges[(int)YogaEdge.End]);
            return Yoga.ToPublic(edges[(int)edge]);
        }

        // ---- Node helpers used by the algorithm ----

        internal YogaFloatOptional GetLeadingPosition(YogaFlexDirection axis, float axisSize)
        {
            if (Yoga.IsRow(axis))
            {
                var startPosition = Yoga.ComputedEdgeValue(style.Position, YogaEdge.Start, Yoga.ValueUndefined);
                if (startPosition.Unit != YogaUnit.Undefined)
                    return Yoga.ResolveValue(startPosition, axisSize);
            }
            var leadingPosition = Yoga.ComputedEdgeValue(style.Position, Yoga.Leading[(int)axis], Yoga.ValueUndefined);
            return leadingPosition.Unit == YogaUnit.Undefined
                ? new YogaFloatOptional(0)
                : Yoga.ResolveValue(leadingPosition, axisSize);
        }

        internal YogaFloatOptional GetTrailingPosition(YogaFlexDirection axis, float axisSize)
        {
            if (Yoga.IsRow(axis))
            {
                var endPosition = Yoga.ComputedEdgeValue(style.Position, YogaEdge.End, Yoga.ValueUndefined);
                if (endPosition.Unit != YogaUnit.Undefined)
                    return Yoga.ResolveValue(endPosition, axisSize);
            }
            var trailingPosition = Yoga.ComputedEdgeValue(style.Position, Yoga.Trailing[(int)axis], Yoga.ValueUndefined);
            return trailingPosition.Unit == YogaUnit.Undefined
                ? new YogaFloatOptional(0)
                : Yoga.ResolveValue(trailingPosition, axisSize);
        }

        internal bool IsLeadingPositionDefined(YogaFlexDirection axis) =>
            (Yoga.IsRow(axis) && Yoga.ComputedEdgeValue(style.Position, YogaEdge.Start, Yoga.ValueUndefined).Unit != YogaUnit.Undefined)
            || Yoga.ComputedEdgeValue(style.Position, Yoga.Leading[(int)axis], Yoga.ValueUndefined).Unit != YogaUnit.Undefined;

        internal bool IsTrailingPosDefined(YogaFlexDirection axis) =>
            (Yoga.IsRow(axis) && Yoga.ComputedEdgeValue(style.Position, YogaEdge.End, Yoga.ValueUndefined).Unit != YogaUnit.Undefined)
            || Yoga.ComputedEdgeValue(style.Position, Yoga.Trailing[(int)axis], Yoga.ValueUndefined).Unit != YogaUnit.Undefined;

        internal YogaFloatOptional GetLeadingMargin(YogaFlexDirection axis, float widthSize)
        {
            if (Yoga.IsRow(axis) && style.Margin[(int)YogaEdge.Start].Unit != YogaUnit.Undefined)
                return config.RoundEdge(Yoga.ResolveValueMargin(style.Margin[(int)YogaEdge.Start], widthSize));
            return config.RoundEdge(Yoga.ResolveValueMargin(
                Yoga.ComputedEdgeValue(style.Margin, Yoga.Leading[(int)axis], Yoga.ValueZero), widthSize));
        }

        internal YogaFloatOptional GetTrailingMargin(YogaFlexDirection axis, float widthSize)
        {
            if (Yoga.IsRow(axis) && style.Margin[(int)YogaEdge.End].Unit != YogaUnit.Undefined)
                return config.RoundEdge(Yoga.ResolveValueMargin(style.Margin[(int)YogaEdge.End], widthSize));
            return config.RoundEdge(Yoga.ResolveValueMargin(
                Yoga.ComputedEdgeValue(style.Margin, Yoga.Trailing[(int)axis], Yoga.ValueZero), widthSize));
        }

        internal YogaFloatOptional GetMarginForAxis(YogaFlexDirection axis, float widthSize) =>
            GetLeadingMargin(axis, widthSize) + GetTrailingMargin(axis, widthSize);

        internal float GetLeadingBorder(YogaFlexDirection axis)
        {
            var start = style.Border[(int)YogaEdge.Start];
            if (Yoga.IsRow(axis) && start.Unit != YogaUnit.Undefined && !Yoga.IsUndefined(start.Value) && start.Value >= 0.0f)
                return config.RoundEdge(start.Value);
            float computedEdgeValue = Yoga.ComputedEdgeValue(style.Border, Yoga.Leading[(int)axis], Yoga.ValueZero).Value;
            return config.RoundEdge(Yoga.FloatMax(computedEdgeValue, 0.0f));
        }

        internal float GetTrailingBorder(YogaFlexDirection axis)
        {
            var end = style.Border[(int)YogaEdge.End];
            if (Yoga.IsRow(axis) && end.Unit != YogaUnit.Undefined && !Yoga.IsUndefined(end.Value) && end.Value >= 0.0f)
                return config.RoundEdge(end.Value);
            float computedEdgeValue = Yoga.ComputedEdgeValue(style.Border, Yoga.Trailing[(int)axis], Yoga.ValueZero).Value;
            return config.RoundEdge(Yoga.FloatMax(computedEdgeValue, 0.0f));
        }

        internal YogaFloatOptional GetLeadingPadding(YogaFlexDirection axis, float widthSize)
        {
            var paddingEdgeStart = Yoga.ResolveValue(style.Padding[(int)YogaEdge.Start], widthSize);
            if (Yoga.IsRow(axis) && style.Padding[(int)YogaEdge.Start].Unit != YogaUnit.Undefined
                && !paddingEdgeStart.IsUndefined && paddingEdgeStart.Value > 0.0f)
                return config.RoundEdge(paddingEdgeStart);
            var resolvedValue = Yoga.ResolveValue(
                Yoga.ComputedEdgeValue(style.Padding, Yoga.Leading[(int)axis], Yoga.ValueZero), widthSize);
            return config.RoundEdge(Yoga.FloatOptionalMax(resolvedValue, new YogaFloatOptional(0.0f)));
        }

        internal YogaFloatOptional GetTrailingPadding(YogaFlexDirection axis, float widthSize)
        {
            var paddingEdgeEnd = Yoga.ResolveValue(style.Padding[(int)YogaEdge.End], widthSize);
            if (Yoga.IsRow(axis) && style.Padding[(int)YogaEdge.End].Unit != YogaUnit.Undefined
                && !paddingEdgeEnd.IsUndefined && paddingEdgeEnd.Value >= 0.0f)
                return config.RoundEdge(paddingEdgeEnd);
            var resolvedValue = Yoga.ResolveValue(
                Yoga.ComputedEdgeValue(style.Padding, Yoga.Trailing[(int)axis], Yoga.ValueZero), widthSize);
            return config.RoundEdge(Yoga.FloatOptionalMax(resolvedValue, new YogaFloatOptional(0.0f)));
        }

        internal YogaFloatOptional GetLeadingPaddingAndBorder(YogaFlexDirection axis, float widthSize) =>
            GetLeadingPadding(axis, widthSize) + new YogaFloatOptional(GetLeadingBorder(axis));

        internal YogaFloatOptional GetTrailingPaddingAndBorder(YogaFlexDirection axis, float widthSize) =>
            GetTrailingPadding(axis, widthSize) + new YogaFloatOptional(GetTrailingBorder(axis));

        // Leading position if defined, otherwise minus the trailing position.
        YogaFloatOptional RelativePosition(YogaFlexDirection axis, float axisSize)
        {
            if (IsLeadingPositionDefined(axis))
                return GetLeadingPosition(axis, axisSize);
            var trailingPosition = GetTrailingPosition(axis, axisSize);
            if (!trailingPosition.IsUndefined)
                trailingPosition = new YogaFloatOptional(-1 * trailingPosition.Value);
            return trailingPosition;
        }

        /// <summary>Sets the layout position from the margins and the relative offsets.</summary>
        internal void SetLayoutPositionFromStyle(YogaDirection direction, float mainSize, float crossSize, float ownerWidth)
        {
            // Root nodes are always laid out as LTR, so that they don't get negative positions.
            var directionRespectingRoot = owner != null ? direction : YogaDirection.LTR;
            var mainAxis = Yoga.ResolveFlexDirection(style.FlexDirection, directionRespectingRoot);
            var crossAxis = Yoga.FlexDirectionCross(mainAxis, directionRespectingRoot);

            var relativePositionMain = RelativePosition(mainAxis, mainSize);
            var relativePositionCross = RelativePosition(crossAxis, crossSize);

            layout.Position[(int)Yoga.Leading[(int)mainAxis]] =
                Yoga.Unwrap(GetLeadingMargin(mainAxis, ownerWidth) + relativePositionMain);
            layout.Position[(int)Yoga.Trailing[(int)mainAxis]] =
                Yoga.Unwrap(GetTrailingMargin(mainAxis, ownerWidth) + relativePositionMain);
            layout.Position[(int)Yoga.Leading[(int)crossAxis]] =
                Yoga.Unwrap(GetLeadingMargin(crossAxis, ownerWidth) + relativePositionCross);
            layout.Position[(int)Yoga.Trailing[(int)crossAxis]] =
                Yoga.Unwrap(GetTrailingMargin(crossAxis, ownerWidth) + relativePositionCross);
        }

        internal YogaValue MarginLeadingValue(YogaFlexDirection axis)
        {
            if (Yoga.IsRow(axis) && style.Margin[(int)YogaEdge.Start].Unit != YogaUnit.Undefined)
                return style.Margin[(int)YogaEdge.Start];
            return style.Margin[(int)Yoga.Leading[(int)axis]];
        }

        internal YogaValue MarginTrailingValue(YogaFlexDirection axis)
        {
            if (Yoga.IsRow(axis) && style.Margin[(int)YogaEdge.End].Unit != YogaUnit.Undefined)
                return style.Margin[(int)YogaEdge.End];
            return style.Margin[(int)Yoga.Trailing[(int)axis]];
        }

        internal YogaValue ResolveFlexBasisPtr()
        {
            var flexBasis = style.FlexBasis;
            if (flexBasis.Unit != YogaUnit.Auto && flexBasis.Unit != YogaUnit.Undefined)
                return flexBasis;
            if (!style.Flex.IsUndefined && style.Flex.Value > 0.0f)
                return config.UseWebDefaults ? Yoga.ValueAuto : Yoga.ValueZero;
            return Yoga.ValueAuto;
        }

        // A max dimension equal to the min dimension overrides the dimension.
        internal void ResolveDimension()
        {
            for (int dim = Yoga.DimWidth; dim <= Yoga.DimHeight; dim++)
            {
                if (style.MaxDimensions[dim].Unit != YogaUnit.Undefined
                    && Yoga.ValueEqual(style.MaxDimensions[dim], style.MinDimensions[dim]))
                    resolvedDimensions[dim] = style.MaxDimensions[dim];
                else
                    resolvedDimensions[dim] = style.Dimensions[dim];
            }
        }

        internal YogaDirection ResolveDirection(YogaDirection ownerDirection)
        {
            if (style.Direction == YogaDirection.Inherit)
                return ownerDirection > YogaDirection.Inherit ? ownerDirection : YogaDirection.LTR;
            return style.Direction;
        }

        internal float ResolveFlexGrow()
        {
            // The root never grows.
            if (owner == null)
                return 0.0f;
            if (!style.FlexGrow.IsUndefined)
                return style.FlexGrow.Value;
            if (!style.Flex.IsUndefined && style.Flex.Value > 0.0f)
                return style.Flex.Value;
            return Yoga.DefaultFlexGrow;
        }

        internal float ResolveFlexShrink()
        {
            if (owner == null)
                return 0.0f;
            if (!style.FlexShrink.IsUndefined)
                return style.FlexShrink.Value;
            if (!config.UseWebDefaults && !style.Flex.IsUndefined && style.Flex.Value < 0.0f)
                return -style.Flex.Value;
            return config.UseWebDefaults ? Yoga.WebDefaultFlexShrink : Yoga.DefaultFlexShrink;
        }

        internal bool IsNodeFlexible() =>
            style.PositionType == YogaPositionType.Relative && (ResolveFlexGrow() != 0 || ResolveFlexShrink() != 0);

        internal YogaMeasureFunc Measure => measure;

        internal YogaBaselineFunc Baseline => baseline;
    }

    /// <summary>The flexbox layout algorithm.</summary>
    internal static class Yoga
    {
        /// <summary>The internal representation of an undefined value.</summary>
        public const float Undefined = 10E20f;

        public const int DimWidth = 0;
        public const int DimHeight = 1;

        public const float DefaultFlexGrow = 0.0f;
        public const float DefaultFlexShrink = 0.0f;
        public const float WebDefaultFlexShrink = 1.0f;

        public static readonly YogaValue ValueZero = new YogaValue(0, YogaUnit.Point);
        public static readonly YogaValue ValueUndefined = new YogaValue(Undefined, YogaUnit.Undefined);
        public static readonly YogaValue ValueAuto = new YogaValue(Undefined, YogaUnit.Auto);

        // Edges and dimensions per flex direction (column, column-reverse, row, row-reverse).
        public static readonly YogaEdge[] Leading = { YogaEdge.Top, YogaEdge.Bottom, YogaEdge.Left, YogaEdge.Right };
        public static readonly YogaEdge[] Trailing = { YogaEdge.Bottom, YogaEdge.Top, YogaEdge.Right, YogaEdge.Left };
        static readonly YogaEdge[] Pos = { YogaEdge.Top, YogaEdge.Bottom, YogaEdge.Left, YogaEdge.Right };
        static readonly int[] Dim = { DimHeight, DimHeight, DimWidth, DimWidth };

        // Each layout pass gets a new generation; nodes visited in this pass carry it.
        static uint generationCounter;
        [ThreadStatic] static uint currentGenerationCount;

        // ---- Utilities ----

        public static bool IsUndefined(float value) => value >= 10E8 || value <= -10E8;

        public static float FromPublic(float value) => float.IsNaN(value) ? Undefined : value;

        public static float ToPublic(float value) => IsUndefined(value) ? float.NaN : value;

        public static float ToPublic(YogaFloatOptional value) => value.IsUndefined ? float.NaN : value.Value;

        public static YogaValue ToPublic(YogaValue value) =>
            value.Unit == YogaUnit.Undefined || value.Unit == YogaUnit.Auto
                ? new YogaValue(float.NaN, value.Unit)
                : value;

        public static YogaValue ComputedEdgeValue(YogaValue[] edges, YogaEdge edge, YogaValue defaultValue)
        {
            if (edges[(int)edge].Unit != YogaUnit.Undefined)
                return edges[(int)edge];

            if ((edge == YogaEdge.Top || edge == YogaEdge.Bottom) && edges[(int)YogaEdge.Vertical].Unit != YogaUnit.Undefined)
                return edges[(int)YogaEdge.Vertical];

            if ((edge == YogaEdge.Left || edge == YogaEdge.Right || edge == YogaEdge.Start || edge == YogaEdge.End)
                && edges[(int)YogaEdge.Horizontal].Unit != YogaUnit.Undefined)
                return edges[(int)YogaEdge.Horizontal];

            if (edges[(int)YogaEdge.All].Unit != YogaUnit.Undefined)
                return edges[(int)YogaEdge.All];

            if (edge == YogaEdge.Start || edge == YogaEdge.End)
                return ValueUndefined;

            return defaultValue;
        }

        public static bool ValueEqual(YogaValue a, YogaValue b)
        {
            if (a.Unit != b.Unit)
                return false;
            if (a.Unit == YogaUnit.Undefined || (IsUndefined(a.Value) && IsUndefined(b.Value)))
                return true;
            return MathF.Abs(a.Value - b.Value) < 0.0001f;
        }

        // True if both are undefined or they differ by less than 0.0001.
        public static bool FloatsEqual(float a, float b)
        {
            if (!IsUndefined(a) && !IsUndefined(b))
                return MathF.Abs(a - b) < 0.0001f;
            return IsUndefined(a) && IsUndefined(b);
        }

        // fmaxf / fminf: a NaN operand yields the other one.
        static float Fmaxf(float a, float b)
        {
            if (float.IsNaN(a))
                return b;
            if (float.IsNaN(b))
                return a;
            return a > b ? a : b;
        }

        static float Fminf(float a, float b)
        {
            if (float.IsNaN(a))
                return b;
            if (float.IsNaN(b))
                return a;
            return a < b ? a : b;
        }

        // Max where an undefined operand yields the other one.
        public static float FloatMax(float a, float b)
        {
            if (!IsUndefined(a) && !IsUndefined(b))
                return Fmaxf(a, b);
            return IsUndefined(a) ? b : a;
        }

        public static float FloatMin(float a, float b)
        {
            if (!IsUndefined(a) && !IsUndefined(b))
                return Fminf(a, b);
            return IsUndefined(a) ? b : a;
        }

        public static YogaFloatOptional FloatOptionalMax(YogaFloatOptional op1, YogaFloatOptional op2)
        {
            if (!op1.IsUndefined && !op2.IsUndefined)
                return op1.Value > op2.Value ? op1 : op2;
            return op1.IsUndefined ? op2 : op1;
        }

        public static float Sanitize(float value) => IsUndefined(value) ? 0 : value;

        public static float Unwrap(YogaFloatOptional op) => op.IsUndefined ? Undefined : op.Value;

        public static bool IsRow(YogaFlexDirection flexDirection) =>
            flexDirection == YogaFlexDirection.Row || flexDirection == YogaFlexDirection.RowReverse;

        public static bool IsColumn(YogaFlexDirection flexDirection) =>
            flexDirection == YogaFlexDirection.Column || flexDirection == YogaFlexDirection.ColumnReverse;

        public static YogaFlexDirection ResolveFlexDirection(YogaFlexDirection flexDirection, YogaDirection direction)
        {
            if (direction == YogaDirection.RTL)
            {
                if (flexDirection == YogaFlexDirection.Row)
                    return YogaFlexDirection.RowReverse;
                if (flexDirection == YogaFlexDirection.RowReverse)
                    return YogaFlexDirection.Row;
            }
            return flexDirection;
        }

        public static YogaFlexDirection FlexDirectionCross(YogaFlexDirection flexDirection, YogaDirection direction) =>
            IsColumn(flexDirection) ? ResolveFlexDirection(YogaFlexDirection.Row, direction) : YogaFlexDirection.Column;

        public static YogaFloatOptional ResolveValue(YogaValue value, float ownerSize)
        {
            switch (value.Unit)
            {
                case YogaUnit.Point:
                    return new YogaFloatOptional(value.Value);
                case YogaUnit.Percent:
                    // The percentage scale is applied in double precision.
                    return new YogaFloatOptional((float)(value.Value * ownerSize * 0.01));
                default:
                    return YogaFloatOptional.Undefined;
            }
        }

        public static YogaFloatOptional ResolveValueMargin(YogaValue value, float ownerSize) =>
            value.Unit == YogaUnit.Auto ? new YogaFloatOptional(0) : ResolveValue(value, ownerSize);

        // ---- Layout helpers ----

        static float PaddingAndBorderForAxis(YogaNode node, YogaFlexDirection axis, float widthSize) =>
            Unwrap(node.GetLeadingPaddingAndBorder(axis, widthSize) + node.GetTrailingPaddingAndBorder(axis, widthSize));

        static YogaAlign AlignItem(YogaNode node, YogaNode child)
        {
            var align = child.style.AlignSelf == YogaAlign.Auto ? node.style.AlignItems : child.style.AlignSelf;
            if (align == YogaAlign.Baseline && IsColumn(node.style.FlexDirection))
                return YogaAlign.FlexStart;
            return align;
        }

        static float Baseline(YogaNode node)
        {
            if (node.Baseline != null)
            {
                float baseline = node.Baseline(
                    node,
                    ToPublic(node.layout.MeasuredDimensions[DimWidth]),
                    ToPublic(node.layout.MeasuredDimensions[DimHeight]));
                if (IsUndefined(baseline))
                    throw new InvalidOperationException("Expect custom baseline function to not return NaN");
                return baseline;
            }

            YogaNode baselineChild = null;
            var children = node.Children;
            int childCount = children.Count;
            for (int i = 0; i < childCount; i++)
            {
                var child = children[i];
                if (child.lineIndex > 0)
                    break;
                if (child.style.PositionType == YogaPositionType.Absolute)
                    continue;
                if (AlignItem(node, child) == YogaAlign.Baseline)
                {
                    baselineChild = child;
                    break;
                }
                if (baselineChild == null)
                    baselineChild = child;
            }

            if (baselineChild == null)
                return node.layout.MeasuredDimensions[DimHeight];

            float childBaseline = Baseline(baselineChild);
            return childBaseline + baselineChild.layout.Position[(int)YogaEdge.Top];
        }

        static bool IsBaselineLayout(YogaNode node)
        {
            if (IsColumn(node.style.FlexDirection))
                return false;
            if (node.style.AlignItems == YogaAlign.Baseline)
                return true;
            foreach (var child in node.Children)
            {
                if (child.style.PositionType == YogaPositionType.Relative && child.style.AlignSelf == YogaAlign.Baseline)
                    return true;
            }
            return false;
        }

        static float DimWithMargin(YogaNode node, YogaFlexDirection axis, float widthSize) =>
            node.layout.MeasuredDimensions[Dim[(int)axis]]
            + Unwrap(node.GetLeadingMargin(axis, widthSize) + node.GetTrailingMargin(axis, widthSize));

        static bool IsStyleDimDefined(YogaNode node, YogaFlexDirection axis, float ownerSize)
        {
            var resolved = node.resolvedDimensions[Dim[(int)axis]];
            bool isUndefined = IsUndefined(resolved.Value);
            return !(
                resolved.Unit == YogaUnit.Auto
                || resolved.Unit == YogaUnit.Undefined
                || (resolved.Unit == YogaUnit.Point && !isUndefined && resolved.Value < 0.0f)
                || (resolved.Unit == YogaUnit.Percent && !isUndefined && (resolved.Value < 0.0f || IsUndefined(ownerSize))));
        }

        static bool IsLayoutDimDefined(YogaNode node, YogaFlexDirection axis)
        {
            float value = node.layout.MeasuredDimensions[Dim[(int)axis]];
            return !IsUndefined(value) && value >= 0.0f;
        }

        static YogaFloatOptional BoundAxisWithinMinAndMax(YogaNode node, YogaFlexDirection axis, float value, float axisSize)
        {
            var min = YogaFloatOptional.Undefined;
            var max = YogaFloatOptional.Undefined;

            if (IsColumn(axis))
            {
                min = ResolveValue(node.style.MinDimensions[DimHeight], axisSize);
                max = ResolveValue(node.style.MaxDimensions[DimHeight], axisSize);
            }
            else if (IsRow(axis))
            {
                min = ResolveValue(node.style.MinDimensions[DimWidth], axisSize);
                max = ResolveValue(node.style.MaxDimensions[DimWidth], axisSize);
            }

            if (!max.IsUndefined && max.Value >= 0 && value > max.Value)
                return max;

            if (!min.IsUndefined && min.Value >= 0 && value < min.Value)
                return min;

            return new YogaFloatOptional(value);
        }

        // Like BoundAxisWithinMinAndMax, but never below the padding and border.
        static float BoundAxis(YogaNode node, YogaFlexDirection axis, float value, float axisSize, float widthSize) =>
            FloatMax(
                Unwrap(BoundAxisWithinMinAndMax(node, axis, value, axisSize)),
                PaddingAndBorderForAxis(node, axis, widthSize));

        static void SetChildTrailingPosition(YogaNode node, YogaNode child, YogaFlexDirection axis)
        {
            float size = child.layout.MeasuredDimensions[Dim[(int)axis]];
            child.layout.Position[(int)Trailing[(int)axis]] =
                node.layout.MeasuredDimensions[Dim[(int)axis]] - size - child.layout.Position[(int)Pos[(int)axis]];
        }

        static void ConstrainMaxSizeForMode(YogaNode node, YogaFlexDirection axis, float ownerAxisSize, float ownerWidth,
            ref YogaMeasureMode mode, ref float size)
        {
            var maxSize = ResolveValue(node.style.MaxDimensions[Dim[(int)axis]], ownerAxisSize)
                + node.GetMarginForAxis(axis, ownerWidth);
            switch (mode)
            {
                case YogaMeasureMode.Exactly:
                case YogaMeasureMode.AtMost:
                    size = (maxSize.IsUndefined || size < maxSize.Value) ? size : maxSize.Value;
                    break;
                case YogaMeasureMode.Undefined:
                    if (!maxSize.IsUndefined)
                    {
                        mode = YogaMeasureMode.AtMost;
                        size = maxSize.Value;
                    }
                    break;
            }
        }

        static void ComputeFlexBasisForChild(YogaNode node, YogaNode child, float width, YogaMeasureMode widthMode,
            float height, float ownerWidth, float ownerHeight, YogaMeasureMode heightMode, YogaDirection direction,
            YogaConfig config)
        {
            var mainAxis = ResolveFlexDirection(node.style.FlexDirection, direction);
            bool isMainAxisRow = IsRow(mainAxis);
            float mainAxisSize = isMainAxisRow ? width : height;
            float mainAxisOwnerSize = isMainAxisRow ? ownerWidth : ownerHeight;

            float childWidth;
            float childHeight;
            YogaMeasureMode childWidthMeasureMode;
            YogaMeasureMode childHeightMeasureMode;

            var resolvedFlexBasis = ResolveValue(child.ResolveFlexBasisPtr(), mainAxisOwnerSize);
            // UI Toolkit resolves a percentage flex basis only against a container whose main size is
            // exact; measured against an indefinite one it sizes to content (as CSS does): a toggle's
            // input (flex: 1 0 0) contributes its checkmark's width to the toggle's measured width.
            if (child.config.PercentFlexBasisNeedsExactContainer && child.ResolveFlexBasisPtr().Unit == YogaUnit.Percent
                && (isMainAxisRow ? widthMode : heightMode) != YogaMeasureMode.Exactly)
                resolvedFlexBasis = YogaFloatOptional.Undefined;
            bool isRowStyleDimDefined = IsStyleDimDefined(child, YogaFlexDirection.Row, ownerWidth);
            bool isColumnStyleDimDefined = IsStyleDimDefined(child, YogaFlexDirection.Column, ownerHeight);

            if (!resolvedFlexBasis.IsUndefined && !IsUndefined(mainAxisSize))
            {
                // The web flex-basis experiment is off, so a computed flex basis is reused.
                if (child.layout.ComputedFlexBasis.IsUndefined)
                {
                    var paddingAndBorder = new YogaFloatOptional(PaddingAndBorderForAxis(child, mainAxis, ownerWidth));
                    child.layout.ComputedFlexBasis = FloatOptionalMax(resolvedFlexBasis, paddingAndBorder);
                }
            }
            else if (isMainAxisRow && isRowStyleDimDefined)
            {
                // The width is definite, so use that as the flex basis.
                var paddingAndBorder = new YogaFloatOptional(PaddingAndBorderForAxis(child, YogaFlexDirection.Row, ownerWidth));
                child.layout.ComputedFlexBasis = FloatOptionalMax(
                    ResolveValue(child.resolvedDimensions[DimWidth], ownerWidth), paddingAndBorder);
            }
            else if (!isMainAxisRow && isColumnStyleDimDefined)
            {
                // The height is definite, so use that as the flex basis.
                var paddingAndBorder = new YogaFloatOptional(PaddingAndBorderForAxis(child, YogaFlexDirection.Column, ownerWidth));
                child.layout.ComputedFlexBasis = FloatOptionalMax(
                    ResolveValue(child.resolvedDimensions[DimHeight], ownerHeight), paddingAndBorder);
            }
            else
            {
                // Compute the flex basis and hypothetical main size (the clamped flex basis).
                childWidth = Undefined;
                childHeight = Undefined;
                childWidthMeasureMode = YogaMeasureMode.Undefined;
                childHeightMeasureMode = YogaMeasureMode.Undefined;

                float marginRow = Unwrap(child.GetMarginForAxis(YogaFlexDirection.Row, ownerWidth));
                float marginColumn = Unwrap(child.GetMarginForAxis(YogaFlexDirection.Column, ownerWidth));

                if (isRowStyleDimDefined)
                {
                    childWidth = Unwrap(ResolveValue(child.resolvedDimensions[DimWidth], ownerWidth)) + marginRow;
                    childWidthMeasureMode = YogaMeasureMode.Exactly;
                }
                if (isColumnStyleDimDefined)
                {
                    childHeight = Unwrap(ResolveValue(child.resolvedDimensions[DimHeight], ownerHeight)) + marginColumn;
                    childHeightMeasureMode = YogaMeasureMode.Exactly;
                }

                // Overflow: scroll leaves the main axis unconstrained, as browsers do (in UI Toolkit hidden
                // too: a scroll view's content container measures its whole content in its hidden viewport).
                bool scrolls = node.style.Overflow == YogaOverflow.Scroll
                    || (node.config.OverflowHiddenMeasuresContent && node.style.Overflow == YogaOverflow.Hidden);
                if ((!isMainAxisRow && scrolls) || !scrolls)
                {
                    if (IsUndefined(childWidth) && !IsUndefined(width))
                    {
                        childWidth = width;
                        childWidthMeasureMode = YogaMeasureMode.AtMost;
                    }
                }

                if ((isMainAxisRow && scrolls) || !scrolls)
                {
                    if (IsUndefined(childHeight) && !IsUndefined(height))
                    {
                        childHeight = height;
                        childHeightMeasureMode = YogaMeasureMode.AtMost;
                    }
                }

                if (!child.style.AspectRatio.IsUndefined)
                {
                    if (!isMainAxisRow && childWidthMeasureMode == YogaMeasureMode.Exactly)
                    {
                        childHeight = marginColumn + (childWidth - marginRow) / child.style.AspectRatio.Value;
                        childHeightMeasureMode = YogaMeasureMode.Exactly;
                    }
                    else if (isMainAxisRow && childHeightMeasureMode == YogaMeasureMode.Exactly)
                    {
                        childWidth = marginRow + (childHeight - marginColumn) * child.style.AspectRatio.Value;
                        childWidthMeasureMode = YogaMeasureMode.Exactly;
                    }
                }

                // A child without a cross size that stretches is measured exactly with the available
                // inner cross size.
                bool hasExactWidth = !IsUndefined(width) && widthMode == YogaMeasureMode.Exactly;
                bool childWidthStretch = AlignItem(node, child) == YogaAlign.Stretch
                    && childWidthMeasureMode != YogaMeasureMode.Exactly;
                if (!isMainAxisRow && !isRowStyleDimDefined && hasExactWidth && childWidthStretch)
                {
                    childWidth = width;
                    childWidthMeasureMode = YogaMeasureMode.Exactly;
                    if (!child.style.AspectRatio.IsUndefined)
                    {
                        childHeight = (childWidth - marginRow) / child.style.AspectRatio.Value;
                        childHeightMeasureMode = YogaMeasureMode.Exactly;
                    }
                }

                bool hasExactHeight = !IsUndefined(height) && heightMode == YogaMeasureMode.Exactly;
                bool childHeightStretch = AlignItem(node, child) == YogaAlign.Stretch
                    && childHeightMeasureMode != YogaMeasureMode.Exactly;
                if (isMainAxisRow && !isColumnStyleDimDefined && hasExactHeight && childHeightStretch)
                {
                    childHeight = height;
                    childHeightMeasureMode = YogaMeasureMode.Exactly;

                    if (!child.style.AspectRatio.IsUndefined)
                    {
                        childWidth = (childHeight - marginColumn) * child.style.AspectRatio.Value;
                        childWidthMeasureMode = YogaMeasureMode.Exactly;
                    }
                }

                ConstrainMaxSizeForMode(child, YogaFlexDirection.Row, ownerWidth, ownerWidth,
                    ref childWidthMeasureMode, ref childWidth);
                ConstrainMaxSizeForMode(child, YogaFlexDirection.Column, ownerHeight, ownerWidth,
                    ref childHeightMeasureMode, ref childHeight);

                // Measure the child.
                LayoutNodeInternal(child, childWidth, childHeight, direction, childWidthMeasureMode,
                    childHeightMeasureMode, ownerWidth, ownerHeight, false, config);

                child.layout.ComputedFlexBasis = new YogaFloatOptional(FloatMax(
                    child.layout.MeasuredDimensions[Dim[(int)mainAxis]],
                    PaddingAndBorderForAxis(child, mainAxis, ownerWidth)));
            }
            child.layout.ComputedFlexBasisGeneration = currentGenerationCount;
        }

        static void AbsoluteLayoutChild(YogaNode node, YogaNode child, float width, YogaMeasureMode widthMode,
            float height, YogaDirection direction, YogaConfig config)
        {
            var mainAxis = ResolveFlexDirection(node.style.FlexDirection, direction);
            var crossAxis = FlexDirectionCross(mainAxis, direction);
            bool isMainAxisRow = IsRow(mainAxis);

            float childWidth = Undefined;
            float childHeight = Undefined;
            var childWidthMeasureMode = YogaMeasureMode.Undefined;
            var childHeightMeasureMode = YogaMeasureMode.Undefined;

            float marginRow = Unwrap(child.GetMarginForAxis(YogaFlexDirection.Row, width));
            float marginColumn = Unwrap(child.GetMarginForAxis(YogaFlexDirection.Column, width));

            if (IsStyleDimDefined(child, YogaFlexDirection.Row, width))
            {
                childWidth = Unwrap(ResolveValue(child.resolvedDimensions[DimWidth], width)) + marginRow;
            }
            else
            {
                // Without a width, use the left and right offsets if both are defined.
                if (child.IsLeadingPositionDefined(YogaFlexDirection.Row) && child.IsTrailingPosDefined(YogaFlexDirection.Row))
                {
                    childWidth = node.layout.MeasuredDimensions[DimWidth]
                        - (node.GetLeadingBorder(YogaFlexDirection.Row) + node.GetTrailingBorder(YogaFlexDirection.Row))
                        - Unwrap(child.GetLeadingPosition(YogaFlexDirection.Row, width)
                            + child.GetTrailingPosition(YogaFlexDirection.Row, width));
                    childWidth = BoundAxis(child, YogaFlexDirection.Row, childWidth, width, width);
                }
            }

            if (IsStyleDimDefined(child, YogaFlexDirection.Column, height))
            {
                childHeight = Unwrap(ResolveValue(child.resolvedDimensions[DimHeight], height)) + marginColumn;
            }
            else
            {
                // Without a height, use the top and bottom offsets if both are defined.
                if (child.IsLeadingPositionDefined(YogaFlexDirection.Column) && child.IsTrailingPosDefined(YogaFlexDirection.Column))
                {
                    childHeight = node.layout.MeasuredDimensions[DimHeight]
                        - (node.GetLeadingBorder(YogaFlexDirection.Column) + node.GetTrailingBorder(YogaFlexDirection.Column))
                        - Unwrap(child.GetLeadingPosition(YogaFlexDirection.Column, height)
                            + child.GetTrailingPosition(YogaFlexDirection.Column, height));
                    childHeight = BoundAxis(child, YogaFlexDirection.Column, childHeight, height, width);
                }
            }

            // The aspect ratio needs exactly one defined dimension to derive the other.
            if (IsUndefined(childWidth) ^ IsUndefined(childHeight))
            {
                if (!child.style.AspectRatio.IsUndefined)
                {
                    if (IsUndefined(childWidth))
                        childWidth = marginRow + (childHeight - marginColumn) * child.style.AspectRatio.Value;
                    else if (IsUndefined(childHeight))
                        childHeight = marginColumn + (childWidth - marginRow) / child.style.AspectRatio.Value;
                }
            }

            // If a dimension is still missing, measure the content.
            if (IsUndefined(childWidth) || IsUndefined(childHeight))
            {
                childWidthMeasureMode = IsUndefined(childWidth) ? YogaMeasureMode.Undefined : YogaMeasureMode.Exactly;
                childHeightMeasureMode = IsUndefined(childHeight) ? YogaMeasureMode.Undefined : YogaMeasureMode.Exactly;

                // Constrain the child to a defined owner width, so that text wraps inside it.
                if (!isMainAxisRow && IsUndefined(childWidth) && widthMode != YogaMeasureMode.Undefined
                    && !IsUndefined(width) && width > 0)
                {
                    childWidth = width;
                    childWidthMeasureMode = YogaMeasureMode.AtMost;
                }

                LayoutNodeInternal(child, childWidth, childHeight, direction, childWidthMeasureMode,
                    childHeightMeasureMode, childWidth, childHeight, false, config);
                childWidth = child.layout.MeasuredDimensions[DimWidth]
                    + Unwrap(child.GetMarginForAxis(YogaFlexDirection.Row, width));
                childHeight = child.layout.MeasuredDimensions[DimHeight]
                    + Unwrap(child.GetMarginForAxis(YogaFlexDirection.Column, width));
            }

            LayoutNodeInternal(child, childWidth, childHeight, direction, YogaMeasureMode.Exactly,
                YogaMeasureMode.Exactly, childWidth, childHeight, true, config);

            if (child.IsTrailingPosDefined(mainAxis) && !child.IsLeadingPositionDefined(mainAxis))
            {
                child.layout.Position[(int)Leading[(int)mainAxis]] =
                    node.layout.MeasuredDimensions[Dim[(int)mainAxis]]
                    - child.layout.MeasuredDimensions[Dim[(int)mainAxis]]
                    - node.GetTrailingBorder(mainAxis)
                    - Unwrap(child.GetTrailingMargin(mainAxis, width))
                    - Unwrap(child.GetTrailingPosition(mainAxis, isMainAxisRow ? width : height));
            }
            else if (!child.IsLeadingPositionDefined(mainAxis) && node.style.JustifyContent == YogaJustify.Center)
            {
                child.layout.Position[(int)Leading[(int)mainAxis]] =
                    (node.layout.MeasuredDimensions[Dim[(int)mainAxis]]
                     - child.layout.MeasuredDimensions[Dim[(int)mainAxis]]) / 2.0f;
            }
            else if (!child.IsLeadingPositionDefined(mainAxis) && node.style.JustifyContent == YogaJustify.FlexEnd)
            {
                child.layout.Position[(int)Leading[(int)mainAxis]] =
                    node.layout.MeasuredDimensions[Dim[(int)mainAxis]]
                    - child.layout.MeasuredDimensions[Dim[(int)mainAxis]];
            }

            if (child.IsTrailingPosDefined(crossAxis) && !child.IsLeadingPositionDefined(crossAxis))
            {
                child.layout.Position[(int)Leading[(int)crossAxis]] =
                    node.layout.MeasuredDimensions[Dim[(int)crossAxis]]
                    - child.layout.MeasuredDimensions[Dim[(int)crossAxis]]
                    - node.GetTrailingBorder(crossAxis)
                    - Unwrap(child.GetTrailingMargin(crossAxis, width))
                    - Unwrap(child.GetTrailingPosition(crossAxis, isMainAxisRow ? height : width));
            }
            else if (!child.IsLeadingPositionDefined(crossAxis) && AlignItem(node, child) == YogaAlign.Center)
            {
                child.layout.Position[(int)Leading[(int)crossAxis]] =
                    (node.layout.MeasuredDimensions[Dim[(int)crossAxis]]
                     - child.layout.MeasuredDimensions[Dim[(int)crossAxis]]) / 2.0f;
            }
            else if (!child.IsLeadingPositionDefined(crossAxis)
                     && ((AlignItem(node, child) == YogaAlign.FlexEnd) ^ (node.style.FlexWrap == YogaWrap.WrapReverse)))
            {
                child.layout.Position[(int)Leading[(int)crossAxis]] =
                    node.layout.MeasuredDimensions[Dim[(int)crossAxis]]
                    - child.layout.MeasuredDimensions[Dim[(int)crossAxis]];
            }
        }

        static void WithMeasureFuncSetMeasuredDimensions(YogaNode node, float availableWidth, float availableHeight,
            YogaMeasureMode widthMeasureMode, YogaMeasureMode heightMeasureMode, float ownerWidth, float ownerHeight)
        {
            float paddingAndBorderAxisRow = PaddingAndBorderForAxis(node, YogaFlexDirection.Row, availableWidth);
            float paddingAndBorderAxisColumn = PaddingAndBorderForAxis(node, YogaFlexDirection.Column, availableWidth);
            float marginAxisRow = Unwrap(node.GetMarginForAxis(YogaFlexDirection.Row, availableWidth));
            float marginAxisColumn = Unwrap(node.GetMarginForAxis(YogaFlexDirection.Column, availableWidth));

            // The measure function never gets a negative size.
            float innerWidth = IsUndefined(availableWidth)
                ? availableWidth
                : FloatMax(0, availableWidth - marginAxisRow - paddingAndBorderAxisRow);
            float innerHeight = IsUndefined(availableHeight)
                ? availableHeight
                : FloatMax(0, availableHeight - marginAxisColumn - paddingAndBorderAxisColumn);

            if (widthMeasureMode == YogaMeasureMode.Exactly && heightMeasureMode == YogaMeasureMode.Exactly)
            {
                // Both dimensions are already known: no need to measure.
                node.layout.MeasuredDimensions[DimWidth] = BoundAxis(
                    node, YogaFlexDirection.Row, availableWidth - marginAxisRow, ownerWidth, ownerWidth);
                node.layout.MeasuredDimensions[DimHeight] = BoundAxis(
                    node, YogaFlexDirection.Column, availableHeight - marginAxisColumn, ownerHeight, ownerWidth);
            }
            else
            {
                // Measure the content under the current constraints.
                var measuredSize = node.Measure(node, ToPublic(innerWidth), widthMeasureMode, ToPublic(innerHeight), heightMeasureMode);

                node.layout.MeasuredDimensions[DimWidth] = BoundAxis(
                    node,
                    YogaFlexDirection.Row,
                    (widthMeasureMode == YogaMeasureMode.Undefined || widthMeasureMode == YogaMeasureMode.AtMost)
                        ? measuredSize.Width + paddingAndBorderAxisRow
                        : availableWidth - marginAxisRow,
                    ownerWidth,
                    ownerWidth);

                node.layout.MeasuredDimensions[DimHeight] = BoundAxis(
                    node,
                    YogaFlexDirection.Column,
                    (heightMeasureMode == YogaMeasureMode.Undefined || heightMeasureMode == YogaMeasureMode.AtMost)
                        ? measuredSize.Height + paddingAndBorderAxisColumn
                        : availableHeight - marginAxisColumn,
                    ownerHeight,
                    ownerWidth);
            }
        }

        // A node without children takes the available size if given, else its padding and border.
        static void EmptyContainerSetMeasuredDimensions(YogaNode node, float availableWidth, float availableHeight,
            YogaMeasureMode widthMeasureMode, YogaMeasureMode heightMeasureMode, float ownerWidth, float ownerHeight)
        {
            float paddingAndBorderAxisRow = PaddingAndBorderForAxis(node, YogaFlexDirection.Row, ownerWidth);
            float paddingAndBorderAxisColumn = PaddingAndBorderForAxis(node, YogaFlexDirection.Column, ownerWidth);
            float marginAxisRow = Unwrap(node.GetMarginForAxis(YogaFlexDirection.Row, ownerWidth));
            float marginAxisColumn = Unwrap(node.GetMarginForAxis(YogaFlexDirection.Column, ownerWidth));

            node.layout.MeasuredDimensions[DimWidth] = BoundAxis(
                node,
                YogaFlexDirection.Row,
                (widthMeasureMode == YogaMeasureMode.Undefined || widthMeasureMode == YogaMeasureMode.AtMost)
                    ? paddingAndBorderAxisRow
                    : availableWidth - marginAxisRow,
                ownerWidth,
                ownerWidth);

            node.layout.MeasuredDimensions[DimHeight] = BoundAxis(
                node,
                YogaFlexDirection.Column,
                (heightMeasureMode == YogaMeasureMode.Undefined || heightMeasureMode == YogaMeasureMode.AtMost)
                    ? paddingAndBorderAxisColumn
                    : availableHeight - marginAxisColumn,
                ownerHeight,
                ownerWidth);
        }

        static bool FixedSizeSetMeasuredDimensions(YogaNode node, float availableWidth, float availableHeight,
            YogaMeasureMode widthMeasureMode, YogaMeasureMode heightMeasureMode, float ownerWidth, float ownerHeight)
        {
            if ((!IsUndefined(availableWidth) && widthMeasureMode == YogaMeasureMode.AtMost && availableWidth <= 0.0f)
                || (!IsUndefined(availableHeight) && heightMeasureMode == YogaMeasureMode.AtMost && availableHeight <= 0.0f)
                || (widthMeasureMode == YogaMeasureMode.Exactly && heightMeasureMode == YogaMeasureMode.Exactly))
            {
                float marginAxisColumn = Unwrap(node.GetMarginForAxis(YogaFlexDirection.Column, ownerWidth));
                float marginAxisRow = Unwrap(node.GetMarginForAxis(YogaFlexDirection.Row, ownerWidth));

                node.layout.MeasuredDimensions[DimWidth] = BoundAxis(
                    node,
                    YogaFlexDirection.Row,
                    IsUndefined(availableWidth) || (widthMeasureMode == YogaMeasureMode.AtMost && availableWidth < 0.0f)
                        ? 0.0f
                        : availableWidth - marginAxisRow,
                    ownerWidth,
                    ownerWidth);

                node.layout.MeasuredDimensions[DimHeight] = BoundAxis(
                    node,
                    YogaFlexDirection.Column,
                    IsUndefined(availableHeight) || (heightMeasureMode == YogaMeasureMode.AtMost && availableHeight < 0.0f)
                        ? 0.0f
                        : availableHeight - marginAxisColumn,
                    ownerHeight,
                    ownerWidth);
                return true;
            }

            return false;
        }

        static void ZeroOutLayoutRecursively(YogaNode node)
        {
            node.layout.Zero();
            node.HasNewLayout = true;
            foreach (var child in node.Children)
                ZeroOutLayoutRecursively(child);
        }

        static float CalculateAvailableInnerDim(YogaNode node, YogaFlexDirection axis, float availableDim, float ownerDim)
        {
            var direction = IsRow(axis) ? YogaFlexDirection.Row : YogaFlexDirection.Column;
            int dimension = IsRow(axis) ? DimWidth : DimHeight;

            float margin = Unwrap(node.GetMarginForAxis(direction, ownerDim));
            float paddingAndBorder = PaddingAndBorderForAxis(node, direction, ownerDim);

            float availableInnerDim = availableDim - margin - paddingAndBorder;
            // The max dimension overrides the available size, and the min dimension overrides both.
            if (!IsUndefined(availableInnerDim))
            {
                var minDimensionOptional = ResolveValue(node.style.MinDimensions[dimension], ownerDim);
                float minInnerDim = minDimensionOptional.IsUndefined
                    ? 0.0f
                    : minDimensionOptional.Value - paddingAndBorder;

                var maxDimensionOptional = ResolveValue(node.style.MaxDimensions[dimension], ownerDim);
                float maxInnerDim = maxDimensionOptional.IsUndefined
                    ? float.MaxValue
                    : maxDimensionOptional.Value - paddingAndBorder;
                availableInnerDim = FloatMax(FloatMin(availableInnerDim, maxInnerDim), minInnerDim);
            }

            return availableInnerDim;
        }

        static void ComputeFlexBasisForChildren(YogaNode node, float availableInnerWidth, float availableInnerHeight,
            YogaMeasureMode widthMeasureMode, YogaMeasureMode heightMeasureMode, YogaDirection direction,
            YogaFlexDirection mainAxis, YogaConfig config, bool performLayout, ref float totalOuterFlexBasis)
        {
            YogaNode singleFlexChild = null;
            var children = node.Children;
            var measureModeMainDim = IsRow(mainAxis) ? widthMeasureMode : heightMeasureMode;
            // A single child that both grows and shrinks gets a flex basis of 0 instead of being
            // measured and then flexed to fill the remaining space exactly.
            if (measureModeMainDim == YogaMeasureMode.Exactly)
            {
                foreach (var child in children)
                {
                    if (child.IsNodeFlexible())
                    {
                        if (singleFlexChild != null
                            || FloatsEqual(child.ResolveFlexGrow(), 0.0f)
                            || FloatsEqual(child.ResolveFlexShrink(), 0.0f))
                        {
                            // There is already a flexible child, or this one doesn't both grow and shrink.
                            singleFlexChild = null;
                            break;
                        }
                        singleFlexChild = child;
                    }
                }
            }

            foreach (var child in children)
            {
                child.ResolveDimension();
                if (child.style.Display == YogaDisplay.None)
                {
                    ZeroOutLayoutRecursively(child);
                    child.HasNewLayout = true;
                    child.SetDirty(false);
                    continue;
                }
                if (performLayout)
                {
                    // Set the initial position (relative to the owner).
                    var childDirection = child.ResolveDirection(direction);
                    float mainDim = IsRow(mainAxis) ? availableInnerWidth : availableInnerHeight;
                    float crossDim = IsRow(mainAxis) ? availableInnerHeight : availableInnerWidth;
                    child.SetLayoutPositionFromStyle(childDirection, mainDim, crossDim, availableInnerWidth);
                }

                if (child.style.PositionType == YogaPositionType.Absolute)
                    continue;
                if (child == singleFlexChild)
                {
                    child.layout.ComputedFlexBasisGeneration = currentGenerationCount;
                    child.layout.ComputedFlexBasis = new YogaFloatOptional(0);
                }
                else
                {
                    ComputeFlexBasisForChild(node, child, availableInnerWidth, widthMeasureMode, availableInnerHeight,
                        availableInnerWidth, availableInnerHeight, heightMeasureMode, direction, config);
                }

                totalOuterFlexBasis += Unwrap(
                    child.layout.ComputedFlexBasis + child.GetMarginForAxis(mainAxis, availableInnerWidth));
            }
        }

        /// <summary>The flex items of one line and their accumulated sizes and factors.</summary>
        sealed class CollectFlexItemsRowValues
        {
            // Items that fit on the line (absolute children excluded).
            public uint ItemsOnLine;
            // Sum of the items' flex bases and margins on the line.
            public float SizeConsumedOnCurrentLine;
            public float TotalFlexGrowFactors;
            public float TotalFlexShrinkScaledFactors;
            // One past the last child examined for the line.
            public uint EndOfLineIndex;
            public readonly List<YogaNode> RelativeChildren = new List<YogaNode>();
            public float RemainingFreeSpace;
            // The line's main size with sizes, padding, margins and borders; items are placed along it in
            // double precision so that long runs of items keep their positions (as UI Toolkit's layout:
            // measured at 1.25 pixels per point, the 20th 24.4pt item of a list lies at 467.6, not 467.59991).
            public double MainDim;
            // The line's cross size with sizes, padding, margins and borders.
            public float CrossDim;

            public void Reset()
            {
                ItemsOnLine = 0;
                SizeConsumedOnCurrentLine = 0;
                TotalFlexGrowFactors = 0;
                TotalFlexShrinkScaledFactors = 0;
                EndOfLineIndex = 0;
                RelativeChildren.Clear();
                RemainingFreeSpace = 0;
                MainDim = 0;
                CrossDim = 0;
            }
        }

        // Collects the items of the line starting at startOfLineIndex (flex bases already computed).
        static void CalculateCollectFlexItemsRowValues(CollectFlexItemsRowValues values, YogaNode node,
            YogaDirection ownerDirection, float mainAxisOwnerSize, float availableInnerWidth,
            float availableInnerMainDim, uint startOfLineIndex, uint lineCount)
        {
            values.Reset();

            float sizeConsumedOnCurrentLineIncludingMinConstraint = 0;
            var mainAxis = ResolveFlexDirection(node.style.FlexDirection, node.ResolveDirection(ownerDirection));
            bool isNodeFlexWrap = node.style.FlexWrap != YogaWrap.NoWrap;
            var children = node.Children;

            // Add items to the line until it is full or there are no items left.
            uint endOfLineIndex = startOfLineIndex;
            for (; endOfLineIndex < (uint)children.Count; endOfLineIndex++)
            {
                var child = children[(int)endOfLineIndex];
                if (child.style.Display == YogaDisplay.None || child.style.PositionType == YogaPositionType.Absolute)
                    continue;
                child.lineIndex = lineCount;
                float childMarginMainAxis = Unwrap(child.GetMarginForAxis(mainAxis, availableInnerWidth));
                float flexBasisWithMinAndMaxConstraints = Unwrap(BoundAxisWithinMinAndMax(
                    child, mainAxis, Unwrap(child.layout.ComputedFlexBasis), mainAxisOwnerSize));

                // In a multi-line container, an item that overflows the line starts the next one.
                if (sizeConsumedOnCurrentLineIncludingMinConstraint + flexBasisWithMinAndMaxConstraints
                        + childMarginMainAxis > availableInnerMainDim
                    && isNodeFlexWrap && values.ItemsOnLine > 0)
                    break;

                sizeConsumedOnCurrentLineIncludingMinConstraint += flexBasisWithMinAndMaxConstraints + childMarginMainAxis;
                values.SizeConsumedOnCurrentLine += flexBasisWithMinAndMaxConstraints + childMarginMainAxis;
                values.ItemsOnLine++;

                if (child.IsNodeFlexible())
                {
                    values.TotalFlexGrowFactors += child.ResolveFlexGrow();

                    // The shrink factor is scaled by the item's flex basis.
                    values.TotalFlexShrinkScaledFactors +=
                        -child.ResolveFlexShrink() * Unwrap(child.layout.ComputedFlexBasis);
                }

                values.RelativeChildren.Add(child);
            }

            // The total flex factors are floored to 1.
            if (values.TotalFlexGrowFactors > 0 && values.TotalFlexGrowFactors < 1)
                values.TotalFlexGrowFactors = 1;

            if (values.TotalFlexShrinkScaledFactors > 0 && values.TotalFlexShrinkScaledFactors < 1)
                values.TotalFlexShrinkScaledFactors = 1;
            values.EndOfLineIndex = endOfLineIndex;
        }

        // Sizes the flexible items of the line within their min and max, and lays them out.
        // Returns the free space consumed.
        static float DistributeFreeSpaceSecondPass(CollectFlexItemsRowValues values, YogaNode node,
            YogaFlexDirection mainAxis, YogaFlexDirection crossAxis, float mainAxisOwnerSize,
            float availableInnerMainDim, float availableInnerCrossDim, float availableInnerWidth,
            float availableInnerHeight, bool flexBasisOverflows, YogaMeasureMode measureModeCrossDim,
            bool performLayout, YogaConfig config)
        {
            float childFlexBasis = 0;
            float flexShrinkScaledFactor = 0;
            float flexGrowFactor = 0;
            float deltaFreeSpace = 0;
            bool isMainAxisRow = IsRow(mainAxis);
            bool isNodeFlexWrap = node.style.FlexWrap != YogaWrap.NoWrap;

            foreach (var currentRelativeChild in values.RelativeChildren)
            {
                childFlexBasis = Unwrap(BoundAxisWithinMinAndMax(
                    currentRelativeChild, mainAxis, Unwrap(currentRelativeChild.layout.ComputedFlexBasis), mainAxisOwnerSize));
                float updatedMainSize = childFlexBasis;

                if (!IsUndefined(values.RemainingFreeSpace) && values.RemainingFreeSpace < 0)
                {
                    flexShrinkScaledFactor = -currentRelativeChild.ResolveFlexShrink() * childFlexBasis;
                    // Can this child shrink?
                    if (flexShrinkScaledFactor != 0)
                    {
                        float childSize;

                        if (!IsUndefined(values.TotalFlexShrinkScaledFactors) && values.TotalFlexShrinkScaledFactors == 0)
                            childSize = childFlexBasis + flexShrinkScaledFactor;
                        else
                            childSize = childFlexBasis
                                + (values.RemainingFreeSpace / values.TotalFlexShrinkScaledFactors) * flexShrinkScaledFactor;

                        updatedMainSize = BoundAxis(currentRelativeChild, mainAxis, childSize, availableInnerMainDim,
                            availableInnerWidth);
                    }
                }
                else if (!IsUndefined(values.RemainingFreeSpace) && values.RemainingFreeSpace > 0)
                {
                    flexGrowFactor = currentRelativeChild.ResolveFlexGrow();

                    // Can this child grow?
                    if (!IsUndefined(flexGrowFactor) && flexGrowFactor != 0)
                    {
                        updatedMainSize = BoundAxis(
                            currentRelativeChild,
                            mainAxis,
                            childFlexBasis + values.RemainingFreeSpace / values.TotalFlexGrowFactors * flexGrowFactor,
                            availableInnerMainDim,
                            availableInnerWidth);
                    }
                }

                deltaFreeSpace += updatedMainSize - childFlexBasis;

                float marginMain = Unwrap(currentRelativeChild.GetMarginForAxis(mainAxis, availableInnerWidth));
                float marginCross = Unwrap(currentRelativeChild.GetMarginForAxis(crossAxis, availableInnerWidth));

                float childCrossSize;
                float childMainSize = updatedMainSize + marginMain;
                YogaMeasureMode childCrossMeasureMode;
                var childMainMeasureMode = YogaMeasureMode.Exactly;

                if (!currentRelativeChild.style.AspectRatio.IsUndefined)
                {
                    childCrossSize = isMainAxisRow
                        ? (childMainSize - marginMain) / currentRelativeChild.style.AspectRatio.Value
                        : (childMainSize - marginMain) * currentRelativeChild.style.AspectRatio.Value;
                    childCrossMeasureMode = YogaMeasureMode.Exactly;

                    childCrossSize += marginCross;
                }
                else if (!IsUndefined(availableInnerCrossDim)
                         && !IsStyleDimDefined(currentRelativeChild, crossAxis, availableInnerCrossDim)
                         && measureModeCrossDim == YogaMeasureMode.Exactly
                         && !(isNodeFlexWrap && flexBasisOverflows)
                         && AlignItem(node, currentRelativeChild) == YogaAlign.Stretch
                         && currentRelativeChild.MarginLeadingValue(crossAxis).Unit != YogaUnit.Auto
                         && currentRelativeChild.MarginTrailingValue(crossAxis).Unit != YogaUnit.Auto)
                {
                    childCrossSize = availableInnerCrossDim;
                    childCrossMeasureMode = YogaMeasureMode.Exactly;
                }
                else if (!IsStyleDimDefined(currentRelativeChild, crossAxis, availableInnerCrossDim))
                {
                    childCrossSize = availableInnerCrossDim;
                    childCrossMeasureMode = IsUndefined(childCrossSize) ? YogaMeasureMode.Undefined : YogaMeasureMode.AtMost;
                }
                else
                {
                    childCrossSize = Unwrap(ResolveValue(
                            currentRelativeChild.resolvedDimensions[Dim[(int)crossAxis]], availableInnerCrossDim))
                        + marginCross;
                    bool isLoosePercentageMeasurement =
                        currentRelativeChild.resolvedDimensions[Dim[(int)crossAxis]].Unit == YogaUnit.Percent
                        && measureModeCrossDim != YogaMeasureMode.Exactly;
                    childCrossMeasureMode = IsUndefined(childCrossSize) || isLoosePercentageMeasurement
                        ? YogaMeasureMode.Undefined
                        : YogaMeasureMode.Exactly;
                }

                ConstrainMaxSizeForMode(currentRelativeChild, mainAxis, availableInnerMainDim, availableInnerWidth,
                    ref childMainMeasureMode, ref childMainSize);
                ConstrainMaxSizeForMode(currentRelativeChild, crossAxis, availableInnerCrossDim, availableInnerWidth,
                    ref childCrossMeasureMode, ref childCrossSize);

                bool requiresStretchLayout =
                    !IsStyleDimDefined(currentRelativeChild, crossAxis, availableInnerCrossDim)
                    && AlignItem(node, currentRelativeChild) == YogaAlign.Stretch
                    && currentRelativeChild.MarginLeadingValue(crossAxis).Unit != YogaUnit.Auto
                    && currentRelativeChild.MarginTrailingValue(crossAxis).Unit != YogaUnit.Auto;

                float childWidth = isMainAxisRow ? childMainSize : childCrossSize;
                float childHeight = !isMainAxisRow ? childMainSize : childCrossSize;

                var childWidthMeasureMode = isMainAxisRow ? childMainMeasureMode : childCrossMeasureMode;
                var childHeightMeasureMode = !isMainAxisRow ? childMainMeasureMode : childCrossMeasureMode;

                // Lay out the child with its flexed main size.
                LayoutNodeInternal(currentRelativeChild, childWidth, childHeight, node.layout.Direction,
                    childWidthMeasureMode, childHeightMeasureMode, availableInnerWidth, availableInnerHeight,
                    performLayout && !requiresStretchLayout, config);
                node.layout.HadOverflow = node.layout.HadOverflow | currentRelativeChild.layout.HadOverflow;
            }
            return deltaFreeSpace;
        }

        // Finds the flexible items whose min or max constraint triggers, and takes their clamped
        // sizes and flex factors out of the remaining free space.
        static void DistributeFreeSpaceFirstPass(CollectFlexItemsRowValues values, YogaFlexDirection mainAxis,
            float mainAxisOwnerSize, float availableInnerMainDim, float availableInnerWidth)
        {
            float flexShrinkScaledFactor = 0;
            float flexGrowFactor = 0;
            float baseMainSize = 0;
            float boundMainSize = 0;
            float deltaFreeSpace = 0;

            foreach (var currentRelativeChild in values.RelativeChildren)
            {
                float childFlexBasis = Unwrap(BoundAxisWithinMinAndMax(
                    currentRelativeChild, mainAxis, Unwrap(currentRelativeChild.layout.ComputedFlexBasis), mainAxisOwnerSize));

                if (values.RemainingFreeSpace < 0)
                {
                    flexShrinkScaledFactor = -currentRelativeChild.ResolveFlexShrink() * childFlexBasis;

                    // Can this child shrink?
                    if (!IsUndefined(flexShrinkScaledFactor) && flexShrinkScaledFactor != 0)
                    {
                        baseMainSize = childFlexBasis
                            + values.RemainingFreeSpace / values.TotalFlexShrinkScaledFactors * flexShrinkScaledFactor;
                        boundMainSize = BoundAxis(currentRelativeChild, mainAxis, baseMainSize, availableInnerMainDim,
                            availableInnerWidth);
                        if (!IsUndefined(baseMainSize) && !IsUndefined(boundMainSize) && baseMainSize != boundMainSize)
                        {
                            // Excluding the clamped item makes its constraint trigger again in the
                            // second pass, with the same size.
                            deltaFreeSpace += boundMainSize - childFlexBasis;
                            values.TotalFlexShrinkScaledFactors -= flexShrinkScaledFactor;
                        }
                    }
                }
                else if (!IsUndefined(values.RemainingFreeSpace) && values.RemainingFreeSpace > 0)
                {
                    flexGrowFactor = currentRelativeChild.ResolveFlexGrow();

                    // Can this child grow?
                    if (!IsUndefined(flexGrowFactor) && flexGrowFactor != 0)
                    {
                        baseMainSize = childFlexBasis
                            + values.RemainingFreeSpace / values.TotalFlexGrowFactors * flexGrowFactor;
                        boundMainSize = BoundAxis(currentRelativeChild, mainAxis, baseMainSize, availableInnerMainDim,
                            availableInnerWidth);

                        if (!IsUndefined(baseMainSize) && !IsUndefined(boundMainSize) && baseMainSize != boundMainSize)
                        {
                            // Excluding the clamped item makes its constraint trigger again in the
                            // second pass, with the same size.
                            deltaFreeSpace += boundMainSize - childFlexBasis;
                            values.TotalFlexGrowFactors -= flexGrowFactor;
                        }
                    }
                }
            }
            values.RemainingFreeSpace -= deltaFreeSpace;
        }

        // Distributes the remaining space in two passes: the first freezes the items whose min/max
        // constraints trigger, the second sizes every item (a fixed number of passes, unlike the spec's
        // loop).
        static void ResolveFlexibleLength(YogaNode node, CollectFlexItemsRowValues values, YogaFlexDirection mainAxis,
            YogaFlexDirection crossAxis, float mainAxisOwnerSize, float availableInnerMainDim,
            float availableInnerCrossDim, float availableInnerWidth, float availableInnerHeight,
            bool flexBasisOverflows, YogaMeasureMode measureModeCrossDim, bool performLayout, YogaConfig config)
        {
            float originalFreeSpace = values.RemainingFreeSpace;
            // First pass: find the items whose min/max constraints trigger.
            DistributeFreeSpaceFirstPass(values, mainAxis, mainAxisOwnerSize, availableInnerMainDim, availableInnerWidth);

            // Second pass: size the flexible items.
            float distributedFreeSpace = DistributeFreeSpaceSecondPass(values, node, mainAxis, crossAxis,
                mainAxisOwnerSize, availableInnerMainDim, availableInnerCrossDim, availableInnerWidth,
                availableInnerHeight, flexBasisOverflows, measureModeCrossDim, performLayout, config);

            values.RemainingFreeSpace = originalFreeSpace - distributedFreeSpace;
        }

        static void JustifyMainAxis(YogaNode node, CollectFlexItemsRowValues values, uint startOfLineIndex,
            YogaFlexDirection mainAxis, YogaFlexDirection crossAxis, YogaMeasureMode measureModeMainDim,
            YogaMeasureMode measureModeCrossDim, float mainAxisOwnerSize, float ownerWidth,
            float availableInnerMainDim, float availableInnerCrossDim, float availableInnerWidth, bool performLayout)
        {
            var style = node.style;
            var children = node.Children;

            // With an "at most" main size, the remaining space is what the min size leaves.
            if (measureModeMainDim == YogaMeasureMode.AtMost && values.RemainingFreeSpace > 0)
            {
                if (style.MinDimensions[Dim[(int)mainAxis]].Unit != YogaUnit.Undefined
                    && !ResolveValue(style.MinDimensions[Dim[(int)mainAxis]], mainAxisOwnerSize).IsUndefined)
                {
                    values.RemainingFreeSpace = FloatMax(
                        0,
                        Unwrap(ResolveValue(style.MinDimensions[Dim[(int)mainAxis]], mainAxisOwnerSize))
                        - (availableInnerMainDim - values.RemainingFreeSpace));
                }
                else
                {
                    values.RemainingFreeSpace = 0;
                }
            }

            int numberOfAutoMarginsOnCurrentLine = 0;
            for (uint i = startOfLineIndex; i < values.EndOfLineIndex; i++)
            {
                var child = children[(int)i];
                if (child.style.PositionType == YogaPositionType.Relative)
                {
                    if (child.MarginLeadingValue(mainAxis).Unit == YogaUnit.Auto)
                        numberOfAutoMarginsOnCurrentLine++;
                    if (child.MarginTrailingValue(mainAxis).Unit == YogaUnit.Auto)
                        numberOfAutoMarginsOnCurrentLine++;
                }
            }

            // The space before the first item, and the space between items.
            float leadingMainDim = 0;
            float betweenMainDim = 0;
            var justifyContent = node.style.JustifyContent;

            if (numberOfAutoMarginsOnCurrentLine == 0)
            {
                switch (justifyContent)
                {
                    case YogaJustify.Center:
                        leadingMainDim = values.RemainingFreeSpace / 2;
                        break;
                    case YogaJustify.FlexEnd:
                        leadingMainDim = values.RemainingFreeSpace;
                        break;
                    case YogaJustify.SpaceBetween:
                        if (values.ItemsOnLine > 1)
                            betweenMainDim = FloatMax(values.RemainingFreeSpace, 0) / (values.ItemsOnLine - 1);
                        else
                            betweenMainDim = 0;
                        break;
                    case YogaJustify.SpaceEvenly:
                        // The space is distributed evenly around all items.
                        betweenMainDim = values.RemainingFreeSpace / (values.ItemsOnLine + 1);
                        leadingMainDim = betweenMainDim;
                        break;
                    case YogaJustify.SpaceAround:
                        // The space on the edges is half the space between items.
                        betweenMainDim = values.RemainingFreeSpace / values.ItemsOnLine;
                        leadingMainDim = betweenMainDim / 2;
                        break;
                    case YogaJustify.FlexStart:
                        break;
                }
            }

            float leadingPaddingAndBorderMain = Unwrap(node.GetLeadingPaddingAndBorder(mainAxis, ownerWidth));
            values.MainDim = leadingPaddingAndBorderMain + leadingMainDim;
            values.CrossDim = 0;

            for (uint i = startOfLineIndex; i < values.EndOfLineIndex; i++)
            {
                var child = children[(int)i];
                var childStyle = child.style;
                var childLayout = child.layout;
                if (childStyle.Display == YogaDisplay.None)
                    continue;
                if (childStyle.PositionType == YogaPositionType.Absolute && child.IsLeadingPositionDefined(mainAxis))
                {
                    if (performLayout)
                    {
                        // An absolute child with a leading offset is placed at that offset (plus border
                        // and margin).
                        child.layout.Position[(int)Pos[(int)mainAxis]] =
                            Unwrap(child.GetLeadingPosition(mainAxis, availableInnerMainDim))
                            + node.GetLeadingBorder(mainAxis)
                            + Unwrap(child.GetLeadingMargin(mainAxis, availableInnerWidth));
                    }
                }
                else
                {
                    // Only relative children take part in the main-axis placement.
                    if (childStyle.PositionType == YogaPositionType.Relative)
                    {
                        if (child.MarginLeadingValue(mainAxis).Unit == YogaUnit.Auto)
                            values.MainDim += values.RemainingFreeSpace / numberOfAutoMarginsOnCurrentLine;

                        if (performLayout)
                        {
                            child.layout.Position[(int)Pos[(int)mainAxis]] =
                                (float)(childLayout.Position[(int)Pos[(int)mainAxis]] + values.MainDim);
                        }

                        if (child.MarginTrailingValue(mainAxis).Unit == YogaUnit.Auto)
                            values.MainDim += values.RemainingFreeSpace / numberOfAutoMarginsOnCurrentLine;

                        bool canSkipFlex = !performLayout && measureModeCrossDim == YogaMeasureMode.Exactly;
                        if (canSkipFlex)
                        {
                            // The flex step was skipped, so the measured dimensions are not valid:
                            // use the flex basis.
                            values.MainDim += betweenMainDim
                                + Unwrap(child.GetMarginForAxis(mainAxis, availableInnerWidth))
                                + Unwrap(childLayout.ComputedFlexBasis);
                            values.CrossDim = availableInnerCrossDim;
                        }
                        else
                        {
                            // The main size is the sum of the items and the spacing.
                            values.MainDim += betweenMainDim + DimWithMargin(child, mainAxis, availableInnerWidth);

                            // The cross size is the largest item.
                            values.CrossDim = FloatMax(values.CrossDim, DimWithMargin(child, crossAxis, availableInnerWidth));
                        }
                    }
                    else if (performLayout)
                    {
                        child.layout.Position[(int)Pos[(int)mainAxis]] =
                            childLayout.Position[(int)Pos[(int)mainAxis]] + node.GetLeadingBorder(mainAxis) + leadingMainDim;
                    }
                }
            }
            values.MainDim += Unwrap(node.GetTrailingPaddingAndBorder(mainAxis, ownerWidth));
        }

        // Lays out a node and, when performLayout is set, its subtree (otherwise it only measures).
        //
        // Measure modes map to the CSS sizing terms: Undefined is max-content, Exactly is
        // fill-available and AtMost is fit-content. An undefined available size must come with the
        // Undefined measure mode. measuredDimensions include padding and border but not margins.
        static void NodeLayoutImpl(YogaNode node, float availableWidth, float availableHeight,
            YogaDirection ownerDirection, YogaMeasureMode widthMeasureMode, YogaMeasureMode heightMeasureMode,
            float ownerWidth, float ownerHeight, bool performLayout, YogaConfig config)
        {
            if (IsUndefined(availableWidth) && widthMeasureMode != YogaMeasureMode.Undefined)
                throw new InvalidOperationException("availableWidth is indefinite so widthMeasureMode must be Undefined");
            if (IsUndefined(availableHeight) && heightMeasureMode != YogaMeasureMode.Undefined)
                throw new InvalidOperationException("availableHeight is indefinite so heightMeasureMode must be Undefined");

            // Resolve the direction.
            var direction = node.ResolveDirection(ownerDirection);
            node.layout.Direction = direction;

            var flexRowDirection = ResolveFlexDirection(YogaFlexDirection.Row, direction);
            var flexColumnDirection = ResolveFlexDirection(YogaFlexDirection.Column, direction);

            node.layout.Margin[(int)YogaEdge.Start] = Unwrap(node.GetLeadingMargin(flexRowDirection, ownerWidth));
            node.layout.Margin[(int)YogaEdge.End] = Unwrap(node.GetTrailingMargin(flexRowDirection, ownerWidth));
            node.layout.Margin[(int)YogaEdge.Top] = Unwrap(node.GetLeadingMargin(flexColumnDirection, ownerWidth));
            node.layout.Margin[(int)YogaEdge.Bottom] = Unwrap(node.GetTrailingMargin(flexColumnDirection, ownerWidth));

            node.layout.Border[(int)YogaEdge.Start] = node.GetLeadingBorder(flexRowDirection);
            node.layout.Border[(int)YogaEdge.End] = node.GetTrailingBorder(flexRowDirection);
            node.layout.Border[(int)YogaEdge.Top] = node.GetLeadingBorder(flexColumnDirection);
            node.layout.Border[(int)YogaEdge.Bottom] = node.GetTrailingBorder(flexColumnDirection);

            node.layout.Padding[(int)YogaEdge.Start] = Unwrap(node.GetLeadingPadding(flexRowDirection, ownerWidth));
            node.layout.Padding[(int)YogaEdge.End] = Unwrap(node.GetTrailingPadding(flexRowDirection, ownerWidth));
            node.layout.Padding[(int)YogaEdge.Top] = Unwrap(node.GetLeadingPadding(flexColumnDirection, ownerWidth));
            node.layout.Padding[(int)YogaEdge.Bottom] = Unwrap(node.GetTrailingPadding(flexColumnDirection, ownerWidth));

            if (node.Measure != null)
            {
                WithMeasureFuncSetMeasuredDimensions(node, availableWidth, availableHeight, widthMeasureMode,
                    heightMeasureMode, ownerWidth, ownerHeight);
                return;
            }

            var children = node.Children;
            uint childCount = (uint)children.Count;
            if (childCount == 0)
            {
                EmptyContainerSetMeasuredDimensions(node, availableWidth, availableHeight, widthMeasureMode,
                    heightMeasureMode, ownerWidth, ownerHeight);
                return;
            }

            // When only measuring, a known size makes the algorithm unnecessary.
            if (!performLayout && FixedSizeSetMeasuredDimensions(node, availableWidth, availableHeight,
                    widthMeasureMode, heightMeasureMode, ownerWidth, ownerHeight))
                return;

            // Reset the layout flags, as they could have changed.
            node.layout.HadOverflow = false;

            // STEP 1: CALCULATE VALUES FOR REMAINDER OF ALGORITHM
            var mainAxis = ResolveFlexDirection(node.style.FlexDirection, direction);
            var crossAxis = FlexDirectionCross(mainAxis, direction);
            bool isMainAxisRow = IsRow(mainAxis);
            bool isNodeFlexWrap = node.style.FlexWrap != YogaWrap.NoWrap;

            float mainAxisOwnerSize = isMainAxisRow ? ownerWidth : ownerHeight;
            float crossAxisOwnerSize = isMainAxisRow ? ownerHeight : ownerWidth;

            float leadingPaddingAndBorderCross = Unwrap(node.GetLeadingPaddingAndBorder(crossAxis, ownerWidth));
            float paddingAndBorderAxisMain = PaddingAndBorderForAxis(node, mainAxis, ownerWidth);
            float paddingAndBorderAxisCross = PaddingAndBorderForAxis(node, crossAxis, ownerWidth);

            var measureModeMainDim = isMainAxisRow ? widthMeasureMode : heightMeasureMode;
            var measureModeCrossDim = isMainAxisRow ? heightMeasureMode : widthMeasureMode;

            float paddingAndBorderAxisRow = isMainAxisRow ? paddingAndBorderAxisMain : paddingAndBorderAxisCross;
            float paddingAndBorderAxisColumn = isMainAxisRow ? paddingAndBorderAxisCross : paddingAndBorderAxisMain;

            float marginAxisRow = Unwrap(node.GetMarginForAxis(YogaFlexDirection.Row, ownerWidth));
            float marginAxisColumn = Unwrap(node.GetMarginForAxis(YogaFlexDirection.Column, ownerWidth));

            float minInnerWidth = Unwrap(ResolveValue(node.style.MinDimensions[DimWidth], ownerWidth)) - paddingAndBorderAxisRow;
            float maxInnerWidth = Unwrap(ResolveValue(node.style.MaxDimensions[DimWidth], ownerWidth)) - paddingAndBorderAxisRow;
            float minInnerHeight = Unwrap(ResolveValue(node.style.MinDimensions[DimHeight], ownerHeight)) - paddingAndBorderAxisColumn;
            float maxInnerHeight = Unwrap(ResolveValue(node.style.MaxDimensions[DimHeight], ownerHeight)) - paddingAndBorderAxisColumn;

            float minInnerMainDim = isMainAxisRow ? minInnerWidth : minInnerHeight;
            float maxInnerMainDim = isMainAxisRow ? maxInnerWidth : maxInnerHeight;

            // STEP 2: DETERMINE AVAILABLE SIZE IN MAIN AND CROSS DIRECTIONS

            float availableInnerWidth = CalculateAvailableInnerDim(node, YogaFlexDirection.Row, availableWidth, ownerWidth);
            float availableInnerHeight = CalculateAvailableInnerDim(node, YogaFlexDirection.Column, availableHeight, ownerHeight);

            float availableInnerMainDim = isMainAxisRow ? availableInnerWidth : availableInnerHeight;
            float availableInnerCrossDim = isMainAxisRow ? availableInnerHeight : availableInnerWidth;

            float totalOuterFlexBasis = 0;

            // STEP 3: DETERMINE FLEX BASIS FOR EACH ITEM

            ComputeFlexBasisForChildren(node, availableInnerWidth, availableInnerHeight, widthMeasureMode,
                heightMeasureMode, direction, mainAxis, config, performLayout, ref totalOuterFlexBasis);

            bool flexBasisOverflows = measureModeMainDim == YogaMeasureMode.Undefined
                ? false
                : totalOuterFlexBasis > availableInnerMainDim;
            if (isNodeFlexWrap && flexBasisOverflows && measureModeMainDim == YogaMeasureMode.AtMost)
                measureModeMainDim = YogaMeasureMode.Exactly;

            // STEP 4: COLLECT FLEX ITEMS INTO FLEX LINES

            // Indexes of the first and one past the last items of the line.
            uint startOfLineIndex = 0;
            uint endOfLineIndex = 0;

            uint lineCount = 0;

            // Cross size of the lines so far.
            float totalLineCrossDim = 0;

            // Largest main size of the lines.
            float maxLineMainDim = 0;
            var collectedFlexItemsValues = new CollectFlexItemsRowValues();
            for (; endOfLineIndex < childCount; lineCount++, startOfLineIndex = endOfLineIndex)
            {
                CalculateCollectFlexItemsRowValues(collectedFlexItemsValues, node, ownerDirection, mainAxisOwnerSize,
                    availableInnerWidth, availableInnerMainDim, startOfLineIndex, lineCount);
                endOfLineIndex = collectedFlexItemsValues.EndOfLineIndex;

                // Without the cross axis to measure, the flex step can be skipped.
                bool canSkipFlex = !performLayout && measureModeCrossDim == YogaMeasureMode.Exactly;

                // STEP 5: RESOLVING FLEXIBLE LENGTHS ON MAIN AXIS
                // An unknown main size comes from the line itself, leaving no space to distribute.

                bool sizeBasedOnContent = false;
                // Without an exact main size, respect the min and max.
                if (measureModeMainDim != YogaMeasureMode.Exactly)
                {
                    if (!IsUndefined(minInnerMainDim) && collectedFlexItemsValues.SizeConsumedOnCurrentLine < minInnerMainDim)
                    {
                        availableInnerMainDim = minInnerMainDim;
                    }
                    else if (!IsUndefined(maxInnerMainDim) && collectedFlexItemsValues.SizeConsumedOnCurrentLine > maxInnerMainDim)
                    {
                        availableInnerMainDim = maxInnerMainDim;
                    }
                    else
                    {
                        // Both conditions require an undefined factor equal to 0, so this never applies.
                        if (!node.config.UseLegacyStretchBehaviour
                            && ((IsUndefined(collectedFlexItemsValues.TotalFlexGrowFactors)
                                 && collectedFlexItemsValues.TotalFlexGrowFactors == 0)
                                || (IsUndefined(node.ResolveFlexGrow()) && node.ResolveFlexGrow() == 0)))
                        {
                            // Nothing to flex: the space used is all the space needed.
                            availableInnerMainDim = collectedFlexItemsValues.SizeConsumedOnCurrentLine;
                        }

                        if (node.config.UseLegacyStretchBehaviour)
                            node.layout.DidUseLegacyFlag = true;
                        sizeBasedOnContent = !node.config.UseLegacyStretchBehaviour;
                    }
                }

                if (!sizeBasedOnContent && !IsUndefined(availableInnerMainDim))
                {
                    collectedFlexItemsValues.RemainingFreeSpace =
                        availableInnerMainDim - collectedFlexItemsValues.SizeConsumedOnCurrentLine;
                }
                else if (collectedFlexItemsValues.SizeConsumedOnCurrentLine < 0)
                {
                    // Sized by content with a negative consumed size: the node gives its content 0,
                    // so the remaining space is minus the consumed size.
                    collectedFlexItemsValues.RemainingFreeSpace = -collectedFlexItemsValues.SizeConsumedOnCurrentLine;
                }

                if (!canSkipFlex)
                {
                    ResolveFlexibleLength(node, collectedFlexItemsValues, mainAxis, crossAxis, mainAxisOwnerSize,
                        availableInnerMainDim, availableInnerCrossDim, availableInnerWidth, availableInnerHeight,
                        flexBasisOverflows, measureModeCrossDim, performLayout, config);
                }

                node.layout.HadOverflow = node.layout.HadOverflow | (collectedFlexItemsValues.RemainingFreeSpace < 0);

                // STEP 6: MAIN-AXIS JUSTIFICATION & CROSS-AXIS SIZE DETERMINATION

                // All children now have their main size, and their cross size except for stretched
                // items, which are handled below along with the final positions.

                JustifyMainAxis(node, collectedFlexItemsValues, startOfLineIndex, mainAxis, crossAxis,
                    measureModeMainDim, measureModeCrossDim, mainAxisOwnerSize, ownerWidth, availableInnerMainDim,
                    availableInnerCrossDim, availableInnerWidth, performLayout);

                float containerCrossAxis = availableInnerCrossDim;
                if (measureModeCrossDim == YogaMeasureMode.Undefined || measureModeCrossDim == YogaMeasureMode.AtMost)
                {
                    // The cross size is the largest cross size of the children.
                    containerCrossAxis = BoundAxis(node, crossAxis,
                            collectedFlexItemsValues.CrossDim + paddingAndBorderAxisCross, crossAxisOwnerSize, ownerWidth)
                        - paddingAndBorderAxisCross;
                }

                // Without wrapping, the container defines the cross size.
                if (!isNodeFlexWrap && measureModeCrossDim == YogaMeasureMode.Exactly)
                    collectedFlexItemsValues.CrossDim = availableInnerCrossDim;

                // Clamp to the container's min and max.
                collectedFlexItemsValues.CrossDim = BoundAxis(node, crossAxis,
                        collectedFlexItemsValues.CrossDim + paddingAndBorderAxisCross, crossAxisOwnerSize, ownerWidth)
                    - paddingAndBorderAxisCross;

                // STEP 7: CROSS-AXIS ALIGNMENT
                // Only needed for a full layout.
                if (performLayout)
                {
                    for (uint i = startOfLineIndex; i < endOfLineIndex; i++)
                    {
                        var child = children[(int)i];
                        if (child.style.Display == YogaDisplay.None)
                            continue;
                        if (child.style.PositionType == YogaPositionType.Absolute)
                        {
                            // An absolute child with offsets is placed from them.
                            bool isChildLeadingPosDefined = child.IsLeadingPositionDefined(crossAxis);
                            if (isChildLeadingPosDefined)
                            {
                                child.layout.Position[(int)Pos[(int)crossAxis]] =
                                    Unwrap(child.GetLeadingPosition(crossAxis, availableInnerCrossDim))
                                    + node.GetLeadingBorder(crossAxis)
                                    + Unwrap(child.GetLeadingMargin(crossAxis, availableInnerWidth));
                            }
                            // Without a leading offset, or with an undefined result, use the border and margin.
                            if (!isChildLeadingPosDefined || IsUndefined(child.layout.Position[(int)Pos[(int)crossAxis]]))
                            {
                                child.layout.Position[(int)Pos[(int)crossAxis]] =
                                    node.GetLeadingBorder(crossAxis)
                                    + Unwrap(child.GetLeadingMargin(crossAxis, availableInnerWidth));
                            }
                        }
                        else
                        {
                            float leadingCrossDim = leadingPaddingAndBorderCross;

                            // Relative children align with alignItems (owner) or alignSelf (child).
                            var alignItem = AlignItem(node, child);

                            // A stretched child is laid out again with the line's cross size.
                            if (alignItem == YogaAlign.Stretch
                                && child.MarginLeadingValue(crossAxis).Unit != YogaUnit.Auto
                                && child.MarginTrailingValue(crossAxis).Unit != YogaUnit.Auto)
                            {
                                // A definite cross size doesn't stretch.
                                if (!IsStyleDimDefined(child, crossAxis, availableInnerCrossDim))
                                {
                                    float childMainSize = child.layout.MeasuredDimensions[Dim[(int)mainAxis]];
                                    float childCrossSize = !child.style.AspectRatio.IsUndefined
                                        ? Unwrap(child.GetMarginForAxis(crossAxis, availableInnerWidth))
                                          + (isMainAxisRow
                                              ? childMainSize / child.style.AspectRatio.Value
                                              : childMainSize * child.style.AspectRatio.Value)
                                        : collectedFlexItemsValues.CrossDim;

                                    childMainSize += Unwrap(child.GetMarginForAxis(mainAxis, availableInnerWidth));

                                    var childMainMeasureMode = YogaMeasureMode.Exactly;
                                    var childCrossMeasureMode = YogaMeasureMode.Exactly;
                                    ConstrainMaxSizeForMode(child, mainAxis, availableInnerMainDim, availableInnerWidth,
                                        ref childMainMeasureMode, ref childMainSize);
                                    ConstrainMaxSizeForMode(child, crossAxis, availableInnerCrossDim, availableInnerWidth,
                                        ref childCrossMeasureMode, ref childCrossSize);

                                    float childWidth = isMainAxisRow ? childMainSize : childCrossSize;
                                    float childHeight = !isMainAxisRow ? childMainSize : childCrossSize;

                                    var childWidthMeasureMode = IsUndefined(childWidth) ? YogaMeasureMode.Undefined : YogaMeasureMode.Exactly;
                                    var childHeightMeasureMode = IsUndefined(childHeight) ? YogaMeasureMode.Undefined : YogaMeasureMode.Exactly;

                                    LayoutNodeInternal(child, childWidth, childHeight, direction, childWidthMeasureMode,
                                        childHeightMeasureMode, availableInnerWidth, availableInnerHeight, true, config);
                                }
                            }
                            else
                            {
                                float remainingCrossDim = containerCrossAxis - DimWithMargin(child, crossAxis, availableInnerWidth);

                                if (child.MarginLeadingValue(crossAxis).Unit == YogaUnit.Auto
                                    && child.MarginTrailingValue(crossAxis).Unit == YogaUnit.Auto)
                                {
                                    leadingCrossDim += FloatMax(0.0f, remainingCrossDim / 2);
                                }
                                else if (child.MarginTrailingValue(crossAxis).Unit == YogaUnit.Auto)
                                {
                                    // Nothing to add.
                                }
                                else if (child.MarginLeadingValue(crossAxis).Unit == YogaUnit.Auto)
                                {
                                    leadingCrossDim += FloatMax(0.0f, remainingCrossDim);
                                }
                                else if (alignItem == YogaAlign.FlexStart)
                                {
                                    // Nothing to add.
                                }
                                else if (alignItem == YogaAlign.Center)
                                {
                                    leadingCrossDim += remainingCrossDim / 2;
                                }
                                else
                                {
                                    leadingCrossDim += remainingCrossDim;
                                }
                            }
                            // Apply the position.
                            child.layout.Position[(int)Pos[(int)crossAxis]] =
                                child.layout.Position[(int)Pos[(int)crossAxis]] + totalLineCrossDim + leadingCrossDim;
                        }
                    }
                }

                totalLineCrossDim += collectedFlexItemsValues.CrossDim;
                maxLineMainDim = FloatMax(maxLineMainDim, (float)collectedFlexItemsValues.MainDim);
            }

            // STEP 8: MULTI-LINE CONTENT ALIGNMENT
            if (performLayout && (lineCount > 1 || IsBaselineLayout(node)) && !IsUndefined(availableInnerCrossDim))
            {
                float remainingAlignContentDim = availableInnerCrossDim - totalLineCrossDim;

                float crossDimLead = 0;
                float currentLead = leadingPaddingAndBorderCross;

                switch (node.style.AlignContent)
                {
                    case YogaAlign.FlexEnd:
                        currentLead += remainingAlignContentDim;
                        break;
                    case YogaAlign.Center:
                        currentLead += remainingAlignContentDim / 2;
                        break;
                    case YogaAlign.Stretch:
                        if (availableInnerCrossDim > totalLineCrossDim)
                            crossDimLead = remainingAlignContentDim / lineCount;
                        break;
                    case YogaAlign.SpaceAround:
                        if (availableInnerCrossDim > totalLineCrossDim)
                        {
                            currentLead += remainingAlignContentDim / (2 * lineCount);
                            if (lineCount > 1)
                                crossDimLead = remainingAlignContentDim / lineCount;
                        }
                        else
                        {
                            currentLead += remainingAlignContentDim / 2;
                        }
                        break;
                    case YogaAlign.SpaceBetween:
                        if (availableInnerCrossDim > totalLineCrossDim && lineCount > 1)
                            crossDimLead = remainingAlignContentDim / (lineCount - 1);
                        break;
                    case YogaAlign.Auto:
                    case YogaAlign.FlexStart:
                    case YogaAlign.Baseline:
                        break;
                }

                uint endIndex = 0;
                for (uint i = 0; i < lineCount; i++)
                {
                    uint startIndex = endIndex;
                    uint ii;

                    // Compute the line's height and find its end.
                    float lineHeight = 0;
                    float maxAscentForCurrentLine = 0;
                    float maxDescentForCurrentLine = 0;
                    for (ii = startIndex; ii < childCount; ii++)
                    {
                        var child = children[(int)ii];
                        if (child.style.Display == YogaDisplay.None)
                            continue;
                        if (child.style.PositionType == YogaPositionType.Relative)
                        {
                            if (child.lineIndex != i)
                                break;
                            if (IsLayoutDimDefined(child, crossAxis))
                            {
                                lineHeight = FloatMax(lineHeight,
                                    child.layout.MeasuredDimensions[Dim[(int)crossAxis]]
                                    + Unwrap(child.GetMarginForAxis(crossAxis, availableInnerWidth)));
                            }
                            if (AlignItem(node, child) == YogaAlign.Baseline)
                            {
                                float ascent = Baseline(child)
                                    + Unwrap(child.GetLeadingMargin(YogaFlexDirection.Column, availableInnerWidth));
                                float descent = child.layout.MeasuredDimensions[DimHeight]
                                    + Unwrap(child.GetMarginForAxis(YogaFlexDirection.Column, availableInnerWidth))
                                    - ascent;
                                maxAscentForCurrentLine = FloatMax(maxAscentForCurrentLine, ascent);
                                maxDescentForCurrentLine = FloatMax(maxDescentForCurrentLine, descent);
                                lineHeight = FloatMax(lineHeight, maxAscentForCurrentLine + maxDescentForCurrentLine);
                            }
                        }
                    }
                    endIndex = ii;
                    lineHeight += crossDimLead;

                    if (performLayout)
                    {
                        for (ii = startIndex; ii < endIndex; ii++)
                        {
                            var child = children[(int)ii];
                            if (child.style.Display == YogaDisplay.None)
                                continue;
                            if (child.style.PositionType == YogaPositionType.Relative)
                            {
                                switch (AlignItem(node, child))
                                {
                                    case YogaAlign.FlexStart:
                                        child.layout.Position[(int)Pos[(int)crossAxis]] =
                                            currentLead + Unwrap(child.GetLeadingMargin(crossAxis, availableInnerWidth));
                                        break;
                                    case YogaAlign.FlexEnd:
                                        child.layout.Position[(int)Pos[(int)crossAxis]] =
                                            currentLead + lineHeight
                                            - Unwrap(child.GetTrailingMargin(crossAxis, availableInnerWidth))
                                            - child.layout.MeasuredDimensions[Dim[(int)crossAxis]];
                                        break;
                                    case YogaAlign.Center:
                                    {
                                        float childHeight = child.layout.MeasuredDimensions[Dim[(int)crossAxis]];
                                        child.layout.Position[(int)Pos[(int)crossAxis]] =
                                            currentLead + (lineHeight - childHeight) / 2;
                                        break;
                                    }
                                    case YogaAlign.Stretch:
                                    {
                                        child.layout.Position[(int)Pos[(int)crossAxis]] =
                                            currentLead + Unwrap(child.GetLeadingMargin(crossAxis, availableInnerWidth));

                                        // Measure the child again with the line height (it was measured
                                        // with the owner's height).
                                        if (!IsStyleDimDefined(child, crossAxis, availableInnerCrossDim))
                                        {
                                            float childWidth = isMainAxisRow
                                                ? child.layout.MeasuredDimensions[DimWidth]
                                                  + Unwrap(child.GetMarginForAxis(mainAxis, availableInnerWidth))
                                                : lineHeight;

                                            float childHeight = !isMainAxisRow
                                                ? child.layout.MeasuredDimensions[DimHeight]
                                                  + Unwrap(child.GetMarginForAxis(crossAxis, availableInnerWidth))
                                                : lineHeight;

                                            if (!(FloatsEqual(childWidth, child.layout.MeasuredDimensions[DimWidth])
                                                  && FloatsEqual(childHeight, child.layout.MeasuredDimensions[DimHeight])))
                                            {
                                                LayoutNodeInternal(child, childWidth, childHeight, direction,
                                                    YogaMeasureMode.Exactly, YogaMeasureMode.Exactly,
                                                    availableInnerWidth, availableInnerHeight, true, config);
                                            }
                                        }
                                        break;
                                    }
                                    case YogaAlign.Baseline:
                                        child.layout.Position[(int)YogaEdge.Top] =
                                            currentLead + maxAscentForCurrentLine - Baseline(child)
                                            + Unwrap(child.GetLeadingPosition(YogaFlexDirection.Column, availableInnerCrossDim));
                                        break;
                                    case YogaAlign.Auto:
                                    case YogaAlign.SpaceBetween:
                                    case YogaAlign.SpaceAround:
                                        break;
                                }
                            }
                        }
                    }

                    currentLead += lineHeight;
                }
            }

            // STEP 9: COMPUTING FINAL DIMENSIONS

            node.layout.MeasuredDimensions[DimWidth] = BoundAxis(
                node, YogaFlexDirection.Row, availableWidth - marginAxisRow, ownerWidth, ownerWidth);

            node.layout.MeasuredDimensions[DimHeight] = BoundAxis(
                node, YogaFlexDirection.Column, availableHeight - marginAxisColumn, ownerHeight, ownerWidth);

            // Without a given size, the size comes from the children.
            if (measureModeMainDim == YogaMeasureMode.Undefined
                || (node.style.Overflow != YogaOverflow.Scroll && measureModeMainDim == YogaMeasureMode.AtMost))
            {
                // Clamp to the min/max size, and to at least the padding and border.
                node.layout.MeasuredDimensions[Dim[(int)mainAxis]] =
                    BoundAxis(node, mainAxis, maxLineMainDim, mainAxisOwnerSize, ownerWidth);
            }
            else if (measureModeMainDim == YogaMeasureMode.AtMost && node.style.Overflow == YogaOverflow.Scroll)
            {
                node.layout.MeasuredDimensions[Dim[(int)mainAxis]] = FloatMax(
                    FloatMin(
                        availableInnerMainDim + paddingAndBorderAxisMain,
                        Unwrap(BoundAxisWithinMinAndMax(node, mainAxis, maxLineMainDim, mainAxisOwnerSize))),
                    paddingAndBorderAxisMain);
            }

            if (measureModeCrossDim == YogaMeasureMode.Undefined
                || (node.style.Overflow != YogaOverflow.Scroll && measureModeCrossDim == YogaMeasureMode.AtMost))
            {
                // Clamp to the min/max size, and to at least the padding and border.
                node.layout.MeasuredDimensions[Dim[(int)crossAxis]] = BoundAxis(
                    node, crossAxis, totalLineCrossDim + paddingAndBorderAxisCross, crossAxisOwnerSize, ownerWidth);
            }
            else if (measureModeCrossDim == YogaMeasureMode.AtMost && node.style.Overflow == YogaOverflow.Scroll)
            {
                node.layout.MeasuredDimensions[Dim[(int)crossAxis]] = FloatMax(
                    FloatMin(
                        availableInnerCrossDim + paddingAndBorderAxisCross,
                        Unwrap(BoundAxisWithinMinAndMax(node, crossAxis, totalLineCrossDim + paddingAndBorderAxisCross,
                            crossAxisOwnerSize))),
                    paddingAndBorderAxisCross);
            }

            // Lines were wrapped in the normal direction: reverse the positions for wrap-reverse.
            if (performLayout && node.style.FlexWrap == YogaWrap.WrapReverse)
            {
                for (int i = 0; i < (int)childCount; i++)
                {
                    var child = children[i];
                    if (child.style.PositionType == YogaPositionType.Relative)
                    {
                        child.layout.Position[(int)Pos[(int)crossAxis]] =
                            node.layout.MeasuredDimensions[Dim[(int)crossAxis]]
                            - child.layout.Position[(int)Pos[(int)crossAxis]]
                            - child.layout.MeasuredDimensions[Dim[(int)crossAxis]];
                    }
                }
            }

            if (performLayout)
            {
                // STEP 10: SIZING AND POSITIONING ABSOLUTE CHILDREN
                foreach (var child in children)
                {
                    if (child.style.PositionType != YogaPositionType.Absolute)
                        continue;
                    AbsoluteLayoutChild(node, child, availableInnerWidth,
                        isMainAxisRow ? measureModeMainDim : measureModeCrossDim, availableInnerHeight, direction, config);
                }

                // STEP 11: SETTING TRAILING POSITIONS FOR CHILDREN
                bool needsMainTrailingPos = mainAxis == YogaFlexDirection.RowReverse || mainAxis == YogaFlexDirection.ColumnReverse;
                bool needsCrossTrailingPos = crossAxis == YogaFlexDirection.RowReverse || crossAxis == YogaFlexDirection.ColumnReverse;

                if (needsMainTrailingPos || needsCrossTrailingPos)
                {
                    for (int i = 0; i < (int)childCount; i++)
                    {
                        var child = children[i];
                        if (child.style.Display == YogaDisplay.None)
                            continue;
                        if (needsMainTrailingPos)
                            SetChildTrailingPosition(node, child, mainAxis);
                        if (needsCrossTrailingPos)
                            SetChildTrailingPosition(node, child, crossAxis);
                    }
                }
            }
        }

        static bool MeasureModeSizeIsExactAndMatchesOldMeasuredSize(YogaMeasureMode sizeMode, float size, float lastComputedSize) =>
            sizeMode == YogaMeasureMode.Exactly && FloatsEqual(size, lastComputedSize);

        static bool MeasureModeOldSizeIsUnspecifiedAndStillFits(YogaMeasureMode sizeMode, float size,
            YogaMeasureMode lastSizeMode, float lastComputedSize) =>
            sizeMode == YogaMeasureMode.AtMost && lastSizeMode == YogaMeasureMode.Undefined
            && (size >= lastComputedSize || FloatsEqual(size, lastComputedSize));

        static bool MeasureModeNewMeasureSizeIsStricterAndStillValid(YogaMeasureMode sizeMode, float size,
            YogaMeasureMode lastSizeMode, float lastSize, float lastComputedSize) =>
            lastSizeMode == YogaMeasureMode.AtMost && sizeMode == YogaMeasureMode.AtMost
            && !IsUndefined(lastSize) && !IsUndefined(size) && !IsUndefined(lastComputedSize)
            && lastSize > size
            && (lastComputedSize <= size || FloatsEqual(size, lastComputedSize));

        /// <summary>A value on the pixel grid, computed in double precision (as current Yoga does): to the
        /// nearest pixel with halves up (within 1e-4), or up or down when forced.</summary>
        public static float RoundValueToPixelGrid(double value, double pointScaleFactor, bool forceCeil, bool forceFloor)
        {
            double scaledValue = value * pointScaleFactor;
            // floor(scaledValue) == scaledValue - fractial, negative values included.
            double fractial = scaledValue % 1.0;
            if (fractial < 0) ++fractial;
            if (DoublesEqual(fractial, 0))
            {
                // Already on the grid.
                scaledValue = scaledValue - fractial;
            }
            else if (DoublesEqual(fractial, 1.0))
            {
                scaledValue = scaledValue - fractial + 1.0;
            }
            else if (forceCeil)
            {
                scaledValue = scaledValue - fractial + 1.0;
            }
            else if (forceFloor)
            {
                scaledValue = scaledValue - fractial;
            }
            else
            {
                // Round half up.
                scaledValue = scaledValue - fractial
                    + (!double.IsNaN(fractial) && (fractial > 0.5 || DoublesEqual(fractial, 0.5)) ? 1.0 : 0.0);
            }
            return double.IsNaN(scaledValue) || double.IsNaN(pointScaleFactor)
                ? Undefined
                : (float)(scaledValue / pointScaleFactor);
        }

        static bool DoublesEqual(double a, double b) =>
            double.IsNaN(a) || double.IsNaN(b) ? double.IsNaN(a) && double.IsNaN(b) : Math.Abs(a - b) < 0.0001;

        public static bool CanUseCachedMeasurement(YogaMeasureMode widthMode, float width, YogaMeasureMode heightMode,
            float height, YogaMeasureMode lastWidthMode, float lastWidth, YogaMeasureMode lastHeightMode,
            float lastHeight, float lastComputedWidth, float lastComputedHeight, float marginRow, float marginColumn,
            YogaConfig config)
        {
            if ((!IsUndefined(lastComputedHeight) && lastComputedHeight < 0)
                || (!IsUndefined(lastComputedWidth) && lastComputedWidth < 0))
                return false;
            bool useRoundedComparison = config != null && config.PointScaleFactor != 0;
            float effectiveWidth = useRoundedComparison
                ? RoundValueToPixelGrid(width, config.PointScaleFactor, false, false)
                : width;
            float effectiveHeight = useRoundedComparison
                ? RoundValueToPixelGrid(height, config.PointScaleFactor, false, false)
                : height;
            float effectiveLastWidth = useRoundedComparison
                ? RoundValueToPixelGrid(lastWidth, config.PointScaleFactor, false, false)
                : lastWidth;
            float effectiveLastHeight = useRoundedComparison
                ? RoundValueToPixelGrid(lastHeight, config.PointScaleFactor, false, false)
                : lastHeight;

            bool hasSameWidthSpec = lastWidthMode == widthMode && FloatsEqual(effectiveLastWidth, effectiveWidth);
            bool hasSameHeightSpec = lastHeightMode == heightMode && FloatsEqual(effectiveLastHeight, effectiveHeight);

            bool widthIsCompatible = hasSameWidthSpec
                || MeasureModeSizeIsExactAndMatchesOldMeasuredSize(widthMode, width - marginRow, lastComputedWidth)
                || MeasureModeOldSizeIsUnspecifiedAndStillFits(widthMode, width - marginRow, lastWidthMode, lastComputedWidth)
                || MeasureModeNewMeasureSizeIsStricterAndStillValid(widthMode, width - marginRow, lastWidthMode, lastWidth,
                    lastComputedWidth);

            bool heightIsCompatible = hasSameHeightSpec
                || MeasureModeSizeIsExactAndMatchesOldMeasuredSize(heightMode, height - marginColumn, lastComputedHeight)
                || MeasureModeOldSizeIsUnspecifiedAndStillFits(heightMode, height - marginColumn, lastHeightMode,
                    lastComputedHeight)
                || MeasureModeNewMeasureSizeIsStricterAndStillValid(heightMode, height - marginColumn, lastHeightMode,
                    lastHeight, lastComputedHeight);

            return widthIsCompatible && heightIsCompatible;
        }

        // Lays out or measures a node unless a cached result for the same constraints can be used.
        // Returns true if the layout was performed.
        static bool LayoutNodeInternal(YogaNode node, float availableWidth, float availableHeight,
            YogaDirection ownerDirection, YogaMeasureMode widthMeasureMode, YogaMeasureMode heightMeasureMode,
            float ownerWidth, float ownerHeight, bool performLayout, YogaConfig config)
        {
            var layout = node.layout;

            bool needToVisitNode = (node.IsDirty && layout.GenerationCount != currentGenerationCount)
                || layout.LastOwnerDirection != ownerDirection;

            if (needToVisitNode)
            {
                // Invalidate the cached results.
                layout.NextCachedMeasurementsIndex = 0;
                layout.CachedLayout.WidthMeasureMode = (YogaMeasureMode)(-1);
                layout.CachedLayout.HeightMeasureMode = (YogaMeasureMode)(-1);
                layout.CachedLayout.ComputedWidth = -1;
                layout.CachedLayout.ComputedHeight = -1;
            }

            YogaCachedMeasurement cachedResults = null;

            // Layouts and measurements are cached separately: a node is laid out at most once per
            // pass, but may be measured several times. Nodes with measure functions, the most
            // expensive to measure, also reuse results for compatible constraints.
            if (node.Measure != null)
            {
                float marginAxisRow = Unwrap(node.GetMarginForAxis(YogaFlexDirection.Row, ownerWidth));
                float marginAxisColumn = Unwrap(node.GetMarginForAxis(YogaFlexDirection.Column, ownerWidth));

                // First, try the layout cache.
                if (CanUseCachedMeasurement(widthMeasureMode, availableWidth, heightMeasureMode, availableHeight,
                        layout.CachedLayout.WidthMeasureMode, layout.CachedLayout.AvailableWidth,
                        layout.CachedLayout.HeightMeasureMode, layout.CachedLayout.AvailableHeight,
                        layout.CachedLayout.ComputedWidth, layout.CachedLayout.ComputedHeight,
                        marginAxisRow, marginAxisColumn, config))
                {
                    cachedResults = layout.CachedLayout;
                }
                else
                {
                    // Then the measurement cache.
                    for (uint i = 0; i < layout.NextCachedMeasurementsIndex; i++)
                    {
                        var entry = layout.CachedMeasurements[i];
                        if (CanUseCachedMeasurement(widthMeasureMode, availableWidth, heightMeasureMode, availableHeight,
                                entry.WidthMeasureMode, entry.AvailableWidth, entry.HeightMeasureMode,
                                entry.AvailableHeight, entry.ComputedWidth, entry.ComputedHeight,
                                marginAxisRow, marginAxisColumn, config))
                        {
                            cachedResults = entry;
                            break;
                        }
                    }
                }
            }
            else if (performLayout)
            {
                if (FloatsEqual(layout.CachedLayout.AvailableWidth, availableWidth)
                    && FloatsEqual(layout.CachedLayout.AvailableHeight, availableHeight)
                    && layout.CachedLayout.WidthMeasureMode == widthMeasureMode
                    && layout.CachedLayout.HeightMeasureMode == heightMeasureMode)
                {
                    cachedResults = layout.CachedLayout;
                }
            }
            else
            {
                for (uint i = 0; i < layout.NextCachedMeasurementsIndex; i++)
                {
                    var entry = layout.CachedMeasurements[i];
                    if (FloatsEqual(entry.AvailableWidth, availableWidth)
                        && FloatsEqual(entry.AvailableHeight, availableHeight)
                        && entry.WidthMeasureMode == widthMeasureMode
                        && entry.HeightMeasureMode == heightMeasureMode)
                    {
                        cachedResults = entry;
                        break;
                    }
                }
            }

            if (!needToVisitNode && cachedResults != null)
            {
                layout.MeasuredDimensions[DimWidth] = cachedResults.ComputedWidth;
                layout.MeasuredDimensions[DimHeight] = cachedResults.ComputedHeight;
            }
            else
            {
                NodeLayoutImpl(node, availableWidth, availableHeight, ownerDirection, widthMeasureMode,
                    heightMeasureMode, ownerWidth, ownerHeight, performLayout, config);

                layout.LastOwnerDirection = ownerDirection;

                if (cachedResults == null)
                {
                    if (layout.NextCachedMeasurementsIndex == YogaLayoutData.MaxCachedResultCount)
                        layout.NextCachedMeasurementsIndex = 0;

                    YogaCachedMeasurement newCacheEntry;
                    if (performLayout)
                    {
                        // Use the single layout cache entry.
                        newCacheEntry = layout.CachedLayout;
                    }
                    else
                    {
                        // Take a new measurement cache entry.
                        newCacheEntry = layout.CachedMeasurements[layout.NextCachedMeasurementsIndex];
                        layout.NextCachedMeasurementsIndex++;
                    }

                    newCacheEntry.AvailableWidth = availableWidth;
                    newCacheEntry.AvailableHeight = availableHeight;
                    newCacheEntry.WidthMeasureMode = widthMeasureMode;
                    newCacheEntry.HeightMeasureMode = heightMeasureMode;
                    newCacheEntry.ComputedWidth = layout.MeasuredDimensions[DimWidth];
                    newCacheEntry.ComputedHeight = layout.MeasuredDimensions[DimHeight];
                }
            }

            if (performLayout)
            {
                node.layout.Dimensions[DimWidth] = node.layout.MeasuredDimensions[DimWidth];
                node.layout.Dimensions[DimHeight] = node.layout.MeasuredDimensions[DimHeight];

                node.HasNewLayout = true;
                node.SetDirty(false);
            }

            layout.GenerationCount = currentGenerationCount;
            return needToVisitNode || cachedResults == null;
        }


        static void RoundToPixelGrid(YogaNode node, float pointScaleFactor, float absoluteLeft, float absoluteTop)
        {
            if (pointScaleFactor == 0.0f)
                return;

            double nodeLeft = node.layout.Position[(int)YogaEdge.Left];
            double nodeTop = node.layout.Position[(int)YogaEdge.Top];

            double nodeWidth = node.layout.Dimensions[DimWidth];
            double nodeHeight = node.layout.Dimensions[DimHeight];

            float absoluteNodeLeft = absoluteLeft + (float)nodeLeft;
            float absoluteNodeTop = absoluteTop + (float)nodeTop;

            float absoluteNodeRight = absoluteNodeLeft + (float)nodeWidth;
            float absoluteNodeBottom = absoluteNodeTop + (float)nodeHeight;

            // A node with a measure function is never rounded down, which could truncate its text.
            bool textRounding = node.isTextNode && !node.config.MeasuredNodesRoundAsOthers;

            if (node.config.RoundPositionsFromAbsolute)
            {
                node.layout.Position[(int)YogaEdge.Left] = RoundValueToPixelGrid(absoluteNodeLeft, pointScaleFactor, false, textRounding)
                    - RoundValueToPixelGrid(absoluteLeft, pointScaleFactor, false, false);
                node.layout.Position[(int)YogaEdge.Top] = RoundValueToPixelGrid(absoluteNodeTop, pointScaleFactor, false, textRounding)
                    - RoundValueToPixelGrid(absoluteTop, pointScaleFactor, false, false);
            }
            else
            {
                node.layout.Position[(int)YogaEdge.Left] = RoundValueToPixelGrid(nodeLeft, pointScaleFactor, false, textRounding);
                node.layout.Position[(int)YogaEdge.Top] = RoundValueToPixelGrid(nodeTop, pointScaleFactor, false, textRounding);
            }

            // A scaled dimension close to a whole number (from either side) has no fraction.
            bool hasFractionalWidth = !DoublesEqual((nodeWidth * pointScaleFactor) % 1.0, 0)
                && !DoublesEqual((nodeWidth * pointScaleFactor) % 1.0, 1.0);
            bool hasFractionalHeight = !DoublesEqual((nodeHeight * pointScaleFactor) % 1.0, 0)
                && !DoublesEqual((nodeHeight * pointScaleFactor) % 1.0, 1.0);

            node.layout.Dimensions[DimWidth] =
                RoundValueToPixelGrid(absoluteNodeRight, pointScaleFactor, textRounding && hasFractionalWidth,
                    textRounding && !hasFractionalWidth)
                - RoundValueToPixelGrid(absoluteNodeLeft, pointScaleFactor, false, textRounding);

            node.layout.Dimensions[DimHeight] =
                RoundValueToPixelGrid(absoluteNodeBottom, pointScaleFactor, textRounding && hasFractionalHeight,
                    textRounding && !hasFractionalHeight)
                - RoundValueToPixelGrid(absoluteNodeTop, pointScaleFactor, false, textRounding);

            foreach (var child in node.Children)
                RoundToPixelGrid(child, pointScaleFactor, absoluteNodeLeft, absoluteNodeTop);
        }

        public static void CalculateLayout(YogaNode node, float ownerWidth, float ownerHeight, YogaDirection ownerDirection)
        {
            // A new generation makes the recursion visit every dirty node at least once; later visits
            // are skipped when the constraints don't change.
            currentGenerationCount = Interlocked.Increment(ref generationCounter);
            node.ResolveDimension();
            float width;
            YogaMeasureMode widthMeasureMode;
            if (IsStyleDimDefined(node, YogaFlexDirection.Row, ownerWidth))
            {
                width = Unwrap(ResolveValue(node.resolvedDimensions[Dim[(int)YogaFlexDirection.Row]], ownerWidth)
                    + node.GetMarginForAxis(YogaFlexDirection.Row, ownerWidth));
                widthMeasureMode = YogaMeasureMode.Exactly;
            }
            else if (!ResolveValue(node.style.MaxDimensions[DimWidth], ownerWidth).IsUndefined)
            {
                width = Unwrap(ResolveValue(node.style.MaxDimensions[DimWidth], ownerWidth));
                widthMeasureMode = YogaMeasureMode.AtMost;
            }
            else
            {
                width = ownerWidth;
                widthMeasureMode = IsUndefined(width) ? YogaMeasureMode.Undefined : YogaMeasureMode.Exactly;
            }

            float height;
            YogaMeasureMode heightMeasureMode;
            if (IsStyleDimDefined(node, YogaFlexDirection.Column, ownerHeight))
            {
                height = Unwrap(ResolveValue(node.resolvedDimensions[Dim[(int)YogaFlexDirection.Column]], ownerHeight)
                    + node.GetMarginForAxis(YogaFlexDirection.Column, ownerWidth));
                heightMeasureMode = YogaMeasureMode.Exactly;
            }
            else if (!ResolveValue(node.style.MaxDimensions[DimHeight], ownerHeight).IsUndefined)
            {
                height = Unwrap(ResolveValue(node.style.MaxDimensions[DimHeight], ownerHeight));
                heightMeasureMode = YogaMeasureMode.AtMost;
            }
            else
            {
                height = ownerHeight;
                heightMeasureMode = IsUndefined(height) ? YogaMeasureMode.Undefined : YogaMeasureMode.Exactly;
            }

            if (LayoutNodeInternal(node, width, height, ownerDirection, widthMeasureMode, heightMeasureMode,
                    ownerWidth, ownerHeight, true, node.config))
            {
                node.SetLayoutPositionFromStyle(node.layout.Direction, ownerWidth, ownerHeight, ownerWidth);
                RoundToPixelGrid(node, node.config.PointScaleFactor, 0.0f, 0.0f);
            }
        }
    }
}
