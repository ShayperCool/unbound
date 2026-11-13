using System.Globalization;
using System.Runtime.CompilerServices;

namespace UnityEngine.Tests
{
    static class TestCulture
    {
        /// <summary>Tests compare text that code formats with the current culture, so every
        /// test thread starts in en-US whatever the host's locale. On macOS .NET otherwise takes
        /// the system region (en-RU formats 0.5 as "0,5") when LANG names no language.</summary>
        [ModuleInitializer]
        internal static void Pin()
        {
            var culture = CultureInfo.GetCultureInfo("en-US");
            CultureInfo.DefaultThreadCurrentCulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;
        }
    }
}
