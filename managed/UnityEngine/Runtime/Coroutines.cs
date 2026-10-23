using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>
    /// Coroutines with Unity's timing: a routine runs at once until its first yield; `null` and
    /// unknown values resume after the next Update; WaitForSeconds after its scaled time (checked
    /// after Update); WaitForFixedUpdate after the physics step; WaitForEndOfFrame after rendering; a
    /// Coroutine or nested IEnumerator when it finishes; AsyncOperation when done; a
    /// CustomYieldInstruction when keepWaiting turns false. Routines stop when their behaviour is
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
                if (!moved)
                {
                    c.Stack.Pop();
                    if (c.Stack.Count == 0)
                    {
                        c.Finish();
                        return;
                    }
                    continue; // the parent routine resumes at once
                }
                var current = top.Current;
                if (current is IEnumerator nested && !(current is CustomYieldInstruction))
                {
                    c.Stack.Push(nested);
                    continue;
                }
                c.Wait(current);
                return;
            }
        }

        /// <summary>Resumes routines whose wait is over, for the phase of the frame that just ran.</summary>
        internal static void Run(Phase phase)
        {
            int count = s_Running.Count;
            for (int i = 0; i < count && i < s_Running.Count; i++)
            {
                var c = s_Running[i];
                if (c.Done) continue;
                if (c.ReadyIn(phase)) Step(c);
            }
            s_Running.RemoveAll(c => c.Done);
        }

        internal static void Reset() => s_Running.Clear();
    }

    internal static class Invokes
    {
        sealed class Entry
        {
            internal MonoBehaviour Target;
            internal string Method;
            internal double Time;
            internal float Repeat;
            internal bool Cancelled;
        }

        static readonly List<Entry> s_Entries = new List<Entry>();

        internal static void Schedule(MonoBehaviour target, string method, float delay, float repeat)
        {
            s_Entries.Add(new Entry
            {
                Target = target,
                Method = method,
                Time = UnityEngine.Time.timeAsDouble + Math.Max(0f, delay),
                Repeat = repeat,
            });
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
            double now = UnityEngine.Time.timeAsDouble;
            int count = s_Entries.Count;
            for (int i = 0; i < count && i < s_Entries.Count; i++)
            {
                var e = s_Entries[i];
                if (e.Cancelled || e.Time > now) continue;
                if (e.Target.m_Destroyed)
                {
                    e.Cancelled = true;
                    continue;
                }
                // Unity skips (but keeps) invokes of behaviours whose GameObject is inactive.
                if (e.Target.m_GameObject.activeInHierarchy)
                {
                    if (!Messaging.TryCall(e.Target, e.Method, null, out _))
                        Debug.LogError($"Trying to Invoke method: {e.Target.GetType().Name}.{e.Method} couldn't be called.", e.Target);
                }
                if (e.Repeat > 0f) e.Time += e.Repeat;
                else e.Cancelled = true;
            }
            s_Entries.RemoveAll(e => e.Cancelled);
        }

        internal static void Reset() => s_Entries.Clear();
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
        double m_WaitUntil;
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
                    m_WaitUntil = Time.timeAsDouble + seconds.m_Seconds;
                    break;
            }
        }

        internal bool ReadyIn(Unbound.Runtime.Coroutines.Phase phase)
        {
            switch (m_Waiting)
            {
                case WaitForFixedUpdate _:
                    return phase == Unbound.Runtime.Coroutines.Phase.FixedUpdate;
                case WaitForEndOfFrame _:
                    return phase == Unbound.Runtime.Coroutines.Phase.EndOfFrame;
                case WaitForSeconds _:
                    return phase == Unbound.Runtime.Coroutines.Phase.Update && Time.timeAsDouble >= m_WaitUntil;
                case Coroutine other:
                    return phase == Unbound.Runtime.Coroutines.Phase.Update && other.Done;
                case CustomYieldInstruction custom:
                    if (phase != Unbound.Runtime.Coroutines.Phase.Update) return false;
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

        public object Current => null;

        public bool MoveNext() => keepWaiting;

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
