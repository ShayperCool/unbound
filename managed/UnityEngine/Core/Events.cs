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

        /// <summary>The object argument; setting it also names its type: the object's own, empty for
        /// none (matches the Unity 6000.0.71f1 Editor, tests/projects/unityeventtools add.object*).</summary>
        public Object unityObjectArgument
        {
            get => m_ObjectArgument;
            set
            {
                m_ObjectArgument = value;
                m_ObjectArgumentAssemblyTypeName = value != null ? value.GetType().AssemblyQualifiedName : string.Empty;
            }
        }
        public string unityObjectArgumentAssemblyTypeName => m_ObjectArgumentAssemblyTypeName;
        public int intArgument { get => m_IntArgument; set => m_IntArgument = value; }
        public float floatArgument { get => m_FloatArgument; set => m_FloatArgument = value; }
        public string stringArgument { get => m_StringArgument; set => m_StringArgument = value; }
        public bool boolArgument { get => m_BoolArgument; set => m_BoolArgument = value; }

        public void OnBeforeSerialize() => m_ObjectArgumentAssemblyTypeName = UnityEventTools.TidyAssemblyTypeName(m_ObjectArgumentAssemblyTypeName);
        public void OnAfterDeserialize() => m_ObjectArgumentAssemblyTypeName = UnityEventTools.TidyAssemblyTypeName(m_ObjectArgumentAssemblyTypeName);
    }

    internal partial class UnityEventTools
    {
        static readonly string[] s_AssemblyDetails = { ", Version=", ", Culture=", ", PublicKeyToken=" };

        /// <summary>A stored type name without version, culture and key, naming UnityEngine.*Module
        /// types as in UnityEngine (matches the Unity 6000.0.71f1 Editor, tests/projects/eventtypenames).</summary>
        internal static string TidyAssemblyTypeName(string assemblyTypeName)
        {
            if (string.IsNullOrEmpty(assemblyTypeName)) return assemblyTypeName;
            int cut = int.MaxValue;
            foreach (var part in s_AssemblyDetails)
            {
                int i = assemblyTypeName.IndexOf(part, StringComparison.Ordinal);
                if (i != -1) cut = Math.Min(cut, i);
            }
            if (cut != int.MaxValue) assemblyTypeName = assemblyTypeName.Substring(0, cut);
            int module = assemblyTypeName.IndexOf(", UnityEngine.", StringComparison.Ordinal);
            if (module != -1 && assemblyTypeName.EndsWith("Module", StringComparison.Ordinal))
                assemblyTypeName = assemblyTypeName.Substring(0, module) + ", UnityEngine";
            return assemblyTypeName;
        }
    }

    /// <summary>A call the event list stores: a target, a method name and an argument mode.</summary>
    [Serializable]
    internal partial class PersistentCall : ISerializationCallbackReceiver
    {
        [FormerlySerializedAs("instance")] [SerializeField] Object m_Target;
        [SerializeField] string m_TargetAssemblyTypeName;
        [FormerlySerializedAs("methodName")] [SerializeField] string m_MethodName;
        [FormerlySerializedAs("mode")] [SerializeField] PersistentListenerMode m_Mode = PersistentListenerMode.EventDefined;
        [FormerlySerializedAs("arguments")] [SerializeField] ArgumentCache m_Arguments = new ArgumentCache();
        [FormerlySerializedAs("enabled")] [FormerlySerializedAs("m_Enabled")] [SerializeField] UnityEventCallState m_CallState = UnityEventCallState.RuntimeOnly;

        public Object target => m_Target;
        public string targetAssemblyTypeName
        {
            get
            {
                if (string.IsNullOrEmpty(m_TargetAssemblyTypeName) && m_Target != null)
                    m_TargetAssemblyTypeName = UnityEventTools.TidyAssemblyTypeName(m_Target.GetType().AssemblyQualifiedName);
                return m_TargetAssemblyTypeName;
            }
        }
        public string methodName => m_MethodName;
        public PersistentListenerMode mode { get => m_Mode; set => m_Mode = value; }
        public ArgumentCache arguments => m_Arguments;
        public UnityEventCallState callState { get => m_CallState; set => m_CallState = value; }

        public bool IsValid() => !string.IsNullOrEmpty(targetAssemblyTypeName) && !string.IsNullOrEmpty(m_MethodName);

        public void OnBeforeSerialize() => m_TargetAssemblyTypeName = UnityEventTools.TidyAssemblyTypeName(m_TargetAssemblyTypeName);
        public void OnAfterDeserialize() => m_TargetAssemblyTypeName = UnityEventTools.TidyAssemblyTypeName(m_TargetAssemblyTypeName);

        internal void Register(Object target, string method, PersistentListenerMode mode)
        {
            Register(target, target?.GetType(), method, mode);
        }

        internal void Register(Object target, Type targetType, string method, PersistentListenerMode mode)
        {
            m_Target = target;
            m_TargetAssemblyTypeName = UnityEventTools.TidyAssemblyTypeName(targetType?.AssemblyQualifiedName);
            m_MethodName = method;
            m_Mode = mode;
        }

        /// <summary>Forgets the target and method; the mode, argument and state stay (matches the Unity
        /// 6000.0.71f1 Editor, tests/projects/unityeventtools unregister).</summary>
        internal void Unregister()
        {
            m_Target = null;
            m_TargetAssemblyTypeName = string.Empty;
            m_MethodName = string.Empty;
        }

        /// <summary>The invokable call of this entry, or null when it is off, or its target or method
        /// is missing: the event's own call for its own arguments, a call with no argument, or one that
        /// passes the entry's argument whatever the event passes.</summary>
        public BaseInvokableCall GetRuntimeCall(UnityEventBase owner)
        {
            if (m_CallState == UnityEventCallState.Off || !IsValid())
                return null;
            if (m_CallState == UnityEventCallState.RuntimeOnly
                && (!Unbound.Runtime.EditorSession.IsPlaying || (m_Target != null && !Application.IsPlaying(m_Target)))) return null;
            var targetType = m_Target != null ? m_Target.GetType() : Unbound.Runtime.TypeResolver.FindByAssemblyQualifiedName(targetAssemblyTypeName);
            if (targetType == null) return null;
            var argType = m_Mode == PersistentListenerMode.Object && !string.IsNullOrEmpty(m_Arguments.unityObjectArgumentAssemblyTypeName)
                ? Unbound.Runtime.TypeResolver.FindByAssemblyQualifiedName(m_Arguments.unityObjectArgumentAssemblyTypeName) : null;
            var parameters = UnityEventBase.ParametersOf(m_Mode, owner, argType);
            if (parameters == null) return null;
            var method = UnityEventBase.GetValidMethodInfo(targetType, m_MethodName, parameters);
            if (method == null)
            {
                Debug.LogWarning($"UnityEvent: {targetType.Name}.{m_MethodName} not found for persistent call.", m_Target);
                return null;
            }
            if (!method.IsStatic && m_Target == null) return null;
            var target = method.IsStatic ? null : m_Target;
            switch (m_Mode)
            {
                case PersistentListenerMode.EventDefined: return owner.GetDelegate(target, method);
                case PersistentListenerMode.Void: return new InvokableCall(target, method);
                case PersistentListenerMode.Object:
                {
                    var type = parameters[0];
                    var arg = m_Arguments.unityObjectArgument;
                    if (arg != null && !type.IsInstanceOfType(arg)) arg = null;
                    return (BaseInvokableCall)Activator.CreateInstance(typeof(CachedInvokableCall<>).MakeGenericType(type), target, method, arg);
                }
                case PersistentListenerMode.Int: return new CachedInvokableCall<int>(target, method, m_Arguments.intArgument);
                case PersistentListenerMode.Float: return new CachedInvokableCall<float>(target, method, m_Arguments.floatArgument);
                case PersistentListenerMode.String: return new CachedInvokableCall<string>(target, method, m_Arguments.stringArgument);
                default: return new CachedInvokableCall<bool>(target, method, m_Arguments.boolArgument);
            }
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

        /// <summary>Puts the invokable calls of the valid entries into the event's list, in order.</summary>
        public void Initialize(InvokableCallList invokableList, UnityEventBase unityEventBase)
        {
            foreach (var persistentCall in m_Calls)
            {
                if (!persistentCall.IsValid()) continue;
                var call = persistentCall.GetRuntimeCall(unityEventBase);
                if (call != null) invokableList.AddPersistentInvokableCall(call);
            }
        }
    }

    [Serializable]
    public abstract partial class UnityEventBase : ISerializationCallbackReceiver
    {
        [FormerlySerializedAs("m_PersistentListeners")] [SerializeField] PersistentCallGroup m_PersistentCalls = new PersistentCallGroup();

        readonly InvokableCallList m_Calls = new InvokableCallList();
        bool m_CallsDirty = true;

        protected UnityEventBase() { }

        void ISerializationCallbackReceiver.OnBeforeSerialize() => DirtyPersistentCalls();

        void ISerializationCallbackReceiver.OnAfterDeserialize() => DirtyPersistentCalls();

        void DirtyPersistentCalls()
        {
            m_Calls.ClearPersistent();
            m_CallsDirty = true;
        }

        void RebuildPersistentCallsIfNeeded()
        {
            if (!m_CallsDirty) return;
            m_PersistentCalls.Initialize(m_Calls, this);
            m_CallsDirty = false;
        }

        /// <summary>The calls an Invoke goes over: the persistent calls (made again when they changed),
        /// then the runtime listeners.</summary>
        internal List<BaseInvokableCall> PrepareInvoke()
        {
            RebuildPersistentCallsIfNeeded();
            return m_Calls.PrepareInvoke();
        }

        internal int GetCallsCount() => m_Calls.Count;

        internal void AddCall(BaseInvokableCall call) => m_Calls.AddListener(call);

        /// <summary>The call of a method with the event's own arguments.</summary>
        internal abstract BaseInvokableCall GetDelegate(object target, MethodInfo theFunction);

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

        public void RemoveAllListeners() => m_Calls.Clear();

        protected void AddListener(object targetObj, MethodInfo method) => m_Calls.AddListener(GetDelegate(targetObj, method));

        internal void UaAddPersistentCall(Delegate callback, UnityEventCallState state)
        {
            int index = m_PersistentCalls.Count;
            AddPersistentListener();
            RegisterPersistentListener(index, callback, PersistentListenerMode.EventDefined);
            m_PersistentCalls.GetListener(index).callState = state;
        }

        /// <summary>A new, empty persistent call at the end (state RuntimeOnly).</summary>
        internal void AddPersistentListener()
        {
            m_PersistentCalls.AddListener();
            DirtyPersistentCalls();
        }

        /// <summary>Points the persistent call at <paramref name="index"/> at a delegate's method in a
        /// mode, if the method takes that mode's parameters: a null delegate is a warning, a method
        /// with no UnityEngine.Object to call it on an exception, before the index is looked at
        /// (matches the Unity 6000.0.71f1 Editor, tests/projects/unityeventtools).</summary>
        internal bool RegisterPersistentListener(int index, Delegate callback, PersistentListenerMode mode, Type objectArgumentType = null)
        {
            if (callback == null)
            {
                Debug.LogWarning("Registering a Listener requires an action");
                return false;
            }
            var method = callback.Method;
            var target = callback.Target as Object;
            // The event's own mode names the target as a UnityEngine.Object, the argument modes the
            // delegate's target itself (a lambda's closure, for one).
            object named = mode == PersistentListenerMode.EventDefined ? target : callback.Target;
            if (method.DeclaringType == null)
                throw new NullReferenceException($"Method '{method.Name}' declaring type is null, global methods are not supported");
            if (!method.IsStatic && (target == null || target.GetInstanceID() == 0))
                throw new ArgumentException($"Could not register callback {method.Name} on {named}. The class {(named == null ? "null" : named.GetType().ToString())} does not derive from UnityEngine.Object");
            if (GetValidMethodInfo(method.IsStatic ? method.DeclaringType : target.GetType(), method.Name, ParametersOf(mode, this, objectArgumentType)) == null)
            {
                Debug.LogWarning($"Could not register listener {target}.{method} on {GetType()} the method could not be found.");
                return false;
            }
            m_PersistentCalls.GetListener(index).Register(target, method.DeclaringType, method.Name, mode);
            DirtyPersistentCalls();
            return true;
        }

        /// <summary>The persistent call's argument, which the registering mode sets.</summary>
        internal ArgumentCache PersistentArguments(int index) => m_PersistentCalls.GetListener(index).arguments;

        internal void UnregisterPersistentListener(int index)
        {
            m_PersistentCalls.GetListener(index).Unregister();
            DirtyPersistentCalls();
        }

        internal void RemovePersistentListener(int index)
        {
            m_PersistentCalls.RemoveListener(index);
            DirtyPersistentCalls();
        }

        /// <summary>The parameters a persistent call's method takes in a mode: the event's own, none,
        /// or the one argument; null for an unknown mode.</summary>
        internal static Type[] ParametersOf(PersistentListenerMode mode, UnityEventBase owner, Type objectArgumentType)
        {
            switch (mode)
            {
                case PersistentListenerMode.EventDefined: return owner.ArgumentTypes();
                case PersistentListenerMode.Void: return Type.EmptyTypes;
                case PersistentListenerMode.Object: return new[] { objectArgumentType ?? typeof(Object) };
                case PersistentListenerMode.Int: return new[] { typeof(int) };
                case PersistentListenerMode.Float: return new[] { typeof(float) };
                case PersistentListenerMode.String: return new[] { typeof(string) };
                case PersistentListenerMode.Bool: return new[] { typeof(bool) };
                default: return null;
            }
        }

        internal void RemovePersistentListener(Object target, MethodInfo method)
        {
            if (method == null) return;
            for (int i = m_PersistentCalls.Count - 1; i >= 0; i--)
            {
                var call = m_PersistentCalls.GetListener(i);
                if (call.target == target && call.methodName == method.Name) m_PersistentCalls.RemoveListener(i);
            }
            DirtyPersistentCalls();
        }


        protected void RemoveListener(object targetObj, MethodInfo method) => m_Calls.RemoveListener(targetObj, method);

        internal void RemoveCall(Delegate call)
        {
            RemoveListener(call.Target, call.Method);
        }

        [Unbound.UnityFrame("Invoke"), System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        protected void Invoke(object[] parameters)
        {
            List<BaseInvokableCall> calls = PrepareInvoke();
            for (var i = 0; i < calls.Count; i++)
                calls[i].Invoke(parameters);
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

        object[] m_InvokeArray;

        public void AddListener(UnityAction call) => AddCall(GetDelegate(call));
        public void RemoveListener(UnityAction call) => RemoveCall(call);

        internal override BaseInvokableCall GetDelegate(object target, MethodInfo theFunction) => new InvokableCall(target, theFunction);

        static BaseInvokableCall GetDelegate(UnityAction action) => new InvokableCall(action);

        protected override MethodInfo FindMethod_Impl(string name, Type targetObjType) => GetValidMethodInfo(targetObjType, name, Type.EmptyTypes);

        internal override Type[] ArgumentTypes() => Type.EmptyTypes;

        [Unbound.UnityFrame("Invoke"), System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void Invoke()
        {
            List<BaseInvokableCall> calls = PrepareInvoke();
            for (var i = 0; i < calls.Count; i++)
            {
                if (calls[i] is InvokableCall call) call.Invoke();
                else
                {
                    m_InvokeArray ??= new object[0];
                    calls[i].Invoke(m_InvokeArray);
                }
            }
        }

        internal void AddPersistentListener(UnityAction call) => AddPersistentListener(call, UnityEventCallState.RuntimeOnly);
        internal void AddPersistentListener(UnityAction call, UnityEventCallState callState) => UaAddPersistentCall(call, callState);
    }

    [Serializable]
    public partial class UnityEvent<T0> : UnityEventBase
    {
        public UnityEvent() { }

        object[] m_InvokeArray;

        public void AddListener(UnityAction<T0> call) => AddCall(GetDelegate(call));

        internal override BaseInvokableCall GetDelegate(object target, MethodInfo theFunction) => new InvokableCall<T0>(target, theFunction);

        static BaseInvokableCall GetDelegate(UnityAction<T0> action) => new InvokableCall<T0>(action);

        internal void AddPersistentListener(UnityAction<T0> call) => AddPersistentListener(call, UnityEventCallState.RuntimeOnly);
        internal void AddPersistentListener(UnityAction<T0> call, UnityEventCallState callState) => UaAddPersistentCall(call, callState);
        public void RemoveListener(UnityAction<T0> call) => RemoveCall(call);

        protected override MethodInfo FindMethod_Impl(string name, Type targetObjType) => GetValidMethodInfo(targetObjType, name, new[] { typeof(T0) });

        internal override Type[] ArgumentTypes() => new[] { typeof(T0) };

        [Unbound.UnityFrame("Invoke"), System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void Invoke(T0 arg0)
        {
            List<BaseInvokableCall> calls = PrepareInvoke();
            for (var i = 0; i < calls.Count; i++)
            {
                if (calls[i] is InvokableCall<T0> call) call.Invoke(arg0);
                else if (calls[i] is InvokableCall staticCall) staticCall.Invoke();
                else
                {
                    m_InvokeArray ??= new object[1];
                    m_InvokeArray[0] = arg0;
                    calls[i].Invoke(m_InvokeArray);
                }
            }
        }
    }

    [Serializable]
    public partial class UnityEvent<T0, T1> : UnityEventBase
    {
        public UnityEvent() { }

        object[] m_InvokeArray;

        public void AddListener(UnityAction<T0, T1> call) => AddCall(GetDelegate(call));

        internal override BaseInvokableCall GetDelegate(object target, MethodInfo theFunction) => new InvokableCall<T0, T1>(target, theFunction);

        static BaseInvokableCall GetDelegate(UnityAction<T0, T1> action) => new InvokableCall<T0, T1>(action);

        internal void AddPersistentListener(UnityAction<T0, T1> call) => AddPersistentListener(call, UnityEventCallState.RuntimeOnly);
        internal void AddPersistentListener(UnityAction<T0, T1> call, UnityEventCallState callState) => UaAddPersistentCall(call, callState);
        public void RemoveListener(UnityAction<T0, T1> call) => RemoveCall(call);

        protected override MethodInfo FindMethod_Impl(string name, Type targetObjType) => GetValidMethodInfo(targetObjType, name, new[] { typeof(T0), typeof(T1) });

        internal override Type[] ArgumentTypes() => new[] { typeof(T0), typeof(T1) };

        [Unbound.UnityFrame("Invoke"), System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void Invoke(T0 arg0, T1 arg1)
        {
            List<BaseInvokableCall> calls = PrepareInvoke();
            for (var i = 0; i < calls.Count; i++)
            {
                if (calls[i] is InvokableCall<T0, T1> call) call.Invoke(arg0, arg1);
                else if (calls[i] is InvokableCall staticCall) staticCall.Invoke();
                else
                {
                    m_InvokeArray ??= new object[2];
                    m_InvokeArray[0] = arg0;
                    m_InvokeArray[1] = arg1;
                    calls[i].Invoke(m_InvokeArray);
                }
            }
        }
    }

    [Serializable]
    public partial class UnityEvent<T0, T1, T2> : UnityEventBase
    {
        public UnityEvent() { }

        object[] m_InvokeArray;

        public void AddListener(UnityAction<T0, T1, T2> call) => AddCall(GetDelegate(call));

        internal override BaseInvokableCall GetDelegate(object target, MethodInfo theFunction) => new InvokableCall<T0, T1, T2>(target, theFunction);

        static BaseInvokableCall GetDelegate(UnityAction<T0, T1, T2> action) => new InvokableCall<T0, T1, T2>(action);

        internal void AddPersistentListener(UnityAction<T0, T1, T2> call) => AddPersistentListener(call, UnityEventCallState.RuntimeOnly);
        internal void AddPersistentListener(UnityAction<T0, T1, T2> call, UnityEventCallState callState) => UaAddPersistentCall(call, callState);
        public void RemoveListener(UnityAction<T0, T1, T2> call) => RemoveCall(call);

        protected override MethodInfo FindMethod_Impl(string name, Type targetObjType) => GetValidMethodInfo(targetObjType, name, new[] { typeof(T0), typeof(T1), typeof(T2) });

        internal override Type[] ArgumentTypes() => new[] { typeof(T0), typeof(T1), typeof(T2) };

        [Unbound.UnityFrame("Invoke"), System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void Invoke(T0 arg0, T1 arg1, T2 arg2)
        {
            List<BaseInvokableCall> calls = PrepareInvoke();
            for (var i = 0; i < calls.Count; i++)
            {
                if (calls[i] is InvokableCall<T0, T1, T2> call) call.Invoke(arg0, arg1, arg2);
                else if (calls[i] is InvokableCall staticCall) staticCall.Invoke();
                else
                {
                    m_InvokeArray ??= new object[3];
                    m_InvokeArray[0] = arg0;
                    m_InvokeArray[1] = arg1;
                    m_InvokeArray[2] = arg2;
                    calls[i].Invoke(m_InvokeArray);
                }
            }
        }
    }

    [Serializable]
    public partial class UnityEvent<T0, T1, T2, T3> : UnityEventBase
    {
        public UnityEvent() { }

        object[] m_InvokeArray;

        public void AddListener(UnityAction<T0, T1, T2, T3> call) => AddCall(GetDelegate(call));

        internal override BaseInvokableCall GetDelegate(object target, MethodInfo theFunction) => new InvokableCall<T0, T1, T2, T3>(target, theFunction);

        static BaseInvokableCall GetDelegate(UnityAction<T0, T1, T2, T3> action) => new InvokableCall<T0, T1, T2, T3>(action);

        internal void AddPersistentListener(UnityAction<T0, T1, T2, T3> call) => AddPersistentListener(call, UnityEventCallState.RuntimeOnly);
        internal void AddPersistentListener(UnityAction<T0, T1, T2, T3> call, UnityEventCallState callState) => UaAddPersistentCall(call, callState);
        public void RemoveListener(UnityAction<T0, T1, T2, T3> call) => RemoveCall(call);

        protected override MethodInfo FindMethod_Impl(string name, Type targetObjType) => GetValidMethodInfo(targetObjType, name, new[] { typeof(T0), typeof(T1), typeof(T2), typeof(T3) });

        internal override Type[] ArgumentTypes() => new[] { typeof(T0), typeof(T1), typeof(T2), typeof(T3) };

        [Unbound.UnityFrame("Invoke"), System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
        public void Invoke(T0 arg0, T1 arg1, T2 arg2, T3 arg3)
        {
            List<BaseInvokableCall> calls = PrepareInvoke();
            for (var i = 0; i < calls.Count; i++)
            {
                if (calls[i] is InvokableCall<T0, T1, T2, T3> call) call.Invoke(arg0, arg1, arg2, arg3);
                else if (calls[i] is InvokableCall staticCall) staticCall.Invoke();
                else
                {
                    m_InvokeArray ??= new object[4];
                    m_InvokeArray[0] = arg0;
                    m_InvokeArray[1] = arg1;
                    m_InvokeArray[2] = arg2;
                    m_InvokeArray[3] = arg3;
                    calls[i].Invoke(m_InvokeArray);
                }
            }
        }
    }
}
