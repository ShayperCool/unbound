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

        const BindingFlags InstanceMembers = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance;

        /// <summary>The constructor the engine runs when it creates an object of a class: the class's
        /// own parameterless one (with its field initializers), else, for a script class whose
        /// constructors all take arguments, the one of the engine class it derives from. Such a
        /// script is created anyway: none of its own constructors or those of script base classes
        /// run, their fields stay at their defaults until deserialization, and nothing is logged
        /// (Unity 6000.0.71f1, tests/projects/gameinterop).</summary>
        static ConstructorInfo EngineConstructor(Type type)
        {
            var own = type.GetConstructor(InstanceMembers, binder: null, Type.EmptyTypes, modifiers: null);
            if (own != null || !typeof(Object).IsAssignableFrom(type)) return own;
            for (var t = type.BaseType; t != null; t = t.BaseType)
                if (t.Assembly == typeof(Object).Assembly)
                    return t.GetConstructor(InstanceMembers, binder: null, Type.EmptyTypes, modifiers: null);
            return null;
        }

        /// <summary>An object of a class as the engine creates it (see <see cref="EngineConstructor"/>),
        /// without the uncreated-wrapper association of <see cref="CreateWithBacking"/>.</summary>
        internal static Object Create(Type type)
        {
            var constructor = EngineConstructor(type) ?? throw new MissingMethodException(type.FullName, ".ctor");
            if (constructor.DeclaringType == type) return (Object)Activator.CreateInstance(type, nonPublic: true);
            var instance = (Object)RuntimeHelpers.GetUninitializedObject(type);
            try
            {
                constructor.Invoke(instance, BindingFlags.Default, binder: null, Array.Empty<object>(), culture: null);
            }
            catch (TargetInvocationException error)
            {
                throw error.InnerException ?? error;
            }
            return instance;
        }

        internal static Object CreateWithBacking(Type type, string gameObjectName = null)
        {
            if (!Compat.UncreatedObjectWrappersMeasured)
                return Create(type);
            var constructor = EngineConstructor(type)
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
