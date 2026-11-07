using System;
using Unbound.Runtime;
using Unity.Collections;
using Unity.Jobs;
using B3 = Unbound.Box3D.B3;

// Unity 6000.0 batch query contracts and measured limits are recorded in
// tests/projects/physicsbatch. Commands share the single-call native query path, with
// per-command face flags and initial sweep overlaps omitted at every maxHits.
// Results start at commandIndex * maxHits; unused slots are cleared. No hit order is promised.
// Scheduling currently completes dependencies and queries on the calling thread. Parallel
// execution remains a limitation while scene preparation and managed lookup use main-thread state.
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
            PhysicsBatchQueries.Overlaps("OverlapSphereCommand.ScheduleBatch", commands, results, maxHits, dependsOn,
                (in OverlapSphereCommand c, Collider[] found) => PhysicsQueries.BatchOverlap(c.physicsScene, B3.Proxy.Sphere, c.point, default, Quaternion.identity, c.radius, c.queryParameters, found));
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
            PhysicsBatchQueries.Overlaps("OverlapBoxCommand.ScheduleBatch", commands, results, maxHits, dependsOn,
                (in OverlapBoxCommand c, Collider[] found) => PhysicsQueries.BatchOverlap(c.physicsScene, B3.Proxy.Box, c.center, c.halfExtents, c.orientation, 0f, c.queryParameters, found));
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
            PhysicsBatchQueries.Overlaps("OverlapCapsuleCommand.ScheduleBatch", commands, results, maxHits, dependsOn,
                (in OverlapCapsuleCommand c, Collider[] found) => PhysicsQueries.BatchOverlap(c.physicsScene, B3.Proxy.Capsule, c.point0, c.point1, Quaternion.identity, c.radius, c.queryParameters, found));
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
            const string api = "ClosestPointCommand.ScheduleBatch";
            if (results.Length < commands.Length)
            {
                Debug.LogWarning("The supplied results buffer is too small, there should be at least one result per command in the batch.");
                return default;
            }
            if (commands.Length == 0) return dependsOn;
            PhysicsBatchQueries.CheckContainer(api, commands, nameof(commands));
            PhysicsBatchQueries.CheckContainer(api, results, nameof(results));
            dependsOn.Complete();
            using var batch = PhysicsQueries.Batch();
            for (int i = 0; i < commands.Length; i++)
            {
                var command = commands[i];
                var collider = Physics.GetColliderByInstanceID(command.colliderInstanceID);
                if (collider == null)
                {
                    Debug.LogError($"Failed to resolve instance id: {command.colliderInstanceID}");
                    results[i] = command.point;
                    continue;
                }
                if (collider is not BoxCollider && collider is not SphereCollider && collider is not CapsuleCollider && collider is not MeshCollider { convex: true })
                {
                    Debug.LogError("ClosestPointCommand can only be used with a BoxCollider, SphereCollider, CapsuleCollider and a convex MeshCollider.");
                    results[i] = command.point;
                    continue;
                }
                // The scale field has no effect in the measured Editor. Use the collider's
                // geometry at its actual scale, as Physics.ClosestPoint does.
                results[i] = Physics.ClosestPoint(command.point, collider, command.position, command.rotation);
            }
            return default;
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
            // Unbound's choice (unmeasured): the obsolete per-command maxHits reads 1 unless set.
            m_MaxHits = 1;
        }

        // Unbound's choice (unmeasured) for the obsolete signatures: the layer mask goes into
        // QueryParameters, every other parameter keeps its constructor default.
        [Obsolete("This struct signature is no longer supported. Use struct with a QueryParameters instead")]
        public RaycastCommand(Vector3 from, Vector3 direction, float distance = float.MaxValue, int layerMask = -5, int maxHits = 1)
            : this(Physics.defaultPhysicsScene, from, direction, new QueryParameters(layerMask), distance)
        {
            m_MaxHits = maxHits;
        }

        [Obsolete("This struct signature is no longer supported. Use struct with a QueryParameters instead")]
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
        [Obsolete("maxHits property was moved to be a part of RaycastCommand.ScheduleBatch.")]
        public int maxHits
        {
            get => m_MaxHits;
            set => m_MaxHits = value;
        }

        [Obsolete("Layer Mask is now a part of QueryParameters struct")]
        public int layerMask
        {
            get => queryParameters.layerMask;
            set => queryParameters.layerMask = value;
        }

        public static JobHandle ScheduleBatch(NativeArray<RaycastCommand> commands, NativeArray<RaycastHit> results, int minCommandsPerJob, int maxHits, JobHandle dependsOn = default) =>
            PhysicsBatchQueries.Casts("RaycastCommand.ScheduleBatch", commands, results, maxHits, dependsOn,
                (in RaycastCommand c, RaycastHit[] found, bool closest) => PhysicsQueries.BatchCast(c.physicsScene, true, default, c.from, default, Quaternion.identity, 0f, c.direction, c.distance, c.queryParameters, found, closest));

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

        [Obsolete("This struct signature is no longer supported. Use struct with a QueryParameters instead")]
        public SpherecastCommand(Vector3 origin, float radius, Vector3 direction, float distance = float.MaxValue, int layerMask = -5)
            : this(Physics.defaultPhysicsScene, origin, radius, direction, new QueryParameters(layerMask), distance)
        {
        }

        [Obsolete("This struct signature is no longer supported. Use struct with a QueryParameters instead")]
        public SpherecastCommand(PhysicsScene physicsScene, Vector3 origin, float radius, Vector3 direction, float distance = float.MaxValue, int layerMask = -5)
            : this(physicsScene, origin, radius, direction, new QueryParameters(layerMask), distance)
        {
        }

        public Vector3 origin { get; set; }

        public float radius { get; set; }

        public Vector3 direction { get; set; }

        public float distance { get; set; }

        public PhysicsScene physicsScene { get; set; }

        [Obsolete("Layer Mask is now a part of QueryParameters struct")]
        public int layerMask
        {
            get => queryParameters.layerMask;
            set => queryParameters.layerMask = value;
        }

        public static JobHandle ScheduleBatch(NativeArray<SpherecastCommand> commands, NativeArray<RaycastHit> results, int minCommandsPerJob, int maxHits, JobHandle dependsOn = default) =>
            PhysicsBatchQueries.Casts("SpherecastCommand.ScheduleBatch", commands, results, maxHits, dependsOn,
                (in SpherecastCommand c, RaycastHit[] found, bool closest) => PhysicsQueries.BatchCast(c.physicsScene, false, B3.Proxy.Sphere, c.origin, default, Quaternion.identity, c.radius, c.direction, c.distance, c.queryParameters, found, closest));

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

        [Obsolete("This struct signature is no longer supported. Use struct with a QueryParameters instead")]
        public BoxcastCommand(Vector3 center, Vector3 halfExtents, Quaternion orientation, Vector3 direction, float distance = float.MaxValue, int layerMask = -5)
            : this(Physics.defaultPhysicsScene, center, halfExtents, orientation, direction, new QueryParameters(layerMask), distance)
        {
        }

        [Obsolete("This struct signature is no longer supported. Use struct with a QueryParameters instead")]
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

        [Obsolete("Layer Mask is now a part of QueryParameters struct")]
        public int layerMask
        {
            get => queryParameters.layerMask;
            set => queryParameters.layerMask = value;
        }

        public static JobHandle ScheduleBatch(NativeArray<BoxcastCommand> commands, NativeArray<RaycastHit> results, int minCommandsPerJob, int maxHits, JobHandle dependsOn = default) =>
            PhysicsBatchQueries.Casts("BoxcastCommand.ScheduleBatch", commands, results, maxHits, dependsOn,
                (in BoxcastCommand c, RaycastHit[] found, bool closest) => PhysicsQueries.BatchCast(c.physicsScene, false, B3.Proxy.Box, c.center, c.halfExtents, c.orientation, 0f, c.direction, c.distance, c.queryParameters, found, closest));

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

        [Obsolete("This struct signature is no longer supported. Use struct with a QueryParameters instead")]
        public CapsulecastCommand(Vector3 p1, Vector3 p2, float radius, Vector3 direction, float distance = float.MaxValue, int layerMask = -5)
            : this(Physics.defaultPhysicsScene, p1, p2, radius, direction, new QueryParameters(layerMask), distance)
        {
        }

        [Obsolete("This struct signature is no longer supported. Use struct with a QueryParameters instead")]
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

        [Obsolete("Layer Mask is now a part of QueryParameters struct")]
        public int layerMask
        {
            get => queryParameters.layerMask;
            set => queryParameters.layerMask = value;
        }

        public static JobHandle ScheduleBatch(NativeArray<CapsulecastCommand> commands, NativeArray<RaycastHit> results, int minCommandsPerJob, int maxHits, JobHandle dependsOn = default) =>
            PhysicsBatchQueries.Casts("CapsulecastCommand.ScheduleBatch", commands, results, maxHits, dependsOn,
                (in CapsulecastCommand c, RaycastHit[] found, bool closest) => PhysicsQueries.BatchCast(c.physicsScene, false, B3.Proxy.Capsule, c.point1, c.point2, Quaternion.identity, c.radius, c.direction, c.distance, c.queryParameters, found, closest));

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
        internal delegate int OverlapQuery<TCommand>(in TCommand command, Collider[] found);

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

        internal static JobHandle Overlaps<TCommand>(string api, NativeArray<TCommand> commands, NativeArray<ColliderHit> results, int maxHits, JobHandle dependsOn, OverlapQuery<TCommand> query)
            where TCommand : struct
        {
            if (!CheckCapacity(commands.Length, results.Length, maxHits)) return default;
            if (commands.Length == 0) return dependsOn;
            CheckContainer(api, commands, nameof(commands));
            CheckContainer(api, results, nameof(results));
            dependsOn.Complete();

            using var batch = PhysicsQueries.Batch();
            PhysicsQueries.Reserve(maxHits);
            var found = new Collider[maxHits];
            for (int i = 0; i < commands.Length; i++)
            {
                var command = commands[i];
                Array.Clear(found, 0, found.Length);
                int count = query(in command, found);
                int start = i * maxHits;
                for (int k = 0; k < maxHits; k++)
                    results[start + k] = k < count ? new ColliderHit(found[k].GetInstanceID()) : default;
            }
            return default;
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

        internal delegate int CastQuery<TCommand>(in TCommand command, RaycastHit[] found, bool closest);

        internal static JobHandle Casts<TCommand>(string api, NativeArray<TCommand> commands, NativeArray<RaycastHit> results, int maxHits, JobHandle dependsOn,
            CastQuery<TCommand> query) where TCommand : struct
        {
            // The public size checks precede scheduling/container validation in the Editor.
            if (!CheckCapacity(commands.Length, results.Length, maxHits)) return default;
            if (commands.Length == 0) return dependsOn;
            CheckContainer(api, commands, nameof(commands));
            CheckContainer(api, results, nameof(results));
            dependsOn.Complete();
            using var batch = PhysicsQueries.Batch();
            PhysicsQueries.Reserve(maxHits);
            var found = new RaycastHit[maxHits];
            for (int i = 0; i < commands.Length; i++)
            {
                var command = commands[i];
                int count = query(in command, found, maxHits == 1);
                int start = i * maxHits;
                for (int k = 0; k < maxHits; k++)
                    results[start + k] = k < count ? found[k] : default;
            }
            return default;
        }
    }
}
