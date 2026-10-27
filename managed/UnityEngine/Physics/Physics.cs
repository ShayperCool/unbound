using System;
using System.Collections.Generic;
using Unbound.Box3D;
using Unbound.Runtime;
using Q = Unbound.Runtime.PhysicsQueries;
using W = Unbound.Runtime.PhysicsWorld;

namespace UnityEngine
{
    /// <summary>
    /// The global 3D physics settings (ProjectSettings/DynamicsManager.asset) and queries. The
    /// simulation itself is <see cref="PhysicsWorld"/> over Box3D.
    /// </summary>
    public partial class Physics
    {
        public const int IgnoreRaycastLayer = 4;
        public const int DefaultRaycastLayers = -5;
        public const int AllLayers = -1;

        // Unity 6000.0's defaults; the project's settings asset overrides them.
        internal static Vector3 s_Gravity = new Vector3(0f, -9.81f, 0f);
        internal static float s_BounceThreshold = 2f;
        internal static float s_SleepThreshold = 0.005f;
        internal static float s_DefaultContactOffset = 0.01f;
        internal static int s_DefaultSolverIterations = 6;
        internal static int s_DefaultSolverVelocityIterations = 1;
        internal static float s_DefaultMaxAngularSpeed = 50f;
        internal static float s_DefaultMaxDepenetrationVelocity = 10f;
        internal static bool s_QueriesHitBackfaces;
        internal static bool s_QueriesHitTriggers = true;
        internal static bool s_AutoSyncTransforms;
        internal static bool s_ReuseCollisionCallbacks = true;
        internal static bool s_InvokeCollisionCallbacks = true;
        internal static bool s_ImprovedPatchFriction;
        internal static SimulationMode s_SimulationMode = SimulationMode.FixedUpdate;
        static float s_InterCollisionDistance = 0.1f;
        static float s_InterCollisionStiffness = 0.2f;
        static bool s_InterCollisionSettingsToggle;
        static Vector3 s_ClothGravity = new Vector3(0f, -9.81f, 0f);
        /// <summary>Bit j of entry i: layers i and j collide.</summary>
        internal static readonly int[] s_LayerCollisionMatrix = NewMatrix();
        static PhysicsMaterial s_DefaultMaterial;
        static ObjectRef s_DefaultMaterialRef;
        static bool s_DefaultMaterialResolved = true;
        static bool s_SettingsLoaded;

        static int[] NewMatrix()
        {
            var m = new int[32];
            for (int i = 0; i < 32; i++) m[i] = -1;
            return m;
        }

        // ------------------------------------------------------------------ settings

        /// <summary>Reads ProjectSettings/DynamicsManager.asset once (when the native side is there).</summary>
        internal static void EnsureSettings()
        {
            if (s_SettingsLoaded) return;
            s_SettingsLoaded = true;
            try
            {
                if (Native.IsReady && !string.IsNullOrEmpty(ProjectInfo.ProjectPath))
                {
                    var path = System.IO.Path.Combine(ProjectInfo.ProjectPath, "ProjectSettings", "DynamicsManager.asset");
                    if (System.IO.File.Exists(path))
                        foreach (var doc in Native.LoadDocuments(path))
                            if (doc.ClassId == 55) ReadSettings(doc.Body);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Unbound: reading DynamicsManager: {e.Message}");
            }
        }

        /// <summary>The fields of a PhysicsManager document (older names where Unity renamed them). A
        /// document written before autoSyncTransforms existed always synced, and Unity keeps that.</summary>
        internal static void ReadSettings(YNode body)
        {
            if (body.Get("m_Gravity") != null) s_Gravity = body.Vector3("m_Gravity");
            s_BounceThreshold = body.Float("m_BounceThreshold", s_BounceThreshold);
            s_SleepThreshold = body.Float("m_SleepThreshold", s_SleepThreshold);
            s_DefaultContactOffset = body.Float("m_DefaultContactOffset", s_DefaultContactOffset);
            s_DefaultSolverIterations = body.Int("m_DefaultSolverIterations", s_DefaultSolverIterations);
            s_DefaultSolverVelocityIterations = body.Int("m_DefaultSolverVelocityIterations", s_DefaultSolverVelocityIterations);
            s_DefaultMaxAngularSpeed = body.Float("m_DefaultMaxAngularSpeed", s_DefaultMaxAngularSpeed);
            s_DefaultMaxDepenetrationVelocity = body.Float("m_DefaultMaxDepenetrationVelocity", s_DefaultMaxDepenetrationVelocity);
            s_QueriesHitBackfaces = body.Bool("m_QueriesHitBackfaces", s_QueriesHitBackfaces);
            s_QueriesHitTriggers = body.Bool("m_QueriesHitTriggers", body.Bool("m_RaycastsHitTriggers", s_QueriesHitTriggers));
            s_AutoSyncTransforms = body.Bool("m_AutoSyncTransforms", true);
            s_ReuseCollisionCallbacks = body.Bool("m_ReuseCollisionCallbacks", false);
            s_InvokeCollisionCallbacks = body.Bool("m_InvokeCollisionCallbacks", true);
            s_ImprovedPatchFriction = body.Bool("m_ImprovedPatchFriction", s_ImprovedPatchFriction);
            if (body.Get("m_SimulationMode") != null) s_SimulationMode = (SimulationMode)body.Int("m_SimulationMode");
            else if (body.Get("m_AutoSimulation") != null) s_SimulationMode = body.Bool("m_AutoSimulation", true) ? SimulationMode.FixedUpdate : SimulationMode.Script;
            var matrix = body.Str("m_LayerCollisionMatrix");
            if (matrix.Length >= 8)
            {
                for (int i = 0; i < 32 && (i + 1) * 8 <= matrix.Length; i++)
                {
                    // Little-endian 32-bit words, as Unity writes the array's bytes.
                    uint word = 0;
                    for (int b = 0; b < 4; b++)
                        word |= (uint)Convert.ToByte(matrix.Substring(i * 8 + b * 2, 2), 16) << (8 * b);
                    s_LayerCollisionMatrix[i] = unchecked((int)word);
                }
            }
            s_DefaultMaterialRef = body.Ref("m_DefaultMaterial");
            s_DefaultMaterialResolved = s_DefaultMaterialRef.IsNull;
            s_DefaultMaterial = null;
        }

        /// <summary>The material a collider's shape uses: its own or the project's default.</summary>
        internal static PhysicsMaterial MaterialOf(Collider c) => c != null && c.m_Material != null ? c.m_Material : DefaultMaterial;

        static PhysicsMaterial DefaultMaterial
        {
            get
            {
                if (!s_DefaultMaterialResolved)
                {
                    s_DefaultMaterialResolved = true;
                    try
                    {
                        if (Native.IsReady)
                            s_DefaultMaterial = AssetDatabaseRuntime.Load(s_DefaultMaterialRef.Guid, s_DefaultMaterialRef.FileId, typeof(PhysicsMaterial)) as PhysicsMaterial;
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Unbound: the default physics material: {e.Message}");
                    }
                }
                return s_DefaultMaterial;
            }
        }

        /// <summary>A material's shape values: friction (Unity's dynamic friction), bounciness, and
        /// the combine modes for the shim (in Unity's priority order: Average, Minimum, Multiply, Maximum).</summary>
        internal static void MaterialValues(PhysicsMaterial material, out float friction, out float staticFriction, out float bounciness, out ulong modes)
        {
            friction = material != null ? Mathf.Max(0f, material.dynamicFriction) : 0.6f;
            staticFriction = material != null ? Mathf.Max(0f, material.staticFriction) : 0.6f;
            bounciness = material != null ? Mathf.Clamp01(material.bounciness) : 0f;
            var frictionCombine = material != null ? material.frictionCombine : PhysicsMaterialCombine.Average;
            var bounceCombine = material != null ? material.bounceCombine : PhysicsMaterialCombine.Average;
            modes = Priority(frictionCombine) | (Priority(bounceCombine) << 2);

            static ulong Priority(PhysicsMaterialCombine mode) => mode switch
            {
                PhysicsMaterialCombine.Minimum => 1,
                PhysicsMaterialCombine.Multiply => 2,
                PhysicsMaterialCombine.Maximum => 3,
                _ => 0,
            };
        }

        // ------------------------------------------------------------------ global settings

        public static Vector3 gravity
        {
            get { EnsureSettings(); return s_Gravity; }
            set { EnsureSettings(); s_Gravity = value; }
        }

        public static SimulationMode simulationMode
        {
            get { EnsureSettings(); return s_SimulationMode; }
            set { EnsureSettings(); s_SimulationMode = value; }
        }

        [Obsolete("Physics.autoSimulation has been replaced by Physics.simulationMode", false)]
        public static bool autoSimulation
        {
            get => simulationMode != SimulationMode.Script;
            set => simulationMode = value ? SimulationMode.FixedUpdate : SimulationMode.Script;
        }

        public static bool autoSyncTransforms
        {
            get { EnsureSettings(); return s_AutoSyncTransforms; }
            set { EnsureSettings(); s_AutoSyncTransforms = value; }
        }

        public static bool queriesHitTriggers
        {
            get { EnsureSettings(); return s_QueriesHitTriggers; }
            set { EnsureSettings(); s_QueriesHitTriggers = value; }
        }

        public static bool queriesHitBackfaces
        {
            get { EnsureSettings(); return s_QueriesHitBackfaces; }
            set { EnsureSettings(); s_QueriesHitBackfaces = value; }
        }

        public static float bounceThreshold
        {
            get { EnsureSettings(); return s_BounceThreshold; }
            set
            {
                EnsureSettings();
                s_BounceThreshold = value;
                B3.ua3_world_set_restitution_threshold(W.World, Mathf.Max(0f, value));
            }
        }

        public static float sleepThreshold
        {
            get { EnsureSettings(); return s_SleepThreshold; }
            set { EnsureSettings(); s_SleepThreshold = value; }
        }

        public static float defaultContactOffset
        {
            get { EnsureSettings(); return s_DefaultContactOffset; }
            set { EnsureSettings(); s_DefaultContactOffset = value; }
        }

        public static int defaultSolverIterations
        {
            get { EnsureSettings(); return s_DefaultSolverIterations; }
            set { EnsureSettings(); s_DefaultSolverIterations = value; }
        }

        public static int defaultSolverVelocityIterations
        {
            get { EnsureSettings(); return s_DefaultSolverVelocityIterations; }
            set { EnsureSettings(); s_DefaultSolverVelocityIterations = value; }
        }

        public static float defaultMaxAngularSpeed
        {
            get { EnsureSettings(); return s_DefaultMaxAngularSpeed; }
            set { EnsureSettings(); s_DefaultMaxAngularSpeed = value; }
        }

        public static float defaultMaxDepenetrationVelocity
        {
            get { EnsureSettings(); return s_DefaultMaxDepenetrationVelocity; }
            set { EnsureSettings(); s_DefaultMaxDepenetrationVelocity = value; }
        }

        public static bool reuseCollisionCallbacks
        {
            get { EnsureSettings(); return s_ReuseCollisionCallbacks; }
            set { EnsureSettings(); s_ReuseCollisionCallbacks = value; }
        }

        public static bool invokeCollisionCallbacks
        {
            get { EnsureSettings(); return s_InvokeCollisionCallbacks; }
            set { EnsureSettings(); s_InvokeCollisionCallbacks = value; }
        }

        public static bool improvedPatchFriction
        {
            get { EnsureSettings(); return s_ImprovedPatchFriction; }
            set { EnsureSettings(); s_ImprovedPatchFriction = value; }
        }

        public static float interCollisionDistance { get => s_InterCollisionDistance; set => s_InterCollisionDistance = value; }

        public static float interCollisionStiffness { get => s_InterCollisionStiffness; set => s_InterCollisionStiffness = value; }

        public static bool interCollisionSettingsToggle { get => s_InterCollisionSettingsToggle; set => s_InterCollisionSettingsToggle = value; }

        public static Vector3 clothGravity { get => s_ClothGravity; set => s_ClothGravity = value; }

        // ------------------------------------------------------------------ layers and pairs

        public static void IgnoreLayerCollision(int layer1, int layer2, [Internal.DefaultValue("true")] bool ignore)
        {
            if (layer1 < 0 || layer1 > 31 || layer2 < 0 || layer2 > 31)
                throw new ArgumentException("layer numbers must be between 0 and 31");
            EnsureSettings();
            if (ignore)
            {
                s_LayerCollisionMatrix[layer1] &= ~(1 << layer2);
                s_LayerCollisionMatrix[layer2] &= ~(1 << layer1);
            }
            else
            {
                s_LayerCollisionMatrix[layer1] |= 1 << layer2;
                s_LayerCollisionMatrix[layer2] |= 1 << layer1;
            }
            W.RefilterAll();
        }

        public static void IgnoreLayerCollision(int layer1, int layer2) => IgnoreLayerCollision(layer1, layer2, true);

        public static bool GetIgnoreLayerCollision(int layer1, int layer2)
        {
            EnsureSettings();
            return (s_LayerCollisionMatrix[layer1 & 31] & (1 << (layer2 & 31))) == 0;
        }

        /// <summary>The layers a layer collides with.</summary>
        internal static int GetLayerCollisionMask(int layer)
        {
            EnsureSettings();
            return s_LayerCollisionMatrix[layer & 31];
        }

        public static void IgnoreCollision(Collider collider1, Collider collider2, [Internal.DefaultValue("true")] bool ignore)
        {
            if (collider1 == null || collider2 == null) throw new NullReferenceException("Ignore collision failed. Both colliders need to be valid.");
            W.SetIgnored(collider1, collider2, ignore);
        }

        public static void IgnoreCollision(Collider collider1, Collider collider2) => IgnoreCollision(collider1, collider2, true);

        public static bool GetIgnoreCollision(Collider collider1, Collider collider2) =>
            collider1 != null && collider2 != null && W.IsIgnored(collider1, collider2);

        // ------------------------------------------------------------------ simulation

        public static void Simulate(float step)
        {
            if (simulationMode != SimulationMode.Script)
            {
                Debug.LogWarning("Physics.Simulate(...) was called but simulation mode is not set to Script. You should set simulation mode to Script first before calling this function therefore the simulation was not run.");
                return;
            }
            W.EnsureRegistered();
            W.Simulate(step);
        }

        public static void SyncTransforms()
        {
            if (W.Colliders.Count == 0 && W.Rigidbodies.Count == 0) return;
            W.SyncTransforms();
        }

        public static Collider GetColliderByInstanceID(int instanceID) => ObjectRegistry.FindById(instanceID) as Collider;

        public static Component GetBodyByInstanceID(int instanceID) => ObjectRegistry.FindById(instanceID) as Rigidbody;

        public static Vector3 ClosestPoint(Vector3 point, Collider collider, Vector3 position, Quaternion rotation)
        {
            if (collider == null) throw new ArgumentNullException(nameof(collider));
            // In the collider's frame at the given pose, then on the collider where it is.
            var t = collider.transform;
            var local = Quaternion.Inverse(rotation) * (point - position);
            var closest = collider.ClosestPoint(t.position + t.rotation * local);
            return position + rotation * (Quaternion.Inverse(t.rotation) * (closest - t.position));
        }

        // ------------------------------------------------------------------ rays

        public static bool Raycast(Vector3 origin, Vector3 direction, [Internal.DefaultValue("Mathf.Infinity")] float maxDistance, [Internal.DefaultValue("DefaultRaycastLayers")] int layerMask, [Internal.DefaultValue("QueryTriggerInteraction.UseGlobal")] QueryTriggerInteraction queryTriggerInteraction) =>
            Q.Raycast(origin, direction, maxDistance, layerMask, queryTriggerInteraction, closest: true).Count > 0;

        public static bool Raycast(Vector3 origin, Vector3 direction, float maxDistance, int layerMask) => Raycast(origin, direction, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);

        public static bool Raycast(Vector3 origin, Vector3 direction, float maxDistance) => Raycast(origin, direction, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static bool Raycast(Vector3 origin, Vector3 direction) => Raycast(origin, direction, Mathf.Infinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hitInfo, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) =>
            Q.First(Q.Raycast(origin, direction, maxDistance, layerMask, queryTriggerInteraction, closest: true), out hitInfo);

        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hitInfo, float maxDistance, int layerMask) => Raycast(origin, direction, out hitInfo, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);

        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hitInfo, float maxDistance) => Raycast(origin, direction, out hitInfo, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static bool Raycast(Vector3 origin, Vector3 direction, out RaycastHit hitInfo) => Raycast(origin, direction, out hitInfo, Mathf.Infinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static bool Raycast(Ray ray, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => Raycast(ray.origin, ray.direction, maxDistance, layerMask, queryTriggerInteraction);

        public static bool Raycast(Ray ray, float maxDistance, int layerMask) => Raycast(ray.origin, ray.direction, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);

        public static bool Raycast(Ray ray, float maxDistance) => Raycast(ray.origin, ray.direction, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static bool Raycast(Ray ray) => Raycast(ray.origin, ray.direction, Mathf.Infinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static bool Raycast(Ray ray, out RaycastHit hitInfo, float maxDistance, int layerMask, QueryTriggerInteraction queryTriggerInteraction) => Raycast(ray.origin, ray.direction, out hitInfo, maxDistance, layerMask, queryTriggerInteraction);

        public static bool Raycast(Ray ray, out RaycastHit hitInfo, float maxDistance, int layerMask) => Raycast(ray.origin, ray.direction, out hitInfo, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);

        public static bool Raycast(Ray ray, out RaycastHit hitInfo, float maxDistance) => Raycast(ray.origin, ray.direction, out hitInfo, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static bool Raycast(Ray ray, out RaycastHit hitInfo) => Raycast(ray.origin, ray.direction, out hitInfo, Mathf.Infinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static bool Linecast(Vector3 start, Vector3 end, int layerMask, QueryTriggerInteraction queryTriggerInteraction)
        {
            var d = end - start;
            return Raycast(start, d, d.magnitude, layerMask, queryTriggerInteraction);
        }

        public static bool Linecast(Vector3 start, Vector3 end, int layerMask) => Linecast(start, end, layerMask, QueryTriggerInteraction.UseGlobal);

        public static bool Linecast(Vector3 start, Vector3 end) => Linecast(start, end, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static bool Linecast(Vector3 start, Vector3 end, out RaycastHit hitInfo, int layerMask, QueryTriggerInteraction queryTriggerInteraction)
        {
            var d = end - start;
            return Raycast(start, d, out hitInfo, d.magnitude, layerMask, queryTriggerInteraction);
        }

        public static bool Linecast(Vector3 start, Vector3 end, out RaycastHit hitInfo, int layerMask) => Linecast(start, end, out hitInfo, layerMask, QueryTriggerInteraction.UseGlobal);

        public static bool Linecast(Vector3 start, Vector3 end, out RaycastHit hitInfo) => Linecast(start, end, out hitInfo, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static RaycastHit[] RaycastAll(Vector3 origin, Vector3 direction, [Internal.DefaultValue("Mathf.Infinity")] float maxDistance, [Internal.DefaultValue("DefaultRaycastLayers")] int layerMask, [Internal.DefaultValue("QueryTriggerInteraction.UseGlobal")] QueryTriggerInteraction queryTriggerInteraction) =>
            Q.Raycast(origin, direction, maxDistance, layerMask, queryTriggerInteraction, closest: false).ToArray();

        public static RaycastHit[] RaycastAll(Vector3 origin, Vector3 direction, float maxDistance, int layerMask) => RaycastAll(origin, direction, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);

        public static RaycastHit[] RaycastAll(Vector3 origin, Vector3 direction, float maxDistance) => RaycastAll(origin, direction, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static RaycastHit[] RaycastAll(Vector3 origin, Vector3 direction) => RaycastAll(origin, direction, Mathf.Infinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static RaycastHit[] RaycastAll(Ray ray, [Internal.DefaultValue("Mathf.Infinity")] float maxDistance, [Internal.DefaultValue("DefaultRaycastLayers")] int layerMask, [Internal.DefaultValue("QueryTriggerInteraction.UseGlobal")] QueryTriggerInteraction queryTriggerInteraction) =>
            RaycastAll(ray.origin, ray.direction, maxDistance, layerMask, queryTriggerInteraction);

        public static RaycastHit[] RaycastAll(Ray ray, float maxDistance, int layerMask) => RaycastAll(ray.origin, ray.direction, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);

        public static RaycastHit[] RaycastAll(Ray ray, float maxDistance) => RaycastAll(ray.origin, ray.direction, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static RaycastHit[] RaycastAll(Ray ray) => RaycastAll(ray.origin, ray.direction, Mathf.Infinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static int RaycastNonAlloc(Ray ray, RaycastHit[] results, [Internal.DefaultValue("Mathf.Infinity")] float maxDistance, [Internal.DefaultValue("DefaultRaycastLayers")] int layerMask, [Internal.DefaultValue("QueryTriggerInteraction.UseGlobal")] QueryTriggerInteraction queryTriggerInteraction) =>
            RaycastNonAlloc(ray.origin, ray.direction, results, maxDistance, layerMask, queryTriggerInteraction);

        public static int RaycastNonAlloc(Ray ray, RaycastHit[] results, float maxDistance, int layerMask) => RaycastNonAlloc(ray.origin, ray.direction, results, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);

        public static int RaycastNonAlloc(Ray ray, RaycastHit[] results, float maxDistance) => RaycastNonAlloc(ray.origin, ray.direction, results, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static int RaycastNonAlloc(Ray ray, RaycastHit[] results) => RaycastNonAlloc(ray.origin, ray.direction, results, Mathf.Infinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static int RaycastNonAlloc(Vector3 origin, Vector3 direction, RaycastHit[] results, [Internal.DefaultValue("Mathf.Infinity")] float maxDistance, [Internal.DefaultValue("DefaultRaycastLayers")] int layerMask, [Internal.DefaultValue("QueryTriggerInteraction.UseGlobal")] QueryTriggerInteraction queryTriggerInteraction) =>
            Q.Fill(Q.Raycast(origin, direction, maxDistance, layerMask, queryTriggerInteraction, closest: false), results);

        public static int RaycastNonAlloc(Vector3 origin, Vector3 direction, RaycastHit[] results, float maxDistance, int layerMask) => RaycastNonAlloc(origin, direction, results, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);

        public static int RaycastNonAlloc(Vector3 origin, Vector3 direction, RaycastHit[] results, float maxDistance) => RaycastNonAlloc(origin, direction, results, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static int RaycastNonAlloc(Vector3 origin, Vector3 direction, RaycastHit[] results) => RaycastNonAlloc(origin, direction, results, Mathf.Infinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        // ------------------------------------------------------------------ spheres

        static List<RaycastHit> SphereCastHits(Vector3 origin, float radius, Vector3 direction, float maxDistance, int layerMask, QueryTriggerInteraction q, bool closest) =>
            Q.Cast(B3.Proxy.Sphere, origin, origin, Quaternion.identity, radius, direction, maxDistance, layerMask, q, closest);

        public static bool SphereCast(Vector3 origin, float radius, Vector3 direction, out RaycastHit hitInfo, [Internal.DefaultValue("Mathf.Infinity")] float maxDistance, [Internal.DefaultValue("DefaultRaycastLayers")] int layerMask, [Internal.DefaultValue("QueryTriggerInteraction.UseGlobal")] QueryTriggerInteraction queryTriggerInteraction) =>
            Q.First(SphereCastHits(origin, radius, direction, maxDistance, layerMask, queryTriggerInteraction, true), out hitInfo);

        public static bool SphereCast(Vector3 origin, float radius, Vector3 direction, out RaycastHit hitInfo, float maxDistance, int layerMask) => SphereCast(origin, radius, direction, out hitInfo, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);

        public static bool SphereCast(Vector3 origin, float radius, Vector3 direction, out RaycastHit hitInfo, float maxDistance) => SphereCast(origin, radius, direction, out hitInfo, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static bool SphereCast(Vector3 origin, float radius, Vector3 direction, out RaycastHit hitInfo) => SphereCast(origin, radius, direction, out hitInfo, Mathf.Infinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static bool SphereCast(Ray ray, float radius, [Internal.DefaultValue("Mathf.Infinity")] float maxDistance, [Internal.DefaultValue("DefaultRaycastLayers")] int layerMask, [Internal.DefaultValue("QueryTriggerInteraction.UseGlobal")] QueryTriggerInteraction queryTriggerInteraction) =>
            SphereCastHits(ray.origin, radius, ray.direction, maxDistance, layerMask, queryTriggerInteraction, true).Count > 0;

        public static bool SphereCast(Ray ray, float radius, float maxDistance, int layerMask) => SphereCast(ray, radius, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);

        public static bool SphereCast(Ray ray, float radius, float maxDistance) => SphereCast(ray, radius, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static bool SphereCast(Ray ray, float radius) => SphereCast(ray, radius, Mathf.Infinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static bool SphereCast(Ray ray, float radius, out RaycastHit hitInfo, [Internal.DefaultValue("Mathf.Infinity")] float maxDistance, [Internal.DefaultValue("DefaultRaycastLayers")] int layerMask, [Internal.DefaultValue("QueryTriggerInteraction.UseGlobal")] QueryTriggerInteraction queryTriggerInteraction) =>
            SphereCast(ray.origin, radius, ray.direction, out hitInfo, maxDistance, layerMask, queryTriggerInteraction);

        public static bool SphereCast(Ray ray, float radius, out RaycastHit hitInfo, float maxDistance, int layerMask) => SphereCast(ray.origin, radius, ray.direction, out hitInfo, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);

        public static bool SphereCast(Ray ray, float radius, out RaycastHit hitInfo, float maxDistance) => SphereCast(ray.origin, radius, ray.direction, out hitInfo, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static bool SphereCast(Ray ray, float radius, out RaycastHit hitInfo) => SphereCast(ray.origin, radius, ray.direction, out hitInfo, Mathf.Infinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static RaycastHit[] SphereCastAll(Vector3 origin, float radius, Vector3 direction, [Internal.DefaultValue("Mathf.Infinity")] float maxDistance, [Internal.DefaultValue("DefaultRaycastLayers")] int layerMask, [Internal.DefaultValue("QueryTriggerInteraction.UseGlobal")] QueryTriggerInteraction queryTriggerInteraction) =>
            SphereCastHits(origin, radius, direction, maxDistance, layerMask, queryTriggerInteraction, false).ToArray();

        public static RaycastHit[] SphereCastAll(Vector3 origin, float radius, Vector3 direction, float maxDistance, int layerMask) => SphereCastAll(origin, radius, direction, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);

        public static RaycastHit[] SphereCastAll(Vector3 origin, float radius, Vector3 direction, float maxDistance) => SphereCastAll(origin, radius, direction, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static RaycastHit[] SphereCastAll(Vector3 origin, float radius, Vector3 direction) => SphereCastAll(origin, radius, direction, Mathf.Infinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static RaycastHit[] SphereCastAll(Ray ray, float radius, [Internal.DefaultValue("Mathf.Infinity")] float maxDistance, [Internal.DefaultValue("DefaultRaycastLayers")] int layerMask, [Internal.DefaultValue("QueryTriggerInteraction.UseGlobal")] QueryTriggerInteraction queryTriggerInteraction) =>
            SphereCastAll(ray.origin, radius, ray.direction, maxDistance, layerMask, queryTriggerInteraction);

        public static RaycastHit[] SphereCastAll(Ray ray, float radius, float maxDistance, int layerMask) => SphereCastAll(ray.origin, radius, ray.direction, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);

        public static RaycastHit[] SphereCastAll(Ray ray, float radius, float maxDistance) => SphereCastAll(ray.origin, radius, ray.direction, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static RaycastHit[] SphereCastAll(Ray ray, float radius) => SphereCastAll(ray.origin, radius, ray.direction, Mathf.Infinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static int SphereCastNonAlloc(Vector3 origin, float radius, Vector3 direction, RaycastHit[] results, [Internal.DefaultValue("Mathf.Infinity")] float maxDistance, [Internal.DefaultValue("DefaultRaycastLayers")] int layerMask, [Internal.DefaultValue("QueryTriggerInteraction.UseGlobal")] QueryTriggerInteraction queryTriggerInteraction) =>
            Q.Fill(SphereCastHits(origin, radius, direction, maxDistance, layerMask, queryTriggerInteraction, false), results);

        public static int SphereCastNonAlloc(Vector3 origin, float radius, Vector3 direction, RaycastHit[] results, float maxDistance, int layerMask) => SphereCastNonAlloc(origin, radius, direction, results, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);

        public static int SphereCastNonAlloc(Vector3 origin, float radius, Vector3 direction, RaycastHit[] results, float maxDistance) => SphereCastNonAlloc(origin, radius, direction, results, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static int SphereCastNonAlloc(Vector3 origin, float radius, Vector3 direction, RaycastHit[] results) => SphereCastNonAlloc(origin, radius, direction, results, Mathf.Infinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static int SphereCastNonAlloc(Ray ray, float radius, RaycastHit[] results, [Internal.DefaultValue("Mathf.Infinity")] float maxDistance, [Internal.DefaultValue("DefaultRaycastLayers")] int layerMask, [Internal.DefaultValue("QueryTriggerInteraction.UseGlobal")] QueryTriggerInteraction queryTriggerInteraction) =>
            SphereCastNonAlloc(ray.origin, radius, ray.direction, results, maxDistance, layerMask, queryTriggerInteraction);

        public static int SphereCastNonAlloc(Ray ray, float radius, RaycastHit[] results, float maxDistance, int layerMask) => SphereCastNonAlloc(ray.origin, radius, ray.direction, results, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);

        public static int SphereCastNonAlloc(Ray ray, float radius, RaycastHit[] results, float maxDistance) => SphereCastNonAlloc(ray.origin, radius, ray.direction, results, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static int SphereCastNonAlloc(Ray ray, float radius, RaycastHit[] results) => SphereCastNonAlloc(ray.origin, radius, ray.direction, results, Mathf.Infinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static Collider[] OverlapSphere(Vector3 position, float radius, [Internal.DefaultValue("AllLayers")] int layerMask, [Internal.DefaultValue("QueryTriggerInteraction.UseGlobal")] QueryTriggerInteraction queryTriggerInteraction) =>
            Q.Overlap(B3.Proxy.Sphere, position, position, Quaternion.identity, radius, layerMask, queryTriggerInteraction).ToArray();

        public static Collider[] OverlapSphere(Vector3 position, float radius, int layerMask) => OverlapSphere(position, radius, layerMask, QueryTriggerInteraction.UseGlobal);

        public static Collider[] OverlapSphere(Vector3 position, float radius) => OverlapSphere(position, radius, AllLayers, QueryTriggerInteraction.UseGlobal);

        public static int OverlapSphereNonAlloc(Vector3 position, float radius, Collider[] results, [Internal.DefaultValue("AllLayers")] int layerMask, [Internal.DefaultValue("QueryTriggerInteraction.UseGlobal")] QueryTriggerInteraction queryTriggerInteraction) =>
            Q.Fill(Q.Overlap(B3.Proxy.Sphere, position, position, Quaternion.identity, radius, layerMask, queryTriggerInteraction), results);

        public static int OverlapSphereNonAlloc(Vector3 position, float radius, Collider[] results, int layerMask) => OverlapSphereNonAlloc(position, radius, results, layerMask, QueryTriggerInteraction.UseGlobal);

        public static int OverlapSphereNonAlloc(Vector3 position, float radius, Collider[] results) => OverlapSphereNonAlloc(position, radius, results, AllLayers, QueryTriggerInteraction.UseGlobal);

        public static bool CheckSphere(Vector3 position, float radius, [Internal.DefaultValue("DefaultRaycastLayers")] int layerMask, [Internal.DefaultValue("QueryTriggerInteraction.UseGlobal")] QueryTriggerInteraction queryTriggerInteraction) =>
            Q.Overlap(B3.Proxy.Sphere, position, position, Quaternion.identity, radius, layerMask, queryTriggerInteraction).Count > 0;

        public static bool CheckSphere(Vector3 position, float radius, int layerMask) => CheckSphere(position, radius, layerMask, QueryTriggerInteraction.UseGlobal);

        public static bool CheckSphere(Vector3 position, float radius) => CheckSphere(position, radius, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        // ------------------------------------------------------------------ capsules

        static List<RaycastHit> CapsuleCastHits(Vector3 point1, Vector3 point2, float radius, Vector3 direction, float maxDistance, int layerMask, QueryTriggerInteraction q, bool closest) =>
            Q.Cast(B3.Proxy.Capsule, point1, point2, Quaternion.identity, radius, direction, maxDistance, layerMask, q, closest);

        public static bool CapsuleCast(Vector3 point1, Vector3 point2, float radius, Vector3 direction, [Internal.DefaultValue("Mathf.Infinity")] float maxDistance, [Internal.DefaultValue("DefaultRaycastLayers")] int layerMask, [Internal.DefaultValue("QueryTriggerInteraction.UseGlobal")] QueryTriggerInteraction queryTriggerInteraction) =>
            CapsuleCastHits(point1, point2, radius, direction, maxDistance, layerMask, queryTriggerInteraction, true).Count > 0;

        public static bool CapsuleCast(Vector3 point1, Vector3 point2, float radius, Vector3 direction, float maxDistance, int layerMask) => CapsuleCast(point1, point2, radius, direction, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);

        public static bool CapsuleCast(Vector3 point1, Vector3 point2, float radius, Vector3 direction, float maxDistance) => CapsuleCast(point1, point2, radius, direction, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static bool CapsuleCast(Vector3 point1, Vector3 point2, float radius, Vector3 direction) => CapsuleCast(point1, point2, radius, direction, Mathf.Infinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static bool CapsuleCast(Vector3 point1, Vector3 point2, float radius, Vector3 direction, out RaycastHit hitInfo, [Internal.DefaultValue("Mathf.Infinity")] float maxDistance, [Internal.DefaultValue("DefaultRaycastLayers")] int layerMask, [Internal.DefaultValue("QueryTriggerInteraction.UseGlobal")] QueryTriggerInteraction queryTriggerInteraction) =>
            Q.First(CapsuleCastHits(point1, point2, radius, direction, maxDistance, layerMask, queryTriggerInteraction, true), out hitInfo);

        public static bool CapsuleCast(Vector3 point1, Vector3 point2, float radius, Vector3 direction, out RaycastHit hitInfo, float maxDistance, int layerMask) => CapsuleCast(point1, point2, radius, direction, out hitInfo, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);

        public static bool CapsuleCast(Vector3 point1, Vector3 point2, float radius, Vector3 direction, out RaycastHit hitInfo, float maxDistance) => CapsuleCast(point1, point2, radius, direction, out hitInfo, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static bool CapsuleCast(Vector3 point1, Vector3 point2, float radius, Vector3 direction, out RaycastHit hitInfo) => CapsuleCast(point1, point2, radius, direction, out hitInfo, Mathf.Infinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static RaycastHit[] CapsuleCastAll(Vector3 point1, Vector3 point2, float radius, Vector3 direction, [Internal.DefaultValue("Mathf.Infinity")] float maxDistance, [Internal.DefaultValue("DefaultRaycastLayers")] int layerMask, [Internal.DefaultValue("QueryTriggerInteraction.UseGlobal")] QueryTriggerInteraction queryTriggerInteraction) =>
            CapsuleCastHits(point1, point2, radius, direction, maxDistance, layerMask, queryTriggerInteraction, false).ToArray();

        public static RaycastHit[] CapsuleCastAll(Vector3 point1, Vector3 point2, float radius, Vector3 direction, float maxDistance, int layerMask) => CapsuleCastAll(point1, point2, radius, direction, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);

        public static RaycastHit[] CapsuleCastAll(Vector3 point1, Vector3 point2, float radius, Vector3 direction, float maxDistance) => CapsuleCastAll(point1, point2, radius, direction, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static RaycastHit[] CapsuleCastAll(Vector3 point1, Vector3 point2, float radius, Vector3 direction) => CapsuleCastAll(point1, point2, radius, direction, Mathf.Infinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static int CapsuleCastNonAlloc(Vector3 point1, Vector3 point2, float radius, Vector3 direction, RaycastHit[] results, [Internal.DefaultValue("Mathf.Infinity")] float maxDistance, [Internal.DefaultValue("DefaultRaycastLayers")] int layerMask, [Internal.DefaultValue("QueryTriggerInteraction.UseGlobal")] QueryTriggerInteraction queryTriggerInteraction) =>
            Q.Fill(CapsuleCastHits(point1, point2, radius, direction, maxDistance, layerMask, queryTriggerInteraction, false), results);

        public static int CapsuleCastNonAlloc(Vector3 point1, Vector3 point2, float radius, Vector3 direction, RaycastHit[] results, float maxDistance, int layerMask) => CapsuleCastNonAlloc(point1, point2, radius, direction, results, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);

        public static int CapsuleCastNonAlloc(Vector3 point1, Vector3 point2, float radius, Vector3 direction, RaycastHit[] results, float maxDistance) => CapsuleCastNonAlloc(point1, point2, radius, direction, results, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static int CapsuleCastNonAlloc(Vector3 point1, Vector3 point2, float radius, Vector3 direction, RaycastHit[] results) => CapsuleCastNonAlloc(point1, point2, radius, direction, results, Mathf.Infinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static Collider[] OverlapCapsule(Vector3 point0, Vector3 point1, float radius, [Internal.DefaultValue("AllLayers")] int layerMask, [Internal.DefaultValue("QueryTriggerInteraction.UseGlobal")] QueryTriggerInteraction queryTriggerInteraction) =>
            Q.Overlap(B3.Proxy.Capsule, point0, point1, Quaternion.identity, radius, layerMask, queryTriggerInteraction).ToArray();

        public static Collider[] OverlapCapsule(Vector3 point0, Vector3 point1, float radius, int layerMask) => OverlapCapsule(point0, point1, radius, layerMask, QueryTriggerInteraction.UseGlobal);

        public static Collider[] OverlapCapsule(Vector3 point0, Vector3 point1, float radius) => OverlapCapsule(point0, point1, radius, AllLayers, QueryTriggerInteraction.UseGlobal);

        public static int OverlapCapsuleNonAlloc(Vector3 point0, Vector3 point1, float radius, Collider[] results, [Internal.DefaultValue("AllLayers")] int layerMask, [Internal.DefaultValue("QueryTriggerInteraction.UseGlobal")] QueryTriggerInteraction queryTriggerInteraction) =>
            Q.Fill(Q.Overlap(B3.Proxy.Capsule, point0, point1, Quaternion.identity, radius, layerMask, queryTriggerInteraction), results);

        public static int OverlapCapsuleNonAlloc(Vector3 point0, Vector3 point1, float radius, Collider[] results, int layerMask) => OverlapCapsuleNonAlloc(point0, point1, radius, results, layerMask, QueryTriggerInteraction.UseGlobal);

        public static int OverlapCapsuleNonAlloc(Vector3 point0, Vector3 point1, float radius, Collider[] results) => OverlapCapsuleNonAlloc(point0, point1, radius, results, AllLayers, QueryTriggerInteraction.UseGlobal);

        public static bool CheckCapsule(Vector3 start, Vector3 end, float radius, [Internal.DefaultValue("DefaultRaycastLayers")] int layerMask, [Internal.DefaultValue("QueryTriggerInteraction.UseGlobal")] QueryTriggerInteraction queryTriggerInteraction) =>
            Q.Overlap(B3.Proxy.Capsule, start, end, Quaternion.identity, radius, layerMask, queryTriggerInteraction).Count > 0;

        public static bool CheckCapsule(Vector3 start, Vector3 end, float radius, int layerMask) => CheckCapsule(start, end, radius, layerMask, QueryTriggerInteraction.UseGlobal);

        public static bool CheckCapsule(Vector3 start, Vector3 end, float radius) => CheckCapsule(start, end, radius, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        // ------------------------------------------------------------------ boxes

        static List<RaycastHit> BoxCastHits(Vector3 center, Vector3 halfExtents, Vector3 direction, Quaternion orientation, float maxDistance, int layerMask, QueryTriggerInteraction q, bool closest) =>
            Q.Cast(B3.Proxy.Box, center, Abs(halfExtents), orientation, 0f, direction, maxDistance, layerMask, q, closest);

        static Vector3 Abs(Vector3 v) => new Vector3(Mathf.Abs(v.x), Mathf.Abs(v.y), Mathf.Abs(v.z));

        public static bool BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction, [Internal.DefaultValue("Quaternion.identity")] Quaternion orientation, [Internal.DefaultValue("Mathf.Infinity")] float maxDistance, [Internal.DefaultValue("DefaultRaycastLayers")] int layerMask, [Internal.DefaultValue("QueryTriggerInteraction.UseGlobal")] QueryTriggerInteraction queryTriggerInteraction) =>
            BoxCastHits(center, halfExtents, direction, orientation, maxDistance, layerMask, queryTriggerInteraction, true).Count > 0;

        public static bool BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction, Quaternion orientation, float maxDistance, int layerMask) => BoxCast(center, halfExtents, direction, orientation, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);

        public static bool BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction, Quaternion orientation, float maxDistance) => BoxCast(center, halfExtents, direction, orientation, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static bool BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction, Quaternion orientation) => BoxCast(center, halfExtents, direction, orientation, Mathf.Infinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static bool BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction) => BoxCast(center, halfExtents, direction, Quaternion.identity, Mathf.Infinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static bool BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction, out RaycastHit hitInfo, [Internal.DefaultValue("Quaternion.identity")] Quaternion orientation, [Internal.DefaultValue("Mathf.Infinity")] float maxDistance, [Internal.DefaultValue("DefaultRaycastLayers")] int layerMask, [Internal.DefaultValue("QueryTriggerInteraction.UseGlobal")] QueryTriggerInteraction queryTriggerInteraction) =>
            Q.First(BoxCastHits(center, halfExtents, direction, orientation, maxDistance, layerMask, queryTriggerInteraction, true), out hitInfo);

        public static bool BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction, out RaycastHit hitInfo, Quaternion orientation, float maxDistance, int layerMask) => BoxCast(center, halfExtents, direction, out hitInfo, orientation, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);

        public static bool BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction, out RaycastHit hitInfo, Quaternion orientation, float maxDistance) => BoxCast(center, halfExtents, direction, out hitInfo, orientation, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static bool BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction, out RaycastHit hitInfo, Quaternion orientation) => BoxCast(center, halfExtents, direction, out hitInfo, orientation, Mathf.Infinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static bool BoxCast(Vector3 center, Vector3 halfExtents, Vector3 direction, out RaycastHit hitInfo) => BoxCast(center, halfExtents, direction, out hitInfo, Quaternion.identity, Mathf.Infinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static RaycastHit[] BoxCastAll(Vector3 center, Vector3 halfExtents, Vector3 direction, [Internal.DefaultValue("Quaternion.identity")] Quaternion orientation, [Internal.DefaultValue("Mathf.Infinity")] float maxDistance, [Internal.DefaultValue("DefaultRaycastLayers")] int layerMask, [Internal.DefaultValue("QueryTriggerInteraction.UseGlobal")] QueryTriggerInteraction queryTriggerInteraction) =>
            BoxCastHits(center, halfExtents, direction, orientation, maxDistance, layerMask, queryTriggerInteraction, false).ToArray();

        public static RaycastHit[] BoxCastAll(Vector3 center, Vector3 halfExtents, Vector3 direction, Quaternion orientation, float maxDistance, int layerMask) => BoxCastAll(center, halfExtents, direction, orientation, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);

        public static RaycastHit[] BoxCastAll(Vector3 center, Vector3 halfExtents, Vector3 direction, Quaternion orientation, float maxDistance) => BoxCastAll(center, halfExtents, direction, orientation, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static RaycastHit[] BoxCastAll(Vector3 center, Vector3 halfExtents, Vector3 direction, Quaternion orientation) => BoxCastAll(center, halfExtents, direction, orientation, Mathf.Infinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static RaycastHit[] BoxCastAll(Vector3 center, Vector3 halfExtents, Vector3 direction) => BoxCastAll(center, halfExtents, direction, Quaternion.identity, Mathf.Infinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static int BoxCastNonAlloc(Vector3 center, Vector3 halfExtents, Vector3 direction, RaycastHit[] results, [Internal.DefaultValue("Quaternion.identity")] Quaternion orientation, [Internal.DefaultValue("Mathf.Infinity")] float maxDistance, [Internal.DefaultValue("DefaultRaycastLayers")] int layerMask, [Internal.DefaultValue("QueryTriggerInteraction.UseGlobal")] QueryTriggerInteraction queryTriggerInteraction) =>
            Q.Fill(BoxCastHits(center, halfExtents, direction, orientation, maxDistance, layerMask, queryTriggerInteraction, false), results);

        public static int BoxCastNonAlloc(Vector3 center, Vector3 halfExtents, Vector3 direction, RaycastHit[] results, Quaternion orientation) => BoxCastNonAlloc(center, halfExtents, direction, results, orientation, Mathf.Infinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static int BoxCastNonAlloc(Vector3 center, Vector3 halfExtents, Vector3 direction, RaycastHit[] results, Quaternion orientation, float maxDistance) => BoxCastNonAlloc(center, halfExtents, direction, results, orientation, maxDistance, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static int BoxCastNonAlloc(Vector3 center, Vector3 halfExtents, Vector3 direction, RaycastHit[] results, Quaternion orientation, float maxDistance, int layerMask) => BoxCastNonAlloc(center, halfExtents, direction, results, orientation, maxDistance, layerMask, QueryTriggerInteraction.UseGlobal);

        public static int BoxCastNonAlloc(Vector3 center, Vector3 halfExtents, Vector3 direction, RaycastHit[] results) => BoxCastNonAlloc(center, halfExtents, direction, results, Quaternion.identity, Mathf.Infinity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static Collider[] OverlapBox(Vector3 center, Vector3 halfExtents, [Internal.DefaultValue("Quaternion.identity")] Quaternion orientation, [Internal.DefaultValue("AllLayers")] int layerMask, [Internal.DefaultValue("QueryTriggerInteraction.UseGlobal")] QueryTriggerInteraction queryTriggerInteraction) =>
            Q.Overlap(B3.Proxy.Box, center, Abs(halfExtents), orientation, 0f, layerMask, queryTriggerInteraction).ToArray();

        public static Collider[] OverlapBox(Vector3 center, Vector3 halfExtents, Quaternion orientation, int layerMask) => OverlapBox(center, halfExtents, orientation, layerMask, QueryTriggerInteraction.UseGlobal);

        public static Collider[] OverlapBox(Vector3 center, Vector3 halfExtents, Quaternion orientation) => OverlapBox(center, halfExtents, orientation, AllLayers, QueryTriggerInteraction.UseGlobal);

        public static Collider[] OverlapBox(Vector3 center, Vector3 halfExtents) => OverlapBox(center, halfExtents, Quaternion.identity, AllLayers, QueryTriggerInteraction.UseGlobal);

        public static int OverlapBoxNonAlloc(Vector3 center, Vector3 halfExtents, Collider[] results, [Internal.DefaultValue("Quaternion.identity")] Quaternion orientation, [Internal.DefaultValue("AllLayers")] int mask, [Internal.DefaultValue("QueryTriggerInteraction.UseGlobal")] QueryTriggerInteraction queryTriggerInteraction) =>
            Q.Fill(Q.Overlap(B3.Proxy.Box, center, Abs(halfExtents), orientation, 0f, mask, queryTriggerInteraction), results);

        public static int OverlapBoxNonAlloc(Vector3 center, Vector3 halfExtents, Collider[] results, Quaternion orientation, int mask) => OverlapBoxNonAlloc(center, halfExtents, results, orientation, mask, QueryTriggerInteraction.UseGlobal);

        public static int OverlapBoxNonAlloc(Vector3 center, Vector3 halfExtents, Collider[] results, Quaternion orientation) => OverlapBoxNonAlloc(center, halfExtents, results, orientation, AllLayers, QueryTriggerInteraction.UseGlobal);

        public static int OverlapBoxNonAlloc(Vector3 center, Vector3 halfExtents, Collider[] results) => OverlapBoxNonAlloc(center, halfExtents, results, Quaternion.identity, AllLayers, QueryTriggerInteraction.UseGlobal);

        public static bool CheckBox(Vector3 center, Vector3 halfExtents, [Internal.DefaultValue("Quaternion.identity")] Quaternion orientation, [Internal.DefaultValue("DefaultRaycastLayers")] int layermask, [Internal.DefaultValue("QueryTriggerInteraction.UseGlobal")] QueryTriggerInteraction queryTriggerInteraction) =>
            Q.Overlap(B3.Proxy.Box, center, Abs(halfExtents), orientation, 0f, layermask, queryTriggerInteraction).Count > 0;

        public static bool CheckBox(Vector3 center, Vector3 halfExtents, Quaternion orientation, int layerMask) => CheckBox(center, halfExtents, orientation, layerMask, QueryTriggerInteraction.UseGlobal);

        public static bool CheckBox(Vector3 center, Vector3 halfExtents, Quaternion orientation) => CheckBox(center, halfExtents, orientation, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);

        public static bool CheckBox(Vector3 center, Vector3 halfExtents) => CheckBox(center, halfExtents, Quaternion.identity, DefaultRaycastLayers, QueryTriggerInteraction.UseGlobal);
    }
}
