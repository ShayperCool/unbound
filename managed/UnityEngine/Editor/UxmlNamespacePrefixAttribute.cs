using System;

namespace UnityEditor.UIElements
{
    /// <summary>The prefix the UI Builder writes for an assembly's UXML namespace: an assembly
    /// attribute that only carries its two values.</summary>
    [AttributeUsage(AttributeTargets.Assembly, AllowMultiple = true)]
    public sealed partial class UxmlNamespacePrefixAttribute : Attribute
    {
        public string ns { get; }

        public string prefix { get; }

        public UxmlNamespacePrefixAttribute(string ns, string prefix)
        {
            this.ns = ns;
            this.prefix = prefix;
        }
    }
}
