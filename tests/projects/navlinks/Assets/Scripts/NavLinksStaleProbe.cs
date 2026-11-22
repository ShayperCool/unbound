using System;
using System.Collections;
using Unity.AI.Navigation;
using UnityEngine;
using UnityEngine.AI;

public partial class NavLinksProbe
{
    static readonly int[] s_StaleReadDelay = { 0, 1, 5, 28 };
    static readonly string[] s_IdlePlacement = {
        "fresh", "new", "newOnFloor", "warp", "warpexact", "reset", "enable", "enableOffMesh",
        "warpUncoupled", "enableUncoupled", "warpFailedGoal", "translate", "sameTransform",
        "exactThenMove", "exactThenNext", "exactThenTransform", "exactThenRotate", "exactThenScale",
        "exactThenParent", "exactThenReset", "enableThenTransform", "enableThenWarp", "exactThenAcceptedGoal",
        "freshNoop", "freshRoundTrip", "exactNoop", "exactRoundTrip", "exactSameRotation",
        "exactRotateRoundTrip", "enableReadDisabledThenCoupled", "enableReadCoupledThenDisabled",
    };
    static readonly string[] s_StaleQueries = {
        "next", "remaining", "calculate", "edge", "raycast", "sample", "end", "path",
        "transform", "owner", "onMesh", "onLink", "destination", "desired", "steering", "velocity",
        "hasPath", "pathStatus", "failedDestination", "setPositionSame", "moveZero", "velocitySet", "reset", "detach",
    };

    IEnumerator RecordStaleControls()
    {
        var remote = new Lane { Name = "remote", O = Lanes.Origin(Lanes.Index("crosstile")) };
        var remoteLink = AddLink(remote, new Vector3(-2f, 0f, 0f), new Vector3(2f, 0f, 0f));
        var local = new Lane { Name = "local", O = Lanes.Origin(Lanes.Index("disableagent")) };
        local.Link = AddLink(local, new Vector3(-2f, 0f, 0f), new Vector3(2f, 0f, 0f));
        yield return null;
        foreach (string mode in s_IdlePlacement)
        foreach (int lag in s_StaleReadDelay)
            yield return RecordIdlePlacement(local, remoteLink, mode, lag);

        // Each call gets its own agent. An earlier syncing call could otherwise hide the effect.
        foreach (string placement in new[] { "exact", "translate", "edgeMove", "activePath" })
        foreach (string mode in s_StaleQueries)
            yield return RecordStaleQuery(local, remoteLink, placement, mode);
        yield return RecordStaleHierarchy(remoteLink);
        yield return RecordStalePathReset(remoteLink, local.O);
        yield return RecordCloneStaleness(remoteLink, local.O);
        yield return RecordAreaStaleness(local, remoteLink);
        Finish(0);
    }

    IEnumerator RecordIdlePlacement(Lane local, NavMeshLink remoteLink, string mode, int lag)
    {
        remoteLink.activated = true;
        var lane = new Lane { Name = "stalePlacement." + mode, O = local.O, Link = local.Link };
        bool free = mode is "fresh" or "new" or "newOnFloor" or "translate" or "sameTransform"
            or "freshNoop" or "freshRoundTrip" or "exactNoop" or "exactRoundTrip"
            or "exactSameRotation" or "exactRotateRoundTrip" or "enableReadDisabledThenCoupled" or "enableReadCoupledThenDisabled";
        Vector3? goal = free ? null : new Vector3(mode == "enableOffMesh" ? 8f : -3f, 0f, -0.3f);
        Vector3 at = new Vector3(-8f, 0f, -0.3f);
        if (mode == "newOnFloor")
        {
            if (!NavMesh.SamplePosition(lane.O + at, out var floor, 0.5f, NavMesh.AllAreas))
                throw new InvalidOperationException("test agent has no floor");
            at = floor.position - lane.O;
        }
        var agent = Agent(lane, "a", at, goal, a => a.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance);
        bool newAgent = mode is "new" or "newOnFloor";
        if (!newAgent)
            for (int i = 0; i < 15; i++) yield return null;
        if (mode == "enableOffMesh")
            for (int i = 0; i < 300 && !agent.isOnOffMeshLink; i++) yield return null;
        bool onLink = agent.isOnOffMeshLink;
        if (mode == "enableOffMesh" && !onLink) throw new InvalidOperationException("test agent never entered its link");

        if (mode.EndsWith("Uncoupled", StringComparison.Ordinal)) agent.updatePosition = false;
        if (mode == "reset") agent.ResetPath();
        if (mode is "warp" or "warpUncoupled" or "warpFailedGoal")
            CheckWarp(agent, lane.O + new Vector3(-6f, 0f, -0.3f));
        if (mode == "warpexact" || mode.StartsWith("exact", StringComparison.Ordinal))
            CheckWarp(agent, agent.nextPosition);
        if (mode.StartsWith("enable", StringComparison.Ordinal))
        {
            agent.enabled = false;
            if (onLink)
                for (int i = 0; i < 17; i++) yield return null;
            agent.enabled = true;
        }

        if (mode.StartsWith("exact", StringComparison.Ordinal) || mode is "enableThenTransform" or "enableThenWarp"
            or "freshNoop" or "freshRoundTrip" or "enableReadDisabledThenCoupled" or "enableReadCoupledThenDisabled")
            yield return null;
        Vector3 delta = Vector3.right * 0.1f;
        GameObject parent = null;
        switch (mode)
        {
            case "exactThenMove": agent.Move(delta); break;
            case "exactThenNext": agent.nextPosition += delta; break;
            case "exactThenTransform":
            case "enableThenTransform": agent.transform.position += delta; break;
            case "exactThenRotate": agent.transform.rotation *= Quaternion.Euler(0f, 1f, 0f); break;
            case "exactThenScale": agent.transform.localScale *= 1.1f; break;
            case "exactThenReset": agent.ResetPath(); break;
            case "exactThenParent":
                parent = new GameObject("agent parent");
                agent.transform.SetParent(parent.transform, true);
                parent.transform.position += delta;
                break;
            case "enableThenWarp": CheckWarp(agent, agent.nextPosition + delta); break;
            case "freshNoop":
            case "exactNoop": agent.transform.position = agent.transform.position; break;
            case "freshRoundTrip":
            case "exactRoundTrip":
                var position = agent.transform.position;
                agent.transform.position = position + delta;
                agent.transform.position = position;
                break;
            case "exactSameRotation": agent.transform.rotation = agent.transform.rotation; break;
            case "exactRotateRoundTrip":
                var rotation = agent.transform.rotation;
                agent.transform.rotation = rotation * Quaternion.Euler(0f, 1f, 0f);
                agent.transform.rotation = rotation;
                break;
            case "enableReadDisabledThenCoupled": agent.updatePosition = false; break;
        }

        bool rejected = mode == "warpFailedGoal" && !agent.SetDestination(lane.O + new Vector3(-6f, 500f, -0.3f));
        if (mode == "exactThenAcceptedGoal") rejected = !agent.SetDestination(lane.O + new Vector3(-4f, 0f, -0.3f));
        for (int i = 0; i < lag; i++) yield return null;
        bool previous = agent.isPathStale;
        remoteLink.activated = false;
        bool staleAfterLink = agent.isPathStale;
        if (mode == "translate") agent.transform.position += delta;
        if (mode == "sameTransform") agent.transform.position = agent.transform.position;
        var first = agent.nextPosition;
        bool afterFirst = agent.isPathStale;
        if (mode == "enableReadDisabledThenCoupled") agent.updatePosition = true;
        if (mode == "enableReadCoupledThenDisabled") agent.updatePosition = false;
        var second = agent.nextPosition;
        bool afterSecond = agent.isPathStale;
        remoteLink.activated = true;
        bool staleAfterReopen = agent.isPathStale;
        var third = agent.nextPosition;
        Line(lane.Name, "a", lag, "\"fromLink\":" + B(onLink) + ",\"rejected\":" + B(rejected)
            + ",\"previous\":" + B(previous) + ",\"toggle\":" + B(staleAfterLink)
            + ",\"read\":" + B(afterFirst) + ",\"read2\":" + B(afterSecond)
            + ",\"reopen\":" + B(staleAfterReopen) + ",\"read3\":" + B(agent.isPathStale)
            + ",\"n\":" + V(first) + ",\"n2\":" + V(second) + ",\"n3\":" + V(third));
        DestroyImmediate(agent.gameObject);
        if (parent != null) DestroyImmediate(parent);
        yield return null;
    }

    IEnumerator RecordStaleHierarchy(NavMeshLink link)
    {
        Vector3 at = Lanes.Origin(Lanes.Index("disableagent")) + new Vector3(-8f, 0f, -0.3f);
        foreach (var mode in new[] { "parentNoop", "parentRoundTrip", "sameParent", "reparentIdentity",
                     "reparentWorld", "rectMove", "rectRoundTrip", "rectParentResize", "rectParentRoundTrip" })
        foreach (var lag in new[] { 0, 1, 5 })
        {
            link.activated = true;
            bool rect = mode.StartsWith("rect", StringComparison.Ordinal);
            var parent = rect ? new GameObject("parent", typeof(RectTransform)) : new GameObject("parent");
            var child = rect ? new GameObject("agent", typeof(RectTransform)) : new GameObject("agent");
            if (mode != "reparentIdentity" && mode != "reparentWorld") child.transform.SetParent(parent.transform, true);
            if (rect)
            {
                var box = (RectTransform)child.transform;
                box.anchorMin = box.anchorMax = new Vector2(0.75f, 0.5f);
            }
            child.transform.position = at;
            var agent = child.AddComponent<NavMeshAgent>();
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            for (int i = 0; i < 8; i++) yield return null;
            if (!agent.Warp(agent.nextPosition)) throw new InvalidOperationException("warp refused");
            yield return null;
            Vector3 position = child.transform.position;
            Vector3 translation = Vector3.right * 0.25f;
            switch (mode)
            {
                case "parentNoop":
                    parent.transform.position = parent.transform.position;
                    break;
                case "parentRoundTrip":
                    parent.transform.position += translation;
                    parent.transform.position -= translation;
                    break;
                case "sameParent":
                    child.transform.SetParent(parent.transform, true);
                    break;
                case "reparentWorld":
                    parent.transform.position += translation;
                    child.transform.SetParent(parent.transform, true);
                    break;
                case "reparentIdentity":
                    child.transform.SetParent(parent.transform, true);
                    break;
                case "rectMove":
                case "rectRoundTrip":
                    var box = (RectTransform)child.transform;
                    box.anchoredPosition += new Vector2(0.25f, 0f);
                    if (mode == "rectRoundTrip") box.anchoredPosition -= new Vector2(0.25f, 0f);
                    break;
                case "rectParentResize":
                case "rectParentRoundTrip":
                    var container = (RectTransform)parent.transform;
                    container.sizeDelta += new Vector2(1f, 0f);
                    if (mode == "rectParentRoundTrip") container.sizeDelta -= new Vector2(1f, 0f);
                    break;
            }
            var moved = child.transform.position - position;
            for (int i = 0; i < lag; i++) yield return null;
            link.activated = false;
            bool before = agent.isPathStale;
            var after = agent.nextPosition;
            Line("staleHierarchy." + mode, "a", lag, "\"before\":" + B(before) + ",\"after\":" + B(agent.isPathStale)
                + ",\"transformDelta\":" + V(moved) + ",\"nextDelta\":" + V(after - position));
            DestroyImmediate(parent);
            yield return null;
        }
    }

    IEnumerator RecordStalePathReset(NavMeshLink link, Vector3 local)
    {
        foreach (var mode in new[] { "acceptedMoved", "acceptedStanding", "acceptedHere", "velocityMoved", "standing" })
        {
            link.activated = true;
            var lane = new Lane { Name = "stalePathReset." + mode, O = local };
            var agent = Agent(lane, "a", new Vector3(-8f, 0f, -0.3f), null,
                a => a.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance);
            for (int i = 0; i < 6; i++) yield return null;
            Vector3 here = agent.nextPosition;
            if (!agent.Warp(here)) throw new InvalidOperationException("test warp refused");
            yield return null;
            if (mode.StartsWith("accepted", StringComparison.Ordinal))
            {
                if (mode == "acceptedStanding") agent.speed = 0f;
                Vector3 to = mode == "acceptedHere" ? here : lane.O + new Vector3(-5f, 0f, -0.3f);
                if (!agent.SetDestination(to)) throw new InvalidOperationException("test destination refused");
            }
            for (int i = 0; i < 12; i++)
            {
                if (mode == "velocityMoved") agent.velocity = Vector3.right * 0.5f;
                yield return null;
            }
            bool moved = (agent.transform.position - here).sqrMagnitude > 0.0001f;
            agent.ResetPath();
            link.activated = false;
            bool before = agent.isPathStale;
            _ = agent.nextPosition;
            Line(lane.Name, "a", 0, "\"moved\":" + B(moved) + ",\"before\":" + B(before) + ",\"after\":" + B(agent.isPathStale));
            DestroyImmediate(agent.gameObject);
            yield return null;
        }
    }

    IEnumerator RecordCloneStaleness(NavMeshLink link, Vector3 origin)
    {
        foreach (string mode in new[] { "original", "clone", "cloneSeeded" })
        {
            link.activated = true;
            var root = new GameObject("root");
            var parent = new GameObject("parent");
            parent.transform.SetParent(root.transform, false);
            parent.transform.localPosition = origin + new Vector3(-8f, 0f, -0.3f);
            var live = mode == "original" ? root : Instantiate(root);
            var liveParent = live.transform.GetChild(0);
            if (mode == "cloneSeeded") liveParent.localPosition = liveParent.localPosition;
            var child = new GameObject("child");
            child.transform.SetParent(liveParent, false);
            var agent = child.AddComponent<NavMeshAgent>();
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            for (int i = 0; i < 5; i++) yield return null;
            CheckWarp(agent, agent.nextPosition);
            agent.transform.localScale *= 1.1f;
            for (int i = 0; i < 5; i++) yield return null;
            var position = agent.transform.position;
            link.activated = false;
            bool before = agent.isPathStale;
            liveParent.localPosition = liveParent.localPosition;
            _ = agent.nextPosition;
            Line("staleClone." + mode, "a", 0, "\"before\":" + B(before) + ",\"after\":" + B(agent.isPathStale)
                + ",\"delta\":" + V(agent.transform.position - position));
            if (live != root) DestroyImmediate(live);
            DestroyImmediate(root);
            yield return null;
        }
    }

    IEnumerator RecordAreaStaleness(Lane local, NavMeshLink remoteLink)
    {
        foreach (string mode in new[] { "idle", "idleOnMesh", "idleOwner", "stopped", "pending", "pendingWithPath", "immediate", "arrived", "reset", "idleThenPlan" })
        foreach (string change in new[] { "areaMask", "agentCost", "globalCost" })
        {
            remoteLink.activated = true;
            var lane = new Lane { Name = "staleArea." + mode + "." + change, O = local.O, Link = local.Link };
            var agent = Agent(lane, "a", new Vector3(-8f, 0f, -0.3f), null, a => {
                a.speed = 0f;
                a.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            });
            for (int i = 0; i < 5; i++) yield return null;
            var here = agent.nextPosition;
            if (mode == "stopped" || mode == "pendingWithPath" || mode == "reset")
            {
                SetTestDestination(agent, here + Vector3.right * 2f);
                yield return null;
                if (!agent.hasPath) throw new InvalidOperationException("agent has no test route");
            }
            if (mode == "immediate") SetTestDestination(agent, here + Vector3.right * 0.15f);
            if (mode == "arrived")
            {
                SetTestDestination(agent, here);
                for (int i = 0; i < 5; i++) yield return null;
                if (agent.hasPath || agent.pathPending) throw new InvalidOperationException("agent did not arrive");
            }
            if (mode == "pending" || mode == "pendingWithPath")
            {
                SetTestDestination(agent, lane.O + new Vector3(8f, 0f, -0.3f));
                if (!agent.pathPending) throw new InvalidOperationException("test route is not pending");
            }
            if (mode == "reset") agent.ResetPath();
            float cost = NavMesh.GetAreaCost(4);
            switch (change)
            {
                case "areaMask": agent.areaMask &= ~(1 << 4); break;
                case "agentCost": agent.SetAreaCost(4, cost + 1f); break;
                case "globalCost": NavMesh.SetAreaCost(4, cost + 1f); break;
            }
            bool translated = mode == "idleOnMesh" || mode == "idleOwner";
            if (translated) agent.transform.position += Vector3.right * 0.25f;
            bool afterArea = agent.isPathStale;
            if (mode == "idleOnMesh") _ = agent.isOnNavMesh;
            if (mode == "idleOwner") _ = agent.navMeshOwner;
            Vector3 positionDelta = translated ? agent.destination - here : Vector3.zero;
            if (mode == "idleThenPlan") SetTestDestination(agent, lane.O + new Vector3(8f, 0f, -0.3f));
            bool afterRequest = agent.isPathStale;
            bool hadPath = agent.hasPath;
            bool wasPending = agent.pathPending;
            yield return null;
            bool afterUpdate = agent.isPathStale;
            remoteLink.activated = false;
            Line(lane.Name, "a", 0, "\"area\":" + B(afterArea) + ",\"request\":" + B(afterRequest)
                + ",\"link\":" + B(agent.isPathStale) + ",\"path\":" + B(hadPath) + ",\"pending\":" + B(wasPending)
                + ",\"update\":" + B(afterUpdate) + ",\"positionDelta\":" + V(positionDelta));
            DestroyImmediate(agent.gameObject);
            if (change == "globalCost") NavMesh.SetAreaCost(4, cost);
            yield return null;
        }
    }

    static void SetTestDestination(NavMeshAgent agent, Vector3 goal)
    {
        if (!agent.SetDestination(goal)) throw new InvalidOperationException("test destination refused");
    }

    static void CheckWarp(NavMeshAgent agent, Vector3 position)
    {
        if (!agent.Warp(position)) throw new InvalidOperationException("test warp did not find the NavMesh");
    }

    IEnumerator RecordStaleQuery(Lane local, NavMeshLink remoteLink, string placement, string mode)
    {
        remoteLink.activated = true;
        var lane = new Lane { Name = "staleQueries." + mode + "." + placement, O = local.O };
        var agent = Agent(lane, "a", new Vector3(-8f, 0f, -0.3f), null,
            a => a.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance);
        for (int i = 0; i < 4; i++) yield return null;
        var here = agent.nextPosition;
        CheckWarp(agent, here);
        yield return null;
        var goal = lane.O + new Vector3(-5f, 0f, -0.3f);
        if (placement == "activePath")
        {
            agent.speed = 0f;
            if (!agent.SetDestination(goal)) throw new InvalidOperationException("test destination was rejected");
            yield return null;
            if (!agent.hasPath) throw new InvalidOperationException("test agent has no active path");
        }
        remoteLink.activated = false;
        if (placement == "translate") agent.transform.position += Vector3.right * 0.25f;
        if (placement == "edgeMove") agent.transform.position += Vector3.left * 30f;

        bool before = agent.isPathStale;
        var originalDestination = agent.destination;
        switch (mode)
        {
            case "next": _ = agent.nextPosition; break;
            case "remaining": _ = agent.remainingDistance; break;
            case "calculate": agent.CalculatePath(goal, new NavMeshPath()); break;
            case "edge": agent.FindClosestEdge(out _); break;
            case "raycast": agent.Raycast(goal, out _); break;
            case "sample": agent.SamplePathPosition(NavMesh.AllAreas, 1f, out _); break;
            case "end": _ = agent.pathEndPosition; break;
            case "path": _ = agent.path; break;
            case "transform": _ = agent.transform.position; break;
            case "owner": _ = agent.navMeshOwner; break;
            case "onMesh": _ = agent.isOnNavMesh; break;
            case "onLink": _ = agent.isOnOffMeshLink; break;
            case "destination": _ = agent.destination; break;
            case "desired": _ = agent.desiredVelocity; break;
            case "steering": _ = agent.steeringTarget; break;
            case "velocity": _ = agent.velocity; break;
            case "hasPath": _ = agent.hasPath; break;
            case "pathStatus": _ = agent.pathStatus; break;
            case "failedDestination": agent.SetDestination(here + Vector3.up * 500f); break;
            case "setPositionSame": agent.nextPosition = here; break;
            case "moveZero": agent.Move(Vector3.zero); break;
            case "velocitySet": agent.velocity = Vector3.zero; break;
            case "reset": agent.ResetPath(); break;
            case "detach": agent.updatePosition = false; break;
        }
        Line(lane.Name, "a", 0, "\"before\":" + B(before) + ",\"after\":" + B(agent.isPathStale)
            + ",\"destinationDelta\":" + V(agent.destination - originalDestination));
        DestroyImmediate(agent.gameObject);
        yield return null;
    }
}
