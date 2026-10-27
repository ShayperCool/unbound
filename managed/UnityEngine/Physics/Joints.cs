using System;
using System.Collections.Generic;
using Unbound.Box3D;
using Unbound.Runtime;
using W = Unbound.Runtime.PhysicsWorld;

namespace UnityEngine
{
    public partial struct JointMotor
    {
        float m_TargetVelocity, m_Force;
        int m_FreeSpin;

        public float targetVelocity { get => m_TargetVelocity; set => m_TargetVelocity = value; }
        public float force { get => m_Force; set => m_Force = value; }
        public bool freeSpin { get => m_FreeSpin != 0; set => m_FreeSpin = value ? 1 : 0; }
    }

    public partial struct JointLimits
    {
        float m_Min, m_Max, m_Bounciness, m_BounceMinVelocity, m_ContactDistance;

        [Obsolete("minBounce and maxBounce are replaced by a single JointLimits.bounciness for both limit ends.")]
        public float minBounce;
        [Obsolete("minBounce and maxBounce are replaced by a single JointLimits.bounciness for both limit ends.")]
        public float maxBounce;

        public float min { get => m_Min; set => m_Min = value; }
        public float max { get => m_Max; set => m_Max = value; }
        public float bounciness { get => m_Bounciness; set => m_Bounciness = value; }
        public float bounceMinVelocity { get => m_BounceMinVelocity; set => m_BounceMinVelocity = value; }
        public float contactDistance { get => m_ContactDistance; set => m_ContactDistance = value; }

        internal static JointLimits Default => new JointLimits { m_BounceMinVelocity = 0.2f };
    }

    public partial struct SoftJointLimit
    {
        float m_Limit, m_Bounciness, m_ContactDistance;

        public float limit { get => m_Limit; set => m_Limit = value; }
        public float bounciness { get => m_Bounciness; set => m_Bounciness = value; }
        public float contactDistance { get => m_ContactDistance; set => m_ContactDistance = value; }
    }

    public partial struct SoftJointLimitSpring
    {
        float m_Spring, m_Damper;

        public float spring { get => m_Spring; set => m_Spring = value; }
        public float damper { get => m_Damper; set => m_Damper = value; }
    }

    public partial struct JointDrive
    {
        float m_PositionSpring, m_PositionDamper, m_MaximumForce;
        int m_UseAcceleration;

        public float positionSpring { get => m_PositionSpring; set => m_PositionSpring = value; }
        public float positionDamper { get => m_PositionDamper; set => m_PositionDamper = value; }
        public float maximumForce { get => m_MaximumForce; set => m_MaximumForce = value; }
        public bool useAcceleration { get => m_UseAcceleration != 0; set => m_UseAcceleration = value ? 1 : 0; }

        internal static JointDrive Default => new JointDrive { m_MaximumForce = float.MaxValue };
    }

    /// <summary>
    /// A joint between its Rigidbody and a connected one (or the world), as Unity builds them on
    /// PhysX (measured: tests/projects/joints): the joint's own body is actor0 with the frame at
    /// the anchor, x along the axis; the connected body (or the world) actor1 with the frame at
    /// the connected anchor, turned so that the joint starts at rest; drive limits are forces.
    /// Hinge, character, configurable and fixed joints are D6 joints, spring joints distance joints.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public partial class Joint : Component
    {
        internal Rigidbody m_ConnectedBody;
        internal Vector3 m_Axis = Vector3.right;
        internal Vector3 m_Anchor;
        internal Vector3 m_ConnectedAnchor;
        internal bool m_AutoConfigureConnectedAnchor = true;
        internal bool m_ConnectedAnchorConfigured;
        internal float m_BreakForce = float.PositiveInfinity, m_BreakTorque = float.PositiveInfinity;
        internal bool m_EnableCollision;
        internal bool m_EnablePreprocessing = true;
        internal float m_MassScale = 1f, m_ConnectedMassScale = 1f;

        // The native joint and the bodies it was built between.
        internal ulong m_Joint;
        internal ulong m_Body0, m_Body1;
        internal bool m_Dirty = true;
        internal bool m_FramesDirty = true;
        internal bool m_Registered;

        public Joint() { }

        public Rigidbody connectedBody
        {
            get => m_ConnectedBody;
            set { m_ConnectedBody = value; m_ConnectedAnchorConfigured = false; Rebuild(); }
        }

        public Vector3 axis { get => m_Axis; set { m_Axis = value; FramesChanged(); } }

        public Vector3 anchor
        {
            get => m_Anchor;
            set { m_Anchor = value; if (m_AutoConfigureConnectedAnchor) m_ConnectedAnchorConfigured = false; FramesChanged(); }
        }

        public Vector3 connectedAnchor
        {
            get
            {
                if (m_AutoConfigureConnectedAnchor && !m_ConnectedAnchorConfigured) m_ConnectedAnchor = AutoConnectedAnchor();
                return m_ConnectedAnchor;
            }
            set { m_ConnectedAnchor = value; m_ConnectedAnchorConfigured = true; FramesChanged(); }
        }

        public bool autoConfigureConnectedAnchor
        {
            get => m_AutoConfigureConnectedAnchor;
            set { m_AutoConfigureConnectedAnchor = value; if (value) m_ConnectedAnchorConfigured = false; FramesChanged(); }
        }

        public float breakForce { get => m_BreakForce; set { m_BreakForce = value; Changed(); } }
        public float breakTorque { get => m_BreakTorque; set { m_BreakTorque = value; Changed(); } }
        public bool enableCollision { get => m_EnableCollision; set { m_EnableCollision = value; Changed(); } }
        public bool enablePreprocessing { get => m_EnablePreprocessing; set { m_EnablePreprocessing = value; Changed(); } }
        public float massScale { get => m_MassScale; set { m_MassScale = value; Changed(); } }
        public float connectedMassScale { get => m_ConnectedMassScale; set { m_ConnectedMassScale = value; Changed(); } }

        public Vector3 currentForce
        {
            get
            {
                if (m_Joint == 0) return Vector3.zero;
                B3.ua3_joint_force(m_Joint, out var force, out _);
                return force;
            }
        }

        public Vector3 currentTorque
        {
            get
            {
                if (m_Joint == 0) return Vector3.zero;
                B3.ua3_joint_force(m_Joint, out _, out var torque);
                return torque;
            }
        }

        // ------------------------------------------------------------------ building

        internal Rigidbody OwnBody => GetComponent<Rigidbody>();

        /// <summary>The connected anchor that puts it where the anchor is now: in the connected body's
        /// space, or in the world's.</summary>
        Vector3 AutoConnectedAnchor()
        {
            var world = transform.TransformPoint(m_Anchor);
            return m_ConnectedBody != null ? m_ConnectedBody.transform.InverseTransformPoint(world) : world;
        }

        /// <summary>The joint's own x (axis) and y (secondary) directions in its transform's space.</summary>
        internal virtual Vector3 SecondaryAxis => Vector3.up;

        /// <summary>The kind of native joint: 0 D6, 1 distance.</summary>
        internal virtual int Kind => 0;

        /// <summary>A setting changed: applied to the native joint at the next flush.</summary>
        internal void Changed()
        {
            if (m_Joint != 0) m_Dirty = true;
            W.MarkJointDirty(this);
        }

        /// <summary>The frames changed (anchors, axes): recomputed from the bodies' poses then.</summary>
        internal void FramesChanged()
        {
            m_FramesDirty = true;
            Changed();
        }

        internal void Rebuild()
        {
            W.ReleaseJoint(this);
            W.MarkJointDirty(this);
        }

        /// <summary>The frame of the joint in its transform's space as a rotation: x along the axis,
        /// y along the secondary axis made perpendicular.</summary>
        internal static Quaternion Frame(Vector3 axis, Vector3 secondary)
        {
            var x = axis.sqrMagnitude > 1e-12f ? axis.normalized : Vector3.right;
            var z = Vector3.Cross(x, secondary);
            if (z.sqrMagnitude < 1e-12f)
            {
                // A secondary axis along the axis: another perpendicular.
                z = Vector3.Cross(x, Mathf.Abs(x.y) < 0.9f ? Vector3.up : Vector3.right);
            }
            z.Normalize();
            var y = Vector3.Cross(z, x);
            return Quaternion.LookRotation(z, y);
        }

        /// <summary>Creates or updates the native joint (after bodies and shapes exist).</summary>
        internal void Build()
        {
            var rb = OwnBody;
            if (rb == null || rb.m_Body == 0)
            {
                W.ReleaseJoint(this);
                return;
            }
            var connected = m_ConnectedBody != null && m_ConnectedBody.m_Registered ? m_ConnectedBody : null;
            ulong body1 = connected != null ? connected.m_Body : 0;
            if (connected != null && body1 == 0) return;
            if (m_Joint != 0 && (m_Body0 != rb.m_Body || m_Body1 != body1)) W.ReleaseJoint(this);

            if (m_Joint == 0)
            {
                Frames(rb, connected, out var p0, out var q0, out var p1, out var q1);
                m_Joint = B3.ua3_joint_create(W.World, Kind, rb.m_Body, p0, q0, body1, p1, q1);
                if (m_Joint == 0) return;
                m_Body0 = rb.m_Body;
                m_Body1 = body1;
                m_FramesDirty = false;
                W.Register(this);
            }
            else if (m_FramesDirty)
            {
                Frames(rb, connected, out var p0, out var q0, out var p1, out var q1);
                B3.ua3_joint_set_frames(m_Joint, p0, q0, p1, q1);
                m_FramesDirty = false;
            }
            B3.ua3_joint_set_common(m_Joint, Native(m_BreakForce), Native(m_BreakTorque), m_EnableCollision ? 1 : 0, m_EnablePreprocessing ? 1 : 0, m_MassScale, m_ConnectedMassScale);
            Configure();
            m_Dirty = false;
        }

        static float Native(float limit) => float.IsInfinity(limit) || limit >= float.MaxValue ? float.MaxValue : Mathf.Max(0f, limit);

        /// <summary>The two frames in the bodies' (actors') spaces.</summary>
        internal virtual void Frames(Rigidbody rb, Rigidbody connected, out Vector3 p0, out Quaternion q0, out Vector3 p1, out Quaternion q1)
        {
            var t = transform;
            var worldAnchor = t.TransformPoint(m_Anchor);
            var worldFrame = t.rotation * Frame(m_Axis, SecondaryAxis);
            var bodyPose = rb.transform;
            p0 = Quaternion.Inverse(bodyPose.rotation) * (worldAnchor - bodyPose.position);
            q0 = Quaternion.Inverse(bodyPose.rotation) * worldFrame;
            var connectedAnchorValue = connectedAnchor;
            m_ConnectedAnchorConfigured = true;
            if (connected != null)
            {
                var ct = connected.transform;
                var worldConnected = ct.TransformPoint(connectedAnchorValue);
                p1 = Quaternion.Inverse(ct.rotation) * (worldConnected - ct.position);
                q1 = Quaternion.Inverse(ct.rotation) * worldFrame;
            }
            else
            {
                p1 = connectedAnchorValue;
                q1 = worldFrame;
            }
        }

        /// <summary>The joint type's own settings on the native joint.</summary>
        internal virtual void Configure()
        {
            // Fixed: every axis locked (D6's default).
        }

        internal static float Rad(float degrees) => degrees * Mathf.Deg2Rad;

        /// <summary>A limit's bounce threshold: below 0.2 (the default) it is 0.2 (measured: a limit
        /// with bounceMinVelocity 0 bounces as one with 0.2).</summary>
        internal static float BounceThreshold(float value) => Mathf.Max(value, 0.2f);

        /// <summary>A hinge limit's contact distance in radians: 0 is 0.01 rad on the native side.</summary>
        internal static float AngularContact(float degrees) => degrees > 0f ? Rad(degrees) : 0f;

        /// <summary>A character or configurable joint's angular limit contact distance: 0 is PhysX's
        /// default (-1 on the native side), measured.</summary>
        internal static float DefaultedContact(float degrees) => degrees > 0f ? Rad(degrees) : -1f;

        internal static void SetDrive(ulong joint, int index, JointDrive d) =>
            B3.ua3_d6_set_drive(joint, index, Mathf.Max(0f, d.positionSpring), Mathf.Max(0f, d.positionDamper), Mathf.Max(0f, d.maximumForce), d.useAcceleration ? 1 : 0);

        // ------------------------------------------------------------------ lifecycle

        /// <summary>A new joint's anchor: the top of the object's mesh when it has one, else its
        /// origin (measured: (0, 0.5, 0) on primitives, zero on a bare collider).</summary>
        internal override void OnAttached()
        {
            base.OnAttached();
            var filter = GetComponent<MeshFilter>();
            var mesh = filter != null ? filter.sharedMesh : null;
            if (mesh != null) m_Anchor = new Vector3(0f, mesh.bounds.max.y, 0f);
        }

        internal override void OnEngineEnable() => W.MarkJointDirty(this);

        internal override void OnEngineDisable() => W.ReleaseJoint(this, forget: true);

        internal override void OnEngineDestroy() => W.ReleaseJoint(this, forget: true);

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (Joint)source;
            m_ConnectedBody = remap(s.m_ConnectedBody) as Rigidbody;
            m_Axis = s.m_Axis;
            m_Anchor = s.m_Anchor;
            m_ConnectedAnchor = s.m_ConnectedAnchor;
            m_AutoConfigureConnectedAnchor = s.m_AutoConfigureConnectedAnchor;
            m_ConnectedAnchorConfigured = s.m_ConnectedAnchorConfigured;
            m_BreakForce = s.m_BreakForce;
            m_BreakTorque = s.m_BreakTorque;
            m_EnableCollision = s.m_EnableCollision;
            m_EnablePreprocessing = s.m_EnablePreprocessing;
            m_MassScale = s.m_MassScale;
            m_ConnectedMassScale = s.m_ConnectedMassScale;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            var connected = body.Ref("m_ConnectedBody");
            m_ConnectedBody = connected.IsNull ? null : resolver.Resolve(connected, typeof(Rigidbody)) as Rigidbody;
            m_Anchor = body.Vector3("m_Anchor");
            m_Axis = body.Vector3("m_Axis", Vector3.right);
            m_AutoConfigureConnectedAnchor = body.Bool("m_AutoConfigureConnectedAnchor", true);
            m_ConnectedAnchor = body.Vector3("m_ConnectedAnchor");
            m_ConnectedAnchorConfigured = !m_AutoConfigureConnectedAnchor;
            m_BreakForce = ReadInfinity(body, "m_BreakForce");
            m_BreakTorque = ReadInfinity(body, "m_BreakTorque");
            m_EnableCollision = body.Bool("m_EnableCollision");
            m_EnablePreprocessing = body.Bool("m_EnablePreprocessing", true);
            m_MassScale = body.Float("m_MassScale", 1f);
            m_ConnectedMassScale = body.Float("m_ConnectedMassScale", 1f);
        }

        static float ReadInfinity(YNode body, string key)
        {
            var text = body.Str(key, "Infinity");
            return text == "Infinity" || text == "inf" ? float.PositiveInfinity : body.Float(key, float.PositiveInfinity);
        }

        internal static SoftJointLimit ReadSoft(YNode n) => new SoftJointLimit
        {
            limit = n?.Float("limit") ?? 0f,
            bounciness = n?.Float("bounciness") ?? 0f,
            contactDistance = n?.Float("contactDistance") ?? 0f,
        };

        internal static SoftJointLimitSpring ReadSoftSpring(YNode n) => new SoftJointLimitSpring { spring = n?.Float("spring") ?? 0f, damper = n?.Float("damper") ?? 0f };

        internal static JointDrive ReadDrive(YNode n) => new JointDrive
        {
            positionSpring = n?.Float("positionSpring") ?? 0f,
            positionDamper = n?.Float("positionDamper") ?? 0f,
            maximumForce = n?.Float("maximumForce", float.MaxValue) ?? float.MaxValue,
            useAcceleration = n?.Bool("useAcceleration") ?? false,
        };
    }

    [ClassId(138)]
    public partial class FixedJoint : Joint
    {
        public FixedJoint() { }
    }

    /// <summary>A hinge: a D6 joint free (or limited) about its axis, driven by its motor or spring.</summary>
    [ClassId(59)]
    public partial class HingeJoint : Joint
    {
        JointMotor m_Motor;
        JointLimits m_Limits = JointLimits.Default;
        JointSpring m_Spring;
        bool m_UseMotor, m_UseLimits, m_UseSpring, m_ExtendedLimits, m_UseAcceleration;

        // A motor is a velocity drive: PhysX's damping as large as it stays finite (measured).
        const float MotorDamping = 1e10f;

        public HingeJoint() { }

        public JointMotor motor { get => m_Motor; set { m_Motor = value; Changed(); } }
        public JointLimits limits { get => m_Limits; set { m_Limits = value; Changed(); } }
        public JointSpring spring { get => m_Spring; set { m_Spring = value; Changed(); } }
        public bool useMotor { get => m_UseMotor; set { m_UseMotor = value; Changed(); } }
        public bool useLimits { get => m_UseLimits; set { m_UseLimits = value; Changed(); } }
        public bool extendedLimits { get => m_ExtendedLimits; set { m_ExtendedLimits = value; Changed(); } }
        public bool useSpring { get => m_UseSpring; set { m_UseSpring = value; Changed(); } }
        public bool useAcceleration { get => m_UseAcceleration; set { m_UseAcceleration = value; Changed(); } }

        /// <summary>The body's rotation about the axis relative to the connected body, in degrees.</summary>
        public float angle
        {
            get
            {
                if (m_Joint == 0) return 0f;
                B3.ua3_d6_twist(m_Joint, out float a, out _);
                // In (-180, 180].
                float degrees = -a * Mathf.Rad2Deg;
                degrees %= 360f;
                if (degrees > 180f) degrees -= 360f;
                else if (degrees <= -180f) degrees += 360f;
                return degrees;
            }
        }

        public float velocity
        {
            get
            {
                if (m_Joint == 0) return 0f;
                B3.ua3_d6_twist(m_Joint, out _, out float v);
                return -v * Mathf.Rad2Deg;
            }
        }

        internal override void Configure()
        {
            B3.ua3_d6_set_motion(m_Joint, 3, m_UseLimits ? 1 : 2);
            if (m_UseLimits)
                B3.ua3_d6_set_twist_limit(m_Joint, -Rad(m_Limits.max), -Rad(m_Limits.min), 0f, 0f, Mathf.Clamp01(m_Limits.bounciness),
                    BounceThreshold(m_Limits.bounceMinVelocity), AngularContact(m_Limits.contactDistance));
            if (m_UseMotor)
            {
                B3.ua3_d6_set_drive(m_Joint, 4, 0f, MotorDamping, Mathf.Max(0f, m_Motor.force), m_UseAcceleration ? 1 : 0);
                B3.ua3_d6_set_drive_target(m_Joint, Vector3.zero, Quaternion.identity, Vector3.zero, new Vector3(Rad(m_Motor.targetVelocity), 0f, 0f));
            }
            else if (m_UseSpring)
            {
                B3.ua3_d6_set_drive(m_Joint, 4, Mathf.Max(0f, m_Spring.spring), Mathf.Max(0f, m_Spring.damper), float.MaxValue, m_UseAcceleration ? 1 : 0);
                B3.ua3_d6_set_drive_target(m_Joint, Vector3.zero, Quaternion.AngleAxis(-m_Spring.targetPosition, Vector3.right), Vector3.zero, Vector3.zero);
            }
            else
            {
                B3.ua3_d6_set_drive(m_Joint, 4, 0f, 0f, 0f, 0);
            }
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            base.CopyFrom(source, remap);
            var s = (HingeJoint)source;
            m_Motor = s.m_Motor;
            m_Limits = s.m_Limits;
            m_Spring = s.m_Spring;
            m_UseMotor = s.m_UseMotor;
            m_UseLimits = s.m_UseLimits;
            m_UseSpring = s.m_UseSpring;
            m_ExtendedLimits = s.m_ExtendedLimits;
            m_UseAcceleration = s.m_UseAcceleration;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            base.ReadSerialized(body, resolver);
            var motor = body.Get("m_Motor");
            m_Motor = new JointMotor { targetVelocity = motor?.Float("m_TargetVelocity") ?? 0f, force = motor?.Float("m_Force") ?? 0f, freeSpin = motor?.Bool("m_FreeSpin") ?? false };
            var limits = body.Get("m_Limits");
            m_Limits = new JointLimits
            {
                min = limits?.Float("min") ?? 0f,
                max = limits?.Float("max") ?? 0f,
                bounciness = limits?.Float("bounciness") ?? 0f,
                bounceMinVelocity = limits?.Float("bounceMinVelocity", 0.2f) ?? 0.2f,
                contactDistance = limits?.Float("contactDistance") ?? 0f,
            };
            var spring = body.Get("m_Spring");
            m_Spring = new JointSpring { spring = spring?.Float("spring") ?? 0f, damper = spring?.Float("damper") ?? 0f, targetPosition = spring?.Float("targetPosition") ?? 0f };
            m_UseMotor = body.Bool("m_UseMotor");
            m_UseLimits = body.Bool("m_UseLimits");
            m_UseSpring = body.Bool("m_UseSpring");
            m_ExtendedLimits = body.Bool("m_ExtendedLimits");
            m_UseAcceleration = body.Bool("m_UseAcceleration");
        }
    }

    /// <summary>A spring: a distance joint keeping the anchors between the min and max distances,
    /// with the spring outside them.</summary>
    [ClassId(145)]
    public partial class SpringJoint : Joint
    {
        float m_Spring = 10f, m_Damper = 0.2f, m_MinDistance, m_MaxDistance, m_Tolerance = 0.025f;

        public SpringJoint() { }

        public float spring { get => m_Spring; set { m_Spring = value; Changed(); } }
        public float damper { get => m_Damper; set { m_Damper = value; Changed(); } }
        public float minDistance { get => m_MinDistance; set { m_MinDistance = value; Changed(); } }
        public float maxDistance { get => m_MaxDistance; set { m_MaxDistance = value; Changed(); } }
        public float tolerance { get => m_Tolerance; set { m_Tolerance = value; Changed(); } }

        internal override int Kind => 1;

        internal override void Configure() =>
            B3.ua3_distance_set(m_Joint, Mathf.Max(0f, m_MinDistance), Mathf.Max(0f, m_MaxDistance), Mathf.Max(0f, m_Tolerance), Mathf.Max(0f, m_Spring), Mathf.Max(0f, m_Damper));

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            base.CopyFrom(source, remap);
            var s = (SpringJoint)source;
            m_Spring = s.m_Spring;
            m_Damper = s.m_Damper;
            m_MinDistance = s.m_MinDistance;
            m_MaxDistance = s.m_MaxDistance;
            m_Tolerance = s.m_Tolerance;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            base.ReadSerialized(body, resolver);
            m_Spring = body.Float("m_Spring", 10f);
            m_Damper = body.Float("m_Damper", 0.2f);
            m_MinDistance = body.Float("m_MinDistance");
            m_MaxDistance = body.Float("m_MaxDistance");
            m_Tolerance = body.Float("m_Tolerance", 0.025f);
        }
    }

    /// <summary>A character joint: a D6 joint with the anchor locked, twist limited about the axis and
    /// swing limited in a cone about the swing axes.</summary>
    [ClassId(144)]
    public partial class CharacterJoint : Joint
    {
        Vector3 m_SwingAxis = Vector3.up;
        SoftJointLimitSpring m_TwistLimitSpring, m_SwingLimitSpring;
        SoftJointLimit m_LowTwistLimit = new SoftJointLimit { limit = -20f };
        SoftJointLimit m_HighTwistLimit = new SoftJointLimit { limit = 70f };
        SoftJointLimit m_Swing1Limit = new SoftJointLimit { limit = 40f };
        SoftJointLimit m_Swing2Limit = new SoftJointLimit { limit = 40f };
        bool m_EnableProjection;
        float m_ProjectionDistance = 0.1f, m_ProjectionAngle = 180f;

        public CharacterJoint() { }

        public Vector3 swingAxis { get => m_SwingAxis; set { m_SwingAxis = value; FramesChanged(); } }
        public SoftJointLimitSpring twistLimitSpring { get => m_TwistLimitSpring; set { m_TwistLimitSpring = value; Changed(); } }
        public SoftJointLimitSpring swingLimitSpring { get => m_SwingLimitSpring; set { m_SwingLimitSpring = value; Changed(); } }
        public SoftJointLimit lowTwistLimit { get => m_LowTwistLimit; set { m_LowTwistLimit = value; Changed(); } }
        public SoftJointLimit highTwistLimit { get => m_HighTwistLimit; set { m_HighTwistLimit = value; Changed(); } }
        public SoftJointLimit swing1Limit { get => m_Swing1Limit; set { m_Swing1Limit = value; Changed(); } }
        public SoftJointLimit swing2Limit { get => m_Swing2Limit; set { m_Swing2Limit = value; Changed(); } }
        public bool enableProjection { get => m_EnableProjection; set { m_EnableProjection = value; Changed(); } }
        public float projectionDistance { get => m_ProjectionDistance; set { m_ProjectionDistance = value; Changed(); } }
        public float projectionAngle { get => m_ProjectionAngle; set { m_ProjectionAngle = value; Changed(); } }

        internal override Vector3 SecondaryAxis => m_SwingAxis;

        internal override void Configure()
        {
            B3.ua3_d6_set_motion(m_Joint, 3, 1);
            B3.ua3_d6_set_motion(m_Joint, 4, 1);
            B3.ua3_d6_set_motion(m_Joint, 5, 1);
            // Unlike the hinge's, the twist limits pass as they are (measured).
            B3.ua3_d6_set_twist_limit(m_Joint, Rad(m_LowTwistLimit.limit), Rad(m_HighTwistLimit.limit), m_TwistLimitSpring.spring, m_TwistLimitSpring.damper,
                Mathf.Clamp01(m_LowTwistLimit.bounciness), BounceThreshold(0f), DefaultedContact(m_LowTwistLimit.contactDistance));
            B3.ua3_d6_set_swing_limit(m_Joint, Rad(Mathf.Max(m_Swing1Limit.limit, 0.01f)), Rad(Mathf.Max(m_Swing2Limit.limit, 0.01f)), m_SwingLimitSpring.spring, m_SwingLimitSpring.damper,
                Mathf.Clamp01(m_Swing1Limit.bounciness), BounceThreshold(0f), DefaultedContact(m_Swing1Limit.contactDistance));
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            base.CopyFrom(source, remap);
            var s = (CharacterJoint)source;
            m_SwingAxis = s.m_SwingAxis;
            m_TwistLimitSpring = s.m_TwistLimitSpring;
            m_SwingLimitSpring = s.m_SwingLimitSpring;
            m_LowTwistLimit = s.m_LowTwistLimit;
            m_HighTwistLimit = s.m_HighTwistLimit;
            m_Swing1Limit = s.m_Swing1Limit;
            m_Swing2Limit = s.m_Swing2Limit;
            m_EnableProjection = s.m_EnableProjection;
            m_ProjectionDistance = s.m_ProjectionDistance;
            m_ProjectionAngle = s.m_ProjectionAngle;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            base.ReadSerialized(body, resolver);
            m_SwingAxis = body.Vector3("m_SwingAxis", Vector3.up);
            m_TwistLimitSpring = ReadSoftSpring(body.Get("m_TwistLimitSpring"));
            m_SwingLimitSpring = ReadSoftSpring(body.Get("m_SwingLimitSpring"));
            m_LowTwistLimit = ReadSoft(body.Get("m_LowTwistLimit"));
            m_HighTwistLimit = ReadSoft(body.Get("m_HighTwistLimit"));
            m_Swing1Limit = ReadSoft(body.Get("m_Swing1Limit"));
            m_Swing2Limit = ReadSoft(body.Get("m_Swing2Limit"));
            m_EnableProjection = body.Bool("m_EnableProjection");
            m_ProjectionDistance = body.Float("m_ProjectionDistance", 0.1f);
            m_ProjectionAngle = body.Float("m_ProjectionAngle", 180f);
        }
    }

    /// <summary>A configurable joint: PhysX's D6 joint, each axis locked, limited or free, with
    /// linear and angular limits and drives.</summary>
    [ClassId(153)]
    public partial class ConfigurableJoint : Joint
    {
        Vector3 m_SecondaryAxis = Vector3.up;
        ConfigurableJointMotion m_XMotion = ConfigurableJointMotion.Free, m_YMotion = ConfigurableJointMotion.Free, m_ZMotion = ConfigurableJointMotion.Free;
        ConfigurableJointMotion m_AngularXMotion = ConfigurableJointMotion.Free, m_AngularYMotion = ConfigurableJointMotion.Free, m_AngularZMotion = ConfigurableJointMotion.Free;
        SoftJointLimitSpring m_LinearLimitSpring, m_AngularXLimitSpring, m_AngularYZLimitSpring;
        SoftJointLimit m_LinearLimit, m_LowAngularXLimit, m_HighAngularXLimit, m_AngularYLimit, m_AngularZLimit;
        Vector3 m_TargetPosition, m_TargetVelocity, m_TargetAngularVelocity;
        Quaternion m_TargetRotation = Quaternion.identity;
        JointDrive m_XDrive = JointDrive.Default, m_YDrive = JointDrive.Default, m_ZDrive = JointDrive.Default;
        JointDrive m_AngularXDrive = JointDrive.Default, m_AngularYZDrive = JointDrive.Default, m_SlerpDrive = JointDrive.Default;
        RotationDriveMode m_RotationDriveMode;
        JointProjectionMode m_ProjectionMode;
        float m_ProjectionDistance = 0.1f, m_ProjectionAngle = 180f;
        bool m_ConfiguredInWorldSpace, m_SwapBodies;

        public ConfigurableJoint() { }

        public Vector3 secondaryAxis { get => m_SecondaryAxis; set { m_SecondaryAxis = value; FramesChanged(); } }
        public ConfigurableJointMotion xMotion { get => m_XMotion; set { m_XMotion = value; Changed(); } }
        public ConfigurableJointMotion yMotion { get => m_YMotion; set { m_YMotion = value; Changed(); } }
        public ConfigurableJointMotion zMotion { get => m_ZMotion; set { m_ZMotion = value; Changed(); } }
        public ConfigurableJointMotion angularXMotion { get => m_AngularXMotion; set { m_AngularXMotion = value; Changed(); } }
        public ConfigurableJointMotion angularYMotion { get => m_AngularYMotion; set { m_AngularYMotion = value; Changed(); } }
        public ConfigurableJointMotion angularZMotion { get => m_AngularZMotion; set { m_AngularZMotion = value; Changed(); } }
        public SoftJointLimitSpring linearLimitSpring { get => m_LinearLimitSpring; set { m_LinearLimitSpring = value; Changed(); } }
        public SoftJointLimitSpring angularXLimitSpring { get => m_AngularXLimitSpring; set { m_AngularXLimitSpring = value; Changed(); } }
        public SoftJointLimitSpring angularYZLimitSpring { get => m_AngularYZLimitSpring; set { m_AngularYZLimitSpring = value; Changed(); } }
        public SoftJointLimit linearLimit { get => m_LinearLimit; set { m_LinearLimit = value; Changed(); } }
        public SoftJointLimit lowAngularXLimit { get => m_LowAngularXLimit; set { m_LowAngularXLimit = value; Changed(); } }
        public SoftJointLimit highAngularXLimit { get => m_HighAngularXLimit; set { m_HighAngularXLimit = value; Changed(); } }
        public SoftJointLimit angularYLimit { get => m_AngularYLimit; set { m_AngularYLimit = value; Changed(); } }
        public SoftJointLimit angularZLimit { get => m_AngularZLimit; set { m_AngularZLimit = value; Changed(); } }
        public Vector3 targetPosition { get => m_TargetPosition; set { m_TargetPosition = value; Changed(); } }
        public Vector3 targetVelocity { get => m_TargetVelocity; set { m_TargetVelocity = value; Changed(); } }
        public JointDrive xDrive { get => m_XDrive; set { m_XDrive = value; Changed(); } }
        public JointDrive yDrive { get => m_YDrive; set { m_YDrive = value; Changed(); } }
        public JointDrive zDrive { get => m_ZDrive; set { m_ZDrive = value; Changed(); } }
        public Quaternion targetRotation { get => m_TargetRotation; set { m_TargetRotation = value; Changed(); } }
        public Vector3 targetAngularVelocity { get => m_TargetAngularVelocity; set { m_TargetAngularVelocity = value; Changed(); } }
        public RotationDriveMode rotationDriveMode { get => m_RotationDriveMode; set { m_RotationDriveMode = value; Changed(); } }
        public JointDrive angularXDrive { get => m_AngularXDrive; set { m_AngularXDrive = value; Changed(); } }
        public JointDrive angularYZDrive { get => m_AngularYZDrive; set { m_AngularYZDrive = value; Changed(); } }
        public JointDrive slerpDrive { get => m_SlerpDrive; set { m_SlerpDrive = value; Changed(); } }
        public JointProjectionMode projectionMode { get => m_ProjectionMode; set { m_ProjectionMode = value; Changed(); } }
        public float projectionDistance { get => m_ProjectionDistance; set { m_ProjectionDistance = value; Changed(); } }
        public float projectionAngle { get => m_ProjectionAngle; set { m_ProjectionAngle = value; Changed(); } }
        public bool configuredInWorldSpace { get => m_ConfiguredInWorldSpace; set { m_ConfiguredInWorldSpace = value; FramesChanged(); } }
        public bool swapBodies { get => m_SwapBodies; set { m_SwapBodies = value; Rebuild(); } }

        internal override Vector3 SecondaryAxis => m_SecondaryAxis;

        internal override void Configure()
        {
            B3.ua3_d6_set_motion(m_Joint, 0, (int)m_XMotion);
            B3.ua3_d6_set_motion(m_Joint, 1, (int)m_YMotion);
            B3.ua3_d6_set_motion(m_Joint, 2, (int)m_ZMotion);
            B3.ua3_d6_set_motion(m_Joint, 3, (int)m_AngularXMotion);
            B3.ua3_d6_set_motion(m_Joint, 4, (int)m_AngularYMotion);
            B3.ua3_d6_set_motion(m_Joint, 5, (int)m_AngularZMotion);
            B3.ua3_d6_set_linear_limit(m_Joint, Mathf.Max(0f, m_LinearLimit.limit), m_LinearLimitSpring.spring, m_LinearLimitSpring.damper,
                Mathf.Clamp01(m_LinearLimit.bounciness), BounceThreshold(0f), m_LinearLimit.contactDistance);
            B3.ua3_d6_set_twist_limit(m_Joint, Rad(m_LowAngularXLimit.limit), Rad(m_HighAngularXLimit.limit), m_AngularXLimitSpring.spring, m_AngularXLimitSpring.damper,
                Mathf.Clamp01(m_LowAngularXLimit.bounciness), BounceThreshold(0f), DefaultedContact(m_LowAngularXLimit.contactDistance));
            B3.ua3_d6_set_swing_limit(m_Joint, Rad(Mathf.Max(m_AngularYLimit.limit, 0.01f)), Rad(Mathf.Max(m_AngularZLimit.limit, 0.01f)), m_AngularYZLimitSpring.spring,
                m_AngularYZLimitSpring.damper, Mathf.Clamp01(m_AngularYLimit.bounciness), BounceThreshold(0f), DefaultedContact(m_AngularYLimit.contactDistance));
            SetDrive(m_Joint, 0, m_XDrive);
            SetDrive(m_Joint, 1, m_YDrive);
            SetDrive(m_Joint, 2, m_ZDrive);
            bool slerp = m_RotationDriveMode == RotationDriveMode.Slerp;
            SetDrive(m_Joint, 3, slerp ? default : m_AngularYZDrive);
            SetDrive(m_Joint, 4, slerp ? default : m_AngularXDrive);
            SetDrive(m_Joint, 5, slerp ? m_SlerpDrive : default);
            B3.ua3_d6_set_drive_target(m_Joint, m_TargetPosition, m_TargetRotation, m_TargetVelocity, m_TargetAngularVelocity);
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            base.CopyFrom(source, remap);
            var s = (ConfigurableJoint)source;
            m_SecondaryAxis = s.m_SecondaryAxis;
            m_XMotion = s.m_XMotion; m_YMotion = s.m_YMotion; m_ZMotion = s.m_ZMotion;
            m_AngularXMotion = s.m_AngularXMotion; m_AngularYMotion = s.m_AngularYMotion; m_AngularZMotion = s.m_AngularZMotion;
            m_LinearLimitSpring = s.m_LinearLimitSpring; m_AngularXLimitSpring = s.m_AngularXLimitSpring; m_AngularYZLimitSpring = s.m_AngularYZLimitSpring;
            m_LinearLimit = s.m_LinearLimit; m_LowAngularXLimit = s.m_LowAngularXLimit; m_HighAngularXLimit = s.m_HighAngularXLimit;
            m_AngularYLimit = s.m_AngularYLimit; m_AngularZLimit = s.m_AngularZLimit;
            m_TargetPosition = s.m_TargetPosition; m_TargetVelocity = s.m_TargetVelocity; m_TargetAngularVelocity = s.m_TargetAngularVelocity;
            m_TargetRotation = s.m_TargetRotation;
            m_XDrive = s.m_XDrive; m_YDrive = s.m_YDrive; m_ZDrive = s.m_ZDrive;
            m_AngularXDrive = s.m_AngularXDrive; m_AngularYZDrive = s.m_AngularYZDrive; m_SlerpDrive = s.m_SlerpDrive;
            m_RotationDriveMode = s.m_RotationDriveMode; m_ProjectionMode = s.m_ProjectionMode;
            m_ProjectionDistance = s.m_ProjectionDistance; m_ProjectionAngle = s.m_ProjectionAngle;
            m_ConfiguredInWorldSpace = s.m_ConfiguredInWorldSpace; m_SwapBodies = s.m_SwapBodies;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            base.ReadSerialized(body, resolver);
            m_SecondaryAxis = body.Vector3("m_SecondaryAxis", Vector3.up);
            m_XMotion = (ConfigurableJointMotion)body.Int("m_XMotion", 2);
            m_YMotion = (ConfigurableJointMotion)body.Int("m_YMotion", 2);
            m_ZMotion = (ConfigurableJointMotion)body.Int("m_ZMotion", 2);
            m_AngularXMotion = (ConfigurableJointMotion)body.Int("m_AngularXMotion", 2);
            m_AngularYMotion = (ConfigurableJointMotion)body.Int("m_AngularYMotion", 2);
            m_AngularZMotion = (ConfigurableJointMotion)body.Int("m_AngularZMotion", 2);
            m_LinearLimitSpring = ReadSoftSpring(body.Get("m_LinearLimitSpring"));
            m_AngularXLimitSpring = ReadSoftSpring(body.Get("m_AngularXLimitSpring"));
            m_AngularYZLimitSpring = ReadSoftSpring(body.Get("m_AngularYZLimitSpring"));
            m_LinearLimit = ReadSoft(body.Get("m_LinearLimit"));
            m_LowAngularXLimit = ReadSoft(body.Get("m_LowAngularXLimit"));
            m_HighAngularXLimit = ReadSoft(body.Get("m_HighAngularXLimit"));
            m_AngularYLimit = ReadSoft(body.Get("m_AngularYLimit"));
            m_AngularZLimit = ReadSoft(body.Get("m_AngularZLimit"));
            m_TargetPosition = body.Vector3("m_TargetPosition");
            m_TargetVelocity = body.Vector3("m_TargetVelocity");
            m_XDrive = ReadDrive(body.Get("m_XDrive"));
            m_YDrive = ReadDrive(body.Get("m_YDrive"));
            m_ZDrive = ReadDrive(body.Get("m_ZDrive"));
            m_TargetRotation = body.Get("m_TargetRotation") != null ? body.Quaternion("m_TargetRotation") : Quaternion.identity;
            m_TargetAngularVelocity = body.Vector3("m_TargetAngularVelocity");
            m_RotationDriveMode = (RotationDriveMode)body.Int("m_RotationDriveMode");
            m_AngularXDrive = ReadDrive(body.Get("m_AngularXDrive"));
            m_AngularYZDrive = ReadDrive(body.Get("m_AngularYZDrive"));
            m_SlerpDrive = ReadDrive(body.Get("m_SlerpDrive"));
            m_ProjectionMode = (JointProjectionMode)body.Int("m_ProjectionMode");
            m_ProjectionDistance = body.Float("m_ProjectionDistance", 0.1f);
            m_ProjectionAngle = body.Float("m_ProjectionAngle", 180f);
            m_ConfiguredInWorldSpace = body.Bool("m_ConfiguredInWorldSpace");
            m_SwapBodies = body.Bool("m_SwapBodies");
        }
    }
}
