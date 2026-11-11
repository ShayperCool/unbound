using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Tilemaps;

public static class ProbeEntry
{
    struct Control
    {
        public string name;
        public Vector3 position, scale, parentAngles;
        public Vector2 displacement, offset;
        public float angle;
        public Control(string name, float angle, Vector3 position, Vector2 displacement = default,
            Vector2 offset = default, Vector3? scale = null, Vector3 parentAngles = default)
        {
            this.name = name; this.angle = angle; this.position = position;
            this.displacement = displacement; this.offset = offset;
            this.scale = scale ?? new Vector3(1, .8f, 1); this.parentAngles = parentAngles;
        }
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Run()
    {
        var output = Environment.GetEnvironmentVariable("PHYSICS_PROBE_OUT");
        if (string.IsNullOrEmpty(output)) return;
        Physics2D.OverlapPoint(Vector2.zero);
        Physics2D.simulationMode = SimulationMode2D.Script;
        Physics2D.gravity = Vector2.zero;
        Physics2D.queriesStartInColliders = true;
        Physics2D.defaultContactOffset = .01f;
        var center = new Vector3(.3f, -.2f, 2);
        var controls = new[]
        {
            new Control("origin", 23, Vector3.zero),
            new Control("translated10", 10, center),
            new Control("translated23", 23, center),
            new Control("translated30", 30, center),
            new Control("offset", 23, center, offset: new Vector2(.2f, -.1f)),
            new Control("dx-positive", 23, center, new Vector2(1e-7f, 0)),
            new Control("dx-negative", 10, center, new Vector2(-1e-7f, 0)),
            new Control("dy-positive", 23, center, new Vector2(0, 1e-7f)),
            new Control("dy-negative", 10, center, new Vector2(0, -1e-7f)),
            new Control("displaced-offset", 23, center, new Vector2(.125f, -.0625f), new Vector2(.2f, -.1f)),
            new Control("nonuniform", 23, center, scale: new Vector3(1.7f, .45f, 1)),
            new Control("reflected", 23, center, scale: new Vector3(-1.25f, .6f, 1)),
            new Control("large", 23, new Vector3(8192.3f, -4096.2f, 2)),
            new Control("large-offset", 10, new Vector3(8192.3f, -4096.2f, 2), new Vector2(.125f, -.0625f), new Vector2(.2f, -.1f)),
            new Control("nonplanar", 23, center, parentAngles: new Vector3(15, -20, 11)),
            new Control("nonplanar-offset", 10, center, new Vector2(1e-7f, -1e-7f), new Vector2(.2f, -.1f), new Vector3(-1.25f, .6f, 1), new Vector3(15, -20, 11))
        };
        foreach (var c in controls)
        foreach (string frame in new[] { "static", "body", "child", "rotated-child" })
        foreach (string kind in new[] { "circle", "box", "capsule", "polygon", "composite" })
            Record(c, frame, kind);
        foreach (bool delaunay in new[] { false, true })
        foreach (float radius in new[] { 0f, .125f })
            PartitionControl(delaunay, radius);
        foreach (bool delaunay in new[] { false, true }) PartitionDelaunay(delaunay);
        OutlineFrameControl();
        foreach (float coordinate in new[] { 800000f, 999000f, 1000000f, 8000000f, 9000000f, -9000000f }) LargeBodyLocalControl(coordinate);
        foreach (bool body in new[] { false, true })
        foreach (bool delaunay in new[] { false, true })
        foreach (int cellFrame in new[] { 0, 1, 2 })
        foreach (float extrusion in new[] { 0f, .1f }) TilemapControl(body, delaunay, cellFrame, extrusion);
        File.WriteAllText(output, ProbeLog.Json());
#if UNITY_EDITOR
        UnityEditor.EditorApplication.Exit(0);
#else
        Application.Quit();
#endif
    }

    static void Record(Control c, string frame, string kind)
    {
        var anchor = new GameObject("anchor");
        anchor.transform.position = c.position;
        anchor.transform.rotation = Quaternion.Euler(c.parentAngles);
        if (c.parentAngles != Vector3.zero) anchor.transform.localScale = new Vector3(1.4f, .7f, 1.2f);
        var body = new GameObject("body");
        body.transform.SetParent(anchor.transform, false);
        body.transform.localRotation = Quaternion.Euler(0, 0, frame == "child" ? 0 : frame == "rotated-child" ? 11 : c.angle);
        if (frame != "static" || kind == "composite")
            body.AddComponent<Rigidbody2D>().bodyType = frame == "static" ? RigidbodyType2D.Static : RigidbodyType2D.Kinematic;
        var target = body;
        if (frame == "child" || frame == "rotated-child" || kind == "composite")
        {
            target = new GameObject("target");
            target.transform.SetParent(body.transform, false);
            target.transform.localRotation = Quaternion.Euler(0, 0, c.angle - (frame == "child" ? 0 : frame == "rotated-child" ? 11 : c.angle));
            target.transform.localPosition = c.displacement;
        }
        else target.transform.position += (Vector3)c.displacement;
        target.name = "target";
        target.transform.localScale = c.scale;
        Collider2D collider;
        CompositeCollider2D composite = null;
        switch (kind)
        {
            case "circle": collider = target.AddComponent<CircleCollider2D>(); ((CircleCollider2D)collider).radius = .75f; break;
            case "box": collider = target.AddComponent<BoxCollider2D>(); ((BoxCollider2D)collider).size = new Vector2(2, 1); break;
            case "capsule": collider = target.AddComponent<CapsuleCollider2D>(); ((CapsuleCollider2D)collider).size = new Vector2(1, 3); break;
            case "polygon":
                collider = target.AddComponent<PolygonCollider2D>();
                ((PolygonCollider2D)collider).points = new[] { new Vector2(-1, -.5f), new Vector2(1, -.5f), new Vector2(.7f, .8f), new Vector2(-.6f, .9f) };
                break;
            default:
                composite = body.AddComponent<CompositeCollider2D>();
                composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
                composite.generationType = CompositeCollider2D.GenerationType.Manual;
                var source = target.AddComponent<BoxCollider2D>();
                source.size = new Vector2(2, 1); source.offset = c.offset;
                source.compositeOperation = Collider2D.CompositeOperation.Merge;
                var other = new GameObject("other"); other.transform.SetParent(body.transform, false);
                other.transform.localPosition = new Vector3(.375f, .125f, 0);
                var box = other.AddComponent<BoxCollider2D>(); box.size = new Vector2(1, 1);
                box.compositeOperation = Collider2D.CompositeOperation.Merge;
                collider = composite;
                break;
        }
        if (composite == null) collider.offset = c.offset;
        Physics2D.SyncTransforms();
        if (composite != null) { composite.GenerateGeometry(); Physics2D.SyncTransforms(); }
        ProbeLog.Begin(c.name + "/" + frame + "/" + kind);
        ProbeLog.Value("target.position", target.transform.position);
        ProbeLog.Value("target.right", target.transform.right);
        ProbeLog.Value("target.up", target.transform.up);
        Shapes(collider, composite);
        var center = (Vector2)target.transform.position;
        var axis = (Vector2)(Quaternion.Euler(0, 0, c.angle) * Vector3.up);
        foreach (var sample in new[] { -1e-7f, 0f, 1e-7f })
        {
            var key = sample < 0 ? "negative" : sample > 0 ? "positive" : "center";
            var origin = center + sample * axis;
            ProbeLog.Value(key + ".origin", origin);
            Hit(key + ".circle", Physics2D.CircleCast(origin, .3f, Vector2.right, 1));
            Hit(key + ".box", Physics2D.BoxCast(origin, new Vector2(.5f, .8f), 10, Vector2.right, 1));
            Hit(key + ".capsule", Physics2D.CapsuleCast(origin, new Vector2(1.6f, .6f), CapsuleDirection2D.Horizontal, 30, Vector2.right, 1));
        }
        UnityEngine.Object.DestroyImmediate(anchor);
    }

    static void Shapes(Collider2D collider, CompositeCollider2D composite)
    {
        ProbeLog.Value("offset", collider.offset);
        var group = new PhysicsShapeGroup2D(); collider.GetShapes(group);
        var shapes = new List<PhysicsShape2D>(); var vertices = new List<Vector2>();
        group.GetShapeData(shapes, vertices);
        ProbeLog.Value("shapeCount", shapes.Count); ProbeLog.Value("vertexCount", vertices.Count);
        for (int i = 0; i < shapes.Count; i++)
        {
            ProbeLog.Value("shape." + i + ".type", shapes[i].shapeType.ToString());
            ProbeLog.Value("shape." + i + ".radius", shapes[i].radius);
            ProbeLog.Value("shape." + i + ".vertexStartIndex", shapes[i].vertexStartIndex);
            ProbeLog.Value("shape." + i + ".vertexCount", shapes[i].vertexCount);
        }
        for (int i = 0; i < vertices.Count; i++)
        {
            ProbeLog.Value("local." + i, vertices[i]);
            ProbeLog.Value("world." + i, (Vector2)group.localToWorldMatrix.MultiplyPoint3x4(vertices[i]));
        }
        if (composite != null)
        {
            ProbeLog.Value("pathCount", composite.pathCount); ProbeLog.Value("pointCount", composite.pointCount);
            for (int i = 0; i < composite.pathCount; i++)
            {
                var points = new Vector2[composite.GetPathPointCount(i)];
                ProbeLog.Value("path." + i + ".count", composite.GetPath(i, points));
                for (int j = 0; j < points.Length; j++)
                {
                    ProbeLog.Value("path." + i + ".local." + j, points[j]);
                    ProbeLog.Value("path." + i + ".world." + j, (Vector2)composite.transform.TransformPoint(points[j]));
                }
            }
        }
    }

    static void PartitionControl(bool delaunay, float radius)
    {
        var root = new GameObject("partition");
        root.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
        var composite = root.AddComponent<CompositeCollider2D>();
        composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
        composite.generationType = CompositeCollider2D.GenerationType.Manual;
        composite.useDelaunayMesh = delaunay; composite.edgeRadius = radius;
        try
        {
            var outer = root.AddComponent<BoxCollider2D>(); outer.size = new Vector2(4, 4);
            outer.compositeOperation = Collider2D.CompositeOperation.Merge; outer.compositeOrder = 0;
            var hole = root.AddComponent<BoxCollider2D>(); hole.size = new Vector2(2, 2);
            hole.compositeOperation = Collider2D.CompositeOperation.Difference; hole.compositeOrder = 1;
            var island = root.AddComponent<BoxCollider2D>(); island.size = new Vector2(1, 1);
            island.offset = new Vector2(4, 0);
            island.compositeOperation = Collider2D.CompositeOperation.Merge; island.compositeOrder = 2;
            var flat = root.AddComponent<PolygonCollider2D>();
            flat.points = new[] { new Vector2(10, 0), new Vector2(10, 0), new Vector2(11, 0), new Vector2(12, 0) };
            flat.compositeOperation = Collider2D.CompositeOperation.Merge; flat.compositeOrder = 3;
            Physics2D.SyncTransforms(); composite.GenerateGeometry(); Physics2D.SyncTransforms();
            ProbeLog.Begin($"partition/{delaunay}/{radius}");
            Shapes(composite, composite);
            ProbeLog.Value("overlap.hole", composite.OverlapPoint(Vector2.zero));
            ProbeLog.Value("overlap.solid", composite.OverlapPoint(new Vector2(1.5f, 0)));
            ProbeLog.Value("overlap.island", composite.OverlapPoint(new Vector2(4, 0)));
            ProbeLog.Value("overlap.flat", composite.OverlapPoint(new Vector2(11, 0)));
            var sample = new Vector2(1.2f, 0);
            ProbeLog.Value("manual.before", composite.OverlapPoint(sample));
            hole.offset = new Vector2(.5f, 0); Physics2D.SyncTransforms();
            ProbeLog.Value("manual.held", composite.OverlapPoint(sample));
            composite.GenerateGeometry(); Physics2D.SyncTransforms();
            ProbeLog.Value("manual.generated", composite.OverlapPoint(sample));
        }
        catch (Exception error) { ProbeLog.Error(error); }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static void PartitionDelaunay(bool delaunay)
    {
        var root = new GameObject("delaunay");
        root.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
        var composite = root.AddComponent<CompositeCollider2D>();
        composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
        composite.generationType = CompositeCollider2D.GenerationType.Manual;
        composite.useDelaunayMesh = delaunay;
        try
        {
            var wide = root.AddComponent<BoxCollider2D>(); wide.size = new Vector2(2, .8f);
            wide.compositeOperation = Collider2D.CompositeOperation.Merge;
            var top = root.AddComponent<BoxCollider2D>(); top.size = Vector2.one; top.offset = new Vector2(.375f, .125f);
            top.compositeOperation = Collider2D.CompositeOperation.Merge;
            Physics2D.SyncTransforms(); composite.GenerateGeometry(); Physics2D.SyncTransforms();
            ProbeLog.Begin($"partition-delaunay/{delaunay}"); Shapes(composite, composite);
        }
        catch (Exception error) { ProbeLog.Error(error); }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static void TilemapControl(bool attachedBody, bool delaunay, int cellFrame, float extrusion)
    {
        var grid = new GameObject("tile-grid", typeof(Grid));
        grid.transform.position = new Vector3(.3f, -.2f, 0);
        grid.transform.rotation = Quaternion.Euler(0, 0, 11);
        grid.transform.localScale = new Vector3(1.4f, .7f, 1);
        var root = new GameObject("tile-map", typeof(Tilemap)); root.transform.SetParent(grid.transform, false);
        if (attachedBody) root.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
        var map = root.GetComponent<Tilemap>(); var collider = root.AddComponent<TilemapCollider2D>();
        var tile = ScriptableObject.CreateInstance<Tile>();
        try
        {
            foreach (var sprite in Resources.LoadAll<Sprite>("tiles")) if (sprite.name == "tile_7") tile.sprite = sprite;
            if (tile.sprite == null) throw new Exception("Missing concave tile sprite");
            tile.colliderType = Tile.ColliderType.Sprite; tile.flags = TileFlags.None;
            collider.useDelaunayMesh = delaunay; collider.extrusionFactor = extrusion;
            map.SetTile(Vector3Int.zero, tile); map.SetTile(new Vector3Int(2, 1, 0), tile);
            var matrix = Matrix4x4.TRS(new Vector3(.125f, -.0625f, 0),
                Quaternion.Euler(0, 0, cellFrame == 1 ? 90 : 23), new Vector3(cellFrame == 2 ? -1.25f : 1, .8f, 1));
            map.SetTransformMatrix(Vector3Int.zero, matrix);
            map.SetTransformMatrix(new Vector3Int(2, 1, 0), matrix);
            collider.ProcessTilemapChanges(); Physics2D.SyncTransforms();
            ProbeLog.Begin("tilemap/" + attachedBody + "/" + delaunay + "/" + cellFrame + "/" + extrusion.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            Shapes(collider, null);
            var solid = matrix.MultiplyPoint3x4(new Vector3(-.25f, -.25f, 0));
            var hole = matrix.MultiplyPoint3x4(new Vector3(.25f, .25f, 0));
            var center = map.GetCellCenterLocal(Vector3Int.zero);
            ProbeLog.Value("solid", collider.OverlapPoint(root.transform.TransformPoint(solid + center)));
            ProbeLog.Value("notch", collider.OverlapPoint(root.transform.TransformPoint(hole + center)));
        }
        catch (Exception error) { ProbeLog.Error(error); }
        finally { UnityEngine.Object.DestroyImmediate(grid); UnityEngine.Object.DestroyImmediate(tile); }
    }

    static void LargeBodyLocalControl(float coordinate)
    {
        var root = new GameObject("large-body-local");
        root.AddComponent<Rigidbody2D>().bodyType = RigidbodyType2D.Static;
        var composite = root.AddComponent<CompositeCollider2D>();
        composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
        composite.generationType = CompositeCollider2D.GenerationType.Manual;
        try
        {
            var near = root.AddComponent<BoxCollider2D>(); near.size = new Vector2(2, 2);
            near.compositeOperation = Collider2D.CompositeOperation.Merge;
            var far = root.AddComponent<BoxCollider2D>(); far.size = new Vector2(1000, 1000);
            far.offset = new Vector2(coordinate, 0); far.compositeOperation = Collider2D.CompositeOperation.Merge;
            Physics2D.SyncTransforms(); composite.GenerateGeometry(); Physics2D.SyncTransforms();
            ProbeLog.Begin("large-body-local/" + coordinate.ToString("R", System.Globalization.CultureInfo.InvariantCulture));
            Shapes(composite, composite);
            ProbeLog.Value("near", composite.OverlapPoint(Vector2.zero));
            ProbeLog.Value("far", composite.OverlapPoint(new Vector2(coordinate, 0)));
        }
        catch (Exception error) { ProbeLog.Error(error); }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    static void OutlineFrameControl()
    {
        var anchor = new GameObject("outline-anchor");
        anchor.transform.rotation = Quaternion.Euler(17, 29, 11);
        anchor.transform.localScale = new Vector3(1.4f, .7f, 1.2f);
        var root = new GameObject("outline"); root.transform.SetParent(anchor.transform, false);
        root.transform.localRotation = Quaternion.Euler(0, 0, 23);
        var body = root.AddComponent<Rigidbody2D>(); body.bodyType = RigidbodyType2D.Static;
        var composite = root.AddComponent<CompositeCollider2D>();
        composite.geometryType = CompositeCollider2D.GeometryType.Outlines;
        composite.generationType = CompositeCollider2D.GenerationType.Manual; composite.edgeRadius = .125f;
        try
        {
            var box = root.AddComponent<BoxCollider2D>(); box.size = new Vector2(2, 2);
            box.compositeOperation = Collider2D.CompositeOperation.Merge;
            Physics2D.SyncTransforms(); composite.GenerateGeometry(); Physics2D.SyncTransforms();
            ProbeLog.Begin("outline-frame");
            ProbeLog.Value("pathCount", composite.pathCount);
            var path = new Vector2[composite.GetPathPointCount(0)]; composite.GetPath(0, path);
            ProbeLog.Value("path.count", path.Length);
            for (int i = 0; i < path.Length; i++) ProbeLog.Value("path." + i, path[i]);
            var point = body.position + (Vector2)(Quaternion.Euler(0, 0, body.rotation) * (Vector3)path[0]);
            ProbeLog.Value("edge", composite.OverlapPoint(point));
            var near = point + Vector2.right * .0625f;
            ProbeLog.Value("edge.offset", composite.OverlapPoint(near));
            ProbeLog.Value("edge.far", composite.OverlapPoint(point + Vector2.right));
            composite.edgeRadius = 0; Physics2D.SyncTransforms();
            ProbeLog.Value("radius.held", composite.OverlapPoint(near));
            ProbeLog.Value("outline.center", composite.OverlapPoint(body.position));
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons; Physics2D.SyncTransforms();
            ProbeLog.Value("polygon.center", composite.OverlapPoint(body.position));
            var clone = UnityEngine.Object.Instantiate(root);
            clone.transform.position += Vector3.right * 10; Physics2D.SyncTransforms();
            ProbeLog.Value("clone.pending.center", clone.GetComponent<CompositeCollider2D>().OverlapPoint(clone.GetComponent<Rigidbody2D>().position));
            UnityEngine.Object.DestroyImmediate(clone);
            composite.geometryType = CompositeCollider2D.GeometryType.Outlines; Physics2D.SyncTransforms();
            ProbeLog.Value("outline.again", composite.OverlapPoint(body.position));
            ProbeLog.Value("edge.again", composite.OverlapPoint(point));
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons; Physics2D.SyncTransforms();
            ProbeLog.Value("polygon.pending", composite.OverlapPoint(body.position));
            composite.GenerateGeometry(); Physics2D.SyncTransforms();
            ProbeLog.Value("polygon.generated", composite.OverlapPoint(body.position));
            composite.geometryType = CompositeCollider2D.GeometryType.Outlines; Physics2D.SyncTransforms();
            ProbeLog.Value("outline.pending", composite.OverlapPoint(body.position));
            composite.GenerateGeometry(); Physics2D.SyncTransforms();
            ProbeLog.Value("outline.generated", composite.OverlapPoint(body.position));
            ProbeLog.Value("radius.generated", composite.OverlapPoint(near));
            composite.geometryType = CompositeCollider2D.GeometryType.Polygons;
            composite.generationType = CompositeCollider2D.GenerationType.Synchronous; Physics2D.SyncTransforms();
            ProbeLog.Value("synchronous", composite.OverlapPoint(body.position));
            box.offset = new Vector2(.25f, 0); Physics2D.SyncTransforms();
            ProbeLog.Value("synchronous.source", composite.OverlapPoint(body.position));
        }
        catch (Exception error) { ProbeLog.Error(error); }
        finally { UnityEngine.Object.DestroyImmediate(anchor); }
    }

    static void Hit(string key, RaycastHit2D hit)
    {
        ProbeLog.Value(key + ".collider", hit.collider ? hit.collider.name : "none");
        ProbeLog.Value(key + ".distance", hit.distance); ProbeLog.Value(key + ".fraction", hit.fraction);
        ProbeLog.Value(key + ".normal", hit.normal); ProbeLog.Value(key + ".point", hit.point);
        ProbeLog.Value(key + ".centroid", hit.centroid);
    }
}
