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
    /// without a Rigidbody2D gets a static body of its own at its transform. Transform changes reach
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
    /// </summary>
    internal static class Physics2DWorld
    {
        static World s_World;
        static bool s_Registered;
        static bool s_StructureDirty;
        static bool s_AttachmentDirty;
        static bool s_Stepping;
        /// <summary>Fixtures are being destroyed because their collider left the simulation.</summary>
        static bool s_Removing;
        static bool s_SilentRemoval;
        static bool s_Dispatching;
        static readonly List<Rigidbody2D> s_Rigidbodies = new List<Rigidbody2D>();
        static readonly List<Collider2D> s_Colliders = new List<Collider2D>();
        static readonly List<B2Shape> s_ShapeBuffer = new List<B2Shape>();
        static readonly Listener s_Listener = new Listener();
        static readonly Filter s_Filter = new Filter();

        internal static bool IsStepping => s_Stepping || s_Dispatching;

        internal static World World
        {
            get
            {
                EnsureWorld();
                return s_World;
            }
        }

        internal static IReadOnlyList<Rigidbody2D> Rigidbodies => s_Rigidbodies;
        internal static IReadOnlyList<Collider2D> Colliders => s_Colliders;

        static void EnsureWorld()
        {
            if (s_World != null) return;
            Physics2D.EnsureSettings();
            s_World = new World(ToB2(Physics2D.s_Gravity));
            s_World.SetContactFilter(s_Filter);
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

        /// <summary>Forgets every body (the game domain is torn down).</summary>
        internal static void Reset()
        {
            Physics2DJoints.Reset();
            Physics2DEffectors.Reset();
            s_Rigidbodies.Clear();
            s_Colliders.Clear();
            s_Listener.Clear();
            s_IgnoredPairs.Clear();
            s_World = null;
            s_StructureDirty = false;
            s_AttachmentDirty = false;
            s_Removing = s_SilentRemoval = s_Dispatching = s_Stepping = false;
        }

        // ------------------------------------------------------------------ conversions

        internal static B2 ToB2(Vector2 v) => new B2(v.x, v.y);

        internal static Vector2 ToUnity(B2 v) => new Vector2(v.X, v.Y);

        /// <summary>The 2D pose of a transform: its position in the XY plane and its rotation about Z.</summary>
        internal static void PoseOf(Transform t, out Vector2 position, out float angle)
        {
            var p = t.position;
            position = new Vector2(p.x, p.y);
            var right = t.rotation * Vector3.right;
            angle = Mathf.Atan2(right.y, right.x);
        }

        /// <summary>Maps points of a collider's transform space into the frame of the body at the given pose.</summary>
        internal static Matrix4x4 ToBodyMatrix(Transform collider, Vector2 bodyPosition, float bodyAngle)
        {
            var pose = Matrix4x4.TRS(new Vector3(bodyPosition.x, bodyPosition.y, 0f), Quaternion.Euler(0f, 0f, bodyAngle * Mathf.Rad2Deg), Vector3.one);
            var m = pose.inverse * collider.localToWorldMatrix;
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
            EnsureWorld();
            if (rb.m_Registered) return;
            rb.m_Registered = true;
            s_Rigidbodies.Add(rb);
            rb.m_TransformVersion = -1;
            s_StructureDirty = true;
            s_AttachmentDirty = true;
            if (rb.m_GameObject.m_Transform != null) Flush();
        }

        internal static void Remove(Rigidbody2D rb, bool silent = false)
        {
            if (!rb.m_Registered) return;
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
            EnsureWorld();
            if (c.m_Registered) return;
            c.CaptureSkin();
            c.m_Registered = true;
            c.m_ShapeDirty = true;
            s_Colliders.Add(c);
            s_StructureDirty = true;
            if (c.m_GameObject.m_Transform != null) Flush();
        }

        internal static void Remove(Collider2D c)
        {
            if (!c.m_Registered) return;
            c.m_Registered = false;
            s_Colliders.Remove(c);
            DestroyFixtures(c, removing: true);
            if (c.m_StaticBody != null)
            {
                s_World.DestroyBody(c.m_StaticBody);
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
            s_StructureDirty = true;
        }

        /// <summary>A rigidbody's simulation state changed (simulated, body type, mass properties).</summary>
        internal static void MarkBodyDirty(Rigidbody2D rb)
        {
            if (!rb.m_Registered) return;
            rb.m_BodyDirty = true;
            s_StructureDirty = true;
        }

        internal static void MarkAttachmentDirty()
        {
            s_StructureDirty = true;
            s_AttachmentDirty = true;
        }

        // ------------------------------------------------------------------ structure

        /// <summary>Brings bodies and fixtures in line with the components (not with moved transforms;
        /// <see cref="SyncTransforms"/> does that).</summary>
        internal static void Flush()
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

        /// <summary>The Rigidbody2D a collider attaches to: the nearest one on its GameObject or a parent.</summary>
        internal static Rigidbody2D FindRigidbody(Collider2D c)
        {
            for (var t = c.m_GameObject.m_Transform; t != null; t = t.m_Parent)
            {
                var components = t.m_GameObject.m_Components;
                for (int i = 0; i < components.Count; i++)
                    if (components[i] is Rigidbody2D rb && rb.m_Registered && !rb.m_Destroyed) return rb;
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
                    s_World.DestroyBody(c.m_StaticBody);
                    c.m_StaticBody = null;
                }
                body = rb.m_Body;
                if (body == null) return; // not simulated
                PoseOf(rb.m_GameObject.m_Transform, out framePosition, out frameAngle);
            }
            else
            {
                PoseOf(transform, out framePosition, out frameAngle);
                if (c.m_StaticBody == null)
                {
                    c.m_StaticBody = s_World.CreateBody(new BodyDef
                    {
                        BodyType = BodyType.StaticBody,
                        Position = ToB2(framePosition),
                        Angle = frameAngle,
                        Enabled = true,
                        UserData = c,
                    });
                }
                else
                {
                    var oldPose = c.m_StaticBody.GetTransform();
                    if (oldPose.Position != ToB2(framePosition) || !Mathf.Approximately(oldPose.Rotation.Angle, frameAngle))
                        c.CaptureSkin();
                    c.m_StaticBody.SetTransform(ToB2(framePosition), frameAngle);
                }
                body = c.m_StaticBody;
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
            if (rb != null)
            {
                rb.m_MassDirty = true;
                // A new shape on a sleeping body must not stay unnoticed.
                if (c.m_Fixtures.Count > 0 && rb.m_SleepingMode != RigidbodySleepMode2D.StartAsleep) body.IsAwake = true;
            }
        }

        /// <summary>Contact filtering changed for a collider (layer, ignore pairs): its contacts are
        /// filtered again and new ones may appear.</summary>
        internal static void Refilter(Collider2D c)
        {
            foreach (var f in c.m_Fixtures) f.Refilter();
            if (s_World != null) s_World.HasNewContacts = true;
        }

        internal static void RefilterAll()
        {
            foreach (var c in s_Colliders) Refilter(c);
        }

        // ------------------------------------------------------------------ transforms

        /// <summary>Moves bodies and shapes to their transforms where the transforms changed.</summary>
        internal static void SyncTransforms()
        {
            if (s_World == null) return;
            Flush();
            for (int i = 0; i < s_Rigidbodies.Count; i++)
            {
                var rb = s_Rigidbodies[i];
                var body = rb.m_Body;
                if (body == null) continue;
                var t = rb.m_GameObject.m_Transform;
                if (t.m_Version == rb.m_TransformVersion) continue;
                rb.m_TransformVersion = t.m_Version;
                PoseOf(t, out var position, out var angle);
                if (position == rb.m_SyncPosition && Mathf.Approximately(angle, rb.m_SyncAngle)) continue;
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
                    PoseOf(t, out var position, out var angle);
                    var matrix = ToBodyMatrix(t, position, angle);
                    var current = c.m_StaticBody.GetTransform();
                    bool changedShape = !SameShapeMatrix(matrix, c.m_ShapeMatrix);
                    bool changedPose = current.Position != ToB2(position) || !Mathf.Approximately(current.Rotation.Angle, angle);
                    if (changedShape || changedPose)
                    {
                        c.CaptureSkin();
                        if (changedShape || c is BoxCollider2D box && box.SkinDiffersFromFixtures()) { Rebuild(c); continue; }
                        c.m_StaticBody.SetTransform(ToB2(position), angle);
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
            if (s_World == null) return;
            if (Physics2D.s_AutoSyncTransforms) SyncTransforms();
            else Flush();
        }

        // ------------------------------------------------------------------ simulation

        internal static bool Simulate(float deltaTime, int simulationLayers, bool fromLoop)
        {
            if (IsStepping)
            {
                Debug.LogError("Physics2D.Simulate cannot be called during a simulation step or its callbacks.");
                return false;
            }
            if (!fromLoop && Physics2D.s_SimulationMode != SimulationMode2D.Script)
            {
                // Unity allows it in any mode since 2021; the step simply runs.
            }
            if (s_World == null) return false;
            if (deltaTime < 0f) return false;
            Physics2D.ApplyTuning();
            SyncTransforms();
            Physics2DJoints.Sync();
            s_World.Gravity = ToB2(Physics2D.s_Gravity);

            // Bodies asked to move (MovePosition/MoveRotation) get the velocity that reaches the
            // target in this step.
            for (int i = 0; i < s_Rigidbodies.Count; i++)
            {
                var rb = s_Rigidbodies[i];
                var body = rb.m_Body;
                if (body == null) continue;
                rb.m_PreviousPosition = rb.m_CurrentPosition;
                rb.m_PreviousAngle = rb.m_CurrentAngle;
                rb.BeginMove(deltaTime);
            }

            s_Stepping = true;
            try
            {
                s_World.Step(deltaTime, Math.Max(1, Physics2D.s_VelocityIterations), Math.Max(1, Physics2D.s_PositionIterations));
            }
            finally
            {
                s_Stepping = false;
            }

            for (int i = 0; i < s_Rigidbodies.Count; i++)
            {
                var rb = s_Rigidbodies[i];
                if (rb.m_Body == null) continue;
                rb.EndMove();
                WriteBack(rb);
            }

            Physics2DEffectors.AfterStep(deltaTime);
            Physics2DJoints.AfterStep(deltaTime);
            s_Listener.Dispatch();
            Flush();
            return true;
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
            if (position == rb.m_SyncPosition && angle == rb.m_SyncAngle) return;
            SetTransformPose(rb, position, angle);
        }

        static void SetTransformPose(Rigidbody2D rb, Vector2 position, float angle)
        {
            var t = rb.m_GameObject.m_Transform;
            var p = t.position;
            if (position != rb.m_SyncPosition) t.position = new Vector3(position.x, position.y, p.z);
            float delta = angle - rb.m_SyncAngle;
            if (delta != 0f) t.rotation = Quaternion.AngleAxis(delta * Mathf.Rad2Deg, Vector3.forward) * t.rotation;
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
            bool? decisionA = LayerDecision(a, rbA, layerB);
            bool? decisionB = LayerDecision(b, rbB, layerA);
            if (decisionA == null && decisionB == null) return Physics2DEffectors.LayerDecision(a, b, layerA, layerB) ?? !Physics2D.GetIgnoreLayerCollision(layerA, layerB);
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

        sealed class Filter : IContactFilter
        {
            public bool ShouldCollide(Fixture fixtureA, Fixture fixtureB) =>
                Physics2DWorld.ShouldCollide(fixtureA.UserData as Collider2D, fixtureB.UserData as Collider2D);
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
            internal bool VelocityCaptured;
            /// <summary>B's velocity relative to A's at the first contact point, before the solver
            /// resolved the impact.</summary>
            internal Vector2 EnterRelativeVelocity;
        }

        static readonly Dictionary<PairKey, Pair> s_Pairs = new Dictionary<PairKey, Pair>();
        static readonly List<Pair> s_PairOrder = new List<Pair>();

        internal static Pair FindPair(Collider2D a, Collider2D b) =>
            s_Pairs.TryGetValue(new PairKey(a, b), out var pair) ? pair : null;

        /// <summary>The pairs a collider is part of.</summary>
        internal static IEnumerable<Pair> PairsOf(Collider2D c)
        {
            for (int i = 0; i < s_PairOrder.Count; i++)
            {
                var p = s_PairOrder[i];
                if (p.Touching > 0 && (p.A == (object)c || p.B == (object)c)) yield return p;
            }
        }

        sealed class Listener : IContactListener
        {
            internal void Clear()
            {
                s_Pairs.Clear();
                s_PairOrder.Clear();
            }

            public void BeginContact(Contact contact)
            {
                var a = contact.FixtureA.UserData as Collider2D;
                var b = contact.FixtureB.UserData as Collider2D;
                if (a == null || b == null) return;
                var key = new PairKey(a, b);
                if (!s_Pairs.TryGetValue(key, out var pair))
                {
                    pair = new Pair { A = a, B = b, FirstIsB = b.m_ContactOrder > a.m_ContactOrder };
                    s_Pairs[key] = pair;
                    s_PairOrder.Add(pair);
                }
                pair.IsTrigger = contact.FixtureA.IsSensor || contact.FixtureB.IsSensor;
                pair.Touching++;
                if (pair.Touching == 1)
                {
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
                var a = contact.FixtureA.UserData as Collider2D;
                var b = contact.FixtureB.UserData as Collider2D;
                if (a == null || b == null) return;
                Physics2DEffectors.PreSolve(contact, a, b);
                if (!s_Pairs.TryGetValue(new PairKey(a, b), out var pair) || pair.VelocityCaptured || !pair.EnterPending) return;
                contact.GetWorldManifold(out var world);
                var point = contact.Manifold.PointCount > 0 ? world.Points[0] : contact.FixtureA.Body.GetWorldCenter();
                var vA = contact.FixtureA.Body.GetLinearVelocityFromWorldPoint(point);
                var vB = contact.FixtureB.Body.GetLinearVelocityFromWorldPoint(point);
                var relative = ToUnity(vB - vA);
                pair.EnterRelativeVelocity = pair.A == (object)a ? relative : -relative;
                pair.VelocityCaptured = true;
            }

            public void PostSolve(Contact contact, in ContactImpulse impulse)
            {
                Physics2DEffectors.PostSolve(contact);
            }


            static void Forget(Pair pair)
            {
                s_Pairs.Remove(new PairKey(pair.A, pair.B));
                s_PairOrder.Remove(pair);
            }

            // Drain real EndContact notifications only after fixture/body removal is complete.
            // Reentrant removals wait until both sides of the current notification have run.
            internal void DispatchRemovals()
            {
                if (s_Dispatching) return;
                s_Dispatching = true;
                try
                {
                    while (true)
                    {
                        Pair next = null;
                        foreach (var pair in s_PairOrder)
                            if (pair.RemovalExit && pair.ExitPending) { next = pair; break; }
                        if (next == null) break;
                        next.RemovalExit = next.ExitPending = false;
                        Send(next, Phase.Exit);
                        if (next.Touching == 0 && !next.EnterPending) Forget(next);
                    }
                }
                finally { s_Dispatching = false; }
            }

            /// <summary>Sends the callbacks of the step that just ended.</summary>
            internal void Dispatch()
            {
                if (s_PairOrder.Count == 0) return;
                var pairs = s_PairOrder.ToArray();
                s_Dispatching = true;
                try
                {
                    // Native sends the trigger phase before the solid-contact phase, including
                    // reentry after a solid contact has already begun. Preserve order within each
                    // phase; creation order across the two kinds must not interleave callbacks.
                    for (int kind = 0; kind < 2; kind++)
                    foreach (var pair in pairs)
                    {
                        if (pair.IsTrigger != (kind == 0) || !s_PairOrder.Contains(pair)) continue;
                        bool enter = pair.EnterPending, exit = pair.ExitPending;
                        pair.EnterPending = false;
                        pair.ExitPending = false;
                        pair.RemovalExit = false;
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
                m_CollisionCollider = other,
                m_CollisionOtherCollider = self,
                m_CollisionRigidbody = other.m_AttachedBody,
                m_CollisionOtherRigidbody = self.m_AttachedBody,
            };
            // Unity's exit messages carry no contacts and no relative velocity.
            if (phase == Phase.Exit) return collision;
            var points = new List<ContactPoint2D>(2);
            bool enabled = CollectContacts(self, other, points, phase == Phase.Enter && pair.VelocityCaptured
                ? (flip ? -pair.EnterRelativeVelocity : pair.EnterRelativeVelocity)
                : (Vector2?)null);
            collision.m_CollisionContacts = points.ToArray();
            collision.m_CollisionContactCount = points.Count;
            collision.m_CollisionEnabled = enabled;
            collision.m_CollisionRelativeVelocity = phase == Phase.Enter && pair.VelocityCaptured
                ? (flip ? -pair.EnterRelativeVelocity : pair.EnterRelativeVelocity)
                : RelativeVelocity(self, other);
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

        /// <summary>The touching contact points between two colliders, seen from <paramref name="self"/>
        /// (normals point from <paramref name="other"/> towards <paramref name="self"/>). Returns
        /// whether the contact is enabled.</summary>
        internal static bool CollectContacts(Collider2D self, Collider2D other, List<ContactPoint2D> into, Vector2? relativeVelocity)
        {
            bool enabled = true;
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
                    for (int i = 0; i < count; i++)
                    {
                        var p = world.Points[i];
                        var mp = contact.Manifold.Points[i];
                        var rel = relativeVelocity ?? ToUnity(otherBody.GetLinearVelocityFromWorldPoint(p) - selfBody.GetLinearVelocityFromWorldPoint(p));
                        into.Add(new ContactPoint2D(ToUnity(p), normal, rel, contact.Friction, contact.Restitution, world.Separations[i],
                            mp.NormalImpulse, mp.TangentImpulse, otherCollider, self, contact.IsEnabled));
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
                    if (result is IEnumerator routine && mb.isActiveAndEnabled) Coroutines.Start(mb, routine, name);
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
