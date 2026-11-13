using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    internal static class ObjectConstruction
    {
        [ThreadStatic] static Request t_Request;

        sealed class Request
        {
            internal readonly Object Instance;
            internal bool Consumed;
            internal Request(Object instance) { Instance = instance; }
        }

        internal static bool TakeBacking(Object instance)
        {
            var request = t_Request;
            if (request == null || request.Consumed || !ReferenceEquals(request.Instance, instance)) return false;
            request.Consumed = true;
            return true;
        }

        internal static Object CreateWithBacking(Type type, string gameObjectName = null)
        {
            if (!Compat.UncreatedObjectWrappersMeasured)
                return (Object)Activator.CreateInstance(type, nonPublic: true);
            var constructor = type.GetConstructor(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance,
                binder: null, Type.EmptyTypes, modifiers: null)
                ?? throw new MissingMethodException(type.FullName, ".ctor");
            // Associate this exact instance before its field initializers can construct
            // another wrapper, including one of the same type.
            var instance = (Object)RuntimeHelpers.GetUninitializedObject(type);
            var previous = t_Request;
            t_Request = new Request(instance);
            try
            {
                var context = typeof(UnityEngine.Component).IsAssignableFrom(type)
                    && !typeof(UnityEngine.MonoBehaviour).IsAssignableFrom(type) ? null : type;
                using var construction = MainThreadCheck.Construct(context, gameObjectName);
                constructor.Invoke(instance, BindingFlags.Default, binder: null, Array.Empty<object>(), culture: null);
                return instance;
            }
            catch (TargetInvocationException error)
            {
                // A constructor error is reported while the factory retains its object
                // (defaultstate, 6000.0.71f1).
                instance.PreserveCreationIdentity();
                UnityEngine.Debug.LogException(error.InnerException ?? error);
                return instance;
            }
            finally { t_Request = previous; }
        }
    }
}
