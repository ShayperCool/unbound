using System.Text;

namespace UnityEditor
{
    /// <summary>The Editor's naming helpers.</summary>
    public sealed partial class ObjectNames
    {
        /// <summary>
        /// The Inspector's label for a field name: an `m_` prefix, a `k` constant prefix (before an
        /// upper-case letter) and leading underscores are dropped, the first letter is capitalised and
        /// words are split at case changes and digits ("m_myHTMLParser2" → "My HTML Parser 2").
        /// </summary>
        public static string NicifyVariableName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name ?? "";
            int start = 0;
            if (name.Length > 2 && name[0] == 'm' && name[1] == '_') start = 2;
            else if (name.Length > 1 && name[0] == 'k' && char.IsUpper(name[1])) start = 1;
            while (start < name.Length - 1 && name[start] == '_') start++;
            var sb = new StringBuilder(name.Length + 4);
            for (int i = start; i < name.Length; i++)
            {
                char c = name[i];
                if (sb.Length == 0)
                {
                    sb.Append(char.ToUpperInvariant(c));
                    continue;
                }
                char prev = name[i - 1];
                char next = i + 1 < name.Length ? name[i + 1] : '\0';
                if (char.IsUpper(c))
                {
                    if (char.IsLower(prev) || ((char.IsUpper(prev) || char.IsDigit(prev)) && char.IsLower(next)))
                        sb.Append(' ');
                }
                else if (char.IsDigit(c))
                {
                    if (char.IsLetter(prev)) sb.Append(' ');
                }
                sb.Append(c);
            }
            return sb.ToString();
        }
    }
}
