using System;
using System.Collections.Generic;

namespace UnityEngine
{
    /// <summary>A sphere's change of state in a <see cref="CullingGroup"/>: a state byte holds the
    /// visibility in its top bit and the distance band in the other seven.</summary>
    public partial struct CullingGroupEvent
    {
        int m_Index;
        byte m_PrevState;
        byte m_ThisState;

        const byte kIsVisibleMask = 1 << 7;
        const byte kDistanceMask = (1 << 7) - 1;

        internal CullingGroupEvent(int index, byte previous, byte current)
        {
            m_Index = index;
            m_PrevState = previous;
            m_ThisState = current;
        }

        public int index => m_Index;
        public bool isVisible => (m_ThisState & kIsVisibleMask) != 0;
        public bool wasVisible => (m_PrevState & kIsVisibleMask) != 0;
        public bool hasBecomeVisible => isVisible && !wasVisible;
        public bool hasBecomeInvisible => !isVisible && wasVisible;
        public int currentDistance => m_ThisState & kDistanceMask;
        public int previousDistance => m_PrevState & kDistanceMask;
    }

    /// <summary>
    /// Bounding spheres tested against a target camera, as the Editor does (measured:
    /// tests/projects/cullinggroup). A group is evaluated in its target camera's culling, after
    /// <c>OnPreCull</c> and <c>Camera.onPreCull</c> and before renderers become visible; then the
    /// callbacks go out, group by group in creation order (a disposed group's place takes the last
    /// group), sphere by sphere. A sphere's state is its visibility and its distance band; a callback
    /// is sent when the state differs from that of its last callback (initially invisible, band 0).
    /// Visible: the sphere touches the camera's six frustum planes (no occlusion) and is not in the
    /// last band (beyond the last distance). Band: the number of distances below the distance from
    /// the reference point (the origin unless set) to the sphere's surface. A disabled group is not
    /// evaluated; one with no target camera only gets its distances updated in cameras' culling,
    /// without visibility or callbacks.
    /// </summary>
    public partial class CullingGroup : IDisposable
    {
        /// <summary>The groups not disposed, in creation order (Dispose moves the last into the slot).</summary>
        static readonly List<CullingGroup> s_Groups = new List<CullingGroup>();
        static readonly List<CullingGroupEvent> s_Events = new List<CullingGroupEvent>();
        static readonly Plane[] s_Planes = new Plane[6];

        const int kMaxDistances = 7;
        const byte kVisible = 1 << 7;
        const byte kDistanceMask = (1 << 7) - 1;

        bool m_Disposed;
        bool m_Enabled = true;
        Camera m_TargetCamera;
        BoundingSphere[] m_Spheres;
        int m_Count;
        /// <summary>The state IsVisible and GetDistance report, and that of each sphere's last callback.</summary>
        byte[] m_States = Array.Empty<byte>(), m_SentStates = Array.Empty<byte>();
        float[] m_Distances = Array.Empty<float>();
        Vector3 m_ReferencePoint;
        Transform m_ReferenceTransform;
        StateChanged m_OnStateChanged;

        public delegate void StateChanged(CullingGroupEvent sphere);

        public CullingGroup()
        {
            s_Groups.Add(this);
        }

        void Check()
        {
            if (m_Disposed) throw new NullReferenceException();
        }

        public void Dispose()
        {
            Check();
            m_Disposed = true;
            int slot = s_Groups.IndexOf(this);
            int last = s_Groups.Count - 1;
            s_Groups[slot] = s_Groups[last];
            s_Groups.RemoveAt(last);
            m_Spheres = null;
        }

        public StateChanged onStateChanged
        {
            get => m_OnStateChanged;
            set => m_OnStateChanged = value;
        }

        public bool enabled
        {
            get { Check(); return m_Enabled; }
            set { Check(); m_Enabled = value; }
        }

        public Camera targetCamera
        {
            get { Check(); return m_TargetCamera; }
            set { Check(); m_TargetCamera = value; }
        }

        /// <summary>The group reads the array itself (later changes to it count) and keeps the
        /// spheres' states; the count becomes the array's length.</summary>
        public void SetBoundingSpheres(BoundingSphere[] array)
        {
            Check();
            int length = array?.Length ?? 0;
            var states = new byte[length];
            var sent = new byte[length];
            int kept = Math.Min(m_Count, length);
            Array.Copy(m_States, states, kept);
            Array.Copy(m_SentStates, sent, kept);
            m_Spheres = array;
            m_States = states;
            m_SentStates = sent;
            m_Count = length;
        }

        /// <summary>Spheres coming (back) into the count start invisible in band 0; a count outside the
        /// array is ignored with a warning.</summary>
        public void SetBoundingSphereCount(int count)
        {
            Check();
            if (count < 0 || count > (m_Spheres?.Length ?? 0))
            {
                Debug.LogWarning("Bounding sphere count must be less or equal to the bounding sphere array.");
                return;
            }
            for (int i = m_Count; i < count; i++) m_States[i] = m_SentStates[i] = 0;
            m_Count = count;
        }

        /// <summary>Moves the last sphere (in the group's array too) and its state into the index.</summary>
        public void EraseSwapBack(int index)
        {
            Check();
            if (index < 0 || index >= m_Count)
            {
                Debug.LogError("CullingGroup index is out of bounds");
                return;
            }
            int last = m_Count - 1;
            m_Spheres[index] = m_Spheres[last];
            m_States[index] = m_States[last];
            m_SentStates[index] = m_SentStates[last];
            m_Count = last;
        }

        public static void EraseSwapBack<T>(int index, T[] myArray, ref int size)
        {
            size--;
            myArray[index] = myArray[size];
        }

        public int QueryIndices(bool visible, int[] result, int firstIndex) =>
            QueryIndices(visible, -1, CullingQueryOptions.IgnoreDistance, result, firstIndex);

        public int QueryIndices(int distanceIndex, int[] result, int firstIndex) =>
            QueryIndices(false, distanceIndex, CullingQueryOptions.IgnoreVisibility, result, firstIndex);

        public int QueryIndices(bool visible, int distanceIndex, int[] result, int firstIndex) =>
            QueryIndices(visible, distanceIndex, CullingQueryOptions.Normal, result, firstIndex);

        /// <summary>The matching indices from <paramref name="firstIndex"/> on, up to the result's
        /// length; a null or empty result only counts them all.</summary>
        int QueryIndices(bool visible, int distanceIndex, CullingQueryOptions options, int[] result, int firstIndex)
        {
            Check();
            if (firstIndex < 0) throw new IndexOutOfRangeException("firstIndex must not be negative.");
            int capacity = result?.Length ?? 0;
            int found = 0;
            for (int i = firstIndex; i < m_Count; i++)
            {
                byte state = m_States[i];
                if (options != CullingQueryOptions.IgnoreVisibility && ((state & kVisible) != 0) != visible) continue;
                if (options != CullingQueryOptions.IgnoreDistance && (state & kDistanceMask) != distanceIndex) continue;
                if (capacity > 0) result[found] = i;
                if (++found == capacity) break;
            }
            return found;
        }

        public bool IsVisible(int index)
        {
            Check();
            if (index < 0 || index >= m_Count) throw new IndexOutOfRangeException("Sphere index is out of range.");
            return (m_States[index] & kVisible) != 0;
        }

        public int GetDistance(int index)
        {
            Check();
            if (index < 0 || index >= m_Count) throw new IndexOutOfRangeException("Sphere index is out of range.");
            return m_States[index] & kDistanceMask;
        }

        /// <summary>Ascending distances bounding the bands. More than seven log an error and are kept;
        /// unsorted ones log an error and are ignored.</summary>
        public void SetBoundingDistances(float[] distances)
        {
            Check();
            distances ??= Array.Empty<float>();
            if (distances.Length > kMaxDistances)
                Debug.LogError("CullingGroup only supports a maximum of 7 bounding distances.");
            for (int i = 1; i < distances.Length; i++)
            {
                if (distances[i] >= distances[i - 1]) continue;
                Debug.LogError("CullingGroup bounding distances must be sorted in ascending order.");
                return;
            }
            m_Distances = (float[])distances.Clone();
        }

        public void SetDistanceReferencePoint(Vector3 point)
        {
            Check();
            m_ReferencePoint = point;
            m_ReferenceTransform = null;
        }

        /// <summary>The transform's position at each evaluation; null keeps the last point.</summary>
        public void SetDistanceReferencePoint(Transform transform)
        {
            Check();
            m_ReferenceTransform = transform;
        }

        Vector3 ReferencePoint()
        {
            if (m_ReferenceTransform != null) m_ReferencePoint = m_ReferenceTransform.position;
            return m_ReferencePoint;
        }

        int Band(in BoundingSphere sphere, Vector3 reference)
        {
            float distance = Vector3.Distance(sphere.position, reference) - sphere.radius;
            int band = 0;
            while (band < m_Distances.Length && distance > m_Distances[band]) band++;
            return band;
        }

        /// <summary>Evaluates the groups in a camera's culling (built-in and scriptable pipelines, after
        /// the camera's OnPreCull) and sends their callbacks.</summary>
        internal static void EvaluateForCamera(Camera camera)
        {
            if (s_Groups.Count == 0 || camera == null) return;
            GeometryUtility.CalculateFrustumPlanes(camera, s_Planes);
            s_Events.Clear();
            List<(CullingGroup Group, int Start, int Count)> sends = null;
            foreach (var group in s_Groups)
            {
                if (!group.m_Enabled || group.m_Count == 0) continue;
                if (group.m_TargetCamera == null) group.UpdateDistances();
                else if (group.m_TargetCamera == camera)
                {
                    int start = s_Events.Count;
                    group.Evaluate();
                    if (s_Events.Count > start) (sends ??= new List<(CullingGroup, int, int)>()).Add((group, start, s_Events.Count - start));
                }
            }
            if (sends == null) return;
            // Callbacks may render cameras (and evaluate groups) themselves.
            var events = s_Events.ToArray();
            s_Events.Clear();
            foreach (var (group, start, count) in sends)
            {
                if (group.m_OnStateChanged == null) continue;
                // A callback that throws ends its group's callbacks for this evaluation.
                try
                {
                    for (int i = 0; i < count; i++) group.m_OnStateChanged(events[start + i]);
                }
                catch (Exception e)
                {
                    Debug.LogException(e);
                }
            }
        }

        /// <summary>A group with no target camera: distance bands only, no visibility, no callbacks.</summary>
        void UpdateDistances()
        {
            var reference = ReferencePoint();
            for (int i = 0; i < m_Count; i++)
                m_States[i] = (byte)((m_States[i] & kVisible) | (Band(m_Spheres[i], reference) & kDistanceMask));
        }

        void Evaluate()
        {
            var reference = ReferencePoint();
            for (int i = 0; i < m_Count; i++)
            {
                var sphere = m_Spheres[i];
                int band = Band(sphere, reference);
                bool visible = (m_Distances.Length == 0 || band < m_Distances.Length) && InFrustum(sphere);
                byte state = (byte)((visible ? kVisible : 0) | (band & kDistanceMask));
                m_States[i] = state;
                if (state == m_SentStates[i]) continue;
                s_Events.Add(new CullingGroupEvent(i, m_SentStates[i], state));
                m_SentStates[i] = state;
            }
        }

        static bool InFrustum(in BoundingSphere sphere)
        {
            foreach (var plane in s_Planes)
                if (plane.GetDistanceToPoint(sphere.position) < -sphere.radius) return false;
            return true;
        }
    }

    internal enum CullingQueryOptions
    {
        Normal = 0,
        IgnoreVisibility = 1,
        IgnoreDistance = 2,
    }
}
