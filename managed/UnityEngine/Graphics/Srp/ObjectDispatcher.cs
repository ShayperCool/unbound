using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using Object = UnityEngine.Object;

namespace UnityEngine
{
    /// <summary>
    /// Change tracking of engine objects by type, for the GPU-driven rendering code of the render
    /// pipelines: each dispatcher reports, per tracked type, the objects created or changed and the
    /// ones destroyed since it last asked, and per transform-tracked type the components whose
    /// transform changed (<see cref="Unbound.Runtime.ObjectDispatch"/> records the changes).
    /// </summary>
    internal sealed partial class ObjectDispatcher : IDisposable
    {
        public enum TransformTrackingType
        {
            GlobalTRS = 0,
            LocalTRS = 1,
            Hierarchy = 2,
        }

        [Flags]
        public enum TypeTrackingFlags
        {
            SceneObjects = 1,
            Assets = 2,
            EditorOnlyObjects = 4,
            Default = SceneObjects | Assets,
            All = SceneObjects | Assets | EditorOnlyObjects,
        }

        readonly Unbound.Runtime.ObjectDispatch.System m_System;

        public ObjectDispatcher() => m_System = Unbound.Runtime.ObjectDispatch.Create();

        public bool valid => m_System.Live;

        public int maxDispatchHistoryFramesCount
        {
            get => m_System.MaxHistory;
            set => m_System.MaxHistory = value;
        }

        public void Dispose() => Unbound.Runtime.ObjectDispatch.Destroy(m_System);

        public void EnableTypeTracking(TypeTrackingFlags typeTrackingMask, params Type[] types)
        {
            foreach (var type in types) m_System.EnableType(type, typeTrackingMask);
        }

        public void EnableTypeTracking(params Type[] types) => EnableTypeTracking(TypeTrackingFlags.Default, types);

        [Obsolete("EnableTypeTrackingIncludingAssets is deprecated, please use EnableTypeTracking and provide the flag that specifies whether you need assets or not.")]
        public void EnableTypeTrackingIncludingAssets(params Type[] types) => EnableTypeTracking(TypeTrackingFlags.SceneObjects | TypeTrackingFlags.Assets, types);

        public void DisableTypeTracking(params Type[] types)
        {
            foreach (var type in types) m_System.DisableType(type);
        }

        public void EnableTransformTracking(TransformTrackingType trackingType, params Type[] types)
        {
            foreach (var type in types) m_System.EnableTransforms(type, trackingType);
        }

        public void DisableTransformTracking(TransformTrackingType trackingType, params Type[] types)
        {
            foreach (var type in types) m_System.DisableTransforms(type, trackingType);
        }

        public void EnableTypeTracking<T>(TypeTrackingFlags typeTrackingMask = TypeTrackingFlags.Default) where T : Object => EnableTypeTracking(typeTrackingMask, typeof(T));
        public void DisableTypeTracking<T>() where T : Object => DisableTypeTracking(typeof(T));
        public void EnableTransformTracking<T>(TransformTrackingType trackingType) where T : Object => EnableTransformTracking(trackingType, typeof(T));
        public void DisableTransformTracking<T>(TransformTrackingType trackingType) where T : Object => DisableTransformTracking(trackingType, typeof(T));

        public void ClearTypeChanges(Type type) => m_System.TakeTypeChanges(type, null, null, false);
        public void ClearTypeChanges<T>() where T : Object => ClearTypeChanges(typeof(T));

        public TypeDispatchData GetTypeChangesAndClear(Type type, Allocator allocator, bool sortByInstanceID = false, bool noScriptingArray = false)
        {
            var changed = Unbound.Runtime.ObjectDispatch.Scratch;
            var destroyed = Unbound.Runtime.ObjectDispatch.ScratchDestroyed;
            m_System.TakeTypeChanges(type, changed, destroyed, sortByInstanceID);
            var data = new TypeDispatchData
            {
                changedID = Unbound.Runtime.ObjectDispatch.ToNative(changed, allocator),
                destroyedID = Unbound.Runtime.ObjectDispatch.ToNative(destroyed, allocator),
                changed = noScriptingArray ? null : Unbound.Runtime.ObjectDispatch.Objects(changed),
            };
            changed.Clear();
            destroyed.Clear();
            return data;
        }

        public TypeDispatchData GetTypeChangesAndClear<T>(Allocator allocator, bool sortByInstanceID = false, bool noScriptingArray = false) where T : Object =>
            GetTypeChangesAndClear(typeof(T), allocator, sortByInstanceID, noScriptingArray);

        public void GetTypeChangesAndClear(Type type, List<Object> changed, out NativeArray<int> changedID, out NativeArray<int> destroyedID, Allocator allocator, bool sortByInstanceID = false)
        {
            var data = GetTypeChangesAndClear(type, allocator, sortByInstanceID, false);
            changed?.Clear();
            changed?.AddRange(data.changed);
            changedID = data.changedID;
            destroyedID = data.destroyedID;
        }

        public void GetTypeChangesAndClear<T>(List<Object> changed, out NativeArray<int> changedID, out NativeArray<int> destroyedID, Allocator allocator, bool sortByInstanceID = false) where T : Object =>
            GetTypeChangesAndClear(typeof(T), changed, out changedID, out destroyedID, allocator, sortByInstanceID);

        public void DispatchTypeChangesAndClear(Type type, Action<TypeDispatchData> callback, bool sortByInstanceID = false, bool noScriptingArray = false)
        {
            var data = GetTypeChangesAndClear(type, Allocator.TempJob, sortByInstanceID, noScriptingArray);
            try { callback?.Invoke(data); }
            finally { data.Dispose(); }
        }

        public void DispatchTypeChangesAndClear<T>(Action<TypeDispatchData> callback, bool sortByInstanceID = false, bool noScriptingArray = false) where T : Object =>
            DispatchTypeChangesAndClear(typeof(T), callback, sortByInstanceID, noScriptingArray);

        public TransformDispatchData GetTransformChangesAndClear(Type type, TransformTrackingType trackingType, Allocator allocator) =>
            m_System.TakeTransformChanges(type, trackingType, allocator);

        public TransformDispatchData GetTransformChangesAndClear<T>(TransformTrackingType trackingType, Allocator allocator) where T : Object =>
            GetTransformChangesAndClear(typeof(T), trackingType, allocator);

        public Component[] GetTransformChangesAndClear(Type type, TransformTrackingType trackingType, bool sortByInstanceID = false)
        {
            var data = m_System.TakeTransformChanges(type, trackingType, Allocator.Temp, sortByInstanceID);
            try
            {
                var components = new Component[data.transformedID.Length];
                for (int i = 0; i < components.Length; i++)
                    components[i] = Unbound.Runtime.ObjectRegistry.FindById(data.transformedID[i]) as Component;
                return components;
            }
            finally { data.Dispose(); }
        }

        public Component[] GetTransformChangesAndClear<T>(TransformTrackingType trackingType, bool sortByInstanceID = false) where T : Object =>
            GetTransformChangesAndClear(typeof(T), trackingType, sortByInstanceID);

        public void DispatchTransformChangesAndClear(Type type, TransformTrackingType trackingType, Action<Component[]> callback, bool sortByInstanceID = false) =>
            callback?.Invoke(GetTransformChangesAndClear(type, trackingType, sortByInstanceID));

        public void DispatchTransformChangesAndClear<T>(TransformTrackingType trackingType, Action<Component[]> callback, bool sortByInstanceID = false) where T : Object =>
            DispatchTransformChangesAndClear(typeof(T), trackingType, callback, sortByInstanceID);

        public void DispatchTransformChangesAndClear(Type type, TransformTrackingType trackingType, Action<TransformDispatchData> callback)
        {
            var data = GetTransformChangesAndClear(type, trackingType, Allocator.TempJob);
            try { callback?.Invoke(data); }
            finally { data.Dispose(); }
        }

        public void DispatchTransformChangesAndClear<T>(TransformTrackingType trackingType, Action<TransformDispatchData> callback) where T : Object =>
            DispatchTransformChangesAndClear(typeof(T), trackingType, callback);
    }

    /// <summary>One dispatch of a tracked type: the objects created or changed since the last one
    /// (unless the caller asked for none), their IDs and the destroyed objects' IDs.</summary>
    internal partial struct TypeDispatchData : IDisposable
    {
        public Object[] changed;
        public NativeArray<int> changedID;
        public NativeArray<int> destroyedID;

        public void Dispose()
        {
            changed = null;
            if (changedID.IsCreated) changedID.Dispose();
            if (destroyedID.IsCreated) destroyedID.Dispose();
        }
    }

    /// <summary>One dispatch of a transform-tracked type: the components whose transform changed
    /// and, by the kind of tracking, their world matrices (GlobalTRS), local position, rotation
    /// and scale (LocalTRS) or parent's instance ID (Hierarchy).</summary>
    internal partial struct TransformDispatchData : IDisposable
    {
        public NativeArray<int> transformedID;
        public NativeArray<int> parentID;
        public NativeArray<Matrix4x4> localToWorldMatrices;
        public NativeArray<Vector3> positions;
        public NativeArray<Quaternion> rotations;
        public NativeArray<Vector3> scales;

        public void Dispose()
        {
            if (transformedID.IsCreated) transformedID.Dispose();
            if (parentID.IsCreated) parentID.Dispose();
            if (localToWorldMatrices.IsCreated) localToWorldMatrices.Dispose();
            if (positions.IsCreated) positions.Dispose();
            if (rotations.IsCreated) rotations.Dispose();
            if (scales.IsCreated) scales.Dispose();
        }
    }
}

namespace Unbound.Runtime
{
    /// <summary>
    /// The changes <see cref="UnityEngine.ObjectDispatcher"/>s report. Engine code records an
    /// object's creation (<see cref="Created"/>), a change of its state (<see cref="Changed"/>: a
    /// renderer's materials, mesh, settings or activation, a material's or a mesh's contents, an
    /// LOD group's levels) and its destruction (<see cref="Destroyed"/>); each dispatcher that
    /// tracks the object's type (exactly, not a base type) queues it until the dispatcher next
    /// asks; enabling the tracking of a type queues the objects of it alive then, as created. An object changed
    /// again moves to the end of the queue; a destroyed object leaves the changed queue for the
    /// destroyed one. Transform changes are read when asked: the tracked components whose
    /// transform's version moved since the last dispatch (or that the dispatcher had not seen).
    /// With no dispatcher alive, recording costs one comparison.
    /// </summary>
    internal static class ObjectDispatch
    {
        /// <summary>The number of live dispatchers tracking any type (the recording fast path).</summary>
        internal static int s_Tracking;
        static readonly List<System> s_Systems = new List<System>();
        internal static readonly List<int> Scratch = new List<int>();
        internal static readonly List<int> ScratchDestroyed = new List<int>();

        /// <summary>The frames a dispatcher may leave a tracked type's changes undispatched before
        /// its tracking stops with an error, as a new dispatcher starts (the Unity 6000.0.71f1
        /// Editor's, tests/projects/residentdrawer).</summary>
        internal const int DefaultMaxHistory = 64;

        internal static System Create()
        {
            var system = new System();
            s_Systems.Add(system);
            return system;
        }

        internal static void Destroy(System system)
        {
            if (!system.Live) return;
            system.Live = false;
            system.Clear();
            s_Systems.Remove(system);
            Recount();
        }

        internal static void Recount()
        {
            int tracking = 0;
            foreach (var system in s_Systems) tracking += system.TypeCount;
            s_Tracking = tracking;
        }

        internal static void Created(Object obj)
        {
            if (s_Tracking == 0 || obj is null) return;
            foreach (var system in s_Systems) system.Record(obj, created: true);
        }

        internal static void Changed(Object obj)
        {
            if (s_Tracking == 0 || obj is null) return;
            foreach (var system in s_Systems) system.Record(obj, created: false);
        }

        internal static void Destroyed(Object obj)
        {
            if (s_Tracking == 0 || obj is null) return;
            foreach (var system in s_Systems) system.RecordDestroyed(obj);
        }

        /// <summary>Whether the tracking flags take an object: a component of a GameObject in a
        /// scene is a scene object, a persistent asset an asset, any other object made at run time
        /// a scene object.</summary>
        static bool Takes(Object obj, ObjectDispatcher.TypeTrackingFlags flags) =>
            (flags & (IsAsset(obj) ? ObjectDispatcher.TypeTrackingFlags.Assets : ObjectDispatcher.TypeTrackingFlags.SceneObjects)) != 0;

        static bool IsAsset(Object obj) => obj is Component component
            ? component.m_GameObject is GameObject go && !go.scene.IsValid()
            : obj.IsPersistentAsset;

        internal static NativeArray<int> ToNative(List<int> ids, Allocator allocator)
        {
            var array = new NativeArray<int>(ids.Count, allocator, NativeArrayOptions.UninitializedMemory);
            for (int i = 0; i < ids.Count; i++) array[i] = ids[i];
            return array;
        }

        internal static Object[] Objects(List<int> ids)
        {
            var objects = new Object[ids.Count];
            for (int i = 0; i < ids.Count; i++) objects[i] = ObjectRegistry.FindById(ids[i]);
            return objects;
        }

        /// <summary>The per-frame check of every dispatcher's history: a tracked type whose changes
        /// were not dispatched for more than the dispatcher's frame count stops being tracked, with
        /// the Editor's error.</summary>
        internal static void PostLateUpdate()
        {
            if (s_Tracking == 0) return;
            int frame = Time.frameCount;
            foreach (var system in s_Systems) system.CheckHistory(frame);
        }

        /// <summary>A dispatcher's tracked types and their queues.</summary>
        internal sealed class System
        {
            internal bool Live = true;
            internal int MaxHistory = DefaultMaxHistory;
            readonly Dictionary<Type, TypeQueue> m_Types = new Dictionary<Type, TypeQueue>();
            readonly List<TypeQueue> m_Queues = new List<TypeQueue>();
            readonly Dictionary<(Type, ObjectDispatcher.TransformTrackingType), TransformQueue> m_Transforms = new Dictionary<(Type, ObjectDispatcher.TransformTrackingType), TransformQueue>();

            internal int TypeCount => m_Queues.Count + m_Transforms.Count;

            internal void Clear()
            {
                m_Types.Clear();
                m_Queues.Clear();
                m_Transforms.Clear();
            }

            internal void EnableType(Type type, ObjectDispatcher.TypeTrackingFlags flags)
            {
                if (type == null) return;
                if (m_Types.TryGetValue(type, out var queue))
                {
                    queue.Flags = flags;
                    return;
                }
                queue = new TypeQueue { Type = type, Flags = flags, LastDispatch = Time.frameCount };
                m_Types[type] = queue;
                m_Queues.Add(queue);
                Recount();
                // The objects of the type alive already come first, as created.
                foreach (var obj in ObjectRegistry.All(type))
                    if (obj.GetType() == type) queue.Change(obj.m_InstanceID, created: true);
            }

            internal void DisableType(Type type)
            {
                if (type == null || !m_Types.Remove(type, out var queue)) return;
                m_Queues.Remove(queue);
                Recount();
            }

            internal void EnableTransforms(Type type, ObjectDispatcher.TransformTrackingType tracking)
            {
                if (type == null || m_Transforms.ContainsKey((type, tracking))) return;
                m_Transforms[(type, tracking)] = new TransformQueue { Type = type };
                Recount();
            }

            internal void DisableTransforms(Type type, ObjectDispatcher.TransformTrackingType tracking)
            {
                if (type != null && m_Transforms.Remove((type, tracking))) Recount();
            }

            internal void Record(Object obj, bool created)
            {
                for (int i = 0; i < m_Queues.Count; i++)
                {
                    var queue = m_Queues[i];
                    if (queue.Type == obj.GetType()) queue.Change(obj.m_InstanceID, created);
                }
            }

            internal void RecordDestroyed(Object obj)
            {
                int id = obj.m_InstanceID;
                for (int i = 0; i < m_Queues.Count; i++)
                {
                    var queue = m_Queues[i];
                    if (queue.Type == obj.GetType() && Takes(obj, queue.Flags)) queue.Destroy(id);
                }
                foreach (var transforms in m_Transforms.Values) transforms.Forget(obj);
            }

            /// <summary>The type's changes since the last time, in queue order (or by instance
            /// ID), the objects the tracking flags take; the queue empties.</summary>
            internal void TakeTypeChanges(Type type, List<int> changed, List<int> destroyed, bool sort)
            {
                if (!Live) throw new ObjectDisposedException(nameof(ObjectDispatcher));
                if (type == null || !m_Types.TryGetValue(type, out var queue))
                {
                    Debug.LogError("Type tracking is not enabled for the system.");
                    return;
                }
                queue.LastDispatch = Time.frameCount;
                if (changed != null)
                {
                    foreach (int id in queue.Order)
                        if (id != 0 && ObjectRegistry.FindById(id) is Object obj && Takes(obj, queue.Flags)) changed.Add(id);
                    if (sort) changed.Sort();
                }
                if (destroyed != null)
                {
                    destroyed.AddRange(queue.DestroyedIds);
                    if (sort) destroyed.Sort();
                }
                queue.Reset();
            }

            internal TransformDispatchData TakeTransformChanges(Type type, ObjectDispatcher.TransformTrackingType tracking, Allocator allocator, bool sort = false)
            {
                if (!Live) throw new ObjectDisposedException(nameof(ObjectDispatcher));
                var data = new TransformDispatchData();
                if (type == null || !m_Transforms.TryGetValue((type, tracking), out var queue))
                {
                    Debug.LogError("Type Transform tracking is not enabled for the system.");
                    data.transformedID = new NativeArray<int>(0, allocator);
                    return data;
                }
                var changed = queue.Take();
                if (sort) changed.Sort((a, b) => a.GetInstanceID().CompareTo(b.GetInstanceID()));
                int count = changed.Count;
                data.transformedID = new NativeArray<int>(count, allocator, NativeArrayOptions.UninitializedMemory);
                switch (tracking)
                {
                    case ObjectDispatcher.TransformTrackingType.GlobalTRS:
                        data.localToWorldMatrices = new NativeArray<Matrix4x4>(count, allocator, NativeArrayOptions.UninitializedMemory);
                        break;
                    case ObjectDispatcher.TransformTrackingType.LocalTRS:
                        data.positions = new NativeArray<Vector3>(count, allocator, NativeArrayOptions.UninitializedMemory);
                        data.rotations = new NativeArray<Quaternion>(count, allocator, NativeArrayOptions.UninitializedMemory);
                        data.scales = new NativeArray<Vector3>(count, allocator, NativeArrayOptions.UninitializedMemory);
                        break;
                    default:
                        data.parentID = new NativeArray<int>(count, allocator, NativeArrayOptions.UninitializedMemory);
                        break;
                }
                for (int i = 0; i < count; i++)
                {
                    var component = changed[i];
                    var transform = component.transform;
                    data.transformedID[i] = component.GetInstanceID();
                    switch (tracking)
                    {
                        case ObjectDispatcher.TransformTrackingType.GlobalTRS:
                            data.localToWorldMatrices[i] = transform.localToWorldMatrix;
                            break;
                        case ObjectDispatcher.TransformTrackingType.LocalTRS:
                            data.positions[i] = transform.localPosition;
                            data.rotations[i] = transform.localRotation;
                            data.scales[i] = transform.localScale;
                            break;
                        default:
                            data.parentID[i] = transform.parent != null ? transform.parent.GetInstanceID() : 0;
                            break;
                    }
                }
                changed.Clear();
                return data;
            }

            internal void CheckHistory(int frame)
            {
                for (int i = m_Queues.Count - 1; i >= 0; i--)
                {
                    var queue = m_Queues[i];
                    if (frame - queue.LastDispatch <= MaxHistory) continue;
                    Debug.LogError($"ObjectDispatcher hasn't collected changes for type {queue.Type.Name} for more than {MaxHistory} frames. The Type tracking will be disabled. Use 'maxDispatchHistoryFramesCount' to increase maximum number of frames of the dispatch history.This might also happen if you forgot to dispose ObjectDispatcher or forgot to disable the Type tracking.");
                    DisableType(queue.Type);
                }
            }
        }

        /// <summary>A tracked type's queue: changed IDs in the order of their last change (0 marks
        /// a moved or destroyed entry), and the destroyed IDs.</summary>
        sealed class TypeQueue
        {
            internal Type Type;
            internal ObjectDispatcher.TypeTrackingFlags Flags;
            internal int LastDispatch;
            internal readonly List<int> Order = new List<int>();
            readonly Dictionary<int, int> m_At = new Dictionary<int, int>();
            internal readonly List<int> DestroyedIds = new List<int>();

            internal void Change(int id, bool created)
            {
                if (m_At.TryGetValue(id, out int at))
                {
                    if (created) return;
                    Order[at] = 0;
                }
                m_At[id] = Order.Count;
                Order.Add(id);
            }

            internal void Destroy(int id)
            {
                if (m_At.Remove(id, out int at)) Order[at] = 0;
                DestroyedIds.Add(id);
            }

            internal void Reset()
            {
                Order.Clear();
                m_At.Clear();
                DestroyedIds.Clear();
            }
        }

        /// <summary>A transform-tracked type: the transform version each live component of it had
        /// at the last dispatch.</summary>
        sealed class TransformQueue
        {
            internal Type Type;
            readonly Dictionary<Component, int> m_Versions = new Dictionary<Component, int>();
            readonly List<Component> m_Changed = new List<Component>();

            /// <summary>The live components of the type (active and enabled ones, as the drawer's
            /// renderers and LOD groups are) whose transform changed since the last dispatch.</summary>
            internal List<Component> Take()
            {
                m_Changed.Clear();
                foreach (var component in TrackedComponents.Of(Type))
                {
                    if (!component || !Type.IsInstanceOfType(component)) continue;
                    int version = component.transform.SyncedVersion;
                    if (m_Versions.TryGetValue(component, out int seen) && seen == version) continue;
                    m_Versions[component] = version;
                    m_Changed.Add(component);
                }
                return m_Changed;
            }

            internal void Forget(Object obj)
            {
                if (obj is Component component) m_Versions.Remove(component);
            }
        }
    }

    /// <summary>The live components a transform-tracked type enumerates: enabled renderers in
    /// active hierarchies, and LOD groups.</summary>
    internal static class TrackedComponents
    {
        static readonly List<Component> s_Scratch = new List<Component>();

        internal static List<Component> Of(Type type)
        {
            s_Scratch.Clear();
            if (typeof(Renderer).IsAssignableFrom(type))
                foreach (var renderer in RenderRegistry.Renderers) s_Scratch.Add(renderer);
            else if (typeof(LODGroup).IsAssignableFrom(type))
                foreach (var group in LODGroup.s_Live) s_Scratch.Add(group);
            else
                foreach (var obj in ObjectRegistry.All(type))
                    if (obj is Component component) s_Scratch.Add(component);
            return s_Scratch;
        }
    }
}
