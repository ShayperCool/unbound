// Unity's UnityEngine.Categorization attributes: data only, nothing in the engine reads them.
#pragma warning disable

namespace UnityEngine.Categorization
{
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)4, Inherited = false, AllowMultiple = false)]
    public sealed partial class CategoryInfoAttribute : global::System.Attribute
    {
        public CategoryInfoAttribute() { }
        public int Order { get; set; }
        public string Name { get; set; }
    }
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)4, Inherited = false, AllowMultiple = false)]
    public sealed partial class ElementInfoAttribute : global::System.Attribute
    {
        public ElementInfoAttribute() { }
        public int Order { get; set; }
        public string Name { get; set; }
    }
}
