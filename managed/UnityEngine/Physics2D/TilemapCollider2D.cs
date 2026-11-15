using System;
using System.Collections.Generic;
using Unbound.Runtime;
using W = Unbound.Runtime.Physics2DWorld;
using Shape = Unbound.Box2D.Collision.Shapes.Shape;

namespace UnityEngine.Tilemaps
{
    /// <summary>
    /// Shapes for a tilemap's cells: a Grid cell gives the cell's outline, a Sprite cell its
    /// sprite's physics shapes, both under the cell's transform at the cell center. As in Unity
    /// (measured): a cell's outline is taken when its tile changes and the changes are processed
    /// (at the end of the frame or by <see cref="ProcessTilemapChanges"/>), not by physics steps;
    /// cells are placed at the layout's current centers when shapes are built; the extrusion
    /// grows each cell's outline with square corners (Clipper's square join); concave outlines
    /// split into convex pieces; shapes come in z, y, x order of the cells, changed cells last.
    /// </summary>
    [ClassId(19719996)]
    [RequireComponent(typeof(Tilemap))]
    public sealed partial class TilemapCollider2D : Collider2D
    {
        bool m_UseDelaunayMesh;
        uint m_MaximumTileChangeCount = 1000;
        float m_ExtrusionFactor;
        /// <summary>A cell's outlines in its own space (before its transform), as last processed.</summary>
        sealed class CellShape
        {
            internal Vector3Int Position;
            internal List<Vector2[]> Outlines;
            internal Matrix4x4 Transform;
            // What the outlines came from: a change that leaves these as they were does nothing.
            internal Sprite Sprite;
            internal Tile.ColliderType Collider;
            /// <summary>The fixtures the cell made when the shapes were last built (-1: unknown).</summary>
            internal int ShapeCount = -1;
        }

        /// <summary>The cells' shapes in Unity's order: all cells in z, y, x order when built
        /// whole, then each changed cell moves to the end (measured).</summary>
        readonly List<CellShape> m_Cells = new List<CellShape>();
        bool m_Processed;

        /// <summary>One step of the shape hash's history since the shapes were last built: a
        /// changed cell's removed shapes (last first) or an added cell's new shapes.</summary>
        struct HashStep
        {
            internal List<Shape> Removed;
            internal CellShape Added;
        }

        /// <summary>The hash before the pending tile changes and their steps in order; consumed
        /// when the shapes are built again.</summary>
        readonly List<HashStep> m_HashSteps = new List<HashStep>();
        bool m_HashPending;
        uint m_HashBase;

        public TilemapCollider2D() { }

        public bool useDelaunayMesh
        {
            get => m_UseDelaunayMesh;
            set
            {
                m_UseDelaunayMesh = value;
                GeometrySettingChanged();
            }
        }

        public uint maximumTileChangeCount
        {
            get => m_MaximumTileChangeCount;
            set => m_MaximumTileChangeCount = value;
        }

        public float extrusionFactor
        {
            get => m_ExtrusionFactor;
            set
            {
                m_ExtrusionFactor = Mathf.Max(0f, value);
                GeometrySettingChanged();
            }
        }

        /// <summary>The collider builds again from its cells in z, y, x order (measured), and its
        /// shapes change.</summary>
        internal override void GeometrySettingChanged()
        {
            m_Cells.Sort((a, b) => Tilemap.CellOrder.Instance.Compare(a.Position, b.Position));
            ShapeChanged();
        }

        Tilemap Map =>m_GameObject != null ? m_GameObject.GetComponent<Tilemap>() : null;

        public bool hasTilemapChanges
        {
            get
            {
                var map = Map;
                return map != null && (!m_Processed || map.m_ShapeChanges.Count > 0);
            }
        }

        /// <summary>Takes the tilemap's changed cells into the collider's shapes now.</summary>
        public void ProcessTilemapChanges()
        {
            var map = Map;
            if (map == null) return;
            // One trace per sprite in a pass: texels cannot change while it runs.
            m_PassOutlines.Clear();
            if (!m_Processed || map.m_ShapeChanges.Count > m_MaximumTileChangeCount)
            {
                m_Cells.Clear();
                foreach (var pair in map.m_Cells) Outline(map, pair.Key, pair.Value);
                m_HashPending = false;
                m_HashSteps.Clear();
            }
            else
            {
                ApplyChanges(map);
            }
            m_PassOutlines.Clear();
            m_Processed = true;
            map.m_ShapeChanges.Clear();
            ShapeChanged();
            if (!m_HashPending) return;
            // Unity creates the changed shapes here: build them now, so that a later rebuild for
            // another reason is a full one. Shapes that cannot be built now (a disabled collider,
            // a running step) are built whole later, in z, y, x order, from the seed.
            W.Flush();
            m_HashPending = false;
            m_HashSteps.Clear();
        }

        /// <summary>
        /// The changed cells in change order. A change that leaves the cell's sprite, collider
        /// type and transform as they were does nothing; otherwise the cell's shapes go and its
        /// new ones come last. The shape hash takes each removed shape (the cell's last first),
        /// then each added one, on top of its previous value (matches the Unity 6000.0.71f1
        /// Editor, tests/projects/shapes2d).
        /// </summary>
        void ApplyChanges(Tilemap map)
        {
            // Start from the shapes as built so far; without them the hash restarts when built.
            W.Flush();
            var shapes = BuiltShapes();
            if (shapes != null && !m_HashPending)
            {
                m_HashBase = GetShapeHash();
                m_HashPending = true;
            }
            foreach (var p in map.m_ShapeChanges)
            {
                int index = m_Cells.FindIndex(c => c.Position == p);
                map.m_Cells.TryGetValue(p, out var cell);
                if (index >= 0 && cell != null && SameSource(m_Cells[index], cell)) continue;
                if (index >= 0)
                {
                    var old = m_Cells[index];
                    if (shapes != null && old.ShapeCount >= 0)
                    {
                        int start = 0;
                        for (int i = 0; i < index; i++) start += m_Cells[i].ShapeCount;
                        var removed = shapes.GetRange(start, old.ShapeCount);
                        removed.Reverse();
                        shapes.RemoveRange(start, old.ShapeCount);
                        m_HashSteps.Add(new HashStep { Removed = removed });
                    }
                    else shapes = null;
                    m_Cells.RemoveAt(index);
                }
                if (cell != null && Outline(map, p, cell)) m_HashSteps.Add(new HashStep { Added = m_Cells[m_Cells.Count - 1] });
            }
            if (shapes == null)
            {
                m_HashPending = false;
                m_HashSteps.Clear();
            }
        }

        static bool SameSource(CellShape shape, Tilemap.Cell cell) =>
            ReferenceEquals(shape.Sprite, cell.Sprite) && shape.Collider == cell.Collider && shape.Transform == cell.Transform;

        /// <summary>The built fixtures' shapes, when they are the cells' (each cell's count known).</summary>
        List<Shape> BuiltShapes()
        {
            int total = 0;
            foreach (var cell in m_Cells)
            {
                if (cell.ShapeCount < 0) return null;
                total += cell.ShapeCount;
            }
            if (total != m_Fixtures.Count) return null;
            var shapes = new List<Shape>(total);
            foreach (var fixture in m_Fixtures) shapes.Add(fixture.Shape);
            return shapes;
        }

        /// <summary>After an incremental pass the hash carries the pass's removed and added shapes;
        /// any other rebuild recreates every shape from the seed.</summary>
        internal override void ShapesRebuilt()
        {
            m_ShapeHashCarried = false;
            if (!m_HashPending) return;
            m_HashPending = false;
            uint hash = m_HashBase;
            bool valid = BuiltShapes() != null;
            foreach (var step in m_HashSteps)
            {
                if (!valid) break;
                if (step.Removed != null)
                {
                    foreach (var shape in step.Removed) hash = ShapeHash(shape, hash);
                    continue;
                }
                int index = m_Cells.IndexOf(step.Added);
                if (index < 0) continue;
                int start = 0;
                for (int i = 0; i < index; i++) start += m_Cells[i].ShapeCount;
                for (int i = 0; i < step.Added.ShapeCount; i++) hash = ShapeHash(m_Fixtures[start + i].Shape, hash);
            }
            m_HashSteps.Clear();
            if (!valid) return;
            m_CarriedShapeHash = hash;
            m_ShapeHashCarried = true;
        }

        /// <summary>Each sprite's collider outline, during one pass over the cells.</summary>
        readonly Dictionary<Sprite, Vector2[][]> m_PassOutlines = new Dictionary<Sprite, Vector2[][]>();

        /// <summary>The collider outlines of one cell, around its center before its transform:
        /// the cell's shape (Grid), or the sprite's collider outline, else its rectangle (Sprite).</summary>
        bool Outline(Tilemap map, Vector3Int position, Tilemap.Cell cell)
        {
            if (cell.Tile == null || cell.Collider == Tile.ColliderType.None) return false;
            var outlines = new List<Vector2[]>();
            if (cell.Collider == Tile.ColliderType.Grid)
            {
                outlines.Add(GridOutline(map));
            }
            else
            {
                var sprite = cell.Sprite;
                if (sprite == null) return false;
                // The physics shape, else the traced outline (measured: tests/projects/spriteoutlines).
                if (!m_PassOutlines.TryGetValue(sprite, out var traced)) m_PassOutlines[sprite] = traced = sprite.ColliderOutline();
                foreach (var shape in traced)
                    if (shape.Length >= 3) outlines.Add(shape);
                if (outlines.Count == 0)
                {
                    var r = sprite.rect;
                    float ppu = sprite.pixelsPerUnit;
                    var min = -sprite.pivot / ppu;
                    var max = min + new Vector2(r.width, r.height) / ppu;
                    outlines.Add(new[] { new Vector2(max.x, min.y), max, new Vector2(min.x, max.y), min });
                }
            }
            m_Cells.Add(new CellShape { Position = position, Outlines = outlines, Transform = cell.Transform, Sprite = cell.Sprite, Collider = cell.Collider });
            return true;
        }

        /// <summary>A cell's outline around its center: the cell's rectangle, hexagon or diamond.</summary>
        static Vector2[] GridOutline(Tilemap map)
        {
            var size = map.cellSize;
            float hx = size.x * 0.5f, hy = size.y * 0.5f;
            switch (map.cellLayout)
            {
                case GridLayout.CellLayout.Hexagon:
                    return new[]
                    {
                        new Vector2(hx, -hy * 0.5f), new Vector2(hx, hy * 0.5f), new Vector2(0f, hy),
                        new Vector2(-hx, hy * 0.5f), new Vector2(-hx, -hy * 0.5f), new Vector2(0f, -hy),
                    };
                case GridLayout.CellLayout.Isometric:
                case GridLayout.CellLayout.IsometricZAsY:
                    return new[] { new Vector2(0f, -hy), new Vector2(hx, 0f), new Vector2(0f, hy), new Vector2(-hx, 0f) };
                default:
                    return new[] { new Vector2(hx, -hy), new Vector2(hx, hy), new Vector2(-hx, hy), new Vector2(-hx, -hy) };
            }
        }

        /// <summary>The cells' outlines in the tilemap's space: extruded in the cell's own space
        /// (square corners), under the cell's transform, at the cell's current center, offset.</summary>
        IEnumerable<(List<Vector2[]> Outlines, Matrix4x4 Transform, Vector2 Center)> CellGeometry()
        {
            var map = Map;
            if (map == null) yield break;
            if (!m_Processed) ProcessTilemapChanges();
            foreach (var cell in m_Cells)
                yield return (m_ExtrusionFactor > 0f ? ClipperOps.Offset(cell.Outlines, m_ExtrusionFactor) : cell.Outlines,
                    cell.Transform, (Vector2)map.TileCenterLocal(cell.Position));
        }

        internal List<Vector2[]> Polygons()
        {
            var result = new List<Vector2[]>();
            foreach (var cell in CellGeometry())
                foreach (var outline in cell.Outlines)
                {
                    var points = new Vector2[outline.Length];
                    for (int i = 0; i < points.Length; i++) points[i] = (Vector2)cell.Transform.MultiplyPoint3x4(outline[i]) + (cell.Center + m_Offset);
                    result.Add(points);
                }
            return result;
        }

        internal override void BuildCompositeSource(Matrix4x4 toBody, List<Vector2[]> polygons)
        {
            foreach (var polygon in Polygons())
            {
                var points = new Vector2[polygon.Length];
                for (int i = 0; i < points.Length; i++) points[i] = toBody.MultiplyPoint3x4(polygon[i]);
                polygons.Add(points);
            }
        }

        internal override void BuildShapes(Matrix4x4 toBody, List<Shape> shapes)
        {
            if (!Compat.Physics2DGeneratedGeometryMeasured)
            {
                Stubs.Ignored("TilemapCollider2D generated geometry", "this release is unmeasured; using the legacy transformed-contour ear partition without refinement");
                BuildLegacyShapes(toBody, shapes);
                return;
            }
            // A full rebuild takes the cells in z, y, x order; an incremental pass keeps its order.
            if (!m_HashPending) m_Cells.Sort((a, b) => Tilemap.CellOrder.Instance.Compare(a.Position, b.Position));
            int dropped = 0, cellIndex = 0;
            foreach (var cell in CellGeometry())
            {
                int before = shapes.Count;
                var contours = new List<IReadOnlyList<System.Numerics.Vector2>>(cell.Outlines.Count);
                foreach (var outline in cell.Outlines)
                {
                    var points = new System.Numerics.Vector2[outline.Length];
                    for (int i = 0; i < points.Length; i++) points[i] = W.ToB2(outline[i]);
                    contours.Add(points);
                }
                if (!PolygonPartition.TryGenerate(contours, m_UseDelaunayMesh, out var pieces, out bool removed) || removed) dropped++;
                var cellMatrix = cell.Transform;
                cellMatrix.m03 += cell.Center.x; cellMatrix.m13 += cell.Center.y;
                cellMatrix.m03 += m_Offset.x; cellMatrix.m13 += m_Offset.y;
                var bodyMatrix = toBody * cellMatrix;
                foreach (var piece in pieces)
                {
                    bool valid = true;
                    for (int i = 0; i < piece.Count; i++)
                    {
                        piece[i] = ToBody(bodyMatrix, W.ToUnity(piece[i]));
                        if (!PolygonPartition.IsValidPoint(piece[i])) valid = false;
                    }
                    var shape = valid ? ConvexShape(piece) : null;
                    if (shape == null) { dropped++; continue; }
                    shapes.Add(shape);
                }
                m_Cells[cellIndex++].ShapeCount = shapes.Count - before;
            }
            m_ErrorState = dropped > 0 ? ColliderErrorState2D.RemovedShapes : ColliderErrorState2D.None;
        }

        void BuildLegacyShapes(Matrix4x4 toBody, List<Shape> shapes)
        {
            int made = 0, dropped = 0;
            foreach (var polygon in Polygons())
            {
                var points = new List<System.Numerics.Vector2>(polygon.Length);
                foreach (var p in polygon) points.Add(ToBody(toBody, p));
                foreach (var piece in PolygonDecomposition.TilePieces(points))
                {
                    var shape = ConvexShape(piece);
                    if (shape == null) { dropped++; continue; }
                    shapes.Add(shape);
                    made++;
                }
            }
            m_ErrorState = dropped > 0 ? ColliderErrorState2D.RemovedShapes : ColliderErrorState2D.None;
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            ReadColliderData(body, resolver);
            m_MaximumTileChangeCount = (uint)body.Long("m_MaximumTileChangeCount", 1000);
            m_ExtrusionFactor = body.Float("m_ExtrusionFactor");
            m_UseDelaunayMesh = body.Bool("m_UseDelaunayMesh");
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (TilemapCollider2D)source;
            CopyColliderData(s);
            m_MaximumTileChangeCount = s.m_MaximumTileChangeCount;
            m_ExtrusionFactor = s.m_ExtrusionFactor;
            m_UseDelaunayMesh = s.m_UseDelaunayMesh;
        }
    }
}
