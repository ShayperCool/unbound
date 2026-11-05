using System;
using System.Collections.Generic;
using Unbound.Box2D.Collision.Shapes;
using Unbound.Box2D.Dynamics;
using Unbound.Runtime;
using W = Unbound.Runtime.Physics2DWorld;

namespace UnityEngine
{
    /// <summary>
    /// A 2D rigid body: a Box2D body while its GameObject is active and it is simulated. Its state is
    /// kept here while there is no body, and given to the body when it is created.
    /// </summary>
    [ClassId(50)]
    [RequireComponent(typeof(Transform))]
    public sealed partial class Rigidbody2D : Component
    {
        // Serialized state.
        internal RigidbodyType2D m_BodyType = RigidbodyType2D.Dynamic;
        internal bool m_Simulated = true;
        internal bool m_UseFullKinematicContacts;
        internal bool m_UseAutoMass;
        internal float m_Mass = 1f;
        internal float m_LinearDamping;
        internal float m_AngularDamping = 0.05f;
        internal float m_GravityScale = 1f;
        internal PhysicsMaterial2D m_Material;
        internal int m_IncludeLayers;
        internal int m_ExcludeLayers;
        internal RigidbodyInterpolation2D m_Interpolate;
        internal RigidbodySleepMode2D m_SleepingMode = RigidbodySleepMode2D.StartAwake;
        internal CollisionDetectionMode2D m_CollisionDetection;
        internal RigidbodyConstraints2D m_Constraints;

        // Runtime state.
        internal Body m_Body;
        internal bool m_Registered;
        internal bool m_BodyDirty;
        internal bool m_MassDirty;
        internal Vector2 m_PendingVelocity;
        internal float m_PendingAngularVelocity;
        internal float m_PendingInertia;
        internal Vector2? m_CustomCenterOfMass;
        internal float? m_CustomInertia;
        /// <summary>The transform version the body last matched (changes after it are the script's).</summary>
        internal int m_TransformVersion = -1;
        /// <summary>The pose the transform shows, as last written or read by physics.</summary>
        internal Vector2 m_SyncPosition;
        internal float m_SyncAngle;
        internal Vector2 m_PreviousPosition, m_CurrentPosition;
        internal float m_PreviousAngle, m_CurrentAngle;
        bool m_HasMovePosition, m_HasMoveRotation;
        Vector2 m_MovePosition;
        float m_MoveRotation;
        bool m_RestoreVelocity;
        Vector2 m_SavedVelocity;
        float m_SavedAngularVelocity;
        /// <summary>The current step does not simulate it (its layer is not a simulation layer).</summary>
        internal bool m_SimulationHeld;

        public Rigidbody2D() { }

        internal override void OnEngineEnable() => W.Add(this);

        internal override void OnEngineDisable() => W.Remove(this);

        /// <summary>Destroying it removes it too (components without enabled state get no disable).</summary>
        internal override void OnEngineDestroy() => W.Remove(this);

        internal static BodyType ToB2(RigidbodyType2D type) => type switch
        {
            RigidbodyType2D.Kinematic => BodyType.KinematicBody,
            RigidbodyType2D.Static => BodyType.StaticBody,
            _ => BodyType.DynamicBody,
        };

        /// <summary>The body, created now if the rigidbody is simulated and not yet in the world.</summary>
        internal Body Body
        {
            get
            {
                if (m_Registered && m_Body == null) W.Flush();
                return m_Body;
            }
        }

        void Changed()
        {
            if (m_Registered) W.MarkBodyDirty(this);
        }

        /// <summary>Pushes the serialized settings to the body (after one of them changed).</summary>
        internal void ApplyBodySettings()
        {
            var body = m_Body;
            if (body == null) return;
            var type = ToB2(m_BodyType);
            if (body.BodyType != type)
            {
                body.BodyType = type;
                m_MassDirty = true;
                foreach (var c in W.Colliders)
                    if (ReferenceEquals(c.m_AttachedBody, this)) W.Refilter(c);
            }
            body.LinearDamping = Mathf.Max(0f, m_LinearDamping);
            body.AngularDamping = Mathf.Max(0f, m_AngularDamping);
            body.GravityScale = m_GravityScale;
            body.IsBullet = m_CollisionDetection == CollisionDetectionMode2D.Continuous;
            body.IsSleepingAllowed = m_SleepingMode != RigidbodySleepMode2D.NeverSleep;
            bool fixedRotation = (m_Constraints & RigidbodyConstraints2D.FreezeRotation) != 0;
            if (body.IsFixedRotation != fixedRotation)
            {
                body.IsFixedRotation = fixedRotation;
                m_MassDirty = true;
            }
            body.FreezeX = (m_Constraints & RigidbodyConstraints2D.FreezePositionX) != 0;
            body.FreezeY = (m_Constraints & RigidbodyConstraints2D.FreezePositionY) != 0;
            if (body.FreezeX || body.FreezeY)
            {
                var v = body.LinearVelocity;
                if (body.FreezeX) v.X = 0f;
                if (body.FreezeY) v.Y = 0f;
                body.SetLinearVelocity(v);
            }
            if (m_MassDirty) ApplyMass();
        }

        /// <summary>The body's mass: from the colliders' densities (useAutoMass) or the set mass,
        /// with the colliders' center and the inertia their shapes give that mass.</summary>
        internal void ApplyMass()
        {
            m_MassDirty = false;
            var body = m_Body;
            if (body == null || body.BodyType != BodyType.DynamicBody) return;
            // Mass of the shapes, as Box2D sums them.
            float mass = 0f, inertia = 0f;
            var center = System.Numerics.Vector2.Zero;
            foreach (var f in body.Fixtures)
            {
                if (f.Density <= 0f || f.IsSensor) continue;
                f.GetMassData(out var md);
                mass += md.Mass;
                center += md.Mass * md.Center;
                inertia += md.RotationInertia;
            }
            if (mass > 0f) center /= mass;
            float centralInertia = inertia - mass * System.Numerics.Vector2.Dot(center, center);
            float targetMass = m_UseAutoMass ? mass : m_Mass;
            if (targetMass <= 0f) targetMass = m_UseAutoMass ? 1f : 1e-4f;
            if (mass > 0f) centralInertia *= targetMass / mass;
            // A body whose colliders give no mass turns as Unity's does: inertia 1.
            else centralInertia = 1f;
            if (m_CustomCenterOfMass.HasValue) center = W.ToB2(m_CustomCenterOfMass.Value);
            if (m_CustomInertia.HasValue) centralInertia = m_CustomInertia.Value;
            if ((m_Constraints & RigidbodyConstraints2D.FreezeRotation) != 0) centralInertia = 0f;
            body.SetMassData(new MassData
            {
                Mass = targetMass,
                Center = center,
                RotationInertia = centralInertia > 0f ? centralInertia + targetMass * System.Numerics.Vector2.Dot(center, center) : 0f,
            });
            m_PendingInertia = body.Inertia - body.Mass * body.GetLocalCenter().LengthSquared();
        }

        // ------------------------------------------------------------------ pose

        public Vector2 position
        {
            get
            {
                var body = Body;
                if (body != null) return W.ToUnity(body.GetPosition());
                var p = transform.position;
                return new Vector2(p.x, p.y);
            }
            set
            {
                var body = Body;
                if (body == null)
                {
                    var p = transform.position;
                    transform.position = new Vector3(value.x, value.y, p.z);
                    return;
                }
                body.SetTransform(W.ToB2(value), body.GetAngle());
                if (body.BodyType != BodyType.StaticBody) body.IsAwake = true;
                m_CurrentPosition = m_PreviousPosition = value;
            }
        }

        public float rotation
        {
            get
            {
                var body = Body;
                if (body != null) return body.GetAngle() * Mathf.Rad2Deg;
                W.PoseOf(transform, out _, out var angle);
                return angle * Mathf.Rad2Deg;
            }
            set => SetRotation(value);
        }

        public void SetRotation(float angle)
        {
            var body = Body;
            if (body == null)
            {
                transform.rotation = Quaternion.Euler(0f, 0f, angle);
                return;
            }
            body.SetTransform(body.GetPosition(), angle * Mathf.Deg2Rad);
            if (body.BodyType != BodyType.StaticBody) body.IsAwake = true;
            m_CurrentAngle = m_PreviousAngle = angle * Mathf.Deg2Rad;
        }

        public void SetRotation(Quaternion rotation) => SetRotation(ZAngle(rotation));

        static float ZAngle(Quaternion rotation)
        {
            return W.RotationAngle(rotation) * Mathf.Rad2Deg;
        }

        public void MovePosition(Vector2 position)
        {
            m_HasMovePosition = true;
            m_MovePosition = position;
        }

        public void MoveRotation(float angle)
        {
            m_HasMoveRotation = true;
            // Native stores the pending target in radians. Converting the
            // current angle back to degrees before subtraction loses precision.
            m_MoveRotation = angle * Mathf.Deg2Rad;
        }

        public void MoveRotation(Quaternion rotation) => MoveRotation(ZAngle(rotation));

        public void MovePositionAndRotation(Vector2 position, float angle)
        {
            MovePosition(position);
            MoveRotation(angle);
        }

        public void MovePositionAndRotation(Vector2 position, Quaternion rotation) => MovePositionAndRotation(position, ZAngle(rotation));

        /// <summary>Before a step: the velocity that takes the body to its MovePosition/MoveRotation targets.</summary>
        internal void BeginMove(float deltaTime)
        {
            m_RestoreVelocity = false;
            if (!m_HasMovePosition && !m_HasMoveRotation) return;
            var body = m_Body;
            if (body.BodyType == BodyType.StaticBody || deltaTime <= 0f)
            {
                m_HasMovePosition = m_HasMoveRotation = false;
                return;
            }
            m_SavedVelocity = W.ToUnity(body.LinearVelocity);
            m_SavedAngularVelocity = body.AngularVelocity;
            m_RestoreVelocity = true;
            if (m_HasMovePosition)
            {
                // The body origin moves to the target: its center of mass follows.
                var origin = W.ToUnity(body.GetPosition());
                body.SetLinearVelocity(W.ToB2((m_MovePosition - origin) / deltaTime));
            }
            if (m_HasMoveRotation)
            {
                float delta = m_MoveRotation - body.GetAngle();
                if (delta > Mathf.PI || delta < -Mathf.PI)
                {
                    delta %= 2f * Mathf.PI;
                    if (delta > Mathf.PI) delta -= 2f * Mathf.PI;
                    else if (delta < -Mathf.PI) delta += 2f * Mathf.PI;
                }
                body.SetAngularVelocity(delta / deltaTime);
            }
            body.IsAwake = true;
            m_HasMovePosition = m_HasMoveRotation = false;
        }

        internal void EndMove()
        {
            if (!m_RestoreVelocity) return;
            m_RestoreVelocity = false;
            var body = m_Body;
            if (body.BodyType == BodyType.KinematicBody)
            {
                body.SetLinearVelocity(W.ToB2(m_SavedVelocity));
                body.SetAngularVelocity(m_SavedAngularVelocity);
            }
        }

        public Matrix4x4 localToWorldMatrix
        {
            get
            {
                var body = Body;
                Vector2 p;
                float angle;
                if (body != null)
                {
                    p = W.ToUnity(body.GetPosition());
                    angle = body.GetAngle();
                }
                else W.PoseOf(transform, out p, out angle);
                return Matrix4x4.TRS(new Vector3(p.x, p.y, 0f),
                    Quaternion.FromEulerRad(new Vector3(0f, 0f, angle)), Vector3.one);
            }
        }

        // ------------------------------------------------------------------ motion

        public Vector2 linearVelocity
        {
            get
            {
                var body = Body;
                return body != null ? W.ToUnity(body.LinearVelocity) : m_PendingVelocity;
            }
            set
            {
                var body = Body;
                if (body == null)
                {
                    m_PendingVelocity = value;
                    return;
                }
                if (body.BodyType == BodyType.StaticBody) return;
                body.SetLinearVelocity(W.ToB2(value));
            }
        }

        public float linearVelocityX
        {
            get => linearVelocity.x;
            set => linearVelocity = new Vector2(value, linearVelocity.y);
        }

        public float linearVelocityY
        {
            get => linearVelocity.y;
            set => linearVelocity = new Vector2(linearVelocity.x, value);
        }

        public Vector2 velocity
        {
            get => linearVelocity;
            set => linearVelocity = value;
        }

        public float velocityX
        {
            get => linearVelocityX;
            set => linearVelocityX = value;
        }

        public float velocityY
        {
            get => linearVelocityY;
            set => linearVelocityY = value;
        }

        /// <summary>Degrees per second.</summary>
        public float angularVelocity
        {
            get
            {
                var body = Body;
                return body != null ? body.AngularVelocity * Mathf.Rad2Deg : m_PendingAngularVelocity;
            }
            set
            {
                var body = Body;
                if (body == null)
                {
                    m_PendingAngularVelocity = value;
                    return;
                }
                if (body.BodyType == BodyType.StaticBody) return;
                body.SetAngularVelocity(value * Mathf.Deg2Rad);
            }
        }

        public Vector2 totalForce
        {
            get
            {
                var body = Body;
                return body != null ? W.ToUnity(body.Force) : Vector2.zero;
            }
            set
            {
                var body = Body;
                if (body == null || body.BodyType != BodyType.DynamicBody) return;
                body.Force = W.ToB2(value);
                if (value != Vector2.zero) body.IsAwake = true;
            }
        }

        public float totalTorque
        {
            get
            {
                var body = Body;
                return body != null ? body.Torque : 0f;
            }
            set
            {
                var body = Body;
                if (body == null || body.BodyType != BodyType.DynamicBody) return;
                body.Torque = value;
                if (value != 0f) body.IsAwake = true;
            }
        }

        // ------------------------------------------------------------------ mass

        public bool useAutoMass
        {
            get => m_UseAutoMass;
            set
            {
                m_UseAutoMass = value;
                MassChanged();
            }
        }

        public float mass
        {
            get
            {
                if (m_UseAutoMass)
                {
                    var body = Body;
                    if (body != null && body.BodyType == BodyType.DynamicBody)
                    {
                        if (m_MassDirty) ApplyMass();
                        return body.Mass;
                    }
                }
                return m_Mass;
            }
            set
            {
                if (m_UseAutoMass)
                {
                    Debug.LogWarning("Cannot set the mass of a Rigidbody2D that uses auto mass.", this);
                    return;
                }
                m_Mass = Mathf.Clamp(value, 0.0001f, 1000000f);
                MassChanged();
            }
        }

        void MassChanged()
        {
            m_MassDirty = true;
            if (m_Body != null) ApplyMass();
        }

        public Vector2 centerOfMass
        {
            get
            {
                var body = Body;
                if (body == null) return m_CustomCenterOfMass ?? Vector2.zero;
                if (m_MassDirty) ApplyMass();
                return W.ToUnity(body.GetLocalCenter());
            }
            set
            {
                m_CustomCenterOfMass = value;
                MassChanged();
            }
        }

        public Vector2 worldCenterOfMass
        {
            get
            {
                var body = Body;
                if (body == null) return position;
                if (m_MassDirty) ApplyMass();
                return W.ToUnity(body.GetWorldCenter());
            }
        }

        public float inertia
        {
            get
            {
                var body = Body;
                if (body == null) return m_CustomInertia ?? m_PendingInertia;
                if (body.BodyType != BodyType.DynamicBody) return m_CustomInertia ?? 0f;
                if (m_MassDirty) ApplyMass();
                return body.Inertia - body.Mass * body.GetLocalCenter().LengthSquared();
            }
            set
            {
                m_CustomInertia = Mathf.Max(0f, value);
                MassChanged();
            }
        }

        // ------------------------------------------------------------------ settings

        public float linearDamping
        {
            get => m_LinearDamping;
            set
            {
                m_LinearDamping = Mathf.Max(0f, value);
                if (m_Body != null) m_Body.LinearDamping = m_LinearDamping;
            }
        }

        public float angularDamping
        {
            get => m_AngularDamping;
            set
            {
                m_AngularDamping = Mathf.Max(0f, value);
                if (m_Body != null) m_Body.AngularDamping = m_AngularDamping;
            }
        }

        public float drag
        {
            get => linearDamping;
            set => linearDamping = value;
        }

        public float angularDrag
        {
            get => angularDamping;
            set => angularDamping = value;
        }

        public float gravityScale
        {
            get => m_GravityScale;
            set
            {
                m_GravityScale = value;
                if (m_Body != null)
                {
                    m_Body.GravityScale = value;
                    m_Body.IsAwake = true;
                }
            }
        }

        public RigidbodyType2D bodyType
        {
            get => m_BodyType;
            set
            {
                if (m_BodyType == value) return;
                m_BodyType = value;
                Changed();
                W.Flush();
            }
        }

        public bool isKinematic
        {
            get => m_BodyType == RigidbodyType2D.Kinematic;
            set => bodyType = value ? RigidbodyType2D.Kinematic : RigidbodyType2D.Dynamic;
        }

        public bool useFullKinematicContacts
        {
            get => m_UseFullKinematicContacts;
            set
            {
                if (m_UseFullKinematicContacts == value) return;
                m_UseFullKinematicContacts = value;
                RefilterColliders();
            }
        }

        void RefilterColliders()
        {
            if (!m_Registered) return;
            W.Flush();
            foreach (var c in W.Colliders)
                if (ReferenceEquals(c.m_AttachedBody, this)) W.Refilter(c);
        }

        public bool freezeRotation
        {
            get => (m_Constraints & RigidbodyConstraints2D.FreezeRotation) != 0;
            set => constraints = value ? m_Constraints | RigidbodyConstraints2D.FreezeRotation : m_Constraints & ~RigidbodyConstraints2D.FreezeRotation;
        }

        public RigidbodyConstraints2D constraints
        {
            get => m_Constraints;
            set
            {
                if (m_Constraints == value) return;
                m_Constraints = value;
                Changed();
                W.Flush();
            }
        }

        public bool simulated
        {
            get => m_Simulated;
            set
            {
                if (m_Simulated == value) return;
                m_Simulated = value;
                Changed();
                W.Flush();
            }
        }

        public RigidbodyInterpolation2D interpolation
        {
            get => m_Interpolate;
            set => m_Interpolate = value;
        }

        public RigidbodySleepMode2D sleepMode
        {
            get => m_SleepingMode;
            set
            {
                m_SleepingMode = value;
                if (m_Body != null)
                {
                    m_Body.IsSleepingAllowed = value != RigidbodySleepMode2D.NeverSleep;
                    if (value == RigidbodySleepMode2D.NeverSleep) m_Body.IsAwake = true;
                }
            }
        }

        public CollisionDetectionMode2D collisionDetectionMode
        {
            get => m_CollisionDetection;
            set
            {
                m_CollisionDetection = value;
                if (m_Body != null) m_Body.IsBullet = value == CollisionDetectionMode2D.Continuous;
            }
        }

        public PhysicsMaterial2D sharedMaterial
        {
            get => m_Material;
            set
            {
                m_Material = value;
                if (m_GameObject == null) return;
                foreach (var c in m_GameObject.GetComponentsInChildren<Collider2D>(true))
                    if (c.m_Material == null && ReferenceEquals(c.FindMaterialBody(), this)) c.CaptureMaterial();
            }
        }

        public LayerMask excludeLayers
        {
            get => m_ExcludeLayers;
            set
            {
                m_ExcludeLayers = value;
                RefilterColliders();
            }
        }

        public LayerMask includeLayers
        {
            get => m_IncludeLayers;
            set
            {
                m_IncludeLayers = value;
                RefilterColliders();
            }
        }

        public bool fixedAngle
        {
            get => freezeRotation;
            set => freezeRotation = value;
        }

        // ------------------------------------------------------------------ sleep

        public bool IsSleeping()
        {
            var body = Body;
            return body != null && !body.IsAwake;
        }

        public bool IsAwake() => !IsSleeping();

        public void Sleep()
        {
            var body = Body;
            if (body != null) body.IsAwake = false;
        }

        public void WakeUp()
        {
            var body = Body;
            if (body != null) body.IsAwake = true;
        }

        // ------------------------------------------------------------------ forces

        public void AddForce(Vector2 force) => AddForce(force, ForceMode2D.Force);

        public void AddForce(Vector2 force, ForceMode2D mode = ForceMode2D.Force)
        {
            var body = Body;
            if (body == null || body.BodyType != BodyType.DynamicBody) return;
            if (mode == ForceMode2D.Impulse) body.ApplyLinearImpulseToCenter(W.ToB2(force), true);
            else body.ApplyForceToCenter(W.ToB2(force), true);
        }

        public void AddForceX(float force, ForceMode2D mode = ForceMode2D.Force) => AddForce(new Vector2(force, 0f), mode);

        public void AddForceY(float force, ForceMode2D mode = ForceMode2D.Force) => AddForce(new Vector2(0f, force), mode);

        public void AddRelativeForce(Vector2 relativeForce) => AddRelativeForce(relativeForce, ForceMode2D.Force);

        public void AddRelativeForce(Vector2 relativeForce, ForceMode2D mode = ForceMode2D.Force) => AddForce(GetRelativeVector(relativeForce), mode);

        public void AddRelativeForceX(float force, ForceMode2D mode = ForceMode2D.Force) => AddRelativeForce(new Vector2(force, 0f), mode);

        public void AddRelativeForceY(float force, ForceMode2D mode = ForceMode2D.Force) => AddRelativeForce(new Vector2(0f, force), mode);

        public void AddForceAtPosition(Vector2 force, Vector2 position) => AddForceAtPosition(force, position, ForceMode2D.Force);

        public void AddForceAtPosition(Vector2 force, Vector2 position, ForceMode2D mode)
        {
            var body = Body;
            if (body == null || body.BodyType != BodyType.DynamicBody) return;
            if (m_MassDirty) ApplyMass();
            if (mode == ForceMode2D.Impulse) body.ApplyLinearImpulse(W.ToB2(force), W.ToB2(position), true);
            else body.ApplyForce(W.ToB2(force), W.ToB2(position), true);
        }

        public void AddTorque(float torque) => AddTorque(torque, ForceMode2D.Force);

        public void AddTorque(float torque, ForceMode2D mode)
        {
            var body = Body;
            if (body == null || body.BodyType != BodyType.DynamicBody) return;
            if (mode == ForceMode2D.Impulse) body.ApplyAngularImpulse(torque, true);
            else body.ApplyTorque(torque, true);
        }

        // ------------------------------------------------------------------ space conversion

        public Vector2 GetPoint(Vector2 point)
        {
            var body = Body;
            if (body == null) return (Vector2)transform.InverseTransformPoint(point);
            return W.ToUnity(body.GetLocalPoint(W.ToB2(point)));
        }

        public Vector2 GetRelativePoint(Vector2 relativePoint)
        {
            var body = Body;
            if (body == null) return (Vector2)transform.TransformPoint(relativePoint);
            return W.ToUnity(body.GetWorldPoint(W.ToB2(relativePoint)));
        }

        public Vector2 GetVector(Vector2 vector)
        {
            var body = Body;
            if (body == null) return (Vector2)transform.InverseTransformDirection(vector);
            return W.ToUnity(body.GetLocalVector(W.ToB2(vector)));
        }

        public Vector2 GetRelativeVector(Vector2 relativeVector)
        {
            var body = Body;
            if (body == null) return (Vector2)transform.TransformDirection(relativeVector);
            return W.ToUnity(body.GetWorldVector(W.ToB2(relativeVector)));
        }

        public Vector2 GetPointVelocity(Vector2 point)
        {
            var body = Body;
            if (body == null) return m_PendingVelocity;
            if (m_MassDirty) ApplyMass();
            return W.ToUnity(body.GetLinearVelocityFromWorldPoint(W.ToB2(point)));
        }

        public Vector2 GetRelativePointVelocity(Vector2 relativePoint)
        {
            var body = Body;
            if (body == null) return m_PendingVelocity;
            if (m_MassDirty) ApplyMass();
            return W.ToUnity(body.GetLinearVelocityFromLocalPoint(W.ToB2(relativePoint)));
        }

        // ------------------------------------------------------------------ colliders and contacts

        public int attachedColliderCount
        {
            get
            {
                W.Flush();
                int count = 0;
                foreach (var c in W.Colliders)
                    // Colliders a composite merges are not attached themselves (measured).
                    if (ReferenceEquals(c.m_AttachedBody, this) && !c.HasComposite()) count++;
                return count;
            }
        }

        /// <summary>The colliders attached to this body.</summary>
        internal List<Collider2D> AttachedColliders(bool findTriggers)
        {
            W.Flush();
            var list = new List<Collider2D>();
            foreach (var c in W.Colliders)
                if (ReferenceEquals(c.m_AttachedBody, this) && (findTriggers || !c.m_IsTrigger)) list.Add(c);
            return list;
        }

        public int GetAttachedColliders(Collider2D[] results) => GetAttachedColliders(results, true);

        public int GetAttachedColliders(List<Collider2D> results) => GetAttachedColliders(results, true);

        public int GetAttachedColliders(Collider2D[] results, bool findTriggers = true)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            var list = AttachedColliders(findTriggers);
            int count = Math.Min(list.Count, results.Length);
            for (int i = 0; i < count; i++) results[i] = list[i];
            return count;
        }

        public int GetAttachedColliders(List<Collider2D> results, bool findTriggers = true)
        {
            if (results == null) throw new ArgumentNullException(nameof(results));
            results.Clear();
            results.AddRange(AttachedColliders(findTriggers));
            return results.Count;
        }

        public bool IsTouching(Collider2D collider)
        {
            if (collider == null) throw new ArgumentNullException(nameof(collider));
            foreach (var c in AttachedColliders(true))
                if (Physics2D.IsTouching(c, collider)) return true;
            return false;
        }

        public bool IsTouching(Collider2D collider, ContactFilter2D contactFilter)
        {
            foreach (var c in AttachedColliders(true))
                if (Physics2D.IsTouching(c, collider, contactFilter)) return true;
            return false;
        }

        public bool IsTouching(ContactFilter2D contactFilter)
        {
            foreach (var c in AttachedColliders(true))
                if (Physics2D.IsTouching(c, contactFilter)) return true;
            return false;
        }

        public bool IsTouchingLayers() => IsTouchingLayers(Physics2D.AllLayers);

        public bool IsTouchingLayers(int layerMask = -1)
        {
            foreach (var c in AttachedColliders(true))
                if (Physics2D.IsTouchingLayers(c, layerMask)) return true;
            return false;
        }

        public bool OverlapPoint(Vector2 point)
        {
            foreach (var c in AttachedColliders(true))
                if (c.OverlapPoint(point)) return true;
            return false;
        }

        public ColliderDistance2D Distance(Collider2D collider)
        {
            if (collider == null) throw new ArgumentNullException(nameof(collider));
            var best = new ColliderDistance2D { isValid = false };
            foreach (var c in AttachedColliders(true))
            {
                var d = Physics2D.Distance(c, collider);
                if (d.isValid && (!best.isValid || d.distance < best.distance)) best = d;
            }
            return best;
        }

        public ColliderDistance2D Distance(Vector2 thisPosition, float thisAngle, Collider2D collider, Vector2 position, float angle)
        {
            if (collider == null) throw new ArgumentNullException(nameof(collider));
            var best = new ColliderDistance2D { isValid = false };
            var body = Body;
            if (body == null) return best;
            var pose = new Unbound.Box2D.Common.Transform(W.ToB2(thisPosition), thisAngle * Mathf.Deg2Rad);
            var otherPose = new Unbound.Box2D.Common.Transform(W.ToB2(position), angle * Mathf.Deg2Rad);
            foreach (var c in AttachedColliders(true))
            {
                var d = Physics2DQueries.Distance(c, pose, collider, otherPose, overrideB: true);
                if (d.isValid && (!best.isValid || d.distance < best.distance)) best = d;
            }
            return best;
        }

        public Vector2 ClosestPoint(Vector2 position) => Physics2D.ClosestPoint(position, this);

        public int GetContacts(ContactPoint2D[] contacts) => Physics2D.GetContacts(this, new ContactFilter2D().NoFilter(), contacts);

        public int GetContacts(List<ContactPoint2D> contacts) => Physics2D.GetContacts(this, new ContactFilter2D().NoFilter(), contacts);

        public int GetContacts(ContactFilter2D contactFilter, ContactPoint2D[] contacts) => Physics2D.GetContacts(this, contactFilter, contacts);

        public int GetContacts(ContactFilter2D contactFilter, List<ContactPoint2D> contacts) => Physics2D.GetContacts(this, contactFilter, contacts);

        public int GetContacts(Collider2D[] colliders) => Physics2D.GetContacts(this, new ContactFilter2D().NoFilter(), colliders);

        public int GetContacts(List<Collider2D> colliders) => Physics2D.GetContacts(this, new ContactFilter2D().NoFilter(), colliders);

        public int GetContacts(ContactFilter2D contactFilter, Collider2D[] colliders) => Physics2D.GetContacts(this, contactFilter, colliders);

        public int GetContacts(ContactFilter2D contactFilter, List<Collider2D> colliders) => Physics2D.GetContacts(this, contactFilter, colliders);

        // ------------------------------------------------------------------ queries

        public int Cast(Vector2 direction, RaycastHit2D[] results) => Cast(direction, Physics2D.DefaultCastFilter(m_GameObject), results, float.PositiveInfinity);

        public int Cast(Vector2 direction, RaycastHit2D[] results, float distance) => Cast(direction, Physics2D.DefaultCastFilter(m_GameObject), results, distance);

        public int Cast(Vector2 direction, List<RaycastHit2D> results, float distance = float.PositiveInfinity) =>
            Cast(direction, Physics2D.DefaultCastFilter(m_GameObject), results, distance);

        public int Cast(Vector2 direction, ContactFilter2D contactFilter, RaycastHit2D[] results) => Cast(direction, contactFilter, results, float.PositiveInfinity);

        public int Cast(Vector2 direction, ContactFilter2D contactFilter, RaycastHit2D[] results, float distance = float.PositiveInfinity)
        {
            var hits = Physics2DQueries.CastColliders(AttachedColliders(true), null, direction, distance, contactFilter, this);
            return Physics2DQueries.CopyHits(hits, results);
        }

        public int Cast(Vector2 direction, ContactFilter2D contactFilter, List<RaycastHit2D> results, float distance = float.PositiveInfinity)
        {
            var hits = Physics2DQueries.CastColliders(AttachedColliders(true), null, direction, distance, contactFilter, this);
            return Physics2DQueries.CopyHits(hits, results);
        }

        public int Cast(Vector2 position, float angle, Vector2 direction, List<RaycastHit2D> results, float distance = float.PositiveInfinity) =>
            Cast(position, angle, direction, Physics2D.DefaultCastFilter(m_GameObject), results, distance);

        public int Cast(Vector2 position, float angle, Vector2 direction, ContactFilter2D contactFilter, List<RaycastHit2D> results, float distance = float.PositiveInfinity)
        {
            var pose = new Unbound.Box2D.Common.Transform(W.ToB2(position), angle * Mathf.Deg2Rad);
            var hits = Physics2DQueries.CastColliders(AttachedColliders(true), pose, direction, distance, contactFilter, this);
            return Physics2DQueries.CopyHits(hits, results);
        }

        public int Overlap(ContactFilter2D contactFilter, Collider2D[] results) =>
            Physics2DQueries.CopyColliders(Physics2DQueries.OverlapColliders(AttachedColliders(true), null, contactFilter, this), results);

        public int Overlap(List<Collider2D> results) => Overlap(new ContactFilter2D().NoFilter(), results);

        public int Overlap(ContactFilter2D contactFilter, List<Collider2D> results) =>
            Physics2DQueries.CopyColliders(Physics2DQueries.OverlapColliders(AttachedColliders(true), null, contactFilter, this), results);

        public int Overlap(Vector2 position, float angle, List<Collider2D> results) => Overlap(position, angle, new ContactFilter2D().NoFilter(), results);

        public int Overlap(Vector2 position, float angle, ContactFilter2D contactFilter, List<Collider2D> results)
        {
            var pose = new Unbound.Box2D.Common.Transform(W.ToB2(position), angle * Mathf.Deg2Rad);
            return Physics2DQueries.CopyColliders(Physics2DQueries.OverlapColliders(AttachedColliders(true), pose, contactFilter, this), results);
        }

        public int OverlapCollider(ContactFilter2D contactFilter, Collider2D[] results) => Overlap(contactFilter, results);

        public int OverlapCollider(ContactFilter2D contactFilter, List<Collider2D> results) => Overlap(contactFilter, results);

        // ------------------------------------------------------------------ slide

        // Rigidbody2D.Slide, from the Scripting API 6000.0 pages Rigidbody2D.Slide,
        // Rigidbody2D.SlideMovement (and each of its members) and Rigidbody2D.SlideResults.
        // Documented: the velocity is integrated over deltaTime; a contact changes the direction
        // without friction or bounce and the rest of the movement continues (one iteration, at
        // most maxIterations); surfaceSlideAngle limits sliding, measured in degrees from
        // surfaceUp; surfaceAnchor (default down, zero disables it) is looked along for a
        // surface on each iteration; gravity (default (0, -9.81)) moves the result by
        // gravity * deltaTime afterwards, stopping at a contact; gravity slips along a surface
        // only when no velocity was given; a zero surfaceUp always slides and slips;
        // useSimulationMove moves with MovePosition, otherwise the Rigidbody2D
        // and its Transform move at once; useNoMove moves nothing (and wins over
        // useSimulationMove); selectedCollider (one attached collider) overrides
        // useAttachedTriggers; useLayerMask's layerMask replaces the layer collision matrix.
        // The pages conflict on the two angles, and no Reference capture settles them: the
        // surfaceSlideAngle page's description slides below the angle while its remark speaks of
        // slopes above it, and the Slide page slips below gravitySlipAngle while the
        // gravitySlipAngle and gravity pages slip above it. Unbound follows the property
        // descriptions (slide below surfaceSlideAngle, slip above gravitySlipAngle); that is an
        // unmeasured reading of conflicting pages, not a settled Unity guarantee.
        // Unbound's choices where the pages are silent, none measured against Unity (no Reference
        // capture exists): see the comments below and in SlideMovement().

        /// <summary>The first <see cref="Slide"/> call in the process warns once that SlideMovement
        /// and SlideResults may not be serialized as Unity does (0 until then, 1 after).</summary>
        static int s_SlideSerializationWarned;

        /// <summary>Unity's serialized backing field names for SlideMovement and SlideResults, and
        /// the SlideMovement constructor defaults the pages do not state, are not verified against
        /// Unity, so values authored in the Inspector may not load. Nothing here looks at the YAML;
        /// the warning only says that the support is unverified.</summary>
        internal const string SlideSerializationWarning =
            "Unbound: Rigidbody2D.Slide is used, but the Unity-serialized backing field names of "
            + "Rigidbody2D.SlideMovement and Rigidbody2D.SlideResults are unverified, and so are the "
            + "undocumented SlideMovement constructor defaults (maxIterations, surfaceSlideAngle, "
            + "gravitySlipAngle, layerMask and the use flags). Values authored in the Inspector for "
            + "these structs may not be preserved when a scene or prefab loads; Slide then runs with "
            + "Unbound's constructor defaults or zeros instead.";

        /// <summary>Test only: sets whether the Slide warning was already logged and returns the
        /// previous state, so a test can see the first use and then put the state back.</summary>
        internal static bool ResetSlideWarningForTests(bool logged = false) =>
            System.Threading.Interlocked.Exchange(ref s_SlideSerializationWarned, logged ? 1 : 0) != 0;

        /// <summary>A cast meets a surface when it moves into it: against its normal by more than
        /// this cosine. Contacts the movement only grazes (a floor under a horizontal move) do not
        /// stop it. Unbound's tolerance.</summary>
        const float SlideApproach = 1e-4f;

        public SlideResults Slide(Vector2 velocity, float deltaTime, SlideMovement slideMovement)
        {
            // Fail loudly, once, before any argument check or movement: whatever the values,
            // they may not be the ones authored in the Inspector (see SlideSerializationWarning).
            if (System.Threading.Interlocked.Exchange(ref s_SlideSerializationWarned, 1) == 0)
                Debug.LogWarning(SlideSerializationWarning, this);
            var body = Body;
            var start = slideMovement.useStartPosition ? slideMovement.startPosition : position;
            var results = new SlideResults { position = start, remainingVelocity = velocity };
            // Unbound's choices: a body outside the world, a negative or non-finite time, a
            // non-finite velocity or start and fewer than one iteration are invalid arguments that
            // log an error and leave the whole velocity unused.
            if (body == null)
            {
                Debug.LogError("Rigidbody2D.Slide needs an active, simulated Rigidbody2D.", this);
                return results;
            }
            if (!(deltaTime >= 0f) || float.IsInfinity(deltaTime) || !Finite(velocity) || !Finite(start) || slideMovement.maxIterations < 1)
            {
                Debug.LogError("Rigidbody2D.Slide: invalid argument (deltaTime must be finite and not negative, the velocity and start position finite, and maxIterations at least 1).", this);
                return results;
            }
            List<Collider2D> colliders;
            var selected = slideMovement.selectedCollider;
            if (selected != null)
            {
                W.Flush();
                if (!ReferenceEquals(selected.m_AttachedBody, this))
                {
                    Debug.LogError("Rigidbody2D.Slide: the selectedCollider is not attached to this Rigidbody2D.", this);
                    return results;
                }
                colliders = new List<Collider2D> { selected };
            }
            else colliders = AttachedColliders(slideMovement.useAttachedTriggers);

            // Unbound's choices: other triggers never stop a slide, and without useLayerMask the
            // layer collision matrix row of this Rigidbody2D's layer selects the colliders.
            var filter = new ContactFilter2D().NoFilter();
            filter.useTriggers = false;
            filter.SetLayerMask(slideMovement.useLayerMask ? (int)slideMovement.layerMask : Physics2D.GetLayerCollisionMask(m_GameObject.m_Layer));
            float angle = body.GetAngle();
            var up = slideMovement.surfaceUp.normalized;
            // Unbound's choice: anchoring holds the body to a surface unless the velocity moves
            // away from the anchor direction (a jump off the ground is not pulled back).
            var anchor = slideMovement.surfaceAnchor;
            float anchorDistance = anchor.magnitude;
            bool anchoring = anchorDistance > 0f && Vector2.Dot(velocity, anchor) >= 0f;
            var anchorDirection = anchoring ? anchor / anchorDistance : Vector2.zero;

            var pos = start;
            var move = velocity * deltaTime;
            int iterations = 0;
            while (move.sqrMagnitude > 0f)
            {
                float distance = move.magnitude;
                var direction = move / distance;
                if (!SlideCast(colliders, pos, angle, direction, distance, filter, out var hit))
                {
                    pos += move;
                    move = Vector2.zero;
                }
                else
                {
                    // Each detected contact is an iteration (SlideMovement.maxIterations).
                    iterations++;
                    float travel = Mathf.Clamp(hit.distance, 0f, distance);
                    pos += direction * travel;
                    results.slideHit = hit;
                    move = direction * (distance - travel);
                    // Unbound's choice: a surface too steep to slide on stops the movement.
                    if (!WithinAngle(hit.normal, up, slideMovement.surfaceSlideAngle, below: true)) move = Vector2.zero;
                    else move -= Vector2.Dot(move, hit.normal) * hit.normal;
                }
                if (anchoring && SlideCast(colliders, pos, angle, anchorDirection, anchorDistance, filter, out var surface))
                {
                    pos += anchorDirection * Mathf.Clamp(surface.distance, 0f, anchorDistance);
                    results.surfaceHit = surface;
                }
                if (iterations >= slideMovement.maxIterations) break;
            }

            var gravity = slideMovement.gravity * deltaTime;
            float fall = gravity.magnitude;
            if (fall > 0f)
            {
                var direction = gravity / fall;
                if (SlideCast(colliders, pos, angle, direction, fall, filter, out var ground))
                {
                    float travel = Mathf.Clamp(ground.distance, 0f, fall);
                    pos += direction * travel;
                    results.surfaceHit = ground;
                    // Unbound's choices: the slip is one more iteration, within maxIterations, along
                    // the surface for the rest of the gravity movement; a contact it meets is the
                    // slide hit.
                    if (velocity.x == 0f && velocity.y == 0f && iterations < slideMovement.maxIterations
                        && WithinAngle(ground.normal, up, slideMovement.gravitySlipAngle, below: false))
                    {
                        iterations++;
                        var slip = direction * (fall - travel);
                        slip -= Vector2.Dot(slip, ground.normal) * ground.normal;
                        float slipDistance = slip.magnitude;
                        if (slipDistance > 0f)
                        {
                            var slipDirection = slip / slipDistance;
                            if (SlideCast(colliders, pos, angle, slipDirection, slipDistance, filter, out var slipHit))
                            {
                                pos += slipDirection * Mathf.Clamp(slipHit.distance, 0f, slipDistance);
                                results.slideHit = slipHit;
                            }
                            else pos += slip;
                        }
                    }
                }
                else pos += gravity;
            }

            results.position = pos;
            results.iterationsUsed = iterations;
            // What maxIterations left unused (SlideResults.remainingVelocity).
            results.remainingVelocity = deltaTime > 0f ? move / deltaTime : Vector2.zero;
            if (!slideMovement.useNoMove)
            {
                if (slideMovement.useSimulationMove) MovePosition(pos);
                else
                {
                    body.SetTransform(W.ToB2(pos), body.GetAngle());
                    if (body.BodyType != BodyType.StaticBody) body.IsAwake = true;
                    // Unbound's choice: interpolation starts again from the new pose.
                    m_CurrentPosition = m_PreviousPosition = pos;
                    W.SetTransformPose(this, pos, body.GetAngle());
                }
            }
            return results;
        }

        static bool Finite(Vector2 v) => float.IsFinite(v.x) && float.IsFinite(v.y);

        /// <summary>Whether a surface's angle from surfaceUp is below (or above) a limit in degrees;
        /// a zero surfaceUp ignores the limit (SlideMovement.surfaceUp).</summary>
        static bool WithinAngle(Vector2 normal, Vector2 up, float limit, bool below)
        {
            if (up == Vector2.zero) return true;
            float angle = Vector2.Angle(normal, up);
            return below ? angle < limit : angle > limit;
        }

        /// <summary>The nearest surface the colliders, placed at <paramref name="origin"/>, move into
        /// when cast along <paramref name="direction"/>. Unbound's choice: the casts always see the
        /// colliders they start in (contacts the body already touches), whatever
        /// Physics2D.queriesStartInColliders says.</summary>
        bool SlideCast(List<Collider2D> colliders, Vector2 origin, float angle, Vector2 direction, float distance, ContactFilter2D filter, out RaycastHit2D hit)
        {
            hit = default;
            Physics2D.EnsureSettings();
            var pose = new Unbound.Box2D.Common.Transform(W.ToB2(origin), angle);
            bool startInColliders = Physics2D.s_QueriesStartInColliders;
            Physics2D.s_QueriesStartInColliders = true;
            List<RaycastHit2D> hits;
            try
            {
                hits = Physics2DQueries.CastColliders(colliders, pose, direction, distance, filter, this);
            }
            finally
            {
                Physics2D.s_QueriesStartInColliders = startInColliders;
            }
            foreach (var h in hits)
            {
                if (Vector2.Dot(h.normal, direction) >= -SlideApproach) continue;
                hit = h;
                return true;
            }
            return false;
        }

        /// <summary>How <see cref="Slide"/> moves (Rigidbody2D.SlideMovement, Scripting API 6000.0).</summary>
        [Serializable]
        public partial struct SlideMovement
        {
            // The serialized names are Unbound's guesses: Unity's are not documented and not
            // verified (no Reference capture), so Inspector-authored values may not load. The first
            // Slide call warns (SlideSerializationWarning). The order follows the public members.
            [SerializeField] int m_MaxIterations;
            [SerializeField] float m_SurfaceSlideAngle;
            [SerializeField] float m_GravitySlipAngle;
            [SerializeField] Vector2 m_SurfaceUp;
            [SerializeField] Vector2 m_SurfaceAnchor;
            [SerializeField] Vector2 m_Gravity;
            [SerializeField] Vector2 m_StartPosition;
            [SerializeField] Collider2D m_SelectedCollider;
            [SerializeField] LayerMask m_LayerMask;
            [SerializeField] bool m_UseLayerMask;
            [SerializeField] bool m_UseStartPosition;
            [SerializeField] bool m_UseNoMove;
            [SerializeField] bool m_UseSimulationMove;
            [SerializeField] bool m_UseAttachedTriggers;

            public SlideMovement()
            {
                // Documented defaults: surfaceUp up, surfaceAnchor down, gravity (0, -9.81).
                m_SurfaceUp = Vector2.up;
                m_SurfaceAnchor = Vector2.down;
                m_Gravity = new Vector2(0f, -9.81f);
                // Unbound's choices, not documented or measured: three iterations, slides on any
                // surface less steep than a wall and slips only on overhangs, all layers, and the
                // flags off.
                m_MaxIterations = 3;
                m_SurfaceSlideAngle = 90f;
                m_GravitySlipAngle = 90f;
                m_StartPosition = Vector2.zero;
                m_SelectedCollider = null;
                m_LayerMask = Physics2D.AllLayers;
                m_UseLayerMask = false;
                m_UseStartPosition = false;
                m_UseNoMove = false;
                m_UseSimulationMove = false;
                m_UseAttachedTriggers = false;
            }

            public void SetLayerMask(LayerMask mask)
            {
                m_LayerMask = mask;
                m_UseLayerMask = true;
            }

            public void SetStartPosition(Vector2 position)
            {
                m_StartPosition = position;
                m_UseStartPosition = true;
            }

            public int maxIterations { get => m_MaxIterations; set => m_MaxIterations = value; }
            public float surfaceSlideAngle { get => m_SurfaceSlideAngle; set => m_SurfaceSlideAngle = value; }
            public float gravitySlipAngle { get => m_GravitySlipAngle; set => m_GravitySlipAngle = value; }
            public Vector2 surfaceUp { get => m_SurfaceUp; set => m_SurfaceUp = value; }
            public Vector2 surfaceAnchor { get => m_SurfaceAnchor; set => m_SurfaceAnchor = value; }
            public Vector2 gravity { get => m_Gravity; set => m_Gravity = value; }
            public Vector2 startPosition { get => m_StartPosition; set => m_StartPosition = value; }
            public Collider2D selectedCollider { get => m_SelectedCollider; set => m_SelectedCollider = value; }
            public LayerMask layerMask { get => m_LayerMask; set => m_LayerMask = value; }
            public bool useLayerMask { get => m_UseLayerMask; set => m_UseLayerMask = value; }
            public bool useStartPosition { get => m_UseStartPosition; set => m_UseStartPosition = value; }
            public bool useNoMove { get => m_UseNoMove; set => m_UseNoMove = value; }
            public bool useSimulationMove { get => m_UseSimulationMove; set => m_UseSimulationMove = value; }
            public bool useAttachedTriggers { get => m_UseAttachedTriggers; set => m_UseAttachedTriggers = value; }
        }

        /// <summary>What a <see cref="Slide"/> did (Rigidbody2D.SlideResults, Scripting API 6000.0).</summary>
        [Serializable]
        public partial struct SlideResults
        {
            // The serialized names are Unbound's guesses, not verified against Unity; a
            // SlideResults serialized without any Slide call gets no warning. The order follows
            // the public members.
            [SerializeField] Vector2 m_RemainingVelocity;
            [SerializeField] Vector2 m_Position;
            [SerializeField] int m_IterationsUsed;
            [SerializeField] RaycastHit2D m_SlideHit;
            [SerializeField] RaycastHit2D m_SurfaceHit;

            public Vector2 remainingVelocity { get => m_RemainingVelocity; set => m_RemainingVelocity = value; }
            public Vector2 position { get => m_Position; set => m_Position = value; }
            public int iterationsUsed { get => m_IterationsUsed; set => m_IterationsUsed = value; }
            public RaycastHit2D slideHit { get => m_SlideHit; set => m_SlideHit = value; }
            public RaycastHit2D surfaceHit { get => m_SurfaceHit; set => m_SurfaceHit = value; }
        }

        // ------------------------------------------------------------------ serialization

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_BodyType = (RigidbodyType2D)body.Int("m_BodyType", 0);
            m_Simulated = body.Bool("m_Simulated", true);
            m_UseFullKinematicContacts = body.Bool("m_UseFullKinematicContacts");
            m_UseAutoMass = body.Bool("m_UseAutoMass");
            m_Mass = body.Float("m_Mass", 1f);
            m_LinearDamping = body.Float("m_LinearDamping", body.Float("m_LinearDrag", 0f));
            m_AngularDamping = body.Float("m_AngularDamping", body.Float("m_AngularDrag", 0.05f));
            m_GravityScale = body.Float("m_GravityScale", 1f);
            var material = body.Ref("m_Material");
            m_Material = material.IsNull ? null : resolver.Resolve(material, typeof(PhysicsMaterial2D)) as PhysicsMaterial2D;
            m_IncludeLayers = Collider2D.ReadMask(body, "m_IncludeLayers", 0);
            m_ExcludeLayers = Collider2D.ReadMask(body, "m_ExcludeLayers", 0);
            m_Interpolate = (RigidbodyInterpolation2D)body.Int("m_Interpolate", 0);
            m_SleepingMode = (RigidbodySleepMode2D)body.Int("m_SleepingMode", 1);
            m_CollisionDetection = (CollisionDetectionMode2D)body.Int("m_CollisionDetection", 0);
            m_Constraints = (RigidbodyConstraints2D)body.Int("m_Constraints", 0);
            // Before 5.5 a fixed angle stood for the rotation constraint.
            if (body.Bool("m_FixedAngle")) m_Constraints |= RigidbodyConstraints2D.FreezeRotation;
            // Before 5.5, isKinematic was the body type.
            if (body.Get("m_BodyType") == null && body.Bool("m_IsKinematic")) m_BodyType = RigidbodyType2D.Kinematic;
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (Rigidbody2D)source;
            m_BodyType = s.m_BodyType;
            m_Simulated = s.m_Simulated;
            m_UseFullKinematicContacts = s.m_UseFullKinematicContacts;
            m_UseAutoMass = s.m_UseAutoMass;
            m_Mass = s.m_Mass;
            m_LinearDamping = s.m_LinearDamping;
            m_AngularDamping = s.m_AngularDamping;
            m_GravityScale = s.m_GravityScale;
            m_Material = s.m_Material;
            m_IncludeLayers = s.m_IncludeLayers;
            m_ExcludeLayers = s.m_ExcludeLayers;
            m_Interpolate = s.m_Interpolate;
            m_SleepingMode = s.m_SleepingMode;
            m_CollisionDetection = s.m_CollisionDetection;
            m_Constraints = s.m_Constraints;
        }
    }
}
