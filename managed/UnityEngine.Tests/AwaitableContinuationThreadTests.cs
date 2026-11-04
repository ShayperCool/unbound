using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using Unbound.Runtime;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Where an `async Awaitable` method's awaiter resumes. Unity 6000.0 Manual, "Awaitable completion
    /// and continuation"
    /// (https://docs.unity.com/en-us/engine/6000.0/manual/scripting/programming-distribute-work-threads/async-await-support/async-awaitable-continuations):
    /// "If the method is called from the main thread, it resumes on the main thread"; the effect of
    /// MainThreadAsync and BackgroundThreadAsync is "local to the current method only". The Manual's
    /// "Introduction to Awaitable" table: an Awaitable continuation "runs synchronously when completion
    /// is triggered".
    ///
    /// Each test makes its own thread the main thread. Other threads are dedicated threads started
    /// and joined by the test; the one pool switch is observed through an await registration, so
    /// no test sleeps or polls.
    /// </summary>
    [Collection("Frames")]
    public sealed class AwaitableContinuationThreadTests : IDisposable
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

        /// <summary>Runs an action on a new thread and waits for it to end; returns its thread id.</summary>
        static int OnOtherThread(Action action)
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
            if (failure != null) throw new InvalidOperationException("the other thread failed", failure);
            return id;
        }

        sealed class Probe
        {
            public readonly ManualResetEventSlim Resumed = new ManualResetEventSlim();
            public int BodyThread = -1, CallerThread = -1, Result;
            public Exception Failure;

            public void CallerResumed()
            {
                CallerThread = Environment.CurrentManagedThreadId;
                Resumed.Set();
            }
        }

        /// <summary>An await the test resumes itself: records the thread that awaited it.</summary>
        sealed class Gate : INotifyCompletion
        {
            readonly ManualResetEventSlim m_Registered = new ManualResetEventSlim();
            Action m_Continuation;
            public int AwaitedOn = -1;

            public Gate GetAwaiter() => this;
            public bool IsCompleted => false;
            public void GetResult() { }

            public void OnCompleted(Action continuation)
            {
                m_Continuation = continuation;
                AwaitedOn = Environment.CurrentManagedThreadId;
                m_Registered.Set();
            }

            public Action AwaitedContinuation()
            {
                Assert.True(m_Registered.Wait(Bound));
                return m_Continuation;
            }
        }

        static async Awaitable<int> FinishesAfter(Awaitable wait, Probe probe)
        {
            await wait;
            probe.BodyThread = Environment.CurrentManagedThreadId;
            return 5;
        }

        static async Awaitable CallsFinishesAfter(Awaitable wait, Probe probe)
        {
            probe.Result = await FinishesAfter(wait, probe);
            probe.CallerResumed();
        }

        static async Awaitable<int> SwitchesToBackground(Gate gate, Probe probe)
        {
            await Awaitable.BackgroundThreadAsync();
            probe.BodyThread = Environment.CurrentManagedThreadId;
            await gate;
            return 7;
        }

        static async Awaitable CallsSwitchesToBackground(Gate gate, Probe probe)
        {
            probe.Result = await SwitchesToBackground(gate, probe);
            probe.CallerResumed();
        }

        static async Awaitable ThrowsAfter(Awaitable wait)
        {
            await wait;
            throw new InvalidOperationException("thrown by the awaited method");
        }

        static async Awaitable Catches(Awaitable awaited, Probe probe)
        {
            try { await awaited; }
            catch (Exception e) { probe.Failure = e; }
            probe.CallerResumed();
        }

        static async Awaitable WaitsForSeconds(CancellationToken token) => await Awaitable.WaitForSecondsAsync(100f, token);

        static async Awaitable WaitsForNextFrame() => await Awaitable.NextFrameAsync();

        // Old Core: red at the first Assert.False(probe.Resumed.IsSet): the caller resumed inline
        // on the completing thread.
        [Fact]
        public void AMainThreadCallerResumesOnTheMainThreadWhenTheMethodEndsElsewhere()
        {
            int main = BecomeMainThread();
            var source = new AwaitableCompletionSource();
            var probe = new Probe();
            var caller = CallsFinishesAfter(source.Awaitable, probe);
            int other = OnOtherThread(() => source.SetResult());
            // A completion source's own awaiter goes on synchronously on the completing thread...
            Assert.Equal(other, probe.BodyThread);
            // ...but the method's main-thread caller waits for the main thread.
            Assert.False(probe.Resumed.IsSet);
            Assert.False(caller.IsCompleted);
            MainThreadContext.Instance.RunPending();
            Assert.True(probe.Resumed.IsSet);
            Assert.Equal(main, probe.CallerThread);
            Assert.Equal(5, probe.Result);
            Assert.True(caller.IsCompleted);
        }

        // The Manual's own example: the inner switch stays on the pool (guard), the caller comes
        // back to the main thread (old Core: red at Assert.False(probe.Resumed.IsSet)).
        [Fact]
        public void BackgroundThreadAsyncIsLocalToTheMethodThatAwaitsIt()
        {
            int main = BecomeMainThread();
            var gate = new Gate();
            var probe = new Probe();
            var caller = CallsSwitchesToBackground(gate, probe);
            var resume = gate.AwaitedContinuation();
            Assert.NotEqual(main, probe.BodyThread);
            Assert.Equal(probe.BodyThread, gate.AwaitedOn);
            Assert.False(probe.Resumed.IsSet);
            OnOtherThread(resume);
            Assert.False(probe.Resumed.IsSet);
            MainThreadContext.Instance.RunPending();
            Assert.True(probe.Resumed.IsSet);
            Assert.Equal(main, probe.CallerThread);
            Assert.Equal(7, probe.Result);
            Assert.True(caller.IsCompleted);
        }

        // Old Core: red at Assert.False(probe.Resumed.IsSet).
        [Fact]
        public void AnExceptionReachesTheMainThreadCaller()
        {
            int main = BecomeMainThread();
            var source = new AwaitableCompletionSource();
            var probe = new Probe();
            _ = Catches(ThrowsAfter(source.Awaitable), probe);
            OnOtherThread(() => source.SetResult());
            Assert.False(probe.Resumed.IsSet);
            MainThreadContext.Instance.RunPending();
            Assert.True(probe.Resumed.IsSet);
            Assert.Equal(main, probe.CallerThread);
            Assert.IsType<InvalidOperationException>(probe.Failure);
            Assert.Equal("thrown by the awaited method", probe.Failure.Message);
        }

        // Cancelling the method's Awaitable from another thread completes it there; the main-thread
        // caller still resumes on the main thread. Old Core: red at Assert.False(probe.Resumed.IsSet).
        [Fact]
        public void CancellingFromAnotherThreadResumesTheMainThreadCallerThere()
        {
            int main = BecomeMainThread();
            var source = new AwaitableCompletionSource();
            var probe = new Probe();
            var method = FinishesAfter(source.Awaitable, new Probe());
            _ = Catches(AwaitsResult(method), probe);
            OnOtherThread(() => method.Cancel());
            Assert.False(probe.Resumed.IsSet);
            MainThreadContext.Instance.RunPending();
            Assert.True(probe.Resumed.IsSet);
            Assert.Equal(main, probe.CallerThread);
            Assert.IsType<OperationCanceledException>(probe.Failure);
            // The cancelled method's own body may still finish; its result is dropped.
            source.SetResult();
            MainThreadContext.Instance.RunPending();
        }

        static async Awaitable AwaitsResult(Awaitable<int> awaited) => await awaited;

        // Guard (green on old and new Core): token cancellation already reaches the main thread
        // through WatchCancellation, so the whole chain completes there in one pass.
        [Fact]
        public void TokenCancellationFromAnotherThreadCompletesTheChainOnTheMainThread()
        {
            int main = BecomeMainThread();
            using var cts = new CancellationTokenSource();
            var probe = new Probe();
            _ = Catches(WaitsForSeconds(cts.Token), probe);
            OnOtherThread(() => cts.Cancel());
            Assert.False(probe.Resumed.IsSet);
            MainThreadContext.Instance.RunPending();
            Assert.True(probe.Resumed.IsSet);
            Assert.Equal(main, probe.CallerThread);
            Assert.IsType<OperationCanceledException>(probe.Failure);
        }

        // Guard (green on old and new Core): completed on the main thread, the caller resumes at
        // once, inside the scheduler pass; nothing is posted or delayed to a later frame.
        [Fact]
        public void CompletionOnTheMainThreadResumesTheCallerSynchronously()
        {
            int main = BecomeMainThread();
            var probe = new Probe();
            _ = Catches(WaitsForNextFrame(), probe);
            Time.s_FrameCount++;
            AwaitableScheduler.RunUpdate();
            Assert.True(probe.Resumed.IsSet);
            Assert.Equal(main, probe.CallerThread);
            Assert.Null(probe.Failure);
        }

        // Guard (green on old and new Core): a method that completes before it is awaited lets its
        // main-thread caller go on without waiting.
        [Fact]
        public void AnAlreadyCompletedMethodDoesNotSuspendItsCaller()
        {
            int main = BecomeMainThread();
            var source = new AwaitableCompletionSource();
            source.SetResult();
            var probe = new Probe();
            var caller = CallsFinishesAfter(source.Awaitable, probe);
            Assert.True(probe.Resumed.IsSet);
            Assert.True(caller.IsCompleted);
            Assert.Equal(main, probe.BodyThread);
            Assert.Equal(main, probe.CallerThread);
            Assert.Equal(5, probe.Result);
        }

        // Guard (green on old and new Core): custom completion is not marshalled. A direct awaiter of
        // a completion source completed on another thread resumes synchronously on that thread.
        [Fact]
        public void ACompletionSourceResumesItsAwaiterOnTheCompletingThread()
        {
            BecomeMainThread();
            var source = new AwaitableCompletionSource<int>();
            int resumedOn = -1, result = 0;
            var awaited = source.Awaitable;
            awaited.GetAwaiter().OnCompleted(() =>
            {
                resumedOn = Environment.CurrentManagedThreadId;
                result = awaited.GetAwaiter().GetResult();
            });
            int other = OnOtherThread(() => source.SetResult(3));
            Assert.Equal(other, resumedOn);
            Assert.Equal(3, result);
        }

        // Guard (green on old and new Core): a method called from another thread is not sent to the
        // main thread. Its caller resumes off the main thread without a main-thread pass. Whether
        // that is the completing thread or a pool thread is not asserted.
        [Fact]
        public void AMethodCalledFromAnotherThreadDoesNotResumeOnTheMainThread()
        {
            int main = BecomeMainThread();
            var source = new AwaitableCompletionSource();
            var probe = new Probe();
            OnOtherThread(() => _ = CallsFinishesAfter(source.Awaitable, probe));
            OnOtherThread(() => source.SetResult());
            Assert.True(probe.Resumed.Wait(Bound));
            Assert.NotEqual(main, probe.CallerThread);
            Assert.Equal(5, probe.Result);
        }
    }
}
