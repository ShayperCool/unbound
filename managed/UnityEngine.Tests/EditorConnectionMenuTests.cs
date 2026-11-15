using System;
using System.Collections.Concurrent;
using System.Reflection;
using System.Linq;
using Unbound.Runtime;
using UnityEditor;
using UnityEditor.Networking.PlayerConnection;
using UnityEngine.Events;
using UnityEngine.Networking.PlayerConnection;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class EditorConnectionMenuTests
    {
        public class Receiver : ScriptableObject
        {
            public int Sum;
            public void State(int value) { Sum += value; }
            public void Message(MessageEventArgs value) { Sum += value.playerId; }
        }
        class PlainReceiver { public void State(int value) { } }
        static int s_StaticSum;
        static void StaticState(int value) { s_StaticSum += value; }

        [Fact]
        public void DynamicMenuValidationCheckmarksReplacementAndSubtreeRemovalShareOneRegistry()
        {
            using var engine = MainThreadCheck.Engine();
            const string root = "Unit registry menu";
            const string path = root + "/Branch/Command";
            bool enabled = false;
            int calls = 0;
            try
            {
                Menu.AddMenuItem(path, "", true, 200, () => calls++, () => enabled);
                Assert.True(Menu.GetChecked(path));
                Assert.False(Menu.GetEnabled(path));
                Assert.False(EditorApplication.ExecuteMenuItem(path));
                Assert.Equal(0, calls);
                enabled = true;
                Assert.True(Menu.GetEnabled(path));
                Assert.True(EditorApplication.ExecuteMenuItem(path));
                Assert.Equal(1, calls);
                Menu.AddMenuItem(path, "", false, 200, () => calls += 100, () => false);
                Assert.False(Menu.GetChecked(path));
                Assert.True(EditorApplication.ExecuteMenuItem(path));
                Assert.Equal(2, calls);
                Menu.RemoveMenuItem(root + "/Branch");
                Assert.False(Menu.GetEnabled(path));
                Assert.False(EditorApplication.ExecuteMenuItem(path));
                Assert.Equal(2, calls);
            }
            finally { Menu.RemoveMenuItem(root); }
        }

        const string GenericPath = "Tools/Menu robustness/Generic";
        const string RobustPath = "Tools/Menu robustness/Valid";
        static int s_RobustCalls;
        [MenuItem(GenericPath)] static void GenericCommand<T>() { }
        [MenuItem(RobustPath)] static void RobustCommand() { s_RobustCalls++; }
        [MenuItem(RobustPath, true)] static bool GenericValidator<T>() => false;

        [Fact]
        public void OpenGenericEntriesCannotBreakOtherCommandsOrLeaveAPartialCache()
        {
            using var engine = MainThreadCheck.Engine();
            var assemblies = (System.Collections.Generic.List<Assembly>)typeof(GameDomain).GetField("s_Assemblies", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var generation = typeof(GameDomain).GetField("s_Generation", BindingFlags.Static | BindingFlags.NonPublic);
            var original = assemblies.ToArray(); int oldGeneration = (int)generation.GetValue(null);
            try
            {
                assemblies.Clear(); assemblies.Add(typeof(EditorConnectionMenuTests).Assembly);
                generation.SetValue(null, oldGeneration + 1);
                Assert.False(Menu.GetChecked(GenericPath));
                Assert.True(Menu.GetEnabled(RobustPath));
                int before = s_RobustCalls;
                Assert.True(EditorApplication.ExecuteMenuItem(RobustPath));
                Assert.Equal(before + 1, s_RobustCalls);
                Assert.True(Menu.GetEnabled(RobustPath));
            }
            finally
            {
                assemblies.Clear(); assemblies.AddRange(original); generation.SetValue(null, oldGeneration);
            }
        }

        [Fact]
        public void DirectMenuExceptionsAreLoggedAndDoNotEscapeExecution()
        {
            using var engine = MainThreadCheck.Engine();
            const string path = "Unit throwing menu";
            var logger = Debug.unityLogger; var previous = logger.logHandler;
            bool enabled = logger.logEnabled; var filter = logger.filterLogType;
            var capture = new EditorPlayModeApiTests.DiagnosticCapture(previous);
            int calls = 0;
            try
            {
                logger.logHandler = capture; logger.logEnabled = true; logger.filterLogType = LogType.Log;
                Menu.AddMenuItem(path, "", false, 200, () => { calls++; throw new InvalidOperationException("execution-control"); }, null);
                Assert.True(EditorApplication.ExecuteMenuItem(path));
                Assert.Equal(1, calls);
                Assert.Contains(capture.Exceptions, error => error is InvalidOperationException && error.Message == "execution-control");
            }
            finally { logger.logHandler = previous; logger.logEnabled = enabled; logger.filterLogType = filter; Menu.RemoveMenuItem(path); }
        }

        [Fact]
        public void BuiltinAvailabilityIsScopedToTheMeasuredPlayMode()
        {
            using var engine = MainThreadCheck.Engine();
            string release = ProjectInfo.UnityVersion;
            bool playing = EditorSession.IsPlaying;
            var logger = Debug.unityLogger;
            var previous = logger.logHandler;
            bool enabled = logger.logEnabled;
            var filter = logger.filterLogType;
            var capture = new EditorPlayModeApiTests.DiagnosticCapture(previous);
            const string diagnostic = "Built-in menu availability outside the measured Play-mode profile";
            var hits = (ConcurrentDictionary<string, int>)typeof(Stubs).GetField("Hits", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            bool hadHit = hits.TryGetValue(diagnostic, out int oldHits);
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1"; EditorSession.IsPlaying = true;
                Assert.True(Menu.GetEnabled("Edit/Play"));
                Assert.True(Menu.GetEnabled("Edit/Pause"));
                Assert.True(Menu.GetEnabled("Edit/Step"));
                EditorSession.IsPlaying = false;
                logger.logHandler = capture; logger.logEnabled = true; logger.filterLogType = LogType.Log;
                hits.TryRemove(diagnostic, out _);
                Assert.False(Menu.GetEnabled("Edit/Play"));
                Assert.Contains(capture.Warnings, message => message.Contains(diagnostic));
                EditorSession.IsPlaying = true; ProjectInfo.UnityVersion = "6000.0.70f1";
                Assert.False(Menu.GetEnabled("Edit/Pause"));
            }
            finally
            {
                ProjectInfo.UnityVersion = release; EditorSession.IsPlaying = playing;
                logger.logHandler = previous; logger.logEnabled = enabled; logger.filterLogType = filter;
                if (hadHit) hits[diagnostic] = oldHits; else hits.TryRemove(diagnostic, out _);
            }
        }

        [Fact]
        public void EditorCallbackCastUsesTheSelectedReleaseMessage()
        {
            using var engine = MainThreadCheck.Engine();
            string release = ProjectInfo.UnityVersion;
            var connection = ScriptableObject.CreateInstance<EditorConnection>();
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                var callback = new PlainReceiver();
                Assert.Equal("Specified cast is not valid.", Assert.Throws<InvalidCastException>(() => connection.UnregisterConnection(callback.State)).Message);
                ProjectInfo.UnityVersion = "6000.0.70f1";
                Assert.NotEqual("Specified cast is not valid.", Assert.Throws<InvalidCastException>(() => connection.UnregisterDisconnection(callback.State)).Message);
            }
            finally { ProjectInfo.UnityVersion = release; Object.DestroyImmediate(connection); }
        }

        [Fact]
        public void PersistentEventsInvokeStaticAndInstanceCallbacksAndDropDestroyedTargets()
        {
            using var engine = MainThreadCheck.Engine();
            var receiver = ScriptableObject.CreateInstance<Receiver>();
            var evt = new UnityEvent<int>();
            int staticBefore = s_StaticSum;
            try
            {
                evt.AddPersistentListener(receiver.State, UnityEventCallState.EditorAndRuntime);
                evt.AddPersistentListener(StaticState, UnityEventCallState.EditorAndRuntime);
                Assert.Equal(2, evt.GetPersistentEventCount());
                Assert.Same(receiver, evt.GetPersistentTarget(0));
                Assert.Null(evt.GetPersistentTarget(1));
                evt.Invoke(3);
                Assert.Equal(3, receiver.Sum);
                Assert.Equal(staticBefore + 3, s_StaticSum);
                evt.RemovePersistentListener(null, ((UnityAction<int>)StaticState).Method);
                evt.Invoke(4);
                Assert.Equal(7, receiver.Sum);
                Assert.Equal(staticBefore + 3, s_StaticSum);
                Object.DestroyImmediate(receiver);
                evt.Invoke(5);
                Assert.Equal(7, receiver.Sum);
                Assert.Throws<ArgumentException>(() => evt.AddPersistentListener(new PlainReceiver().State, UnityEventCallState.EditorAndRuntime));
            }
            finally { if (receiver != null) Object.DestroyImmediate(receiver); }
        }

        [Fact]
        public void PersistentTargetTypeCanBeRecoveredFromAnOlderSerializedCall()
        {
            using var engine = MainThreadCheck.Engine();
            var receiver = ScriptableObject.CreateInstance<Receiver>();
            try
            {
                var evt = new UnityEvent<int>();
                evt.AddPersistentListener(receiver.State, UnityEventCallState.EditorAndRuntime);
                var group = (PersistentCallGroup)typeof(UnityEventBase).GetField("m_PersistentCalls", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(evt);
                typeof(PersistentCall).GetField("m_TargetAssemblyTypeName", BindingFlags.Instance | BindingFlags.NonPublic).SetValue(group.GetListener(0), null);
                evt.Invoke(9);
                Assert.Equal(9, receiver.Sum);
                Assert.True(group.GetListener(0).IsValid());
            }
            finally { Object.DestroyImmediate(receiver); }
        }

        [Fact]
        public void EditorMessageMembershipCountsUsePersistentCallbacksAndKeepPayloads()
        {
            using var engine = MainThreadCheck.Engine();
            var receiver = ScriptableObject.CreateInstance<Receiver>();
            var registry = new ConnectionMessageRegistry();
            var id = Guid.NewGuid();
            try
            {
                registry.Register(id, receiver.Message, persistent: true);
                registry.Register(id, receiver.Message, persistent: true);
                registry.Invoke(id, new MessageEventArgs { playerId = 3 });
                Assert.Equal(6, receiver.Sum);
                registry.Unregister(id, receiver.Message);
                registry.Invoke(id, new MessageEventArgs { playerId = 5 });
                Assert.Equal(16, receiver.Sum);
                registry.Unregister(id, receiver.Message);
                Assert.False(registry.Contains(id));
                registry.Invoke(id, new MessageEventArgs { playerId = 7 });
                Assert.Equal(16, receiver.Sum);
                // The runtime connection's removal is effective before its count reaches zero.
                registry.Register(id, receiver.Message, persistent: false);
                registry.Register(id, receiver.Message, persistent: false);
                registry.Unregister(id, receiver.Message);
                Assert.True(registry.Contains(id));
                registry.Invoke(id, new MessageEventArgs { playerId = 11 });
                Assert.Equal(16, receiver.Sum);
            }
            finally { Object.DestroyImmediate(receiver); }
        }
    }
}
