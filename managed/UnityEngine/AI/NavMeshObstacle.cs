using System;
using Unbound.Runtime;

namespace UnityEngine.AI
{
    /// <summary>
    /// An obstacle on the NavMesh. A carving obstacle cuts its footprint, grown by the agents'
    /// radius, out of the NavMesh (see <see cref="ObstacleSystem"/>). Avoidance of obstacles that
    /// do not carve is not supported yet.
    /// </summary>
    [ClassId(208)]
    public sealed partial class NavMeshObstacle : Behaviour
    {
        // Unity keeps the half size: radius reads its x, height its y, and setting the height
        // sets the half size to half of it (measured: the default height reads 0.5; a height
        // set to 2 reads 1 with size.y 2; tests/projects/navobstacle).
        Vector3 m_Extents = new Vector3(0.5f, 0.5f, 0.5f);
        Vector3 m_Velocity;
        bool m_Carving;
        bool m_CarveOnlyStationary = true;
        float m_CarvingMoveThreshold = 0.1f;
        float m_CarvingTimeToStationary = 0.5f;
        NavMeshObstacleShape m_Shape = NavMeshObstacleShape.Box;
        Vector3 m_Center;

        public NavMeshObstacle() { }

        public float height
        {
            get => m_Extents.y;
            set { m_Extents.y = Mathf.Max(0f, value) * 0.5f; ObstacleSystem.Changed(this); }
        }

        public float radius
        {
            get => m_Extents.x;
            set { m_Extents.x = m_Extents.z = Mathf.Max(0f, value); ObstacleSystem.Changed(this); }
        }

        /// <summary>As set by scripts; not derived from the obstacle's movement (measured).</summary>
        public Vector3 velocity { get => m_Velocity; set => m_Velocity = value; }
        public bool carving { get => m_Carving; set { m_Carving = value; ObstacleSystem.Changed(this); } }
        public bool carveOnlyStationary { get => m_CarveOnlyStationary; set => m_CarveOnlyStationary = value; }
        public float carvingMoveThreshold { get => m_CarvingMoveThreshold; set => m_CarvingMoveThreshold = Mathf.Max(0f, value); }
        public float carvingTimeToStationary { get => m_CarvingTimeToStationary; set => m_CarvingTimeToStationary = Mathf.Max(0f, value); }
        public NavMeshObstacleShape shape { get => m_Shape; set { m_Shape = value; ObstacleSystem.Changed(this); } }
        public Vector3 center { get => m_Center; set { m_Center = value; ObstacleSystem.Changed(this); } }

        public Vector3 size
        {
            get => m_Extents * 2f;
            set { m_Extents = Vector3.Max(value, Vector3.zero) * 0.5f; ObstacleSystem.Changed(this); }
        }

        internal Vector3 Extents => m_Extents;

        /// <summary>Fits the obstacle to the GameObject's mesh, as Unity does when it is added.</summary>
        internal void FitExtents()
        {
            var filter = m_GameObject.GetComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                var b = filter.sharedMesh.bounds;
                m_Center = b.center;
                m_Extents = b.extents;
            }
        }

        internal override void OnAttached()
        {
            base.OnAttached();
            FitExtents();
        }

        internal override void OnEngineEnable() => ObstacleSystem.Add(this);

        internal override void OnEngineDisable() => ObstacleSystem.Remove(this);

        internal override void OnEngineDestroy() => ObstacleSystem.Remove(this);

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_Shape = (NavMeshObstacleShape)body.Int("m_Shape", 1);
            m_Extents = body.Vector3("m_Extents", new Vector3(0.5f, 0.5f, 0.5f));
            m_Center = body.Vector3("m_Center");
            m_Carving = body.Bool("m_Carve");
            m_CarveOnlyStationary = body.Bool("m_CarveOnlyStationary", true);
            m_CarvingMoveThreshold = body.Float("m_MoveThreshold", 0.1f);
            m_CarvingTimeToStationary = body.Float("m_TimeToStationary", 0.5f);
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (NavMeshObstacle)source;
            m_Extents = s.m_Extents;
            m_Carving = s.m_Carving;
            m_CarveOnlyStationary = s.m_CarveOnlyStationary;
            m_CarvingMoveThreshold = s.m_CarvingMoveThreshold;
            m_CarvingTimeToStationary = s.m_CarvingTimeToStationary;
            m_Shape = s.m_Shape;
            m_Center = s.m_Center;
            m_Velocity = s.m_Velocity;
        }
    }

    /// <summary>The legacy link component: a link between two transforms.</summary>
    [Obsolete("The OffMeshLink component is no longer supported and will be removed. Use NavMeshLink instead.")]
    [ClassId(191)]
    public sealed partial class OffMeshLink : Behaviour
    {
        Transform m_Start;
        Transform m_End;
        float m_CostOverride = -1f;
        bool m_BiDirectional = true;
        bool m_Activated = true;
        int m_Area;
        bool m_AutoUpdatePositions;
        int m_Link;

        public OffMeshLink() { }

        [Obsolete("activated has been deprecated together with the class. Declare the object as NavMeshLink and use activated as before.")]
        public bool activated
        {
            get => m_Activated;
            set
            {
                m_Activated = value;
                if (m_Link != 0) NavMesh.SetOffMeshConnectionActive(m_Link, value);
            }
        }

        [Obsolete("occupied has been deprecated together with the class. Declare the object as NavMeshLink and use occupied as before.")]
        public bool occupied => m_Link != 0 && NavMesh.IsOffMeshConnectionOccupied(m_Link);

        [Obsolete("costOverride has been deprecated together with the class. Declare the object as NavMeshLink and use costModifier instead.")]
        public float costOverride { get => m_CostOverride; set { m_CostOverride = value; Relink(); } }

        [Obsolete("biDirectional has been deprecated together with the class. Declare the object as NavMeshLink and use bidirectional instead.")]
        public bool biDirectional { get => m_BiDirectional; set { m_BiDirectional = value; Relink(); } }

        [Obsolete("navMeshLayer has been deprecated together with the class. Declare the object as NavMeshLink and use area instead. (UnityUpgradable) -> area")]
        public int navMeshLayer { get => m_Area; set { m_Area = value; Relink(); } }

        [Obsolete("area has been deprecated together with the class. Declare the object as NavMeshLink and use area as before.")]
        public int area { get => m_Area; set { m_Area = value; Relink(); } }

        [Obsolete("autoUpdatePositions has been deprecated together with the class. Declare the object as NavMeshLink and use autoUpdate instead.")]
        public bool autoUpdatePositions { get => m_AutoUpdatePositions; set => m_AutoUpdatePositions = value; }

        [Obsolete("startTransform has been deprecated together with the class. Declare the object as NavMeshLink and use startTransform as before.")]
        public Transform startTransform { get => m_Start; set { m_Start = value; Relink(); } }

        [Obsolete("endTransform has been deprecated together with the class. Declare the object as NavMeshLink and use endTransform as before.")]
        public Transform endTransform { get => m_End; set { m_End = value; Relink(); } }

        [Obsolete("UpdatePositions() has been deprecated together with the class. Declare the object as NavMeshLink and use UpdateLink() instead.")]
        public void UpdatePositions() => Relink();

        void Relink()
        {
            if (m_Link != 0) NavMesh.RemoveLinkInternal(m_Link);
            m_Link = 0;
            if (!isActiveAndEnabled || m_Start == null || m_End == null) return;
            var data = new NavMeshLinkData
            {
                startPosition = m_Start.position,
                endPosition = m_End.position,
                bidirectional = m_BiDirectional,
                costModifier = m_CostOverride,
                width = 0f,
                area = m_Area,
            };
            m_Link = NavMesh.AddLinkInternal(data, Vector3.zero, Quaternion.identity);
            NavMesh.InternalSetLinkOwner(m_Link, GetInstanceID());
            NavMesh.SetOffMeshConnectionActive(m_Link, m_Activated);
        }

        internal override void OnEngineEnable() => Relink();

        internal override void OnEngineDisable()
        {
            if (m_Link != 0) NavMesh.RemoveLinkInternal(m_Link);
            m_Link = 0;
        }

        internal override void OnEngineDestroy() => OnEngineDisable();

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            var start = body.Ref("m_Start");
            var end = body.Ref("m_End");
            m_Start = start.IsNull ? null : resolver.Resolve(start, typeof(Transform)) as Transform;
            m_End = end.IsNull ? null : resolver.Resolve(end, typeof(Transform)) as Transform;
            m_CostOverride = body.Float("m_CostOverride", -1f);
            m_BiDirectional = body.Bool("m_BiDirectional", true);
            m_Activated = body.Bool("m_Activated", true);
            m_Area = body.Int("m_AreaIndex");
            m_AutoUpdatePositions = body.Bool("m_AutoUpdatePositions");
        }
    }
}

namespace Unbound.Runtime
{
    using System.Collections.Generic;
    using UnityEngine;
    using UnityEngine.AI;

    /// <summary>
    /// The enabled obstacles and their carving (measured, tests/projects/navobstacle and
    /// tests/projects/navmesh). Once per frame, after the scripts' Update, each carving obstacle
    /// is looked at where Update left it; the NavMesh changes at the start of the next frame
    /// (an obstacle added in LateUpdate is seen a frame later):
    /// - a new obstacle, one just enabled or one whose carving was just switched on carves at once;
    /// - it has moved when it lies more than carvingMoveThreshold from where it last moved;
    /// - carveOnlyStationary: a move removes the carve; once it has not moved for
    ///   carvingTimeToStationary (the frames' times summed, from the look after its move) it
    ///   carves where it stands;
    /// - otherwise a move carves anew where it stands; smaller moves leave the carve in place.
    /// The carve is the footprint grown by the agents' radius of each NavMesh (see CarveHull);
    /// only the tiles under it are rebuilt, and agents whose path ran over them find it again
    /// at once (see AgentSystem.NavMeshCarved).
    /// </summary>
    internal static class ObstacleSystem
    {
        sealed class State
        {
            internal bool Seen;
            internal Vector3 Moved;
            internal float Still;
            internal Matrix4x4? Carved;
            internal NavMeshObstacleShape Shape;
            internal Vector3 Center, Extents;
            internal bool Dirty;
        }

        static readonly List<NavMeshObstacle> s_Obstacles = new List<NavMeshObstacle>();
        static readonly Dictionary<NavMeshObstacle, State> s_States = new Dictionary<NavMeshObstacle, State>();
        static readonly List<(Matrix4x4 Pose, NavMeshObstacleShape Shape, Vector3 Center, Vector3 Extents)> s_Removed =
            new List<(Matrix4x4, NavMeshObstacleShape, Vector3, Vector3)>();
        static bool s_Registered;
        static bool s_WarnedAvoidance;

        static void EnsureRegistered()
        {
            if (s_Registered) return;
            s_Registered = true;
            PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.PreLateUpdate.AIUpdatePostScript), Detect);
            PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.PreUpdate.AIUpdate), Apply);
        }

        internal static void Add(NavMeshObstacle obstacle)
        {
            EnsureRegistered();
            if (!s_Obstacles.Contains(obstacle)) s_Obstacles.Add(obstacle);
            s_States[obstacle] = new State();
        }

        internal static void Remove(NavMeshObstacle obstacle)
        {
            s_Obstacles.Remove(obstacle);
            if (s_States.TryGetValue(obstacle, out var state) && state.Carved.HasValue)
                s_Removed.Add((state.Carved.Value, state.Shape, state.Center, state.Extents));
            s_States.Remove(obstacle);
        }

        internal static void Changed(NavMeshObstacle obstacle)
        {
            if (s_States.TryGetValue(obstacle, out var state)) state.Dirty = true;
        }

        /// <summary>The carves on the NavMesh of an agent type (grown by its agents' radius).</summary>
        internal static IReadOnlyList<CarveHull> Hulls(NavWorld.Surface surface)
        {
            var hulls = new List<CarveHull>();
            foreach (var obstacle in s_Obstacles)
                if (s_States.TryGetValue(obstacle, out var state) && state.Carved.HasValue)
                    hulls.Add(Hull(state.Carved.Value, state.Shape, state.Center, state.Extents, surface.Settings.agentRadius));
            return hulls;
        }

        static CarveHull Hull(Matrix4x4 pose, NavMeshObstacleShape shape, Vector3 center, Vector3 extents, float grow)
        {
            if (shape == NavMeshObstacleShape.Capsule)
            {
                var c = pose.MultiplyPoint3x4(center);
                var scale = pose.lossyScale;
                float r = extents.x * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                float hh = extents.y * Mathf.Abs(scale.y);
                return CarveHull.Capsule(c.x, c.z, r + grow, c.y - hh, c.y + hh);
            }
            var corners = new List<(double, double)>(8);
            float minY = float.MaxValue, maxY = float.MinValue;
            for (int i = 0; i < 8; i++)
            {
                var local = center + new Vector3((i & 1) != 0 ? extents.x : -extents.x, (i & 2) != 0 ? extents.y : -extents.y, (i & 4) != 0 ? extents.z : -extents.z);
                var w = pose.MultiplyPoint3x4(local);
                corners.Add((w.x, w.z));
                minY = Mathf.Min(minY, w.y);
                maxY = Mathf.Max(maxY, w.y);
            }
            return CarveHull.Box(corners, grow, minY, maxY);
        }

        static readonly List<(Matrix4x4, NavMeshObstacleShape, Vector3, Vector3)> s_Pending =
            new List<(Matrix4x4, NavMeshObstacleShape, Vector3, Vector3)>();

        /// <summary>After the scripts' Update: what the obstacles' carves become.</summary>
        static void Detect()
        {
            float dt = Time.deltaTime;
            var regions = s_Pending;
            regions.AddRange(s_Removed);
            s_Removed.Clear();
            foreach (var obstacle in s_Obstacles)
            {
                var state = s_States[obstacle];
                var position = obstacle.transform.position;
                bool carve = obstacle.carving && obstacle.isActiveAndEnabled;
                if (!carve)
                {
                    if (obstacle.isActiveAndEnabled && !s_WarnedAvoidance)
                    {
                        s_WarnedAvoidance = true;
                        Debug.LogWarning("Unbound: NavMeshObstacle avoidance is not supported yet; agents do not steer around obstacles that do not carve.", obstacle);
                    }
                    if (state.Carved.HasValue) Uncarve(state, regions);
                    state.Seen = false;
                    continue;
                }
                if (!state.Seen || state.Dirty)
                {
                    // New, enabled, switched on or reshaped: carves where it stands.
                    if (state.Carved.HasValue) Uncarve(state, regions);
                    state.Seen = true;
                    state.Dirty = false;
                    state.Moved = position;
                    state.Still = 0f;
                    Carve(obstacle, state, regions);
                    continue;
                }
                bool moved = (position - state.Moved).magnitude > obstacle.carvingMoveThreshold;
                if (obstacle.carveOnlyStationary)
                {
                    if (moved)
                    {
                        state.Moved = position;
                        state.Still = 0f;
                        if (state.Carved.HasValue) Uncarve(state, regions);
                    }
                    else if (!state.Carved.HasValue)
                    {
                        state.Still += dt;
                        if (state.Still >= obstacle.carvingTimeToStationary) Carve(obstacle, state, regions);
                    }
                }
                else if (moved)
                {
                    state.Moved = position;
                    if (state.Carved.HasValue) Uncarve(state, regions);
                    Carve(obstacle, state, regions);
                }
            }
        }

        /// <summary>At the start of the next frame (before the scripts' Update): the NavMesh
        /// under the changed carves is cut anew.</summary>
        static void Apply()
        {
            var regions = s_Pending;
            if (regions.Count == 0) return;
            foreach (var surface in NavWorld.Surfaces)
            {
                float minX = float.MaxValue, minZ = float.MaxValue, maxX = float.MinValue, maxZ = float.MinValue;
                foreach (var (pose, shape, center, extents) in regions)
                {
                    var hull = Hull(pose, shape, center, extents, surface.Settings.agentRadius);
                    minX = Mathf.Min(minX, (float)hull.MinX); minZ = Mathf.Min(minZ, (float)hull.MinZ);
                    maxX = Mathf.Max(maxX, (float)hull.MaxX); maxZ = Mathf.Max(maxZ, (float)hull.MaxZ);
                }
                NavWorld.Recarve(surface, minX, minZ, maxX, maxZ);
                AgentSystem.NavMeshCarved(surface);
            }
            regions.Clear();
        }

        static void Carve(NavMeshObstacle obstacle, State state, List<(Matrix4x4, NavMeshObstacleShape, Vector3, Vector3)> regions)
        {
            state.Carved = obstacle.transform.localToWorldMatrix;
            state.Shape = obstacle.shape;
            state.Center = obstacle.center;
            state.Extents = obstacle.Extents;
            regions.Add((state.Carved.Value, state.Shape, state.Center, state.Extents));
        }

        static void Uncarve(State state, List<(Matrix4x4, NavMeshObstacleShape, Vector3, Vector3)> regions)
        {
            regions.Add((state.Carved.Value, state.Shape, state.Center, state.Extents));
            state.Carved = null;
        }
    }
}
