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
    }
}
