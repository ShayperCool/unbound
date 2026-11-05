using System;
using Unbound.Runtime;
using Unity.Collections;
using Unity.Jobs;

// Batch physics queries (6000.0 Scripting API: QueryParameters, ColliderHit, OverlapSphereCommand,
// OverlapBoxCommand, OverlapCapsuleCommand, ClosestPointCommand and their ScheduleBatch). Each
// command is answered by the engine's existing query for it (PhysicsScene.OverlapSphere,
// PhysicsScene.OverlapBox, PhysicsScene.OverlapCapsule, Physics.ClosestPoint); there is no
// separate batch query model.
//
// Documented: a command at index N with maxHits slots has its results at N * maxHits; when it hits
// fewer colliders than maxHits, the first unused result has collider instance ID 0 (no collider) and the results of the
// command end there. A ClosestPointCommand at index N has its result at index N.
//
// Unbound's choices (unmeasured):
// - ScheduleBatch completes dependsOn, then answers every command on the calling thread before it
//   returns, and returns an already completed handle (default). The results are therefore final
//   when ScheduleBatch returns; Unity only promises them once the returned handle is complete.
//   minCommandsPerJob does not change the results and is not used.
// - Every unused result slot of a command is cleared, not only the first one.
// - When more colliders overlap than maxHits, the ones kept and their order are those the
//   PhysicsScene overlap query fills a maxHits-long array with; Unity documents no order.
// - The checks throw before any result is written: an unassigned or deallocated container
//   (InvalidOperationException, as the job scheduler reports it), maxHits below 1
//   (ArgumentOutOfRangeException), a results array shorter than the commands need
//   (ArgumentException), a ClosestPointCommand naming no live Collider (ArgumentException) or a
//   scale other than its Collider's current lossy scale (NotSupportedException).
// - The overlap commands do not read QueryParameters.hitBackfaces or hitMultipleFaces: their pages
//   describe their QueryParameters as "layer mask or hit triggers" only.
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
                (in OverlapSphereCommand c, Collider[] found) => c.physicsScene.OverlapSphere(c.point, c.radius, found, c.queryParameters.layerMask, c.queryParameters.hitTriggers));
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
                (in OverlapBoxCommand c, Collider[] found) => c.physicsScene.OverlapBox(c.center, c.halfExtents, found, c.orientation, c.queryParameters.layerMask, c.queryParameters.hitTriggers));
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
                (in OverlapCapsuleCommand c, Collider[] found) => c.physicsScene.OverlapCapsule(c.point0, c.point1, c.radius, found, c.queryParameters.layerMask, c.queryParameters.hitTriggers));
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

        // Unbound's choice (unmeasured): a null reference is rejected here; a destroyed Collider still
        // has its instance ID, and ScheduleBatch reports that it is gone.
        static int InstanceIdOf(Collider collider) =>
            ReferenceEquals(collider, null) ? throw new ArgumentNullException(nameof(collider)) : collider.GetInstanceID();

        public Vector3 point { get; set; }

        public int colliderInstanceID { get; set; }

        public Vector3 position { get; set; }

        public Quaternion rotation { get; set; }

        public Vector3 scale { get; set; }

        public static JobHandle ScheduleBatch(NativeArray<ClosestPointCommand> commands, NativeArray<Vector3> results, int minCommandsPerJob, JobHandle dependsOn = default)
        {
            const string api = "ClosestPointCommand.ScheduleBatch";
            PhysicsBatchQueries.CheckContainer(api, commands, nameof(commands));
            PhysicsBatchQueries.CheckContainer(api, results, nameof(results));
            if (results.Length < commands.Length)
                throw new ArgumentException($"{api}: results has {results.Length} elements, but the {commands.Length} commands need {commands.Length}.", nameof(results));
            dependsOn.Complete();

            // Every command is checked before any result is written.
            var colliders = new Collider[commands.Length];
            for (int i = 0; i < commands.Length; i++)
            {
                var command = commands[i];
                var collider = Physics.GetColliderByInstanceID(command.colliderInstanceID);
                if (collider == null)
                    throw new ArgumentException($"{api}: command {i} names collider instance ID {command.colliderInstanceID}, which is not a live Collider.", nameof(commands));
                // Physics.ClosestPoint, which answers the command, uses the Collider's own scale.
                var lossy = collider.transform.lossyScale;
                if (command.scale != lossy)
                    throw new NotSupportedException($"{api}: command {i} gives scale {command.scale} for '{collider.name}', whose scale is {lossy}; Unbound answers closest points at the Collider's own scale only.");
                colliders[i] = collider;
            }
            for (int i = 0; i < commands.Length; i++)
            {
                var command = commands[i];
                results[i] = Physics.ClosestPoint(command.point, colliders[i], command.position, command.rotation);
            }
            return default;
        }
    }

    // ---------------------------------------------------------------------------- cast commands
    // 6000.0 Scripting API: RaycastCommand, SpherecastCommand, BoxcastCommand, CapsulecastCommand,
    // their ScheduleBatch, and QueryParameters. Each command is answered by the engine's existing
    // cast for its physicsScene (PhysicsScene.Raycast / SphereCast / BoxCast / CapsuleCast); there
    // is no separate batch query model.
    //
    // Documented: the results of the command at index N start at N * maxHits; the ScheduleBatch
    // overload without maxHits uses maxHits 1; for RaycastCommand, the first invalid result has a
    // null collider and ends the command's results; only RaycastCommand supports hitMultipleFaces.
    //
    // Managed answer, not the native batch: QueryParameters.hitBackfaces and hitMultipleFaces are
    // not supported by Unbound's batch commands. Each answered command hits back faces as its own
    // hitBackfaces (false) says, not as Physics.queriesHitBackfaces does (Unbound's reading,
    // unmeasured: the field has no "use the global setting" value). A batch with hitBackfaces set
    // on any command, or hitMultipleFaces set on a RaycastCommand, throws NotSupportedException
    // after dependsOn completes and before any result is written, instead of being answered without
    // them (Unbound's choice). The QueryParameters page (6000.0 Remarks: "Only RaycastCommand supports hitting
    // multiple Mesh faces") gives hitMultipleFaces no effect on SpherecastCommand, BoxcastCommand
    // and CapsulecastCommand, so they accept it and answer as without it. That it is ignored there
    // is inferred from the documented API scope; Unity's own result for it is not measured.
    //
    // Unbound's choices (unmeasured), as for the overlap commands: ScheduleBatch answers on the
    // calling thread and returns an already completed handle; minCommandsPerJob is not used; every
    // unused slot is cleared to an empty hit; the container, maxHits and results-length checks
    // throw before anything else. Hypotheses (unmeasured, Unity documents no order): with maxHits 1
    // a command's hit is the closest one, as the single PhysicsScene cast with an out hit answers
    // (shapes the cast starts in are not hit); with a larger maxHits the hits kept and their order
    // are those the PhysicsScene cast writes into a maxHits-long array (unsorted, shapes the cast
    // starts in reported at distance 0).

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
                (in RaycastCommand c) => c.queryParameters,
                (in RaycastCommand c, out RaycastHit hit) => c.physicsScene.Raycast(c.from, c.direction, out hit, c.distance, c.queryParameters.layerMask, c.queryParameters.hitTriggers),
                (in RaycastCommand c, RaycastHit[] found) => c.physicsScene.Raycast(c.from, c.direction, found, c.distance, c.queryParameters.layerMask, c.queryParameters.hitTriggers));

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
                (in SpherecastCommand c) => c.queryParameters,
                (in SpherecastCommand c, out RaycastHit hit) => c.physicsScene.SphereCast(c.origin, c.radius, c.direction, out hit, c.distance, c.queryParameters.layerMask, c.queryParameters.hitTriggers),
                (in SpherecastCommand c, RaycastHit[] found) => c.physicsScene.SphereCast(c.origin, c.radius, c.direction, found, c.distance, c.queryParameters.layerMask, c.queryParameters.hitTriggers));

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
                (in BoxcastCommand c) => c.queryParameters,
                (in BoxcastCommand c, out RaycastHit hit) => c.physicsScene.BoxCast(c.center, c.halfExtents, c.direction, out hit, c.orientation, c.distance, c.queryParameters.layerMask, c.queryParameters.hitTriggers),
                (in BoxcastCommand c, RaycastHit[] found) => c.physicsScene.BoxCast(c.center, c.halfExtents, c.direction, found, c.orientation, c.distance, c.queryParameters.layerMask, c.queryParameters.hitTriggers));

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
                (in CapsulecastCommand c) => c.queryParameters,
                (in CapsulecastCommand c, out RaycastHit hit) => c.physicsScene.CapsuleCast(c.point1, c.point2, c.radius, c.direction, out hit, c.distance, c.queryParameters.layerMask, c.queryParameters.hitTriggers),
                (in CapsulecastCommand c, RaycastHit[] found) => c.physicsScene.CapsuleCast(c.point1, c.point2, c.radius, c.direction, found, c.distance, c.queryParameters.layerMask, c.queryParameters.hitTriggers));

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
            if (!array.IsCreated)
                throw new InvalidOperationException($"{api}: the {type} {name} has not been assigned or constructed. All containers must be valid when scheduling a job.");
        }

        internal static JobHandle Overlaps<TCommand>(string api, NativeArray<TCommand> commands, NativeArray<ColliderHit> results, int maxHits, JobHandle dependsOn, OverlapQuery<TCommand> query)
            where TCommand : struct
        {
            CheckContainer(api, commands, nameof(commands));
            CheckContainer(api, results, nameof(results));
            if (maxHits < 1)
                throw new ArgumentOutOfRangeException(nameof(maxHits), maxHits, $"{api}: maxHits must be at least 1.");
            long needed = (long)commands.Length * maxHits;
            if (results.Length < needed)
                throw new ArgumentException($"{api}: results has {results.Length} elements, but {commands.Length} commands with maxHits {maxHits} need {needed}.", nameof(results));
            dependsOn.Complete();

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

        internal delegate QueryParameters CastParameters<TCommand>(in TCommand command);
        internal delegate bool ClosestCast<TCommand>(in TCommand command, out RaycastHit hit);
        internal delegate int AllCasts<TCommand>(in TCommand command, RaycastHit[] found);

        /// <summary>Answers cast commands into maxHits result slots each: with maxHits 1 the
        /// command's closest hit (its PhysicsScene cast with an out hit), otherwise the hits its
        /// PhysicsScene cast writes into a maxHits-long array. A command asking for back faces, or a
        /// RaycastCommand asking for multiple faces, is refused before any result is written.</summary>
        internal static JobHandle Casts<TCommand>(string api, NativeArray<TCommand> commands, NativeArray<RaycastHit> results, int maxHits, JobHandle dependsOn,
            CastParameters<TCommand> parameters, ClosestCast<TCommand> closest, AllCasts<TCommand> all)
            where TCommand : struct
        {
            CheckContainer(api, commands, nameof(commands));
            CheckContainer(api, results, nameof(results));
            if (maxHits < 1)
                throw new ArgumentOutOfRangeException(nameof(maxHits), maxHits, $"{api}: maxHits must be at least 1.");
            long needed = (long)commands.Length * maxHits;
            if (results.Length < needed)
                throw new ArgumentException($"{api}: results has {results.Length} elements, but {commands.Length} commands with maxHits {maxHits} need {needed}.", nameof(results));
            dependsOn.Complete();

            // Every command is checked before any result is written.
            for (int i = 0; i < commands.Length; i++)
            {
                var query = parameters(commands[i]);
                if (query.hitBackfaces)
                    throw new NotSupportedException($"{api}: command {i} sets QueryParameters.hitBackfaces, which Unbound's batch commands do not support yet, so the batch is refused instead of answered without back faces.");
                // Only RaycastCommand supports hitting multiple Mesh faces (QueryParameters, 6000.0);
                // the shape casts accept the flag and answer as without it.
                if (query.hitMultipleFaces && typeof(TCommand) == typeof(RaycastCommand))
                    throw new NotSupportedException($"{api}: command {i} sets QueryParameters.hitMultipleFaces, which Unbound does not support: its casts report one hit per collider, so the batch is refused instead of answered without multiple faces.");
            }

            var found = maxHits > 1 ? new RaycastHit[maxHits] : null;
            for (int i = 0; i < commands.Length; i++)
            {
                var command = commands[i];
                int start = i * maxHits;
                // A command hits back faces as its QueryParameters.hitBackfaces says, not as
                // Physics.queriesHitBackfaces does.
                using var backfaces = PhysicsQueries.Backfaces(parameters(command).hitBackfaces);
                if (found == null)
                {
                    results[start] = closest(in command, out var hit) ? hit : default;
                    continue;
                }
                Array.Clear(found, 0, found.Length);
                int count = all(in command, found);
                for (int k = 0; k < maxHits; k++)
                    results[start + k] = k < count ? found[k] : default;
            }
            return default;
        }
    }
}
