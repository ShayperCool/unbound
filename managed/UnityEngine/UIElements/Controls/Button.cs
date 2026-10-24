using System;

namespace UnityEngine.UIElements
{
    /// <summary>A clickable text element; with an icon image its text moves into a child TextElement
    /// next to an Image, as Unity's Button builds it.</summary>
    public partial class Button : TextElement
    {
        public new static readonly string ussClassName = "unity-button";
        public static readonly string iconUssClassName = ussClassName + "--with-icon";
        public static readonly string iconOnlyUssClassName = ussClassName + "--with-icon-only";
        public static readonly string imageUSSClassName = ussClassName + "__image";
        static readonly string s_NonEmptyString = " ";

        Clickable m_Clickable;
        TextElement m_TextElement;
        Image m_ImageElement;
        Background m_IconImage;
        string m_Text = string.Empty;

        public Clickable clickable
        {
            get => m_Clickable;
            set
            {
                if (m_Clickable != null && m_Clickable.target == this) this.RemoveManipulator(m_Clickable);
                m_Clickable = value;
                if (m_Clickable != null) this.AddManipulator(m_Clickable);
            }
        }

        public Background iconImage
        {
            get => m_IconImage;
            set
            {
                if ((value.IsEmpty() && m_ImageElement == null) || value == m_IconImage) return;
                if (value.IsEmpty())
                {
                    m_IconImage = value;
                    ResetButtonHierarchy();
                    return;
                }
                if (m_ImageElement == null) UpdateButtonHierarchy();
                if (value.texture != null) m_ImageElement.image = value.texture;
                else if (value.sprite != null) m_ImageElement.sprite = value.sprite;
                else if (value.renderTexture != null) m_ImageElement.image = value.renderTexture;
                else m_ImageElement.vectorImage = value.vectorImage;
                m_IconImage = value;
                EnableInClassList(iconOnlyUssClassName, string.IsNullOrEmpty(text));
            }
        }

        public override string text
        {
            get => m_Text ?? string.Empty;
            set
            {
                m_Text = value;
                EnableInClassList(iconOnlyUssClassName, !m_IconImage.IsEmpty() && string.IsNullOrEmpty(text));
                if (m_TextElement != null)
                {
                    base.text = string.Empty;
                    if (m_TextElement.text != m_Text) m_TextElement.text = m_Text;
                }
                else if (base.text != m_Text)
                {
                    base.text = m_Text;
                }
            }
        }

        [Obsolete("onClick is obsolete. Use clicked instead (UnityUpgradable) -> clicked", true)]
        public event Action onClick
        {
            add => clicked += value;
            remove => clicked -= value;
        }

        public event Action clicked
        {
            add
            {
                if (m_Clickable == null) clickable = new Clickable(value);
                else m_Clickable.clicked += value;
            }
            remove
            {
                if (m_Clickable != null) m_Clickable.clicked -= value;
            }
        }

        public Button() : this(default(Background), null) { }

        public Button(Background iconImage, Action clickEvent = null) : this(clickEvent)
        {
            this.iconImage = iconImage;
        }

        public Button(Action clickEvent)
        {
            AddToClassList(ussClassName);
            clickable = new Clickable(clickEvent);
            focusable = true;
            tabIndex = 0;
            RegisterCallback<NavigationSubmitEvent>(OnNavigationSubmit);
        }

        void OnNavigationSubmit(NavigationSubmitEvent evt)
        {
            clickable?.SimulateSingleClick(evt);
            evt.StopPropagation();
        }

        protected internal override Vector2 DoMeasure(float desiredWidth, MeasureMode widthMode, float desiredHeight, MeasureMode heightMode)
        {
            var measured = text;
            if (string.IsNullOrEmpty(measured)) measured = s_NonEmptyString;
            return MeasureTextSize(measured, desiredWidth, widthMode, desiredHeight, heightMode);
        }

        void UpdateButtonHierarchy()
        {
            if (m_ImageElement == null)
            {
                m_ImageElement = new Image();
                m_ImageElement.AddToClassList(imageUSSClassName);
                Add(m_ImageElement);
                AddToClassList(iconUssClassName);
            }
            if (m_TextElement == null)
            {
                m_TextElement = new TextElement { text = text };
                m_Text = text;
                base.text = string.Empty;
                Add(m_TextElement);
            }
        }

        void ResetButtonHierarchy()
        {
            if (m_ImageElement != null)
            {
                m_ImageElement.RemoveFromHierarchy();
                m_ImageElement = null;
                RemoveFromClassList(iconUssClassName);
                RemoveFromClassList(iconOnlyUssClassName);
            }
            if (m_TextElement != null)
            {
                var previous = m_TextElement.text;
                m_TextElement.RemoveFromHierarchy();
                m_TextElement = null;
                text = previous;
            }
        }
    }

    /// <summary>A button that clicks once when pressed, then repeatedly while held.</summary>
    public partial class RepeatButton : TextElement
    {
        public new static readonly string ussClassName = "unity-repeat-button";

        Clickable m_Clickable;
        bool m_AcceptClicksIfDisabled;

        internal bool acceptClicksIfDisabled
        {
            get => m_AcceptClicksIfDisabled;
            set
            {
                if (m_AcceptClicksIfDisabled == value) return;
                m_AcceptClicksIfDisabled = value;
                if (m_Clickable != null) m_Clickable.acceptClicksIfDisabled = value;
            }
        }

        public RepeatButton()
        {
            AddToClassList(ussClassName);
        }

        public RepeatButton(Action clickEvent, long delay, long interval) : this()
        {
            SetAction(clickEvent, delay, interval);
        }

        public void SetAction(Action clickEvent, long delay, long interval)
        {
            this.RemoveManipulator(m_Clickable);
            m_Clickable = new Clickable(clickEvent, delay, interval);
            this.AddManipulator(m_Clickable);
        }

        internal void AddAction(Action clickEvent) => m_Clickable.clicked += clickEvent;
    }

    public partial class Box : VisualElement
    {
        public static readonly string ussClassName = "unity-box";

        public Box()
        {
            AddToClassList(ussClassName);
        }
    }

    /// <summary>An image sized to its texture, sprite or vector image (the renderer draws it with its
    /// scale mode, uv and tint).</summary>
    public partial class Image : VisualElement
    {
        public static readonly string ussClassName = "unity-image";

        static readonly CustomStyleProperty<Texture2D> s_ImageProperty = new CustomStyleProperty<Texture2D>("--unity-image");
        static readonly CustomStyleProperty<Sprite> s_SpriteProperty = new CustomStyleProperty<Sprite>("--unity-image");
        static readonly CustomStyleProperty<VectorImage> s_VectorImageProperty = new CustomStyleProperty<VectorImage>("--unity-image");
        static readonly CustomStyleProperty<string> s_ScaleModeProperty = new CustomStyleProperty<string>("--unity-image-size");
        static readonly CustomStyleProperty<Color> s_TintColorProperty = new CustomStyleProperty<Color>("--unity-image-tint-color");

        ScaleMode m_ScaleMode;
        Texture m_Image;
        Sprite m_Sprite;
        VectorImage m_VectorImage;
        Rect m_UV;
        Color m_TintColor;
        internal bool m_ImageIsInline;
        bool m_ScaleModeIsInline;
        bool m_TintColorIsInline;

        public Texture image
        {
            get => m_Image;
            set
            {
                if (m_Image == value && m_ImageIsInline) return;
                m_ImageIsInline = value != null;
                SetProperty(value, ref m_Image, ref m_Sprite, ref m_VectorImage);
            }
        }

        public Sprite sprite
        {
            get => m_Sprite;
            set
            {
                if (m_Sprite == value && m_ImageIsInline) return;
                m_ImageIsInline = value != null;
                SetProperty(value, ref m_Sprite, ref m_Image, ref m_VectorImage);
            }
        }

        public VectorImage vectorImage
        {
            get => m_VectorImage;
            set
            {
                if (m_VectorImage == value && m_ImageIsInline) return;
                m_ImageIsInline = value != null;
                SetProperty(value, ref m_VectorImage, ref m_Image, ref m_Sprite);
            }
        }

        public Rect sourceRect
        {
            get => GetSourceRect();
            set
            {
                if (GetSourceRect() == value) return;
                if (sprite != null)
                {
                    Debug.LogError("Cannot set sourceRect on a sprite image");
                    return;
                }
                CalculateUV(value);
            }
        }

        public Rect uv
        {
            get => m_UV;
            set => m_UV = value;
        }

        public ScaleMode scaleMode
        {
            get => m_ScaleMode;
            set
            {
                if (m_ScaleMode == value && m_ScaleModeIsInline) return;
                m_ScaleModeIsInline = true;
                SetScaleMode(value);
            }
        }

        public Color tintColor
        {
            get => m_TintColor;
            set
            {
                if (m_TintColor == value && m_TintColorIsInline) return;
                m_TintColorIsInline = true;
                SetTintColor(value);
            }
        }

        public Image()
        {
            AddToClassList(ussClassName);
            m_ScaleMode = ScaleMode.ScaleToFit;
            m_TintColor = Color.white;
            m_UV = new Rect(0f, 0f, 1f, 1f);
            requireMeasureFunction = true;
            RegisterCallback<CustomStyleResolvedEvent>(OnCustomStyleResolved);
        }

        static Vector2 GetTextureDisplaySize(Texture texture)
        {
            if (texture == null) return Vector2.zero;
            var size = new Vector2(texture.width, texture.height);
            if (texture is Texture2D t2d) size /= t2d.pixelsPerPoint;
            return size;
        }

        Vector2 GetTextureDisplaySize(Sprite s)
        {
            if (s == null) return Vector2.zero;
            float scale = Unbound.UIElements.UaSprites.PixelsPerUnitScaleForElement(this, s);
            return (Vector2)(s.bounds.size * s.pixelsPerUnit) * scale;
        }

        protected internal override Vector2 DoMeasure(float desiredWidth, MeasureMode widthMode, float desiredHeight, MeasureMode heightMode)
        {
            float w = float.NaN, h = float.NaN;
            if (image == null && sprite == null && vectorImage == null) return new Vector2(w, h);
            Vector2 size = image != null ? GetTextureDisplaySize(image) : sprite != null ? GetTextureDisplaySize(sprite) : vectorImage.size;
            var source = sourceRect;
            bool hasSource = source != Rect.zero;
            w = hasSource ? Mathf.Abs(source.width) : size.x;
            h = hasSource ? Mathf.Abs(source.height) : size.y;
            if (widthMode == MeasureMode.AtMost) w = Mathf.Min(w, desiredWidth);
            if (heightMode == MeasureMode.AtMost) h = Mathf.Min(h, desiredHeight);
            return new Vector2(w, h);
        }

        void OnCustomStyleResolved(CustomStyleResolvedEvent e) => ReadCustomProperties(e.customStyle);

        void ReadCustomProperties(ICustomStyle custom)
        {
            if (!m_ImageIsInline)
            {
                if (custom.TryGetValue(s_ImageProperty, out var texture)) SetProperty(texture, ref m_Image, ref m_Sprite, ref m_VectorImage);
                else if (custom.TryGetValue(s_SpriteProperty, out var s)) SetProperty(s, ref m_Sprite, ref m_Image, ref m_VectorImage);
                else if (custom.TryGetValue(s_VectorImageProperty, out var vector)) SetProperty(vector, ref m_VectorImage, ref m_Image, ref m_Sprite);
                else ClearProperty();
            }
            if (!m_ScaleModeIsInline && custom.TryGetValue(s_ScaleModeProperty, out var mode))
                SetScaleMode(Unbound.UIElements.UaSprites.ParseScaleMode(mode));
            if (!m_TintColorIsInline) SetTintColor(custom.TryGetValue(s_TintColorProperty, out var tint) ? tint : Color.white);
        }

        void SetProperty<T0, T1, T2>(T0 src, ref T0 dst, ref T1 alt0, ref T2 alt1) where T0 : Object where T1 : Object where T2 : Object
        {
            if (src == dst) return;
            dst = src;
            if (dst != null)
            {
                alt0 = null;
                alt1 = null;
            }
            if (dst == null)
            {
                uv = new Rect(0f, 0f, 1f, 1f);
                ReadCustomProperties(customStyle);
            }
            IncrementVersion(VersionChangeType.Layout | VersionChangeType.Repaint);
            UaMarkMeasureDirty();
        }

        void ClearProperty()
        {
            if (m_ImageIsInline) return;
            image = null;
            sprite = null;
            vectorImage = null;
        }

        void SetScaleMode(ScaleMode mode)
        {
            if (m_ScaleMode == mode) return;
            m_ScaleMode = mode;
            IncrementVersion(VersionChangeType.Repaint);
        }

        void SetTintColor(Color color)
        {
            if (m_TintColor == color) return;
            m_TintColor = color;
            IncrementVersion(VersionChangeType.Repaint);
        }

        Vector2 DisplaySize()
        {
            if (image != null) return GetTextureDisplaySize(image);
            if (vectorImage != null) return vectorImage.size;
            return Vector2.zero;
        }

        void CalculateUV(Rect srcRect)
        {
            m_UV = new Rect(0f, 0f, 1f, 1f);
            var size = DisplaySize();
            if (size == Vector2.zero) return;
            m_UV.x = srcRect.x / size.x;
            m_UV.width = srcRect.width / size.x;
            m_UV.height = srcRect.height / size.y;
            m_UV.y = 1f - m_UV.height - srcRect.y / size.y;
        }

        Rect GetSourceRect()
        {
            var r = Rect.zero;
            var size = DisplaySize();
            if (size == Vector2.zero) return r;
            r.x = uv.x * size.x;
            r.width = uv.width * size.x;
            r.y = (1f - uv.y - uv.height) * size.y;
            r.height = uv.height * size.y;
            return r;
        }
    }
}

namespace Unbound.UIElements
{
    using UnityEngine;
    using UnityEngine.UIElements;

    internal static class UaSprites
    {
        /// <summary>A USS scale mode keyword (stretch-to-fill, scale-and-crop, scale-to-fit); unknown
        /// values read as the enum's first value, as Unity's lookup leaves them.</summary>
        internal static ScaleMode ParseScaleMode(string keyword)
        {
            switch (keyword)
            {
                case "scale-and-crop": return ScaleMode.ScaleAndCrop;
                case "scale-to-fit": return ScaleMode.ScaleToFit;
                default: return ScaleMode.StretchToFill;
            }
        }

        /// <summary>The scale of a sprite's pixels for an element: the reference pixels per unit of the
        /// panel's settings over the sprite's own (Unity's UIElementsUtility.PixelsPerUnitScaleForElement).</summary>
        internal static float PixelsPerUnitScaleForElement(VisualElement ve, Sprite sprite)
        {
            if (ve == null || ve.elementPanel == null || sprite == null) return 1f;
            float referencePixelsPerUnit = ve.elementPanel is BaseRuntimePanel runtime && runtime.ownerObject is PanelSettings settings ? settings.referenceSpritePixelsPerUnit : 100f;
            float spritePixelsPerUnit = sprite.pixelsPerUnit;
            spritePixelsPerUnit = Mathf.Max(0.01f, spritePixelsPerUnit);
            return referencePixelsPerUnit / spritePixelsPerUnit;
        }
    }
}
