using System;
using System.Globalization;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>The OS's user interface language, as Application.systemLanguage reports it: the
    /// POSIX locale variables (LC_ALL, LC_MESSAGES, LANG), else .NET's UI culture.</summary>
    internal static class SystemLocale
    {
        static SystemLanguage? s_Language;

        internal static SystemLanguage Language => s_Language ??= LanguageOf(Name());

        static string Name()
        {
            foreach (var variable in new[] { "LC_ALL", "LC_MESSAGES", "LANG" })
            {
                var value = Environment.GetEnvironmentVariable(variable);
                if (!string.IsNullOrEmpty(value)) return value;
            }
            try { return CultureInfo.CurrentUICulture.Name; }
            catch (Exception) { return ""; }
        }

        /// <summary>The language of a locale name: POSIX (<c>zh_TW.UTF-8</c>, <c>sr@latin</c>) or
        /// BCP 47 (<c>zh-Hant-HK</c>). Chinese is Simplified or Traditional by script or region.</summary>
        internal static SystemLanguage LanguageOf(string locale)
        {
            if (string.IsNullOrEmpty(locale)) return SystemLanguage.Unknown;
            int cut = locale.IndexOfAny(new[] { '.', '@' });
            var parts = (cut >= 0 ? locale.Substring(0, cut) : locale).Split('_', '-');
            var language = parts[0].ToLowerInvariant();
            if (language == "c" || language == "posix") return SystemLanguage.English;
            if (language == "zh")
            {
                for (int i = 1; i < parts.Length; i++)
                {
                    var part = parts[i].ToUpperInvariant();
                    if (part == "HANT" || part == "TW" || part == "HK" || part == "MO") return SystemLanguage.ChineseTraditional;
                }
                return SystemLanguage.ChineseSimplified;
            }
            switch (language)
            {
                case "af": return SystemLanguage.Afrikaans;
                case "ar": return SystemLanguage.Arabic;
                case "eu": return SystemLanguage.Basque;
                case "be": return SystemLanguage.Belarusian;
                case "bg": return SystemLanguage.Bulgarian;
                case "ca": return SystemLanguage.Catalan;
                case "cs": return SystemLanguage.Czech;
                case "da": return SystemLanguage.Danish;
                case "nl": return SystemLanguage.Dutch;
                case "en": return SystemLanguage.English;
                case "et": return SystemLanguage.Estonian;
                case "fo": return SystemLanguage.Faroese;
                case "fi": return SystemLanguage.Finnish;
                case "fr": return SystemLanguage.French;
                case "de": return SystemLanguage.German;
                case "el": return SystemLanguage.Greek;
                case "he": case "iw": return SystemLanguage.Hebrew;
                case "hu": return SystemLanguage.Hungarian;
                case "is": return SystemLanguage.Icelandic;
                case "id": case "in": return SystemLanguage.Indonesian;
                case "it": return SystemLanguage.Italian;
                case "ja": return SystemLanguage.Japanese;
                case "ko": return SystemLanguage.Korean;
                case "lv": return SystemLanguage.Latvian;
                case "lt": return SystemLanguage.Lithuanian;
                case "no": case "nb": case "nn": return SystemLanguage.Norwegian;
                case "pl": return SystemLanguage.Polish;
                case "pt": return SystemLanguage.Portuguese;
                case "ro": return SystemLanguage.Romanian;
                case "ru": return SystemLanguage.Russian;
                case "sh": case "sr": case "hr": case "bs": return SystemLanguage.SerboCroatian;
                case "sk": return SystemLanguage.Slovak;
                case "sl": return SystemLanguage.Slovenian;
                case "es": return SystemLanguage.Spanish;
                case "sv": return SystemLanguage.Swedish;
                case "th": return SystemLanguage.Thai;
                case "tr": return SystemLanguage.Turkish;
                case "uk": return SystemLanguage.Ukrainian;
                case "vi": return SystemLanguage.Vietnamese;
                case "hi": return SystemLanguage.Hindi;
                default: return SystemLanguage.Unknown;
            }
        }
    }
}
