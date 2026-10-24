using System;
using Unbound.Runtime;

namespace UnityEngine
{
    /// <summary>
    /// A layout of cells: converts between cell coordinates and local positions. A
    /// <see cref="Grid"/> owns the layout; other layouts (tilemaps) use the grid they are under.
    /// The conversions follow what Unity 6 computes (measured, `tests/projects/twod`):
    /// rectangles step by size plus gap; hexagons are pointy-topped, odd rows shifted half a cell
    /// right, rows 3/4 of the cell height apart, gaps ignored; isometric cells step half the size
    /// along both diagonals; Z-as-Y isometric adds z (in cell heights) to y. The cell swizzle
    /// reorders the computed position.
    /// </summary>
    [RequireComponent(typeof(Transform))]
    public partial class GridLayout : Behaviour
    {
        public GridLayout() { }

        /// <summary>The grid whose layout this one uses (itself for a grid).</summary>
        internal virtual Grid LayoutSource => null;

        public Vector3 cellSize => LayoutSource is Grid g ? g.m_CellSize : Vector3.one;
        public Vector3 cellGap => LayoutSource is Grid g ? g.m_CellGap : Vector3.zero;
        public CellLayout cellLayout => LayoutSource is Grid g ? g.m_CellLayout : CellLayout.Rectangle;
        public CellSwizzle cellSwizzle => LayoutSource is Grid g ? g.m_CellSwizzle : CellSwizzle.XYZ;

        public Bounds GetBoundsLocal(Vector3Int cellPosition) =>
            new Bounds(CellToLocal(cellPosition), Grid.Swizzle(cellSwizzle, cellSize));

        public Bounds GetBoundsLocal(Vector3 origin, Vector3 size)
        {
            var layout = cellLayout;
            if (layout == CellLayout.Hexagon)
            {
                // The hexagons' own extent: half a cell around the first center, rows 3/4 apart,
                // odd rows half a cell further right (measured).
                var cs = cellSize;
                var first = CellToLocalInterpolated(origin);
                var hexMin = new Vector3(first.x - cs.x * 0.5f, first.y - cs.y * 0.5f, origin.z * cs.z);
                var extent = new Vector3((size.x + 0.5f) * cs.x, ((size.y - 1f) * 0.75f + 1f) * cs.y, size.z * cs.z);
                return new Bounds(hexMin + extent * 0.5f, extent);
            }
            // Z-as-Y isometric bounds are the plain isometric ones.
            if (layout == CellLayout.IsometricZAsY) layout = CellLayout.Isometric;
            var min = new Vector3(float.MaxValue, float.MaxValue, float.MaxValue);
            var max = new Vector3(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i < 8; i++)
            {
                var corner = new Vector3((i & 1) != 0 ? origin.x + size.x : origin.x, (i & 2) != 0 ? origin.y + size.y : origin.y, (i & 4) != 0 ? origin.z + size.z : origin.z);
                var p = GridMath.CellToLocal(corner, layout, cellSize, cellGap, cellSwizzle);
                min = Vector3.Min(min, p);
                max = Vector3.Max(max, p);
            }
            var b = new Bounds();
            b.SetMinMax(min, max);
            return b;
        }

        public Vector3 CellToLocal(Vector3Int cellPosition) => CellToLocalInterpolated(cellPosition);

        public Vector3Int LocalToCell(Vector3 localPosition) =>
            GridMath.LocalToCell(localPosition, cellLayout, cellSize, cellGap, cellSwizzle);

        public Vector3 CellToLocalInterpolated(Vector3 cellPosition) =>
            GridMath.CellToLocal(cellPosition, cellLayout, cellSize, cellGap, cellSwizzle);

        public Vector3 LocalToCellInterpolated(Vector3 localPosition) =>
            GridMath.LocalToCellInterpolated(localPosition, cellLayout, cellSize, cellGap, cellSwizzle);

        public Vector3 CellToWorld(Vector3Int cellPosition) => LocalToWorld(CellToLocal(cellPosition));

        public Vector3Int WorldToCell(Vector3 worldPosition) => LocalToCell(WorldToLocal(worldPosition));

        public Vector3 LocalToWorld(Vector3 localPosition) => transform.TransformPoint(localPosition);

        public Vector3 WorldToLocal(Vector3 worldPosition) => transform.InverseTransformPoint(worldPosition);

        /// <summary>Where in a cell its center is, in cell units.</summary>
        public Vector3 GetLayoutCellCenter() =>
            cellLayout == CellLayout.Hexagon ? Vector3.zero : new Vector3(0.5f, 0.5f, 0.5f);

        public enum CellLayout
        {
            Rectangle = 0,
            Hexagon = 1,
            Isometric = 2,
            IsometricZAsY = 3,
        }

        public enum CellSwizzle
        {
            XYZ = 0,
            XZY = 1,
            YXZ = 2,
            YZX = 3,
            ZXY = 4,
            ZYX = 5,
        }
    }

    /// <summary>The layout tilemaps under it use: cell size, gap, layout and swizzle.</summary>
    [ClassId(156049354)]
    [RequireComponent(typeof(Transform))]
    public sealed partial class Grid : GridLayout
    {
        internal Vector3 m_CellSize = new Vector3(1f, 1f, 0f);
        internal Vector3 m_CellGap;
        internal CellLayout m_CellLayout;
        internal CellSwizzle m_CellSwizzle;
        /// <summary>Bumped on every layout change, so tilemaps under the grid rebuild.</summary>
        internal int m_LayoutVersion;

        public Grid() { }

        internal override Grid LayoutSource => this;

        public new Vector3 cellSize
        {
            get => m_CellSize;
            set
            {
                m_CellSize = value;
                LayoutChanged();
            }
        }

        public new Vector3 cellGap
        {
            get => m_CellGap;
            set
            {
                m_CellGap = value;
                LayoutChanged();
            }
        }

        public new CellLayout cellLayout
        {
            get => m_CellLayout;
            set
            {
                m_CellLayout = value;
                LayoutChanged();
            }
        }

        public new CellSwizzle cellSwizzle
        {
            get => m_CellSwizzle;
            set
            {
                m_CellSwizzle = value;
                LayoutChanged();
            }
        }

        void LayoutChanged()
        {
            m_LayoutVersion++;
            if (m_GameObject == null) return;
            foreach (var map in m_GameObject.GetComponentsInChildren<Tilemaps.Tilemap>(true))
                map.LayoutChanged();
        }

        public Vector3 GetCellCenterLocal(Vector3Int position) => CellToLocalInterpolated(position + GetLayoutCellCenter());

        public Vector3 GetCellCenterWorld(Vector3Int position) => LocalToWorld(CellToLocalInterpolated(position + GetLayoutCellCenter()));

        public static Vector3 Swizzle(CellSwizzle swizzle, Vector3 position)
        {
            switch (swizzle)
            {
                case CellSwizzle.XZY: return new Vector3(position.x, position.z, position.y);
                case CellSwizzle.YXZ: return new Vector3(position.y, position.x, position.z);
                case CellSwizzle.YZX: return new Vector3(position.y, position.z, position.x);
                case CellSwizzle.ZXY: return new Vector3(position.z, position.x, position.y);
                case CellSwizzle.ZYX: return new Vector3(position.z, position.y, position.x);
                default: return position;
            }
        }

        public static Vector3 InverseSwizzle(CellSwizzle swizzle, Vector3 position)
        {
            switch (swizzle)
            {
                case CellSwizzle.XZY: return new Vector3(position.x, position.z, position.y);
                case CellSwizzle.YXZ: return new Vector3(position.y, position.x, position.z);
                case CellSwizzle.YZX: return new Vector3(position.z, position.x, position.y);
                case CellSwizzle.ZXY: return new Vector3(position.y, position.z, position.x);
                case CellSwizzle.ZYX: return new Vector3(position.z, position.y, position.x);
                default: return position;
            }
        }

        internal override void ReadSerialized(YNode body, IReferenceResolver resolver)
        {
            m_CellSize = body.Vector3("m_CellSize", new Vector3(1f, 1f, 0f));
            m_CellGap = body.Vector3("m_CellGap");
            m_CellLayout = (CellLayout)body.Int("m_CellLayout");
            m_CellSwizzle = (CellSwizzle)body.Int("m_CellSwizzle");
        }

        internal override void CopyFrom(Component source, Func<Object, Object> remap)
        {
            var s = (Grid)source;
            m_CellSize = s.m_CellSize;
            m_CellGap = s.m_CellGap;
            m_CellLayout = s.m_CellLayout;
            m_CellSwizzle = s.m_CellSwizzle;
        }
    }
}

namespace Unbound.Runtime
{
    using UnityEngine;
    using CellLayout = UnityEngine.GridLayout.CellLayout;
    using CellSwizzle = UnityEngine.GridLayout.CellSwizzle;

    /// <summary>The cell layouts' conversions between cell and local coordinates.</summary>
    internal static class GridMath
    {
        /// <summary>The distance between neighbouring cells: size plus gap (hexagons ignore gaps).</summary>
        static Vector3 Step(CellLayout layout, Vector3 size, Vector3 gap) => layout == CellLayout.Hexagon ? size : size + gap;

        static float Divide(float a, float b) => b == 0f ? 0f : a / b;

        static bool OddRow(float row) => ((int)row & 1) != 0;

        internal static Vector3 CellToLocal(Vector3 c, CellLayout layout, Vector3 size, Vector3 gap, CellSwizzle swizzle)
        {
            var s = Step(layout, size, gap);
            Vector3 local;
            switch (layout)
            {
                case CellLayout.Hexagon:
                    local = new Vector3((c.x + (OddRow(c.y) ? 0.5f : 0f)) * s.x, c.y * 0.75f * s.y, c.z * s.z);
                    break;
                case CellLayout.Isometric:
                    local = new Vector3((c.x - c.y) * s.x * 0.5f, (c.x + c.y) * s.y * 0.5f, c.z * s.z);
                    break;
                case CellLayout.IsometricZAsY:
                    local = new Vector3((c.x - c.y) * s.x * 0.5f, (c.x + c.y + c.z * s.z) * s.y * 0.5f, c.z * s.z);
                    break;
                default:
                    local = Vector3.Scale(c, s);
                    break;
            }
            return Grid.Swizzle(swizzle, local);
        }

        internal static Vector3 LocalToCellInterpolated(Vector3 position, CellLayout layout, Vector3 size, Vector3 gap, CellSwizzle swizzle)
        {
            var l = Grid.InverseSwizzle(swizzle, position);
            var s = Step(layout, size, gap);
            switch (layout)
            {
                case CellLayout.Hexagon:
                {
                    float y = Divide(l.y, 0.75f * s.y);
                    return new Vector3(Divide(l.x, s.x) - (OddRow(y) ? 0.5f : 0f), y, Divide(l.z, s.z));
                }
                case CellLayout.Isometric:
                case CellLayout.IsometricZAsY:
                {
                    float z = Divide(l.z, s.z);
                    float u = Divide(l.x, s.x * 0.5f);
                    float v = Divide(l.y, s.y * 0.5f);
                    if (layout == CellLayout.IsometricZAsY) v -= z * s.z;
                    return new Vector3((u + v) * 0.5f, (v - u) * 0.5f, z);
                }
                default:
                    return new Vector3(Divide(l.x, s.x), Divide(l.y, s.y), Divide(l.z, s.z));
            }
        }

        internal static Vector3Int LocalToCell(Vector3 position, CellLayout layout, Vector3 size, Vector3 gap, CellSwizzle swizzle)
        {
            if (layout != CellLayout.Hexagon)
                return Vector3Int.FloorToInt(LocalToCellInterpolated(position, layout, size, gap, swizzle));
            // The hexagon containing the point: pointy-topped, the cell's width and height. A point
            // on a shared edge belongs to the higher row, then to the cell on the right.
            var l = Grid.InverseSwizzle(swizzle, position);
            float w = size.x, h = size.y;
            // Hexagon layers round to the nearest (measured).
            int z = Mathf.RoundToInt(Divide(l.z, size.z));
            if (w == 0f || h == 0f) return new Vector3Int(0, 0, z);
            int row = Mathf.FloorToInt(l.y / (0.75f * h));
            float eps = 1e-5f * Mathf.Max(w, h);
            (int X, int Y) best = default;
            float bestDistance = float.MaxValue;
            for (int r = row + 1; r >= row - 1; r--)
            {
                float offset = (r & 1) != 0 ? 0.5f : 0f;
                int col = Mathf.FloorToInt(l.x / w - offset + 0.5f);
                for (int c = col + 1; c >= col - 1; c--)
                {
                    float cx = (c + offset) * w, cy = r * 0.75f * h;
                    float dx = Mathf.Abs(l.x - cx), dy = Mathf.Abs(l.y - cy);
                    if (dx <= w * 0.5f + eps && dy <= h * 0.5f - h * 0.5f * (dx / w) + eps)
                        return new Vector3Int(c, r, z);
                    float distance = dx * dx + dy * dy;
                    if (distance < bestDistance)
                    {
                        bestDistance = distance;
                        best = (c, r);
                    }
                }
            }
            return new Vector3Int(best.X, best.Y, z);
        }
    }
}
