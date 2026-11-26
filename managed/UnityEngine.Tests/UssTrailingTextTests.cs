using System;
using System.Collections.Generic;
using Unbound.UIElements;
using UnityEngine.UIElements;
using Xunit;

// Style sheets ending in text no rule takes (tests/projects/ussvalidation Trailing*, measured in
// Unity 6000.0.71f1: nothing logged, the rules before kept, a block the end closes kept with its
// declarations, a declaration without ';' included).
namespace UnityEngine.Tests
{
    public class UssTrailingTextTests
    {
        sealed class LogCapture : ILogHandler
        {
            public readonly List<string> Messages = new List<string>();
            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                Messages.Add(logType + ": " + (args == null || args.Length == 0 ? format : string.Format(format, args)));
            public void LogException(Exception exception, Object context) => Messages.Add("Exception: " + exception.Message);
        }

        [Theory]
        [InlineData(".a { width: 31px; }\n\n›", 1, 0)]
        [InlineData(".a { width: 32px; }\nstray", 1, 0)]
        [InlineData(".a { width: 33px; }\n.b { width: 1px;", 2, 1)]
        [InlineData(".a { width: 34px; }\n.b { width: 2px", 2, 1)]
        [InlineData(".a { width: 35px; }\n.b {", 2, 0)]
        public void TrailingTextMakesNoErrorAndKeepsTheRules(string text, int rules, int lastRuleProperties)
        {
            var sheet = ScriptableObject.CreateInstance<StyleSheet>();
            var capture = new LogCapture();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = capture;
            try
            {
                new UssImporter("trailing.uss").Import(sheet, text);
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
            }
            Assert.Empty(capture.Messages);
            Assert.Equal(rules, sheet.rules.Length);
            Assert.Equal("width", sheet.rules[0].properties[0].name);
            if (rules > 1) Assert.Equal(lastRuleProperties, sheet.rules[1].properties.Length);
            Object.DestroyImmediate(sheet);
        }
    }
}
