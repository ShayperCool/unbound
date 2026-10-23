using System;
using System.Collections.Generic;
using Unbound.Box3D;
using Unbound.Runtime;
using W = Unbound.Runtime.PhysicsWorld;

namespace UnityEngine
{
    /// <summary>
    /// A 3D rigid body: a Box3D body while its GameObject is active. Its state is kept here while
    /// there is no body, and given to the body when it is created.
    /// </summary>
    [ClassId(54)]
    [RequireComponent(typeof(Transform))]
    public partial class Rigidbody : Component
    {
        // Serialized state.
        internal float m_Mass = 1f;
        internal float m_LinearDamping;
        internal float m_AngularDamping = 0.05f;
        internal Vector3 m_CenterOfMass;
        internal Vector3 m_InertiaTensor = Vector3.one;
        internal Quaternion m_InertiaRotation = Quaternion.identity;
        internal int m_IncludeLayers;
        internal int m_ExcludeLayers;
        internal bool m_ImplicitCom = true;
        internal bool m_ImplicitTensor = true;
        internal bool m_UseGravity = true;
        internal bool m_IsKinematic;
        internal RigidbodyInterpolation m_Interpolation;
        internal RigidbodyConstraints m_Constraints;
        internal CollisionDetectionMode m_CollisionDetection;

        // Settings without a body counterpart (kept for scripts that read them back).
        internal bool m_DetectCollisions = true;
        float m_MaxAngularVelocity = 50f;
        float m_MaxLinearVelocity = 1e16f;
        float m_MaxDepenetrationVelocity = 1e32f;
        float m_SleepThreshold = 0.005f;
        int m_SolverIterations = 6;
        int m_SolverVelocityIterations = 1;

        // Runtime state.
        internal ulong m_Body;
        internal bool m_Registered;
        internal bool m_BodyDirty;
        internal bool m_MassDirty;
        internal Vector3 m_PendingVelocity;
        internal Vector3 m_PendingAngularVelocity;
        internal Vector3 m_PreStepVelocity;
        /// <summary>The transform version the body last matched (changes after it are the script's).</summary>
        internal int m_TransformVersion = -1;
        /// <summary>The pose the transform shows, as last written or read by physics.</summary>
        internal Vector3 m_SyncPosition;
        internal Quaternion m_SyncRotation = Quaternion.identity;
        internal Vector3 m_PreviousPosition, m_CurrentPosition;
        internal Quaternion m_PreviousRotation = Quaternion.identity, m_CurrentRotation = Quaternion.identity;
        /// <summary>The body's mass properties as applied: center (body frame) and inertia about it.</summary>
        Vector3 m_AppliedCenter;
        B3.Matrix3 m_AppliedInertia;
        bool m_HasMove;
        Vector3 m_MovePosition;
        Quaternion m_MoveRotation;
        Vector3 m_AccumulatedForce, m_AccumulatedTorque;

        public Rigidbody() { }

        internal override void OnEngineEnable() => W.Add(this);

        internal override void OnEngineDisable() => W.Remove(this);

        /// <summary>Destroying it removes it too (components without enabled state get no disable).</summary>
        internal override void OnEngineDestroy() => W.Remove(this);

        internal B3.BodyType BodyType => m_IsKinematic ? B3.BodyType.Kinematic : B3.BodyType.Dynamic;

        /// <summary>Box3D's motion locks: RigidbodyConstraints without its unused first bit.</summary>
        internal int Locks => ((int)m_Constraints >> 1) & 63;

        /// <summary>The body, created now if the rigidbody is in the world but not yet built.</summary>
        internal ulong Body
        {
            get
            {
                if (m_Registered && m_Body == 0) W.Flush();
                return m_Body;
            }
        }

        void Changed()
        {
            if (m_Registered) W.MarkBodyDirty(this);
        }

        /// <summary>Pushes the settings to the body (after one of them changed).</summary>
        internal void ApplyBodySettings()
        {
            m_BodyDirty = false;
            var body = m_Body;
            if (body == 0) return;
            B3.ua3_body_set_type(body, (int)BodyType);
            B3.ua3_body_set_damping(body, Mathf.Max(0f, m_LinearDamping), Mathf.Max(0f, m_AngularDamping));
            B3.ua3_body_set_gravity_scale(body, m_UseGravity ? 1f : 0f);
            B3.ua3_body_set_locks(body, Locks);
            B3.ua3_body_set_bullet(body, m_CollisionDetection != CollisionDetectionMode.Discrete ? 1 : 0);
            // Changing the type drops the mass data.
            m_MassDirty = true;
            ApplyMass();
        }

        /// <summary>The body's mass: the set mass, at the colliders' center of mass, with the inertia
        /// their shapes give that mass (Unity's automatic center and tensor), or the set ones.</summary>
        internal void ApplyMass()
        {
            m_MassDirty = false;
            var body = m_Body;
            if (body == 0 || m_IsKinematic) return;
            B3.ua3_body_shape_mass(body, out float shapeMass, out var center, out var inertia);
            float mass = Mathf.Max(1e-7f, m_Mass);
            if (shapeMass > 0f)
            {
                float k = mass / shapeMass;
                inertia.X *= k;
                inertia.Y *= k;
                inertia.Z *= k;
            }
            else
            {
                // No colliders: Unity's unit tensor at the origin.
                center = Vector3.zero;
                inertia = Diagonal(Vector3.one, Quaternion.identity);
            }
            if (!m_ImplicitCom) center = m_CenterOfMass;
            if (!m_ImplicitTensor) inertia = Diagonal(m_InertiaTensor, m_InertiaRotation);
            m_AppliedCenter = center;
            m_AppliedInertia = inertia;
            B3.ua3_body_set_mass(body, mass, center, inertia);
        }

        /// <summary>R diag(d) Rᵀ as columns.</summary>
        static B3.Matrix3 Diagonal(Vector3 d, Quaternion r)
        {
            var x = r * Vector3.right;
            var y = r * Vector3.up;
            var z = r * Vector3.forward;
            Vector3 Column(int j)
            {
                // Column j of R diag(d) Rᵀ = Σ_k d_k * axis_k * axis_k[j].
                return x * (d.x * x[j]) + y * (d.y * y[j]) + z * (d.z * z[j]);
            }
            return new B3.Matrix3 { X = Column(0), Y = Column(1), Z = Column(2) };
        }

        /// <summary>The principal moments and axes of a symmetric 3x3 matrix (Jacobi rotations).</summary>
        static void Principal(B3.Matrix3 m, out Vector3 moments, out Quaternion axes)
        {
            var a = new double[3, 3]
            {
                { m.X.x, m.Y.x, m.Z.x },
                { m.X.y, m.Y.y, m.Z.y },
                { m.X.z, m.Y.z, m.Z.z },
            };
            var v = new double[3, 3] { { 1, 0, 0 }, { 0, 1, 0 }, { 0, 0, 1 } };
            for (int sweep = 0; sweep < 32; sweep++)
            {
                double off = Math.Abs(a[0, 1]) + Math.Abs(a[0, 2]) + Math.Abs(a[1, 2]);
                if (off < 1e-12) break;
                for (int p = 0; p < 2; p++)
                for (int q = p + 1; q < 3; q++)
                {
                    if (Math.Abs(a[p, q]) < 1e-15) continue;
                    double theta = (a[q, q] - a[p, p]) / (2 * a[p, q]);
                    double t = Math.Sign(theta) / (Math.Abs(theta) + Math.Sqrt(theta * theta + 1));
                    if (theta == 0) t = 1;
                    double c = 1 / Math.Sqrt(t * t + 1), s = t * c;
                    for (int k = 0; k < 3; k++)
                    {
                        double akp = a[k, p], akq = a[k, q];
                        a[k, p] = c * akp - s * akq;
                        a[k, q] = s * akp + c * akq;
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        double apk = a[p, k], aqk = a[q, k];
                        a[p, k] = c * apk - s * aqk;
                        a[q, k] = s * apk + c * aqk;
                    }
                    for (int k = 0; k < 3; k++)
                    {
                        double vkp = v[k, p], vkq = v[k, q];
                        v[k, p] = c * vkp - s * vkq;
                        v[k, q] = s * vkp + c * vkq;
                    }
                }
            }
            moments = new Vector3((float)a[0, 0], (float)a[1, 1], (float)a[2, 2]);
            var x = new Vector3((float)v[0, 0], (float)v[1, 0], (float)v[2, 0]);
            var y = new Vector3((float)v[0, 1], (float)v[1, 1], (float)v[2, 1]);
            var z = Vector3.Cross(x, y);
            axes = Quaternion.LookRotation(z, y);
        }

        /// <summary>Kinematic bodies asked to move get the velocity that reaches the target in this step.</summary>
        internal void BeginStep(float deltaTime)
        {
            if (!m_HasMove) return;
            m_HasMove = false;
            if (m_IsKinematic) B3.ua3_body_set_target_transform(m_Body, m_MovePosition, m_MoveRotation, deltaTime);
            else B3.ua3_body_set_transform(m_Body, m_MovePosition, m_MoveRotation);
        }

        internal void EndStep()
        {
            m_AccumulatedForce = Vector3.zero;
            m_AccumulatedTorque = Vector3.zero;
            var w = B3.ua3_body_get_angular_velocity(m_Body);
            if (w.sqrMagnitude > m_MaxAngularVelocity * m_MaxAngularVelocity)
                B3.ua3_body_set_angular_velocity(m_Body, w.normalized * m_MaxAngularVelocity);
            var v = B3.ua3_body_get_linear_velocity(m_Body);
            if (v.sqrMagnitude > m_MaxLinearVelocity * m_MaxLinearVelocity)
                B3.ua3_body_set_linear_velocity(m_Body, v.normalized * m_MaxLinearVelocity);
        }

        // ------------------------------------------------------------------ properties

        public Vector3 linearVelocity
        {
            get
            {
                var body = Body;
                return body != 0 ? B3.ua3_body_get_linear_velocity(body) : m_PendingVelocity;
            }
            set
            {
                var body = Body;
                if (body != 0 && !m_IsKinematic) B3.ua3_body_set_linear_velocity(body, value);
                else m_PendingVelocity = value;
            }
        }

        public Vector3 angularVelocity
        {
            get
            {
                var body = Body;
                return body != 0 ? B3.ua3_body_get_angular_velocity(body) : m_PendingAngularVelocity;
            }
            set
            {
                var body = Body;
                if (body != 0 && !m_IsKinematic) B3.ua3_body_set_angular_velocity(body, value);
                else m_PendingAngularVelocity = value;
            }
        }

        [Obsolete("Please use Rigidbody.linearVelocity instead. (UnityUpgradable) -> linearVelocity")]
        public Vector3 velocity { get => linearVelocity; set => linearVelocity = value; }

        public float linearDamping
        {
            get => m_LinearDamping;
            set { m_LinearDamping = value; Changed(); }
        }

        public float angularDamping
        {
            get => m_AngularDamping;
            set { m_AngularDamping = value; Changed(); }
        }

        [Obsolete("Please use Rigidbody.linearDamping instead. (UnityUpgradable) -> linearDamping")]
        public float drag { get => linearDamping; set => linearDamping = value; }

        [Obsolete("Please use Rigidbody.angularDamping instead. (UnityUpgradable) -> angularDamping")]
        public float angularDrag { get => angularDamping; set => angularDamping = value; }

        public float mass
        {
            get => m_Mass;
            set
            {
                m_Mass = Mathf.Max(1e-7f, value);
                m_MassDirty = true;
                if (m_Registered) W.MarkBodyDirty(this);
            }
        }

        public void SetDensity(float density)
        {
            var body = Body;
            if (body == 0) return;
            B3.ua3_body_shape_mass(body, out float shapeMass, out _, out _);
            if (shapeMass > 0f) mass = shapeMass * density;
        }

        public bool useGravity
        {
            get => m_UseGravity;
            set { m_UseGravity = value; Changed(); }
        }

        public bool isKinematic
        {
            get => m_IsKinematic;
            set
            {
                if (m_IsKinematic == value) return;
                m_IsKinematic = value;
                Changed();
                foreach (var c in W.Colliders)
                    if (ReferenceEquals(c.m_AttachedBody, this)) W.MarkShapeDirty(c);
            }
        }

        public bool freezeRotation
        {
            get => (m_Constraints & RigidbodyConstraints.FreezeRotation) == RigidbodyConstraints.FreezeRotation;
            set => constraints = value ? m_Constraints | RigidbodyConstraints.FreezeRotation : m_Constraints & ~RigidbodyConstraints.FreezeRotation;
        }

        public RigidbodyConstraints constraints
        {
            get => m_Constraints;
            set { m_Constraints = value; Changed(); }
        }

        public CollisionDetectionMode collisionDetectionMode
        {
            get => m_CollisionDetection;
            set { m_CollisionDetection = value; Changed(); }
        }

        public RigidbodyInterpolation interpolation
        {
            get => m_Interpolation;
            set => m_Interpolation = value;
        }

        public bool automaticCenterOfMass
        {
            get => m_ImplicitCom;
            set { m_ImplicitCom = value; MassChanged(); }
        }

        public bool automaticInertiaTensor
        {
            get => m_ImplicitTensor;
            set { m_ImplicitTensor = value; MassChanged(); }
        }

        void MassChanged()
        {
            m_MassDirty = true;
            if (m_Registered) W.MarkBodyDirty(this);
        }

        void EnsureMass()
        {
            var body = Body;
            if (body != 0 && m_MassDirty) ApplyMass();
        }

        public Vector3 centerOfMass
        {
            get
            {
                EnsureMass();
                return m_Body != 0 && !m_IsKinematic ? m_AppliedCenter : m_CenterOfMass;
            }
            set
            {
                m_CenterOfMass = value;
                m_ImplicitCom = false;
                MassChanged();
            }
        }

        public Vector3 worldCenterOfMass
        {
            get
            {
                var body = Body;
                if (body != 0) return B3.ua3_body_get_world_center(body);
                return transform.TransformPoint(m_CenterOfMass);
            }
        }

        public Vector3 inertiaTensor
        {
            get
            {
                EnsureMass();
                if (m_Body == 0 || m_IsKinematic) return m_InertiaTensor;
                Principal(m_AppliedInertia, out var moments, out _);
                return moments;
            }
            set
            {
                m_InertiaTensor = value;
                m_ImplicitTensor = false;
                MassChanged();
            }
        }

        public Quaternion inertiaTensorRotation
        {
            get
            {
                EnsureMass();
                if (m_Body == 0 || m_IsKinematic) return m_InertiaRotation;
                Principal(m_AppliedInertia, out _, out var axes);
                return axes;
            }
            set
            {
                m_InertiaRotation = value;
                m_ImplicitTensor = false;
                MassChanged();
            }
        }

        public void ResetCenterOfMass()
        {
            m_ImplicitCom = true;
            MassChanged();
        }

        public void ResetInertiaTensor()
        {
            m_ImplicitTensor = true;
            MassChanged();
        }

        public bool detectCollisions
        {
            get => m_DetectCollisions;
            set
            {
                if (m_DetectCollisions == value) return;
                m_DetectCollisions = value;
                foreach (var c in W.Colliders)
                    if (ReferenceEquals(c.m_AttachedBody, this)) W.Refilter(c);
            }
        }

        public LayerMask includeLayers
        {
            get => m_IncludeLayers;
            set
            {
                m_IncludeLayers = value;
                foreach (var c in W.Colliders)
                    if (ReferenceEquals(c.m_AttachedBody, this)) W.Refilter(c);
            }
        }

        public LayerMask excludeLayers
        {
            get => m_ExcludeLayers;
            set
            {
                m_ExcludeLayers = value;
                foreach (var c in W.Colliders)
                    if (ReferenceEquals(c.m_AttachedBody, this)) W.Refilter(c);
            }
        }

        public Vector3 position
        {
            get
            {
                var body = Body;
                if (body == 0) return transform.position;
                B3.ua3_body_get_transform(body, out var p, out _);
                return p;
            }
            set
            {
                var body = Body;
                if (body == 0)
                {
                    transform.position = value;
                    return;
                }
                B3.ua3_body_get_transform(body, out _, out var q);
                B3.ua3_body_set_transform(body, value, q);
                m_PreviousPosition = m_CurrentPosition = value;
            }
        }

        public Quaternion rotation
        {
            get
            {
                var body = Body;
                if (body == 0) return transform.rotation;
                B3.ua3_body_get_transform(body, out _, out var q);
                return q;
            }
            set
            {
                var body = Body;
                if (body == 0)
                {
                    transform.rotation = value;
                    return;
                }
                B3.ua3_body_get_transform(body, out var p, out _);
                var q = Quaternion.Normalize(value);
                B3.ua3_body_set_transform(body, p, q);
                m_PreviousRotation = m_CurrentRotation = q;
            }
        }

        public void MovePosition(Vector3 position) => Move(position, m_HasMove ? m_MoveRotation : rotation);

        public void MoveRotation(Quaternion rot) => Move(m_HasMove ? m_MovePosition : position, rot);

        public void Move(Vector3 position, Quaternion rotation)
        {
            if (Body == 0)
            {
                transform.SetPositionAndRotation(position, rotation);
                return;
            }
            m_HasMove = true;
            m_MovePosition = position;
            m_MoveRotation = Quaternion.Normalize(rotation);
        }

        public void PublishTransform()
        {
            var body = Body;
            if (body == 0) return;
            B3.ua3_body_get_transform(body, out var p, out var q);
            W.SetTransformPose(this, p, q);
        }

        public float maxAngularVelocity
        {
            get => m_MaxAngularVelocity;
            set => m_MaxAngularVelocity = Mathf.Max(0f, value);
        }

        public float maxLinearVelocity
        {
            get => m_MaxLinearVelocity;
            set => m_MaxLinearVelocity = Mathf.Max(0f, value);
        }

        public float maxDepenetrationVelocity
        {
            get => m_MaxDepenetrationVelocity;
            set => m_MaxDepenetrationVelocity = value;
        }

        public float sleepThreshold
        {
            get => m_SleepThreshold;
            set => m_SleepThreshold = Mathf.Max(0f, value);
        }

        public int solverIterations
        {
            get => m_SolverIterations;
            set => m_SolverIterations = Mathf.Max(1, value);
        }

        public int solverVelocityIterations
        {
            get => m_SolverVelocityIterations;
            set => m_SolverVelocityIterations = Mathf.Max(1, value);
        }

        public void Sleep()
        {
            var body = Body;
            if (body != 0) B3.ua3_body_set_awake(body, 0);
        }

        public bool IsSleeping()
        {
            var body = Body;
            return body != 0 && B3.ua3_body_is_awake(body) == 0;
        }

        public void WakeUp()
        {
            var body = Body;
            if (body != 0) B3.ua3_body_set_awake(body, 1);
        }

        public Vector3 GetPointVelocity(Vector3 worldPoint)
        {
            var body = Body;
            return body != 0 ? B3.ua3_body_get_point_velocity(body, worldPoint) : Vector3.zero;
        }

        public Vector3 GetRelativePointVelocity(Vector3 relativePoint) => GetPointVelocity(transform.TransformPoint(relativePoint));

        // ------------------------------------------------------------------ forces

        /// <summary>The body's inertia in world space applied to a vector (for torque modes given as
        /// accelerations or velocity changes).</summary>
        Vector3 WorldInertiaTimes(Vector3 v)
        {
            EnsureMass();
            var q = rotation;
            var local = Quaternion.Inverse(q) * v;
            var m = m_AppliedInertia;
            var r = m.X * local.x + m.Y * local.y + m.Z * local.z;
            return q * r;
        }

        public void AddForce(Vector3 force, [Internal.DefaultValue("ForceMode.Force")] ForceMode mode)
        {
            var body = Body;
            if (body == 0 || m_IsKinematic || !IsFinite(force)) return;
            switch (mode)
            {
                case ForceMode.Force:
                    m_AccumulatedForce += force;
                    B3.ua3_body_apply(body, (int)B3.Apply.Force, force, default);
                    break;
                case ForceMode.Acceleration:
                    m_AccumulatedForce += force * m_Mass;
                    B3.ua3_body_apply(body, (int)B3.Apply.Force, force * m_Mass, default);
                    break;
                case ForceMode.Impulse:
                    B3.ua3_body_apply(body, (int)B3.Apply.Impulse, force, default);
                    break;
                default:
                    B3.ua3_body_apply(body, (int)B3.Apply.Impulse, force * m_Mass, default);
                    break;
            }
        }

        public void AddForce(Vector3 force) => AddForce(force, ForceMode.Force);

        public void AddForce(float x, float y, float z, [Internal.DefaultValue("ForceMode.Force")] ForceMode mode) => AddForce(new Vector3(x, y, z), mode);

        public void AddForce(float x, float y, float z) => AddForce(new Vector3(x, y, z), ForceMode.Force);

        public void AddRelativeForce(Vector3 force, [Internal.DefaultValue("ForceMode.Force")] ForceMode mode) => AddForce(rotation * force, mode);

        public void AddRelativeForce(Vector3 force) => AddRelativeForce(force, ForceMode.Force);

        public void AddRelativeForce(float x, float y, float z, [Internal.DefaultValue("ForceMode.Force")] ForceMode mode) => AddRelativeForce(new Vector3(x, y, z), mode);

        public void AddRelativeForce(float x, float y, float z) => AddRelativeForce(new Vector3(x, y, z), ForceMode.Force);

        public void AddTorque(Vector3 torque, [Internal.DefaultValue("ForceMode.Force")] ForceMode mode)
        {
            var body = Body;
            if (body == 0 || m_IsKinematic || !IsFinite(torque)) return;
            switch (mode)
            {
                case ForceMode.Force:
                    m_AccumulatedTorque += torque;
                    B3.ua3_body_apply(body, (int)B3.Apply.Torque, torque, default);
                    break;
                case ForceMode.Acceleration:
                    var t = WorldInertiaTimes(torque);
                    m_AccumulatedTorque += t;
                    B3.ua3_body_apply(body, (int)B3.Apply.Torque, t, default);
                    break;
                case ForceMode.Impulse:
                    B3.ua3_body_apply(body, (int)B3.Apply.AngularImpulse, torque, default);
                    break;
                default:
                    B3.ua3_body_apply(body, (int)B3.Apply.AngularImpulse, WorldInertiaTimes(torque), default);
                    break;
            }
        }

        public void AddTorque(Vector3 torque) => AddTorque(torque, ForceMode.Force);

        public void AddTorque(float x, float y, float z, [Internal.DefaultValue("ForceMode.Force")] ForceMode mode) => AddTorque(new Vector3(x, y, z), mode);

        public void AddTorque(float x, float y, float z) => AddTorque(new Vector3(x, y, z), ForceMode.Force);

        public void AddRelativeTorque(Vector3 torque, [Internal.DefaultValue("ForceMode.Force")] ForceMode mode) => AddTorque(rotation * torque, mode);

        public void AddRelativeTorque(Vector3 torque) => AddRelativeTorque(torque, ForceMode.Force);

        public void AddRelativeTorque(float x, float y, float z, [Internal.DefaultValue("ForceMode.Force")] ForceMode mode) => AddRelativeTorque(new Vector3(x, y, z), mode);

        public void AddRelativeTorque(float x, float y, float z) => AddRelativeTorque(new Vector3(x, y, z), ForceMode.Force);

        public void AddForceAtPosition(Vector3 force, Vector3 position, [Internal.DefaultValue("ForceMode.Force")] ForceMode mode)
        {
            var body = Body;
            if (body == 0 || m_IsKinematic || !IsFinite(force)) return;
            switch (mode)
            {
                case ForceMode.Force:
                    m_AccumulatedForce += force;
                    B3.ua3_body_apply(body, (int)B3.Apply.ForceAtPoint, force, position);
                    break;
                case ForceMode.Acceleration:
                    m_AccumulatedForce += force * m_Mass;
                    B3.ua3_body_apply(body, (int)B3.Apply.ForceAtPoint, force * m_Mass, position);
                    break;
                case ForceMode.Impulse:
                    B3.ua3_body_apply(body, (int)B3.Apply.ImpulseAtPoint, force, position);
                    break;
                default:
                    B3.ua3_body_apply(body, (int)B3.Apply.ImpulseAtPoint, force * m_Mass, position);
                    break;
            }
        }

        public void AddForceAtPosition(Vector3 force, Vector3 position) => AddForceAtPosition(force, position, ForceMode.Force);

        /// <summary>A force away from the explosion, falling off linearly with the distance to the
        /// body's surface, applied at the body's closest point; the upwards modifier moves the
        /// explosion down so the push lifts.</summary>
        public void AddExplosionForce(float explosionForce, Vector3 explosionPosition, float explosionRadius, [Internal.DefaultValue("0.0f")] float upwardsModifier, [Internal.DefaultValue("ForceMode.Force")] ForceMode mode)
        {
            var body = Body;
            if (body == 0 || m_IsKinematic) return;
            var target = ClosestPointOnColliders(explosionPosition);
            var origin = explosionPosition;
            if (upwardsModifier != 0f) origin.y -= upwardsModifier;
            var delta = target - origin;
            float distance = (target - explosionPosition).magnitude;
            if (explosionRadius > 0f && distance > explosionRadius) return;
            float falloff = explosionRadius > 0f ? 1f - distance / explosionRadius : 1f;
            var direction = delta.sqrMagnitude > 1e-12f ? delta.normalized : Vector3.up;
            AddForceAtPosition(direction * (explosionForce * falloff), target, mode);
        }

        public void AddExplosionForce(float explosionForce, Vector3 explosionPosition, float explosionRadius, [Internal.DefaultValue("0.0f")] float upwardsModifier) =>
            AddExplosionForce(explosionForce, explosionPosition, explosionRadius, upwardsModifier, ForceMode.Force);

        public void AddExplosionForce(float explosionForce, Vector3 explosionPosition, float explosionRadius) =>
            AddExplosionForce(explosionForce, explosionPosition, explosionRadius, 0f, ForceMode.Force);

        Vector3 ClosestPointOnColliders(Vector3 point)
        {
            var best = worldCenterOfMass;
            float bestDistance = float.PositiveInfinity;
            foreach (var c in W.Colliders)
            {
                if (!ReferenceEquals(c.m_AttachedBody, this) || c.m_Shape == 0) continue;
                var p = B3.ua3_shape_closest_point(c.m_Shape, point);
                float d = (p - point).sqrMagnitude;
                if (d < bestDistance)
                {
                    bestDistance = d;
                    best = p;
                }
            }
            return best;
        }

        public Vector3 GetAccumulatedForce([Internal.DefaultValue("Time.fixedDeltaTime")] float step) => m_AccumulatedForce;

        public Vector3 GetAccumulatedForce() => m_AccumulatedForce;

        public Vector3 GetAccumulatedTorque([Internal.DefaultValue("Time.fixedDeltaTime")] float step) => m_AccumulatedTorque;

        public Vector3 GetAccumulatedTorque() => m_AccumulatedTorque;

        static bool IsFinite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);

        public Vector3 ClosestPointOnBounds(Vector3 position)
        {
            bool any = false;
            var bounds = new Bounds();
            foreach (var c in W.Colliders)
            {
                if (!ReferenceEquals(c.m_AttachedBody, this)) continue;
                if (!any) bounds = c.bounds;
                else bounds.Encapsulate(c.bounds);
                any = true;
            }
            return any ? bounds.ClosestPoint(position) : position;
        }

        // ------------------------------------------------------------------ sweeps

        public bool SweepTest(Vector3 direction, out RaycastHit hitInfo, [Internal.DefaultValue("Mathf.Infinity")] float maxDistance, [Internal.DefaultValue("QueryTriggerInteraction.UseGlobal")] QueryTriggerInteraction queryTriggerInteraction)
        {
            var hits = Sweep(direction, maxDistance, queryTriggerInteraction, closest: true);
            hitInfo = hits.Count > 0 ? hits[0] : default;
            return hits.Count > 0;
        }

        public bool SweepTest(Vector3 direction, out RaycastHit hitInfo, [Internal.DefaultValue("Mathf.Infinity")] float maxDistance) =>
            SweepTest(direction, out hitInfo, maxDistance, QueryTriggerInteraction.UseGlobal);

        public bool SweepTest(Vector3 direction, out RaycastHit hitInfo) => SweepTest(direction, out hitInfo, Mathf.Infinity, QueryTriggerInteraction.UseGlobal);

        public RaycastHit[] SweepTestAll(Vector3 direction, [Internal.DefaultValue("Mathf.Infinity")] float maxDistance, [Internal.DefaultValue("QueryTriggerInteraction.UseGlobal")] QueryTriggerInteraction queryTriggerInteraction) =>
            Sweep(direction, maxDistance, queryTriggerInteraction, closest: false).ToArray();

        public RaycastHit[] SweepTestAll(Vector3 direction, [Internal.DefaultValue("Mathf.Infinity")] float maxDistance) =>
            SweepTestAll(direction, maxDistance, QueryTriggerInteraction.UseGlobal);

        public RaycastHit[] SweepTestAll(Vector3 direction) => SweepTestAll(direction, Mathf.Infinity, QueryTriggerInteraction.UseGlobal);

        /// <summary>Casts every collider of the body along a direction (its own colliders excluded),
        /// with the layers the body's layer collides with.</summary>
        List<RaycastHit> Sweep(Vector3 direction, float maxDistance, QueryTriggerInteraction triggers, bool closest)
        {
            var result = new List<RaycastHit>();
            W.PrepareQuery();
            int mask = Physics.GetLayerCollisionMask(gameObject.layer);
            foreach (var c in W.Colliders)
            {
                if (!ReferenceEquals(c.m_AttachedBody, this) || c.m_Shape == 0) continue;
                foreach (var hit in PhysicsQueries.CastCollider(c, direction, maxDistance, mask, triggers, closest))
                {
                    if (hit.collider != null && ReferenceEquals(hit.collider.m_AttachedBody, this)) continue;
                    result.Add(hit);
                }
            }
            result.Sort((a, b) => a.distance.CompareTo(b.distance));
            if (closest && result.Count > 1) result.RemoveRange(1, result.Count - 1);
            return result;
        }

        // ------------------------------------------------------------------ serialization

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_Mass = body.Float("m_Mass", 1f);
            m_LinearDamping = body.Float("m_LinearDamping", body.Float("m_Drag", 0f));
            m_AngularDamping = body.Float("m_AngularDamping", body.Float("m_AngularDrag", 0.05f));
            m_CenterOfMass = body.Vector3("m_CenterOfMass");
            m_InertiaTensor = body.Get("m_InertiaTensor") != null ? body.Vector3("m_InertiaTensor") : Vector3.one;
            m_InertiaRotation = body.Get("m_InertiaRotation") != null ? body.Quaternion("m_InertiaRotation") : Quaternion.identity;
            m_IncludeLayers = Collider.ReadMask(body, "m_IncludeLayers", 0);
            m_ExcludeLayers = Collider.ReadMask(body, "m_ExcludeLayers", 0);
            m_ImplicitCom = body.Bool("m_ImplicitCom", true);
            m_ImplicitTensor = body.Bool("m_ImplicitTensor", true);
            m_UseGravity = body.Bool("m_UseGravity", true);
            m_IsKinematic = body.Bool("m_IsKinematic");
            m_Interpolation = (RigidbodyInterpolation)body.Int("m_Interpolate");
            m_Constraints = (RigidbodyConstraints)body.Int("m_Constraints");
            m_CollisionDetection = (CollisionDetectionMode)body.Int("m_CollisionDetection");
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (Rigidbody)source;
            m_Mass = s.m_Mass;
            m_LinearDamping = s.m_LinearDamping;
            m_AngularDamping = s.m_AngularDamping;
            m_CenterOfMass = s.m_CenterOfMass;
            m_InertiaTensor = s.m_InertiaTensor;
            m_InertiaRotation = s.m_InertiaRotation;
            m_IncludeLayers = s.m_IncludeLayers;
            m_ExcludeLayers = s.m_ExcludeLayers;
            m_ImplicitCom = s.m_ImplicitCom;
            m_ImplicitTensor = s.m_ImplicitTensor;
            m_UseGravity = s.m_UseGravity;
            m_IsKinematic = s.m_IsKinematic;
            m_Interpolation = s.m_Interpolation;
            m_Constraints = s.m_Constraints;
            m_CollisionDetection = s.m_CollisionDetection;
            m_DetectCollisions = s.m_DetectCollisions;
            m_MaxAngularVelocity = s.m_MaxAngularVelocity;
            m_MaxLinearVelocity = s.m_MaxLinearVelocity;
            m_MaxDepenetrationVelocity = s.m_MaxDepenetrationVelocity;
            m_SleepThreshold = s.m_SleepThreshold;
            m_SolverIterations = s.m_SolverIterations;
            m_SolverVelocityIterations = s.m_SolverVelocityIterations;
            m_PendingVelocity = s.linearVelocity;
            m_PendingAngularVelocity = s.angularVelocity;
        }
    }
}
