using System;
using UnityEngine;

public partial class LineProbe
{
    void MiterChains()
    {
        var cases = new[]
        {
            ("asymForward", new[] { P(-1, 0), P(0, 0), P(-0.49992385f, 0.00872623f) }, 0.4f, false),
            ("asymBackward", new[] { P(-0.5f, 0), P(0, 0), P(-0.9998477f, 0.01745246f) }, 0.4f, false),
            ("api2Constant", new[] { P(0, 1.2f), P(0.5f, 1.3f), P(0, 1.1f), P(-0.5f, 1.3f), P(-1, 1.2f) }, 0.3f, false),
            ("api2Taper", new[] { P(0, 1.2f), P(0.5f, 1.3f), P(0, 1.1f), P(-0.5f, 1.3f), P(-1, 1.2f) }, 0.3f, true),
            ("api2EmittingConstant", new[] { P(-0.95f, 0.95f), P(-0.96f, 0.95f), P(0.5f, 1), P(0, 0.9f), P(-0.5f, 1), P(-1, 0.95f) }, 0.3f, false),
            ("api2EmittingTaper", new[] { P(-0.95f, 0.95f), P(-0.96f, 0.95f), P(0.5f, 1), P(0, 0.9f), P(-0.5f, 1), P(-1, 0.95f) }, 0.3f, true),
            ("prefixShort", new[] { P(-0.01f, 0), P(0, 0), P(1.46f, 0.05f), P(0.96f, -0.05f), P(0.46f, 0.05f) }, 0.3f, false),
            ("prefixLong", new[] { P(-1, 0), P(0, 0), P(1.46f, 0.05f), P(0.96f, -0.05f), P(0.46f, 0.05f) }, 0.3f, false),
            ("prefixLongReversed", new[] { P(0.46f, 0.05f), P(0.96f, -0.05f), P(1.46f, 0.05f), P(0, 0), P(-1, 0) }, 0.3f, false),
            ("twoReversals", new[] { P(-0.01f, 0), P(0, 0), P(-1, 0.05f), P(0, 0.1f), P(-1, 0.15f) }, 0.3f, false),
            ("lagVsMinimum", new[] { P(-0.01f, 0), P(0, 0), P(-1, 0.05f), P(-0.5f, 0.075f), P(-1.5f, 0.1f) }, 0.3f, false),
            ("shortPrefix90", new[] { P(-0.01f, 0), P(0, 0), P(1, 0), P(1, 1) }, 0.3f, false),
            ("shortPrefix140", new[] { P(-0.01f, 0), P(0, 0), P(1, 0), P(0.23395556f, 0.6427876f) }, 0.3f, false),
            ("shortPrefix10", new[] { P(-0.01f, 0), P(0, 0), P(1, 0), P(1.9848077f, 0.17364818f) }, 0.3f, false),
        };
        foreach (var (name, points, width, taper) in cases)
            foreach (int corners in new[] { 0, 2 })
                foreach (bool trail in new[] { false, true })
                {
                    string key = "miterchain." + name + ".c" + corners + (trail ? ".trail" : ".line");
                    var go = new GameObject(key);
                    var curve = taper ? new AnimationCurve(new Keyframe(0, 1), new Keyframe(1, 0.25f))
                        : AnimationCurve.Constant(0, 1, 1);
                    Renderer renderer;
                    if (trail)
                    {
                        go.transform.position = points[0];
                        var r = go.AddComponent<TrailRenderer>();
                        r.emitting = true;
                        r.time = 10000;
                        r.minVertexDistance = 0;
                        r.widthCurve = curve;
                        r.widthMultiplier = width;
                        r.numCornerVertices = corners;
                        r.generateLightingData = true;
                        r.Clear();
                        var old = new Vector3[points.Length - 1];
                        Array.Copy(points, 1, old, 0, old.Length);
                        Array.Reverse(old);
                        r.AddPositions(old);
                        renderer = r;
                    }
                    else
                    {
                        var r = go.AddComponent<LineRenderer>();
                        r.positionCount = points.Length;
                        r.SetPositions(points);
                        r.widthCurve = curve;
                        r.widthMultiplier = width;
                        r.numCornerVertices = corners;
                        r.generateLightingData = true;
                        renderer = r;
                    }
                    Value(key + ".inputs", "{\"points\":" + Flat(points) + ",\"width\":" + F(width) + ",\"taper\":" + B(taper) + "}");
                    Bake(key + ".mesh", renderer, main, false);
                    DestroyImmediate(go);
                }
    }
}
