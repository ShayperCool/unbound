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
    }
}
