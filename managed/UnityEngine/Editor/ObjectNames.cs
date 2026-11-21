using System.Text;

namespace UnityEditor
{
    /// <summary>The Editor's naming helpers.</summary>
    public sealed partial class ObjectNames
    {
        /// <summary>
        /// The Inspector's label for a field name: an `m_` prefix, a leading `_`, a `k` constant
        /// prefix (before an upper-case letter) and compiler auto-property wrappers are stripped,
        /// the first letter is capitalised, and words are separated at lowercase-to-uppercase/digit
        /// transitions and acronym/digit-to-word transitions ("m_myHTMLParser2" → "My HTML Parser 2",
        /// "u32" → "U32", "u32Value" → "U32 Value").
        /// </summary>
        public static string NicifyVariableName(string name)
        {
            if (string.IsNullOrEmpty(name)) return name ?? "";
            int start = 0;
            if (name.Length >= 2 && name[0] == 'm' && name[1] == '_') start = 2;
            else if (name[0] == '_') start = 1;
            if (start + 1 < name.Length && name[start] == 'k' && IsAsciiUpper(name[start + 1])) start++;
            if (start >= name.Length) return "";

            var sb = new StringBuilder(name.Length + 4);
            bool previousWasCapital = false;
            bool previousWasLowercase = false;
            bool previousWasNumber = false;
            bool capitalizeNext = false;
            bool insideAutoProperty = false;
            int i = start;

            if (IsAsciiLower(name[i]))
            {
                sb.Append((char)(name[i] - 32));
                previousWasCapital = true;
                i++;
            }

            while (i < name.Length)
            {
                char c = name[i];
                bool isUpper = IsAsciiUpper(c);
                bool isDigit = IsAsciiDigit(c);
                if (isUpper || isDigit)
                {
                    if (previousWasLowercase)
                    {
                        bool isDimension = isDigit && i - start >= 2 && name[i - 1] == 'x' && IsAsciiDigit(name[i - 2]);
                        if (!isDimension) sb.Append(' ');
                    }
                    else if (previousWasCapital && i + 1 < name.Length && IsAsciiLower(name[i + 1]))
                    {
                        if (!(previousWasNumber && isDigit)) sb.Append(' ');
                    }

                    capitalizeNext = false;
                    sb.Append(c);
                    previousWasCapital = true;
                    previousWasLowercase = false;
                    previousWasNumber = isDigit;
                    i++;
                    continue;
                }

                if (c == '<')
                {
                    capitalizeNext = true;
                    insideAutoProperty = true;
                    i++;
                    continue;
                }

                if (insideAutoProperty && c == '>')
                {
                    const string backingField = "k__BackingField";
                    if (i + 1 + backingField.Length <= name.Length &&
                        string.CompareOrdinal(name, i + 1, backingField, 0, backingField.Length) == 0)
                    {
                        i += 1 + backingField.Length;
                    }
                    else
                    {
                        sb.Append(' ');
                        i++;
                    }
                    continue;
                }

                bool isLower = IsAsciiLower(c);
                if (capitalizeNext)
                {
                    capitalizeNext = false;
                    if (isLower)
                    {
                        sb.Append((char)(c - 32));
                        previousWasCapital = true;
                        previousWasLowercase = false;
                    }
                    else
                    {
                        sb.Append(c);
                        previousWasCapital = false;
                        previousWasLowercase = false;
                    }
                }
                else
                {
                    sb.Append(c);
                    previousWasCapital = false;
                    previousWasLowercase = isLower;
                }
                previousWasNumber = false;
                i++;
            }

            return sb.ToString();
        }

        static bool IsAsciiUpper(char c) => c >= 'A' && c <= 'Z';
        static bool IsAsciiLower(char c) => c >= 'a' && c <= 'z';
        static bool IsAsciiDigit(char c) => c >= '0' && c <= '9';
    }
}
