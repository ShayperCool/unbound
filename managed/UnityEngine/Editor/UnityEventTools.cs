using UnityEngine;
using UnityEngine.Events;

namespace UnityEditor.Events
{
    /// <summary>
    /// Editor-time edits of an event's persistent listeners. An Add method appends an empty call
    /// and registers it; a Register method points an existing call at a delegate's method in a
    /// mode (the event's own arguments, none, or one stored argument); a call whose method has no
    /// UnityEngine.Object to run on throws, a null delegate is a warning, and the call stays
    /// empty. Unregister forgets a call's target and method; Remove takes the call out. Matches
    /// the Unity 6000.0.71f1 Editor, tests/projects/unityeventtools.
    /// </summary>
    public static partial class UnityEventTools
    {
        public static void AddPersistentListener(UnityEventBase unityEvent) => unityEvent.AddPersistentListener();

        public static void RemovePersistentListener(UnityEventBase unityEvent, int index) => unityEvent.RemovePersistentListener(index);

        public static void AddPersistentListener(UnityEvent unityEvent, UnityAction call) =>
            RegisterPersistentListener(unityEvent, Append(unityEvent), call);

        public static void AddPersistentListener<T0>(UnityEvent<T0> unityEvent, UnityAction<T0> call) =>
            RegisterPersistentListener(unityEvent, Append(unityEvent), call);

        public static void AddPersistentListener<T0, T1>(UnityEvent<T0, T1> unityEvent, UnityAction<T0, T1> call) =>
            RegisterPersistentListener(unityEvent, Append(unityEvent), call);

        public static void AddPersistentListener<T0, T1, T2>(UnityEvent<T0, T1, T2> unityEvent, UnityAction<T0, T1, T2> call) =>
            RegisterPersistentListener(unityEvent, Append(unityEvent), call);

        public static void AddPersistentListener<T0, T1, T2, T3>(UnityEvent<T0, T1, T2, T3> unityEvent, UnityAction<T0, T1, T2, T3> call) =>
            RegisterPersistentListener(unityEvent, Append(unityEvent), call);

        public static void RegisterPersistentListener(UnityEvent unityEvent, int index, UnityAction call) =>
            unityEvent.RegisterPersistentListener(index, call, PersistentListenerMode.EventDefined);

        public static void RegisterPersistentListener<T0>(UnityEvent<T0> unityEvent, int index, UnityAction<T0> call) =>
            unityEvent.RegisterPersistentListener(index, call, PersistentListenerMode.EventDefined);

        public static void RegisterPersistentListener<T0, T1>(UnityEvent<T0, T1> unityEvent, int index, UnityAction<T0, T1> call) =>
            unityEvent.RegisterPersistentListener(index, call, PersistentListenerMode.EventDefined);

        public static void RegisterPersistentListener<T0, T1, T2>(UnityEvent<T0, T1, T2> unityEvent, int index, UnityAction<T0, T1, T2> call) =>
            unityEvent.RegisterPersistentListener(index, call, PersistentListenerMode.EventDefined);

        public static void RegisterPersistentListener<T0, T1, T2, T3>(UnityEvent<T0, T1, T2, T3> unityEvent, int index, UnityAction<T0, T1, T2, T3> call) =>
            unityEvent.RegisterPersistentListener(index, call, PersistentListenerMode.EventDefined);

        /// <summary>Takes out every persistent call of the delegate's target and method, whatever its mode.</summary>
        public static void RemovePersistentListener(UnityEventBase unityEvent, UnityAction call) =>
            unityEvent.RemovePersistentListener(call.Target as Object, call.Method);

        public static void RemovePersistentListener<T0>(UnityEventBase unityEvent, UnityAction<T0> call) =>
            unityEvent.RemovePersistentListener(call.Target as Object, call.Method);

        public static void RemovePersistentListener<T0, T1>(UnityEventBase unityEvent, UnityAction<T0, T1> call) =>
            unityEvent.RemovePersistentListener(call.Target as Object, call.Method);

        public static void RemovePersistentListener<T0, T1, T2>(UnityEventBase unityEvent, UnityAction<T0, T1, T2> call) =>
            unityEvent.RemovePersistentListener(call.Target as Object, call.Method);

        public static void RemovePersistentListener<T0, T1, T2, T3>(UnityEventBase unityEvent, UnityAction<T0, T1, T2, T3> call) =>
            unityEvent.RemovePersistentListener(call.Target as Object, call.Method);

        public static void UnregisterPersistentListener(UnityEventBase unityEvent, int index) => unityEvent.UnregisterPersistentListener(index);

        public static void AddVoidPersistentListener(UnityEventBase unityEvent, UnityAction call) =>
            RegisterVoidPersistentListener(unityEvent, Append(unityEvent), call);

        public static void RegisterVoidPersistentListener(UnityEventBase unityEvent, int index, UnityAction call) =>
            unityEvent.RegisterPersistentListener(index, call, PersistentListenerMode.Void);

        public static void AddIntPersistentListener(UnityEventBase unityEvent, UnityAction<int> call, int argument) =>
            RegisterIntPersistentListener(unityEvent, Append(unityEvent), call, argument);

        public static void RegisterIntPersistentListener(UnityEventBase unityEvent, int index, UnityAction<int> call, int argument)
        {
            if (unityEvent.RegisterPersistentListener(index, call, PersistentListenerMode.Int))
                unityEvent.PersistentArguments(index).intArgument = argument;
        }

        public static void AddFloatPersistentListener(UnityEventBase unityEvent, UnityAction<float> call, float argument) =>
            RegisterFloatPersistentListener(unityEvent, Append(unityEvent), call, argument);

        public static void RegisterFloatPersistentListener(UnityEventBase unityEvent, int index, UnityAction<float> call, float argument)
        {
            if (unityEvent.RegisterPersistentListener(index, call, PersistentListenerMode.Float))
                unityEvent.PersistentArguments(index).floatArgument = argument;
        }

        public static void AddBoolPersistentListener(UnityEventBase unityEvent, UnityAction<bool> call, bool argument) =>
            RegisterBoolPersistentListener(unityEvent, Append(unityEvent), call, argument);

        public static void RegisterBoolPersistentListener(UnityEventBase unityEvent, int index, UnityAction<bool> call, bool argument)
        {
            if (unityEvent.RegisterPersistentListener(index, call, PersistentListenerMode.Bool))
                unityEvent.PersistentArguments(index).boolArgument = argument;
        }

        public static void AddStringPersistentListener(UnityEventBase unityEvent, UnityAction<string> call, string argument) =>
            RegisterStringPersistentListener(unityEvent, Append(unityEvent), call, argument);

        public static void RegisterStringPersistentListener(UnityEventBase unityEvent, int index, UnityAction<string> call, string argument)
        {
            if (unityEvent.RegisterPersistentListener(index, call, PersistentListenerMode.String))
                unityEvent.PersistentArguments(index).stringArgument = argument;
        }

        public static void AddObjectPersistentListener<T>(UnityEventBase unityEvent, UnityAction<T> call, T argument) where T : Object =>
            RegisterObjectPersistentListener(unityEvent, Append(unityEvent), call, argument);

        public static void RegisterObjectPersistentListener<T>(UnityEventBase unityEvent, int index, UnityAction<T> call, T argument) where T : Object
        {
            if (unityEvent.RegisterPersistentListener(index, call, PersistentListenerMode.Object, typeof(T)))
                unityEvent.PersistentArguments(index).SetObjectArgument(argument);
        }

        /// <summary>Appends an empty persistent call; its index.</summary>
        static int Append(UnityEventBase unityEvent)
        {
            unityEvent.AddPersistentListener();
            return unityEvent.GetPersistentEventCount() - 1;
        }
    }
}
