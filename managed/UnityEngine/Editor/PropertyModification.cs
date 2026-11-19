using System;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    /// <summary>A change to a property: of a prefab instance to its source prefab's object (the
    /// source object, the property's path, the value as text or an object reference), or one an
    /// edit records for Undo.</summary>
    [Serializable]
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    public sealed partial class PropertyModification
    {
        public Object target;
        public string propertyPath;
        public string value;
        public Object objectReference;

        public PropertyModification() { }
    }
}
