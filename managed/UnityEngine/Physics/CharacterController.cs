using System;
using Unbound.Box3D;
using Unbound.Runtime;
using W = Unbound.Runtime.PhysicsWorld;

namespace UnityEngine
{
    /// <summary>
    /// A capsule moved by <see cref="Move"/>: PhysX's character controller (a PxCapsuleController
    /// in the scene's controller manager), as Unity runs it. Its kinematic actor (PhysX's, 0.8 of
    /// the capsule) is the collider queries, triggers and bodies meet; the controller itself sweeps
    /// the full capsule plus the skin width (PhysX's contact offset) against the layers its layer
    /// collides with. Measured against the Editor: tests/projects/controller.
    /// </summary>
    [ClassId(143)]
    public partial class CharacterController : Collider
    {
        // PhysX's defaults for what Unity does not expose (the kinematic actor's scale, density
        // and the cached volume's growth) and the capsule's climbing on step edges.
        const float k_ScaleCoeff = 0.8f;
        const float k_Density = 10f;
        const float k_VolumeGrowth = 1.5f;
        const int k_ClimbingMode = 1;
        const int k_NonWalkableMode = 0;

        internal float m_Height = 2f;
        internal float m_Radius = 0.5f;
        internal float m_SlopeLimit = 45f;
        internal float m_StepOffset = 0.3f;
        internal float m_SkinWidth = 0.08f;
        internal float m_MinMoveDistance = 0.001f;
        internal Vector3 m_Center;
        bool m_DetectCollisions = true;
        bool m_EnableOverlapRecovery = true;

        internal ulong m_Controller;
        Vector3 m_Velocity;
        CollisionFlags m_CollisionFlags;
        /// <summary>SimpleMove's own velocity: the speed taken on the ground, gravity added each call.</summary>
        Vector3 m_SimpleVelocity;
        static B3.ControllerHit[] s_Hits = new B3.ControllerHit[16];

        public CharacterController() { }

        // ------------------------------------------------------------------ moving

        public CollisionFlags Move(Vector3 motion)
        {
            if (m_Registered) W.Flush();
            if (!m_Registered || m_Controller == 0)
            {
                Debug.LogError("CharacterController.Move called on inactive controller");
                return CollisionFlags.None;
            }
            float dt = Time.deltaTime;
            var before = B3.ua3_cct_position(m_Controller);
            int flags = B3.ua3_cct_move(m_Controller, motion, m_MinMoveDistance, dt, m_EnableOverlapRecovery ? 1 : 0);
            var after = B3.ua3_cct_position(m_Controller);
            var t = transform;
            var offset = t.TransformPoint(m_Center) - t.position;
            t.position = after - offset;
            m_TransformVersion = t.m_Version;
            m_Velocity = dt > 0f ? (after - before) / dt : Vector3.zero;
            m_CollisionFlags = (CollisionFlags)flags;
            SendHits();
            return m_CollisionFlags;
        }

        public bool SimpleMove(Vector3 speed)
        {
            float dt = Time.deltaTime;
            if (isGrounded) m_SimpleVelocity = new Vector3(speed.x, 0f, speed.z);
            m_SimpleVelocity += Physics.gravity * dt;
            Move(m_SimpleVelocity * dt);
            return isGrounded;
        }

        /// <summary>OnControllerColliderHit for each shape the last move touched, after it.</summary>
        unsafe void SendHits()
        {
            int count;
            fixed (B3.ControllerHit* p = s_Hits) count = B3.ua3_cct_hits(m_Controller, p, s_Hits.Length);
            if (count > s_Hits.Length)
            {
                s_Hits = new B3.ControllerHit[count];
                fixed (B3.ControllerHit* p = s_Hits) count = B3.ua3_cct_hits(m_Controller, p, s_Hits.Length);
            }
            if (count == 0) return;
            var hits = new B3.ControllerHit[count];
            Array.Copy(s_Hits, hits, count);
            var go = gameObject;
            foreach (var h in hits)
            {
                var other = W.ColliderOf(h.Shape);
                if (other == null || go == null || !go.activeInHierarchy) continue;
                var hit = new ControllerColliderHit
                {
                    m_Controller = this,
                    m_Collider = other,
                    m_Point = h.Point,
                    m_Normal = h.Normal,
                    m_MoveDirection = h.Direction,
                    m_MoveLength = h.Length,
                };
                Physics2DWorld.Deliver(go, "OnControllerColliderHit", typeof(ControllerColliderHit), hit);
            }
        }

        // ------------------------------------------------------------------ state

        public Vector3 velocity => m_Velocity;

        public bool isGrounded => (m_CollisionFlags & CollisionFlags.Below) != 0;

        public CollisionFlags collisionFlags => m_CollisionFlags;

        public float radius
        {
            get => m_Radius;
            set { m_Radius = value; Reconfigure(); }
        }

        public float height
        {
            get => m_Height;
            set { m_Height = value; Reconfigure(); }
        }

        public Vector3 center
        {
            get => m_Center;
            set { m_Center = value; Reconfigure(); }
        }

        public float slopeLimit
        {
            get => m_SlopeLimit;
            set { m_SlopeLimit = value; Reconfigure(); }
        }

        public float stepOffset
        {
            get => m_StepOffset;
            set { m_StepOffset = value; Reconfigure(); }
        }

        public float skinWidth
        {
            get => m_SkinWidth;
            set { m_SkinWidth = value; Reconfigure(); }
        }

        public float minMoveDistance
        {
            get => m_MinMoveDistance;
            set => m_MinMoveDistance = value;
        }

        public bool detectCollisions
        {
            get => m_DetectCollisions;
            set
            {
                m_DetectCollisions = value;
                W.Refilter(this);
            }
        }

        public bool enableOverlapRecovery
        {
            get => m_EnableOverlapRecovery;
            set => m_EnableOverlapRecovery = value;
        }

        // ------------------------------------------------------------------ the PhysX controller

        /// <summary>The capsule in the world from a transform matrix: its centre, radius (by the
        /// larger horizontal scale) and the height between the caps' centres (by the vertical one).</summary>
        void Geometry(Matrix4x4 m, out Vector3 centre, out float radius, out float cylinder)
        {
            var s = AxisScales(m);
            radius = Mathf.Max(1e-4f, Mathf.Abs(m_Radius) * Mathf.Max(s.x, s.z));
            cylinder = Mathf.Max(0f, Mathf.Abs(m_Height) * s.y - 2f * radius);
            centre = m.MultiplyPoint3x4(m_Center);
        }

        float SlopeCosine => Mathf.Cos(Mathf.Clamp(m_SlopeLimit, 0f, 180f) * Mathf.Deg2Rad);

        float StepOffsetFor(float radius, float cylinder) => Mathf.Clamp(m_StepOffset, 0f, cylinder + 2f * radius);

        internal void Build(in B3.ShapeDef shape, Matrix4x4 matrix)
        {
            DestroyController();
            Geometry(matrix, out var centre, out float r, out float cylinder);
            var def = new B3.ControllerDef
            {
                Position = centre,
                Radius = r,
                Height = cylinder,
                ContactOffset = Mathf.Max(1e-4f, m_SkinWidth),
                StepOffset = StepOffsetFor(r, cylinder),
                SlopeLimit = SlopeCosine,
                ClimbingMode = k_ClimbingMode,
                NonWalkableMode = k_NonWalkableMode,
                Density = k_Density,
                ScaleCoeff = k_ScaleCoeff,
                VolumeGrowth = k_VolumeGrowth,
                CategoryBits = shape.CategoryBits,
                MaskBits = shape.MaskBits,
                Friction = shape.Friction,
                Restitution = shape.Restitution,
                Material = shape.Material,
                StaticFriction = shape.StaticFriction,
                DetectCollisions = m_DetectCollisions ? 1 : 0,
            };
            m_Controller = B3.ua3_cct_create(W.World, def);
            m_Shape = m_Controller != 0 ? B3.ua3_cct_shape(m_Controller) : 0;
        }

        internal void DestroyController()
        {
            if (m_Controller != 0) B3.ua3_cct_destroy(m_Controller);
            m_Controller = 0;
            m_Shape = 0;
        }

        internal void SetFilter(ulong category, ulong mask)
        {
            if (m_Controller != 0) B3.ua3_cct_set_filter(m_Controller, category, mask, m_DetectCollisions ? 1 : 0);
        }

        /// <summary>The transform moved or scaled: the capsule follows it.</summary>
        internal void SyncFromTransform()
        {
            if (m_Controller == 0) return;
            Geometry(transform.localToWorldMatrix, out var centre, out float r, out float cylinder);
            B3.ua3_cct_configure(m_Controller, r, cylinder, Mathf.Max(1e-4f, m_SkinWidth), StepOffsetFor(r, cylinder), SlopeCosine);
            B3.ua3_cct_set_position(m_Controller, centre);
        }

        void Reconfigure()
        {
            if (m_Controller != 0) SyncFromTransform();
        }

        /// <summary>The capsule's bounds (without the skin).</summary>
        internal Bounds ControllerBounds()
        {
            Geometry(transform.localToWorldMatrix, out var centre, out float r, out float cylinder);
            if (m_Controller != 0) centre = B3.ua3_cct_position(m_Controller);
            return new Bounds(centre, new Vector3(2f * r, cylinder + 2f * r, 2f * r));
        }

        internal override Bounds LocalBounds() =>
            new Bounds(m_Center, new Vector3(2f * Mathf.Abs(m_Radius), Mathf.Max(Mathf.Abs(m_Height), 2f * Mathf.Abs(m_Radius)), 2f * Mathf.Abs(m_Radius)));

        // ------------------------------------------------------------------ serialization

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            ReadColliderData(body, resolver);
            m_Height = body.Float("m_Height", 2f);
            m_Radius = body.Float("m_Radius", 0.5f);
            m_SlopeLimit = body.Float("m_SlopeLimit", 45f);
            m_StepOffset = body.Float("m_StepOffset", 0.3f);
            m_SkinWidth = body.Float("m_SkinWidth", 0.08f);
            m_MinMoveDistance = body.Float("m_MinMoveDistance", 0.001f);
            m_Center = body.Vector3("m_Center");
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (CharacterController)source;
            CopyColliderData(s);
            m_Height = s.m_Height;
            m_Radius = s.m_Radius;
            m_SlopeLimit = s.m_SlopeLimit;
            m_StepOffset = s.m_StepOffset;
            m_SkinWidth = s.m_SkinWidth;
            m_MinMoveDistance = s.m_MinMoveDistance;
            m_Center = s.m_Center;
            m_DetectCollisions = s.m_DetectCollisions;
            m_EnableOverlapRecovery = s.m_EnableOverlapRecovery;
        }
    }

    public enum CollisionFlags
    {
        None = 0,
        Sides = 1,
        Above = 2,
        Below = 4,
        CollidedSides = 1,
        CollidedAbove = 2,
        CollidedBelow = 4,
    }

    /// <summary>What a CharacterController's move touched (OnControllerColliderHit).</summary>
    public partial class ControllerColliderHit
    {
        internal CharacterController m_Controller;
        internal Collider m_Collider;
        internal Vector3 m_Point;
        internal Vector3 m_Normal;
        internal Vector3 m_MoveDirection;
        internal float m_MoveLength;
        internal int m_Push;

        public ControllerColliderHit() { }

        public CharacterController controller => m_Controller;
        public Collider collider => m_Collider;
        public Rigidbody rigidbody => m_Collider != null ? m_Collider.attachedRigidbody : null;
        public GameObject gameObject => m_Collider != null ? m_Collider.gameObject : null;
        public Transform transform => m_Collider != null ? m_Collider.transform : null;
        public Vector3 point => m_Point;
        public Vector3 normal => m_Normal;
        public Vector3 moveDirection => m_MoveDirection;
        public float moveLength => m_MoveLength;
    }
}
