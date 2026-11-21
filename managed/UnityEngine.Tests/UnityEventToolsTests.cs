using System;
using Tools = UnityEditor.Events.UnityEventTools;
using UnityEngine.Events;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>UnityEventTools' edits of persistent listeners, as the Unity 6000.0.71f1 Editor makes
    /// them (tests/projects/unityeventtools).</summary>
    public sealed class UnityEventToolsTests
    {
        sealed class Listener : MonoBehaviour
        {
            public void Ping() { }
            public void TakeInt(int value) { }
            public void TakeObject(Object value) { }
            public void TakeTransform(Transform value) { }
            public static void StaticPing() { }
        }

        static Listener NewListener(string name) => new GameObject(name).AddComponent<Listener>();

        [Fact]
        public void AnArgumentListenerStoresItsModeAndArgumentAndUnregisteringKeepsThem()
        {
            var listener = NewListener("L");
            var unityEvent = new UnityEvent();
            try
            {
                Tools.AddIntPersistentListener(unityEvent, listener.TakeInt, 42);
                Assert.Equal(1, unityEvent.GetPersistentEventCount());
                Assert.Same(listener, unityEvent.GetPersistentTarget(0));
                Assert.Equal("TakeInt", unityEvent.GetPersistentMethodName(0));
                Assert.Equal(42, unityEvent.PersistentArguments(0).intArgument);
                // Registering another mode over it replaces the method, mode and argument.
                Tools.RegisterVoidPersistentListener(unityEvent, 0, listener.Ping);
                Assert.Equal("Ping", unityEvent.GetPersistentMethodName(0));
                Tools.RegisterIntPersistentListener(unityEvent, 0, listener.TakeInt, 5);
                Tools.UnregisterPersistentListener(unityEvent, 0);
                Assert.Equal(1, unityEvent.GetPersistentEventCount());
                Assert.Null(unityEvent.GetPersistentTarget(0));
                Assert.Equal("", unityEvent.GetPersistentMethodName(0));
                Assert.Equal(5, unityEvent.PersistentArguments(0).intArgument);
            }
            finally
            {
                Object.DestroyImmediate(listener.gameObject);
            }
        }

        [Fact]
        public void ACallbackWithoutAnObjectToRunOnThrowsAndLeavesItsEmptyCall()
        {
            var unityEvent = new UnityEvent();
            var error = Assert.Throws<ArgumentException>(() => Tools.AddPersistentListener(unityEvent, () => { }));
            Assert.Matches(@"^Could not register callback .+ on \. The class null does not derive from UnityEngine.Object$", error.Message);
            Assert.Equal(1, unityEvent.GetPersistentEventCount());
            Assert.Equal("", unityEvent.GetPersistentMethodName(0) ?? "");

            // A destroyed target names its class.
            var gone = NewListener("Gone");
            Object.DestroyImmediate(gone.gameObject);
            error = Assert.Throws<ArgumentException>(() => Tools.AddPersistentListener(unityEvent, gone.Ping));
            Assert.Equal($"Could not register callback Ping on null. The class {typeof(Listener)} does not derive from UnityEngine.Object", error.Message);

            // A static method needs no object.
            Tools.AddPersistentListener(unityEvent, Listener.StaticPing);
            Assert.Equal("StaticPing", unityEvent.GetPersistentMethodName(2));
            Assert.Null(unityEvent.GetPersistentTarget(2));
        }

        sealed class LogCapture : ILogHandler
        {
            internal readonly System.Collections.Generic.List<string> Lines = new System.Collections.Generic.List<string>();
            public void LogException(Exception exception, Object context) => Lines.Add("Exception: " + exception.Message);
            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                Lines.Add(logType + ": " + string.Format(format, args));
        }

        [Fact]
        public void TheArgumentModesNameTheDelegatesOwnTargetAndCheckTheArgumentsType()
        {
            var listener = NewListener("L");
            var unityEvent = new UnityEvent();
            var logs = new LogCapture();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = logs;
            try
            {
                var error = Assert.Throws<ArgumentException>(() => Tools.AddIntPersistentListener(unityEvent, x => { }, 1));
                Assert.Matches(@"^Could not register callback .+ on .+\+<>c\. The class .+\+<>c does not derive from UnityEngine.Object$", error.Message);
                // A typed object listener with a null argument must take UnityEngine.Object.
                Tools.AddObjectPersistentListener<Transform>(unityEvent, listener.TakeTransform, null);
                Assert.Equal(2, unityEvent.GetPersistentEventCount());
                Assert.Equal("", unityEvent.GetPersistentMethodName(1) ?? "");
                Assert.Equal(new[] { $"Warning: Could not register listener {listener}.Void TakeTransform(UnityEngine.Transform) on {typeof(UnityEvent)} the method could not be found." }, logs.Lines);
                Assert.Throws<ArgumentNullException>(() => Tools.AddObjectPersistentListener<Object>(unityEvent, null, listener));
                Assert.Equal(3, unityEvent.GetPersistentEventCount());
                logs.Lines.Clear();
                Tools.AddVoidPersistentListener(unityEvent, null);
                Assert.Equal(new[] { "Warning: Registering a Listener requires an action" }, logs.Lines);
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
                Object.DestroyImmediate(listener.gameObject);
            }
        }

        [Fact]
        public void IndicesOutOfRangeThrowAndANullCallbackOnlyWarns()
        {
            var listener = NewListener("L");
            var unityEvent = new UnityEvent();
            try
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => Tools.RegisterPersistentListener(unityEvent, 3, listener.Ping));
                Assert.Throws<ArgumentOutOfRangeException>(() => Tools.RemovePersistentListener(unityEvent, 0));
                Assert.Throws<ArgumentOutOfRangeException>(() => Tools.UnregisterPersistentListener(unityEvent, 0));
                Assert.Equal(0, unityEvent.GetPersistentEventCount());
                Tools.AddPersistentListener(unityEvent, (UnityAction)null);
                Assert.Equal(1, unityEvent.GetPersistentEventCount());
                Assert.Null(unityEvent.GetPersistentTarget(0));
            }
            finally
            {
                Object.DestroyImmediate(listener.gameObject);
            }
        }

        [Fact]
        public void AnObjectArgumentNamesItsOwnTypeAndRemovingByDelegateTakesEveryMatch()
        {
            var listener = NewListener("L");
            var other = NewListener("O");
            var unityEvent = new UnityEvent();
            try
            {
                Tools.AddObjectPersistentListener<Object>(unityEvent, listener.TakeObject, other);
                Tools.AddObjectPersistentListener<Object>(unityEvent, listener.TakeObject, null);
                Assert.Equal(typeof(Listener).AssemblyQualifiedName, unityEvent.PersistentArguments(0).unityObjectArgumentAssemblyTypeName);
                Assert.Equal("", unityEvent.PersistentArguments(1).unityObjectArgumentAssemblyTypeName);

                Tools.AddPersistentListener(unityEvent, listener.Ping);
                Tools.AddPersistentListener(unityEvent, other.Ping);
                Tools.AddPersistentListener(unityEvent, listener.Ping);
                Tools.RemovePersistentListener(unityEvent, listener.Ping);
                Assert.Equal(3, unityEvent.GetPersistentEventCount());
                Assert.Same(other, unityEvent.GetPersistentTarget(2));
                Tools.RemovePersistentListener(unityEvent, 0);
                Assert.Equal(2, unityEvent.GetPersistentEventCount());
            }
            finally
            {
                Object.DestroyImmediate(listener.gameObject);
                Object.DestroyImmediate(other.gameObject);
            }
        }
    }
}
