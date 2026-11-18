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
        public void TypeNamesUseTheWholeMeasuredTableAndRejectAnotherFamily()
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
                int gameObject = UnityEditor.UnityType.FindTypeByName("GameObject").persistentTypeID;
                // Another patch of the family answers from the same measured table.
                ProjectInfo.UnityVersion = "6000.0.67f1";
                Assert.Equal(gameObject, UnityEditor.UnityType.FindTypeByName("GameObject").persistentTypeID);
                // Another family has no measured table.
                ProjectInfo.UnityVersion = "6000.1.0f1";
                Assert.Null(ClassCoverage.UnityTable(ProjectInfo.UnityVersion));
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

        internal sealed class DiagnosticCapture : ILogHandler
        {
            readonly ILogHandler m_Next;
            internal readonly List<string> Warnings = new List<string>();
            internal readonly List<Exception> Exceptions = new List<Exception>();
            internal DiagnosticCapture(ILogHandler next) { m_Next = next; }
            public void LogFormat(LogType type, Object context, string format, params object[] args)
            {
                if (type == LogType.Warning) Warnings.Add(string.Format(format, args));
                m_Next.LogFormat(type, context, format, args);
            }
            public void LogException(Exception error, Object context) { Exceptions.Add(error); m_Next.LogException(error, context); }
        }

        /// <summary>Undoable operations publish their changes to changesPublished handlers in the
        /// frame's Editor update, in order, and undo publishes the undone ones in reverse order
        /// (Unity 6000.0.71f1, tests/projects/gameinterop objectchange.*); without a handler nothing
        /// is kept, and a plain change publishes nothing.</summary>
        [Fact]
        public void UndoPublishesItsChangesToSubscribers()
        {
            using var engine = MainThreadCheck.Engine();
            const BindingFlags flags = BindingFlags.Static | BindingFlags.NonPublic;
            var undo = (IList)typeof(Undo).GetField("s_Undo", flags).GetValue(null);
            var redo = (IList)typeof(Undo).GetField("s_Redo", flags).GetValue(null);
            var savedUndo = undo.Cast<object>().ToArray();
            var savedRedo = redo.Cast<object>().ToArray();
            var current = typeof(Undo).GetField("s_Current", flags);
            int savedCurrent = (int)current.GetValue(null);
            var published = new List<string>();
            string Name(int id) => id == 0 ? "0" : EditorUtility.InstanceIDToObject(id) is Object o && o != null ? o.name : "missing";
            ObjectChangeEvents.ObjectChangeEventsHandler listener = (ref ObjectChangeEventStream stream) =>
            {
                var parts = new List<string>();
                for (int i = 0; i < stream.length; i++)
                {
                    var kind = stream.GetEventType(i);
                    switch (kind)
                    {
                        case ObjectChangeKind.ChangeGameObjectOrComponentProperties: stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out var p); parts.Add(kind + " " + Name(p.instanceId)); break;
                        case ObjectChangeKind.CreateGameObjectHierarchy: stream.GetCreateGameObjectHierarchyEvent(i, out var c); parts.Add(kind + " " + Name(c.instanceId)); break;
                        case ObjectChangeKind.DestroyGameObjectHierarchy: stream.GetDestroyGameObjectHierarchyEvent(i, out var d); parts.Add(kind + " " + Name(d.instanceId) + " parent=" + Name(d.parentInstanceId)); break;
                        case ObjectChangeKind.ChangeGameObjectStructure: stream.GetChangeGameObjectStructureEvent(i, out var s); parts.Add(kind + " " + Name(s.instanceId)); break;
                        case ObjectChangeKind.ChangeGameObjectParent: stream.GetChangeGameObjectParentEvent(i, out var r); parts.Add(kind + " " + Name(r.instanceId) + " " + Name(r.previousParentInstanceId) + ">" + Name(r.newParentInstanceId)); break;
                        case ObjectChangeKind.ChangeAssetObjectProperties: stream.GetChangeAssetObjectPropertiesEvent(i, out var a); parts.Add(kind + " " + Name(a.instanceId)); break;
                        default: parts.Add(kind.ToString()); break;
                    }
                }
                var copy = stream;
                Assert.Throws<InvalidOperationException>(() => copy.GetChangeSceneEvent(0, out _));
                published.Add(string.Join("; ", parts));
            };
            GameObject target = null, child = null, doomed = null;
            ScriptableObject data = null;
            try
            {
                Undo.ClearAll();
                target = new GameObject("Target");
                child = new GameObject("Child");
                doomed = new GameObject("Doomed");
                data = ScriptableObject.CreateInstance<ScriptableObject>();
                data.name = "Data";
                // Without a handler nothing is kept for later.
                Undo.RecordObject(target.transform, "unsubscribed");
                ObjectChangeEvents.Publish();
                ObjectChangeEvents.changesPublished += listener;
                ObjectChangeEvents.Publish();
                Assert.Empty(published);

                target.transform.position = Vector3.one;
                ObjectChangeEvents.Publish();
                Assert.Empty(published);

                Undo.RecordObject(target.transform, "move");
                target.transform.position = new Vector3(1, 2, 3);
                Undo.AddComponent<BoxCollider>(target);
                ObjectChangeEvents.Publish();
                Assert.Equal(new[] { "ChangeGameObjectOrComponentProperties Target; ChangeGameObjectStructure Target" }, published);
                published.Clear();

                Undo.SetTransformParent(child.transform, target.transform, "parent");
                Undo.DestroyObjectImmediate(doomed);
                Undo.RecordObject(data, "data");
                ObjectChangeEvents.Publish();
                Assert.Equal(new[] { "ChangeGameObjectParent Child 0>Target; DestroyGameObjectHierarchy missing parent=0; ChangeAssetObjectProperties Data" }, published);
                published.Clear();

                Undo.PerformUndo();
                ObjectChangeEvents.Publish();
                // The whole group, the unsubscribed recording too, in reverse order.
                Assert.Equal("ChangeAssetObjectProperties Data; ChangeGameObjectParent Child Target>0; ChangeGameObjectStructure Target; "
                    + "ChangeGameObjectOrComponentProperties Target; ChangeGameObjectOrComponentProperties Target", Assert.Single(published));
                Assert.Equal(Vector3.zero, target.transform.position);

                // A stream outlives the call only as a clone; a disposed one is empty.
                var builder = new ObjectChangeEventStream.Builder(Unity.Collections.Allocator.Temp);
                var made = new CreateGameObjectHierarchyEventArgs(target.GetInstanceID(), target.scene);
                builder.PushCreateGameObjectHierarchyEvent(ref made);
                var stream = builder.ToStream(Unity.Collections.Allocator.Temp);
                builder.Dispose();
                Assert.Equal(1, stream.length);
                stream.GetCreateGameObjectHierarchyEvent(0, out var back);
                Assert.Equal(target.GetInstanceID(), back.instanceId);
                var copy = stream.Clone(Unity.Collections.Allocator.Temp);
                stream.Dispose();
                Assert.False(stream.isCreated);
                Assert.Equal(0, stream.length);
                Assert.Equal(1, copy.length);
                copy.Dispose();
            }
            finally
            {
                ObjectChangeEvents.changesPublished -= listener;
                ObjectChangeEvents.Publish();
                foreach (var o in new Object[] { target, child, doomed, data }) if (o != null) Object.DestroyImmediate(o);
                Undo.ClearAll();
                foreach (var entry in savedUndo) undo.Add(entry);
                foreach (var entry in savedRedo) redo.Add(entry);
                current.SetValue(null, savedCurrent);
            }
        }
    }
}
