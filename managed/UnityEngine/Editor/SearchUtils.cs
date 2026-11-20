using System;

namespace UnityEditor
{
    /// <summary>
    /// Search matching helpers used by SettingsProvider.HasSearchInterest (measured:
    /// tests/projects/settingsprovider).
    /// </summary>
    internal static partial class SearchUtils
    {
        internal static bool MatchSearch(string searchContext, string content) =>
            content != null && searchContext != null && content.IndexOf(searchContext, StringComparison.OrdinalIgnoreCase) >= 0;

        internal static bool MatchSearchGroups(string searchContext, string content) =>
            MatchSearchGroups(searchContext, content, out _, out _);

        internal static bool MatchSearchGroups(string searchContext, string content, out int startIndex, out int endIndex)
        {
            startIndex = -1;
            endIndex = -1;
            if (string.IsNullOrEmpty(searchContext) || string.IsNullOrEmpty(content))
                return false;

            if (searchContext.Equals(content, StringComparison.CurrentCultureIgnoreCase))
            {
                startIndex = 0;
                endIndex = content.Length - 1;
                return true;
            }

            if (!searchContext.Contains(' '))
            {
                startIndex = content.IndexOf(searchContext, 0, StringComparison.CurrentCultureIgnoreCase);
                endIndex = startIndex + searchContext.Length - 1;
                return startIndex != -1;
            }

            string[] words = searchContext.Split(' ');
            int index = 0;
            foreach (string word in words)
            {
                if (word.Length == 0)
                    continue;
                index = content.IndexOf(word, index, StringComparison.CurrentCultureIgnoreCase);
                if (index == -1)
                    return false;
                if (startIndex == -1)
                    startIndex = index;
                index = endIndex = index + word.Length - 1;
            }

            return startIndex != -1 && endIndex != -1;
        }
    }
}
