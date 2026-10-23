using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using System.Reflection;
using UnityEngine;

namespace Unbound.Runtime
{
    /// <summary>
    /// The Unity messages a script class implements, found as Unity finds them: by name, on the class
    /// or any base class below MonoBehaviour, whatever their visibility. Calls go through compiled
    /// delegates, not reflection.
    /// </summary>
    internal sealed class ScriptMessages
    {
        internal Action<MonoBehaviour> Awake, OnEnable, Update, LateUpdate, FixedUpdate, OnDisable, OnDestroy;
        internal Action<MonoBehaviour> OnApplicationQuit, OnGUI, OnValidate, Reset, OnPreRender, OnPostRender, OnRenderObject;
        internal Action<MonoBehaviour> OnBecameVisible, OnBecameInvisible;
        internal Action<MonoBehaviour> Start;
        internal Func<MonoBehaviour, IEnumerator> StartCoroutine;
        internal Action<MonoBehaviour, bool> OnApplicationPause, OnApplicationFocus;
        internal int ExecutionOrder;

        static readonly ConcurrentDictionary<Type, ScriptMessages> s_Cache = new ConcurrentDictionary<Type, ScriptMessages>();

        /// <summary>Execution orders from the project's script .meta files (MonoImporter executionOrder).</summary>
        internal static readonly ConcurrentDictionary<Type, int> ExecutionOrders = new ConcurrentDictionary<Type, int>();

        internal static ScriptMessages For(Type type) => s_Cache.GetOrAdd(type, Build);

        internal static void ClearCache() => s_Cache.Clear();

        static ScriptMessages Build(Type type)
        {
            var m = new ScriptMessages();
            m.Awake = Action(type, "Awake");
            m.OnEnable = Action(type, "OnEnable");
            m.Update = Action(type, "Update");
            m.LateUpdate = Action(type, "LateUpdate");
            m.FixedUpdate = Action(type, "FixedUpdate");
            m.OnDisable = Action(type, "OnDisable");
            m.OnDestroy = Action(type, "OnDestroy");
            m.OnApplicationQuit = Action(type, "OnApplicationQuit");
            m.OnGUI = Action(type, "OnGUI");
            m.OnValidate = Action(type, "OnValidate");
            m.Reset = Action(type, "Reset");
            m.OnPreRender = Action(type, "OnPreRender");
            m.OnPostRender = Action(type, "OnPostRender");
            m.OnRenderObject = Action(type, "OnRenderObject");
            m.OnBecameVisible = Action(type, "OnBecameVisible");
            m.OnBecameInvisible = Action(type, "OnBecameInvisible");
            m.OnApplicationPause = BoolAction(type, "OnApplicationPause");
            m.OnApplicationFocus = BoolAction(type, "OnApplicationFocus");
            var start = Find(type, "Start", Type.EmptyTypes);
            if (start != null)
            {
                if (typeof(IEnumerator).IsAssignableFrom(start.ReturnType))
                    m.StartCoroutine = Compile<Func<MonoBehaviour, IEnumerator>>(start, typeof(IEnumerator));
                else
                    m.Start = Compile<Action<MonoBehaviour>>(start, null);
            }
            m.ExecutionOrder = OrderOf(type);
            return m;
        }

        internal static int OrderOf(Type type)
        {
            if (ExecutionOrders.TryGetValue(type, out var order)) return order;
            var attribute = type.GetCustomAttribute<DefaultExecutionOrder>(inherit: true);
            return attribute?.order ?? 0;
        }

        /// <summary>The method Unity would call: declared on the type or a script base class.</summary>
        internal static MethodInfo Find(Type type, string name, Type[] parameters)
        {
            const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            for (var t = type; t != null && t != typeof(MonoBehaviour) && t != typeof(Behaviour); t = t.BaseType)
            {
                MethodInfo found = null;
                try
                {
                    found = t.GetMethod(name, flags, null, parameters, null);
                }
                catch (AmbiguousMatchException)
                {
                }
                if (found != null && !found.IsGenericMethodDefinition) return found;
            }
            return null;
        }

        static Action<MonoBehaviour> Action(Type type, string name)
        {
            var method = Find(type, name, Type.EmptyTypes);
            return method == null ? null : Compile<Action<MonoBehaviour>>(method, null);
        }

        static Action<MonoBehaviour, bool> BoolAction(Type type, string name)
        {
            var method = Find(type, name, new[] { typeof(bool) });
            if (method == null) return null;
            var target = Expression.Parameter(typeof(MonoBehaviour));
            var value = Expression.Parameter(typeof(bool));
            var call = Expression.Call(Expression.Convert(target, method.DeclaringType), method, value);
            return Expression.Lambda<Action<MonoBehaviour, bool>>(call, target, value).Compile();
        }

        static T Compile<T>(MethodInfo method, Type returnType) where T : Delegate
        {
            var target = Expression.Parameter(typeof(MonoBehaviour));
            Expression call = Expression.Call(Expression.Convert(target, method.DeclaringType), method);
            if (returnType != null && call.Type != returnType)
                call = Expression.Convert(call, returnType);
            return Expression.Lambda<T>(call, target).Compile();
        }
    }

    /// <summary>SendMessage, BroadcastMessage and string-named calls (Invoke, StartCoroutine(string)).</summary>
    internal static class Messaging
    {
        static readonly ConcurrentDictionary<(Type, string), MethodInfo[]> s_Methods = new ConcurrentDictionary<(Type, string), MethodInfo[]>();

        static MethodInfo[] Methods(Type type, string name) =>
            s_Methods.GetOrAdd((type, name), key =>
            {
                const BindingFlags flags = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
                var list = new List<MethodInfo>();
                for (var t = key.Item1; t != null && t != typeof(MonoBehaviour); t = t.BaseType)
                    foreach (var m in t.GetMethods(flags))
                        if (m.Name == key.Item2 && !m.IsGenericMethodDefinition && m.GetParameters().Length <= 1)
                            list.Add(m);
                return list.ToArray();
            });

        /// <summary>Calls the method on every enabled-or-not MonoBehaviour of the GameObject.</summary>
        internal static void Send(GameObject go, string methodName, object value, SendMessageOptions options, out bool received)
        {
            received = false;
            if (go == null || go.m_Destroyed) return;
            var components = go.m_Components.ToArray();
            foreach (var c in components)
            {
                if (c.m_Destroyed || !(c is MonoBehaviour mb)) continue;
                if (TryCall(mb, methodName, value, out _)) received = true;
            }
            if (!received && options == SendMessageOptions.RequireReceiver)
                Debug.LogError($"SendMessage {methodName} has no receiver!", go);
        }

        internal static void Broadcast(GameObject go, string methodName, object value, SendMessageOptions options)
        {
            bool any = false;
            foreach (var target in Descendants(go.m_Transform))
            {
                if (!target.activeInHierarchy && target != go) continue;
                Send(target, methodName, value, SendMessageOptions.DontRequireReceiver, out var received);
                any |= received;
            }
            if (!any && options == SendMessageOptions.RequireReceiver)
                Debug.LogError($"BroadcastMessage {methodName} has no receiver!", go);
        }

        static IEnumerable<GameObject> Descendants(Transform root)
        {
            yield return root.m_GameObject;
            foreach (var child in root.m_Children.ToArray())
                foreach (var go in Descendants(child))
                    yield return go;
        }

        static readonly ConcurrentDictionary<(Type, string), Action<MonoBehaviour>> s_Fast = new ConcurrentDictionary<(Type, string), Action<MonoBehaviour>>();

        internal static void ClearCaches()
        {
            s_Fast.Clear();
            s_Methods.Clear();
        }

        /// <summary>A parameterless engine message (OnRectTransformDimensionsChange, ...) to the
        /// scripts of one GameObject, through cached compiled calls.</summary>
        internal static void SendFast(GameObject go, string methodName)
        {
            var components = go.m_Components;
            for (int i = 0; i < components.Count; i++)
            {
                if (!(components[i] is MonoBehaviour mb) || mb.m_Destroyed) continue;
                var call = s_Fast.GetOrAdd((mb.GetType(), methodName), key =>
                {
                    var method = ScriptMessages.Find(key.Item1, key.Item2, Type.EmptyTypes);
                    if (method == null) return null;
                    var target = System.Linq.Expressions.Expression.Parameter(typeof(MonoBehaviour));
                    var body = System.Linq.Expressions.Expression.Call(System.Linq.Expressions.Expression.Convert(target, method.DeclaringType), method);
                    return System.Linq.Expressions.Expression.Lambda<Action<MonoBehaviour>>(body, target).Compile();
                });
                if (call == null) continue;
                try { call(mb); }
                catch (Exception e) { Debug.LogException(e, mb); }
            }
        }

        /// <summary>Sends a parameterless message to every component of a hierarchy.</summary>
        internal static void SendToHierarchy(Transform root, string methodName)
        {
            foreach (var go in Descendants(root))
                SendFast(go, methodName);
        }

        internal static bool TryCall(MonoBehaviour target, string methodName, object value, out object result)
        {
            result = null;
            var methods = Methods(target.GetType(), methodName);
            if (methods.Length == 0) return false;
            MethodInfo chosen = null;
            foreach (var m in methods)
            {
                var parameters = m.GetParameters();
                if (value == null && parameters.Length == 0) { chosen = m; break; }
                if (parameters.Length == 1 && (value == null ? !parameters[0].ParameterType.IsValueType : parameters[0].ParameterType.IsInstanceOfType(value)))
                {
                    chosen = m;
                    if (value != null) break;
                }
            }
            if (chosen == null)
            {
                // Unity calls a parameterless overload even when a value was sent.
                foreach (var m in methods)
                    if (m.GetParameters().Length == 0) { chosen = m; break; }
            }
            if (chosen == null) return false;
            try
            {
                result = chosen.Invoke(target, chosen.GetParameters().Length == 0 ? null : new[] { value });
            }
            catch (TargetInvocationException e)
            {
                Debug.LogException(e.InnerException ?? e, target);
            }
            if (result is IEnumerator routine && target.isActiveAndEnabled)
                Coroutines.Start(target, routine, methodName);
            return true;
        }

        internal static IEnumerator InvokeForEnumerator(MonoBehaviour target, string methodName, object value)
        {
            foreach (var m in Methods(target.GetType(), methodName))
            {
                if (!typeof(IEnumerator).IsAssignableFrom(m.ReturnType)) continue;
                var parameters = m.GetParameters();
                try
                {
                    if (parameters.Length == 0) return (IEnumerator)m.Invoke(target, null);
                    if (parameters.Length == 1) return (IEnumerator)m.Invoke(target, new[] { value });
                }
                catch (TargetInvocationException e)
                {
                    Debug.LogException(e.InnerException ?? e, target);
                    return null;
                }
            }
            return null;
        }
    }
}
