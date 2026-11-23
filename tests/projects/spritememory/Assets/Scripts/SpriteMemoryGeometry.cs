using System;
using UnityEngine;
using Object = UnityEngine.Object;

static class SpriteMemoryGeometry
{
    static void SetGeometry(Sprite sprite, int vertices, int triangles)
    {
        var positions = new Vector2[vertices];
        for (int index = 0; index < vertices; index++)
        {
            float angle = index * 2f * Mathf.PI / vertices;
            positions[index] = new Vector2(32f + 30f * Mathf.Cos(angle), 32f + 30f * Mathf.Sin(angle));
        }
        var indices = new ushort[triangles * 3];
        for (int triangle = 0; triangle < triangles; triangle++)
        {
            int local = triangle % (vertices - 2);
            indices[triangle * 3] = 0;
            indices[triangle * 3 + 1] = (ushort)(local + 1);
            indices[triangle * 3 + 2] = (ushort)(local + 2);
        }
        sprite.OverrideGeometry(positions, indices);
    }

    internal static void Capture(Func<Sprite> make, Camera camera, int layer, Action<string, Sprite> read)
    {
        var renderer = new GameObject("Variable sprite geometry").AddComponent<SpriteRenderer>();
        renderer.gameObject.layer = layer;
        var shapes = new (int Vertices, int Triangles)[]
        {
            (3, 0), (3, 1), (4, 1), (4, 2), (4, 3), (5, 1), (8, 6), (12, 10), (32, 30), (129, 127),
        };
        foreach (var shape in shapes)
        {
            string stage = $"geometry.{shape.Vertices}/{shape.Triangles * 3}";
            var sprite = make();
            SetGeometry(sprite, shape.Vertices, shape.Triangles);
            read(stage + ".replaced", sprite);
            var copy = Object.Instantiate(sprite);
            read(stage + ".sourceCloned", sprite);
            read(stage + ".copy", copy);
            renderer.sprite = sprite;
            camera.Render();
            read(stage + ".drawn", sprite);
            renderer.sprite = null;
            SetGeometry(sprite, 4, 2);
            read(stage + ".quadReplacement", sprite);
            renderer.sprite = sprite;
            camera.Render();
            read(stage + ".quadDrawn", sprite);
            renderer.sprite = null;
            Object.DestroyImmediate(copy);
            Object.DestroyImmediate(sprite);
        }

        var growing = make();
        renderer.sprite = growing;
        foreach (int triangles in new[] { 10, 1, 11, 3, 20 })
        {
            string stage = $"geometry.reused.{triangles * 3}";
            SetGeometry(growing, 5, triangles);
            read(stage + ".replaced", growing);
            camera.Render();
            read(stage + ".drawn", growing);
        }
        Object.DestroyImmediate(renderer.gameObject);
        Object.DestroyImmediate(growing);
    }
}
