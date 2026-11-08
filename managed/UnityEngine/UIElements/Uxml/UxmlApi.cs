using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using Unbound.UIElements;

// The public API that custom controls use to take their values from UXML: the [UxmlElement] and
// [UxmlAttribute] declarations with their converters, and the older UxmlFactory/UxmlTraits classes
// with their typed attribute descriptions. Unbound's builder (VisualTreeAsset.cs) and value reader
// (UxmlAttributeValues.cs) give them their effect.

namespace UnityEngine.UIElements
{
    /// <summary>Gives the attribute values of one UXML element by name.</summary>
    public partial interface IUxmlAttributes
    {
        bool TryGetAttributeValue(string attributeName, out string value);
    }

    [Obsolete("IUxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.")]
    public partial interface IBaseUxmlFactory
    {
        bool AcceptsAttributeBag(IUxmlAttributes bag, CreationContext cc);
        string uxmlName { get; }
        string uxmlNamespace { get; }
        string uxmlQualifiedName { get; }
        Type uxmlType { get; }
        bool canHaveAnyAttribute { get; }
        IEnumerable<UxmlAttributeDescription> uxmlAttributesDescription { get; }
        IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription { get; }
        string substituteForTypeName { get; }
        string substituteForTypeNamespace { get; }
        string substituteForTypeQualifiedName { get; }
    }

    [Obsolete("IUxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.")]
    public partial interface IUxmlFactory : IBaseUxmlFactory
    {
        VisualElement Create(IUxmlAttributes bag, CreationContext cc);
    }

    internal partial interface IUxmlAssetAttributeDescription
    {
        Type assetType { get; }
    }

    /// <summary>What an element is being created for: the document cloned and the element it is
    /// cloned into.</summary>
    public partial struct CreationContext : IEquatable<CreationContext>
    {
        public static readonly CreationContext Default;

        internal CreationContext(VisualTreeAsset vta) : this(null, vta, null) { }

        internal CreationContext(Dictionary<string, VisualElement> slotInsertionPoints, VisualTreeAsset vta, VisualElement target)
        {
            this.slotInsertionPoints = slotInsertionPoints;
            visualTreeAsset = vta;
            this.target = target;
        }

        public VisualElement target { get; }

        public VisualTreeAsset visualTreeAsset { get; }

        public Dictionary<string, VisualElement> slotInsertionPoints { get; }

        public override bool Equals(object obj) => obj is CreationContext other && Equals(other);

        public bool Equals(CreationContext other) =>
            ReferenceEquals(target, other.target) && ReferenceEquals(visualTreeAsset, other.visualTreeAsset)
            && ReferenceEquals(slotInsertionPoints, other.slotInsertionPoints);

        public override int GetHashCode() => HashCode.Combine(target, visualTreeAsset, slotInsertionPoints);

        public static bool operator ==(CreationContext context1, CreationContext context2) => context1.Equals(context2);

        public static bool operator !=(CreationContext context1, CreationContext context2) => !context1.Equals(context2);
    }

    // ------------------------------------------------------------------ UxmlElement / UxmlAttribute

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public partial class UxmlElementAttribute : Attribute
    {
        public readonly string name;
        public string libraryPath;
        internal readonly Type[] supportedChildTypes;

        public UxmlElementAttribute() : this(null) { }

        public UxmlElementAttribute(string uxmlName) => name = uxmlName;

        public UxmlElementAttribute(string uxmlName, params Type[] supportedTypes)
        {
            name = uxmlName;
            supportedChildTypes = supportedTypes;
        }
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public partial class UxmlAttributeAttribute : Attribute
    {
        public string name;
        public string[] obsoleteNames;

        public UxmlAttributeAttribute() : this(null) { }

        public UxmlAttributeAttribute(string name) => this.name = name;

        public UxmlAttributeAttribute(string name, params string[] obsoleteNames)
        {
            this.name = name;
            this.obsoleteNames = obsoleteNames;
        }
    }

    [AttributeUsage(AttributeTargets.Property | AttributeTargets.Field)]
    public partial class UxmlTypeReferenceAttribute : PropertyAttribute
    {
        public Type baseType;

        public UxmlTypeReferenceAttribute() { }

        public UxmlTypeReferenceAttribute(Type baseType) => this.baseType = baseType;
    }

    // ------------------------------------------------------------------ attribute descriptions

    /// <summary>One attribute a UxmlTraits class reads: its name, the names it had before, and how
    /// its text is read from an element's attribute bag.</summary>
    public abstract partial class UxmlAttributeDescription
    {
        protected const string xmlSchemaNamespace = "http://www.w3.org/2001/XMLSchema";

        protected UxmlAttributeDescription() { }

        public string name { get; set; }

        public IEnumerable<string> obsoleteNames { get; set; }

        public string type { get; protected internal set; }

        public string typeNamespace { get; protected set; }

        public abstract string defaultValueAsString { get; }

        public UxmlAttributeDescription.Use use { get; set; }

        public enum Use
        {
            None = 0,
            Optional = 1,
            Prohibited = 2,
            Required = 3,
        }

        /// <summary>The attribute's text under its name or, failing that, the first of its obsolete
        /// names the bag has. Unbound's choice: the current name wins when both are present.</summary>
        internal bool TryGetValueFromBagAsString(IUxmlAttributes bag, CreationContext cc, out string value)
        {
            value = null;
            if (bag == null) return false;
            if (!string.IsNullOrEmpty(name) && bag.TryGetAttributeValue(name, out value)) return true;
            if (obsoleteNames != null)
            {
                foreach (var obsolete in obsoleteNames)
                    if (!string.IsNullOrEmpty(obsolete) && bag.TryGetAttributeValue(obsolete, out value)) return true;
            }
            value = null;
            return false;
        }

        protected bool TryGetValueFromBag<T>(IUxmlAttributes bag, CreationContext cc, Func<string, T, T> converterFunc, T defaultValue, ref T value)
        {
            if (!TryGetValueFromBagAsString(bag, cc, out var text)) return false;
            value = converterFunc != null ? converterFunc(text, defaultValue) : defaultValue;
            return true;
        }

        protected T GetValueFromBag<T>(IUxmlAttributes bag, CreationContext cc, Func<string, T, T> converterFunc, T defaultValue)
        {
            var value = defaultValue;
            TryGetValueFromBag(bag, cc, converterFunc, defaultValue, ref value);
            return value;
        }
    }

    public abstract partial class TypedUxmlAttributeDescription<T> : UxmlAttributeDescription
    {
        protected TypedUxmlAttributeDescription() { }

        public abstract T GetValueFromBag(IUxmlAttributes bag, CreationContext cc);

        public T defaultValue { get; set; }

        public override string defaultValueAsString => defaultValue == null ? "null" : defaultValue.ToString();
    }

    public partial class UxmlStringAttributeDescription : TypedUxmlAttributeDescription<string>
    {
        public UxmlStringAttributeDescription()
        {
            type = "string";
            typeNamespace = xmlSchemaNamespace;
            defaultValue = "";
        }

        public override string defaultValueAsString => defaultValue;

        public override string GetValueFromBag(IUxmlAttributes bag, CreationContext cc) => GetValueFromBag(bag, cc, (s, d) => s, defaultValue);

        public bool TryGetValueFromBag(IUxmlAttributes bag, CreationContext cc, ref string value) => TryGetValueFromBag(bag, cc, (s, d) => s, defaultValue, ref value);
    }

    public partial class UxmlFloatAttributeDescription : TypedUxmlAttributeDescription<float>
    {
        public UxmlFloatAttributeDescription()
        {
            type = "float";
            typeNamespace = xmlSchemaNamespace;
        }

        public override string defaultValueAsString => defaultValue.ToString(CultureInfo.InvariantCulture);

        public override float GetValueFromBag(IUxmlAttributes bag, CreationContext cc) => GetValueFromBag(bag, cc, Read, defaultValue);

        public bool TryGetValueFromBag(IUxmlAttributes bag, CreationContext cc, ref float value) => TryGetValueFromBag(bag, cc, Read, defaultValue, ref value);

        float Read(string text, float fallback) => UxmlAttributeValues.ReadTyped(this, text, fallback);
    }

    public partial class UxmlDoubleAttributeDescription : TypedUxmlAttributeDescription<double>
    {
        public UxmlDoubleAttributeDescription()
        {
            type = "double";
            typeNamespace = xmlSchemaNamespace;
        }

        public override string defaultValueAsString => defaultValue.ToString(CultureInfo.InvariantCulture);

        public override double GetValueFromBag(IUxmlAttributes bag, CreationContext cc) => GetValueFromBag(bag, cc, Read, defaultValue);

        public bool TryGetValueFromBag(IUxmlAttributes bag, CreationContext cc, ref double value) => TryGetValueFromBag(bag, cc, Read, defaultValue, ref value);

        double Read(string text, double fallback) => UxmlAttributeValues.ReadTyped(this, text, fallback);
    }

    public partial class UxmlIntAttributeDescription : TypedUxmlAttributeDescription<int>
    {
        public UxmlIntAttributeDescription()
        {
            type = "int";
            typeNamespace = xmlSchemaNamespace;
        }

        public override string defaultValueAsString => defaultValue.ToString(CultureInfo.InvariantCulture);

        public override int GetValueFromBag(IUxmlAttributes bag, CreationContext cc) => GetValueFromBag(bag, cc, Read, defaultValue);

        public bool TryGetValueFromBag(IUxmlAttributes bag, CreationContext cc, ref int value) => TryGetValueFromBag(bag, cc, Read, defaultValue, ref value);

        int Read(string text, int fallback) => UxmlAttributeValues.ReadTyped(this, text, fallback);
    }

    public partial class UxmlLongAttributeDescription : TypedUxmlAttributeDescription<long>
    {
        public UxmlLongAttributeDescription()
        {
            type = "long";
            typeNamespace = xmlSchemaNamespace;
        }

        public override string defaultValueAsString => defaultValue.ToString(CultureInfo.InvariantCulture);

        public override long GetValueFromBag(IUxmlAttributes bag, CreationContext cc) => GetValueFromBag(bag, cc, Read, defaultValue);

        public bool TryGetValueFromBag(IUxmlAttributes bag, CreationContext cc, ref long value) => TryGetValueFromBag(bag, cc, Read, defaultValue, ref value);

        long Read(string text, long fallback) => UxmlAttributeValues.ReadTyped(this, text, fallback);
    }

    public partial class UxmlUnsignedIntAttributeDescription : TypedUxmlAttributeDescription<uint>
    {
        public UxmlUnsignedIntAttributeDescription()
        {
            type = "unsignedInt";
            typeNamespace = xmlSchemaNamespace;
        }

        public override string defaultValueAsString => defaultValue.ToString(CultureInfo.InvariantCulture);

        public override uint GetValueFromBag(IUxmlAttributes bag, CreationContext cc) => GetValueFromBag(bag, cc, Read, defaultValue);

        public bool TryGetValueFromBag(IUxmlAttributes bag, CreationContext cc, ref uint value) => TryGetValueFromBag(bag, cc, Read, defaultValue, ref value);

        uint Read(string text, uint fallback) => UxmlAttributeValues.ReadTyped(this, text, fallback);
    }

    public partial class UxmlUnsignedLongAttributeDescription : TypedUxmlAttributeDescription<ulong>
    {
        public UxmlUnsignedLongAttributeDescription()
        {
            type = "unsignedLong";
            typeNamespace = xmlSchemaNamespace;
        }

        public override string defaultValueAsString => defaultValue.ToString(CultureInfo.InvariantCulture);

        public override ulong GetValueFromBag(IUxmlAttributes bag, CreationContext cc) => GetValueFromBag(bag, cc, Read, defaultValue);

        public bool TryGetValueFromBag(IUxmlAttributes bag, CreationContext cc, ref ulong value) => TryGetValueFromBag(bag, cc, Read, defaultValue, ref value);

        ulong Read(string text, ulong fallback) => UxmlAttributeValues.ReadTyped(this, text, fallback);
    }

    public partial class UxmlBoolAttributeDescription : TypedUxmlAttributeDescription<bool>
    {
        public UxmlBoolAttributeDescription()
        {
            type = "boolean";
            typeNamespace = xmlSchemaNamespace;
        }

        public override string defaultValueAsString => defaultValue ? "true" : "false";

        public override bool GetValueFromBag(IUxmlAttributes bag, CreationContext cc) => GetValueFromBag(bag, cc, Read, defaultValue);

        public bool TryGetValueFromBag(IUxmlAttributes bag, CreationContext cc, ref bool value) => TryGetValueFromBag(bag, cc, Read, defaultValue, ref value);

        bool Read(string text, bool fallback) => UxmlAttributeValues.ReadTyped(this, text, fallback);
    }

    public partial class UxmlColorAttributeDescription : TypedUxmlAttributeDescription<Color>
    {
        public UxmlColorAttributeDescription()
        {
            type = "string";
            typeNamespace = xmlSchemaNamespace;
        }

        public override string defaultValueAsString => "#" + ColorUtility.ToHtmlStringRGBA(defaultValue);

        public override Color GetValueFromBag(IUxmlAttributes bag, CreationContext cc) => GetValueFromBag(bag, cc, Read, defaultValue);

        public bool TryGetValueFromBag(IUxmlAttributes bag, CreationContext cc, ref Color value) => TryGetValueFromBag(bag, cc, Read, defaultValue, ref value);

        Color Read(string text, Color fallback) => UxmlAttributeValues.ReadTyped(this, text, fallback);
    }

    public partial class UxmlHash128AttributeDescription : TypedUxmlAttributeDescription<Hash128>
    {
        public UxmlHash128AttributeDescription()
        {
            type = "string";
            typeNamespace = xmlSchemaNamespace;
        }

        public override string defaultValueAsString => defaultValue.ToString();

        public override Hash128 GetValueFromBag(IUxmlAttributes bag, CreationContext cc) => GetValueFromBag(bag, cc, Read, defaultValue);

        public bool TryGetValueFromBag(IUxmlAttributes bag, CreationContext cc, ref Hash128 value) => TryGetValueFromBag(bag, cc, Read, defaultValue, ref value);

        Hash128 Read(string text, Hash128 fallback) => UxmlAttributeValues.ReadTyped(this, text, fallback);
    }

    public partial class UxmlEnumAttributeDescription<T> : TypedUxmlAttributeDescription<T> where T : struct, IConvertible
    {
        public UxmlEnumAttributeDescription()
        {
            type = "string";
            typeNamespace = xmlSchemaNamespace;
        }

        public override string defaultValueAsString => defaultValue.ToString();

        public override T GetValueFromBag(IUxmlAttributes bag, CreationContext cc) => GetValueFromBag(bag, cc, Read, defaultValue);

        public bool TryGetValueFromBag(IUxmlAttributes bag, CreationContext cc, ref T value) => TryGetValueFromBag(bag, cc, Read, defaultValue, ref value);

        T Read(string text, T fallback) => UxmlAttributeValues.ReadTyped(this, text, fallback);
    }

    public partial class UxmlTypeAttributeDescription<TBase> : TypedUxmlAttributeDescription<Type>
    {
        public UxmlTypeAttributeDescription()
        {
            type = "string";
            typeNamespace = xmlSchemaNamespace;
        }

        public override string defaultValueAsString => defaultValue == null ? "null" : defaultValue.FullName;

        public override Type GetValueFromBag(IUxmlAttributes bag, CreationContext cc) => GetValueFromBag(bag, cc, Read, defaultValue);

        public bool TryGetValueFromBag(IUxmlAttributes bag, CreationContext cc, ref Type value) => TryGetValueFromBag(bag, cc, Read, defaultValue, ref value);

        Type Read(string text, Type fallback) => UxmlAttributeValues.ReadTypeName(this, text, typeof(TBase), fallback);
    }

    /// <summary>An attribute naming an asset by its UXML url (`project://database/...`, `/Assets/...`
    /// or relative to the document).</summary>
    public partial class UxmlAssetAttributeDescription<T> : TypedUxmlAttributeDescription<T>, IUxmlAssetAttributeDescription where T : Object
    {
        public UxmlAssetAttributeDescription()
        {
            type = "string";
            typeNamespace = xmlSchemaNamespace;
        }

        public override string defaultValueAsString => defaultValue == null ? "null" : defaultValue.name;

        Type IUxmlAssetAttributeDescription.assetType => typeof(T);

        public override T GetValueFromBag(IUxmlAttributes bag, CreationContext cc)
        {
            TryGetValueFromBag(bag, cc, out var value);
            return value;
        }

        public bool TryGetValueFromBag(IUxmlAttributes bag, CreationContext cc, out T value)
        {
            value = defaultValue;
            if (!TryGetValueFromBagAsString(bag, cc, out var text)) return false;
            value = UxmlAttributeValues.ReadAsset(this, text, cc.visualTreeAsset, typeof(T)) as T;
            return true;
        }
    }

    /// <summary>A kind of element allowed as a child.</summary>
    public partial class UxmlChildElementDescription
    {
        public UxmlChildElementDescription(Type t)
        {
            if (t == null) throw new ArgumentNullException(nameof(t));
            elementName = t.Name;
            elementNamespace = t.Namespace ?? "";
        }

        public string elementName { get; protected set; }

        public string elementNamespace { get; protected set; }
    }

    // ------------------------------------------------------------------ UxmlTraits / UxmlFactory

    [Obsolete("BaseUxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.")]
    public abstract partial class BaseUxmlTraits
    {
        // Unbound does not validate attributes against the declared ones: every traits class accepts
        // any attribute unless it says otherwise.
        protected BaseUxmlTraits() => canHaveAnyAttribute = true;

        public bool canHaveAnyAttribute { get; protected set; }

        /// <summary>The descriptions held by this traits object's fields and properties, base classes
        /// included (Unbound reads them by reflection).</summary>
        public virtual IEnumerable<UxmlAttributeDescription> uxmlAttributesDescription => UxmlAttributeValues.DescriptionsOf(this, null);

        [Unbound.EditorFaithful("Base traits describe no children; Unity 6000.0.71f1, tests/projects/uxmlbaseapi")]
        public virtual IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription => Array.Empty<UxmlChildElementDescription>();
    }

    [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.")]
    public abstract partial class UxmlTraits : BaseUxmlTraits
    {
        protected UxmlTraits() { }

        [Unbound.EditorFaithful("Base initialization leaves elements unchanged and accepts null arguments; Unity 6000.0.71f1, tests/projects/uxmlbaseapi")]
        public virtual void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc) { }
    }

    [Obsolete("BaseUxmlFactory<TCreatedType, TTraits> is deprecated and will be removed. Use UxmlElementAttribute instead.")]
    public abstract partial class BaseUxmlFactory<TCreatedType, TTraits> where TCreatedType : new() where TTraits : BaseUxmlTraits, new()
    {
        internal TTraits m_Traits;

        protected BaseUxmlFactory() => m_Traits = new TTraits();

        [Unbound.EditorFaithful("Base factories accept every attribute bag, including null; Unity 6000.0.71f1, tests/projects/uxmlbaseapi")]
        public virtual bool AcceptsAttributeBag(IUxmlAttributes bag, CreationContext cc) => true;

        public virtual string uxmlName => typeof(TCreatedType).Name;

        public virtual string uxmlNamespace => typeof(TCreatedType).Namespace ?? string.Empty;

        public virtual string uxmlQualifiedName => typeof(TCreatedType).FullName;

        public virtual Type uxmlType => typeof(TCreatedType);

        public bool canHaveAnyAttribute => m_Traits.canHaveAnyAttribute;

        // Factories expose their traits' schema, including custom overrides and inherited descriptions
        // (Unity 6000.0.71f1, tests/projects/uxmlbaseapi).
        public virtual IEnumerable<UxmlAttributeDescription> uxmlAttributesDescription => m_Traits.uxmlAttributesDescription;

        public virtual IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription => m_Traits.uxmlChildElementsDescription;

        public virtual string substituteForTypeName => typeof(TCreatedType) != typeof(VisualElement) ? nameof(VisualElement) : string.Empty;

        public virtual string substituteForTypeNamespace => typeof(TCreatedType) != typeof(VisualElement) ? typeof(VisualElement).Namespace : string.Empty;

        public virtual string substituteForTypeQualifiedName => typeof(TCreatedType) != typeof(VisualElement) ? typeof(VisualElement).FullName : string.Empty;
    }

    [Obsolete("UxmlFactory<TCreatedType, TTraits> is deprecated and will be removed. Use UxmlElementAttribute instead.")]
    public partial class UxmlFactory<TCreatedType, TTraits> : BaseUxmlFactory<TCreatedType, TTraits>, IUxmlFactory, IBaseUxmlFactory where TCreatedType : VisualElement, new() where TTraits : UxmlTraits, new()
    {
        public UxmlFactory() { }

        /// <summary>Creates the element and initializes it through the traits' Init.</summary>
        public virtual VisualElement Create(IUxmlAttributes bag, CreationContext cc)
        {
            var element = new TCreatedType();
            m_Traits.Init(element, bag, cc);
            return element;
        }
    }

    [Obsolete("UxmlFactory<TCreatedType> is deprecated and will be removed. Use UxmlElementAttribute instead.")]
    public partial class UxmlFactory<TCreatedType> : UxmlFactory<TCreatedType, VisualElement.UxmlTraits> where TCreatedType : VisualElement, new()
    {
        public UxmlFactory() { }
    }

    public partial class VisualElement
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.")]
        public partial class UxmlFactory : UxmlFactory<VisualElement, VisualElement.UxmlTraits>
        {
            public UxmlFactory() { }
        }

        /// <summary>The attributes every element reads. The values are applied as Unbound's builder
        /// applies them to built-in elements.</summary>
        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.")]
        public partial class UxmlTraits : UnityEngine.UIElements.UxmlTraits
        {
            protected UxmlStringAttributeDescription m_Name = new UxmlStringAttributeDescription { name = "name" };
            protected UxmlEnumAttributeDescription<PickingMode> m_PickingMode = new UxmlEnumAttributeDescription<PickingMode> { name = "picking-mode" };
            readonly UxmlStringAttributeDescription viewDataKeyDescription = new UxmlStringAttributeDescription { name = "view-data-key" };
            readonly UxmlStringAttributeDescription tooltipDescription = new UxmlStringAttributeDescription { name = "tooltip" };
            readonly UxmlBoolAttributeDescription enabledDescription = new UxmlBoolAttributeDescription { name = "enabled" };

            public UxmlTraits()
            {
                // Unbound's names, the ones its builder reads for built-in elements.
                focusIndex = new UxmlIntAttributeDescription { name = "tabindex", obsoleteNames = new[] { "tab-index" } };
                focusable = new UxmlBoolAttributeDescription { name = "focusable" };
            }

            protected UxmlIntAttributeDescription focusIndex { get; set; }

            protected UxmlBoolAttributeDescription focusable { get; set; }

            public override IEnumerable<UxmlChildElementDescription> uxmlChildElementsDescription
            {
                get { yield return new UxmlChildElementDescription(typeof(VisualElement)); }
            }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                if (ve == null) throw new ArgumentNullException(nameof(ve));
                Apply(ve, bag, cc, m_Name);
                Apply(ve, bag, cc, viewDataKeyDescription);
                Apply(ve, bag, cc, m_PickingMode);
                Apply(ve, bag, cc, tooltipDescription);
                Apply(ve, bag, cc, focusIndex);
                Apply(ve, bag, cc, focusable);
                Apply(ve, bag, cc, enabledDescription);
            }

            /// <summary>Applies a present attribute under its description's name, so that the value
            /// is read exactly as the builder reads it on built-in elements.</summary>
            internal static void Apply(VisualElement ve, IUxmlAttributes bag, CreationContext cc, UxmlAttributeDescription description)
            {
                if (description != null && description.TryGetValueFromBagAsString(bag, cc, out var value))
                    UxmlBuilder.ApplyAttribute(ve, description.name, value);
            }
        }
    }

    public partial class BindableElement
    {
        [Obsolete("UxmlFactory is deprecated and will be removed. Use UxmlElementAttribute instead.")]
        public partial class UxmlFactory : UxmlFactory<BindableElement, BindableElement.UxmlTraits>
        {
            public UxmlFactory() { }
        }

        [Obsolete("UxmlTraits is deprecated and will be removed. Use UxmlElementAttribute instead.")]
        public partial class UxmlTraits : VisualElement.UxmlTraits
        {
            readonly UxmlStringAttributeDescription bindingPathDescription = new UxmlStringAttributeDescription { name = "binding-path" };

            public UxmlTraits() { }

            public override void Init(VisualElement ve, IUxmlAttributes bag, CreationContext cc)
            {
                base.Init(ve, bag, cc);
                Apply(ve, bag, cc, bindingPathDescription);
            }
        }
    }
}

namespace UnityEditor.UIElements
{
    /// <summary>Reads a [UxmlAttribute] of type <typeparamref name="T"/> from its UXML text. Unbound
    /// finds the non-abstract converters of the game's assemblies when it first needs one.</summary>
    public abstract partial class UxmlAttributeConverter<T>
    {
        protected UxmlAttributeConverter() { }

        public abstract T FromString(string value);

        public virtual string ToString(T value) => Convert.ToString(value, CultureInfo.InvariantCulture);
    }
}
