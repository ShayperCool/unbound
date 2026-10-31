using System;
using System.Collections.Generic;
using Unbound.Box2D.Collision.Shapes;
using Unbound.Runtime;
using B2Settings = Unbound.Box2D.Common.Settings;
using B2Transform = Unbound.Box2D.Common.Transform;
using Q = Unbound.Runtime.Physics2DQueries;
using Shape = Unbound.Box2D.Collision.Shapes.Shape;
using W = Unbound.Runtime.Physics2DWorld;

namespace UnityEngine
{
    /// <summary>
    /// The global 2D physics settings (ProjectSettings/Physics2DSettings.asset) and queries. The
    /// simulation itself is <see cref="Physics2DWorld"/>.
    /// </summary>
    public partial class Physics2D
    {
        public const int IgnoreRaycastLayer = 4;
        public const int DefaultRaycastLayers = -5;
        public const int AllLayers = -1;
        public const int MaxPolygonShapeVertices = 8;

        // Unity 6000.0's defaults; the project's settings asset overrides them.
        internal static Vector2 s_Gravity = new Vector2(0f, -9.81f);
        internal static int s_VelocityIterations = 8;
        internal static int s_PositionIterations = 3;
        internal static bool s_QueriesHitTriggers = true;
        internal static bool s_QueriesStartInColliders = true;
        internal static bool s_CallbacksOnDisable = true;
        internal static bool s_ReuseCollisionCallbacks = true;
        internal static bool s_AutoSyncTransforms;
        internal static SimulationMode2D s_SimulationMode = SimulationMode2D.FixedUpdate;
        internal static int s_SimulationLayers = -1;
        internal static bool s_UseSubStepping;
        internal static bool s_UseSubStepContacts;
        internal static float s_MinSubStepFPS = 30f;
        internal static int s_MaxSubStepCount = 4;
        internal static float s_BounceThreshold = 1f;
        internal static float s_ContactThreshold;
        internal static float s_MaxLinearCorrection = 0.2f;
        internal static float s_MaxAngularCorrection = 8f;
        internal static float s_MaxTranslationSpeed = 100f;
        internal static float s_MaxRotationSpeed = 360f;
        internal static float s_DefaultContactOffset = 0.01f;
        internal static float s_BaumgarteScale = 0.2f;
        internal static float s_BaumgarteTOIScale = 0.75f;
        internal static float s_TimeToSleep = 0.5f;
        internal static float s_LinearSleepTolerance = 0.01f;
        internal static float s_AngularSleepTolerance = 2f;
        /// <summary>Bit j of entry i: layers i and j collide.</summary>
        internal static readonly int[] s_LayerCollisionMatrix = NewMatrix();
        static PhysicsMaterial2D s_DefaultMaterial;
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

        /// <summary>Reads ProjectSettings/Physics2DSettings.asset once (when the native side is there).</summary>
        internal static void EnsureSettings()
        {
            if (s_SettingsLoaded) return;
            s_SettingsLoaded = true;
            try
            {
                if (Native.IsReady && !string.IsNullOrEmpty(ProjectInfo.ProjectPath))
                {
                    var path = System.IO.Path.Combine(ProjectInfo.ProjectPath, "ProjectSettings", "Physics2DSettings.asset");
                    if (System.IO.File.Exists(path))
                        foreach (var doc in Native.LoadDocuments(path))
                            if (doc.ClassId == 19) ReadSettings(doc.Body);
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Unbound: reading Physics2DSettings: {e.Message}");
            }
            ApplyTuning();
        }

        /// <summary>The fields of a Physics2DSettings document (older names where Unity renamed them).
        /// A setting the document lacks keeps its default, except autoSyncTransforms: a settings asset
        /// written before the setting existed always synced, and Unity keeps that for such projects.</summary>
        internal static void ReadSettings(YNode body)
        {
            var g = body.Get("m_Gravity");
            if (g != null) s_Gravity = body.Vector2("m_Gravity");
            s_VelocityIterations = body.Int("m_VelocityIterations", s_VelocityIterations);
            s_PositionIterations = body.Int("m_PositionIterations", s_PositionIterations);
            s_BounceThreshold = body.Float("m_BounceThreshold", body.Float("m_VelocityThreshold", s_BounceThreshold));
            s_ContactThreshold = body.Float("m_ContactThreshold", s_ContactThreshold);
            s_MaxLinearCorrection = body.Float("m_MaxLinearCorrection", s_MaxLinearCorrection);
            s_MaxAngularCorrection = body.Float("m_MaxAngularCorrection", s_MaxAngularCorrection);
            s_MaxTranslationSpeed = body.Float("m_MaxTranslationSpeed", s_MaxTranslationSpeed);
            s_MaxRotationSpeed = body.Float("m_MaxRotationSpeed", s_MaxRotationSpeed);
            s_BaumgarteScale = body.Float("m_BaumgarteScale", s_BaumgarteScale);
            s_BaumgarteTOIScale = body.Float("m_BaumgarteTimeOfImpactScale", s_BaumgarteTOIScale);
            s_TimeToSleep = body.Float("m_TimeToSleep", s_TimeToSleep);
            s_LinearSleepTolerance = body.Float("m_LinearSleepTolerance", s_LinearSleepTolerance);
            s_AngularSleepTolerance = body.Float("m_AngularSleepTolerance", s_AngularSleepTolerance);
            s_DefaultContactOffset = body.Float("m_DefaultContactOffset", s_DefaultContactOffset);
            s_QueriesHitTriggers = body.Bool("m_QueriesHitTriggers", body.Bool("m_RaycastsHitTriggers", s_QueriesHitTriggers));
            s_QueriesStartInColliders = body.Bool("m_QueriesStartInColliders", body.Bool("m_RaycastsStartInColliders", s_QueriesStartInColliders));
            s_CallbacksOnDisable = body.Bool("m_CallbacksOnDisable", s_CallbacksOnDisable);
            s_ReuseCollisionCallbacks = body.Bool("m_ReuseCollisionCallbacks", false);
            s_AutoSyncTransforms = body.Bool("m_AutoSyncTransforms", true);
            if (body.Get("m_SimulationMode") != null) s_SimulationMode = (SimulationMode2D)body.Int("m_SimulationMode");
            else if (body.Get("m_AutoSimulation") != null) s_SimulationMode = body.Bool("m_AutoSimulation", true) ? SimulationMode2D.FixedUpdate : SimulationMode2D.Script;
            if (body.Get("m_SimulationLayers") != null) s_SimulationLayers = Collider2D.ReadMask(body, "m_SimulationLayers", -1);
            s_UseSubStepping = body.Bool("m_UseSubStepping", s_UseSubStepping);
            s_UseSubStepContacts = body.Bool("m_UseSubStepContacts", s_UseSubStepContacts);
            s_MinSubStepFPS = body.Float("m_MinSubStepFPS", s_MinSubStepFPS);
            s_MaxSubStepCount = body.Int("m_MaxSubStepCount", s_MaxSubStepCount);
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

        /// <summary>Box2D's tuning values from the settings.</summary>
        internal static void ApplyTuning()
        {
            float offset = Mathf.Max(0.0001f, s_DefaultContactOffset);
            B2Settings.PolygonRadius = offset;
            B2Settings.LinearSlop = offset * 0.5f;
            B2Settings.VelocityThreshold = Mathf.Max(0f, s_BounceThreshold);
            B2Settings.MaxLinearCorrection = Mathf.Max(0.0001f, s_MaxLinearCorrection);
            B2Settings.MaxAngularCorrection = Mathf.Max(0.0001f, s_MaxAngularCorrection) * Mathf.Deg2Rad;
            // Unity's limits are per simulation step.
            B2Settings.SetMaxMotion(Mathf.Max(0.0001f, s_MaxTranslationSpeed), Mathf.Max(0.0001f, s_MaxRotationSpeed) * Mathf.Deg2Rad);
            B2Settings.Baumgarte = Mathf.Max(0f, s_BaumgarteScale);
            B2Settings.ToiBaumgarte = Mathf.Max(0f, s_BaumgarteTOIScale);
            B2Settings.TimeToSleep = Mathf.Max(0f, s_TimeToSleep);
            B2Settings.LinearSleepTolerance = Mathf.Max(0f, s_LinearSleepTolerance);
            B2Settings.AngularSleepTolerance = Mathf.Max(0f, s_AngularSleepTolerance) * Mathf.Deg2Rad;
        }

        /// <summary>The material a collider's shapes use: its own, its rigidbody's, or the default.</summary>
        internal static PhysicsMaterial2D MaterialOf(Collider2D c)
        {
            if (c == null) return DefaultMaterial;
            if (c.m_Material != null) return c.m_Material;
            var rb = c.m_Registered ? c.m_AttachedBody : null;
            if (rb != null && rb.m_Material != null) return rb.m_Material;
            return DefaultMaterial;
        }

        static PhysicsMaterial2D DefaultMaterial
        {
            get
            {
                if (!s_DefaultMaterialResolved)
                {
                    s_DefaultMaterialResolved = true;
                    try
                    {
                        if (Native.IsReady)
                            s_DefaultMaterial = AssetDatabaseRuntime.Load(s_DefaultMaterialRef.Guid, s_DefaultMaterialRef.FileId, typeof(PhysicsMaterial2D)) as PhysicsMaterial2D;
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Unbound: the default 2D physics material: {e.Message}");
                    }
                }
                return s_DefaultMaterial;
            }
        }

        internal static float CombineFriction(Collider2D a, float frictionA, Collider2D b, float frictionB) =>
            PhysicsMaterial2D.GetCombinedValues(frictionA, frictionB, a != null ? a.m_EffectiveFrictionCombine : PhysicsMaterialCombine2D.Mean, b != null ? b.m_EffectiveFrictionCombine : PhysicsMaterialCombine2D.Mean);

        internal static float CombineBounciness(Collider2D a, float bouncinessA, Collider2D b, float bouncinessB) =>
            PhysicsMaterial2D.GetCombinedValues(bouncinessA, bouncinessB, a != null ? a.m_EffectiveBounceCombine : PhysicsMaterialCombine2D.Maximum, b != null ? b.m_EffectiveBounceCombine : PhysicsMaterialCombine2D.Maximum);

        /// <summary>The filter of a collider's or rigidbody's casts without one: the layers its own
        /// layer collides with.</summary>
        internal static ContactFilter2D DefaultCastFilter(GameObject go)
        {
            var filter = new ContactFilter2D().NoFilter();
            filter.useTriggers = s_QueriesHitTriggers;
            if (go != null) filter.SetLayerMask(GetLayerCollisionMask(go.m_Layer));
            return filter;
        }

        [Unbound.EditorFaithful("handle 0 is the one 2D world (PhysicsScene2D.IsValid answers so)")]
        public static PhysicsScene2D defaultPhysicsScene => default;

        public static int velocityIterations
        {
            get => s_VelocityIterations;
            set => s_VelocityIterations = Math.Max(1, value);
        }

        public static int positionIterations
        {
            get => s_PositionIterations;
            set => s_PositionIterations = Math.Max(1, value);
        }

        public static Vector2 gravity
        {
            get => s_Gravity;
            set
            {
                s_Gravity = value;
                // Sleeping bodies notice a new gravity.
                foreach (var rb in W.Rigidbodies) if (rb.m_Body != null) rb.m_Body.IsAwake = true;
            }
        }

        public static bool queriesHitTriggers { get => s_QueriesHitTriggers; set => s_QueriesHitTriggers = value; }
        public static bool queriesStartInColliders { get => s_QueriesStartInColliders; set => s_QueriesStartInColliders = value; }
        public static bool callbacksOnDisable { get => s_CallbacksOnDisable; set => s_CallbacksOnDisable = value; }
        public static bool reuseCollisionCallbacks { get => s_ReuseCollisionCallbacks; set => s_ReuseCollisionCallbacks = value; }
        public static bool autoSyncTransforms { get => s_AutoSyncTransforms; set => s_AutoSyncTransforms = value; }
        public static SimulationMode2D simulationMode { get => s_SimulationMode; set => s_SimulationMode = value; }
        public static LayerMask simulationLayers { get => s_SimulationLayers; set => s_SimulationLayers = value; }
        public static bool useSubStepping { get => s_UseSubStepping; set => s_UseSubStepping = value; }
        public static bool useSubStepContacts { get => s_UseSubStepContacts; set => s_UseSubStepContacts = value; }
        public static float minSubStepFPS { get => s_MinSubStepFPS; set => s_MinSubStepFPS = Mathf.Max(1f, value); }
        public static int maxSubStepCount { get => s_MaxSubStepCount; set => s_MaxSubStepCount = Math.Max(1, value); }
        public static float bounceThreshold { get => s_BounceThreshold; set { s_BounceThreshold = Mathf.Max(0f, value); ApplyTuning(); } }
        public static float contactThreshold { get => s_ContactThreshold; set => s_ContactThreshold = Mathf.Max(0f, value); }
        public static float maxLinearCorrection { get => s_MaxLinearCorrection; set { s_MaxLinearCorrection = Mathf.Max(0.0001f, value); ApplyTuning(); } }
        public static float maxAngularCorrection { get => s_MaxAngularCorrection; set { s_MaxAngularCorrection = Mathf.Max(0.0001f, value); ApplyTuning(); } }
        public static float maxTranslationSpeed { get => s_MaxTranslationSpeed; set { s_MaxTranslationSpeed = Mathf.Max(0.0001f, value); ApplyTuning(); } }
        public static float maxRotationSpeed { get => s_MaxRotationSpeed; set { s_MaxRotationSpeed = Mathf.Max(0.0001f, value); ApplyTuning(); } }
        public static float defaultContactOffset { get => s_DefaultContactOffset; set { s_DefaultContactOffset = Mathf.Max(0.0001f, value); ApplyTuning(); } }
        public static float baumgarteScale { get => s_BaumgarteScale; set { s_BaumgarteScale = Mathf.Max(0f, value); ApplyTuning(); } }
        public static float baumgarteTOIScale { get => s_BaumgarteTOIScale; set { s_BaumgarteTOIScale = Mathf.Max(0f, value); ApplyTuning(); } }
        public static float timeToSleep { get => s_TimeToSleep; set { s_TimeToSleep = Mathf.Max(0f, value); ApplyTuning(); } }
        public static float linearSleepTolerance { get => s_LinearSleepTolerance; set { s_LinearSleepTolerance = Mathf.Max(0f, value); ApplyTuning(); } }
        public static float angularSleepTolerance { get => s_AngularSleepTolerance; set { s_AngularSleepTolerance = Mathf.Max(0f, value); ApplyTuning(); } }

        public static bool raycastsHitTriggers { get => queriesHitTriggers; set => queriesHitTriggers = value; }
        public static bool raycastsStartInColliders { get => queriesStartInColliders; set => queriesStartInColliders = value; }
        public static float velocityThreshold { get => bounceThreshold; set => bounceThreshold = value; }
        public static bool autoSimulation
        {
            get => s_SimulationMode != SimulationMode2D.Script;
            set => s_SimulationMode = value ? SimulationMode2D.FixedUpdate : SimulationMode2D.Script;
        }

        // ------------------------------------------------------------------ simulation

        public static bool Simulate(float deltaTime) => Simulate(deltaTime, AllLayers);

        public static bool Simulate(float deltaTime, int simulationLayers = -1)
        {
            W.EnsureRegistered();
            return W.Simulate(deltaTime, simulationLayers, fromLoop: false);
        }

        public static void SyncTransforms() => W.SyncTransforms();

        // ------------------------------------------------------------------ collision filtering

        public static void IgnoreCollision(Collider2D collider1, Collider2D collider2) => IgnoreCollision(collider1, collider2, true);

        public static void IgnoreCollision(Collider2D collider1, Collider2D collider2, bool ignore)
        {
            if (collider1 == null) throw new ArgumentNullException(nameof(collider1));
            if (collider2 == null) throw new ArgumentNullException(nameof(collider2));
            if (!collider1.m_Registered || !collider2.m_Registered)
            {
                Debug.LogWarning("Physics2D.IgnoreCollision needs both colliders to be active and enabled.", collider1);
                return;
            }
            W.SetIgnored(collider1, collider2, ignore);
        }

        public static bool GetIgnoreCollision(Collider2D collider1, Collider2D collider2)
        {
            if (collider1 == null) throw new ArgumentNullException(nameof(collider1));
            if (collider2 == null) throw new ArgumentNullException(nameof(collider2));
            return W.IsIgnored(collider1, collider2);
        }

        public static void IgnoreLayerCollision(int layer1, int layer2) => IgnoreLayerCollision(layer1, layer2, true);

        public static void IgnoreLayerCollision(int layer1, int layer2, bool ignore)
        {
            if (layer1 < 0 || layer1 > 31) throw new ArgumentOutOfRangeException(nameof(layer1), "layer1 is out of range. Layer numbers must be in the range 0 to 31.");
            if (layer2 < 0 || layer2 > 31) throw new ArgumentOutOfRangeException(nameof(layer2), "layer2 is out of range. Layer numbers must be in the range 0 to 31.");
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
            W.Flush();
            W.RefilterAll();
        }

        public static bool GetIgnoreLayerCollision(int layer1, int layer2)
        {
            if (layer1 < 0 || layer1 > 31) throw new ArgumentOutOfRangeException(nameof(layer1), "layer1 is out of range. Layer numbers must be in the range 0 to 31.");
            if (layer2 < 0 || layer2 > 31) throw new ArgumentOutOfRangeException(nameof(layer2), "layer2 is out of range. Layer numbers must be in the range 0 to 31.");
            return (s_LayerCollisionMatrix[layer1] & (1 << layer2)) == 0;
        }

        public static void SetLayerCollisionMask(int layer, int layerMask)
        {
            if (layer < 0 || layer > 31) throw new ArgumentOutOfRangeException(nameof(layer), "layer is out of range. Layer numbers must be in the range 0 to 31.");
            for (int other = 0; other < 32; other++)
            {
                bool collide = (layerMask & (1 << other)) != 0;
                if (collide)
                {
                    s_LayerCollisionMatrix[layer] |= 1 << other;
                    s_LayerCollisionMatrix[other] |= 1 << layer;
                }
                else
                {
                    s_LayerCollisionMatrix[layer] &= ~(1 << other);
                    s_LayerCollisionMatrix[other] &= ~(1 << layer);
                }
            }
            W.Flush();
            W.RefilterAll();
        }

        public static int GetLayerCollisionMask(int layer)
        {
            if (layer < 0 || layer > 31) throw new ArgumentOutOfRangeException(nameof(layer), "layer is out of range. Layer numbers must be in the range 0 to 31.");
            return s_LayerCollisionMatrix[layer];
        }

        // ------------------------------------------------------------------ contacts

        static bool PairPasses(W.Pair pair, Collider2D self, ContactFilter2D filter, bool requireEnabled = false)
        {
            var other = ReferenceEquals(pair.A, self) ? pair.B : pair.A;
            if (other == null || other.m_Destroyed) return false;
            if (requireEnabled && !W.HasEnabledContact(self, other)) return false;
            if (pair.IsTrigger && !filter.useTriggers) return false;
            if (filter.IsFilteringLayerMask(other.m_GameObject) || filter.IsFilteringDepth(other.m_GameObject)) return false;
            if (filter.useNormalAngle && !pair.IsTrigger)
            {
                var points = new List<ContactPoint2D>();
                W.CollectContacts(self, other, points, false);
                foreach (var p in points)
                    if ((!requireEnabled || p.enabled) && !filter.IsFilteringNormalAngle(p.normal)) return true;
                return false;
            }
            return true;
        }

        public static bool IsTouching(Collider2D collider1, Collider2D collider2)
        {
            if (collider1 == null) throw new ArgumentNullException(nameof(collider1));
            if (collider2 == null) throw new ArgumentNullException(nameof(collider2));
            var pair = W.FindPair(collider1, collider2);
            return pair != null && pair.Touching > 0 && W.HasEnabledContact(collider1, collider2);
        }

        public static bool IsTouching(Collider2D collider1, Collider2D collider2, ContactFilter2D contactFilter)
        {
            if (collider1 == null || collider2 == null) return false;
            var pair = W.FindPair(collider1, collider2);
            return pair != null && pair.Touching > 0 && PairPasses(pair, collider1, contactFilter, requireEnabled: true);
        }

        public static bool IsTouching(Collider2D collider, ContactFilter2D contactFilter)
        {
            if (collider == null) return false;
            foreach (var pair in W.PairsOf(collider))
                if (PairPasses(pair, collider, contactFilter, requireEnabled: true)) return true;
            return false;
        }

        public static bool IsTouchingLayers(Collider2D collider) => IsTouchingLayers(collider, AllLayers);

        public static bool IsTouchingLayers(Collider2D collider, int layerMask)
        {
            if (collider == null) throw new ArgumentNullException(nameof(collider));
            foreach (var pair in W.PairsOf(collider))
            {
                var other = ReferenceEquals(pair.A, collider) ? pair.B : pair.A;
                if (other != null && !other.m_Destroyed && (layerMask & (1 << other.m_GameObject.m_Layer)) != 0 && W.HasEnabledContact(collider, other)) return true;
            }
            return false;
        }

        static List<ContactPoint2D> ContactPoints(IEnumerable<Collider2D> colliders, Collider2D only, ContactFilter2D filter)
        {
            var result = new List<ContactPoint2D>();
            var points = new List<ContactPoint2D>();
            foreach (var self in colliders)
            {
                foreach (var pair in W.PairsOf(self))
                {
                    var other = ReferenceEquals(pair.A, self) ? pair.B : pair.A;
                    if (only != null && !ReferenceEquals(other, only)) continue;
                    if (pair.IsTrigger || !PairPasses(pair, self, filter)) continue;
                    points.Clear();
                    W.CollectContacts(self, other, points, false);
                    foreach (var p in points)
                        if (!filter.IsFilteringNormalAngle(p.normal)) result.Add(p);
                }
            }
            return result;
        }

        static List<Collider2D> ContactColliders(IEnumerable<Collider2D> colliders, ContactFilter2D filter)
        {
            var result = new List<Collider2D>();
            foreach (var self in colliders)
                foreach (var pair in W.PairsOf(self))
                {
                    var other = ReferenceEquals(pair.A, self) ? pair.B : pair.A;
                    if (!PairPasses(pair, self, filter) || result.Contains(other)) continue;
                    result.Add(other);
                }
            return result;
        }

        static int Copy<T>(List<T> found, T[] results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            int count = Math.Min(found.Count, results.Length);
            for (int i = 0; i < count; i++) results[i] = found[i];
            return count;
        }

        static int Copy<T>(List<T> found, List<T> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            results.Clear();
            results.AddRange(found);
            return results.Count;
        }

        static IEnumerable<Collider2D> Attached(Rigidbody2D rigidbody)
        {
            if (rigidbody == null) throw new ArgumentNullException(nameof(rigidbody));
            return rigidbody.AttachedColliders(true);
        }

        public static int GetContacts(Collider2D collider1, Collider2D collider2, ContactFilter2D contactFilter, ContactPoint2D[] contacts) => Copy(ContactPoints(new[] { collider1 }, collider2, contactFilter), contacts);
        public static int GetContacts(Collider2D collider, ContactPoint2D[] contacts) => GetContacts(collider, new ContactFilter2D().NoFilter(), contacts);
        public static int GetContacts(Collider2D collider, ContactFilter2D contactFilter, ContactPoint2D[] contacts) => Copy(ContactPoints(new[] { collider }, null, contactFilter), contacts);
        public static int GetContacts(Collider2D collider, Collider2D[] colliders) => GetContacts(collider, new ContactFilter2D().NoFilter(), colliders);
        public static int GetContacts(Collider2D collider, ContactFilter2D contactFilter, Collider2D[] colliders) => Copy(ContactColliders(new[] { collider }, contactFilter), colliders);
        public static int GetContacts(Rigidbody2D rigidbody, ContactPoint2D[] contacts) => GetContacts(rigidbody, new ContactFilter2D().NoFilter(), contacts);
        public static int GetContacts(Rigidbody2D rigidbody, ContactFilter2D contactFilter, ContactPoint2D[] contacts) => Copy(ContactPoints(Attached(rigidbody), null, contactFilter), contacts);
        public static int GetContacts(Rigidbody2D rigidbody, Collider2D[] colliders) => GetContacts(rigidbody, new ContactFilter2D().NoFilter(), colliders);
        public static int GetContacts(Rigidbody2D rigidbody, ContactFilter2D contactFilter, Collider2D[] colliders) => Copy(ContactColliders(Attached(rigidbody), contactFilter), colliders);
        public static int GetContacts(Collider2D collider1, Collider2D collider2, ContactFilter2D contactFilter, List<ContactPoint2D> contacts) => Copy(ContactPoints(new[] { collider1 }, collider2, contactFilter), contacts);
        public static int GetContacts(Collider2D collider, List<ContactPoint2D> contacts) => GetContacts(collider, new ContactFilter2D().NoFilter(), contacts);
        public static int GetContacts(Collider2D collider, ContactFilter2D contactFilter, List<ContactPoint2D> contacts) => Copy(ContactPoints(new[] { collider }, null, contactFilter), contacts);
        public static int GetContacts(Collider2D collider, List<Collider2D> colliders) => GetContacts(collider, new ContactFilter2D().NoFilter(), colliders);
        public static int GetContacts(Collider2D collider, ContactFilter2D contactFilter, List<Collider2D> colliders) => Copy(ContactColliders(new[] { collider }, contactFilter), colliders);
        public static int GetContacts(Rigidbody2D rigidbody, List<ContactPoint2D> contacts) => GetContacts(rigidbody, new ContactFilter2D().NoFilter(), contacts);
        public static int GetContacts(Rigidbody2D rigidbody, ContactFilter2D contactFilter, List<ContactPoint2D> contacts) => Copy(ContactPoints(Attached(rigidbody), null, contactFilter), contacts);
        public static int GetContacts(Rigidbody2D rigidbody, List<Collider2D> colliders) => GetContacts(rigidbody, new ContactFilter2D().NoFilter(), colliders);
        public static int GetContacts(Rigidbody2D rigidbody, ContactFilter2D contactFilter, List<Collider2D> colliders) => Copy(ContactColliders(Attached(rigidbody), contactFilter), colliders);

        // ------------------------------------------------------------------ distance

        public static ColliderDistance2D Distance(Collider2D colliderA, Collider2D colliderB)
        {
            if (colliderA == null) throw new ArgumentNullException(nameof(colliderA));
            if (colliderB == null) throw new ArgumentNullException(nameof(colliderB));
            return Q.Distance(colliderA, null, colliderB, null, overrideB: false);
        }

        public static ColliderDistance2D Distance(Collider2D colliderA, Vector2 positionA, float angleA, Collider2D colliderB, Vector2 positionB, float angleB)
        {
            if (colliderA == null) throw new ArgumentNullException(nameof(colliderA));
            if (colliderB == null) throw new ArgumentNullException(nameof(colliderB));
            return Q.Distance(colliderA, new B2Transform(W.ToB2(positionA), angleA * Mathf.Deg2Rad), colliderB, new B2Transform(W.ToB2(positionB), angleB * Mathf.Deg2Rad), overrideB: true);
        }

        public static Vector2 ClosestPoint(Vector2 position, Collider2D collider)
        {
            if (collider == null) throw new ArgumentNullException(nameof(collider));
            return Q.ClosestPoint(position, new[] { collider });
        }

        public static Vector2 ClosestPoint(Vector2 position, Rigidbody2D rigidbody) => Q.ClosestPoint(position, Attached(rigidbody));

        // ------------------------------------------------------------------ query cores

        static ContactFilter2D Legacy(int layerMask, float minDepth, float maxDepth) => ContactFilter2D.CreateLegacyFilter(layerMask, minDepth, maxDepth);

        static RaycastHit2D First(List<RaycastHit2D> hits) => hits.Count > 0 ? hits[0] : default;

        static Collider2D First(List<Collider2D> colliders) => colliders.Count > 0 ? colliders[0] : null;

        static B2Transform Pose(Vector2 position, float angleDegrees) => new B2Transform(W.ToB2(position), angleDegrees * Mathf.Deg2Rad);

        static Shape CircleShapeAt(float radius) => new CircleShape { Radius = Mathf.Max(radius, 0f) };

        static Shape BoxShape(Vector2 size)
        {
            var shape = new PolygonShape();
            float min = B2Settings.LinearSlop;
            shape.SetAsBox(Mathf.Max(Mathf.Abs(size.x) * 0.5f, min), Mathf.Max(Mathf.Abs(size.y) * 0.5f, min));
            return shape;
        }

        static Shape CapsuleShape(Vector2 size, CapsuleDirection2D direction) =>
            CapsuleCollider2D.CapsuleShapeOf(Matrix4x4.identity, Vector2.zero, size, direction) ?? CircleShapeAt(B2Settings.LinearSlop);

        internal static List<RaycastHit2D> RaycastList(Vector2 origin, Vector2 direction, float distance, ContactFilter2D filter) => Q.Raycast(origin, direction, distance, filter);

        internal static List<RaycastHit2D> LinecastList(Vector2 start, Vector2 end, ContactFilter2D filter)
        {
            var delta = end - start;
            return Q.Raycast(start, delta, delta.magnitude, filter);
        }

        internal static List<RaycastHit2D> CircleCastList(Vector2 origin, float radius, Vector2 direction, float distance, ContactFilter2D filter) =>
            Q.ShapeCast(CircleShapeAt(radius), Pose(origin, 0f), direction, distance, filter);

        internal static List<RaycastHit2D> BoxCastList(Vector2 origin, Vector2 size, float angle, Vector2 direction, float distance, ContactFilter2D filter) =>
            Q.ShapeCast(BoxShape(size), Pose(origin, angle), direction, distance, filter);

        internal static List<RaycastHit2D> CapsuleCastList(Vector2 origin, Vector2 size, CapsuleDirection2D capsuleDirection, float angle, Vector2 direction, float distance, ContactFilter2D filter) =>
            Q.ShapeCast(CapsuleShape(size, capsuleDirection), Pose(origin, angle), direction, distance, filter);

        internal static List<Collider2D> OverlapPointList(Vector2 point, ContactFilter2D filter) => Q.OverlapPoint(point, filter);

        internal static List<Collider2D> OverlapCircleList(Vector2 point, float radius, ContactFilter2D filter) => Q.OverlapShape(CircleShapeAt(radius), Pose(point, 0f), filter);

        internal static List<Collider2D> OverlapBoxList(Vector2 point, Vector2 size, float angle, ContactFilter2D filter) => Q.OverlapShape(BoxShape(size), Pose(point, angle), filter);

        internal static List<Collider2D> OverlapAreaList(Vector2 pointA, Vector2 pointB, ContactFilter2D filter)
        {
            var min = Vector2.Min(pointA, pointB);
            var max = Vector2.Max(pointA, pointB);
            return Q.OverlapShape(BoxShape(max - min), Pose((min + max) * 0.5f, 0f), filter);
        }

        internal static List<Collider2D> OverlapCapsuleList(Vector2 point, Vector2 size, CapsuleDirection2D direction, float angle, ContactFilter2D filter) =>
            Q.OverlapShape(CapsuleShape(size, direction), Pose(point, angle), filter);

        const float Inf = float.PositiveInfinity;
        const float NegInf = float.NegativeInfinity;

        // ------------------------------------------------------------------ linecasts

        public static RaycastHit2D Linecast(Vector2 start, Vector2 end) => Linecast(start, end, DefaultRaycastLayers, NegInf, Inf);
        public static RaycastHit2D Linecast(Vector2 start, Vector2 end, int layerMask) => Linecast(start, end, layerMask, NegInf, Inf);
        public static RaycastHit2D Linecast(Vector2 start, Vector2 end, int layerMask, float minDepth) => Linecast(start, end, layerMask, minDepth, Inf);
        public static RaycastHit2D Linecast(Vector2 start, Vector2 end, int layerMask, float minDepth, float maxDepth) => First(LinecastList(start, end, Legacy(layerMask, minDepth, maxDepth)));
        public static int Linecast(Vector2 start, Vector2 end, ContactFilter2D contactFilter, RaycastHit2D[] results) => Copy(LinecastList(start, end, contactFilter), results);
        public static int Linecast(Vector2 start, Vector2 end, ContactFilter2D contactFilter, List<RaycastHit2D> results) => Copy(LinecastList(start, end, contactFilter), results);
        public static RaycastHit2D[] LinecastAll(Vector2 start, Vector2 end) => LinecastAll(start, end, DefaultRaycastLayers, NegInf, Inf);
        public static RaycastHit2D[] LinecastAll(Vector2 start, Vector2 end, int layerMask) => LinecastAll(start, end, layerMask, NegInf, Inf);
        public static RaycastHit2D[] LinecastAll(Vector2 start, Vector2 end, int layerMask, float minDepth) => LinecastAll(start, end, layerMask, minDepth, Inf);
        public static RaycastHit2D[] LinecastAll(Vector2 start, Vector2 end, int layerMask, float minDepth, float maxDepth) => LinecastList(start, end, Legacy(layerMask, minDepth, maxDepth)).ToArray();
        public static int LinecastNonAlloc(Vector2 start, Vector2 end, RaycastHit2D[] results) => LinecastNonAlloc(start, end, results, DefaultRaycastLayers, NegInf, Inf);
        public static int LinecastNonAlloc(Vector2 start, Vector2 end, RaycastHit2D[] results, int layerMask) => LinecastNonAlloc(start, end, results, layerMask, NegInf, Inf);
        public static int LinecastNonAlloc(Vector2 start, Vector2 end, RaycastHit2D[] results, int layerMask, float minDepth) => LinecastNonAlloc(start, end, results, layerMask, minDepth, Inf);
        public static int LinecastNonAlloc(Vector2 start, Vector2 end, RaycastHit2D[] results, int layerMask, float minDepth, float maxDepth) => Copy(LinecastList(start, end, Legacy(layerMask, minDepth, maxDepth)), results);

        // ------------------------------------------------------------------ raycasts

        public static RaycastHit2D Raycast(Vector2 origin, Vector2 direction) => Raycast(origin, direction, Inf, DefaultRaycastLayers, NegInf, Inf);
        public static RaycastHit2D Raycast(Vector2 origin, Vector2 direction, float distance) => Raycast(origin, direction, distance, DefaultRaycastLayers, NegInf, Inf);
        public static RaycastHit2D Raycast(Vector2 origin, Vector2 direction, float distance, int layerMask) => Raycast(origin, direction, distance, layerMask, NegInf, Inf);
        public static RaycastHit2D Raycast(Vector2 origin, Vector2 direction, float distance, int layerMask, float minDepth) => Raycast(origin, direction, distance, layerMask, minDepth, Inf);
        public static RaycastHit2D Raycast(Vector2 origin, Vector2 direction, float distance, int layerMask, float minDepth, float maxDepth) => First(RaycastList(origin, direction, distance, Legacy(layerMask, minDepth, maxDepth)));
        public static int Raycast(Vector2 origin, Vector2 direction, ContactFilter2D contactFilter, RaycastHit2D[] results) => Raycast(origin, direction, contactFilter, results, Inf);
        public static int Raycast(Vector2 origin, Vector2 direction, ContactFilter2D contactFilter, RaycastHit2D[] results, float distance) => Copy(RaycastList(origin, direction, distance, contactFilter), results);
        public static int Raycast(Vector2 origin, Vector2 direction, ContactFilter2D contactFilter, List<RaycastHit2D> results, float distance = float.PositiveInfinity) => Copy(RaycastList(origin, direction, distance, contactFilter), results);
        public static RaycastHit2D[] RaycastAll(Vector2 origin, Vector2 direction) => RaycastAll(origin, direction, Inf, DefaultRaycastLayers, NegInf, Inf);
        public static RaycastHit2D[] RaycastAll(Vector2 origin, Vector2 direction, float distance) => RaycastAll(origin, direction, distance, DefaultRaycastLayers, NegInf, Inf);
        public static RaycastHit2D[] RaycastAll(Vector2 origin, Vector2 direction, float distance, int layerMask) => RaycastAll(origin, direction, distance, layerMask, NegInf, Inf);
        public static RaycastHit2D[] RaycastAll(Vector2 origin, Vector2 direction, float distance, int layerMask, float minDepth) => RaycastAll(origin, direction, distance, layerMask, minDepth, Inf);
        public static RaycastHit2D[] RaycastAll(Vector2 origin, Vector2 direction, float distance, int layerMask, float minDepth, float maxDepth) => RaycastList(origin, direction, distance, Legacy(layerMask, minDepth, maxDepth)).ToArray();
        public static int RaycastNonAlloc(Vector2 origin, Vector2 direction, RaycastHit2D[] results) => RaycastNonAlloc(origin, direction, results, Inf, DefaultRaycastLayers, NegInf, Inf);
        public static int RaycastNonAlloc(Vector2 origin, Vector2 direction, RaycastHit2D[] results, float distance) => RaycastNonAlloc(origin, direction, results, distance, DefaultRaycastLayers, NegInf, Inf);
        public static int RaycastNonAlloc(Vector2 origin, Vector2 direction, RaycastHit2D[] results, float distance, int layerMask) => RaycastNonAlloc(origin, direction, results, distance, layerMask, NegInf, Inf);
        public static int RaycastNonAlloc(Vector2 origin, Vector2 direction, RaycastHit2D[] results, float distance, int layerMask, float minDepth) => RaycastNonAlloc(origin, direction, results, distance, layerMask, minDepth, Inf);
        public static int RaycastNonAlloc(Vector2 origin, Vector2 direction, RaycastHit2D[] results, float distance, int layerMask, float minDepth, float maxDepth) => Copy(RaycastList(origin, direction, distance, Legacy(layerMask, minDepth, maxDepth)), results);

        // ------------------------------------------------------------------ circle casts

        public static RaycastHit2D CircleCast(Vector2 origin, float radius, Vector2 direction) => CircleCast(origin, radius, direction, Inf, DefaultRaycastLayers, NegInf, Inf);
        public static RaycastHit2D CircleCast(Vector2 origin, float radius, Vector2 direction, float distance) => CircleCast(origin, radius, direction, distance, DefaultRaycastLayers, NegInf, Inf);
        public static RaycastHit2D CircleCast(Vector2 origin, float radius, Vector2 direction, float distance, int layerMask) => CircleCast(origin, radius, direction, distance, layerMask, NegInf, Inf);
        public static RaycastHit2D CircleCast(Vector2 origin, float radius, Vector2 direction, float distance, int layerMask, float minDepth) => CircleCast(origin, radius, direction, distance, layerMask, minDepth, Inf);
        public static RaycastHit2D CircleCast(Vector2 origin, float radius, Vector2 direction, float distance, int layerMask, float minDepth, float maxDepth) => First(CircleCastList(origin, radius, direction, distance, Legacy(layerMask, minDepth, maxDepth)));
        public static int CircleCast(Vector2 origin, float radius, Vector2 direction, ContactFilter2D contactFilter, RaycastHit2D[] results) => CircleCast(origin, radius, direction, contactFilter, results, Inf);
        public static int CircleCast(Vector2 origin, float radius, Vector2 direction, ContactFilter2D contactFilter, RaycastHit2D[] results, float distance) => Copy(CircleCastList(origin, radius, direction, distance, contactFilter), results);
        public static int CircleCast(Vector2 origin, float radius, Vector2 direction, ContactFilter2D contactFilter, List<RaycastHit2D> results, float distance = float.PositiveInfinity) => Copy(CircleCastList(origin, radius, direction, distance, contactFilter), results);
        public static RaycastHit2D[] CircleCastAll(Vector2 origin, float radius, Vector2 direction) => CircleCastAll(origin, radius, direction, Inf, DefaultRaycastLayers, NegInf, Inf);
        public static RaycastHit2D[] CircleCastAll(Vector2 origin, float radius, Vector2 direction, float distance) => CircleCastAll(origin, radius, direction, distance, DefaultRaycastLayers, NegInf, Inf);
        public static RaycastHit2D[] CircleCastAll(Vector2 origin, float radius, Vector2 direction, float distance, int layerMask) => CircleCastAll(origin, radius, direction, distance, layerMask, NegInf, Inf);
        public static RaycastHit2D[] CircleCastAll(Vector2 origin, float radius, Vector2 direction, float distance, int layerMask, float minDepth) => CircleCastAll(origin, radius, direction, distance, layerMask, minDepth, Inf);
        public static RaycastHit2D[] CircleCastAll(Vector2 origin, float radius, Vector2 direction, float distance, int layerMask, float minDepth, float maxDepth) => CircleCastList(origin, radius, direction, distance, Legacy(layerMask, minDepth, maxDepth)).ToArray();
        public static int CircleCastNonAlloc(Vector2 origin, float radius, Vector2 direction, RaycastHit2D[] results) => CircleCastNonAlloc(origin, radius, direction, results, Inf, DefaultRaycastLayers, NegInf, Inf);
        public static int CircleCastNonAlloc(Vector2 origin, float radius, Vector2 direction, RaycastHit2D[] results, float distance) => CircleCastNonAlloc(origin, radius, direction, results, distance, DefaultRaycastLayers, NegInf, Inf);
        public static int CircleCastNonAlloc(Vector2 origin, float radius, Vector2 direction, RaycastHit2D[] results, float distance, int layerMask) => CircleCastNonAlloc(origin, radius, direction, results, distance, layerMask, NegInf, Inf);
        public static int CircleCastNonAlloc(Vector2 origin, float radius, Vector2 direction, RaycastHit2D[] results, float distance, int layerMask, float minDepth) => CircleCastNonAlloc(origin, radius, direction, results, distance, layerMask, minDepth, Inf);
        public static int CircleCastNonAlloc(Vector2 origin, float radius, Vector2 direction, RaycastHit2D[] results, float distance, int layerMask, float minDepth, float maxDepth) => Copy(CircleCastList(origin, radius, direction, distance, Legacy(layerMask, minDepth, maxDepth)), results);

        // ------------------------------------------------------------------ box casts

        public static RaycastHit2D BoxCast(Vector2 origin, Vector2 size, float angle, Vector2 direction) => BoxCast(origin, size, angle, direction, Inf, DefaultRaycastLayers, NegInf, Inf);
        public static RaycastHit2D BoxCast(Vector2 origin, Vector2 size, float angle, Vector2 direction, float distance) => BoxCast(origin, size, angle, direction, distance, DefaultRaycastLayers, NegInf, Inf);
        public static RaycastHit2D BoxCast(Vector2 origin, Vector2 size, float angle, Vector2 direction, float distance, int layerMask) => BoxCast(origin, size, angle, direction, distance, layerMask, NegInf, Inf);
        public static RaycastHit2D BoxCast(Vector2 origin, Vector2 size, float angle, Vector2 direction, float distance, int layerMask, float minDepth) => BoxCast(origin, size, angle, direction, distance, layerMask, minDepth, Inf);
        public static RaycastHit2D BoxCast(Vector2 origin, Vector2 size, float angle, Vector2 direction, float distance, int layerMask, float minDepth, float maxDepth) => First(BoxCastList(origin, size, angle, direction, distance, Legacy(layerMask, minDepth, maxDepth)));
        public static int BoxCast(Vector2 origin, Vector2 size, float angle, Vector2 direction, ContactFilter2D contactFilter, RaycastHit2D[] results) => BoxCast(origin, size, angle, direction, contactFilter, results, Inf);
        public static int BoxCast(Vector2 origin, Vector2 size, float angle, Vector2 direction, ContactFilter2D contactFilter, RaycastHit2D[] results, float distance) => Copy(BoxCastList(origin, size, angle, direction, distance, contactFilter), results);
        public static int BoxCast(Vector2 origin, Vector2 size, float angle, Vector2 direction, ContactFilter2D contactFilter, List<RaycastHit2D> results, float distance = float.PositiveInfinity) => Copy(BoxCastList(origin, size, angle, direction, distance, contactFilter), results);
        public static RaycastHit2D[] BoxCastAll(Vector2 origin, Vector2 size, float angle, Vector2 direction) => BoxCastAll(origin, size, angle, direction, Inf, DefaultRaycastLayers, NegInf, Inf);
        public static RaycastHit2D[] BoxCastAll(Vector2 origin, Vector2 size, float angle, Vector2 direction, float distance) => BoxCastAll(origin, size, angle, direction, distance, DefaultRaycastLayers, NegInf, Inf);
        public static RaycastHit2D[] BoxCastAll(Vector2 origin, Vector2 size, float angle, Vector2 direction, float distance, int layerMask) => BoxCastAll(origin, size, angle, direction, distance, layerMask, NegInf, Inf);
        public static RaycastHit2D[] BoxCastAll(Vector2 origin, Vector2 size, float angle, Vector2 direction, float distance, int layerMask, float minDepth) => BoxCastAll(origin, size, angle, direction, distance, layerMask, minDepth, Inf);
        public static RaycastHit2D[] BoxCastAll(Vector2 origin, Vector2 size, float angle, Vector2 direction, float distance, int layerMask, float minDepth, float maxDepth) => BoxCastList(origin, size, angle, direction, distance, Legacy(layerMask, minDepth, maxDepth)).ToArray();
        public static int BoxCastNonAlloc(Vector2 origin, Vector2 size, float angle, Vector2 direction, RaycastHit2D[] results) => BoxCastNonAlloc(origin, size, angle, direction, results, Inf, DefaultRaycastLayers, NegInf, Inf);
        public static int BoxCastNonAlloc(Vector2 origin, Vector2 size, float angle, Vector2 direction, RaycastHit2D[] results, float distance) => BoxCastNonAlloc(origin, size, angle, direction, results, distance, DefaultRaycastLayers, NegInf, Inf);
        public static int BoxCastNonAlloc(Vector2 origin, Vector2 size, float angle, Vector2 direction, RaycastHit2D[] results, float distance, int layerMask) => BoxCastNonAlloc(origin, size, angle, direction, results, distance, layerMask, NegInf, Inf);
        public static int BoxCastNonAlloc(Vector2 origin, Vector2 size, float angle, Vector2 direction, RaycastHit2D[] results, float distance, int layerMask, float minDepth) => BoxCastNonAlloc(origin, size, angle, direction, results, distance, layerMask, minDepth, Inf);
        public static int BoxCastNonAlloc(Vector2 origin, Vector2 size, float angle, Vector2 direction, RaycastHit2D[] results, float distance, int layerMask, float minDepth, float maxDepth) => Copy(BoxCastList(origin, size, angle, direction, distance, Legacy(layerMask, minDepth, maxDepth)), results);

        // ------------------------------------------------------------------ capsule casts

        public static RaycastHit2D CapsuleCast(Vector2 origin, Vector2 size, CapsuleDirection2D capsuleDirection, float angle, Vector2 direction) => CapsuleCast(origin, size, capsuleDirection, angle, direction, Inf, DefaultRaycastLayers, NegInf, Inf);
        public static RaycastHit2D CapsuleCast(Vector2 origin, Vector2 size, CapsuleDirection2D capsuleDirection, float angle, Vector2 direction, float distance) => CapsuleCast(origin, size, capsuleDirection, angle, direction, distance, DefaultRaycastLayers, NegInf, Inf);
        public static RaycastHit2D CapsuleCast(Vector2 origin, Vector2 size, CapsuleDirection2D capsuleDirection, float angle, Vector2 direction, float distance, int layerMask) => CapsuleCast(origin, size, capsuleDirection, angle, direction, distance, layerMask, NegInf, Inf);
        public static RaycastHit2D CapsuleCast(Vector2 origin, Vector2 size, CapsuleDirection2D capsuleDirection, float angle, Vector2 direction, float distance, int layerMask, float minDepth) => CapsuleCast(origin, size, capsuleDirection, angle, direction, distance, layerMask, minDepth, Inf);
        public static RaycastHit2D CapsuleCast(Vector2 origin, Vector2 size, CapsuleDirection2D capsuleDirection, float angle, Vector2 direction, float distance, int layerMask, float minDepth, float maxDepth) => First(CapsuleCastList(origin, size, capsuleDirection, angle, direction, distance, Legacy(layerMask, minDepth, maxDepth)));
        public static int CapsuleCast(Vector2 origin, Vector2 size, CapsuleDirection2D capsuleDirection, float angle, Vector2 direction, ContactFilter2D contactFilter, RaycastHit2D[] results) => CapsuleCast(origin, size, capsuleDirection, angle, direction, contactFilter, results, Inf);
        public static int CapsuleCast(Vector2 origin, Vector2 size, CapsuleDirection2D capsuleDirection, float angle, Vector2 direction, ContactFilter2D contactFilter, RaycastHit2D[] results, float distance) => Copy(CapsuleCastList(origin, size, capsuleDirection, angle, direction, distance, contactFilter), results);
        public static int CapsuleCast(Vector2 origin, Vector2 size, CapsuleDirection2D capsuleDirection, float angle, Vector2 direction, ContactFilter2D contactFilter, List<RaycastHit2D> results, float distance = float.PositiveInfinity) => Copy(CapsuleCastList(origin, size, capsuleDirection, angle, direction, distance, contactFilter), results);
        public static RaycastHit2D[] CapsuleCastAll(Vector2 origin, Vector2 size, CapsuleDirection2D capsuleDirection, float angle, Vector2 direction) => CapsuleCastAll(origin, size, capsuleDirection, angle, direction, Inf, DefaultRaycastLayers, NegInf, Inf);
        public static RaycastHit2D[] CapsuleCastAll(Vector2 origin, Vector2 size, CapsuleDirection2D capsuleDirection, float angle, Vector2 direction, float distance) => CapsuleCastAll(origin, size, capsuleDirection, angle, direction, distance, DefaultRaycastLayers, NegInf, Inf);
        public static RaycastHit2D[] CapsuleCastAll(Vector2 origin, Vector2 size, CapsuleDirection2D capsuleDirection, float angle, Vector2 direction, float distance, int layerMask) => CapsuleCastAll(origin, size, capsuleDirection, angle, direction, distance, layerMask, NegInf, Inf);
        public static RaycastHit2D[] CapsuleCastAll(Vector2 origin, Vector2 size, CapsuleDirection2D capsuleDirection, float angle, Vector2 direction, float distance, int layerMask, float minDepth) => CapsuleCastAll(origin, size, capsuleDirection, angle, direction, distance, layerMask, minDepth, Inf);
        public static RaycastHit2D[] CapsuleCastAll(Vector2 origin, Vector2 size, CapsuleDirection2D capsuleDirection, float angle, Vector2 direction, float distance, int layerMask, float minDepth, float maxDepth) => CapsuleCastList(origin, size, capsuleDirection, angle, direction, distance, Legacy(layerMask, minDepth, maxDepth)).ToArray();
        public static int CapsuleCastNonAlloc(Vector2 origin, Vector2 size, CapsuleDirection2D capsuleDirection, float angle, Vector2 direction, RaycastHit2D[] results) => CapsuleCastNonAlloc(origin, size, capsuleDirection, angle, direction, results, Inf, DefaultRaycastLayers, NegInf, Inf);
        public static int CapsuleCastNonAlloc(Vector2 origin, Vector2 size, CapsuleDirection2D capsuleDirection, float angle, Vector2 direction, RaycastHit2D[] results, float distance) => CapsuleCastNonAlloc(origin, size, capsuleDirection, angle, direction, results, distance, DefaultRaycastLayers, NegInf, Inf);
        public static int CapsuleCastNonAlloc(Vector2 origin, Vector2 size, CapsuleDirection2D capsuleDirection, float angle, Vector2 direction, RaycastHit2D[] results, float distance, int layerMask) => CapsuleCastNonAlloc(origin, size, capsuleDirection, angle, direction, results, distance, layerMask, NegInf, Inf);
        public static int CapsuleCastNonAlloc(Vector2 origin, Vector2 size, CapsuleDirection2D capsuleDirection, float angle, Vector2 direction, RaycastHit2D[] results, float distance, int layerMask, float minDepth) => CapsuleCastNonAlloc(origin, size, capsuleDirection, angle, direction, results, distance, layerMask, minDepth, Inf);
        public static int CapsuleCastNonAlloc(Vector2 origin, Vector2 size, CapsuleDirection2D capsuleDirection, float angle, Vector2 direction, RaycastHit2D[] results, float distance, int layerMask, float minDepth, float maxDepth) => Copy(CapsuleCastList(origin, size, capsuleDirection, angle, direction, distance, Legacy(layerMask, minDepth, maxDepth)), results);

        // ------------------------------------------------------------------ 3D ray intersection

        public static RaycastHit2D GetRayIntersection(Ray ray) => GetRayIntersection(ray, Inf, DefaultRaycastLayers);
        public static RaycastHit2D GetRayIntersection(Ray ray, float distance) => GetRayIntersection(ray, distance, DefaultRaycastLayers);
        public static RaycastHit2D GetRayIntersection(Ray ray, float distance, int layerMask = -5) => First(Q.GetRayIntersection(ray, distance, layerMask));
        public static int GetRayIntersection(Ray ray, float distance, List<RaycastHit2D> results, int layerMask = -5) => Copy(Q.GetRayIntersection(ray, distance, layerMask), results);
        public static RaycastHit2D[] GetRayIntersectionAll(Ray ray) => GetRayIntersectionAll(ray, Inf, DefaultRaycastLayers);
        public static RaycastHit2D[] GetRayIntersectionAll(Ray ray, float distance) => GetRayIntersectionAll(ray, distance, DefaultRaycastLayers);
        public static RaycastHit2D[] GetRayIntersectionAll(Ray ray, float distance, int layerMask = -5) => Q.GetRayIntersection(ray, distance, layerMask).ToArray();
        public static int GetRayIntersectionNonAlloc(Ray ray, RaycastHit2D[] results) => GetRayIntersectionNonAlloc(ray, results, Inf, DefaultRaycastLayers);
        public static int GetRayIntersectionNonAlloc(Ray ray, RaycastHit2D[] results, float distance) => GetRayIntersectionNonAlloc(ray, results, distance, DefaultRaycastLayers);
        public static int GetRayIntersectionNonAlloc(Ray ray, RaycastHit2D[] results, float distance, int layerMask = -5) => Copy(Q.GetRayIntersection(ray, distance, layerMask), results);

        // ------------------------------------------------------------------ overlaps

        public static Collider2D OverlapPoint(Vector2 point) => OverlapPoint(point, DefaultRaycastLayers, NegInf, Inf);
        public static Collider2D OverlapPoint(Vector2 point, int layerMask) => OverlapPoint(point, layerMask, NegInf, Inf);
        public static Collider2D OverlapPoint(Vector2 point, int layerMask, float minDepth) => OverlapPoint(point, layerMask, minDepth, Inf);
        public static Collider2D OverlapPoint(Vector2 point, int layerMask, float minDepth, float maxDepth) => First(OverlapPointList(point, Legacy(layerMask, minDepth, maxDepth)));
        public static int OverlapPoint(Vector2 point, ContactFilter2D contactFilter, Collider2D[] results) => Copy(OverlapPointList(point, contactFilter), results);
        public static int OverlapPoint(Vector2 point, ContactFilter2D contactFilter, List<Collider2D> results) => Copy(OverlapPointList(point, contactFilter), results);
        public static Collider2D[] OverlapPointAll(Vector2 point) => OverlapPointAll(point, DefaultRaycastLayers, NegInf, Inf);
        public static Collider2D[] OverlapPointAll(Vector2 point, int layerMask) => OverlapPointAll(point, layerMask, NegInf, Inf);
        public static Collider2D[] OverlapPointAll(Vector2 point, int layerMask, float minDepth) => OverlapPointAll(point, layerMask, minDepth, Inf);
        public static Collider2D[] OverlapPointAll(Vector2 point, int layerMask, float minDepth, float maxDepth) => OverlapPointList(point, Legacy(layerMask, minDepth, maxDepth)).ToArray();
        public static int OverlapPointNonAlloc(Vector2 point, Collider2D[] results) => OverlapPointNonAlloc(point, results, DefaultRaycastLayers, NegInf, Inf);
        public static int OverlapPointNonAlloc(Vector2 point, Collider2D[] results, int layerMask) => OverlapPointNonAlloc(point, results, layerMask, NegInf, Inf);
        public static int OverlapPointNonAlloc(Vector2 point, Collider2D[] results, int layerMask, float minDepth) => OverlapPointNonAlloc(point, results, layerMask, minDepth, Inf);
        public static int OverlapPointNonAlloc(Vector2 point, Collider2D[] results, int layerMask, float minDepth, float maxDepth) => Copy(OverlapPointList(point, Legacy(layerMask, minDepth, maxDepth)), results);

        public static Collider2D OverlapCircle(Vector2 point, float radius) => OverlapCircle(point, radius, DefaultRaycastLayers, NegInf, Inf);
        public static Collider2D OverlapCircle(Vector2 point, float radius, int layerMask) => OverlapCircle(point, radius, layerMask, NegInf, Inf);
        public static Collider2D OverlapCircle(Vector2 point, float radius, int layerMask, float minDepth) => OverlapCircle(point, radius, layerMask, minDepth, Inf);
        public static Collider2D OverlapCircle(Vector2 point, float radius, int layerMask, float minDepth, float maxDepth) => First(OverlapCircleList(point, radius, Legacy(layerMask, minDepth, maxDepth)));
        public static int OverlapCircle(Vector2 point, float radius, ContactFilter2D contactFilter, Collider2D[] results) => Copy(OverlapCircleList(point, radius, contactFilter), results);
        public static int OverlapCircle(Vector2 point, float radius, ContactFilter2D contactFilter, List<Collider2D> results) => Copy(OverlapCircleList(point, radius, contactFilter), results);
        public static Collider2D[] OverlapCircleAll(Vector2 point, float radius) => OverlapCircleAll(point, radius, DefaultRaycastLayers, NegInf, Inf);
        public static Collider2D[] OverlapCircleAll(Vector2 point, float radius, int layerMask) => OverlapCircleAll(point, radius, layerMask, NegInf, Inf);
        public static Collider2D[] OverlapCircleAll(Vector2 point, float radius, int layerMask, float minDepth) => OverlapCircleAll(point, radius, layerMask, minDepth, Inf);
        public static Collider2D[] OverlapCircleAll(Vector2 point, float radius, int layerMask, float minDepth, float maxDepth) => OverlapCircleList(point, radius, Legacy(layerMask, minDepth, maxDepth)).ToArray();
        public static int OverlapCircleNonAlloc(Vector2 point, float radius, Collider2D[] results) => OverlapCircleNonAlloc(point, radius, results, DefaultRaycastLayers, NegInf, Inf);
        public static int OverlapCircleNonAlloc(Vector2 point, float radius, Collider2D[] results, int layerMask) => OverlapCircleNonAlloc(point, radius, results, layerMask, NegInf, Inf);
        public static int OverlapCircleNonAlloc(Vector2 point, float radius, Collider2D[] results, int layerMask, float minDepth) => OverlapCircleNonAlloc(point, radius, results, layerMask, minDepth, Inf);
        public static int OverlapCircleNonAlloc(Vector2 point, float radius, Collider2D[] results, int layerMask, float minDepth, float maxDepth) => Copy(OverlapCircleList(point, radius, Legacy(layerMask, minDepth, maxDepth)), results);

        public static Collider2D OverlapBox(Vector2 point, Vector2 size, float angle) => OverlapBox(point, size, angle, DefaultRaycastLayers, NegInf, Inf);
        public static Collider2D OverlapBox(Vector2 point, Vector2 size, float angle, int layerMask) => OverlapBox(point, size, angle, layerMask, NegInf, Inf);
        public static Collider2D OverlapBox(Vector2 point, Vector2 size, float angle, int layerMask, float minDepth) => OverlapBox(point, size, angle, layerMask, minDepth, Inf);
        public static Collider2D OverlapBox(Vector2 point, Vector2 size, float angle, int layerMask, float minDepth, float maxDepth) => First(OverlapBoxList(point, size, angle, Legacy(layerMask, minDepth, maxDepth)));
        public static int OverlapBox(Vector2 point, Vector2 size, float angle, ContactFilter2D contactFilter, Collider2D[] results) => Copy(OverlapBoxList(point, size, angle, contactFilter), results);
        public static int OverlapBox(Vector2 point, Vector2 size, float angle, ContactFilter2D contactFilter, List<Collider2D> results) => Copy(OverlapBoxList(point, size, angle, contactFilter), results);
        public static Collider2D[] OverlapBoxAll(Vector2 point, Vector2 size, float angle) => OverlapBoxAll(point, size, angle, DefaultRaycastLayers, NegInf, Inf);
        public static Collider2D[] OverlapBoxAll(Vector2 point, Vector2 size, float angle, int layerMask) => OverlapBoxAll(point, size, angle, layerMask, NegInf, Inf);
        public static Collider2D[] OverlapBoxAll(Vector2 point, Vector2 size, float angle, int layerMask, float minDepth) => OverlapBoxAll(point, size, angle, layerMask, minDepth, Inf);
        public static Collider2D[] OverlapBoxAll(Vector2 point, Vector2 size, float angle, int layerMask, float minDepth, float maxDepth) => OverlapBoxList(point, size, angle, Legacy(layerMask, minDepth, maxDepth)).ToArray();
        public static int OverlapBoxNonAlloc(Vector2 point, Vector2 size, float angle, Collider2D[] results) => OverlapBoxNonAlloc(point, size, angle, results, DefaultRaycastLayers, NegInf, Inf);
        public static int OverlapBoxNonAlloc(Vector2 point, Vector2 size, float angle, Collider2D[] results, int layerMask) => OverlapBoxNonAlloc(point, size, angle, results, layerMask, NegInf, Inf);
        public static int OverlapBoxNonAlloc(Vector2 point, Vector2 size, float angle, Collider2D[] results, int layerMask, float minDepth) => OverlapBoxNonAlloc(point, size, angle, results, layerMask, minDepth, Inf);
        public static int OverlapBoxNonAlloc(Vector2 point, Vector2 size, float angle, Collider2D[] results, int layerMask, float minDepth, float maxDepth) => Copy(OverlapBoxList(point, size, angle, Legacy(layerMask, minDepth, maxDepth)), results);

        public static Collider2D OverlapArea(Vector2 pointA, Vector2 pointB) => OverlapArea(pointA, pointB, DefaultRaycastLayers, NegInf, Inf);
        public static Collider2D OverlapArea(Vector2 pointA, Vector2 pointB, int layerMask) => OverlapArea(pointA, pointB, layerMask, NegInf, Inf);
        public static Collider2D OverlapArea(Vector2 pointA, Vector2 pointB, int layerMask, float minDepth) => OverlapArea(pointA, pointB, layerMask, minDepth, Inf);
        public static Collider2D OverlapArea(Vector2 pointA, Vector2 pointB, int layerMask, float minDepth, float maxDepth) => First(OverlapAreaList(pointA, pointB, Legacy(layerMask, minDepth, maxDepth)));
        public static int OverlapArea(Vector2 pointA, Vector2 pointB, ContactFilter2D contactFilter, Collider2D[] results) => Copy(OverlapAreaList(pointA, pointB, contactFilter), results);
        public static int OverlapArea(Vector2 pointA, Vector2 pointB, ContactFilter2D contactFilter, List<Collider2D> results) => Copy(OverlapAreaList(pointA, pointB, contactFilter), results);
        public static Collider2D[] OverlapAreaAll(Vector2 pointA, Vector2 pointB) => OverlapAreaAll(pointA, pointB, DefaultRaycastLayers, NegInf, Inf);
        public static Collider2D[] OverlapAreaAll(Vector2 pointA, Vector2 pointB, int layerMask) => OverlapAreaAll(pointA, pointB, layerMask, NegInf, Inf);
        public static Collider2D[] OverlapAreaAll(Vector2 pointA, Vector2 pointB, int layerMask, float minDepth) => OverlapAreaAll(pointA, pointB, layerMask, minDepth, Inf);
        public static Collider2D[] OverlapAreaAll(Vector2 pointA, Vector2 pointB, int layerMask, float minDepth, float maxDepth) => OverlapAreaList(pointA, pointB, Legacy(layerMask, minDepth, maxDepth)).ToArray();
        public static int OverlapAreaNonAlloc(Vector2 pointA, Vector2 pointB, Collider2D[] results) => OverlapAreaNonAlloc(pointA, pointB, results, DefaultRaycastLayers, NegInf, Inf);
        public static int OverlapAreaNonAlloc(Vector2 pointA, Vector2 pointB, Collider2D[] results, int layerMask) => OverlapAreaNonAlloc(pointA, pointB, results, layerMask, NegInf, Inf);
        public static int OverlapAreaNonAlloc(Vector2 pointA, Vector2 pointB, Collider2D[] results, int layerMask, float minDepth) => OverlapAreaNonAlloc(pointA, pointB, results, layerMask, minDepth, Inf);
        public static int OverlapAreaNonAlloc(Vector2 pointA, Vector2 pointB, Collider2D[] results, int layerMask, float minDepth, float maxDepth) => Copy(OverlapAreaList(pointA, pointB, Legacy(layerMask, minDepth, maxDepth)), results);

        public static Collider2D OverlapCapsule(Vector2 point, Vector2 size, CapsuleDirection2D direction, float angle) => OverlapCapsule(point, size, direction, angle, DefaultRaycastLayers, NegInf, Inf);
        public static Collider2D OverlapCapsule(Vector2 point, Vector2 size, CapsuleDirection2D direction, float angle, int layerMask) => OverlapCapsule(point, size, direction, angle, layerMask, NegInf, Inf);
        public static Collider2D OverlapCapsule(Vector2 point, Vector2 size, CapsuleDirection2D direction, float angle, int layerMask, float minDepth) => OverlapCapsule(point, size, direction, angle, layerMask, minDepth, Inf);
        public static Collider2D OverlapCapsule(Vector2 point, Vector2 size, CapsuleDirection2D direction, float angle, int layerMask, float minDepth, float maxDepth) => First(OverlapCapsuleList(point, size, direction, angle, Legacy(layerMask, minDepth, maxDepth)));
        public static int OverlapCapsule(Vector2 point, Vector2 size, CapsuleDirection2D direction, float angle, ContactFilter2D contactFilter, Collider2D[] results) => Copy(OverlapCapsuleList(point, size, direction, angle, contactFilter), results);
        public static int OverlapCapsule(Vector2 point, Vector2 size, CapsuleDirection2D direction, float angle, ContactFilter2D contactFilter, List<Collider2D> results) => Copy(OverlapCapsuleList(point, size, direction, angle, contactFilter), results);
        public static Collider2D[] OverlapCapsuleAll(Vector2 point, Vector2 size, CapsuleDirection2D direction, float angle) => OverlapCapsuleAll(point, size, direction, angle, DefaultRaycastLayers, NegInf, Inf);
        public static Collider2D[] OverlapCapsuleAll(Vector2 point, Vector2 size, CapsuleDirection2D direction, float angle, int layerMask) => OverlapCapsuleAll(point, size, direction, angle, layerMask, NegInf, Inf);
        public static Collider2D[] OverlapCapsuleAll(Vector2 point, Vector2 size, CapsuleDirection2D direction, float angle, int layerMask, float minDepth) => OverlapCapsuleAll(point, size, direction, angle, layerMask, minDepth, Inf);
        public static Collider2D[] OverlapCapsuleAll(Vector2 point, Vector2 size, CapsuleDirection2D direction, float angle, int layerMask, float minDepth, float maxDepth) => OverlapCapsuleList(point, size, direction, angle, Legacy(layerMask, minDepth, maxDepth)).ToArray();
        public static int OverlapCapsuleNonAlloc(Vector2 point, Vector2 size, CapsuleDirection2D direction, float angle, Collider2D[] results) => OverlapCapsuleNonAlloc(point, size, direction, angle, results, DefaultRaycastLayers, NegInf, Inf);
        public static int OverlapCapsuleNonAlloc(Vector2 point, Vector2 size, CapsuleDirection2D direction, float angle, Collider2D[] results, int layerMask) => OverlapCapsuleNonAlloc(point, size, direction, angle, results, layerMask, NegInf, Inf);
        public static int OverlapCapsuleNonAlloc(Vector2 point, Vector2 size, CapsuleDirection2D direction, float angle, Collider2D[] results, int layerMask, float minDepth) => OverlapCapsuleNonAlloc(point, size, direction, angle, results, layerMask, minDepth, Inf);
        public static int OverlapCapsuleNonAlloc(Vector2 point, Vector2 size, CapsuleDirection2D direction, float angle, Collider2D[] results, int layerMask, float minDepth, float maxDepth) => Copy(OverlapCapsuleList(point, size, direction, angle, Legacy(layerMask, minDepth, maxDepth)), results);

        public static int OverlapCollider(Collider2D collider, ContactFilter2D contactFilter, Collider2D[] results) =>
            Copy(Q.OverlapColliders(new[] { collider ?? throw new ArgumentNullException(nameof(collider)) }, null, contactFilter, null), results);

        public static int OverlapCollider(Collider2D collider, ContactFilter2D contactFilter, List<Collider2D> results) =>
            Copy(Q.OverlapColliders(new[] { collider ?? throw new ArgumentNullException(nameof(collider)) }, null, contactFilter, null), results);

        public static int OverlapCollider(Collider2D collider, List<Collider2D> results) => OverlapCollider(collider, new ContactFilter2D().NoFilter(), results);
    }

    /// <summary>The 2D physics world of a scene. Unbound has one world: every scene shares it.</summary>
    public partial struct PhysicsScene2D : IEquatable<PhysicsScene2D>
    {
        int m_SceneHandle;

        public override string ToString() => $"({m_SceneHandle})";

        public static bool operator ==(PhysicsScene2D lhs, PhysicsScene2D rhs) => lhs.m_SceneHandle == rhs.m_SceneHandle;

        public static bool operator !=(PhysicsScene2D lhs, PhysicsScene2D rhs) => lhs.m_SceneHandle != rhs.m_SceneHandle;

        public override int GetHashCode() => m_SceneHandle;

        public override bool Equals(object other) => other is PhysicsScene2D scene && scene.m_SceneHandle == m_SceneHandle;

        public bool Equals(PhysicsScene2D other) => other.m_SceneHandle == m_SceneHandle;

        public bool IsValid() => m_SceneHandle == 0;

        public bool IsEmpty() => W.Rigidbodies.Count == 0 && W.Colliders.Count == 0;

        public bool Simulate(float deltaTime) => Physics2D.Simulate(deltaTime);

        public bool Simulate(float deltaTime, int simulationLayers = -1) => Physics2D.Simulate(deltaTime, simulationLayers);

        static ContactFilter2D Legacy(int layerMask) => ContactFilter2D.CreateLegacyFilter(layerMask, float.NegativeInfinity, float.PositiveInfinity);

        static RaycastHit2D First(List<RaycastHit2D> hits) => hits.Count > 0 ? hits[0] : default;

        static Collider2D First(List<Collider2D> colliders) => colliders.Count > 0 ? colliders[0] : null;

        static int Copy<T>(List<T> found, T[] results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            int count = Math.Min(found.Count, results.Length);
            for (int i = 0; i < count; i++) results[i] = found[i];
            return count;
        }

        static int Copy<T>(List<T> found, List<T> results)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            results.Clear();
            results.AddRange(found);
            return results.Count;
        }

        public RaycastHit2D Linecast(Vector2 start, Vector2 end, int layerMask = -5) => First(Physics2D.LinecastList(start, end, Legacy(layerMask)));
        public RaycastHit2D Linecast(Vector2 start, Vector2 end, ContactFilter2D contactFilter) => First(Physics2D.LinecastList(start, end, contactFilter));
        public int Linecast(Vector2 start, Vector2 end, RaycastHit2D[] results, int layerMask = -5) => Copy(Physics2D.LinecastList(start, end, Legacy(layerMask)), results);
        public int Linecast(Vector2 start, Vector2 end, ContactFilter2D contactFilter, RaycastHit2D[] results) => Copy(Physics2D.LinecastList(start, end, contactFilter), results);
        public int Linecast(Vector2 start, Vector2 end, ContactFilter2D contactFilter, List<RaycastHit2D> results) => Copy(Physics2D.LinecastList(start, end, contactFilter), results);
        public RaycastHit2D Raycast(Vector2 origin, Vector2 direction, float distance, int layerMask = -5) => First(Physics2D.RaycastList(origin, direction, distance, Legacy(layerMask)));
        public RaycastHit2D Raycast(Vector2 origin, Vector2 direction, float distance, ContactFilter2D contactFilter) => First(Physics2D.RaycastList(origin, direction, distance, contactFilter));
        public int Raycast(Vector2 origin, Vector2 direction, float distance, RaycastHit2D[] results, int layerMask = -5) => Copy(Physics2D.RaycastList(origin, direction, distance, Legacy(layerMask)), results);
        public int Raycast(Vector2 origin, Vector2 direction, float distance, ContactFilter2D contactFilter, RaycastHit2D[] results) => Copy(Physics2D.RaycastList(origin, direction, distance, contactFilter), results);
        public int Raycast(Vector2 origin, Vector2 direction, float distance, ContactFilter2D contactFilter, List<RaycastHit2D> results) => Copy(Physics2D.RaycastList(origin, direction, distance, contactFilter), results);
        public RaycastHit2D CircleCast(Vector2 origin, float radius, Vector2 direction, float distance, int layerMask = -5) => First(Physics2D.CircleCastList(origin, radius, direction, distance, Legacy(layerMask)));
        public RaycastHit2D CircleCast(Vector2 origin, float radius, Vector2 direction, float distance, ContactFilter2D contactFilter) => First(Physics2D.CircleCastList(origin, radius, direction, distance, contactFilter));
        public int CircleCast(Vector2 origin, float radius, Vector2 direction, float distance, RaycastHit2D[] results, int layerMask = -5) => Copy(Physics2D.CircleCastList(origin, radius, direction, distance, Legacy(layerMask)), results);
        public int CircleCast(Vector2 origin, float radius, Vector2 direction, float distance, ContactFilter2D contactFilter, RaycastHit2D[] results) => Copy(Physics2D.CircleCastList(origin, radius, direction, distance, contactFilter), results);
        public int CircleCast(Vector2 origin, float radius, Vector2 direction, float distance, ContactFilter2D contactFilter, List<RaycastHit2D> results) => Copy(Physics2D.CircleCastList(origin, radius, direction, distance, contactFilter), results);
        public RaycastHit2D BoxCast(Vector2 origin, Vector2 size, float angle, Vector2 direction, float distance, int layerMask = -5) => First(Physics2D.BoxCastList(origin, size, angle, direction, distance, Legacy(layerMask)));
        public RaycastHit2D BoxCast(Vector2 origin, Vector2 size, float angle, Vector2 direction, float distance, ContactFilter2D contactFilter) => First(Physics2D.BoxCastList(origin, size, angle, direction, distance, contactFilter));
        public int BoxCast(Vector2 origin, Vector2 size, float angle, Vector2 direction, float distance, RaycastHit2D[] results, int layerMask = -5) => Copy(Physics2D.BoxCastList(origin, size, angle, direction, distance, Legacy(layerMask)), results);
        public int BoxCast(Vector2 origin, Vector2 size, float angle, Vector2 direction, float distance, ContactFilter2D contactFilter, RaycastHit2D[] results) => Copy(Physics2D.BoxCastList(origin, size, angle, direction, distance, contactFilter), results);
        public int BoxCast(Vector2 origin, Vector2 size, float angle, Vector2 direction, float distance, ContactFilter2D contactFilter, List<RaycastHit2D> results) => Copy(Physics2D.BoxCastList(origin, size, angle, direction, distance, contactFilter), results);
        public RaycastHit2D CapsuleCast(Vector2 origin, Vector2 size, CapsuleDirection2D capsuleDirection, float angle, Vector2 direction, float distance, int layerMask = -5) => First(Physics2D.CapsuleCastList(origin, size, capsuleDirection, angle, direction, distance, Legacy(layerMask)));
        public RaycastHit2D CapsuleCast(Vector2 origin, Vector2 size, CapsuleDirection2D capsuleDirection, float angle, Vector2 direction, float distance, ContactFilter2D contactFilter) => First(Physics2D.CapsuleCastList(origin, size, capsuleDirection, angle, direction, distance, contactFilter));
        public int CapsuleCast(Vector2 origin, Vector2 size, CapsuleDirection2D capsuleDirection, float angle, Vector2 direction, float distance, RaycastHit2D[] results, int layerMask = -5) => Copy(Physics2D.CapsuleCastList(origin, size, capsuleDirection, angle, direction, distance, Legacy(layerMask)), results);
        public int CapsuleCast(Vector2 origin, Vector2 size, CapsuleDirection2D capsuleDirection, float angle, Vector2 direction, float distance, ContactFilter2D contactFilter, RaycastHit2D[] results) => Copy(Physics2D.CapsuleCastList(origin, size, capsuleDirection, angle, direction, distance, contactFilter), results);
        public int CapsuleCast(Vector2 origin, Vector2 size, CapsuleDirection2D capsuleDirection, float angle, Vector2 direction, float distance, ContactFilter2D contactFilter, List<RaycastHit2D> results) => Copy(Physics2D.CapsuleCastList(origin, size, capsuleDirection, angle, direction, distance, contactFilter), results);
        public RaycastHit2D GetRayIntersection(Ray ray, float distance, int layerMask = -5) => Physics2D.GetRayIntersection(ray, distance, layerMask);
        public int GetRayIntersection(Ray ray, float distance, RaycastHit2D[] results, int layerMask = -5) => Physics2D.GetRayIntersectionNonAlloc(ray, results, distance, layerMask);
        public int GetRayIntersection(Ray ray, float distance, List<RaycastHit2D> results, int layerMask = -5) => Physics2D.GetRayIntersection(ray, distance, results, layerMask);
        public Collider2D OverlapPoint(Vector2 point, int layerMask = -5) => First(Physics2D.OverlapPointList(point, Legacy(layerMask)));
        public Collider2D OverlapPoint(Vector2 point, ContactFilter2D contactFilter) => First(Physics2D.OverlapPointList(point, contactFilter));
        public int OverlapPoint(Vector2 point, Collider2D[] results, int layerMask = -5) => Copy(Physics2D.OverlapPointList(point, Legacy(layerMask)), results);
        public int OverlapPoint(Vector2 point, ContactFilter2D contactFilter, Collider2D[] results) => Copy(Physics2D.OverlapPointList(point, contactFilter), results);
        public int OverlapPoint(Vector2 point, ContactFilter2D contactFilter, List<Collider2D> results) => Copy(Physics2D.OverlapPointList(point, contactFilter), results);
        public Collider2D OverlapCircle(Vector2 point, float radius, int layerMask = -5) => First(Physics2D.OverlapCircleList(point, radius, Legacy(layerMask)));
        public Collider2D OverlapCircle(Vector2 point, float radius, ContactFilter2D contactFilter) => First(Physics2D.OverlapCircleList(point, radius, contactFilter));
        public int OverlapCircle(Vector2 point, float radius, Collider2D[] results, int layerMask = -5) => Copy(Physics2D.OverlapCircleList(point, radius, Legacy(layerMask)), results);
        public int OverlapCircle(Vector2 point, float radius, ContactFilter2D contactFilter, Collider2D[] results) => Copy(Physics2D.OverlapCircleList(point, radius, contactFilter), results);
        public int OverlapCircle(Vector2 point, float radius, ContactFilter2D contactFilter, List<Collider2D> results) => Copy(Physics2D.OverlapCircleList(point, radius, contactFilter), results);
        public Collider2D OverlapBox(Vector2 point, Vector2 size, float angle, int layerMask = -5) => First(Physics2D.OverlapBoxList(point, size, angle, Legacy(layerMask)));
        public Collider2D OverlapBox(Vector2 point, Vector2 size, float angle, ContactFilter2D contactFilter) => First(Physics2D.OverlapBoxList(point, size, angle, contactFilter));
        public int OverlapBox(Vector2 point, Vector2 size, float angle, Collider2D[] results, int layerMask = -5) => Copy(Physics2D.OverlapBoxList(point, size, angle, Legacy(layerMask)), results);
        public int OverlapBox(Vector2 point, Vector2 size, float angle, ContactFilter2D contactFilter, Collider2D[] results) => Copy(Physics2D.OverlapBoxList(point, size, angle, contactFilter), results);
        public int OverlapBox(Vector2 point, Vector2 size, float angle, ContactFilter2D contactFilter, List<Collider2D> results) => Copy(Physics2D.OverlapBoxList(point, size, angle, contactFilter), results);
        public Collider2D OverlapArea(Vector2 pointA, Vector2 pointB, int layerMask = -5) => First(Physics2D.OverlapAreaList(pointA, pointB, Legacy(layerMask)));
        public Collider2D OverlapArea(Vector2 pointA, Vector2 pointB, ContactFilter2D contactFilter) => First(Physics2D.OverlapAreaList(pointA, pointB, contactFilter));
        public int OverlapArea(Vector2 pointA, Vector2 pointB, Collider2D[] results, int layerMask = -5) => Copy(Physics2D.OverlapAreaList(pointA, pointB, Legacy(layerMask)), results);
        public int OverlapArea(Vector2 pointA, Vector2 pointB, ContactFilter2D contactFilter, Collider2D[] results) => Copy(Physics2D.OverlapAreaList(pointA, pointB, contactFilter), results);
        public int OverlapArea(Vector2 pointA, Vector2 pointB, ContactFilter2D contactFilter, List<Collider2D> results) => Copy(Physics2D.OverlapAreaList(pointA, pointB, contactFilter), results);
    }
}
