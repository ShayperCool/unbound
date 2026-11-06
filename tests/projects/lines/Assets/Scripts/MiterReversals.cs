using System;
using System.Collections.Generic;
using UnityEngine;

public partial class LineProbe
{
    // Synchronous, after all old observations. Trail points are added while emitting,
    // oldest first, with the object at the head. No frame, movement or aging is involved.
    void MiterReversals()
    {
        var cases = new List<(string name, float angle, float incoming, float outgoing, float width, int neighbors)>();
        foreach (float angle in new[] { 170f, 172f, 174f, 176f, 178f, 179f, 179.5f, 179.9f, 180f, 181f })
            cases.Add(("angle" + F(angle), angle, 1f, 1f, 0.4f, 0));
        cases.Add(("narrow", 179f, 1f, 1f, 0.1f, 0));
        cases.Add(("wide", 179f, 1f, 1f, 0.8f, 0));
        cases.Add(("short", 179f, 0.3f, 0.3f, 0.1f, 0));
        cases.Add(("long", 179f, 3f, 3f, 0.4f, 0));
        cases.Add(("asymmetric", 179f, 1f, 0.5f, 0.4f, 0));
        cases.Add(("neighbors", 179f, 1f, 1f, 0.4f, 1));
        cases.Add(("shortNeighbor", 179f, 1f, 1f, 0.4f, 2));
        cases.Add(("perspective", 179f, 1f, 1f, 0.4f, 0));
        cases.Add(("transformZ", 179f, 1f, 1f, 0.4f, 0));
        foreach (var c in cases)
        {
            float a = c.angle * Mathf.Deg2Rad;
            var end = c.angle == 180f ? Vector3.left * c.outgoing : P(Mathf.Cos(a), Mathf.Sin(a)) * c.outgoing;
            var points = new List<Vector3> { P(-c.incoming, 0), Vector3.zero, end };
            if (c.neighbors != 0)
            {
                points.Insert(0, points[0] - P(0.5f, 0.5f));
                points.Add(end + P(0, c.neighbors == 1 ? 1f : 0.1f));
            }
            foreach (int corners in new[] { 0, 2 })
                foreach (bool trail in new[] { false, true })
                {
                    string key = "miterrev." + c.name + ".c" + corners + (trail ? ".trail" : ".line");
                    var go = new GameObject(key);
                    go.layer = ImageLayer;
                    var alignment = c.name == "transformZ" ? LineAlignment.TransformZ : LineAlignment.View;
                    if (alignment == LineAlignment.TransformZ) go.transform.rotation = Quaternion.Euler(25f, 15f, 30f);
                    var camera = c.name == "perspective" ? persp : main;
                    Renderer renderer;
                    Vector3[] readback;
                    if (trail)
                    {
                        go.transform.position = points[0];
                        var r = go.AddComponent<TrailRenderer>();
                        r.emitting = true;
                        r.autodestruct = false;
                        r.time = 10000f;
                        r.minVertexDistance = 0f;
                        r.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
                        r.widthMultiplier = c.width;
                        r.colorGradient = WhiteGradient();
                        r.numCornerVertices = corners;
                        r.numCapVertices = 0;
                        r.alignment = alignment;
                        r.generateLightingData = true;
                        r.sharedMaterial = colorMat;
                        r.Clear();
                        readback = points.GetRange(1, points.Count - 1).ToArray();
                        Array.Reverse(readback);
                        r.AddPositions(readback);
                        readback = new Vector3[r.positionCount];
                        r.GetPositions(readback);
                        renderer = r;
                    }
                    else
                    {
                        var r = go.AddComponent<LineRenderer>();
                        r.useWorldSpace = true;
                        r.positionCount = points.Count;
                        r.SetPositions(points.ToArray());
                        r.widthCurve = AnimationCurve.Constant(0f, 1f, 1f);
                        r.widthMultiplier = c.width;
                        r.colorGradient = WhiteGradient();
                        r.numCornerVertices = corners;
                        r.numCapVertices = 0;
                        r.alignment = alignment;
                        r.generateLightingData = true;
                        r.sharedMaterial = colorMat;
                        readback = new Vector3[r.positionCount];
                        r.GetPositions(readback);
                        renderer = r;
                    }
                    Value(key + ".inputs", "{\"points\":" + Flat(points) + ",\"readback\":" + Flat(readback)
                        + ",\"width\":" + F(c.width) + ",\"corners\":" + corners + ",\"alignment\":" + Q(alignment.ToString()) + "}");
                    Bake(key + ".mesh", renderer, camera, true);
                    if (c.name == "angle179" && corners == 0 && !trail) Image("miterrev.image", imageOrtho);
                    DestroyImmediate(go);
                }
        }
    }
}
