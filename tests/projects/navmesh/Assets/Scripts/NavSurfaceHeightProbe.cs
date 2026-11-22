using System;
using System.Collections;
using UnityEngine;
using UnityEngine.AI;

public partial class NavProbe
{
    IEnumerator MeasureSurfaceHeights()
    {
        (string Name, Vector3 Point)[] cases =
        {
            ("flat", new Vector3(-6f, 0.08f, -3f)),
            ("climberFirst", new Vector3(2.47456217f, 0.08f, -6f)),
            ("climberSecond", new Vector3(2.4253056f, 0.08f, -6f)),
            ("climberSlopeBefore", new Vector3(-0.652989745f, 0.28f, -6f)),
            ("climberSlope", new Vector3(-0.7016004f, 0.28f, -6f)),
            ("climberSlopeAfter", new Vector3(-0.798849344f, 0.34f, -6f)),
            ("climberUpper", new Vector3(-0.8474879f, 0.34f, -6f)),
            ("walkerSlopeBefore", new Vector3(-0.24529317f, 0.13f, -6.16679335f)),
            ("walkerSlope", new Vector3(-0.190662742f, 0.13f, -6.12304258f)),
            ("oblique", new Vector3(-0.44f, 0.21f, -6.0988f)),
            ("nearDetailSeam", new Vector3(-1.42510319f, 0.22f, -7.09152985f)),
            ("pastDetailSeam", new Vector3(-1.42522967f, 0.22f, -7.09136295f)),
        };
        const float StartOffset = 0.03125f;
        foreach (var (name, target) in cases)
        {
            var go = new GameObject(name);
            go.transform.position = target + Vector3.right * StartOffset;
            var agent = go.AddComponent<NavMeshAgent>();
            agent.updatePosition = false;
            agent.obstacleAvoidanceType = ObstacleAvoidanceType.NoObstacleAvoidance;
            yield return null;
            if (!agent.isOnNavMesh) throw new InvalidOperationException("expected a placed agent");
            Vector3 before = agent.nextPosition;
            Vector3 horizontalMove = target - before;
            horizontalMove.y = 0f;
            agent.Move(horizontalMove);
            Vector3 after = agent.nextPosition;
            if (!NavMesh.SamplePosition(target, out var hit, 0.5f, NavMesh.AllAreas))
                throw new InvalidOperationException("sample did not find the surface");
            ProbeLog.Begin("height." + name);
            ProbeLog.Value("previous", before);
            ProbeLog.Value("position", after);
            ProbeLog.Value("moveHeightBits", BitConverter.SingleToInt32Bits(after.y).ToString("x8"));
            ProbeLog.Value("sample", hit.position);
            ProbeLog.Value("sampleHeightBits", BitConverter.SingleToInt32Bits(hit.position.y).ToString("x8"));
            DestroyImmediate(go);
        }

        ProbeLog.Begin("height.complete");
        ProbeLog.Value("ok", true);
        Finish();
    }
}
