// Generated from the metadata of Unity 6000.0's reference assemblies: names and values must match
// exactly. Regenerate with the enum dumper (managed/Unbound.EnumDumper):
//   UnityEngine.UIElements.Align UnityEngine.UIElements.AlternatingRowBackground UnityEngine.UIElements.AngleUnit UnityEngine.UIElements.Axis UnityEngine.UIElements.BackgroundPositionKeyword UnityEngine.UIElements.BackgroundSizeType UnityEngine.UIElements.CollectionVirtualizationMethod UnityEngine.UIElements.ContextType UnityEngine.UIElements.DisplayStyle UnityEngine.UIElements.EasingMode UnityEngine.UIElements.EditorTextRenderingMode UnityEngine.UIElements.FlexDirection UnityEngine.UIElements.Justify UnityEngine.UIElements.LanguageDirection UnityEngine.UIElements.LengthUnit UnityEngine.UIElements.ListViewReorderMode UnityEngine.UIElements.MouseButton UnityEngine.UIElements.NavigationDeviceType UnityEngine.UIElements.Overflow UnityEngine.UIElements.OverflowClipBox UnityEngine.UIElements.OverflowInternal UnityEngine.UIElements.PanelRenderMode UnityEngine.UIElements.PanelScaleMode UnityEngine.UIElements.PanelScreenMatchMode UnityEngine.UIElements.PickingMode UnityEngine.UIElements.Position UnityEngine.UIElements.PropagationPhase UnityEngine.UIElements.PseudoStates UnityEngine.UIElements.Repeat UnityEngine.UIElements.RepeatXY UnityEngine.UIElements.ScrollViewMode UnityEngine.UIElements.ScrollerVisibility UnityEngine.UIElements.SelectionType UnityEngine.UIElements.SliceType UnityEngine.UIElements.SliderDirection UnityEngine.UIElements.StyleKeyword UnityEngine.UIElements.StyleSelectorRelationship UnityEngine.UIElements.StyleSelectorType UnityEngine.UIElements.StyleValueFunction UnityEngine.UIElements.StyleValueKeyword UnityEngine.UIElements.StyleValueType UnityEngine.UIElements.TextOverflow UnityEngine.UIElements.TextOverflowPosition UnityEngine.UIElements.TimeUnit UnityEngine.UIElements.TransformOriginOffset UnityEngine.UIElements.TrickleDown UnityEngine.UIElements.UsageHints UnityEngine.UIElements.VersionChangeType UnityEngine.UIElements.Visibility UnityEngine.UIElements.WhiteSpace UnityEngine.UIElements.Wrap UnityEngine.UIElements.KeyboardNavigationOperation UnityEngine.UIElements.DeltaSpeed UnityEngine.UIElements.HelpBoxMessageType UnityEngine.UIElements.EventCategory UnityEngine.UIElements.DispatchMode UnityEngine.UIElements.StyleSheets.StylePropertyId UnityEngine.UIElements.StyleSheets.StyleEnumType UnityEngine.UIElements.EventInterestOptions UnityEngine.UIElements.DynamicAtlasFilters UnityEngine.UIElements.BindingLogLevel UnityEngine.TextGeneratorType
using System;

namespace UnityEngine
{
    public enum TextGeneratorType
    {
        Standard = 0,
        Advanced = 1,
    }

}

namespace UnityEngine.UIElements
{
    public enum Align
    {
        Auto = 0,
        FlexStart = 1,
        Center = 2,
        FlexEnd = 3,
        Stretch = 4,
    }

    public enum AlternatingRowBackground
    {
        None = 0,
        ContentOnly = 1,
        All = 2,
    }

    public enum AngleUnit
    {
        Degree = 0,
        Gradian = 1,
        Radian = 2,
        Turn = 3,
    }

    internal enum Axis
    {
        X = 0,
        Y = 1,
        Z = 2,
    }

    public enum BackgroundPositionKeyword
    {
        Center = 0,
        Top = 1,
        Bottom = 2,
        Left = 3,
        Right = 4,
    }

    public enum BackgroundSizeType
    {
        Length = 0,
        Cover = 1,
        Contain = 2,
    }

    public enum BindingLogLevel
    {
        None = 0,
        Once = 1,
        All = 2,
    }

    public enum CollectionVirtualizationMethod
    {
        FixedHeight = 0,
        DynamicHeight = 1,
    }

    public enum ContextType
    {
        Player = 0,
        Editor = 1,
    }

    public enum DeltaSpeed
    {
        Fast = 0,
        Normal = 1,
        Slow = 2,
    }

    internal enum DispatchMode
    {
        Default = 1,
        Queued = 1,
        Immediate = 2,
    }

    public enum DisplayStyle
    {
        Flex = 0,
        None = 1,
    }

    [Flags]
    public enum DynamicAtlasFilters
    {
        None = 0,
        Readability = 1,
        Size = 2,
        Format = 4,
        ColorSpace = 8,
        FilterMode = 16,
    }

    public enum EasingMode
    {
        Ease = 0,
        EaseIn = 1,
        EaseOut = 2,
        EaseInOut = 3,
        Linear = 4,
        EaseInSine = 5,
        EaseOutSine = 6,
        EaseInOutSine = 7,
        EaseInCubic = 8,
        EaseOutCubic = 9,
        EaseInOutCubic = 10,
        EaseInCirc = 11,
        EaseOutCirc = 12,
        EaseInOutCirc = 13,
        EaseInElastic = 14,
        EaseOutElastic = 15,
        EaseInOutElastic = 16,
        EaseInBack = 17,
        EaseOutBack = 18,
        EaseInOutBack = 19,
        EaseInBounce = 20,
        EaseOutBounce = 21,
        EaseInOutBounce = 22,
    }

    public enum EditorTextRenderingMode
    {
        SDF = 0,
        Bitmap = 1,
    }

    internal enum EventCategory
    {
        Default = 0,
        Pointer = 1,
        PointerMove = 2,
        PointerDown = 3,
        EnterLeave = 4,
        EnterLeaveWindow = 5,
        Keyboard = 6,
        Geometry = 7,
        Style = 8,
        ChangeValue = 9,
        Bind = 10,
        Focus = 11,
        ChangePanel = 12,
        StyleTransition = 13,
        Navigation = 14,
        Command = 15,
        Tooltip = 16,
        DragAndDrop = 17,
        IMGUI = 18,
    }

    public enum EventInterestOptions
    {
        Inherit = 0,
        AllEventTypes = -1,
    }

    public enum FlexDirection
    {
        Column = 0,
        ColumnReverse = 1,
        Row = 2,
        RowReverse = 3,
    }

    public enum HelpBoxMessageType
    {
        None = 0,
        Info = 1,
        Warning = 2,
        Error = 3,
    }

    public enum Justify
    {
        FlexStart = 0,
        Center = 1,
        FlexEnd = 2,
        SpaceBetween = 3,
        SpaceAround = 4,
        SpaceEvenly = 5,
    }

    public enum KeyboardNavigationOperation
    {
        None = 0,
        SelectAll = 1,
        Cancel = 2,
        Submit = 3,
        Previous = 4,
        Next = 5,
        MoveRight = 6,
        MoveLeft = 7,
        PageUp = 8,
        PageDown = 9,
        Begin = 10,
        End = 11,
    }

    public enum LanguageDirection
    {
        Inherit = 0,
        LTR = 1,
        RTL = 2,
    }

    public enum LengthUnit
    {
        Pixel = 0,
        Percent = 1,
    }

    public enum ListViewReorderMode
    {
        Simple = 0,
        Animated = 1,
    }

    public enum MouseButton
    {
        LeftMouse = 0,
        RightMouse = 1,
        MiddleMouse = 2,
    }

    internal enum NavigationDeviceType
    {
        Unknown = 0,
        Keyboard = 1,
        NonKeyboard = 2,
    }

    public enum Overflow
    {
        Visible = 0,
        Hidden = 1,
    }

    public enum OverflowClipBox
    {
        PaddingBox = 0,
        ContentBox = 1,
    }

    internal enum OverflowInternal
    {
        Visible = 0,
        Hidden = 1,
        Scroll = 2,
    }

    public enum PanelRenderMode
    {
        ScreenSpaceOverlay = 0,
        WorldSpace = 1,
    }

    public enum PanelScaleMode
    {
        ConstantPixelSize = 0,
        ConstantPhysicalSize = 1,
        ScaleWithScreenSize = 2,
    }

    public enum PanelScreenMatchMode
    {
        MatchWidthOrHeight = 0,
        Shrink = 1,
        Expand = 2,
    }

    public enum PickingMode
    {
        Position = 0,
        Ignore = 1,
    }

    public enum Position
    {
        Relative = 0,
        Absolute = 1,
    }

    public enum PropagationPhase
    {
        None = 0,
        TrickleDown = 1,
        BubbleUp = 3,
        [Obsolete] AtTarget = 2,
        [Obsolete] DefaultAction = 4,
        [Obsolete] DefaultActionAtTarget = 5,
    }

    [Flags]
    internal enum PseudoStates
    {
        Active = 1,
        Hover = 2,
        Checked = 8,
        Disabled = 32,
        Focus = 64,
        Root = 128,
    }

    public enum Repeat
    {
        NoRepeat = 0,
        Space = 1,
        Round = 2,
        Repeat = 3,
    }

    internal enum RepeatXY
    {
        RepeatX = 0,
        RepeatY = 1,
    }

    public enum ScrollerVisibility
    {
        Auto = 0,
        AlwaysVisible = 1,
        Hidden = 2,
    }

    public enum ScrollViewMode
    {
        Vertical = 0,
        Horizontal = 1,
        VerticalAndHorizontal = 2,
    }

    public enum SelectionType
    {
        None = 0,
        Single = 1,
        Multiple = 2,
    }

    public enum SliceType
    {
        Sliced = 0,
        Tiled = 1,
    }

    public enum SliderDirection
    {
        Horizontal = 0,
        Vertical = 1,
    }

    public enum StyleKeyword
    {
        Undefined = 0,
        Null = 1,
        Auto = 2,
        None = 3,
        Initial = 4,
    }

    internal enum StyleSelectorRelationship
    {
        None = 0,
        Child = 1,
        Descendent = 2,
    }

    internal enum StyleSelectorType
    {
        Unknown = 0,
        Wildcard = 1,
        Type = 2,
        Class = 3,
        PseudoClass = 4,
        RecursivePseudoClass = 5,
        ID = 6,
        Predicate = 7,
    }

    internal enum StyleValueFunction
    {
        Unknown = 0,
        Var = 1,
        Env = 2,
        LinearGradient = 3,
    }

    internal enum StyleValueKeyword
    {
        Inherit = 0,
        Initial = 1,
        Auto = 2,
        Unset = 3,
        True = 4,
        False = 5,
        None = 6,
        Cover = 7,
        Contain = 8,
    }

    internal enum StyleValueType
    {
        Invalid = 0,
        Keyword = 1,
        Float = 2,
        Dimension = 3,
        Color = 4,
        ResourcePath = 5,
        AssetReference = 6,
        Enum = 7,
        Variable = 8,
        String = 9,
        Function = 10,
        CommaSeparator = 11,
        ScalableImage = 12,
        MissingAssetReference = 13,
    }

    public enum TextOverflow
    {
        Clip = 0,
        Ellipsis = 1,
    }

    public enum TextOverflowPosition
    {
        End = 0,
        Start = 1,
        Middle = 2,
    }

    public enum TimeUnit
    {
        Second = 0,
        Millisecond = 1,
    }

    public enum TransformOriginOffset
    {
        Left = 1,
        Right = 2,
        Top = 3,
        Bottom = 4,
        Center = 5,
    }

    public enum TrickleDown
    {
        NoTrickleDown = 0,
        TrickleDown = 1,
    }

    [Flags]
    public enum UsageHints
    {
        None = 0,
        DynamicTransform = 1,
        GroupTransform = 2,
        MaskContainer = 4,
        DynamicColor = 8,
    }

    [Flags]
    public enum VersionChangeType
    {
        Bindings = 1,
        ViewData = 2,
        Hierarchy = 4,
        Layout = 8,
        StyleSheet = 16,
        Styles = 32,
        Overflow = 64,
        BorderRadius = 128,
        BorderWidth = 256,
        Transform = 512,
        Size = 1024,
        Repaint = 2048,
        Opacity = 4096,
        Color = 8192,
        RenderHints = 16384,
        TransitionProperty = 32768,
        EventCallbackCategories = 65536,
        DisableRendering = 131072,
        BindingRegistration = 262144,
        DataSource = 524288,
        Picking = 1048576,
    }

    public enum Visibility
    {
        Visible = 0,
        Hidden = 1,
    }

    public enum WhiteSpace
    {
        Normal = 0,
        NoWrap = 1,
        Pre = 2,
        PreWrap = 3,
    }

    public enum Wrap
    {
        NoWrap = 0,
        Wrap = 1,
        WrapReverse = 2,
    }

}

namespace UnityEngine.UIElements.StyleSheets
{
    internal enum StyleEnumType
    {
        Align = 0,
        Axis = 1,
        BackgroundPositionKeyword = 2,
        BackgroundSizeType = 3,
        DisplayStyle = 4,
        EasingMode = 5,
        EditorTextRenderingMode = 6,
        FlexDirection = 7,
        FontStyle = 8,
        Justify = 9,
        Overflow = 10,
        OverflowClipBox = 11,
        OverflowInternal = 12,
        Position = 13,
        Repeat = 14,
        RepeatXY = 15,
        ScaleMode = 16,
        SliceType = 17,
        TextAnchor = 18,
        TextGeneratorType = 19,
        TextOverflow = 20,
        TextOverflowPosition = 21,
        TransformOriginOffset = 22,
        Visibility = 23,
        WhiteSpace = 24,
        Wrap = 25,
    }

    internal enum StylePropertyId
    {
        Unknown = 0,
        Custom = -1,
        AlignContent = 131072,
        AlignItems = 131073,
        AlignSelf = 131074,
        All = 262144,
        BackgroundColor = 458752,
        BackgroundImage = 458753,
        BackgroundPosition = 262145,
        BackgroundPositionX = 458754,
        BackgroundPositionY = 458755,
        BackgroundRepeat = 458756,
        BackgroundSize = 458757,
        BorderBottomColor = 458758,
        BorderBottomLeftRadius = 458759,
        BorderBottomRightRadius = 458760,
        BorderBottomWidth = 131075,
        BorderColor = 262146,
        BorderLeftColor = 458761,
        BorderLeftWidth = 131076,
        BorderRadius = 262147,
        BorderRightColor = 458762,
        BorderRightWidth = 131077,
        BorderTopColor = 458763,
        BorderTopLeftRadius = 458764,
        BorderTopRightRadius = 458765,
        BorderTopWidth = 131078,
        BorderWidth = 262148,
        Bottom = 131079,
        Color = 65536,
        Cursor = 196608,
        Display = 131080,
        Flex = 262149,
        FlexBasis = 131081,
        FlexDirection = 131082,
        FlexGrow = 131083,
        FlexShrink = 131084,
        FlexWrap = 131085,
        FontSize = 65537,
        Height = 131086,
        JustifyContent = 131087,
        Left = 131088,
        LetterSpacing = 65538,
        Margin = 262150,
        MarginBottom = 131089,
        MarginLeft = 131090,
        MarginRight = 131091,
        MarginTop = 131092,
        MaxHeight = 131093,
        MaxWidth = 131094,
        MinHeight = 131095,
        MinWidth = 131096,
        Opacity = 458766,
        Overflow = 458767,
        Padding = 262151,
        PaddingBottom = 131097,
        PaddingLeft = 131098,
        PaddingRight = 131099,
        PaddingTop = 131100,
        Position = 131101,
        Right = 131102,
        Rotate = 327680,
        Scale = 327681,
        TextOverflow = 196609,
        TextShadow = 65539,
        Top = 131103,
        TransformOrigin = 327682,
        Transition = 262152,
        TransitionDelay = 393216,
        TransitionDuration = 393217,
        TransitionProperty = 393218,
        TransitionTimingFunction = 393219,
        Translate = 327683,
        UnityBackgroundImageTintColor = 196610,
        UnityBackgroundScaleMode = 262153,
        UnityEditorTextRenderingMode = 65540,
        UnityFont = 65541,
        UnityFontDefinition = 65542,
        UnityFontStyleAndWeight = 65543,
        UnityOverflowClipBox = 196611,
        UnityParagraphSpacing = 65544,
        UnitySliceBottom = 196612,
        UnitySliceLeft = 196613,
        UnitySliceRight = 196614,
        UnitySliceScale = 196615,
        UnitySliceTop = 196616,
        UnitySliceType = 196617,
        UnityTextAlign = 65545,
        UnityTextGenerator = 65546,
        UnityTextOutline = 262154,
        UnityTextOutlineColor = 65547,
        UnityTextOutlineWidth = 65548,
        UnityTextOverflowPosition = 196618,
        Visibility = 65549,
        WhiteSpace = 65550,
        Width = 131104,
        WordSpacing = 65551,
    }

}
