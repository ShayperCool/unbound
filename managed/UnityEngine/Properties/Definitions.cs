using System;
using UnityEngine.Scripting;

// Unity.Properties (UnityEngine.PropertiesModule): the attributes, enums, delegates and exceptions.

namespace Unity.Properties
{
    /// <summary>Makes a member a property of its container's property bag (fields that would not be
    /// serialized, C# properties).</summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public partial class CreatePropertyAttribute : RequiredMemberAttribute
    {
        public bool ReadOnly { get; set; }
    }

    /// <summary>Keeps a field out of its container's property bag.</summary>
    [AttributeUsage(AttributeTargets.Field)]
    public partial class DontCreatePropertyAttribute : Attribute
    {
    }

    /// <summary>Asks the source generator for a property bag of the type.</summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct, Inherited = false)]
    public partial class GeneratePropertyBagAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = false)]
    public partial class GeneratePropertyBagsForAssemblyAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public partial class GeneratePropertyBagsForTypeAttribute : Attribute
    {
        public GeneratePropertyBagsForTypeAttribute(Type type)
        {
            if (!TypeTraits.IsContainer(type)) throw new ArgumentException(type.Name + " is not a valid container type.");
            Type = type;
        }

        public Type Type { get; }
    }

    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public partial class GeneratePropertyBagsForTypesQualifiedWithAttribute : Attribute
    {
        public GeneratePropertyBagsForTypesQualifiedWithAttribute(Type type, TypeGenerationOptions options = TypeGenerationOptions.Default)
        {
            if (type == null) throw new ArgumentException("type is null.");
            if (!type.IsInterface) throw new ArgumentException("GeneratePropertyBagsForTypesQualifiedWithAttribute Type must be an interface type.");
            Type = type;
            Options = options;
        }

        public Type Type { get; }
        public TypeGenerationOptions Options { get; }
    }

    [Flags]
    public enum TypeGenerationOptions
    {
        None = 0,
        ValueType = 2,
        ReferenceType = 4,
        Default = 6,
    }

    public enum InstantiationKind
    {
        Activator = 0,
        PropertyBagOverride = 1,
        NotInstantiatable = 2,
    }

    public enum PropertyPathPartKind
    {
        Name = 0,
        Index = 1,
        Key = 2,
    }

    [Flags]
    public enum VisitExceptionKind
    {
        None = 0,
        Internal = 1,
        Visitor = 2,
        All = 3,
    }

    public enum VisitReturnCode
    {
        Ok = 0,
        NullContainer = 1,
        InvalidContainerType = 2,
        MissingPropertyBag = 3,
        InvalidPath = 4,
        InvalidCast = 5,
        AccessViolation = 6,
    }

    /// <summary>How a visitation treats exceptions: those of the kinds named are swallowed.</summary>
    public partial struct VisitParameters
    {
        public VisitExceptionKind IgnoreExceptions { get; set; }
    }

    public delegate TValue PropertyGetter<TContainer, out TValue>(ref TContainer container);

    public delegate void PropertySetter<TContainer, in TValue>(ref TContainer container, TValue value);

    public delegate TDestination TypeConverter<TSource, out TDestination>(ref TSource value);

    [Serializable]
    public partial class InvalidContainerTypeException : Exception
    {
        public InvalidContainerTypeException(Type type) : base(Describe(type)) { Type = type; }

        public InvalidContainerTypeException(Type type, Exception inner) : base(Describe(type), inner) { Type = type; }

        public Type Type { get; }

        static string Describe(Type type) => "Invalid container Type=[" + type.Name + "." + type.Name + "]";
    }

    [Serializable]
    public partial class InvalidPathException : Exception
    {
        public InvalidPathException(string message) : base(message) { }

        public InvalidPathException(string message, Exception inner) : base(message, inner) { }
    }

    [Serializable]
    public partial class MissingPropertyBagException : Exception
    {
        public MissingPropertyBagException(Type type) : base(Describe(type)) { Type = type; }

        public MissingPropertyBagException(Type type, Exception inner) : base(Describe(type), inner) { Type = type; }

        public Type Type { get; }

        static string Describe(Type type) => "No PropertyBag was found for Type=[" + type.FullName + "]. Please make sure all types are declared ahead of time using [GeneratePropertyBagAttribute], [GeneratePropertyBagsForTypeAttribute] or [GeneratePropertyBagsForTypesQualifiedWithAttribute]";
    }
}

namespace Unity.Properties.Internal
{
    /// <summary>Marks a property bag the reflection provider made.</summary>
    internal partial class ReflectedPropertyBagAttribute : Attribute
    {
    }
}
