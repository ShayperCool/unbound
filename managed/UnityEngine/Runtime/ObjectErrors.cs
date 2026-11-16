using System;
using UnityEngine;
using Object = UnityEngine.Object;

namespace Unbound.Runtime
{
    /// <summary>
    /// The exceptions and messages of Unity's object API errors, one place per message family
    /// (Unity 6000.0.71f1, tests/projects/objecterrors).
    /// </summary>
    internal static class ObjectErrors
    {
        const string DestroyedSuffix = "' has been destroyed but you are still trying to access it.\n" +
                                       "Your script should either check if it is null or you should not destroy the object.";
        const string ParameterSuffix = " Parameter name: ";
        const string MissingReferencePrefix = "MissingReferenceException:";

        /// <summary>The text of a null reference the Editor's Mono runtime raises (it has no full stop).</summary>
        internal const string RuntimeNullReference = "Object reference not set to an instance of an object";

        /// <summary>What a member of a dead object throws: a destroyed object, or one the engine
        /// never made. Unity's bindings read the object's error string ("Kind:message", written
        /// for the Editor's placeholder objects) and fall back to a plain NullReferenceException;
        /// a destroyed object gets the destroyed-object message (the release's reference
        /// assemblies, MarshalledUnityObject).</summary>
        internal static Exception Receiver(Object o) => Editor(o, null) ?? new NullReferenceException();

        /// <summary>What a binding throws for an object argument it requires that is null or dead:
        /// the same messages with the parameter's name after them, else an ArgumentNullException
        /// of the parameter. The Editor's exception has both the parameter name (tests/projects/vfx
        /// reads it) and the Mono runtime's message ("Value cannot be null.\nParameter name: x");
        /// .NET writes the name into the message itself whenever it is set, so this one keeps the
        /// name and its message ends " (Parameter 'x')".</summary>
        internal static Exception Argument(Object o, string parameterName) =>
            (ReferenceEquals(o, null) ? null : Editor(o, parameterName)) ?? new ArgumentNullException(parameterName);

        static Exception Editor(Object o, string parameterName)
        {
            string error = o.m_UnityRuntimeErrorString ?? "";
            if (o.m_InstanceID != 0 && !error.StartsWith(MissingReferencePrefix, StringComparison.Ordinal))
                return new MissingReferenceException(WithParameter("The object of type '" + o.GetType().FullName + DestroyedSuffix, parameterName));
            int colon = error.IndexOf(':');
            if (colon <= 0) return null;
            string message = WithParameter(error.Substring(colon + 1), parameterName);
            return error.Substring(0, colon) switch
            {
                "MissingReferenceException" => new MissingReferenceException(message),
                "MissingComponentException" => new MissingComponentException(message),
                "UnassignedReferenceException" => new UnassignedReferenceException(message),
                // Unity creates any other kind its string names by reflection; none is known.
                _ => null,
            };
        }

        static string WithParameter(string message, string parameterName) =>
            string.IsNullOrEmpty(parameterName) ? message : message + ParameterSuffix + parameterName;

        // ------------------------------------------------------------------ component types

        internal const string TypeCannotBeNull = "Type cannot be null.";

        /// <summary>The type check of GetComponent and its relatives: a component class or an
        /// interface; null when the type is one.</summary>
        internal static Exception ComponentQueryType(Type type)
        {
            if (type == null) return new ArgumentException(TypeCannotBeNull);
            if (typeof(Component).IsAssignableFrom(type) || type.IsInterface) return null;
            return new ArgumentException($"GetComponent requires that the requested component '{type.Name}' derives from MonoBehaviour or Component or is an interface.");
        }

        /// <summary>Whether a generic query's type is valid, decided once per type; the exception
        /// itself is made when thrown.</summary>
        static class ComponentQuery<T>
        {
            internal static readonly bool Valid = ComponentQueryType(typeof(T)) == null;
        }

        internal static void CheckComponentQuery<T>()
        {
            if (!ComponentQuery<T>.Valid) throw ComponentQueryType(typeof(T));
        }

        internal static void CheckComponentQuery(Type type)
        {
            var error = ComponentQueryType(type);
            if (error != null) throw error;
        }

        // ------------------------------------------------------------------ finding objects

        /// <summary>The finders' type check: an engine object class, else the engine's error is
        /// logged.</summary>
        internal static bool FindTypeIsValid(Type type)
        {
            if (type != null && typeof(Object).IsAssignableFrom(type)) return true;
            Log.Engine(LogType.Error, type == null ? "FindAllObjectsOfType: Invalid Type"
                : $"FindAllObjectsOfType: The type has to be derived from UnityEngine.Object. Type is {type.Name}.", null);
            return false;
        }

        // ------------------------------------------------------------------ tags

        internal const string TagNullOrEmpty = "Tag: tag name is null or empty.";

        internal static string TagNotDefined(string tag) => $"Tag: {tag} is not defined.";

        /// <summary>The tag error of a tag the project lacks (null when it is defined): the empty
        /// name has its own message.</summary>
        internal static string TagError(string tag) =>
            string.IsNullOrEmpty(tag) ? TagNullOrEmpty : TagManager.IsDefined(tag) ? null : TagNotDefined(tag);

        /// <summary>A tag lookup (FindWithTag and FindGameObjectsWithTag): the empty name throws
        /// ArgumentException, an undefined one UnityException.</summary>
        internal static void CheckTagLookup(string tag)
        {
            if (string.IsNullOrEmpty(tag)) throw new ArgumentException(TagNullOrEmpty);
            if (!TagManager.IsDefined(tag)) throw new UnityException(TagNotDefined(tag));
        }
    }

    /// <summary>
    /// The receiver check of game code's calls to engine members whose Unity binding refuses a
    /// dead object (Unbound.Host's lifetime checks call it before such a member): a null
    /// reference fails as the Mono runtime's own null reference does, a destroyed or placeholder
    /// object with its <see cref="ObjectErrors.Receiver"/> exception.
    /// </summary>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static class Lifetime
    {
        /// <summary>The name of the type holding a game assembly's checked calls; stack traces
        /// leave its frames out.</summary>
        public const string CallsType = "<UnboundLifetimeChecks>";

        [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining), System.Diagnostics.StackTraceHidden]
        public static void Check(Object receiver)
        {
            if (receiver is null) ThrowNull();
            if (receiver.m_Destroyed) ThrowDead(receiver);
        }

        [System.Diagnostics.StackTraceHidden]
        static void ThrowNull() => throw new NullReferenceException(ObjectErrors.RuntimeNullReference);

        [System.Diagnostics.StackTraceHidden]
        static void ThrowDead(Object receiver) => throw ObjectErrors.Receiver(receiver);
    }
}
