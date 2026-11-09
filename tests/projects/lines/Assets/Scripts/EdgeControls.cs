using System;
using System.Text;
using UnityEngine;

/// <summary>
/// Controls recorded after the build-space controls: TransformZ segments along the frame's z
/// axis (no side in the build plane), gradient keys closer than a 1/65535 step, and the object
/// matrix a drawn local-space line and a moved trail are drawn with.
/// </summary>
public partial class LineProbe
{
    void EdgeControls()
    {
        foreach (int corners in new[] { 0, 2 })
            LineControl("edge.line.zParallel.c" + corners, main, false, l =>
            {
                l.alignment = LineAlignment.TransformZ;
                l.numCornerVertices = corners;
                l.SetPositions(new[] { P(-1, 0, 0), P(0, 0, 0), P(0, 0, 1), P(1, 0.5f, 1) });
            });
        TrailControlZ("edge.trail.zParallel");

        // Keys 0.5 and 0.500005 fall into one 1/65535 step; times between and around them.
        foreach (var mode in new[] { GradientMode.Blend, GradientMode.PerceptualBlend })
        {
            var g = new Gradient { mode = mode };
            g.SetKeys(new[] { new GradientColorKey(Color.red, 0f), new GradientColorKey(Color.green, 0.5f), new GradientColorKey(Color.blue, 0.500005f), new GradientColorKey(Color.white, 1f) },
                new[] { new GradientAlphaKey(1f, 0f), new GradientAlphaKey(0.2f, 0.5f), new GradientAlphaKey(0.9f, 0.500005f), new GradientAlphaKey(0.5f, 1f) });
            var s = new StringBuilder("[");
            float[] times = { 0.4999f, 0.5f, 0.500001f, 0.500002f, 0.500004f, 0.500005f, 0.500006f, 0.50001f, 0.50002f, 0.7f };
            for (int i = 0; i < times.Length; i++) s.Append(i > 0 ? "," : "").Append('[').Append(F(times[i])).Append(',').Append(C(g.Evaluate(times[i]))).Append(']');
            Value("edge.gradient.closeKeys." + mode, s.Append(']').ToString());
        }

        var matrixMat = new Material(Resources.Load<Shader>("LineObjectMatrix"));
        LineCase("objectMatrix.local", "zigzag", l =>
        {
            l.sharedMaterial = matrixMat;
            l.useWorldSpace = false;
            l.transform.SetPositionAndRotation(P(0.25f, -0.2f, 1), Quaternion.Euler(0, 0, 15));
            l.transform.localScale = P(1.2f, 0.8f, 1);
        }, null, false, imageOrtho);
        var go = new GameObject("trail.objectMatrix") { layer = ImageLayer };
        go.transform.SetPositionAndRotation(P(1, 0.5f, 1), Quaternion.Euler(0, 0, 25));
        go.transform.localScale = P(1.5f, 1.5f, 1.5f);
        var t = go.AddComponent<TrailRenderer>();
        t.sharedMaterial = matrixMat;
        t.time = 10000;
        t.minVertexDistance = 0;
        t.widthMultiplier = 0.4f;
        t.Clear();
        t.AddPositions(new[] { P(-1.5f, -0.5f, 1), P(-0.5f, 0.6f, 1), P(0.2f, -0.4f, 1) });
        Image("img.objectMatrix.trail", imageOrtho);
        DestroyImmediate(go);
        DestroyImmediate(matrixMat);
    }

    void TrailControlZ(string key)
    {
        var go = new GameObject(key);
        go.transform.position = P(1, 0.5f, 1);
        var r = go.AddComponent<TrailRenderer>();
        r.alignment = LineAlignment.TransformZ;
        r.emitting = true;
        r.time = 10000;
        r.minVertexDistance = 0;
        r.widthMultiplier = 0.3f;
        r.numCornerVertices = 2;
        r.numCapVertices = 2;
        r.Clear();
        var points = new[] { P(-1, 0, 0), P(0, 0, 0), P(0, 0, 1) };
        r.AddPositions(points);
        Value(key + ".inputs", "{\"head\":" + V3(go.transform.position) + ",\"points\":" + Flat(points) + "}");
        Bake(key + ".mesh", r, main, false);
        DestroyImmediate(go);
    }
}
