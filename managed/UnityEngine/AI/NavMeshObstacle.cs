using System;
using Unbound.Runtime;

namespace UnityEngine.AI
{
    /// <summary>
    /// An obstacle agents avoid in their local avoidance. Unity's carving obstacle cuts a hole in
    /// the NavMesh; Unbound does not carve yet and reports it (see ObstacleSystem).
    /// </summary>
    [ClassId(208)]
    public sealed partial class NavMeshObstacle : Behaviour
    {
        float m_Height = 1f;
        float m_Radius = 0.5f;
        Vector3 m_Velocity;
        bool m_Carving;
        bool m_CarveOnlyStationary = true;
        float m_CarvingMoveThreshold = 0.1f;
        float m_CarvingTimeToStationary = 0.5f;
        NavMeshObstacleShape m_Shape = NavMeshObstacleShape.Box;
        Vector3 m_Center;
        Vector3 m_Size = Vector3.one;

        public NavMeshObstacle() { }

        public float height { get => m_Height; set { m_Height = Mathf.Max(0f, value); ObstacleSystem.Changed(this); } }
        public float radius { get => m_Radius; set { m_Radius = Mathf.Max(0f, value); ObstacleSystem.Changed(this); } }
        public Vector3 velocity { get => m_Velocity; set => m_Velocity = value; }
        public bool carving { get => m_Carving; set { m_Carving = value; ObstacleSystem.Changed(this); } }
        public bool carveOnlyStationary { get => m_CarveOnlyStationary; set => m_CarveOnlyStationary = value; }
        public float carvingMoveThreshold { get => m_CarvingMoveThreshold; set => m_CarvingMoveThreshold = Mathf.Max(0f, value); }
        public float carvingTimeToStationary { get => m_CarvingTimeToStationary; set => m_CarvingTimeToStationary = Mathf.Max(0f, value); }
        public NavMeshObstacleShape shape { get => m_Shape; set { m_Shape = value; ObstacleSystem.Changed(this); } }
        public Vector3 center { get => m_Center; set { m_Center = value; ObstacleSystem.Changed(this); } }
        public Vector3 size { get => m_Size; set { m_Size = Vector3.Max(value, Vector3.zero); ObstacleSystem.Changed(this); } }

        /// <summary>Fits the obstacle to the GameObject's mesh, as Unity does when it is added.</summary>
        internal void FitExtents()
        {
            var filter = m_GameObject.FindComponent<MeshFilter>();
            if (filter != null && filter.sharedMesh != null)
            {
                var b = filter.sharedMesh.bounds;
                m_Center = b.center;
                m_Size = b.size;
                m_Radius = Mathf.Max(b.extents.x, b.extents.z);
                m_Height = b.size.y;
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
            m_Size = body.Vector3("m_Extents", new Vector3(0.5f, 0.5f, 0.5f)) * 2f;
            m_Center = body.Vector3("m_Center");
            m_Carving = body.Bool("m_Carve");
            m_CarveOnlyStationary = body.Bool("m_CarveOnlyStationary", true);
            m_CarvingMoveThreshold = body.Float("m_MoveThreshold", 0.1f);
            m_CarvingTimeToStationary = body.Float("m_TimeToStationary", 0.5f);
            m_Radius = m_Size.x * 0.5f;
            m_Height = m_Size.y;
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (NavMeshObstacle)source;
            m_Height = s.m_Height;
            m_Radius = s.m_Radius;
            m_Carving = s.m_Carving;
            m_CarveOnlyStationary = s.m_CarveOnlyStationary;
            m_CarvingMoveThreshold = s.m_CarvingMoveThreshold;
            m_CarvingTimeToStationary = s.m_CarvingTimeToStationary;
            m_Shape = s.m_Shape;
            m_Center = s.m_Center;
            m_Size = s.m_Size;
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
    using System.Runtime.CompilerServices;
    using DotRecast.Core.Numerics;
    using DotRecast.Detour.Crowd;
    using UnityEngine;
    using UnityEngine.AI;

    /// <summary>
    /// The enabled obstacles, and their shapes in the agents' local avoidance.
    /// </summary>
    // Documented (Scripting API 6000.0, NavMeshObstacle; Manual "NavMesh Obstacle" and "Inner
    // Workings of the Navigation System", AI Navigation 2.0): an obstacle for agents to avoid; by
    // default it affects only the agents' avoidance, not their paths: agents plot paths ignoring it
    // and sidestep it as they move, and one blocking a gap stops them until it is clear. Local
    // avoidance only looks at imminent collisions: it cannot steer around traps or a blocked path.
    // A carving obstacle cuts a hole in the NavMesh, which paths route around, when it stands still
    // (carveOnlyStationary, after carvingTimeToStationary) or as it moves past carvingMoveThreshold;
    // while it moves without carving it is an obstruction for avoidance; the hole takes effect in
    // the next frame.
    //
    // Carving is not supported: Unbound's NavMesh tiles are the baked polygons, with no source
    // geometry or tile cache to rebuild them from, so a hole cannot be cut without approximating
    // the polygons around it. A carving obstacle is reported once and leaves the NavMesh (and
    // isPathStale) as it is; Unbound's choice (unmeasured): it stays an avoidance obstacle, moving
    // or not, so agents sidestep it instead of planning around it.
    //
    // Avoidance: each enabled obstacle is one or more circles that the agents of every agent type
    // avoid in their crowd as they avoid other agents, moving at the obstacle's velocity. The
    // circles are fixed crowd members that never move themselves and that every agent ranks as more
    // important than itself, so agents also keep apart from them and are pushed out of them, as from
    // a more important agent. Unbound's choices (unmeasured): the obstacle's position is read from
    // its transform when the agents update; its velocity is the velocity property (an obstacle a
    // script moves without setting it is avoided where it stands); a capsule is an upright circle of
    // its radius times the larger horizontal scale; a box is the rectangle around its horizontal
    // outline, along its turned x axis, covered by up to four circles along its longer side; an
    // obstacle counts for an agent whose height span (the agent type's height) overlaps its own; an
    // obstacle whose centre lies beyond an agent's neighbour range is not seen, and obstacles take
    // places among the agent's nearest neighbours as other agents do.
    internal static class ObstacleSystem
    {
        static readonly List<NavMeshObstacle> s_Obstacles = new List<NavMeshObstacle>();
        /// <summary>The carving warning was given (once per run).</summary>
        internal static bool s_Warned;

        internal static void Add(NavMeshObstacle obstacle)
        {
            if (!s_Obstacles.Contains(obstacle)) s_Obstacles.Add(obstacle);
            Changed(obstacle);
        }

        internal static void Remove(NavMeshObstacle obstacle) => s_Obstacles.Remove(obstacle);

        internal static void Changed(NavMeshObstacle obstacle)
        {
            if (obstacle.carving && obstacle.isActiveAndEnabled && !s_Warned)
            {
                s_Warned = true;
                Debug.LogWarning("Unbound: NavMeshObstacle carving is not supported yet; carving obstacles leave the NavMesh unchanged "
                    + "and agents only avoid them locally.", obstacle);
            }
        }

        // ------------------------------------------------------------------ avoidance

        /// <summary>The most circles a box becomes.</summary>
        const int MaxBoxCircles = 4;

        readonly struct Circle
        {
            internal readonly Vector3 Center;
            internal readonly float Radius;
            internal readonly float Bottom;
            internal readonly float Top;
            internal readonly Vector3 Velocity;
            internal readonly NavMeshObstacle Owner;

            internal Circle(Vector3 center, float radius, float bottom, float top, Vector3 velocity, NavMeshObstacle owner)
            {
                Center = center;
                Radius = radius;
                Bottom = bottom;
                Top = top;
                Velocity = velocity;
                Owner = owner;
            }
        }

        /// <summary>The obstacle circles in each crowd, reused from update to update.</summary>
        static readonly ConditionalWeakTable<DtCrowd, List<DtCrowdAgent>> s_InCrowd = new ConditionalWeakTable<DtCrowd, List<DtCrowdAgent>>();
        static readonly List<Circle> s_Circles = new List<Circle>();

        /// <summary>Before the crowd of <paramref name="surface"/> updates: the enabled obstacles,
        /// where they are now, become its obstacle circles. Without obstacles (and none fed before)
        /// the crowd is left untouched.</summary>
        internal static void Feed(DtCrowd crowd, NavWorld.Surface surface)
        {
            if (!s_InCrowd.TryGetValue(crowd, out var members))
            {
                if (s_Obstacles.Count == 0) return;
                members = new List<DtCrowdAgent>();
                s_InCrowd.Add(crowd, members);
            }
            s_Circles.Clear();
            foreach (var obstacle in s_Obstacles)
                if (obstacle != null && obstacle.isActiveAndEnabled) AddCircles(obstacle, s_Circles);
            while (members.Count > s_Circles.Count)
            {
                crowd.RemoveAgent(members[members.Count - 1]);
                members.RemoveAt(members.Count - 1);
            }
            if (s_Circles.Count == 0)
            {
                s_InCrowd.Remove(crowd);
                return;
            }
            // The agents' neighbour test compares their feet with the circle's position over half
            // their heights together: placed half the agent type's height under the obstacle's
            // middle, it passes when the height spans overlap.
            float agentHeight = surface != null ? surface.Settings.agentHeight : 2f;
            for (int i = 0; i < s_Circles.Count; i++)
            {
                var c = s_Circles[i];
                var position = new RcVec3f(c.Center.x, (c.Bottom + c.Top) * 0.5f - agentHeight * 0.5f, c.Center.z);
                var options = new DtCrowdAgentParams
                {
                    radius = c.Radius,
                    height = Mathf.Max(0f, c.Top - c.Bottom),
                    maxAcceleration = 0f,
                    maxSpeed = 0f,
                    collisionQueryRange = 0f,
                    pathOptimizationRange = 0f,
                    separationWeight = 0f,
                    updateFlags = 0,
                    obstacleAvoidanceType = 0,
                    queryFilterType = 0,
                    userData = c.Owner,
                    // More important than any agent: every agent avoids it, and it never gives way.
                    avoidancePriority = int.MinValue,
                };
                if (i == members.Count) members.Add(crowd.AddAgent(position, options));
                var member = members[i];
                crowd.UpdateAgentParameters(member, options);
                // Not walking: the crowd neither steers, moves nor pushes it; agents still see it as
                // a neighbour.
                member.state = DtCrowdAgentState.DT_CROWDAGENT_STATE_INVALID;
                member.targetState = DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE;
                member.npos = position;
                var velocity = NavMesh.V(c.Velocity);
                member.vel = velocity;
                member.dvel = velocity;
                member.nvel = velocity;
                member.pvel = velocity;
            }
        }

        /// <summary>The obstacle's shape as circles in the horizontal plane, with its height span.</summary>
        static void AddCircles(NavMeshObstacle obstacle, List<Circle> into)
        {
            var t = obstacle.transform;
            var center = t.TransformPoint(obstacle.center);
            var scale = t.lossyScale;
            var velocity = obstacle.velocity;
            if (obstacle.shape == NavMeshObstacleShape.Capsule)
            {
                float radius = obstacle.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
                float half = obstacle.height * Mathf.Abs(scale.y) * 0.5f;
                into.Add(new Circle(center, radius, center.y - half, center.y + half, velocity, obstacle));
                return;
            }
            var rotation = t.rotation;
            var size = obstacle.size;
            var ax = rotation * new Vector3(size.x * scale.x * 0.5f, 0f, 0f);
            var ay = rotation * new Vector3(0f, size.y * scale.y * 0.5f, 0f);
            var az = rotation * new Vector3(0f, 0f, size.z * scale.z * 0.5f);
            float halfHeight = Mathf.Abs(ax.y) + Mathf.Abs(ay.y) + Mathf.Abs(az.y);
            // The rectangle's axes: the box's x axis turned about the vertical (its z axis turned a
            // quarter when x stands upright).
            var right = rotation * Vector3.right;
            var u = new Vector2(right.x, right.z);
            if (u.sqrMagnitude < 1e-8f)
            {
                var forward = rotation * Vector3.forward;
                u = new Vector2(forward.z, -forward.x);
            }
            u.Normalize();
            var v = new Vector2(-u.y, u.x);
            float hu = Extent(ax, u) + Extent(ay, u) + Extent(az, u);
            float hv = Extent(ax, v) + Extent(ay, v) + Extent(az, v);
            var along = hu >= hv ? u : v;
            float length = Mathf.Max(hu, hv), width = Mathf.Min(hu, hv);
            int count = width > 0f ? Mathf.Clamp(Mathf.CeilToInt(length / width - 1e-4f), 1, MaxBoxCircles) : length > 0f ? MaxBoxCircles : 1;
            float piece = length / count;
            float circleRadius = Mathf.Sqrt(width * width + piece * piece);
            for (int i = 0; i < count; i++)
            {
                float offset = -length + piece * (2 * i + 1);
                var c = center + new Vector3(along.x, 0f, along.y) * offset;
                into.Add(new Circle(c, circleRadius, center.y - halfHeight, center.y + halfHeight, velocity, obstacle));
            }
        }

        static float Extent(Vector3 axis, Vector2 direction) => Mathf.Abs(axis.x * direction.x + axis.z * direction.y);
    }
}
