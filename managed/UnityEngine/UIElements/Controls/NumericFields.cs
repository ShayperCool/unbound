using System;
using System.Collections.Generic;
using System.Globalization;
using Unity.Properties;

namespace UnityEngine.UIElements
{
    /// <summary>
    /// A text field holding a number (matches the Unity 6000.0.71f1 Editor,
    /// tests/projects/numericfields). Typed text is read with ExpressionEvaluator on every change
    /// (unless delayed), keeping the typed text until the edit ends (Return or losing focus), when
    /// it shows the value again; text that does not read keeps the value. Its label drags the value
    /// (not while read-only), and input device deltas step it by a sensitivity that grows with the
    /// value. The derived fields only read, print and step their own type.
    /// </summary>
    public abstract partial class TextValueField<TValueType> : TextInputBaseField<TValueType>, IValueField<TValueType>
    {
        internal static readonly BindingId formatStringProperty = nameof(formatString);
        internal const int kMaxValueFieldLength = 1000;

        BaseFieldMouseDragger m_Dragger;
        bool m_ForceUpdateDisplay;

        TextValueInput textValueInput => (TextValueInput)textInputBase;

        internal bool forceUpdateDisplay
        {
            set => m_ForceUpdateDisplay = value;
        }

        [CreateProperty]
        public string formatString
        {
            get => textValueInput.formatString;
            set
            {
                if (textValueInput.formatString == value) return;
                textValueInput.formatString = value;
                textEdition.UpdateText(ValueToString(rawValue));
                NotifyPropertyChanged(formatStringProperty);
            }
        }

        protected TextValueField(int maxLength, TextValueInput textValueInput) : this(null, maxLength, textValueInput) { }

        protected TextValueField(string label, int maxLength, TextValueInput textValueInput) : base(label, maxLength, char.MinValue, textValueInput)
        {
            textEdition.UpdateText(ValueToString(rawValue));
            onIsReadOnlyChanged += readOnly => EnableLabelDragger(!readOnly);
        }

        public abstract void ApplyInputDeviceDelta(Vector3 delta, DeltaSpeed speed, TValueType startValue);

        public void StartDragging()
        {
            if (showMixedValue) value = default;
            textValueInput.StartDragging();
        }

        public void StopDragging() => textValueInput.StopDragging();

        /// <summary>Reads the typed text into the value while keeping the text as typed.</summary>
        internal override void UpdateValueFromText()
        {
            UpdatePlaceholderClassList();
            m_UpdateTextFromValue = false;
            try
            {
                value = StringToValue(text);
            }
            finally
            {
                m_UpdateTextFromValue = true;
            }
        }

        internal override void UpdateTextFromValue()
        {
            if (m_UpdateTextFromValue) text = ValueToString(rawValue);
        }

        internal virtual bool CanTryParse(string textString) => false;

        protected void AddLabelDragger<TDraggerType>()
        {
            m_Dragger = new FieldMouseDragger<TDraggerType>((IValueField<TDraggerType>)this);
            EnableLabelDragger(!isReadOnly);
        }

        void EnableLabelDragger(bool enable)
        {
            if (m_Dragger == null) return;
            m_Dragger.SetDragZone(enable ? labelElement : null);
            labelElement.EnableInClassList(labelDraggerVariantUssClassName, enable);
        }

        public override void SetValueWithoutNotify(TValueType newValue)
        {
            // A value equal to the shown one keeps the text (an expression stays as typed).
            bool refresh = m_ForceUpdateDisplay || (m_UpdateTextFromValue && !EqualityComparer<TValueType>.Default.Equals(rawValue, newValue));
            base.SetValueWithoutNotify(newValue);
            if (refresh) textEdition.UpdateText(ValueToString(rawValue));
            m_ForceUpdateDisplay = false;
        }

        public void ClearValue()
        {
            text = string.Empty;
            UpdateValueFromText();
        }

        [global::UnityEngine.UIElements.@EventInterestAttribute(new global::System.Type[] { typeof(global::UnityEngine.UIElements.BlurEvent), typeof(global::UnityEngine.UIElements.FocusEvent) })]
        protected override void HandleEventBubbleUp(EventBase evt)
        {
            base.HandleEventBubbleUp(evt);
            // An empty field showing its placeholder keeps it.
            if (string.IsNullOrEmpty(text) && !string.IsNullOrEmpty(textEdition.placeholder)) return;
            if (evt.eventTypeId == BlurEvent.TypeId())
            {
                if (showMixedValue)
                {
                    UpdateMixedValueContent();
                }
                else if (string.IsNullOrEmpty(text))
                {
                    textInputBase.UpdateTextFromValue();
                }
                else
                {
                    textInputBase.UpdateValueFromText();
                    textInputBase.UpdateTextFromValue();
                }
            }
            else if (evt.eventTypeId == FocusEvent.TypeId() && showMixedValue && textInputBase.textElement.hasFocus)
            {
                textInputBase.text = "";
            }
        }

        internal override void RegisterEditingCallbacks()
        {
            base.RegisterEditingCallbacks();
            labelElement.RegisterCallback<PointerDownEvent>(StartEditing, TrickleDown.TrickleDown);
            labelElement.RegisterCallback<PointerUpEvent>(EndEditing);
        }

        internal override void UnregisterEditingCallbacks()
        {
            base.UnregisterEditingCallbacks();
            labelElement.UnregisterCallback<PointerDownEvent>(StartEditing, TrickleDown.TrickleDown);
            labelElement.UnregisterCallback<PointerUpEvent>(EndEditing);
        }

        /// <summary>The text of a value as the input prints it while dragging a delayed field: the
        /// format with the current culture (the field's own text otherwise).</summary>
        private protected virtual string FormatForInput(TValueType value) => ValueToString(value);

        /// <summary>A dragged value: a delayed field only shows it until the edit ends.</summary>
        private protected void ApplyDraggedValue(TValueType dragged)
        {
            if (isDelayed) text = FormatForInput(dragged);
            else value = dragged;
        }

        /// <summary>The value the current text reads as (the drag's starting point).</summary>
        private protected TValueType TextValue => StringToValue(text);

        /// <summary>The input of a numeric field: accepts the characters the field reads.</summary>
        protected abstract partial class TextValueInput : TextInputBase
        {
            TextValueField<TValueType> textValueFieldParent => (TextValueField<TValueType>)parent;

            protected TextValueInput() { }

            internal override bool AcceptCharacter(char c) => base.AcceptCharacter(c) && c != 0 && allowedCharacters.IndexOf(c) != -1;

            protected abstract string allowedCharacters { get; }

            public string formatString { get; set; }

            public abstract void ApplyInputDeviceDelta(Vector3 delta, DeltaSpeed speed, TValueType startValue);

            public void StartDragging()
            {
                isDragging = true;
                textSelection.SelectNone();
                MarkDirtyRepaint();
            }

            public void StopDragging()
            {
                if (textValueFieldParent.isDelayed) UpdateValueFromText();
                isDragging = false;
                textSelection.SelectAll();
                MarkDirtyRepaint();
            }

            protected abstract string ValueToString(TValueType value);

            protected override TValueType StringToValue(string str) => base.StringToValue(str);
        }

        /// <summary>The input of the numeric fields, forwarding to its field (each field names its
        /// own subclass, as the release's inputs are named).</summary>
        private protected class NumericInput : TextValueInput
        {
            readonly string m_AllowedCharacters;

            internal NumericInput(string allowed, string format)
            {
                m_AllowedCharacters = allowed;
                formatString = format;
            }

            TextValueField<TValueType> field => (TextValueField<TValueType>)parent;

            protected override string allowedCharacters => m_AllowedCharacters;

            public override void ApplyInputDeviceDelta(Vector3 delta, DeltaSpeed speed, TValueType startValue) => field.ApplyInputDeviceDelta(delta, speed, startValue);

            protected override string ValueToString(TValueType value) => field.FormatForInput(value);

            protected override TValueType StringToValue(string str) => field.StringToValue(str);
        }
    }

    /// <summary>Input device steps of the numeric fields: a sensitivity that grows with the start
    /// value, times the delta's signed length and the speed's acceleration.</summary>
    internal static class NumericFieldSteps
    {
        static float Step(Vector3 delta, DeltaSpeed speed) =>
            NumericFieldDraggerUtility.NiceDelta(delta, NumericFieldDraggerUtility.Acceleration(speed == DeltaSpeed.Fast, speed == DeltaSpeed.Slow));

        /// <summary>A whole-number step, rounded half to even.</summary>
        internal static long IntegerStep(Vector3 delta, DeltaSpeed speed, double sensitivity) => (long)Math.Round(Step(delta, speed) * sensitivity);

        /// <summary>A real value moved and rounded to the sensitivity's decimals.</summary>
        internal static double RealStep(double current, Vector3 delta, DeltaSpeed speed, double sensitivity) =>
            Mathf.RoundBasedOnMinimumDifference(current + Step(delta, speed) * sensitivity, sensitivity);

        internal static long SaturatingAdd(long value, long step)
        {
            long magnitude = Math.Abs(step);
            if (step > 0) return value > 0 && magnitude > long.MaxValue - value ? long.MaxValue : value + step;
            return value < 0 && value < long.MinValue + magnitude ? long.MinValue : value - magnitude;
        }

        internal static ulong SaturatingAdd(ulong value, long step)
        {
            ulong magnitude = (ulong)Math.Abs(step);
            if (step > 0) return magnitude > ulong.MaxValue - value ? ulong.MaxValue : value + magnitude;
            return magnitude > value ? ulong.MinValue : value - magnitude;
        }
    }

    public partial class IntegerField : TextValueField<int>
    {
        public new static readonly string ussClassName = "unity-integer-field";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";

        sealed class IntegerInput : NumericInput
        {
            internal IntegerInput() : base(UINumericFieldsUtils.k_AllowedCharactersForInt, UINumericFieldsUtils.k_IntFieldFormatString) { }
        }

        public IntegerField() : this((string)null) { }

        public IntegerField(int maxLength) : this(null, maxLength) { }

        public IntegerField(string label, int maxLength = kMaxValueFieldLength)
            : base(label, maxLength, new IntegerInput())
        {
            AddToClassList(ussClassName);
            labelElement.AddToClassList(labelUssClassName);
            visualInput.AddToClassList(inputUssClassName);
            AddLabelDragger<int>();
        }

        protected override string ValueToString(int v) => v.ToString(formatString, CultureInfo.InvariantCulture.NumberFormat);

        private protected override string FormatForInput(int v) => v.ToString(formatString);

        protected override int StringToValue(string str)
        {
            bool success = UINumericFieldsUtils.TryConvertStringToInt(str, textInputBase.originalText, out var v, out var expression);
            expressionEvaluated?.Invoke(expression);
            return success ? v : rawValue;
        }

        internal override bool CanTryParse(string textString) => int.TryParse(textString, out _);

        public override void ApplyInputDeviceDelta(Vector3 delta, DeltaSpeed speed, int startValue)
        {
            long moved = TextValue + NumericFieldSteps.IntegerStep(delta, speed, NumericFieldDraggerUtility.CalculateIntDragSensitivity(startValue));
            ApplyDraggedValue(Mathf.ClampToInt(moved));
        }
    }

    public partial class LongField : TextValueField<long>
    {
        public new static readonly string ussClassName = "unity-long-field";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";

        sealed class LongInput : NumericInput
        {
            internal LongInput() : base(UINumericFieldsUtils.k_AllowedCharactersForInt, UINumericFieldsUtils.k_IntFieldFormatString) { }
        }

        public LongField() : this((string)null) { }

        public LongField(int maxLength) : this(null, maxLength) { }

        public LongField(string label, int maxLength = kMaxValueFieldLength)
            : base(label, maxLength, new LongInput())
        {
            AddToClassList(ussClassName);
            labelElement.AddToClassList(labelUssClassName);
            visualInput.AddToClassList(inputUssClassName);
            AddLabelDragger<long>();
        }

        protected override string ValueToString(long v) => v.ToString(formatString, CultureInfo.InvariantCulture.NumberFormat);

        private protected override string FormatForInput(long v) => v.ToString(formatString);

        protected override long StringToValue(string str)
        {
            bool success = UINumericFieldsUtils.TryConvertStringToLong(str, textInputBase.originalText, out var v, out var expression);
            expressionEvaluated?.Invoke(expression);
            return success ? v : rawValue;
        }

        internal override bool CanTryParse(string textString) => long.TryParse(textString, out _);

        public override void ApplyInputDeviceDelta(Vector3 delta, DeltaSpeed speed, long startValue)
        {
            long step = NumericFieldSteps.IntegerStep(delta, speed, NumericFieldDraggerUtility.CalculateIntDragSensitivity(startValue));
            ApplyDraggedValue(NumericFieldSteps.SaturatingAdd(TextValue, step));
        }
    }

    public partial class UnsignedIntegerField : TextValueField<uint>
    {
        public new static readonly string ussClassName = "unity-unsigned-integer-field";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";

        sealed class UnsignedIntegerInput : NumericInput
        {
            internal UnsignedIntegerInput() : base(UINumericFieldsUtils.k_AllowedCharactersForInt, UINumericFieldsUtils.k_IntFieldFormatString) { }
        }

        public UnsignedIntegerField() : this((string)null) { }

        public UnsignedIntegerField(int maxLength) : this(null, maxLength) { }

        public UnsignedIntegerField(string label, int maxLength = kMaxValueFieldLength)
            : base(label, maxLength, new UnsignedIntegerInput())
        {
            AddToClassList(ussClassName);
            labelElement.AddToClassList(labelUssClassName);
            visualInput.AddToClassList(inputUssClassName);
            AddLabelDragger<uint>();
        }

        protected override string ValueToString(uint v) => v.ToString(formatString, CultureInfo.InvariantCulture.NumberFormat);

        private protected override string FormatForInput(uint v) => v.ToString(formatString);

        protected override uint StringToValue(string str)
        {
            bool success = UINumericFieldsUtils.TryConvertStringToUInt(str, textInputBase.originalText, out var v, out var expression);
            expressionEvaluated?.Invoke(expression);
            return success ? v : rawValue;
        }

        internal override bool CanTryParse(string textString) => uint.TryParse(textString, out _);

        public override void ApplyInputDeviceDelta(Vector3 delta, DeltaSpeed speed, uint startValue)
        {
            long moved = TextValue + NumericFieldSteps.IntegerStep(delta, speed, NumericFieldDraggerUtility.CalculateIntDragSensitivity(startValue));
            ApplyDraggedValue(Mathf.ClampToUInt(moved));
        }
    }

    public partial class UnsignedLongField : TextValueField<ulong>
    {
        public new static readonly string ussClassName = "unity-unsigned-long-field";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";

        sealed class UnsignedLongInput : NumericInput
        {
            internal UnsignedLongInput() : base(UINumericFieldsUtils.k_AllowedCharactersForInt, UINumericFieldsUtils.k_IntFieldFormatString) { }
        }

        public UnsignedLongField() : this((string)null) { }

        public UnsignedLongField(int maxLength) : this(null, maxLength) { }

        public UnsignedLongField(string label, int maxLength = kMaxValueFieldLength)
            : base(label, maxLength, new UnsignedLongInput())
        {
            AddToClassList(ussClassName);
            labelElement.AddToClassList(labelUssClassName);
            visualInput.AddToClassList(inputUssClassName);
            AddLabelDragger<ulong>();
        }

        protected override string ValueToString(ulong v) => v.ToString(formatString, CultureInfo.InvariantCulture.NumberFormat);

        private protected override string FormatForInput(ulong v) => v.ToString(formatString);

        protected override ulong StringToValue(string str)
        {
            bool success = UINumericFieldsUtils.TryConvertStringToULong(str, textInputBase.originalText, out var v, out var expression);
            expressionEvaluated?.Invoke(expression);
            return success ? v : rawValue;
        }

        internal override bool CanTryParse(string textString) => ulong.TryParse(textString, out _);

        public override void ApplyInputDeviceDelta(Vector3 delta, DeltaSpeed speed, ulong startValue)
        {
            long step = NumericFieldSteps.IntegerStep(delta, speed, NumericFieldDraggerUtility.CalculateIntDragSensitivity(startValue));
            ApplyDraggedValue(NumericFieldSteps.SaturatingAdd(TextValue, step));
        }
    }

    public partial class FloatField : TextValueField<float>
    {
        public new static readonly string ussClassName = "unity-float-field";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";

        sealed class FloatInput : NumericInput
        {
            internal FloatInput() : base(UINumericFieldsUtils.k_AllowedCharactersForFloat, UINumericFieldsUtils.k_FloatFieldFormatString) { }
        }

        public FloatField() : this((string)null) { }

        public FloatField(int maxLength) : this(null, maxLength) { }

        public FloatField(string label, int maxLength = kMaxValueFieldLength)
            : base(label, maxLength, new FloatInput())
        {
            AddToClassList(ussClassName);
            labelElement.AddToClassList(labelUssClassName);
            visualInput.AddToClassList(inputUssClassName);
            AddLabelDragger<float>();
        }

        protected override string ValueToString(float v) => Unbound.MonoNumberFormat.Format(v, formatString, CultureInfo.InvariantCulture.NumberFormat);

        private protected override string FormatForInput(float v) => Unbound.MonoNumberFormat.Format(v, formatString, null);

        protected override float StringToValue(string str)
        {
            bool success = UINumericFieldsUtils.TryConvertStringToFloat(str, textInputBase.originalText, out var v, out var expression);
            expressionEvaluated?.Invoke(expression);
            return success ? v : rawValue;
        }

        /// <summary>Text that still shows the value (a float prints fewer digits than it holds) is
        /// not read again.</summary>
        internal override void UpdateValueFromText()
        {
            if (ValueToString(rawValue) != text) base.UpdateValueFromText();
        }

        internal override bool CanTryParse(string textString) => float.TryParse(textString, out _);

        public override void ApplyInputDeviceDelta(Vector3 delta, DeltaSpeed speed, float startValue)
        {
            double moved = NumericFieldSteps.RealStep(TextValue, delta, speed, NumericFieldDraggerUtility.CalculateFloatDragSensitivity(startValue));
            ApplyDraggedValue(Mathf.ClampToFloat(moved));
        }
    }

    public partial class DoubleField : TextValueField<double>
    {
        public new static readonly string ussClassName = "unity-double-field";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";

        sealed class DoubleInput : NumericInput
        {
            internal DoubleInput() : base(UINumericFieldsUtils.k_AllowedCharactersForFloat, UINumericFieldsUtils.k_DoubleFieldFormatString) { }
        }

        public DoubleField() : this((string)null) { }

        public DoubleField(int maxLength) : this(null, maxLength) { }

        public DoubleField(string label, int maxLength = kMaxValueFieldLength)
            : base(label, maxLength, new DoubleInput())
        {
            AddToClassList(ussClassName);
            labelElement.AddToClassList(labelUssClassName);
            visualInput.AddToClassList(inputUssClassName);
            AddLabelDragger<double>();
        }

        protected override string ValueToString(double v) => Unbound.MonoNumberFormat.Format(v, formatString, CultureInfo.InvariantCulture.NumberFormat);

        private protected override string FormatForInput(double v) => Unbound.MonoNumberFormat.Format(v, formatString, null);

        protected override double StringToValue(string str)
        {
            bool success = UINumericFieldsUtils.TryConvertStringToDouble(str, textInputBase.originalText, out var v, out var expression);
            expressionEvaluated?.Invoke(expression);
            return success ? v : rawValue;
        }

        internal override bool CanTryParse(string textString) => double.TryParse(textString, out _);

        public override void ApplyInputDeviceDelta(Vector3 delta, DeltaSpeed speed, double startValue)
        {
            ApplyDraggedValue(NumericFieldSteps.RealStep(TextValue, delta, speed, NumericFieldDraggerUtility.CalculateFloatDragSensitivity(startValue)));
        }
    }
}
