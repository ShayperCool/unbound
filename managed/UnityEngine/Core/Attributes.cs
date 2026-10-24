using System;

namespace UnityEngine
{
    [AttributeUsage(AttributeTargets.Field)]
    public sealed partial class SerializeField : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field)]
    public sealed partial class SerializeReference : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public partial class HideInInspector : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
    public sealed partial class RequireComponent : Attribute
    {
        public Type m_Type0;
        public Type m_Type1;
        public Type m_Type2;

        public RequireComponent(Type requiredComponent) { m_Type0 = requiredComponent; }

        public RequireComponent(Type requiredComponent, Type requiredComponent2)
        {
            m_Type0 = requiredComponent;
            m_Type1 = requiredComponent2;
        }

        public RequireComponent(Type requiredComponent, Type requiredComponent2, Type requiredComponent3)
        {
            m_Type0 = requiredComponent;
            m_Type1 = requiredComponent2;
            m_Type2 = requiredComponent3;
        }
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false, AllowMultiple = false)]
    public sealed partial class DisallowMultipleComponent : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed partial class ExecuteInEditMode : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed partial class ExecuteAlways : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class)]
    public partial class DefaultExecutionOrder : Attribute
    {
        public DefaultExecutionOrder(int order) { this.order = order; }
        public int order { get; private set; }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public sealed partial class AddComponentMenu : Attribute
    {
        public AddComponentMenu(string menuName) { componentMenu = menuName; }
        public AddComponentMenu(string menuName, int order) { componentMenu = menuName; componentOrder = order; }
        public string componentMenu { get; }
        public int componentOrder { get; }
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed partial class CreateAssetMenuAttribute : Attribute
    {
        public string menuName { get; set; }
        public string fileName { get; set; }
        public int order { get; set; }
    }

    [AttributeUsage(AttributeTargets.Method, AllowMultiple = true)]
    public sealed partial class ContextMenu : Attribute
    {
        public readonly string menuItem;
        public readonly bool validate;
        public readonly int priority;
        public ContextMenu(string itemName) : this(itemName, false) { }
        public ContextMenu(string itemName, bool isValidateFunction) : this(itemName, isValidateFunction, 1000000) { }
        public ContextMenu(string itemName, bool isValidateFunction, int priority)
        {
            menuItem = itemName;
            validate = isValidateFunction;
            this.priority = priority;
        }
    }

    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true)]
    public partial class ContextMenuItemAttribute : PropertyAttribute
    {
        public readonly string name;
        public readonly string function;
        public ContextMenuItemAttribute(string name, string function)
        {
            this.name = name;
            this.function = function;
        }
    }

    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public partial class HelpURLAttribute : Attribute
    {
        public HelpURLAttribute(string url) { URL = url; }
        public virtual string URL { get; }
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public partial class SelectionBaseAttribute : Attribute
    {
    }

    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Enum | AttributeTargets.Delegate, Inherited = false)]
    public sealed partial class ExcludeFromPresetAttribute : Attribute
    {
    }

    namespace Scripting
    {
        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Constructor | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Event | AttributeTargets.Interface | AttributeTargets.Delegate, Inherited = false)]
        public partial class PreserveAttribute : Attribute
        {
        }

        [AttributeUsage(AttributeTargets.Assembly | AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Constructor | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Event | AttributeTargets.Interface | AttributeTargets.Delegate, Inherited = false)]
        public partial class AlwaysLinkAssemblyAttribute : Attribute
        {
        }

        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
        public partial class RequireDerivedAttribute : Attribute
        {
        }

        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
        public partial class RequireImplementorsAttribute : Attribute
        {
        }

        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Event)]
        public partial class RequiredMemberAttribute : Attribute
        {
        }

        [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface)]
        public partial class RequireAttributeUsagesAttribute : Attribute
        {
        }
    }


    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public partial class RuntimeInitializeOnLoadMethodAttribute : Scripting.PreserveAttribute
    {
        public RuntimeInitializeOnLoadMethodAttribute() { loadType = RuntimeInitializeLoadType.AfterSceneLoad; }
        public RuntimeInitializeOnLoadMethodAttribute(RuntimeInitializeLoadType loadType) { this.loadType = loadType; }
        public RuntimeInitializeLoadType loadType { get; private set; }
    }

    // ------------------------------------------------------------------ inspector attributes

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public abstract partial class PropertyAttribute : Attribute
    {
        protected PropertyAttribute() { }
        protected PropertyAttribute(bool applyToCollection) { applyToCollection_ = applyToCollection; }
        readonly bool applyToCollection_;
        public int order { get; set; }
        public bool applyToCollection => applyToCollection_;
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = true)]
    public partial class HeaderAttribute : PropertyAttribute
    {
        public readonly string header;
        public HeaderAttribute(string header) { this.header = header; }
    }

    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Class, Inherited = true, AllowMultiple = true)]
    public partial class SpaceAttribute : PropertyAttribute
    {
        public readonly float height;
        public SpaceAttribute() { height = 8f; }
        public SpaceAttribute(float height) { this.height = height; }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public partial class TooltipAttribute : PropertyAttribute
    {
        public readonly string tooltip;
        public TooltipAttribute(string tooltip) { this.tooltip = tooltip; }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public sealed partial class RangeAttribute : PropertyAttribute
    {
        public readonly float min;
        public readonly float max;
        public RangeAttribute(float min, float max)
        {
            this.min = min;
            this.max = max;
        }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public sealed partial class MinAttribute : PropertyAttribute
    {
        public readonly float min;
        public MinAttribute(float min) { this.min = min; }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public sealed partial class MultilineAttribute : PropertyAttribute
    {
        public readonly int lines;
        public MultilineAttribute() { lines = 3; }
        public MultilineAttribute(int lines) { this.lines = lines; }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public sealed partial class TextAreaAttribute : PropertyAttribute
    {
        public readonly int minLines;
        public readonly int maxLines;
        public TextAreaAttribute() { minLines = 3; maxLines = 3; }
        public TextAreaAttribute(int minLines, int maxLines)
        {
            this.minLines = minLines;
            this.maxLines = maxLines;
        }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public sealed partial class ColorUsageAttribute : PropertyAttribute
    {
        public readonly bool showAlpha = true;
        public readonly bool hdr;
        public ColorUsageAttribute(bool showAlpha) { this.showAlpha = showAlpha; }
        public ColorUsageAttribute(bool showAlpha, bool hdr)
        {
            this.showAlpha = showAlpha;
            this.hdr = hdr;
        }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public sealed partial class GradientUsageAttribute : PropertyAttribute
    {
        public readonly bool hdr;
        public readonly ColorSpace colorSpace;
        public GradientUsageAttribute(bool hdr) { this.hdr = hdr; colorSpace = ColorSpace.Gamma; }
        public GradientUsageAttribute(bool hdr, ColorSpace colorSpace) { this.hdr = hdr; this.colorSpace = colorSpace; }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public sealed partial class DelayedAttribute : PropertyAttribute
    {
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public partial class InspectorNameAttribute : PropertyAttribute
    {
        public readonly string displayName;
        public InspectorNameAttribute(string displayName) { this.displayName = displayName; }
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public sealed partial class NonReorderableAttribute : PropertyAttribute
    {
    }

    [AttributeUsage(AttributeTargets.Field, Inherited = true, AllowMultiple = false)]
    public partial class InspectorOrderAttribute : PropertyAttribute
    {
        public InspectorOrderAttribute(InspectorSort inspectorSort = InspectorSort.ByName, InspectorSortDirection sortDirection = InspectorSortDirection.Ascending) { }
    }

    public enum InspectorSort
    {
        ByName = 0,
        ByValue = 1,
    }

    public enum InspectorSortDirection
    {
        Ascending = 0,
        Descending = 1,
    }

    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public partial class IconAttribute : Attribute
    {
        public IconAttribute(string path) { this.path = path; }
        public string path { get; }
    }


    public partial interface ISerializationCallbackReceiver
    {
        void OnBeforeSerialize();
        void OnAfterDeserialize();
    }

    /// <summary>Image effect markers: Opaque runs an OnRenderImage before transparent objects.</summary>
    public sealed partial class ImageEffectOpaque : Attribute { }

    public sealed partial class ImageEffectTransformsToLDR : Attribute { }

    public sealed partial class ImageEffectAllowedInSceneView : Attribute { }

    public sealed partial class ImageEffectAfterScale : Attribute { }

    [AttributeUsage(AttributeTargets.Method)]
    public sealed partial class ImageEffectUsesCommandBuffer : Attribute { }
}

namespace UnityEngine.Serialization
{
    [AttributeUsage(AttributeTargets.Field, AllowMultiple = true, Inherited = false)]
    public partial class FormerlySerializedAsAttribute : Attribute
    {
        readonly string m_oldName;
        public FormerlySerializedAsAttribute(string oldName) { m_oldName = oldName; }
        public string oldName => m_oldName;
    }
}

namespace UnityEngine.Scripting.APIUpdating
{
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Enum | AttributeTargets.Interface | AttributeTargets.Delegate)]
    public partial class MovedFromAttribute : Attribute
    {
        public MovedFromAttribute(bool autoUpdateAPI, string sourceNamespace = null, string sourceAssembly = null, string sourceClassName = null) { }
        public MovedFromAttribute(string sourceNamespace) { }
    }
}

namespace UnityEngine.Bindings
{
    // Attributes Unity's own engine code carries; packages occasionally reference them.
    [AttributeUsage(AttributeTargets.All)]
    internal partial class VisibleToOtherModulesAttribute : Attribute
    {
        public VisibleToOtherModulesAttribute() { }
        public VisibleToOtherModulesAttribute(params string[] modules) { }
    }
}
