using System;
using System.Reflection;
using System.Threading;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Awaiting an `async Awaitable` method's awaitable after it has completed, on a thread of the
    /// other kind than the one that called the method. Unity 6000.0 Manual, "Awaitable completion
    /// and continuation"
    /// (https://docs.unity.com/en-us/engine/6000.0/manual/scripting/programming-distribute-work-threads/async-await-support/async-awaitable-continuations),
    /// states where the method's caller resumes; it does not cover readiness or a registration
    /// after completion. The evidence for those is Unity's public 6000.0.71f1 C# reference source
    /// (UnityCsReference 78d4b97c, Runtime/Export/Scripting/Awaitable.cs): public IsCompleted is
    /// "_managedAwaitableDone && MatchCompletionThreadAffinity(...)", and SetContinuation on a
    /// logically completed awaitable calls RunOrScheduleContinuation with the captured affinity.
    /// That is implementation evidence for this release, unmeasured against the Editor.
    ///
    /// "Completed" (logical completion) and "ready to go on synchronously on this thread" stay two
    /// things: a completed main-called method is ready only on the main thread, a completed
    /// off-main-called one only off it, and a completion source (no affinity) everywhere.
    ///
    /// Each test makes its own thread the main thread (or, for the unbooted case, no thread). Other
    /// threads are dedicated threads started and joined by the test, or pool work items the test
    /// waits for. Waits are bounded joins and events set by the code under test; no test sleeps,
    /// polls or measures time, and none asserts when a queued pool item runs.
    /// </summary>
    [Collection("Frames")]
    public sealed class AwaitableCompletedAffinityTests : IDisposable
    {
        static readonly FieldInfo MainThread = typeof(PlayerLoopDriver).GetField("s_MainThreadId", BindingFlags.Static | BindingFlags.NonPublic);
        static readonly TimeSpan Bound = TimeSpan.FromSeconds(30);
        readonly object m_PreviousMainThread = MainThread.GetValue(null);

        public void Dispose()
        {
            MainThreadContext.Instance.RunPending();
            MainThread.SetValue(null, m_PreviousMainThread);
        }

        /// <summary>Makes the calling thread the main thread, after running what earlier tests
        /// posted, so that only this test's continuations are queued.</summary>
        static int BecomeMainThread()
        {
            MainThreadContext.Instance.RunPending();
            MainThread.SetValue(null, Environment.CurrentManagedThreadId);
            return Environment.CurrentManagedThreadId;
        }

        /// <summary>Runs an action on a new dedicated (non-pool) thread and waits for it to end;
        /// returns its thread id.</summary>
        static int OnDedicatedThread(Action action)
        {
            Exception failure = null;
            int id = -1;
            var thread = new Thread(() =>
            {
                id = Environment.CurrentManagedThreadId;
                try { action(); }
                catch (Exception e) { failure = e; }
            });
            thread.Start();
            Assert.True(thread.Join(Bound));
            if (failure != null) throw new InvalidOperationException("the dedicated thread failed", failure);
            return id;
        }

        /// <summary>Runs an action as a thread pool work item and waits for it to end; returns its
        /// thread id.</summary>
        static int OnPoolThread(Action action)
        {
            Exception failure = null;
            int id = -1;
            var done = new ManualResetEventSlim();
            ThreadPool.UnsafeQueueUserWorkItem(_ =>
            {
                id = Environment.CurrentManagedThreadId;
                try { action(); }
                catch (Exception e) { failure = e; }
                finally { done.Set(); }
            }, null);
            Assert.True(done.Wait(Bound));
            if (failure != null) throw new InvalidOperationException("the pool work item failed", failure);
            return id;
        }

        /// <summary>Where a continuation ran.</summary>
        sealed class Probe
        {
            public readonly ManualResetEventSlim Resumed = new ManualResetEventSlim();
            public int Thread = -1, Result;
            public bool OnPool;
            public Exception Failure;

            public void Ran()
            {
                Thread = Environment.CurrentManagedThreadId;
                OnPool = System.Threading.Thread.CurrentThread.IsThreadPoolThread;
                Resumed.Set();
            }

            public void WaitForIt() => Assert.True(Resumed.Wait(Bound));
        }

        /// <summary>A completion source that has completed: the method awaiting it finishes
        /// synchronously, inside its call, on the calling thread.</summary>
        static Awaitable Completed()
        {
            var source = new AwaitableCompletionSource();
            source.SetResult();
            return source.Awaitable;
        }

        static async Awaitable EndsAfter(Awaitable wait) => await wait;

        static async Awaitable<int> ReturnsAfter(Awaitable wait, int value)
        {
            await wait;
            return value;
        }

        static async Awaitable ThrowsAfter(Awaitable wait)
        {
            await wait;
            throw new InvalidOperationException("thrown by the completed method");
        }

        static async Awaitable AwaitsResult(Awaitable<int> awaited, Probe probe)
        {
            probe.Result = await awaited;
            probe.Ran();
        }

        static async Awaitable Catches(Awaitable awaited, Probe probe)
        {
            try { await awaited; }
            catch (Exception e) { probe.Failure = e; }
            probe.Ran();
        }

        // --- Compiler-generated awaits of a logically completed method -------------------------

        // A method called (and completed) on the main thread, awaited by a method running on a
        // dedicated thread: not ready there, so the await registers and its continuation is posted
        // to the main thread. Old Core (c4130273 and 2c329443): red at the first
        // Assert.False(probe.Resumed.IsSet) (IsCompleted was true; the awaiter went on at once on
        // the dedicated thread).
        [Fact]
        public void ACompletedMainThreadCalledMethodAwaitedOffTheMainThreadResumesOnTheMainThread()
        {
            int main = BecomeMainThread();
            var method = ReturnsAfter(Completed(), 6);
            Assert.True(method.GetAwaiter().IsCompleted); // guard: ready on the main thread
            var probe = new Probe();
            OnDedicatedThread(() => _ = AwaitsResult(method, probe));
            Assert.False(probe.Resumed.IsSet);
            MainThreadContext.Instance.RunPending();
            Assert.True(probe.Resumed.IsSet);
            Assert.Equal(main, probe.Thread);
            Assert.Equal(6, probe.Result);
        }

        // A method called (and completed) on a dedicated thread, awaited on the main thread: not
        // ready there, so the continuation is queued to the pool. Old Core (c4130273 and
        // 2c329443): red at Assert.NotEqual(main, probe.Thread) (it went on at once on the main
        // thread).
        [Fact]
        public void ACompletedBackgroundCalledMethodAwaitedOnTheMainThreadResumesOnThePool()
        {
            int main = BecomeMainThread();
            Awaitable<int> method = null;
            OnDedicatedThread(() => method = ReturnsAfter(Completed(), 8));
            var probe = new Probe();
            _ = AwaitsResult(method, probe);
            probe.WaitForIt();
            Assert.NotEqual(main, probe.Thread);
            Assert.True(probe.OnPool);
            Assert.Equal(8, probe.Result);
            // Guard: the result read on the pool consumed it; a second read fails as before.
            Assert.Throws<NullReferenceException>(() => method.GetAwaiter().GetResult());
        }

        // An exception of a completed main-called method reaches an off-main awaiter on the main
        // thread. Old Core: red at Assert.False(probe.Resumed.IsSet).
        [Fact]
        public void AnExceptionOfACompletedMainThreadCalledMethodReachesAnOffMainAwaiterOnTheMainThread()
        {
            int main = BecomeMainThread();
            var method = ThrowsAfter(Completed());
            var probe = new Probe();
            OnDedicatedThread(() => _ = Catches(method, probe));
            Assert.False(probe.Resumed.IsSet);
            MainThreadContext.Instance.RunPending();
            Assert.True(probe.Resumed.IsSet);
            Assert.Equal(main, probe.Thread);
            Assert.IsType<InvalidOperationException>(probe.Failure);
            Assert.Equal("thrown by the completed method", probe.Failure.Message);
        }

        // A background-called method cancelled before anyone awaits it, then awaited on the main
        // thread: the cancellation reaches the awaiter on the pool. Old Core: red at
        // Assert.NotEqual(main, probe.Thread).
        [Fact]
        public void ACancelledBackgroundCalledMethodAwaitedOnTheMainThreadThrowsOnThePool()
        {
            int main = BecomeMainThread();
            var never = new AwaitableCompletionSource();
            Awaitable method = null;
            OnDedicatedThread(() =>
            {
                method = EndsAfter(never.Awaitable);
                method.Cancel();
            });
            var probe = new Probe();
            _ = Catches(method, probe);
            probe.WaitForIt();
            Assert.NotEqual(main, probe.Thread);
            Assert.True(probe.OnPool);
            Assert.IsType<OperationCanceledException>(probe.Failure);
            // The cancelled method's own body may still finish; its result is dropped.
            never.SetResult();
        }

        // --- Public IsCompleted and awaiter readiness per affinity -----------------------------

        // Old Core: red at Assert.False(offMain) (readiness ignored the affinity).
        [Fact]
        public void ACompletedMainThreadCalledMethodIsReadyOnlyOnTheMainThread()
        {
            BecomeMainThread();
            var pending = new AwaitableCompletionSource();
            var notYet = EndsAfter(pending.Awaitable);
            Assert.False(notYet.IsCompleted); // guard: not completed is not ready anywhere
            var method = EndsAfter(Completed());
            var generic = ReturnsAfter(Completed(), 1);
            Assert.True(method.IsCompleted);
            Assert.True(method.GetAwaiter().IsCompleted);
            Assert.True(generic.GetAwaiter().IsCompleted);
            bool offMain = true, offMainAwaiter = true, offMainGeneric = true, onPool = true;
            OnDedicatedThread(() =>
            {
                offMain = method.IsCompleted;
                offMainAwaiter = method.GetAwaiter().IsCompleted;
                offMainGeneric = generic.GetAwaiter().IsCompleted;
            });
            OnPoolThread(() => onPool = method.IsCompleted);
            Assert.False(offMain);
            Assert.False(offMainAwaiter);
            Assert.False(offMainGeneric);
            Assert.False(onPool);
            // Guard: still completed for the thread that can go on.
            Assert.True(method.IsCompleted);
            pending.SetResult();
        }

        // Old Core: red at Assert.False(method.IsCompleted) on the main thread.
        [Fact]
        public void ACompletedBackgroundCalledMethodIsReadyOnlyOffTheMainThread()
        {
            BecomeMainThread();
            Awaitable method = null;
            Awaitable<int> generic = null;
            bool onCaller = false;
            OnDedicatedThread(() =>
            {
                method = EndsAfter(Completed());
                generic = ReturnsAfter(Completed(), 2);
                onCaller = method.IsCompleted;
            });
            Assert.False(method.IsCompleted);
            Assert.False(method.GetAwaiter().IsCompleted);
            Assert.False(generic.GetAwaiter().IsCompleted);
            // Guards: ready on the calling thread, on another dedicated thread and on the pool.
            Assert.True(onCaller);
            bool onOther = false, onOtherGeneric = false, onPool = false;
            OnDedicatedThread(() =>
            {
                onOther = method.GetAwaiter().IsCompleted;
                onOtherGeneric = generic.GetAwaiter().IsCompleted;
            });
            OnPoolThread(() => onPool = method.IsCompleted);
            Assert.True(onOther);
            Assert.True(onOtherGeneric);
            Assert.True(onPool);
        }

        // --- Late OnCompleted registration ------------------------------------------------------

        // Old Core: red at Assert.False(probe.Resumed.IsSet) (ran inline on the registering
        // dedicated thread).
        [Fact]
        public void ALateRegistrationOffTheMainThreadOnACompletedMainThreadCalledMethodIsPosted()
        {
            int main = BecomeMainThread();
            var method = EndsAfter(Completed());
            var probe = new Probe();
            OnDedicatedThread(() => method.GetAwaiter().OnCompleted(probe.Ran));
            Assert.False(probe.Resumed.IsSet);
            MainThreadContext.Instance.RunPending();
            Assert.True(probe.Resumed.IsSet);
            Assert.Equal(main, probe.Thread);
            method.GetAwaiter().GetResult();
        }

        // Old Core: red at Assert.NotEqual(main, probe.Thread) (ran inline on the main thread).
        [Fact]
        public void ALateRegistrationOnTheMainThreadOnACompletedBackgroundCalledMethodGoesToThePool()
        {
            int main = BecomeMainThread();
            Awaitable<int> method = null;
            OnDedicatedThread(() => method = ReturnsAfter(Completed(), 3));
            var probe = new Probe();
            method.GetAwaiter().OnCompleted(() =>
            {
                probe.Result = method.GetAwaiter().GetResult();
                probe.Ran();
            });
            probe.WaitForIt();
            Assert.NotEqual(main, probe.Thread);
            Assert.True(probe.OnPool);
            Assert.Equal(3, probe.Result);
        }

        // --- Matching-thread guards (green on old and new Core) ---------------------------------

        // A late registration on a thread that suits the affinity runs inline, before OnCompleted
        // returns: main-called on the main thread; off-main-called on another dedicated thread and on
        // the pool (BackgroundThread suits every thread but the main one).
        [Fact]
        public void ALateRegistrationOnAMatchingThreadRunsInline()
        {
            int main = BecomeMainThread();
            var mainCalled = EndsAfter(Completed());
            int ranOn = -1;
            mainCalled.GetAwaiter().OnCompleted(() => ranOn = Environment.CurrentManagedThreadId);
            Assert.Equal(main, ranOn);

            // One awaitable per registration: an Awaitable is awaited once.
            Awaitable forOther = null, forPool = null;
            OnDedicatedThread(() =>
            {
                forOther = EndsAfter(Completed());
                forPool = EndsAfter(Completed());
            });
            int otherRanOn = -1, poolRanOn = -1;
            int other = OnDedicatedThread(() => forOther.GetAwaiter().OnCompleted(() => otherRanOn = Environment.CurrentManagedThreadId));
            Assert.Equal(other, otherRanOn);
            int pool = OnPoolThread(() => forPool.GetAwaiter().OnCompleted(() => poolRanOn = Environment.CurrentManagedThreadId));
            Assert.Equal(pool, poolRanOn);
        }

        // A compiler-generated await of a completed off-main-called method on another dedicated
        // thread goes on synchronously there, without suspending.
        [Fact]
        public void ACompletedBackgroundCalledMethodAwaitedOnAnotherDedicatedThreadGoesOnSynchronously()
        {
            BecomeMainThread();
            Awaitable<int> method = null;
            OnDedicatedThread(() => method = ReturnsAfter(Completed(), 4));
            var probe = new Probe();
            bool resumedInline = false;
            int other = OnDedicatedThread(() =>
            {
                _ = AwaitsResult(method, probe);
                resumedInline = probe.Resumed.IsSet;
            });
            Assert.True(resumedInline);
            Assert.Equal(other, probe.Thread);
            Assert.Equal(4, probe.Result);
        }

        // --- None-affinity guards (green on old and new Core) -----------------------------------

        // A completed completion source is ready on every thread, and a late registration runs
        // inline on the registering thread, main or not.
        [Fact]
        public void ACompletedCompletionSourceIsReadyAndRunsInlineEverywhere()
        {
            int main = BecomeMainThread();
            var source = new AwaitableCompletionSource<int>();
            source.SetResult(5);
            var awaited = source.Awaitable;
            var plain = Completed();
            Assert.True(plain.IsCompleted);
            Assert.True(awaited.GetAwaiter().IsCompleted);
            bool offMain = false, offMainGeneric = false, onPool = false;
            int ranOn = -1, otherRanOn = -1;
            int other = OnDedicatedThread(() =>
            {
                offMain = plain.IsCompleted;
                offMainGeneric = awaited.GetAwaiter().IsCompleted;
                plain.GetAwaiter().OnCompleted(() => otherRanOn = Environment.CurrentManagedThreadId);
            });
            OnPoolThread(() => onPool = plain.IsCompleted);
            awaited.GetAwaiter().OnCompleted(() => ranOn = Environment.CurrentManagedThreadId);
            Assert.True(offMain);
            Assert.True(offMainGeneric);
            Assert.True(onPool);
            Assert.Equal(other, otherRanOn);
            Assert.Equal(main, ranOn);
            Assert.Equal(5, awaited.GetAwaiter().GetResult());
            // Guard: completion is logical, whatever thread asks; a second completion is refused.
            Assert.False(source.TrySetResult(6));
        }

        // --- Unbooted guard (green on old and new Core) -----------------------------------------

        // Before the player loop boots every thread is the main thread: a method called on any
        // thread is ready everywhere and a late registration runs inline on the registering thread.
        [Fact]
        public void BeforeBootACompletedMethodIsReadyAndRunsInlineOnEveryThread()
        {
            MainThreadContext.Instance.RunPending();
            MainThread.SetValue(null, -1);
            Awaitable method = null;
            OnDedicatedThread(() => method = EndsAfter(Completed()));
            Assert.True(method.IsCompleted);
            bool onOther = false;
            int otherRanOn = -1;
            int other = OnDedicatedThread(() =>
            {
                onOther = method.IsCompleted;
                method.GetAwaiter().OnCompleted(() => otherRanOn = Environment.CurrentManagedThreadId);
            });
            Assert.True(onOther);
            Assert.Equal(other, otherRanOn);
        }
    }
}
