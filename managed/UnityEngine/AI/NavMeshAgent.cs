using System;
using System.Collections.Generic;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Detour.Crowd;
using Unbound.Runtime;

namespace UnityEngine.AI
{
    /// <summary>
    /// An agent moving over the NavMesh, driven by a Detour crowd as Unity's agents are. Before the
    /// scripts' Update (PreUpdate's AIUpdate) the pending path requests are found, then the crowd
    /// steers from where the agents stand (to the next corners, avoiding other agents) and moves
    /// them by the velocity planned the update before (acceleration-limited), so the update that
    /// finds a path plans with it but moves only from the next; transforms follow (measured in
    /// `tests/projects/navmesh`, `navcost` loop.*).
    /// </summary>
    [ClassId(195)]
    public sealed partial class NavMeshAgent : Behaviour
    {
        int m_AgentTypeID;
        float m_Radius = 0.5f;
        float m_Height = 2f;
        float m_BaseOffset;
        float m_Speed = 3.5f;
        float m_AngularSpeed = 120f;
        float m_Acceleration = 8f;
        float m_StoppingDistance;
        bool m_AutoTraverseOffMeshLink = true;
        bool m_AutoBraking = true;
        bool m_AutoRepath = true;
        int m_AreaMask = -1;
        ObstacleAvoidanceType m_ObstacleAvoidanceType = ObstacleAvoidanceType.HighQualityObstacleAvoidance;
        int m_AvoidancePriority = 50;
        bool m_UpdatePosition = true;
        bool m_UpdateRotation = true;
        bool m_UpdateUpAxis = true;
        bool m_IsStopped;
        float[] m_AreaCosts;

        // Runtime state.
        internal DtCrowdAgent m_Crowd;
        internal NavWorld.Surface m_Surface;
        internal Vector3 m_NextPosition;
        /// <summary>The destination requested, mapped onto the NavMesh (what paths are searched to).</summary>
        Vector3 m_Destination = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        /// <summary>The destination reported: the mapped one, then the end of the path found for it.</summary>
        Vector3 m_ReportedDestination;
        bool m_HasDestination;
        bool m_PathPending;
        NavMeshPath m_RequestedPath;
        // A new agent reports a complete path, also while its first path is pending (measured).
        NavMeshPathStatus m_PathStatus = NavMeshPathStatus.PathComplete;
        Vector3 m_WrittenPosition;
        bool m_OnNavMesh;
        int m_WorldVersion = -1;
        internal int m_LinkId;
        /// <summary>CompleteOffMeshLink was called: the agent stands at the link's end and leaves
        /// the link with the next crowd update.</summary>
        internal bool m_LinkCompleting;
        /// <summary>ResetPath took the agent off its link: it lands on the link's landing side at the
        /// end of the next agent update (whatever path it is given meanwhile).</summary>
        internal bool m_LandAfterReset;
        /// <summary>The agent was walking when the crowd update began (a link it reaches is new).</summary>
        internal bool m_StepWalking;

        public NavMeshAgent() { }

        // ------------------------------------------------------------------ settings

        public int agentTypeID { get => m_AgentTypeID; set { m_AgentTypeID = value; AgentSystem.Reattach(this); } }
        public float radius { get => m_Radius; set { m_Radius = value; ApplyParams(); } }
        public float height { get => m_Height; set { m_Height = value; ApplyParams(); } }
        /// <summary>Setting it moves the transform to the new height at once (measured right after
        /// AddComponent; for an agent whose transform waits for its early update, unmeasured).</summary>
        public float baseOffset
        {
            get => m_BaseOffset;
            set
            {
                m_BaseOffset = value;
                if (m_Crowd != null && m_UpdatePosition) AgentSystem.WriteTransform(this);
            }
        }
        public float speed { get => m_Speed; set { m_Speed = Mathf.Max(0f, value); ApplyParams(); } }
        public float angularSpeed { get => m_AngularSpeed; set => m_AngularSpeed = value; }
        public float acceleration { get => m_Acceleration; set { m_Acceleration = Mathf.Max(0f, value); ApplyParams(); } }
        public float stoppingDistance { get => m_StoppingDistance; set => m_StoppingDistance = Mathf.Max(0f, value); }
        public bool autoTraverseOffMeshLink { get => m_AutoTraverseOffMeshLink; set => m_AutoTraverseOffMeshLink = value; }
        public bool autoBraking { get => m_AutoBraking; set => m_AutoBraking = value; }
        public bool autoRepath { get => m_AutoRepath; set => m_AutoRepath = value; }
        /// <summary>Changing it makes the path stale (Scripting API).</summary>
        public int areaMask
        {
            get => m_AreaMask;
            set
            {
                if (m_AreaMask != value) m_OwnPathStale = true;
                m_AreaMask = value;
            }
        }

        [Obsolete("Use areaMask instead.")]
        public int walkableMask { get => areaMask; set => areaMask = value; }

        public ObstacleAvoidanceType obstacleAvoidanceType { get => m_ObstacleAvoidanceType; set { m_ObstacleAvoidanceType = value; ApplyParams(); } }
        public int avoidancePriority { get => m_AvoidancePriority; set { m_AvoidancePriority = value; ApplyParams(); } }
        /// <summary>Turned back on, the transform moves to nextPosition at once (documented,
        /// NavMeshAgent.nextPosition, Scripting API 6000.0).</summary>
        public bool updatePosition
        {
            get => m_UpdatePosition;
            set
            {
                bool turnedOn = value && !m_UpdatePosition;
                m_UpdatePosition = value;
                if (!turnedOn) return;
                AgentSystem.Sync(this);
                if (m_Crowd != null) AgentSystem.WriteTransform(this);
            }
        }
        public bool updateRotation { get => m_UpdateRotation; set => m_UpdateRotation = value; }
        public bool updateUpAxis { get => m_UpdateUpAxis; set => m_UpdateUpAxis = value; }

        public bool isStopped
        {
            get => m_IsStopped;
            set
            {
                // A stopped agent slows down at its acceleration from its next update (measured:
                // tests/projects/navcost loop.*).
                m_IsStopped = value;
            }
        }

        // Area costs. Documented (Scripting API 6000.0): NavMesh.SetAreaCost sets an area's cost "on
        // all agents", replacing any custom area costs on all agents, and is the default for agents
        // created later; enabling or disabling an agent resets its costs to the defaults
        // (NavMeshAgent.SetAreaCost). Unbound's choices (unmeasured): NavMesh.SetAreaCost replaces
        // the custom cost of that one area (an agent's custom costs for other areas stay), on every
        // agent with custom costs, enabled or not, and makes the path of an agent whose cost it
        // changes stale (as the agent's own SetAreaCost does); the reset happens at once when the
        // agent is enabled and when it is disabled, before it is placed, and does not by itself
        // make the path stale.

        /// <summary>The agents with their own area costs (<see cref="m_AreaCosts"/> set).</summary>
        static readonly List<NavMeshAgent> s_WithOwnAreaCosts = new List<NavMeshAgent>();

        public void SetAreaCost(int areaIndex, float areaCost)
        {
            if (m_AreaCosts == null)
            {
                NavMeshAreas.Ensure();
                m_AreaCosts = (float[])NavMeshAreas.Costs.Clone();
                s_WithOwnAreaCosts.Add(this);
            }
            if (areaIndex < 0 || areaIndex >= 32) return;
            // The agent's own area costs changed: its path is stale (unmeasured).
            if (m_AreaCosts[areaIndex] != areaCost) m_OwnPathStale = true;
            m_AreaCosts[areaIndex] = areaCost;
        }

        public float GetAreaCost(int areaIndex)
        {
            NavMeshAreas.Ensure();
            var costs = m_AreaCosts ?? NavMeshAreas.Costs;
            return areaIndex >= 0 && areaIndex < 32 ? costs[areaIndex] : 0f;
        }

        [Obsolete("Use SetAreaCost instead.")]
        public void SetLayerCost(int layer, float cost) => SetAreaCost(layer, cost);

        [Obsolete("Use GetAreaCost instead.")]
        public float GetLayerCost(int layer) => GetAreaCost(layer);

        /// <summary>NavMesh.SetAreaCost: the area's cost replaces every agent's own cost for it.</summary>
        internal static void ReplaceAreaCost(int areaIndex, float cost)
        {
            for (int i = s_WithOwnAreaCosts.Count - 1; i >= 0; i--)
            {
                var agent = s_WithOwnAreaCosts[i];
                if (agent.m_Destroyed || agent.m_AreaCosts == null)
                {
                    s_WithOwnAreaCosts.RemoveAt(i);
                    continue;
                }
                if (agent.m_AreaCosts[areaIndex] != cost) agent.m_OwnPathStale = true;
                agent.m_AreaCosts[areaIndex] = cost;
            }
        }

        /// <summary>Enabled or disabled: the agent's area costs are the defaults again.</summary>
        void ResetAreaCosts()
        {
            if (m_AreaCosts == null) return;
            m_AreaCosts = null;
            s_WithOwnAreaCosts.Remove(this);
        }

        internal AreaFilter Filter => AreaFilter.Of(m_AreaMask, m_AreaCosts);

        void ApplyParams()
        {
            if (m_Crowd != null) AgentSystem.CrowdOf(m_Surface)?.UpdateAgentParameters(m_Crowd, AgentSystem.Params(this));
        }

        // ------------------------------------------------------------------ state

        public bool isOnNavMesh
        {
            get
            {
                AgentSystem.Sync(this);
                return m_OnNavMesh;
            }
        }

        /// <summary>An agent not on a crowd reports its transform (measured for an agent on an
        /// inactive GameObject; an enabled agent off the NavMesh, unmeasured, reports it too).</summary>
        public Vector3 nextPosition
        {
            get
            {
                AgentSystem.Sync(this);
                return m_Crowd != null ? m_NextPosition + Vector3.up * m_BaseOffset : transform.position;
            }
            set
            {
                AgentSystem.Sync(this);
                // The transform follows at once (measured: tests/projects/navcost loop.c*, loop.d*).
                if (MoveAlongSurface(value) && m_UpdatePosition) AgentSystem.WriteTransform(this);
            }
        }

        /// <summary>The agent moves along the NavMesh surface towards the position (its transform
        /// stays); false when it is not on the NavMesh or cannot move.</summary>
        internal bool MoveAlongSurface(Vector3 position)
        {
            if (m_Crowd == null) return false;
            var target = position - Vector3.up * m_BaseOffset;
            if (!m_Crowd.corridor.MovePosition(NavMesh.V(target), m_Surface.Query, Filter)) return false;
            m_Crowd.npos = m_Crowd.corridor.GetPos();
            m_NextPosition = NavMesh.V(m_Crowd.npos);
            return true;
        }

        /// <summary>The velocity the agent reports: its last step over the frame's time, along the
        /// NavMesh as it slid (measured: an agent steering into an edge reports the slide, and on a
        /// slope the vertical speed), while its steering velocity carries on unconstrained.</summary>
        internal Vector3 m_Velocity;

        public Vector3 velocity
        {
            get => m_Crowd != null ? m_Velocity : Vector3.zero;
            set
            {
                if (m_Crowd == null) return;
                // The next update steps at this velocity; until then the agent reports its last
                // step and plan (measured: tests/projects/navcost loop.*).
                m_Crowd.vel = NavMesh.V(value);
                m_Crowd.pvel = m_Crowd.vel;
            }
        }

        public Vector3 desiredVelocity => m_Crowd != null ? NavMesh.V(m_Crowd.nvel) : Vector3.zero;

        /// <summary>On a link: the link's end (measured: tests/projects/navlinks, every frame on a
        /// link, crossing or waiting). Else the corner the agent last steered by: a path given
        /// between agent updates shows from the next update on (navlinks `incall`); without one, the
        /// end of its path.</summary>
        public Vector3 steeringTarget =>
            isOnOffMeshLink ? CurrentLinkEnd()
            : m_Crowd == null ? m_NextPosition
            : m_Crowd.hasSteer ? NavMesh.V(m_Crowd.steerPos)
            : FollowingPath ? NavMesh.V(m_Crowd.corridor.GetTarget()) : m_NextPosition;

        /// <summary>The destination as it lies on the NavMesh (the point the target was mapped to),
        /// then the end of the path found for it, partial ones included (measured:
        /// tests/projects/navlinks offedge, island, targets).</summary>
        public Vector3 destination
        {
            get => m_HasDestination ? m_ReportedDestination : m_NextPosition;
            set => SetDestination(value);
        }

        /// <summary>The target is mapped onto the NavMesh at once, within the agent's
        /// destination search box (<see cref="NavMesh.DestinationExtents"/>), to the nearest point;
        /// a target that maps nowhere is refused (false) and changes nothing. The path is then given
        /// in the call when it needs no search (see <see cref="AgentSystem.PlanAtOnce"/>), else
        /// at the next agent update, while the agent goes on following and reporting its current
        /// path (measured: tests/projects/navlinks offedge, pendingold, targets).</summary>
        public bool SetDestination(Vector3 target)
        {
            AgentSystem.Sync(this);
            if (!m_OnNavMesh)
            {
                Debug.LogError("\"SetDestination\" can only be called on an active agent that has been placed on a NavMesh.", this);
                return false;
            }
            if (!NavMesh.Nearest(m_Surface, target, NavMesh.DestinationExtents(m_Radius), Filter, out _, out var mapped))
                return false;
            m_Destination = mapped;
            m_ReportedDestination = mapped;
            bool reported = hasPath, following = FollowingPath;
            m_HasDestination = true;
            m_PathPending = true;
            m_RequestedPath = null;
            AgentSystem.RequestPath(this);
            AgentSystem.PlanAtOnce(this, following);
            // A path given in the call to an agent that reported none is reported from the next update.
            if (!m_PathPending && !reported) m_PathFromCall = true;
            return true;
        }

        public bool SetPath(NavMeshPath path)
        {
            AgentSystem.Sync(this);
            if (path == null || path.status == NavMeshPathStatus.PathInvalid || path.m_Polygons.Length == 0 || m_Crowd == null)
            {
                ResetPath();
                return false;
            }
            m_RequestedPath = new NavMeshPath();
            path.CopyTo(m_RequestedPath);
            m_Destination = path.m_Corners.Length > 0 ? path.m_Corners[path.m_Corners.Length - 1] : m_NextPosition;
            m_ReportedDestination = m_Destination;
            m_HasDestination = true;
            AgentSystem.ApplyPath(this, m_RequestedPath);
            return true;
        }

        /// <summary>The path and destination are dropped (see ForgetPath). On a link the agent is off
        /// it at once and, at the end of the next agent update, on the landing side's NavMesh
        /// (measured: tests/projects/navlinks resetmid; see AgentSystem.LandOffLink).</summary>
        public void ResetPath()
        {
            ForgetPath();
            if (m_Crowd != null)
            {
                if (isOnOffMeshLink)
                {
                    m_Crowd.animation.active = false;
                    m_LinkId = 0;
                    m_LinkCompleting = true;
                    m_LandAfterReset = true;
                }
                AgentSystem.CrowdOf(m_Surface)?.ResetMoveTarget(m_Crowd);
                m_Crowd.corridor.Reset(m_Crowd.corridor.GetFirstPoly(), m_Crowd.npos);
                m_Crowd.ncorners = 0;
                m_Crowd.hasSteer = false;
            }
        }

        /// <summary>A path is being followed: also the current one while a new one is pending
        /// (measured: tests/projects/navlinks pendingold, offedge). A path given in the call to a
        /// standing agent counts from the next agent update (measured: targets, incall).</summary>
        public bool hasPath => FollowingPath && !m_PathFromCall;

        /// <summary>A path given in SetDestination to an agent without one: not reported by hasPath
        /// until the next agent update.</summary>
        internal bool m_PathFromCall;

        /// <summary>The agent follows a path (as hasPath, also one given in the call).</summary>
        internal bool FollowingPath => m_Crowd != null && m_HasDestination && !m_Arrived
            && m_Crowd.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_VALID;

        bool m_Arrived;
        internal void SetArrived(bool arrived) => m_Arrived = arrived;

        public bool pathPending => m_PathPending;

        // Path staleness. Documented (Scripting API 6000.0, isPathStale and areaMask): changing the
        // areaMask, enabling or disabling an off-mesh link, or changing the NavMesh area costs
        // makes the path stale; the agent goes on following it. Measured (Unity 6000.0.71f1,
        // tests/projects/navlinks, every lane): a link activated or deactivated anywhere makes
        // every agent's path stale at once; an agent with a destination stays stale (also while
        // the NavMesh change replans it) until a new path is applied for a new destination; one
        // without a destination (never given, ResetPath, Warp) is not stale from its next agent
        // update; the agent's own areaMask and area costs make its path stale until its next
        // destination. Unbound's choices (unmeasured): area cost changes count as link changes;
        // only a changed value counts; NavMesh data added or removed does not. Open (listed): links
        // added mark only paths over the tiles round them, partial paths are replanned (and not
        // stale) about 0.3 s after the change and at once after a link activation.
        bool m_OwnPathStale;
        int m_PathStaleVersion;

        public bool isPathStale =>
            m_Crowd != null && (m_OwnPathStale || m_PathStaleVersion != NavWorld.PathStaleVersion);

        /// <summary>A new path was applied: it is not stale.</summary>
        internal void PathRenewed() => CatchUpStaleness();

        /// <summary>The changes so far are no longer this agent's stale path.</summary>
        internal void CatchUpStaleness()
        {
            m_OwnPathStale = false;
            m_PathStaleVersion = NavWorld.PathStaleVersion;
        }

        /// <summary>Whether the agent's path is stale (its own change, the NavMesh's version), to
        /// keep it over a replan that is not a new path (<see cref="RestoreStaleness"/>).</summary>
        internal (bool Own, int Version) Staleness => (m_OwnPathStale, m_PathStaleVersion);

        internal void RestoreStaleness((bool Own, int Version) staleness) => (m_OwnPathStale, m_PathStaleVersion) = staleness;

        public NavMeshPathStatus pathStatus => m_PathStatus;

        /// <summary>The end of the path being followed, also one given in the call or still
        /// followed while a new one is pending (measured).</summary>
        public Vector3 pathEndPosition => FollowingPath ? NavMesh.V(m_Crowd.corridor.GetTarget()) : m_NextPosition;

        /// <summary>The distance left along the corners the agent sees; unknown (infinity) while
        /// the path's end is beyond them (measured).</summary>
        public float remainingDistance
        {
            get
            {
                // While a new path is pending, the current one's (measured).
                if (m_Crowd == null || m_Crowd.targetState != DtMoveRequestState.DT_CROWDAGENT_TARGET_VALID) return 0f;
                // On a link the path's end is beyond the corners the agent sees (measured:
                // tests/projects/navlinks, every frame on a link).
                if (isOnOffMeshLink) return float.PositiveInfinity;
                if (m_Crowd.ncorners == 0) return 0f;
                if ((m_Crowd.corners[m_Crowd.ncorners - 1].flags & DtStraightPathFlags.DT_STRAIGHTPATH_END) == 0) return float.PositiveInfinity;
                float d = 0f;
                var p = m_Crowd.npos;
                for (int i = 0; i < m_Crowd.ncorners; i++)
                {
                    d += RcVec3f.Distance(p, m_Crowd.corners[i].pos);
                    p = m_Crowd.corners[i].pos;
                }
                return d;
            }
        }

        public NavMeshPath path
        {
            get
            {
                var p = new NavMeshPath();
                CopyPathTo(p);
                return p;
            }
            set => SetPath(value);
        }

        internal void CopyPathTo(NavMeshPath result)
        {
            result.ClearCorners();
            if (m_Crowd == null) return;
            result.m_Status = m_PathStatus;
            if (m_Crowd.targetState != DtMoveRequestState.DT_CROWDAGENT_TARGET_VALID)
            {
                // No path to follow (a pending one keeps reporting the current path): the path is
                // where the agent stands.
                result.m_Corners = new[] { m_NextPosition };
                return;
            }
            var corridor = m_Crowd.corridor.GetPath();
            int count = m_Crowd.corridor.GetPathCount();
            var straight = new DtStraightPath[256];
            m_Surface.Query.FindStraightPath(m_Crowd.npos, m_Crowd.corridor.GetTarget(), corridor, count, straight, out int n, straight.Length, 0);
            var corners = new Vector3[n];
            for (int i = 0; i < n; i++) corners[i] = NavMesh.V(straight[i].pos);
            // On a link the path goes on from the link's own end (measured: tests/projects/navlinks).
            if (n > 0 && isOnOffMeshLink) corners[0] = CurrentLinkEnd();
            result.m_Corners = corners;
            result.m_Polygons = corridor.Slice(0, count).ToArray();
            result.m_Status = m_PathStatus;
        }

        /// <summary>The owner of the NavMeshDataInstance whose polygon the agent stands on, and null
        /// when that instance has no owner or the agent is on no NavMesh (Scripting API 6000.0).
        /// On an off-mesh link: the link instance's owner (the page names NavMesh and link
        /// instance owners; reporting the link's while on it is Unbound's choice, unmeasured).</summary>
        public Object navMeshOwner
        {
            get
            {
                AgentSystem.Sync(this);
                if (m_Crowd == null || !m_OnNavMesh) return null;
                if (isOnOffMeshLink) return m_LinkId != 0 ? NavMesh.InternalGetLinkOwner(m_LinkId) : null;
                int data = NavWorld.DataOfPoly(m_Surface, m_Crowd.corridor.GetFirstPoly());
                return data != 0 ? NavMesh.InternalGetOwner(data) : null;
            }
        }

        // ------------------------------------------------------------------ queries

        /// <summary>The transform goes to the position given; the agent stands on the NavMesh below
        /// it, where the transform follows at its next early update, with no path or destination
        /// (measured: tests/projects/navlinks warpauto, warpwait; navcost loop.*).</summary>
        public bool Warp(Vector3 newPosition)
        {
            transform.position = newPosition;
            // It stands at once (measured: tests/projects/navcost loop.*).
            m_Velocity = Vector3.zero;
            ForgetPath();
            return AgentSystem.Place(this, newPosition - Vector3.up * m_BaseOffset);
        }

        /// <summary>No destination, path or request any more; a path change before is no longer this
        /// agent's stale path. Without a path the agent reports a complete one, as a new agent does
        /// (measured: tests/projects/navlinks targets, after a partial path, ResetPath and Warp).</summary>
        void ForgetPath()
        {
            m_PathStatus = NavMeshPathStatus.PathComplete;
            m_PathFromCall = false;
            m_HasDestination = false;
            m_PathPending = false;
            CatchUpStaleness();
        }

        public void Move(Vector3 offset)
        {
            AgentSystem.Sync(this);
            if (m_Crowd == null) return;
            MoveAlongSurface(m_NextPosition + Vector3.up * m_BaseOffset + offset);
            if (m_UpdatePosition) AgentSystem.WriteTransform(this);
        }

        public bool FindClosestEdge(out NavMeshHit hit) => NavMesh.FindClosestEdge(m_NextPosition, out hit, Filter, m_AgentTypeID);

        public bool Raycast(Vector3 targetPosition, out NavMeshHit hit) => NavMesh.Raycast(m_NextPosition, targetPosition, out hit, Filter, m_AgentTypeID);

        public bool CalculatePath(Vector3 targetPosition, NavMeshPath path) =>
            NavMesh.CalculatePath(m_NextPosition, targetPosition, Filter, m_AgentTypeID, path);

        public bool SamplePathPosition(int areaMask, float maxDistance, out NavMeshHit hit)
        {
            hit = default;
            hit.position = m_NextPosition;
            if (m_Crowd == null) return false;
            var corridor = m_Crowd.corridor.GetPath();
            int count = m_Crowd.corridor.GetPathCount();
            var straight = new DtStraightPath[256];
            m_Surface.Query.FindStraightPath(m_Crowd.npos, m_Crowd.corridor.GetTarget(), corridor, count, straight, out int n, straight.Length, 0);
            float travelled = 0f;
            for (int i = 0; i + 1 < n; i++)
            {
                var a = NavMesh.V(straight[i].pos);
                var b = NavMesh.V(straight[i + 1].pos);
                m_Surface.Mesh.GetPolyArea(straight[i].refs, out int area);
                float len = Vector3.Distance(a, b);
                if ((areaMask & (1 << area)) == 0)
                {
                    hit.position = a;
                    hit.distance = travelled;
                    hit.mask = 1 << area;
                    hit.hit = true;
                    return true;
                }
                if (travelled + len >= maxDistance)
                {
                    hit.position = Vector3.Lerp(a, b, (maxDistance - travelled) / Mathf.Max(len, 1e-6f));
                    hit.distance = maxDistance;
                    hit.mask = 1 << area;
                    return false;
                }
                travelled += len;
            }
            hit.position = n > 0 ? NavMesh.V(straight[n - 1].pos) : m_NextPosition;
            hit.distance = travelled;
            return false;
        }

        // ------------------------------------------------------------------ off-mesh links
        //
        // "Measured" for off-mesh links means observed in the Unity 6000.0.71f1 navlinks capture
        // (tests/projects/navlinks/Reference/unity-6000.0.71f1.jsonl, committed at 982cee91 and
        // not yet admitted), not documented: the Scripting API only says that without
        // autoTraverseOffMeshLink the agent does not cross by itself and that CompleteOffMeshLink
        // completes the movement on the current link.

        public bool isOnOffMeshLink => m_Crowd != null && m_Crowd.state == DtCrowdAgentState.DT_CROWDAGENT_STATE_OFFMESH && !m_LinkCompleting;

        /// <summary>On a link and not moving over it: with autoTraverseOffMeshLink off the agent
        /// waits where it reached the link until a script completes it.</summary>
        internal bool WaitingOnLink => isOnOffMeshLink && !m_Crowd.animation.active;

        /// <summary>Where the current link lands: the link's own end point, as the crossing set it
        /// (measured: the end as the link places it, not snapped onto the NavMesh as Detour's
        /// connection end is).</summary>
        Vector3 CurrentLinkEnd() => NavMesh.V(m_Crowd.animation.endPos);

        /// <summary>A script moving the transform of an agent that waits on a link moves the agent
        /// there freely (measured: nextPosition is the transform's position, off the NavMesh too).</summary>
        internal void FollowTransformOnLink()
        {
            m_NextPosition = transform.position - Vector3.up * m_BaseOffset;
            m_Crowd.npos = NavMesh.V(m_NextPosition);
        }

        public OffMeshLinkData currentOffMeshLinkData => GetCurrentOffMeshLinkDataInternal();

        public OffMeshLinkData nextOffMeshLinkData => GetNextOffMeshLinkDataInternal();

        internal OffMeshLinkData GetCurrentOffMeshLinkDataInternal()
        {
            var data = new OffMeshLinkData();
            if (!isOnOffMeshLink) return data;
            var anim = m_Crowd.animation;
            var link = NavWorld.FindLink(m_LinkId);
            data.m_Valid = 1;
            // A link deactivated while the agent is on it reports so (measured).
            data.m_Activated = link == null || link.Active ? 1 : 0;
            data.m_StartPos = NavMesh.V(anim.startPos);
            data.m_EndPos = CurrentLinkEnd();
            data.m_InstanceID = link?.Owner ?? 0;
            data.m_LinkType = link?.Type ?? OffMeshLinkType.LinkTypeManual;
            return data;
        }

        internal OffMeshLinkData GetNextOffMeshLinkDataInternal()
        {
            var data = new OffMeshLinkData();
            if (m_Crowd == null || m_Crowd.ncorners == 0) return data;
            var last = m_Crowd.corners[m_Crowd.ncorners - 1];
            if ((last.flags & DtStraightPathFlags.DT_STRAIGHTPATH_OFFMESH_CONNECTION) == 0) return data;
            data.m_Valid = 1;
            data.m_Activated = 1;
            data.m_StartPos = NavMesh.V(last.pos);
            if (m_Surface.LinkOfPoly.TryGetValue(last.refs, out int linkId) && NavWorld.FindLink(linkId) is NavWorld.LinkInstance link)
            {
                // The link's own end points, the way the path takes it (measured: tests/projects/
                // navlinks; the current link's start is where the crossing starts, on the NavMesh).
                bool forward = (data.m_StartPos - link.Start).sqrMagnitude < (data.m_StartPos - link.End).sqrMagnitude;
                data.m_StartPos = forward ? link.Start : link.End;
                data.m_EndPos = forward ? link.End : link.Start;
                data.m_InstanceID = link.Owner;
                data.m_LinkType = link.Type;
            }
            return data;
        }

        public void ActivateCurrentOffMeshLink(bool activated)
        {
            if (m_LinkId != 0) NavMesh.SetOffMeshConnectionActive(m_LinkId, activated);
        }

        /// <summary>The agent is at once at the link's end (nextPosition), off the link and steering
        /// to the next corner; its transform stays until the next early update, which moves it on
        /// at its planned velocity (measured, with and without autoTraverseOffMeshLink).</summary>
        public void CompleteOffMeshLink()
        {
            if (!isOnOffMeshLink) return;
            var end = CurrentLinkEnd();
            m_Crowd.animation.active = false;
            m_Crowd.npos = NavMesh.V(end);
            m_NextPosition = end;
            m_LinkId = 0;
            m_LinkCompleting = true;
            var crowd = AgentSystem.CrowdOf(m_Surface);
            if (crowd != null)
            {
                m_Crowd.ncorners = m_Crowd.corridor.FindCorners(m_Crowd.corners, DtCrowdConst.DT_CROWDAGENT_MAX_CORNERS, crowd.GetNavMeshQuery(), crowd.GetFilter(0));
                m_Crowd.KeepSteer();
            }
        }

        internal static bool LinkOccupied(int linkId) => AgentSystem.LinkOccupied(linkId);

        [Obsolete("Set isStopped to true instead.")]
        public void Stop() => isStopped = true;

        [Obsolete("Set isStopped to true instead.")]
        public void Stop(bool stopUpdates) => isStopped = true;

        [Obsolete("Set isStopped to false instead.")]
        public void Resume() => isStopped = false;

        // ------------------------------------------------------------------ system hooks

        internal void SetPathPending(bool pending) => m_PathPending = pending;
        internal void SetPathStatus(NavMeshPathStatus status) => m_PathStatus = status;
        internal void SetPathEnd(Vector3 end) => m_ReportedDestination = end;
        internal bool HasDestination => m_HasDestination;
        internal Vector3 RequestedDestination => m_Destination;
        internal NavMeshPath RequestedPath => m_RequestedPath;
        internal void SetOnNavMesh(bool on) => m_OnNavMesh = on;
        internal Vector3 WrittenPosition { get => m_WrittenPosition; set => m_WrittenPosition = value; }
        internal int WorldVersion { get => m_WorldVersion; set => m_WorldVersion = value; }

        internal override void OnEngineEnable()
        {
            ResetAreaCosts();
            CatchUpStaleness();
            AgentSystem.Add(this);
        }

        internal override void OnEngineDisable()
        {
            ResetAreaCosts();
            AgentSystem.Remove(this);
        }

        internal override void OnEngineDestroy() => AgentSystem.Remove(this);

        /// <summary>An agent added by AddComponent stands where it was placed at once (measured;
        /// one enabled by its scene, activation or enabled waits for its next early update).</summary>
        internal override void OnAddedByScript()
        {
            if (m_Crowd != null && m_UpdatePosition) AgentSystem.WriteTransform(this);
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_AgentTypeID = body.Int("m_AgentTypeID");
            m_Radius = body.Float("m_Radius", 0.5f);
            m_Speed = body.Float("m_Speed", 3.5f);
            m_Acceleration = body.Float("m_Acceleration", 8f);
            m_AvoidancePriority = body.Int("avoidancePriority", 50);
            m_AngularSpeed = body.Float("m_AngularSpeed", 120f);
            m_StoppingDistance = body.Float("m_StoppingDistance");
            m_AutoTraverseOffMeshLink = body.Bool("m_AutoTraverseOffMeshLink", true);
            m_AutoBraking = body.Bool("m_AutoBraking", true);
            m_AutoRepath = body.Bool("m_AutoRepath", true);
            m_Height = body.Float("m_Height", 2f);
            m_BaseOffset = body.Float("m_BaseOffset");
            m_AreaMask = unchecked((int)(uint)body.Long("m_WalkableMask", 0xffffffff));
            m_ObstacleAvoidanceType = (ObstacleAvoidanceType)body.Int("m_ObstacleAvoidanceType", 4);
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (NavMeshAgent)source;
            m_AgentTypeID = s.m_AgentTypeID;
            m_Radius = s.m_Radius;
            m_Height = s.m_Height;
            m_BaseOffset = s.m_BaseOffset;
            m_Speed = s.m_Speed;
            m_AngularSpeed = s.m_AngularSpeed;
            m_Acceleration = s.m_Acceleration;
            m_StoppingDistance = s.m_StoppingDistance;
            m_AutoTraverseOffMeshLink = s.m_AutoTraverseOffMeshLink;
            m_AutoBraking = s.m_AutoBraking;
            m_AutoRepath = s.m_AutoRepath;
            m_AreaMask = s.m_AreaMask;
            m_ObstacleAvoidanceType = s.m_ObstacleAvoidanceType;
            m_AvoidancePriority = s.m_AvoidancePriority;
        }
    }
}

namespace Unbound.Runtime
{
    using UnityEngine;
    using UnityEngine.AI;
    using PL = UnityEngine.PlayerLoop;

    /// <summary>The agents and one Detour crowd per agent type's NavMesh.</summary>
    internal static class AgentSystem
    {
        static readonly List<NavMeshAgent> s_Agents = new List<NavMeshAgent>();
        static readonly List<NavMeshAgent> s_PathRequests = new List<NavMeshAgent>();
        static readonly Dictionary<NavWorld.Surface, DtCrowd> s_Crowds = new Dictionary<NavWorld.Surface, DtCrowd>();
        static int s_CrowdVersion = -1;
        static bool s_Registered;

        static void EnsureRegistered()
        {
            if (s_Registered) return;
            s_Registered = true;
            // Before Update the agents follow their transforms, the pending paths are found and
            // the crowds step; the update after the scripts (PreLateUpdate's AIUpdatePostScript)
            // changes no agent: a request still pending in Update stays pending through LateUpdate
            // (measured: tests/projects/navcost loop.*).
            PlayerLoopDriver.Register(typeof(PL.PreUpdate.AIUpdate), Step);
        }

        /// <summary>The start of the agent update (PreUpdate's AIUpdate): an agent follows its
        /// transform moved by script, then every transform goes to where its agent stands
        /// (documented: the transform matches the simulated position when Update is called;
        /// measured: an agent placed by its scene, activation or Warp, or moved by script, keeps its
        /// transform until then).</summary>
        internal static void SyncTransforms()
        {
            // A NavMesh changed before (this frame's carving) places its agents again first.
            if (s_CrowdVersion != NavWorld.Version) Rebuild();
            foreach (var agent in s_Agents)
            {
                if (agent.m_Crowd == null || !agent.updatePosition) continue;
                FollowTransform(agent);
                // Written when the agent stands elsewhere than it last wrote (or found) its transform.
                if ((agent.m_NextPosition + Vector3.up * agent.baseOffset - agent.WrittenPosition).sqrMagnitude > 0f) WriteTransform(agent);
            }
        }

        /// <summary>A transform moved by script moves the agent (constrained to the NavMesh; freely
        /// while it waits on a link; a completed link's end wins over the transform); the transform
        /// is written back at the next early update.</summary>
        internal static void FollowTransform(NavMeshAgent agent)
        {
            if (agent.m_Crowd == null || !agent.updatePosition || agent.m_LinkCompleting
                || (agent.transform.position - agent.WrittenPosition).sqrMagnitude <= 1e-8f) return;
            var moved = agent.transform.position;
            // Recorded first: the nextPosition setter syncs the agent again.
            agent.WrittenPosition = moved;
            if (agent.WaitingOnLink) agent.FollowTransformOnLink();
            else agent.MoveAlongSurface(moved);
        }

        /// <summary>The agent stands where its transform does not: the transform is written at the
        /// next early update, and is not a script's move until then.</summary>
        internal static void DeferTransform(NavMeshAgent agent) => agent.WrittenPosition = agent.transform.position;

        /// <summary>The frame's agent update, before Update (PreUpdate's AIUpdate): transforms, path
        /// requests, then the crowds steer, plan, move and cross links, and transforms follow.</summary>
        internal static void Step()
        {
            SyncTransforms();
            ProcessRequests();
            UpdateCrowds();
        }


        internal static void Add(NavMeshAgent agent)
        {
            EnsureRegistered();
            if (!s_Agents.Contains(agent)) s_Agents.Add(agent);
            Place(agent, agent.transform.position - Vector3.up * agent.baseOffset);
        }

        internal static void Remove(NavMeshAgent agent)
        {
            s_Agents.Remove(agent);
            s_PathRequests.Remove(agent);
            s_Restarted.Remove(agent);
            Detach(agent);
        }

        static void Detach(NavMeshAgent agent)
        {
            if (agent.m_Crowd != null && agent.m_Surface != null && s_Crowds.TryGetValue(agent.m_Surface, out var crowd))
                crowd.RemoveAgent(agent.m_Crowd);
            agent.m_Crowd = null;
            agent.m_Surface = null;
            agent.m_LinkId = 0;
            agent.m_LinkCompleting = false;
            agent.m_LandAfterReset = false;
            agent.SetOnNavMesh(false);
        }

        internal static void Reattach(NavMeshAgent agent)
        {
            if (!s_Agents.Contains(agent)) return;
            Detach(agent);
            Place(agent, agent.m_NextPosition);
        }

        internal static DtCrowd CrowdOf(NavWorld.Surface surface)
        {
            if (surface == null) return null;
            if (s_CrowdVersion != NavWorld.Version) Rebuild();
            if (!s_Crowds.TryGetValue(surface, out var crowd))
            {
                var config = new DtCrowdConfig(4f);
                crowd = new DtCrowd(config, surface.Mesh, i => AreaFilter.Of(-1));
                crowd.tryEnterConnection = polyRef => TryEnterLink(surface, polyRef);
                ConfigureAvoidance(crowd);
                s_Crowds[surface] = crowd;
            }
            return crowd;
        }

        /// <summary>The four avoidance qualities (low to high) as Detour's adaptive sampling presets.</summary>
        static float s_ConfiguredPrediction = float.NaN;

        static void ConfigureAvoidance(DtCrowd crowd)
        {
            s_ConfiguredPrediction = NavMesh.avoidancePredictionTime;
            (int Divs, int Rings, int Depth)[] presets = { (5, 2, 1), (5, 2, 2), (7, 2, 3), (7, 3, 3) };
            for (int i = 0; i < presets.Length; i++)
            {
                // Unity's sampling (measured, tests/projects/avoidance): Detour's adaptive patterns
                // with the weights DtObstacleAvoidanceParams defaults to, looking as far ahead as
                // the prediction time.
                var q = new DtObstacleAvoidanceParams();
                q.horizTime = NavMesh.avoidancePredictionTime;
                q.adaptiveDivs = presets[i].Divs;
                q.adaptiveRings = presets[i].Rings;
                q.adaptiveDepth = presets[i].Depth;
                crowd.SetObstacleAvoidanceParams(i, q);
            }
        }

        internal static DtCrowdAgentParams Params(NavMeshAgent agent)
        {
            // Straight to the next corner along the corridor, as Unity's agents steer (measured).
            int flags = 0;
            if (agent.obstacleAvoidanceType != ObstacleAvoidanceType.NoObstacleAvoidance)
                flags |= DtCrowdAgentUpdateFlags.DT_CROWD_OBSTACLE_AVOIDANCE | DtCrowdAgentUpdateFlags.DT_CROWD_SEPARATION;
            if (!agent.autoBraking) flags |= DtCrowdAgentUpdateFlags.UNBOUND_NO_BRAKING;
            return new DtCrowdAgentParams
            {
                radius = agent.radius,
                height = agent.height,
                maxAcceleration = agent.acceleration,
                maxSpeed = agent.isStopped ? 0f : agent.speed,
                reachSpeed = agent.speed,
                pathOptimizationRange = agent.radius * 30f,
                separationWeight = 1.4f,
                updateFlags = flags,
                obstacleAvoidanceType = Math.Max(0, (int)agent.obstacleAvoidanceType - 1),
                queryFilterType = 0,
                userData = agent,
                avoidancePriority = agent.avoidancePriority,
            };
        }

        /// <summary>The crowds follow the NavMesh: when it was rebuilt, agents rejoin at their places.
        /// An agent that was following a path goes on to its destination; one waiting for a path
        /// still waits for it.</summary>
        // The NavMesh changing (a link added, removed, enabled or disabled; NavMesh data added,
        // removed or updated) rebuilds Unbound's NavMesh, whose polygons are new, so the agents'
        // corridors cannot be kept as they are. Documented (Scripting API 6000.0): the change makes
        // the path stale (isPathStale), pathPending is true while a path is being computed
        // (pathPending, SetDestination). Observed, not documented (navlinks capture
        // tests/projects/navlinks/Reference/unity-6000.0.71f1.jsonl, committed at 982cee91, not
        // admitted; sha256 b5d6cbe7...23bf): through four link toggles, every agent of the other
        // lanes walking a path (11 to 26 per toggle) kept hasPath true and pathPending false, with
        // no drop in speed, in the frame of the toggle and the next. Unbound's choices (unmeasured
        // unless noted): the path of an agent following one is planned again, at once and in full,
        // from where the agent rejoins the new NavMesh to its destination, with no pathPending in
        // between and no part of the frame's pathfinding iterations, and the agent keeps its
        // velocity; autoRepath is not consulted (the rebuild discards Unbound's corridor whether or
        // not Unity's path became invalid); the new path is a newly applied path for isPathStale; a
        // path that cannot be planned leaves the agent without a path (PathInvalid, its destination
        // kept), as a failed request does; an agent the new NavMesh cannot place loses its pending
        // request; an agent that had arrived, whose request had failed, or that has no destination
        // is given no path (one within its last centimetre of a path still finishes it); a path given
        // by SetPath is planned again to its last corner. The rebuild
        // is taken in when Unbound next refreshes its NavMesh (a NavMesh query or an agent
        // placement), which can be later than the change. An agent on an off-mesh link, or
        // completing one, rejoins as before.
        static void Rebuild()
        {
            s_CrowdVersion = NavWorld.Version;
            s_Crowds.Clear();
            foreach (var agent in s_Agents.ToArray())
            {
                // An agent still being placed (the first of a new NavMesh) is placed by its caller.
                if (agent.m_Crowd == null) continue;
                var destination = agent.HasDestination ? agent.RequestedDestination : (Vector3?)null;
                var old = agent.isOnOffMeshLink ? agent.m_Crowd : null;
                bool onLink = old != null || agent.m_LinkCompleting;
                bool following = !onLink && destination.HasValue && agent.FollowingPath;
                // Within its last centimetre (hasPath already false) and still moving to the end; an
                // agent stopped within its stoppingDistance stays stopped.
                bool finishing = !onLink && destination.HasValue && !following && !agent.pathPending
                    && agent.m_Crowd.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_VALID
                    && FlatRemaining(agent.m_Crowd) < 0.01f;
                var moving = agent.m_Crowd;
                bool completing = agent.m_LinkCompleting, landing = agent.m_LandAfterReset;
                var linkEnd = moving.animation.endPos;
                agent.m_Crowd = null;
                agent.m_Surface = null;
                if (old != null && RejoinOnLink(agent, old))
                {
                    if (agent.pathPending) RestartRequest(agent);
                    else if (destination.HasValue) PlanFromLinkEnd(agent);
                    continue;
                }
                Place(agent, agent.m_NextPosition);
                // Landing after ResetPath from over a gap: placed where it lands.
                if (agent.m_Crowd == null && landing)
                {
                    var surface = NavWorld.Get(agent.agentTypeID);
                    if (NavMesh.Nearest(surface, NavMesh.V(linkEnd), NavMesh.AgentExtents(agent.agentTypeID), agent.Filter, out var poly, out _)
                        && surface.Query.ClosestPointOnPoly(poly, moving.npos, out var landed, out _).Succeeded())
                        Place(agent, NavMesh.V(landed));
                }
                if (onLink)
                {
                    // A link being left still lands at the next update (see LeaveLink).
                    if (completing && agent.m_Crowd != null)
                    {
                        agent.m_LinkCompleting = true;
                        agent.m_LandAfterReset = landing;
                        agent.m_Crowd.state = DtCrowdAgentState.DT_CROWDAGENT_STATE_OFFMESH;
                        agent.m_Crowd.animation.endPos = linkEnd;
                        KeepMotion(agent.m_Crowd, moving, false);
                    }
                    if (destination.HasValue && agent.m_Crowd != null)
                    {
                        // A request under way starts again; an agent landing after ResetPath gets its
                        // path at the next update, from where it lands; one completing its link goes on
                        // from the link's end; one whose link is gone from where it stands.
                        if (agent.pathPending) RestartRequest(agent);
                        else if (landing)
                        {
                            agent.SetPathPending(true);
                            RequestPath(agent);
                        }
                        else if (completing) PlanFromLinkEnd(agent);
                        else PlanAgain(agent);
                    }
                    continue;
                }
                if (agent.m_Crowd == null)
                {
                    // Off the new NavMesh no path can be computed.
                    agent.SetPathPending(false);
                    continue;
                }
                // It moves on as it moved (also an agent sliding to a stop without a path: measured,
                // tests/projects/navlinks resetmid through a link toggle).
                KeepMotion(agent.m_Crowd, moving, following || finishing);
                if (agent.pathPending) RestartRequest(agent);
                else if (following) PlanAgain(agent);
                else if (finishing)
                {
                    PlanAgain(agent);
                    agent.SetArrived(true);
                }
            }
        }

        /// <summary>A request again on the rebuilt NavMesh. A search that was under way starts again
        /// and is then finished at once, so a NavMesh changing every frame (a moving carving
        /// obstacle) cannot keep a request pending forever (Unbound's choice, unmeasured).</summary>
        static void RestartRequest(NavMeshAgent agent)
        {
            bool underWay = s_Searches.ContainsKey(agent);
            RequestPath(agent);
            if (underWay) s_Restarted.Add(agent);
        }

        /// <summary>The distance left along the agent's corners in the ground plane, as arrival
        /// measures it (StopAtDestination).</summary>
        static float FlatRemaining(DtCrowdAgent ag)
        {
            float left = 0f;
            var p = ag.npos;
            for (int i = 0; i < ag.ncorners; i++)
            {
                left += RcVec.Dist2D(p, ag.corners[i].pos);
                p = ag.corners[i].pos;
            }
            return left;
        }

        /// <summary>The agent placed again on the rebuilt NavMesh keeps its velocity and the step it
        /// planned; one still following a path also the plan it reports (desired velocity).</summary>
        static void KeepMotion(DtCrowdAgent ag, DtCrowdAgent moving, bool following)
        {
            ag.vel = moving.vel;
            ag.pvel = moving.pvel;
            ag.braking = moving.braking;
            if (!following) return;
            ag.nvel = moving.nvel;
            ag.dvel = moving.dvel;
            ag.desiredSpeed = moving.desiredSpeed;
            ag.hasSteer = moving.hasSteer;
            ag.steerPos = moving.steerPos;
            ag.steerFlags = moving.steerFlags;
        }

        /// <summary>The path of an agent that was following one, planned again on the rebuilt
        /// NavMesh from where the agent now stands to its destination; the agent keeps its velocity
        /// (see the note on <see cref="Rebuild"/>).</summary>
        static void PlanAgain(NavMeshAgent agent)
        {
            var search = NavMesh.PathSearch.Begin(agent.m_Surface, agent.m_NextPosition, agent.RequestedDestination, agent.Filter, agent.agentTypeID);
            int budget = int.MaxValue;
            if (search != null && search.Step(ref budget) && search.Result != null)
            {
                // The path goes on: as stale as it was (measured, see isPathStale).
                var staleness = agent.Staleness;
                ApplyPath(agent, search.Result);
                agent.RestoreStaleness(staleness);
                return;
            }
            agent.SetPathStatus(NavMeshPathStatus.PathInvalid);
        }

        /// <summary>An agent on a link stays on it when the NavMesh changes, its link deactivated
        /// included: waiting where it was until a script completes the link, or carried on over it
        /// by the traversal under way (navlinks capture, see the off-mesh link note above: a link
        /// deactivated while the agent waits on it or crosses it). It joins the new NavMesh at the
        /// link's end, where its path goes on, as Detour's corridor does when it moves over a link.
        /// A link that is gone, or a NavMesh with no place at its end, places the agent again where
        /// it is (unmeasured).</summary>
        static bool RejoinOnLink(NavMeshAgent agent, DtCrowdAgent old)
        {
            if (NavWorld.FindLink(agent.m_LinkId) == null) return false;
            var surface = NavWorld.Get(agent.agentTypeID);
            var oldAnim = old.animation;
            if (!NavMesh.Nearest(surface, NavMesh.V(oldAnim.endPos), NavMesh.AgentExtents(agent.agentTypeID), agent.Filter, out _, out var landing))
                return false;
            var ag = CrowdOf(surface).AddAgent(NavMesh.V(landing), Params(agent));
            ag.state = DtCrowdAgentState.DT_CROWDAGENT_STATE_OFFMESH;
            ag.npos = old.npos;
            ag.vel = old.vel;
            ag.nvel = old.nvel;
            ag.pvel = old.pvel;
            ag.dvel = old.dvel;
            ag.desiredSpeed = old.desiredSpeed;
            var anim = ag.animation;
            anim.active = oldAnim.active;
            anim.initPos = oldAnim.initPos;
            anim.startPos = oldAnim.startPos;
            anim.endPos = oldAnim.endPos;
            anim.t = oldAnim.t;
            anim.tmax = oldAnim.tmax;
            // The old link polygon is not part of the new NavMesh; the link id stays.
            anim.polyRef = 0;
            agent.m_Crowd = ag;
            agent.m_Surface = surface;
            agent.WorldVersion = NavWorld.Version;
            agent.SetOnNavMesh(true);
            return true;
        }

        /// <summary>The path to the destination again, from the end of the link the agent is on (its
        /// old path's polygons are gone with the old NavMesh).</summary>
        static void PlanFromLinkEnd(NavMeshAgent agent)
        {
            var from = agent.m_Crowd.animation.endPos;
            var search = NavMesh.PathSearch.Begin(agent.m_Surface, NavMesh.V(from), agent.RequestedDestination, agent.Filter, agent.agentTypeID);
            int budget = int.MaxValue;
            if (search != null && search.Step(ref budget) && search.Result != null)
            {
                var staleness = agent.Staleness;
                ApplyPath(agent, search.Result, from);
                agent.RestoreStaleness(staleness);
            }
        }

        /// <summary>Before the agent's state is read or changed: placed again when its NavMesh changed
        /// since it was placed, and moved with its transform moved by script (measured: nextPosition
        /// follows such a move at once).</summary>
        internal static void Sync(NavMeshAgent agent)
        {
            if (s_CrowdVersion != NavWorld.Version) Rebuild();
            if (agent.m_Crowd == null && s_Agents.Contains(agent) && agent.WorldVersion != NavWorld.Version)
                Place(agent, agent.transform.position - Vector3.up * agent.baseOffset);
            FollowTransform(agent);
        }

        internal static bool Place(NavMeshAgent agent, Vector3 position)
        {
            Detach(agent);
            agent.WorldVersion = NavWorld.Version;
            var surface = NavWorld.Get(agent.agentTypeID);
            if (!NavMesh.Nearest(surface, position, NavMesh.AgentExtents(agent.agentTypeID), agent.Filter, out var polyRef, out var point))
            {
                agent.m_NextPosition = position;
                Debug.LogWarning("Failed to create agent because it is not close enough to the NavMesh", agent);
                return false;
            }
            var crowd = CrowdOf(surface);
            agent.m_Surface = surface;
            agent.m_Crowd = crowd.AddAgent(NavMesh.V(point), Params(agent));
            agent.m_NextPosition = point;
            agent.SetOnNavMesh(true);
            // The transform stays until the agent's next early update (measured).
            DeferTransform(agent);
            return true;
        }

        /// <summary>A new destination whose path needs no search is the agent's path at once: one on
        /// the agent's own polygon, or, for an agent following a path, one on the rest of that path;
        /// the path is the agent's corridor up to the destination's polygon. Any other waits for the
        /// next early update (measured, tests/projects/navcost loop.*: pathPending right after
        /// SetDestination, for agents made in that frame and earlier, standing and walking, with
        /// destinations on their polygon, ahead on their path, at its end, behind them and beyond the
        /// NavMesh). Cutting the corridor is Unbound's way to give that path (unmeasured). An agent
        /// on a link, or whose path is stale, keeps its request for the early update.</summary>
        internal static void PlanAtOnce(NavMeshAgent agent, bool hadPath)
        {
            var ag = agent.m_Crowd;
            // A stale path (areaMask or area costs changed, a link toggled) is searched anew
            // (measured: tests/projects/navlinks inactive, a destination set after the link opened).
            if (ag == null || agent.isOnOffMeshLink || agent.m_LandAfterReset || (hadPath && agent.isPathStale)) return;
            if (!NavMesh.Nearest(agent.m_Surface, agent.RequestedDestination, NavMesh.AgentExtents(agent.agentTypeID), agent.Filter, out var targetPoly, out var target))
                return;
            var point = NavMesh.V(target);
            int end = hadPath ? RemainingPathIndex(ag, targetPoly, point) : ag.corridor.GetFirstPoly() == targetPoly ? 0 : -1;
            if (end < 0) return;
            s_PathRequests.Remove(agent);
            s_Searches.Remove(agent);
            s_Restarted.Remove(agent);
            Follow(agent, ag.corridor.GetPath(), end + 1, point, NavMeshPathStatus.PathComplete);
        }

        /// <summary>Where on the rest of the agent's path a point on <paramref name="polyRef"/> lies:
        /// the index of a later polygon of its corridor, 0 for the agent's own polygon when the point
        /// is not behind it (against the way to its next corner), else -1.</summary>
        static int RemainingPathIndex(DtCrowdAgent ag, long polyRef, RcVec3f point)
        {
            var path = ag.corridor.GetPath();
            int count = ag.corridor.GetPathCount();
            for (int i = 1; i < count; i++)
                if (path[i] == polyRef) return i;
            if (count == 0 || path[0] != polyRef) return -1;
            if (ag.ncorners == 0) return 0;
            var ahead = RcVec3f.Subtract(ag.corners[0].pos, ag.npos);
            var to = RcVec3f.Subtract(point, ag.npos);
            return ahead.X * to.X + ahead.Z * to.Z >= 0f ? 0 : -1;
        }

        internal static void RequestPath(NavMeshAgent agent)
        {
            s_Searches.Remove(agent);
            s_Restarted.Remove(agent);
            if (!s_PathRequests.Contains(agent)) s_PathRequests.Add(agent);
        }

        static readonly Dictionary<NavMeshAgent, NavMesh.PathSearch> s_Searches = new Dictionary<NavMeshAgent, NavMesh.PathSearch>();
        /// <summary>Agents whose search under way a NavMesh rebuild restarted: finished in one go.</summary>
        static readonly HashSet<NavMeshAgent> s_Restarted = new HashSet<NavMeshAgent>();

        /// <summary>Paths for the pending requests, in the agent update, in request order within the
        /// frame's A* iteration budget; a request the budget does not finish goes on next frame.</summary>
        static void ProcessRequests()
        {
            if (s_CrowdVersion != NavWorld.Version) Rebuild();
            int budget = NavMesh.pathfindingIterationsPerFrame;
            while (s_PathRequests.Count > 0 && budget > 0)
            {
                var agent = s_PathRequests[0];
                if (agent == null || agent.m_Crowd == null || !agent.pathPending)
                {
                    s_PathRequests.RemoveAt(0);
                    if (agent != null)
                    {
                        s_Searches.Remove(agent);
                        s_Restarted.Remove(agent);
                    }
                    continue;
                }
                if (!s_Searches.TryGetValue(agent, out var search))
                {
                    search = NavMesh.PathSearch.Begin(agent.m_Surface, PathOrigin(agent), agent.RequestedDestination, agent.Filter, agent.agentTypeID);
                    if (search == null)
                    {
                        s_PathRequests.RemoveAt(0);
                        agent.SetPathPending(false);
                        agent.SetPathStatus(NavMeshPathStatus.PathInvalid);
                        continue;
                    }
                    s_Searches[agent] = search;
                }
                bool restarted = s_Restarted.Remove(agent);
                int allowed = restarted ? int.MaxValue : budget;
                bool finished = search.Step(ref allowed);
                if (!restarted) budget = allowed;
                if (!finished) break;
                s_PathRequests.RemoveAt(0);
                s_Searches.Remove(agent);
                if (search.Result == null)
                {
                    agent.SetPathPending(false);
                    agent.SetPathStatus(NavMeshPathStatus.PathInvalid);
                    continue;
                }
                // The corridor starts where the search did (see PathOrigin).
                ApplyPath(agent, search.Result, NavMesh.V(search.Origin));
            }
        }

        /// <summary>Where the agent's paths start: where it stands; on a link (crossing or waiting),
        /// the link's end, where it goes on (a path from where it is would take it back over the
        /// link, or start off the NavMesh); taken off its link by ResetPath, where it lands. The
        /// navlinks capture measured this with automatic traversal only: an agent given a new
        /// destination on a link finishes the link and follows the new path from its end; a
        /// waiting or reset agent's re-target is unmeasured.</summary>
        static Vector3 PathOrigin(NavMeshAgent agent)
        {
            if (agent.isOnOffMeshLink) return NavMesh.V(agent.m_Crowd.animation.endPos);
            if (agent.m_LandAfterReset && LandingPoint(agent, out _, out var landing)) return NavMesh.V(landing);
            return agent.m_NextPosition;
        }

        /// <summary>A path search under way began on the other side of a link the agent now waits
        /// on, or no longer waits on: it begins again from where the agent's paths now start.</summary>
        static void RestartSearch(NavMeshAgent agent)
        {
            s_Searches.Remove(agent);
            s_Restarted.Remove(agent);
        }

        internal static void ApplyPath(NavMeshAgent agent, NavMeshPath path) => ApplyPath(agent, path, null);

        /// <summary>The agent follows the path; its corridor starts at <paramref name="from"/>, else
        /// where it stands: waiting on a link, where the path starts; landing after ResetPath, where
        /// it lands.</summary>
        static void ApplyPath(NavMeshAgent agent, NavMeshPath path, RcVec3f? from)
        {
            var ag = agent.m_Crowd;
            var polys = path.m_Polygons;
            var target = path.m_Corners.Length > 0 ? path.m_Corners[path.m_Corners.Length - 1] : agent.m_NextPosition;
            RcVec3f start;
            if (from.HasValue) start = from.Value;
            else if (agent.m_LandAfterReset && LandingPoint(agent, out _, out var landing)) start = landing;
            else if (agent.WaitingOnLink && path.m_Corners.Length > 0) start = NavMesh.V(path.m_Corners[0]);
            else start = ag.npos;
            ag.corridor.Reset(polys[0], start);
            Follow(agent, polys, polys.Length, NavMesh.V(target), path.status);
        }

        /// <summary>The agent follows the corridor <paramref name="polys"/> (its first
        /// <paramref name="count"/>) from where its corridor starts to <paramref name="target"/>.</summary>
        static void Follow(NavMeshAgent agent, Span<long> polys, int count, RcVec3f target, NavMeshPathStatus status)
        {
            var crowd = CrowdOf(agent.m_Surface);
            var ag = agent.m_Crowd;
            var last = polys[count - 1];
            ag.corridor.SetCorridor(target, polys, count);
            ag.boundary.Reset();
            ag.partial = status == NavMeshPathStatus.PathPartial;
            ag.targetRef = last;
            ag.targetPos = target;
            ag.targetState = DtMoveRequestState.DT_CROWDAGENT_TARGET_VALID;
            ag.targetReplanTime = 0f;
            ag.targetReplan = false;
            agent.SetPathPending(false);
            agent.SetArrived(false);
            agent.SetPathStatus(status);
            agent.SetPathEnd(NavMesh.V(target));
            agent.PathRenewed();
            // The corners the agent steers by, for this frame's queries.
            ag.ncorners = ag.corridor.FindCorners(ag.corners, DtCrowdConst.DT_CROWDAGENT_MAX_CORNERS, crowd.GetNavMeshQuery(), crowd.GetFilter(0));
        }

        /// <summary>Unity's agents share one proximity grid whatever their agent type, its cells
        /// sized from all of them (see DtCrowd.GridCell) at the velocity this update steps them to.
        /// NavMeshObstacles do not count here (Unbound's choice: Unity counts them too).</summary>
        static void SizeGrids(float dt)
        {
            float horizon = NavMesh.avoidancePredictionTime, sx = 0f, sz = 0f;
            int count = 0;
            foreach (var agent in s_Agents)
            {
                var ag = agent.m_Crowd;
                if (ag == null) continue;
                // On a link: the velocity it crosses at (the crowd keeps none for it).
                var vel = ag.state == DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING ? ag.NextVelocity(dt) : NavMesh.V(agent.m_Velocity);
                DtCrowd.AddToGridCell(ag, vel, horizon, ref sx, ref sz);
                count++;
            }
            if (count == 0) return;
            float x = DtCrowd.GridCell(sx, count), z = DtCrowd.GridCell(sz, count);
            foreach (var crowd in s_Crowds.Values) crowd.SetGridCellSize(x, z);
        }

        /// <summary>The crowds step: steering, avoidance, motion; then transforms follow.</summary>
        static void UpdateCrowds()
        {
            if (s_Agents.Count == 0) return;
            NavMesh.onPreUpdate?.Invoke();
            if (s_CrowdVersion != NavWorld.Version) Rebuild();
            float dt = Time.deltaTime;
            foreach (var agent in s_Agents)
            {
                // A path given in a call counts from this update; an agent without a destination
                // has no stale path from now.
                agent.m_PathFromCall = false;
                if (!agent.HasDestination) agent.CatchUpStaleness();
                FollowTransform(agent);
                if (agent.m_Crowd != null)
                {
                    CrowdOf(agent.m_Surface).UpdateAgentParameters(agent.m_Crowd, Params(agent));
                }
            }
            if (dt <= 0f) return;
            if (s_ConfiguredPrediction != NavMesh.avoidancePredictionTime)
                foreach (var crowd in s_Crowds.Values) ConfigureAvoidance(crowd);
            s_OccupiedLinks.Clear();
            foreach (var agent in s_Agents)
            {
                if (agent.m_Crowd == null) continue;
                if (agent.m_LinkCompleting)
                {
                    // After ResetPath on the link it lands after this update's step.
                    if (!agent.m_LandAfterReset) LeaveLink(agent);
                }
                else PrepareLink(agent);
                if (agent.isOnOffMeshLink && agent.m_LinkId != 0) s_OccupiedLinks.Add(agent.m_LinkId);
            }
            SizeGrids(dt);
            foreach (var pair in s_Crowds)
            {
                // The enabled NavMeshObstacles, as they stand now, are among what the agents avoid.
                ObstacleSystem.Feed(pair.Value);
                pair.Value.Update(dt, null);
            }
            foreach (var agent in s_Agents)
            {
                if (agent.m_Crowd == null) continue;
                if (agent.m_LandAfterReset) LandOffLink(agent);
                else if (agent.m_StepWalking && agent.m_Crowd.animation.polyRef != 0) ReachLink(agent);
                if (agent.m_Crowd.state != DtCrowdAgentState.DT_CROWDAGENT_STATE_OFFMESH) agent.m_LinkId = 0;
                var previous = agent.m_NextPosition;
                agent.m_NextPosition = NavMesh.V(agent.m_Crowd.npos);
                // The step over the frame, pushes out of other agents included (measured).
                agent.m_Velocity = (agent.m_NextPosition - previous) / dt;
                StopAtDestination(agent, dt, NavMesh.V(previous));
                // An agent that moved stands there now; one that did not keeps its transform until
                // its next early update (measured).
                if (agent.updatePosition && !agent.m_NextPosition.Equals(previous)) WriteTransform(agent);
                if (agent.updateRotation) Rotate(agent, dt);
            }
        }

        /// <summary>Before the crowd steps: a walking agent may reach a link (marked by the link
        /// polygon Detour records); an agent on a link follows its autoTraverseOffMeshLink as it is
        /// now (navlinks capture: turned off, it stops where it is; turned on while waiting, it
        /// traverses the link from where it stands as Detour's traversal does).</summary>
        static void PrepareLink(NavMeshAgent agent)
        {
            var ag = agent.m_Crowd;
            var anim = ag.animation;
            agent.m_StepWalking = ag.state == DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING;
            if (agent.m_StepWalking)
            {
                anim.polyRef = 0;
                return;
            }
            if (ag.state != DtCrowdAgentState.DT_CROWDAGENT_STATE_OFFMESH || agent.m_LinkCompleting) return;
            if (anim.active && !agent.autoTraverseOffMeshLink)
            {
                anim.active = false;
                RestartSearch(agent);
            }
            else if (!anim.active && agent.autoTraverseOffMeshLink)
            {
                anim.initPos = ag.npos;
                anim.t = 0f;
                anim.active = true;
                RestartSearch(agent);
            }
        }

        /// <summary>The agent reached a link in this update: it is on that link; without
        /// autoTraverseOffMeshLink it waits where it reached it, with its desired velocity kept,
        /// until a script completes the link (navlinks capture).</summary>
        static void ReachLink(NavMeshAgent agent)
        {
            var ag = agent.m_Crowd;
            var anim = ag.animation;
            agent.m_LinkId = agent.m_Surface.LinkOfPoly.TryGetValue(anim.polyRef, out int linkId) ? linkId : 0;
            if (agent.autoTraverseOffMeshLink) return;
            ag.npos = anim.initPos;
            anim.active = false;
            // Also when Detour's traversal of a short link would have ended within the update.
            ag.state = DtCrowdAgentState.DT_CROWDAGENT_STATE_OFFMESH;
            RestartSearch(agent);
        }

        /// <summary>The update after CompleteOffMeshLink: the agent walks on from the link's end at
        /// its planned velocity, full speed at once (navlinks capture).</summary>
        static void LeaveLink(NavMeshAgent agent)
        {
            var ag = agent.m_Crowd;
            ag.state = DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING;
            ag.vel = ag.pvel;
            agent.m_LinkCompleting = false;
            agent.m_StepWalking = true;
            ag.animation.polyRef = 0;
        }

        /// <summary>An agent whose path was reset on a link: at the end of the next update it stands
        /// at the point of the link's landing polygon nearest to where it was, with its walking
        /// velocity from before the link, which it loses at its acceleration from the next update
        /// (measured: tests/projects/navlinks resetmid).</summary>
        static void LandOffLink(NavMeshAgent agent)
        {
            var ag = agent.m_Crowd;
            agent.m_LinkCompleting = false;
            agent.m_LandAfterReset = false;
            ag.state = DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING;
            ag.animation.polyRef = 0;
            if (ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_VALID)
                // A path given since starts at the landing point (see PathOrigin, ApplyPath).
                ag.npos = ag.corridor.GetPos();
            else if (LandingPoint(agent, out var poly, out var landing))
            {
                ag.corridor.Reset(poly, landing);
                ag.npos = landing;
            }
            ag.vel = ag.pvel;
            ag.pvel = RcVec3f.Zero;
            ag.nvel = RcVec3f.Zero;
            ag.dvel = RcVec3f.Zero;
        }

        /// <summary>Where an agent taken off its link by ResetPath lands: the point of the link's
        /// landing polygon nearest to where it is.</summary>
        static bool LandingPoint(NavMeshAgent agent, out long poly, out RcVec3f landing)
        {
            landing = default;
            var ag = agent.m_Crowd;
            return NavMesh.Nearest(agent.m_Surface, NavMesh.V(ag.animation.endPos), NavMesh.AgentExtents(agent.agentTypeID), agent.Filter, out poly, out _)
                && agent.m_Surface.Query.ClosestPointOnPoly(poly, ag.npos, out landing, out _).Succeeded();
        }

        /// <summary>The end of the path (measured): within the stopping distance an agent stops;
        /// within 0.01 its path no longer counts as one (hasPath is false) while it closes in;
        /// a step that would pass the end lands on it and the agent stands.</summary>
        static void StopAtDestination(NavMeshAgent agent, float dt, RcVec3f before)
        {
            var ag = agent.m_Crowd;
            if (ag.targetState != DtMoveRequestState.DT_CROWDAGENT_TARGET_VALID || ag.ncorners == 0) return;
            var last = ag.corners[ag.ncorners - 1];
            if ((last.flags & DtStraightPathFlags.DT_STRAIGHTPATH_END) == 0) return;
            float left = 0f, leftBefore = 0f;
            var p = ag.npos;
            var q = before;
            for (int i = 0; i < ag.ncorners; i++)
            {
                left += RcVec.Dist2D(p, ag.corners[i].pos);
                leftBefore += RcVec.Dist2D(q, ag.corners[i].pos);
                p = q = ag.corners[i].pos;
            }
            float step = ag.vel.Length() * dt;
            if (agent.stoppingDistance > 0f && left <= agent.stoppingDistance)
            {
                ag.vel = RcVec3f.Zero;
                ag.nvel = RcVec3f.Zero;
                ag.pvel = RcVec3f.Zero;
                agent.m_Velocity = Vector3.zero;
                agent.SetArrived(true);
                return;
            }
            // The step that reaches the end from where the update began lands on it (measured).
            if (leftBefore <= 1e-3f || (agent.autoBraking && step >= leftBefore))
            {
                ag.npos = last.pos;
                ag.vel = RcVec3f.Zero;
                ag.nvel = RcVec3f.Zero;
                ag.pvel = RcVec3f.Zero;
                agent.m_Velocity = Vector3.zero;
                agent.m_NextPosition = NavMesh.V(last.pos);
                CrowdOf(agent.m_Surface)?.ResetMoveTarget(ag);
                ag.corridor.Reset(ag.corridor.GetFirstPoly(), ag.npos);
                ag.ncorners = 0;
                ag.hasSteer = false;
                agent.SetArrived(true);
                return;
            }
            agent.SetArrived(left < 0.01f);
        }

        internal static void WriteTransform(NavMeshAgent agent)
        {
            var position = agent.m_NextPosition + Vector3.up * agent.baseOffset;
            agent.transform.position = position;
            agent.WrittenPosition = position;
        }

        /// <summary>Gain of the turn towards the agent's heading: per second, (this + speed) times the
        /// angle left, in degrees, up to angularSpeed (measured: tests/projects/navlinks rotations).</summary>
        const float TurnGain = 1f;

        /// <summary>The agent turns towards the velocity its next step takes (its current velocity
        /// accelerated towards this update's plan), so it begins to turn in the update that plans
        /// its first step; on a link, towards its desired velocity there: along the link at its
        /// speed while it crosses, its kept plan while it waits. It turns by (1 + that speed) times
        /// the angle left per second, at most angularSpeed (measured: tests/projects/navlinks
        /// rotations walking and on links, navcost loop.*).</summary>
        static void Rotate(NavMeshAgent agent, float dt)
        {
            var ag = agent.m_Crowd;
            RcVec3f next;
            if (ag.state == DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING) next = ag.NextVelocity(dt);
            else if (ag.animation.active)
            {
                var along = RcVec3f.Subtract(ag.animation.endPos, ag.animation.startPos);
                float length = MathF.Sqrt(along.X * along.X + along.Z * along.Z);
                next = length > 1e-6f ? new RcVec3f(along.X, 0f, along.Z) * (agent.speed / length) : RcVec3f.Zero;
            }
            else next = ag.nvel;
            var heading = new Vector3(next.X, 0f, next.Z);
            float speed = heading.magnitude;
            if (speed < 1e-3f) return;
            var rotation = agent.transform.rotation;
            var target = Quaternion.LookRotation(heading);
            float left = Quaternion.Angle(rotation, target);
            float step = Mathf.Min(agent.angularSpeed * dt, (TurnGain + speed) * left * dt);
            agent.transform.rotation = Quaternion.RotateTowards(rotation, target, step);
        }

        /// <summary>The links an agent is on during the crowd update (also those taken in it).</summary>
        static readonly HashSet<int> s_OccupiedLinks = new HashSet<int>();

        /// <summary>An agent reaching a link takes it unless another agent is on it (then it waits:
        /// see DtCrowd.tryEnterConnection).</summary>
        static bool TryEnterLink(NavWorld.Surface surface, long polyRef) =>
            !surface.LinkOfPoly.TryGetValue(polyRef, out int link) || s_OccupiedLinks.Add(link);

        internal static bool LinkOccupied(int linkId)
        {
            foreach (var agent in s_Agents)
                if (agent.isOnOffMeshLink && agent.m_LinkId == linkId) return true;
            return false;
        }
    }
}
