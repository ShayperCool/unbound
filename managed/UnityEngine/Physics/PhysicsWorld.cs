using System;
using System.Collections.Generic;
using Unbound.Box3D;
using UnityEngine;
using PL = UnityEngine.PlayerLoop;

namespace Unbound.Runtime
{
    /// <summary>
    /// The 3D physics world: a Box3D world (native/box3d, through the unbound_box3d shim) that mirrors
    /// the active Rigidbody and Collider components, as Unity mirrors them into PhysX.
    ///
    /// Each Rigidbody owns a body; each enabled collider adds one shape to the body of the rigidbody
    /// it is attached to (on its GameObject or the nearest parent), in that body's frame, or to a
    /// static body of its own. A shape's collision filter is its layer (category bit) and the layers
    /// that layer collides with (the layer collision matrix, adjusted by include/exclude layers).
    ///
    /// Transform changes reach the bodies when transforms are synced: before every simulation step,
    /// and before queries when Physics.autoSyncTransforms is set. After a step, moved bodies write their
    /// poses back to their transforms, then OnCollision*/OnTrigger* messages are sent.
    ///
    /// The simulation runs in FixedUpdate.PhysicsFixedUpdate (SimulationMode.FixedUpdate), in
    /// PreLateUpdate.PhysicsLateUpdate (Update) or only through Physics.Simulate (Script); interpolated
    /// bodies get their transform poses in PreUpdate.PhysicsUpdate.
    ///
    /// A scene created or loaded with LocalPhysicsMode.Physics3D owns a physics scene of its own (a
    /// separate native world, <see cref="Space"/>): its components are simulated, queried and
    /// reported only there, and only by its PhysicsScene.Simulate. Everything else is in the default
    /// physics scene, whose behaviour is expected to be unchanged (not yet measured).
    /// </summary>
    internal static unsafe class PhysicsWorld
    {
        static bool s_Registered;
        static bool s_Stepping;
        static readonly Dictionary<ulong, Collider> s_ByShape = new Dictionary<ulong, Collider>();
        static readonly Dictionary<ulong, Rigidbody> s_ByActor = new Dictionary<ulong, Rigidbody>();

        // ------------------------------------------------------------------ physics scenes

        /// <summary>
        /// One 3D physics scene (PhysicsScene): the default one, or the local one a scene made with
        /// LocalPhysicsMode.Physics3D owns (6000.0 Scripting API: LocalPhysicsMode, PhysicsScene).
        /// Each is its own native world. Components join the physics scene of their GameObject's
        /// scene; only the default one is simulated automatically.
        /// </summary>
        internal sealed class Space
        {
            internal uint World;
            internal bool StructureDirty;
            internal bool SelectiveContactReports;
            internal bool AttachmentDirty;
            internal readonly List<Rigidbody> Rigidbodies = new List<Rigidbody>();
            internal readonly HashSet<Rigidbody> InitiallyKinematic = new HashSet<Rigidbody>();
            internal readonly List<Collider> Colliders = new List<Collider>();
            internal readonly Dictionary<(PairKey Colliders, bool Trigger), Pair> Pairs = new Dictionary<(PairKey, bool), Pair>();
            internal readonly List<Pair> PairOrder = new List<Pair>();
            internal readonly HashSet<(ulong, ulong)> PublicContactPairs = new HashSet<(ulong, ulong)>();
            /// <summary>The handle of the scene that owns it; 0 for the default physics scene.</summary>
            internal readonly int Scene;
            /// <summary>Its PhysicsScene identity. The default scene is (0, 0) (measured:
            /// contactevents3d); local identities are Unbound's own (Unity's are not measured).</summary>
            internal readonly int Index, Version;
            internal bool Released;

            internal Space(int scene, int index, int version)
            {
                Scene = scene;
                Index = index;
                Version = version;
            }
        }

        static readonly Space s_Default = new Space(0, 0, 0);
        /// <summary>The physics scene the code below works on; the default one except while a
        /// local scene's members are changed, synced, queried or simulated.</summary>
        static Space s_Space = s_Default;
        static readonly List<Space> s_Locals = new List<Space>();
        static readonly Dictionary<int, Space> s_ByScene = new Dictionary<int, Space>();
        static int s_NextLocalIndex = 1;
        /// <summary>Native worlds of physics scenes released during a step, destroyed after it.</summary>
        static readonly List<uint> s_PendingDestroy = new List<uint>();

        static void DestroyPendingWorlds()
        {
            if (s_Stepping || s_PendingDestroy.Count == 0) return;
            var worlds = s_PendingDestroy.ToArray();
            s_PendingDestroy.Clear();
            foreach (var world in worlds) B3.ua3_world_destroy(world);
        }

        // The state of the current physics scene, under the names it had when there was one.
        static uint s_World { get => s_Space.World; set => s_Space.World = value; }
        static bool s_StructureDirty { get => s_Space.StructureDirty; set => s_Space.StructureDirty = value; }
        static bool s_AttachmentDirty { get => s_Space.AttachmentDirty; set => s_Space.AttachmentDirty = value; }
        static List<Rigidbody> s_Rigidbodies => s_Space.Rigidbodies;
        static HashSet<Rigidbody> s_InitiallyKinematic => s_Space.InitiallyKinematic;
        static List<Collider> s_Colliders => s_Space.Colliders;
        static Dictionary<(PairKey Colliders, bool Trigger), Pair> s_Pairs => s_Space.Pairs;
        static List<Pair> s_PairOrder => s_Space.PairOrder;
        static HashSet<(ulong, ulong)> s_PublicContactPairs => s_Space.PublicContactPairs;

        readonly struct Scope : IDisposable
        {
            readonly Space m_Previous;
            internal Scope(Space space)
            {
                m_Previous = s_Space;
                s_Space = space;
            }
            public void Dispose() => s_Space = m_Previous;
        }

        static Scope Enter(Space space) => new Scope(space ?? s_Default);

        internal static Space DefaultSpace => s_Default;

        /// <summary>No physics scene: no world and nothing to hit (an invalid PhysicsScene's queries).</summary>
        internal static readonly Space NoSpace = new Space(-1, -1, -1) { Released = true };

        /// <summary>The physics scene a GameObject's components belong to: its scene's local one,
        /// or the default one.</summary>
        internal static Space SpaceOf(GameObject go) =>
            s_ByScene.Count == 0 || go == null || !s_ByScene.TryGetValue(go.m_Scene.Value, out var space) ? s_Default : space;

        /// <summary>The local physics scene a scene owns, also one already released with the scene's
        /// unload; null when the scene uses the default one.</summary>
        internal static Space LocalOf(int sceneHandle) =>
            s_ByScene.TryGetValue(sceneHandle, out var space) || s_Released.TryGetValue(sceneHandle, out space) ? space : null;

        /// <summary>Released local physics scenes by the scene that owned them.</summary>
        static readonly Dictionary<int, Space> s_Released = new Dictionary<int, Space>();

        /// <summary>A valid physics scene by its PhysicsScene identity; null when there is none.</summary>
        internal static Space Find(int index, int version)
        {
            if (index == 0 && version == 0) return s_Default;
            foreach (var space in s_Locals)
                if (space.Index == index && space.Version == version) return space;
            return null;
        }

        /// <summary>The PhysicsScene a physics scene is seen as.</summary>
        internal static PhysicsScene SceneOf(Space space) => new PhysicsScene(space.Index, space.Version);

        /// <summary>A scene made with LocalPhysicsMode.Physics3D gets its own physics scene. Its
        /// native world is created with the first component that joins it.</summary>
        internal static void CreateLocal(int sceneHandle)
        {
            if (s_ByScene.ContainsKey(sceneHandle)) return;
            var space = new Space(sceneHandle, s_NextLocalIndex++, 1);
            s_Locals.Add(space);
            s_ByScene[sceneHandle] = space;
        }

        /// <summary>The scene that owns a local physics scene is unloaded: the physics scene goes with
        /// it (6000.0 Scripting API, LocalPhysicsMode: "the lifetime of the physics Scene(s) is the
        /// same as the Scene"). Objects moved out of the scene first join their new physics scene;
        /// what is left leaves the simulation.</summary>
        internal static void ReleaseLocal(int sceneHandle)
        {
            if (!s_ByScene.TryGetValue(sceneHandle, out var space)) return;
            if (!s_Stepping) Migrate();
            using (Enter(space))
            {
                foreach (var rb in s_Rigidbodies.ToArray()) Remove(rb);
                foreach (var c in s_Colliders.ToArray()) Remove(c);
                // During a step's callbacks the native reports being delivered are the world's:
                // it goes once the step has ended.
                if (s_World != 0)
                {
                    if (s_Stepping) s_PendingDestroy.Add(s_World);
                    else B3.ua3_world_destroy(s_World);
                }
                s_World = 0;
                s_InitiallyKinematic.Clear();
                s_Pairs.Clear();
                s_PairOrder.Clear();
                s_PublicContactPairs.Clear();
                s_StructureDirty = false;
                s_AttachmentDirty = false;
            }
            space.Released = true;
            s_Locals.Remove(space);
            s_ByScene.Remove(sceneHandle);
            s_Released[sceneHandle] = space;
        }

        /// <summary>GameObjects moved to another scene (SceneManager.MoveGameObjectToScene, a scene
        /// merge, DontDestroyOnLoad or a new parent) take their bodies and colliders to that scene's
        /// physics scene. Without local physics scenes every component is in the default one.</summary>
        internal static void SceneMembershipChanged()
        {
            if (s_Locals.Count > 0 && !s_Stepping) Migrate();
        }

        static void Migrate()
        {
            List<Rigidbody> bodies = null;
            List<Collider> colliders = null;
            Collect(s_Default);
            for (int i = 0; i < s_Locals.Count; i++) Collect(s_Locals[i]);
            if (bodies == null && colliders == null) return;
            // Leave the old physics scene (a body takes its shapes along), then join the new one
            // as an activation does: at the current transform, keeping the body's velocity. Unity
            // documents only the move; the kept velocity and the timing (here: the next physics
            // access) are Unbound's, not measured.
            if (bodies != null) foreach (var rb in bodies) Remove(rb);
            if (colliders != null) foreach (var c in colliders) Remove(c);
            if (bodies != null) foreach (var rb in bodies) Add(rb);
            if (colliders != null) foreach (var c in colliders) Add(c);

            void Collect(Space space)
            {
                foreach (var rb in space.Rigidbodies)
                {
                    int scene = rb.m_GameObject.m_Scene.Value;
                    if (scene == rb.m_SceneHandle) continue;
                    if (ReferenceEquals(SpaceOf(rb.m_GameObject), space)) rb.m_SceneHandle = scene;
                    else (bodies ??= new List<Rigidbody>()).Add(rb);
                }
                foreach (var c in space.Colliders)
                {
                    int scene = c.m_GameObject.m_Scene.Value;
                    if (scene == c.m_SceneHandle) continue;
                    if (ReferenceEquals(SpaceOf(c.m_GameObject), space)) c.m_SceneHandle = scene;
                    else (colliders ??= new List<Collider>()).Add(c);
                }
            }
        }

        internal static bool IsStepping => s_Stepping;
        /// <summary>The members of the current physics scene (the one being simulated).</summary>
        internal static IReadOnlyList<Rigidbody> Rigidbodies => s_Rigidbodies;
        internal static IReadOnlyList<Collider> Colliders => s_Colliders;
        /// <summary>The colliders of a body's physics scene.</summary>
        internal static IReadOnlyList<Collider> CollidersOf(Rigidbody rb) => (rb.m_Space ?? s_Default).Colliders;

        /// <summary>Whether any physics scene has bodies or colliders.</summary>
        internal static bool HasMembers
        {
            get
            {
                if (s_Default.Colliders.Count > 0 || s_Default.Rigidbodies.Count > 0) return true;
                foreach (var space in s_Locals)
                    if (space.Colliders.Count > 0 || space.Rigidbodies.Count > 0) return true;
                return false;
            }
        }

        /// <summary>PhysicsScene.IsEmpty. The 6000.0 Scripting API does not say what counts; Unbound's
        /// choice (unmeasured): empty when no Rigidbody or Collider is in the physics scene, counted
        /// after pending scene moves.</summary>
        internal static bool IsEmpty(Space space)
        {
            SceneMembershipChanged();
            return space.Rigidbodies.Count == 0 && space.Colliders.Count == 0;
        }

        internal static uint World
        {
            get
            {
                EnsureWorld();
                return s_World;
            }
        }

        static void EnsureWorld()
        {
            if (s_World != 0) return;
            int version = B3.ua3_version();
            if (version != B3.Version)
                throw new InvalidOperationException($"Unbound: the unbound_physx library is version {version}, the engine expects {B3.Version}; rebuild with cargo build");
            Physics.EnsureSettings();
            s_Space.SelectiveContactReports = Compat.SelectiveContactReportsMeasured;
            B3.ua3_settings(Physics.s_DefaultContactOffset, Physics.s_SleepThreshold, Physics.s_DefaultMaxDepenetrationVelocity,
                Physics.s_DefaultMaxAngularSpeed, Physics.s_DefaultSolverIterations, Physics.s_DefaultSolverVelocityIterations);
            var settings = new B3.SceneDef
            {
                SolverType = Physics.s_SolverType, FrictionType = Physics.s_FrictionType,
                ContactPairsMode = Physics.s_ContactPairsMode,
                EnableEnhancedDeterminism = Physics.s_EnableEnhancedDeterminism ? 1 : 0,
                EnablePcm = Physics.s_EnablePcm ? 1 : 0,
                ImprovedPatchFriction = Physics.s_ImprovedPatchFriction ? 1 : 0,
            };
            if (!s_ContactModifyRegistered)
            {
                B3.ua3_set_contact_modify_callback(&OnContactModify);
                s_ContactModifyRegistered = true;
            }
            s_World = B3.ua3_world_create(Physics.s_Gravity, Mathf.Max(0f, Physics.s_BounceThreshold), 1f, 1, settings);
            if (s_World == 0)
                throw new InvalidOperationException($"Unbound: could not create the PhysX scene for DynamicsManager (solver type {settings.SolverType}, friction type {settings.FrictionType}, contact pairs mode {settings.ContactPairsMode})");
            EnsureRegistered();
        }

        static bool s_ContactModifyRegistered;

        /// <summary>PhysX's contact modification buffers of a world, during its step.</summary>
        [System.Runtime.InteropServices.UnmanagedCallersOnly(CallConvs = new[] { typeof(System.Runtime.CompilerServices.CallConvCdecl) })]
        static void OnContactModify(uint world, IntPtr pairs, uint count, int ccd)
        {
            try
            {
                var space = ReferenceEquals(s_Space, null) || s_Space.World != world ? SpaceOfWorld(world) : s_Space;
                if (space != null) Physics.SendContactModify(SceneOf(space), pairs, (int)count, ccd != 0);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        static Space SpaceOfWorld(uint world)
        {
            if (s_Default.World == world) return s_Default;
            foreach (var space in s_Locals)
                if (space.World == world) return space;
            return null;
        }

        internal static void SetImprovedPatchFriction(bool value)
        {
            // A native setting of every world; applied once a world exists, as before.
            bool any = s_Default.World != 0;
            foreach (var space in s_Locals) any |= space.World != 0;
            if (any) B3.ua3_set_improved_patch_friction(value ? 1 : 0);
        }

        /// <summary>Physics.bounceThreshold: every physics scene's restitution threshold. The default
        /// world is created for it, as before.</summary>
        internal static void SetBounceThreshold(float value)
        {
            using (Enter(s_Default)) B3.ua3_world_set_restitution_threshold(World, value);
            foreach (var space in s_Locals)
                if (space.World != 0) B3.ua3_world_set_restitution_threshold(space.World, value);
        }

        internal static void EnsureRegistered()
        {
            if (s_Registered) return;
            s_Registered = true;
            PlayerLoopDriver.Register(typeof(PL.FixedUpdate.PhysicsFixedUpdate), () =>
            {
                if (Physics.s_SimulationMode == SimulationMode.FixedUpdate) Simulate(Time.fixedDeltaTime);
            });
            PlayerLoopDriver.Register(typeof(PL.PreUpdate.PhysicsUpdate), Interpolate);
            PlayerLoopDriver.Register(typeof(PL.PreLateUpdate.PhysicsLateUpdate), () =>
            {
                if (Physics.s_SimulationMode == SimulationMode.Update) Simulate(Time.deltaTime);
            });
        }

        /// <summary>Forgets every body (the game domain is torn down).</summary>
        internal static void Reset()
        {
            foreach (var space in s_Locals)
            {
                if (space.World != 0) B3.ua3_world_destroy(space.World);
                space.World = 0;
                space.Released = true;
            }
            s_Locals.Clear();
            s_ByScene.Clear();
            s_Released.Clear();
            foreach (var world in s_PendingDestroy) B3.ua3_world_destroy(world);
            s_PendingDestroy.Clear();
            s_Space = s_Default;
            if (s_World != 0) B3.ua3_world_destroy(s_World);
            s_World = 0;
            s_Rigidbodies.Clear();
            s_InitiallyKinematic.Clear();
            s_Colliders.Clear();
            s_ByShape.Clear();
            s_ByActor.Clear();
            s_ContactRequestCache.Clear();
            s_CollisionRequestTypes.Clear();
            s_ContactDeliveries.Clear();
            s_CollisionSnapshots.Clear();
            s_CollisionPoints.Clear();
            s_ReusedCollision.CopyFrom(new Collision());
            s_Pairs.Clear();
            s_PairOrder.Clear();
            s_IgnoredPairs.Clear();
            s_PublicContactPairs.Clear();
            s_StructureDirty = false;
            s_AttachmentDirty = false;
        }

        internal static Collider ColliderOf(ulong shape) => s_ByShape.TryGetValue(shape, out var c) ? c : null;

        // ------------------------------------------------------------------ poses

        /// <summary>The pose of a transform without its scale.</summary>
        internal static Matrix4x4 PoseMatrix(Vector3 position, Quaternion rotation) => Matrix4x4.TRS(position, rotation, Vector3.one);

        /// <summary>Maps points of a collider's transform space into the frame of the body at the given pose.</summary>
        internal static Matrix4x4 ToBodyMatrix(Transform collider, Vector3 bodyPosition, Quaternion bodyRotation) =>
            PoseMatrix(bodyPosition, bodyRotation).inverse * collider.localToWorldMatrix;

        static bool SameShapeMatrix(Matrix4x4 a, Matrix4x4 b)
        {
            for (int i = 0; i < 12; i++)
            {
                float x = a[i], y = b[i];
                if (Mathf.Abs(x - y) > 1e-5f * Mathf.Max(1f, Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)))) return false;
            }
            return true;
        }

        static bool SamePose(Vector3 p1, Quaternion q1, Vector3 p2, Quaternion q2) =>
            p1 == p2 && Mathf.Abs(Quaternion.Dot(q1, q2)) > 1f - 1e-7f;

        // ------------------------------------------------------------------ membership

        internal static void Add(Rigidbody rb)
        {
            using (Enter(rb.m_Registered ? rb.m_Space : SpaceOf(rb.m_GameObject))) AddCurrent(rb);
        }

        static void AddCurrent(Rigidbody rb)
        {
            EnsureWorld();
            if (rb.m_Registered) return;
            rb.m_Registered = true;
            rb.m_Space = s_Space;
            rb.m_SceneHandle = rb.m_GameObject.m_Scene.Value;
            // Activation/import/cloning enters with the serialized type already set. Adding a
            // component then setting isKinematic transitions after its shapes, as before.
            if (rb.m_IsKinematic) s_InitiallyKinematic.Add(rb);
            s_Rigidbodies.Add(rb);
            rb.m_TransformVersion = -1;
            s_StructureDirty = true;
            s_AttachmentDirty = true;
            // Converting an existing static collider is local to the new component.
            bool hasStatic = false;
            foreach (var c in s_Colliders)
                if (c.m_StaticBody != 0 && ReferenceEquals(FindRigidbody(c), rb)) hasStatic = true;
            if (hasStatic)
            {
                CreateBody(rb);
                foreach (var c in s_Colliders)
                    if (ReferenceEquals(FindRigidbody(c), rb)) Rebuild(c);
                ApplyPendingKinematic(rb);
                if (rb.m_MassDirty) rb.ApplyMass();
            }
        }

        internal static void Remove(Rigidbody rb)
        {
            if (!rb.m_Registered) return;
            using (Enter(rb.m_Space)) RemoveCurrent(rb);
        }

        static void RemoveCurrent(Rigidbody rb)
        {
            ReleaseJointsOf(rb);
            rb.m_Registered = false;
            s_Rigidbodies.Remove(rb);
            s_InitiallyKinematic.Remove(rb);
            // Its colliders find another body (or become static) at the next flush.
            foreach (var c in s_Colliders)
            {
                if (!ReferenceEquals(c.m_AttachedBody, rb)) continue;
                DestroyShape(c);
                c.m_ShapeDirty = true;
            }
            DestroyBody(rb);
            s_StructureDirty = true;
            s_AttachmentDirty = true;
        }

        internal static void Add(Collider c)
        {
            using (Enter(c.m_Registered ? c.m_Space : SpaceOf(c.m_GameObject))) AddCurrent(c);
        }

        static void AddCurrent(Collider c)
        {
            EnsureWorld();
            if (c.m_Registered) return;
            c.m_Registered = true;
            c.m_Space = s_Space;
            c.m_SceneHandle = c.m_GameObject.m_Scene.Value;
            c.m_ShapeDirty = true;
            var t = c.m_GameObject.m_Transform;
            c.m_UseAddedPose = true;
            c.m_AddedPosition = t.position;
            c.m_AddedRotation = t.rotation;
            c.m_AddedMatrix = t.localToWorldMatrix;
            c.m_AddedVersion = t.m_Version;
            s_Colliders.Add(c);
            s_StructureDirty = true;
            bool hasBodyComponent = false;
            for (var parent = t; parent != null; parent = parent.m_Parent)
                foreach (var component in parent.m_GameObject.m_Components)
                    if (component is Rigidbody && !component.m_Destroyed) hasBodyComponent = true;
            if (!hasBodyComponent && !(c is CharacterController)) Rebuild(c);
        }

        internal static void Remove(Collider c)
        {
            if (!c.m_Registered) return;
            using (Enter(c.m_Space)) RemoveCurrent(c);
        }

        static void RemoveCurrent(Collider c)
        {
            c.m_Registered = false;
            s_Colliders.Remove(c);
            DestroyShape(c);
            if (c.m_StaticBody != 0)
            {
                B3.ua3_body_destroy(c.m_StaticBody);
                c.m_StaticBody = 0;
            }
            if (c.m_AttachedBody != null) c.m_AttachedBody.m_MassDirty = true;
            c.m_AttachedBody = null;
            // As in Unity, a collider leaving the simulation sends no exit messages.
            ForgetPairs(c);
        }

        internal static void ForgetIgnoredPairs(Collider c) => s_IgnoredPairs.RemoveWhere(p => p.Contains(c));

        internal static void MarkShapeDirty(Collider c, bool geometryChanged)
        {
            if (!c.m_Registered) return;
            using (Enter(c.m_Space)) MarkShapeDirtyCurrent(c, geometryChanged);
        }

        static void MarkShapeDirtyCurrent(Collider c, bool geometryChanged)
        {
            c.m_GeometryDirty |= geometryChanged;
            if (c is BoxCollider box && c.m_Shape != 0 && ReferenceEquals(FindRigidbody(c), c.m_AttachedBody))
            {
                var matrix = c.m_ShapeMatrix;
                var scales = Collider.AxisScales(matrix);
                var size = box.m_Size;
                var half = Vector3.Scale(new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)) * .5f, scales);
                half = Vector3.Max(half, new Vector3(1e-4f, 1e-4f, 1e-4f));
                B3.ua3_shape_update_box(c.m_Shape, ShapeDefOf(c, c.m_AttachedBody), matrix.MultiplyPoint3x4(box.m_Center), Collider.AxisRotation(matrix), half);
                if (c.m_AttachedBody != null) c.m_AttachedBody.m_MassDirty = true;
                c.m_GeometryDirty = false;
                return;
            }
            c.m_ShapeDirty = true;
            s_StructureDirty = true;
        }

        internal static void MarkBodyDirty(Rigidbody rb)
        {
            if (!rb.m_Registered) return;
            rb.m_BodyDirty = true;
            (rb.m_Space ?? s_Default).StructureDirty = true;
        }

        internal static void MarkAttachmentDirty()
        {
            s_Default.StructureDirty = true;
            s_Default.AttachmentDirty = true;
            foreach (var space in s_Locals)
            {
                space.StructureDirty = true;
                space.AttachmentDirty = true;
            }
        }

        // ------------------------------------------------------------------ structure

        /// <summary>Brings bodies and shapes in line with the components (not with moved transforms;
        /// <see cref="SyncTransforms"/> does that).</summary>
        internal static void Flush()
        {
            if (s_Stepping) return;
            if (s_Locals.Count > 0) Migrate();
            FlushSpace(s_Default);
            for (int i = 0; i < s_Locals.Count; i++) FlushSpace(s_Locals[i]);
        }

        static void FlushSpace(Space space)
        {
            if (!space.StructureDirty || space.World == 0 || s_Stepping) return;
            using (Enter(space)) FlushCurrent();
        }

        static void FlushCurrent()
        {
            s_StructureDirty = false;
            for (int i = 0; i < s_Rigidbodies.Count; i++)
            {
                var rb = s_Rigidbodies[i];
                if (rb.m_Body != 0 && rb.m_BodyDirty) rb.ApplyBodySettings();
            }
            bool attachment = s_AttachmentDirty;
            s_AttachmentDirty = false;
            for (int i = 0; i < s_Colliders.Count; i++)
            {
                var c = s_Colliders[i];
                // A body is created right before its first collider's shape, as Unity's order puts
                // them into PhysX (which orders contact pairs, and so the solver, by it).
                var owner = FindRigidbody(c);
                if (owner != null && owner.m_Body == 0) CreateBody(owner);
                // By reference: a destroyed body is "null" to Unity's operators.
                if (!c.m_ShapeDirty && attachment && !ReferenceEquals(FindRigidbody(c), c.m_AttachedBody)) c.m_ShapeDirty = true;
                if (c.m_ShapeDirty) Rebuild(c);
            }
            for (int i = 0; i < s_Rigidbodies.Count; i++)
            {
                var rb = s_Rigidbodies[i];
                if (rb.m_Body == 0) CreateBody(rb);
                // New kinematic bodies become kinematic once their shapes are in (Unity creates the
                // body, attaches the colliders, then applies isKinematic: PhysX orders the pairs by it).
                ApplyPendingKinematic(rb);
                if (rb.m_MassDirty) rb.ApplyMass();
            }
            BuildJoints();
        }

        // ------------------------------------------------------------------ joints

        static readonly List<Joint> s_DirtyJoints = new List<Joint>();
        static readonly Dictionary<ulong, Joint> s_Joints = new Dictionary<ulong, Joint>();

        internal static void MarkJointDirty(Joint j)
        {
            if (j.m_Destroyed) return;
            j.m_Dirty = true;
            if (!s_DirtyJoints.Contains(j)) s_DirtyJoints.Add(j);
            SpaceOf(j.m_GameObject).StructureDirty = true;
        }

        internal static void Register(Joint j) => s_Joints[j.m_Joint] = j;

        /// <summary>Releases a joint's native joint (rebuilt at the next flush unless forgotten).</summary>
        internal static void ReleaseJoint(Joint j, bool forget = false)
        {
            if (j.m_Joint != 0)
            {
                s_Joints.Remove(j.m_Joint);
                B3.ua3_joint_destroy(j.m_Joint);
                j.m_Joint = 0;
                j.m_Body0 = j.m_Body1 = 0;
            }
            if (forget) s_DirtyJoints.Remove(j);
        }

        /// <summary>Joints on or to a body going away let go of it first.</summary>
        static void ReleaseJointsOf(Rigidbody rb)
        {
            if (rb.m_Body == 0 || s_Joints.Count == 0) return;
            foreach (var j in new List<Joint>(s_Joints.Values))
            {
                if (j.m_Body0 != rb.m_Body && j.m_Body1 != rb.m_Body) continue;
                ReleaseJoint(j);
                MarkJointDirty(j);
            }
        }

        static void BuildJoints()
        {
            if (s_DirtyJoints.Count == 0) return;
            var joints = s_DirtyJoints.ToArray();
            s_DirtyJoints.Clear();
            foreach (var j in joints)
            {
                if (j == null || j.m_Destroyed || !j.m_GameObject.activeInHierarchy) continue;
                // A joint is built into its own physics scene, by that scene's flush.
                if (s_Locals.Count > 0 && !ReferenceEquals(SpaceOf(j.m_GameObject), s_Space))
                {
                    s_DirtyJoints.Add(j);
                    continue;
                }
                try { j.Build(); }
                catch (Exception e) { Debug.LogException(e, j); }
            }
        }

        static ulong[] s_BrokenJoints = new ulong[16];
        static float[] s_BrokenForces = new float[16];

        /// <summary>Joints that broke in the step: OnJointBreak with the breaking force, then the joint
        /// goes (as Unity destroys it).</summary>
        static void BreakJoints()
        {
            int count;
            while (true)
            {
                fixed (ulong* joints = s_BrokenJoints)
                fixed (float* forces = s_BrokenForces)
                    count = B3.ua3_world_broken_joints(s_World, joints, forces, s_BrokenJoints.Length);
                if (count <= s_BrokenJoints.Length) break;
                s_BrokenJoints = new ulong[count * 2];
                s_BrokenForces = new float[count * 2];
            }
            for (int i = 0; i < count; i++)
            {
                if (!s_Joints.TryGetValue(s_BrokenJoints[i], out var j) || j == null) continue;
                var go = j.m_GameObject;
                if (go != null && !go.m_Destroyed) Physics2DWorld.Deliver(go, "OnJointBreak", typeof(float), s_BrokenForces[i]);
                UnityEngine.Object.Destroy(j);
            }
        }

        /// <summary>The Rigidbody a collider attaches to: the nearest one on its GameObject or a parent.</summary>
        internal static Rigidbody FindRigidbody(Collider c)
        {
            // A character controller is its own (kinematic) body.
            if (c is CharacterController) return null;
            for (var t = c.m_GameObject.m_Transform; t != null; t = t.m_Parent)
            {
                var components = t.m_GameObject.m_Components;
                for (int i = 0; i < components.Count; i++)
                    if (components[i] is Rigidbody rb && rb.m_Registered && !rb.m_Destroyed) return rb;
            }
            return null;
        }

        static void CreateBody(Rigidbody rb)
        {
            var t = rb.m_GameObject.m_Transform;
            var position = t.position;
            var rotation = t.rotation;
            var def = new B3.BodyDef
            {
                Type = (int)rb.BodyType,
                Position = position,
                Rotation = rotation,
                LinearVelocity = rb.m_PendingVelocity,
                AngularVelocity = rb.m_PendingAngularVelocity,
                LinearDamping = Mathf.Max(0f, rb.m_LinearDamping),
                AngularDamping = Mathf.Max(0f, rb.m_AngularDamping),
                GravityScale = rb.m_UseGravity ? 1f : 0f,
                Locks = rb.Locks,
                EnableSleep = 1,
                IsAwake = 1,
                IsBullet = rb.NativeCollisionDetection,
            };
            rb.m_Body = B3.ua3_body_create(s_World, def);
            if (rb.m_Body != 0) s_ByActor[rb.m_Body] = rb;
            rb.m_HasCreatedBody = true;
            rb.m_KinematicPending = rb.m_IsKinematic;
            rb.ApplyTuning();
            rb.m_BodyDirty = false;
            rb.m_TransformVersion = t.m_Version;
            rb.m_SyncPosition = position;
            rb.m_SyncRotation = rotation;
            rb.m_PreviousPosition = rb.m_CurrentPosition = position;
            rb.m_PreviousRotation = rb.m_CurrentRotation = rotation;
            rb.m_MassDirty = true;
            // Its colliders attach in this flush.
            foreach (var c in s_Colliders)
                if (ReferenceEquals(c.m_AttachedBody, rb)) c.m_ShapeDirty = true;
            s_AttachmentDirty = true;
        }

        static void ApplyPendingKinematic(Rigidbody rb)
        {
            bool activate = s_InitiallyKinematic.Remove(rb);
            if (!rb.m_KinematicPending) return;
            rb.m_KinematicPending = false;
            if (!rb.m_IsKinematic) return;
            B3.ua3_body_set_type(rb.m_Body, (int)B3.BodyType.Kinematic);
            // Activation starts a real kinematic activity period without displacement.
            // A type setter on an already active dynamic body instead remains asleep.
            if (activate)
                B3.ua3_body_set_target_transform(rb.m_Body, rb.m_CurrentPosition, rb.m_CurrentRotation, Time.fixedDeltaTime);
        }

        static void DestroyBody(Rigidbody rb)
        {
            if (rb.m_Body == 0) return;
            // Retain the actual physics pose separately from any unpublished Transform edit.
            B3.ua3_body_get_transform(rb.m_Body, out rb.m_InactivePosition, out rb.m_InactiveRotation);
            // Dynamic motion survives deactivation. A kinematic actor's computed target
            // velocity belongs to that simulation step and is not retained while inactive.
            rb.m_PendingVelocity = rb.m_IsKinematic ? Vector3.zero : B3.ua3_body_get_linear_velocity(rb.m_Body);
            rb.m_PendingAngularVelocity = rb.m_IsKinematic ? Vector3.zero : B3.ua3_body_get_angular_velocity(rb.m_Body);
            s_ByActor.Remove(rb.m_Body);
            B3.ua3_body_destroy(rb.m_Body);
            rb.m_Body = 0;
            rb.ForgetMove();
        }

        static void DestroyShape(Collider c)
        {
            if (c is CharacterController controller)
            {
                if (c.m_Shape != 0) s_ByShape.Remove(c.m_Shape);
                controller.DestroyController();
                return;
            }
            foreach (var extra in c.m_ExtraShapes)
            {
                s_ByShape.Remove(extra);
                B3.ua3_shape_destroy(extra, 0);
            }
            c.m_ExtraShapes.Clear();
            if (c.m_Shape != 0)
            {
                s_ByShape.Remove(c.m_Shape);
                B3.ua3_shape_destroy(c.m_Shape, 0);
                c.m_Shape = 0;
                if (c.m_AttachedBody != null) c.m_AttachedBody.m_MassDirty = true;
            }
            if (c.m_HeightFieldData != IntPtr.Zero)
            {
                B3.ua3_height_field_destroy(c.m_HeightFieldData);
                c.m_HeightFieldData = IntPtr.Zero;
            }
            if (c.m_MeshData != IntPtr.Zero)
            {
                B3.ua3_mesh_destroy(c.m_MeshData);
                c.m_MeshData = IntPtr.Zero;
            }
        }

        /// <summary>Recreates a collider's shape on the body it attaches to now.</summary>
        static void Rebuild(Collider c, bool syncTransform = false)
        {
            var rb = FindRigidbody(c);
            // Contact properties keep the actor pose and collider-to-body frame. Geometry edits
            // use the Transform frame, as do explicit syncs. Keep the version when retaining the
            // frame, so a later sync still sees pending Transform edits.
            bool keepPose = !syncTransform && !c.m_GeometryDirty && !c.m_UseAddedPose && !(c is CharacterController)
                && ReferenceEquals(rb, c.m_AttachedBody)
                && (rb != null ? rb.m_Body != 0 : c.m_StaticBody != 0);
            c.m_ShapeDirty = false;
            c.m_GeometryDirty = false;
            DestroyShape(c);
            var transform = c.m_GameObject.m_Transform;
            if (!keepPose) c.m_TransformVersion = transform.m_Version;
            c.m_Layer = c.m_GameObject.m_Layer;
            c.m_AttachedBody = rb;
            bool addedPose = c.m_UseAddedPose;
            c.m_UseAddedPose = false;
            if (c is CharacterController controller)
            {
                if (addedPose) c.m_TransformVersion = c.m_AddedVersion;
                controller.Build(ShapeDefOf(c, null), addedPose ? c.m_AddedMatrix : transform.localToWorldMatrix);
                if (c.m_Shape != 0)
                {
                    s_ByShape[c.m_Shape] = c;
                    ReapplyIgnores(c);
                }
                return;
            }
            var world = transform.localToWorldMatrix;
            ulong body;
            Vector3 framePosition;
            Quaternion frameRotation;
            if (rb != null)
            {
                if (c.m_StaticBody != 0)
                {
                    B3.ua3_body_destroy(c.m_StaticBody);
                    c.m_StaticBody = 0;
                }
                body = rb.m_Body;
                if (body == 0) return;
                var bodyTransform = rb.m_GameObject.m_Transform;
                framePosition = bodyTransform.position;
                frameRotation = bodyTransform.rotation;
            }
            else
            {
                framePosition = transform.position;
                frameRotation = transform.rotation;
                if (addedPose)
                {
                    framePosition = c.m_AddedPosition;
                    frameRotation = c.m_AddedRotation;
                    world = c.m_AddedMatrix;
                    c.m_TransformVersion = c.m_AddedVersion;
                }
                c.StaticFrame(ref framePosition, ref frameRotation);
                if (c.m_StaticBody == 0)
                {
                    var def = new B3.BodyDef
                    {
                        Type = (int)B3.BodyType.Static,
                        Position = framePosition,
                        Rotation = frameRotation,
                        EnableSleep = 1,
                        IsAwake = 1,
                    };
                    c.m_StaticBody = B3.ua3_body_create(s_World, def);
                }
                else if (!keepPose)
                {
                    B3.ua3_body_set_transform(c.m_StaticBody, framePosition, frameRotation);
                }
                body = c.m_StaticBody;
            }

            var matrix = keepPose ? c.m_ShapeMatrix : PoseMatrix(framePosition, frameRotation).inverse * world;
            c.m_ShapeMatrix = matrix;
            var shapeDef = ShapeDefOf(c, rb);
            try
            {
                c.m_Shape = c.CreateShape(body, shapeDef, matrix, rb);
            }
            catch (Exception e)
            {
                Debug.LogException(e, c);
                c.m_Shape = 0;
            }
            if (c.m_Shape != 0)
            {
                s_ByShape[c.m_Shape] = c;
                ReapplyIgnores(c);
            }
            foreach (var extra in c.m_ExtraShapes) s_ByShape[extra] = c;
            if (rb != null)
            {
                rb.m_MassDirty = true;
                if (c.m_Shape != 0 && !rb.m_IsKinematic) B3.ua3_body_set_awake(body, 1);
            }
        }

        /// <summary>The material and filter of a collider's shape.</summary>
        static B3.ShapeDef ShapeDefOf(Collider c, Rigidbody rb)
        {
            var material = Physics.MaterialOf(c);
            c.m_UsedMaterial = material;
            c.m_MaterialVersion = material != null ? material.m_ChangeVersion : 0;
            Physics.MaterialValues(material, out float friction, out float staticFriction, out float bounciness, out ulong modes);
            FilterOf(c, rb, out ulong category, out ulong mask, out int layerFlags);
            return new B3.ShapeDef
            {
                Density = 1f,
                Friction = friction,
                Restitution = bounciness,
                Material = modes,
                CategoryBits = category,
                MaskBits = mask,
                IsSensor = c.m_IsTrigger ? 1 : 0,
                EnableSensorEvents = 1,
                EnableContactEvents = 1,
                UpdateBodyMass = 0,
                StaticFriction = staticFriction,
                ContactOffset = c.contactOffset,
                LayerFlags = layerFlags,
                LayerPriority = c.m_LayerOverridePriority,
            };
        }

        /// <summary>A collider's filter: its layer, and the layers it decides to contact: its layer's
        /// row of the collision matrix, plus its own and its rigidbody's include layers, minus both
        /// exclude layers (6000.0 Collider.excludeLayers: includes first, then excludes). Any
        /// include/exclude layer makes it an override for <see cref="Collider.layerOverridePriority"/>
        /// (measured, 6000.0.71f1: also layers that do not change the decision). A rigidbody that
        /// does not detect collisions contacts nothing.</summary>
        static void FilterOf(Collider c, Rigidbody rb, out ulong category, out ulong mask, out int layerFlags)
        {
            int layer = c.m_GameObject.m_Layer & 31;
            category = 1UL << layer;
            uint bits = unchecked((uint)Physics.s_LayerCollisionMatrix[layer]);
            int include = c.m_IncludeLayers | (rb != null ? rb.m_IncludeLayers : 0);
            int exclude = c.m_ExcludeLayers | (rb != null ? rb.m_ExcludeLayers : 0);
            mask = (bits | unchecked((uint)include)) & ~unchecked((uint)exclude);
            layerFlags = (include | exclude) != 0 ? B3.LayerOverride : 0;
            if (rb != null && !rb.m_DetectCollisions) layerFlags |= B3.LayerNoContacts;
            if (c.m_HasModifiableContacts) layerFlags |= B3.LayerModifiable;
            if (!(c.m_Space ?? s_Space).SelectiveContactReports || c.m_ContactReportFlags != 0) layerFlags |= B3.LayerContactReports;
        }

        /// <summary>The filter of a collider changed (layer, include/exclude layers, layer override
        /// priority, detectCollisions, hasModifiableContacts).</summary>
        internal static void Refilter(Collider c)
        {
            if (c.m_Shape == 0) return;
            FilterOf(c, c.m_AttachedBody, out var category, out var mask, out int layerFlags);
            if (c is CharacterController controller)
            {
                controller.SetFilter(category, mask, layerFlags);
                return;
            }
            B3.ua3_shape_set_filter(c.m_Shape, category, mask, layerFlags, c.m_LayerOverridePriority);
            // A terrain collider's tree shapes carry its filter too.
            foreach (var extra in c.m_ExtraShapes) B3.ua3_shape_set_filter(extra, category, mask, layerFlags, c.m_LayerOverridePriority);
        }

        internal static void RefilterAll()
        {
            foreach (var c in s_Default.Colliders) Refilter(c);
            foreach (var space in s_Locals)
                foreach (var c in space.Colliders) Refilter(c);
        }

        static void UpdateMaterial(Collider c)
        {
            var material = Physics.MaterialOf(c);
            c.m_UsedMaterial = material;
            c.m_MaterialVersion = material != null ? material.m_ChangeVersion : 0;
            if (c.m_Shape == 0) return;
            Physics.MaterialValues(material, out float friction, out float staticFriction, out float bounciness, out ulong modes);
            B3.ua3_shape_set_material(c.m_Shape, friction, bounciness, modes, staticFriction);
        }

        // ------------------------------------------------------------------ transforms

        /// <summary>Moves bodies and shapes to their transforms where the transforms changed
        /// (Physics.SyncTransforms). Syncing every physics scene here is Unbound's choice
        /// (unmeasured).</summary>
        internal static void SyncTransforms()
        {
            using (Enter(s_Default)) SyncCurrent();
            for (int i = 0; i < s_Locals.Count; i++)
                using (Enter(s_Locals[i])) SyncCurrent();
        }

        /// <summary>The transform sync of the current physics scene.</summary>
        static void SyncCurrent()
        {
            if (s_World == 0) return;
            Flush();
            for (int i = 0; i < s_Rigidbodies.Count; i++)
            {
                var rb = s_Rigidbodies[i];
                if (rb.m_Body == 0) continue;
                var t = rb.m_GameObject.m_Transform;
                if (t.m_Version == rb.m_TransformVersion) continue;
                rb.m_TransformVersion = t.m_Version;
                var position = t.position;
                var rotation = t.rotation;
                if (SamePose(position, rotation, rb.m_SyncPosition, rb.m_SyncRotation)) continue;
                rb.Teleport(position, rotation);
                rb.m_SyncPosition = position;
                rb.m_SyncRotation = rotation;
                rb.m_PreviousPosition = rb.m_CurrentPosition = position;
                rb.m_PreviousRotation = rb.m_CurrentRotation = rotation;
            }
            for (int i = 0; i < s_Colliders.Count; i++)
            {
                var c = s_Colliders[i];
                var go = c.m_GameObject;
                if (go.m_Layer != c.m_Layer)
                {
                    c.m_Layer = go.m_Layer;
                    Refilter(c);
                }
                if (c.m_UsedMaterial != null && c.m_UsedMaterial.m_ChangeVersion != c.m_MaterialVersion)
                    UpdateMaterial(c);
                var t = go.m_Transform;
                if (t.m_Version == c.m_TransformVersion) continue;
                c.m_TransformVersion = t.m_Version;
                if (c is CharacterController controller)
                {
                    controller.SyncFromTransform();
                    continue;
                }
                var rb = FindRigidbody(c);
                if (!ReferenceEquals(rb, c.m_AttachedBody))
                {
                    Rebuild(c, syncTransform: true);
                    continue;
                }
                if (rb == null)
                {
                    if (c.m_StaticBody == 0)
                    {
                        Rebuild(c, syncTransform: true);
                        continue;
                    }
                    var position = t.position;
                    var rotation = t.rotation;
                    if (!SameShapeMatrix(ToBodyMatrix(t, position, rotation), c.m_ShapeMatrix))
                    {
                        Rebuild(c, syncTransform: true);
                        continue;
                    }
                    B3.ua3_body_set_transform(c.m_StaticBody, position, rotation);
                }
                else if (rb.m_Body != 0)
                {
                    var bodyTransform = rb.m_GameObject.m_Transform;
                    if (!SameShapeMatrix(ToBodyMatrix(t, bodyTransform.position, bodyTransform.rotation), c.m_ShapeMatrix)) Rebuild(c, syncTransform: true);
                }
            }
            for (int i = 0; i < s_Rigidbodies.Count; i++)
            {
                var rb = s_Rigidbodies[i];
                if (rb.m_Body == 0) CreateBody(rb);
                // New kinematic bodies become kinematic once their shapes are in (Unity creates the
                // body, attaches the colliders, then applies isKinematic: PhysX orders the pairs by it).
                ApplyPendingKinematic(rb);
                if (rb.m_MassDirty) rb.ApplyMass();
            }
        }

        /// <summary>Before a query: structural changes always apply, transform changes when
        /// Physics.autoSyncTransforms is set.</summary>
        internal static void PrepareQuery()
        {
            if (s_Default.World == 0 && s_Locals.Count == 0) return;
            if (Physics.s_AutoSyncTransforms) SyncTransforms();
            else Flush();
        }

        /// <summary>Before a query of one physics scene (the default one for Physics, a scene's own
        /// for its PhysicsScene): the same preparation, for that physics scene's transforms only
        /// (Unbound's choice, unmeasured).</summary>
        internal static void PrepareQuery(Space space)
        {
            using (Enter(space))
            {
                if (s_World == 0) return;
                if (Physics.s_AutoSyncTransforms) SyncCurrent();
                else Flush();
            }
        }

        // ------------------------------------------------------------------ simulation

        /// <summary>A step of the default physics scene: the automatic simulation and Physics.Simulate.
        /// Local physics scenes are only simulated by their PhysicsScene.Simulate (6000.0 Scripting
        /// API, PhysicsScene.Simulate, comment in its example: "All of the non-default physics scenes
        /// need to be simulated manually").</summary>
        internal static void Simulate(float deltaTime) => Simulate(s_Default, deltaTime);

        /// <summary>A step of one physics scene: only the components in it move and get callbacks
        /// (6000.0 Scripting API, PhysicsScene.Simulate).</summary>
        internal static void Simulate(Space space, float deltaTime)
        {
            if (s_Stepping)
            {
                Debug.LogError("Physics.Simulate cannot be called during a simulation step or its callbacks.");
                return;
            }
            if (s_Locals.Count > 0) Migrate();
            try
            {
                using (Enter(space)) SimulateCurrent(deltaTime);
            }
            finally
            {
                DestroyPendingWorlds();
            }
        }

        static void SimulateCurrent(float deltaTime)
        {
            if (s_World == 0 || deltaTime <= 0f) return;
            SyncCurrent();
            UpdateContactRequests();
            B3.ua3_world_set_gravity(s_World, Physics.s_Gravity);

            ReadBodyStates();
            for (int i = 0; i < s_Rigidbodies.Count; i++)
            {
                var rb = s_Rigidbodies[i];
                if (rb.m_Body == 0) continue;
                rb.m_PreviousPosition = rb.m_CurrentPosition;
                rb.m_PreviousRotation = rb.m_CurrentRotation;
                rb.m_PreStepVelocity = s_States[i].LinearVelocity;
                rb.BeginStep(deltaTime);
            }

            // Wheels act on their bodies before the step, as PhysX's vehicles do.
            Vehicles.Update(deltaTime);

            s_Stepping = true;
            try
            {
                B3.ua3_world_step(s_World, deltaTime, 1);
            }
            finally
            {
                s_Stepping = false;
            }
            Vehicles.AfterStep();
            BreakJoints();

            ReadBodyStates();
            for (int i = 0; i < s_Rigidbodies.Count; i++)
            {
                var rb = s_Rigidbodies[i];
                if (rb.m_Body == 0) continue;
                rb.EndStep();
                WriteBack(rb, s_States[i].Position, s_States[i].Rotation);
            }

            CollectEvents();
            s_Stepping = true;
            try { DispatchContactEvents(); }
            finally { s_Stepping = false; }
            Flush();
        }

        // The arrays borrow SDK reports, never overlaps or the legacy contact map.
        // Native keeps them alive through legacy messages and invalidates the view on return.
        static void DispatchContactEvents()
        {
            int count;
            var reports = B3.ua3_world_contact_headers(s_World, &count);
            var pairReports = B3.ua3_world_contact_pairs(s_World);
            var points = B3.ua3_world_contact_points(s_World);
            var selected = new List<int>();
            var headerIndices = new List<int>();
            var starts = new List<int>();
            for (int i=0; i<count; i++)
            {
                var h=reports[i]; int first=selected.Count;
                for (int j=0; j<h.Count; j++)
                {
                    int index=(int)h.Start+j; var p=pairReports[index];
                    var key=p.Shape0<p.Shape1 ? (p.Shape0,p.Shape1) : (p.Shape1,p.Shape0);
                    bool removed=(p.Flags & 3)!=0;
                    bool wanted=removed ? s_PublicContactPairs.Contains(key) :
                        WantsPublicContacts(ColliderOf(p.Shape0), p.Events) || WantsPublicContacts(ColliderOf(p.Shape1), p.Events);
                    if (wanted) { selected.Add(index); s_PublicContactPairs.Add(key); }
                    if ((p.Events & (ushort)CollisionPairEventFlags.NotifyTouchLost)!=0) s_PublicContactPairs.Remove(key);
                }
                if (selected.Count!=first) { headerIndices.Add(i); starts.Add(first); }
            }
            if (!Physics.HasContactEventSubscribers || headerIndices.Count==0) { Dispatch(); return; }
            var headers = new ContactPairHeader[headerIndices.Count];
            var pairs = new ContactPair[selected.Count];
            fixed (ContactPairHeader* headerPtr = headers)
            fixed (ContactPair* pairPtr = pairs)
            {
                for (int i=0; i<headers.Length; i++)
                {
                    var h = reports[headerIndices[i]];
                    uint pairCount=(uint)((i+1<headers.Length ? starts[i+1] : pairs.Length)-starts[i]);
                    headers[i] = new ContactPairHeader(BodyInstanceId(h.Actor0, (h.Flags & 1) != 0),
                        BodyInstanceId(h.Actor1, (h.Flags & 2) != 0), (IntPtr)(pairPtr+starts[i]), pairCount,
                        (CollisionPairHeaderFlags)h.Flags);
                }
                for (int i=0; i<pairs.Length; i++)
                {
                    var p = pairReports[selected[i]];
                    pairs[i] = new ContactPair(ShapeInstanceId(p.Shape0, (p.Flags & 1) != 0),
                        ShapeInstanceId(p.Shape1, (p.Flags & 2) != 0), (IntPtr)(points+p.Start), p.Count,
                        (CollisionPairFlags)p.Flags, (CollisionPairEventFlags)p.Events, p.ImpulseSum);
                }
                var safety = Unity.Collections.LowLevel.Unsafe.AtomicSafetyHandle.Create();
                var view = new Unity.Collections.NativeArray<ContactPairHeader>.ReadOnly(headerPtr, headers.Length, ref safety);
                Dispatch(s_PairOrder.ToArray(), triggers: true);
                var deliveries = PrepareCollisionMessages();
                try
                {
                    s_Stepping = true;
                    Physics.SendContactEvent(SceneOf(s_Space), view);
                    foreach (var delivery in deliveries)
                        if (Physics.s_InvokeCollisionCallbacks)
                            DeliverCollision(delivery.Receiver, delivery.Name, delivery.Collision, delivery.Reusable);
                }
                finally
                {
                    deliveries.Clear();
                    for (int i = 0; i < s_CollisionSnapshotsUsed; i++)
                    {
                        var collision = s_CollisionSnapshots[i];
                        collision.m_Collider = null; collision.m_Rigidbody = null; collision.m_Self = null;
                    }
                    s_Stepping = false;
                    Unity.Collections.LowLevel.Unsafe.AtomicSafetyHandle.Release(safety);
                }
            }
        }
        // The request cache is rebuilt once per step, so compound children share their body's
        // component scan and receiver changes take effect without scanning other colliders.
        const int ContactEnterExit = 1, ContactStay = 2;
        static readonly Dictionary<Type, int> s_CollisionRequestTypes = new Dictionary<Type, int>();
        static readonly Dictionary<GameObject, int> s_ContactRequestCache = new Dictionary<GameObject, int>();
        static readonly Type[] s_CollisionArguments = { typeof(Collision) };

        static void UpdateContactRequests()
        {
            s_ContactRequestCache.Clear();
            foreach (var collider in s_Colliders)
            {
                int request = collider.m_ProvidesContacts ? ContactEnterExit | ContactStay :
                    ReceiverRequest(collider.m_GameObject) | ReceiverRequest(collider.m_AttachedBody?.m_GameObject);
                if (request == collider.m_ContactReportFlags) continue;
                collider.m_ContactReportFlags = request;
                Refilter(collider);
            }
        }

        static int ReceiverRequest(GameObject go)
        {
            if (go == null) return 0;
            if (s_ContactRequestCache.TryGetValue(go, out int request)) return request;
            foreach (var component in go.m_Components)
            {
                if (!(component is MonoBehaviour) || component.m_Destroyed) continue;
                var type = component.GetType();
                if (!s_CollisionRequestTypes.TryGetValue(type, out int flags))
                {
                    for (int i = 0; i < s_CollisionMessages.Length; i++)
                        if (ScriptMessages.Find(type, s_CollisionMessages[i], s_CollisionArguments) != null ||
                            ScriptMessages.Find(type, s_CollisionMessages[i], Type.EmptyTypes) != null)
                            flags |= i == 1 ? ContactStay : ContactEnterExit;
                    s_CollisionRequestTypes[type] = flags;
                }
                request |= flags;
            }
            s_ContactRequestCache[go] = request;
            return request;
        }

        static bool WantsPublicContacts(Collider collider, ushort events)
        {
            if (collider == null) return false;
            int request = collider.m_ContactReportFlags;
            if (!s_Space.SelectiveContactReports) return request != 0;
            return (request & ContactStay) != 0 || ((request & ContactEnterExit) != 0 &&
                (events & (ushort)(CollisionPairEventFlags.NotifyTouchFound | CollisionPairEventFlags.NotifyTouchLost)) != 0);
        }

        readonly struct ContactDelivery
        {
            internal readonly GameObject Receiver;
            internal readonly string Name;
            internal readonly Collision Collision;
            internal readonly bool Reusable;
            internal ContactDelivery(GameObject receiver, string name, Collision collision)
            { Receiver=receiver; Name=name; Collision=collision; Reusable=Physics.s_ReuseCollisionCallbacks; }
        }
        static readonly List<ContactDelivery> s_ContactDeliveries = new List<ContactDelivery>();
        static readonly List<Collision> s_CollisionSnapshots = new List<Collision>();
        static readonly Collision s_ReusedCollision = new Collision();
        static readonly List<ContactPoint> s_CollisionPoints = new List<ContactPoint>(16);
        static int s_CollisionSnapshotsUsed;

        static void DeliverCollision(GameObject receiver, string name, Collision collision, bool bufferedReusable = false)
        {
            if (Physics.s_ReuseCollisionCallbacks && !ReferenceEquals(collision, s_ReusedCollision))
            {
                s_ReusedCollision.CopyFrom(collision);
                collision = s_ReusedCollision;
            }
            else if (!Physics.s_ReuseCollisionCallbacks && bufferedReusable)
            {
                // A ContactEvent listener can change reuse before these buffered messages run.
                var retained = new Collision();
                retained.CopyFrom(collision);
                retained.m_Contacts = collision.contacts;
                collision = retained;
            }
            Physics2DWorld.Deliver(receiver, name, typeof(Collision), collision);
        }

        static List<ContactDelivery> PrepareCollisionMessages()
        {
            var deliveries = s_ContactDeliveries;
            deliveries.Clear();
            s_CollisionSnapshotsUsed = 0;
            foreach (var pair in s_PairOrder.ToArray())
            {
                if (pair.IsTrigger) continue;
                bool enter=pair.EnterPending, exit=pair.ExitPending;
                bool stay=!exit && pair.Touching>0 && !enter && (IsAwake(pair.A) || IsAwake(pair.B) ||
                    (!IsDynamic(pair.A) && !IsDynamic(pair.B) && pair.ContactStayReported));
                pair.EnterPending=false; pair.ExitPending=false;
                if (enter) Prepare(pair, Phase.Enter);
                if (stay) Prepare(pair, Phase.Stay);
                if (exit)
                {
                    Prepare(pair, Phase.Exit);
                    if (pair.Touching==0 && !pair.EnterPending)
                    { s_Pairs.Remove(EventKey(pair.A,pair.B,false)); s_PairOrder.Remove(pair); }
                }
            }
            return deliveries;
            void Prepare(Pair pair, Phase phase)
            {
                Side(pair.A,pair.B,false); Side(pair.B,pair.A,true);
                void Side(Collider self, Collider other, bool flip)
                {
                    var go=self.m_GameObject;
                    if (go==null || go.m_Destroyed || !go.activeInHierarchy) return;
                    var rbGo = self.m_AttachedBody?.m_GameObject;
                    bool ownReceiver = ReceiverRequest(go) != 0;
                    bool bodyReceiver = rbGo != null && rbGo != go && rbGo.activeInHierarchy && ReceiverRequest(rbGo) != 0;
                    if (!ownReceiver && !bodyReceiver) return;
                    var collision=MakeCollision(pair,self,other,phase,flip, snapshot: true);
                    var name=s_CollisionMessages[(int)phase];
                    if (ownReceiver) deliveries.Add(new ContactDelivery(go,name,collision));
                    if (bodyReceiver) deliveries.Add(new ContactDelivery(rbGo,name,collision));
                }
            }
        }

        static int ShapeInstanceId(ulong shape, bool removed) =>
            !removed && s_ByShape.TryGetValue(shape, out var c) && c != null ? c.GetInstanceID() : 0;
        internal static int BodyInstanceId(ulong actor, bool removed)
        {
            return !removed && s_ByActor.TryGetValue(actor, out var rb) && rb != null ? rb.GetInstanceID() : 0;
        }

        static ulong[] s_StateBodies = new ulong[64];
        static B3.BodyState[] s_States = new B3.BodyState[64];

        /// <summary>Every body's pose and linear velocity, in one native call, into
        /// <see cref="s_States"/> (by index in <see cref="s_Rigidbodies"/>).</summary>
        static unsafe void ReadBodyStates()
        {
            int count = s_Rigidbodies.Count;
            if (s_StateBodies.Length < count)
            {
                s_StateBodies = new ulong[count * 2];
                s_States = new B3.BodyState[count * 2];
            }
            for (int i = 0; i < count; i++) s_StateBodies[i] = s_Rigidbodies[i].m_Body;
            fixed (ulong* bodies = s_StateBodies)
            fixed (B3.BodyState* states = s_States)
                B3.ua3_bodies_state(bodies, count, states);
        }

        /// <summary>A body's pose after a step goes to its transform (not counted as a transform change).</summary>
        static void WriteBack(Rigidbody rb, Vector3 position, Quaternion rotation)
        {
            rb.m_CurrentPosition = position;
            rb.m_CurrentRotation = rotation;
            // Native leaves a teleported body's Transform unpublished if Sleep preceded
            // simulation. Its actual physics pose remains readable through Rigidbody.
            if (B3.ua3_body_is_awake(rb.m_Body) == 0) return;
            if (SamePose(position, rotation, rb.m_SyncPosition, rb.m_SyncRotation)) return;
            SetTransformPose(rb, position, rotation);
        }

        internal static void SetTransformPose(Rigidbody rb, Vector3 position, Quaternion rotation)
        {
            var t = rb.m_GameObject.m_Transform;
            if (position != rb.m_SyncPosition) t.position = position;
            if (Mathf.Abs(Quaternion.Dot(rotation, rb.m_SyncRotation)) <= 1f - 1e-7f) t.rotation = rotation;
            rb.m_SyncPosition = position;
            rb.m_SyncRotation = rotation;
            rb.m_TransformVersion = t.m_Version;
        }

        /// <summary>Interpolated and extrapolated bodies: their transforms show the pose between steps.</summary>
        static void Interpolate()
        {
            using (Enter(s_Default)) InterpolateCurrent();
        }

        static void InterpolateCurrent()
        {
            if (s_World == 0 || Physics.s_SimulationMode != SimulationMode.FixedUpdate) return;
            float dt = Time.fixedDeltaTime;
            if (dt <= 0f) return;
            float elapsed = Mathf.Max(0f, (float)(Time.s_Time - Time.s_FixedTime));
            for (int i = 0; i < s_Rigidbodies.Count; i++)
            {
                var rb = s_Rigidbodies[i];
                if (rb.m_Body == 0 || rb.m_Interpolation == RigidbodyInterpolation.None) continue;
                if (rb.m_GameObject.m_Transform.m_Version != rb.m_TransformVersion) continue; // moved by script
                Vector3 position;
                Quaternion rotation;
                if (rb.m_Interpolation == RigidbodyInterpolation.Interpolate)
                {
                    float alpha = Mathf.Clamp01(elapsed / dt);
                    position = Vector3.LerpUnclamped(rb.m_PreviousPosition, rb.m_CurrentPosition, alpha);
                    rotation = Quaternion.Slerp(rb.m_PreviousRotation, rb.m_CurrentRotation, alpha);
                }
                else
                {
                    float ahead = Mathf.Min(elapsed, dt);
                    position = rb.m_CurrentPosition + B3.ua3_body_get_linear_velocity(rb.m_Body) * ahead;
                    var w = B3.ua3_body_get_angular_velocity(rb.m_Body);
                    float angle = w.magnitude * ahead;
                    rotation = angle > 0f ? Quaternion.AngleAxis(angle * Mathf.Rad2Deg, w / w.magnitude) * rb.m_CurrentRotation : rb.m_CurrentRotation;
                }
                SetTransformPose(rb, position, rotation);
            }
        }

        // ------------------------------------------------------------------ ignored pairs

        /// <summary>An unordered pair of colliders, by identity (destroyed colliders compare equal to
        /// null, so Unity's equality cannot key them).</summary>
        internal readonly struct PairKey : IEquatable<PairKey>
        {
            internal readonly Collider First, Second;

            internal PairKey(Collider a, Collider b)
            {
                bool ordered = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a) <= System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(b);
                First = ordered ? a : b;
                Second = ordered ? b : a;
            }

            internal bool Contains(Collider c) => ReferenceEquals(First, c) || ReferenceEquals(Second, c);

            public bool Equals(PairKey other) => ReferenceEquals(First, other.First) && ReferenceEquals(Second, other.Second);

            public override bool Equals(object obj) => obj is PairKey other && Equals(other);

            public override int GetHashCode() =>
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(First) * 397 ^ System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Second);
        }

        static readonly HashSet<PairKey> s_IgnoredPairs = new HashSet<PairKey>();

        internal static void SetIgnored(Collider a, Collider b, bool ignore)
        {
            var key = new PairKey(a, b);
            if (ignore ? !s_IgnoredPairs.Add(key) : !s_IgnoredPairs.Remove(key)) return;
            Flush();
            ApplyIgnore(key, ignore);
        }

        internal static bool IsIgnored(Collider a, Collider b) => s_IgnoredPairs.Count > 0 && s_IgnoredPairs.Contains(new PairKey(a, b));

        static void ApplyIgnore(PairKey key, bool ignore)
        {
            // Colliders in different physics scenes never meet: only a pair in one is filtered.
            var space = key.First.m_Space ?? s_Default;
            if (!ReferenceEquals(space, key.Second.m_Space ?? s_Default)) return;
            if (space.World == 0 || key.First.m_Shape == 0 || key.Second.m_Shape == 0) return;
            B3.ua3_ignore_pair(space.World, key.First.m_Shape, key.Second.m_Shape, ignore ? 1 : 0);
        }

        /// <summary>A collider got a new shape: its ignored pairs apply to it again.</summary>
        internal static void ReapplyIgnores(Collider c)
        {
            if (s_IgnoredPairs.Count == 0) return;
            foreach (var key in s_IgnoredPairs)
                if (key.Contains(c)) ApplyIgnore(key, true);
        }

        // ------------------------------------------------------------------ contacts and callbacks

        /// <summary>The touch state of two colliders: Unity reports contacts per collider pair.</summary>
        internal sealed class Pair
        {
            internal Collider A, B;
            internal int Touching;
            internal bool IsTrigger;
            internal bool EnterPending, ExitPending;
            internal bool ContactStayReported;
            /// <summary>B's velocity relative to A's before the step that made them touch.</summary>
            internal Vector3 EnterRelativeVelocity;
        }

        static B3.Pair[] s_EventBuffer = new B3.Pair[64];

        static (PairKey, bool) EventKey(Collider a, Collider b, bool trigger) => (new PairKey(a, b), trigger);

        internal static Pair FindPair(Collider a, Collider b) => s_Pairs.TryGetValue(EventKey(a, b, false), out var pair) ? pair : null;

        static void ForgetPairs(Collider c)
        {
            for (int i = s_PairOrder.Count - 1; i >= 0; i--)
            {
                var p = s_PairOrder[i];
                if (!ReferenceEquals(p.A, c) && !ReferenceEquals(p.B, c)) continue;
                s_Pairs.Remove(EventKey(p.A, p.B, p.IsTrigger));
                s_PairOrder.RemoveAt(i);
            }
        }

        static int ReadEvents(B3.Events kind)
        {
            while (true)
            {
                int count;
                fixed (B3.Pair* buffer = s_EventBuffer)
                    count = B3.ua3_world_events(s_World, (int)kind, buffer, s_EventBuffer.Length);
                if (count <= s_EventBuffer.Length) return count;
                s_EventBuffer = new B3.Pair[count * 2];
            }
        }

        static void CollectEvents()
        {
            foreach (var pair in s_PairOrder) pair.ContactStayReported = false;
            Collect(B3.Events.ContactBegin, begin: true, trigger: false);
            Collect(B3.Events.SensorBegin, begin: true, trigger: true);
            Collect(B3.Events.ContactEnd, begin: false, trigger: false);
            Collect(B3.Events.SensorEnd, begin: false, trigger: true);
            int count = ReadEvents(B3.Events.ContactPersist);
            for (int i = 0; i < count; i++)
            {
                var e = s_EventBuffer[i];
                var a = ColliderOf(e.A); var b = ColliderOf(e.B);
                if (a != null && b != null && s_Pairs.TryGetValue(EventKey(a, b, false), out var pair))
                    pair.ContactStayReported = true;
            }
        }

        static void Collect(B3.Events kind, bool begin, bool trigger)
        {
            int count = ReadEvents(kind);
            for (int i = 0; i < count; i++)
            {
                var e = s_EventBuffer[i];
                var a = ColliderOf(e.A);
                var b = ColliderOf(e.B);
                if (a == null || b == null) continue;
                if (begin) Begin(a, b, trigger);
                else End(a, b, trigger);
            }
        }

        static Vector3 PreStepVelocity(Collider c)
        {
            var rb = c.m_AttachedBody;
            return rb != null && rb.m_Body != 0 ? rb.m_PreStepVelocity : Vector3.zero;
        }

        static void Begin(Collider a, Collider b, bool trigger)
        {
            // The native scene filters contact pairs according to DynamicsManager. Enabled
            // kinematic pairs report real contacts too; triggers still need a rigidbody.
            if (trigger)
            {
                if (a.m_AttachedBody == null && b.m_AttachedBody == null && !(a is CharacterController) && !(b is CharacterController)) return;
            }
            var key = EventKey(a, b, trigger);
            if (!s_Pairs.TryGetValue(key, out var pair))
            {
                pair = new Pair { A = a, B = b };
                s_Pairs[key] = pair;
                s_PairOrder.Add(pair);
            }
            pair.IsTrigger = trigger;
            pair.Touching++;
            if (pair.Touching != 1) return;
            if (pair.ExitPending)
            {
                pair.ExitPending = false; // left and came back before the callbacks: it stays
                return;
            }
            pair.EnterPending = true;
            var relative = PreStepVelocity(b) - PreStepVelocity(a);
            pair.EnterRelativeVelocity = ReferenceEquals(pair.A, a) ? relative : -relative;
        }

        static void End(Collider a, Collider b, bool trigger)
        {
            if (!s_Pairs.TryGetValue(EventKey(a, b, trigger), out var pair)) return;
            pair.Touching = Math.Max(0, pair.Touching - 1);
            if (pair.Touching > 0) return;
            pair.ExitPending = true;
        }

        static bool IsDynamic(Collider c)
        {
            var rb = c.m_AttachedBody;
            return rb != null && !rb.m_IsKinematic && rb.m_Body != 0;
        }

        static bool IsAwake(Collider c)
        {
            var rb = c.m_AttachedBody;
            return rb != null && rb.m_Body != 0 && B3.ua3_body_is_awake(rb.m_Body) != 0;
        }

        enum Phase { Enter, Stay, Exit }

        static readonly string[] s_CollisionMessages = { "OnCollisionEnter", "OnCollisionStay", "OnCollisionExit" };
        static readonly string[] s_TriggerMessages = { "OnTriggerEnter", "OnTriggerStay", "OnTriggerExit" };

        /// <summary>Sends the messages of the step that just ended.</summary>
        static void Dispatch()
        {
            if (s_PairOrder.Count == 0) return;
            var pairs = s_PairOrder.ToArray();
            // Native reports trigger phases before collision phases. A flags conversion
            // can end one kind and start the other for the same two colliders in one step.
            Dispatch(pairs, triggers: true);
            Dispatch(pairs, triggers: false);
        }

        static void Dispatch(Pair[] pairs, bool triggers)
        {
            foreach (var pair in pairs)
            {
                if (pair.IsTrigger != triggers) continue;
                bool enter = pair.EnterPending, exit = pair.ExitPending;
                // Unity sends trigger Stay for every overlapping simulation, including Enter
                // and sleeping pairs. Snapshot before callbacks: disabling a collider during
                // Enter does not retract this simulation's already recorded Stay.
                bool stay = !exit && pair.Touching > 0 && (pair.IsTrigger || (!enter &&
                    (IsAwake(pair.A) || IsAwake(pair.B) ||
                     (!IsDynamic(pair.A) && !IsDynamic(pair.B) && pair.ContactStayReported))));
                pair.EnterPending = false;
                pair.ExitPending = false;
                if (enter) Send(pair, Phase.Enter);
                if (stay) Send(pair, Phase.Stay);
                if (exit)
                {
                    Send(pair, Phase.Exit);
                    if (pair.Touching == 0 && !pair.EnterPending)
                    {
                        s_Pairs.Remove(EventKey(pair.A, pair.B, pair.IsTrigger));
                        s_PairOrder.Remove(pair);
                    }
                }
            }
        }

        static void Send(Pair pair, Phase phase)
        {
            if (!pair.IsTrigger && !Physics.s_InvokeCollisionCallbacks) return;
            SendSide(pair, pair.A, pair.B, phase, flip: false);
            SendSide(pair, pair.B, pair.A, phase, flip: true);
        }

        /// <summary>The messages <paramref name="self"/>'s objects get about <paramref name="other"/>:
        /// its own GameObject's scripts and its rigidbody's, when that is another GameObject.</summary>
        static void SendSide(Pair pair, Collider self, Collider other, Phase phase, bool flip)
        {
            if (self.m_Destroyed && phase != Phase.Exit) return;
            var go = self.m_GameObject;
            if (go == null || go.m_Destroyed || !go.activeInHierarchy) return;
            var rb = self.m_AttachedBody;
            var rbGo = rb != null && !rb.m_Destroyed ? rb.m_GameObject : null;
            if (pair.IsTrigger)
            {
                var name = s_TriggerMessages[(int)phase];
                Physics2DWorld.Deliver(go, name, typeof(Collider), other);
                if (rbGo != null && rbGo != go && rbGo.activeInHierarchy) Physics2DWorld.Deliver(rbGo, name, typeof(Collider), other);
            }
            else
            {
                var name = s_CollisionMessages[(int)phase];
                bool ownReceiver = ReceiverRequest(go) != 0;
                bool bodyReceiver = rbGo != null && rbGo != go && rbGo.activeInHierarchy && ReceiverRequest(rbGo) != 0;
                if (!ownReceiver && !bodyReceiver) return;
                var collision = MakeCollision(pair, self, other, phase, flip);
                if (ownReceiver) DeliverCollision(go, name, collision);
                if (bodyReceiver) DeliverCollision(rbGo, name, collision);
            }
        }

        static Collision MakeCollision(Pair pair, Collider self, Collider other, Phase phase, bool flip, bool snapshot = false)
        {
            Collision collision;
            if (!Physics.s_ReuseCollisionCallbacks) collision = new Collision();
            else if (!snapshot) collision = s_ReusedCollision;
            else
            {
                if (s_CollisionSnapshotsUsed == s_CollisionSnapshots.Count) s_CollisionSnapshots.Add(new Collision());
                collision = s_CollisionSnapshots[s_CollisionSnapshotsUsed++];
            }
            collision.m_Collider = other;
            collision.m_Rigidbody = other.m_AttachedBody;
            collision.m_Self = self;
            collision.m_RelativeVelocity = phase == Phase.Enter
                ? (flip ? -pair.EnterRelativeVelocity : pair.EnterRelativeVelocity)
                : PreStepVelocity(other) - PreStepVelocity(self);
            collision.m_Impulse = Vector3.zero;
            collision.m_ContactCount = 0;
            if (phase == Phase.Exit) return collision;
            var points = s_CollisionPoints;
            points.Clear();
            CollectContacts(self, other, points);
            if (collision.m_Contacts.Length < points.Count)
                collision.m_Contacts = new ContactPoint[Math.Max(4, points.Count * 2)];
            points.CopyTo(collision.m_Contacts);
            collision.m_ContactCount = points.Count;
            foreach (var point in points) collision.m_Impulse += point.m_Impulse;
            return collision;
        }

        static B3.Contact[] s_ContactBuffer = new B3.Contact[32];

        /// <summary>The touching contact points of a collider (with one other collider, or all), seen
        /// from it: normals point from the other collider towards it.</summary>
        internal static void CollectContacts(Collider self, Collider other, List<ContactPoint> into)
        {
            if (self.m_Shape == 0) return;
            int count;
            while (true)
            {
                fixed (B3.Contact* buffer = s_ContactBuffer)
                    count = B3.ua3_shape_contacts(self.m_Shape, buffer, s_ContactBuffer.Length);
                if (count <= s_ContactBuffer.Length) break;
                s_ContactBuffer = new B3.Contact[count * 2];
            }
            for (int i = 0; i < count; i++)
            {
                var c = s_ContactBuffer[i];
                bool selfIsA = c.ShapeA == self.m_Shape;
                var otherCollider = ColliderOf(selfIsA ? c.ShapeB : c.ShapeA);
                if (otherCollider == null || (other != null && !ReferenceEquals(otherCollider, other))) continue;
                var normal = selfIsA ? -c.Normal : c.Normal;
                // Unity passes PhysX's impulse on as it is: its direction follows PhysX's order of
                // the pair, not the collider asking (measured: tests/projects/physics).
                into.Add(new ContactPoint(c.Point, normal, c.Impulse, c.Separation, self.GetInstanceID(), otherCollider.GetInstanceID()));
            }
        }
    }
}
