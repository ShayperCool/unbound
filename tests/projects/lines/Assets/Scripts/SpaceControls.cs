using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

/// <summary>
/// Controls recorded after the reversal chains: where a strip is built (TransformZ frames of
/// trails away from the origin and of scaled lines, BakeMesh's useTransform on view-aligned lines,
/// an orthographic camera's near distance, points behind the camera) and the Simplify rules.
/// </summary>
public partial class LineProbe
{
    static readonly Vector3[] TrailPath = { P(-1, 0), P(0, 0.6f), P(0.8f, 0), P(1.4f, 0.7f) };
    static readonly Vector3[] SpatialPath = { P(-1, 0, -2), P(0, 0.5f, 3), P(1, -0.5f, 0), P(1.5f, 0.6f, 1) };

    void SpaceControls()
    {
        // TransformZ trails: the head at z 30, unturned and turned half round about y, baked with
        // and without the transform.
        TrailControl("space.trail.far", P(0, 0, 30), Quaternion.identity, false);
        TrailControl("space.trail.farTurned.u0", P(0, 0, 30), Quaternion.Euler(0, 180, 0), false);
        TrailControl("space.trail.farTurned.u1", P(0, 0, 30), Quaternion.Euler(0, 180, 0), true);

        // TransformZ lines on a moved, turned and unevenly scaled transform.
        foreach (bool world in new[] { false, true })
            foreach (bool useTransform in new[] { false, true })
                LineControl("space.line.scaledZ." + (world ? "world" : "local") + ".u" + (useTransform ? 1 : 0), main, useTransform, l =>
                {
                    l.alignment = LineAlignment.TransformZ;
                    l.useWorldSpace = world;
                    l.transform.SetPositionAndRotation(P(1, 2, 3), Quaternion.Euler(0, 0, 30));
                    l.transform.localScale = P(2, 0.5f, 1);
                    l.SetPositions(TrailPath);
                });

        // A view-aligned world-space line on a turned, scaled transform: does useTransform change it?
        foreach (bool useTransform in new[] { false, true })
            LineControl("space.line.viewTransform.u" + (useTransform ? 1 : 0), main, useTransform, l =>
            {
                l.transform.SetPositionAndRotation(P(0.5f, -0.5f, 1), Quaternion.Euler(20, 40, 10));
                l.transform.localScale = P(2, 0.5f, 1.5f);
                l.SetPositions(SpatialPath);
            });

        // A spatial line from orthographic cameras whose near planes differ.
        foreach (float near in new[] { 0.3f, 5f })
        {
            var cam = NewCamera("SpaceOrtho", true, P(0, 0, -10), Vector3.zero);
            cam.enabled = false;
            cam.nearClipPlane = near;
            LineControl("space.line.orthoNear" + F(near), cam, false, l => l.SetPositions(SpatialPath));
            DestroyImmediate(cam.gameObject);
        }

        // A line passing behind a perspective camera at z -10.
        var behind = NewCamera("SpaceBehind", false, P(0, 0, -10), Vector3.zero);
        behind.enabled = false;
        LineControl("space.line.behind", behind, false, l =>
            l.SetPositions(new[] { P(-1, 0, -12), P(0, 0.5f, -11), P(1, 0, -5), P(2, 1, -4) }));
        DestroyImmediate(behind.gameObject);

        SimplifyControls();
    }

    void TrailControl(string key, Vector3 head, Quaternion rotation, bool useTransform)
    {
        var go = new GameObject(key);
        go.transform.SetPositionAndRotation(head + TrailPath[TrailPath.Length - 1], rotation);
        var r = go.AddComponent<TrailRenderer>();
        r.alignment = LineAlignment.TransformZ;
        r.emitting = true;
        r.time = 10000;
        r.minVertexDistance = 0;
        r.widthMultiplier = 0.3f;
        r.numCornerVertices = 2;
        r.numCapVertices = 2;
        r.generateLightingData = true;
        r.Clear();
        var points = new Vector3[TrailPath.Length - 1];
        for (int i = 0; i < points.Length; i++) points[i] = head + TrailPath[i];
        r.AddPositions(points);
        Value(key + ".inputs", "{\"head\":" + V3(go.transform.position) + ",\"points\":" + Flat(points) + ",\"rotation\":" + V3(rotation.eulerAngles) + ",\"useTransform\":" + B(useTransform) + "}");
        Bake(key + ".mesh", r, main, useTransform);
        DestroyImmediate(go);
    }

    void LineControl(string key, Camera cam, bool useTransform, Action<LineRenderer> configure)
    {
        var go = new GameObject(key);
        var l = go.AddComponent<LineRenderer>();
        l.positionCount = 4;
        l.widthMultiplier = 0.3f;
        l.numCornerVertices = 2;
        l.numCapVertices = 2;
        l.generateLightingData = true;
        configure(l);
        var points = new Vector3[l.positionCount];
        l.GetPositions(points);
        var t = go.transform;
        Value(key + ".inputs", "{\"points\":" + Flat(points) + ",\"position\":" + V3(t.position) + ",\"rotation\":" + V3(t.eulerAngles)
            + ",\"scale\":" + V3(t.localScale) + ",\"world\":" + B(l.useWorldSpace) + ",\"alignment\":" + Q(l.alignment.ToString())
            + ",\"camera\":{\"orthographic\":" + B(cam.orthographic) + ",\"near\":" + F(cam.nearClipPlane) + ",\"position\":" + V3(cam.transform.position) + "},\"useTransform\":" + B(useTransform) + "}");
        Bake(key + ".mesh", l, cam, useTransform);
        DestroyImmediate(go);
    }

    // ---- Simplify ----

    void SimplifyControls()
    {
        // A point beyond the end of its span: near the line through the ends, far from the segment.
        SimplifyLine("simplify.beyond", new[] { P(0, 0), P(2, 0.05f), P(-1, 0.02f), P(1, 0) }, 0.1f);
        SimplifyLine("simplify.zigzag", new[] { P(0, 0), P(0.5f, 0.04f), P(1, -0.03f), P(1.5f, 0.6f), P(2, 0.02f), P(2.5f, -0.05f), P(3, 0) }, 0.1f);
        // A closed polyline: its last point is its first.
        SimplifyLine("simplify.closed", new[] { P(0, 0), P(1, 0), P(1, 1), P(0, 1), P(0, 0) }, 0.1f);
        // A point exactly at the tolerance.
        SimplifyLine("simplify.tie", new[] { P(0, 0), P(1, 0.5f), P(2, 0) }, 0.5f);
        SimplifyLine("simplify.two", new[] { P(0, 0), P(1, 1) }, 10f);
        SimplifyLine("simplify.one", new[] { P(0, 0) }, 0.1f);

        var points = new List<Vector3> { P(0, 0), P(2, 0.05f), P(-1, 0.02f), P(1, 0), P(1.5f, 0.6f), P(3, 0) };
        var indices = new List<int> { 99 };
        Simplified("simplify.utility.indices", () => { LineUtility.Simplify(points, 0.1f, indices); return Ints(indices); });
        var kept = new List<Vector3> { P(9, 9, 9) };
        Simplified("simplify.utility.points", () => { LineUtility.Simplify(points, 0.1f, kept); return Flat(kept); });
        var flat = new List<Vector2>();
        foreach (var p in points) flat.Add(p);
        var indices2 = new List<int> { 99 };
        Simplified("simplify.utility.indices2", () => { LineUtility.Simplify(flat, 0.1f, indices2); return Ints(indices2); });
        var kept2 = new List<Vector2> { new Vector2(9, 9) };
        Simplified("simplify.utility.points2", () =>
        {
            LineUtility.Simplify(flat, 0.1f, kept2);
            var s = new StringBuilder("[");
            for (int i = 0; i < kept2.Count; i++) s.Append(i > 0 ? "," : "").Append(V2(kept2[i]));
            return s.Append(']').ToString();
        });
    }

    void SimplifyLine(string key, Vector3[] points, float tolerance)
    {
        var go = new GameObject(key);
        var l = go.AddComponent<LineRenderer>();
        l.positionCount = points.Length;
        l.SetPositions(points);
        Value(key + ".inputs", "{\"points\":" + Flat(points) + ",\"tolerance\":" + F(tolerance) + "}");
        Simplified(key + ".positions", () =>
        {
            l.Simplify(tolerance);
            var after = new Vector3[l.positionCount];
            l.GetPositions(after);
            return Flat(after);
        });
        DestroyImmediate(go);
    }

    void Simplified(string key, Func<string> run)
    {
        BeginLogs();
        try { Value(key, run()); }
        catch (Exception e) { Value(key + ".error", Q(e.GetType().Name)); }
        EndLogs(key);
    }

    static string Ints(List<int> values)
    {
        var s = new StringBuilder("[");
        for (int i = 0; i < values.Count; i++) s.Append(i > 0 ? "," : "").Append(values[i]);
        return s.Append(']').ToString();
    }
}
