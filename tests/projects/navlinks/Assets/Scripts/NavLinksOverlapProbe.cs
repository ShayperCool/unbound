using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

public partial class NavLinksProbe
{
    IEnumerator RecordOverlapControls()
    {
        var settings = NavMesh.GetSettingsByID(0);
        var mesh = NavMeshBuilder.BuildNavMeshData(settings, new List<NavMeshBuildSource>
        {
            new NavMeshBuildSource
            {
                shape = NavMeshBuildSourceShape.Box,
                size = new Vector3(12f, 1f, 12f),
                transform = Matrix4x4.Translate(Vector3.down * 0.5f),
            },
        }, new Bounds(Vector3.zero, new Vector3(14f, 4f, 14f)), Vector3.zero, Quaternion.identity);
        var origin = new Vector3(-20f, 0f, -60f);
        var cases = new[]
        {
            (name: "flat", gap: 0f, sideways: 0f, aRadius: 0.5f, bRadius: 0.5f, aPriority: 50, bPriority: 50),
            (name: "near", gap: 0.25f, sideways: 0f, aRadius: 0.5f, bRadius: 0.5f, aPriority: 50, bPriority: 50),
            (name: "diagonal", gap: 0.45f, sideways: 0.2f, aRadius: 0.5f, bRadius: 0.5f, aPriority: 50, bPriority: 50),
            (name: "above", gap: 0.8f, sideways: 0f, aRadius: 0.5f, bRadius: 0.5f, aPriority: 50, bPriority: 50),
            (name: "priority", gap: 0.25f, sideways: 0.15f, aRadius: 0.65f, bRadius: 0.45f, aPriority: 40, bPriority: 60),
        };
        foreach (var c in cases)
        {
            var lower = NavMesh.AddNavMeshData(mesh, origin, Quaternion.identity);
            var upper = c.gap > 0f ? NavMesh.AddNavMeshData(mesh, origin + Vector3.up * c.gap, Quaternion.identity) : default;
            yield return null;
            var lane = new Lane { Name = "overlap." + c.name, O = origin };
            var a = Agent(lane, "a", new Vector3(-0.4f, 0.05f, 0f), null, Configure);
            var b = Agent(lane, "b", new Vector3(0.4f, c.gap + 0.05f, c.sideways), null, Configure);
            a.radius = c.aRadius;
            b.radius = c.bRadius;
            a.avoidancePriority = c.aPriority;
            b.avoidancePriority = c.bPriority;
            if (!a.isOnNavMesh || !b.isOnNavMesh) throw new InvalidOperationException("overlap agents were not placed");
            var startA = a.nextPosition;
            var startB = b.nextPosition;
            if (Mathf.Abs(startB.y - startA.y - c.gap) > 0.02f)
                throw new InvalidOperationException("overlap agents were not placed on their separate surfaces");
            for (int f = 0; f < 4; f++)
            {
                Line(lane.Name, "a", f, "\"a\":" + V(a.nextPosition - origin) + ",\"b\":" + V(b.nextPosition - origin)
                    + ",\"dA\":" + V(a.nextPosition - startA) + ",\"dB\":" + V(b.nextPosition - startB));
                yield return null;
            }
            DestroyImmediate(a.gameObject);
            DestroyImmediate(b.gameObject);
            NavMesh.RemoveNavMeshData(lower);
            if (upper.valid) NavMesh.RemoveNavMeshData(upper);
            yield return null;
        }
        DestroyImmediate(mesh);
        Finish(0);
    }

    static void Configure(NavMeshAgent agent)
    {
        agent.speed = 0f;
        agent.obstacleAvoidanceType = ObstacleAvoidanceType.LowQualityObstacleAvoidance;
    }
}
