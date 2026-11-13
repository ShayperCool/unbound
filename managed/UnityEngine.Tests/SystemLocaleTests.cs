using System;
using System.Globalization;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    public class SystemLocaleTests
    {
        [Theory]
        [InlineData("ja_JP.UTF-8", SystemLanguage.Japanese)]
        [InlineData("zh_CN.UTF-8", SystemLanguage.ChineseSimplified)]
        [InlineData("zh_TW.UTF-8", SystemLanguage.ChineseTraditional)]
        [InlineData("zh-Hant-HK", SystemLanguage.ChineseTraditional)]
        [InlineData("pt-BR", SystemLanguage.Portuguese)]
        [InlineData("sr_RS@latin", SystemLanguage.SerboCroatian)]
        [InlineData("nb_NO", SystemLanguage.Norwegian)]
        [InlineData("C.UTF-8", SystemLanguage.English)]
        [InlineData("tlh", SystemLanguage.Unknown)]
        [InlineData("", SystemLanguage.Unknown)]
        public void LocaleNamesMapToLanguages(string locale, SystemLanguage language) =>
            Assert.Equal(language, SystemLocale.LanguageOf(locale));

        [Fact]
        public void CulturesKeepFormattingButCompareLikeMono()
        {
            var thai = SystemLocale.WithInvariantCollation(new System.Globalization.CultureInfo("th-TH"));
            Assert.Equal("th-TH", thai.Name);
            Assert.Equal(-1, thai.CompareInfo.LastIndexOf("Hit", "/"));
            Assert.Equal(9, thai.CompareInfo.LastIndexOf("Particles/Hit", "/"));
            var turkish = SystemLocale.WithInvariantCollation(new System.Globalization.CultureInfo("tr-TR"));
            Assert.Equal("\u0130", "i".ToUpper(turkish));
            var german = SystemLocale.WithInvariantCollation(new System.Globalization.CultureInfo("de-DE"));
            Assert.Equal("1,5", 1.5.ToString(german));
        }

        // The macOS Editor's game culture is en-US whatever the system's settings; elsewhere the
        // OS culture is kept.
        [Fact]
        public void TheGameCultureIsTheEditorsOfThisOs()
        {
            var culture = CultureInfo.CurrentCulture;
            var ui = CultureInfo.CurrentUICulture;
            var defaultCulture = CultureInfo.DefaultThreadCurrentCulture;
            var defaultUi = CultureInfo.DefaultThreadCurrentUICulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo("de-DE");
                CultureInfo.CurrentUICulture = new CultureInfo("de-DE");
                SystemLocale.InstallCulture();
                bool mac = OperatingSystem.IsMacOS();
                Assert.Equal(mac ? "en-US" : "de-DE", CultureInfo.CurrentCulture.Name);
                Assert.Equal(mac ? "en-US" : "de-DE", CultureInfo.CurrentUICulture.Name);
                Assert.Equal(mac ? "0.5" : "0,5", 0.5.ToString());
                Assert.Equal(-1, CultureInfo.CurrentCulture.CompareInfo.LastIndexOf("Hit", "/"));
            }
            finally
            {
                CultureInfo.DefaultThreadCurrentCulture = defaultCulture;
                CultureInfo.DefaultThreadCurrentUICulture = defaultUi;
                CultureInfo.CurrentCulture = culture;
                CultureInfo.CurrentUICulture = ui;
            }
        }

        // The macOS Editor takes the first preferred language and ignores the POSIX variables;
        // elsewhere they decide.
        [Fact]
        public void TheLocaleNameComesFromTheOsSetting()
        {
            string lang = Environment.GetEnvironmentVariable("LANG");
            string all = Environment.GetEnvironmentVariable("LC_ALL");
            string messages = Environment.GetEnvironmentVariable("LC_MESSAGES");
            try
            {
                Environment.SetEnvironmentVariable("LC_ALL", null);
                Environment.SetEnvironmentVariable("LC_MESSAGES", null);
                Environment.SetEnvironmentVariable("LANG", "tlh_XX.UTF-8");
                if (!OperatingSystem.IsMacOS())
                {
                    Assert.Equal("tlh_XX.UTF-8", SystemLocale.Name());
                    return;
                }
                string name = SystemLocale.Name();
                Assert.NotEqual("tlh_XX.UTF-8", name);
                string preferred = FirstPreferredLanguage();
                if (preferred != null) Assert.Equal(preferred, name);
            }
            finally
            {
                Environment.SetEnvironmentVariable("LANG", lang);
                Environment.SetEnvironmentVariable("LC_ALL", all);
                Environment.SetEnvironmentVariable("LC_MESSAGES", messages);
            }
        }

        /// <summary>The user's first preferred language as the defaults tool prints the list
        /// (<c>(\n    "en-US",\n    de\n)</c>), or null when the user has set none.</summary>
        static string FirstPreferredLanguage()
        {
            var start = new System.Diagnostics.ProcessStartInfo("/usr/bin/defaults", "read -g AppleLanguages")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            using var process = System.Diagnostics.Process.Start(start);
            string output = process.StandardOutput.ReadToEnd();
            process.StandardError.ReadToEnd();
            process.WaitForExit();
            if (process.ExitCode != 0) return null;
            foreach (string entry in output.Split(new[] { '(', ')', ',', '\n' }, StringSplitOptions.RemoveEmptyEntries))
            {
                string language = entry.Trim().Trim('"');
                if (language.Length > 0) return language;
            }
            return null;
        }
    }
}
