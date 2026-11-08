using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>
    /// Unity 6's awaitable: a one-shot completion that game code awaits (or yields from a
    /// coroutine). The engine completes the frame-based ones at the player loop points Unity uses:
    /// NextFrame and WaitForSeconds after Update (with `yield return null`), FixedUpdate after the
    /// fixed update scripts, EndOfFrame with WaitForEndOfFrame. Continuations run synchronously on
    /// the thread that completes the awaitable, except for the awaitable of an `async Awaitable`
    /// method: its caller resumes on the main thread when the method was called there, and otherwise
    /// off the main thread (see StartedOnCurrentThread and RunOrScheduleContinuation). Such an
    /// awaitable also counts as completed, and so is awaited without suspending, only on a thread of
    /// that kind (see IsCompleted and SetContinuation).
    /// </summary>
    [AsyncMethodBuilder(typeof(Awaitable.AwaitableAsyncMethodBuilder))]
    public partial class Awaitable : IEnumerator
    {
        readonly object m_Lock = new object();
        Action m_Continuation;
        bool m_Completed;
        ExceptionDispatchInfo m_Exception;
        CancellationTokenRegistration m_Cancellation;
        AwaiterCompletionThreadAffinity m_Affinity;

        internal Awaitable() { }

        internal enum AwaiterCompletionThreadAffinity
        {
            None = 0,
            MainThread = 1,
            BackgroundThread = 2,
        }

        /// <summary>
        /// Called by the builder before an `async Awaitable` method's body runs. Unity 6000.0 Manual,
        /// "Awaitable completion and continuation": "If the method is called from the main thread, it
        /// resumes on the main thread. Otherwise it resumes on a .NET ThreadPool thread."; the effect
        /// of MainThreadAsync and BackgroundThreadAsync is "local to the current method only". So the
        /// calling thread, not the thread the body ends on, decides where the method's awaiter
        /// resumes: MainThread when called on the main thread, BackgroundThread otherwise (a pool
        /// thread or a thread the game started). Only this builder path sets an affinity; frame waits,
        /// completion sources and AsyncOperation awaitables keep None. Before the player loop boots
        /// every thread counts as the main thread, so every method gets MainThread, every thread
        /// matches it and nothing is ever moved then.
        /// </summary>
        internal void StartedOnCurrentThread()
        {
            var affinity = PlayerLoopDriver.IsMainThread
                ? AwaiterCompletionThreadAffinity.MainThread
                : AwaiterCompletionThreadAffinity.BackgroundThread;
            lock (m_Lock) m_Affinity = affinity;
        }

        /// <summary>
        /// Completed and ready to be continued synchronously on the calling thread: false on a thread
        /// that does not suit the affinity even after the awaitable has completed (an `async
        /// Awaitable` method called on the main thread, asked off it; one called off the main thread,
        /// asked on it). The awaiter's IsCompleted is this one, so an await on such a thread does not
        /// go on at once: it registers its continuation, which SetContinuation then moves to a
        /// suitable thread. Unity's public 6000.0.71f1 C# reference source (UnityCsReference 78d4b97c,
        /// Runtime/Export/Scripting/Awaitable.cs) returns IsCompletedNoLock here, "_managedAwaitableDone
        /// && MatchCompletionThreadAffinity(_completionThreadAffinity)"; that is implementation
        /// evidence for this release, not a documented guarantee. For None (frame waits, completion
        /// sources, AsyncOperation awaitables) and on a thread that suits the affinity it equals
        /// IsLogicallyCompleted.
        /// </summary>
        public bool IsCompleted
        {
            get { lock (m_Lock) return m_Completed && MatchesCurrentThread(m_Affinity); }
        }

        /// <summary>Completed (with a result, an exception or a cancellation), whatever the calling
        /// thread. Engine code that asks whether completion has happened, rather than whether to
        /// continue synchronously here, uses this one (the reference source's IsLogicallyCompletedNoLock).
        /// </summary>
        internal bool IsLogicallyCompleted
        {
            get { lock (m_Lock) return m_Completed; }
        }

        public Awaiter GetAwaiter() => new Awaiter(this);

        public void Cancel() => Complete(ExceptionDispatchInfo.Capture(new OperationCanceledException()));

        internal void Complete(ExceptionDispatchInfo exception = null)
        {
            Action continuation;
            AwaiterCompletionThreadAffinity affinity;
            lock (m_Lock)
            {
                if (m_Completed) return;
                m_Completed = true;
                m_Exception = exception;
                continuation = m_Continuation;
                m_Continuation = null;
                affinity = m_Affinity;
            }
            m_Cancellation.Dispose();
            // Only frame waits enter the scheduler lists; builder awaitables have an affinity.
            if (affinity == AwaiterCompletionThreadAffinity.None)
                AwaitableScheduler.Forget(this);
            if (continuation == null) return;
            RunOrScheduleContinuation(affinity, continuation);
        }

        /// <summary>
        /// Runs a continuation now when this thread suits the affinity ("runs synchronously when
        /// completion is triggered", Manual "Introduction to Awaitable"), otherwise moves it there.
        /// None suits every thread. MainThread suits the main thread; off it the continuation goes
        /// through the main thread's SynchronizationContext (MainThreadContext, drained at
        /// Update.ScriptRunDelayedTasks and in paused/edit-mode frames), the same context
        /// MainThreadAsync posts to. BackgroundThread suits every thread but the main one, a pool
        /// thread or a thread the game started alike, and runs inline there; only on the main thread
        /// is the continuation queued to the pool, as a task, so that an exception thrown by it stays
        /// in that task instead of ending the process.
        ///
        /// Two sources are distinguished here. The Manual ("Awaitable completion and continuation")
        /// states the default dispatch: a method called off the main thread "resumes on a .NET
        /// ThreadPool thread". Unity's public 6000.0.71f1 C# reference source (UnityCsReference
        /// 78d4b97c, Awaitable.cs MatchCompletionThreadAffinity / RunOrScheduleContinuation) adds the
        /// affinity optimization followed here: BackgroundThread matches "ManagedThreadId !=
        /// _mainThreadId", so completion on any other thread continues inline without a hop, and
        /// Task.Run is the fallback when completion happens on the main thread. That is
        /// implementation evidence for this release, not a promise that every off-main completion
        /// moves to a pool thread; it is unmeasured against the Editor. Used both by completion and
        /// by a registration on an awaitable that has already completed (SetContinuation).
        /// </summary>
        static void RunOrScheduleContinuation(AwaiterCompletionThreadAffinity affinity, Action continuation)
        {
            if (MatchesCurrentThread(affinity))
                continuation();
            else if (affinity == AwaiterCompletionThreadAffinity.MainThread)
                PlayerLoopDriver.RunOnMainThread(continuation); // off the main thread: posted to it
            else
                System.Threading.Tasks.Task.Run(continuation); // BackgroundThread on the main thread
        }

        /// <summary>Whether the calling thread suits an affinity (the reference source's
        /// MatchCompletionThreadAffinity). Before the player loop boots every thread is the main
        /// thread (PlayerLoopDriver.IsMainThread), so MainThread, the only affinity given then,
        /// matches everywhere.</summary>
        static bool MatchesCurrentThread(AwaiterCompletionThreadAffinity affinity) => affinity switch
        {
            AwaiterCompletionThreadAffinity.MainThread => PlayerLoopDriver.IsMainThread,
            AwaiterCompletionThreadAffinity.BackgroundThread => !PlayerLoopDriver.IsMainThread,
            _ => true,
        };

        /// <summary>
        /// Registers the awaiter's continuation. While the awaitable has not completed it waits for
        /// Complete, which dispatches it by the affinity. When the awaitable has already logically
        /// completed (the awaiter's IsCompleted was false only because this thread does not suit the
        /// affinity, or completion raced this registration) the continuation goes through the same
        /// affinity-aware dispatch now: inline on a suitable thread, otherwise posted to the main
        /// thread or queued to the pool. Unity's public 6000.0.71f1 C# reference source does the
        /// same (SetContinuation: IsLogicallyCompletedNoLock, then RunOrScheduleContinuation with
        /// the captured affinity). The Manual states its rule for the method's caller; an await of
        /// a completed method's awaitable from another kind of thread follows this source evidence.
        /// For None it runs inline on the registering thread, as before.
        /// </summary>
        internal void SetContinuation(Action continuation)
        {
            bool done;
            AwaiterCompletionThreadAffinity affinity;
            lock (m_Lock)
            {
                done = m_Completed;
                affinity = m_Affinity;
                if (!done) m_Continuation += continuation;
            }
            if (done) RunOrScheduleContinuation(affinity, continuation);
        }

        internal void ThrowIfFailed() => m_Exception?.Throw();

        internal void WatchCancellation(CancellationToken token)
        {
            if (!token.CanBeCanceled) return;
            if (token.IsCancellationRequested)
            {
                Cancel();
                return;
            }
            m_Cancellation = token.Register(() => PlayerLoopDriver.RunOnMainThread(Cancel));
        }

        public static Awaitable NextFrameAsync(CancellationToken cancellationToken = default)
        {
            var awaitable = new Awaitable();
            AwaitableScheduler.NextFrame(awaitable, Time.s_FrameCount);
            awaitable.WatchCancellation(cancellationToken);
            return awaitable;
        }

        public static Awaitable WaitForSecondsAsync(float seconds, CancellationToken cancellationToken = default)
        {
            var awaitable = new Awaitable();
            AwaitableScheduler.AtTime(awaitable, Time.TimeAsDoubleNoCheck + seconds);
            awaitable.WatchCancellation(cancellationToken);
            return awaitable;
        }

        public static Awaitable FixedUpdateAsync(CancellationToken cancellationToken = default)
        {
            var awaitable = new Awaitable();
            AwaitableScheduler.FixedUpdate(awaitable);
            awaitable.WatchCancellation(cancellationToken);
            return awaitable;
        }

        public static Awaitable EndOfFrameAsync(CancellationToken cancellationToken = default)
        {
            var awaitable = new Awaitable();
            AwaitableScheduler.EndOfFrame(awaitable);
            awaitable.WatchCancellation(cancellationToken);
            return awaitable;
        }

        public static Awaitable FromAsyncOperation(AsyncOperation op, CancellationToken cancellationToken = default)
        {
            var awaitable = new Awaitable();
            if (op == null) awaitable.Complete();
            else op.completed += _ => awaitable.Complete();
            awaitable.WatchCancellation(cancellationToken);
            return awaitable;
        }

        public static MainThreadAwaitable MainThreadAsync() =>
            new MainThreadAwaitable(MainThreadContext.Instance, PlayerLoopDriver.MainThreadId);

        public static BackgroundThreadAwaitable BackgroundThreadAsync() =>
            new BackgroundThreadAwaitable(MainThreadContext.Instance, PlayerLoopDriver.MainThreadId);

        /// <summary>A coroutine yielding the awaitable waits until it has logically completed. Held,
        /// not changed: the reference source's MoveNext asks the affinity-aware IsCompleted, which
        /// on the main thread stays false for a completed method called off it; how Unity's
        /// coroutine scheduler treats a yielded Awaitable is not visible there and is unmeasured.
        /// </summary>
        bool IEnumerator.MoveNext() => !IsLogicallyCompleted;

        void IEnumerator.Reset() => throw new NotSupportedException();

        object IEnumerator.Current => null;

        public partial struct Awaiter : INotifyCompletion
        {
            readonly Awaitable m_Awaited;

            internal Awaiter(Awaitable awaited) { m_Awaited = awaited; }

            /// <summary>Awaitable.IsCompleted: completed and suiting the calling thread.</summary>
            public bool IsCompleted => m_Awaited.IsCompleted;

            public void OnCompleted(Action continuation) => m_Awaited.SetContinuation(continuation);

            public void GetResult() => m_Awaited.ThrowIfFailed();
        }

        /// <summary>Builds the Awaitable of an `async Awaitable` method.</summary>
        public partial struct AwaitableAsyncMethodBuilder
        {
            Awaitable m_Task;
            IAsyncStateMachine m_Machine;

            public static AwaitableAsyncMethodBuilder Create() => new AwaitableAsyncMethodBuilder { m_Task = new Awaitable() };

            public Awaitable Task => m_Task ??= new Awaitable();

            /// <summary>Runs the method's body up to its first pending await. The calling thread
            /// decides where the method's own awaiter resumes (see Awaitable.StartedOnCurrentThread);
            /// awaits inside the body keep their own behaviour.</summary>
            public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
            {
                Task.StartedOnCurrentThread();
                stateMachine.MoveNext();
            }

            public void SetStateMachine(IAsyncStateMachine stateMachine) => m_Machine = stateMachine;

            public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
                where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
            {
                var machine = Box(ref stateMachine);
                awaiter.OnCompleted(machine.MoveNext);
            }

            public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
                where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
            {
                var machine = Box(ref stateMachine);
                awaiter.UnsafeOnCompleted(machine.MoveNext);
            }

            IAsyncStateMachine Box<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
            {
                _ = Task;
                if (m_Machine == null)
                {
                    // Boxing copies the machine (and this builder in it): the box carries on from here.
                    m_Machine = stateMachine;
                    m_Machine.SetStateMachine(m_Machine);
                }
                return m_Machine;
            }

            public void SetException(Exception e) => Task.Complete(ExceptionDispatchInfo.Capture(e));

            public void SetResult() => Task.Complete();
        }

        public partial struct AwaitableAsyncMethodBuilder<T>
        {
            Awaitable<T> m_Task;
            IAsyncStateMachine m_Machine;

            public static AwaitableAsyncMethodBuilder<T> Create() => new AwaitableAsyncMethodBuilder<T> { m_Task = new Awaitable<T>() };

            public Awaitable<T> Task => m_Task ??= new Awaitable<T>();

            /// <summary>As the non-generic builder's Start: the result's awaiter resumes on the main
            /// thread when the method was called there, otherwise on a thread pool thread.</summary>
            public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
            {
                Task.Inner.StartedOnCurrentThread();
                stateMachine.MoveNext();
            }

            public void SetStateMachine(IAsyncStateMachine stateMachine) => m_Machine = stateMachine;

            public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
                where TAwaiter : INotifyCompletion where TStateMachine : IAsyncStateMachine
            {
                var machine = Box(ref stateMachine);
                awaiter.OnCompleted(machine.MoveNext);
            }

            public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
                where TAwaiter : ICriticalNotifyCompletion where TStateMachine : IAsyncStateMachine
            {
                var machine = Box(ref stateMachine);
                awaiter.UnsafeOnCompleted(machine.MoveNext);
            }

            IAsyncStateMachine Box<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine
            {
                _ = Task;
                if (m_Machine == null)
                {
                    m_Machine = stateMachine;
                    m_Machine.SetStateMachine(m_Machine);
                }
                return m_Machine;
            }

            public void SetResult(T value) => Task.SetResult(value);

            public void SetException(Exception e) => Task.SetException(e);
        }
    }

    [AsyncMethodBuilder(typeof(Awaitable.AwaitableAsyncMethodBuilder<>))]
    public partial class Awaitable<T>
    {
        readonly Awaitable m_Inner = new Awaitable();
        T m_Result;
        int m_Consumed;

        internal Awaitable() { }

        internal void SetResult(T value)
        {
            m_Result = value;
            m_Inner.Complete();
        }

        internal void SetException(Exception exception) => m_Inner.Complete(ExceptionDispatchInfo.Capture(exception));

        internal Awaitable Inner => m_Inner;

        public void Cancel() => m_Inner.Cancel();

        public Awaiter GetAwaiter() => new Awaiter(this);

        public partial struct Awaiter : INotifyCompletion
        {
            readonly Awaitable<T> m_Awaited;

            public Awaiter(Awaitable<T> coroutine) { m_Awaited = coroutine; }

            /// <summary>As Awaitable.IsCompleted: completed and suiting the calling thread.</summary>
            public bool IsCompleted => m_Awaited.m_Inner.IsCompleted;

            public void OnCompleted(Action continuation) => m_Awaited.m_Inner.SetContinuation(continuation);

            public T GetResult()
            {
                if (Interlocked.Exchange(ref m_Awaited.m_Consumed, 1) != 0)
                    throw new NullReferenceException("Object reference not set to an instance of an object");
                try
                {
                    m_Awaited.m_Inner.ThrowIfFailed();
                    return m_Awaited.m_Result;
                }
                finally { m_Awaited.m_Result = default; }
            }
        }
    }

    /// <summary>Completes an Awaitable from game code.</summary>
    public partial class AwaitableCompletionSource
    {
        Awaitable m_Awaitable = new Awaitable();

        public AwaitableCompletionSource() { }

        public Awaitable Awaitable => m_Awaitable;

        public void SetResult()
        {
            if (!TrySetResult()) throw new InvalidOperationException("Can't raise completion of the same Awaitable twice");
        }

        public void SetCanceled()
        {
            if (!TrySetCanceled()) throw new InvalidOperationException("Can't raise completion of the same Awaitable twice");
        }

        public void SetException(Exception exception)
        {
            if (!TrySetException(exception)) throw new InvalidOperationException("Can't raise completion of the same Awaitable twice");
        }

        public bool TrySetResult()
        {
            if (m_Awaitable.IsLogicallyCompleted) return false;
            m_Awaitable.Complete();
            return true;
        }

        public bool TrySetCanceled()
        {
            if (m_Awaitable.IsLogicallyCompleted) return false;
            m_Awaitable.Cancel();
            return true;
        }

        public bool TrySetException(Exception exception)
        {
            if (m_Awaitable.IsLogicallyCompleted) return false;
            m_Awaitable.Complete(ExceptionDispatchInfo.Capture(exception));
            return true;
        }

        public void Reset() => m_Awaitable = new Awaitable();
    }

    public partial class AwaitableCompletionSource<T>
    {
        Awaitable<T> m_Awaitable = new Awaitable<T>();

        public AwaitableCompletionSource() { }

        public Awaitable<T> Awaitable => m_Awaitable;

        public void SetResult(in T value)
        {
            if (!TrySetResult(value)) throw new InvalidOperationException("Can't raise completion of the same Awaitable twice");
        }

        public void SetCanceled()
        {
            if (!TrySetCanceled()) throw new InvalidOperationException("Can't raise completion of the same Awaitable twice");
        }

        public void SetException(Exception exception)
        {
            if (!TrySetException(exception)) throw new InvalidOperationException("Can't raise completion of the same Awaitable twice");
        }

        public bool TrySetResult(in T value)
        {
            if (m_Awaitable.Inner.IsLogicallyCompleted) return false;
            m_Awaitable.SetResult(value);
            return true;
        }

        public bool TrySetCanceled()
        {
            if (m_Awaitable.Inner.IsLogicallyCompleted) return false;
            m_Awaitable.Cancel();
            return true;
        }

        public bool TrySetException(Exception exception)
        {
            if (m_Awaitable.Inner.IsLogicallyCompleted) return false;
            m_Awaitable.SetException(exception);
            return true;
        }

        public void Reset() => m_Awaitable = new Awaitable<T>();
    }

    public static partial class AsyncOperationAwaitableExtensions
    {
        public static Awaitable.Awaiter GetAwaiter(this AsyncOperation op) => Awaitable.FromAsyncOperation(op).GetAwaiter();
    }

    /// <summary>Continues on the main thread: at once when already there, otherwise in the frame.</summary>
    public partial struct MainThreadAwaitable : INotifyCompletion
    {
        readonly SynchronizationContext m_Context;
        readonly int m_MainThreadId;

        internal MainThreadAwaitable(SynchronizationContext syncContext, int mainThreadId)
        {
            m_Context = syncContext;
            m_MainThreadId = mainThreadId;
        }

        public MainThreadAwaitable GetAwaiter() => this;

        public bool IsCompleted => Thread.CurrentThread.ManagedThreadId == m_MainThreadId;

        public void OnCompleted(Action continuation) => m_Context.Post(_ => continuation(), null);

        [Unbound.EditorFaithful("a thread switch has no result: Unity's GetResult does nothing too")]
        public void GetResult() { }
    }

    /// <summary>Continues on a thread pool thread.</summary>
    public partial struct BackgroundThreadAwaitable : INotifyCompletion
    {
        readonly int m_MainThreadId;

        internal BackgroundThreadAwaitable(SynchronizationContext syncContext, int mainThreadId) { m_MainThreadId = mainThreadId; }

        public BackgroundThreadAwaitable GetAwaiter() => this;

        public bool IsCompleted => Thread.CurrentThread.ManagedThreadId != m_MainThreadId;

        public void OnCompleted(Action continuation) => ThreadPool.UnsafeQueueUserWorkItem(_ => continuation(), null);

        [Unbound.EditorFaithful("a thread switch has no result: Unity's GetResult does nothing too")]
        public void GetResult() { }
    }
}

namespace Unbound.Runtime
{
    using UnityEngine;

    /// <summary>The frame-based Awaitables waiting for their player loop point.</summary>
    internal static class AwaitableScheduler
    {
        static readonly List<(Awaitable Awaitable, int Frame)> s_NextFrame = new List<(Awaitable, int)>();
        static readonly List<(Awaitable Awaitable, double Time)> s_Timed = new List<(Awaitable, double)>();
        static readonly List<Awaitable> s_FixedUpdate = new List<Awaitable>();
        static readonly List<Awaitable> s_EndOfFrame = new List<Awaitable>();
        static bool s_Registered;

        static void EnsureRegistered()
        {
            if (s_Registered) return;
            s_Registered = true;
            PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.Update.ScriptRunDelayedDynamicFrameRate), RunUpdate);
            PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.FixedUpdate.ScriptRunDelayedFixedFrameRate), RunFixedUpdate);
            PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.PostLateUpdate.TriggerEndOfFrameCallbacks), RunLegacyEndOfFrame);
        }

        internal static void NextFrame(Awaitable awaitable, int frame) { EnsureRegistered(); s_NextFrame.Add((awaitable, frame)); }
        internal static void AtTime(Awaitable awaitable, double time) { EnsureRegistered(); s_Timed.Add((awaitable, time)); }
        internal static void FixedUpdate(Awaitable awaitable) { EnsureRegistered(); s_FixedUpdate.Add(awaitable); }
        internal static void EndOfFrame(Awaitable awaitable) { EnsureRegistered(); s_EndOfFrame.Add(awaitable); }

        /// <summary>A completed or cancelled awaitable leaves the waiting lists.</summary>
        internal static void Forget(Awaitable awaitable)
        {
            s_NextFrame.RemoveAll(e => e.Awaitable == awaitable);
            s_Timed.RemoveAll(e => e.Awaitable == awaitable);
            s_FixedUpdate.Remove(awaitable);
            s_EndOfFrame.Remove(awaitable);
        }

        internal static void RunUpdate()
        {
            var due = new List<Awaitable>();
            foreach (var (a, frame) in s_NextFrame)
                if (frame < Time.frameCount) due.Add(a);
            foreach (var (a, time) in s_Timed)
                if (time <= Time.timeAsDouble) due.Add(a);
            Complete(due);
        }

        internal static void RunFixedUpdate() => Complete(new List<Awaitable>(s_FixedUpdate));

        static void RunLegacyEndOfFrame()
        {
            if (!Compat.RenderingAfterPublicLoop) RunEndOfFrame();
        }

        internal static void RunEndOfFrame()
        {
            if (s_EndOfFrame.Count == 0) return;
            var due = Playables.Scratch<Awaitable>.Rent(s_EndOfFrame);
            try { Complete(due); }
            finally { Playables.Scratch<Awaitable>.Return(due); }
        }

        static void Complete(List<Awaitable> due)
        {
            foreach (var a in due)
            {
                try { a.Complete(); }
                catch (Exception e) { Debug.LogException(e); }
            }
        }
    }
}
