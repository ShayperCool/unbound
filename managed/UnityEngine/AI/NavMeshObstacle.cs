using System;
using Unbound.Runtime;

namespace UnityEngine.AI
{
    /// <summary>
    /// An obstacle agents avoid in their local avoidance; a carving obstacle cuts a hole in the
    /// NavMesh (see ObstacleSystem and NavMeshCarving).
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

        public float height { get => m_Height; set { m_Height = Mathf.Max(0f, value); } }
        public float radius { get => m_Radius; set { m_Radius = Mathf.Max(0f, value); } }
        public Vector3 velocity { get => m_Velocity; set => m_Velocity = value; }
        public bool carving { get => m_Carving; set { m_Carving = value; } }
        public bool carveOnlyStationary { get => m_CarveOnlyStationary; set => m_CarveOnlyStationary = value; }
        public float carvingMoveThreshold { get => m_CarvingMoveThreshold; set => m_CarvingMoveThreshold = Mathf.Max(0f, value); }
        public float carvingTimeToStationary { get => m_CarvingTimeToStationary; set => m_CarvingTimeToStationary = Mathf.Max(0f, value); }
        public NavMeshObstacleShape shape { get => m_Shape; set { m_Shape = value; } }
        public Vector3 center { get => m_Center; set { m_Center = value; } }
        public Vector3 size { get => m_Size; set { m_Size = Vector3.Max(value, Vector3.zero); } }

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
    // C1: carving (see UpdateCarving for the timing, NavMeshCarving for the hole's shape). The hole
    // is cut into the tiles when Unbound next refreshes its NavMesh, and the agents following a path
    // plan it again as for any NavMesh change (AgentSystem.Rebuild); isPathStale is not set
    // (carving is not among its documented causes). Unbound's choice (unmeasured): an obstacle
    // whose hole is in the NavMesh is not an avoidance obstacle as well; one moving without a hole
    // (carveOnlyStationary) is.
    //
    // Avoidance: each enabled obstacle is one or more circles that the agents of every agent type
    // avoid in their crowd as they avoid other agents, moving at the obstacle's velocity. The
    // circles are fixed crowd members that never move themselves and that every agent ranks as more
    // important than itself, so agents also keep apart from them and are pushed out of them, as from
    // a more important agent.
    // C2 (Unbound's choice, unmeasured): the obstacle's position is read from its transform when the
    //   agents update.
    // C3: its velocity in avoidance is the velocity property; an obstacle a script moves without
    //   setting it is avoided where it stands. Documented support: the velocity page's own example
    //   (Scripting API 6000.0 NavMeshObstacle.velocity, ManualObstacleVelocityUpdater,
    //   https://docs.unity3d.com/6000.0/Documentation/ScriptReference/AI.NavMeshObstacle-velocity.html)
    //   is a script that moves the object and computes and sets velocity itself. Obstacles moved by
    //   physics or animation are unmeasured.
    // C4 (Unbound's choice, unmeasured): a capsule is an upright circle of its radius times the
    //   larger horizontal scale (the Scripting API calls the obstacle "cylindrical in shape").
    // C5 (Unbound's choice, unmeasured): a box is the rectangle around its horizontal outline, along
    //   its turned x axis, cut into ceil(long side / short side) equal pieces along its longer side,
    //   each covered by the circle through its corners. Bound: each circle's radius is at most
    //   sqrt(2) times the box's half-width, so the cover bulges at most (sqrt(2) - 1) times the
    //   half-width beyond the box's long sides and less than the half-width beyond its ends (a square
    //   box is one circle). At most MaxBoxCircles (16) circles per box: a box
    //   longer than 16 times its width (or with no width) gets 16 circles of larger radius, whose
    //   bulge grows with its length; that is reported once per run.
    // C6 (Unbound's choice, unmeasured): an obstacle counts for an agent whose height span overlaps
    //   its own (the test Unity's agents apply to each other: the obstacle's bottom is not above the
    //   agent's head, its top not below the agent's feet).
    // C7 (Unbound's choice, unmeasured): an obstacle circle whose centre lies beyond an agent's
    //   neighbour range is not seen, and circles take places among the agent's nearest neighbours as
    //   other agents do. Limit: the crowd gathers at most DtCrowd.NeighbourCandidates (64) neighbour
    //   candidates per agent from the proximity-grid cells its range touches before it keeps the
    //   soonest reachable (DtCrowd GetNeighbours), so obstacle circles there can leave agents out. To
    //   keep that from happening needlessly, only circles that can be an agent's neighbour are fed:
    //   within the square of some walking agent's neighbour reach, with an overlapping height span
    //   (C6). When an agent's candidates still exceed that with obstacle circles among them, it is
    //   reported once per run.
    // C8: agents keep apart from obstacle circles and are pushed out of them, including agents with
    //   NoObstacleAvoidance. Documented support: AI Navigation 2.0 Manual, NavMesh Agent component
    //   (https://docs.unity3d.com/Packages/com.unity.ai.navigation@2.0/manual/NavMeshAgent.html),
    //   Quality None: "collisions resolve, but other agents and obstacles are not actively
    //   avoided"; Manual, NavMesh Obstacle
    //   (https://docs.unity3d.com/Packages/com.unity.ai.navigation@2.0/manual/AboutObstacles.html):
    //   "NavMesh Agents try to avoid collisions with the NavMesh Obstacle, and when close, they
    //   collide with the NavMesh Obstacle." How far and how fast a collision resolves is unmeasured.
    internal static class ObstacleSystem
    {
        static readonly List<NavMeshObstacle> s_Obstacles = new List<NavMeshObstacle>();
        /// <summary>A box was covered by fewer circles than its length needs (C5; once per run).</summary>
        internal static bool s_WarnedLongBox;
        /// <summary>An agent's neighbour candidates exceeded the crowd's cap with obstacle circles
        /// among them (C7; once per run).</summary>
        internal static bool s_WarnedCandidates;

        internal static void Add(NavMeshObstacle obstacle)
        {
            if (!s_Registered)
            {
                s_Registered = true;
                // The holes decided are cut before the agents' transform sync at the same node
                // (whichever registers first), which places the agents on the cut NavMesh: an agent
                // placed again by a cut stands there from this frame's Update (Unbound's order;
                // unmeasured). The obstacles' holes are decided after the scripts' Update
                // (PreLateUpdate's AIUpdatePostScript), so a change made in Update or a coroutine
                // is cut at the next frame (measured: tests/projects/navpath carve.rules) and one
                // made in LateUpdate the frame after (tests/projects/navmesh).
                PlayerLoopDriver.RegisterFirst(typeof(UnityEngine.PlayerLoop.PreUpdate.AIUpdate), ApplyCarving);
                PlayerLoopDriver.Register(typeof(UnityEngine.PlayerLoop.PreLateUpdate.AIUpdatePostScript), DecideCarving);
            }
            if (!s_Obstacles.Contains(obstacle)) s_Obstacles.Add(obstacle);
        }

        internal static void Remove(NavMeshObstacle obstacle) => s_Obstacles.Remove(obstacle);

        static bool s_Registered;

        // ------------------------------------------------------------------ carving

        /// <summary>A carving obstacle's hole: its transform when last counted as moving, whether
        /// it is moving (stationary-only carving) and how long it has stood still since, the hole
        /// in the NavMesh and the one decided for the next update.</summary>
        sealed class Carve
        {
            internal Vector3 Position;
            internal Quaternion Rotation;
            internal Vector3 Scale;
            /// <summary>The squared length of the obstacle's extents in the world then.</summary>
            internal float ExtentSq;
            internal bool Moving;
            internal float Still;
            internal NavMeshCarving.Footprint Applied;
            internal bool Pending;
            internal NavMeshCarving.Footprint Next;
            /// <summary>The hole as it will be once the pending change is taken in.</summary>
            internal NavMeshCarving.Footprint Target => Pending ? Next : Applied;

            internal void Snapshot(NavMeshObstacle obstacle)
            {
                var t = obstacle.transform;
                Position = t.position;
                Rotation = t.rotation;
                Scale = t.lossyScale;
                ExtentSq = WorldExtents(obstacle, Scale).sqrMagnitude;
            }

            /// <summary>Moved since the snapshot by more than <paramref name="threshold"/>: its
            /// position (in 3D), its turn (the angle in radians times the length of its scale and
            /// of its extents) or its scale (the change times the length of its extents).
            /// Matches the Unity 6000.0.71f1 Editor, tests/projects/navpath carve.rules (a 5.7
            /// degree turn and an 8 % scale of a 1 m box count as moving at threshold 0.1).</summary>
            internal bool HasMoved(Transform t, float threshold)
            {
                float sq = threshold * threshold;
                if ((t.position - Position).sqrMagnitude > sq) return true;
                var rotation = t.rotation;
                if (rotation != Rotation)
                {
                    float dot = Mathf.Min(Mathf.Abs(Quaternion.Dot(Rotation, rotation)), 1f);
                    float angle = Mathf.Acos(dot) * 2f;
                    if (sq < angle * angle * Scale.sqrMagnitude * ExtentSq) return true;
                }
                return sq < (t.lossyScale - Scale).sqrMagnitude * ExtentSq;
            }

            internal void Schedule(NavMeshCarving.Footprint hole)
            {
                Pending = true;
                Next = hole;
            }
        }

        static readonly Dictionary<NavMeshObstacle, Carve> s_Carves = new Dictionary<NavMeshObstacle, Carve>(ReferenceEqualityComparer.Instance);
        static readonly List<NavMeshCarving.Footprint> s_Holes = new List<NavMeshCarving.Footprint>();
        static readonly List<NavMeshObstacle> s_Gone = new List<NavMeshObstacle>();
        static readonly Comparison<NavMeshCarving.Footprint> s_HoleOrder = (a, b) =>
            a.Center.x != b.Center.x ? a.Center.x.CompareTo(b.Center.x)
            : a.Center.z != b.Center.z ? a.Center.z.CompareTo(b.Center.z)
            : a.Center.y != b.Center.y ? a.Center.y.CompareTo(b.Center.y)
            : b.ExtentSq.CompareTo(a.ExtentSq);

        /// <summary>The holes added or removed by this carving update (reused).</summary>
        static readonly List<NavMeshCarving.Footprint> s_Changed = new List<NavMeshCarving.Footprint>();

        /// <summary>The holes in the NavMesh now.</summary>
        internal static IReadOnlyList<NavMeshCarving.Footprint> Holes => s_Holes;

        static bool Carving(NavMeshObstacle obstacle) => obstacle != null && obstacle.carving && obstacle.isActiveAndEnabled;

        /// <summary>Forgets every hole at once (tests between cases).</summary>
        internal static void ResetCarving()
        {
            s_Carves.Clear();
            if (s_Holes.Count == 0) return;
            s_Holes.Clear();
            NavWorld.MarkDirty();
        }

        /// <summary>The obstacle's hole is in the NavMesh.</summary>
        internal static bool IsCarved(NavMeshObstacle obstacle) => s_Carves.TryGetValue(obstacle, out var c) && c.Applied != null;

        /// <summary>Both halves of the carving update in a row (tests): the holes decided before
        /// are cut, then each carving obstacle's hole is decided for the next cut.</summary>
        // Documented (AI Navigation 2.0 Manual, NavMesh Obstacle; Scripting API 6000.0
        // NavMeshObstacle): with carveOnlyStationary (the default) the hole is removed when the
        // obstacle moves (more than carvingMoveThreshold) and comes back once it has stood still for
        // more than carvingTimeToStationary; without it the hole moves whenever the obstacle has
        // moved more than carvingMoveThreshold; the result is available in the next frame.
        // Measured (tests/projects/navmesh, agents.f*.holes, 0.02 s frames): an obstacle added or a
        // change made in LateUpdate of frame N shows in the NavMesh from Update of frame N + 2, and
        // agents replan in that frame; a new obstacle is carved at once (no wait to be stationary);
        // a move of 0.05 m (threshold 0.1) leaves the hole; after a move of 1.25 m in frame 180 the
        // hole is gone from frame 182 and back at the new place from frame 208 (0.5 s: carved once
        // more than that has passed); turning carving off removes the hole in the same two frames.
        // "Moved" compares the obstacle's transform with where it was last counted as moving
        // (Carve.HasMoved; measured: tests/projects/navpath carve.rules); the time standing still
        // adds up the frames' Time.deltaTime.
        internal static void UpdateCarving()
        {
            ApplyCarving();
            DecideCarving();
        }

        /// <summary>The holes decided by the last carving decision are cut (PreUpdate's AIUpdate).</summary>
        internal static void ApplyCarving()
        {
            if (s_Carves.Count == 0 && s_Obstacles.Count == 0) return;
            s_Changed.Clear();
            s_Gone.Clear();
            foreach (var pair in s_Carves)
            {
                var c = pair.Value;
                if (c.Pending)
                {
                    if (!ReferenceEquals(c.Applied, c.Next))
                    {
                        if (c.Applied != null) s_Changed.Add(c.Applied);
                        if (c.Next != null) s_Changed.Add(c.Next);
                    }
                    c.Applied = c.Next;
                    c.Pending = false;
                }
                if (c.Applied == null && !Carving(pair.Key)) s_Gone.Add(pair.Key);
            }
            foreach (var gone in s_Gone) s_Carves.Remove(gone);
            if (s_Changed.Count > 0)
            {
                s_Holes.Clear();
                foreach (var c in s_Carves.Values)
                    if (c.Applied != null) s_Holes.Add(c.Applied);
                // Cut in order of their centres' x, then z, then y, the larger first at the same
                // place (Unity's order: holes overlapping in one polygon split it accordingly).
                s_Holes.Sort(s_HoleOrder);
                // Cut now, only the tiles the changed holes reach, so the agents replan in this
                // frame's update (measured).
                NavWorld.HolesChanged(s_Changed);
            }
        }

        /// <summary>Each carving obstacle's hole is decided for the next cut (PreLateUpdate's
        /// AIUpdatePostScript).</summary>
        internal static void DecideCarving()
        {
            if (s_Carves.Count == 0 && s_Obstacles.Count == 0) return;
            float dt = Time.deltaTime;
            foreach (var obstacle in s_Obstacles)
            {
                if (!Carving(obstacle)) continue;
                if (!s_Carves.TryGetValue(obstacle, out var c))
                {
                    c = new Carve();
                    c.Snapshot(obstacle);
                    s_Carves[obstacle] = c;
                    c.Schedule(FootprintOf(obstacle));
                    continue;
                }
                var t = obstacle.transform;
                float threshold = Mathf.Max(obstacle.carvingMoveThreshold, MinMoveThreshold);
                if (obstacle.carveOnlyStationary)
                {
                    if (!c.Moving)
                    {
                        if (c.HasMoved(t, threshold))
                        {
                            c.Moving = true;
                            c.Still = 0f;
                            c.Snapshot(obstacle);
                            if (c.Target != null) c.Schedule(null);
                        }
                        else if (c.Target == null) c.Schedule(FootprintOf(obstacle));
                    }
                    // Moving: still once it has moved less than a tenth of the threshold per update for
                    // more than carvingTimeToStationary.
                    else if (c.HasMoved(t, Mathf.Max(threshold * StillFraction, MinMoveThreshold)))
                    {
                        c.Still = 0f;
                        c.Snapshot(obstacle);
                    }
                    else
                    {
                        c.Still += dt;
                        if (c.Still > obstacle.carvingTimeToStationary)
                        {
                            c.Moving = false;
                            c.Schedule(FootprintOf(obstacle));
                        }
                    }
                }
                else if (c.HasMoved(t, threshold))
                {
                    c.Snapshot(obstacle);
                    c.Schedule(FootprintOf(obstacle));
                }
                else if (c.Target == null) c.Schedule(FootprintOf(obstacle));
            }
            foreach (var pair in s_Carves)
                if (!Carving(pair.Key) && pair.Value.Target != null) pair.Value.Schedule(null);
        }

        /// <summary>A capsule's hole: eight directions, at its radius over cos(pi / 8), and the cap ring
        /// at cos(pi / 4) of that.</summary>
        const int CapsuleDirections = 8;
        const float CapsuleOctagon = 1.0823922f;
        const float CapsuleCap = 0.70710677f;

        /// <summary>The smallest move threshold (moves below it never count).</summary>
        const float MinMoveThreshold = 1e-5f;

        /// <summary>While a stationary-only obstacle is moving, it stands still once it moves less
        /// than this part of its threshold.</summary>
        const float StillFraction = 0.1f;

        /// <summary>The obstacle's extents in the world for <paramref name="scale"/>: a box's half
        /// size, a capsule's radius across (the larger of x and z scale) and half its height.</summary>
        static Vector3 WorldExtents(NavMeshObstacle obstacle, Vector3 scale)
        {
            scale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            if (obstacle.shape == NavMeshObstacleShape.Capsule)
            {
                float r = obstacle.radius * Mathf.Max(scale.x, scale.z);
                return new Vector3(r, obstacle.height * 0.5f * scale.y, r);
            }
            return Vector3.Scale(obstacle.size * 0.5f, scale);
        }

        /// <summary>The obstacle's outline on the ground and its height span, where it stands now.</summary>
        internal static NavMeshCarving.Footprint FootprintOf(NavMeshObstacle obstacle)
        {
            var t = obstacle.transform;
            var points = new List<Vector2>();
            float bottom, top;
            if (obstacle.shape == NavMeshObstacleShape.Capsule)
            {
                // Eight directions round its axis at the radius over cos 22.5 degrees (the octagon
                // round its circle), at both ends of its straight part, a ring at 45 degrees up its
                // caps and the caps' tips; the hole is their outline seen from above (matches the
                // Unity 6000.0.71f1 Editor, tests/projects/navpath carve.rules capsules).
                var center = t.TransformPoint(obstacle.center);
                var extents = WorldExtents(obstacle, t.lossyScale);
                float radius = Mathf.Max(extents.x, extents.z);
                float straight = Mathf.Max(extents.y - radius, 0f);
                // The rotation's matrix columns in float.
                var axes = Matrix4x4.Rotate(t.rotation);
                Vector3 right = axes.GetColumn(0), up = axes.GetColumn(1), forward = axes.GetColumn(2);
                float ring = radius * CapsuleOctagon;
                float cap = CapsuleCap * radius * CapsuleOctagon;
                bottom = float.MaxValue;
                top = float.MinValue;
                void Add(Vector3 p)
                {
                    points.Add(new Vector2(p.x, p.z));
                    bottom = Mathf.Min(bottom, p.y);
                    top = Mathf.Max(top, p.y);
                }
                for (int i = 0; i < CapsuleDirections; i++)
                {
                    // Summed in this order, the points round as the Editor's (ties such as the turned
                    // octagon's sides decide how the polygons round it are split; carve.rules).
                    float a = i * 2f * MathF.PI / CapsuleDirections;
                    Vector3 across = right * MathF.Cos(a), along = forward * MathF.Sin(a);
                    var atRing = along * ring + across * ring + center;
                    var atCap = along * cap + across * cap + center;
                    Add(atRing - up * straight);
                    Add(atRing + up * straight);
                    Add(atCap - up * (cap + straight));
                    Add(atCap + up * (cap + straight));
                }
                Add(center - up * (ring + straight));
                Add(center + up * (ring + straight));
            }
            else
            {
                bottom = float.MaxValue;
                top = float.MinValue;
                var half = obstacle.size * 0.5f;
                for (int i = 0; i < 8; i++)
                {
                    var corner = t.TransformPoint(obstacle.center + new Vector3((i & 1) != 0 ? half.x : -half.x,
                        (i & 2) != 0 ? half.y : -half.y, (i & 4) != 0 ? half.z : -half.z));
                    points.Add(new Vector2(corner.x, corner.z));
                    bottom = Mathf.Min(bottom, corner.y);
                    top = Mathf.Max(top, corner.y);
                }
            }
            return new NavMeshCarving.Footprint
            {
                Outline = points.ToArray(), Bottom = bottom, Top = top,
                Center = t.TransformPoint(obstacle.center), ExtentSq = WorldExtents(obstacle, t.lossyScale).sqrMagnitude,
            };
        }

        // ------------------------------------------------------------------ avoidance

        /// <summary>The most circles a box becomes (C5).</summary>
        internal const int MaxBoxCircles = 16;

        /// <summary>The most neighbour candidates the crowd gathers for an agent before it keeps the
        /// soonest reachable (DtCrowd.GetNeighbours; C7).</summary>
        internal const int NeighbourCandidates = DtCrowd.NeighbourCandidates;

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

            internal float Height => Mathf.Max(0f, Top - Bottom);
        }

        /// <summary>The obstacle circles in each crowd, reused from update to update.</summary>
        static readonly ConditionalWeakTable<DtCrowd, List<DtCrowdAgent>> s_InCrowd = new ConditionalWeakTable<DtCrowd, List<DtCrowdAgent>>();
        static readonly List<Circle> s_Circles = new List<Circle>();
        /// <summary>The circles fed this update (near a walking agent) and their crowd positions.</summary>
        static readonly List<Circle> s_Near = new List<Circle>();
        static readonly List<RcVec3f> s_NearPositions = new List<RcVec3f>();

        /// <summary>Before <paramref name="crowd"/> updates: the enabled obstacles,
        /// where they are now, become its obstacle circles, those near a walking agent (C7). Without
        /// obstacles (and none fed before) the crowd is left untouched.</summary>
        internal static void Feed(DtCrowd crowd)
        {
            if (!s_InCrowd.TryGetValue(crowd, out var members))
            {
                if (s_Obstacles.Count == 0) return;
                members = new List<DtCrowdAgent>();
                s_InCrowd.Add(crowd, members);
            }
            s_Circles.Clear();
            foreach (var obstacle in s_Obstacles)
                if (obstacle != null && obstacle.isActiveAndEnabled && !IsCarved(obstacle)) AddCircles(obstacle, s_Circles);
            // The circle stands on the obstacle's bottom, as tall as it (C6).
            var agents = crowd.GetActiveAgents();
            float cell = LargestGridCell(agents, s_Circles);
            s_Near.Clear();
            s_NearPositions.Clear();
            foreach (var c in s_Circles)
            {
                var position = new RcVec3f(c.Center.x, c.Bottom, c.Center.z);
                if (!NearAWalkingAgent(agents, position, c, cell)) continue;
                s_Near.Add(c);
                s_NearPositions.Add(position);
            }
            while (members.Count > s_Near.Count)
            {
                crowd.RemoveAgent(members[members.Count - 1]);
                members.RemoveAt(members.Count - 1);
            }
            if (s_Near.Count == 0) return;
            if (!s_WarnedCandidates) CheckCandidates(crowd, agents, members.Count);
            for (int i = 0; i < s_Near.Count; i++)
            {
                var c = s_Near[i];
                var position = s_NearPositions[i];
                var options = new DtCrowdAgentParams
                {
                    radius = c.Radius,
                    height = c.Height,
                    maxAcceleration = 0f,
                    maxSpeed = 0f,
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

        /// <summary>Whether the circle, placed at <paramref name="position"/>, can be the neighbour of
        /// a walking agent of the crowd: its extent meets the square of the agent's neighbour reach
        /// grown by a grid cell (the crowd gathers whole cells),
        /// with the crowd's height test passing (C6, C7).</summary>
        static bool NearAWalkingAgent(IList<DtCrowdAgent> agents, RcVec3f position, Circle circle, float cell)
        {
            float height = circle.Height;
            float horizon = NavMesh.avoidancePredictionTime;
            for (int i = 0; i < agents.Count; i++)
            {
                var ag = agents[i];
                // Obstacle circles are never walking; agents on links or off the NavMesh do not look
                // for neighbours.
                if (ag.state != DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING) continue;
                if (!DtCrowd.HeightsOverlap(ag, position, height)) continue;
                float reach = DtCrowd.NeighbourReach(ag, horizon) + circle.Radius + cell;
                if (MathF.Abs(ag.npos.X - position.X) <= reach && MathF.Abs(ag.npos.Z - position.Z) <= reach) return true;
            }
            return false;
        }

        /// <summary>The widest grid cell the crowd can size this update (it takes the mean of what
        /// these give, see DtCrowd.BuildProximityGrid).</summary>
        static float LargestGridCell(IList<DtCrowdAgent> agents, List<Circle> circles)
        {
            float horizon = NavMesh.avoidancePredictionTime, cell = DtCrowd.MinGridCell;
            for (int i = 0; i < agents.Count; i++)
            {
                var ag = agents[i];
                cell = MathF.Max(cell, 2f * ag.option.radius + horizon * MathF.Max(MathF.Abs(ag.vel.X), MathF.Abs(ag.vel.Z)));
            }
            foreach (var c in circles)
                cell = MathF.Max(cell, 2f * c.Radius + horizon * Mathf.Max(Mathf.Abs(c.Velocity.x), Mathf.Abs(c.Velocity.z)));
            return cell;
        }

        /// <summary>Reports once when a walking agent's neighbour query will gather more candidates
        /// than the crowd keeps, obstacle circles among them: the proximity-grid items (agents and
        /// the circles fed now) in the grid cells its range touches, as the crowd counts them (C7).
        /// Positions are those before the crowd updates.</summary>
        static void CheckCandidates(DtCrowd crowd, IList<DtCrowdAgent> agents, int fedBefore)
        {
            if (agents.Count - fedBefore + s_Near.Count <= NeighbourCandidates) return;
            // The cells the crowd last sized (the grid holds positions).
            float invX = 1.0f / crowd.GetGrid().GetCellSizeX(), invZ = 1.0f / crowd.GetGrid().GetCellSizeY();
            for (int i = 0; i < agents.Count; i++)
            {
                var ag = agents[i];
                if (ag.state != DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING) continue;
                float range = DtCrowd.NeighbourReach(ag, NavMesh.avoidancePredictionTime);
                int x0 = Cell(ag.npos.X - range, invX), x1 = Cell(ag.npos.X + range, invX);
                int z0 = Cell(ag.npos.Z - range, invZ), z1 = Cell(ag.npos.Z + range, invZ);
                int candidates = 0, circles = 0;
                for (int j = 0; j < agents.Count; j++)
                {
                    var other = agents[j];
                    // Last update's circles are replaced by this update's below.
                    if (other.option.userData is NavMeshObstacle) continue;
                    if (InCells(other.npos.X, other.npos.Z, x0, x1, z0, z1, invX, invZ)) candidates++;
                }
                for (int j = 0; j < s_Near.Count; j++)
                    if (InCells(s_NearPositions[j].X, s_NearPositions[j].Z, x0, x1, z0, z1, invX, invZ)) circles++;
                if (circles == 0 || candidates + circles <= NeighbourCandidates) continue;
                s_WarnedCandidates = true;
                Debug.LogWarning("Unbound: " + (candidates + circles) + " NavMesh agents and NavMeshObstacle circles ("
                    + circles + " circles) are near one agent, more than the " + NeighbourCandidates
                    + " neighbours its avoidance considers; some may be left out of its avoidance and collisions.",
                    ag.option.userData as UnityEngine.Object);
                return;
            }
        }

        /// <summary>The proximity grid's cell of a coordinate, as the crowd's grid computes it.</summary>
        static int Cell(float v, float inv) => (int)MathF.Floor(v * inv);

        static bool InCells(float x, float z, int x0, int x1, int z0, int z1, float invX, float invZ)
        {
            int cx = Cell(x, invX), cz = Cell(z, invZ);
            return cx >= x0 && cx <= x1 && cz >= z0 && cz <= z1;
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
            // Half the long and short sides.
            float length = Mathf.Max(hu, hv), width = Mathf.Min(hu, hv);
            // Pieces no longer than wide, so each circle's radius is at most sqrt(2) half-widths
            // (C5); beyond MaxBoxCircles the pieces lengthen.
            bool beyondCap;
            int count;
            if (width > 0f)
            {
                float pieces = length / width - 1e-4f;
                beyondCap = pieces > MaxBoxCircles;
                count = beyondCap ? MaxBoxCircles : Mathf.Max(1, Mathf.CeilToInt(pieces));
            }
            else
            {
                beyondCap = length > 0f;
                count = beyondCap ? MaxBoxCircles : 1;
            }
            if (beyondCap && !s_WarnedLongBox)
            {
                s_WarnedLongBox = true;
                Debug.LogWarning("Unbound: NavMeshObstacle '" + obstacle.name + "' is a box more than " + MaxBoxCircles
                    + " times as long as it is wide; agents avoid it as " + MaxBoxCircles
                    + " circles that reach beyond its long sides further the longer it is.", obstacle);
            }
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
