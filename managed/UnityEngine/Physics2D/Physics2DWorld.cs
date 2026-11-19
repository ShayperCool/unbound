using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq.Expressions;
using Unbound.Box2D.Collision.Collider;
using Unbound.Box2D.Collision.Shapes;
using Unbound.Box2D.Dynamics;
using Unbound.Box2D.Dynamics.Contacts;
using UnityEngine;
using B2 = System.Numerics.Vector2;
using B2Shape = Unbound.Box2D.Collision.Shapes.Shape;
using B2Settings = Unbound.Box2D.Common.Settings;
using B2Transform = Unbound.Box2D.Common.Transform;
using Object = UnityEngine.Object;
using PL = UnityEngine.PlayerLoop;

namespace Unbound.Runtime
{
    /// <summary>
    /// The 2D physics world: a Box2D v2.4 world (the vendored managed port in Unbound.Box2D) that mirrors
    /// the active Rigidbody2D and Collider2D components, as Unity's 2D physics mirrors them into its
    /// Box2D world.
    ///
    /// Each simulated Rigidbody2D owns a body; its attached colliders (on its GameObject and on
    /// children without a nearer Rigidbody2D) add their shapes to it in the body's frame. A collider
    /// without a Rigidbody2D adds world-space shapes to the shared static scene body. Transform changes reach
    /// the bodies when transforms are synced: before every simulation step, and before queries when
    /// Physics2D.autoSyncTransforms is set. After a step, moved bodies write their poses back to their
    /// transforms, then the contact callbacks (OnCollision*2D, OnTrigger*2D) are sent.
    /// Sensors also update after integration, including newly discovered pairs; solid contact
    /// manifolds keep their pre-solver/TOI phases. A sensor can enter and exit in the same step.
    ///
    /// The simulation runs in FixedUpdate.Physics2DFixedUpdate (SimulationMode2D.FixedUpdate), in
    /// PreLateUpdate.Physics2DLateUpdate (Update) or only through Physics2D.Simulate (Script);
    /// interpolated bodies get their transform poses in PreUpdate.Physics2DUpdate. The systems are
    /// registered when the world is first needed.
    ///
    /// A scene created or loaded with LocalPhysicsMode.Physics2D owns a physics scene of its own (a
    /// separate Box2D world, <see cref="Space"/>): its components are simulated, queried and
    /// reported only there, and only by its PhysicsScene2D.Simulate (tests/projects/localphysics).
    /// Everything else is in the default physics scene.
    /// </summary>
    internal static class Physics2DWorld
    {
        static bool s_Registered;
        static bool s_Stepping;
        /// <summary>Fixtures are being destroyed because their collider left the simulation.</summary>
        static bool s_Removing;
        static bool s_SilentRemoval;
        static bool s_Dispatching;
        static bool s_MeasuredSimulation;
        internal static bool MeasuredSlide { get; private set; }
        internal static bool MeasuredCastContacts { get; private set; }
        internal static bool MeasuredSimulation => s_MeasuredSimulation;
        static readonly List<B2Shape> s_ShapeBuffer = new List<B2Shape>();
        static readonly List<HeldBody> s_Held = new List<HeldBody>();

        // ------------------------------------------------------------------ physics scenes

        /// <summary>
        /// One 2D physics scene (PhysicsScene2D): the default one, or the local one a scene made with
        /// LocalPhysicsMode.Physics2D owns (6000.0 Scripting API: LocalPhysicsMode, PhysicsScene2D).
        /// Each is its own Box2D world with its own bodies, contacts and callbacks. Components join
        /// the physics scene of their GameObject's scene; only the default one is simulated
        /// automatically (measured: tests/projects/localphysics).
        /// </summary>
        internal sealed class Space
        {
            internal World World;
            /// <summary>The body that holds the shapes of colliders without a Rigidbody2D.</summary>
            internal Body StaticBody;
            /// <summary>The body joints without a connected Rigidbody2D attach to.</summary>
            internal Body Ground;
            internal bool StructureDirty;
            internal bool AttachmentDirty;
            internal readonly List<Rigidbody2D> Rigidbodies = new List<Rigidbody2D>();
            internal readonly List<Collider2D> Colliders = new List<Collider2D>();
            /// <summary>Its contact listener and filter: Box2D calls them while it changes this world's
            /// contacts, also outside a step (a body type change), so they work on this physics scene.</summary>
            internal readonly Listener Listener;
            internal readonly Filter Filter;
            /// <summary>The layers whose colliders get contacts: the simulation layers of the last step
            /// (Physics2D.simulationLayers, or the mask passed to Simulate).</summary>
            internal int ContactLayers = Physics2D.AllLayers;
            internal readonly Dictionary<PairKey, Pair> Pairs = new Dictionary<PairKey, Pair>();
            // Native getters filter the scene's dense touching-pair list. Removal
            // fills a slot from the end. Solver edges keep their linked order.
            internal readonly List<Pair> ContactPairs = new List<Pair>();
            internal uint ContactPairRemovalVersion;
            internal readonly Dictionary<Contact, ContactObservation> ContactObservations = new Dictionary<Contact, ContactObservation>();
            internal int SubStepCount;
            internal float SubStepLostTime;
            /// <summary>Removal exits that waited for another dispatch to end.</summary>
            internal bool RemovalsDeferred;
            /// <summary>The handle of the scene that owns it; 0 for the default physics scene.</summary>
            internal readonly int Scene;
            /// <summary>Its PhysicsScene2D identity: 0 for the default one; local identities are
            /// Unbound's own (Unity's are not measured).</summary>
            internal readonly int Handle;
            internal bool Released;
            /// <summary>Its scene was unloaded, yet it stays valid and empty: nothing joins it, its
            /// Simulate does nothing and its queries find nothing (<see cref="ReleaseLocal"/>).</summary>
            internal bool Orphaned;

            internal Space(int scene, int handle)
            {
                Scene = scene;
                Handle = handle;
                Listener = new Listener(this);
                Filter = new Filter(this);
            }
        }

        static readonly Space s_Default = new Space(0, 0);
        /// <summary>The physics scene the code below works on; the default one except while a
        /// local scene's members are changed, synced, queried or simulated.</summary>
        static Space s_Space = s_Default;
        static readonly List<Space> s_Locals = new List<Space>();
        static readonly Dictionary<int, Space> s_ByScene = new Dictionary<int, Space>();
        static int s_NextHandle = 1;
        /// <summary>Scene moves made during a step or its callbacks, applied once the step has ended.</summary>
        static bool s_MembershipPending;

        // The state of the current physics scene, under the names it had when there was one.
        static World s_World { get => s_Space.World; set => s_Space.World = value; }
        static Body s_StaticBody { get => s_Space.StaticBody; set => s_Space.StaticBody = value; }
        static bool s_StructureDirty { get => s_Space.StructureDirty; set => s_Space.StructureDirty = value; }
        static bool s_AttachmentDirty { get => s_Space.AttachmentDirty; set => s_Space.AttachmentDirty = value; }
        static List<Rigidbody2D> s_Rigidbodies => s_Space.Rigidbodies;
        static List<Collider2D> s_Colliders => s_Space.Colliders;
        static Listener s_Listener => s_Space.Listener;
        static int s_ContactLayers { get => s_Space.ContactLayers; set => s_Space.ContactLayers = value; }
        static Dictionary<PairKey, Pair> s_Pairs => s_Space.Pairs;
        static List<Pair> s_ContactPairs => s_Space.ContactPairs;
        static uint s_ContactPairRemovalVersion { get => s_Space.ContactPairRemovalVersion; set => s_Space.ContactPairRemovalVersion = value; }
        static Dictionary<Contact, ContactObservation> s_ContactObservations => s_Space.ContactObservations;
        internal static int SubStepCount => s_Default.SubStepCount;
        internal static float SubStepLostTime => s_Default.SubStepLostTime;

        internal readonly struct Scope : IDisposable
        {
            readonly Space m_Previous;
            internal Scope(Space space)
            {
                m_Previous = s_Space;
                s_Space = space;
            }
            public void Dispose() => s_Space = m_Previous;
        }

        internal static Scope Enter(Space space) => new Scope(space ?? s_Default);

        internal static Space DefaultSpace => s_Default;

        /// <summary>No physics scene: an empty world with nothing to hit (an invalid PhysicsScene2D's
        /// queries find nothing, measured: tests/projects/localphysics `p2.unload.raycast`).</summary>
        internal static readonly Space NoSpace = new Space(-1, -1) { Released = true };

        /// <summary>The physics scene a GameObject's components belong to: its scene's local one,
        /// or the default one.</summary>
        internal static Space SpaceOf(GameObject go) =>
            s_ByScene.Count == 0 || go == null || !s_ByScene.TryGetValue(go.m_Scene.Value, out var space) ? s_Default : space;

        static Space SpaceOf(Rigidbody2D rb) => rb.m_Registered ? rb.m_Space ?? s_Default : SpaceOf(rb.m_GameObject);
        static Space SpaceOf(Collider2D c) => c.m_Registered ? c.m_Space ?? s_Default : SpaceOf(c.m_GameObject);

        /// <summary>The local physics scene a scene owns; null when it uses the default one.</summary>
        internal static Space LocalOf(int sceneHandle) => s_ByScene.TryGetValue(sceneHandle, out var space) ? space : null;

        /// <summary>A physics scene by its PhysicsScene2D handle; null when there is none (any more).</summary>
        internal static Space Find(int handle)
        {
            if (handle == 0) return s_Default;
            foreach (var space in s_Locals)
                if (space.Handle == handle) return space;
            foreach (var space in s_Orphaned)
                if (space.Handle == handle) return space;
            return null;
        }

        /// <summary>Local physics scenes that stay valid after their scene's unload.</summary>
        static readonly List<Space> s_Orphaned = new List<Space>();

        /// <summary>A scene made with LocalPhysicsMode.Physics2D gets its own physics scene. Its
        /// Box2D world is created with the first component that joins it.</summary>
        internal static void CreateLocal(int sceneHandle)
        {
            if (s_ByScene.ContainsKey(sceneHandle)) return;
            var space = new Space(sceneHandle, s_NextHandle++);
            s_Locals.Add(space);
            s_ByScene[sceneHandle] = space;
        }

        /// <summary>The scene that owns a local physics scene is unloaded: the physics scene goes with
        /// it (6000.0 Scripting API, LocalPhysicsMode: "the lifetime of the physics Scene(s) is the
        /// same as the Scene"; a created scene's is invalid afterwards, tests/projects/localphysics).
        /// Objects moved out of the scene first join their new physics scene; what is left leaves
        /// the simulation without callbacks.</summary>
        internal static void ReleaseLocal(int sceneHandle, bool orphan = false)
        {
            if (!s_ByScene.TryGetValue(sceneHandle, out var space)) return;
            SceneMembershipChanged();
            using (Enter(space))
            {
                foreach (var rb in s_Rigidbodies.ToArray()) Remove(rb, silent: true);
                foreach (var c in s_Colliders.ToArray())
                {
                    bool was = s_SilentRemoval;
                    s_SilentRemoval = true;
                    try { Remove(c); }
                    finally { s_SilentRemoval = was; }
                }
                Physics2DJoints.Forget(space);
                s_Listener.Clear();
                space.World = null;
                space.StaticBody = null;
                space.Ground = null;
                space.StructureDirty = space.AttachmentDirty = false;
            }
            if (orphan)
            {
                space.Orphaned = true;
                s_Orphaned.Add(space);
            }
            else space.Released = true;
            s_Locals.Remove(space);
            s_ByScene.Remove(sceneHandle);
        }

        /// <summary>GameObjects moved to another scene (SceneManager.MoveGameObjectToScene, a scene
        /// merge, DontDestroyOnLoad or a new parent) take their bodies and colliders to that scene's
        /// physics scene. A body that changes physics scene starts there at rest: its linear and
        /// angular velocity are zero, into a local scene and back (measured:
        /// tests/projects/localphysics `p2.into.*`, `p2.back.velocity`). Unbound's choices
        /// (unmeasured): the move applies at once, or after the step when made in its callbacks, and
        /// the contacts the components leave send their exit callbacks as a removal does. Without
        /// local physics scenes every component is in the default one.</summary>
        internal static void SceneMembershipChanged()
        {
            if (s_Locals.Count == 0) return;
            if (s_Stepping || s_Dispatching) { s_MembershipPending = true; return; }
            s_MembershipPending = false;
            List<Rigidbody2D> bodies = null;
            List<Collider2D> colliders = null;
            Collect(s_Default);
            for (int i = 0; i < s_Locals.Count; i++) Collect(s_Locals[i]);
            if (bodies == null && colliders == null) return;
            // The exits of the contacts they leave wait until every component is in its new physics
            // scene, so no callback runs between leaving and joining.
            s_Dispatching = true;
            try
            {
                if (colliders != null) foreach (var c in colliders) Remove(c);
                if (bodies != null)
                    foreach (var rb in bodies)
                    {
                        Remove(rb);
                        rb.m_PendingVelocity = Vector2.zero;
                        rb.m_PendingAngularVelocity = 0f;
                    }
                if (bodies != null) foreach (var rb in bodies) if (!rb.m_Destroyed) Add(rb);
                if (colliders != null) foreach (var c in colliders) if (!c.m_Destroyed) Add(c);
            }
            finally { s_Dispatching = false; }
            DispatchPendingRemovals();

            void Collect(Space space)
            {
                foreach (var rb in space.Rigidbodies)
                    if (!ReferenceEquals(SpaceOf(rb.m_GameObject), space)) (bodies ??= new List<Rigidbody2D>()).Add(rb);
                foreach (var c in space.Colliders)
                    if (!ReferenceEquals(SpaceOf(c.m_GameObject), space)) (colliders ??= new List<Collider2D>()).Add(c);
            }
        }

        internal static bool IsStepping => s_Stepping || s_Dispatching;

        /// <summary>A physics scene's Box2D world, made when first needed.</summary>
        internal static World WorldOf(Space space)
        {
            using (Enter(space))
            {
                EnsureWorld();
                return s_World;
            }
        }

        internal static Space SpaceOfCollider(Collider2D c) => SpaceOf(c);

        /// <summary>PhysicsScene2D.IsEmpty: no Rigidbody2D and no Collider2D in the physics scene (a
        /// static collider alone counts, measured: tests/projects/localphysics `p2.lone.empty`).</summary>
        internal static bool IsEmpty(Space space)
        {
            SceneMembershipChanged();
            return space.Rigidbodies.Count == 0 && space.Colliders.Count == 0;
        }

        /// <summary>Whether a GameObject's components are in the physics scene being worked on.</summary>
        internal static bool InCurrentSpace(GameObject go) => ReferenceEquals(SpaceOf(go), s_Space);

        /// <summary>The ground body of the physics scene a world belongs to: what a joint without a
        /// connected Rigidbody2D holds to.</summary>
        internal static Body GroundOf(World world)
        {
            var space = ReferenceEquals(s_Default.World, world) ? s_Default : null;
            for (int i = 0; space == null && i < s_Locals.Count; i++)
                if (ReferenceEquals(s_Locals[i].World, world)) space = s_Locals[i];
            space ??= s_Space;
            return space.Ground ??= world.CreateBody(new BodyDef());
        }

        /// <summary>Wakes every body of every physics scene (a new Physics2D.gravity, which they all share:
        /// tests/projects/localphysics `p2.gravity.velocity`).</summary>
        internal static void WakeAll()
        {
            Wake(s_Default);
            foreach (var space in s_Locals) Wake(space);

            static void Wake(Space space)
            {
                foreach (var rb in space.Rigidbodies) if (rb.m_Body != null) rb.m_Body.IsAwake = true;
            }
        }

        /// <summary>The colliders of a body's or collider's physics scene.</summary>
        internal static IReadOnlyList<Collider2D> CollidersOf(Rigidbody2D rb) => SpaceOf(rb).Colliders;
        internal static IReadOnlyList<Collider2D> CollidersOf(Collider2D c) => SpaceOf(c).Colliders;


        static void EnsureWorld()
        {
            if (s_World != null) return;
            Physics2D.EnsureSettings();
            s_MeasuredSimulation = Compat.Physics2DSimulationMeasured;
            if (!s_MeasuredSimulation)
                Stubs.Ignored("Physics2D contact payload", "this release is unmeasured; impulses use the discrete warm-start cache and entering collision velocity uses the first contact observation");
            MeasuredSlide = Compat.Physics2DSlideMeasured;
            MeasuredCastContacts = Compat.Physics2DCastContactsMeasured;
            if (!MeasuredCastContacts)
                Stubs.Ignored("Physics2D cast contact features", "this release is unmeasured; using legacy polygon clipping and core-spread witness selection");
            s_World = new World(ToB2(Physics2D.s_Gravity));
            s_World.SetContactFilter(s_Space.Filter);
            s_World.SetContactListener(s_Listener);
            Contact.FrictionMixer = (a, b) => Physics2D.CombineFriction(a.UserData as Collider2D, a.Friction, b.UserData as Collider2D, b.Friction);
            Contact.RestitutionMixer = (a, b) => Physics2D.CombineBounciness(a.UserData as Collider2D, a.Restitution, b.UserData as Collider2D, b.Restitution);
            EnsureRegistered();
        }

        internal static void EnsureRegistered()
        {
            if (s_Registered) return;
            s_Registered = true;
            PlayerLoopDriver.Register(typeof(PL.FixedUpdate.Physics2DFixedUpdate), () =>
            {
                if (Physics2D.s_SimulationMode == SimulationMode2D.FixedUpdate) Simulate(Time.fixedDeltaTime, -1, fromLoop: true);
            });
            PlayerLoopDriver.Register(typeof(PL.PreUpdate.Physics2DUpdate), Interpolate);
            PlayerLoopDriver.Register(typeof(PL.PreLateUpdate.Physics2DLateUpdate), () =>
            {
                if (Physics2D.s_SimulationMode == SimulationMode2D.Update) Simulate(Time.deltaTime, -1, fromLoop: true);
            });
        }

        /// <summary>Forgets every body and every local physics scene (the game domain is torn down).</summary>
        internal static void Reset()
        {
            Physics2DJoints.Reset();
            Physics2DEffectors.Reset();
            s_IgnoredPairs.Clear();
            foreach (var space in s_Locals) space.Released = true;
            foreach (var space in s_Orphaned) space.Released = true;
            s_Locals.Clear();
            s_Orphaned.Clear();
            s_ByScene.Clear();
            s_Space = s_Default;
            s_MembershipPending = false;
            s_Rigidbodies.Clear();
            s_Colliders.Clear();
            s_Listener.Clear();
            s_World = null;
            s_StaticBody = null;
            s_Default.Ground = null;
            s_StructureDirty = false;
            s_AttachmentDirty = false;
            s_Removing = s_SilentRemoval = s_Dispatching = s_Stepping = false;
            s_ContactLayers = Physics2D.AllLayers;
            s_Held.Clear();
            s_MeasuredSimulation = MeasuredSlide = MeasuredCastContacts = false;
            s_Default.SubStepCount = 0;
            s_Default.SubStepLostTime = 0f;
        }

        // ------------------------------------------------------------------ conversions

        internal static B2 ToB2(Vector2 v) => new B2(v.x, v.y);

        internal static Vector2 ToUnity(B2 v) => new Vector2(v.X, v.Y);

        /// <summary>The 2D pose of a transform: its position in the XY plane and its rotation about Z.</summary>
        internal static void PoseOf(Transform t, out Vector2 position, out float angle)
        {
            var p = t.position;
            position = new Vector2(p.x, p.y);
            angle = RotationAngle(t.rotation);
        }

        // Native Physics2D takes the quaternion's Z/W twist, including tilted
        // transforms. Projecting the rotated right axis changes the body angle.
        internal static float RotationAngle(Quaternion rotation)
        {
            if (rotation.w < 0f) return 2f * MathF.Atan2(-rotation.z, -rotation.w);
            return 2f * MathF.Atan2(rotation.z, rotation.w);
        }

        /// <summary>Maps points of a collider's transform space into the frame of the body at the given pose.</summary>
        internal static Matrix4x4 ToBodyMatrix(Transform collider, Vector2 bodyPosition, float bodyAngle)
        {
            // Native uses the conjugate of the pose quaternion. A general
            // matrix inverse adds determinant division and changes local vertices.
            var rotation = Quaternion.FromEulerRad(new Vector3(0f, 0f, bodyAngle));
            var inverse = Matrix4x4.Rotate(Quaternion.Inverse(rotation));
            var world = collider.localToWorldMatrix;
            var m = inverse * world;
            // Compose both translations in the rotated frame before subtracting.
            // Tilted and nested frame controls retain this accumulation order.
            m.m03 -= inverse.m00 * bodyPosition.x + inverse.m01 * bodyPosition.y;
            m.m13 -= inverse.m10 * bodyPosition.x + inverse.m11 * bodyPosition.y;
            // The XY plane only: the depth of the collider does not matter.
            m.m23 = 0f;
            return m;
        }

        static bool SameShapeMatrix(Matrix4x4 a, Matrix4x4 b)
        {
            const float tolerance = 1e-5f;
            return Close(a.m00, b.m00) && Close(a.m01, b.m01) && Close(a.m10, b.m10) && Close(a.m11, b.m11)
                && Close(a.m03, b.m03) && Close(a.m13, b.m13);

            static bool Close(float x, float y) => Mathf.Abs(x - y) <= tolerance * Mathf.Max(1f, Mathf.Max(Mathf.Abs(x), Mathf.Abs(y)));
        }

        // ------------------------------------------------------------------ membership

        internal static void Add(Rigidbody2D rb)
        {
            if (rb.m_Registered) return;
            using (Enter(SpaceOf(rb.m_GameObject))) AddCurrent(rb);
        }

        static void AddCurrent(Rigidbody2D rb)
        {
            EnsureWorld();
            rb.m_Registered = true;
            rb.m_Space = s_Space;
            s_Rigidbodies.Add(rb);
            rb.m_TransformVersion = -1;
            s_StructureDirty = true;
            s_AttachmentDirty = true;
            if (rb.m_GameObject.m_Transform != null) FlushCurrent();
        }

        internal static void Remove(Rigidbody2D rb, bool silent = false)
        {
            if (!rb.m_Registered) return;
            using (Enter(rb.m_Space)) RemoveCurrent(rb, silent);
        }

        static void RemoveCurrent(Rigidbody2D rb, bool silent)
        {
            rb.m_Registered = false;
            s_Rigidbodies.Remove(rb);
            bool wasSilent = s_SilentRemoval;
            s_SilentRemoval = silent;
            try
            {
                // Its colliders find another body or become static at the next flush.
                foreach (var c in s_Colliders)
                {
                    if (!ReferenceEquals(c.m_AttachedBody, rb)) continue;
                    DestroyFixtures(c, removing: true);
                    c.m_ShapeDirty = true;
                }
                DestroyBody(rb);
            }
            finally { s_SilentRemoval = wasSilent; }
            s_Listener.DispatchRemovals();
            s_StructureDirty = true;
            s_AttachmentDirty = true;
        }

        internal static void Add(Collider2D c)
        {
            if (c.m_Registered) return;
            using (Enter(SpaceOf(c.m_GameObject))) AddCurrent(c);
        }

        static void AddCurrent(Collider2D c)
        {
            EnsureWorld();
            c.CaptureSkin();
            c.m_Registered = true;
            c.m_Space = s_Space;
            c.m_ShapeDirty = true;
            s_Colliders.Add(c);
            s_StructureDirty = true;
            if (c.m_GameObject.m_Transform != null) FlushCurrent();
        }

        internal static void Remove(Collider2D c)
        {
            if (!c.m_Registered) return;
            using (Enter(c.m_Space)) RemoveCurrent(c);
        }

        static void RemoveCurrent(Collider2D c)
        {
            c.m_Registered = false;
            s_Colliders.Remove(c);
            DestroyFixtures(c, removing: true);
            if (c.m_StaticBody != null)
            {
                c.m_StaticBody = null;
            }
            s_Listener.DispatchRemovals();
            c.m_AttachedBody = null;
            ForgetIgnores(c);
        }

        /// <summary>A collider's shapes or contact properties changed: rebuild them before the next use.</summary>
        internal static void MarkShapeDirty(Collider2D c)
        {
            if (!c.m_Registered) return;
            c.m_ShapeDirty = true;
            SpaceOf(c).StructureDirty = true;
        }

        /// <summary>A rigidbody's simulation state changed (simulated, body type, mass properties).</summary>
        internal static void MarkBodyDirty(Rigidbody2D rb)
        {
            if (!rb.m_Registered) return;
            rb.m_BodyDirty = true;
            SpaceOf(rb).StructureDirty = true;
        }

        internal static void MarkAttachmentDirty()
        {
            s_Default.StructureDirty = s_Default.AttachmentDirty = true;
            foreach (var space in s_Locals) space.StructureDirty = space.AttachmentDirty = true;
        }

        // ------------------------------------------------------------------ structure

        /// <summary>Brings bodies and fixtures in line with the components (not with moved transforms;
        /// <see cref="SyncTransforms"/> does that), in every physics scene.</summary>
        internal static void Flush()
        {
            if (s_MembershipPending) SceneMembershipChanged();
            using (Enter(s_Default)) FlushCurrent();
            for (int i = 0; i < s_Locals.Count; i++)
                using (Enter(s_Locals[i])) FlushCurrent();
        }

        static void FlushCurrent()
        {
            if (!s_StructureDirty || s_World == null) return;
            if (s_Stepping) return;
            s_StructureDirty = false;
            for (int i = 0; i < s_Rigidbodies.Count; i++)
            {
                var rb = s_Rigidbodies[i];
                if (rb.m_BodyDirty || rb.m_Body == null && rb.m_Simulated) UpdateBody(rb);
            }
            bool attachment = s_AttachmentDirty;
            s_AttachmentDirty = false;
            for (int i = 0; i < s_Colliders.Count; i++)
            {
                var c = s_Colliders[i];
                // By reference: a destroyed body is "null" to Unity's operators.
                if (!c.m_ShapeDirty && attachment && !ReferenceEquals(FindRigidbody(c), c.m_AttachedBody)) c.m_ShapeDirty = true;
                if (c.m_ShapeDirty) Rebuild(c);
            }
            for (int i = 0; i < s_Rigidbodies.Count; i++)
            {
                var rb = s_Rigidbodies[i];
                if (rb.m_MassDirty) rb.ApplyMass();
            }
        }

        /// <summary>The Rigidbody2D a collider attaches to: the nearest one on its GameObject or a parent,
        /// in the collider's physics scene (a body another physics scene holds, while a scene move is
        /// pending, is not one to attach to).</summary>
        internal static Rigidbody2D FindRigidbody(Collider2D c)
        {
            var space = SpaceOf(c);
            for (var t = c.m_GameObject.m_Transform; t != null; t = t.m_Parent)
            {
                var components = t.m_GameObject.m_Components;
                for (int i = 0; i < components.Count; i++)
                    if (components[i] is Rigidbody2D rb && rb.m_Registered && !rb.m_Destroyed)
                        return ReferenceEquals(rb.m_Space ?? s_Default, space) ? rb : null;
            }
            return null;
        }

        static void UpdateBody(Rigidbody2D rb)
        {
            rb.m_BodyDirty = false;
            if (!rb.m_Simulated)
            {
                // Box2D disabling removes proxies and real contacts while preserving the
                // body's sleep, motion and fixtures. Its exits belong to the next step and
                // do not use callbacksOnDisable, unlike removal of a collider or GameObject.
                if (rb.m_Body != null) rb.m_Body.IsEnabled = false;
                return;
            }
            if (rb.m_Body == null)
            {
                CreateBody(rb);
                // Its colliders attach in this flush.
                foreach (var c in s_Colliders)
                    if (ReferenceEquals(c.m_AttachedBody, rb)) c.m_ShapeDirty = true;
                s_AttachmentDirty = true;
                return;
            }
            rb.m_Body.IsEnabled = true;
            rb.ApplyBodySettings();
        }

        static void CreateBody(Rigidbody2D rb)
        {
            PoseOf(rb.m_GameObject.m_Transform, out var position, out var angle);
            var def = new BodyDef
            {
                BodyType = Rigidbody2D.ToB2(rb.m_BodyType),
                Position = ToB2(position),
                Angle = angle,
                LinearVelocity = ToB2(rb.m_PendingVelocity),
                AngularVelocity = rb.m_PendingAngularVelocity * Mathf.Deg2Rad,
                LinearDamping = Mathf.Max(0f, rb.m_LinearDamping),
                AngularDamping = Mathf.Max(0f, rb.m_AngularDamping),
                GravityScale = rb.m_GravityScale,
                AllowSleep = rb.m_SleepingMode != RigidbodySleepMode2D.NeverSleep,
                Awake = rb.m_SleepingMode != RigidbodySleepMode2D.StartAsleep,
                Bullet = rb.m_CollisionDetection == CollisionDetectionMode2D.Continuous,
                FixedRotation = (rb.m_Constraints & RigidbodyConstraints2D.FreezeRotation) != 0,
                Enabled = true,
                UserData = rb,
            };
            rb.m_Body = s_World.CreateBody(def);
            rb.m_Body.FreezeX = (rb.m_Constraints & RigidbodyConstraints2D.FreezePositionX) != 0;
            rb.m_Body.FreezeY = (rb.m_Constraints & RigidbodyConstraints2D.FreezePositionY) != 0;
            rb.m_TransformVersion = rb.m_GameObject.m_Transform.m_Version;
            rb.m_SyncPosition = position;
            rb.m_SyncAngle = angle;
            rb.m_PreviousPosition = rb.m_CurrentPosition = position;
            rb.m_PreviousAngle = rb.m_CurrentAngle = angle;
            rb.m_MassDirty = true;
        }

        static void DestroyBody(Rigidbody2D rb)
        {
            if (rb.m_Body == null) return;
            Physics2DJoints.BeforeDestroy(rb.m_Body);
            // Keep the motion for when it is simulated again.
            rb.m_PendingVelocity = ToUnity(rb.m_Body.LinearVelocity);
            rb.m_PendingAngularVelocity = rb.m_Body.AngularVelocity * Mathf.Rad2Deg;
            bool was = s_Removing;
            s_Removing = true;
            try { s_World.DestroyBody(rb.m_Body); }
            finally { s_Removing = was; }
            rb.m_Body = null;
        }

        static void DestroyFixtures(Collider2D c, bool removing)
        {
            if (c.m_Fixtures.Count == 0) return;
            bool was = s_Removing;
            s_Removing = removing;
            try
            {
                foreach (var f in c.m_Fixtures)
                    if (f.Body != null && f.Body.Fixtures.Contains(f))
                        f.Body.DestroyFixture(f);
            }
            finally
            {
                s_Removing = was;
            }
            if (c.m_AttachedBody != null) c.m_AttachedBody.m_MassDirty = true;
            c.m_Fixtures.Clear();
            c.m_ShapeHashCarried = false;
        }

        /// <summary>Recreates a collider's fixtures on the body it attaches to now.</summary>
        static void Rebuild(Collider2D c)
        {
            if (!c.m_ShapeDirty || !c.m_MaterialCaptured || !ReferenceEquals(c.m_MaterialBody, FindRigidbody(c))) c.CaptureMaterial();
            c.m_ShapeDirty = false;
            DestroyFixtures(c, removing: false);
            var transform = c.m_GameObject.m_Transform;
            c.m_TransformVersion = transform.m_Version;
            c.m_Layer = c.m_GameObject.m_Layer;
            var rb = FindRigidbody(c);
            if (!ReferenceEquals(rb, c.m_AttachedBody)) c.CaptureSkin();
            c.m_AttachedBody = rb;
            Body body;
            Vector2 framePosition;
            float frameAngle;
            if (rb != null)
            {
                if (c.m_StaticBody != null)
                {
                    c.m_StaticBody = null;
                }
                body = rb.m_Body;
                if (body == null) return; // not simulated
                PoseOf(rb.m_GameObject.m_Transform, out framePosition, out frameAngle);
            }
            else
            {
                // Native keeps unattached collider geometry in world space on
                // one scene body. Its pose stays at the world origin.
                framePosition = Vector2.zero;
                frameAngle = 0f;
                if (s_StaticBody == null)
                {
                    s_StaticBody = s_World.CreateBody(new BodyDef
                    {
                        BodyType = BodyType.StaticBody,
                        Enabled = true,
                    });
                }
                body = c.m_StaticBody = s_StaticBody;
            }

            var matrix = ToBodyMatrix(transform, framePosition, frameAngle);
            if (!SameShapeMatrix(matrix, c.m_ShapeMatrix)) c.CaptureSkin();
            c.m_ShapeMatrix = matrix;
            s_ShapeBuffer.Clear();
            c.m_ErrorState = ColliderErrorState2D.None;
            if (c.m_CompositeOperation == Collider2D.CompositeOperation.None || !c.HasComposite())
            {
                try
                {
                    c.BuildShapes(matrix, s_ShapeBuffer);
                }
                catch (Exception e)
                {
                    Debug.LogException(e, c);
                }
            }
            float friction = c.m_EffectiveFriction;
            float bounciness = c.m_EffectiveBounciness;
            foreach (var shape in s_ShapeBuffer)
            {
                var fixture = body.CreateFixture(new FixtureDef
                {
                    Shape = shape,
                    Density = Mathf.Max(0f, c.m_Density),
                    Friction = friction,
                    Restitution = bounciness,
                    RestitutionThreshold = B2Settings.VelocityThreshold,
                    IsSensor = c.m_IsTrigger,
                    UserData = c,
                });
                c.m_Fixtures.Add(fixture);
            }
            s_ShapeBuffer.Clear();
            c.ShapesRebuilt();
            if (rb != null)
            {
                rb.m_MassDirty = true;
                // A new shape on a sleeping body must not stay unnoticed.
                if (c.m_Fixtures.Count > 0 && rb.m_SleepingMode != RigidbodySleepMode2D.StartAsleep) body.IsAwake = true;
            }
        }

        static bool UpdateStaticGeometry(Collider2D c, Matrix4x4 matrix)
        {
            s_ShapeBuffer.Clear();
            try
            {
                c.m_ErrorState = ColliderErrorState2D.None;
                if (c.m_CompositeOperation != Collider2D.CompositeOperation.None && c.HasComposite()) return false;
                c.BuildShapes(matrix, s_ShapeBuffer);
                if (s_ShapeBuffer.Count == 0 || s_ShapeBuffer.Count != c.m_Fixtures.Count) return false;
                for (int i = 0; i < s_ShapeBuffer.Count; i++)
                    if (s_ShapeBuffer[i].ShapeType != c.m_Fixtures[i].ShapeType ||
                        s_ShapeBuffer[i].GetChildCount() != c.m_Fixtures[i].Shape.GetChildCount()) return false;
                // Static pose changes wake the bodies connected to the scene body.
                // Controls: tests/projects/solverpairorder/Sources/UnattachedStatics.
                foreach (var edge in c.m_StaticBody.ContactEdges) edge.Other.IsAwake = true;
                var broadPhase = s_World.ContactManager.BroadPhase;
                foreach (var fixture in c.m_Fixtures) fixture.DestroyProxies(broadPhase);
                for (int i = 0; i < c.m_Fixtures.Count; i++) c.m_Fixtures[i].ReplaceGeometry(s_ShapeBuffer[i]);
                foreach (var fixture in c.m_Fixtures) fixture.CreateProxies(broadPhase, c.m_StaticBody.GetTransform());
                c.m_ShapeMatrix = matrix;
                c.m_ShapeHashCarried = false;
                s_World.HasNewContacts = true;
                return true;
            }
            catch (Exception e)
            {
                Debug.LogException(e, c);
                return false;
            }
            finally { s_ShapeBuffer.Clear(); }
        }

        /// <summary>Contact filtering changed for a collider (layer, ignore pairs): its contacts are
        /// filtered again and new ones may appear.</summary>
        internal static void Refilter(Collider2D c)
        {
            foreach (var f in c.m_Fixtures) f.Refilter();
            var world = SpaceOf(c).World;
            if (world != null) world.HasNewContacts = true;
        }

        internal static void RefilterAll()
        {
            foreach (var c in s_Default.Colliders) Refilter(c);
            foreach (var space in s_Locals)
                foreach (var c in space.Colliders) Refilter(c);
        }

        // ------------------------------------------------------------------ transforms

        /// <summary>Moves bodies and shapes to their transforms where the transforms changed, in every
        /// physics scene.</summary>
        internal static void SyncTransforms()
        {
            if (s_MembershipPending) SceneMembershipChanged();
            using (Enter(s_Default)) SyncCurrent();
            for (int i = 0; i < s_Locals.Count; i++)
                using (Enter(s_Locals[i])) SyncCurrent();
        }

        static void SyncCurrent()
        {
            if (s_World == null) return;
            FlushCurrent();
            for (int i = 0; i < s_Rigidbodies.Count; i++)
            {
                var rb = s_Rigidbodies[i];
                var body = rb.m_Body;
                if (body == null) continue;
                var t = rb.m_GameObject.m_Transform;
                if (t.m_Version == rb.m_TransformVersion) continue;
                rb.m_TransformVersion = t.m_Version;
                PoseOf(t, out var position, out var angle);
                if (position.Equals(rb.m_SyncPosition) && angle == rb.m_SyncAngle) continue;
                body.SetTransform(ToB2(position), angle);
                if (body.BodyType != BodyType.StaticBody) body.IsAwake = true;
                rb.m_SyncPosition = position;
                rb.m_SyncAngle = angle;
                rb.m_PreviousPosition = rb.m_CurrentPosition = position;
                rb.m_PreviousAngle = rb.m_CurrentAngle = angle;
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
                var t = go.m_Transform;
                if (t.m_Version == c.m_TransformVersion) continue;
                c.m_TransformVersion = t.m_Version;
                var rb = FindRigidbody(c);
                if (!ReferenceEquals(rb, c.m_AttachedBody))
                {
                    Rebuild(c);
                    continue;
                }
                if (rb == null)
                {
                    if (c.m_StaticBody == null) { Rebuild(c); continue; }
                    var matrix = ToBodyMatrix(t, Vector2.zero, 0f);
                    if (!SameShapeMatrix(matrix, c.m_ShapeMatrix) || matrix.m03 != c.m_ShapeMatrix.m03 || matrix.m13 != c.m_ShapeMatrix.m13)
                    {
                        c.CaptureSkin();
                        if (!UpdateStaticGeometry(c, matrix)) Rebuild(c);
                    }
                }
                else if (rb.m_Body != null)
                {
                    PoseOf(rb.m_GameObject.m_Transform, out var position, out var angle);
                    if (!SameShapeMatrix(ToBodyMatrix(t, position, angle), c.m_ShapeMatrix))
                    {
                        c.CaptureSkin();
                        Rebuild(c);
                    }
                }
            }
            for (int i = 0; i < s_Rigidbodies.Count; i++)
            {
                var rb = s_Rigidbodies[i];
                if (rb.m_MassDirty) rb.ApplyMass();
            }
        }

        internal static void UpdateMaterial(Collider2D c)
        {
            foreach (var f in c.m_Fixtures)
            {
                f.Friction = c.m_EffectiveFriction;
                f.Restitution = c.m_EffectiveBounciness;
                if (f.Body == null) continue;
                for (var edge = f.Body.ContactEdges.First; edge != null; edge = edge.Next)
                {
                    var contact = edge.Value.Contact;
                    if (contact.FixtureA != f && contact.FixtureB != f) continue;
                    contact.ResetFriction(); contact.ResetRestitution();
                }
            }
        }

        /// <summary>Before a query: structural changes always apply, transform changes when
        /// Physics2D.autoSyncTransforms is set.</summary>
        internal static void PrepareQuery()
        {
            if (s_Default.World == null && s_Locals.Count == 0) return;
            if (Physics2D.s_AutoSyncTransforms) SyncTransforms();
            else Flush();
        }

        // ------------------------------------------------------------------ simulation

        /// <summary>The default physics scene's step: the automatic simulation, Physics2D.Simulate and
        /// the default PhysicsScene2D's Simulate. Called by script, it runs only in
        /// SimulationMode2D.Script; otherwise the Editor warns and does not run it (measured:
        /// tests/projects/localphysics `p2.defaultSim.*`).</summary>
        internal static bool Simulate(float deltaTime, int simulationLayers, bool fromLoop)
        {
            if (IsStepping)
            {
                Debug.LogError("Physics2D.Simulate cannot be called during a simulation step or its callbacks.");
                return false;
            }
            if (!fromLoop && Physics2D.s_SimulationMode != SimulationMode2D.Script)
            {
                if (s_MeasuredSimulation)
                {
                    Debug.LogWarning(NotScriptMode);
                    return false;
                }
            }
            // The automatic modes simulate Physics2D.simulationLayers; Physics2D.Simulate takes
            // the layers as its argument (Physics2D.simulationLayers, Scripting API 6000.0).
            using (Enter(s_Default)) return SimulateCurrent(deltaTime, fromLoop ? Physics2D.s_SimulationLayers : simulationLayers);
        }

        const string NotScriptMode = "Physics2D.Simulate(...) was called but simulation mode was not set to 'Script'. You should set the simulation mode to 'Script' before calling this function therefore the simulation was not run.";

        /// <summary>A local physics scene's step (PhysicsScene2D.Simulate). Local scenes are stepped
        /// only by script, whatever Physics2D.simulationMode is (measured:
        /// tests/projects/localphysics `p2.sim.*`, in SimulationMode2D.FixedUpdate).</summary>
        internal static bool Simulate(Space space, float deltaTime, int simulationLayers)
        {
            if (ReferenceEquals(space, s_Default)) return Simulate(deltaTime, simulationLayers, fromLoop: false);
            if (IsStepping)
            {
                Debug.LogError("Physics2D.Simulate cannot be called during a simulation step or its callbacks.");
                return false;
            }
            using (Enter(space)) return SimulateCurrent(deltaTime, simulationLayers);
        }

        static bool SimulateCurrent(float deltaTime, int layers)
        {
            if (s_MembershipPending) SceneMembershipChanged();
            // A valid local physics scene steps even before anything joined it (Unbound's choice,
            // unmeasured: the measured steps have members); the default one needs its world.
            if (deltaTime < 0f) return false;
            if (s_World == null && !ReferenceEquals(s_Space, s_Default)) EnsureWorld();
            if (s_World == null) return false;
            Physics2D.ApplyTuning();
            SyncCurrent();
            Physics2DJoints.Sync();
            Physics2DJoints.SetSimulationLayers(s_World, s_MeasuredSimulation ? layers : Physics2D.AllLayers);
            s_World.Gravity = ToB2(Physics2D.s_Gravity);
            if (layers != s_ContactLayers)
            {
                // Only contacts of colliders on the simulated layers are handled: the contact
                // filter drops the others, and pairs it allowed before are filtered again.
                s_ContactLayers = layers;
                foreach (var c in s_Colliders) Refilter(c);
            }
            if (layers != Physics2D.AllLayers) HoldBodiesOutside(layers);

            // Sub-stepping applies to the per-frame modes, Update and Script (Physics2D.useSubStepping).
            int subStepCount = 1;
            float subStepTime = deltaTime, simulatedTime = deltaTime;
            bool subStepping = Physics2D.s_UseSubStepping && Physics2D.s_SimulationMode != SimulationMode2D.FixedUpdate
                && SubSteps(deltaTime, out subStepCount, out subStepTime, out simulatedTime);
            s_Space.SubStepCount = subStepCount;
            s_Space.SubStepLostTime = deltaTime - simulatedTime;
            if (subStepping && !Physics2D.s_UseSubStepContacts && !s_MeasuredSimulation)
            {
                Unbound.Runtime.Stubs.Ignored("Physics2D.useSubStepContacts",
                    "= false is not honoured: Unbound calculates contacts on every simulation sub-step, as useSubStepContacts = true does");
            }

            // Bodies asked to move (MovePosition/MoveRotation) get the velocity that reaches the
            // target in this step. A body that is not simulated keeps its pending move.
            for (int i = 0; i < s_Rigidbodies.Count; i++)
            {
                var rb = s_Rigidbodies[i];
                var body = rb.m_Body;
                if (body == null) continue;
                rb.m_PreviousPosition = rb.m_CurrentPosition;
                rb.m_PreviousAngle = rb.m_CurrentAngle;
                if (rb.m_SimulationHeld) continue;
                rb.BeginMove(simulatedTime);
            }

            s_Stepping = true;
            try
            {
                int velocityIterations = Math.Max(1, Physics2D.s_VelocityIterations);
                int positionIterations = Math.Max(1, Physics2D.s_PositionIterations);
                s_Listener.BeginStep();
                if (!subStepping) s_World.Step(deltaTime, velocityIterations, positionIterations);
                else StepSubSteps(subStepCount, subStepTime, velocityIterations, positionIterations);
            }
            finally
            {
                s_Stepping = false;
                if (s_Held.Count > 0) ReleaseHeldBodies();
            }

            for (int i = 0; i < s_Rigidbodies.Count; i++)
            {
                var rb = s_Rigidbodies[i];
                if (rb.m_Body == null) continue;
                rb.EndMove();
                WriteBack(rb);
            }

            Physics2DEffectors.AfterStep(deltaTime);
            // Reaction forces come from the impulses of the last (sub-)step.
            Physics2DJoints.AfterStep(s_World, subStepTime);
            s_Listener.Dispatch();
            Flush();
            return true;
        }

        static bool s_DrainingRemovals;

        /// <summary>Removals one physics scene's callbacks make in another wait while those callbacks
        /// are being dispatched; once the outermost dispatch ends, every physics scene sends them,
        /// until none is left.</summary>
        static void DispatchPendingRemovals()
        {
            if (s_Locals.Count == 0 || s_Dispatching || s_DrainingRemovals) return;
            s_DrainingRemovals = true;
            try
            {
                bool sent;
                do
                {
                    sent = s_Default.RemovalsDeferred && s_Default.Listener.DispatchOwnRemovals();
                    for (int i = 0; i < s_Locals.Count; i++)
                        if (s_Locals[i].RemovalsDeferred) sent |= s_Locals[i].Listener.DispatchOwnRemovals();
                }
                while (sent);
            }
            finally { s_DrainingRemovals = false; }
        }

        // ------------------------------------------------------------------ sub-stepping

        /// <summary>
        /// The sub-steps of a step whose frame rate (1 / deltaTime) is below Physics2D.minSubStepFPS:
        /// sub-steps at or above that rate, at most Physics2D.maxSubStepCount of them. When more are
        /// needed, the maximum count runs at the minimum rate and the rest of deltaTime is "lost"
        /// (Physics2D.maxSubStepCount, Scripting API 6000.0). Equal sub-steps and the
        /// capped duration are measured by physics2dadvanced on Unity 6000.0.71f1.
        /// Returns false when no sub-stepping is needed.
        /// </summary>
        internal static bool SubSteps(float deltaTime, out int count, out float stepTime, out float simulatedTime)
        {
            count = 1;
            stepTime = simulatedTime = deltaTime;
            float minFps = Physics2D.s_MinSubStepFPS;
            if (!(deltaTime > 0f) || float.IsInfinity(deltaTime) || !(minFps > 0f)) return false;
            double required = s_MeasuredSimulation ? deltaTime * minFps
                : (double)deltaTime * minFps * (1.0 - 1e-5);
            int needed = (int)Math.Min(int.MaxValue, Math.Ceiling(required));
            if (needed <= 1) return false;
            int max = Math.Max(1, Physics2D.s_MaxSubStepCount);
            if (needed > max)
            {
                count = max;
                stepTime = 1f / minFps;
                simulatedTime = Math.Min(deltaTime, count * stepTime);
            }
            else
            {
                count = needed;
                stepTime = deltaTime / needed;
            }
            return true;
        }

        /// <summary>Runs the sub-steps of one simulation step. Forces (AddForce, effectors) act for
        /// the whole step, as without sub-stepping: Box2D clears them after the last sub-step
        /// only (physics2dadvanced, Unity 6000.0.71f1 force and damping controls).</summary>
        static void StepSubSteps(int count, float stepTime, int velocityIterations, int positionIterations)
        {
            bool autoClear = s_World.IsAutoClearForces;
            s_World.IsAutoClearForces = false;
            try
            {
                for (int i = 0; i < count; i++)
                {
                    s_World.Step(stepTime, velocityIterations, positionIterations,
                        updateContacts: i == 0 || Physics2D.s_UseSubStepContacts || !s_MeasuredSimulation);
                }
            }
            finally
            {
                s_World.IsAutoClearForces = autoClear;
                if (autoClear) s_World.ClearForces();
            }
        }

        // ------------------------------------------------------------------ simulation layers

        /// <summary>The state of a body that a step does not simulate.</summary>
        struct HeldBody
        {
            internal Rigidbody2D Rigidbody;
            internal B2 Position;
            internal float Angle;
            internal B2 LinearVelocity;
            internal float AngularVelocity;
            internal B2 Force;
            internal float Torque;
            internal float SleepTime;
            internal bool Awake;
        }

        /// <summary>
        /// Only the Rigidbody2D on the simulated layers are simulated (Physics2D.simulationLayers,
        /// Scripting API 6000.0). The others sleep through the step, so they neither integrate nor
        /// move; their contacts are already filtered when their colliders are on such layers too.
        /// Afterwards <see cref="ReleaseHeldBodies"/> gives them back their exact pose, motion,
        /// forces and sleep state. A held body keeps pending moves and accumulated forces for a
        /// later simulated step. Its child colliders and connected joints are also excluded until
        /// both bodies' layers participate (physics2dadvanced, Unity 6000.0.71f1).
        /// </summary>
        static void HoldBodiesOutside(int layers)
        {
            for (int i = 0; i < s_Rigidbodies.Count; i++)
            {
                var rb = s_Rigidbodies[i];
                var body = rb.m_Body;
                if (body == null || !body.IsEnabled || body.BodyType == BodyType.StaticBody) continue;
                if ((layers & (1 << rb.m_GameObject.m_Layer)) != 0) continue;
                s_Held.Add(new HeldBody
                {
                    Rigidbody = rb,
                    Position = body.GetPosition(),
                    Angle = body.GetAngle(),
                    LinearVelocity = body.LinearVelocity,
                    AngularVelocity = body.AngularVelocity,
                    Force = body.Force,
                    Torque = body.Torque,
                    SleepTime = body.SleepTime,
                    Awake = body.IsAwake,
                });
                rb.m_SimulationHeld = true;
                body.IsAwake = false;
            }
        }

        static void ReleaseHeldBodies()
        {
            foreach (var held in s_Held)
            {
                var rb = held.Rigidbody;
                rb.m_SimulationHeld = false;
                var body = rb.m_Body;
                if (body == null) continue;
                if (body.GetPosition() != held.Position || body.GetAngle() != held.Angle) body.SetTransform(held.Position, held.Angle);
                body.IsAwake = held.Awake;
                body.LinearVelocity = held.LinearVelocity;
                body.AngularVelocity = held.AngularVelocity;
                body.Force = held.Force;
                body.Torque = held.Torque;
                body.SleepTime = held.SleepTime;
            }
            s_Held.Clear();
        }

        /// <summary>A body's pose after a step goes to its transform (not counted as a transform change).</summary>
        static void WriteBack(Rigidbody2D rb)
        {
            var body = rb.m_Body;
            var position = ToUnity(body.GetPosition());
            var angle = body.GetAngle();
            rb.m_CurrentPosition = position;
            rb.m_CurrentAngle = angle;
            if (body.BodyType == BodyType.StaticBody) return;
            if (position.Equals(rb.m_SyncPosition) && angle == rb.m_SyncAngle) return;
            SetTransformPose(rb, position, angle);
        }

        internal static void SetTransformPose(Rigidbody2D rb, Vector2 position, float angle)
        {
            var t = rb.m_GameObject.m_Transform;
            var p = t.position;
            if (!position.Equals(rb.m_SyncPosition)) t.position = new Vector3(position.x, position.y, p.z);
            float delta = angle - rb.m_SyncAngle;
            if (delta != 0f) t.rotation = QuaternionMath.AngleAxis(delta * Mathf.Rad2Deg, Vector3.forward) * t.rotation;
            rb.m_SyncPosition = position;
            rb.m_SyncAngle = angle;
            rb.m_TransformVersion = t.m_Version;
            // A simulation pose does not rebuild authored shapes. Rotating under a
            // nonuniform parent changes the transform's apparent scale and shear,
            // but Unity retains the physics shape and its inertia until an external edit.
            foreach (var fixture in rb.m_Body.Fixtures)
                if (fixture.UserData is Collider2D collider)
                    collider.m_TransformVersion = collider.m_GameObject.m_Transform.m_Version;
        }

        /// <summary>Interpolated and extrapolated bodies: their transforms show the pose between steps.</summary>
        static void Interpolate()
        {
            using (Enter(s_Default)) InterpolateCurrent();
        }

        /// <summary>Only the default physics scene is simulated automatically, so only its bodies are
        /// interpolated between its steps.</summary>
        static void InterpolateCurrent()
        {
            if (s_World == null || Physics2D.s_SimulationMode != SimulationMode2D.FixedUpdate) return;
            float dt = Time.fixedDeltaTime;
            if (dt <= 0f) return;
            float elapsed = Mathf.Max(0f, (float)(Time.s_Time - Time.s_FixedTime));
            for (int i = 0; i < s_Rigidbodies.Count; i++)
            {
                var rb = s_Rigidbodies[i];
                var body = rb.m_Body;
                if (body == null || rb.m_Interpolate == RigidbodyInterpolation2D.None || body.BodyType == BodyType.StaticBody) continue;
                if (rb.m_GameObject.m_Transform.m_Version != rb.m_TransformVersion) continue; // moved by script
                Vector2 position;
                float angle;
                if (rb.m_Interpolate == RigidbodyInterpolation2D.Interpolate)
                {
                    float alpha = Mathf.Clamp01(elapsed / dt);
                    position = Vector2.LerpUnclamped(rb.m_PreviousPosition, rb.m_CurrentPosition, alpha);
                    angle = Mathf.LerpUnclamped(rb.m_PreviousAngle, rb.m_CurrentAngle, alpha);
                }
                else
                {
                    float ahead = Mathf.Min(elapsed, dt);
                    position = rb.m_CurrentPosition + ToUnity(body.LinearVelocity) * ahead;
                    angle = rb.m_CurrentAngle + body.AngularVelocity * ahead;
                }
                SetTransformPose(rb, position, angle);
            }
        }

        // ------------------------------------------------------------------ filtering

        /// <summary>An unordered pair of colliders, by identity (destroyed colliders compare equal to
        /// null, so Unity's equality cannot key them).</summary>
        internal readonly struct PairKey : IEquatable<PairKey>
        {
            readonly Collider2D m_First, m_Second;

            internal PairKey(Collider2D a, Collider2D b)
            {
                bool ordered = System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(a) <= System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(b);
                m_First = ordered ? a : b;
                m_Second = ordered ? b : a;
            }

            internal bool Contains(Collider2D c) => ReferenceEquals(m_First, c) || ReferenceEquals(m_Second, c);

            public bool Equals(PairKey other) => ReferenceEquals(m_First, other.m_First) && ReferenceEquals(m_Second, other.m_Second);

            public override bool Equals(object obj) => obj is PairKey other && Equals(other);

            public override int GetHashCode() =>
                System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(m_First) * 397 ^ System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(m_Second);
        }

        static readonly HashSet<PairKey> s_IgnoredPairs = new HashSet<PairKey>();

        internal static void SetIgnored(Collider2D a, Collider2D b, bool ignore)
        {
            var key = new PairKey(a, b);
            if (ignore ? !s_IgnoredPairs.Add(key) : !s_IgnoredPairs.Remove(key)) return;
            Flush();
            Refilter(a);
            Refilter(b);
        }

        internal static bool IsIgnored(Collider2D a, Collider2D b) => s_IgnoredPairs.Count > 0 && s_IgnoredPairs.Contains(new PairKey(a, b));

        static void ForgetIgnores(Collider2D c)
        {
            if (s_IgnoredPairs.Count == 0) return;
            s_IgnoredPairs.RemoveWhere(p => p.Contains(c));
        }

        static RigidbodyType2D TypeOf(Collider2D c) => c.m_AttachedBody != null ? c.m_AttachedBody.m_BodyType : RigidbodyType2D.Static;

        /// <summary>Unity's rules for which collider pairs make contacts.</summary>
        internal static bool ShouldCollide(Collider2D a, Collider2D b)
        {
            if (a == null || b == null) return false;
            var rbA = a.m_AttachedBody;
            var rbB = b.m_AttachedBody;
            if (rbA != null && rbA == rbB) return false;
            var typeA = TypeOf(a);
            var typeB = TypeOf(b);
            if (typeA != RigidbodyType2D.Dynamic && typeB != RigidbodyType2D.Dynamic)
            {
                // Static against static never; triggers need a kinematic body on one side; solid
                // contacts need full kinematic contacts on a kinematic body.
                if (typeA == RigidbodyType2D.Static && typeB == RigidbodyType2D.Static) return false;
                if (!a.m_IsTrigger && !b.m_IsTrigger)
                {
                    bool fullA = typeA == RigidbodyType2D.Kinematic && rbA.m_UseFullKinematicContacts;
                    bool fullB = typeB == RigidbodyType2D.Kinematic && rbB.m_UseFullKinematicContacts;
                    if (!fullA && !fullB) return false;
                }
            }
            if (IsIgnored(a, b)) return false;
            int layerA = a.m_GameObject.m_Layer, layerB = b.m_GameObject.m_Layer;
            // An effector's collider mask that leaves the other layer out keeps the pair apart
            // whatever the colliders' include layers say (matches the Unity 6000.0.71f1 Editor,
            // tests/projects/cookingforcesvideo effector.*.maskAincludeB).
            bool? effector = Physics2DEffectors.LayerDecision(a, b, layerA, layerB);
            if (effector == false) return false;
            bool? decisionA = LayerDecision(a, rbA, layerB);
            bool? decisionB = LayerDecision(b, rbB, layerA);
            if (decisionA == null && decisionB == null) return effector ?? !Physics2D.GetIgnoreLayerCollision(layerA, layerB);
            if (decisionB == null) return decisionA.Value;
            if (decisionA == null) return decisionB.Value;
            if (decisionA.Value == decisionB.Value) return decisionA.Value;
            if (a.m_LayerOverridePriority != b.m_LayerOverridePriority)
                return a.m_LayerOverridePriority > b.m_LayerOverridePriority ? decisionA.Value : decisionB.Value;
            return false;
        }

        /// <summary>A collider's (and its rigidbody's) include/exclude layers about another layer.</summary>
        static bool? LayerDecision(Collider2D c, Rigidbody2D rb, int otherLayer)
        {
            int bit = 1 << otherLayer;
            int exclude = c.m_ExcludeLayers | (rb != null ? rb.m_ExcludeLayers : 0);
            if ((exclude & bit) != 0) return false;
            int include = c.m_IncludeLayers | (rb != null ? rb.m_IncludeLayers : 0);
            if ((include & bit) != 0) return true;
            return null;
        }

        internal sealed class Filter : IContactFilter
        {
            readonly Space m_Owner;
            internal Filter(Space owner) { m_Owner = owner; }

            public bool ShouldCollide(Fixture fixtureA, Fixture fixtureB)
            {
                var a = fixtureA.UserData as Collider2D;
                var b = fixtureB.UserData as Collider2D;
                int layers = m_Owner.ContactLayers;
                if (layers != Physics2D.AllLayers && !(OnContactLayers(a, layers) && OnContactLayers(b, layers))) return false;
                return Physics2DWorld.ShouldCollide(a, b);
            }

            static bool OnContactLayers(Collider2D c, int layers) => c != null
                && (layers & (1 << c.m_GameObject.m_Layer)) != 0
                && (!s_MeasuredSimulation || c.m_AttachedBody == null
                    || (layers & (1 << c.m_AttachedBody.m_GameObject.m_Layer)) != 0);
        }

        // ------------------------------------------------------------------ contacts and callbacks

        /// <summary>The contact state of two colliders: Unity reports contacts per collider pair,
        /// whatever the number of shapes touching.</summary>
        internal sealed class Pair
        {
            internal Collider2D A, B;
            internal int Touching;
            internal bool IsTrigger;
            internal bool EnterPending, ExitPending;
            internal bool RemovalExit;
            internal bool FirstIsB;
            internal bool PublicListed;
            internal bool VelocityCaptured;
            internal Vector2 EnterRelativeVelocity;
        }

        internal sealed class ContactObservation
        {
            internal readonly Vector2[] Points = new Vector2[2];
            internal readonly Vector2[] Velocities = new Vector2[2];
            internal readonly float[] Separations = new float[2];
            internal Unbound.Box2D.Common.FixedArray2<float> NormalImpulses, TangentImpulses;
            internal ulong ImpulseStep;
        }

        internal static IEnumerable<Pair> PublicContactPairs(Object view)
        {
            if (view == null) yield break;
            var space = view is Collider2D collider ? SpaceOf(collider) : view is Rigidbody2D body ? SpaceOf(body) : s_Space;
            foreach (var pair in space.ContactPairs)
                if (pair.Touching > 0 && (pair.A == (object)view || pair.B == (object)view ||
                    pair.A.m_AttachedBody == (object)view || pair.B.m_AttachedBody == (object)view))
                    yield return pair;
        }

        static void RemovePublicContactPair(Pair pair)
        {
            if (!pair.PublicListed) return;
            int index = s_ContactPairs.IndexOf(pair);
            s_ContactPairs[index] = s_ContactPairs[s_ContactPairs.Count - 1];
            s_ContactPairs.RemoveAt(s_ContactPairs.Count - 1);
            pair.PublicListed = false;
            unchecked { s_ContactPairRemovalVersion++; }
        }

        internal static Pair FindPair(Collider2D a, Collider2D b) =>
            SpaceOf(a).Pairs.TryGetValue(new PairKey(a, b), out var pair) ? pair : null;

        /// <summary>The pairs a collider is part of.</summary>
        internal static IEnumerable<Pair> PairsOf(Collider2D c)
        {
            var pairs = SpaceOf(c).ContactPairs;
            for (int i = 0; i < pairs.Count; i++)
            {
                var p = pairs[i];
                if (p.Touching > 0 && (p.A == (object)c || p.B == (object)c)) yield return p;
            }
        }

        internal sealed class Listener : IContactListener
        {
            readonly Space m_Owner;
            ulong step;
            internal Listener(Space owner) { m_Owner = owner; }
            internal void BeginStep() => step++;
            internal void Clear()
            {
                using var scope = Enter(m_Owner);
                s_Pairs.Clear();
                s_ContactPairs.Clear();
                s_ContactObservations.Clear();
            }

            public void BeginContact(Contact contact)
            {
                using var scope = Enter(m_Owner);
                var a = contact.FixtureA.UserData as Collider2D;
                var b = contact.FixtureB.UserData as Collider2D;
                if (a == null || b == null) return;
                var key = new PairKey(a, b);
                if (!s_Pairs.TryGetValue(key, out var pair))
                {
                    pair = new Pair { A = a, B = b, FirstIsB = b.m_ContactOrder > a.m_ContactOrder };
                    s_Pairs[key] = pair;
                }
                pair.IsTrigger = contact.FixtureA.IsSensor || contact.FixtureB.IsSensor;
                pair.Touching++;
                if (pair.Touching == 1)
                {
                    if (!pair.PublicListed)
                    {
                        s_ContactPairs.Add(pair);
                        pair.PublicListed = true;
                    }
                    if (pair.ExitPending) pair.ExitPending = false; // left and came back before the callbacks: it stays
                    else
                    {
                        pair.EnterPending = true;
                        pair.VelocityCaptured = false;
                    }
                }
            }

            public void EndContact(Contact contact)
            {
                using var scope = Enter(m_Owner);
                s_ContactObservations.Remove(contact);
                Physics2DEffectors.End(contact);
                var a = contact.FixtureA.UserData as Collider2D;
                var b = contact.FixtureB.UserData as Collider2D;
                if (a == null || b == null) return;
                if (!s_Pairs.TryGetValue(new PairKey(a, b), out var pair)) return;
                pair.Touching = Math.Max(0, pair.Touching - 1);
                if (pair.Touching > 0) return;
                if (s_SilentRemoval || s_Removing && !Physics2D.s_CallbacksOnDisable)
                {
                    Forget(pair);
                    return;
                }
                pair.ExitPending = true;
                pair.RemovalExit = s_Removing;
            }

            public void PreSolve(Contact contact, in Manifold oldManifold)
            {
                using var scope = Enter(m_Owner);
                var a = contact.FixtureA.UserData as Collider2D;
                var b = contact.FixtureB.UserData as Collider2D;
                if (a == null || b == null) return;
                Physics2DEffectors.PreSolve(contact, a, b);
                if (!s_ContactObservations.TryGetValue(contact, out var observation))
                    s_ContactObservations[contact] = observation = new ContactObservation();
                contact.GetWorldManifold(out var observedWorld);
                for (int i = 0; i < contact.Manifold.PointCount; i++)
                {
                    var point = observedWorld.Points[i];
                    observation.Points[i] = ToUnity(point);
                    observation.Separations[i] = observedWorld.Separations[i];
                    observation.Velocities[i] = ToUnity(contact.FixtureB.Body.GetLinearVelocityFromWorldPoint(point)
                        - contact.FixtureA.Body.GetLinearVelocityFromWorldPoint(point));
                }
                if (!s_MeasuredSimulation && contact.Manifold.PointCount > 0 &&
                    s_Pairs.TryGetValue(new PairKey(a, b), out var pair) && pair.EnterPending && !pair.VelocityCaptured)
                {
                    pair.EnterRelativeVelocity = pair.A == (object)a ? observation.Velocities[0] : -observation.Velocities[0];
                    pair.VelocityCaptured = true;
                }
                if (observation.ImpulseStep != step)
                {
                    observation.NormalImpulses = observation.TangentImpulses = default;
                    observation.ImpulseStep = step;
                }
            }

            public void PostSolve(Contact contact, in ContactImpulse impulse)
            {
                using var scope = Enter(m_Owner);
                // Accumulate every solve in this step, including time-of-impact solves.
                // The manifold impulses remain the discrete solver's warm-start cache.
                if (s_MeasuredSimulation && s_ContactObservations.TryGetValue(contact, out var observation))
                    for (int i = 0; i < impulse.Count; i++)
                    {
                        observation.NormalImpulses[i] += impulse.NormalImpulses[i];
                        observation.TangentImpulses[i] += impulse.TangentImpulses[i];
                    }
                Physics2DEffectors.PostSolve(contact);
            }


            static void Forget(Pair pair)
            {
                s_Pairs.Remove(new PairKey(pair.A, pair.B));
                RemovePublicContactPair(pair);
            }

            // Drain real EndContact notifications only after fixture/body removal is complete.
            // Reentrant removals wait until both sides of the current notification have run.
            internal void DispatchRemovals()
            {
                DispatchOwnRemovals();
                DispatchPendingRemovals();
            }

            /// <summary>Sends this physics scene's pending removal exits; whether it sent any.</summary>
            internal bool DispatchOwnRemovals()
            {
                using var scope = Enter(m_Owner);
                if (s_Dispatching)
                {
                    m_Owner.RemovalsDeferred = true;
                    return false;
                }
                m_Owner.RemovalsDeferred = false;
                bool sent = false;
                s_Dispatching = true;
                try
                {
                    while (true)
                    {
                        Pair next = null;
                        for (int kind = 0; kind < 2 && next == null; kind++)
                            foreach (var pair in s_ContactPairs)
                                if (pair.IsTrigger == (kind == 0) && pair.RemovalExit && pair.ExitPending)
                                { next = pair; break; }
                        if (next == null) break;
                        next.RemovalExit = next.ExitPending = false;
                        RemovePublicContactPair(next);
                        Send(next, Phase.Exit);
                        sent = true;
                        if (next.Touching == 0 && !next.EnterPending) Forget(next);
                    }
                }
                finally { s_Dispatching = false; }
                return sent;
            }

            /// <summary>Sends the callbacks of the step that just ended.</summary>
            internal void Dispatch()
            {
                using var scope = Enter(m_Owner);
                if (s_ContactPairs.Count == 0) return;
                s_Dispatching = true;
                try
                {
                    // Native sends the trigger phase before the solid-contact phase, including
                    // reentry after a solid contact has already begun. Preserve order within each
                    // phase; creation order across the two kinds must not interleave callbacks.
                    for (int kind = 0; kind < 2; kind++)
                    {
                        var dispatched = new HashSet<Pair>();
                        for (int i = 0; i < s_ContactPairs.Count;)
                        {
                            var pair = s_ContactPairs[i];
                            if (pair.IsTrigger != (kind == 0) ||
                                !dispatched.Add(pair)) { i++; continue; }
                            uint removalVersion = s_ContactPairRemovalVersion;
                            bool enter = pair.EnterPending, exit = pair.ExitPending;
                            pair.EnterPending = false;
                            pair.ExitPending = false;
                            pair.RemovalExit = false;
                            // Native keeps an ended pair in its scene slot until
                            // notification, after new touching pairs have appended.
                            // Its tail replacement is the next pair visited here.
                            if (exit && pair.Touching == 0) RemovePublicContactPair(pair);
                            if (enter)
                            {
                                Send(pair, Phase.Enter);
                            }
                            else if (!exit && pair.Touching > 0 && IsAwake(pair))
                            {
                                Send(pair, Phase.Stay);
                            }
                            if (exit)
                            {
                                Send(pair, Phase.Exit);
                                if (pair.Touching == 0 && !pair.EnterPending) Forget(pair);
                            }
                            // A callback may remove an earlier slot and move an unvisited
                            // tail behind us. Revisit current slots, keeping each pair once.
                            if (removalVersion != s_ContactPairRemovalVersion) i = 0;
                            else if (i < s_ContactPairs.Count && ReferenceEquals(s_ContactPairs[i], pair)) i++;
                        }
                    }
                }
                finally { s_Dispatching = false; }
                DispatchRemovals();
            }

            static bool IsAwake(Pair pair) => IsAwake(pair.A) || IsAwake(pair.B);

            static bool IsAwake(Collider2D c)
            {
                var body = c.m_AttachedBody?.m_Body;
                return body != null && body.BodyType != BodyType.StaticBody && body.IsAwake;
            }
        }

        enum Phase { Enter, Stay, Exit }

        static readonly string[] s_CollisionMessages = { "OnCollisionEnter2D", "OnCollisionStay2D", "OnCollisionExit2D" };
        static readonly string[] s_TriggerMessages = { "OnTriggerEnter2D", "OnTriggerStay2D", "OnTriggerExit2D" };

        static void Send(Pair pair, Phase phase)
        {
            if (pair.FirstIsB)
            {
                SendSide(pair, pair.B, pair.A, phase, flip: true);
                SendSide(pair, pair.A, pair.B, phase, flip: false);
            }
            else
            {
                SendSide(pair, pair.A, pair.B, phase, flip: false);
                SendSide(pair, pair.B, pair.A, phase, flip: true);
            }
        }

        /// <summary>The messages <paramref name="self"/>'s objects get about <paramref name="other"/>.</summary>
        static void SendSide(Pair pair, Collider2D self, Collider2D other, Phase phase, bool flip)
        {
            if (self.m_Destroyed && phase != Phase.Exit) return;
            var go = self.m_GameObject;
            if (go == null || go.m_Destroyed || phase != Phase.Exit && !go.activeInHierarchy) return;
            var otherGo = other.m_GameObject;
            int otherLayer = otherGo != null ? otherGo.m_Layer : 0;
            if ((self.m_CallbackLayers & (1 << otherLayer)) == 0) return;
            var rb = self.m_AttachedBody;
            var rbGo = rb != null && !rb.m_Destroyed ? rb.m_GameObject : null;
            if (pair.IsTrigger)
            {
                var name = s_TriggerMessages[(int)phase];
                Deliver(go, name, typeof(Collider2D), other);
                if (rbGo != null && rbGo != go && rbGo.activeInHierarchy) Deliver(rbGo, name, typeof(Collider2D), other);
            }
            else
            {
                var name = s_CollisionMessages[(int)phase];
                var collision = MakeCollision(pair, self, other, phase, flip);
                Deliver(go, name, typeof(Collision2D), collision);
                if (rbGo != null && rbGo != go && rbGo.activeInHierarchy) Deliver(rbGo, name, typeof(Collision2D), collision);
            }
        }

        static Collision2D MakeCollision(Pair pair, Collider2D self, Collider2D other, Phase phase, bool flip)
        {
            var collision = new Collision2D
            {
                m_CollisionEnabled = true,
                m_CollisionContacts = Array.Empty<ContactPoint2D>(),
                m_CollisionCollider = other,
                m_CollisionOtherCollider = self,
                m_CollisionRigidbody = other.m_AttachedBody,
                m_CollisionOtherRigidbody = self.m_AttachedBody,
            };
            // Unity's exit messages carry no contacts and no relative velocity.
            if (phase == Phase.Exit) return collision;
            var points = new List<ContactPoint2D>(2);
            bool enabled = CollectContacts(self, other, points);
            collision.m_CollisionContacts = points.ToArray();
            collision.m_CollisionContactCount = points.Count;
            collision.m_CollisionEnabled = enabled;
            collision.m_CollisionRelativeVelocity = !s_MeasuredSimulation && phase == Phase.Enter && pair.VelocityCaptured
                ? (flip ? -pair.EnterRelativeVelocity : pair.EnterRelativeVelocity)
                : points.Count > 0 ? points[0].relativeVelocity : RelativeVelocity(self, other);
            return collision;
        }

        /// <summary>The other collider's linear velocity relative to this one's.</summary>
        static Vector2 RelativeVelocity(Collider2D self, Collider2D other)
        {
            Vector2 Velocity(Collider2D c)
            {
                var body = c.m_AttachedBody?.m_Body;
                return body != null ? ToUnity(body.LinearVelocity) : Vector2.zero;
            }
            return Velocity(other) - Velocity(self);
        }

        internal static bool HasEnabledContact(Collider2D self, Collider2D other)
        {
            foreach (var fixture in self.m_Fixtures)
            {
                if (fixture.Body == null) continue;
                for (var edge = fixture.Body.ContactEdges.First; edge != null; edge = edge.Next)
                {
                    var contact = edge.Value.Contact;
                    if (!contact.IsTouching || !contact.IsEnabled) continue;
                    if (contact.FixtureA == fixture && contact.FixtureB.UserData == (object)other
                        || contact.FixtureB == fixture && contact.FixtureA.UserData == (object)other) return true;
                }
            }
            return false;
        }

        internal static bool HasContactPoints(Collider2D self, Collider2D other)
        {
            foreach (var fixture in self.m_Fixtures)
            {
                if (fixture.Body == null) continue;
                foreach (var edge in fixture.Body.ContactEdges)
                {
                    var contact = edge.Contact;
                    if (!contact.IsTouching || contact.Manifold.PointCount == 0) continue;
                    if (contact.FixtureA == fixture && contact.FixtureB.UserData == (object)other ||
                        contact.FixtureB == fixture && contact.FixtureA.UserData == (object)other) return true;
                }
            }
            return false;
        }

        /// <summary>The touching contact points between two colliders, seen from <paramref name="self"/>
        /// (normals point from <paramref name="other"/> towards <paramref name="self"/>). Returns
        /// whether the contact is enabled.</summary>
        internal static bool CollectContacts(Collider2D self, Collider2D other, List<ContactPoint2D> into)
        {
            bool enabled = true;
            var observations = SpaceOf(self).ContactObservations;
            foreach (var f in self.m_Fixtures)
            {
                var body = f.Body;
                if (body == null) continue;
                for (var node = body.ContactEdges.First; node != null; node = node.Next)
                {
                    var contact = node.Value.Contact;
                    bool selfIsA;
                    if (contact.FixtureA == f && (other == null || contact.FixtureB.UserData == (object)other)) selfIsA = true;
                    else if (contact.FixtureB == f && (other == null || contact.FixtureA.UserData == (object)other)) selfIsA = false;
                    else continue;
                    if (!contact.IsTouching) continue;
                    var otherCollider = (selfIsA ? contact.FixtureB.UserData : contact.FixtureA.UserData) as Collider2D;
                    enabled &= contact.IsEnabled;
                    int count = contact.Manifold.PointCount;
                    if (count == 0) continue;
                    contact.GetWorldManifold(out var world);
                    var normal = ToUnity(world.Normal);
                    if (selfIsA) normal = -normal;
                    var selfBody = selfIsA ? contact.FixtureA.Body : contact.FixtureB.Body;
                    var otherBody = selfIsA ? contact.FixtureB.Body : contact.FixtureA.Body;
                    observations.TryGetValue(contact, out var observation);
                    for (int i = 0; i < count; i++)
                    {
                        var p = world.Points[i];
                        var mp = contact.Manifold.Points[i];
                        var rel = observation != null ? (selfIsA ? observation.Velocities[i] : -observation.Velocities[i])
                            : ToUnity(otherBody.GetLinearVelocityFromWorldPoint(p) - selfBody.GetLinearVelocityFromWorldPoint(p));
                        into.Add(new ContactPoint2D(observation != null ? observation.Points[i] : ToUnity(p), normal, rel, contact.Friction, contact.Restitution,
                            observation != null ? observation.Separations[i] : world.Separations[i],
                            s_MeasuredSimulation && observation != null ? observation.NormalImpulses[i] : mp.NormalImpulse,
                            s_MeasuredSimulation && observation != null ? observation.TangentImpulses[i] : mp.TangentImpulse, otherCollider, self, contact.IsEnabled));
                    }
                }
            }
            return enabled;
        }

        // ------------------------------------------------------------------ messages

        static readonly ConcurrentDictionary<(Type, string, Type), Func<MonoBehaviour, object, object>> s_Calls =
            new ConcurrentDictionary<(Type, string, Type), Func<MonoBehaviour, object, object>>();

        internal static void ClearCaches() => s_Calls.Clear();

        /// <summary>A physics message to every script of a GameObject, enabled or not, as Unity sends
        /// them (so a callback can enable its behaviour). The method may take the argument or nothing,
        /// and may be a coroutine.</summary>
        internal static void Deliver(GameObject go, string name, Type argumentType, object argument)
        {
            var components = go.m_Components.ToArray();
            foreach (var component in components)
            {
                if (!(component is MonoBehaviour mb) || mb.m_Destroyed) continue;
                var call = s_Calls.GetOrAdd((mb.GetType(), name, argumentType), key => Compile(key.Item1, key.Item2, key.Item3));
                if (call == null) continue;
                try
                {
                    var result = call(mb, argument);
                    if (result is IEnumerator routine && mb.IsActiveAndEnabledNoThrow) Coroutines.Start(mb, routine, name);
                }
                catch (Exception e)
                {
                    Debug.LogException(e, mb);
                }
            }
        }

        static Func<MonoBehaviour, object, object> Compile(Type type, string name, Type argumentType)
        {
            var method = ScriptMessages.Find(type, name, new[] { argumentType }) ?? ScriptMessages.Find(type, name, Type.EmptyTypes);
            if (method == null) return null;
            var target = Expression.Parameter(typeof(MonoBehaviour));
            var argument = Expression.Parameter(typeof(object));
            var instance = Expression.Convert(target, method.DeclaringType);
            Expression call = method.GetParameters().Length == 1
                ? Expression.Call(instance, method, Expression.Convert(argument, argumentType))
                : Expression.Call(instance, method);
            Expression body = call.Type == typeof(void)
                ? Expression.Block(call, Expression.Constant(null, typeof(object)))
                : Expression.Convert(call, typeof(object));
            return Expression.Lambda<Func<MonoBehaviour, object, object>>(body, target, argument).Compile();
        }
    }
}
