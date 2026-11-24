using System;
using System.Collections.Generic;
using System.Reflection;
using Unbound.Runtime;
using UnityEngine.LowLevel;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>Engine actions registered at a player loop node while that node runs (an engine
    /// system first used inside another system's action registers itself then).</summary>
    [Collection("Frames")]
    public class PlayerLoopRegistrationTests
    {
        const BindingFlags StaticPrivate = BindingFlags.Static | BindingFlags.NonPublic;
        static Dictionary<Type, List<Action>> Engine =>
            (Dictionary<Type, List<Action>>)typeof(PlayerLoopDriver).GetField("s_Engine", StaticPrivate).GetValue(null);

        static void Run(Type node)
        {
            try
            {
                typeof(PlayerLoopDriver).GetMethod("RunSystem", StaticPrivate)
                    .Invoke(null, new object[] { new PlayerLoopSystem { type = node } });
            }
            catch (TargetInvocationException e) when (e.InnerException != null)
            {
                throw e.InnerException;
            }
        }

        // Nodes of these tests' own, which no engine system registers at.
        sealed class LastNode { }
        sealed class FirstNode { }

        sealed class LogCapture : ILogHandler
        {
            internal readonly List<string> Lines = new List<string>();
            public void LogException(Exception exception, Object context) => Lines.Add("Exception: " + exception.Message);
            public void LogFormat(LogType logType, Object context, string format, params object[] args) =>
                Lines.Add(logType + ": " + string.Format(format, args));
        }

        [Fact]
        public void ARegistrationWhileItsNodeRunsTakesEffectFromTheNextRun()
        {
            var logs = new LogCapture();
            var previous = Debug.unityLogger.logHandler;
            Debug.unityLogger.logHandler = logs;
            var ran = new List<string>();
            try
            {
                bool registered = false;
                PlayerLoopDriver.Register(typeof(LastNode), () =>
                {
                    ran.Add("first");
                    if (registered) return;
                    registered = true;
                    PlayerLoopDriver.Register(typeof(LastNode), () => ran.Add("added last"));
                    PlayerLoopDriver.RegisterFirst(typeof(LastNode), () => ran.Add("added first"));
                });
                PlayerLoopDriver.Register(typeof(LastNode), () => ran.Add("second"));
                var running = Engine[typeof(LastNode)];
                Run(typeof(LastNode));
                // The run finished over the list it began with, which is left unchanged.
                Assert.Equal(new[] { "first", "second" }, ran);
                Assert.Equal(2, running.Count);
                Assert.Empty(logs.Lines);
                ran.Clear();
                Run(typeof(LastNode));
                Assert.Equal(new[] { "added first", "first", "second", "added last" }, ran);
                Assert.Empty(logs.Lines);
            }
            finally
            {
                Debug.unityLogger.logHandler = previous;
                Engine.Remove(typeof(LastNode));
            }
        }

        [Fact]
        public void ARegistrationAtAnotherNodeWhileANodeRunsRunsAtThatNode()
        {
            var ran = new List<string>();
            try
            {
                PlayerLoopDriver.Register(typeof(LastNode), () =>
                    PlayerLoopDriver.Register(typeof(FirstNode), () => ran.Add("other")));
                Run(typeof(LastNode));
                Run(typeof(FirstNode));
                Assert.Equal(new[] { "other" }, ran);
            }
            finally
            {
                Engine.Remove(typeof(LastNode));
                Engine.Remove(typeof(FirstNode));
            }
        }
    }
}
