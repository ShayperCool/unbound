namespace UnityEditor
{
    public partial class EditorUtility
    {
        /// <summary>
        /// The order the Editor lists names in (folders by AssetDatabase.GetSubFolders): characters
        /// compared case-insensitively by their code (so "_" sorts before letters and "é" after
        /// "z"), runs of ASCII digits compared as numbers of any length, and, when the names are
        /// otherwise equal, the shorter one first, then the one whose first differing number has
        /// more leading zeros ("a02" before "a2"). Names equal but for case compare equal. Matches
        /// the Unity 6000.0.71f1 Editor's signs (tests/projects/packagefolders natural.*); its
        /// magnitudes are not measured, and null is taken as an empty name (unmeasured).
        /// </summary>
        public static int NaturalCompare(string a, string b)
        {
            a ??= "";
            b ??= "";
            int i = 0, j = 0, zeros = 0;
            while (i < a.Length && j < b.Length)
            {
                if (IsDigit(a[i]) && IsDigit(b[j]))
                {
                    int startA = i, startB = j;
                    while (i < a.Length && a[i] == '0') i++;
                    while (j < b.Length && b[j] == '0') j++;
                    int numberA = i, numberB = j;
                    while (i < a.Length && IsDigit(a[i])) i++;
                    while (j < b.Length && IsDigit(b[j])) j++;
                    int lengthA = i - numberA, lengthB = j - numberB;
                    if (lengthA != lengthB) return lengthA < lengthB ? -1 : 1;
                    for (int k = 0; k < lengthA; k++)
                        if (a[numberA + k] != b[numberB + k]) return a[numberA + k] < b[numberB + k] ? -1 : 1;
                    if (zeros == 0)
                    {
                        int zerosA = numberA - startA, zerosB = numberB - startB;
                        if (zerosA != zerosB) zeros = zerosA > zerosB ? -1 : 1;
                    }
                    continue;
                }
                char x = char.ToLowerInvariant(a[i]), y = char.ToLowerInvariant(b[j]);
                if (x != y) return x < y ? -1 : 1;
                i++;
                j++;
            }
            int restA = a.Length - i, restB = b.Length - j;
            if (restA != restB) return restA < restB ? -1 : 1;
            return zeros;
        }

        static bool IsDigit(char c) => c >= '0' && c <= '9';
    }
}
