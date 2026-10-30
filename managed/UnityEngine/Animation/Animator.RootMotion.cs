namespace UnityEngine
{
    public partial class Animator
    {
        internal Vector3 m_RootVelocity, m_RootAngularVelocity;
        internal bool m_RootMotionReady, m_RootMotionApplied;
        internal Quaternion m_MotionRootRotation = Quaternion.identity;
        Vector3 m_MotionRootPosition;
        Vector3 m_RootPosition;
        Quaternion m_RootRotation = Quaternion.identity;
        int m_RootPoseVersion = int.MinValue;

        public Vector3 velocity => m_RootVelocity;
        public Vector3 angularVelocity => m_RootAngularVelocity;

        // The API pose is separate from the Transform. Setters change that pose without moving
        // the GameObject; evaluation replaces it with the root motion pose (animator probe).
        public Vector3 rootPosition
        {
            get { EnsureRootPose(); return m_RootPosition; }
            set { EnsureRootPose(); m_RootPosition = value; }
        }
        public Quaternion rootRotation
        {
            get { EnsureRootPose(); return m_RootRotation; }
            set { EnsureRootPose(); m_RootRotation = value; }
        }

        void EnsureRootPose() { if (m_RootPoseVersion == int.MinValue) SyncRootPose(); }

        void SyncRootPose()
        {
            var t = transform;
            if (m_RootPoseVersion == t.m_Version) return;
            var parent = t.parent;
            m_MotionRootPosition = parent == null ? t.localPosition
                : parent.position + parent.rotation * Vector3.Scale(parent.lossyScale, t.localPosition);
            // The generic animation stream composes its root rotation without the parent's
            // reflection. The Transform's physical world rotation can consequently differ.
            m_MotionRootRotation = parent == null ? t.localRotation : parent.rotation * t.localRotation;
            m_RootPoseVersion = t.m_Version;
            m_RootPosition = m_MotionRootPosition;
            m_RootRotation = m_MotionRootRotation;
        }

        internal void CaptureRootMotionPose()
        {
            SyncRootPose();
            m_RootPosition = m_MotionRootPosition;
            m_RootRotation = m_MotionRootRotation;
        }

        public void ApplyBuiltinRootMotion()
        {
            if (!m_RootMotionReady || m_RootMotionApplied) return;
            m_RootMotionApplied = true;
            SyncRootPose();
            var body = GetComponent<Rigidbody>();
            if (body != null)
            {
                if (body.isKinematic) body.position += m_DeltaPosition;
                else
                {
                    var velocity = Time.deltaTime > 0f ? m_DeltaPosition / Time.deltaTime : Vector3.zero;
                    var up = m_MotionRootRotation * Vector3.up;
                    velocity += up * Vector3.Dot(body.linearVelocity, up);
                    body.linearVelocity = velocity;
                }
                body.rotation = m_MotionRootRotation * m_DeltaRotation;
                return;
            }
            var controller = GetComponent<CharacterController>();
            if (controller != null)
            {
                controller.ApplyAnimatorMotion(m_DeltaPosition);
                transform.rotation *= m_DeltaRotation;
                SyncRootPose();
                return;
            }
            m_MotionRootPosition += m_DeltaPosition;
            m_MotionRootRotation *= m_DeltaRotation;
            var t = transform;
            var parent = t.parent;
            if (parent == null) t.SetLocalPositionAndRotation(m_MotionRootPosition, m_MotionRootRotation);
            else
            {
                // Unity's animation stream converts with the parent's decomposed TRS, including
                // signed scale. A sheared hierarchy's actual Transform pose can therefore differ.
                var inverse = Quaternion.Inverse(parent.rotation);
                var scale = parent.lossyScale;
                var position = inverse * (m_MotionRootPosition - parent.position);
                position = new Vector3(scale.x != 0f ? position.x / scale.x : 0f,
                    scale.y != 0f ? position.y / scale.y : 0f, scale.z != 0f ? position.z / scale.z : 0f);
                var rotation = inverse * m_MotionRootRotation;
                t.SetLocalPositionAndRotation(position, rotation);
            }
            m_RootPoseVersion = t.m_Version;
            m_RootPosition = m_MotionRootPosition;
            m_RootRotation = m_MotionRootRotation;
        }
    }
}
