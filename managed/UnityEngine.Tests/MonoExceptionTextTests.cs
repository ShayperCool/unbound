using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Unbound.Host;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Exception texts as the Unity 6000.0.71f1 Editor's Mono runtime writes them
    /// (tests/projects/apierrors <c>format.*</c>): an argument exception's parameter name on its own
    /// line, for game code's reads of Message and ToString() (redirected when its assembly loads)
    /// and for the log, while the exception stays the class library's with its parameter name.
    /// </summary>
    public sealed class MonoExceptionTextTests : IDisposable
    {
        const string Source = @"
using System;
public sealed class Suffixed : ArgumentException
{
    public Suffixed() : base(""Bad."", ""p"") { }
    public override string Message => base.Message + ""!"";
}
public static class Probe
{
    public static string Message(Exception e) => e.Message;
    public static string NullMessage(ArgumentNullException e) => e.Message;
    public static string RangeMessage(ArgumentOutOfRangeException e) => e.Message;
    public static string Text(Exception e) => e.ToString();
    public static string Concat(Exception e) => ""caught "" + e;
    public static string Format(Exception e) => string.Format(""caught {0}"", e);
    public static string Builder(Exception e) => new System.Text.StringBuilder().Append(e).ToString();
    public static string Converted(Exception e) => Convert.ToString(e);
    public static Func<string> Delegate(Exception e) => () => e.Message;
}";

        readonly List<string> m_Logs = new List<string>();

        public MonoExceptionTextTests() => Application.logMessageReceived += Record;

        public void Dispose() => Application.logMessageReceived -= Record;

        void Record(string message, string stack, LogType type) => m_Logs.Add(type + ": " + message);

        static Exception Thrown(Action action)
        {
            try { action(); }
            catch (Exception e) { return e; }
            throw new InvalidOperationException("nothing thrown");
        }

        [Fact]
        public void TheParameterNameGoesOnItsOwnLineAfterTheMessage()
        {
            Assert.Equal("Value cannot be null.\nParameter name: p", MonoExceptions.MessageOf(new ArgumentNullException("p")));
            Assert.Equal("Must be set.\nParameter name: p", MonoExceptions.MessageOf(new ArgumentNullException("p", "Must be set.")));
            Assert.Equal("Bad thing.\nParameter name: p", MonoExceptions.MessageOf(new ArgumentException("Bad thing.", "p")));
            Assert.Equal("Specified argument was out of the range of valid values.\nParameter name: p",
                MonoExceptions.MessageOf(new ArgumentOutOfRangeException("p")));
            Assert.Equal("Too big.\nParameter name: p\nActual value was 8.", MonoExceptions.MessageOf(new ArgumentOutOfRangeException("p", 8, "Too big.")));
            // A float or double actual value prints in Mono's number format (tests/projects/apierrors format.game_range_actual_third*).
            Assert.Equal("Too big.\nParameter name: p\nActual value was 0.3333333.", MonoExceptions.MessageOf(new ArgumentOutOfRangeException("p", 1f / 3f, "Too big.")));
            Assert.Equal("Too big.\nParameter name: p\nActual value was 0.333333333333333.", MonoExceptions.MessageOf(new ArgumentOutOfRangeException("p", 1.0 / 3.0, "Too big.")));
            Assert.Equal("Too big.\nActual value was 0.3333333.", MonoExceptions.MessageOf(new ArgumentOutOfRangeException(null, 1f / 3f, "Too big.")));
            // Unity passes whole sentences as parameter names.
            Assert.Equal("stream\nParameter name: ManagedStream object must be non-null",
                MonoExceptions.MessageOf(new ArgumentNullException("ManagedStream object must be non-null", "stream")));
            // Without a name, and for other exceptions, the message is the same on both runtimes.
            Assert.Equal("Bad thing.", MonoExceptions.MessageOf(new ArgumentException("Bad thing.")));
            Assert.Equal("Bad thing.", MonoExceptions.MessageOf(new ArgumentException("Bad thing.", "")));
            Assert.Equal("Too big.\nActual value was 8.", MonoExceptions.MessageOf(new ArgumentOutOfRangeException(null, 8, "Too big.")));
            Assert.Equal("Cannot access a disposed object.\nObject name: 'Thing'.", MonoExceptions.MessageOf(new ObjectDisposedException("Thing")));
            Assert.Equal("Outer.", MonoExceptions.MessageOf(new InvalidOperationException("Outer.", new ArgumentNullException("p"))));
        }

        [Fact]
        public void ToStringWritesEveryMessageInTheChainAndKeepsTheStack()
        {
            Assert.Equal("System.ArgumentNullException: Value cannot be null.\nParameter name: p", MonoExceptions.ToStringOf(new ArgumentNullException("p")));
            // An inner exception joins on the message's line (tests/projects/apierrors format.game_inner.string).
            var outer = new InvalidOperationException("Outer.", new ArgumentOutOfRangeException("q", 8, "Too big."));
            Assert.Equal("System.InvalidOperationException: Outer. ---> System.ArgumentOutOfRangeException: Too big.\nParameter name: q\nActual value was 8.\n" +
                "   --- End of inner exception stack trace ---", MonoExceptions.ToStringOf(outer));
            Assert.Equal("System.Exception: Exception of type 'System.Exception' was thrown. ---> System.ArgumentNullException: Value cannot be null.\nParameter name: p\n" +
                "   --- End of inner exception stack trace ---", MonoExceptions.ToStringOf(new Exception(null, new ArgumentNullException("p"))));
            // An exception that writes more after its message (a file name) keeps the runtime's layout
            // there (unmeasured); the inner message is still Mono's.
            var file = new System.IO.FileNotFoundException("Missing.", "a.txt", new ArgumentNullException("p"));
            Assert.Equal(file.ToString().Replace("Value cannot be null. (Parameter 'p')", "Value cannot be null.\nParameter name: p", StringComparison.Ordinal),
                MonoExceptions.ToStringOf(file));
            Assert.Contains("\n ---> System.ArgumentNullException", file.ToString());
            var thrown = Thrown(() => throw new ArgumentException("Bad thing.", "p"));
            string text = MonoExceptions.ToStringOf(thrown);
            Assert.StartsWith("System.ArgumentException: Bad thing.\nParameter name: p\n", text);
            Assert.Contains(nameof(ToStringWritesEveryMessageInTheChainAndKeepsTheStack), text);
            // The class name holds the message's first word: the message is found after it.
            Assert.Equal("System.ArgumentException: Argument\nParameter name: p", MonoExceptions.ToStringOf(new ArgumentException("Argument", "p")));
        }

        static Assembly Redirected(OptimizationLevel optimization)
        {
            var compilation = WideFloatsTests.Compilation(Source, optimization, "MonoExceptionProbe");
            return WideFloatsTests.Load(NumberFormatShims.Redirect(WideFloatsTests.Emit(compilation).Image));
        }

        static object Run(Assembly assembly, string method, object argument)
        {
            try { return assembly.GetType("Probe")!.GetMethod(method)!.Invoke(null, new[] { argument })!; }
            catch (TargetInvocationException e) { throw e.InnerException!; }
        }

        [Theory]
        [MemberData(nameof(WideFloatsTests.Modes), MemberType = typeof(WideFloatsTests))]
        public void GameCodeReadsMonosTextsOfTheClassLibrarysExceptions(OptimizationLevel optimization)
        {
            var probe = Redirected(optimization);
            var exception = new ArgumentNullException("p");
            const string Mono = "Value cannot be null.\nParameter name: p";
            Assert.Equal(Mono, Run(probe, "Message", exception));
            Assert.Equal(Mono, Run(probe, "NullMessage", exception));
            Assert.Equal("Too big.\nParameter name: p\nActual value was 8.", Run(probe, "RangeMessage", new ArgumentOutOfRangeException("p", 8, "Too big.")));
            Assert.Equal("System.ArgumentNullException: " + Mono, Run(probe, "Text", exception));
            // (Unity's class library has no interpolation handler: game code's interpolations are string.Format.)
            foreach (var method in new[] { "Concat", "Format" })
                Assert.Equal(method + ": caught System.ArgumentNullException: " + Mono, method + ": " + Run(probe, method, exception));
            Assert.Equal("System.ArgumentNullException: " + Mono, Run(probe, "Builder", exception));
            Assert.Equal("System.ArgumentNullException: " + Mono, Run(probe, "Converted", exception));
            Assert.Equal(Mono, ((Func<string>)Run(probe, "Delegate", exception))());
            // The exception itself is untouched: its class, name and the runtime's own message.
            Assert.IsType<ArgumentNullException>(exception);
            Assert.Equal("p", exception.ParamName);
            Assert.Equal("Value cannot be null. (Parameter 'p')", exception.Message);
            // Other exceptions read as they are.
            Assert.Equal("Outer.", Run(probe, "Message", new InvalidOperationException("Outer.")));
        }

        [Theory]
        [MemberData(nameof(WideFloatsTests.Modes), MemberType = typeof(WideFloatsTests))]
        public void AnOverridesBaseMessageStaysTheRuntimesAndItsCallersReadMonos(OptimizationLevel optimization)
        {
            var probe = Redirected(optimization);
            var suffixed = (Exception)Activator.CreateInstance(probe.GetType("Suffixed")!)!;
            // base.Message is not redirected (no recursion); the caller's read writes the name as Mono does.
            Assert.Equal("Bad. (Parameter 'p')!", suffixed.Message);
            Assert.Equal("Bad.\nParameter name: p!", Run(probe, "Message", suffixed));
        }

        [Fact]
        public void TheLogWritesMonosTexts()
        {
            Debug.LogException(new ArgumentOutOfRangeException("p", 8, "Too big."));
            Debug.LogException(new InvalidOperationException("Outer.", new ArgumentNullException("p")));
            Debug.Log(new ArgumentNullException("p"));
            Debug.LogException(new ArgumentException("Bad thing."));
            Assert.Equal(new[]
            {
                "Exception: ArgumentOutOfRangeException: Too big.\nParameter name: p\nActual value was 8.",
                "Exception: ArgumentNullException: Value cannot be null.\nParameter name: p",
                "Log: System.ArgumentNullException: Value cannot be null.\nParameter name: p",
                "Exception: ArgumentException: Bad thing.",
            }, m_Logs);
        }
    }
}
