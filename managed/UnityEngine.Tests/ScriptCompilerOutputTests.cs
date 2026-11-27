using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor.Scripting.Compilers;
using Xunit;

namespace UnityEngine.Tests
{
    public class ScriptCompilerOutputTests
    {
        /// <summary>A parser of C# compiler lines, as Burst's and Unity's own parsers derive one.</summary>
        sealed class CSharpParser : CompilerOutputParserBase
        {
            static readonly Regex s_Line = new Regex(@"(?<filename>[^:(]+)\((?<line>\d+),(?<column>\d+)\):\s*(?<type>warning|error|info)\s*(?<id>[^:]*):\s*(?<message>.*)", RegexOptions.ExplicitCapture);
            protected override string GetErrorIdentifier() => "error";
            protected override Regex GetOutputRegex() => s_Line;
        }

        [Fact]
        public void LinesBecomeMessagesOfTheirKind()
        {
            var messages = new CSharpParser().Parse(new[]
            {
                "Assets/A.cs(3,5): error CS1002: ; expected",
                "Assets/B.cs(10,1): warning CS0168: unused",
                "Assets/C.cs(2,7): info RS001: note",
                "not a compiler line",
            }, false).ToArray();
            Assert.Equal(3, messages.Length);
            Assert.Equal(("Assets/A.cs", 3, 5, CompilerMessageType.Error), (messages[0].file, messages[0].line, messages[0].column, messages[0].type));
            Assert.Equal("Assets/A.cs(3,5): error CS1002: ; expected", messages[0].message);
            Assert.Equal(CompilerMessageType.Warning, messages[1].type);
            Assert.Equal(CompilerMessageType.Information, messages[2].type);
        }

        [Fact]
        public void AFailureWithoutAnErrorMessageGetsOneOfTheWholeOutput()
        {
            var output = new[] { "Assets/B.cs(10,1): warning CS0168: unused", "the compiler crashed" };
            var messages = new CSharpParser().Parse(output, true).ToArray();
            Assert.Equal(2, messages.Length);
            Assert.Equal(CompilerMessageType.Error, messages[1].type);
            Assert.Equal("", messages[1].file);
            Assert.Equal(string.Join(System.Environment.NewLine, output), messages[1].message);
            // With an error message there is no extra one.
            Assert.Single(new CSharpParser().Parse(new[] { "Assets/A.cs(3,5): error CS1002: x" }, true));
        }

        [Fact]
        public void ALongLineIsMatchedByItsStartAndKeptWhole()
        {
            string line = "Assets/A.cs(3,5): error CS1002: " + new string('x', 1200);
            var message = new CSharpParser().Parse(new[] { line }, false).Single();
            Assert.Equal(line, message.message);
            Assert.Equal(CompilerMessageType.Error, message.type);
        }
    }
}
