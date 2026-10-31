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
    /// </summary>
    internal static unsafe class PhysicsWorld
    {
        static uint s_World;
        static bool s_Registered;
        static bool s_StructureDirty;
        static bool s_AttachmentDirty;
        static bool s_Stepping;
        static readonly List<Rigidbody> s_Rigidbodies = new List<Rigidbody>();
        static readonly HashSet<Rigidbody> s_InitiallyKinematic = new HashSet<Rigidbody>();
        static readonly List<Collider> s_Colliders = new List<Collider>();
        static readonly Dictionary<ulong, Collider> s_ByShape = new Dictionary<ulong, Collider>();

        internal static bool IsStepping => s_Stepping;
        internal static IReadOnlyList<Rigidbody> Rigidbodies => s_Rigidbodies;
        internal static IReadOnlyList<Collider> Colliders => s_Colliders;

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
            s_World = B3.ua3_world_create(Physics.s_Gravity, Mathf.Max(0f, Physics.s_BounceThreshold), 1f, 1, settings);
            if (s_World == 0)
                throw new InvalidOperationException($"Unbound: could not create the PhysX scene for DynamicsManager (solver type {settings.SolverType}, friction type {settings.FrictionType}, contact pairs mode {settings.ContactPairsMode})");
            EnsureRegistered();
        }

        internal static void SetImprovedPatchFriction(bool value)
        {
            if (s_World != 0) B3.ua3_set_improved_patch_friction(value ? 1 : 0);
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
            if (s_World != 0) B3.ua3_world_destroy(s_World);
            s_World = 0;
            s_Rigidbodies.Clear();
            s_InitiallyKinematic.Clear();
            s_Colliders.Clear();
            s_ByShape.Clear();
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
            EnsureWorld();
            if (rb.m_Registered) return;
            rb.m_Registered = true;
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
            EnsureWorld();
            if (c.m_Registered) return;
            c.m_Registered = true;
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

        internal static void MarkShapeDirty(Collider c)
        {
            if (!c.m_Registered) return;
            if (c is BoxCollider box && c.m_Shape != 0 && ReferenceEquals(FindRigidbody(c), c.m_AttachedBody))
            {
                var matrix = c.m_ShapeMatrix;
                var scales = Collider.AxisScales(matrix);
                var size = box.m_Size;
                var half = Vector3.Scale(new Vector3(Mathf.Abs(size.x), Mathf.Abs(size.y), Mathf.Abs(size.z)) * .5f, scales);
                half = Vector3.Max(half, new Vector3(1e-4f, 1e-4f, 1e-4f));
                B3.ua3_shape_update_box(c.m_Shape, ShapeDefOf(c, c.m_AttachedBody), matrix.MultiplyPoint3x4(box.m_Center), Collider.AxisRotation(matrix), half);
                if (c.m_AttachedBody != null) c.m_AttachedBody.m_MassDirty = true;
                return;
            }
            c.m_ShapeDirty = true;
            s_StructureDirty = true;
        }

        internal static void MarkBodyDirty(Rigidbody rb)
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

        /// <summary>Brings bodies and shapes in line with the components (not with moved transforms;
        /// <see cref="SyncTransforms"/> does that).</summary>
        internal static void Flush()
        {
            if (!s_StructureDirty || s_World == 0 || s_Stepping) return;
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
            s_StructureDirty = true;
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
                IsBullet = rb.m_CollisionDetection != CollisionDetectionMode.Discrete ? 1 : 0,
            };
            rb.m_Body = B3.ua3_body_create(s_World, def);
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
        static void Rebuild(Collider c)
        {
            c.m_ShapeDirty = false;
            DestroyShape(c);
            var transform = c.m_GameObject.m_Transform;
            c.m_TransformVersion = transform.m_Version;
            c.m_Layer = c.m_GameObject.m_Layer;
            var rb = FindRigidbody(c);
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
                else
                {
                    B3.ua3_body_set_transform(c.m_StaticBody, framePosition, frameRotation);
                }
                body = c.m_StaticBody;
            }

            var matrix = PoseMatrix(framePosition, frameRotation).inverse * world;
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
            FilterOf(c, rb, out ulong category, out ulong mask);
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
            };
        }

        /// <summary>A collider's filter: its layer, and the layers its layer collides with as its
        /// own and its rigidbody's include/exclude layers adjust them. A rigidbody that does not
        /// detect collisions collides with nothing.</summary>
        static void FilterOf(Collider c, Rigidbody rb, out ulong category, out ulong mask)
        {
            int layer = c.m_GameObject.m_Layer & 31;
            category = 1UL << layer;
            uint bits = unchecked((uint)Physics.s_LayerCollisionMatrix[layer]);
            int include = c.m_IncludeLayers | (rb != null ? rb.m_IncludeLayers : 0);
            int exclude = c.m_ExcludeLayers | (rb != null ? rb.m_ExcludeLayers : 0);
            bits = (bits | unchecked((uint)include)) & ~unchecked((uint)exclude);
            if (rb != null && !rb.m_DetectCollisions) bits = 0;
            mask = bits;
        }

        /// <summary>The filter of a collider changed (layer, include/exclude layers, detectCollisions).</summary>
        internal static void Refilter(Collider c)
        {
            if (c.m_Shape == 0) return;
            FilterOf(c, c.m_AttachedBody, out var category, out var mask);
            if (c is CharacterController controller)
            {
                controller.SetFilter(category, mask);
                return;
            }
            B3.ua3_shape_set_filter(c.m_Shape, category, mask);
        }

        internal static void RefilterAll()
        {
            foreach (var c in s_Colliders) Refilter(c);
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

        /// <summary>Moves bodies and shapes to their transforms where the transforms changed.</summary>
        internal static void SyncTransforms()
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
                    Rebuild(c);
                    continue;
                }
                if (rb == null)
                {
                    if (c.m_StaticBody == 0)
                    {
                        Rebuild(c);
                        continue;
                    }
                    var position = t.position;
                    var rotation = t.rotation;
                    if (!SameShapeMatrix(ToBodyMatrix(t, position, rotation), c.m_ShapeMatrix))
                    {
                        Rebuild(c);
                        continue;
                    }
                    B3.ua3_body_set_transform(c.m_StaticBody, position, rotation);
                }
                else if (rb.m_Body != 0)
                {
                    var bodyTransform = rb.m_GameObject.m_Transform;
                    if (!SameShapeMatrix(ToBodyMatrix(t, bodyTransform.position, bodyTransform.rotation), c.m_ShapeMatrix)) Rebuild(c);
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
            if (s_World == 0) return;
            if (Physics.s_AutoSyncTransforms) SyncTransforms();
            else Flush();
        }

        // ------------------------------------------------------------------ simulation

        internal static void Simulate(float deltaTime)
        {
            if (s_Stepping)
            {
                Debug.LogError("Physics.Simulate cannot be called during a simulation step or its callbacks.");
                return;
            }
            if (s_World == 0 || deltaTime <= 0f) return;
            SyncTransforms();
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
            DispatchContactEvents();
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
                        WantsPublicContacts(ColliderOf(p.Shape0)) || WantsPublicContacts(ColliderOf(p.Shape1));
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
                var view = new Unity.Collections.NativeArray<ContactPairHeader>.ReadOnly(headerPtr, count, ref safety);
                Dispatch(s_PairOrder.ToArray(), triggers: true);
                var deliveries = PrepareCollisionMessages();
                try
                {
                    s_Stepping = true;
                    Physics.SendContactEvent(view);
                    foreach (var delivery in deliveries)
                        if (Physics.s_InvokeCollisionCallbacks)
                            Physics2DWorld.Deliver(delivery.Receiver, delivery.Name, typeof(Collision), delivery.Collision);
                }
                finally { s_Stepping = false; Unity.Collections.LowLevel.Unsafe.AtomicSafetyHandle.Release(safety); }
            }
        }
        static readonly HashSet<(ulong,ulong)> s_PublicContactPairs = new HashSet<(ulong,ulong)>();
        static readonly Dictionary<Type,bool> s_HasCollisionMessages = new Dictionary<Type,bool>();
        static bool WantsPublicContacts(Collider collider)
        {
            if (collider==null) return false;
            if (collider.m_ProvidesContacts) return true;
            return HasReceiver(collider.m_GameObject) || HasReceiver(collider.m_AttachedBody?.m_GameObject);
        }
        static bool HasReceiver(GameObject go)
        {
            if (go==null) return false;
            foreach (var component in go.m_Components)
            {
                if (!(component is MonoBehaviour) || component.m_Destroyed) continue;
                var type=component.GetType();
                if (!s_HasCollisionMessages.TryGetValue(type,out bool has))
                {
                    foreach (string name in s_CollisionMessages)
                        if (ScriptMessages.Find(type,name,new[]{typeof(Collision)})!=null || ScriptMessages.Find(type,name,Type.EmptyTypes)!=null)
                        { has=true; break; }
                    s_HasCollisionMessages[type]=has;
                }
                if (has) return true;
            }
            return false;
        }

        readonly struct ContactDelivery
        {
            internal readonly GameObject Receiver;
            internal readonly string Name;
            internal readonly Collision Collision;
            internal ContactDelivery(GameObject receiver, string name, Collision collision)
            { Receiver=receiver; Name=name; Collision=collision; }
        }
        static List<ContactDelivery> PrepareCollisionMessages()
        {
            var deliveries = new List<ContactDelivery>();
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
                    var collision=MakeCollision(pair,self,other,phase,flip);
                    var name=s_CollisionMessages[(int)phase];
                    deliveries.Add(new ContactDelivery(go,name,collision));
                    var rbGo=self.m_AttachedBody?.m_GameObject;
                    if (rbGo!=null && rbGo!=go && rbGo.activeInHierarchy)
                        deliveries.Add(new ContactDelivery(rbGo,name,collision));
                }
            }
        }

        static int ShapeInstanceId(ulong shape, bool removed) =>
            !removed && s_ByShape.TryGetValue(shape, out var c) && c != null ? c.GetInstanceID() : 0;
        static int BodyInstanceId(ulong actor, bool removed)
        {
            if (removed) return 0;
            foreach (var rb in s_Rigidbodies)
                if (rb.m_Body == actor && rb != null) return rb.GetInstanceID();
            return 0;
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
            if (s_World == 0 || key.First.m_Shape == 0 || key.Second.m_Shape == 0) return;
            B3.ua3_ignore_pair(s_World, key.First.m_Shape, key.Second.m_Shape, ignore ? 1 : 0);
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

        static readonly Dictionary<(PairKey Colliders, bool Trigger), Pair> s_Pairs = new Dictionary<(PairKey, bool), Pair>();
        static readonly List<Pair> s_PairOrder = new List<Pair>();
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
                var collision = MakeCollision(pair, self, other, phase, flip);
                Physics2DWorld.Deliver(go, name, typeof(Collision), collision);
                if (rbGo != null && rbGo != go && rbGo.activeInHierarchy) Physics2DWorld.Deliver(rbGo, name, typeof(Collision), collision);
            }
        }

        static Collision MakeCollision(Pair pair, Collider self, Collider other, Phase phase, bool flip)
        {
            var collision = new Collision
            {
                m_Collider = other,
                m_Rigidbody = other.m_AttachedBody,
                m_Self = self,
            };
            if (phase == Phase.Enter) collision.m_RelativeVelocity = flip ? -pair.EnterRelativeVelocity : pair.EnterRelativeVelocity;
            else if (phase == Phase.Exit) collision.m_RelativeVelocity = PreStepVelocity(other) - PreStepVelocity(self);
            else collision.m_RelativeVelocity = Velocity(other) - Velocity(self);
            if (phase == Phase.Exit)
            {
                collision.m_Contacts = Array.Empty<ContactPoint>();
                return collision;
            }
            var points = new List<ContactPoint>(4);
            CollectContacts(self, other, points);
            collision.m_Contacts = points.ToArray();
            var impulse = Vector3.zero;
            foreach (var p in points) impulse += p.m_Impulse;
            collision.m_Impulse = impulse;
            return collision;
        }

        static Vector3 Velocity(Collider c)
        {
            var rb = c.m_AttachedBody;
            return rb != null && rb.m_Body != 0 ? B3.ua3_body_get_linear_velocity(rb.m_Body) : Vector3.zero;
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
