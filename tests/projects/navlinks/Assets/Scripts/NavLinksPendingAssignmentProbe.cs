using System;
using System.Collections;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

public partial class NavLinksProbe
{
    IEnumerator RecordPendingAssignmentControls()
    {
        var local = new Lane { Name = "pending", O = Lanes.Origin(Lanes.Index("disableagent")) };
        local.Link = AddLink(local, new Vector3(-2f, 0f, 0f), new Vector3(2f, 0f, 0f));
        yield return null;
        foreach (bool afterUpdate in new[] { false, true })
        {
            foreach (string mode in new[] { "idleNear", "activeNear", "idleFar", "activeFar", "markCost", "reset", "invalid", "repeat" })
                yield return RecordPendingAssignment(local, afterUpdate, mode, false);
            foreach (string mode in new[] { "idleNear", "idleFar" })
                yield return RecordPendingAssignment(local, afterUpdate, mode, true);
        }
        yield return RecordPendingAssignment(local, true, "blocked", false);

        // Limit only the CalculatePath query so it ends before the link, while the agent's
        // pending destination can still cross it.
        DestroyImmediate(local.Link.gameObject);
        local.Link = AddLink(local, new Vector3(-2f, 0f, 0f), new Vector3(2f, 0f, 0f), area: 4);
        yield return null;
        foreach (bool afterUpdate in new[] { false, true })
            yield return RecordPendingAssignment(local, afterUpdate, "partial", false);

        DestroyImmediate(local.Link.gameObject);
        local.Link = AddLink(local, new Vector3(-2f, 0f, 0f), new Vector3(2f, 0f, 0f));
        foreach (bool afterUpdate in new[] { false, true })
        foreach (string mode in new[] { "toggleNear", "toggleFar" })
        {
            local.Link.activated = true;
            yield return null;
            yield return RecordPendingAssignment(local, afterUpdate, mode, false);
        }
        yield return RecordRevalidationControls(local);
        yield return RecordResubmissionControls(local);
        yield return RecordSearchFilterControls(local);
        yield return RecordPartialRepairControls(local);
        yield return RecordAreaMaskRepairControls(local);
        Finish(0);
    }

    IEnumerator RecordRevalidationControls(Lane local)
    {
        DestroyImmediate(local.Link.gameObject);
        local.Link = AddLink(local, new Vector3(-2f, 0f, 0f), new Vector3(2f, 0f, 0f), cost: 1f, area: 4);
        var alternate = AddLink(local, new Vector3(-2f, 0f, 2f), new Vector3(2f, 0f, 2f), cost: 20f, area: 5);
        foreach (bool hasAlternate in new[] { false, true })
        foreach (bool afterUpdate in new[] { false, true })
        foreach (string mode in new[] { "beforeNear", "afterNear" })
        {
            local.Link.activated = true;
            alternate.activated = hasAlternate;
            yield return null;
            yield return RecordPendingAssignment(local, afterUpdate, mode, false,
                hasAlternate ? "alternate" : "disconnected", alternate);
        }

        local.Link.activated = true;
        alternate.activated = true;
        yield return null;
        yield return RecordPendingAssignment(local, true, "unrelatedBefore", false, "control", alternate);

        foreach (bool afterUpdate in new[] { false, true })
        foreach (string mode in new[] { "costBefore", "costBeforeFar" })
        {
            local.Link.activated = true;
            alternate.activated = true;
            yield return null;
            yield return RecordPendingAssignment(local, afterUpdate, mode, false, "control", alternate);
        }

        var third = AddLink(local, new Vector3(-2f, 0f, 1f), new Vector3(2f, 0f, 1f), cost: 50f, area: 6);
        foreach (string mode in new[] { "lateLink", "thirdRoute" })
        {
            local.Link.activated = true;
            alternate.activated = true;
            third.activated = mode == "thirdRoute";
            yield return null;
            yield return RecordPendingAssignment(local, true, mode, false, "control", alternate);
        }
        local.Link.activated = true;
        alternate.activated = true;
        DestroyImmediate(third.gameObject);
        yield return null;
        yield return RecordSearchStartControls(local);
        DestroyImmediate(alternate.gameObject);
    }

    IEnumerator RecordSearchStartControls(Lane local)
    {
        var disconnected = Lanes.Origin(Lanes.Index("inactive")) + new Vector3(-5f, 0f, 0);
        foreach (int budget in new[] { 1, 2 })
        foreach (string mode in new[] { "noneFar", "nearFar", "nearDisconnected", "multiDisconnected", "partialFar" })
        {
            var lane = new Lane { Name = "initialSearch." + mode + "." + budget, O = local.O };
            var agent = Agent(lane, "a", new Vector3(-8f, 0f, -0.3f), null, a =>
            {
                a.speed = 2.5f;
                a.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            });
            yield return null;
            int previousBudget = NavMesh.pathfindingIterationsPerFrame;
            NavMesh.pathfindingIterationsPerFrame = 100000;
            Vector3 near = lane.O + new Vector3(-5f, 0f, -0.3f);
            Vector3 far = lane.O + new Vector3(8f, 0f, -0.3f);
            if (!mode.StartsWith("none", StringComparison.Ordinal))
            {
                bool partial = mode == "partialFar";
                Vector3 previous = partial ? disconnected : mode == "multiDisconnected" ? far : near;
                SetTestDestination(agent, previous);
                for (int frame = 0; frame < 5; frame++) yield return null;
                var previousStatus = partial ? NavMeshPathStatus.PathPartial : NavMeshPathStatus.PathComplete;
                if (!agent.hasPath || agent.pathStatus != previousStatus)
                    throw new InvalidOperationException("previous path was not reported");
            }
            NavMesh.pathfindingIterationsPerFrame = budget;
            SetTestDestination(agent, mode.EndsWith("Disconnected", StringComparison.Ordinal) ? disconnected : far);
            if (!agent.pathPending) throw new InvalidOperationException("expected a pending destination");
            for (int frame = 0; frame <= 8; frame++)
            {
                if (frame == 1) agent.SetAreaCost(4, 4f);
                if (frame <= 4 || frame == 8)
                    Line(lane.Name, "a", frame, "\"pending\":" + B(agent.pathPending) + ",\"path\":" + B(agent.hasPath)
                        + ",\"stale\":" + B(agent.isPathStale) + ",\"position\":" + V(agent.nextPosition) + ",\"velocity\":" + V(agent.velocity)
                        + ",\"destination\":" + V(agent.destination) + ",\"end\":" + V(agent.pathEndPosition)
                        + ",\"status\":" + Text(agent.pathStatus.ToString()));
                yield return null;
            }
            NavMesh.pathfindingIterationsPerFrame = previousBudget;
            DestroyImmediate(agent.gameObject);
            yield return null;
        }
    }

    IEnumerator RecordResubmissionControls(Lane local)
    {
        DestroyImmediate(local.Link.gameObject);
        local.Link = AddLink(local, new Vector3(-2f, 0f, 0f), new Vector3(2f, 0f, 0f));
        yield return null;
        foreach (bool started in new[] { false, true })
        foreach (bool assigned in new[] { false, true })
        foreach (string next in new[] { "same", "near", "disconnected" })
        foreach (bool behind in (next == "near" && started ? new[] { false, true } : new[] { false }))
        {
            var lane = new Lane
            {
                Name = "resubmitted." + (started ? "started" : "queued") + "." + (assigned ? "assigned" : "plain") + "." + next + (behind ? ".behind" : ""),
                O = local.O
            };
            var agent = Agent(lane, "a", new Vector3(-8f, 0f, -0.3f), null, a =>
            {
                a.speed = 0f;
                a.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            });
            NavMeshAgent other = null;
            if (behind)
                other = Agent(lane, "other", new Vector3(-8f, 0f, 0.8f), null, a =>
                {
                    a.speed = 0f;
                    a.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
                });
            yield return null;
            int original = NavMesh.pathfindingIterationsPerFrame;
            NavMesh.pathfindingIterationsPerFrame = 1;
            Vector3 far = lane.O + new Vector3(8f, 0f, -0.3f);
            Vector3 near = lane.O + new Vector3(-5f, 0f, 0.7f);
            SetTestDestination(agent, far);
            if (behind) SetTestDestination(other, far);
            if (started) yield return null;
            if (!agent.pathPending) throw new InvalidOperationException("expected a pending destination");
            if (assigned)
            {
                var path = new NavMeshPath();
                if (!agent.CalculatePath(near, path) || !agent.SetPath(path))
                    throw new InvalidOperationException("could not assign near path");
            }
            var replacement = next == "same" ? far - Vector3.forward * 0.5f
                : next == "near" ? near - Vector3.forward * 0.5f
                : Lanes.Origin(Lanes.Index("inactive")) + new Vector3(-5f, 0f, 0);
            SetTestDestination(agent, replacement);
            for (int frame = 0; frame <= 8; frame++)
            {
                if (frame <= 5 || frame == 8)
                {
                    string fields = "\"pending\":" + B(agent.pathPending) + ",\"path\":" + B(agent.hasPath)
                        + ",\"destination\":" + V(agent.destination) + ",\"end\":" + V(agent.pathEndPosition)
                        + ",\"status\":" + Text(agent.pathStatus.ToString());
                    if (other != null) fields += ",\"otherPending\":" + B(other.pathPending) + ",\"otherPath\":" + B(other.hasPath);
                    Line(lane.Name, "a", frame, fields);
                }
                yield return null;
            }
            NavMesh.pathfindingIterationsPerFrame = original;
            DestroyImmediate(agent.gameObject);
            if (other != null) DestroyImmediate(other.gameObject);
            yield return null;
        }
    }

    IEnumerator RecordSearchFilterControls(Lane lane)
    {
        DestroyImmediate(lane.Link.gameObject);
        var first = AddLink(lane, new Vector3(-2f, 0f, 0f), new Vector3(2f, 0f, 0f), area: 4);
        var alternate = AddLink(lane, new Vector3(-2f, 0f, 2f), new Vector3(2f, 0f, 2f), area: 5);
        yield return null;
        int previousBudget = NavMesh.pathfindingIterationsPerFrame;
        float oldFirstCost = NavMesh.GetAreaCost(4), oldAlternateCost = NavMesh.GetAreaCost(5);
        NavMesh.SetAreaCost(4, 1);
        NavMesh.SetAreaCost(5, 2);
        foreach (string mode in new[] { "unchanged", "highBefore", "localFirst", "localExisting", "global", "mask", "localExistingAssigned", "globalAssigned", "maskAssigned" })
        {
            var a = Agent(lane, mode, new Vector3(-8f, 0f, -0.3f), null, x => {
                x.speed = 0f;
                x.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            });
            yield return null;
            if (mode.Contains("Existing", StringComparison.Ordinal)) a.SetAreaCost(7, a.GetAreaCost(7));
            if (mode == "highBefore") a.SetAreaCost(4, 100);
            NavMesh.pathfindingIterationsPerFrame = 1;
            var far = lane.O + new Vector3(8f, 0f, -0.3f);
            SetTestDestination(a, far);
            yield return null;
            if (!a.pathPending) throw new InvalidOperationException("search did not pend");
            if (mode.StartsWith("local", StringComparison.Ordinal)) a.SetAreaCost(4, 100);
            if (mode.StartsWith("global", StringComparison.Ordinal)) NavMesh.SetAreaCost(4, 100);
            if (mode.StartsWith("mask", StringComparison.Ordinal)) a.areaMask &= ~(1 << 4);
            if (mode.EndsWith("Assigned", StringComparison.Ordinal))
            {
                var p = new NavMeshPath();
                if (!a.CalculatePath(lane.O + new Vector3(-5f, 0f, 0.7f), p) || !a.SetPath(p)) throw new InvalidOperationException("assignment failed");
                if (!a.pathPending) throw new InvalidOperationException("search dropped");
            }
            for (int frame = 0; frame <= 24; frame++)
            {
                var link = a.nextOffMeshLinkData;
                Line("area." + mode, "a", frame, "\"pending\":" + B(a.pathPending) + ",\"path\":" + B(a.hasPath) + ",\"stale\":" + B(a.isPathStale)
                    + ",\"end\":" + V(a.pathEndPosition) + ",\"cost\":" + a.GetAreaCost(4).ToString(System.Globalization.CultureInfo.InvariantCulture)
                    + ",\"valid\":" + B(link.valid) + ",\"link\":" + V(link.startPos));
                if (!a.pathPending && frame >= 12) break;
                yield return null;
            }
            NavMesh.SetAreaCost(4, 1);
            DestroyImmediate(a.gameObject);
            yield return null;
        }
        NavMesh.SetAreaCost(4, oldFirstCost);
        NavMesh.SetAreaCost(5, oldAlternateCost);
        NavMesh.pathfindingIterationsPerFrame = previousBudget;
        DestroyImmediate(first.gameObject);
        DestroyImmediate(alternate.gameObject);
    }

    IEnumerator RecordPartialRepairControls(Lane lane)
    {
        var first = AddLink(lane, new Vector3(-2f, 0f, 0f), new Vector3(2f, 0f, 0f), area: 4, cost: 1);
        var alternate = AddLink(lane, new Vector3(-2f, 0f, 2f), new Vector3(2f, 0f, 2f), area: 5, cost: 7);
        int prior = NavMesh.pathfindingIterationsPerFrame;
        foreach (bool hasAlternate in new[] { false, true })
        foreach (bool beforeAssignment in new[] { false, true })
        {
            first.activated = true;
            alternate.activated = hasAlternate;
            yield return null;
            var a = Agent(lane, "a", new Vector3(-8f, 0f, -0.3f), null, x => {
                x.speed = 0f;
                x.autoRepath = false;
                x.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            });
            NavMesh.pathfindingIterationsPerFrame = 1;
            yield return null;
            SetTestDestination(a, lane.O + new Vector3(35f, 0f, -0.3f));
            yield return null;
            if (!a.pathPending) throw new InvalidOperationException("search did not pend");
            if (beforeAssignment) first.activated = false;
            var p = new NavMeshPath();
            if (!a.CalculatePath(lane.O + new Vector3(-5f, 0f, 0.7f), p) || !a.SetPath(p))
                throw new InvalidOperationException("assignment failed");
            for (int frame = 0; frame <= 10; frame++)
            {
                if (!beforeAssignment && frame == 6)
                {
                    if (a.pathPending || a.pathStatus != NavMeshPathStatus.PathPartial || !a.nextOffMeshLinkData.valid)
                        throw new InvalidOperationException("expected to have a partial path through the first link");
                    first.activated = false;
                }
                var link = a.nextOffMeshLinkData;
                Line("partial." + B(hasAlternate) + "." + B(beforeAssignment), "a", frame,
                     "\"pending\":" + B(a.pathPending) + ",\"path\":" + B(a.hasPath) + ",\"stale\":" + B(a.isPathStale)
                     + ",\"status\":" + Text(a.pathStatus.ToString()) + ",\"destination\":" + V(a.destination)
                     + ",\"end\":" + V(a.pathEndPosition) + ",\"valid\":" + B(link.valid) + ",\"link\":" + V(link.startPos));
                yield return null;
            }
            DestroyImmediate(a.gameObject);
            yield return null;
        }
        NavMesh.pathfindingIterationsPerFrame = prior;
        DestroyImmediate(first.gameObject);
        DestroyImmediate(alternate.gameObject);
    }

    IEnumerator RecordAreaMaskRepairControls(Lane lane)
    {
        var first = AddLink(lane, new Vector3(-2f, 0f, 0f), new Vector3(2f, 0f, 0f), area: 4, cost: 1);
        var alternate = AddLink(lane, new Vector3(-2f, 0f, 2f), new Vector3(2f, 0f, 2f), area: 5, cost: 7);
        int previousBudget = NavMesh.pathfindingIterationsPerFrame;
        var cases = new (string Name, bool Alternate, bool Assigned, bool Established, bool AutoRepath, int Area)[]
        {
            ("disconnected",          false, false, false, true,  4),
            ("disconnectedAssigned",  false, true,  false, true,  4),
            ("established",           true,  false, true,  true,  4),
            ("establishedNoAuto",     true,  false, true,  false, 4),
            ("disconnectedNoAuto",    false, false, true,  false, 4),
            ("unrelated",             true,  false, false, true,  6),
        };
        foreach (var test in cases)
        {
            alternate.activated = test.Alternate;
            yield return null;
            var agent = Agent(lane, "a", new Vector3(-8f, 0f, -0.3f), null, a =>
            {
                a.speed = 0f;
                a.autoRepath = test.AutoRepath;
                a.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            });
            yield return null;
            NavMesh.pathfindingIterationsPerFrame = 1;
            SetTestDestination(agent, lane.O + new Vector3(8f, 0f, -0.3f));
            yield return null;
            if (!agent.pathPending) throw new InvalidOperationException("search did not pend");
            if (!test.Established) agent.areaMask &= ~(1 << test.Area);
            if (test.Assigned)
            {
                var path = new NavMeshPath();
                if (!agent.CalculatePath(lane.O + new Vector3(-5f, 0f, 0.7f), path) || !agent.SetPath(path))
                    throw new InvalidOperationException("assignment failed");
            }
            for (int frame = 0; frame <= 12; frame++)
            {
                if (test.Established && frame == 6)
                {
                    if (agent.pathPending || !agent.nextOffMeshLinkData.valid)
                        throw new InvalidOperationException("expected an established corridor");
                    agent.areaMask &= ~(1 << test.Area);
                }
                var link = agent.nextOffMeshLinkData;
                Line("maskRepair." + test.Name, "a", frame,
                    "\"pending\":" + B(agent.pathPending) + ",\"path\":" + B(agent.hasPath) + ",\"stale\":" + B(agent.isPathStale)
                    + ",\"status\":" + Text(agent.pathStatus.ToString()) + ",\"destination\":" + V(agent.destination)
                    + ",\"end\":" + V(agent.pathEndPosition) + ",\"valid\":" + B(link.valid) + ",\"link\":" + V(link.startPos));
                yield return null;
            }
            DestroyImmediate(agent.gameObject);
            yield return null;
        }
        NavMesh.pathfindingIterationsPerFrame = previousBudget;
        DestroyImmediate(first.gameObject);
        DestroyImmediate(alternate.gameObject);
    }

    IEnumerator RecordPendingAssignment(Lane local, bool afterUpdate, string mode, bool moving,
        string group = null, NavMeshLink alternate = null)
    {
        string phase = mode == "blocked" ? "waiting" : afterUpdate ? "started" : "queued";
        string name = "assignedPending." + phase + "." + (group == null ? "" : group + ".") + mode + (moving ? ".moving" : "");
        var lane = new Lane { Name = name, O = local.O };
        var agent = Agent(lane, "a", new Vector3(-8f, 0f, -0.3f), null, a =>
        {
            a.speed = moving ? 2.5f : 0f;
            a.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
        });
        int previousBudget = NavMesh.pathfindingIterationsPerFrame;
        yield return null;
        Vector3 near = lane.O + new Vector3(-5f, 0f, -0.3f);
        Vector3 far = lane.O + new Vector3(8f, 0f, -0.3f);
        bool changeCostBeforeAssignment = mode.StartsWith("costBefore", StringComparison.Ordinal);
        if (mode.StartsWith("active", StringComparison.Ordinal) || changeCostBeforeAssignment)
        {
            SetTestDestination(agent, near);
            for (int frame = 0; frame < 5; frame++) yield return null;
            if (!agent.hasPath) throw new InvalidOperationException("previous path was not reported");
        }

        NavMesh.pathfindingIterationsPerFrame = 1;
        NavMeshAgent blocker = null;
        if (mode == "blocked")
        {
            blocker = Agent(lane, "blocker", new Vector3(-8f, 0f, 0.65f), null, a =>
            {
                a.speed = 0f;
                a.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            });
            SetTestDestination(blocker, far);
        }
        if (mode == "thirdRoute")
        {
            var test = new NavMeshPath();
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = ~((1 << 4) | (1 << 5)) };
            if (!NavMesh.CalculatePath(agent.nextPosition, far, filter, test) || test.status != NavMeshPathStatus.PathComplete)
                throw new InvalidOperationException("third route must reach the destination");
        }
        SetTestDestination(agent, far);
        if (afterUpdate) yield return null;
        if (!agent.pathPending) throw new InvalidOperationException("expected a pending destination");

        if (mode == "beforeNear") local.Link.activated = false;
        if (mode == "unrelatedBefore") alternate.activated = false;
        if (changeCostBeforeAssignment) agent.SetAreaCost(4, 4f);
        bool staleBeforeAssignment = agent.isPathStale;
        var path = new NavMeshPath();
        bool invalid = mode == "invalid";
        if (mode == "partial")
        {
            var filter = new NavMeshQueryFilter { agentTypeID = agent.agentTypeID, areaMask = ~(1 << 4) };
            if (!NavMesh.CalculatePath(agent.nextPosition, far, filter, path) || path.status != NavMeshPathStatus.PathPartial)
                throw new InvalidOperationException("expected a partial assignment");
        }
        else if (!invalid)
        {
            Vector3 goal = mode.EndsWith("Far", StringComparison.Ordinal) ? far + Vector3.forward : near + Vector3.forward;
            if (!agent.CalculatePath(goal, path)) throw new InvalidOperationException("test route was not found");
        }
        if (agent.SetPath(path) == invalid) throw new InvalidOperationException("unexpected assignment result");
        if (mode == "repeat")
        {
            if (!agent.CalculatePath(near - Vector3.forward * 0.5f, path) || !agent.SetPath(path))
                throw new InvalidOperationException("second assignment was not applied");
        }
        if (mode == "markCost") agent.SetAreaCost(4, 4f);
        if (mode.StartsWith("toggle", StringComparison.Ordinal)) local.Link.activated = false;
        if (mode is "afterNear" or "thirdRoute") local.Link.activated = false;
        if (mode == "reset") agent.ResetPath();
        bool recordLateUpdates = mode is "lateLink" or "thirdRoute";
        for (int frame = 0; frame <= 8; frame++)
        {
            if (mode == "lateLink" && frame == 5) local.Link.activated = false;
            if (mode == "thirdRoute" && frame == 6) alternate.activated = false;
            if (frame <= (recordLateUpdates ? 7 : 4) || frame == 8)
            {
                string fields = "\"pending\":" + B(agent.pathPending) + ",\"path\":" + B(agent.hasPath)
                    + ",\"stale\":" + B(agent.isPathStale) + ",\"destination\":" + V(agent.destination)
                    + ",\"end\":" + V(agent.pathEndPosition) + ",\"status\":" + Text(agent.pathStatus.ToString());
                if (group != null)
                {
                    fields += ",\"staleBefore\":" + B(staleBeforeAssignment) + ",\"steer\":" + V(agent.steeringTarget)
                        + ",\"link\":" + V(agent.nextOffMeshLinkData.startPos);
                }
                if (moving) fields += ",\"position\":" + V(agent.nextPosition) + ",\"velocity\":" + V(agent.velocity);
                if (blocker != null) fields += ",\"blockerPending\":" + B(blocker.pathPending);
                Line(lane.Name, "a", frame, fields);
            }
            yield return null;
        }
        NavMesh.pathfindingIterationsPerFrame = previousBudget;
        DestroyImmediate(agent.gameObject);
        if (blocker != null) DestroyImmediate(blocker.gameObject);
        yield return null;
    }
}
