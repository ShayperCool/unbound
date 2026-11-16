using System;
using System.Collections.Generic;

namespace UnityEngine.UIElements
{
    /// <summary>
    /// A field made of numeric sub-fields, one per component (matches the Unity 6000.0.71f1 Editor,
    /// tests/projects/compositefields): each sub-field is labelled with its component's name, a
    /// change of one sets the whole value, and setting the value shows its components (unless an
    /// edit in a sub-field still shows typed text). Up to <c>fieldsByLine</c> sub-fields per line,
    /// filled to three with invisible spacers; several lines go into field groups.
    /// </summary>
    public abstract partial class BaseCompositeField<TValueType, TField, TFieldValue> : BaseField<TValueType>
        where TField : TextValueField<TFieldValue>, new()
    {
        /// <summary>One component: its label, its sub-field's name and how to read and write it.</summary>
        internal partial struct FieldDescription
        {
            public delegate void WriteDelegate(ref TValueType val, TFieldValue fieldValue);

            internal readonly string name;
            internal readonly string ussName;
            internal readonly Func<TValueType, TFieldValue> read;
            internal readonly WriteDelegate write;

            public FieldDescription(string name, string ussName, Func<TValueType, TFieldValue> read, WriteDelegate write)
            {
                this.name = name;
                this.ussName = ussName;
                this.read = read;
                this.write = write;
            }
        }

        public new static readonly string ussClassName = "unity-composite-field";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";
        public static readonly string spacerUssClassName = ussClassName + "__field-spacer";
        public static readonly string multilineVariantUssClassName = ussClassName + "--multi-line";
        public static readonly string fieldGroupUssClassName = ussClassName + "__field-group";
        public static readonly string fieldUssClassName = ussClassName + "__field";
        public static readonly string firstFieldVariantUssClassName = fieldUssClassName + "--first";
        public static readonly string twoLinesVariantUssClassName = ussClassName + "--two-lines";

        /// <summary>Sub-fields per line are padded to this many with spacers.</summary>
        const int kFieldsPerLineWithSpacers = 3;

        readonly List<TField> m_Fields = new List<TField>();
        bool m_ShouldUpdateDisplay = true;

        internal List<TField> fields => m_Fields;

        /// <summary>The components, in order (the same array on every call).</summary>
        internal abstract FieldDescription[] DescribeFields();

        protected BaseCompositeField(string label, int fieldsByLine) : base(label, null)
        {
            delegatesFocus = false;
            visualInput.focusable = false;
            AddToClassList(ussClassName);
            labelElement.AddToClassList(labelUssClassName);
            visualInput.AddToClassList(inputUssClassName);
            var descriptions = DescribeFields();
            int lines = fieldsByLine > 1 ? descriptions.Length / fieldsByLine : 1;
            bool multiline = lines > 1;
            if (multiline) AddToClassList(multilineVariantUssClassName);
            for (int line = 0; line < lines; line++)
            {
                VisualElement group = null;
                if (multiline)
                {
                    group = new VisualElement();
                    group.AddToClassList(fieldGroupUssClassName);
                }
                VisualElement parent = group ?? visualInput;
                for (int i = line * fieldsByLine; i < (line + 1) * fieldsByLine; i++)
                {
                    var field = CreateSubField(descriptions[i], i == line * fieldsByLine);
                    m_Fields.Add(field);
                    if (multiline) parent.Add(field);
                    else parent.hierarchy.Add(field);
                }
                for (int spacer = fieldsByLine; spacer < kFieldsPerLineWithSpacers; spacer++)
                {
                    if (multiline) parent.Add(CreateSpacer());
                    else parent.hierarchy.Add(CreateSpacer());
                }
                if (multiline) visualInput.hierarchy.Add(group);
            }
            UpdateDisplay();
        }

        TField CreateSubField(FieldDescription description, bool first)
        {
            var field = new TField { name = description.ussName };
            field.delegatesFocus = true;
            field.AddToClassList(fieldUssClassName);
            if (first) field.AddToClassList(firstFieldVariantUssClassName);
            field.label = description.name;
            field.onValidateValue += componentValue =>
            {
                var whole = value;
                description.write(ref whole, componentValue);
                return description.read(ValidatedValue(whole));
            };
            field.RegisterValueChangedCallback(e =>
            {
                var whole = value;
                description.write(ref whole, e.newValue);
                // Text still being edited (an expression, or an emptied field) is kept while the
                // whole value changes; so is the text of a value the sub-field reads as typed.
                var text = ((TField)e.currentTarget).text;
                if (PrintedValue(e.newValue) != text || field.CanTryParse(text)) m_ShouldUpdateDisplay = false;
                value = whole;
                m_ShouldUpdateDisplay = true;
            });
            return field;
        }

        /// <summary>A component's default text as the release's runtime prints it.</summary>
        static string PrintedValue(TFieldValue v) => v is float f ? Unbound.MonoNumberFormat.Format(f, null, null) : v.ToString();

        static VisualElement CreateSpacer()
        {
            var spacer = new VisualElement();
            spacer.AddToClassList(spacerUssClassName);
            spacer.visible = false;
            spacer.focusable = false;
            return spacer;
        }

        void UpdateDisplay()
        {
            var descriptions = DescribeFields();
            for (int i = 0; i < m_Fields.Count; i++) m_Fields[i].SetValueWithoutNotify(descriptions[i].read(rawValue));
        }

        public override void SetValueWithoutNotify(TValueType newValue)
        {
            bool refresh = m_ShouldUpdateDisplay && !EqualityComparer<TValueType>.Default.Equals(rawValue, newValue);
            base.SetValueWithoutNotify(newValue);
            if (refresh) UpdateDisplay();
        }

        protected override void UpdateMixedValueContent()
        {
            foreach (var field in m_Fields) field.showMixedValue = showMixedValue;
        }
    }

    public partial class Vector2Field : BaseCompositeField<Vector2, FloatField, float>
    {
        public new static readonly string ussClassName = "unity-vector2-field";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";

        static readonly FieldDescription[] s_Fields =
        {
            new FieldDescription("X", "unity-x-input", v => v.x, (ref Vector2 v, float c) => v.x = c),
            new FieldDescription("Y", "unity-y-input", v => v.y, (ref Vector2 v, float c) => v.y = c),
        };

        internal override FieldDescription[] DescribeFields() => s_Fields;

        public Vector2Field() : this(null) { }

        public Vector2Field(string label) : base(label, 2)
        {
            AddToClassList(ussClassName);
            labelElement.AddToClassList(labelUssClassName);
            visualInput.AddToClassList(inputUssClassName);
        }
    }

    public partial class Vector3Field : BaseCompositeField<Vector3, FloatField, float>
    {
        public new static readonly string ussClassName = "unity-vector3-field";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";

        static readonly FieldDescription[] s_Fields =
        {
            new FieldDescription("X", "unity-x-input", v => v.x, (ref Vector3 v, float c) => v.x = c),
            new FieldDescription("Y", "unity-y-input", v => v.y, (ref Vector3 v, float c) => v.y = c),
            new FieldDescription("Z", "unity-z-input", v => v.z, (ref Vector3 v, float c) => v.z = c),
        };

        internal override FieldDescription[] DescribeFields() => s_Fields;

        public Vector3Field() : this(null) { }

        public Vector3Field(string label) : base(label, 3)
        {
            AddToClassList(ussClassName);
            labelElement.AddToClassList(labelUssClassName);
            visualInput.AddToClassList(inputUssClassName);
        }
    }

    public partial class Vector4Field : BaseCompositeField<Vector4, FloatField, float>
    {
        public new static readonly string ussClassName = "unity-vector4-field";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";

        static readonly FieldDescription[] s_Fields =
        {
            new FieldDescription("X", "unity-x-input", v => v.x, (ref Vector4 v, float c) => v.x = c),
            new FieldDescription("Y", "unity-y-input", v => v.y, (ref Vector4 v, float c) => v.y = c),
            new FieldDescription("Z", "unity-z-input", v => v.z, (ref Vector4 v, float c) => v.z = c),
            new FieldDescription("W", "unity-w-input", v => v.w, (ref Vector4 v, float c) => v.w = c),
        };

        internal override FieldDescription[] DescribeFields() => s_Fields;

        public Vector4Field() : this(null) { }

        public Vector4Field(string label) : base(label, 4)
        {
            AddToClassList(ussClassName);
            labelElement.AddToClassList(labelUssClassName);
            visualInput.AddToClassList(inputUssClassName);
        }
    }

    public partial class Vector2IntField : BaseCompositeField<Vector2Int, IntegerField, int>
    {
        public new static readonly string ussClassName = "unity-vector2-int-field";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";

        static readonly FieldDescription[] s_Fields =
        {
            new FieldDescription("X", "unity-x-input", v => v.x, (ref Vector2Int v, int c) => v.x = c),
            new FieldDescription("Y", "unity-y-input", v => v.y, (ref Vector2Int v, int c) => v.y = c),
        };

        internal override FieldDescription[] DescribeFields() => s_Fields;

        public Vector2IntField() : this(null) { }

        public Vector2IntField(string label) : base(label, 2)
        {
            AddToClassList(ussClassName);
            labelElement.AddToClassList(labelUssClassName);
            visualInput.AddToClassList(inputUssClassName);
        }
    }

    public partial class Vector3IntField : BaseCompositeField<Vector3Int, IntegerField, int>
    {
        public new static readonly string ussClassName = "unity-vector3-int-field";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";

        static readonly FieldDescription[] s_Fields =
        {
            new FieldDescription("X", "unity-x-input", v => v.x, (ref Vector3Int v, int c) => v.x = c),
            new FieldDescription("Y", "unity-y-input", v => v.y, (ref Vector3Int v, int c) => v.y = c),
            new FieldDescription("Z", "unity-z-input", v => v.z, (ref Vector3Int v, int c) => v.z = c),
        };

        internal override FieldDescription[] DescribeFields() => s_Fields;

        public Vector3IntField() : this(null) { }

        public Vector3IntField(string label) : base(label, 3)
        {
            AddToClassList(ussClassName);
            labelElement.AddToClassList(labelUssClassName);
            visualInput.AddToClassList(inputUssClassName);
        }
    }

    public partial class RectField : BaseCompositeField<Rect, FloatField, float>
    {
        public new static readonly string ussClassName = "unity-rect-field";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";

        static readonly FieldDescription[] s_Fields =
        {
            new FieldDescription("X", "unity-x-input", r => r.x, (ref Rect r, float c) => r.x = c),
            new FieldDescription("Y", "unity-y-input", r => r.y, (ref Rect r, float c) => r.y = c),
            new FieldDescription("W", "unity-width-input", r => r.width, (ref Rect r, float c) => r.width = c),
            new FieldDescription("H", "unity-height-input", r => r.height, (ref Rect r, float c) => r.height = c),
        };

        internal override FieldDescription[] DescribeFields() => s_Fields;

        public RectField() : this(null) { }

        public RectField(string label) : base(label, 2)
        {
            AddToClassList(ussClassName);
            AddToClassList(twoLinesVariantUssClassName);
            labelElement.AddToClassList(labelUssClassName);
            visualInput.AddToClassList(inputUssClassName);
        }
    }

    public partial class RectIntField : BaseCompositeField<RectInt, IntegerField, int>
    {
        public new static readonly string ussClassName = "unity-rect-int-field";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";

        static readonly FieldDescription[] s_Fields =
        {
            new FieldDescription("X", "unity-x-input", r => r.x, (ref RectInt r, int c) => r.x = c),
            new FieldDescription("Y", "unity-y-input", r => r.y, (ref RectInt r, int c) => r.y = c),
            new FieldDescription("W", "unity-width-input", r => r.width, (ref RectInt r, int c) => r.width = c),
            new FieldDescription("H", "unity-height-input", r => r.height, (ref RectInt r, int c) => r.height = c),
        };

        internal override FieldDescription[] DescribeFields() => s_Fields;

        public RectIntField() : this(null) { }

        public RectIntField(string label) : base(label, 2)
        {
            AddToClassList(ussClassName);
            AddToClassList(twoLinesVariantUssClassName);
            labelElement.AddToClassList(labelUssClassName);
            visualInput.AddToClassList(inputUssClassName);
        }
    }

    /// <summary>The two vector fields a bounds field is made of: each edits one part of the value.</summary>
    internal static class BoundsParts
    {
        internal static TPart Add<TValue, TPart, TVector>(BaseField<TValue> owner, TPart part, string name, string className, Func<TValue, TVector, TValue> write)
            where TPart : BaseField<TVector>
        {
            part.name = name;
            part.delegatesFocus = true;
            part.AddToClassList(className);
            part.RegisterValueChangedCallback(e => owner.value = write(owner.value, e.newValue));
            owner.visualInput.hierarchy.Add(part);
            return part;
        }
    }

    public partial class BoundsField : BaseField<Bounds>
    {
        public new static readonly string ussClassName = "unity-bounds-field";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";
        public static readonly string centerFieldUssClassName = ussClassName + "__center-field";
        public static readonly string extentsFieldUssClassName = ussClassName + "__extents-field";

        readonly Vector3Field m_CenterField;
        readonly Vector3Field m_ExtentsField;

        public BoundsField() : this(null) { }

        public BoundsField(string label) : base(label, null)
        {
            delegatesFocus = false;
            visualInput.focusable = false;
            AddToClassList(ussClassName);
            visualInput.AddToClassList(inputUssClassName);
            labelElement.AddToClassList(labelUssClassName);
            m_CenterField = BoundsParts.Add<Bounds, Vector3Field, Vector3>(this, new Vector3Field("Center"), "unity-m_Center-input", centerFieldUssClassName, (b, v) => { b.center = v; return b; });
            m_ExtentsField = BoundsParts.Add<Bounds, Vector3Field, Vector3>(this, new Vector3Field("Extents"), "unity-m_Extent-input", extentsFieldUssClassName, (b, v) => { b.extents = v; return b; });
        }

        public override void SetValueWithoutNotify(Bounds newValue)
        {
            base.SetValueWithoutNotify(newValue);
            m_CenterField.SetValueWithoutNotify(rawValue.center);
            m_ExtentsField.SetValueWithoutNotify(rawValue.extents);
        }

        protected override void UpdateMixedValueContent()
        {
            m_CenterField.showMixedValue = showMixedValue;
            m_ExtentsField.showMixedValue = showMixedValue;
        }
    }

    public partial class BoundsIntField : BaseField<BoundsInt>
    {
        public new static readonly string ussClassName = "unity-bounds-int-field";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";
        public static readonly string positionUssClassName = ussClassName + "__position-field";
        public static readonly string sizeUssClassName = ussClassName + "__size-field";

        readonly Vector3IntField m_PositionField;
        readonly Vector3IntField m_SizeField;

        public BoundsIntField() : this(null) { }

        public BoundsIntField(string label) : base(label, null)
        {
            delegatesFocus = false;
            visualInput.focusable = false;
            AddToClassList(ussClassName);
            visualInput.AddToClassList(inputUssClassName);
            labelElement.AddToClassList(labelUssClassName);
            m_PositionField = BoundsParts.Add<BoundsInt, Vector3IntField, Vector3Int>(this, new Vector3IntField("Position"), "unity-m_Position-input", positionUssClassName, (b, v) => { b.position = v; return b; });
            m_SizeField = BoundsParts.Add<BoundsInt, Vector3IntField, Vector3Int>(this, new Vector3IntField("Size"), "unity-m_Size-input", sizeUssClassName, (b, v) => { b.size = v; return b; });
        }

        public override void SetValueWithoutNotify(BoundsInt newValue)
        {
            base.SetValueWithoutNotify(newValue);
            m_PositionField.SetValueWithoutNotify(rawValue.position);
            m_SizeField.SetValueWithoutNotify(rawValue.size);
        }

        protected override void UpdateMixedValueContent()
        {
            m_PositionField.showMixedValue = showMixedValue;
            m_SizeField.showMixedValue = showMixedValue;
        }
    }

    /// <summary>A text field holding a Hash128 typed as hexadecimal digits (one digit reads as that
    /// number); an emptied field leaving focus takes the default hash.</summary>
    public partial class Hash128Field : TextInputBaseField<Hash128>
    {
        public new static readonly string ussClassName = "unity-hash128-field";
        public new static readonly string labelUssClassName = ussClassName + "__label";
        public new static readonly string inputUssClassName = ussClassName + "__input";

        public Hash128Field() : this((string)null) { }

        public Hash128Field(int maxLength) : this(null, maxLength) { }

        public Hash128Field(string label, int maxLength = kMaxLengthNone) : base(label, maxLength, char.MinValue, new Hash128Input())
        {
            SetValueWithoutNotify(new Hash128());
            AddToClassList(ussClassName);
            labelElement.AddToClassList(labelUssClassName);
            visualInput.AddToClassList(inputUssClassName);
        }

        public override Hash128 value
        {
            get => base.value;
            set
            {
                base.value = value;
                if (m_UpdateTextFromValue) text = rawValue.ToString();
            }
        }

        internal override void UpdateValueFromText()
        {
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

        internal override void UpdateTextFromValue() => text = ValueToString(rawValue);

        public override void SetValueWithoutNotify(Hash128 newValue)
        {
            base.SetValueWithoutNotify(newValue);
            if (m_UpdateTextFromValue) text = rawValue.ToString();
        }

        protected override string ValueToString(Hash128 value) => value.ToString();

        protected override Hash128 StringToValue(string str) => Hash128Input.Parse(str);

        [global::UnityEngine.UIElements.@EventInterestAttribute(new global::System.Type[] { typeof(global::UnityEngine.UIElements.FocusOutEvent) })]
        protected override void HandleEventBubbleUp(EventBase evt)
        {
            base.HandleEventBubbleUp(evt);
            if (isReadOnly || evt.eventTypeId != FocusOutEvent.TypeId()) return;
            if (string.IsNullOrEmpty(text))
            {
                value = new Hash128();
            }
            else
            {
                textInputBase.UpdateValueFromText();
                textInputBase.UpdateTextFromValue();
            }
        }

        sealed class Hash128Input : TextInputBase
        {
            const string k_AllowedCharacters = "0123456789abcdefABCDEF";

            internal override bool AcceptCharacter(char c) => base.AcceptCharacter(c) && c != 0 && k_AllowedCharacters.IndexOf(c) != -1;

            protected override Hash128 StringToValue(string str) => Parse(str);

            /// <summary>Hash128.Parse, except that a single decimal digit reads as that number.</summary>
            internal static Hash128 Parse(string str) =>
                str.Length == 1 && ulong.TryParse(str, out var single) ? new Hash128(single, 0L) : Hash128.Parse(str);
        }
    }
}
