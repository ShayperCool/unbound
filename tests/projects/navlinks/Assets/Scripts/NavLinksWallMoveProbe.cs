using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public partial class NavLinksProbe
{
    IEnumerator RecordWallMoveControls()
    {
        var goals = new[] {
            (key: "inside", point: new Vector3(-1.1523132f, 0f, 3.5160245f)),
            (key: "first", point: new Vector3(-1.1523132f, 0f, 3.5110245f)),
            (key: "second", point: new Vector3(-1.130951f, 0f, 3.5317383f)),
            (key: "third", point: new Vector3(-1.1099243f, 0f, 3.5527878f)),
            (key: "fourth", point: new Vector3(-1.0888977f, 0f, 3.573822f)),
            (key: "fifth", point: new Vector3(-1.02304f, 0f, 3.6342087f)),
            (key: "vertex", point: new Vector3(-1.0f, 0f, 3.6666718f)),
            (key: "past", point: new Vector3(-0.965f, 0f, 3.72f)),
        };
        yield return null;
        foreach (var sample in new[] { (name: "original", lane: "areamask", sign: 1f),
                                      (name: "translated", lane: "costhigh", sign: 1f),
                                      (name: "opposite", lane: "costhigh", sign: -1f) })
        foreach (var entry in goals)
        {
            var origin = Lanes.Origin(Lanes.Index(sample.lane));
            var lane = new Lane { Name = "wallMove." + sample.name + "." + entry.key, O = origin };
            var agent = Agent(lane, "a", new Vector3(-1.1963501f * sample.sign, 0f, 3.472374f), null, a => {
                a.updatePosition = false;
                a.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            });
            for (int i = 0; i < 5; i++) yield return null;
            if (!agent.isOnNavMesh) throw new InvalidOperationException("agent not on the NavMesh");
            var before = agent.nextPosition;
            var local = entry.point;
            local.x *= sample.sign;
            var point = origin + local;
            point.y = before.y;
            agent.Move(point - before);
            var after = agent.nextPosition;
            Line(lane.Name, "a", 0, "\"before\":" + V(before) + ",\"target\":" + V(point)
                + ",\"after\":" + V(after) + ",\"positionBits\":\"" + PreciseWallPosition(after) + "\"");
            DestroyImmediate(agent.gameObject);
            yield return null;
        }
        Finish(0);
    }

    static string PreciseWallPosition(Vector3 p) => BitConverter.SingleToInt32Bits(p.x).ToString("x8")
        + "," + BitConverter.SingleToInt32Bits(p.z).ToString("x8");
}
