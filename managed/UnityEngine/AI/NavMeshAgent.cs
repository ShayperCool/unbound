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
        bool m_OnNavMesh;
        int m_WorldVersion = -1;
        internal int m_LinkId;

        public NavMeshAgent() { }

        // ------------------------------------------------------------------ settings

        public int agentTypeID { get => m_AgentTypeID; set { m_AgentTypeID = value; AgentSystem.Reattach(this); } }
        public float radius { get => m_Radius; set { m_Radius = value; ApplyParams(); } }
        public float height { get => m_Height; set { m_Height = value; ApplyParams(); } }
        public float baseOffset { get => m_BaseOffset; set => m_BaseOffset = value; }
        public float speed { get => m_Speed; set { m_Speed = Mathf.Max(0f, value); ApplyParams(); } }
        public float angularSpeed { get => m_AngularSpeed; set => m_AngularSpeed = value; }
        public float acceleration { get => m_Acceleration; set { m_Acceleration = Mathf.Max(0f, value); ApplyParams(); } }
        public float stoppingDistance { get => m_StoppingDistance; set => m_StoppingDistance = Mathf.Max(0f, value); }
        public bool autoTraverseOffMeshLink { get => m_AutoTraverseOffMeshLink; set => m_AutoTraverseOffMeshLink = value; }
        public bool autoBraking { get => m_AutoBraking; set => m_AutoBraking = value; }
        public bool autoRepath { get => m_AutoRepath; set => m_AutoRepath = value; }
        public int areaMask { get => m_AreaMask; set => m_AreaMask = value; }

        [Obsolete("Use areaMask instead.")]
        public int walkableMask { get => m_AreaMask; set => m_AreaMask = value; }

        public ObstacleAvoidanceType obstacleAvoidanceType { get => m_ObstacleAvoidanceType; set { m_ObstacleAvoidanceType = value; ApplyParams(); } }
        public int avoidancePriority { get => m_AvoidancePriority; set { m_AvoidancePriority = value; ApplyParams(); } }
        public bool updatePosition { get => m_UpdatePosition; set => m_UpdatePosition = value; }
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

        public void SetAreaCost(int areaIndex, float areaCost)
        {
            m_AreaCosts ??= (float[])NavMeshAreas.Costs.Clone();
            if (areaIndex >= 0 && areaIndex < 32) m_AreaCosts[areaIndex] = areaCost;
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

        public Vector3 nextPosition
        {
            get
            {
                AgentSystem.Sync(this);
                return m_NextPosition + Vector3.up * m_BaseOffset;
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

        public bool isPathStale => false;

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

        public Object navMeshOwner => null;

        // ------------------------------------------------------------------ queries

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

        public bool isOnOffMeshLink => m_Crowd != null && m_Crowd.state == DtCrowdAgentState.DT_CROWDAGENT_STATE_OFFMESH;

        public OffMeshLinkData currentOffMeshLinkData => GetCurrentOffMeshLinkDataInternal();

        public OffMeshLinkData nextOffMeshLinkData => GetNextOffMeshLinkDataInternal();

        internal OffMeshLinkData GetCurrentOffMeshLinkDataInternal()
        {
            var data = new OffMeshLinkData();
            if (!isOnOffMeshLink) return data;
            var anim = m_Crowd.animation;
            data.m_Valid = 1;
            data.m_Activated = 1;
            data.m_StartPos = NavMesh.V(anim.startPos);
            data.m_EndPos = NavMesh.V(anim.endPos);
            var link = NavWorld.FindLink(m_LinkId);
            data.m_InstanceID = link?.Owner ?? 0;
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
            }
            return data;
        }

        public void ActivateCurrentOffMeshLink(bool activated)
        {
            if (m_LinkId != 0) NavMesh.SetOffMeshConnectionActive(m_LinkId, activated);
        }

        public void CompleteOffMeshLink()
        {
            if (!isOnOffMeshLink) return;
            m_Crowd.state = DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING;
            m_Crowd.npos = m_Crowd.animation.endPos;
            m_NextPosition = NavMesh.V(m_Crowd.npos);
            m_LinkId = 0;
            if (m_UpdatePosition) AgentSystem.WriteTransform(this);
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

        internal override void OnEngineEnable() => AgentSystem.Add(this);

        internal override void OnEngineDisable() => AgentSystem.Remove(this);

        internal override void OnEngineDestroy() => AgentSystem.Remove(this);

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
            PlayerLoopDriver.Register(typeof(PL.PreLateUpdate.AIUpdatePostScript), () =>
            {
                ProcessRequests();
                UpdateCrowds();
            });
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
            Detach(agent);
        }

        static void Detach(NavMeshAgent agent)
        {
            if (agent.m_Crowd != null && agent.m_Surface != null && s_Crowds.TryGetValue(agent.m_Surface, out var crowd))
                crowd.RemoveAgent(agent.m_Crowd);
            agent.m_Crowd = null;
            agent.m_Surface = null;
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
        static void ConfigureAvoidance(DtCrowd crowd)
        {
            var p = new DtObstacleAvoidanceParams(crowd.GetObstacleAvoidanceParams(0));
            (int Divs, int Rings, int Depth)[] presets = { (5, 2, 1), (5, 2, 2), (7, 2, 3), (7, 3, 3) };
            for (int i = 0; i < presets.Length; i++)
            {
                var q = new DtObstacleAvoidanceParams(p);
                q.velBias = 0.5f;
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
            if (agent.obstacleAvoidanceType != ObstacleAvoidanceType.NoObstacleAvoidance) flags |= DtCrowdAgentUpdateFlags.DT_CROWD_OBSTACLE_AVOIDANCE;
            if (!agent.autoBraking) flags |= DtCrowdAgentUpdateFlags.UNBOUND_NO_BRAKING;
            return new DtCrowdAgentParams
            {
                radius = agent.radius,
                height = agent.height,
                maxAcceleration = agent.acceleration,
                maxSpeed = agent.isStopped ? 0f : agent.speed,
                collisionQueryRange = agent.radius * 12f,
                pathOptimizationRange = agent.radius * 30f,
                separationWeight = 0f,
                updateFlags = flags,
                obstacleAvoidanceType = Math.Max(0, (int)agent.obstacleAvoidanceType - 1),
                queryFilterType = 0,
                userData = agent,
                avoidancePriority = agent.avoidancePriority,
            };
        }

        /// <summary>The crowds follow the NavMesh: when it was rebuilt, agents rejoin at their places.</summary>
        static void Rebuild()
        {
            s_CrowdVersion = NavWorld.Version;
            s_Crowds.Clear();
            foreach (var agent in s_Agents.ToArray())
            {
                var destination = agent.HasDestination ? agent.RequestedDestination : (Vector3?)null;
                agent.m_Crowd = null;
                agent.m_Surface = null;
                Place(agent, agent.m_NextPosition);
                if (destination.HasValue && agent.m_Crowd != null) RequestPath(agent);
            }
        }

        /// <summary>The agent's NavMesh changed since it was placed: place it again.</summary>
        internal static void Sync(NavMeshAgent agent)
        {
            if (s_CrowdVersion != NavWorld.Version) Rebuild();
            if (agent.m_Crowd == null && s_Agents.Contains(agent) && agent.WorldVersion != NavWorld.Version)
                Place(agent, agent.transform.position - Vector3.up * agent.baseOffset);
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
            if (agent.updatePosition) WriteTransform(agent);
            return true;
        }

        internal static void RequestPath(NavMeshAgent agent)
        {
            s_Searches.Remove(agent);
            if (!s_PathRequests.Contains(agent)) s_PathRequests.Add(agent);
        }

        static readonly Dictionary<NavMeshAgent, NavMesh.PathSearch> s_Searches = new Dictionary<NavMeshAgent, NavMesh.PathSearch>();

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
                    if (agent != null) s_Searches.Remove(agent);
                    continue;
                }
                if (!s_Searches.TryGetValue(agent, out var search))
                {
                    search = NavMesh.PathSearch.Begin(agent.m_Surface, agent.m_NextPosition, agent.RequestedDestination, agent.Filter, agent.agentTypeID);
                    if (search == null)
                    {
                        s_PathRequests.RemoveAt(0);
                        agent.SetPathPending(false);
                        agent.SetPathStatus(NavMeshPathStatus.PathInvalid);
                        continue;
                    }
                    s_Searches[agent] = search;
                }
                if (!search.Step(ref budget)) break;
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

        internal static void ApplyPath(NavMeshAgent agent, NavMeshPath path)
        {
            var crowd = CrowdOf(agent.m_Surface);
            var ag = agent.m_Crowd;
            var polys = path.m_Polygons;
            var target = path.m_Corners.Length > 0 ? path.m_Corners[path.m_Corners.Length - 1] : agent.m_NextPosition;
            ag.corridor.Reset(polys[0], ag.npos);
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
                // A transform moved by script moves the agent (constrained to the NavMesh).
                if (agent.m_Crowd != null && agent.updatePosition && (agent.transform.position - agent.WrittenPosition).sqrMagnitude > 1e-8f)
                    agent.nextPosition = agent.transform.position;
                if (agent.m_Crowd != null)
                {
                    CrowdOf(agent.m_Surface).UpdateAgentParameters(agent.m_Crowd, Params(agent));
                }
            }
            if (dt <= 0f) return;
            foreach (var crowd in s_Crowds.Values) crowd.Update(dt, null);
            foreach (var agent in s_Agents)
            {
                if (agent.m_Crowd == null) continue;
                var previous = agent.m_NextPosition;
                agent.m_NextPosition = NavMesh.V(agent.m_Crowd.npos);
                agent.m_Velocity = agent.m_Crowd.vel.LengthSquared() > 0f ? (agent.m_NextPosition - previous) / dt : Vector3.zero;
                StopAtDestination(agent, dt);
                if (agent.updatePosition) WriteTransform(agent);
                if (agent.updateRotation) Rotate(agent, dt);
            }
        }

        /// <summary>The end of the path (measured): within the stopping distance an agent stops;
        /// within 0.01 its path no longer counts as one (hasPath is false) while it closes in;
        /// a step that would pass the end lands on it and the agent stands.</summary>
        static void StopAtDestination(NavMeshAgent agent, float dt)
        {
            var ag = agent.m_Crowd;
            if (ag.targetState != DtMoveRequestState.DT_CROWDAGENT_TARGET_VALID || ag.ncorners == 0) return;
            var last = ag.corners[ag.ncorners - 1];
            if ((last.flags & DtStraightPathFlags.DT_STRAIGHTPATH_END) == 0) return;
            float left = 0f;
            var p = ag.npos;
            for (int i = 0; i < ag.ncorners; i++)
            {
                left += RcVec.Dist2D(p, ag.corners[i].pos);
                p = ag.corners[i].pos;
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
            if (left <= 1e-3f || (agent.autoBraking && step >= left))
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
