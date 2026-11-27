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

        // Reused per-update scratch queue, see UpdateMoveRequest.
        private readonly List<DtCrowdAgent> _moveRequestQueue = new List<DtCrowdAgent>();

        public DtCrowd(DtCrowdConfig config, DtNavMesh nav) : this(config, nav, i => new DtQueryDefaultFilter())
        {
        }

        public DtCrowd(DtCrowdConfig config, DtNavMesh nav, Func<int, IDtQueryFilter> queryFilterFactory)
        {
            _config = config;
            _agentPlacementHalfExtents = new RcVec3f(config.maxAgentRadius * 2.0f, config.maxAgentRadius * 1.5f, config.maxAgentRadius * 2.0f);

            _obstacleQuery = new DtObstacleAvoidanceQuery(
                config.maxObstacleAvoidanceCircles,
                config.maxObstacleAvoidanceSegments,
                config.maxObstacleAvoidanceMovingSegments);

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
            _navQuery = new DtNavMeshQuery(nav, _config.weightedWallProjection);
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
            ag.corridor.joinMovedStart = _config.joinMovedCorridorStart;
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
            // Unbound: with no path it waits for no link.
            agent.waitingForConnection = false;
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

        /// Unbound: <paramref name="topologyAgent"/>, an agent of this crowd or null, is the one
        /// whose corridor is searched again in this update (see WaitForTopology).
        public DtCrowdTelemetry Update(float dt, DtCrowdAgentDebugInfo debug, DtCrowdAgent topologyAgent = null)
        {
            _velocitySampleCount = 0;

            _telemetry.Start();

            IList<DtCrowdAgent> agents = GetActiveAgents();

            // Check that all agents still have valid paths.
            CheckPathValidity(agents, dt);

            // Update async move request and path finder.
            UpdateMoveRequest(agents, dt);

            // Optimize path topology.
            if (topologyAgent != null)
            {
                OptimizeTopology(topologyAgent);
            }

            // Unbound: Unity's agents take the step towards the last plan before steering and
            // planning anew, and steer and plan with the velocity they move at this update (measured);
            // their neighbours are chosen by that velocity too (tests/projects/avoidance separation).
            Accelerate(dt, agents);

            // Register agents to proximity grid.
            BuildProximityGrid(agents);

            // Get nearby navmesh segments and agents to collide with.
            BuildNeighbours(agents);

            // Find next corner to steer to.
            FindCorners(agents, debug);

            // Calculate steering.
            CalculateSteering(agents, dt);

            // Velocity planning.
            PlanVelocity(debug, agents);

            // Integrate.
            Integrate(dt, agents);

            // Handle collisions.
            HandleCollisions(agents);

            MoveAgents(agents);

            // Trigger off-mesh connections (depends on corners).
            // Unbound: Unity's agents reach a link from where this update's step took them, and
            // begin to cross it in the same update (measured: tests/projects/navlinks).
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
                if (ag.deferPathValidation || ag.state != DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING)
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

                // If the end of the path is near and it is not the requested location, replan.
                if (ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_VALID)
                {
                    if (ag.targetReplanTime > _config.targetReplanDelay && ag.corridor.GetPathCount() < _config.checkLookAhead
                                                                        && ag.corridor.GetLastPoly() != ag.targetRef)
                    {
                        replan = true;
                    }
                }

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

        /// Unbound: Unity searches the corridor of one agent again per update, of all its agents: the
        /// one that has waited longest (the first of equals), once its wait is more than
        /// DtCrowdConfig.topologyOptimizationTimeThreshold. An agent waits while it walks with more
        /// than two polygons in its corridor; its wait grows by each update's time and starts again
        /// from zero when its corridor is searched, or when it joins a crowd (matches the Unity
        /// 6000.0.71f1 Editor, tests/projects/navpath `place.moved` walk). Returns whether the agent
        /// waits in this update.
        public static bool WaitForTopology(DtCrowdAgent ag, float dt)
        {
            if (ag.state != DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING || ag.corridor.GetPathCount() <= 2)
            {
                return false;
            }

            ag.topologyOptTime += dt;
            return true;
        }

        private void OptimizeTopology(DtCrowdAgent ag)
        {
            using var timer = _telemetry.ScopedTimer(DtCrowdTimerLabel.UpdateTopologyOptimization);
            ag.corridor.OptimizePathTopology(_navQuery, FilterOf(ag), _config.maxTopologyOptimizationIterations);
            ag.topologyOptTime = 0;
        }

        /// Unbound: the filter an agent optimizes its corridor with: its own, else its query filter type's.
        private IDtQueryFilter FilterOf(DtCrowdAgent ag)
        {
            return ag.option.filter ?? _filters[ag.option.queryFilterType];
        }

        private void BuildProximityGrid(IList<DtCrowdAgent> agents)
        {
            using var timer = _telemetry.ScopedTimer(DtCrowdTimerLabel.BuildProximityGrid);

            _grid.Clear();

            // Cells are sized over every crowd's agents when the caller supplies the sizes,
            // otherwise over this crowd's agents.
            if (_gridCellX > 0f)
            {
                _grid.SetCellSize(_gridCellX, _gridCellY);
            }
            else if (agents.Count > 0)
            {
                float horizon = _obstacleQueryParams[0].horizTime, sx = 0f, sz = 0f;
                for (var i = 0; i < agents.Count; i++)
                    AddToGridCell(agents[i], agents[i].vel, horizon, ref sx, ref sz);
                _grid.SetCellSize(GridCell(sx, agents.Count), GridCell(sz, agents.Count));
            }

            for (var i = 0; i < agents.Count; i++)
            {
                var ag = agents[i];
                RcVec3f p = ag.npos;
                if (_config.predictNeighbourBounds)
                {
                    HorizontalExtents(ag, out float ex, out float ez);
                    PredictionBounds(p, ag.vel, ex, ez, _obstacleQueryParams[0].horizTime, out var min, out var max);
                    _grid.AddItem(ag, min.X, min.Z, max.X, max.Z);
                }
                else _grid.AddItem(ag, p.X, p.Z, p.X, p.Z);
            }
        }

        /// The horizontal bounds swept by a neighbour over the prediction time, expanded by
        /// its radius (Unity 6000.0.71f1, tests/projects/navmesh and avoidance).
        public static void PredictionBounds(RcVec3f position, RcVec3f velocity, float radius, float horizon,
            out RcVec3f min, out RcVec3f max) =>
            PredictionBounds(position, velocity, radius, radius, horizon, out min, out max);

        /// The horizontal bounds swept by a neighbour over the prediction time, expanded by
        /// its per-axis horizontal extents (Unity 6000.0.71f1, tests/projects/navpath obstacle-shapes).
        public static void PredictionBounds(RcVec3f position, RcVec3f velocity, float extentX, float extentZ, float horizon,
            out RcVec3f min, out RcVec3f max)
        {
            float x = position.X + velocity.X * horizon;
            float z = position.Z + velocity.Z * horizon;
            // Keep the previous point insertion for invalid or overflowing stored inputs.
            // They must not turn the cell walk into an unbounded traversal.
            if (!float.IsFinite(x) || !float.IsFinite(z) || !float.IsFinite(extentX) || !float.IsFinite(extentZ))
            {
                min = max = position;
                return;
            }
            min = new RcVec3f(MathF.Min(position.X, x) - extentX, 0f, MathF.Min(position.Z, z) - extentZ);
            max = new RcVec3f(MathF.Max(position.X, x) + extentX, 0f, MathF.Max(position.Z, z) + extentZ);
            if (!float.IsFinite(min.X) || !float.IsFinite(min.Z) || !float.IsFinite(max.X) || !float.IsFinite(max.Z))
                min = max = position;
        }

        /// Unbound: the smallest proximity grid cell (Unity's), in metres.
        public const float MinGridCell = 1f;

        private float _gridCellX, _gridCellY;

        /// Unbound: the grid cells of the next updates, along x and z (see GridCell); zero: this
        /// crowd sizes them from its own agents.
        public void SetGridCellSize(float x, float z)
        {
            _gridCellX = x;
            _gridCellY = z;
        }

        /// The cell sizes selected for this update, including before the grid is rebuilt.
        public void GetGridCellSize(out float x, out float z)
        {
            x = _gridCellX > 0f ? _gridCellX : _grid.GetCellSizeX();
            z = _gridCellY > 0f ? _gridCellY : _grid.GetCellSizeY();
        }

        /// Unbound: an agent's share in Unity's grid cell size: its diameter plus how far it goes
        /// within the prediction time along each axis, at <paramref name="vel"/>.
        public static void AddToGridCell(DtCrowdAgent ag, RcVec3f vel, float horizon, ref float sx, ref float sz)
        {
            HorizontalExtents(ag, out float ex, out float ez);
            AddToGridCell(ex, ez, vel, horizon, ref sx, ref sz);
        }

        public static void AddToGridCell(float radius, RcVec3f vel, float horizon, ref float sx, ref float sz) =>
            AddToGridCell(radius, radius, vel, horizon, ref sx, ref sz);

        public static void AddToGridCell(float extentX, float extentZ, RcVec3f vel, float horizon, ref float sx, ref float sz)
        {
            sx += 2f * extentX + horizon * MathF.Abs(vel.X);
            sz += 2f * extentZ + horizon * MathF.Abs(vel.Z);
        }

        /// Unbound: Unity's grid cell along an axis: the agents' mean share, at least MinGridCell.
        public static float GridCell(float sum, int count) => MathF.Max(MinGridCell, sum / count);

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

                // Unbound: an agent without avoidance gathers neither edges nor neighbours, so other
                // agents and obstacles do not push it out of overlaps either (agents that avoid
                // still gather it). Matches the Unity 6000.0.71f1 Editor, tests/projects/navpath
                // `replan.*`: agents without avoidance started at one point walk on together, one
                // walks through a NavMeshObstacle, and of two overlapping agents only the one with
                // avoidance is pushed out (the AI Navigation 2.0 manual's Quality None says
                // collisions resolve).
                if (!Avoids(ag))
                {
                    ag.boundary.Reset();
                    ag.nneis = 0;
                    continue;
                }

                float horizon = _obstacleQueryParams[ag.option.obstacleAvoidanceType].horizTime;
                UpdateBoundary(ag, horizon);

                // Query neighbour agents
                ag.nneis = GetNeighbours(ag, horizon, ag.neis, DtCrowdConst.DT_CROWDAGENT_MAX_NEIGHBOURS, _grid);
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

        /// How near (in radii) the start of a link on its path an agent takes the link (Detour's).
        public const float LinkTriggerRadiusScale = 2.25f;

        /// Unbound: the most neighbour candidates an agent's proximity query gathers (Unity's).
        public const int NeighbourCandidates = 64;

        /// Unbound: the half-width of the square an agent looks for neighbours in: its radius plus how
        /// far it goes at full speed within the prediction time (Unity's).
        public static float NeighbourReach(DtCrowdAgent ag, float horizon) => ag.option.radius + ag.option.reachSpeed * horizon;

        /// Unbound: whether the agent avoids others (any avoidance quality but none).
        public static bool Avoids(DtCrowdAgent ag) => (ag.option.updateFlags & DtCrowdAgentUpdateFlags.DT_CROWD_OBSTACLE_AVOIDANCE) != 0;

        /// Unbound: whether a neighbour's height span meets the agent's (Unity's: its feet are not
        /// above the agent's head, its head not below the agent's feet).
        public static bool HeightsOverlap(DtCrowdAgent ag, RcVec3f neiPos, float neiHeight)
        {
            float dy = neiPos.Y - ag.npos.Y;
            return dy <= ag.option.height && dy >= -neiHeight;
        }

        /// Unbound: the agent's NavMesh edges, as Unity's agents keep them: none while their steering
        /// target (as the last update found it) is the end of their path or a link within reach (their
        /// radius plus half the way they go at full speed within the prediction time), else the
        /// nearest edges within that reach, gathered again when the agent changes polygon or has
        /// moved a quarter of the reach.
        private void UpdateBoundary(DtCrowdAgent ag, float horizon)
        {
            float reach = ag.option.radius + 0.5f * horizon * ag.option.reachSpeed;
            if (!ag.hasSteer || (ag.steerFlags & (DtStraightPathFlags.DT_STRAIGHTPATH_END | DtStraightPathFlags.DT_STRAIGHTPATH_OFFMESH_CONNECTION)) != 0)
            {
                RcVec3f target = ag.hasSteer ? ag.steerPos : ag.corridor.GetTarget();
                if (RcVec3f.Distance(target, ag.npos) < reach)
                {
                    ag.boundary.Reset();
                    return;
                }
            }

            long poly = ag.corridor.GetFirstPoly();
            if (ag.boundary.GetPoly() != poly
                || RcVec3f.DistanceSquared(ag.npos, ag.boundary.GetCenter()) > RcMath.Sqr(reach * 0.25f)
                || !ag.boundary.IsValid(_navQuery, _filters[ag.option.queryFilterType]))
            {
                ag.boundary.Update(poly, ag.npos, reach, _navQuery, _filters[ag.option.queryFilterType]);
            }
        }

        /// Unbound: an agent's neighbours as Unity's agents choose them: agents (and obstacle circles)
        /// the proximity grid holds around the square of its neighbour reach, as or more important than it, not behind it by more
        /// than both radii along the way it last meant to go, with heights that meet, and reachable
        /// within the prediction time with both at full speed; the soonest reachable first, at most
        /// <paramref name="maxResult"/>.
        private int GetNeighbours(DtCrowdAgent skip, float horizon, DtCrowdNeighbour[] result, int maxResult, DtProximityGrid grid)
        {
            int n = 0;
            RcVec3f pos = skip.npos;
            float range = NeighbourReach(skip, horizon);

            Span<int> ids = stackalloc int[NeighbourCandidates];
            int nids = grid.QueryItems(pos.X - range, pos.Z - range,
                pos.X + range, pos.Z + range,
                ids, ids.Length);

            RcVec3f heading = skip.dvel;
            float headingSqr = heading.LengthSquared();
            bool moving = headingSqr > 0.01f;
            if (moving) heading = heading * (1f / MathF.Sqrt(headingSqr));

            for (int i = 0; i < nids; ++i)
            {
                var ag = GetAgent(ids[i]);
                if (ag == skip || ag.option.avoidancePriority > skip.option.avoidancePriority)
                {
                    continue;
                }

                bool shapeObstacle = ag.option.capsuleObstacle || ag.option.boxObstacle;
                if (!shapeObstacle && !HeightsOverlap(skip, ag.npos, ag.option.height))
                {
                    continue;
                }

                // In 3D, as Unity's agents measure (a neighbour above or below is farther).
                RcVec3f diff = RcVec3f.Subtract(ag.npos, pos);
                float radius = ag.option.radius;
                if (shapeObstacle)
                {
                    // Obstacle admission ranks the center with the horizontal bounds' half-diagonal.
                    // The clipped capsule or box section is tested during avoidance and contact.
                    HorizontalExtents(ag, out float ex, out float ez);
                    radius = MathF.Sqrt(ex * ex + ez * ez);
                }
                float radii = skip.option.radius + radius;
                if (moving && RcVec3f.Dot(diff, heading) < -radii)
                {
                    continue;
                }

                // Both at full speed (an agent standing with both speeds zero: at once if touching).
                float gap = shapeObstacle ? diff.Length() - radius - skip.option.radius : diff.Length() - radii;
                float closing = skip.option.reachSpeed + ag.vel.Length();
                float time = closing > 0f || (shapeObstacle && gap < 0f)
                    ? gap / closing : gap <= 0f ? 0f : float.PositiveInfinity;
                if (time > horizon)
                {
                    continue;
                }

                n = AddNeighbour(ag, time, result, n, maxResult);
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
                    ag.hasSteer = false;
                    continue;
                }

                // Find corners for steering
                ag.ncorners = ag.corridor.FindCorners(ag.corners, DtCrowdConst.DT_CROWDAGENT_MAX_CORNERS, _navQuery, _filters[ag.option.queryFilterType]);
                ag.KeepSteer();

                // Unbound: Unity's agents shortcut their corridor at every update (see
                // DtPathCorridor.OptimizePathVisibility).
                ag.corridor.OptimizePathVisibility(ag.corners, ag.ncorners, _navQuery, FilterOf(ag));
                if (debugAgent == ag)
                {
                    debug.optStart = ag.corridor.GetPos();
                    debug.optEnd = ag.ncorners > 0 ? ag.corners[Math.Min(1, ag.ncorners - 1)].pos : RcVec3f.Zero;
                }
            }
        }

        /// Unbound: whether an agent may take the off-mesh connection polygon (no other agent is on its
        /// link); taking it makes it occupied. Null: every connection may be taken.
        public Func<long, bool> tryEnterConnection;

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

                if (ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_NONE
                    || ag.targetState == DtMoveRequestState.DT_CROWDAGENT_TARGET_VELOCITY)
                {
                    continue;
                }

                // Check
                float triggerRadius = ag.option.radius * LinkTriggerRadiusScale;
                ag.waitingForConnection = false;
                if (ag.OverOffmeshConnection(triggerRadius))
                {
                    // Unbound: a link another agent is on is not taken; the agent stops where it
                    // reached it and takes it once it is free (Unity's; measured:
                    // tests/projects/navlinks crosstile, two).
                    if (tryEnterConnection != null && !tryEnterConnection(ag.corners[ag.ncorners - 1].refs))
                    {
                        ag.waitingForConnection = true;
                        continue;
                    }

                    // Prepare to off-mesh connection.
                    DtCrowdAgentAnimation anim = ag.animation;

                    // Adjust the path over the off-mesh connection.
                    RcFixedArray2<long> refs = new RcFixedArray2<long>();
                    if (ag.corridor.MoveOverOffmeshConnection(ag.corners[ag.ncorners - 1].refs, refs.AsSpan(), ag.npos, ref anim.startPos,
                            ref anim.endPos, out var linkEnd, _navQuery))
                    {
                        anim.initPos = ag.npos;
                        anim.polyRef = refs[1];
                        anim.active = true;
                        anim.distance = 0.0f;
                        // Unbound: Unity's agents cross from the start on the NavMesh to the link's own
                        // end point (not the point the end landed on), taking half the time the
                        // distance between them in 3D takes at full speed (measured).
                        anim.endPos = linkEnd;
                        anim.length = RcVec3f.Distance(anim.startPos, anim.endPos);

                        ag.state = DtCrowdAgentState.DT_CROWDAGENT_STATE_OFFMESH;
                        ag.ncorners = 0;
                        ag.reportedCorners = 0;
                        ag.hasSteer = false;
                        ag.nneis = 0;
                        continue;
                    }
                    else
                    {
                        // Path validity check will ensure that bad/blocked connections will be replanned.
                    }
                }
            }
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

                // A braking plan belongs to its active path, including when the request was reset.
                if (_config.measuredStoppingDistance) ag.brakingNow = false;

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

                    // Unbound: Unity's agents brake at the end of their path once they would reach it
                    // within the time two radii take at full speed: their speed falls by the
                    // deceleration that stops them there (v²/2d) at each update (measured).
                    // Agents without auto braking (UNBOUND_NO_BRAKING) keep their speed.
                    float speed = ag.option.maxSpeed;
                    // Unity 6000.0.71f1, tests/projects/navlinks stopping controls: the stop
                    // radius requests zero speed before the automatic braking calculation.
                    if (_config.measuredStoppingDistance
                        && (ag.ncorners > 0 || ag.corridor.GetPathCount() >= 2)
                        && ag.option.stoppingDistance > (ag.ncorners == 0 ? RcVec3f.Distance(ag.npos, ag.corridor.GetTarget())
                            : DtCrowdAgent.KnownPathLength(ag.npos, ag.corners, ag.ncorners)))
                        speed = 0f;
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

                    // Unbound: the speed it may plan at is the braked one (Unity's).
                    ag.desiredSpeed = speed;
                    dvel = dvel * speed;
                    // Unbound: waiting for an occupied link, it plans to stand (see TriggerOffMeshConnections).
                    if (ag.waitingForConnection)
                    {
                        dvel = RcVec3f.Zero;
                        ag.desiredSpeed = 0f;
                    }
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
                        if (nei.option.excludeFromSeparation ||
                            nei.option.avoidancePriority > ag.option.avoidancePriority) continue;
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

        // Axis vertical component above which a shaft wholly beyond the agent's height collapses
        // to a single clipped cap circle rather than a two-point segment (Unity 6000.0.71f1,
        // tests/projects/navpath obstacle-shapes: cap5low/cap5high vs cap10low/cap10high).
        const float UprightCapCosThreshold = 0.99f;
        const float CapsuleSegmentLengthEpsilon = 1e-5f;

        /// <summary>Per-axis horizontal extents from a crowd participant's center.</summary>
        public static void HorizontalExtents(DtCrowdAgent ag, out float ex, out float ez)
        {
            float r = ag.option.radius;
            if (ag.option.capsuleObstacle)
            {
                ex = MathF.Abs(ag.option.capsuleAxis.X) * ag.option.capsuleHalfShaft + r;
                ez = MathF.Abs(ag.option.capsuleAxis.Z) * ag.option.capsuleHalfShaft + r;
                return;
            }
            if (ag.option.boxObstacle)
            {
                RcVec3f h = ag.option.boxHalfExtents;
                RcVec3f ux = ag.option.boxAxisX;
                RcVec3f uy = ag.option.boxAxisY;
                RcVec3f uz = ag.option.boxAxisZ;
                ex = MathF.Abs(ux.X) * h.X + MathF.Abs(uy.X) * h.Y + MathF.Abs(uz.X) * h.Z;
                ez = MathF.Abs(ux.Z) * h.X + MathF.Abs(uy.Z) * h.Y + MathF.Abs(uz.Z) * h.Z;
                return;
            }
            ex = r;
            ez = r;
        }

        // The twelve edges of an oriented box indexed by corner bits (bit 0 = X, bit 1 = Y, bit 2 = Z).
        static readonly int[] BoxEdgeStart = { 0, 1, 3, 2, 4, 5, 7, 6, 0, 1, 2, 3 };
        static readonly int[] BoxEdgeEnd = { 1, 3, 2, 0, 5, 7, 6, 4, 4, 5, 6, 7 };

        /// <summary>Clips an oriented box obstacle against the agent's vertical span (0, ag.height)
        /// in horizontal coordinates relative to the agent and returns its clockwise 2D convex hull
        /// (at least 3 vertices, or 0 if no polygon survives). Matches Unity 6000.0.71f1
        /// (tests/projects/navpath obstacle-shapes).</summary>
        internal static int ClipBoxHull(DtCrowdAgent ag, DtCrowdAgent box, Span<float> hullX, Span<float> hullZ)
        {
            float rx = box.npos.X - ag.npos.X;
            float ry = box.npos.Y - ag.npos.Y;
            float rz = box.npos.Z - ag.npos.Z;
            float maxY = ag.option.height;
            RcVec3f h = box.option.boxHalfExtents;
            RcVec3f ux = box.option.boxAxisX;
            RcVec3f uy = box.option.boxAxisY;
            RcVec3f uz = box.option.boxAxisZ;

            if (!float.IsFinite(rx) || !float.IsFinite(ry) || !float.IsFinite(rz) || !float.IsFinite(maxY)
                || !float.IsFinite(h.X) || !float.IsFinite(h.Y) || !float.IsFinite(h.Z)
                || !float.IsFinite(ux.X) || !float.IsFinite(ux.Y) || !float.IsFinite(ux.Z)
                || !float.IsFinite(uy.X) || !float.IsFinite(uy.Y) || !float.IsFinite(uy.Z)
                || !float.IsFinite(uz.X) || !float.IsFinite(uz.Y) || !float.IsFinite(uz.Z))
                return 0;

            Span<float> cx = stackalloc float[8];
            Span<float> cy = stackalloc float[8];
            Span<float> cz = stackalloc float[8];
            for (int i = 0; i < 8; i++)
            {
                float sx = (i & 1) != 0 ? h.X : -h.X;
                float sy = (i & 2) != 0 ? h.Y : -h.Y;
                float sz = (i & 4) != 0 ? h.Z : -h.Z;
                cx[i] = rx + sx * ux.X + sy * uy.X + sz * uz.X;
                cy[i] = ry + sx * ux.Y + sy * uy.Y + sz * uz.Y;
                cz[i] = rz + sx * ux.Z + sy * uy.Z + sz * uz.Z;
            }

            Span<float> px = stackalloc float[32];
            Span<float> pz = stackalloc float[32];
            int npts = 0;

            for (int i = 0; i < 8; i++)
            {
                if (cy[i] > 0f && cy[i] < maxY)
                {
                    px[npts] = cx[i];
                    pz[npts] = cz[i];
                    npts++;
                }
            }

            for (int e = 0; e < 12; e++)
            {
                int i0 = BoxEdgeStart[e];
                int i1 = BoxEdgeEnd[e];
                int lo, hi;
                if (cy[i0] > cy[i1]) { hi = i0; lo = i1; }
                else { hi = i1; lo = i0; }

                float yLo = cy[lo], yHi = cy[hi];
                float dy = yHi - yLo;
                if (yHi > maxY && yLo < maxY)
                {
                    float t = (maxY - yLo) / dy;
                    float omt = 1f - t;
                    px[npts] = cx[hi] * t + cx[lo] * omt;
                    pz[npts] = cz[hi] * t + cz[lo] * omt;
                    npts++;
                }
                if (yHi > 0f && yLo < 0f)
                {
                    float t = (0f - yLo) / dy;
                    float omt = 1f - t;
                    px[npts] = cx[hi] * t + cx[lo] * omt;
                    pz[npts] = cz[hi] * t + cz[lo] * omt;
                    npts++;
                }
            }

            if (npts < 3)
                return 0;

            // Sort ascending by (x, z) for monotone chain convex hull.
            for (int i = 1; i < npts; i++)
            {
                float vx = px[i], vz = pz[i];
                int j = i - 1;
                while (j >= 0 && (px[j] > vx || (px[j] == vx && pz[j] > vz)))
                {
                    px[j + 1] = px[j];
                    pz[j + 1] = pz[j];
                    j--;
                }
                px[j + 1] = vx;
                pz[j + 1] = vz;
            }

            // Clockwise monotone chain in (x, z): keep turns where (A-O).z*(B-O).x - (A-O).x*(B-O).z > 0.
            int m = 0;
            for (int i = 0; i < npts; i++)
            {
                while (m >= 2)
                {
                    float oax = hullX[m - 1] - hullX[m - 2];
                    float oaz = hullZ[m - 1] - hullZ[m - 2];
                    float obx = px[i] - hullX[m - 2];
                    float obz = pz[i] - hullZ[m - 2];
                    if (oaz * obx - oax * obz > 0f)
                        break;
                    m--;
                }
                hullX[m] = px[i];
                hullZ[m] = pz[i];
                m++;
            }

            int upperStart = m + 1;
            for (int i = npts - 2; i >= 0; i--)
            {
                while (m >= upperStart)
                {
                    float oax = hullX[m - 1] - hullX[m - 2];
                    float oaz = hullZ[m - 1] - hullZ[m - 2];
                    float obx = px[i] - hullX[m - 2];
                    float obz = pz[i] - hullZ[m - 2];
                    if (oaz * obx - oax * obz > 0f)
                        break;
                    m--;
                }
                hullX[m] = px[i];
                hullZ[m] = pz[i];
                m++;
            }

            m--;
            return m >= 3 ? m : 0;
        }

        /// <summary>Tests whether an agent cylinder at the origin overlaps an oriented box obstacle's
        /// clipped 2D convex hull, returning the closest hull edge point and penetration depth.
        /// Matches Unity 6000.0.71f1 (tests/projects/navpath obstacle-shapes).</summary>
        internal static bool TryOverlapBox(DtCrowdAgent ag, DtCrowdAgent box,
            out float closestX, out float closestZ, out float pen)
        {
            closestX = closestZ = pen = 0f;
            Span<float> hx = stackalloc float[32];
            Span<float> hz = stackalloc float[32];
            int n = ClipBoxHull(ag, box, hx, hz);
            if (n < 3)
                return false;

            float sumX = 0f, sumZ = 0f;
            for (int i = 0; i < n; i++)
            {
                sumX += hx[i];
                sumZ += hz[i];
            }
            float invN = 1f / n;
            float centX = sumX * invN;
            float centZ = sumZ * invN;
            // Agent is at the origin, so vector from hull centroid to agent is (-centX, -centZ).
            float vx = -centX, vz = -centZ;

            for (int prev = n - 1, curr = 0; curr < n; prev = curr++)
            {
                float ax = hx[prev], az = hz[prev];
                float bx = hx[curr], bz = hz[curr];
                float acx = ax - centX, acz = az - centZ;
                if (acx * vz - acz * vx > 0f)
                    continue;
                float bcx = bx - centX, bcz = bz - centZ;
                if (bcx * vz - bcz * vx < 0f)
                    continue;

                float abx = bx - ax, abz = bz - az;
                float lenSq = abx * abx + abz * abz;
                float cx, cz;
                if (lenSq == 0f)
                {
                    cx = ax;
                    cz = az;
                }
                else
                {
                    float t = (-ax * abx - az * abz) / lenSq;
                    if (t <= 0f) { cx = ax; cz = az; }
                    else if (t >= 1f) { cx = bx; cz = bz; }
                    else { float omt = 1f - t; cx = ax * omt + bx * t; cz = az * omt + bz * t; }
                }

                float distSq = cx * cx + cz * cz;
                // Clockwise hull: origin lies strictly inside when AB x (0 - A) < 0.
                float side = abx * (-az) - (-ax) * abz;
                float agentRadius = ag.option.radius;
                if (side < 0f)
                {
                    closestX = cx;
                    closestZ = cz;
                    pen = MathF.Sqrt(distSq) + agentRadius;
                    return true;
                }
                if (distSq < agentRadius * agentRadius)
                {
                    closestX = cx;
                    closestZ = cz;
                    pen = agentRadius - MathF.Sqrt(distSq);
                    return true;
                }
                return false;
            }

            return false;
        }

        /// <summary>Clips a capsule obstacle against the agent's vertical span [0, ag.height] in
        /// horizontal coordinates relative to the agent's position. Returns 0 (no vertical overlap),
        /// 1 (single clipped cap circle at p0), or 2 (horizontal segment [p0, p1] of radius capRadius).
        /// Matches Unity 6000.0.71f1 (tests/projects/navpath obstacle-separation and obstacle-shapes).</summary>
        internal static int ClipCapsule(DtCrowdAgent ag, DtCrowdAgent capsule,
            out float p0x, out float p0z, out float p1x, out float p1z, out float capRadius)
        {
            p0x = p0z = p1x = p1z = 0f;
            float r = capsule.option.radius;
            capRadius = r;
            float halfShaft = capsule.option.capsuleHalfShaft;
            var u = capsule.option.capsuleAxis;
            if (u.Y < 0f) u = new RcVec3f(-u.X, -u.Y, -u.Z);

            float rx = capsule.npos.X - ag.npos.X;
            float ry = capsule.npos.Y - ag.npos.Y;
            float rz = capsule.npos.Z - ag.npos.Z;
            float maxY = ag.option.height;
            if (!float.IsFinite(rx) || !float.IsFinite(ry) || !float.IsFinite(rz)
                || !float.IsFinite(r) || !float.IsFinite(halfShaft) || !float.IsFinite(maxY)
                || !float.IsFinite(u.X) || !float.IsFinite(u.Y) || !float.IsFinite(u.Z) || r < 0f)
                return 0;

            float dyHalf = halfShaft * u.Y;
            float y0 = ry - dyHalf;
            float y1 = ry + dyHalf;
            float ax = halfShaft * u.X;
            float az = halfShaft * u.Z;
            float x0 = rx - ax, z0 = rz - az;
            float x1 = rx + ax, z1 = rz + az;

            // Entire shaft above the agent's head.
            if (y0 > maxY)
            {
                float dy = y0 - maxY;
                if (dy > r) return 0;
                p0x = x0;
                p0z = z0;
                capRadius = MathF.Sqrt(MathF.Max(0f, r * r - dy * dy));
                if (u.Y > UprightCapCosThreshold) return 1;
                if (y1 - maxY < r)
                {
                    p1x = x1;
                    p1z = z1;
                }
                else
                {
                    float spanY = y1 - y0;
                    float t = spanY > 0f ? Math.Clamp((dy + (1f - u.Y) * r) / spanY, 0f, 1f) : 1f;
                    p1x = x0 * (1f - t) + x1 * t;
                    p1z = z0 * (1f - t) + z1 * t;
                }
                return 2;
            }

            // Entire shaft below the agent's feet.
            if (y1 < 0f)
            {
                float dy = -y1;
                if (dy > r) return 0;
                p0x = x1;
                p0z = z1;
                capRadius = MathF.Sqrt(MathF.Max(0f, r * r - dy * dy));
                if (u.Y > UprightCapCosThreshold) return 1;
                if (-y0 < r)
                {
                    p1x = x0;
                    p1z = z0;
                }
                else
                {
                    float spanY = y1 - y0;
                    float t = spanY > 0f ? Math.Clamp((-y0 - (1f - u.Y) * r) / spanY, 0f, 1f) : 1f;
                    p1x = x0 * (1f - t) + x1 * t;
                    p1z = z0 * (1f - t) + z1 * t;
                }
                return 2;
            }

            // Shaft intersects [0, maxY]: clip any endpoint extending past the agent's height,
            // preserving the horizontal cap margin (1 - u.Y) * r.
            float shaftSpanY = y1 - y0;
            p0x = x0;
            p0z = z0;
            if (y0 < 0f && shaftSpanY > 0f)
            {
                float t0 = Math.Clamp((-y0 - (1f - u.Y) * r) / shaftSpanY, 0f, 1f);
                p0x = x0 * (1f - t0) + x1 * t0;
                p0z = z0 * (1f - t0) + z1 * t0;
            }
            p1x = x1;
            p1z = z1;
            if (y1 > maxY && shaftSpanY > 0f)
            {
                float t1 = Math.Clamp(((maxY - y0) + (1f - u.Y) * r) / shaftSpanY, 0f, 1f);
                p1x = x0 * (1f - t1) + x1 * t1;
                p1z = z0 * (1f - t1) + z1 * t1;
            }
            return 2;
        }

        /// <summary>Projects a capsule obstacle to the nearest point on its clipped horizontal
        /// section relative to <paramref name="ag"/>.</summary>
        internal static bool TryProjectCapsule(DtCrowdAgent ag, DtCrowdAgent capsule,
            out float relX, out float relZ, out float capRadius, out bool singleCap)
        {
            int count = ClipCapsule(ag, capsule, out float p0x, out float p0z, out float p1x, out float p1z, out capRadius);
            if (count == 0)
            {
                relX = relZ = 0f;
                singleCap = false;
                return false;
            }
            if (count == 1)
            {
                relX = p0x;
                relZ = p0z;
                singleCap = true;
                return true;
            }
            float sx = p1x - p0x, sz = p1z - p0z;
            float segLen = MathF.Sqrt(sx * sx + sz * sz);
            if (segLen > CapsuleSegmentLengthEpsilon)
            {
                float dx = sx / segLen, dz = sz / segLen;
                float proj = Math.Clamp(-p0x * dx - p0z * dz, 0f, segLen);
                relX = p0x + dx * proj;
                relZ = p0z + dz * proj;
            }
            else
            {
                relX = p0x;
                relZ = p0z;
            }
            singleCap = false;
            return true;
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

                // Unbound: an agent waiting for an occupied link plans to stand, whatever comes near
                // (Unity's; measured: tests/projects/navlinks crosstile); one about to take a link
                // keeps its desired velocity (navlinks `areajump`).
                if (ag.waitingForConnection || !Avoids(ag)
                    || ag.OverOffmeshConnection(ag.option.radius * LinkTriggerRadiusScale))
                {
                    ag.nvel = ag.dvel;
                    continue;
                }

                _obstacleQuery.Reset();

                // Add neighbours as obstacles, relative to the agent.
                for (int j = 0; j < ag.nneis; ++j)
                {
                    DtCrowdAgent nei = ag.neis[j].agent;
                    float share = AvoidanceShare(ag, nei);
                    if (nei.option.capsuleObstacle)
                    {
                        int count = ClipCapsule(ag, nei, out float p0x, out float p0z, out float p1x, out float p1z, out float capRadius);
                        if (count == 0) continue;
                        float combined = capRadius + ag.option.radius;
                        _obstacleQuery.AddCircle(new RcVec3f(p0x, 0f, p0z), combined, nei.vel, share);
                        if (count == 2 && (p0x != p1x || p0z != p1z))
                        {
                            _obstacleQuery.AddCircle(new RcVec3f(p1x, 0f, p1z), combined, nei.vel, share);
                            _obstacleQuery.AddMovingSegment(p0x, p0z, p1x, p1z, nei.vel.X, nei.vel.Z, combined, share);
                        }
                        continue;
                    }
                    if (nei.option.boxObstacle)
                    {
                        AddBoxAvoidance(ag, nei, share);
                        continue;
                    }
                    RcVec3f rel = RcVec3f.Subtract(nei.npos, ag.npos);
                    _obstacleQuery.AddCircle(rel, nei.option.radius + ag.option.radius, nei.vel, share);
                }

                // Append the NavMesh's edges as obstacles.
                for (int j = 0; j < ag.boundary.GetSegmentCount(); ++j)
                {
                    ref RcFixedArray2<RcVec3f> s = ref ag.boundary.GetSegment(j);
                    _obstacleQuery.AddSegment(RcVec3f.Subtract(s[0], ag.npos), RcVec3f.Subtract(s[1], ag.npos));
                }

                DtObstacleAvoidanceDebugData vod = debugAgent == ag ? debug.vod : null;
                DtObstacleAvoidanceParams option = _obstacleQueryParams[ag.option.obstacleAvoidanceType];
                _velocitySampleCount += _obstacleQuery.SampleVelocityAdaptive(ag.desiredSpeed, ag.vel, ag.dvel, out ag.nvel, option, vod);
            }
        }

        /// <summary>Adds the facing edges of an oriented box's clipped hull as padded moving
        /// segments and their adjacent vertices as circles to the avoidance query.</summary>
        private void AddBoxAvoidance(DtCrowdAgent ag, DtCrowdAgent box, float share)
        {
            Span<float> hx = stackalloc float[32];
            Span<float> hz = stackalloc float[32];
            int n = ClipBoxHull(ag, box, hx, hz);
            if (n < 3)
                return;

            float agentRadius = ag.option.radius;
            Span<bool> active = stackalloc bool[32];
            for (int prev = n - 1, curr = 0; curr < n; prev = curr++)
            {
                float ax = hx[prev], az = hz[prev];
                float bx = hx[curr], bz = hz[curr];
                float ex = bx - ax, ez = bz - az;
                active[prev] = ex * az - ez * ax <= 0f;
            }

            for (int prev = n - 1, curr = 0; curr < n; prev = curr++)
            {
                if (active[prev])
                    _obstacleQuery.AddMovingSegment(hx[prev], hz[prev], hx[curr], hz[curr],
                        box.vel.X, box.vel.Z, agentRadius, share);
                if (active[prev] || active[curr])
                    _obstacleQuery.AddCircle(new RcVec3f(hx[curr], 0f, hz[curr]), agentRadius, box.vel, share);
            }
        }

        /// Unbound: the share of a neighbour's velocity in the velocity an agent avoids it with
        /// (Unity's): an agent the neighbour does not avoid in turn, or a more important one, is
        /// avoided alone (1); equals that avoid each other share it (0.5).
        private static float AvoidanceShare(DtCrowdAgent ag, DtCrowdAgent nei)
        {
            if (nei.option.avoidancePriority < ag.option.avoidancePriority || !Avoids(nei))
                return 1f;
            for (int k = 0; k < nei.nneis; ++k)
                if (nei.neis[k].agent == ag)
                    return 0.5f;
            return 1f;
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

                ag.Integrate(dt, _config.measuredBrakingPlan);
            }
        }

        // Below this distance from a clipped cap's surface, contact uses positive X as its normal.
        const float CapsuleSurfaceNormalEpsilon = 0.0001f;

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

                        if (nei.option.boxObstacle)
                        {
                            if (!TryOverlapBox(ag, nei, out float cx, out float cz, out float boxPen) || boxPen <= 0f)
                                continue;
                            float cLen = MathF.Sqrt(cx * cx + cz * cz);
                            var boxDir = cLen > CapsuleSurfaceNormalEpsilon
                                ? new RcVec3f(-cx / cLen, 0f, -cz / cLen)
                                : new RcVec3f(1f, 0f, 0f);
                            ag.disp = RcVec.Mad(ag.disp, boxDir, boxPen * _config.collisionResolveFactor);
                            w += 1f;
                            continue;
                        }

                        float share = nei.option.avoidancePriority < ag.option.avoidancePriority ? 1.0f : 0.5f;
                        RcVec3f diff = RcVec3f.Subtract(ag.npos, nei.npos);
                        float radius = nei.option.radius;
                        bool capsuleObstacle = nei.option.capsuleObstacle;
                        bool clippedCap = false;
                        if (capsuleObstacle)
                        {
                            if (!TryProjectCapsule(ag, nei, out float relX, out float relZ, out radius, out clippedCap)) continue;
                            diff = new RcVec3f(-relX, 0f, -relZ);
                        }
                        else if (!_config.resolveAgentOverlapsIn3D)
                        {
                            diff.Y = 0f;
                        }

                        float combinedRadius = ag.option.radius + radius;
                        float dist = diff.LengthSquared();
                        float radiusSquared = RcMath.Sqr(combinedRadius);
                        if (dist > radiusSquared || (capsuleObstacle && dist == radiusSquared))
                        {
                            continue;
                        }

                        dist = MathF.Sqrt(dist);
                        float pen = combinedRadius - dist;
                        if (capsuleObstacle)
                        {
                            // A two-point clipped shaft pushes away from its nearest point. A single
                            // clipped cap (nearly upright shaft beyond the agent's height) pushes
                            // away from the nearest point on its circumference, which points inward
                            // when the agent is inside that circle.
                            var direction = new RcVec3f(1f, 0f, 0f);
                            if (dist > 0f && (!clippedCap || MathF.Abs(dist - radius) > CapsuleSurfaceNormalEpsilon))
                            {
                                float sign = clippedCap && dist < radius ? -1f : 1f;
                                direction = new RcVec3f(sign * (diff.X / dist), 0f, sign * (diff.Z / dist));
                            }
                            ag.disp = RcVec.Mad(ag.disp, direction, pen * _config.collisionResolveFactor);
                            w += 1f;
                            continue;
                        }
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
                            float sharedPenetration = pen * share;
                            pen = _config.resolveAgentOverlapsIn3D
                                ? sharedPenetration * _config.collisionResolveFactor / dist
                                : (1.0f / dist) * sharedPenetration * _config.collisionResolveFactor;
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

        const float LinkApproachFraction = 0.15f;

        private void UpdateOffMeshConnections(IList<DtCrowdAgent> agents, float dt)
        {
            using var timer = _telemetry.ScopedTimer(DtCrowdTimerLabel.UpdateOffMeshConnections);

            for (var i = 0; i < agents.Count; i++)
            {
                var ag = agents[i];
                DtCrowdAgentAnimation anim = ag.animation;
                if (!anim.active || ag.option.stopped)
                {
                    continue;
                }

                // Traversal advances in distance, so a speed change takes effect on the next
                // step. The approach occupies 15% of the length
                // (Unity 6000.0.71f1, tests/projects/navlinks).
                anim.distance += (ag.option.maxSpeed + ag.option.maxSpeed) * dt;
                float ta = anim.length * LinkApproachFraction;
                float tb = anim.length;
                if (anim.distance >= anim.length)
                {
                    // Reset animation
                    anim.active = false;
                    // Prepare agent for walking.
                    ag.state = DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING;
                    LeaveOffMeshConnection(ag, anim.distance - anim.length, tb - ta);
                    continue;
                }

                // Update position
                if (anim.distance < ta)
                {
                    float u = Tween(anim.distance, 0.0f, ta);
                    ag.npos = RcVec3f.Lerp(anim.initPos, anim.startPos, u);
                    ag.nvel = AlongAtSpeed(RcVec3f.Subtract(anim.startPos, anim.initPos), ag.option.maxSpeed);
                }
                else
                {
                    float u = Tween(anim.distance, ta, tb);
                    ag.npos = RcVec3f.Lerp(anim.startPos, anim.endPos, u);
                    // Unbound: crossing the link proper, the agent plans along it at its speed (Unity's
                    // desiredVelocity; measured).
                    ag.nvel = AlongAtSpeed(RcVec3f.Subtract(anim.endPos, anim.startPos), ag.option.maxSpeed);
                }

                // Other agents predict this agent at its planned traversal velocity.
                // Its reported velocity still comes from the position change over the frame.
                ag.vel = ag.nvel;
            }
        }

        /// Unbound: <paramref name="v"/> scaled to <paramref name="speed"/> (zero when it has no length).
        private static RcVec3f AlongAtSpeed(RcVec3f v, float speed)
        {
            float length = v.Length();
            return length > 1e-6f ? v * (speed / length) : RcVec3f.Zero;
        }

        /// Unbound: an agent at the end of a link carries on over the rest of the update at its crossing
        /// velocity, from the link's own end onto the NavMesh where it landed, and leaves the link at
        /// full speed in the direction it moved from the link's end (Unity's: the next update walks on
        /// from there, its velocity, height included, turning towards its plan at its acceleration;
        /// measured: tests/projects/navlinks).
        private void LeaveOffMeshConnection(DtCrowdAgent ag, float pastEnd, float crossing)
        {
            var anim = ag.animation;
            var past = anim.endPos;
            if (crossing > 0f) past = RcVec.Mad(past, RcVec3f.Subtract(anim.endPos, anim.startPos), pastEnd / crossing);
            var filter = _filters[ag.option.queryFilterType];
            ag.corridor.MovePosition(past, _navQuery, filter);
            ag.npos = ag.corridor.GetPos();
            ag.vel = AlongAtSpeed(RcVec3f.Subtract(ag.npos, anim.endPos), ag.option.maxSpeed);
            ag.dvel = ag.vel;
            ag.nvel = ag.vel;
            ag.pvel = ag.vel;
            ag.braking = false;
            ag.ncorners = ag.corridor.FindCorners(ag.corners, DtCrowdConst.DT_CROWDAGENT_MAX_CORNERS, _navQuery, filter);
            ag.KeepSteer();
        }

        private float Tween(float t, float t0, float t1)
        {
            return t1 == t0 ? 1f : Math.Clamp((t - t0) / (t1 - t0), 0.0f, 1.0f);
        }
    }
}
