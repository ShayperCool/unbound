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
        public void ARegistrationWhileItsNodeRunsKeepsTheRunsList()
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
                // The list the run began with is left unchanged; the appended action runs in the
                // same pass, as it would had it been registered before; the one put first waits.
                Assert.Equal(new[] { "first", "second", "added last" }, ran);
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

        /// <summary>A system first used inside an action of its own node runs in that pass, as it
        /// does once it is registered: the frame it serves does not depend on whether it was
        /// used before.</summary>
        [Fact]
        public void ALazilyRegisteredSystemRunsInThePassItIsFirstUsedIn()
        {
            var ran = new List<string>();
            bool registered = false;
            void Use(string request)
            {
                ran.Add(request);
                if (registered) return;
                registered = true;
                PlayerLoopDriver.Register(typeof(LastNode), () => ran.Add("served"));
            }
            try
            {
                PlayerLoopDriver.Register(typeof(LastNode), () => Use("first use"));
                Run(typeof(LastNode));
                Run(typeof(LastNode));
                Assert.Equal(new[] { "first use", "served", "first use", "served" }, ran);
                // Appended twice in one pass (a system registering two actions): both run.
                ran.Clear();
                Engine.Remove(typeof(LastNode));
                PlayerLoopDriver.Register(typeof(LastNode), () =>
                {
                    ran.Add("user");
                    if (ran.Count > 1) return;
                    PlayerLoopDriver.Register(typeof(LastNode), () => ran.Add("a"));
                    PlayerLoopDriver.Register(typeof(LastNode), () =>
                    {
                        ran.Add("b");
                        if (ran.Count < 4) PlayerLoopDriver.Register(typeof(LastNode), () => ran.Add("c"));
                    });
                });
                Run(typeof(LastNode));
                Assert.Equal(new[] { "user", "a", "b", "c" }, ran);
            }
            finally
            {
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
