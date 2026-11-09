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

            // Unbound: Unity's grid holds the agents' positions in cells sized by GridCell (over
            // every crowd's agents when the caller gives the sizes, else over this crowd's).
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
                _grid.AddItem(ag, p.X, p.Z, p.X, p.Z);
            }
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

        /// Unbound: an agent's share in Unity's grid cell size: its diameter plus how far it goes
        /// within the prediction time along each axis, at <paramref name="vel"/>.
        public static void AddToGridCell(DtCrowdAgent ag, RcVec3f vel, float horizon, ref float sx, ref float sz)
        {
            sx += 2f * ag.option.radius + horizon * MathF.Abs(vel.X);
            sz += 2f * ag.option.radius + horizon * MathF.Abs(vel.Z);
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

                // Unbound: an agent without avoidance keeps no edges; it still gathers neighbours,
                // which push it out of overlaps (AI Navigation 2.0 manual, Quality None: "collisions
                // resolve"), and avoids none of them (PlanVelocity).
                float horizon = _obstacleQueryParams[ag.option.obstacleAvoidanceType].horizTime;
                if (Avoids(ag)) UpdateBoundary(ag, horizon);
                else ag.boundary.Reset();

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

                if (!HeightsOverlap(skip, ag.npos, ag.option.height))
                {
                    continue;
                }

                // In 3D, as Unity's agents measure (a neighbour above or below is farther).
                RcVec3f diff = RcVec3f.Subtract(ag.npos, pos);
                float radii = skip.option.radius + ag.option.radius;
                if (moving && RcVec3f.Dot(diff, heading) < -radii)
                {
                    continue;
                }

                // Both at full speed (an agent standing with both speeds zero: at once if touching).
                float gap = diff.Length() - radii, closing = skip.option.reachSpeed + ag.vel.Length();
                float time = closing > 0f ? gap / closing : gap <= 0f ? 0f : float.PositiveInfinity;
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
                        anim.t = 0.0f;
                        // Unbound: Unity's agents cross from the start on the NavMesh to the link's own
                        // end point (not the point the end landed on), taking half the time the
                        // distance between them in 3D takes at full speed (measured).
                        anim.endPos = linkEnd;
                        anim.tmax = (RcVec3f.Distance(anim.startPos, anim.endPos) / ag.option.maxSpeed) * 0.5f;

                        ag.state = DtCrowdAgentState.DT_CROWDAGENT_STATE_OFFMESH;
                        ag.ncorners = 0;
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
                    if (ag.waitingForConnection) dvel = RcVec3f.Zero;
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
                    _obstacleQuery.AddCircle(RcVec3f.Subtract(nei.npos, ag.npos), nei.option.radius + ag.option.radius,
                        nei.vel, AvoidanceShare(ag, nei));
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

                anim.t += dt;
                float ta = anim.tmax * 0.15f;
                float tb = anim.tmax;
                if (anim.t > anim.tmax)
                {
                    // Reset animation
                    anim.active = false;
                    // Prepare agent for walking.
                    ag.state = DtCrowdAgentState.DT_CROWDAGENT_STATE_WALKING;
                    LeaveOffMeshConnection(ag, anim.t - anim.tmax, tb - ta);
                    continue;
                }

                // Update position
                if (anim.t < ta)
                {
                    float u = Tween(anim.t, 0.0f, ta);
                    ag.npos = RcVec3f.Lerp(anim.initPos, anim.startPos, u);
                }
                else
                {
                    float u = Tween(anim.t, ta, tb);
                    ag.npos = RcVec3f.Lerp(anim.startPos, anim.endPos, u);
                    // Unbound: crossing the link proper, the agent plans along it at its speed (Unity's
                    // desiredVelocity; on the way to the link's start it keeps its plan; measured).
                    ag.nvel = AlongAtSpeed(RcVec3f.Subtract(anim.endPos, anim.startPos), ag.option.maxSpeed);
                }

                // Update velocity.
                ag.vel = RcVec3f.Zero;
                ag.dvel = RcVec3f.Zero;
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
            return Math.Clamp((t - t0) / (t1 - t0), 0.0f, 1.0f);
        }
    }
}