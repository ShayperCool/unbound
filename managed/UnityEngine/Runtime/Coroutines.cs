using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>
    /// Coroutines with Unity's timing: a routine runs at once until its first yield; `null` and
    /// unknown values resume after the next Update; WaitForSeconds as <see cref="FrameTimedWait"/>
    /// says (checked after Update); WaitForFixedUpdate after the physics step; WaitForEndOfFrame after rendering; a
    /// Coroutine or nested IEnumerator when it finishes, in the next frame at the earliest when it
    /// ends without waiting; AsyncOperation when done (the first routine to yield a running one
    /// as it completes, the others after the next Update); a CustomYieldInstruction when keepWaiting
    /// turns false, checked when yielded and then each frame, in the next frame at the earliest. Routines stop when their behaviour is
    /// destroyed or its GameObject deactivated, not when the behaviour is disabled.
    /// </summary>
    internal static class Coroutines
    {
        internal enum Phase
        {
            Update,
            FixedUpdate,
            EndOfFrame,
        }

        static readonly List<Coroutine> s_Running = new List<Coroutine>();
        // Reused by Run (the player loop's coroutine points, never nested).
        static readonly List<Coroutine> s_Scan = new List<Coroutine>();
        static readonly List<Coroutine> s_Due = new List<Coroutine>();

        internal static Coroutine Start(MonoBehaviour owner, IEnumerator routine, string methodName)
        {
            var c = new Coroutine { Owner = owner, MethodName = methodName, Root = routine };
            c.Stack.Push(routine);
            s_Running.Add(c);
            Step(c);
            return c;
        }

        internal static void Stop(MonoBehaviour owner, IEnumerator routine)
        {
            foreach (var c in s_Running)
                if (c.Owner == (object)owner && c.Stack.Count > 0 && c.Root == routine)
                    c.Finish();
        }

        internal static void Stop(MonoBehaviour owner, Coroutine routine)
        {
            if (routine.Owner == (object)owner) routine.Finish();
        }

        internal static void StopByName(MonoBehaviour owner, string methodName)
        {
            foreach (var c in s_Running)
                if (c.Owner == (object)owner && c.MethodName == methodName)
                    c.Finish();
        }

        internal static void StopAll(MonoBehaviour owner)
        {
            foreach (var c in s_Running)
                if (c.Owner == (object)owner)
                    c.Finish();
        }

        /// <summary>Advances the routine until it yields something to wait for, or ends.</summary>
        static void Step(Coroutine c)
        {
            // Nested routines entered during this step: one that ends before ever waiting lets its
            // parent go on only in the next frame (measured: an IEnumerator that ends at once, a
            // finished WaitUntil).
            HashSet<IEnumerator> enteredNow = null;
            while (!c.Done)
            {
                if (c.Owner.m_Destroyed)
                {
                    c.Finish();
                    return;
                }
                var top = c.Stack.Peek();
                bool moved;
                try
                {
                    moved = top.MoveNext();
                }
                catch (Exception e)
                {
                    Debug.LogException(e, c.Owner);
                    c.Finish();
                    return;
                }
                // The routine may have stopped itself (StopCoroutine, StopAllCoroutines, Destroy).
                if (c.Done) return;
                if (!moved)
                {
                    c.Stack.Pop();
                    if (c.Stack.Count == 0)
                    {
                        c.Finish();
                        return;
                    }
                    if (enteredNow != null && enteredNow.Contains(top))
                    {
                        c.Wait(null);
                        return;
                    }
                    continue; // the parent routine resumes at once
                }
                var current = top.Current;
                if (current is CustomYieldInstruction custom)
                {
                    // Checked at once, as the nested routine it is; then each frame.
                    bool waiting;
                    try { waiting = custom.keepWaiting; }
                    catch (Exception e) { Debug.LogException(e, c.Owner); waiting = false; }
                    c.Wait(waiting ? custom : null);
                    return;
                }
                if (current is IEnumerator nested)
                {
                    (enteredNow ??= new HashSet<IEnumerator>(ReferenceEqualityComparer.Instance)).Add(nested);
                    c.Stack.Push(nested);
                    continue;
                }
                c.Wait(current);
                return;
            }
        }

        /// <summary>Resumes routines whose wait is over, for the phase of the frame that just ran.</summary>
        /// <summary>Routines resume in the order they last began to wait (measured: one that
        /// yielded during Update goes before one that yielded later, in the coroutine phase).</summary>
        internal static void Run(Phase phase)
        {
            var due = s_Due;
            due.Clear();
            // A snapshot: a wait's check (keepWaiting) may complete an operation, whose waiting
            // routine then resumes and moves in s_Running.
            s_Scan.Clear();
            s_Scan.AddRange(s_Running);
            foreach (var c in s_Scan)
                if (!c.Done && c.ReadyIn(phase)) due.Add(c);
            s_Scan.Clear();
            foreach (var c in due)
            {
                if (c.Done) continue;
                Step(c);
                // Waiting again: it goes behind the routines already waiting.
                s_Running.Remove(c);
                if (!c.Done) s_Running.Add(c);
            }
            due.Clear();
            s_Running.RemoveAll(s_IsDone);
        }

        static readonly Predicate<Coroutine> s_IsDone = c => c.Done;

        /// <summary>A routine waiting for <paramref name="operation"/> resumes now, as it completes
        /// (wherever in the frame that is), and then waits behind the routines already waiting.</summary>
        internal static void Resume(Coroutine c, object operation)
        {
            if (c.Done || !c.IsWaitingFor(operation)) return;
            Step(c);
            s_Running.Remove(c);
            if (!c.Done) s_Running.Add(c);
        }

        internal static void Reset() => s_Running.Clear();
    }

    /// <summary>
    /// A wait of scaled seconds: a coroutine's WaitForSeconds and Awaitable.WaitForSecondsAsync. It
    /// counts from the time of the frame it starts in, also when it starts in a fixed step (where
    /// Time.time reads the fixed clock), and it is over at the first check in a later frame whose time
    /// has reached that start plus the delay. So no such wait ends in the frame that started it, not
    /// even a zero or negative one. Unity 6000.0 documents for WaitForSeconds that the coroutine
    /// continues "on the first frame after 't' seconds has passed"; the start point is measured
    /// (6000.0.71f1, tests/projects/waitforseconds: at 0.125 s per frame, 0 and -1 s resume one
    /// frame later and 0.3 s three frames later wherever in the frame they start; a 0.2 s wait
    /// started in a 250 ms frame resumes in the next frame, so a long frame does not move the start).
    /// </summary>
    internal readonly struct FrameTimedWait
    {
        readonly int m_Frame;
        readonly double m_Until;

        FrameTimedWait(int frame, double until)
        {
            m_Frame = frame;
            m_Until = until;
        }

        /// <summary>Starts now; safe off the main thread (an awaitable may be created there).</summary>
        internal static FrameTimedWait Start(float seconds) => new FrameTimedWait(Time.s_FrameCount, Time.s_Time + seconds);

        internal bool IsOver => Time.s_FrameCount > m_Frame && Time.s_Time >= m_Until;
    }

    /// <summary>Invoke and InvokeRepeating. The measured release schedules from the earlier of
    /// the fixed and frame clocks, checks the current clock after each fixed step, Update and
    /// LateUpdate, and retains the original cadence of late repeats
    /// (6000.0.71f1, tests/projects/invokesteps).</summary>
    internal static class Invokes
    {
        sealed class Entry
        {
            internal MonoBehaviour Target;
            internal string Method;
            internal double Time;
            internal float Repeat;
            internal bool Cancelled;
            internal long CreatedCheck;
            internal long Sequence;
        }

        static readonly List<Entry> s_Entries = new List<Entry>();
        static readonly PriorityQueue<Entry, (double Time, long Sequence)> s_Due = new PriorityQueue<Entry, (double, long)>();
        static long s_Check, s_Sequence;

        static (double Time, long Sequence) Priority(Entry entry) => (entry.Time, entry.Sequence);

        internal static void Schedule(MonoBehaviour target, string method, float delay, float repeat)
        {
            var entry = new Entry
            {
                Target = target,
                Method = method,
                Time = (Compat.InvokeTimingMeasured ? Math.Min(UnityEngine.Time.s_Time, UnityEngine.Time.s_FixedTime)
                    : UnityEngine.Time.TimeAsDoubleNoCheck) + Math.Max(0f, delay),
                Repeat = repeat,
                CreatedCheck = s_Check,
                Sequence = ++s_Sequence,
            };
            s_Entries.Add(entry);
            // Reserve when scheduling, so repeat delivery and rebuilding the queue do not allocate.
            s_Due.EnsureCapacity(s_Entries.Count);
            s_Due.Enqueue(entry, Priority(entry));
        }

        internal static void Cancel(MonoBehaviour target, string method)
        {
            foreach (var e in s_Entries)
                if (e.Target == (object)target && (method == null || e.Method == method))
                    e.Cancelled = true;
        }

        internal static bool IsInvoking(MonoBehaviour target, string method)
        {
            foreach (var e in s_Entries)
                if (!e.Cancelled && e.Target == (object)target && (method == null || e.Method == method))
                    return true;
            return false;
        }

        internal static void Run()
        {
            double now = UnityEngine.Time.TimeAsDoubleNoCheck;
            bool measured = Compat.InvokeTimingMeasured;
            long check = ++s_Check;
            while (s_Due.TryPeek(out var e, out var priority) && !(priority.Time > now))
            {
                s_Due.Dequeue();
                if (e.Cancelled || e.CreatedCheck == check) continue;
                if (e.Target.m_Destroyed) { e.Cancelled = true; continue; }
                // Delivery walks the due calls in deadline order. A repeat inserted behind the
                // next pending call can be visited again; one inserted before it waits for the
                // next check. With a single late repeat this gives one call per check.
                bool hasNext = s_Due.TryPeek(out _, out var next);
                if (measured)
                {
                    if (e.Repeat > 0f)
                    {
                        e.Time += e.Repeat;
                        e.Sequence = ++s_Sequence;
                        if (hasNext && Comparer<(double, long)>.Default.Compare(Priority(e), next) >= 0)
                            s_Due.Enqueue(e, Priority(e));
                    }
                    else e.Cancelled = true;
                }
                if (e.Target.m_GameObject.activeInHierarchy)
                {
                    if (!Messaging.TryCall(e.Target, e.Method, null, out _))
                        Debug.LogError($"Trying to Invoke method: {e.Target.GetType().Name}.{e.Method} couldn't be called.", e.Target);
                }
                if (!measured)
                {
                    if (e.Repeat > 0f) e.Time += e.Repeat;
                    else e.Cancelled = true;
                }
            }
            // Include repeats skipped by the traversal and calls scheduled by callbacks; discard
            // cancelled calls immediately, including ones with far-future deadlines.
            s_Entries.RemoveAll(e => e.Cancelled);
            s_Due.Clear();
            foreach (var e in s_Entries) s_Due.Enqueue(e, Priority(e));
        }

        internal static void Reset()
        {
            s_Entries.Clear();
            s_Due.Clear();
            s_Check = s_Sequence = 0;
        }

    }
}

namespace UnityEngine
{
    public sealed partial class Coroutine : YieldInstruction
    {
        internal MonoBehaviour Owner;
        internal string MethodName;
        internal readonly Stack<IEnumerator> Stack = new Stack<IEnumerator>();
        internal IEnumerator Root;
        internal bool Done;
        object m_Waiting;
        Unbound.Runtime.FrameTimedWait m_Timed;
        int m_WaitFrame;

        internal Coroutine() { }

        internal void Finish()
        {
            Done = true;
            Stack.Clear();
        }

        internal void Wait(object instruction)
        {
            m_Waiting = instruction;
            m_WaitFrame = Time.frameCount;
            switch (instruction)
            {
                case WaitForSeconds seconds:
                    m_Timed = Unbound.Runtime.FrameTimedWait.Start(seconds.m_Seconds);
                    break;
                case AsyncOperation operation when !operation.isDone && (operation.m_Waiter == null || operation.m_Waiter.Done):
                    operation.m_Waiter = this;
                    break;
            }
        }

        internal bool IsWaitingFor(object instruction) => ReferenceEquals(m_Waiting, instruction);

        internal bool ReadyIn(Unbound.Runtime.Coroutines.Phase phase)
        {
            switch (m_Waiting)
            {
                case WaitForFixedUpdate _:
                    return phase == Unbound.Runtime.Coroutines.Phase.FixedUpdate;
                case WaitForEndOfFrame _:
                    return phase == Unbound.Runtime.Coroutines.Phase.EndOfFrame;
                case WaitForSeconds _:
                    return phase == Unbound.Runtime.Coroutines.Phase.Update && m_Timed.IsOver;
                case Coroutine other:
                    return phase == Unbound.Runtime.Coroutines.Phase.Update && other.Done;
                case CustomYieldInstruction custom:
                    if (phase != Unbound.Runtime.Coroutines.Phase.Update || Time.frameCount <= m_WaitFrame) return false;
                    try { return !custom.keepWaiting; }
                    catch (Exception e) { Debug.LogException(e, Owner); return true; }
                case AsyncOperation operation:
                    return phase == Unbound.Runtime.Coroutines.Phase.Update && operation.isDone;
                default:
                    // null and anything else: the next frame's Update phase.
                    return phase == Unbound.Runtime.Coroutines.Phase.Update && Time.frameCount > m_WaitFrame;
            }
        }
    }

    public partial class YieldInstruction
    {
    }

    public sealed partial class WaitForSeconds : YieldInstruction
    {
        internal float m_Seconds;

        public WaitForSeconds(float seconds)
        {
            m_Seconds = seconds;
        }
    }

    public sealed partial class WaitForFixedUpdate : YieldInstruction
    {
    }

    public sealed partial class WaitForEndOfFrame : YieldInstruction
    {
    }

    public abstract partial class CustomYieldInstruction : IEnumerator
    {
        public abstract bool keepWaiting { get; }

        [Unbound.EditorFaithful("Unity's answer: a custom yield instruction has no current value")]
        public object Current => null;

        public bool MoveNext() => keepWaiting;

        [Unbound.EditorFaithful("Unity's base Reset does nothing too")]
        public virtual void Reset() { }
    }

    public partial class WaitForSecondsRealtime : CustomYieldInstruction
    {
        float m_WaitUntilTime = -1f;

        public float waitTime { get; set; }

        public override bool keepWaiting
        {
            get
            {
                if (m_WaitUntilTime < 0f)
                    m_WaitUntilTime = Time.realtimeSinceStartup + waitTime;
                bool wait = Time.realtimeSinceStartup < m_WaitUntilTime;
                if (!wait) Reset();
                return wait;
            }
        }

        public WaitForSecondsRealtime(float time)
        {
            waitTime = time;
        }

        public override void Reset()
        {
            m_WaitUntilTime = -1f;
        }
    }

    public sealed partial class WaitUntil : CustomYieldInstruction
    {
        readonly Func<bool> m_Predicate;

        public override bool keepWaiting => !m_Predicate();

        public WaitUntil(Func<bool> predicate)
        {
            m_Predicate = predicate;
        }

        public WaitUntil(Func<bool> predicate, TimeSpan timeout, Action onTimeout, WaitTimeoutMode timeoutMode = WaitTimeoutMode.Realtime)
        {
            var deadline = timeoutMode == WaitTimeoutMode.Realtime ? Time.realtimeSinceStartupAsDouble + timeout.TotalSeconds : Time.timeAsDouble + timeout.TotalSeconds;
            m_Predicate = () =>
            {
                if (predicate()) return true;
                var now = timeoutMode == WaitTimeoutMode.Realtime ? Time.realtimeSinceStartupAsDouble : Time.timeAsDouble;
                if (now >= deadline) { onTimeout?.Invoke(); return true; }
                return false;
            };
        }
    }

    public sealed partial class WaitWhile : CustomYieldInstruction
    {
        readonly Func<bool> m_Predicate;

        public override bool keepWaiting => m_Predicate();

        public WaitWhile(Func<bool> predicate)
        {
            m_Predicate = predicate;
        }
    }

}
