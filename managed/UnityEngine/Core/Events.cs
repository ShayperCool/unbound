using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine.Serialization;

namespace UnityEngine.Events
{
    public delegate void UnityAction();
    public delegate void UnityAction<T0>(T0 arg0);
    public delegate void UnityAction<T0, T1>(T0 arg0, T1 arg1);
    public delegate void UnityAction<T0, T1, T2>(T0 arg0, T1 arg1, T2 arg2);
    public delegate void UnityAction<T0, T1, T2, T3>(T0 arg0, T1 arg1, T2 arg2, T3 arg3);

    /// <summary>The argument a persistent call passes when its mode is not EventDefined.</summary>
    [Serializable]
    internal partial class ArgumentCache : ISerializationCallbackReceiver
    {
        [FormerlySerializedAs("objectArgument")] [SerializeField] Object m_ObjectArgument;
        [FormerlySerializedAs("objectArgumentAssemblyTypeName")] [SerializeField] string m_ObjectArgumentAssemblyTypeName;
        [FormerlySerializedAs("intArgument")] [SerializeField] int m_IntArgument;
        [FormerlySerializedAs("floatArgument")] [SerializeField] float m_FloatArgument;
        [FormerlySerializedAs("stringArgument")] [SerializeField] string m_StringArgument;
        [SerializeField] bool m_BoolArgument;

        public Object unityObjectArgument { get => m_ObjectArgument; set => m_ObjectArgument = value; }
        public string unityObjectArgumentAssemblyTypeName => m_ObjectArgumentAssemblyTypeName;
        public int intArgument { get => m_IntArgument; set => m_IntArgument = value; }
        public float floatArgument { get => m_FloatArgument; set => m_FloatArgument = value; }
        public string stringArgument { get => m_StringArgument; set => m_StringArgument = value; }
        public bool boolArgument { get => m_BoolArgument; set => m_BoolArgument = value; }

        public void OnBeforeSerialize() { }
        public void OnAfterDeserialize() { }
    }

    /// <summary>A call the event list stores: a target, a method name and an argument mode.</summary>
    [Serializable]
    internal partial class PersistentCall
    {
        [FormerlySerializedAs("instance")] [SerializeField] Object m_Target;
        [SerializeField] string m_TargetAssemblyTypeName;
        [FormerlySerializedAs("methodName")] [SerializeField] string m_MethodName;
        [FormerlySerializedAs("mode")] [SerializeField] PersistentListenerMode m_Mode = PersistentListenerMode.EventDefined;
        [FormerlySerializedAs("arguments")] [SerializeField] ArgumentCache m_Arguments = new ArgumentCache();
        [FormerlySerializedAs("enabled")] [FormerlySerializedAs("m_Enabled")] [SerializeField] UnityEventCallState m_CallState = UnityEventCallState.RuntimeOnly;

        public Object target => m_Target;
        public string targetAssemblyTypeName => m_TargetAssemblyTypeName;
        public string methodName => m_MethodName;
        public PersistentListenerMode mode { get => m_Mode; set => m_Mode = value; }
        public ArgumentCache arguments => m_Arguments;
        public UnityEventCallState callState { get => m_CallState; set => m_CallState = value; }

        public bool IsValid() => m_Target != null && !string.IsNullOrEmpty(m_MethodName);

        internal void Register(Object target, string method, PersistentListenerMode mode)
        {
            m_Target = target;
            m_TargetAssemblyTypeName = target?.GetType().AssemblyQualifiedName;
            m_MethodName = method;
            m_Mode = mode;
        }

        /// <summary>A callable for this entry, or null when the target or method is missing.</summary>
        internal Action<object[]> Resolve(UnityEventBase owner)
        {
            if (m_CallState == UnityEventCallState.Off || m_Target == null || string.IsNullOrEmpty(m_MethodName))
                return null;
            var targetType = m_Target.GetType();
            MethodInfo method;
            object[] fixedArgs = null;
            switch (m_Mode)
            {
                case PersistentListenerMode.EventDefined:
                    method = UnityEventBase.GetValidMethodInfo(targetType, m_MethodName, owner.ArgumentTypes());
                    break;
                case PersistentListenerMode.Void:
                    method = UnityEventBase.GetValidMethodInfo(targetType, m_MethodName, Type.EmptyTypes);
                    fixedArgs = Array.Empty<object>();
                    break;
                case PersistentListenerMode.Object:
                {
                    var argType = (string.IsNullOrEmpty(m_Arguments.unityObjectArgumentAssemblyTypeName)
                        ? null : Unbound.Runtime.TypeResolver.FindByAssemblyQualifiedName(m_Arguments.unityObjectArgumentAssemblyTypeName)) ?? typeof(Object);
                    method = UnityEventBase.GetValidMethodInfo(targetType, m_MethodName, new[] { argType });
                    var arg = m_Arguments.unityObjectArgument;
                    fixedArgs = new object[] { arg != null && argType.IsInstanceOfType(arg) ? arg : null };
                    break;
                }
                case PersistentListenerMode.Int:
                    method = UnityEventBase.GetValidMethodInfo(targetType, m_MethodName, new[] { typeof(int) });
                    fixedArgs = new object[] { m_Arguments.intArgument };
                    break;
                case PersistentListenerMode.Float:
                    method = UnityEventBase.GetValidMethodInfo(targetType, m_MethodName, new[] { typeof(float) });
                    fixedArgs = new object[] { m_Arguments.floatArgument };
                    break;
                case PersistentListenerMode.String:
                    method = UnityEventBase.GetValidMethodInfo(targetType, m_MethodName, new[] { typeof(string) });
                    fixedArgs = new object[] { m_Arguments.stringArgument };
                    break;
                case PersistentListenerMode.Bool:
                    method = UnityEventBase.GetValidMethodInfo(targetType, m_MethodName, new[] { typeof(bool) });
                    fixedArgs = new object[] { m_Arguments.boolArgument };
                    break;
                default:
                    return null;
            }
            if (method == null)
            {
                Debug.LogWarning($"UnityEvent: {targetType.Name}.{m_MethodName} not found for persistent call.", m_Target);
                return null;
            }
            var target = m_Target;
            return args =>
            {
                if (target == null) return;
                try
                {
                    method.Invoke(method.IsStatic ? null : target, fixedArgs ?? args);
                }
                catch (TargetInvocationException e)
                {
                    throw e.InnerException ?? e;
                }
            };
        }
    }

    [Serializable]
    internal partial class PersistentCallGroup
    {
        [FormerlySerializedAs("m_Listeners")] [SerializeField] List<PersistentCall> m_Calls = new List<PersistentCall>();

        public int Count => m_Calls.Count;
        public PersistentCall GetListener(int index) => m_Calls[index];
        public IEnumerable<PersistentCall> GetListeners() => m_Calls;
        public void AddListener() => m_Calls.Add(new PersistentCall());
        public void AddListener(PersistentCall call) => m_Calls.Add(call);
        public void RemoveListener(int index) => m_Calls.RemoveAt(index);
        public void Clear() => m_Calls.Clear();
    }

    [Serializable]
    public abstract partial class UnityEventBase : ISerializationCallbackReceiver
    {
        [FormerlySerializedAs("m_PersistentListeners")] [SerializeField] PersistentCallGroup m_PersistentCalls = new PersistentCallGroup();

        [NonSerialized] readonly List<Delegate> m_RuntimeCalls = new List<Delegate>();
        [NonSerialized] List<Action<object[]>> m_ResolvedPersistent;
        [NonSerialized] bool m_Dirty = true;

        protected UnityEventBase() { }

        void ISerializationCallbackReceiver.OnBeforeSerialize() => DirtyPersistentCalls();

        void ISerializationCallbackReceiver.OnAfterDeserialize() => DirtyPersistentCalls();

        void DirtyPersistentCalls() => m_Dirty = true;

        /// <summary>The parameter types of the event's own signature.</summary>
        internal abstract Type[] ArgumentTypes();

        protected abstract MethodInfo FindMethod_Impl(string name, Type targetObjType);

        public int GetPersistentEventCount() => m_PersistentCalls.Count;

        public Object GetPersistentTarget(int index) => index >= 0 && index < m_PersistentCalls.Count ? m_PersistentCalls.GetListener(index).target : null;

        public string GetPersistentMethodName(int index) => index >= 0 && index < m_PersistentCalls.Count ? m_PersistentCalls.GetListener(index).methodName : string.Empty;

        public void SetPersistentListenerState(int index, UnityEventCallState state)
        {
            if (index < 0 || index >= m_PersistentCalls.Count) return;
            m_PersistentCalls.GetListener(index).callState = state;
            DirtyPersistentCalls();
        }

        public UnityEventCallState GetPersistentListenerState(int index) =>
            index >= 0 && index < m_PersistentCalls.Count ? m_PersistentCalls.GetListener(index).callState : UnityEventCallState.Off;

        public void RemoveAllListeners() => m_RuntimeCalls.Clear();

        protected void AddListener(object targetObj, MethodInfo method)
        {
            m_RuntimeCalls.Add(Delegate.CreateDelegate(ActionTypeFor(method), targetObj, method));
        }

        internal void AddCall(Delegate call) => m_RuntimeCalls.Add(call);

        protected void RemoveListener(object targetObj, MethodInfo method)
        {
            m_RuntimeCalls.RemoveAll(d => d.Target == targetObj && d.Method == method);
        }

        internal void RemoveCall(Delegate call)
        {
            m_RuntimeCalls.RemoveAll(d => d.Target == call.Target && d.Method == call.Method);
        }

        static Type ActionTypeFor(MethodInfo method)
        {
            var parameters = method.GetParameters();
            switch (parameters.Length)
            {
                case 0: return typeof(UnityAction);
                case 1: return typeof(UnityAction<>).MakeGenericType(parameters[0].ParameterType);
                case 2: return typeof(UnityAction<,>).MakeGenericType(parameters[0].ParameterType, parameters[1].ParameterType);
                case 3: return typeof(UnityAction<,,>).MakeGenericType(parameters[0].ParameterType, parameters[1].ParameterType, parameters[2].ParameterType);
                default: return typeof(UnityAction<,,,>).MakeGenericType(parameters[0].ParameterType, parameters[1].ParameterType, parameters[2].ParameterType, parameters[3].ParameterType);
            }
        }

        /// <summary>Runs the persistent calls, then the runtime listeners, over a snapshot.</summary>
        protected void Invoke(object[] parameters)
        {
            if (m_Dirty)
            {
                m_ResolvedPersistent = new List<Action<object[]>>();
                foreach (var call in m_PersistentCalls.GetListeners())
                {
                    var resolved = call.Resolve(this);
                    if (resolved != null) m_ResolvedPersistent.Add(resolved);
                }
                m_Dirty = false;
            }
            foreach (var call in m_ResolvedPersistent.ToArray())
                call(parameters);
            foreach (var call in m_RuntimeCalls.ToArray())
                call.DynamicInvoke(parameters);
        }

        internal void InvokeRuntime<TDelegate>(Action<TDelegate> invoke, object[] parameters) where TDelegate : Delegate
        {
            if (m_Dirty)
            {
                m_ResolvedPersistent = new List<Action<object[]>>();
                foreach (var call in m_PersistentCalls.GetListeners())
                {
                    var resolved = call.Resolve(this);
                    if (resolved != null) m_ResolvedPersistent.Add(resolved);
                }
                m_Dirty = false;
            }
            foreach (var call in m_ResolvedPersistent.ToArray())
                call(parameters);
            foreach (var call in m_RuntimeCalls.ToArray())
            {
                if (call is TDelegate typed) invoke(typed);
                else call.DynamicInvoke(parameters);
            }
        }

        public override string ToString() => base.ToString() + " " + GetType().FullName;

        public static MethodInfo GetValidMethodInfo(object obj, string functionName, Type[] argumentTypes) =>
            obj == null ? null : GetValidMethodInfo(obj.GetType(), functionName, argumentTypes);

        /// <summary>The instance method of the type or a base type whose parameters accept the
        /// argument types exactly (assignability for reference types, as Unity matches).</summary>
        public static MethodInfo GetValidMethodInfo(Type objectType, string functionName, Type[] argumentTypes)
        {
            for (var type = objectType; type != null && type != typeof(object); type = type.BaseType)
            {
                foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                {
                    if (method.Name != functionName) continue;
                    var parameters = method.GetParameters();
                    if (parameters.Length != argumentTypes.Length) continue;
                    bool match = true;
                    for (int i = 0; i < parameters.Length && match; i++)
                    {
                        var p = parameters[i].ParameterType;
                        var a = argumentTypes[i];
                        match = p == a || (!p.IsPrimitive && p.IsAssignableFrom(a));
                    }
                    if (match) return method;
                }
            }
            return null;
        }
    }

    [Serializable]
    public partial class UnityEvent : UnityEventBase
    {
        public UnityEvent() { }

        public void AddListener(UnityAction call) => AddCall(call);
        public void RemoveListener(UnityAction call) => RemoveCall(call);

        protected override MethodInfo FindMethod_Impl(string name, Type targetObjType) => GetValidMethodInfo(targetObjType, name, Type.EmptyTypes);

        internal override Type[] ArgumentTypes() => Type.EmptyTypes;

        public void Invoke() => InvokeRuntime<UnityAction>(a => a(), Array.Empty<object>());

        internal void AddPersistentListener(UnityAction call) => AddCall(call);
    }

    [Serializable]
    public partial class UnityEvent<T0> : UnityEventBase
    {
        public UnityEvent() { }

        public void AddListener(UnityAction<T0> call) => AddCall(call);
        public void RemoveListener(UnityAction<T0> call) => RemoveCall(call);

        protected override MethodInfo FindMethod_Impl(string name, Type targetObjType) => GetValidMethodInfo(targetObjType, name, new[] { typeof(T0) });

        internal override Type[] ArgumentTypes() => new[] { typeof(T0) };

        public void Invoke(T0 arg0) => InvokeRuntime<UnityAction<T0>>(a => a(arg0), new object[] { arg0 });
    }

    [Serializable]
    public partial class UnityEvent<T0, T1> : UnityEventBase
    {
        public UnityEvent() { }

        public void AddListener(UnityAction<T0, T1> call) => AddCall(call);
        public void RemoveListener(UnityAction<T0, T1> call) => RemoveCall(call);

        protected override MethodInfo FindMethod_Impl(string name, Type targetObjType) => GetValidMethodInfo(targetObjType, name, new[] { typeof(T0), typeof(T1) });

        internal override Type[] ArgumentTypes() => new[] { typeof(T0), typeof(T1) };

        public void Invoke(T0 arg0, T1 arg1) => InvokeRuntime<UnityAction<T0, T1>>(a => a(arg0, arg1), new object[] { arg0, arg1 });
    }

    [Serializable]
    public partial class UnityEvent<T0, T1, T2> : UnityEventBase
    {
        public UnityEvent() { }

        public void AddListener(UnityAction<T0, T1, T2> call) => AddCall(call);
        public void RemoveListener(UnityAction<T0, T1, T2> call) => RemoveCall(call);

        protected override MethodInfo FindMethod_Impl(string name, Type targetObjType) => GetValidMethodInfo(targetObjType, name, new[] { typeof(T0), typeof(T1), typeof(T2) });

        internal override Type[] ArgumentTypes() => new[] { typeof(T0), typeof(T1), typeof(T2) };

        public void Invoke(T0 arg0, T1 arg1, T2 arg2) => InvokeRuntime<UnityAction<T0, T1, T2>>(a => a(arg0, arg1, arg2), new object[] { arg0, arg1, arg2 });
    }

    [Serializable]
    public partial class UnityEvent<T0, T1, T2, T3> : UnityEventBase
    {
        public UnityEvent() { }

        public void AddListener(UnityAction<T0, T1, T2, T3> call) => AddCall(call);
        public void RemoveListener(UnityAction<T0, T1, T2, T3> call) => RemoveCall(call);

        protected override MethodInfo FindMethod_Impl(string name, Type targetObjType) => GetValidMethodInfo(targetObjType, name, new[] { typeof(T0), typeof(T1), typeof(T2), typeof(T3) });

        internal override Type[] ArgumentTypes() => new[] { typeof(T0), typeof(T1), typeof(T2), typeof(T3) };

        public void Invoke(T0 arg0, T1 arg1, T2 arg2, T3 arg3) => InvokeRuntime<UnityAction<T0, T1, T2, T3>>(a => a(arg0, arg1, arg2, arg3), new object[] { arg0, arg1, arg2, arg3 });
    }
}
