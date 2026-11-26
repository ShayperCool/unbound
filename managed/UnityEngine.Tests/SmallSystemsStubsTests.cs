using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.XR;
using Xunit;
using Object = UnityEngine.Object;

namespace UnityEngine.Tests
{
    /// <summary>Members of the smaller systems that were stubs or absent, answered as Unity 6000.0's
    /// own C# answers them (UnityCsReference 6000.0).</summary>
    public class SmallSystemsStubsTests
    {
        sealed class Capture : ILogHandler
        {
            internal readonly List<string> Logs = new List<string>();
            public void LogException(Exception exception, Object context) => Logs.Add(exception.Message);
            public void LogFormat(LogType type, Object context, string format, params object[] args) => Logs.Add(type + ": " + string.Format(format, args));
        }

        [Fact]
        public void VisualElementDeclaresTheFinalizerSubclassesCall()
        {
            // A subclass's ~X() compiled against Unity's assemblies calls VisualElement::Finalize
            // (Unity's VisualElement declares ~VisualElement()); without it the finalizer thread
            // fails to bind the call.
            var finalize = typeof(VisualElement).GetMethod("Finalize", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
            Assert.NotNull(finalize);
            Assert.Equal(typeof(VisualElement), finalize.DeclaringType);
        }

        [Fact]
        public void AnIntegratedSubsystemWithoutANativeObjectIsNotRunning()
        {
            var logs = new Capture();
            var handler = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = logs;
            try
            {
                Unbound.Runtime.Stubs.Forget("UnityEngine.IntegratedSubsystem.running");
                Assert.False(new XRInputSubsystem().running);
                Assert.Empty(logs.Logs);
                Assert.DoesNotContain(Unbound.Runtime.Stubs.HitReport(), h => h.Key == "UnityEngine.IntegratedSubsystem.running");
            }
            finally { Debug.unityLogger.logHandler = handler; }
        }
    }
}
