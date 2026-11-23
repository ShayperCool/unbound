using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public partial class NavLinksProbe
{
    static readonly Vector3 s_StudyOrigin = new Vector3(2000, 0, 2000);

    static NavMeshBuildSource StudyFloor(Vector3 center, float width)
    {
        return new NavMeshBuildSource
        {
            shape = NavMeshBuildSourceShape.Box,
            size = new Vector3(width, 1, 12),
            transform = Matrix4x4.Translate(s_StudyOrigin + center + Vector3.down * 0.5f),
        };
    }

    static NavMeshData StudyMesh(bool separate)
    {
        var settings = NavMesh.GetSettingsByID(0);
        settings.overrideVoxelSize = true;
        settings.voxelSize = 0.25f;
        settings.overrideTileSize = true;
        settings.tileSize = 32;
        var sources = separate
            ? new List<NavMeshBuildSource> { StudyFloor(new Vector3(0, 0, 112), 24) }
            : new List<NavMeshBuildSource> {
                StudyFloor(new Vector3(-7, 0, 0), 10), StudyFloor(new Vector3(7, 0, 0), 10),
                StudyFloor(new Vector3(0, 0, 56), 24),
            };
        var bounds = separate
            ? new Bounds(s_StudyOrigin + Vector3.forward * 112, new Vector3(32, 6, 16))
            : new Bounds(s_StudyOrigin + Vector3.forward * 28, new Vector3(32, 6, 72));
        return NavMeshBuilder.BuildNavMeshData(settings, sources, bounds, Vector3.zero, Quaternion.identity);
    }

    static NavMeshLinkInstance StudyLink(bool unattached)
    {
        float height = unattached ? 64f : 0f;
        return NavMesh.AddLink(new NavMeshLinkData
        {
            startPosition = new Vector3(-3.25f, height, 0),
            endPosition = new Vector3(3.25f, height, 0),
            bidirectional = true,
            area = 0,
            agentTypeID = 0,
            costModifier = -1,
        }, s_StudyOrigin, Quaternion.identity);
    }

    IEnumerator RecordLinkVersionStudy()
    {
        var study = LinkVersionStudySteps();
        while (true)
        {
            bool advance;
            try { advance = study.MoveNext(); }
            catch (Exception error)
            {
                Debug.LogException(error);
                Line("linkVersion", "error", -1, "\"error\":" + Text(error.Message));
                Finish(1);
                advance = false;
            }
            if (!advance) yield break;
            yield return study.Current;
        }
    }

    IEnumerator LinkVersionStudySteps()
    {
        var primaryData = StudyMesh(false);
        var separateData = StudyMesh(true);
        if (primaryData == null || separateData == null)
            throw new InvalidOperationException("test mesh build failed");
        var primary = NavMesh.AddNavMeshData(primaryData);
        var separate = NavMesh.AddNavMeshData(separateData);
        if (!primary.valid || !separate.valid) throw new InvalidOperationException("test mesh registration failed");
        NavMesh.pathfindingIterationsPerFrame = 100000;
        yield return null;

        var routeCases = new (string Name, Vector3 Start, Vector3 Goal)[]
        {
            ("sameTile", new Vector3(-4, 0, -2), new Vector3(-3, 0, -2)),
            ("traversedTile", new Vector3(-10, 0, 2), new Vector3(-3, 0, 2)),
            ("goalTile", new Vector3(10, 0, 0), new Vector3(3, 0, 0)),
            ("remoteTile", new Vector3(-6, 0, 56), new Vector3(6, 0, 56)),
            ("separateData", new Vector3(-6, 0, 112), new Vector3(6, 0, 112)),
        };
        var lane = new Lane { Name = "linkVersion", O = s_StudyOrigin };
        var tracked = new List<(string Name, NavMeshAgent Agent, Vector3 Goal)>();
        foreach (var route in routeCases)
        {
            var agent = Agent(lane, route.Name, route.Start, null, a =>
            {
                a.speed = 0;
                a.autoRepath = false;
                a.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            });
            tracked.Add((route.Name, agent, lane.O + route.Goal));
        }
        var link = new NavMeshLinkInstance();
        foreach (string operation in new[] { "none", "add", "deactivate", "deactivateAgain", "reactivate", "remove",
                     "removeInvalid", "unattachedAdd", "unattachedRemove" })
        {
            foreach (var item in tracked)
                if (!item.Agent.SetDestination(item.Goal)) throw new InvalidOperationException("test destination refused");
            for (int i = 0; i < 6; i++) yield return null;
            foreach (var item in tracked)
                if (item.Agent.pathPending || !item.Agent.hasPath || item.Agent.pathStatus != NavMeshPathStatus.PathComplete)
                    throw new InvalidOperationException("test path was not complete: " + item.Name);
            foreach (var item in tracked) StudyReport(operation, item.Name, -1, item.Agent);
            switch (operation)
            {
                case "add": link = StudyLink(false); break;
                case "deactivate":
                case "deactivateAgain": NavMesh.SetLinkActive(link, false); break;
                case "reactivate": NavMesh.SetLinkActive(link, true); break;
                case "remove":
                case "removeInvalid": NavMesh.RemoveLink(link); break;
                case "unattachedAdd": link = StudyLink(true); break;
                case "unattachedRemove": NavMesh.RemoveLink(link); break;
            }
            var path = new NavMeshPath();
            bool got = NavMesh.CalculatePath(lane.O + Vector3.left * 6, lane.O + Vector3.right * 6, NavMesh.AllAreas, path);
            Line("linkVersion." + operation, "query", 0, "\"valid\":" + B(NavMesh.IsLinkValid(link))
                + ",\"cross\":" + B(got && path.status == NavMeshPathStatus.PathComplete));
            for (int frame = 0; frame < 4; frame++)
            {
                foreach (var item in tracked) StudyReport(operation, item.Name, frame, item.Agent);
                yield return null;
            }
        }
        foreach (var item in tracked) DestroyImmediate(item.Agent.gameObject);

        foreach (string mode in new[] { "near", "far", "stopped", "disabled", "arrived", "unattached", "completeArrived" })
        {
            var start = new Vector3(mode == "near" || mode == "stopped" || mode == "disabled" ? -3.25f : -8f, 0, 0);
            bool complete = mode == "completeArrived";
            bool arrived = complete || mode == "arrived";
            var goal = new Vector3(complete ? -5 : 7, 0, 0);
            var agent = Agent(lane, mode, start, goal, a =>
            {
                a.speed = 2;
                a.acceleration = 20;
                a.autoBraking = false;
                a.autoTraverseOffMeshLink = false;
                a.autoRepath = mode != "disabled";
                a.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            });
            for (int i = 0; i < 5; i++) yield return null;
            if (mode == "stopped") agent.isStopped = true;
            if (arrived)
                for (int i = 0; i < 240 && agent.hasPath; i++) yield return null;
            var status = complete ? NavMeshPathStatus.PathComplete : NavMeshPathStatus.PathPartial;
            if (agent.pathPending || agent.pathStatus != status || agent.hasPath == arrived)
                throw new InvalidOperationException("test agent did not reach initial partial path state: " + mode);
            StudyReport("partial", mode, -1, agent);
            link = StudyLink(mode == "unattached");
            for (int frame = 0; frame < 190; frame++)
            {
                StudyReport("partial", mode, frame, agent);
                yield return null;
            }
            NavMesh.RemoveLink(link);
            DestroyImmediate(agent.gameObject);
            yield return null;
        }
        NavMesh.RemoveNavMeshData(separate);
        NavMesh.RemoveNavMeshData(primary);
        DestroyImmediate(primaryData);
        DestroyImmediate(separateData);
        Finish(0);
    }

    void StudyReport(string kind, string agentName, int age, NavMeshAgent agent)
    {
        Line("linkVersion." + kind, agentName, age, "\"stale\":" + B(agent.isPathStale)
            + ",\"pending\":" + B(agent.pathPending) + ",\"hasPath\":" + B(agent.hasPath)
            + ",\"status\":" + Text(agent.pathStatus.ToString())
            + ",\"next\":" + V(agent.nextPosition - s_StudyOrigin)
            + ",\"end\":" + V(agent.pathEndPosition - s_StudyOrigin));
    }
}
