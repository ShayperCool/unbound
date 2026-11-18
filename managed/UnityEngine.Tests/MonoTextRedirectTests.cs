using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Unbound.Host;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Game code's texts of floats, tuples of floats and exceptions as the Unity 6000.0.71f1
    /// Editor's Mono writes them (tests/projects/apierrors <c>format.number.*</c>): in code redirected
    /// when its assembly loads, under a culture with a decimal comma, and in hot-reload edits.
    /// </summary>
    public sealed class MonoTextRedirectTests
    {
        const string Source = @"
using System;
public static class Game
{
    public static string Float(float value) => value.ToString();
    public static string Message(Exception exception) => exception.Message;
    public static string Tuple(float a, double b) => (a, b).ToString();
    public static string Concat(float a, double b) => ""t="" + (a, b);
    public static string Format(float a, double b) => string.Format(""{0}"", (a, b));
    public static string Actual(object value) => new ArgumentOutOfRangeException(""p"", value, ""Too big."").ActualValue.ToString();
    public static string Nullable(float a) { (float, float)? t = (a, 2f); return t.ToString(); }
}";

        sealed class OwnTuple : System.Runtime.CompilerServices.ITuple
        {
            public int Length => 1;
            public object this[int index] => 1f / 3f;
            public override string ToString() => "own";
        }

        static Assembly Redirected(string source) => WideFloatsTests.Load(NumberFormatShims.Redirect(
            WideFloatsTests.Emit(WideFloatsTests.Compilation(source, OptimizationLevel.Release, "MonoTextGame")).Image));

        static string Call(Assembly game, string method, params object[] args) =>
            (string)game.GetType("Game")!.GetMethod(method)!.Invoke(null, args)!;

        static void WithCulture(string name, Action action)
        {
            var culture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = new CultureInfo(name);
                action();
            }
            finally { CultureInfo.CurrentCulture = culture; }
        }

        [Fact]
        public void ATuplesItemsAreWrittenAsMonoWritesThem()
        {
            var game = Redirected(Source);
            Assert.Equal("(0.3333333, 0.1)", Call(game, "Tuple", 1f / 3f, 0.1));
            Assert.Equal("t=(0.3333333, 0.1)", Call(game, "Concat", 1f / 3f, 0.1));
            Assert.Equal("(0.3333333, 0.333333333333333)", Call(game, "Format", 1f / 3f, 1.0 / 3.0));
            Assert.Equal("(0.3333333, 2.5)", Call(game, "Actual", (1f / 3f, 2.5f)));
            // Nested tuples and a reference Tuple; an item without a value is empty.
            Assert.Equal("(1, (0.3333333, ), 2.5)", Unbound.MonoNumberFormat.ObjectToString((1, (1f / 3f, (string)null), 2.5f)));
            Assert.Equal("(0.3333333, x)", Unbound.MonoNumberFormat.ObjectToString(System.Tuple.Create(1f / 3f, "x")));
            Assert.Equal("Too big.\nParameter name: p\nActual value was (0.3333333, 2.5).",
                MonoExceptions.MessageOf(new ArgumentOutOfRangeException("p", (1f / 3f, 2.5f), "Too big.")));
            // Control: a tuple without floats reads as .NET writes it, and a game's own ITuple keeps its text.
            Assert.Equal((1, "x").ToString(), Unbound.MonoNumberFormat.ObjectToString((1, "x")));
            Assert.Equal("own", Unbound.MonoNumberFormat.ObjectToString(new OwnTuple()));
            // The argument array overloads, a generic join and a nullable tuple.
            Assert.Equal("1 2 3 (0.3333333, 2)", Unbound.MonoNumberFormat.Format("{0} {1} {2} {3}", new object[] { 1, 2, 3, (1f / 3f, 2f) }));
            Assert.Equal("(0.3333333, 2), (1, 2)", Unbound.MonoNumberFormat.Join(", ", new System.Collections.Generic.List<(float, float)> { (1f / 3f, 2f), (1f, 2f) }));
            Assert.Equal("(0.3333333, 2)", Call(game, "Nullable", 1f / 3f));
        }

        [Fact]
        public void ACommaCultureWritesTheValuesWithItsComma()
        {
            var game = Redirected(Source);
            WithCulture("de-DE", () =>
            {
                Assert.Equal("0,3333333", Call(game, "Float", 1f / 3f));
                Assert.Equal("(0,3333333, 2,5)", Call(game, "Tuple", 1f / 3f, 2.5));
                Assert.Equal("Too big.\nParameter name: p\nActual value was 2,5.",
                    MonoExceptions.MessageOf(new ArgumentOutOfRangeException("p", 2.5f, "Too big.")));
                Assert.Equal("Too big.\nParameter name: p\nActual value was (0,3333333, 2,5).",
                    Call(game, "Message", new ArgumentOutOfRangeException("p", (1f / 3f, 2.5f), "Too big.")));
            });
        }

        sealed class LogTexts : ILogHandler
        {
            internal readonly System.Collections.Generic.List<string> Texts = new();
            public void LogFormat(LogType type, Object context, string format, params object[] args) => Texts.Add(string.Format(format, args));
            public void LogException(Exception error, Object context) { }
        }

        /// <summary>Unity's logger writes a value that is not IFormattable by its ToString(), so a
        /// tuple of floats reads as Mono writes it, in the current culture (tests/projects/apierrors
        /// <c>format.number.tuple_to_string</c>), while a float itself is written invariantly.</summary>
        [Fact]
        public void TheLoggerWritesATupleAsItsMonoText()
        {
            var texts = new LogTexts();
            var logger = new Logger(texts);
            logger.Log((1f / 3f, 2.5f));
            logger.Log(new ArgumentOutOfRangeException("p", (1f / 3f, 2.5f), "Too big.").ActualValue);
            WithCulture("de-DE", () =>
            {
                logger.Log((1f / 3f, 2.5f));
                logger.Log(1f / 3f);
            });
            // Control: a tuple without floats and a game's own ITuple keep their own text.
            logger.Log((1, "x"));
            logger.Log(new OwnTuple());
            Assert.Equal(new[] { "(0.3333333, 2.5)", "(0.3333333, 2.5)", "(0,3333333, 2,5)", "0.3333333", "(1, x)", "own" }, texts.Texts);
        }

        /// <summary>A hot-reload edit's calls that format a float or read a message go through the
        /// shims the loaded image references, as the loaded code's do (an image that formats
        /// something references every shim; one that formats nothing is loaded as it is, and an edit
        /// of it keeps the class library's calls).</summary>
        [Fact]
        public void AHotReloadEditsTextsGoThroughTheLoadedShims()
        {
            const string edited = @"
using System;
public static class Game
{
    public static string Float(float value) => ""a"";
    public static string Message(Exception exception) => ""b"";
    public static string Other(double value) => value.ToString();
}";
            var original = WideFloatsTests.Compilation(edited, OptimizationLevel.Debug, "EditedText");
            var (image, pdb) = WideFloatsTests.Emit(original);
            var loaded = NumberFormatShims.Redirect(image);
            var session = new Unbound.Compiler.EditAndContinue();
            session.Begin("EditedText", original, loaded, pdb);
            var current = WideFloatsTests.Compilation(edited
                .Replace(@"Float(float value) => ""a""", "Float(float value) => value.ToString()")
                .Replace(@"Message(Exception exception) => ""b""", "Message(Exception exception) => exception.Message"),
                OptimizationLevel.Debug, "EditedText");
            var delta = session.Emit("EditedText", current);
            Assert.NotNull(delta);
            var redirected = NumberFormatShims.RedirectDelta(loaded, new[] { delta!.Metadata }, delta.IL);
            Assert.NotEqual(delta.IL, redirected);
            using var module = Mono.Cecil.ModuleDefinition.ReadModule(new MemoryStream(loaded));
            int Shim(string name, string parameter) => module.GetMemberReferences().Single(m =>
                m.DeclaringType.FullName == typeof(Unbound.MonoNumberFormat).FullName && m.Name == name
                && m is Mono.Cecil.MethodReference method && method.Parameters.Count == 1 && method.Parameters[0].ParameterType.FullName == parameter)
                .MetadataToken.ToInt32();
            bool Calls(byte[] il, int token) => Enumerable.Range(0, il.Length - 4)
                .Any(at => il[at] == 0x28 && il.AsSpan(at + 1, 4).SequenceEqual(BitConverter.GetBytes(token)));
            int floatText = Shim("ToString", "System.Single&"), message = Shim("get_Message", "System.Exception");
            Assert.True(Calls(redirected, floatText));
            Assert.True(Calls(redirected, message));
            // Control: the edit as emitted calls the class library.
            Assert.False(Calls(delta.IL, floatText));
            Assert.False(Calls(delta.IL, message));
        }
    }
}
