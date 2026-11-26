using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace UnityEngine.Events
{
    // How a UnityEvent calls its listeners, as Unity's own classes do: each listener is an invokable
    // call around a delegate, the event keeps them in an InvokableCallList and its Invoke goes over
    // that list's executing calls. The frames of these methods are Unity's in stack texts
    // (Unbound.UnityFrame: the listener's exception lists InvokableCall.Invoke and UnityEvent.Invoke
    // with their places in Unity's source, matches the Unity 6000.0.71f1 Editor,
    // tests/projects/eventstacks), so none of them may be inlined.

    internal abstract partial class BaseInvokableCall
    {
        protected BaseInvokableCall() { }

        protected BaseInvokableCall(object target, MethodInfo function)
        {
            if (function == null) throw new ArgumentNullException(nameof(function));
            if (function.IsStatic)
            {
                if (target != null) throw new ArgumentException("target must be null");
            }
            else if (target == null) throw new ArgumentNullException(nameof(target));
        }

        public abstract void Invoke(object[] args);

        protected static void ThrowOnInvalidArg<T>(object arg)
        {
            if (arg != null && !(arg is T))
                throw new ArgumentException(string.Format("Passed argument 'args[0]' is of the wrong type. Type:{0} Expected:{1}", arg.GetType(), typeof(T)));
        }

        /// <summary>Whether the delegate may run: not when its target is a destroyed UnityEngine.Object.</summary>
        protected static bool AllowInvoke(Delegate @delegate)
        {
            object target = @delegate.Target;
            if (target == null) return true;
            var unityObject = target as Object;
            if (!ReferenceEquals(unityObject, null)) return unityObject != null;
            return true;
        }

        public abstract bool Find(object targetObj, MethodInfo method);

        /// <summary>Whether a call's delegate is the method on the target. A listener added as null
        /// has none: looking it up throws NullReferenceException, as in Unity (matches the Unity
        /// 6000.0.71f1 Editor, tests/projects/editorapi connection.unregister).</summary>
        internal static bool Same(Delegate @delegate, object targetObj, MethodInfo method) =>
            @delegate.Target == targetObj && @delegate.Method.Equals(method);
    }

    internal partial class InvokableCall : BaseInvokableCall
    {
        event UnityAction Delegate;

        public InvokableCall(object target, MethodInfo theFunction) : base(target, theFunction) =>
            Delegate += (UnityAction)System.Delegate.CreateDelegate(typeof(UnityAction), target, theFunction);

        public InvokableCall(UnityAction action) => Delegate += action;

        [Unbound.UnityFrame("Invoke", Inlined = true), MethodImpl(MethodImplOptions.NoInlining)]
        public override void Invoke(object[] args)
        {
            if (AllowInvoke(Delegate)) Delegate();
        }

        [Unbound.UnityFrame("Invoke", Inlined = true), MethodImpl(MethodImplOptions.NoInlining)]
        public void Invoke()
        {
            if (AllowInvoke(Delegate)) Delegate();
        }

        public override bool Find(object targetObj, MethodInfo method) => Same(Delegate, targetObj, method);
    }

    internal partial class InvokableCall<T1> : BaseInvokableCall
    {
        protected event UnityAction<T1> Delegate;

        public InvokableCall(object target, MethodInfo theFunction) : base(target, theFunction) =>
            Delegate += (UnityAction<T1>)System.Delegate.CreateDelegate(typeof(UnityAction<T1>), target, theFunction);

        public InvokableCall(UnityAction<T1> action) => Delegate += action;

        [Unbound.UnityFrame("Invoke", Inlined = true), MethodImpl(MethodImplOptions.NoInlining)]
        public override void Invoke(object[] args)
        {
            if (args.Length != 1) throw new ArgumentException("Passed argument 'args' is invalid size. Expected size is 1");
            ThrowOnInvalidArg<T1>(args[0]);
            if (AllowInvoke(Delegate)) Delegate((T1)args[0]);
        }

        [Unbound.UnityFrame("Invoke", Inlined = true), MethodImpl(MethodImplOptions.NoInlining)]
        public virtual void Invoke(T1 args0)
        {
            if (AllowInvoke(Delegate)) Delegate(args0);
        }

        public override bool Find(object targetObj, MethodInfo method) => Same(Delegate, targetObj, method);
    }

    internal partial class InvokableCall<T1, T2> : BaseInvokableCall
    {
        protected event UnityAction<T1, T2> Delegate;

        public InvokableCall(object target, MethodInfo theFunction) : base(target, theFunction) =>
            Delegate += (UnityAction<T1, T2>)System.Delegate.CreateDelegate(typeof(UnityAction<T1, T2>), target, theFunction);

        public InvokableCall(UnityAction<T1, T2> action) => Delegate += action;

        [Unbound.UnityFrame("Invoke", Inlined = true), MethodImpl(MethodImplOptions.NoInlining)]
        public override void Invoke(object[] args)
        {
            if (args.Length != 2) throw new ArgumentException("Passed argument 'args' is invalid size. Expected size is 1");
            ThrowOnInvalidArg<T1>(args[0]);
            ThrowOnInvalidArg<T2>(args[1]);
            if (AllowInvoke(Delegate)) Delegate((T1)args[0], (T2)args[1]);
        }

        [Unbound.UnityFrame("Invoke", Inlined = true), MethodImpl(MethodImplOptions.NoInlining)]
        public void Invoke(T1 args0, T2 args1)
        {
            if (AllowInvoke(Delegate)) Delegate(args0, args1);
        }

        public override bool Find(object targetObj, MethodInfo method) => Same(Delegate, targetObj, method);
    }

    internal partial class InvokableCall<T1, T2, T3> : BaseInvokableCall
    {
        protected event UnityAction<T1, T2, T3> Delegate;

        public InvokableCall(object target, MethodInfo theFunction) : base(target, theFunction) =>
            Delegate += (UnityAction<T1, T2, T3>)System.Delegate.CreateDelegate(typeof(UnityAction<T1, T2, T3>), target, theFunction);

        public InvokableCall(UnityAction<T1, T2, T3> action) => Delegate += action;

        [Unbound.UnityFrame("Invoke", Inlined = true), MethodImpl(MethodImplOptions.NoInlining)]
        public override void Invoke(object[] args)
        {
            if (args.Length != 3) throw new ArgumentException("Passed argument 'args' is invalid size. Expected size is 1");
            ThrowOnInvalidArg<T1>(args[0]);
            ThrowOnInvalidArg<T2>(args[1]);
            ThrowOnInvalidArg<T3>(args[2]);
            if (AllowInvoke(Delegate)) Delegate((T1)args[0], (T2)args[1], (T3)args[2]);
        }

        [Unbound.UnityFrame("Invoke", Inlined = true), MethodImpl(MethodImplOptions.NoInlining)]
        public void Invoke(T1 args0, T2 args1, T3 args2)
        {
            if (AllowInvoke(Delegate)) Delegate(args0, args1, args2);
        }

        public override bool Find(object targetObj, MethodInfo method) => Same(Delegate, targetObj, method);
    }

    internal partial class InvokableCall<T1, T2, T3, T4> : BaseInvokableCall
    {
        protected event UnityAction<T1, T2, T3, T4> Delegate;

        public InvokableCall(object target, MethodInfo theFunction) : base(target, theFunction) =>
            Delegate += (UnityAction<T1, T2, T3, T4>)System.Delegate.CreateDelegate(typeof(UnityAction<T1, T2, T3, T4>), target, theFunction);

        public InvokableCall(UnityAction<T1, T2, T3, T4> action) => Delegate += action;

        [Unbound.UnityFrame("Invoke", Inlined = true), MethodImpl(MethodImplOptions.NoInlining)]
        public override void Invoke(object[] args)
        {
            if (args.Length != 4) throw new ArgumentException("Passed argument 'args' is invalid size. Expected size is 1");
            ThrowOnInvalidArg<T1>(args[0]);
            ThrowOnInvalidArg<T2>(args[1]);
            ThrowOnInvalidArg<T3>(args[2]);
            ThrowOnInvalidArg<T4>(args[3]);
            if (AllowInvoke(Delegate)) Delegate((T1)args[0], (T2)args[1], (T3)args[2], (T4)args[3]);
        }

        [Unbound.UnityFrame("Invoke", Inlined = true), MethodImpl(MethodImplOptions.NoInlining)]
        public void Invoke(T1 args0, T2 args1, T3 args2, T4 args3)
        {
            if (AllowInvoke(Delegate)) Delegate(args0, args1, args2, args3);
        }

        public override bool Find(object targetObj, MethodInfo method) => Same(Delegate, targetObj, method);
    }

    /// <summary>A persistent call with a fixed argument (the Int, Float, String, Bool and Object modes):
    /// whatever the event passes, its method gets that argument.</summary>
    internal partial class CachedInvokableCall<T> : InvokableCall<T>
    {
        readonly T m_Arg1;

        public CachedInvokableCall(Object target, MethodInfo theFunction, T argument) : base(target, theFunction) => m_Arg1 = argument;

        [Unbound.UnityFrame("Invoke", Inlined = true), MethodImpl(MethodImplOptions.NoInlining)]
        public override void Invoke(object[] args) => base.Invoke(m_Arg1);

        [Unbound.UnityFrame("Invoke", Inlined = true), MethodImpl(MethodImplOptions.NoInlining)]
        public override void Invoke(T arg0) => base.Invoke(m_Arg1);
    }

    /// <summary>The calls of one event: its persistent calls, then its runtime listeners. An Invoke
    /// goes over the executing list. An added call makes it again, in place, at the next Invoke: an
    /// Invoke of the same event from a listener then changes the rest of the outer one too; a
    /// removal replaces it at once, leaving the outer Invoke its list (matches the Unity 6000.0.71f1
    /// Editor, tests/projects/eventstacks changes.*).</summary>
    internal partial class InvokableCallList
    {
        readonly List<BaseInvokableCall> m_PersistentCalls = new List<BaseInvokableCall>();
        readonly List<BaseInvokableCall> m_RuntimeCalls = new List<BaseInvokableCall>();
        List<BaseInvokableCall> m_ExecutingCalls = new List<BaseInvokableCall>();
        bool m_NeedsUpdate = true;

        public InvokableCallList() { }

        public int Count => m_PersistentCalls.Count + m_RuntimeCalls.Count;

        public void AddPersistentInvokableCall(BaseInvokableCall call)
        {
            m_PersistentCalls.Add(call);
            m_NeedsUpdate = true;
        }

        public void AddListener(BaseInvokableCall call)
        {
            m_RuntimeCalls.Add(call);
            m_NeedsUpdate = true;
        }

        public void RemoveListener(object targetObj, MethodInfo method)
        {
            var toRemove = new List<BaseInvokableCall>();
            foreach (var call in m_RuntimeCalls)
                if (call.Find(targetObj, method)) toRemove.Add(call);
            m_RuntimeCalls.RemoveAll(toRemove.Contains);
            Replace();
        }

        public void Clear()
        {
            m_RuntimeCalls.Clear();
            Replace();
        }

        public void ClearPersistent()
        {
            m_PersistentCalls.Clear();
            Replace();
        }

        /// <summary>A removal makes the executing list anew at once: an Invoke under way goes on
        /// over the list it took (tests/projects/eventstacks changes.nested_invoke_after_remove*,
        /// nested_invoke_after_persistent_off).</summary>
        void Replace()
        {
            var executing = new List<BaseInvokableCall>(m_PersistentCalls.Count + m_RuntimeCalls.Count);
            executing.AddRange(m_PersistentCalls);
            executing.AddRange(m_RuntimeCalls);
            m_ExecutingCalls = executing;
            m_NeedsUpdate = false;
        }

        public List<BaseInvokableCall> PrepareInvoke()
        {
            if (m_NeedsUpdate)
            {
                m_ExecutingCalls.Clear();
                m_ExecutingCalls.AddRange(m_PersistentCalls);
                m_ExecutingCalls.AddRange(m_RuntimeCalls);
                m_NeedsUpdate = false;
            }
            return m_ExecutingCalls;
        }
    }
}
