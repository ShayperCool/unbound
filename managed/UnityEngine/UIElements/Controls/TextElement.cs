using System;
using Unbound.UIElements;

namespace UnityEngine.UIElements
{
    public partial interface INotifyValueChanged<T>
    {
        T value { get; set; }
        void SetValueWithoutNotify(T newValue);
    }

    /// <summary>An element that shows text: measured and drawn with the TextCore text engine.</summary>
    public partial class TextElement : BindableElement, INotifyValueChanged<string>
    {
        public static readonly string ussClassName = "unity-text-element";
        public static readonly string selectableUssClassName = ussClassName + "__selectable";

        string m_Text = string.Empty;
        bool m_EnableRichText = true;
        bool m_EmojiFallbackSupport = true;
        bool m_ParseEscapeSequences;
        bool m_DisplayTooltipWhenElided = true;
        internal readonly UaTextHandle uaText;

        public TextElement()
        {
            requireMeasureFunction = true;
            tabIndex = -1;
            uaText = new UaTextHandle(this);
            AddToClassList(ussClassName);
        }

        public virtual string text
        {
            get => ((INotifyValueChanged<string>)this).value;
            set => ((INotifyValueChanged<string>)this).value = value;
        }

        public bool enableRichText
        {
            get => m_EnableRichText;
            set
            {
                if (m_EnableRichText == value) return;
                m_EnableRichText = value;
                MarkDirtyText();
            }
        }

        public bool emojiFallbackSupport
        {
            get => m_EmojiFallbackSupport;
            set
            {
                if (m_EmojiFallbackSupport == value) return;
                m_EmojiFallbackSupport = value;
                MarkDirtyText();
            }
        }

        public bool parseEscapeSequences
        {
            get => m_ParseEscapeSequences;
            set
            {
                if (m_ParseEscapeSequences == value) return;
                m_ParseEscapeSequences = value;
                MarkDirtyText();
            }
        }

        public bool displayTooltipWhenElided
        {
            get => m_DisplayTooltipWhenElided;
            set => m_DisplayTooltipWhenElided = value;
        }

        public bool isElided => uaText.isElided;

        /// <summary>The text that is laid out (placeholder or masked text of input fields override it).</summary>
        internal virtual string uaRenderedText => m_Text ?? string.Empty;

        internal bool isInputField;
        internal bool uaMultiline { get; set; }

        string INotifyValueChanged<string>.value
        {
            get => m_Text ?? string.Empty;
            set
            {
                if (m_Text == value) return;
                if (panel != null)
                {
                    using (var e = ChangeEvent<string>.GetPooled(text, value))
                    {
                        e.elementTarget = this;
                        ((INotifyValueChanged<string>)this).SetValueWithoutNotify(value);
                        SendEvent(e);
                    }
                    return;
                }
                ((INotifyValueChanged<string>)this).SetValueWithoutNotify(value);
            }
        }

        void INotifyValueChanged<string>.SetValueWithoutNotify(string newValue)
        {
            if (m_Text == newValue) return;
            m_Text = newValue;
            MarkDirtyText();
        }

        public void MarkDirtyText()
        {
            uaText.MarkDirty();
            var c = computedValues;
            if (c.height.IsAuto() || c.height.IsNone() || c.width.IsAuto() || c.width.IsNone())
            {
                UaMarkMeasureDirty();
                IncrementVersion(VersionChangeType.Layout | VersionChangeType.Repaint);
            }
            else
            {
                IncrementVersion(VersionChangeType.Repaint);
            }
        }

        internal override void OnUaStyleResolved(ComputedValues previous, ComputedValues current)
        {
            if (!UaTextHandle.SameTextStyle(previous, current))
            {
                uaText.MarkDirty();
                UaMarkMeasureDirty();
            }
        }

        public Vector2 MeasureTextSize(string textToMeasure, float width, MeasureMode widthMode, float height, MeasureMode heightMode) =>
            uaText.Measure(textToMeasure, width, widthMode, height, heightMode);

        protected internal override Vector2 DoMeasure(float desiredWidth, MeasureMode widthMode, float desiredHeight, MeasureMode heightMode) =>
            uaText.Measure(uaRenderedText, desiredWidth, widthMode, desiredHeight, heightMode);

        protected override void HandleEventBubbleUp(EventBase evt) => base.HandleEventBubbleUp(evt);

        internal void UaSelectNone() => uaSelection?.SelectNone();

        internal UaTextSelection uaSelection;
    }

    public partial class Label : TextElement
    {
        public new static readonly string ussClassName = "unity-label";

        public Label() : this(string.Empty) { }

        public Label(string text)
        {
            AddToClassList(ussClassName);
            this.text = text;
        }
    }
}

namespace Unbound.UIElements
{
    using UnityEngine;
    using UnityEngine.UIElements;

    /// <summary>A text element's selection (cursor and selection indices).</summary>
    internal sealed class UaTextSelection
    {
        internal int cursorIndex;
        internal int selectIndex;
        internal void SelectNone() => selectIndex = cursorIndex;
    }
}
