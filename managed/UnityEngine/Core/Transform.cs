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

        Matrix4x4 m_LocalToWorld = Matrix4x4.identity;
        Quaternion m_WorldRotation = Quaternion.identity;
        bool m_WorldDirty = true;
        bool m_HasChanged = true;
        /// <summary>Bumped on every change of this transform or a parent: lets consumers see changes.</summary>
        internal int m_Version;

        protected Transform()
        {
        }

        // ------------------------------------------------------------------ local state

        public Vector3 localPosition
        {
            get => GetLocalPosition();
            set
            {
                if (!IsFinite(value)) { InvalidValue("localPosition", value); return; }
                SetLocalPosition(value);
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
            get => m_LocalRotation;
            set
            {
                m_LocalRotation = value.normalized;
                MarkChanged();
            }
        }

        public Vector3 localScale
        {
            get => m_LocalScale;
            set
            {
                if (!IsFinite(value)) { InvalidValue("localScale", value); return; }
                m_LocalScale = value;
                MarkChanged();
            }
        }

        public Vector3 localEulerAngles
        {
            get => m_LocalRotation.eulerAngles;
            set => localRotation = Quaternion.Euler(value);
        }

        // ------------------------------------------------------------------ world state

        public Vector3 position
        {
            get
            {
                if (m_Parent == null) return GetLocalPosition();
                return m_Parent.localToWorldMatrix.MultiplyPoint3x4(GetLocalPosition());
            }
            set
            {
                if (!IsFinite(value)) { InvalidValue("position", value); return; }
                SetLocalPosition(m_Parent == null ? value : m_Parent.worldToLocalMatrix.MultiplyPoint3x4(value));
            }
        }

        public Quaternion rotation
        {
            get
            {
                EnsureWorld();
                return m_WorldRotation;
            }
            set
            {
                localRotation = m_Parent == null ? value : Quaternion.Inverse(m_Parent.rotation) * value;
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
                var m = inverseRotation * m_LocalToWorld;
                return new Vector3(m.m00, m.m11, m.m22);
            }
        }

        public Matrix4x4 localToWorldMatrix
        {
            get
            {
                EnsureWorld();
                return m_LocalToWorld;
            }
        }

        public Matrix4x4 worldToLocalMatrix => localToWorldMatrix.inverse;

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
            this.rotation = rotation;
            this.position = position;
        }

        public void SetLocalPositionAndRotation(Vector3 localPosition, Quaternion localRotation)
        {
            m_LocalRotation = localRotation.normalized;
            SetLocalPosition(localPosition);
        }

        public void GetPositionAndRotation(out Vector3 position, out Quaternion rotation)
        {
            position = this.position;
            rotation = this.rotation;
        }

        public void GetLocalPositionAndRotation(out Vector3 localPosition, out Quaternion localRotation)
        {
            localPosition = this.localPosition;
            localRotation = m_LocalRotation;
        }

        internal void EnsureWorld()
        {
            if (!m_WorldDirty) return;
            var local = Matrix4x4.TRS(GetLocalPosition(), m_LocalRotation, m_LocalScale);
            if (m_Parent == null)
            {
                m_LocalToWorld = local;
                m_WorldRotation = m_LocalRotation;
            }
            else
            {
                m_LocalToWorld = m_Parent.localToWorldMatrix * local;
                m_WorldRotation = m_Parent.rotation * m_LocalRotation;
            }
            m_WorldDirty = false;
        }

        /// <summary>This transform changed: it and every descendant must recompute their world state.</summary>
        internal void MarkChanged()
        {
            m_HasChanged = true;
            m_Version++;
            m_WorldDirty = true;
            for (int i = 0; i < m_Children.Count; i++)
                m_Children[i].MarkParentChanged();
            OnLocalChanged();
        }

        void MarkParentChanged()
        {
            m_HasChanged = true;
            m_Version++;
            m_WorldDirty = true;
            OnParentChanged();
            for (int i = 0; i < m_Children.Count; i++)
                m_Children[i].MarkParentChanged();
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
        }

        // ------------------------------------------------------------------ space conversion

        public Vector3 TransformPoint(Vector3 position) => localToWorldMatrix.MultiplyPoint3x4(position);

        public Vector3 TransformPoint(float x, float y, float z) => TransformPoint(new Vector3(x, y, z));

        public Vector3 InverseTransformPoint(Vector3 position) => worldToLocalMatrix.MultiplyPoint3x4(position);

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

        void RotateAroundInternal(Vector3 worldAxis, float radians)
        {
            var q = Quaternion.AngleAxis(radians * Mathf.Rad2Deg, worldAxis);
            rotation = q * rotation;
        }

        public void RotateAround(Vector3 point, Vector3 axis, float angle)
        {
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
            rotation = Quaternion.LookRotation(forward, worldUp);
        }

        public void LookAt(Vector3 worldPosition) => LookAt(worldPosition, Vector3.up);

        // ------------------------------------------------------------------ hierarchy

        public Transform parent
        {
            get => m_Parent;
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

        public int childCount => m_Children.Count;

        public Transform GetChild(int index)
        {
            if (index < 0 || index >= m_Children.Count)
                throw new UnityException("Transform child out of bounds");
            return m_Children[index];
        }

        [Obsolete("warning use Transform.childCount instead (UnityUpgradable) -> Transform.childCount", false)]
        public int GetChildCount() => m_Children.Count;

        public void SetParent(Transform p) => SetParent(p, true);

        public void SetParent(Transform parent, bool worldPositionStays)
        {
            ThrowIfDestroyed();
            if (parent == this)
            {
                Debug.LogError("Cannot set the parent of the GameObject to itself.", this);
                return;
            }
            if ((object)parent != null && parent.m_Destroyed) parent = null;
            if (parent == m_Parent) return;
            if (parent != null && parent.IsChildOf(this))
            {
                Debug.LogError("Cannot make a GameObject a child of itself or of one of its descendants.", this);
                return;
            }
            if (m_GameObject.m_IsPrefabAsset != (parent != null && parent.m_GameObject.m_IsPrefabAsset) && parent != null)
            {
                Debug.LogError("Setting the parent of a transform which resides in a Prefab Asset is disabled to prevent data corruption", this);
                return;
            }
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
        internal void KeepWorldPose(Vector3 worldPosition, Quaternion worldRotation, Vector3 worldScale, Transform newParent)
        {
            if (newParent == null)
            {
                m_LocalRotation = worldRotation;
                m_LocalScale = worldScale;
                SetLocalPosition(worldPosition);
                return;
            }
            m_LocalRotation = (Quaternion.Inverse(newParent.rotation) * worldRotation).normalized;
            var parentScale = newParent.lossyScale;
            m_LocalScale = new Vector3(
                parentScale.x != 0 ? worldScale.x / parentScale.x : 0,
                parentScale.y != 0 ? worldScale.y / parentScale.y : 0,
                parentScale.z != 0 ? worldScale.z / parentScale.z : 0);
            SetLocalPosition(newParent.worldToLocalMatrix.MultiplyPoint3x4(worldPosition));
        }

        public void DetachChildren()
        {
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

}
