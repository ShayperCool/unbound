using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Unbound.Runtime;
using UnityEditor;
using UnityEngine.Events;
using Xunit;

namespace UnityEngine.Tests
{
    public sealed class EditorPlayModeApiTests
    {
        [Fact]
        public void TypeNamesUseTheWholeMeasuredTableAndRejectAnotherRelease()
        {
            string release = ProjectInfo.UnityVersion;
            try
            {
                ProjectInfo.UnityVersion = "6000.0.71f1";
                foreach (var entry in ClassCoverage.UnityTable(ProjectInfo.UnityVersion, exact: true))
                {
                    var type = UnityEditor.UnityType.FindTypeByName(entry.Value.Name);
                    Assert.Equal(entry.Key, type.persistentTypeID);
                    Assert.Equal(entry.Value.Name, type.name);
                    Assert.Same(type, UnityEditor.UnityType.FindTypeByName(entry.Value.Name));
                }
                Assert.Null(UnityEditor.UnityType.FindTypeByName("gameobject"));
                Assert.Equal("key", Assert.Throws<ArgumentNullException>(() => UnityEditor.UnityType.FindTypeByName(null)).ParamName);
                ProjectInfo.UnityVersion = "6000.0.70f1";
                Assert.NotNull(ClassCoverage.UnityTable(ProjectInfo.UnityVersion));
                Assert.Throws<NotSupportedException>(() => UnityEditor.UnityType.FindTypeByName("GameObject"));
            }
            finally { ProjectInfo.UnityVersion = release; }
        }

        static int s_MenuCalls;
        const string MenuPath = "Tools/Unbound managed menu control";
        [MenuItem(MenuPath + " _g")]
        static void MenuControl() { s_MenuCalls++; }

        [Fact]
        public void MenuDiscoveryAndTypeCacheFollowChangesToTheCurrentAssemblyList()
        {
            using var engine = MainThreadCheck.Engine();
            var assemblies = (List<Assembly>)typeof(GameDomain).GetField("s_Assemblies", BindingFlags.Static | BindingFlags.NonPublic).GetValue(null);
            var generation = typeof(GameDomain).GetField("s_Generation", BindingFlags.Static | BindingFlags.NonPublic);
            var original = assemblies.ToArray();
            int oldGeneration = (int)generation.GetValue(null);
            try
            {
                assemblies.Clear();
                generation.SetValue(null, oldGeneration + 1);
                Assert.False(Menu.GetChecked(MenuPath));
                Assert.DoesNotContain(TypeCache.GetMethodsWithAttribute<MenuItem>(), method => method.DeclaringType == typeof(EditorPlayModeApiTests));
                assemblies.Add(typeof(EditorPlayModeApiTests).Assembly);
                Menu.SetChecked(MenuPath, true);
                Assert.True(Menu.GetChecked(MenuPath));
                int before = s_MenuCalls;
                Assert.True(EditorApplication.ExecuteMenuItem(MenuPath));
                Assert.Equal(before + 1, s_MenuCalls);
                // Replace the assembly without changing the count or list identity.
                assemblies[0] = typeof(string).Assembly;
                generation.SetValue(null, oldGeneration + 2);
                Assert.False(Menu.GetChecked(MenuPath));
                Assert.DoesNotContain(TypeCache.GetMethodsWithAttribute<MenuItem>(), method => method.DeclaringType == typeof(EditorPlayModeApiTests));
            }
            finally
            {
                assemblies.Clear(); assemblies.AddRange(original);
                generation.SetValue(null, oldGeneration);
            }
        }

        [Fact]
        public void NullRemovalValidatesBeforeVisitingAnEmptyEventAcrossArities()
        {
            Assert.Throws<NullReferenceException>(() => new UnityEvent().RemoveListener(null));
            Assert.Throws<NullReferenceException>(() => new UnityEvent<string, int>().RemoveListener(null));
        }

        [Fact]
        public void IdleEditorSubscriptionsKeepMultiplicityAndSupportRemoval()
        {
            Action callback = () => { };
            var field = typeof(Lightmapping).GetField("bakeCompleted", BindingFlags.Static | BindingFlags.NonPublic);
            var before = (Delegate)field.GetValue(null);
            int original = before?.GetInvocationList().Length ?? 0;
            Lightmapping.bakeCompleted += callback;
            Lightmapping.bakeCompleted += callback;
            try
            {
                Assert.Equal(original + 2, ((Delegate)field.GetValue(null)).GetInvocationList().Length);
                Lightmapping.bakeCompleted -= callback;
                Assert.Equal(original + 1, ((Delegate)field.GetValue(null)).GetInvocationList().Length);
            }
            finally
            {
                Lightmapping.bakeCompleted -= callback;
                Assert.Equal(original, ((Delegate)field.GetValue(null))?.GetInvocationList().Length ?? 0);
            }
        }

        sealed class DiagnosticCapture : ILogHandler
        {
            readonly ILogHandler m_Next;
            internal readonly List<string> Warnings = new List<string>();
            internal DiagnosticCapture(ILogHandler next) { m_Next = next; }
            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type == LogType.Warning) Warnings.Add(string.Format(format, args));
                m_Next.LogFormat(type, context, format, args);
            }
            public void LogException(Exception error, Object context) => m_Next.LogException(error, context);
        }

        [Fact]
        public void UndoReportsUnavailablePayloadsOnlyWhenSubscribed()
        {
            using var engine = MainThreadCheck.Engine();
            const string diagnostic = "ObjectChangeEvents undo payload streams";
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            var hits = (ConcurrentDictionary<string, int>)typeof(Stubs).GetField("Hits", flags).GetValue(null);
            bool hadHit = hits.TryGetValue(diagnostic, out int previousHits);
            var undo = (IList)typeof(Undo).GetField("s_Undo", flags).GetValue(null);
            var redo = (IList)typeof(Undo).GetField("s_Redo", flags).GetValue(null);
            var savedUndo = undo.Cast<object>().ToArray();
            var savedRedo = redo.Cast<object>().ToArray();
            var current = typeof(Undo).GetField("s_Current", flags);
            int savedCurrent = (int)current.GetValue(null);
            var previousUndoCallback = Undo.undoRedoPerformed;
            var previousUndoEvent = Undo.undoRedoEvent;
            var previousFlush = Undo.willFlushUndoRecord;
            var logger = Debug.unityLogger;
            var previousHandler = logger.logHandler;
            bool previousEnabled = logger.logEnabled;
            var previousFilter = logger.filterLogType;
            var capture = new DiagnosticCapture(previousHandler);
            ObjectChangeEvents.ObjectChangeEventsHandler listener = (ref ObjectChangeEventStream stream) => { };
            GameObject owner = null;
            void ResetDiagnostic()
            {
                hits.TryRemove(diagnostic, out _);
                capture.Warnings.Clear();
            }
            void AssertDiagnostic() => Assert.Contains(capture.Warnings, message => message.Contains(diagnostic));
            try
            {
                Undo.ClearAll();
                Undo.undoRedoPerformed = null; Undo.undoRedoEvent = null; Undo.willFlushUndoRecord = null;
                owner = new GameObject("Undo diagnostic control");
                logger.logHandler = capture; logger.logEnabled = true; logger.filterLogType = LogType.Log;
                ResetDiagnostic();
                Undo.RecordObject(owner.transform, "unsubscribed control");
                owner.transform.localPosition = new Vector3(1, 2, 3);
                Undo.FlushUndoRecordObjects();
                Undo.PerformUndo();
                Assert.Equal(Vector3.zero, owner.transform.localPosition);
                Assert.DoesNotContain(capture.Warnings, message => message.Contains(diagnostic));
                Assert.False(hits.ContainsKey(diagnostic));

                ObjectChangeEvents.changesPublished += listener;
                ResetDiagnostic();
                Undo.RecordObject(owner.transform, "subscribed control");
                AssertDiagnostic();
                owner.transform.localPosition = new Vector3(4, 5, 6);
                ResetDiagnostic(); Undo.FlushUndoRecordObjects(); AssertDiagnostic();
                ResetDiagnostic(); Undo.PerformUndo(); AssertDiagnostic();
                Assert.Equal(Vector3.zero, owner.transform.localPosition);
                ResetDiagnostic(); Undo.PerformRedo(); AssertDiagnostic();
                Assert.Equal(new Vector3(4, 5, 6), owner.transform.localPosition);
            }
            finally
            {
                ObjectChangeEvents.changesPublished -= listener;
                logger.logHandler = previousHandler; logger.logEnabled = previousEnabled; logger.filterLogType = previousFilter;
                if (owner != null) Object.DestroyImmediate(owner);
                Undo.ClearAll();
                foreach (var entry in savedUndo) undo.Add(entry);
                foreach (var entry in savedRedo) redo.Add(entry);
                current.SetValue(null, savedCurrent);
                Undo.undoRedoPerformed = previousUndoCallback; Undo.undoRedoEvent = previousUndoEvent;
                Undo.willFlushUndoRecord = previousFlush;
                hits.TryRemove(diagnostic, out _);
                if (hadHit) hits[diagnostic] = previousHits;
            }
        }
    }
}
