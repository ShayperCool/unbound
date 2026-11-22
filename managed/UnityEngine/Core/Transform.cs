using System;
using System.Collections;
using System.Collections.Generic;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>
    /// Position, rotation and scale, and the hierarchy. The local values are the state; the world
    /// matrix and rotation are cached and recomputed lazily after this transform or a parent changed.
    /// </summary>
    public partial class Transform : Component, IEnumerable
    {
        internal Vector3 m_LocalPosition;
        internal Quaternion m_LocalRotation = Quaternion.identity;
        internal Vector3 m_LocalScale = Vector3.one;
        internal Transform m_Parent;
        internal readonly List<Transform> m_Children = new List<Transform>();
        internal bool m_ConstrainProportionsScale;
        internal Vector3 m_LocalEulerAnglesHint;

        Matrix4x4 m_LocalToWorld = Matrix4x4.identity;
        Quaternion m_WorldRotation = Quaternion.identity;
        bool m_WorldDirty = true;
        bool m_HasChanged = true;
        /// <summary>Bumped on every change of this transform or a parent: lets consumers see changes.</summary>
        internal int m_Version;

        /// <summary>While an Animator with animatePhysics writes its pose: the stamp of that write
        /// (<see cref="Unbound.Runtime.AnimatedPhysicsWrites"/>); 0 otherwise.</summary>
        internal static long s_AnimatedPhysicsStamp;
        /// <summary>The stamp of the last such write that changed this transform.</summary>
        internal long m_AnimatedPhysicsStamp;

        protected Transform()
        {
        }

        internal override void OnHideFlagsChanged(HideFlags previous)
        {
            if (!Compat.HideFlagsRootOwnershipMeasured || previous == hideFlags) return;
            var go = m_GameObject;
            if (go == null || go.m_Transform != this || m_Parent != null) return;
            SceneRegistry.UpdateRootHideFlags(go, previous, hideFlags);
        }

        // ------------------------------------------------------------------ local state

        public Vector3 localPosition
        {
            get
            {
                MainThreadCheck.Check("get_localPosition");
                SyncJobs();
                return GetLocalPosition();
            }
            set
            {
                MainThreadCheck.Check("set_localPosition");
                if (!IsFinite(value)) { InvalidValue("localPosition", value); return; }
                SyncJobs();
                SetLocalPosition(value);
            }
        }

        /// <summary>The main thread waits for the transform jobs (IJobParallelForTransform) before it
        /// touches transforms, as in Unity.</summary>
        internal static void SyncJobs()
        {
            if (Unbound.Runtime.Jobs.JobSystem.TransformJobsPending) Unbound.Runtime.Jobs.JobSystem.SyncTransformJobs();
        }

        /// <summary><see cref="m_Version"/> once the jobs that write transforms are done: the same
        /// value means the same world placement.</summary>
        internal int SyncedVersion
        {
            get
            {
                SyncJobs();
                return m_Version;
            }
        }

        internal virtual Vector3 GetLocalPosition() => m_LocalPosition;

        internal virtual void SetLocalPosition(Vector3 value)
        {
            m_LocalPosition = value;
            MarkChanged();
        }

        public Quaternion localRotation
        {
            get
            {
                MainThreadCheck.Check("get_localRotation");
                SyncJobs();
                return m_LocalRotation;
            }
            set
            {
                MainThreadCheck.Check("set_localRotation");
                SyncJobs();
                m_LocalRotation = NormalizeParentingRotation(value);
                MarkChanged();
            }
        }

        public Vector3 localScale
        {
            get
            {
                MainThreadCheck.Check("get_localScale");
                SyncJobs();
                return m_LocalScale;
            }
            set
            {
                MainThreadCheck.Check("set_localScale");
                if (!IsFinite(value)) { InvalidValue("localScale", value); return; }
                SyncJobs();
                m_LocalScale = value;
                MarkChanged();
            }
        }

        public Vector3 localEulerAngles
        {
            get => m_LocalRotation.eulerAngles;
            set => localRotation = Quaternion.Euler(value);
        }

        /// <summary>The rotation order the Inspector shows Euler angles in; Unity's own order
        /// (Z, then X, then Y) by default. It changes no rotation (measured: tests/projects/runtimeapis
        /// transform.*).</summary>
        internal RotationOrder rotationOrder { get => m_RotationOrder; set => m_RotationOrder = value; }
        RotationOrder m_RotationOrder = RotationOrder.OrderZXY;

        internal int GetRotationOrderInternal() => (int)m_RotationOrder;
        internal void SetRotationOrderInternal(RotationOrder rotationOrder) => m_RotationOrder = rotationOrder;

        /// <summary>The local rotation as angles about the axes applied in the order's sequence (the
        /// first letter first), in single precision from the normalized rotation: the middle angle
        /// from its sine; in gimbal lock (|sine| at least 0.999998) the last angle is zero and the
        /// first is twice the half-angle of the rotation's own component. Of the two sets of angles
        /// that make the rotation, each angle turned by whole turns to the one nearest the Euler
        /// hint's, the set nearer the hint, rounded to a thousandth of a degree (measured:
        /// tests/projects/runtimeapis transform.*euler_order*).</summary>
        internal Vector3 GetLocalEulerAngles(RotationOrder order)
        {
            const float gimbalLock = 0.999998f;
            var (i, j, k) = order switch
            {
                RotationOrder.OrderXYZ => (0, 1, 2),
                RotationOrder.OrderXZY => (0, 2, 1),
                RotationOrder.OrderYZX => (1, 2, 0),
                RotationOrder.OrderYXZ => (1, 0, 2),
                RotationOrder.OrderZYX => (2, 1, 0),
                _ => (2, 0, 1),
            };
            Quaternion r = m_LocalRotation;
            float length = MathF.Sqrt(r.x * r.x + r.y * r.y + r.z * r.z + r.w * r.w);
            Quaternion q = length < RotationMatrix.MinimumLength ? Quaternion.identity
                : new Quaternion(r.x / length, r.y / length, r.z / length, r.w / length);
            float x = q.x, y = q.y, z = q.z, w = q.w;
            // The rotation matrix, then the angles of R(k) * R(j) * R(i).
            float m00 = 1f - 2f * (y * y + z * z), m01 = 2f * (x * y - z * w), m02 = 2f * (x * z + y * w);
            float m10 = 2f * (x * y + z * w), m11 = 1f - 2f * (x * x + z * z), m12 = 2f * (y * z - x * w);
            float m20 = 2f * (x * z - y * w), m21 = 2f * (y * z + x * w), m22 = 1f - 2f * (x * x + y * y);
            float M(int row, int column) => row switch
            {
                0 => column == 0 ? m00 : column == 1 ? m01 : m02,
                1 => column == 0 ? m10 : column == 1 ? m11 : m12,
                _ => column == 0 ? m20 : column == 1 ? m21 : m22,
            };
            float parity = (j - i + 3) % 3 == 1 ? 1f : -1f;
            float sinMiddle = Math.Clamp(-parity * M(k, i), -1f, 1f);
            float first, middle = MathF.Asin(sinMiddle), last;
            if (MathF.Abs(sinMiddle) < gimbalLock)
            {
                first = MathF.Atan2(parity * M(k, j), M(k, k));
                last = MathF.Atan2(parity * M(j, i), M(i, i));
            }
            else
            {
                first = 2f * MathF.Atan2(q[i], w);
                last = 0f;
            }
            const double toDegrees = 180 / Math.PI;
            Span<double> angles = stackalloc double[3], other = stackalloc double[3];
            angles[i] = first * toDegrees;
            angles[j] = middle * toDegrees;
            angles[k] = last * toDegrees;
            // The other set: the first and last angles a half turn on, the middle one mirrored.
            other[i] = angles[i] + 180;
            other[j] = 180 - angles[j];
            other[k] = angles[k] + 180;
            var hint = m_LocalEulerAnglesHint;
            double distance = 0, otherDistance = 0;
            for (int axis = 0; axis < 3; axis++)
            {
                angles[axis] = Near(angles[axis], hint[axis]);
                other[axis] = Near(other[axis], hint[axis]);
                distance += (angles[axis] - hint[axis]) * (angles[axis] - hint[axis]);
                otherDistance += (other[axis] - hint[axis]) * (other[axis] - hint[axis]);
            }
            var nearest = otherDistance < distance ? other : angles;
            return new Vector3(Thousandths(nearest[0]), Thousandths(nearest[1]), Thousandths(nearest[2]));

            static double Near(double degrees, float hint) => degrees + 360 * Math.Round((hint - degrees) / 360);
            static float Thousandths(double degrees) => (float)(Math.Round(degrees * 1000) / 1000);
        }

        /// <summary>Keeps the Euler angles the Inspector shows: GetLocalEulerAngles answers the
        /// angles nearest them; the rotation and localEulerAngles stay (measured:
        /// tests/projects/runtimeapis transform.after_hint.*).</summary>
        internal void SetLocalEulerHint(Vector3 euler)
        {
            m_LocalEulerAnglesHint = euler;
            // The pose after the last change holds the hint too (prefab instances' taken pose).
            m_PoseAfterChange.EulerHint = euler;
        }

        // ------------------------------------------------------------------ world state

        public Vector3 position
        {
            get
            {
                MainThreadCheck.Check("get_position");
                SyncJobs();
                var point = GetLocalPosition();
                for (var ancestor = m_Parent; ancestor != null; ancestor = ancestor.m_Parent)
                    point = ancestor.TransformLocalPoint(point);
                return point;
            }
            set
            {
                MainThreadCheck.Check("set_position");
                if (!IsFinite(value)) { InvalidValue("position", value); return; }
                SyncJobs();
                SetLocalPosition(m_Parent == null ? value : m_Parent.InverseHierarchyPoint(value));
            }
        }

        public Quaternion rotation
        {
            get
            {
                MainThreadCheck.Check("get_rotation");
                EnsureWorld();
                return m_WorldRotation;
            }
            set
            {
                MainThreadCheck.Check("set_rotation");
                SyncJobs();
                localRotation = m_Parent == null ? value : m_Parent.InverseHierarchyRotation(value);
            }
        }

        public Vector3 eulerAngles
        {
            get => rotation.eulerAngles;
            set => rotation = Quaternion.Euler(value);
        }

        public Vector3 lossyScale
        {
            get
            {
                EnsureWorld();
                // The scale left after removing the world rotation from the world matrix.
                var inverseRotation = Matrix4x4.Rotate(Quaternion.Inverse(m_WorldRotation));
                var m = MultiplyRotationScale(inverseRotation, m_LocalToWorld);
                return new Vector3(m.m00, m.m11, m.m22);
            }
        }

        public Matrix4x4 localToWorldMatrix
        {
            get
            {
                MainThreadCheck.Check("get_localToWorldMatrix");
                EnsureWorld();
                return m_LocalToWorld;
            }
        }

        public Matrix4x4 worldToLocalMatrix
        {
            get
            {
                MainThreadCheck.Check("get_worldToLocalMatrix");
                SyncJobs();
                var inverse = InverseLocalMatrix();
                for (var ancestor = m_Parent; ancestor != null; ancestor = ancestor.m_Parent)
                    inverse = MultiplyAffine(inverse, ancestor.InverseLocalMatrix());
                return inverse;
            }
        }

        public Vector3 right
        {
            get => rotation * Vector3.right;
            set => rotation = Quaternion.FromToRotation(Vector3.right, value);
        }

        public Vector3 up
        {
            get => rotation * Vector3.up;
            set => rotation = Quaternion.FromToRotation(Vector3.up, value);
        }

        public Vector3 forward
        {
            get => rotation * Vector3.forward;
            set => rotation = Quaternion.LookRotation(value);
        }

        public bool hasChanged
        {
            get => m_HasChanged;
            set => m_HasChanged = value;
        }

        public int hierarchyCapacity { get; set; } = 1;

        public int hierarchyCount
        {
            get
            {
                int count = 1;
                foreach (var child in m_Children) count += child.hierarchyCount;
                return count;
            }
        }

        public void SetPositionAndRotation(Vector3 position, Quaternion rotation)
        {
            MainThreadCheck.Check("SetPositionAndRotation");
            this.rotation = rotation;
            this.position = position;
        }

        public void SetLocalPositionAndRotation(Vector3 localPosition, Quaternion localRotation)
        {
            MainThreadCheck.Check("SetLocalPositionAndRotation");
            m_LocalRotation = QuaternionMath.Normalize(localRotation);
            SetLocalPosition(localPosition);
        }

        public void GetPositionAndRotation(out Vector3 position, out Quaternion rotation)
        {
            MainThreadCheck.Check("GetPositionAndRotation");
            position = this.position;
            rotation = this.rotation;
        }

        public void GetLocalPositionAndRotation(out Vector3 localPosition, out Quaternion localRotation)
        {
            MainThreadCheck.Check("GetLocalPositionAndRotation");
            localPosition = this.localPosition;
            localRotation = m_LocalRotation;
        }

        // Rotation is part of the decomposed TRS. A reflected parent reverses the child's
        // rotation about the other two axes; composition and its inverse use the same signs.
        internal static Quaternion ReflectRotation(Quaternion rotation, Vector3 scale)
        {
            float x = scale.x < 0 ? -1 : 1, y = scale.y < 0 ? -1 : 1, z = scale.z < 0 ? -1 : 1;
            return new Quaternion(rotation.x * y * z, rotation.y * x * z, rotation.z * x * y, rotation.w);
        }

        internal void EnsureWorld()
        {
            SyncJobs();
            if (!m_WorldDirty) return;
            var matrix = Matrix4x4.TRS(GetLocalPosition(), m_LocalRotation, m_LocalScale);
            var rotation = m_LocalRotation;
            for (var ancestor = m_Parent; ancestor != null; ancestor = ancestor.m_Parent)
            {
                matrix = MultiplyAffine(Matrix4x4.TRS(ancestor.GetLocalPosition(), ancestor.m_LocalRotation, ancestor.m_LocalScale), matrix);
                rotation = MultiplyHierarchyRotation(ancestor.m_LocalRotation, ReflectRotation(rotation, ancestor.m_LocalScale));
            }
            m_LocalToWorld = matrix;
            m_WorldRotation = rotation;
            m_WorldDirty = false;
        }

        internal bool m_MotionDirty;

        /// <summary>The stamp of the last change of the local values (prefab instances' modifications
        /// take it at the next record point: <see cref="Unbound.Runtime.PrefabRecordTiming"/>).</summary>
        internal long m_ChangeStamp;

        /// <summary>The local values a prefab instance's modifications use.</summary>
        internal struct LocalPose
        {
            internal Vector3 Position, Scale, EulerHint;
            internal Quaternion Rotation;
        }

        /// <summary>The pose after the last change, and the one the last Editor update took.</summary>
        internal LocalPose m_PoseAfterChange, m_TakenPose;
        internal bool m_HasTakenPose;

        internal LocalPose CurrentPose() => new LocalPose
        {
            Position = m_LocalPosition, Rotation = m_LocalRotation, Scale = m_LocalScale, EulerHint = m_LocalEulerAnglesHint,
        };

        /// <summary>Sets the local values without any change notification (read back at once).</summary>
        internal void SetPoseQuietly(LocalPose pose)
        {
            m_LocalPosition = pose.Position;
            m_LocalRotation = pose.Rotation;
            m_LocalScale = pose.Scale;
            m_LocalEulerAnglesHint = pose.EulerHint;
        }

        /// <summary>This transform changed: it and every descendant must recompute their world state.</summary>
        internal void MarkChanged()
        {
            if (s_AnimatedPhysicsStamp != 0) m_AnimatedPhysicsStamp = s_AnimatedPhysicsStamp;
            if (Unbound.Runtime.PrefabRecordTiming.Quiet == 0)
            {
                // The pose the last update took: the one after the last change before it (none known
                // for the first change: the stored values stand for it).
                if (m_ChangeStamp == 0) m_HasTakenPose = false;
                else if (m_ChangeStamp < Unbound.Runtime.PrefabRecordTiming.TransformsTaken)
                {
                    m_TakenPose = m_PoseAfterChange;
                    m_HasTakenPose = true;
                }
                m_ChangeStamp = Unbound.Runtime.PrefabRecordTiming.Stamp;
            }
            m_PoseAfterChange = CurrentPose();
            m_HasChanged = true;
            m_Version++;
            m_WorldDirty = true;
            ObjectDispatch.TransformChanged(this);
            FrameBuilder.MarkMotionTransformDirty(this);
            for (int i = 0; i < m_Children.Count; i++)
                m_Children[i].MarkParentChanged();
            OnLocalChanged();
        }

        void MarkParentChanged()
        {
            m_HasChanged = true;
            m_Version++;
            m_WorldDirty = true;
            ObjectDispatch.TransformChanged(this);
            FrameBuilder.MarkMotionTransformDirty(this);
            for (int i = 0; i < m_Children.Count; i++)
                m_Children[i].MarkParentChanged();
            // Dimension listeners can read world matrices on this branch's descendants.
            OnParentChanged();
        }

        /// <summary>RectTransforms recompute their rect when the parent's changes.</summary>
        internal virtual void OnParentChanged() { }

        internal virtual void OnLocalChanged() { }

        /// <summary>Children or the parent changed (layout and canvas bookkeeping listen).</summary>
        internal virtual void OnHierarchyChanged() { }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_LocalRotation = body.Quaternion("m_LocalRotation");
            m_LocalPosition = body.Vector3("m_LocalPosition");
            m_LocalScale = body.Vector3("m_LocalScale", Vector3.one);
            m_ConstrainProportionsScale = body.Bool("m_ConstrainProportionsScale");
            m_LocalEulerAnglesHint = body.Vector3("m_LocalEulerAnglesHint");
        }

        internal override void WriteSerialized(UnityYamlWriter.Writer w)
        {
            // A RectTransform's document has no version line (tests/projects/scenesave).
            if (!(this is RectTransform)) w.Int("serializedVersion", 2);
            w.Quaternion("m_LocalRotation", m_LocalRotation);
            w.Vector3("m_LocalPosition", m_LocalPosition);
            w.Vector3("m_LocalScale", m_LocalScale);
            w.Bool("m_ConstrainProportionsScale", m_ConstrainProportionsScale);
            w.RefList("m_Children", SavedChildren());
            w.Ref("m_Father", m_Parent);
            w.Vector3("m_LocalEulerAnglesHint", m_LocalEulerAnglesHint);
        }

        /// <summary>The children a saved file lists: not those of GameObjects that are not saved
        /// (HideFlags.DontSaveInEditor).</summary>
        IEnumerable<Transform> SavedChildren()
        {
            foreach (var child in m_Children)
                if ((child.gameObject.HideFlagsNoThrow & HideFlags.DontSaveInEditor) == 0) yield return child;
        }

        // ------------------------------------------------------------------ space conversion

        public Vector3 TransformPoint(Vector3 position)
        {
            MainThreadCheck.Check("TransformPoint");
            SyncJobs();
            for (var ancestor = this; ancestor != null; ancestor = ancestor.m_Parent)
                position = ancestor.TransformLocalPoint(position);
            return position;
        }

        public Vector3 TransformPoint(float x, float y, float z) => TransformPoint(new Vector3(x, y, z));

        public Vector3 InverseTransformPoint(Vector3 position)
        {
            MainThreadCheck.Check("InverseTransformPoint");
            SyncJobs();
            return InverseHierarchyPoint(position);
        }

        public Vector3 InverseTransformPoint(float x, float y, float z) => InverseTransformPoint(new Vector3(x, y, z));

        public Vector3 TransformDirection(Vector3 direction) => rotation * direction;

        public Vector3 TransformDirection(float x, float y, float z) => TransformDirection(new Vector3(x, y, z));

        public Vector3 InverseTransformDirection(Vector3 direction) => Quaternion.Inverse(rotation) * direction;

        public Vector3 InverseTransformDirection(float x, float y, float z) => InverseTransformDirection(new Vector3(x, y, z));

        public Vector3 TransformVector(Vector3 vector) => localToWorldMatrix.MultiplyVector(vector);

        public Vector3 TransformVector(float x, float y, float z) => TransformVector(new Vector3(x, y, z));

        public Vector3 InverseTransformVector(Vector3 vector) => worldToLocalMatrix.MultiplyVector(vector);

        public Vector3 InverseTransformVector(float x, float y, float z) => InverseTransformVector(new Vector3(x, y, z));

        public void TransformPoints(Span<Vector3> positions)
        {
            var m = localToWorldMatrix;
            for (int i = 0; i < positions.Length; i++) positions[i] = m.MultiplyPoint3x4(positions[i]);
        }

        public void TransformPoints(ReadOnlySpan<Vector3> positions, Span<Vector3> transformedPositions)
        {
            var m = localToWorldMatrix;
            for (int i = 0; i < positions.Length; i++) transformedPositions[i] = m.MultiplyPoint3x4(positions[i]);
        }

        public void InverseTransformPoints(Span<Vector3> positions)
        {
            var m = worldToLocalMatrix;
            for (int i = 0; i < positions.Length; i++) positions[i] = m.MultiplyPoint3x4(positions[i]);
        }

        public void InverseTransformPoints(ReadOnlySpan<Vector3> positions, Span<Vector3> transformedPositions)
        {
            var m = worldToLocalMatrix;
            for (int i = 0; i < positions.Length; i++) transformedPositions[i] = m.MultiplyPoint3x4(positions[i]);
        }

        public void TransformDirections(Span<Vector3> directions)
        {
            var r = rotation;
            for (int i = 0; i < directions.Length; i++) directions[i] = r * directions[i];
        }

        public void TransformDirections(ReadOnlySpan<Vector3> directions, Span<Vector3> transformedDirections)
        {
            var r = rotation;
            for (int i = 0; i < directions.Length; i++) transformedDirections[i] = r * directions[i];
        }

        public void InverseTransformDirections(Span<Vector3> directions)
        {
            var r = Quaternion.Inverse(rotation);
            for (int i = 0; i < directions.Length; i++) directions[i] = r * directions[i];
        }

        public void InverseTransformDirections(ReadOnlySpan<Vector3> directions, Span<Vector3> transformedDirections)
        {
            var r = Quaternion.Inverse(rotation);
            for (int i = 0; i < directions.Length; i++) transformedDirections[i] = r * directions[i];
        }

        public void TransformVectors(Span<Vector3> vectors)
        {
            var m = localToWorldMatrix;
            for (int i = 0; i < vectors.Length; i++) vectors[i] = m.MultiplyVector(vectors[i]);
        }

        public void TransformVectors(ReadOnlySpan<Vector3> vectors, Span<Vector3> transformedVectors)
        {
            var m = localToWorldMatrix;
            for (int i = 0; i < vectors.Length; i++) transformedVectors[i] = m.MultiplyVector(vectors[i]);
        }

        public void InverseTransformVectors(Span<Vector3> vectors)
        {
            var m = worldToLocalMatrix;
            for (int i = 0; i < vectors.Length; i++) vectors[i] = m.MultiplyVector(vectors[i]);
        }

        public void InverseTransformVectors(ReadOnlySpan<Vector3> vectors, Span<Vector3> transformedVectors)
        {
            var m = worldToLocalMatrix;
            for (int i = 0; i < vectors.Length; i++) transformedVectors[i] = m.MultiplyVector(vectors[i]);
        }

        // ------------------------------------------------------------------ movement

        public void Translate(Vector3 translation, [UnityEngine.Internal.DefaultValue("Space.Self")] Space relativeTo)
        {
            if (relativeTo == Space.World) position += translation;
            else position += TransformDirection(translation);
        }

        public void Translate(Vector3 translation) => Translate(translation, Space.Self);

        public void Translate(float x, float y, float z, [UnityEngine.Internal.DefaultValue("Space.Self")] Space relativeTo) =>
            Translate(new Vector3(x, y, z), relativeTo);

        public void Translate(float x, float y, float z) => Translate(new Vector3(x, y, z), Space.Self);

        public void Translate(Vector3 translation, Transform relativeTo)
        {
            if (relativeTo) position += relativeTo.TransformDirection(translation);
            else position += translation;
        }

        public void Translate(float x, float y, float z, Transform relativeTo) => Translate(new Vector3(x, y, z), relativeTo);

        public void Rotate(Vector3 eulers, [UnityEngine.Internal.DefaultValue("Space.Self")] Space relativeTo)
        {
            var eulerRotation = Quaternion.Euler(eulers.x, eulers.y, eulers.z);
            if (relativeTo == Space.Self)
                localRotation = localRotation * eulerRotation;
            else
                rotation = rotation * (Quaternion.Inverse(rotation) * eulerRotation * rotation);
        }

        public void Rotate(Vector3 eulers) => Rotate(eulers, Space.Self);

        public void Rotate(float xAngle, float yAngle, float zAngle, [UnityEngine.Internal.DefaultValue("Space.Self")] Space relativeTo) =>
            Rotate(new Vector3(xAngle, yAngle, zAngle), relativeTo);

        public void Rotate(float xAngle, float yAngle, float zAngle) => Rotate(new Vector3(xAngle, yAngle, zAngle), Space.Self);

        public void Rotate(Vector3 axis, float angle, [UnityEngine.Internal.DefaultValue("Space.Self")] Space relativeTo)
        {
            if (relativeTo == Space.Self)
                RotateAroundInternal(TransformDirection(axis), angle * Mathf.Deg2Rad);
            else
                RotateAroundInternal(axis, angle * Mathf.Deg2Rad);
        }

        public void Rotate(Vector3 axis, float angle) => Rotate(axis, angle, Space.Self);

        internal void RotateAroundInternal(Vector3 worldAxis, float radians)
        {
            var q = QuaternionMath.AngleAxis(radians * Mathf.Rad2Deg, worldAxis);
            rotation = q * rotation;
        }

        public void RotateAround(Vector3 point, Vector3 axis, float angle)
        {
            // C# in Unity too: the position turns through the scripting Quaternion.AngleAxis; only the
            // rotation goes through the native turn below.
            var worldPos = position;
            var q = Quaternion.AngleAxis(angle, axis);
            var dif = worldPos - point;
            dif = q * dif;
            worldPos = point + dif;
            position = worldPos;
            RotateAroundInternal(axis, angle * Mathf.Deg2Rad);
        }

        [Obsolete("warning use Transform.Rotate instead.")]
        public void RotateAround(Vector3 axis, float angle) => RotateAroundInternal(axis, angle * Mathf.Deg2Rad);

        [Obsolete("warning use Transform.Rotate instead.")]
        public void RotateAroundLocal(Vector3 axis, float angle) => RotateAroundInternal(TransformDirection(axis), angle * Mathf.Deg2Rad);

        public void LookAt(Transform target, [UnityEngine.Internal.DefaultValue("Vector3.up")] Vector3 worldUp)
        {
            if (target) LookAt(target.position, worldUp);
        }

        public void LookAt(Transform target)
        {
            if (target) LookAt(target.position, Vector3.up);
        }

        public void LookAt(Vector3 worldPosition, [UnityEngine.Internal.DefaultValue("Vector3.up")] Vector3 worldUp)
        {
            var forward = worldPosition - position;
            if (forward.sqrMagnitude < 1e-12f) return;
            rotation = Quaternion.LookRotationSilent(forward, worldUp);
        }

        public void LookAt(Vector3 worldPosition) => LookAt(worldPosition, Vector3.up);

        // ------------------------------------------------------------------ hierarchy

        public Transform parent
        {
            get
            {
                MainThreadCheck.Check("GetParent");
                return m_Parent;
            }
            set
            {
                if (this is RectTransform)
                    Debug.LogWarning("Parent of RectTransform is being set with parent property. Consider using the SetParent method instead, with the worldPositionStays argument set to false. This will retain local orientation and scale rather than world orientation and scale, which can prevent common UI scaling issues.", this);
                SetParent(value, true);
            }
        }

        internal Transform parentInternal
        {
            get => m_Parent;
            set => SetParent(value, true);
        }

        public Transform root
        {
            get
            {
                var t = this;
                while (t.m_Parent != null) t = t.m_Parent;
                return t;
            }
        }

        public int childCount
        {
            get
            {
                MainThreadCheck.Check("get_childCount");
                return m_Children.Count;
            }
        }

        public Transform GetChild(int index)
        {
            MainThreadCheck.Check("GetChild");
            if (index < 0 || index >= m_Children.Count)
                throw new UnityException("Transform child out of bounds");
            return m_Children[index];
        }

        [Obsolete("warning use Transform.childCount instead (UnityUpgradable) -> Transform.childCount", false)]
        public int GetChildCount() => m_Children.Count;

        public void SetParent(Transform p) => SetParent(p, true);

        public void SetParent(Transform parent, bool worldPositionStays)
        {
            MainThreadCheck.Check("SetParent");
            ThrowIfDestroyed();
            // Itself as its parent: nothing changes and nothing is logged (tests/projects/objecterrors).
            if (parent == this) return;
            if ((object)parent != null && parent.m_Destroyed) parent = null;
            if (parent == m_Parent) return;
            if (parent != null && parent.IsChildOf(this))
            {
                Debug.LogError("Cannot make a GameObject a child of itself or of one of its descendants.", this);
                return;
            }
            if (Hierarchy.RefusesAssetParenting(this, parent)) return;
            Hierarchy.SetParent(this, parent, worldPositionStays, -1);
        }

        /// <summary>Re-parents without the checks and messages of SetParent (loaders and clones).</summary>
        internal void SetParentRaw(Transform parent)
        {
            m_Parent?.m_Children.Remove(this);
            m_Parent = parent;
            parent?.m_Children.Add(this);
            MarkParentChanged();
        }

        /// <summary>Computes and applies local values that keep the world pose under a new parent.</summary>
        internal virtual void KeepWorldPose(Vector3 worldPosition, Quaternion worldRotation, Vector3 worldScale, Transform newParent)
        {
            Vector3 localPosition;
            if (newParent == null)
            {
                m_LocalRotation = NormalizeParentingRotation(worldRotation);
                m_LocalScale = worldScale;
                SetLocalPosition(worldPosition);
                return;
            }
            else
            {
                m_LocalRotation = NormalizeParentingRotation(newParent.InverseHierarchyRotation(worldRotation));
                localPosition = newParent.InverseHierarchyPoint(worldPosition);
            }
            // Measure the scale contributed by the new hierarchy with a unit local
            // scale, in the original world rotation's frame. Public parenting
            // controls: tests/projects/solverpairorder/Sources/TransformInputs.
            var rotationMatrix = Matrix4x4.Rotate(m_LocalRotation);
            rotationMatrix = MultiplyRotationScale(newParent.localToWorldMatrix, rotationMatrix);
            var inheritedScale = MultiplyRotationScale(Matrix4x4.Rotate(Quaternion.Inverse(worldRotation)), rotationMatrix);
            m_LocalScale = new Vector3(
                worldScale.x * ReciprocalScale(inheritedScale.m00),
                worldScale.y * ReciprocalScale(inheritedScale.m11),
                worldScale.z * ReciprocalScale(inheritedScale.m22));
            SetLocalPosition(localPosition);
        }

        // Keep the measured accumulation order when removing a rotation from scale.
        // TransformInputs covers every component with tilted and nested controls.
        static Matrix4x4 MultiplyRotationScale(in Matrix4x4 a, in Matrix4x4 b)
        {
            return new Matrix4x4
            {
                m00 = (a.m01 * b.m10 + a.m02 * b.m20) + a.m00 * b.m00,
                m01 = (a.m01 * b.m11 + a.m02 * b.m21) + a.m00 * b.m01,
                m02 = (a.m01 * b.m12 + a.m02 * b.m22) + a.m00 * b.m02,
                m03 = 0f,
                m10 = (a.m11 * b.m10 + a.m12 * b.m20) + a.m10 * b.m00,
                m11 = (a.m11 * b.m11 + a.m12 * b.m21) + a.m10 * b.m01,
                m12 = (a.m11 * b.m12 + a.m12 * b.m22) + a.m10 * b.m02,
                m13 = 0f,
                m20 = (a.m21 * b.m10 + a.m22 * b.m20) + a.m20 * b.m00,
                m21 = (a.m21 * b.m11 + a.m22 * b.m21) + a.m20 * b.m01,
                m22 = (a.m21 * b.m12 + a.m22 * b.m22) + a.m20 * b.m02,
                m23 = 0f,
                m33 = 1f,
            };
        }

        static Matrix4x4 MultiplyAffine(in Matrix4x4 a, in Matrix4x4 b)
        {
            return new Matrix4x4
            {
                m00 = (a.m01 * b.m10 + a.m02 * b.m20) + a.m00 * b.m00,
                m01 = (a.m01 * b.m11 + a.m02 * b.m21) + a.m00 * b.m01,
                m02 = (a.m01 * b.m12 + a.m02 * b.m22) + a.m00 * b.m02,
                m03 = ((a.m01 * b.m13 + a.m02 * b.m23) + a.m00 * b.m03) + a.m03,
                m10 = (a.m11 * b.m10 + a.m12 * b.m20) + a.m10 * b.m00,
                m11 = (a.m11 * b.m11 + a.m12 * b.m21) + a.m10 * b.m01,
                m12 = (a.m11 * b.m12 + a.m12 * b.m22) + a.m10 * b.m02,
                m13 = ((a.m11 * b.m13 + a.m12 * b.m23) + a.m10 * b.m03) + a.m13,
                m20 = (a.m21 * b.m10 + a.m22 * b.m20) + a.m20 * b.m00,
                m21 = (a.m21 * b.m11 + a.m22 * b.m21) + a.m20 * b.m01,
                m22 = (a.m21 * b.m12 + a.m22 * b.m22) + a.m20 * b.m02,
                m23 = ((a.m21 * b.m13 + a.m22 * b.m23) + a.m20 * b.m03) + a.m23,
                m33 = 1f,
            };
        }

        static float ReciprocalScale(float scale)
        {
            if (MathF.Abs(scale) < 1e-9f) return 0f;
            float estimate = float.ReciprocalEstimate(scale);
            float first = estimate * (2.0000005f - scale * estimate);
            float refined = first * (2f - scale * first);
            return float.IsNaN(refined) ? estimate : refined;
        }

        Matrix4x4 InverseLocalMatrix()
        {
            var inverse = Matrix4x4.Rotate(Quaternion.Inverse(m_LocalRotation));
            var position = GetLocalPosition();
            for (int row = 0; row < 3; row++)
            {
                float scale = ReciprocalScale(m_LocalScale[row]);
                for (int column = 0; column < 3; column++) inverse[row, column] *= scale;
                inverse[row, 3] = -((inverse[row, 1] * position.y + inverse[row, 2] * position.z) + inverse[row, 0] * position.x);
            }
            return inverse;
        }

        // Position composition retains the original vector separately from the
        // rotation's diagonal correction. Its rounding differs from a matrix product.
        internal static Vector3 RotateHierarchyPoint(Quaternion q, Vector3 point)
        {
            var delta = Matrix4x4.Rotate(q);
            delta.m00 = -(q.y * (q.y * 2f) + q.z * (q.z * 2f));
            delta.m11 = -(q.x * (q.x * 2f) + q.z * (q.z * 2f));
            delta.m22 = -(q.x * (q.x * 2f) + q.y * (q.y * 2f));
            return new Vector3(
                (delta.m02 * point.z + delta.m01 * point.y) + (delta.m00 * point.x + point.x),
                (delta.m12 * point.z + delta.m11 * point.y) + (delta.m10 * point.x + point.y),
                (delta.m22 * point.z + delta.m21 * point.y) + (delta.m20 * point.x + point.z));
        }

        Vector3 TransformLocalPoint(Vector3 point) =>
            RotateHierarchyPoint(m_LocalRotation, Vector3.Scale(point, m_LocalScale)) + GetLocalPosition();

        Vector3 InverseHierarchyPoint(Vector3 point)
        {
            if (m_Parent != null) point = m_Parent.InverseHierarchyPoint(point);
            point = RotateHierarchyPoint(Quaternion.Inverse(m_LocalRotation), point - GetLocalPosition());
            return new Vector3(point.x * ReciprocalScale(m_LocalScale.x), point.y * ReciprocalScale(m_LocalScale.y), point.z * ReciprocalScale(m_LocalScale.z));
        }

        Quaternion InverseHierarchyRotation(Quaternion rotation)
        {
            if (m_Parent != null) rotation = m_Parent.InverseHierarchyRotation(rotation);
            return ReflectRotation(MultiplyHierarchyRotation(Quaternion.Inverse(m_LocalRotation), rotation), m_LocalScale);
        }

        static Quaternion MultiplyHierarchyRotation(Quaternion a, Quaternion b) => new Quaternion(
            (a.w * b.x + (a.y * b.z - a.z * b.y)) + a.x * b.w,
            (a.w * b.y + (a.z * b.x - a.x * b.z)) + a.y * b.w,
            ((a.w * b.z - a.y * b.x) + a.z * b.w) + a.x * b.y,
            ((a.w * b.w - a.x * b.x) - a.z * b.z) - a.y * b.y);

        // A set rotation divided by its length. The Linux x86_64 Editor sums the squares as a horizontal add
        // does, (x² + y²) + (z² + w²) for x and z and (y² + z²) + (w² + x²) for y and w; every host takes
        // that order (the macOS arm64 Editor's own one is not reproduced).
        static Quaternion NormalizeParentingRotation(Quaternion q)
        {
            float x = q.x * q.x, y = q.y * q.y, z = q.z * q.z, w = q.w * q.w;
            float xz = (x + y) + (z + w), yw = (y + z) + (w + x);
            float sxz = MathF.Sqrt(xz), syw = MathF.Sqrt(yw);
            return new Quaternion(xz > 1e-30f ? q.x / sxz : 0f, yw > 1e-30f ? q.y / syw : 0f,
                xz > 1e-30f ? q.z / sxz : 0f, yw > 1e-30f ? q.w / syw : 1f);
        }

        public void DetachChildren()
        {
            // A prefab asset keeps its children: one error naming the first (Unity 6000.0.71f1,
            // tests/projects/gameinterop hier.detach_children).
            if (m_Children.Count > 0 && Hierarchy.IsLoadedPrefabAsset(this))
            {
                Debug.LogError($"{Hierarchy.AssetParentingRefused} (GameObject: '{m_Children[0].m_GameObject.m_Name}').", this);
                return;
            }
            for (int i = m_Children.Count - 1; i >= 0; i--)
                m_Children[i].SetParent(null, true);
        }

        public void SetAsFirstSibling() => SetSiblingIndex(0);

        public void SetAsLastSibling() => SetSiblingIndex(int.MaxValue);

        public void SetSiblingIndex(int index) => Hierarchy.SetSiblingIndex(this, index);

        public int GetSiblingIndex() => Hierarchy.GetSiblingIndex(this);

        public Transform Find(string n)
        {
            if (n == null) throw new ArgumentNullException("Name cannot be null");
            MainThreadCheck.Check("FindRelativeTransformWithPath");
            var current = this;
            foreach (var part in n.Split('/'))
            {
                if (part.Length == 0) continue;
                Transform next = null;
                foreach (var child in current.m_Children)
                {
                    if (child.m_GameObject.m_Name == part)
                    {
                        next = child;
                        break;
                    }
                }
                if (next == null) return null;
                current = next;
            }
            return current;
        }

        [Obsolete("FindChild has been deprecated. Use Find instead (UnityUpgradable) -> Find([mscorlib] System.String)", false)]
        public Transform FindChild(string n) => Find(n);

        public bool IsChildOf(Transform parent)
        {
            for (var t = this; t != null; t = t.m_Parent)
                if (t == parent) return true;
            return false;
        }

        public IEnumerator GetEnumerator() => new Enumerator(this);

        sealed class Enumerator : IEnumerator
        {
            readonly Transform outer;
            int currentIndex = -1;

            internal Enumerator(Transform outer) { this.outer = outer; }

            public object Current => outer.GetChild(currentIndex);

            public bool MoveNext()
            {
                int childCount = outer.childCount;
                return ++currentIndex < childCount;
            }

            public void Reset() { currentIndex = -1; }
        }

        static bool IsFinite(Vector3 v) =>
            !(float.IsNaN(v.x) || float.IsNaN(v.y) || float.IsNaN(v.z) ||
              float.IsInfinity(v.x) || float.IsInfinity(v.y) || float.IsInfinity(v.z));

        void InvalidValue(string property, Vector3 value) =>
            Debug.LogError($"transform.{property} assign attempt for '{name}' is not valid. Input {property} is {{ {value.x}, {value.y}, {value.z} }}.", this);
    }

    internal enum RotationOrder
    {
        OrderXYZ = 0,
        OrderXZY = 1,
        OrderYZX = 2,
        OrderYXZ = 3,
        OrderZXY = 4,
        OrderZYX = 5,
    }
}
