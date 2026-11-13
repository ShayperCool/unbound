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
        public Created[] runtime;
        public Outline[] runtimeOutlines;
        public Created[] overridden;
        public Collider[] overriddenColliders;
        public string[] logs;
    }

    static readonly List<string> s_Logs = new List<string>();

    /// <summary>A ring of the given radii (fractions of the half size) at a centre, as the
    /// authored textures draw shapes (texels outside transparent).</summary>
    static Color32[] Ring(int w, int h, float cx, float cy, float inner, float outer)
    {
        var px = new Color32[w * h];
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                float u = (x + .5f - cx) / (w * .5f), v = (y + .5f - cy) / (h * .5f), d = u * u + v * v;
                px[y * w + x] = d <= outer && d >= inner ? new Color32((byte)(x * 5), (byte)(y * 3), 40, 255) : new Color32(0, 0, 0, 0);
            }
        return px;
    }

    static Sprite TightSprite(Texture2D t, Rect rect) => Sprite.Create(t, rect, new Vector2(.4f, .6f), 50, 1, SpriteMeshType.Tight, Vector4.zero, true);

    static string Try(Action action)
    {
        try { action(); return "ok"; }
        // A null argument's message is the runtime's wording: its parameter name only.
        catch (Exception thrown)
        {
            var e = thrown is TargetInvocationException { InnerException: { } inner } ? inner : thrown;
            return e.GetType().Name + ": " + (e is ArgumentNullException n ? n.ParamName : e.Message);
        }
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
#if UNITY_EDITOR
            var generate = typeof(UnityEditor.EditorApplication).Assembly.GetType("UnityEditor.Sprites.SpriteUtility")
                .GetMethod("GenerateOutline", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic);
#endif
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
            Application.logMessageReceived += (message, stack, type) => s_Logs.Add(type + ": " + message);

            // Textures made by scripts: readable, then made non-readable by Apply; sprites over the
            // whole texture and an off-centre sub-rect.
            var runtime = new List<Created>();
            var runtimeOutlines = new List<Outline>();
            var made = new Texture2D(80, 48, TextureFormat.RGBA32, false) { name = "Made" };
            made.SetPixels32(Ring(80, 48, 44, 22, .2f, .8f));
            made.Apply();
            void Record(string label, Texture2D t)
            {
                foreach (var rect in new[] { new Rect(0, 0, t.width, t.height), new Rect(6.5f, 3, t.width - 10, t.height - 7.75f) })
                {
                    string name = $"{label}-{rect.x}-{rect.y}";
                    Sprite sprite = null;
                    string result = Try(() => sprite = TightSprite(t, rect));
                    runtime.Add(sprite != null ? Describe(name, sprite) : new Created { name = name + " " + result });
#if UNITY_EDITOR
                    var args = new object[] { t, rect, .25f, (byte)0, true, null };
                    result = Try(() => generate.Invoke(null, args));
                    runtimeOutlines.Add(new Outline { name = name + " " + result, paths = (args[5] as Vector2[][] ?? new Vector2[0][]).Select(p => new Path { points = p }).ToArray() });
#endif
                }
            }
            Record("made-readable", made);
            // Written but not applied yet: the texels the CPU holds.
            made.SetPixels32(Ring(80, 48, 30, 26, 0f, .5f));
            Record("made-unapplied", made);
            made.Apply(false, true);
            Record("made-unreadable", made);
            // An imported readable texture a script rewrote (last: it changes the asset in memory).
            var imported = textures.First(t => t.name == "EllipseR");
            imported.SetPixels32(Ring(imported.width, imported.height, 20, 18, .1f, .6f));
            Record("imported-unapplied", imported);
            imported.Apply();
            Record("imported-applied", imported);
            capture.runtime = runtime.ToArray();
            capture.runtimeOutlines = runtimeOutlines.ToArray();

            // Sprite.OverridePhysicsShape: points in rect space (also outside the rect), then a
            // path of two points, then no paths; a PolygonCollider2D added after each.
            var overridden = new List<Created>();
            var overriddenColliders = new List<Collider>();
            var tri = textures.First(t => t.name == "TriR");
            var target = Sprite.Create(tri, new Rect(2, 3, 40, 42), new Vector2(.25f, .75f), 64, 1, SpriteMeshType.FullRect, Vector4.zero, false);
            var overrides = new (string, List<Vector2[]>)[]
            {
                ("two", new List<Vector2[]> { new[] { new Vector2(0, 0), new Vector2(40, 0), new Vector2(20, 42) }, new[] { new Vector2(-5, 10), new Vector2(45.5f, 10), new Vector2(45.5f, 50), new Vector2(1.25f, 50) } }),
                ("short", new List<Vector2[]> { new[] { new Vector2(1, 1), new Vector2(30, 30) } }),
                ("mixed", new List<Vector2[]> { new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(0, 10) }, new[] { new Vector2(1, 1) } }),
                ("nullPath", new List<Vector2[]> { new[] { new Vector2(0, 0), new Vector2(10, 0), new Vector2(0, 10) }, null }),
                ("empty", new List<Vector2[]>()),
            };
            foreach (var (label, list) in overrides)
            {
                s_Logs.Add("override " + label);
                string result = Try(() => target.OverridePhysicsShape(list));
                overridden.Add(Describe($"{label} {result}", target));
                var go = new GameObject("Overridden" + label);
                go.AddComponent<SpriteRenderer>().sprite = target;
                var polygon = go.AddComponent<PolygonCollider2D>();
                overriddenColliders.Add(new Collider { sprite = label, polygon = Enumerable.Range(0, polygon.pathCount).Select(i => new Path { points = polygon.GetPath(i) }).ToArray() });
            }
            s_Logs.Add("override null " + Try(() => target.OverridePhysicsShape(null)));
#if UNITY_EDITOR
            s_Logs.Add("outline of no texture " + Try(() => generate.Invoke(null, new object[] { null, new Rect(0, 0, 8, 8), .25f, (byte)0, true, null })));
#endif
            capture.overridden = overridden.ToArray();
            capture.overriddenColliders = overriddenColliders.ToArray();
            capture.logs = s_Logs.ToArray();
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
