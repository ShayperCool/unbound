/*
Copyright (c) 2009-2010 Mikko Mononen memon@inside.org
recast4j copyright (c) 2015-2019 Piotr Piastucki piotr@jtilia.org
DotRecast Copyright (c) 2023-2024 Choi Ikpil ikpil@naver.com

This software is provided 'as-is', without any express or implied
warranty.  In no event will the authors be held liable for any damages
arising from the use of this software.
Permission is granted to anyone to use this software for any purpose,
including commercial applications, and to alter it and redistribute it
freely, subject to the following restrictions:
1. The origin of this software must not be misrepresented; you must not
 claim that you wrote the original software. If you use this software
 in a product, an acknowledgment in the product documentation would be
 appreciated but is not required.
2. Altered source versions must be plainly marked as such, and must not be
 misrepresented as being the original software.
3. This notice may not be removed or altered from any source distribution.
*/

using System;
using System.Collections.Generic;
using System.Diagnostics;
using DotRecast.Core;
using DotRecast.Core.Collections;
using DotRecast.Core.Numerics;

namespace DotRecast.Detour.Crowd
{
    ///////////////////////////////////////////////////////////////////////////

// This section contains detailed documentation for members that don't have
// a source file. It reduces clutter in the main section of the header.

    /**

    @defgroup crowd Crowd

    Members in this module implement local steering and dynamic avoidance features.

    The crowd is the big beast of the navigation features. It not only handles a
    lot of the path management for you, but also local steering and dynamic
    avoidance between members of the crowd. I.e. It can keep your agents from
    running into each other.

    Main class: #dtCrowd

    The #dtNavMeshQuery and #dtPathCorridor classes provide perfectly good, easy
    to use path planning features. But in the end they only give you points that
    your navigation client should be moving toward. When it comes to deciding things
    like agent velocity and steering to avoid other agents, that is up to you to
    implement. Unless, of course, you decide to use #dtCrowd.

    Basically, you add an agent to the crowd, providing various configuration
    settings such as maximum speed and acceleration. You also provide a local
    target to more toward. The crowd manager then provides, with every update, the
    new agent position and velocity for the frame. The movement will be
    constrained to the navigation mesh, and steering will be applied to ensure
    agents managed by the crowd do not collide with each other.

    This is very powerful feature set. But it comes with limitations.

    The biggest limitation is that you must give control of the agent's position
    completely over to the crowd manager. You can update things like maximum speed
    and acceleration. But in order for the crowd manager to do its thing, it can't
    allow you to constantly be giving it overrides to position and velocity. So
    you give up direct control of the agent's movement. It belongs to the crowd.

    The second biggest limitation revolves around the fact that the crowd manager
    deals with local planning. So the agent's target should never be more than
    256 polygons aways from its current position. If it is, you risk
    your agent failing to reach its target. So you may still need to do long
    distance planning and provide the crowd manager with intermediate targets.

    Other significant limitations:

    - All agents using the crowd manager will use the same #dtQueryFilter.
    - Crowd management is relatively expensive. The maximum agents under crowd
      management at any one time is between 20 and 30.  A good place to start
      is a maximum of 25 agents for 0.5ms per frame.

    @note This is a summary list of members.  Use the index or search
    feature to find minor members.

    @struct dtCrowdAgentParams
    @see dtCrowdAgent, dtCrowd::addAgent(), dtCrowd::updateAgentParameters()

    @var dtCrowdAgentParams::obstacleAvoidanceType
    @par

    #dtCrowd permits agents to use different avoidance configurations.  This value
    is the index of the #dtObstacleAvoidanceParams within the crowd.

    @see dtObstacleAvoidanceParams, dtCrowd::setObstacleAvoidanceParams(),
         dtCrowd::getObstacleAvoidanceParams()

    @var dtCrowdAgentParams::collisionQueryRange
    @par

    Collision elements include other agents and navigation mesh boundaries.

    This value is often based on the agent radius and/or maximum speed. E.g. radius * 8

    @var dtCrowdAgentParams::pathOptimizationRange
    @par

    Only applicable if #updateFlags includes the #DT_CROWD_OPTIMIZE_VIS flag.

    This value is often based on the agent radius. E.g. radius * 30

    @see dtPathCorridor::optimizePathVisibility()

    @var dtCrowdAgentParams::separationWeight
    @par

    A higher value will result in agents trying to stay farther away from each other at
    the cost of more difficult steering in tight spaces.
    */
    /// Provides local steering behaviors for a group of agents. 
    /// @ingroup crowd
    internal class DtCrowd
    {
        private readonly RcAtomicInteger _agentIdx;
        private readonly Dictionary<int, DtCrowdAgent> _agents;
        private readonly List<DtCrowdAgent> _activeAgents;

        private readonly DtPathQueue _pathQ;

        private readonly DtObstacleAvoidanceParams[] _obstacleQueryParams;
        private readonly DtObstacleAvoidanceQuery _obstacleQuery;

        private readonly DtProximityGrid _grid;

        private int _maxPathResult;
        private readonly RcVec3f _agentPlacementHalfExtents;

        private readonly IDtQueryFilter[] _filters;

        private readonly DtCrowdConfig _config;
        private int _velocitySampleCount;

        private DtNavMeshQuery _navQuery;

        private DtNavMesh _navMesh;
        private readonly DtCrowdTelemetry _telemetry = new DtCrowdTelemetry();

        // Reused per-update scratch queues, see UpdateMoveRequest / UpdateTopologyOptimization.
        private readonly List<DtCrowdAgent> _moveRequestQueue = new List<DtCrowdAgent>();
        private readonly List<DtCrowdAgent> _topologyOptQueue = new List<DtCrowdAgent>();

        public DtCrowd(DtCrowdConfig config, DtNavMesh nav) : this(config, nav, i => new DtQueryDefaultFilter())
        {
        }

        public DtCrowd(DtCrowdConfig config, DtNavMesh nav, Func<int, IDtQueryFilter> queryFilterFactory)
        {
            _config = config;
            _agentPlacementHalfExtents = new RcVec3f(config.maxAgentRadius * 2.0f, config.maxAgentRadius * 1.5f, config.maxAgentRadius * 2.0f);

            _obstacleQuery = new DtObstacleAvoidanceQuery(config.maxObstacleAvoidanceCircles, config.maxObstacleAvoidanceSegments);

            _filters = new IDtQueryFilter[DtCrowdConst.DT_CROWD_MAX_QUERY_FILTER_TYPE];
            for (int i = 0; i < DtCrowdConst.DT_CROWD_MAX_QUERY_FILTER_TYPE; i++)
            {
                _filters[i] = queryFilterFactory.Invoke(i);
            }

            // Init obstacle query option.
            _obstacleQueryParams = new DtObstacleAvoidanceParams[DtCrowdConst.DT_CROWD_MAX_OBSTAVOIDANCE_PARAMS];
            for (int i = 0; i < DtCrowdConst.DT_CROWD_MAX_OBSTAVOIDANCE_PARAMS; ++i)
            {
                _obstacleQueryParams[i] = new DtObstacleAvoidanceParams();
            }

            // Allocate temp buffer for merging paths.
            _maxPathResult = DtCrowdConst.MAX_PATH_RESULT;
            _pathQ = new DtPathQueue(config);
            _agentIdx = new RcAtomicInteger(0);
            _agents = new Dictionary<int, DtCrowdAgent>();
            _activeAgents = new List<DtCrowdAgent>();
            _grid = new DtProximityGrid(_config.maxAgentRadius * 3);

            //
            _pathQ.Init(_maxPathResult);

            // The navQuery is mostly used for local searches, no need for large node pool.
            SetNavMesh(nav);
        }

        public void SetNavMesh(DtNavMesh nav)
        {
            _navMesh = nav;
            _navQuery = new DtNavMeshQuery(nav);
        }

        public DtNavMesh GetNavMesh()
        {
            return _navMesh;
        }

        public DtNavMeshQuery GetNavMeshQuery()
        {
            return _navQuery;
        }

        /// Sets the shared avoidance configuration for the specified index.
        /// @param[in] idx The index. [Limits: 0 <= value <
        /// #DT_CROWD_MAX_OBSTAVOIDANCE_PARAMS]
        /// @param[in] option The new configuration.
        public void SetObstacleAvoidanceParams(int idx, DtObstacleAvoidanceParams option)
        {
            if (idx >= 0 && idx < DtCrowdConst.DT_CROWD_MAX_OBSTAVOIDANCE_PARAMS)
            {
                _obstacleQueryParams[idx] = new DtObstacleAvoidanceParams(option);
            }
        }

        /// Gets the shared avoidance configuration for the specified index.
        /// @param[in] idx The index of the configuration to retreive.
        /// [Limits: 0 <= value < #DT_CROWD_MAX_OBSTAVOIDANCE_PARAMS]
        /// @return The requested configuration.
        public DtObstacleAvoidanceParams GetObstacleAvoidanceParams(int idx)
        {
            if (idx >= 0 && idx < DtCrowdConst.DT_CROWD_MAX_OBSTAVOIDANCE_PARAMS)
            {
                return _obstacleQueryParams[idx];
            }

            return null;
        }

        /// Updates the specified agent's configuration.
        /// @param[in] idx The agent index. [Limits: 0 <= value < #GetAgentCount()]
        /// @param[in] params The new agent configuration.
        public void UpdateAgentParameters(DtCrowdAgent agent, DtCrowdAgentParams option)
        {
            agent.option = option;
        }

        /// @par
        ///
        /// The agent's position will be constrained to the surface of the navigation mesh.
        /// Adds a new agent to the crowd.
        ///  @param[in]		pos		The requested position of the agent. [(x, y, z)]
        ///  @param[in]		params	The configuration of the agent.
        /// @return The index of the agent in the agent pool. Or -1 if the agent could not be added.
        public DtCrowdAgent AddAgent(RcVec3f pos, DtCrowdAgentParams option)
        {
            int idx = _agentIdx.GetAndIncrement();
            DtCrowdAgent ag = new DtCrowdAgent(idx);
            ag.corridor.Init(_maxPathResult);
            AddAgent(ag);
            UpdateAgentParameters(ag, option);

            // Find nearest position on navmesh and place the agent there.
            var status = _navQuery.FindNearestPoly(pos, _agentPlacementHalfExtents, _filters[ag.option.queryFilterType], out var refs, out var nearestPt, out var _);
            if (status.Failed())
            {
                nearestPt = pos;
                refs = 0;
            }

            ag.corridor.Reset(refs, nearestPt);
            ag.boundary.Reset();
            ag.partial = false;

            ag.topologyOptTime = 0;
            ag.targetReplanTime = 0;
            ag.nneis = 0;

            ag.dvel = RcVec3f.Zero;
            ag.nvel = RcVec3f.Zero;
            ag.pvel = RcVec3f.Zero;
            ag.vel = RcVec3f.Zero;
            ag.npos = nearestPt;

            ag.desiredSpeed = 0;

            if (refs != 0)
            {
                ag.state = DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING;
            }
            else
            {
                ag.state = DtCrowdAgentState.DT_CROWDAGENT_STATE_INVALID;
            }

            ag.targetState = DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE;

            return ag;
        }

        public DtCrowdAgent GetAgent(int idx)
        {
            return _agents.GetValueOrDefault(idx);
        }

        // Add the agent from the crowd.
        public void AddAgent(DtCrowdAgent agent)
        {
            if (_agents.TryAdd(agent.idx, agent))
            {
                _activeAgents.Add(agent);
            }
        }

        // Removes the agent from the crowd.
        public void RemoveAgent(DtCrowdAgent agent)
        {
            if (_agents.Remove(agent.idx))
            {
                _activeAgents.Remove(agent);
            }
        }

        private bool RequestMoveTargetReplan(DtCrowdAgent ag, long refs, RcVec3f pos)
        {
            ag.SetTarget(refs, pos);
            ag.targetReplan = true;
            return true;
        }

        /// Submits a new move request for the specified agent.
        /// @param[in] idx The agent index. [Limits: 0 <= value < #GetAgentCount()]
        /// @param[in] ref The position's polygon reference.
        /// @param[in] pos The position within the polygon. [(x, y, z)]
        /// @return True if the request was successfully submitted.
        ///
        /// This method is used when a new target is set.
        ///
        /// The position will be constrained to the surface of the navigation mesh.
        ///
        /// The request will be processed during the next #Update().
        public bool RequestMoveTarget(DtCrowdAgent agent, long refs, RcVec3f pos)
        {
            if (refs == 0)
            {
                return false;
            }

            // Initialize request.
            agent.SetTarget(refs, pos);
            agent.targetReplan = false;
            return true;
        }

        /// Submits a new move request for the specified agent.
        /// @param[in] idx The agent index. [Limits: 0 <= value < #GetAgentCount()]
        /// @param[in] vel The movement velocity. [(x, y, z)]
        /// @return True if the request was successfully submitted.
        public bool RequestMoveVelocity(DtCrowdAgent agent, RcVec3f vel)
        {
            // Initialize request.
            agent.targetRef = 0;
            agent.targetPos = vel;
            agent.targetPathQueryResult = null;
            agent.targetReplan = false;
            agent.targetState = DtMoveRequestState.DT_CROWDAGENT_TARGET_VELOCITY;

            return true;
        }

        /// Resets any request for the specified agent.
        /// @param[in] idx The agent index. [Limits: 0 <= value < #GetAgentCount()]
        /// @return True if the request was successfully reseted.
        public bool ResetMoveTarget(DtCrowdAgent agent)
        {
            // Initialize request.
            agent.targetRef = 0;
            agent.targetPos = RcVec3f.Zero;
            agent.dvel = RcVec3f.Zero;
            agent.targetPathQueryResult = null;
            agent.targetReplan = false;
            agent.targetState = DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE;
            return true;
        }

        /**
     * Gets the active agents int the agent pool.
     *
     * @return List of active agents
     */
        public IList<DtCrowdAgent> GetActiveAgents()
        {
            return _activeAgents;
        }

        public RcVec3f GetQueryExtents()
        {
            return _agentPlacementHalfExtents;
        }

        public IDtQueryFilter GetFilter(int i)
        {
            return i >= 0 && i < DtCrowdConst.DT_CROWD_MAX_QUERY_FILTER_TYPE ? _filters[i] : null;
        }

        public DtProximityGrid GetGrid()
        {
            return _grid;
        }

        public DtPathQueue GetPathQueue()
        {
            return _pathQ;
        }

        public DtCrowdTelemetry Telemetry()
        {
            return _telemetry;
        }

        public DtCrowdConfig Config()
        {
            return _config;
        }

        public DtCrowdTelemetry Update(float dt, DtCrowdAgentDebugInfo debug)
        {
            _velocitySampleCount = 0;

            _telemetry.Start();

            IList<DtCrowdAgent> agents = GetActiveAgents();

            // Check that all agents still have valid paths.
            CheckPathValidity(agents, dt);

            // Update async move request and path finder.
            UpdateMoveRequest(agents, dt);

            // Optimize path topology.
            UpdateTopologyOptimization(agents, dt);

            // Register agents to proximity grid.
            BuildProximityGrid(agents);

            // Get nearby navmesh segments and agents to collide with.
            BuildNeighbours(agents);

            // Find next corner to steer to.
            FindCorners(agents, debug);

            // Unbound: Unity's agents take the step towards the last plan before steering and
            // planning anew, and steer and plan with the velocity they move at this update (measured).
            Accelerate(dt, agents);

            // Calculate steering.
            CalculateSteering(agents, dt);

            // Velocity planning.
            PlanVelocity(debug, agents);

            // Integrate.
            Integrate(dt, agents);

            // Handle collisions.
            HandleCollisions(agents);

            MoveAgents(agents);

            // Unbound: Unity's agents enter a link from where this update's step took them (the
            // trigger radius measured from the moved position), and take their first step along
            // it in the same update (measured, tests/projects/navlinks).
            TriggerOffMeshConnections(agents);

            // Update agents using off-mesh connection.
            UpdateOffMeshConnections(agents, dt);
            return _telemetry;
        }


        private void CheckPathValidity(IList<DtCrowdAgent> agents, float dt)
        {
            using var timer = _telemetry.ScopedTimer(DtCrowdTimerLabel.CheckPathValidity);

            for (var i = 0; i < agents.Count; i++)
            {
                var ag = agents[i];
                if (ag.state != DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING)
                {
                    continue;
                }

                ag.targetReplanTime += dt;

                bool replan = false;

                // First check that the current location is valid.
                RcVec3f agentPos = new RcVec3f();
                long agentRef = ag.corridor.GetFirstPoly();
                agentPos = ag.npos;
                if (!_navQuery.IsValidPolyRef(agentRef, _filters[ag.option.queryFilterType]))
                {
                    // Current location is not valid, try to reposition.
                    // TODO: this can snap agents, how to handle that?
                    _navQuery.FindNearestPoly(ag.npos, _agentPlacementHalfExtents, _filters[ag.option.queryFilterType], out agentRef, out var nearestPt, out var _);
                    agentPos = nearestPt;

                    if (agentRef == 0)
                    {
                        // Could not find location in navmesh, set state to invalid.
                        ag.corridor.Reset(0, agentPos);
                        ag.partial = false;
                        ag.boundary.Reset();
                        ag.state = DtCrowdAgentState.DT_CROWDAGENT_STATE_INVALID;
                        continue;
                    }

                    // Make sure the first polygon is valid, but leave other valid
                    // polygons in the path so that replanner can adjust the path
                    // better.
                    ag.corridor.FixPathStart(agentRef, agentPos);
                    // ag.corridor.TrimInvalidPath(agentRef, agentPos, m_navquery,
                    // &m_filter);
                    ag.boundary.Reset();
                    ag.npos = agentPos;

                    replan = true;
                }

                // If the agent does not have move target or is controlled by
                // velocity, no need to recover the target nor replan.
                if (ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE
                    || ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_VELOCITY)
                {
                    continue;
                }

                // Try to recover move request position.
                if (ag.targetState != DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE
                    && ag.targetState != DtMoveRequestState.DT_CROWDAGENT_TARGET_FAILED)
                {
                    if (!_navQuery.IsValidPolyRef(ag.targetRef, _filters[ag.option.queryFilterType]))
                    {
                        // Current target is not valid, try to reposition.
                        _navQuery.FindNearestPoly(ag.targetPos, _agentPlacementHalfExtents, _filters[ag.option.queryFilterType], out ag.targetRef, out var nearestPt, out var _);
                        ag.targetPos = nearestPt;
                        replan = true;
                    }

                    if (ag.targetRef == 0)
                    {
                        // Failed to reposition target, fail moverequest.
                        ag.corridor.Reset(agentRef, agentPos);
                        ag.partial = false;
                        ag.targetState = DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE;
                    }
                }

                // If nearby corridor is not valid, replan.
                if (!ag.corridor.IsValid(_config.checkLookAhead, _navQuery, _filters[ag.option.queryFilterType]))
                {
                    // Fix current path.
                    // ag.corridor.TrimInvalidPath(agentRef, agentPos, m_navquery,
                    // &m_filter);
                    // ag.boundary.Reset();
                    replan = true;
                }

                // Unbound: a partial path is searched again by the engine, as Unity's agents do
                // (with Unity's search; see AgentSystem.ReplanPartialPaths), not here.

                // Try to replan path to goal.
                if (replan)
                {
                    if (ag.targetState != DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE)
                    {
                        RequestMoveTargetReplan(ag, ag.targetRef, ag.targetPos);
                    }
                }
            }
        }

        private void UpdateMoveRequest(IList<DtCrowdAgent> agents, float dt)
        {
            using var timer = _telemetry.ScopedTimer(DtCrowdTimerLabel.UpdateMoveRequest);

            // Longest-waiting agent goes first. The queue is filled completely
            // before it is drained, so a single sort beats a priority queue here.
            List<DtCrowdAgent> queue = _moveRequestQueue;
            queue.Clear();

            // Fire off new requests.
            for (var i = 0; i < agents.Count; i++)
            {
                var ag = agents[i];
                if (ag.state == DtCrowdAgentState.DT_CROWDAGENT_STATE_INVALID)
                {
                    continue;
                }

                if (ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE
                    || ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_VELOCITY)
                {
                    continue;
                }

                if (ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_REQUESTING)
                {
                    Span<long> path = ag.corridor.GetPath();
                    int npath = ag.corridor.GetPathCount();
                    RcDebug.Assert(0 != npath);

                    const int MAX_RES = 32;
                    RcVec3f reqPos = new RcVec3f();
                    RcFixedArray32<long> reqPath = new RcFixedArray32<long>(); // The path to the request location
                    int reqPathCount = 0;

                    RcDebug.Assert(reqPath.Length == MAX_RES);

                    // Quick search towards the goal.
                    const int MAX_ITER = 20;
                    RcDebug.Unused(MAX_ITER); // warning CS0219
                    
                    _navQuery.InitSlicedFindPath(path[0], ag.targetRef, ag.npos, ag.targetPos, _filters[ag.option.queryFilterType], 0);
                    _navQuery.UpdateSlicedFindPath(_config.maxTargetFindPathIterations, out var _);

                    DtStatus status = DtStatus.DT_STATUS_NOTHING;
                    if (ag.targetReplan) // && npath > 10)
                    {
                        // Try to use existing steady path during replan if possible.
                        status = _navQuery.FinalizeSlicedFindPathPartial(path, npath, reqPath.AsSpan(), out reqPathCount, MAX_RES);
                    }
                    else
                    {
                        // Try to move towards target when goal changes.
                        status = _navQuery.FinalizeSlicedFindPath(reqPath.AsSpan(), out reqPathCount, MAX_RES);
                    }

                    if (status.Succeeded() && reqPathCount > 0)
                    {
                        // In progress or succeed.
                        if (reqPath[reqPathCount - 1] != ag.targetRef)
                        {
                            // Partial path, constrain target position inside the
                            // last polygon.
                            var cr = _navQuery.ClosestPointOnPoly(reqPath[reqPathCount - 1], ag.targetPos, out reqPos, out var _);
                            if (cr.Failed())
                            {
                                reqPathCount = 0;
                            }
                        }
                        else
                        {
                            reqPos = ag.targetPos;
                        }
                    }
                    else
                    {
                        // Could not find path, start the request from current location.
                        reqPos = ag.npos;
                        reqPath[0] = path[0];
                        reqPathCount = 1;
                    }

                    ag.corridor.SetCorridor(reqPos, reqPath.AsSpan(), reqPathCount);
                    ag.boundary.Reset();
                    ag.partial = false;

                    if (reqPath[reqPathCount - 1] == ag.targetRef)
                    {
                        ag.targetState = DtMoveRequestState.DT_CROWDAGENT_TARGET_VALID;
                        ag.targetReplanTime = 0;
                    }
                    else
                    {
                        // The path is longer or potentially unreachable, full plan.
                        ag.targetState = DtMoveRequestState.DT_CROWDAGENT_TARGET_WAITING_FOR_QUEUE;
                    }

                    ag.targetReplanWaitTime = 0;
                }

                if (ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_WAITING_FOR_QUEUE)
                {
                    queue.Add(ag);
                }
            }

            queue.Sort((a1, a2) => a2.targetReplanTime.CompareTo(a1.targetReplanTime));
            for (var i = 0; i < queue.Count; i++)
            {
                DtCrowdAgent ag = queue[i];
                ag.targetPathQueryResult = _pathQ.Request(ag.corridor.GetLastPoly(), ag.targetRef, ag.corridor.GetTarget(), ag.targetPos, _filters[ag.option.queryFilterType]);
                if (ag.targetPathQueryResult != null)
                {
                    ag.targetState = DtMoveRequestState.DT_CROWDAGENT_TARGET_WAITING_FOR_PATH;
                }
                else
                {
                    _telemetry.RecordMaxTimeToEnqueueRequest(ag.targetReplanWaitTime);
                    ag.targetReplanWaitTime += dt;
                }
            }

            // Update requests.
            using (var timer2 = _telemetry.ScopedTimer(DtCrowdTimerLabel.PathQueueUpdate))
            {
                _pathQ.Update(_navMesh);
            }

            // Process path results.
            for (var i = 0; i < agents.Count; i++)
            {
                var ag = agents[i];
                if (ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE || ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_VELOCITY)
                {
                    continue;
                }

                if (ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_WAITING_FOR_PATH)
                {
                    // _telemetry.RecordPathWaitTime(ag.targetReplanTime);
                    // Poll path queue.
                    DtStatus status = ag.targetPathQueryResult.status;
                    if (status.Failed())
                    {
                        // Path find failed, retry if the target location is still valid.
                        ag.targetPathQueryResult = null;
                        if (ag.targetRef != 0)
                        {
                            ag.targetState = DtMoveRequestState.DT_CROWDAGENT_TARGET_REQUESTING;
                        }
                        else
                        {
                            ag.targetState = DtMoveRequestState.DT_CROWDAGENT_TARGET_FAILED;
                        }

                        ag.targetReplanTime = 0;
                    }
                    else if (status.Succeeded())
                    {
                        Span<long> path = ag.corridor.GetPath();
                        int npath = ag.corridor.GetPathCount();
                        RcDebug.Assert(0 != npath);

                        // Apply results.
                        var targetPos = ag.targetPos;

                        bool valid = true;
                        Span<long> res = ag.targetPathQueryResult.path;
                        int nres = ag.targetPathQueryResult.npath;
                        status = ag.targetPathQueryResult.status;
                        if (status.Failed() || 0 == nres)
                        {
                            valid = false;
                        }

                        if (status.IsPartial())
                        {
                            ag.partial = true;
                        }
                        else
                        {
                            ag.partial = false;
                        }

                        // Merge result and existing path.
                        // The agent might have moved whilst the request is
                        // being processed, so the path may have changed.
                        // We assume that the end of the path is at the same
                        // location
                        // where the request was issued.

                        // The last ref in the old path should be the same as
                        // the location where the request was issued..
                        if (valid && path[npath - 1] != res[0])
                        {
                            valid = false;
                        }

                        if (valid)
                        {
                            // Put the old path infront of the old path.
                            if (npath > 1)
                            {
                                // Make space for the old path.
                                if ((npath - 1) + nres > _maxPathResult)
                                    nres = _maxPathResult - (npath - 1);

                                RcSpans.Move(res, 0, npath - 1, nres);
                                // Copy old path in the beginning.
                                RcSpans.Copy(path, 0, res, 0, npath - 1);
                                nres += npath - 1;

                                // Remove trackbacks
                                for (int j = 0; j < nres; ++j)
                                {
                                    if (j - 1 >= 0 && j + 1 < nres)
                                    {
                                        if (res[j - 1] == res[j + 1])
                                        {
                                            RcSpans.Move(res, j + 1, (j - 1), nres - (j + 1));
                                            nres -= 2;
                                            j -= 2;
                                        }
                                    }
                                }
                            }


                            // Check for partial path.
                            if (res[nres - 1] != ag.targetRef)
                            {
                                // Partial path, constrain target position inside the last polygon.
                                status = _navQuery.ClosestPointOnPoly(res[nres - 1], targetPos, out var nearest, out var _);
                                if (status.Succeeded())
                                {
                                    targetPos = nearest;
                                }
                                else
                                {
                                    valid = false;
                                }
                            }
                        }

                        if (valid)
                        {
                            // Set current corridor.
                            ag.corridor.SetCorridor(targetPos, res, nres);
                            // Force to update boundary.
                            ag.boundary.Reset();
                            ag.targetState = DtMoveRequestState.DT_CROWDAGENT_TARGET_VALID;
                        }
                        else
                        {
                            // Something went wrong.
                            ag.targetState = DtMoveRequestState.DT_CROWDAGENT_TARGET_FAILED;
                        }

                        ag.targetReplanTime = 0;
                    }

                    _telemetry.RecordMaxTimeToFindPath(ag.targetReplanWaitTime);
                    ag.targetReplanWaitTime += dt;
                }
            }
        }

        private void UpdateTopologyOptimization(IList<DtCrowdAgent> agents, float dt)
        {
            using var timer = _telemetry.ScopedTimer(DtCrowdTimerLabel.UpdateTopologyOptimization);

            // Longest-waiting agent goes first. The queue is filled completely
            // before it is drained, so a single sort beats a priority queue here.
            List<DtCrowdAgent> queue = _topologyOptQueue;
            queue.Clear();

            for (var i = 0; i < agents.Count; i++)
            {
                var ag = agents[i];
                if (ag.state != DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING)
                {
                    continue;
                }

                if (ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE
                    || ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_VELOCITY)
                {
                    continue;
                }

                if ((ag.option.updateFlags & DtCrowdAgentUpdateFlags.DT_CROWD_OPTIMIZE_TOPO) == 0)
                {
                    continue;
                }

                ag.topologyOptTime += dt;
                if (ag.topologyOptTime >= _config.topologyOptimizationTimeThreshold)
                {
                    queue.Add(ag);
                }
            }

            queue.Sort((a1, a2) => a2.topologyOptTime.CompareTo(a1.topologyOptTime));
            for (var i = 0; i < queue.Count; i++)
            {
                DtCrowdAgent ag = queue[i];
                ag.corridor.OptimizePathTopology(_navQuery, _filters[ag.option.queryFilterType], _config.maxTopologyOptimizationIterations);
                ag.topologyOptTime = 0;
            }
        }

        private void BuildProximityGrid(IList<DtCrowdAgent> agents)
        {
            using var timer = _telemetry.ScopedTimer(DtCrowdTimerLabel.BuildProximityGrid);

            _grid.Clear();

            for (var i = 0; i < agents.Count; i++)
            {
                var ag = agents[i];
                RcVec3f p = ag.npos;
                float r = ag.option.radius;
                _grid.AddItem(ag, p.X - r, p.Z - r, p.X + r, p.Z + r);
            }
        }

        private void BuildNeighbours(IList<DtCrowdAgent> agents)
        {
            using var timer = _telemetry.ScopedTimer(DtCrowdTimerLabel.BuildNeighbours);

            for (var i = 0; i < agents.Count; i++)
            {
                var ag = agents[i];
                if (ag.state != DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING)
                {
                    continue;
                }

                // Update the collision boundary after certain distance has been passed or
                // if it has become invalid.
                float updateThr = ag.option.collisionQueryRange * 0.25f;
                if (RcVec.Dist2DSqr(ag.npos, ag.boundary.GetCenter()) > RcMath.Sqr(updateThr)
                    || !ag.boundary.IsValid(_navQuery, _filters[ag.option.queryFilterType]))
                {
                    ag.boundary.Update(ag.corridor.GetFirstPoly(), ag.npos, ag.option.collisionQueryRange, _navQuery,
                        _filters[ag.option.queryFilterType]);
                }

                // Query neighbour agents
                ag.nneis = GetNeighbours(ag.npos, ag.option.height, ag.option.collisionQueryRange, ag, ag.neis, DtCrowdConst.DT_CROWDAGENT_MAX_NEIGHBOURS, _grid);
            }
        }

        public static int AddNeighbour(DtCrowdAgent idx, float dist, Span<DtCrowdNeighbour> neis, int nneis, int maxNeis)
        {
            // Insert neighbour based on the distance.
            int nei = 0;
            if (0 == nneis)
            {
                nei = nneis;
            }
            else if (dist >= neis[nneis - 1].dist)
            {
                if (nneis >= maxNeis)
                    return nneis;
                nei = nneis;
            }
            else
            {
                int i;
                for (i = 0; i < nneis; ++i)
                {
                    if (dist <= neis[i].dist)
                    {
                        break;
                    }
                }

                int tgt = i + 1;
                int n = Math.Min(nneis - i, maxNeis - tgt);

                RcDebug.Assert(tgt + n <= maxNeis);

                if (n > 0)
                {
                    RcSpans.Move(neis, i, tgt, n);
                }

                nei = i;
            }

            neis[nei] = new DtCrowdNeighbour(idx, dist);

            return Math.Min(nneis + 1, maxNeis);
        }

        private int GetNeighbours(RcVec3f pos, float height, float range, DtCrowdAgent skip, DtCrowdNeighbour[] result, int maxResult, DtProximityGrid grid)
        {
            int n = 0;

            const int MAX_NEIS = 32;
            Span<int> ids = stackalloc int[MAX_NEIS];
            int nids = grid.QueryItems(pos.X - range, pos.Z - range,
                pos.X + range, pos.Z + range,
                ids, ids.Length);

            for (int i = 0; i < nids; ++i)
            {
                var ag = GetAgent(ids[i]);
                if (ag == skip)
                {
                    continue;
                }

                // Check for overlap.
                RcVec3f diff = RcVec3f.Subtract(pos, ag.npos);
                if (MathF.Abs(diff.Y) >= (height + ag.option.height) / 2.0f)
                {
                    continue;
                }

                diff.Y = 0;
                float distSqr = diff.LengthSquared();
                if (distSqr > RcMath.Sqr(range))
                {
                    continue;
                }

                n = AddNeighbour(ag, distSqr, result, n, maxResult);
            }

            return n;
        }

        private void FindCorners(IList<DtCrowdAgent> agents, DtCrowdAgentDebugInfo debug)
        {
            using var timer = _telemetry.ScopedTimer(DtCrowdTimerLabel.FindCorners);

            DtCrowdAgent debugAgent = debug != null ? debug.agent : null;
            for (var i = 0; i < agents.Count; i++)
            {
                var ag = agents[i];
                if (ag.state != DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING)
                {
                    continue;
                }

                if (ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE
                    || ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_VELOCITY)
                {
                    continue;
                }

                // Find corners for steering
                ag.ncorners = ag.corridor.FindCorners(ag.corners, DtCrowdConst.DT_CROWDAGENT_MAX_CORNERS, _navQuery, _filters[ag.option.queryFilterType]);

                // Check to see if the corner after the next corner is directly visible,
                // and short cut to there.
                if ((ag.option.updateFlags & DtCrowdAgentUpdateFlags.DT_CROWD_OPTIMIZE_VIS) != 0 && ag.ncorners > 0)
                {
                    RcVec3f target = ag.corners[Math.Min(1, ag.ncorners - 1)].pos;
                    ag.corridor.OptimizePathVisibility(target, ag.option.pathOptimizationRange, _navQuery,
                        _filters[ag.option.queryFilterType]);

                    // Copy data for debug purposes.
                    if (debugAgent == ag)
                    {
                        debug.optStart = ag.corridor.GetPos();
                        debug.optEnd = target;
                    }
                }
                else
                {
                    // Copy data for debug purposes.
                    if (debugAgent == ag)
                    {
                        debug.optStart = RcVec3f.Zero;
                        debug.optEnd = RcVec3f.Zero;
                    }
                }
            }
        }

        private readonly DtStraightPath[] _triggerCorners = new DtStraightPath[DtCrowdConst.DT_CROWDAGENT_MAX_CORNERS];

        private void TriggerOffMeshConnections(IList<DtCrowdAgent> agents)
        {
            using var timer = _telemetry.ScopedTimer(DtCrowdTimerLabel.TriggerOffMeshConnections);

            for (var i = 0; i < agents.Count; i++)
            {
                var ag = agents[i];
                if (ag.state != DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING)
                {
                    continue;
                }

                if (ag.targetState != DtMoveRequestState.DT_CROWDAGENT_TARGET_VALID)
                {
                    continue;
                }

                // Unbound: the corners as seen from where the agent now stands (the agent's own
                // corners stay those it steered by this update).
                int n = ag.corridor.FindCorners(_triggerCorners, DtCrowdConst.DT_CROWDAGENT_MAX_CORNERS, _navQuery, _filters[ag.option.queryFilterType]);
                if (n == 0 || (_triggerCorners[n - 1].flags & DtStraightPathFlags.DT_STRAIGHTPATH_OFFMESH_CONNECTION) == 0)
                {
                    continue;
                }

                float triggerRadius = ag.option.radius * 2.25f;
                if (RcVec.Dist2DSqr(ag.npos, _triggerCorners[n - 1].pos) >= triggerRadius * triggerRadius)
                {
                    continue;
                }

                DtCrowdAgentAnimation anim = ag.animation;
                RcFixedArray2<long> refs = new RcFixedArray2<long>();
                if (ag.corridor.MoveOverOffmeshConnection(_triggerCorners[n - 1].refs, refs.AsSpan(), ref anim.startPos, ref anim.endPos, _navQuery))
                {
                    // Unity's crossing: from where the agent meets the link on the mesh to the
                    // link's own end (also off the mesh), where the agent stands when it leaves.
                    if (_navQuery.GetAttachedNavMesh().GetOffMeshConnectionRawEndPoints(refs[0], refs[1], out var rawStart, out var rawEnd, out int linkId, out float width))
                    {
                        anim.endPos = rawEnd;
                        anim.linkId = linkId;
                        // A wide link is crossed straight from where the agent meets it.
                        if (width > 0.0f)
                        {
                            anim.startPos = _triggerCorners[n - 1].pos;
                            anim.endPos = rawEnd + LinkOffset(rawStart, rawEnd, anim.startPos);
                        }
                    }
                    ag.corridor.SetPos(anim.endPos);
                    anim.initPos = ag.npos;
                    anim.polyRef = refs[1];
                    anim.active = true;
                    anim.t = 0.0f;
                    anim.tmax = RcVec3f.Distance(anim.startPos, anim.endPos);

                    ag.state = DtCrowdAgentState.DT_CROWDAGENT_STATE_OFFMESH;
                    ag.ncorners = 0;
                    ag.nneis = 0;
                }
            }
        }

        /// Unbound: how far across a wide link a point on its start segment lies.
        internal static RcVec3f LinkOffset(RcVec3f start, RcVec3f end, RcVec3f at)
        {
            RcVec3f d = end - start;
            float len = MathF.Sqrt(d.X * d.X + d.Z * d.Z);
            if (len <= 0.0f)
            {
                return RcVec3f.Zero;
            }

            var r = new RcVec3f(d.Z / len, 0.0f, -d.X / len);
            float across = (at.X - start.X) * r.X + (at.Z - start.Z) * r.Z;
            return r * across;
        }

        /// Unbound: an agent placed on a link's start enters it at once (Unity's: an agent whose
        /// path begins with a link it stands on is on the link in the update that finds the path).
        internal void TriggerOffMeshConnection(DtCrowdAgent ag)
        {
            TriggerOffMeshConnections(new[] { ag });
        }

        private void CalculateSteering(IList<DtCrowdAgent> agents, float dt)
        {
            using var timer = _telemetry.ScopedTimer(DtCrowdTimerLabel.CalculateSteering);

            for (var i = 0; i < agents.Count; i++)
            {
                var ag = agents[i];
                if (ag.state != DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING)
                {
                    continue;
                }

                if (ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE)
                {
                    continue;
                }

                RcVec3f dvel = new RcVec3f();

                if (ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_VELOCITY)
                {
                    dvel = ag.targetPos;
                    ag.desiredSpeed = ag.targetPos.Length();
                }
                else
                {
                    // Calculate steering direction.
                    if ((ag.option.updateFlags & DtCrowdAgentUpdateFlags.DT_CROWD_ANTICIPATE_TURNS) != 0)
                    {
                        dvel = ag.CalcSmoothSteerDirection();
                    }
                    else
                    {
                        dvel = ag.CalcStraightSteerDirection();
                    }

                    // Unbound: how far the corner steered to is (Unity reports the height of the
                    // desired velocity only near it; see DtCrowdAgent.steerDistance).
                    ag.steerDistance = ag.ncorners > 0 ? RcVec.Dist2D(ag.npos, ag.corners[0].pos) : 0.0f;

                    // Unbound: Unity's agents brake at the end of their path once they would reach it
                    // within the time two radii take at full speed: their speed falls by the
                    // deceleration that stops them there (v²/2d) at each update (measured).
                    // Agents without auto braking (UNBOUND_NO_BRAKING) keep their speed.
                    float speed = ag.option.maxSpeed;
                    ag.brakingNow = false;
                    if ((ag.option.updateFlags & DtCrowdAgentUpdateFlags.UNBOUND_NO_BRAKING) == 0)
                    {
                        float range = ag.option.radius * 2f;
                        float dist = ag.GetDistanceToGoal(range);
                        float v = ag.vel.Length();
                        if (dist < range && dist * ag.option.maxSpeed < range * v)
                        {
                            speed = Math.Clamp(v - dt * v * v / (2f * Math.Max(dist, 1e-6f)), 0f, ag.option.maxSpeed);
                            ag.brakingNow = true;
                        }
                    }

                    ag.desiredSpeed = ag.option.maxSpeed;
                    dvel = dvel * speed;
                }

                // Separation
                if ((ag.option.updateFlags & DtCrowdAgentUpdateFlags.DT_CROWD_SEPARATION) != 0)
                {
                    // Unbound: Unity's agents keep apart from the neighbours within 2.5 of their
                    // radii, fully from the more important ones, half from their equals, not from
                    // the less important (measured).
                    float separationDist = ag.option.radius * 2.5f;
                    float invSeparationDist = 1.0f / separationDist;
                    float separationWeight = ag.option.separationWeight;

                    float w = 0;
                    RcVec3f disp = new RcVec3f();

                    for (int j = 0; j < ag.nneis; ++j)
                    {
                        DtCrowdAgent nei = ag.neis[j].agent;
                        if (nei.option.avoidancePriority > ag.option.avoidancePriority) continue;
                        float share = nei.option.avoidancePriority < ag.option.avoidancePriority ? 1f : 0.5f;

                        RcVec3f diff = RcVec3f.Subtract(ag.npos, nei.npos);
                        diff.Y = 0;

                        float distSqr = diff.LengthSquared();
                        if (distSqr < 0.00001f)
                        {
                            continue;
                        }

                        if (distSqr > RcMath.Sqr(separationDist))
                        {
                            continue;
                        }

                        float dist = MathF.Sqrt(distSqr);
                        float weight = separationWeight * share * (1.0f - RcMath.Sqr(dist * invSeparationDist));

                        disp = RcVec.Mad(disp, diff, weight / dist);
                        w += 1.0f;
                    }

                    if (w > 0.0001f)
                    {
                        // Adjust desired velocity.
                        dvel = RcVec.Mad(dvel, disp, 1.0f / w);
                        // Clamp desired velocity to desired speed.
                        float speedSqr = dvel.LengthSquared();
                        float desiredSqr = RcMath.Sqr(ag.desiredSpeed);
                        if (speedSqr > desiredSqr)
                        {
                            dvel = dvel * (desiredSqr / speedSqr);
                        }
                    }
                }

                // Set the desired velocity.
                ag.dvel = dvel;
            }
        }

        private void PlanVelocity(DtCrowdAgentDebugInfo debug, IList<DtCrowdAgent> agents)
        {
            using var timer = _telemetry.ScopedTimer(DtCrowdTimerLabel.PlanVelocity);

            DtCrowdAgent debugAgent = debug != null ? debug.agent : null;
            for (var i = 0; i < agents.Count; i++)
            {
                var ag = agents[i];
                if (ag.state != DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING)
                {
                    continue;
                }

                // Unbound: with nothing to avoid, the velocity is the desired one as it is (Unity's: a
                // braking or tilted desired velocity is not resampled; measured).
                // Unbound: only neighbours as or more important than the agent count (Unity's priority).
                int avoided = 0;
                for (int j = 0; j < ag.nneis; ++j)
                    if (ag.neis[j].agent.option.avoidancePriority <= ag.option.avoidancePriority) avoided++;
                bool avoid = (ag.option.updateFlags & DtCrowdAgentUpdateFlags.DT_CROWD_OBSTACLE_AVOIDANCE) != 0
                    && (avoided > 0 || (ag.option.updateFlags & DtCrowdAgentUpdateFlags.UNBOUND_AVOID_EDGES) != 0 && ag.boundary.GetSegmentCount() > 0);
                if (avoid)
                {
                    _obstacleQuery.Reset();
                    // Unbound: edges count up to the next corner; when that is the path's end or a
                    // link's start, only those before it (measured: an agent walks straight up to
                    // the end of a partial path or a link on the NavMesh's edge,
                    // tests/projects/navlinks).
                    // The first plan of a new path heading for a link counts the edges before
                    // the path's end (measured: a fast agent samples away from an edge beyond the
                    // link it heads for only in the update that finds its path,
                    // tests/projects/navlinks; an agent heading for an ordinary corner does not
                    // count the edges beyond it, tests/projects/navmesh).
                    _obstacleQuery.segmentReach = ag.newPath && ag.ncorners > 0
                                                  && (ag.corners[0].flags & DtStraightPathFlags.DT_STRAIGHTPATH_OFFMESH_CONNECTION) != 0
                        ? RcVec.Dist2D(ag.npos, ag.targetPos) - 1e-3f
                        : ag.ncorners > 0
                        ? RcVec.Dist2D(ag.npos, ag.corners[0].pos)
                          + ((ag.corners[0].flags & (DtStraightPathFlags.DT_STRAIGHTPATH_END | DtStraightPathFlags.DT_STRAIGHTPATH_OFFMESH_CONNECTION)) != 0 ? -1e-3f : 1e-3f)
                        : float.MaxValue;

                    // Add neighbours as obstacles.
                    for (int j = 0; j < ag.nneis; ++j)
                    {
                        DtCrowdAgent nei = ag.neis[j].agent;
                        if (nei.option.avoidancePriority > ag.option.avoidancePriority) continue;
                        _obstacleQuery.AddCircle(nei.npos, nei.option.radius, nei.vel, nei.dvel);
                    }

                    // Append neighbour segments as obstacles.
                    // Unbound: Unity's agents avoid other agents, not the NavMesh's edges (they walk
                    // right up to them; measured): no segments unless asked for.
                    for (int j = 0; (ag.option.updateFlags & DtCrowdAgentUpdateFlags.UNBOUND_AVOID_EDGES) != 0 && j < ag.boundary.GetSegmentCount(); ++j)
                    {
                        ref RcFixedArray2<RcVec3f> s = ref ag.boundary.GetSegment(j);
                        RcVec3f s3 = s[1];
                        //RcArrays.Copy(s, 3, s3, 0, 3);
                        // Unbound: an edge the agent stands on does not face it (Unity's agents leave
                        // it; measured).
                        if (DtUtils.TriArea2D(ag.npos, s[0], s3) < 1e-5f)
                        {
                            continue;
                        }

                        _obstacleQuery.AddSegment(s[0], s3);
                    }

                    DtObstacleAvoidanceDebugData vod = null;
                    if (debugAgent == ag)
                    {
                        vod = debug.vod;
                    }

                    // Sample new safe velocity.
                    bool adaptive = true;
                    int ns = 0;

                    DtObstacleAvoidanceParams option = _obstacleQueryParams[ag.option.obstacleAvoidanceType];

                    // Unbound: a desired velocity that meets no neighbour within the horizon is
                    // kept as it is (Unity's; measured).
                    if (option.unityVelocity && _obstacleQuery.DesiredVelocityIsFree(ag.npos, ag.option.radius, ag.dvel, option))
                    {
                        ag.nvel = ag.dvel;
                    }
                    else if (adaptive)
                    {
                        ns = _obstacleQuery.SampleVelocityAdaptive(ag.npos, ag.option.radius, ag.desiredSpeed,
                            ag.vel, ag.dvel, out ag.nvel, option, vod);
                    }
                    else
                    {
                        ns = _obstacleQuery.SampleVelocityGrid(ag.npos, ag.option.radius,
                            ag.desiredSpeed, ag.vel, ag.dvel, out ag.nvel, option, vod);
                    }

                    _velocitySampleCount += ns;
                    // Unbound: a desired velocity nothing makes the agent change stays whole (Unity's
                    // keeps its height: a steering target above the agent tilts it; measured).
                    if (RcVec.Dist2DSqr(ag.nvel, ag.dvel) < 1e-8f) ag.nvel = ag.dvel;
                }
                else
                {
                    // If not using velocity planning, new velocity is directly the desired velocity.
                    ag.nvel = ag.dvel;
                }
            }
        }

        private void Accelerate(float dt, IList<DtCrowdAgent> agents)
        {
            for (var i = 0; i < agents.Count; i++)
            {
                var ag = agents[i];
                if (ag.state != DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING)
                {
                    continue;
                }

                ag.Accelerate(dt);
            }
        }

        private void Integrate(float dt, IList<DtCrowdAgent> agents)
        {
            using var timer = _telemetry.ScopedTimer(DtCrowdTimerLabel.Integrate);

            for (var i = 0; i < agents.Count; i++)
            {
                var ag = agents[i];
                if (ag.state != DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING)
                {
                    continue;
                }

                ag.Integrate(dt);
            }
        }

        private void HandleCollisions(IList<DtCrowdAgent> agents)
        {
            using var timer = _telemetry.ScopedTimer(DtCrowdTimerLabel.HandleCollisions);

            for (int iter = 0; iter < 4; ++iter)
            {
                for (var i = 0; i < agents.Count; i++)
                {
                    var ag = agents[i];
                    long idx0 = ag.idx;
                    if (ag.state != DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING)
                    {
                        continue;
                    }

                    ag.disp = RcVec3f.Zero;

                    float w = 0;

                    for (int j = 0; j < ag.nneis; ++j)
                    {
                        DtCrowdAgent nei = ag.neis[j].agent;
                        long idx1 = nei.idx;
                        // Unbound: an overlap is resolved by the less important agent alone, by
                        // equals half each (Unity's; measured).
                        if (nei.option.avoidancePriority > ag.option.avoidancePriority)
                        {
                            continue;
                        }

                        float share = nei.option.avoidancePriority < ag.option.avoidancePriority ? 1.0f : 0.5f;
                        RcVec3f diff = RcVec3f.Subtract(ag.npos, nei.npos);
                        diff.Y = 0;

                        float dist = diff.LengthSquared();
                        if (dist > RcMath.Sqr(ag.option.radius + nei.option.radius))
                        {
                            continue;
                        }

                        dist = MathF.Sqrt(dist);
                        float pen = (ag.option.radius + nei.option.radius) - dist;
                        if (dist < 0.0001f)
                        {
                            // Agents on top of each other, try to choose diverging separation directions.
                            if (idx0 > idx1)
                            {
                                diff = new RcVec3f(-ag.dvel.Z, 0, ag.dvel.X);
                            }
                            else
                            {
                                diff = new RcVec3f(ag.dvel.Z, 0, -ag.dvel.X);
                            }

                            pen = 0.01f;
                        }
                        else
                        {
                            pen = (1.0f / dist) * (pen * share) * _config.collisionResolveFactor;
                        }

                        ag.disp = RcVec.Mad(ag.disp, diff, pen);

                        w += 1.0f;
                    }

                    if (w > 0.0001f)
                    {
                        float iw = 1.0f / w;
                        ag.disp = ag.disp * iw;
                    }
                }

                for (var i = 0; i < agents.Count; i++)
                {
                    var ag = agents[i];
                    if (ag.state != DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING)
                    {
                        continue;
                    }

                    ag.npos = RcVec3f.Add(ag.npos, ag.disp);
                }
            }
        }

        private void MoveAgents(IList<DtCrowdAgent> agents)
        {
            using var timer = _telemetry.ScopedTimer(DtCrowdTimerLabel.MoveAgents);

            for (var i = 0; i < agents.Count; i++)
            {
                var ag = agents[i];
                if (ag.state != DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING)
                {
                    continue;
                }

                // Move along navmesh.
                ag.corridor.MovePosition(ag.npos, _navQuery, _filters[ag.option.queryFilterType]);
                // Get valid constrained position back.
                ag.npos = ag.corridor.GetPos();

                // If not using path, truncate the corridor to just one poly.
                if (ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE
                    || ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_VELOCITY)
                {
                    ag.corridor.Reset(ag.corridor.GetFirstPoly(), ag.npos);
                    ag.partial = false;
                }
            }
        }

        /// Unbound: Unity's crossing (measured, tests/projects/navlinks). Detour's two parts, 15%
        /// of the crossing from where the agent entered to the link's start and the rest to its
        /// end, but the progress is a fraction of the start-to-end distance (in 3D) advanced at
        /// twice the agent's current speed, and only while the agent traverses links by itself and
        /// is not stopped. The desired velocity points along the current part at the agent's
        /// speed, and is the velocity the agent walks on with when a script completes the link. The update that passes the end
        /// leaves the link: the agent goes from the link's end towards where the rest of the step
        /// would take it, along the mesh, and its velocity becomes that step (at most its speed).
        private void UpdateOffMeshConnections(IList<DtCrowdAgent> agents, float dt)
        {
            using var timer = _telemetry.ScopedTimer(DtCrowdTimerLabel.UpdateOffMeshConnections);

            for (var i = 0; i < agents.Count; i++)
            {
                var ag = agents[i];
                DtCrowdAgentAnimation anim = ag.animation;
                if (!anim.active)
                {
                    continue;
                }

                const float approach = 0.15f;
                float speed = ag.option.linkSpeed;
                if (ag.option.linkTraverse && anim.tmax > 0.0f)
                {
                    anim.t += dt * 2.0f * speed / anim.tmax;
                }
                else if (anim.tmax <= 0.0f && ag.option.linkTraverse)
                {
                    anim.t = float.PositiveInfinity;
                }

                if (anim.t > 1.0f)
                {
                    float u = float.IsInfinity(anim.t) ? 1.0f : (anim.t - approach) / (1.0f - approach);
                    RcVec3f target = anim.startPos + (anim.endPos - anim.startPos) * u;
                    CompleteOffMeshConnection(ag);
                    ag.corridor.MovePosition(target, _navQuery, _filters[ag.option.queryFilterType]);
                    ag.npos = ag.corridor.GetPos();
                    RcVec3f v = (ag.npos - anim.endPos) * (1.0f / dt);
                    float len = v.Length();
                    if (len > speed)
                    {
                        v = v * (len > 0.0f ? speed / len : 0.0f);
                    }

                    ag.vel = v;
                    ag.nvel = v;
                    ag.pvel = v;
                    ag.dvel = v;
                    ag.planFromLink = true;
                    continue;
                }

                // An agent that does not move along its link stays where it is (also where a
                // script put it).
                bool moving = ag.option.linkTraverse;
                RcVec3f from, to;
                if (anim.t < approach)
                {
                    from = anim.initPos;
                    to = anim.startPos;
                    if (moving) ag.npos = Lerp(from, to, anim.t / approach);
                }
                else
                {
                    from = anim.startPos;
                    to = anim.endPos;
                    if (moving) ag.npos = Lerp(from, to, (anim.t - approach) / (1.0f - approach));
                }

                RcVec3f dir = to - from;
                float d = dir.Length();
                ag.nvel = d > 1e-4f ? dir * (speed / d) : RcVec3f.Zero;
                ag.pvel = ag.nvel;
                ag.dvel = ag.nvel;
                ag.vel = ag.nvel;
                ag.planFromLink = true;
            }
        }

        /// Unbound: Unity's interpolation along a link, a·(1 − u) + b·u.
        private static RcVec3f Lerp(RcVec3f a, RcVec3f b, float u)
        {
            return new RcVec3f(a.X * (1.0f - u) + b.X * u, a.Y * (1.0f - u) + b.Y * u, a.Z * (1.0f - u) + b.Z * u);
        }

        /// Unbound: the agent leaves its link at the link's end (Unity's CompleteOffMeshLink and
        /// the end of a crossing), keeping its velocity.
        internal void CompleteOffMeshConnection(DtCrowdAgent ag)
        {
            DtCrowdAgentAnimation anim = ag.animation;
            if (!anim.active)
            {
                return;
            }

            anim.active = false;
            ag.state = DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING;
            ag.npos = anim.endPos;
            ag.corridor.SetPos(anim.endPos);
        }

        private float Tween(float t, float t0, float t1)
        {
            return Math.Clamp((t - t0) / (t1 - t0), 0.0f, 1.0f);
        }
    }
}