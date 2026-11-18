using System;
using System.Collections.Generic;
using Unbound.Runtime;
using Unity.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace UnityEditor
{
    public enum ObjectChangeKind : ushort
    {
        None = 0,
        ChangeScene = 1,
        CreateGameObjectHierarchy = 2,
        ChangeGameObjectStructureHierarchy = 3,
        ChangeGameObjectStructure = 4,
        ChangeGameObjectParent = 5,
        ChangeGameObjectOrComponentProperties = 6,
        DestroyGameObjectHierarchy = 7,
        CreateAssetObject = 8,
        DestroyAssetObject = 9,
        ChangeAssetObjectProperties = 10,
        UpdatePrefabInstances = 11,
        ChangeChildrenOrder = 12,
        ChangeRootOrder = 13,
    }

    /// <summary>
    /// Changes undoable operations make, published to changesPublished handlers in the Editor update
    /// of the frame they happen in, in the order they happen; an undo or redo publishes the changes
    /// it makes, the undone operations in reverse order (Unity 6000.0.71f1, tests/projects/gameinterop
    /// objectchange.*). Unbound produces them for Undo.RecordObject (properties of a GameObject or
    /// component, or of another object; only when it changed by then, not measured), RegisterCreatedObjectUndo of a GameObject, Undo.AddComponent,
    /// SetTransformParent and DestroyObjectImmediate of a GameObject; other operations publish nothing
    /// and say so.
    /// </summary>
    public static partial class ObjectChangeEvents
    {
        public delegate void ObjectChangeEventsHandler(ref ObjectChangeEventStream stream);
        public static event ObjectChangeEventsHandler changesPublished;

        /// <summary>The frame's changes; one with a condition is published only if it still holds then.</summary>
        static readonly List<(ObjectChangeKind Kind, object Payload, Func<bool> Holds)> s_Pending = new List<(ObjectChangeKind, object, Func<bool>)>();

        internal static bool Listening => changesPublished != null;

        internal static void Note(ObjectChangeKind kind, object payload, Func<bool> holds = null)
        {
            if (changesPublished != null) s_Pending.Add((kind, payload, holds));
        }

        internal static int Id(Object o) => o == null ? 0 : o.GetInstanceID();

        internal static Scene SceneOf(Object o) => (o as GameObject ?? (o as Component)?.gameObject) is GameObject go ? go.scene : default;

        /// <summary>The parent GameObject's instance ID, 0 for a root.</summary>
        internal static int ParentId(Transform parent) => parent == null ? 0 : parent.gameObject.GetInstanceID();

        /// <summary>A change of an object's properties: a GameObject's or component's, or an asset's;
        /// with <paramref name="changed"/>, only if it says so when the changes are published.</summary>
        internal static void NoteProperties(Object o, Func<bool> changed = null)
        {
            if (changesPublished == null || o == null) return;
            if (o is GameObject || o is Component)
                Note(ObjectChangeKind.ChangeGameObjectOrComponentProperties, new ChangeGameObjectOrComponentPropertiesEventArgs(Id(o), SceneOf(o)), changed);
            else
                Note(ObjectChangeKind.ChangeAssetObjectProperties, new ChangeAssetObjectPropertiesEventArgs(AssetGuid(o), Id(o), default), changed);
        }

        static GUID AssetGuid(Object o)
        {
            var path = AssetDatabase.GetAssetPath(o);
            return string.IsNullOrEmpty(path) ? default : new GUID(AssetDatabase.AssetPathToGUID(path));
        }

        /// <summary>Publishes the frame's changes (the Editor update).</summary>
        internal static void Publish()
        {
            if (s_Pending.Count == 0) return;
            var pending = s_Pending.ToArray();
            s_Pending.Clear();
            var handlers = changesPublished;
            if (handlers == null) return;
            var events = new List<(ObjectChangeKind, object)>(pending.Length);
            foreach (var (kind, payload, holds) in pending)
                if (holds == null || holds()) events.Add((kind, payload));
            if (events.Count == 0) return;
            var stream = ObjectChangeEventStream.Over(events);
            try
            {
                foreach (ObjectChangeEventsHandler handler in handlers.GetInvocationList())
                {
                    try { handler(ref stream); }
                    catch (Exception e) { Debug.LogException(e); }
                }
            }
            finally
            {
                stream.Release();
            }
        }
    }

    /// <summary>A list of object changes. The stream a handler gets lives for the call; Clone and the
    /// Builder make ones that live until disposed.</summary>
    public unsafe partial struct ObjectChangeEventStream : IDisposable
    {
        fixed long m_OpaqueStorage[Compat.ObjectChangeStreamStorageWords];

        static readonly Dictionary<long, List<(ObjectChangeKind Kind, object Payload)>> s_Streams = new Dictionary<long, List<(ObjectChangeKind, object)>>();
        static long s_Next;

        long Handle => m_OpaqueStorage[0];

        internal static ObjectChangeEventStream Over(List<(ObjectChangeKind, object)> events)
        {
            var stream = new ObjectChangeEventStream();
            stream.m_OpaqueStorage[0] = ++s_Next;
            s_Streams[s_Next] = events;
            return stream;
        }

        internal void Release() => s_Streams.Remove(Handle);

        List<(ObjectChangeKind Kind, object Payload)> Events =>
            s_Streams.TryGetValue(Handle, out var events) ? events : throw new ObjectDisposedException(nameof(ObjectChangeEventStream));

        public int length => s_Streams.TryGetValue(Handle, out var events) ? events.Count : 0;

        public bool isCreated => Handle != 0 && s_Streams.ContainsKey(Handle);

        public ObjectChangeKind GetEventType(int eventIdx) => Event(eventIdx).Kind;

        (ObjectChangeKind Kind, object Payload) Event(int eventIdx)
        {
            var events = Events;
            if ((uint)eventIdx >= (uint)events.Count) throw new ArgumentOutOfRangeException(nameof(eventIdx));
            return events[eventIdx];
        }

        object Payload(int eventIdx, ObjectChangeKind kind)
        {
            var e = Event(eventIdx);
            if (e.Kind != kind) throw new InvalidOperationException($"Event at index {eventIdx} is {e.Kind}, not {kind}");
            return e.Payload;
        }

        public void GetChangeSceneEvent(int eventIdx, out ChangeSceneEventArgs data) => data = (ChangeSceneEventArgs)Payload(eventIdx, ObjectChangeKind.ChangeScene);

        public void GetCreateGameObjectHierarchyEvent(int eventIdx, out CreateGameObjectHierarchyEventArgs data) => data = (CreateGameObjectHierarchyEventArgs)Payload(eventIdx, ObjectChangeKind.CreateGameObjectHierarchy);

        public void GetChangeGameObjectStructureHierarchyEvent(int eventIdx, out ChangeGameObjectStructureHierarchyEventArgs data) => data = (ChangeGameObjectStructureHierarchyEventArgs)Payload(eventIdx, ObjectChangeKind.ChangeGameObjectStructureHierarchy);

        public void GetChangeGameObjectStructureEvent(int eventIdx, out ChangeGameObjectStructureEventArgs data) => data = (ChangeGameObjectStructureEventArgs)Payload(eventIdx, ObjectChangeKind.ChangeGameObjectStructure);

        public void GetChangeChildrenOrderEvent(int eventIdx, out ChangeChildrenOrderEventArgs data) => data = (ChangeChildrenOrderEventArgs)Payload(eventIdx, ObjectChangeKind.ChangeChildrenOrder);

        public void GetChangeRootOrderEvent(int eventIdx, out ChangeRootOrderEventArgs data) => data = (ChangeRootOrderEventArgs)Payload(eventIdx, ObjectChangeKind.ChangeRootOrder);

        public void GetChangeGameObjectOrComponentPropertiesEvent(int eventIdx, out ChangeGameObjectOrComponentPropertiesEventArgs data) => data = (ChangeGameObjectOrComponentPropertiesEventArgs)Payload(eventIdx, ObjectChangeKind.ChangeGameObjectOrComponentProperties);

        public void GetCreateAssetObjectEvent(int eventIdx, out CreateAssetObjectEventArgs data) => data = (CreateAssetObjectEventArgs)Payload(eventIdx, ObjectChangeKind.CreateAssetObject);

        public void GetDestroyAssetObjectEvent(int eventIdx, out DestroyAssetObjectEventArgs data) => data = (DestroyAssetObjectEventArgs)Payload(eventIdx, ObjectChangeKind.DestroyAssetObject);

        public void GetChangeAssetObjectPropertiesEvent(int eventIdx, out ChangeAssetObjectPropertiesEventArgs data) => data = (ChangeAssetObjectPropertiesEventArgs)Payload(eventIdx, ObjectChangeKind.ChangeAssetObjectProperties);

        public void GetDestroyGameObjectHierarchyEvent(int eventIdx, out DestroyGameObjectHierarchyEventArgs data) => data = (DestroyGameObjectHierarchyEventArgs)Payload(eventIdx, ObjectChangeKind.DestroyGameObjectHierarchy);

        public void GetChangeGameObjectParentEvent(int eventIdx, out ChangeGameObjectParentEventArgs data) => data = (ChangeGameObjectParentEventArgs)Payload(eventIdx, ObjectChangeKind.ChangeGameObjectParent);

        public ObjectChangeEventStream Clone(Allocator allocator) => Over(new List<(ObjectChangeKind, object)>(Events));

        public void Dispose() => Release();

        public partial struct Builder : IDisposable
        {
            long m_Handle;

            public Builder(Allocator allocator)
            {
                m_Handle = ++s_Next;
                s_Streams[m_Handle] = new List<(ObjectChangeKind, object)>();
            }

            List<(ObjectChangeKind Kind, object Payload)> Events =>
                s_Streams.TryGetValue(m_Handle, out var events) ? events : throw new ObjectDisposedException(nameof(Builder));

            public int eventCount => s_Streams.TryGetValue(m_Handle, out var events) ? events.Count : 0;

            public ObjectChangeEventStream ToStream(Allocator allocator) => Over(new List<(ObjectChangeKind, object)>(Events));

            public void Dispose() => s_Streams.Remove(m_Handle);

            public void PushChangeSceneEvent(ref ChangeSceneEventArgs data) => Events.Add((ObjectChangeKind.ChangeScene, data));

            public void PushCreateGameObjectHierarchyEvent(ref CreateGameObjectHierarchyEventArgs data) => Events.Add((ObjectChangeKind.CreateGameObjectHierarchy, data));

            public void PushChangeGameObjectStructureHierarchyEvent(ref ChangeGameObjectStructureHierarchyEventArgs data) => Events.Add((ObjectChangeKind.ChangeGameObjectStructureHierarchy, data));

            public void PushChangeGameObjectStructureEvent(ref ChangeGameObjectStructureEventArgs data) => Events.Add((ObjectChangeKind.ChangeGameObjectStructure, data));

            public void PushChangeChildrenOrderEvent(ref ChangeChildrenOrderEventArgs data) => Events.Add((ObjectChangeKind.ChangeChildrenOrder, data));

            public void PushChangeRootOrderEvent(ref ChangeRootOrderEventArgs data) => Events.Add((ObjectChangeKind.ChangeRootOrder, data));

            public void PushChangeGameObjectOrComponentPropertiesEvent(ref ChangeGameObjectOrComponentPropertiesEventArgs data) => Events.Add((ObjectChangeKind.ChangeGameObjectOrComponentProperties, data));

            public void PushCreateAssetObjectEvent(ref CreateAssetObjectEventArgs data) => Events.Add((ObjectChangeKind.CreateAssetObject, data));

            public void PushDestroyAssetObjectEvent(ref DestroyAssetObjectEventArgs data) => Events.Add((ObjectChangeKind.DestroyAssetObject, data));

            public void PushChangeAssetObjectPropertiesEvent(ref ChangeAssetObjectPropertiesEventArgs data) => Events.Add((ObjectChangeKind.ChangeAssetObjectProperties, data));

            public void PushDestroyGameObjectHierarchyEvent(ref DestroyGameObjectHierarchyEventArgs data) => Events.Add((ObjectChangeKind.DestroyGameObjectHierarchy, data));

            public void PushChangeGameObjectParentEvent(ref ChangeGameObjectParentEventArgs data) => Events.Add((ObjectChangeKind.ChangeGameObjectParent, data));
        }
    }

    public partial struct ChangeSceneEventArgs
    {
        readonly Scene m_Scene;

        public ChangeSceneEventArgs(Scene scene)
        {
            m_Scene = scene;
        }

        public Scene scene => m_Scene;
    }

    public partial struct CreateGameObjectHierarchyEventArgs
    {
        readonly int m_InstanceId;
        readonly Scene m_Scene;

        public CreateGameObjectHierarchyEventArgs(int instanceId, Scene scene)
        {
            m_InstanceId = instanceId;
            m_Scene = scene;
        }

        public int instanceId => m_InstanceId;
        public Scene scene => m_Scene;
    }

    public partial struct ChangeGameObjectStructureHierarchyEventArgs
    {
        readonly int m_InstanceId;
        readonly Scene m_Scene;

        public ChangeGameObjectStructureHierarchyEventArgs(int instanceId, Scene scene)
        {
            m_InstanceId = instanceId;
            m_Scene = scene;
        }

        public int instanceId => m_InstanceId;
        public Scene scene => m_Scene;
    }

    public partial struct ChangeGameObjectStructureEventArgs
    {
        readonly int m_InstanceId;
        readonly Scene m_Scene;

        public ChangeGameObjectStructureEventArgs(int instanceId, Scene scene)
        {
            m_InstanceId = instanceId;
            m_Scene = scene;
        }

        public int instanceId => m_InstanceId;
        public Scene scene => m_Scene;
    }

    public partial struct ChangeChildrenOrderEventArgs
    {
        readonly int m_InstanceId;
        readonly Scene m_Scene;

        public ChangeChildrenOrderEventArgs(int instanceId, Scene scene)
        {
            m_InstanceId = instanceId;
            m_Scene = scene;
        }

        public int instanceId => m_InstanceId;
        public Scene scene => m_Scene;
    }

    public partial struct ChangeRootOrderEventArgs
    {
        readonly int m_InstanceId;
        readonly Scene m_Scene;

        public ChangeRootOrderEventArgs(int instanceId, Scene scene)
        {
            m_InstanceId = instanceId;
            m_Scene = scene;
        }

        public int instanceId => m_InstanceId;
        public Scene scene => m_Scene;
    }

    public partial struct ChangeGameObjectOrComponentPropertiesEventArgs
    {
        readonly int m_InstanceId;
        readonly Scene m_Scene;

        public ChangeGameObjectOrComponentPropertiesEventArgs(int instanceId, Scene scene)
        {
            m_InstanceId = instanceId;
            m_Scene = scene;
        }

        public int instanceId => m_InstanceId;
        public Scene scene => m_Scene;
    }

    public partial struct CreateAssetObjectEventArgs
    {
        readonly GUID m_Guid;
        readonly int m_InstanceId;
        readonly Scene m_Scene;

        public CreateAssetObjectEventArgs(GUID guid, int instanceId, Scene scene)
        {
            m_Guid = guid;
            m_InstanceId = instanceId;
            m_Scene = scene;
        }

        public GUID guid => m_Guid;
        public int instanceId => m_InstanceId;
        public Scene scene => m_Scene;
    }

    public partial struct DestroyAssetObjectEventArgs
    {
        readonly GUID m_Guid;
        readonly int m_InstanceId;
        readonly Scene m_Scene;

        public DestroyAssetObjectEventArgs(GUID guid, int instanceId, Scene scene)
        {
            m_Guid = guid;
            m_InstanceId = instanceId;
            m_Scene = scene;
        }

        public GUID guid => m_Guid;
        public int instanceId => m_InstanceId;
        public Scene scene => m_Scene;
    }

    public partial struct ChangeAssetObjectPropertiesEventArgs
    {
        readonly GUID m_Guid;
        readonly int m_InstanceId;
        readonly Scene m_Scene;

        public ChangeAssetObjectPropertiesEventArgs(GUID guid, int instanceId, Scene scene)
        {
            m_Guid = guid;
            m_InstanceId = instanceId;
            m_Scene = scene;
        }

        public GUID guid => m_Guid;
        public int instanceId => m_InstanceId;
        public Scene scene => m_Scene;
    }

    public partial struct DestroyGameObjectHierarchyEventArgs
    {
        readonly int m_InstanceId;
        readonly int m_ParentInstanceId;
        readonly Scene m_Scene;

        public DestroyGameObjectHierarchyEventArgs(int instanceId, Scene scene) : this(instanceId, 0, scene) { }

        public DestroyGameObjectHierarchyEventArgs(int instanceId, int parentInstanceId, Scene scene)
        {
            m_InstanceId = instanceId;
            m_ParentInstanceId = parentInstanceId;
            m_Scene = scene;
        }

        public int instanceId => m_InstanceId;
        public int parentInstanceId => m_ParentInstanceId;
        public Scene scene => m_Scene;
    }

    public partial struct ChangeGameObjectParentEventArgs
    {
        readonly int m_InstanceId;
        readonly Scene m_PreviousScene;
        readonly int m_PreviousParentInstanceId;
        readonly Scene m_NewScene;
        readonly int m_NewParentInstanceId;

        public ChangeGameObjectParentEventArgs(int instanceId, Scene previousScene, int previousParentInstanceId, Scene newScene, int newParentInstanceId)
        {
            m_InstanceId = instanceId;
            m_PreviousScene = previousScene;
            m_PreviousParentInstanceId = previousParentInstanceId;
            m_NewScene = newScene;
            m_NewParentInstanceId = newParentInstanceId;
        }

        public int instanceId => m_InstanceId;
        public int previousParentInstanceId => m_PreviousParentInstanceId;
        public int newParentInstanceId => m_NewParentInstanceId;
        public Scene previousScene => m_PreviousScene;
        public Scene newScene => m_NewScene;
    }
}
