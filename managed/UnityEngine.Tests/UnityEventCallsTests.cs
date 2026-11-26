using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using Unbound.Runtime;
using UnityEngine.Events;
using Xunit;
using Tools = UnityEditor.Events.UnityEventTools;

namespace UnityEngine.Tests
{
    /// <summary>How UnityEvents call their listeners, as the Unity 6000.0.71f1 Editor does
    /// (tests/projects/eventstacks): through invokable calls whose frames stay on a listener's
    /// exception, over a list an Invoke from a listener makes again.</summary>
    public sealed class UnityEventCallsTests
    {
        sealed class Listener : MonoBehaviour
        {
            public int Count;
            public void Throw() => throw new InvalidOperationException("From a listener.");
            public void ThrowInt(int value) => throw new ArgumentOutOfRangeException(nameof(value), value, "From a listener.");
            public void Add() => Count++;
        }

        static string[] Frames(Exception e) =>
            new StackTrace(e, false).GetFrames().Select(f => f.GetMethod()).Where(m => m != null)
                .Select(m => m.DeclaringType.Name + "." + m.Name).ToArray();

        static Exception Thrown(Action action)
        {
            try { action(); }
            catch (Exception e) { return e; }
            return null;
        }

        [Fact]
        public void APersistentListenerThrowsThroughItsOwnFrameAndTheEventsCalls()
        {
            var listener = new GameObject("L").AddComponent<Listener>();
            bool playing = EditorSession.IsPlaying;
            // Persistent listeners are RuntimeOnly: they run in Play mode.
            EditorSession.IsPlaying = true;
            try
            {
                var plain = new UnityEvent();
                Tools.AddVoidPersistentListener(plain, listener.Throw);
                var frames = Frames(Thrown(plain.Invoke));
                Assert.Equal(new[] { "Listener.Throw", "InvokableCall.Invoke", "UnityEvent.Invoke" }, frames.Take(3));

                var fixedArgument = new UnityEvent();
                Tools.AddIntPersistentListener(fixedArgument, listener.ThrowInt, 5);
                var e = Thrown(fixedArgument.Invoke);
                Assert.Equal(5, ((ArgumentOutOfRangeException)e).ActualValue);
                Assert.Equal(new[] { "Listener.ThrowInt", "InvokableCall`1.Invoke", "CachedInvokableCall`1.Invoke", "UnityEvent.Invoke" }, Frames(e).Take(4));

                var dynamic = new UnityEvent<int>();
                Tools.AddPersistentListener(dynamic, listener.ThrowInt);
                e = Thrown(() => dynamic.Invoke(3));
                Assert.Equal(3, ((ArgumentOutOfRangeException)e).ActualValue);
                Assert.Equal(new[] { "Listener.ThrowInt", "InvokableCall`1.Invoke", "UnityEvent`1.Invoke" }, Frames(e).Take(3));

                // A listener without the event's arguments on an event with them.
                var voidOnInt = new UnityEvent<int>();
                Tools.AddVoidPersistentListener(voidOnInt, listener.Throw);
                var voidFrames = new StackTrace(Thrown(() => voidOnInt.Invoke(1)), false).GetFrames().Select(f => f.GetMethod()).ToArray();
                Assert.Equal(new[] { "Listener.Throw", "InvokableCall.Invoke", "UnityEvent`1.Invoke" }, voidFrames.Take(3).Select(m => m.DeclaringType.Name + "." + m.Name));
                // InvokableCall.Invoke(), not Invoke(object[]).
                Assert.Empty(voidFrames[1].GetParameters());

                // The listeners after a throwing one do not run.
                var others = new UnityEvent();
                Tools.AddVoidPersistentListener(others, listener.Throw);
                Tools.AddVoidPersistentListener(others, listener.Add);
                others.AddListener(listener.Add);
                Assert.IsType<InvalidOperationException>(Thrown(others.Invoke));
                Assert.Equal(0, listener.Count);
            }
            finally
            {
                EditorSession.IsPlaying = playing;
                Object.DestroyImmediate(listener.gameObject);
            }
        }

        [Fact]
        public void ListenersChangedDuringAnInvokeRunFromTheNextAndANestedInvokeRebuildsTheOuterList()
        {
            var trace = new List<string>();
            var added = new UnityEvent();
            UnityAction late = () => trace.Add("late");
            added.AddListener(() => { trace.Add("first"); added.AddListener(late); });
            added.AddListener(() => trace.Add("second"));
            added.Invoke();
            trace.Add("|");
            added.Invoke();
            Assert.Equal("first,second,|,first,second,late", string.Join(",", trace));

            trace.Clear();
            var nested = new UnityEvent();
            int depth = 0;
            UnityAction extra = () => trace.Add("extra" + depth);
            nested.AddListener(() =>
            {
                trace.Add("a" + depth);
                if (depth++ == 0) { nested.AddListener(extra); nested.Invoke(); }
            });
            nested.AddListener(() => trace.Add("b" + depth));
            nested.Invoke();
            Assert.Equal("a0,a1,b2,extra2,b2,extra2", string.Join(",", trace));

            // A removal replaces the list an Invoke under way goes over.
            trace.Clear();
            depth = 0;
            var removed = new UnityEvent();
            UnityAction b = () => trace.Add("B" + depth);
            removed.AddListener(() =>
            {
                trace.Add("A" + depth);
                if (depth++ == 0) { removed.RemoveListener(b); removed.Invoke(); }
            });
            removed.AddListener(b);
            removed.AddListener(() => trace.Add("C" + depth));
            removed.AddListener(() => trace.Add("D" + depth));
            removed.Invoke();
            Assert.Equal("A0,A1,C2,D2,B2,C2,D2", string.Join(",", trace));

            trace.Clear();
            depth = 0;
            var emptied = new UnityEvent();
            emptied.AddListener(() =>
            {
                trace.Add("A" + depth);
                if (depth++ == 0) { emptied.RemoveAllListeners(); emptied.Invoke(); }
            });
            emptied.AddListener(() => trace.Add("B" + depth));
            emptied.AddListener(() => trace.Add("C" + depth));
            emptied.Invoke();
            Assert.Equal("A0,B1,C1", string.Join(",", trace));

            trace.Clear();
            var cleared = new UnityEvent();
            cleared.AddListener(() => { trace.Add("first"); cleared.RemoveAllListeners(); });
            cleared.AddListener(() => trace.Add("second"));
            cleared.Invoke();
            trace.Add("|");
            cleared.Invoke();
            Assert.Equal("first,second,|", string.Join(",", trace));
        }

        // Debug.LogException's caller lines leave the invokable call's frame out and give the
        // event's frame no place, as the Editor's do (tests/projects/eventstacks log_exception_inside).
        [Fact]
        public void LogExceptionCallerLinesLeaveTheInvokableCallOut()
        {
            string trace = null;
            var e = new UnityEvent();
            e.AddListener(() =>
            {
                try { throw new InvalidOperationException("Caught."); }
                catch (Exception caught) { trace = UnityStackTrace.ExceptionText(caught).Trace; }
            });
            e.Invoke();
            Assert.Contains("\nUnityEngine.Events.UnityEvent:Invoke()\n", trace);
            Assert.DoesNotContain("UnityEngine.Events.InvokableCall", trace);
        }

        // With Unity's places known (here the engine's own assembly and .pdb stand for Unity's
        // UnityEngine.CoreModule), the event's frame in Debug.LogException's caller lines still has
        // none, while the listener's ExtractStackTrace gives it one (tests/projects/eventstacks
        // log_exception_inside, extract.*).
        [Fact]
        public void TheEventsFrameCallingAnInvokableCallHasNoPlaceInLogExceptionCallerLines()
        {
            var data = UnityEditor.UnityInstallInfo.DataPath;
            var root = Path.Combine(Path.GetTempPath(), "unbound-eventplaces-" + Guid.NewGuid().ToString("N"));
            try
            {
                var managed = Path.Combine(root, "Managed", "UnityEngine");
                Directory.CreateDirectory(managed);
                var engine = typeof(UnityEvent).Assembly.Location;
                File.Copy(engine, Path.Combine(managed, "UnityEngine.CoreModule.dll"));
                File.Copy(Path.ChangeExtension(engine, ".pdb"), Path.Combine(managed, "UnityEngine.CoreModule.pdb"));
                UnityEditor.UnityInstallInfo.DataPath = root;
                string logged = null, extracted = null;
                var e = new UnityEvent();
                e.AddListener(() =>
                {
                    extracted = StackTraceUtility.ExtractStackTrace();
                    try { throw new InvalidOperationException("Caught."); }
                    catch (Exception caught) { logged = UnityStackTrace.ExceptionText(caught).Trace; }
                });
                e.Invoke();
                Assert.Matches(@"\nUnityEngine\.Events\.UnityEvent:Invoke \(\) \(at [^)]*Events\.cs:\d+\)\n", extracted);
                Assert.Contains("\nUnityEngine.Events.UnityEvent:Invoke()\n", logged);
            }
            finally
            {
                UnityEditor.UnityInstallInfo.DataPath = data;
                if (Directory.Exists(root)) Directory.Delete(root, true);
            }
        }

        [Fact]
        public void AListenerOfADestroyedObjectDoesNotRun()
        {
            var listener = new GameObject("L").AddComponent<Listener>();
            var e = new UnityEvent();
            e.AddListener(listener.Add);
            Object.DestroyImmediate(listener.gameObject);
            e.Invoke();
            Assert.Equal(0, listener.Count);
        }

        static int LineOf(string file, string text)
        {
            var lines = File.ReadAllLines(file);
            int found = Array.FindIndex(lines, l => l.Contains(text, StringComparison.Ordinal));
            Assert.True(found >= 0, text);
            return found + 1;
        }

        static string Source(string name, [System.Runtime.CompilerServices.CallerFilePath] string here = "") =>
            Path.GetFullPath(Path.Combine(Path.GetDirectoryName(here), "..", "UnityEngine", "Core", name));

        // The place a frame shows is the call its method makes: of the overload with the frame's
        // parameter types, and of the callee the next frame inward stands for (read here from the
        // engine's own .pdb, whose methods are laid out like Unity's).
        [Fact]
        public void ACallSiteIsTheOverloadsAndTheInnerFramesCall()
        {
            var pdb = PortablePdb.OfFile(typeof(InvokableCall).Assembly.Location);
            Assert.NotNull(pdb);
            string calls = Source("InvokableCalls.cs"), events = Source("Events.cs");

            Assert.True(pdb.CallSite("UnityEngine.Events.InvokableCall`1", "Invoke", 1, new[] { "!0" }, "Invoke", null, -1, out _, out var document, out int line));
            Assert.EndsWith("InvokableCalls.cs", document.Replace('\\', '/'));
            Assert.Equal(LineOf(calls, "if (AllowInvoke(Delegate)) Delegate(args0);"), line);
            Assert.True(pdb.CallSite("UnityEngine.Events.InvokableCall`1", "Invoke", 1, new[] { "System.Object[]" }, "Invoke", null, -1, out _, out _, out line));
            Assert.Equal(LineOf(calls, "if (AllowInvoke(Delegate)) Delegate((T1)args[0]);"), line);

            Assert.True(pdb.CallSite("UnityEngine.Events.UnityEvent", "Invoke", 0, new string[0], "Invoke", "UnityEngine.Events.InvokableCall", 0, out _, out _, out line));
            int direct = LineOf(events, "if (calls[i] is InvokableCall call) call.Invoke();");
            Assert.Equal(direct, line);
            Assert.True(pdb.CallSite("UnityEngine.Events.UnityEvent", "Invoke", 0, new string[0], "Invoke", "UnityEngine.Events.BaseInvokableCall", 1, out _, out _, out line));
            Assert.True(line > direct);
            Assert.True(pdb.CallSite("UnityEngine.Events.UnityEvent`1", "Invoke", 1, new[] { "!0" }, "Invoke", "UnityEngine.Events.InvokableCall`1", 1, out _, out _, out line));
            Assert.Equal(LineOf(events, "if (calls[i] is InvokableCall<T0> call) call.Invoke(arg0);"), line);
        }
    }
}
