using System;
using System.Reflection;
using System.Threading;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Where the caller of an `async Awaitable` method called off the main thread resumes. Unity
    /// 6000.0 Manual, "Awaitable completion and continuation"
    /// (https://docs.unity.com/en-us/engine/6000.0/manual/scripting/programming-distribute-work-threads/async-await-support/async-awaitable-continuations):
    /// "If the method is called from the main thread, it resumes on the main thread. Otherwise it
    /// resumes on a .NET ThreadPool thread." The effect of MainThreadAsync and BackgroundThreadAsync
    /// is "local to the current method only". Completion sources are not async methods and keep
    /// resuming their awaiter on the completing thread.
    ///
    /// Each test makes its own thread the main thread (or, for the unbooted case, no thread). Other
    /// threads are dedicated threads started and joined by the test, or pool work items the test
    /// waits for. Waits are bounded joins and events set by the code under test; no test sleeps,
    /// polls or measures time. Assertions are about which kind of thread ran the caller (main, a
    /// pool thread, or the completing thread), never about when a queued pool item runs.
    /// </summary>
    [Collection("Frames")]
    public sealed class AwaitableBackgroundCallerTests : IDisposable
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

        /// <summary>Runs an action on a new dedicated (non-pool) thread and waits for it to end.</summary>
        static void OnDedicatedThread(Action action)
        {
            Exception failure = null;
            bool onPool = true;
            var thread = new Thread(() =>
            {
                onPool = Thread.CurrentThread.IsThreadPoolThread;
                try { action(); }
                catch (Exception e) { failure = e; }
            });
            thread.Start();
            Assert.True(thread.Join(Bound));
            Assert.False(onPool);
            if (failure != null) throw new InvalidOperationException("the dedicated thread failed", failure);
        }

        /// <summary>Runs an action as a thread pool work item and waits for it to end.</summary>
        static void OnPoolThread(Action action)
        {
            Exception failure = null;
            bool onPool = false;
            var done = new ManualResetEventSlim();
            ThreadPool.UnsafeQueueUserWorkItem(_ =>
            {
                onPool = Thread.CurrentThread.IsThreadPoolThread;
                try { action(); }
                catch (Exception e) { failure = e; }
                finally { done.Set(); }
            }, null);
            Assert.True(done.Wait(Bound));
            Assert.True(onPool);
            if (failure != null) throw new InvalidOperationException("the pool work item failed", failure);
        }

        sealed class Probe
        {
            public readonly ManualResetEventSlim Resumed = new ManualResetEventSlim();
            public int BodyThread = -1, CallerThread = -1, Result;
            public bool CallerOnPool;
            public Exception Failure;
            public Awaitable<int> Method;

            public void CallerResumed()
            {
                CallerThread = Environment.CurrentManagedThreadId;
                CallerOnPool = Thread.CurrentThread.IsThreadPoolThread;
                Resumed.Set();
            }

            public void WaitForCaller() => Assert.True(Resumed.Wait(Bound));
        }

        static async Awaitable EndsAfter(Awaitable wait, Probe probe)
        {
            await wait;
            probe.BodyThread = Environment.CurrentManagedThreadId;
        }

        static async Awaitable CallsEndsAfter(Awaitable wait, Probe probe)
        {
            await EndsAfter(wait, probe);
            probe.CallerResumed();
        }

        static async Awaitable<int> SwitchesToMain(Probe probe)
        {
            await Awaitable.MainThreadAsync();
            probe.BodyThread = Environment.CurrentManagedThreadId;
            return 11;
        }

        static async Awaitable CallsSwitchesToMain(Probe probe)
        {
            var method = SwitchesToMain(probe);
            probe.Method = method;
            probe.Result = await method;
            probe.CallerResumed();
        }

        static async Awaitable<int> ComputesInBackground(Probe probe)
        {
            await Awaitable.BackgroundThreadAsync();
            probe.BodyThread = Environment.CurrentManagedThreadId;
            return 42;
        }

        static async Awaitable CallsComputesInBackground(Probe probe)
        {
            probe.Result = await ComputesInBackground(probe);
            probe.CallerResumed();
        }

        static async Awaitable ThrowsOnMain()
        {
            await Awaitable.MainThreadAsync();
            throw new InvalidOperationException("thrown on the main thread");
        }

        static async Awaitable WaitsForSecondsOnMain(CancellationToken token)
        {
            await Awaitable.MainThreadAsync();
            await Awaitable.WaitForSecondsAsync(100f, token);
        }

        static async Awaitable Catches(Awaitable awaited, Probe probe)
        {
            try { await awaited; }
            catch (Exception e) { probe.Failure = e; }
            probe.CallerResumed();
        }

        // Non-generic method called from a dedicated thread; its body ends on the main thread when a
        // completion source is set there. Old Core: red at Assert.NotEqual(main, probe.CallerThread)
        // (the caller resumed inline on the main thread).
        [Fact]
        public void ABackgroundCallerResumesOnThePoolWhenTheMethodEndsOnTheMainThread()
        {
            int main = BecomeMainThread();
            var source = new AwaitableCompletionSource();
            var probe = new Probe();
            OnDedicatedThread(() => _ = CallsEndsAfter(source.Awaitable, probe));
            Assert.False(probe.Resumed.IsSet);
            source.SetResult();
            // Guard: the source's own awaiter (the body) still goes on inline on the main thread.
            Assert.Equal(main, probe.BodyThread);
            probe.WaitForCaller();
            Assert.NotEqual(main, probe.CallerThread);
            Assert.True(probe.CallerOnPool);
        }

        // Generic method called from a dedicated thread that switches to the main thread itself, the
        // Manual's example the other way round. The switch stays local to the method (guard); its
        // caller resumes on the pool. Old Core: red at Assert.NotEqual(main, probe.CallerThread).
        [Fact]
        public void ABackgroundCallerOfAMethodThatSwitchesToTheMainThreadResumesOnThePool()
        {
            int main = BecomeMainThread();
            var probe = new Probe();
            OnDedicatedThread(() => _ = CallsSwitchesToMain(probe));
            // MainThreadAsync from another thread waits for the main thread's pass.
            Assert.Equal(-1, probe.BodyThread);
            MainThreadContext.Instance.RunPending();
            Assert.Equal(main, probe.BodyThread);
            probe.WaitForCaller();
            Assert.NotEqual(main, probe.CallerThread);
            Assert.True(probe.CallerOnPool);
            Assert.Equal(11, probe.Result);
            // Guard: the result read on the pool consumed it; a second read fails as before.
            Assert.Throws<NullReferenceException>(() => probe.Method.GetAwaiter().GetResult());
        }

        // Guard (green on old and new Core): completion already on a pool thread keeps running the
        // background caller synchronously there, before the completing call returns.
        [Fact]
        public void ACompletionOnAPoolThreadResumesABackgroundCallerThereSynchronously()
        {
            BecomeMainThread();
            var source = new AwaitableCompletionSource();
            var probe = new Probe();
            OnDedicatedThread(() => _ = CallsEndsAfter(source.Awaitable, probe));
            int completer = -1;
            bool resumedInline = false;
            OnPoolThread(() =>
            {
                completer = Environment.CurrentManagedThreadId;
                source.SetResult();
                resumedInline = probe.Resumed.IsSet && probe.CallerThread == completer;
            });
            Assert.True(resumedInline);
            Assert.Equal(completer, probe.BodyThread);
            Assert.True(probe.CallerOnPool);
        }

        // Guard (green on old and new Core): the Manual's example called from a pool thread.
        // BackgroundThreadAsync completes at once off the main thread, the method finishes before it
        // is awaited, and the caller goes on synchronously on the same pool thread.
        [Fact]
        public void ABackgroundCallerOfABackgroundSwitchGoesOnSynchronously()
        {
            BecomeMainThread();
            var probe = new Probe();
            int poolThread = -1;
            bool resumedInline = false;
            OnPoolThread(() =>
            {
                poolThread = Environment.CurrentManagedThreadId;
                _ = CallsComputesInBackground(probe);
                resumedInline = probe.Resumed.IsSet;
            });
            Assert.True(resumedInline);
            Assert.Equal(poolThread, probe.BodyThread);
            Assert.Equal(poolThread, probe.CallerThread);
            Assert.Equal(42, probe.Result);
        }

        // A thread the game started is a background thread but not a ThreadPool thread: completion
        // there moves the background caller to the pool. Old Core: red at
        // Assert.True(probe.CallerOnPool) (the caller resumed inline on the dedicated thread).
        [Fact]
        public void ACompletionOnADedicatedThreadMovesABackgroundCallerToThePool()
        {
            BecomeMainThread();
            var source = new AwaitableCompletionSource();
            var probe = new Probe();
            OnDedicatedThread(() => _ = CallsEndsAfter(source.Awaitable, probe));
            int completer = -1;
            OnDedicatedThread(() =>
            {
                completer = Environment.CurrentManagedThreadId;
                source.SetResult();
            });
            // Guard: the body (the source's own awaiter) ran inline on the dedicated thread.
            Assert.Equal(completer, probe.BodyThread);
            probe.WaitForCaller();
            Assert.True(probe.CallerOnPool);
        }

        // Old Core: red at Assert.NotEqual(main, probe.CallerThread).
        [Fact]
        public void AnExceptionThrownOnTheMainThreadReachesABackgroundCallerOnThePool()
        {
            int main = BecomeMainThread();
            var probe = new Probe();
            OnDedicatedThread(() => _ = Catches(ThrowsOnMain(), probe));
            Assert.False(probe.Resumed.IsSet);
            MainThreadContext.Instance.RunPending();
            probe.WaitForCaller();
            Assert.NotEqual(main, probe.CallerThread);
            Assert.True(probe.CallerOnPool);
            Assert.IsType<InvalidOperationException>(probe.Failure);
            Assert.Equal("thrown on the main thread", probe.Failure.Message);
        }

        // Cancelling a background-called method's Awaitable on the main thread completes it there;
        // its caller still resumes on the pool. Old Core: red at Assert.NotEqual(main, probe.CallerThread).
        [Fact]
        public void CancellingABackgroundCalledMethodOnTheMainThreadResumesItsCallerOnThePool()
        {
            int main = BecomeMainThread();
            var source = new AwaitableCompletionSource();
            var probe = new Probe();
            Awaitable method = null;
            OnDedicatedThread(() =>
            {
                method = EndsAfter(source.Awaitable, new Probe());
                _ = Catches(method, probe);
            });
            method.Cancel();
            probe.WaitForCaller();
            Assert.NotEqual(main, probe.CallerThread);
            Assert.True(probe.CallerOnPool);
            Assert.IsType<OperationCanceledException>(probe.Failure);
            // The cancelled method's own body may still finish; its result is dropped.
            source.SetResult();
        }

        // Token cancellation reaches the timed wait on the main thread (WatchCancellation, unchanged)
        // and the body's OperationCanceledException then reaches the background caller on the pool.
        // Old Core: red at Assert.NotEqual(main, probe.CallerThread).
        [Fact]
        public void TokenCancellationOnTheMainThreadReachesABackgroundCallerOnThePool()
        {
            int main = BecomeMainThread();
            using var cts = new CancellationTokenSource();
            var probe = new Probe();
            OnDedicatedThread(() => _ = Catches(WaitsForSecondsOnMain(cts.Token), probe));
            MainThreadContext.Instance.RunPending();
            Assert.False(probe.Resumed.IsSet);
            cts.Cancel();
            probe.WaitForCaller();
            Assert.NotEqual(main, probe.CallerThread);
            Assert.True(probe.CallerOnPool);
            Assert.IsType<OperationCanceledException>(probe.Failure);
        }

        // Guard (green on old and new Core): a completion source has no affinity. An awaiter
        // registered off the main thread resumes synchronously on the main thread that completes it.
        [Fact]
        public void ACompletionSourceCompletedOnTheMainThreadResumesAnOffMainAwaiterThere()
        {
            int main = BecomeMainThread();
            var source = new AwaitableCompletionSource<int>();
            var awaited = source.Awaitable;
            int resumedOn = -1, result = 0;
            OnDedicatedThread(() => awaited.GetAwaiter().OnCompleted(() =>
            {
                resumedOn = Environment.CurrentManagedThreadId;
                result = awaited.GetAwaiter().GetResult();
            }));
            source.SetResult(4);
            Assert.Equal(main, resumedOn);
            Assert.Equal(4, result);
        }

        // Guard (green on old and new Core): nor is a completion source moved to the pool when a
        // dedicated thread completes it; its awaiter resumes there before SetResult returns.
        [Fact]
        public void ACompletionSourceCompletedOnADedicatedThreadResumesItsAwaiterThere()
        {
            BecomeMainThread();
            var source = new AwaitableCompletionSource();
            int resumedOn = -1, completer = -1;
            bool resumedOnPool = true, resumedInline = false;
            OnDedicatedThread(() => source.Awaitable.GetAwaiter().OnCompleted(() =>
            {
                resumedOn = Environment.CurrentManagedThreadId;
                resumedOnPool = Thread.CurrentThread.IsThreadPoolThread;
            }));
            OnDedicatedThread(() =>
            {
                completer = Environment.CurrentManagedThreadId;
                source.SetResult();
                resumedInline = resumedOn == completer;
            });
            Assert.True(resumedInline);
            Assert.False(resumedOnPool);
        }

        // Guard (green on old and new Core): before the player loop boots there is no main thread,
        // every thread counts as one, and a method called on another thread is not moved either.
        [Fact]
        public void BeforeBootABackgroundCallerResumesOnTheCompletingThread()
        {
            MainThreadContext.Instance.RunPending();
            MainThread.SetValue(null, -1);
            var source = new AwaitableCompletionSource();
            var probe = new Probe();
            OnDedicatedThread(() => _ = CallsEndsAfter(source.Awaitable, probe));
            int completer = -1;
            bool resumedInline = false;
            OnDedicatedThread(() =>
            {
                completer = Environment.CurrentManagedThreadId;
                source.SetResult();
                resumedInline = probe.Resumed.IsSet && probe.CallerThread == completer;
            });
            Assert.True(resumedInline);
            Assert.False(probe.CallerOnPool);
        }
    }
}
