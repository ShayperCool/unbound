using System.Collections.Generic;
using UnityEngine;
using UnityEngine.U2D;
using static ProbeEntry;

/// <summary>Sprite shape scenarios: the geometry com.unity.2d.spriteshape generates into the
/// SpriteShapeRenderer's channels for the scene's shapes (a filled closed shape with a smooth
/// point and a varying height, an open edge, a tinted triangle in front), the renderer's state
/// and bounds, the colliders the controllers fill in, and how the shapes render.</summary>
public static class ShapeProbe
{
    static readonly string[] k_Shapes = { "Hill", "Path", "Tinted" };

    public static void RunAll()
    {
        Scenario("spriteshape.renderer", RendererState);
        Scenario("spriteshape.geometry", Geometry);
        Scenario("spriteshape.collider", Colliders);
        Scenario("spriteshape.render", Render);
    }

    static SpriteShapeController Shape(string name) => GameObject.Find(name).GetComponent<SpriteShapeController>();

    static void Value(string key, Bounds b)
    {
        ProbeLog.Value(key + ".center", b.center);
        ProbeLog.Value(key + ".size", b.size);
    }

    static void RendererState()
    {
        foreach (var name in k_Shapes)
        {
            var renderer = Shape(name).spriteShapeRenderer;
            var materials = renderer.sharedMaterials;
            ProbeLog.Value(name + ".materials", materials.Length);
            for (int i = 0; i < materials.Length; i++)
                ProbeLog.Value(name + ".material" + i, materials[i] != null ? materials[i].shader.name : "null");
            var c = renderer.color;
            ProbeLog.Value(name + ".color", c.r + " " + c.g + " " + c.b + " " + c.a);
            ProbeLog.Value(name + ".maskInteraction", (int)renderer.maskInteraction);
            ProbeLog.Value(name + ".sortingOrder", renderer.sortingOrder);
        }
    }

    static void Geometry()
    {
        foreach (var name in k_Shapes)
        {
            var controller = Shape(name);
            var recorder = ScriptableObject.CreateInstance<GeometryRecorder>();
            controller.modifiers.Add(recorder);
            controller.BakeMesh().Complete();
            var renderer = controller.spriteShapeRenderer;
            ProbeLog.Value(name + ".meshCount", renderer.GetSplineMeshCount());
            Value(name + ".localBounds", renderer.localBounds);
            Value(name + ".bounds", renderer.bounds);
            if (!recorder.segments.IsCreated)
            {
                ProbeLog.Value(name + ".baked", false);
                continue;
            }
            int vertexStart = 0, indexStart = 0;
            for (int s = 0; s < recorder.segments.Length; s++)
            {
                var segment = recorder.segments[s];
                if (segment.indexCount == 0 && segment.vertexCount == 0) continue;
                string key = name + ".segment" + s;
                ProbeLog.Value(key, segment.geomIndex + " " + segment.indexCount + " " + segment.vertexCount + " " + segment.spriteIndex);
                for (int v = 0; v < segment.vertexCount; v++)
                {
                    ProbeLog.Value(key + ".v" + v, recorder.positions[vertexStart + v]);
                    ProbeLog.Value(key + ".uv" + v, recorder.texCoords[vertexStart + v]);
                }
                var triangles = new System.Text.StringBuilder();
                for (int i = 0; i < segment.indexCount; i++) triangles.Append(recorder.indices[indexStart + i]).Append(' ');
                ProbeLog.Value(key + ".indices", triangles.ToString().TrimEnd());
                vertexStart += segment.vertexCount;
                indexStart += segment.indexCount;
            }
            controller.modifiers.Remove(recorder);
            controller.BakeMesh().Complete();
            Object.DestroyImmediate(recorder);
        }
    }

    static void Colliders()
    {
        foreach (var name in k_Shapes)
        {
            var controller = Shape(name);
            controller.BakeMesh().Complete();
            controller.BakeCollider();
            var polygon = controller.GetComponent<PolygonCollider2D>();
            if (polygon != null)
            {
                ProbeLog.Value(name + ".polygon.paths", polygon.pathCount);
                var points = polygon.GetPath(0);
                ProbeLog.Value(name + ".polygon.count", points.Length);
                for (int i = 0; i < points.Length; i++) ProbeLog.Value(name + ".polygon." + i, points[i]);
            }
            var edge = controller.GetComponent<EdgeCollider2D>();
            if (edge != null)
            {
                var points = edge.points;
                ProbeLog.Value(name + ".edge.count", points.Length);
                for (int i = 0; i < points.Length; i++) ProbeLog.Value(name + ".edge." + i, points[i]);
            }
        }
    }

    static Camera s_Camera;

    public static Camera ProbeCamera(Vector3 position, float size)
    {
        if (s_Camera == null)
        {
            var go = new GameObject("ShapeCamera");
            s_Camera = go.AddComponent<Camera>();
            s_Camera.enabled = false;
            s_Camera.orthographic = true;
            s_Camera.clearFlags = CameraClearFlags.SolidColor;
            s_Camera.backgroundColor = new Color(0.1f, 0.1f, 0.15f, 1f);
        }
        s_Camera.transform.position = position;
        s_Camera.orthographicSize = size;
        return s_Camera;
    }

    static void Render()
    {
        var camera = ProbeCamera(new Vector3(40f, -0.5f, -10f), 4f);
        TilemapProbe.Image("shapes", camera, 160, 160);
        foreach (var name in k_Shapes) ProbeLog.Value(name + ".meshCount", Shape(name).spriteShapeRenderer.GetSplineMeshCount());
        var tinted = Shape("Tinted");
        tinted.spriteShapeRenderer.color = new Color(0.3f, 1f, 0.3f, 0.6f);
        tinted.spline.SetPosition(1, new Vector3(2.3f, 2.2f, 0f));
        var path = Shape("Path");
        path.spline.SetHeight(1, 1.6f);
        TilemapProbe.Image("changed", camera, 160, 160);
    }
}
