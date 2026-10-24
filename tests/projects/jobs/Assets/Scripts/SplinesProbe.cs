using Unity.Collections;
using Unity.Mathematics;
using UnityEngine.Splines;

/// <summary>com.unity.splines' jobs: a spline evaluated in parallel jobs (NativeSpline, SplineJobs).</summary>
public static class SplinesProbe
{
    public static void RunAll()
    {
        ProbeLog.Begin("splines.jobs");
        var spline = new Spline();
        spline.Add(new BezierKnot(new float3(0, 0, 0), new float3(0, 0, -1), new float3(0, 0, 1)), TangentMode.Broken);
        spline.Add(new BezierKnot(new float3(4, 2, 3), new float3(-1, 0, 0), new float3(1, 0, 0)), TangentMode.Broken);
        spline.Add(new BezierKnot(new float3(8, 0, 0), new float3(0, 1, 0), new float3(0, -1, 0)), TangentMode.Broken);
        ProbeLog.Value("length", spline.GetLength());

        var positions = new NativeArray<float3>(33, Allocator.TempJob);
        SplineJobs.EvaluatePosition(spline, positions);
        for (int i = 0; i < positions.Length; i += 8)
        {
            ProbeLog.Value("position" + i + ".x", positions[i].x);
            ProbeLog.Value("position" + i + ".y", positions[i].y);
            ProbeLog.Value("position" + i + ".z", positions[i].z);
        }

        using (var native = new NativeSpline(spline, float4x4.Translate(new float3(1, 0, 0)), Allocator.TempJob))
        {
            var tangents = new NativeArray<float3>(positions.Length, Allocator.TempJob);
            var normals = new NativeArray<float3>(positions.Length, Allocator.TempJob);
            SplineJobs.EvaluatePositionTangentNormal(native, positions, tangents, normals);
            ProbeLog.Value("native.position16.x", positions[16].x);
            ProbeLog.Value("native.tangent16.y", tangents[16].y);
            ProbeLog.Value("native.normal16.z", normals[16].z);
            ProbeLog.Value("native.length", native.GetLength());
            tangents.Dispose();
            normals.Dispose();
        }
        positions.Dispose();

        ProbeLog.Value("nearest", SplineUtility.GetNearestPoint(spline, new float3(4, 0, 0), out var nearest, out float t));
        ProbeLog.Value("nearest.t", t);
        ProbeLog.Value("nearest.x", nearest.x);
    }
}
