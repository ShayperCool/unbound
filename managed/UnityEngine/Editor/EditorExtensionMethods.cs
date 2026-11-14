using System;
using System.Collections.Generic;

namespace UnityEditor
{
    /// <summary>Type helpers editor code shares.</summary>
    internal static partial class EditorExtensionMethods
    {
        /// <summary>Whether a type is an array or a <c>List&lt;T&gt;</c>: the collections Unity serializes.</summary>
        internal static bool IsArrayOrList(this Type listType) =>
            listType.IsArray || listType.IsGenericType && listType.GetGenericTypeDefinition() == typeof(List<>);

        /// <summary>The element type of an array or a <c>List&lt;T&gt;</c>; null for another type.</summary>
        internal static Type GetArrayOrListElementType(this Type listType)
        {
            if (listType.IsArray) return listType.GetElementType();
            if (listType.IsGenericType && listType.GetGenericTypeDefinition() == typeof(List<>)) return listType.GetGenericArguments()[0];
            return null;
        }
    }
}
