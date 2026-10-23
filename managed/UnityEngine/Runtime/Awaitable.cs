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
    /// fixed update scripts, EndOfFrame with WaitForEndOfFrame. Continuations of main-thread
    /// awaitables run synchronously when they complete.
    /// </summary>
    [AsyncMethodBuilder(typeof(Awaitable.AwaitableAsyncMethodBuilder))]
    public partial class Awaitable : IEnumerator
    {
        readonly object m_Lock = new object();
        Action m_Continuation;
        bool m_Completed;
        ExceptionDispatchInfo m_Exception;
        CancellationTokenRegistration m_Cancellation;

        internal Awaitable() { }

        internal enum AwaiterCompletionThreadAffinity
        {
            None = 0,
            MainThread = 1,
            BackgroundThread = 2,
        }

        public bool IsCompleted
        {
            get { lock (m_Lock) return m_Completed; }
        }

        public Awaiter GetAwaiter() => new Awaiter(this);

        public void Cancel() => Complete(ExceptionDispatchInfo.Capture(new OperationCanceledException()));

        internal void Complete(ExceptionDispatchInfo exception = null)
        {
            Action continuation;
            lock (m_Lock)
            {
                if (m_Completed) return;
                m_Completed = true;
                m_Exception = exception;
                continuation = m_Continuation;
                m_Continuation = null;
            }
            m_Cancellation.Dispose();
            AwaitableScheduler.Forget(this);
            continuation?.Invoke();
        }

        internal void SetContinuation(Action continuation)
        {
            bool runNow;
            lock (m_Lock)
            {
                runNow = m_Completed;
                if (!runNow) m_Continuation += continuation;
            }
            if (runNow) continuation();
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
            AwaitableScheduler.NextFrame(awaitable, Time.frameCount);
            awaitable.WatchCancellation(cancellationToken);
            return awaitable;
        }

        public static Awaitable WaitForSecondsAsync(float seconds, CancellationToken cancellationToken = default)
        {
            var awaitable = new Awaitable();
            AwaitableScheduler.AtTime(awaitable, Time.timeAsDouble + seconds);
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

        bool IEnumerator.MoveNext() => !IsCompleted;

        void IEnumerator.Reset() => throw new NotSupportedException();

        object IEnumerator.Current => null;

        public partial struct Awaiter : INotifyCompletion
        {
            readonly Awaitable m_Awaited;

            internal Awaiter(Awaitable awaited) { m_Awaited = awaited; }

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

            public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine => stateMachine.MoveNext();

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

            public void Start<TStateMachine>(ref TStateMachine stateMachine) where TStateMachine : IAsyncStateMachine => stateMachine.MoveNext();

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

            public bool IsCompleted => m_Awaited.m_Inner.IsCompleted;

            public void OnCompleted(Action continuation) => m_Awaited.m_Inner.SetContinuation(continuation);

            public T GetResult()
            {
                m_Awaited.m_Inner.ThrowIfFailed();
                return m_Awaited.m_Result;
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
            if (m_Awaitable.IsCompleted) return false;
            m_Awaitable.Complete();
            return true;
        }

        public bool TrySetCanceled()
        {
            if (m_Awaitable.IsCompleted) return false;
            m_Awaitable.Cancel();
            return true;
        }

        public bool TrySetException(Exception exception)
        {
            if (m_Awaitable.IsCompleted) return false;
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
            if (m_Awaitable.Inner.IsCompleted) return false;
            m_Awaitable.SetResult(value);
            return true;
        }

        public bool TrySetCanceled()
        {
            if (m_Awaitable.Inner.IsCompleted) return false;
            m_Awaitable.Cancel();
            return true;
        }

        public bool TrySetException(Exception exception)
        {
            if (m_Awaitable.Inner.IsCompleted) return false;
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
            PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.PostLateUpdate.TriggerEndOfFrameCallbacks), RunEndOfFrame);
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

        internal static void RunEndOfFrame() => Complete(new List<Awaitable>(s_EndOfFrame));

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
