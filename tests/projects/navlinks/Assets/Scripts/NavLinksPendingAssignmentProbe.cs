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
