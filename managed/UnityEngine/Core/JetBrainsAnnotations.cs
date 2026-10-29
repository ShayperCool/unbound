// Unity's JetBrains.Annotations attributes: data only, nothing in the engine reads them.
#pragma warning disable

namespace JetBrains.Annotations
{
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)2048)]
    public sealed partial class AssertionConditionAttribute : global::System.Attribute
    {
        public AssertionConditionAttribute(global::JetBrains.Annotations.AssertionConditionType conditionType) { ConditionType = conditionType; }
        public global::JetBrains.Annotations.AssertionConditionType ConditionType { get; }
    }
    public enum AssertionConditionType
    {
        IS_TRUE = 0,
        IS_FALSE = 1,
        IS_NULL = 2,
        IS_NOT_NULL = 3,
    }
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)64)]
    public sealed partial class AssertionMethodAttribute : global::System.Attribute
    {
        public AssertionMethodAttribute() { }
    }
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)4, AllowMultiple = true)]
    public sealed partial class BaseTypeRequiredAttribute : global::System.Attribute
    {
        public BaseTypeRequiredAttribute(global::System.Type baseType) { BaseType = baseType; }
        public global::System.Type BaseType { get; }
    }
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)24516)]
    public sealed partial class CanBeNullAttribute : global::System.Attribute
    {
        public CanBeNullAttribute() { }
    }
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)1036)]
    public sealed partial class CannotApplyEqualityOperatorAttribute : global::System.Attribute
    {
        public CannotApplyEqualityOperatorAttribute() { }
    }
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)224)]
    public sealed partial class CollectionAccessAttribute : global::System.Attribute
    {
        public CollectionAccessAttribute(global::JetBrains.Annotations.CollectionAccessType collectionAccessType) { CollectionAccessType = collectionAccessType; }
        public global::JetBrains.Annotations.CollectionAccessType CollectionAccessType { get; }
    }
    [global::System.Flags]
    public enum CollectionAccessType
    {
        None = 0,
        Read = 1,
        ModifyExistingContent = 2,
        UpdatedContent = 6,
    }
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)64, AllowMultiple = true)]
    public sealed partial class ContractAnnotationAttribute : global::System.Attribute
    {
        public ContractAnnotationAttribute(string contract) { Contract = contract; }
        public ContractAnnotationAttribute(string contract, bool forceFullStates) { Contract = contract; ForceFullStates = forceFullStates; }
        public string Contract { get; }
        public bool ForceFullStates { get; }
    }
    [global::System.Flags]
    public enum ImplicitUseKindFlags
    {
        Default = 7,
        Access = 1,
        Assign = 2,
        InstantiatedWithFixedConstructorSignature = 4,
        InstantiatedNoFixedConstructorSignature = 8,
    }
    [global::System.Flags]
    public enum ImplicitUseTargetFlags
    {
        Default = 1,
        Itself = 1,
        Members = 2,
        WithMembers = 3,
    }
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)2048)]
    public sealed partial class InstantHandleAttribute : global::System.Attribute
    {
        public InstantHandleAttribute() { }
    }
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)2048)]
    public sealed partial class InvokerParameterNameAttribute : global::System.Attribute
    {
        public InvokerParameterNameAttribute() { }
    }
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)6592)]
    public sealed partial class ItemCanBeNullAttribute : global::System.Attribute
    {
        public ItemCanBeNullAttribute() { }
    }
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)6592)]
    public sealed partial class ItemNotNullAttribute : global::System.Attribute
    {
        public ItemNotNullAttribute() { }
    }
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)64)]
    public sealed partial class LinqTunnelAttribute : global::System.Attribute
    {
        public LinqTunnelAttribute() { }
    }
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)32767)]
    public sealed partial class LocalizationRequiredAttribute : global::System.Attribute
    {
        public LocalizationRequiredAttribute() { }
        public LocalizationRequiredAttribute(bool required) { Required = required; }
        public bool Required { get; }
    }
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)2112, AllowMultiple = true)]
    public sealed partial class MacroAttribute : global::System.Attribute
    {
        public MacroAttribute() { }
        public string Expression { get; set; }
        public int Editable { get; set; }
        public string Target { get; set; }
    }
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)18436)]
    public sealed partial class MeansImplicitUseAttribute : global::System.Attribute
    {
        public MeansImplicitUseAttribute() { }
        public MeansImplicitUseAttribute(global::JetBrains.Annotations.ImplicitUseKindFlags useKindFlags) { UseKindFlags = useKindFlags; }
        public MeansImplicitUseAttribute(global::JetBrains.Annotations.ImplicitUseTargetFlags targetFlags) { TargetFlags = targetFlags; }
        public MeansImplicitUseAttribute(global::JetBrains.Annotations.ImplicitUseKindFlags useKindFlags, global::JetBrains.Annotations.ImplicitUseTargetFlags targetFlags) { UseKindFlags = useKindFlags; TargetFlags = targetFlags; }
        public global::JetBrains.Annotations.ImplicitUseKindFlags UseKindFlags { get; }
        public global::JetBrains.Annotations.ImplicitUseTargetFlags TargetFlags { get; }
    }
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)2156)]
    public sealed partial class MustDisposeResourceAttribute : global::System.Attribute
    {
        public MustDisposeResourceAttribute() { }
        public MustDisposeResourceAttribute(bool value) { Value = value; }
        public bool Value { get; }
    }
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)64)]
    public sealed partial class MustUseReturnValueAttribute : global::System.Attribute
    {
        public MustUseReturnValueAttribute() { }
        public MustUseReturnValueAttribute(string justification) { Justification = justification; }
        public string Justification { get; }
    }
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)2048)]
    public sealed partial class NoEnumerationAttribute : global::System.Attribute
    {
        public NoEnumerationAttribute() { }
    }
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)1052)]
    public sealed partial class NoReorderAttribute : global::System.Attribute
    {
        public NoReorderAttribute() { }
    }
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)24516)]
    public sealed partial class NotNullAttribute : global::System.Attribute
    {
        public NotNullAttribute() { }
    }
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)64)]
    public sealed partial class NotifyPropertyChangedInvocatorAttribute : global::System.Attribute
    {
        public NotifyPropertyChangedInvocatorAttribute() { }
        public NotifyPropertyChangedInvocatorAttribute(string parameterName) { ParameterName = parameterName; }
        public string ParameterName { get; }
    }
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)2048)]
    public sealed partial class PathReferenceAttribute : global::System.Attribute
    {
        public PathReferenceAttribute() { }
        public PathReferenceAttribute(string basePath) { BasePath = basePath; }
        public string BasePath { get; }
    }
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)19916)]
    public sealed partial class ProvidesContextAttribute : global::System.Attribute
    {
        public ProvidesContextAttribute() { }
    }
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)32767, Inherited = false)]
    public sealed partial class PublicAPIAttribute : global::System.Attribute
    {
        public PublicAPIAttribute() { }
        public PublicAPIAttribute(string comment) { Comment = comment; }
        public string Comment { get; }
    }
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)64)]
    public sealed partial class PureAttribute : global::System.Attribute
    {
        public PureAttribute() { }
    }
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)2048)]
    public sealed partial class RegexPatternAttribute : global::System.Attribute
    {
        public RegexPatternAttribute() { }
    }
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)64)]
    public sealed partial class SourceTemplateAttribute : global::System.Attribute
    {
        public SourceTemplateAttribute() { }
    }
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)4320)]
    public sealed partial class StringFormatMethodAttribute : global::System.Attribute
    {
        public StringFormatMethodAttribute(string formatParameterName) { FormatParameterName = formatParameterName; }
        public string FormatParameterName { get; }
    }
    [global::System.Obsolete("Use [ContractAnnotation('=> halt')] instead")]
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)64)]
    public sealed partial class TerminatesProgramAttribute : global::System.Attribute
    {
        public TerminatesProgramAttribute() { }
    }
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)32767, Inherited = false)]
    public sealed partial class UsedImplicitlyAttribute : global::System.Attribute
    {
        public UsedImplicitlyAttribute() { }
        public UsedImplicitlyAttribute(global::JetBrains.Annotations.ImplicitUseKindFlags useKindFlags) { UseKindFlags = useKindFlags; }
        public UsedImplicitlyAttribute(global::JetBrains.Annotations.ImplicitUseTargetFlags targetFlags) { TargetFlags = targetFlags; }
        public UsedImplicitlyAttribute(global::JetBrains.Annotations.ImplicitUseKindFlags useKindFlags, global::JetBrains.Annotations.ImplicitUseTargetFlags targetFlags) { UseKindFlags = useKindFlags; TargetFlags = targetFlags; }
        public global::JetBrains.Annotations.ImplicitUseKindFlags UseKindFlags { get; }
        public global::JetBrains.Annotations.ImplicitUseTargetFlags TargetFlags { get; }
    }
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)2432, AllowMultiple = true)]
    public sealed partial class ValueProviderAttribute : global::System.Attribute
    {
        public ValueProviderAttribute(string name) { Name = name; }
        public string Name { get; }
    }
}
