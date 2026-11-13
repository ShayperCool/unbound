using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>Records, in Play mode, runtime Sprite.Create Tight geometry and fallback physics shapes,
/// the paths a PolygonCollider2D added to a sprite's object takes, a TilemapCollider2D's shapes for
/// sprite tiles, and SpriteUtility.GenerateOutline.</summary>
public sealed class SpriteOutlinesProbe : MonoBehaviour
{
    public Texture2D[] textures;
    public Sprite[] sprites;

    [Serializable] public sealed class Path { public Vector2[] points; }
    [Serializable] public sealed class Shape { public string type; public Vector2[] vertices; }

    [Serializable] public sealed class Created
    {
        public string name;
        public Vector2[] vertices, uv;
        public ushort[] triangles;
        public Rect textureRect;
        public Path[] physics;
    }

    [Serializable] public sealed class Collider
    {
        public string sprite;
        public Path[] polygon;
        public Shape[] tilemap;
    }

    [Serializable] public sealed class Outline { public string name; public Path[] paths; }

    [Serializable] public sealed class Capture
    {
        public bool complete;
        public Created[] created;
        public Collider[] colliders;
        public Outline[] outlines;
    }

    static Path[] Physics(Sprite s)
    {
        var points = new List<Vector2>();
        var paths = new Path[s.GetPhysicsShapeCount()];
        for (int i = 0; i < paths.Length; i++) { s.GetPhysicsShape(i, points); paths[i] = new Path { points = points.ToArray() }; }
        return paths;
    }

    void Start()
    {
        var capture = new Capture();
        try
        {
            var created = new List<Created>();
            foreach (var t in textures.Where(t => t.name.EndsWith("R") || t.name.EndsWith("N")))
                foreach (var physics in new[] { false, true })
                    foreach (uint extrude in new uint[] { 0, 1, 4 })
                    {
                        var s = Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(.5f, .5f), 100, extrude, SpriteMeshType.Tight, Vector4.zero, physics);
                        created.Add(Describe($"{t.name}-physics{physics}-extrude{extrude}", s));
                    }
            foreach (var t in textures.Where(t => t.name.EndsWith("R")))
            {
                // An off-centre sub-rect at 64 pixels per unit, and one under 32 pixels (a quad).
                created.Add(Describe(t.name + "-sub", Sprite.Create(t, new Rect(4, 2, t.width - 10, t.height - 6), new Vector2(.25f, .75f), 64, 1, SpriteMeshType.Tight, Vector4.zero, true)));
                created.Add(Describe(t.name + "-small", Sprite.Create(t, new Rect(8, 4, 40, 30), new Vector2(.5f, .5f), 100, 1, SpriteMeshType.Tight, Vector4.zero, true)));
                created.Add(Describe(t.name + "-fullrect", Sprite.Create(t, new Rect(0, 0, t.width, t.height), new Vector2(.5f, .5f), 100, 1, SpriteMeshType.FullRect, Vector4.zero, true)));
            }
            capture.created = created.ToArray();

            var colliders = new List<Collider>();
            foreach (var sprite in sprites)
            {
                var go = new GameObject(sprite.name);
                go.AddComponent<SpriteRenderer>().sprite = sprite;
                var polygon = go.AddComponent<PolygonCollider2D>();
                var entry = new Collider { sprite = sprite.name, polygon = Enumerable.Range(0, polygon.pathCount).Select(i => new Path { points = polygon.GetPath(i) }).ToArray() };
                // The tile shapes of shapes whose outline is convex (the ring's decomposition is
                // not part of this probe).
                if (!sprite.name.StartsWith("Ring"))
                {
                    var grid = new GameObject("Grid" + sprite.name).AddComponent<Grid>();
                    var map = new GameObject("Map").AddComponent<Tilemap>();
                    map.transform.SetParent(grid.transform);
                    var tile = ScriptableObject.CreateInstance<Tile>();
                    tile.sprite = sprite;
                    tile.colliderType = Tile.ColliderType.Sprite;
                    map.SetTile(Vector3Int.zero, tile);
                    var collider = map.gameObject.AddComponent<TilemapCollider2D>();
                    collider.ProcessTilemapChanges();
                    var group = new PhysicsShapeGroup2D();
                    collider.GetShapes(group);
                    var shapes = new List<Shape>();
                    var vertices = new List<Vector2>();
                    for (int i = 0; i < group.shapeCount; i++)
                    {
                        group.GetShapeVertices(i, vertices);
                        shapes.Add(new Shape { type = group.GetShape(i).shapeType.ToString(), vertices = vertices.ToArray() });
                    }
                    entry.tilemap = shapes.ToArray();
                }
                colliders.Add(entry);
            }
            capture.colliders = colliders.ToArray();

#if UNITY_EDITOR
            var generate = typeof(UnityEditor.EditorApplication).Assembly.GetType("UnityEditor.Sprites.SpriteUtility")
                .GetMethod("GenerateOutline", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
            var outlines = new List<Outline>();
            foreach (var t in textures.Where(t => t.name.EndsWith("R")))
                foreach (var (detail, tolerance, holes) in new[] { (0.25f, (byte)0, true), (1f, (byte)0, true), (0.5f, (byte)128, false), (-1f, (byte)0, true) })
                {
                    var args = new object[] { t, new Rect(0, 0, t.width, t.height), detail, tolerance, holes, null };
                    generate.Invoke(null, args);
                    outlines.Add(new Outline { name = $"{t.name}-detail{detail}-tolerance{tolerance}-holes{holes}", paths = ((Vector2[][])args[5]).Select(p => new Path { points = p }).ToArray() });
                }
            capture.outlines = outlines.ToArray();
#endif
            capture.complete = true;
            File.WriteAllText(Environment.GetEnvironmentVariable("SPRITE_OUTLINES_OUT"), JsonUtility.ToJson(capture, true));
        }
        catch (Exception e) { Debug.LogException(e); }
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(capture.complete ? 0 : 1);
#else
        Application.Quit();
#endif
    }

    static Created Describe(string name, Sprite s) => s == null ? new Created { name = name + " <null>" } :
        new Created { name = name, vertices = s.vertices, uv = s.uv, triangles = s.triangles, textureRect = s.textureRect, physics = Physics(s) };
}
