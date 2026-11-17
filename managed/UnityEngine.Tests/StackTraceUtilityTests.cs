using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using Unbound.Runtime;
using UnityEngine;
using Xunit;

namespace Unbound.Tests
{
    public class StackTraceRoutine : MonoBehaviour
    {
        internal string Stack;
        internal string Text;

        internal IEnumerator Extracts()
        {
            Stack = StackTraceUtility.ExtractStackTrace();
            Text = StackTraceUtility.ExtractStringFromException(new InvalidOperationException("Never thrown."));
            yield break;
        }
    }

    /// <summary>Starts coroutines, so it shares a collection with the other tests that do.</summary>
    [Collection("Frames")]
    public sealed class StackTraceUtilityTests
    {
        const string ThisFile = "StackTraceUtilityTests.cs";

        static readonly Regex MonoFrame = new Regex(@"^  at (?<method>[^\[]+) \[0x(?<offset>[0-9a-f]{5})\] in (?<file>.+):(?<line>\d+) $");

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void Throw(string message) => throw new InvalidOperationException(message);

        [MethodImpl(MethodImplOptions.NoInlining)]
        static void ThrowWrapped()
        {
            try { Throw("Inner."); }
            catch (Exception e) { throw new ArgumentException("Outer.", "p", e); }
        }

        static Exception Caught(Action action)
        {
            try { action(); }
            catch (Exception e) { return e; }
            throw new InvalidOperationException("nothing thrown");
        }

        [Fact]
        public void NullAndOtherObjectsThrowUnitysArgumentTexts()
        {
            var none = Assert.Throws<ArgumentException>(() => StackTraceUtility.ExtractStringFromException(null));
            Assert.Equal("ExtractStringFromExceptionInternal called with null exception", none.Message);
            var other = Assert.Throws<ArgumentException>(() => StackTraceUtility.ExtractStringFromException("text"));
            Assert.Equal("ExtractStringFromExceptionInternal called with an exception that was not of type System.Exception", other.Message);
        }

        [Fact]
        public void AnExceptionNeverThrownHasAnEmptyStackThenTheCallers()
        {
            string text = StackTraceUtility.ExtractStringFromException(new InvalidOperationException("Never thrown."));
            var lines = text.Split('\n');
            Assert.Equal("InvalidOperationException: Never thrown.", lines[0]);
            Assert.Equal("", lines[1]);
            Assert.Equal("UnityEngine.StackTraceUtility:ExtractStringFromException(Object)", lines[2]);
            Assert.StartsWith("Unbound.Tests.StackTraceUtilityTests:AnExceptionNeverThrownHasAnEmptyStackThenTheCallers() (at ", lines[3]);
            Assert.Contains(ThisFile + ":", lines[3]);
        }

        [Fact]
        public void TheInnermostMessageComesFirstAndEachWrapperAfterRethrowAs()
        {
            string text = StackTraceUtility.ExtractStringFromException(Caught(ThrowWrapped));
            var lines = text.Split('\n');
            Assert.Equal("InvalidOperationException: Inner.", lines[0]);
            int rethrow = Array.IndexOf(lines, "Rethrow as ArgumentException: Outer.\nParameter name: p".Split('\n')[0]);
            Assert.True(rethrow > 1, text);
            Assert.Equal("Parameter name: p", lines[rethrow + 1]);
            // The inner exception's own frames: the throwing method, then the wrapper that caught it.
            Assert.Matches(MonoFrame, lines[1]);
            Assert.StartsWith("  at Unbound.Tests.StackTraceUtilityTests.Throw (System.String message) [0x", lines[1]);
            Assert.StartsWith("  at Unbound.Tests.StackTraceUtilityTests.ThrowWrapped () [0x", lines[2]);
            Assert.StartsWith("  at Unbound.Tests.StackTraceUtilityTests.ThrowWrapped () [0x", lines[rethrow + 2]);
            // The caller's stack follows the last exception frame.
            int callers = Array.IndexOf(lines, "UnityEngine.StackTraceUtility:ExtractStringFromException(Object)");
            Assert.True(callers > rethrow, text);
            Assert.StartsWith("  at Unbound.Tests.StackTraceUtilityTests.Caught (System.Action action) [0x", lines[callers - 1]);
        }

        [Fact]
        public void ABlankMessageLeavesTheTypeAlone()
        {
            string text = StackTraceUtility.ExtractStringFromException(Caught(() => throw new Exception(" ")));
            Assert.Equal("Exception", text.Split('\n')[0]);
        }

        [Fact]
        public void ArgumentMessagesUseMonosLayout()
        {
            string text = StackTraceUtility.ExtractStringFromException(Caught(() => throw new ArgumentOutOfRangeException("p", 8, "Too big.")));
            Assert.StartsWith("ArgumentOutOfRangeException: Too big.\nParameter name: p\nActual value was 8.\n  at ", text);
        }

        /// <summary>Each exception frame names where its statement starts, with that statement's
        /// line, as the .pdb says.</summary>
        [Fact]
        public void ExceptionFramesNameTheirStatementsStart()
        {
            var exception = Caught(() => Throw(string.Concat("A", "B")));
            string text = UnityStackTrace.MonoTrace(exception);
            var path = typeof(StackTraceUtilityTests).Assembly.Location;
            using var image = new PEReader(File.OpenRead(path));
            Assert.True(image.TryOpenAssociatedPortablePdb(path, p => File.Exists(p) ? File.OpenRead(p) : null, out var provider, out _));
            using (provider)
            {
                var pdb = provider.GetMetadataReader();
                var frames = new StackTrace(exception, true);
                var lines = text.Split('\n');
                Assert.Equal(frames.FrameCount, lines.Length);
                for (int i = 0; i < lines.Length; i++)
                {
                    var match = MonoFrame.Match(lines[i]);
                    Assert.True(match.Success, lines[i]);
                    var method = frames.GetFrame(i).GetMethod();
                    var handle = MetadataTokens.MethodDefinitionHandle(method.MetadataToken & 0xFFFFFF);
                    var points = pdb.GetMethodDebugInformation(handle).GetSequencePoints().Where(p => !p.IsHidden).ToList();
                    int offset = Convert.ToInt32(match.Groups["offset"].Value, 16);
                    var point = Assert.Single(points, p => p.Offset == offset);
                    Assert.Equal(point.StartLine, int.Parse(match.Groups["line"].Value));
                    Assert.EndsWith(ThisFile, match.Groups["file"].Value);
                }
            }
        }

        [Fact]
        public void TheExtractedStackListsTheUtilityFrameThenTheCaller()
        {
            var lines = StackTraceUtility.ExtractStackTrace().Split('\n');
            Assert.Equal("UnityEngine.Debug:ExtractStackTraceNoAlloc (byte*,int,string)", lines[0]);
            Assert.Equal("UnityEngine.StackTraceUtility:ExtractStackTrace ()", lines[1]);
            Assert.StartsWith("Unbound.Tests.StackTraceUtilityTests:TheExtractedStackListsTheUtilityFrameThenTheCaller () (at ", lines[2]);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        [HideInCallstack]
        static string Hidden() => StackTraceUtility.ExtractFormattedStackTrace(new StackTrace(0, true));

        [MethodImpl(MethodImplOptions.NoInlining)]
        static string Shown() => StackTraceUtility.ExtractFormattedStackTrace(new StackTrace(0, true));

        [Fact]
        public void HideInCallstackLeavesOutTheSourceOnly()
        {
            Assert.StartsWith("Unbound.Tests.StackTraceUtilityTests:Hidden()\n", Hidden());
            Assert.StartsWith("Unbound.Tests.StackTraceUtilityTests:Shown() (at ", Shown());
        }

        [Fact]
        public void ACoroutineStepRunsThroughSetupCoroutine()
        {
            var routine = new GameObject("Routine").AddComponent<StackTraceRoutine>();
            routine.StartCoroutine(routine.Extracts());
            var stack = routine.Stack.Split('\n');
            Assert.Matches(@"^Unbound\.Tests\.StackTraceRoutine/<Extracts>d__\d+:MoveNext \(\) \(at .+:\d+\)$", stack[2]);
            Assert.Equal("UnityEngine.SetupCoroutine:InvokeMoveNext (System.Collections.IEnumerator,intptr)", stack[3]);
            Assert.StartsWith("Unbound.Tests.StackTraceUtilityTests:ACoroutineStepRunsThroughSetupCoroutine () (at ", stack[4]);
            var text = routine.Text.Split('\n');
            // A nested type's namespace is its outermost type's.
            Assert.Matches(@"^Unbound\.Tests\.<Extracts>d__\d+:MoveNext\(\) \(at /.+StackTraceUtilityTests\.cs:\d+\)$", text[3]);
            Assert.Equal("UnityEngine.SetupCoroutine:InvokeMoveNext(IEnumerator, IntPtr)", text[4]);
            Assert.StartsWith("Unbound.Tests.StackTraceUtilityTests:ACoroutineStepRunsThroughSetupCoroutine() (at ", text[5]);
            var missing = Assert.Throws<ArgumentException>(() => SetupCoroutine.InvokeMoveNext(routine.Extracts(), IntPtr.Zero));
            Assert.Equal("returnValueAddress", missing.ParamName);
            Assert.Equal("Return value address cannot be 0.\nParameter name: returnValueAddress", MonoExceptions.MessageOf(missing));
        }

        sealed class RemoteException : Exception
        {
            public RemoteException() : base("Remote.") { }
            public override string StackTrace => "  at Server.Handle () [0x00000] in <server>:0 ";
        }

        [Fact]
        public void AnExceptionThatWritesItsOwnStackKeepsIt()
        {
            var lines = StackTraceUtility.ExtractStringFromException(Caught(() => throw new RemoteException())).Split('\n');
            Assert.Equal("RemoteException: Remote.", lines[0]);
            Assert.Equal("  at Server.Handle () [0x00000] in <server>:0 ", lines[1]);
            Assert.Equal("UnityEngine.StackTraceUtility:ExtractStringFromException(Object)", lines[2]);
        }

        [Fact]
        public void SourcesAreNamedAsTheEditorCompiledThem()
        {
            Assert.Equal("/p/Assets/A.cs", UnityStackTrace.AbsoluteSourcePath("/p/Assets/A.cs", "/p"));
            Assert.Equal("./Library/PackageCache/com.unity.x@0123456789ab/C.cs",
                UnityStackTrace.AbsoluteSourcePath("/p/Library/PackageCache/com.unity.x@0123456789ab/C.cs", "/p"));
            Assert.Equal("/elsewhere/D.cs", UnityStackTrace.AbsoluteSourcePath("/elsewhere/D.cs", "/p"));
            Assert.Equal("/p/Assets/A.cs", UnityStackTrace.AbsoluteSourcePath("/p/Assets/A.cs", ""));
        }

        /// <summary>Caller stacks remove only a leading project folder from the compiled path.</summary>
        [Fact]
        public void CallerSourcesLoseOnlyALeadingProjectFolder()
        {
            const string cached = "/p/Library/PackageCache/com.unity.x@0123456789ab/C.cs";
            Assert.Equal("Assets/A.cs", UnityStackTrace.StackSourcePath("/p/Assets/A.cs", "/p/", "/p"));
            Assert.Equal("./Library/PackageCache/com.unity.x@0123456789ab/C.cs", UnityStackTrace.StackSourcePath(cached, "/p/", "/p"));
            Assert.Equal("/p/Assets/A.cs", UnityStackTrace.StackSourcePath("/p/Assets/A.cs", "", "/p"));
            Assert.Equal("/p/Assets/A.cs", UnityStackTrace.StackSourcePath("/p/Assets/A.cs", "Assets/", "/p"));
        }

        /// <summary>After an edit is applied, a frame keeps the runtime's offset and line: the
        /// loaded .pdb no longer describes the edited methods.</summary>
        [Fact]
        public void AnEditedAssemblysFramesKeepTheRuntimesPlaces()
        {
            var assembly = typeof(StackTraceUtilityTests).Assembly;
            var exception = Caught(() => Throw(string.Concat("A", "B")));
            var frame = new StackTrace(exception, true).GetFrame(0);
            try
            {
                PortablePdb.Forget(assembly);
                var first = UnityStackTrace.MonoTrace(exception).Split('\n')[0];
                Assert.EndsWith(" [0x" + frame.GetILOffset().ToString("x5") + "] in " + frame.GetFileName() + ":" + frame.GetFileLineNumber() + " ", first);
            }
            finally { PortablePdb.Reload(assembly); }
        }

        static int Callee(int value, [CallerLineNumber] int line = 0) => line + value;

        static int CallsCallee()
        {
            int value = 0;
            return Callee(value);
        }

        /// <summary>A Unity method's frame is placed where it calls the next one, read from the .pdb.</summary>
        [Fact]
        public void ACallSiteIsTheCallingStatement()
        {
            var pdb = PortablePdb.OfFile(typeof(StackTraceUtilityTests).Assembly.Location);
            Assert.NotNull(pdb);
            Assert.True(pdb.CallSite(typeof(StackTraceUtilityTests).FullName, nameof(CallsCallee), 0, nameof(Callee), out var document, out int line));
            Assert.EndsWith(ThisFile, document);
            Assert.Equal(CallsCallee(), line);
            Assert.False(pdb.CallSite(typeof(StackTraceUtilityTests).FullName, nameof(CallsCallee), 0, "Absent", out _, out _));
        }
    }
}
