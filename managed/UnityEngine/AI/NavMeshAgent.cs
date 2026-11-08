using System;
using System.Collections.Generic;
using DotRecast.Core.Numerics;
using DotRecast.Detour;
using DotRecast.Detour.Crowd;
using Unbound.Runtime;

namespace UnityEngine.AI
{
    /// <summary>
    /// An agent moving over the NavMesh, driven by a Detour crowd as Unity's agents are. After the
    /// scripts' Update (PreLateUpdate's AIUpdatePostScript) the frame's path requests are found,
    /// then the crowd steers from where the agents stand (to the next corners, avoiding other
    /// agents) and moves them by the velocity planned the frame before (acceleration-limited), so
    /// the frame that asks sees the path and its desired velocity but no motion; transforms follow
    /// (measured in `tests/projects/navmesh`).
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
        Vector3 m_Destination = new Vector3(float.PositiveInfinity, float.PositiveInfinity, float.PositiveInfinity);
        bool m_HasDestination;
        bool m_PathPending;
        NavMeshPath m_RequestedPath;
        // A new agent reports a complete path, also while its first path is pending (measured).
        NavMeshPathStatus m_PathStatus = NavMeshPathStatus.PathComplete;
        Vector3 m_WrittenPosition;
        /// <summary>The transform is to be written at the agent's next early update (PreUpdate's
        /// AIUpdate): the agent was placed (enabled, warped) or followed a script's move.</summary>
        internal bool m_TransformPending;
        bool m_OnNavMesh;
        int m_WorldVersion = -1;
        internal int m_LinkId;
        /// <summary>CompleteOffMeshLink was called: the agent stands at the link's end and leaves
        /// the link with the next crowd update.</summary>
        internal bool m_LinkCompleting;
        /// <summary>The agent was walking when the crowd update began (a link it reaches is new).</summary>
        internal bool m_StepWalking;

        public NavMeshAgent() { }

        // ------------------------------------------------------------------ settings

        public int agentTypeID { get => m_AgentTypeID; set { m_AgentTypeID = value; AgentSystem.Reattach(this); } }
        public float radius { get => m_Radius; set { m_Radius = value; ApplyParams(); } }
        public float height { get => m_Height; set { m_Height = value; ApplyParams(); } }
        /// <summary>Setting it moves the transform to the new height at once (measured).</summary>
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
        /// <summary>Turned on, the transform moves to the agent's position at the agent's next early
        /// update (measured).</summary>
        public bool updatePosition
        {
            get => m_UpdatePosition;
            set
            {
                if (value && !m_UpdatePosition && m_Crowd != null) AgentSystem.DeferTransform(this);
                m_UpdatePosition = value;
            }
        }
        public bool updateRotation { get => m_UpdateRotation; set => m_UpdateRotation = value; }
        public bool updateUpAxis { get => m_UpdateUpAxis; set => m_UpdateUpAxis = value; }

        public bool isStopped
        {
            get => m_IsStopped;
            set
            {
                m_IsStopped = value;
                if (value && m_Crowd != null) m_Crowd.vel = RcVec3f.Zero;
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

        /// <summary>An agent not on a crowd (not enabled, or not placed) reports its transform
        /// (measured for an agent on an inactive GameObject).</summary>
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
                if (m_Crowd == null) return;
                // Moved along the NavMesh surface towards the position.
                var target = value - Vector3.up * m_BaseOffset;
                if (m_Crowd.corridor.MovePosition(NavMesh.V(target), m_Surface.Query, Filter))
                {
                    m_Crowd.npos = m_Crowd.corridor.GetPos();
                    m_NextPosition = NavMesh.V(m_Crowd.npos);
                }
            }
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
                m_Crowd.vel = NavMesh.V(value);
                m_Crowd.nvel = m_Crowd.vel;
                m_Crowd.pvel = m_Crowd.vel;
                m_Velocity = value;
            }
        }

        public Vector3 desiredVelocity => m_Crowd != null ? NavMesh.V(m_Crowd.nvel) : Vector3.zero;

        public Vector3 steeringTarget =>
            m_Crowd != null && m_Crowd.ncorners > 0 ? NavMesh.V(m_Crowd.corners[0].pos) : m_NextPosition;

        public Vector3 destination
        {
            get => m_HasDestination ? m_Destination : m_NextPosition;
            set => SetDestination(value);
        }

        public bool SetDestination(Vector3 target)
        {
            AgentSystem.Sync(this);
            if (!m_OnNavMesh)
            {
                Debug.LogError("\"SetDestination\" can only be called on an active agent that has been placed on a NavMesh.", this);
                return false;
            }
            m_Destination = target;
            m_HasDestination = true;
            m_PathPending = true;
            m_RequestedPath = null;
            AgentSystem.RequestPath(this);
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
            m_HasDestination = true;
            AgentSystem.ApplyPath(this, m_RequestedPath);
            return true;
        }

        public void ResetPath()
        {
            m_PathGiven = false;
            m_OwnPathStale = false;
            m_HasDestination = false;
            m_PathPending = false;
            m_PathStatus = NavMeshPathStatus.PathInvalid;
            if (m_Crowd != null)
            {
                AgentSystem.CrowdOf(m_Surface)?.ResetMoveTarget(m_Crowd);
                m_Crowd.corridor.Reset(m_Crowd.corridor.GetFirstPoly(), m_Crowd.npos);
                m_Crowd.ncorners = 0;
            }
        }

        public bool hasPath => m_Crowd != null && m_HasDestination && !m_PathPending && !m_Arrived && m_Crowd.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_VALID;

        bool m_Arrived;
        internal void SetArrived(bool arrived) => m_Arrived = arrived;

        public bool pathPending => m_PathPending;

        // Path staleness. Documented (Scripting API 6000.0, isPathStale and areaMask): changing the
        // areaMask, enabling or disabling an off-mesh link, or changing the NavMesh area costs
        // makes the path stale; the agent goes on following it. Unbound's choices (unmeasured):
        // the flag is set at once by the change; area cost and link changes count for every
        // agent, areaMask and the agent's own area costs for that agent; only a changed value
        // counts; NavMesh data added or removed does not; it concerns the path last applied to
        // the agent (from SetPath or a path request) until a new one is applied or ResetPath
        // clears it; a pending or failed request keeps the flag; and it starts no new path.
        bool m_PathGiven;
        bool m_OwnPathStale;
        int m_PathStaleVersion;

        public bool isPathStale =>
            m_Crowd != null && m_PathGiven && (m_OwnPathStale || m_PathStaleVersion != NavWorld.PathStaleVersion);

        /// <summary>A new path was applied: it is not stale.</summary>
        internal void PathRenewed()
        {
            m_PathGiven = true;
            m_OwnPathStale = false;
            m_PathStaleVersion = NavWorld.PathStaleVersion;
        }

        public NavMeshPathStatus pathStatus => m_PathStatus;

        public Vector3 pathEndPosition => m_Crowd != null && hasPath ? NavMesh.V(m_Crowd.corridor.GetTarget()) : m_NextPosition;

        /// <summary>The distance left along the corners the agent sees; unknown (infinity) while
        /// the path's end is beyond them (measured).</summary>
        public float remainingDistance
        {
            get
            {
                if (m_Crowd == null || m_PathPending || m_Crowd.targetState != DtMoveRequestState.DT_CROWDAGENT_TARGET_VALID) return 0f;
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
            if (m_PathPending || m_Crowd.targetState != DtMoveRequestState.DT_CROWDAGENT_TARGET_VALID)
            {
                // No path to follow: the path is where the agent stands.
                result.m_Corners = new[] { m_NextPosition };
                return;
            }
            var corridor = m_Crowd.corridor.GetPath();
            int count = m_Crowd.corridor.GetPathCount();
            var straight = new DtStraightPath[256];
            m_Surface.Query.FindStraightPath(m_Crowd.npos, m_Crowd.corridor.GetTarget(), corridor, count, straight, out int n, straight.Length, 0);
            var corners = new Vector3[n];
            for (int i = 0; i < n; i++) corners[i] = NavMesh.V(straight[i].pos);
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
        /// it, where the transform follows at its next early update (measured).</summary>
        public bool Warp(Vector3 newPosition)
        {
            transform.position = newPosition;
            return AgentSystem.Place(this, newPosition - Vector3.up * m_BaseOffset);
        }

        public void Move(Vector3 offset)
        {
            AgentSystem.Sync(this);
            if (m_Crowd == null) return;
            nextPosition = m_NextPosition + Vector3.up * m_BaseOffset + offset;
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

        /// <summary>Where the current link lands: the link's own end point (measured: the end as the
        /// link places it, not snapped onto the NavMesh as Detour's connection end is).</summary>
        Vector3 CurrentLinkEnd()
        {
            var anim = m_Crowd.animation;
            var link = NavWorld.FindLink(m_LinkId);
            if (link == null) return NavMesh.V(anim.endPos);
            var start = NavMesh.V(anim.startPos);
            return (start - link.Start).sqrMagnitude < (start - link.End).sqrMagnitude ? link.End : link.Start;
        }

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
                data.m_EndPos = (data.m_StartPos - link.Start).sqrMagnitude < (data.m_StartPos - link.End).sqrMagnitude ? link.End : link.Start;
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
        /// to the next corner; the frame's crowd update leaves it there without writing its
        /// transform, and the next one moves it on at its planned velocity (measured, with and
        /// without autoTraverseOffMeshLink).</summary>
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
                m_Crowd.ncorners = m_Crowd.corridor.FindCorners(m_Crowd.corners, DtCrowdConst.DT_CROWDAGENT_MAX_CORNERS, crowd.GetNavMeshQuery(), crowd.GetFilter(0));
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
        internal bool HasDestination => m_HasDestination;
        internal Vector3 RequestedDestination => m_Destination;
        internal NavMeshPath RequestedPath => m_RequestedPath;
        internal void SetOnNavMesh(bool on) => m_OnNavMesh = on;
        internal Vector3 WrittenPosition { get => m_WrittenPosition; set => m_WrittenPosition = value; }
        internal int WorldVersion { get => m_WorldVersion; set => m_WorldVersion = value; }

        internal override void OnEngineEnable()
        {
            ResetAreaCosts();
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
            if (m_TransformPending && m_UpdatePosition) AgentSystem.WriteTransform(this);
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
            // After the scripts: the frame's path requests, then the crowds steer from where the
            // agents stand and move (measured: a path asked for in Start has its desired velocity
            // by LateUpdate, and the agents move one frame later).
            PlayerLoopDriver.Register(typeof(PL.PreUpdate.AIUpdate), SyncTransforms);
            PlayerLoopDriver.Register(typeof(PL.PreLateUpdate.AIUpdatePostScript), Step);
        }

        /// <summary>The agents' early update (PreUpdate's AIUpdate): an agent follows its transform
        /// moved by script, and the transform of an agent placed or moved since its last write goes
        /// to where the agent stands (measured: a transform moved in Update or LateUpdate is seen as
        /// moved until the next frame's Update; scene, activation, Warp and updatePosition wait).</summary>
        internal static void SyncTransforms()
        {
            foreach (var agent in s_Agents)
            {
                if (agent.m_Crowd == null) continue;
                FollowTransform(agent);
                if (agent.m_TransformPending && agent.updatePosition) WriteTransform(agent);
            }
        }

        /// <summary>A transform moved by script moves the agent (constrained to the NavMesh; freely
        /// while it waits on a link; a completed link's end wins over the transform). The transform
        /// is written back at the next early update.</summary>
        internal static void FollowTransform(NavMeshAgent agent)
        {
            if (agent.m_Crowd == null || !agent.updatePosition || agent.m_LinkCompleting
                || (agent.transform.position - agent.WrittenPosition).sqrMagnitude <= 1e-8f) return;
            var moved = agent.transform.position;
            // Recorded first: the nextPosition setter syncs the agent again.
            agent.WrittenPosition = moved;
            agent.m_TransformPending = true;
            if (agent.WaitingOnLink) agent.FollowTransformOnLink();
            else agent.nextPosition = moved;
        }

        /// <summary>The transform is written at the agent's next early update.</summary>
        internal static void DeferTransform(NavMeshAgent agent)
        {
            agent.WrittenPosition = agent.transform.position;
            agent.m_TransformPending = true;
        }

        /// <summary>The frame's agent update: path requests, then the crowds.</summary>
        internal static void Step()
        {
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
            var p = new DtObstacleAvoidanceParams(crowd.GetObstacleAvoidanceParams(0));
            (int Divs, int Rings, int Depth)[] presets = { (5, 2, 1), (5, 2, 2), (7, 2, 3), (7, 3, 3) };
            for (int i = 0; i < presets.Length; i++)
            {
                // Unity's sampling (measured, tests/projects/avoidance): Detour's adaptive patterns
                // around 0.7 of the desired velocity, a little to its right; candidates weighed by
                // their distance from the desired and current velocities and their time to impact
                // within the prediction time, no side penalty.
                var q = new DtObstacleAvoidanceParams(p);
                q.velBias = 0.7f;
                q.sideOffset = 0.04f;
                q.unityVelocity = true;
                q.weightDesVel = 1f;
                q.weightCurVel = 0.5f;
                q.weightSide = 0f;
                q.weightToi = 2.5f;
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
                flags |= DtCrowdAgentUpdateFlags.DT_CROWD_OBSTACLE_AVOIDANCE | DtCrowdAgentUpdateFlags.DT_CROWD_SEPARATION
                    | DtCrowdAgentUpdateFlags.UNBOUND_AVOID_EDGES;
            if (!agent.autoBraking) flags |= DtCrowdAgentUpdateFlags.UNBOUND_NO_BRAKING;
            return new DtCrowdAgentParams
            {
                radius = agent.radius,
                height = agent.height,
                maxAcceleration = agent.acceleration,
                maxSpeed = agent.isStopped ? 0f : agent.speed,
                // Every neighbour the agent could meet within the prediction time (it may go and
                // the neighbour come at full speed).
                collisionQueryRange = agent.radius * 2f + 2f * Math.Max(agent.speed, 3.5f) * NavMesh.avoidancePredictionTime,
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
                bool following = !onLink && destination.HasValue && agent.hasPath;
                // Within its last centimetre (hasPath already false) and still moving to the end; an
                // agent stopped within its stoppingDistance stays stopped.
                bool finishing = !onLink && destination.HasValue && !following && !agent.pathPending
                    && agent.m_Crowd.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_VALID
                    && FlatRemaining(agent.m_Crowd) < 0.01f;
                var moving = agent.m_Crowd;
                agent.m_Crowd = null;
                agent.m_Surface = null;
                if (old != null && RejoinOnLink(agent, old))
                {
                    if (agent.pathPending) RestartRequest(agent);
                    else if (destination.HasValue) PlanFromLinkEnd(agent);
                    continue;
                }
                Place(agent, agent.m_NextPosition);
                if (onLink)
                {
                    if (destination.HasValue && agent.m_Crowd != null) RestartRequest(agent);
                    continue;
                }
                if (agent.m_Crowd == null)
                {
                    // Off the new NavMesh no path can be computed.
                    agent.SetPathPending(false);
                    continue;
                }
                if (agent.pathPending) RestartRequest(agent);
                else if (following) PlanAgain(agent, moving);
                else if (finishing)
                {
                    PlanAgain(agent, moving);
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

        /// <summary>The path of an agent that was following one, planned again on the rebuilt
        /// NavMesh from where the agent now stands to its destination; the agent keeps its velocity
        /// (see the note on <see cref="Rebuild"/>).</summary>
        static void PlanAgain(NavMeshAgent agent, DtCrowdAgent moving)
        {
            var ag = agent.m_Crowd;
            ag.vel = moving.vel;
            ag.nvel = moving.nvel;
            ag.pvel = moving.pvel;
            ag.dvel = moving.dvel;
            ag.desiredSpeed = moving.desiredSpeed;
            var search = NavMesh.PathSearch.Begin(agent.m_Surface, agent.m_NextPosition, agent.RequestedDestination, agent.Filter, agent.agentTypeID);
            int budget = int.MaxValue;
            if (search != null && search.Step(ref budget) && search.Result != null)
            {
                ApplyPath(agent, search.Result);
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
            if (search != null && search.Step(ref budget) && search.Result != null) ApplyPath(agent, search.Result, from);
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

        internal static void RequestPath(NavMeshAgent agent)
        {
            s_Searches.Remove(agent);
            s_Restarted.Remove(agent);
            if (!s_PathRequests.Contains(agent)) s_PathRequests.Add(agent);
        }

        static readonly Dictionary<NavMeshAgent, NavMesh.PathSearch> s_Searches = new Dictionary<NavMeshAgent, NavMesh.PathSearch>();
        /// <summary>Agents whose search under way a NavMesh rebuild restarted: finished in one go.</summary>
        static readonly HashSet<NavMeshAgent> s_Restarted = new HashSet<NavMeshAgent>();

        /// <summary>Paths for the requests, after the scripts' Update, in request order within the
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
                ApplyPath(agent, search.Result);
            }
        }

        /// <summary>Where the agent's paths start: where it stands; while it waits on a link, the
        /// link's end, where it goes on once a script completes the link (a path from where it
        /// waits would take it back over the link). The navlinks capture measured this with automatic
        /// traversal only: an agent given a new destination on a link finishes the link and follows
        /// the new path from its end; a waiting agent's re-target is unmeasured.</summary>
        static Vector3 PathOrigin(NavMeshAgent agent) =>
            agent.WaitingOnLink ? NavMesh.V(agent.m_Crowd.animation.endPos) : agent.m_NextPosition;

        /// <summary>A path search under way began on the other side of a link the agent now waits
        /// on, or no longer waits on: it begins again from where the agent's paths now start.</summary>
        static void RestartSearch(NavMeshAgent agent)
        {
            s_Searches.Remove(agent);
            s_Restarted.Remove(agent);
        }

        internal static void ApplyPath(NavMeshAgent agent, NavMeshPath path) => ApplyPath(agent, path, null);

        /// <summary>The agent follows the path; its corridor starts at <paramref name="from"/>, else
        /// where it stands, or, waiting on a link, where the path starts.</summary>
        static void ApplyPath(NavMeshAgent agent, NavMeshPath path, RcVec3f? from)
        {
            var crowd = CrowdOf(agent.m_Surface);
            var ag = agent.m_Crowd;
            var polys = path.m_Polygons;
            var target = path.m_Corners.Length > 0 ? path.m_Corners[path.m_Corners.Length - 1] : agent.m_NextPosition;
            var start = from ?? (agent.WaitingOnLink && path.m_Corners.Length > 0 ? NavMesh.V(path.m_Corners[0]) : ag.npos);
            ag.corridor.Reset(polys[0], start);
            ag.corridor.SetCorridor(NavMesh.V(target), polys, polys.Length);
            ag.boundary.Reset();
            ag.partial = path.status == NavMeshPathStatus.PathPartial;
            ag.targetRef = polys[polys.Length - 1];
            ag.targetPos = NavMesh.V(target);
            ag.targetState = DtMoveRequestState.DT_CROWDAGENT_TARGET_VALID;
            ag.targetReplanTime = 0f;
            ag.targetReplan = false;
            agent.SetPathPending(false);
            agent.SetArrived(false);
            agent.SetPathStatus(path.status);
            agent.PathRenewed();
            // The corners the agent steers by, for this frame's queries.
            ag.ncorners = ag.corridor.FindCorners(ag.corners, DtCrowdConst.DT_CROWDAGENT_MAX_CORNERS, crowd.GetNavMeshQuery(), crowd.GetFilter(0));
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
                FollowTransform(agent);
                if (agent.m_Crowd != null)
                {
                    CrowdOf(agent.m_Surface).UpdateAgentParameters(agent.m_Crowd, Params(agent));
                }
            }
            if (dt <= 0f) return;
            if (s_ConfiguredPrediction != NavMesh.avoidancePredictionTime)
                foreach (var crowd in s_Crowds.Values) ConfigureAvoidance(crowd);
            foreach (var agent in s_Agents)
                if (agent.m_Crowd != null) PrepareLink(agent);
            foreach (var pair in s_Crowds)
            {
                // The enabled NavMeshObstacles, as they stand now, are among what the agents avoid.
                ObstacleSystem.Feed(pair.Value, pair.Key);
                pair.Value.Update(dt, null);
            }
            foreach (var agent in s_Agents)
            {
                if (agent.m_Crowd == null) continue;
                if (agent.m_LinkCompleting)
                {
                    LeaveLink(agent);
                    continue;
                }
                if (agent.m_StepWalking && agent.m_Crowd.animation.polyRef != 0) ReachLink(agent);
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

        /// <summary>The update after CompleteOffMeshLink: the agent stays at the link's end, its
        /// transform and reported velocity as they were; it walks on from the next update at its
        /// planned velocity (navlinks capture: full speed at once).</summary>
        static void LeaveLink(NavMeshAgent agent)
        {
            var ag = agent.m_Crowd;
            ag.state = DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING;
            ag.vel = ag.pvel;
            agent.m_LinkCompleting = false;
            // The transform where it stands is not a script's move back to it.
            agent.WrittenPosition = agent.transform.position;
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
            agent.m_TransformPending = false;
        }

        static void Rotate(NavMeshAgent agent, float dt)
        {
            var v = agent.velocity;
            v.y = 0f;
            if (v.sqrMagnitude < 1e-6f) return;
            var target = Quaternion.LookRotation(v);
            agent.transform.rotation = Quaternion.RotateTowards(agent.transform.rotation, target, agent.angularSpeed * dt);
        }

        internal static bool LinkOccupied(int linkId)
        {
            foreach (var agent in s_Agents)
                if (agent.isOnOffMeshLink && agent.m_LinkId == linkId) return true;
            return false;
        }
    }
}
