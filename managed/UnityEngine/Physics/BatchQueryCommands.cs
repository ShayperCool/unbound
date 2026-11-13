using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using Unity.Collections.LowLevel.Unsafe;
using Unbound.Runtime.Jobs;
using Unbound.Runtime;
using Unity.Collections;
using Unity.Jobs;
using B3 = Unbound.Box3D.B3;

// Unity 6000.0 batch query contracts and measured limits are recorded in
// tests/projects/physicsbatch. Commands share the single-call native query path, with
// per-command face flags and initial sweep overlaps omitted at every maxHits.
// Results start at commandIndex * maxHits; unused slots are cleared. No hit order is promised.
// Scene preparation happens on the calling thread. Jobs read the prepared worlds and own
// their command/result containers until completion, using per-worker query storage.
namespace UnityEngine
{
    /// <summary>The layer mask, trigger interaction and face options of a batch query.</summary>
    public partial struct QueryParameters
    {
        public int layerMask;
        public bool hitMultipleFaces;
        public QueryTriggerInteraction hitTriggers;
        public bool hitBackfaces;

        public QueryParameters(int layerMask = -5, bool hitMultipleFaces = false, QueryTriggerInteraction hitTriggers = QueryTriggerInteraction.UseGlobal, bool hitBackfaces = false)
        {
            this.layerMask = layerMask;
            this.hitMultipleFaces = hitMultipleFaces;
            this.hitTriggers = hitTriggers;
            this.hitBackfaces = hitBackfaces;
        }

        /// <summary>Documented (6000.0 QueryParameters.Default): the default raycast layers, no
        /// back faces, triggers as Physics.queriesHitTriggers says, no multiple faces.</summary>
        public static QueryParameters Default => new QueryParameters(Physics.DefaultRaycastLayers, false, QueryTriggerInteraction.UseGlobal, false);
    }

    /// <summary>A collider an overlap batch query found. It holds the collider's instance ID only,
    /// so it can live in a NativeArray; the collider is looked up when read.</summary>
    public partial struct ColliderHit
    {
        int m_InstanceID;

        internal ColliderHit(int instanceID) => m_InstanceID = instanceID;

        public int instanceID => m_InstanceID;

        /// <summary>The collider with the instance ID; null for an unused result or a collider
        /// destroyed since.</summary>
        public Collider collider => m_InstanceID == 0 ? null : Physics.GetColliderByInstanceID(m_InstanceID);
    }

    public partial struct OverlapSphereCommand
    {
        public QueryParameters queryParameters;

        public OverlapSphereCommand(Vector3 point, float radius, QueryParameters queryParameters)
            : this(Physics.defaultPhysicsScene, point, radius, queryParameters)
        {
        }

        public OverlapSphereCommand(PhysicsScene physicsScene, Vector3 point, float radius, QueryParameters queryParameters)
        {
            this.physicsScene = physicsScene;
            this.point = point;
            this.radius = radius;
            this.queryParameters = queryParameters;
        }

        public Vector3 point { get; set; }

        public float radius { get; set; }

        public PhysicsScene physicsScene { get; set; }

        public static JobHandle ScheduleBatch(NativeArray<OverlapSphereCommand> commands, NativeArray<ColliderHit> results, int minCommandsPerJob, int maxHits, JobHandle dependsOn = default) =>
            PhysicsBatchQueries.Overlaps("OverlapSphereCommand.ScheduleBatch", commands, results, minCommandsPerJob, maxHits, dependsOn,
                (in OverlapSphereCommand c, Collider[] found, int capacity) => PhysicsQueries.BatchOverlap(c.physicsScene, B3.Proxy.Sphere, c.point, default, Quaternion.identity, c.radius, c.queryParameters, found, capacity));
    }

    public partial struct OverlapBoxCommand
    {
        public QueryParameters queryParameters;

        public OverlapBoxCommand(Vector3 center, Vector3 halfExtents, Quaternion orientation, QueryParameters queryParameters)
            : this(Physics.defaultPhysicsScene, center, halfExtents, orientation, queryParameters)
        {
        }

        public OverlapBoxCommand(PhysicsScene physicsScene, Vector3 center, Vector3 halfExtents, Quaternion orientation, QueryParameters queryParameters)
        {
            this.physicsScene = physicsScene;
            this.center = center;
            this.halfExtents = halfExtents;
            this.orientation = orientation;
            this.queryParameters = queryParameters;
        }

        public Vector3 center { get; set; }

        public Vector3 halfExtents { get; set; }

        public Quaternion orientation { get; set; }

        public PhysicsScene physicsScene { get; set; }

        public static JobHandle ScheduleBatch(NativeArray<OverlapBoxCommand> commands, NativeArray<ColliderHit> results, int minCommandsPerJob, int maxHits, JobHandle dependsOn = default) =>
            PhysicsBatchQueries.Overlaps("OverlapBoxCommand.ScheduleBatch", commands, results, minCommandsPerJob, maxHits, dependsOn,
                (in OverlapBoxCommand c, Collider[] found, int capacity) => PhysicsQueries.BatchOverlap(c.physicsScene, B3.Proxy.Box, c.center, c.halfExtents, c.orientation, 0f, c.queryParameters, found, capacity));
    }

    public partial struct OverlapCapsuleCommand
    {
        public QueryParameters queryParameters;

        public OverlapCapsuleCommand(Vector3 point0, Vector3 point1, float radius, QueryParameters queryParameters)
            : this(Physics.defaultPhysicsScene, point0, point1, radius, queryParameters)
        {
        }

        public OverlapCapsuleCommand(PhysicsScene physicsScene, Vector3 point0, Vector3 point1, float radius, QueryParameters queryParameters)
        {
            this.physicsScene = physicsScene;
            this.point0 = point0;
            this.point1 = point1;
            this.radius = radius;
            this.queryParameters = queryParameters;
        }

        public Vector3 point0 { get; set; }

        public Vector3 point1 { get; set; }

        public float radius { get; set; }

        public PhysicsScene physicsScene { get; set; }

        public static JobHandle ScheduleBatch(NativeArray<OverlapCapsuleCommand> commands, NativeArray<ColliderHit> results, int minCommandsPerJob, int maxHits, JobHandle dependsOn = default) =>
            PhysicsBatchQueries.Overlaps("OverlapCapsuleCommand.ScheduleBatch", commands, results, minCommandsPerJob, maxHits, dependsOn,
                (in OverlapCapsuleCommand c, Collider[] found, int capacity) => PhysicsQueries.BatchOverlap(c.physicsScene, B3.Proxy.Capsule, c.point0, c.point1, Quaternion.identity, c.radius, c.queryParameters, found, capacity));
    }

    public partial struct ClosestPointCommand
    {
        public ClosestPointCommand(Vector3 point, int colliderInstanceID, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            this.point = point;
            this.colliderInstanceID = colliderInstanceID;
            this.position = position;
            this.rotation = rotation;
            this.scale = scale;
        }

        public ClosestPointCommand(Vector3 point, Collider collider, Vector3 position, Quaternion rotation, Vector3 scale)
            : this(point, InstanceIdOf(collider), position, rotation, scale)
        {
        }

        static int InstanceIdOf(Collider collider) => collider.GetInstanceID();

        public Vector3 point { get; set; }

        public int colliderInstanceID { get; set; }

        public Vector3 position { get; set; }

        public Quaternion rotation { get; set; }

        public Vector3 scale { get; set; }

        public static JobHandle ScheduleBatch(NativeArray<ClosestPointCommand> commands, NativeArray<Vector3> results, int minCommandsPerJob, JobHandle dependsOn = default)
        {
            return PhysicsBatchQueries.ClosestPoints(commands, results, minCommandsPerJob, dependsOn);
        }
    }

    public partial struct RaycastCommand
    {
        public QueryParameters queryParameters;
        int m_MaxHits;

        public RaycastCommand(Vector3 from, Vector3 direction, QueryParameters queryParameters, float distance = float.MaxValue)
            : this(Physics.defaultPhysicsScene, from, direction, queryParameters, distance)
        {
        }

        public RaycastCommand(PhysicsScene physicsScene, Vector3 from, Vector3 direction, QueryParameters queryParameters, float distance = float.MaxValue)
        {
            this.physicsScene = physicsScene;
            this.from = from;
            this.direction = direction;
            this.queryParameters = queryParameters;
            this.distance = distance;
            // Retained storage is used only by the unmeasured profiles.
            m_MaxHits = 1;
        }

        // Legacy overloads pass the layer mask through QueryParameters. The measured profile
        // ignores per-command maxHits storage; other profiles retain their unmeasured behavior.
        [global::System.@ObsoleteAttribute("This struct signature is no longer supported. Use struct with a QueryParameters instead", false)]
        public RaycastCommand(Vector3 from, Vector3 direction, float distance = float.MaxValue, int layerMask = -5, int maxHits = 1)
            : this(Physics.defaultPhysicsScene, from, direction, new QueryParameters(layerMask), distance)
        {
            m_MaxHits = maxHits;
        }

        [global::System.@ObsoleteAttribute("This struct signature is no longer supported. Use struct with a QueryParameters instead", false)]
        public RaycastCommand(PhysicsScene physicsScene, Vector3 from, Vector3 direction, float distance = float.MaxValue, int layerMask = -5, int maxHits = 1)
            : this(physicsScene, from, direction, new QueryParameters(layerMask), distance)
        {
            m_MaxHits = maxHits;
        }

        public Vector3 from { get; set; }

        public Vector3 direction { get; set; }

        public PhysicsScene physicsScene { get; set; }

        public float distance { get; set; }

        /// <summary>Kept for the obsolete signature only: ScheduleBatch takes maxHits as its
        /// argument (1 in the overload without it) and does not read this.</summary>
        [global::System.@ObsoleteAttribute("maxHits property was moved to be a part of RaycastCommand.ScheduleBatch.", false)]
        public int maxHits
        {
            get => Compat.RemovedPhysicsSettingsMeasured ? 1 : m_MaxHits;
            set { if (!Compat.RemovedPhysicsSettingsMeasured) m_MaxHits = value; }
        }

        [global::System.@ObsoleteAttribute("Layer Mask is now a part of QueryParameters struct", false)]
        public int layerMask
        {
            get => queryParameters.layerMask;
            set => queryParameters.layerMask = value;
        }

        public static JobHandle ScheduleBatch(NativeArray<RaycastCommand> commands, NativeArray<RaycastHit> results, int minCommandsPerJob, int maxHits, JobHandle dependsOn = default) =>
            PhysicsBatchQueries.Casts("RaycastCommand.ScheduleBatch", commands, results, minCommandsPerJob, maxHits, dependsOn,
                (in RaycastCommand c, RaycastHit[] found, bool closest, int capacity) => PhysicsQueries.BatchCast(c.physicsScene, true, default, c.from, default, Quaternion.identity, 0f, c.direction, c.distance, c.queryParameters, found, closest, capacity));

        /// <summary>Documented: maxHits is 1 in this variant.</summary>
        public static JobHandle ScheduleBatch(NativeArray<RaycastCommand> commands, NativeArray<RaycastHit> results, int minCommandsPerJob, JobHandle dependsOn = default) =>
            ScheduleBatch(commands, results, minCommandsPerJob, 1, dependsOn);
    }

    public partial struct SpherecastCommand
    {
        public QueryParameters queryParameters;

        public SpherecastCommand(Vector3 origin, float radius, Vector3 direction, QueryParameters queryParameters, float distance = float.MaxValue)
            : this(Physics.defaultPhysicsScene, origin, radius, direction, queryParameters, distance)
        {
        }

        public SpherecastCommand(PhysicsScene physicsScene, Vector3 origin, float radius, Vector3 direction, QueryParameters queryParameters, float distance = float.MaxValue)
        {
            this.physicsScene = physicsScene;
            this.origin = origin;
            this.radius = radius;
            this.direction = direction;
            this.queryParameters = queryParameters;
            this.distance = distance;
        }

        [global::System.@ObsoleteAttribute("This struct signature is no longer supported. Use struct with a QueryParameters instead", false)]
        public SpherecastCommand(Vector3 origin, float radius, Vector3 direction, float distance = float.MaxValue, int layerMask = -5)
            : this(Physics.defaultPhysicsScene, origin, radius, direction, new QueryParameters(layerMask), distance)
        {
        }

        [global::System.@ObsoleteAttribute("This struct signature is no longer supported. Use struct with a QueryParameters instead", false)]
        public SpherecastCommand(PhysicsScene physicsScene, Vector3 origin, float radius, Vector3 direction, float distance = float.MaxValue, int layerMask = -5)
            : this(physicsScene, origin, radius, direction, new QueryParameters(layerMask), distance)
        {
        }

        public Vector3 origin { get; set; }

        public float radius { get; set; }

        public Vector3 direction { get; set; }

        public float distance { get; set; }

        public PhysicsScene physicsScene { get; set; }

        [global::System.@ObsoleteAttribute("Layer Mask is now a part of QueryParameters struct", false)]
        public int layerMask
        {
            get => queryParameters.layerMask;
            set => queryParameters.layerMask = value;
        }

        public static JobHandle ScheduleBatch(NativeArray<SpherecastCommand> commands, NativeArray<RaycastHit> results, int minCommandsPerJob, int maxHits, JobHandle dependsOn = default) =>
            PhysicsBatchQueries.Casts("SpherecastCommand.ScheduleBatch", commands, results, minCommandsPerJob, maxHits, dependsOn,
                (in SpherecastCommand c, RaycastHit[] found, bool closest, int capacity) => PhysicsQueries.BatchCast(c.physicsScene, false, B3.Proxy.Sphere, c.origin, default, Quaternion.identity, c.radius, c.direction, c.distance, c.queryParameters, found, closest, capacity));

        /// <summary>Documented: maxHits is 1 in this variant.</summary>
        public static JobHandle ScheduleBatch(NativeArray<SpherecastCommand> commands, NativeArray<RaycastHit> results, int minCommandsPerJob, JobHandle dependsOn = default) =>
            ScheduleBatch(commands, results, minCommandsPerJob, 1, dependsOn);
    }

    public partial struct BoxcastCommand
    {
        public QueryParameters queryParameters;

        public BoxcastCommand(Vector3 center, Vector3 halfExtents, Quaternion orientation, Vector3 direction, QueryParameters queryParameters, float distance = float.MaxValue)
            : this(Physics.defaultPhysicsScene, center, halfExtents, orientation, direction, queryParameters, distance)
        {
        }

        public BoxcastCommand(PhysicsScene physicsScene, Vector3 center, Vector3 halfExtents, Quaternion orientation, Vector3 direction, QueryParameters queryParameters, float distance = float.MaxValue)
        {
            this.physicsScene = physicsScene;
            this.center = center;
            this.halfExtents = halfExtents;
            this.orientation = orientation;
            this.direction = direction;
            this.queryParameters = queryParameters;
            this.distance = distance;
        }

        [global::System.@ObsoleteAttribute("This struct signature is no longer supported. Use struct with a QueryParameters instead", false)]
        public BoxcastCommand(Vector3 center, Vector3 halfExtents, Quaternion orientation, Vector3 direction, float distance = float.MaxValue, int layerMask = -5)
            : this(Physics.defaultPhysicsScene, center, halfExtents, orientation, direction, new QueryParameters(layerMask), distance)
        {
        }

        [global::System.@ObsoleteAttribute("This struct signature is no longer supported. Use struct with a QueryParameters instead", false)]
        public BoxcastCommand(PhysicsScene physicsScene, Vector3 center, Vector3 halfExtents, Quaternion orientation, Vector3 direction, float distance = float.MaxValue, int layerMask = -5)
            : this(physicsScene, center, halfExtents, orientation, direction, new QueryParameters(layerMask), distance)
        {
        }

        public Vector3 center { get; set; }

        public Vector3 halfExtents { get; set; }

        public Quaternion orientation { get; set; }

        public Vector3 direction { get; set; }

        public float distance { get; set; }

        public PhysicsScene physicsScene { get; set; }

        [global::System.@ObsoleteAttribute("Layer Mask is now a part of QueryParameters struct", false)]
        public int layerMask
        {
            get => queryParameters.layerMask;
            set => queryParameters.layerMask = value;
        }

        public static JobHandle ScheduleBatch(NativeArray<BoxcastCommand> commands, NativeArray<RaycastHit> results, int minCommandsPerJob, int maxHits, JobHandle dependsOn = default) =>
            PhysicsBatchQueries.Casts("BoxcastCommand.ScheduleBatch", commands, results, minCommandsPerJob, maxHits, dependsOn,
                (in BoxcastCommand c, RaycastHit[] found, bool closest, int capacity) => PhysicsQueries.BatchCast(c.physicsScene, false, B3.Proxy.Box, c.center, c.halfExtents, c.orientation, 0f, c.direction, c.distance, c.queryParameters, found, closest, capacity));

        /// <summary>Documented: maxHits is 1 in this variant.</summary>
        public static JobHandle ScheduleBatch(NativeArray<BoxcastCommand> commands, NativeArray<RaycastHit> results, int minCommandsPerJob, JobHandle dependsOn = default) =>
            ScheduleBatch(commands, results, minCommandsPerJob, 1, dependsOn);
    }

    public partial struct CapsulecastCommand
    {
        public QueryParameters queryParameters;

        public CapsulecastCommand(Vector3 p1, Vector3 p2, float radius, Vector3 direction, QueryParameters queryParameters, float distance = float.MaxValue)
            : this(Physics.defaultPhysicsScene, p1, p2, radius, direction, queryParameters, distance)
        {
        }

        public CapsulecastCommand(PhysicsScene physicsScene, Vector3 p1, Vector3 p2, float radius, Vector3 direction, QueryParameters queryParameters, float distance = float.MaxValue)
        {
            this.physicsScene = physicsScene;
            point1 = p1;
            point2 = p2;
            this.radius = radius;
            this.direction = direction;
            this.queryParameters = queryParameters;
            this.distance = distance;
        }

        [global::System.@ObsoleteAttribute("This struct signature is no longer supported. Use struct with a QueryParameters instead", false)]
        public CapsulecastCommand(Vector3 p1, Vector3 p2, float radius, Vector3 direction, float distance = float.MaxValue, int layerMask = -5)
            : this(Physics.defaultPhysicsScene, p1, p2, radius, direction, new QueryParameters(layerMask), distance)
        {
        }

        [global::System.@ObsoleteAttribute("This struct signature is no longer supported. Use struct with a QueryParameters instead", false)]
        public CapsulecastCommand(PhysicsScene physicsScene, Vector3 p1, Vector3 p2, float radius, Vector3 direction, float distance = float.MaxValue, int layerMask = -5)
            : this(physicsScene, p1, p2, radius, direction, new QueryParameters(layerMask), distance)
        {
        }

        public Vector3 point1 { get; set; }

        public Vector3 point2 { get; set; }

        public float radius { get; set; }

        public Vector3 direction { get; set; }

        public float distance { get; set; }

        public PhysicsScene physicsScene { get; set; }

        [global::System.@ObsoleteAttribute("Layer Mask is now a part of QueryParameters struct", false)]
        public int layerMask
        {
            get => queryParameters.layerMask;
            set => queryParameters.layerMask = value;
        }

        public static JobHandle ScheduleBatch(NativeArray<CapsulecastCommand> commands, NativeArray<RaycastHit> results, int minCommandsPerJob, int maxHits, JobHandle dependsOn = default) =>
            PhysicsBatchQueries.Casts("CapsulecastCommand.ScheduleBatch", commands, results, minCommandsPerJob, maxHits, dependsOn,
                (in CapsulecastCommand c, RaycastHit[] found, bool closest, int capacity) => PhysicsQueries.BatchCast(c.physicsScene, false, B3.Proxy.Capsule, c.point1, c.point2, Quaternion.identity, c.radius, c.direction, c.distance, c.queryParameters, found, closest, capacity));

        /// <summary>Documented: maxHits is 1 in this variant.</summary>
        public static JobHandle ScheduleBatch(NativeArray<CapsulecastCommand> commands, NativeArray<RaycastHit> results, int minCommandsPerJob, JobHandle dependsOn = default) =>
            ScheduleBatch(commands, results, minCommandsPerJob, 1, dependsOn);
    }
}

namespace Unbound.Runtime
{
    using UnityEngine;

    /// <summary>What the 3D batch query commands share: argument checks, and answering overlap
    /// commands into maxHits result slots each with their PhysicsScene query.</summary>
    internal static class PhysicsBatchQueries
    {
        internal delegate int OverlapQuery<TCommand>(in TCommand command, Collider[] found, int capacity);

        /// <summary>A container the batch reads or writes is assigned and not deallocated (the job
        /// scheduler's checks and messages).</summary>
        internal static void CheckContainer<T>(string api, NativeArray<T> array, string name) where T : struct
        {
            var handle = array.m_Safety;
            var type = Safety.TypeName(handle.staticSafetyId);
            // In the job scheduler's order: a disposed array keeps its released handle.
            if (handle.versionNode == IntPtr.Zero)
                throw new InvalidOperationException($"{api}: the {type} {name} has not been assigned or constructed. All containers must be valid when scheduling a job.");
            if (!Safety.IsAlive(handle))
                throw new InvalidOperationException($"{api}: the {type} {name} has been deallocated. All containers must be valid when scheduling a job.");
            if (Safety.IsTemp(handle))
                throw new InvalidOperationException($"{api}: the {type} {name} is allocated with Temp memory. Use TempJob when scheduling a job.");
            if (!array.IsCreated)
                throw new InvalidOperationException($"{api}: the {type} {name} has not been assigned or constructed. All containers must be valid when scheduling a job.");
        }

        internal static JobHandle Overlaps<TCommand>(string api, NativeArray<TCommand> commands, NativeArray<ColliderHit> results, int minCommandsPerJob, int maxHits, JobHandle dependsOn, OverlapQuery<TCommand> query)
            where TCommand : struct
        {
            if (!CheckCapacity(commands.Length, results.Length, maxHits)) return default;
            return Schedule(api, commands, results, minCommandsPerJob, maxHits, dependsOn,
                (in TCommand c, Collider[] found, bool _, int capacity) => query(in c, found, capacity),
                c => new ColliderHit(c.m_InstanceID));
        }

        internal static bool CheckCapacity(int commands, int results, int maxHits)
        {
            if (maxHits < 1)
            {
                Debug.LogWarning("maxHits should be greater than 0.");
                return false;
            }
            if (results < (long)commands * maxHits)
            {
                Debug.LogWarning("The supplied results buffer is too small, there should be at least maxHits space per each command in the batch.");
                return false;
            }
            return true;
        }

        internal delegate int CastQuery<TCommand>(in TCommand command, RaycastHit[] found, bool closest, int capacity);

        internal static JobHandle Casts<TCommand>(string api, NativeArray<TCommand> commands, NativeArray<RaycastHit> results, int minCommandsPerJob, int maxHits, JobHandle dependsOn,
            CastQuery<TCommand> query) where TCommand : struct
        {
            if (!CheckCapacity(commands.Length, results.Length, maxHits)) return default;
            return Schedule<TCommand, RaycastHit, RaycastHit>(api, commands, results, minCommandsPerJob, maxHits, dependsOn, query.Invoke, hit => hit);
        }

        delegate int Query<TCommand, TFound>(in TCommand command, TFound[] found, bool closest, int capacity);

        static class WorkerScratch<T>
        {
            [ThreadStatic] static T[] s_Buffer;
            internal static T[] Reserve(int capacity)
            {
                if (s_Buffer == null) s_Buffer = new T[Math.Max(64, capacity)];
                else if (s_Buffer.Length < capacity)
                    s_Buffer = new T[Math.Max(capacity, (int)Math.Min(int.MaxValue, (long)s_Buffer.Length * 2))];
                return s_Buffer;
            }
        }

        sealed class BatchState<TCommand, TResult, TFound>
            where TCommand : struct where TResult : struct
        {
            internal Dictionary<PhysicsWorld.Space, uint> Worlds;
            internal Query<TCommand, TFound> Query;
            internal Func<TFound, TResult> Convert;
            internal readonly int MaxHits;
            internal GCHandle Handle;
            internal bool Cancelled;

            internal BatchState(int maxHits) => MaxHits = maxHits;
            internal void Dispose() => Handle.Free();

            internal void Execute(NativeArray<TCommand> commands, NativeArray<TResult> results, int index)
            {
                if (Cancelled) return;
                var found = WorkerScratch<TFound>.Reserve(MaxHits);
                using var prepared = PhysicsQueries.Prepared(Worlds);
                PhysicsQueries.Reserve(MaxHits);
                var command = commands[index];
                try
                {
                    int count = Query(in command, found, MaxHits == 1, MaxHits);
                    int start = index * MaxHits;
                    for (int k = 0; k < MaxHits; k++) results[start + k] = k < count ? Convert(found[k]) : default;
                }
                finally
                {
                    // Retained overlap scratch must not keep colliders alive between batches.
                    if (RuntimeHelpers.IsReferenceOrContainsReferences<TFound>()) Array.Clear(found, 0, MaxHits);
                }
            }
        }

        struct BatchJob<TCommand, TResult, TFound> : IJobParallelFor
            where TCommand : struct where TResult : struct
        {
            [ReadOnly] public NativeArray<TCommand> Commands;
            // A command owns its complete maxHits block, which may have several elements.
            [WriteOnly, NativeDisableParallelForRestriction] public NativeArray<TResult> Results;
            public IntPtr State;
            public void Execute(int index) => ((BatchState<TCommand, TResult, TFound>)GCHandle.FromIntPtr(State).Target).Execute(Commands, Results, index);
        }

        static JobHandle Schedule<TCommand, TResult, TFound>(string api, NativeArray<TCommand> commands, NativeArray<TResult> results,
            int minCommandsPerJob, int maxHits, JobHandle dependsOn, Query<TCommand, TFound> query, Func<TFound, TResult> convert)
            where TCommand : struct where TResult : struct
        {
            if (commands.Length == 0) return dependsOn;
            CheckContainer(api, commands, nameof(commands));
            CheckContainer(api, results, nameof(results));
            var state = new BatchState<TCommand, TResult, TFound>(maxHits) { Query = query, Convert = convert };
            state.Handle = GCHandle.Alloc(state);
            JobHandle handle;
            try
            {
                // Scheduling validates dependencies and aliases before scene preparation can wait
                // for earlier query reads. It does not complete the user's dependency handle.
                handle = new BatchJob<TCommand, TResult, TFound> { Commands = commands, Results = results, State = GCHandle.ToIntPtr(state.Handle) }
                    .Schedule(commands.Length, Math.Clamp(minCommandsPerJob, 1, commands.Length), dependsOn);
            }
            catch { state.Dispose(); throw; }
            var record = JobSystem.RecordOf(handle);
            record.Held = true;
            record.OnFinished = state.Dispose;
            try
            {
                state.Worlds = PhysicsWorld.PrepareBatchWorlds();
            }
            catch
            {
                state.Cancelled = true;
                record.Held = false;
                handle.Complete();
                throw;
            }
            record.Held = false;
            PhysicsWorld.TrackQueryJob(handle);
            return handle;
        }

        readonly struct ClosestCollider
        {
            internal readonly ulong Shape;
            internal readonly Vector3 Position;
            internal readonly Quaternion Rotation;
            internal readonly bool Supported;
            internal ClosestCollider(Collider c)
            {
                Shape = c.m_Shape;
                Supported = c is BoxCollider or SphereCollider or CapsuleCollider or MeshCollider { convex: true };
                Position = Vector3.zero;
                Rotation = Quaternion.identity;
                if (Shape == 0) return;
                ulong body = c.m_AttachedBody != null ? c.m_AttachedBody.m_Body : c.m_StaticBody;
                B3.ua3_body_get_transform(body, out var position, out var rotation);
                Position = position + rotation * c.m_ShapeMatrix.MultiplyPoint3x4(Vector3.zero);
                Rotation = rotation * Collider.AxisRotation(c.m_ShapeMatrix);
            }
        }

        internal static JobHandle ClosestPoints(NativeArray<ClosestPointCommand> commands, NativeArray<Vector3> results, int minCommandsPerJob, JobHandle dependsOn)
        {
            if (results.Length < commands.Length)
            {
                Debug.LogWarning("The supplied results buffer is too small, there should be at least one result per command in the batch.");
                return default;
            }
            return Schedule("ClosestPointCommand.ScheduleBatch", commands, results, minCommandsPerJob, 1, dependsOn,
                (in ClosestPointCommand c, Vector3[] found, bool _, int capacity) =>
                {
                    found[0] = c.point;
                    // The registry lookup is synchronized; scene geometry and collider frames
                    // remain stable until this job finishes. Resolve only this command's ID,
                    // after its dependencies have written the command buffer.
                    var target = Physics.GetColliderByInstanceID(c.colliderInstanceID);
                    var collider = target != null ? new ClosestCollider(target) : default;
                    if (target == null)
                        Debug.LogError($"Failed to resolve instance id: {c.colliderInstanceID}");
                    else if (!collider.Supported)
                        Debug.LogError("ClosestPointCommand can only be used with a BoxCollider, SphereCollider, CapsuleCollider and a convex MeshCollider.");
                    else if (collider.Shape != 0)
                    {
                        var local = Quaternion.Inverse(c.rotation) * (c.point - c.position);
                        var closest = B3.ua3_shape_closest_point(collider.Shape, collider.Position + collider.Rotation * local);
                        found[0] = c.position + c.rotation * (Quaternion.Inverse(collider.Rotation) * (closest - collider.Position));
                    }
                    return 1;
                }, point => point);
        }
    }
}
