using System.Collections.Generic;

namespace UnityEngine.UIElements
{
    /// <summary>Unity's C#: element names unique within the session.</summary>
    internal static partial class VisualElementUtils
    {
        static readonly HashSet<string> s_usedNames = new HashSet<string>();

        /// <summary>The base name, then the base name with 2, 3, ... for each later request.</summary>
        public static string GetUniqueName(string nameBase)
        {
            string name = nameBase;
            int counter = 2;
            while (s_usedNames.Contains(name))
            {
                name = nameBase + counter;
                counter++;
            }
            s_usedNames.Add(name);
            return name;
        }
    }
}
