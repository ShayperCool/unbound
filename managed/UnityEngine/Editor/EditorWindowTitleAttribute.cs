using System;

namespace UnityEditor
{
    [global::System.@AttributeUsageAttribute((global::System.AttributeTargets)4)]
    public partial class EditorWindowTitleAttribute : Attribute
    {
        public string title { get; set; }
        public string icon { get; set; }
        public bool useTypeNameAsIconName { get; set; }

        public EditorWindowTitleAttribute() { }
    }
}
