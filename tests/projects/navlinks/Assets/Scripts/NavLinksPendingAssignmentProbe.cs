using System;
using System.Collections;
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
        Finish(0);
    }

    IEnumerator RecordPendingAssignment(Lane local, bool afterUpdate, string mode, bool moving)
    {
        string phase = mode == "blocked" ? "waiting" : afterUpdate ? "started" : "queued";
        var lane = new Lane { Name = "assignedPending." + phase + "." + mode + (moving ? ".moving" : ""), O = local.O };
        var agent = Agent(lane, "a", new Vector3(-8f, 0f, -0.3f), null, a =>
        {
            a.speed = moving ? 2.5f : 0f;
            a.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
        });
        int previousBudget = NavMesh.pathfindingIterationsPerFrame;
        yield return null;
        Vector3 near = lane.O + new Vector3(-5f, 0f, -0.3f);
        Vector3 far = lane.O + new Vector3(8f, 0f, -0.3f);
        if (mode.StartsWith("active", StringComparison.Ordinal))
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
        SetTestDestination(agent, far);
        if (afterUpdate) yield return null;
        if (!agent.pathPending) throw new InvalidOperationException("expected a pending destination");

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
        if (mode == "reset") agent.ResetPath();
        for (int frame = 0; frame <= 8; frame++)
        {
            if (frame <= 4 || frame == 8)
            {
                string fields = "\"pending\":" + B(agent.pathPending) + ",\"path\":" + B(agent.hasPath)
                    + ",\"stale\":" + B(agent.isPathStale) + ",\"destination\":" + V(agent.destination)
                    + ",\"end\":" + V(agent.pathEndPosition) + ",\"status\":" + Text(agent.pathStatus.ToString());
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
