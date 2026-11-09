// Unbound: added (not part of DotRecast).

using System;
using System.Collections.Generic;
using DotRecast.Core.Numerics;

namespace DotRecast.Detour
{
    /// <summary>
    /// Unbound: one NavMesh data instance placed in the world (a NavMesh surface): its pose, its own
    /// tile grid and the world bounds of its tiles. Its tiles keep their places in the grid of the
    /// data as built (tile x, y of the data's own frame), whatever the pose, so a moved or turned
    /// instance's tiles are found by turning a query box into the surface's frame, and ground
    /// polygons connect only to tiles of the same surface, by their sides in that frame
    /// (Unity 6000.0.71f1: tests/projects/navpath `place.moved`, `place.adjacent`).
    /// </summary>
    internal sealed class DtSurface
    {
        public readonly int id;
        /// <summary>Whether the pose is not the identity (the tiles' vertices then differ from their
        /// vertices in the surface's frame, <see cref="DtMeshData.localVerts"/>).</summary>
        public readonly bool moved;
        public readonly RcVec3f position;
        public readonly UnityEngine.Quaternion rotation;
        readonly UnityEngine.Quaternion m_Inverse;
        // The inverse rotation as a matrix (rows), for turning boxes into the surface's frame.
        readonly float m_I00, m_I01, m_I02, m_I10, m_I11, m_I12, m_I20, m_I21, m_I22;
        /// <summary>The size of a tile of the surface's grid along x and z of its frame.</summary>
        public readonly float tileWidth, tileHeight;
        /// <summary>The world bounds of the surface's tiles (grown as tiles are added).</summary>
        public RcVec3f bmin = new RcVec3f(float.MaxValue, float.MaxValue, float.MaxValue);
        public RcVec3f bmax = new RcVec3f(float.MinValue, float.MinValue, float.MinValue);

        readonly Dictionary<long, DtMeshTile> m_Tiles = new Dictionary<long, DtMeshTile>();

        public DtSurface(int id, RcVec3f position, UnityEngine.Quaternion rotation, float tileWidth, float tileHeight)
        {
            this.id = id;
            this.position = position;
            this.rotation = rotation;
            this.tileWidth = tileWidth;
            this.tileHeight = tileHeight;
            moved = position.X != 0f || position.Y != 0f || position.Z != 0f || rotation != UnityEngine.Quaternion.identity;
            m_Inverse = UnityEngine.Quaternion.Inverse(rotation);
            var ix = m_Inverse * UnityEngine.Vector3.right;
            var iy = m_Inverse * UnityEngine.Vector3.up;
            var iz = m_Inverse * UnityEngine.Vector3.forward;
            m_I00 = ix.x; m_I10 = ix.y; m_I20 = ix.z;
            m_I01 = iy.x; m_I11 = iy.y; m_I21 = iy.z;
            m_I02 = iz.x; m_I12 = iz.y; m_I22 = iz.z;
        }

        /// <summary>A point of the world in the surface's frame.</summary>
        public RcVec3f ToLocal(RcVec3f world)
        {
            if (!moved) return world;
            var p = m_Inverse * new UnityEngine.Vector3(world.X - position.X, world.Y - position.Y, world.Z - position.Z);
            return new RcVec3f(p.x, p.y, p.z);
        }

        /// <summary>A direction of the surface's frame in the world.</summary>
        public RcVec3f ToWorldDirection(RcVec3f local)
        {
            if (!moved) return local;
            var d = rotation * new UnityEngine.Vector3(local.X, local.Y, local.Z);
            return new RcVec3f(d.x, d.y, d.z);
        }

        /// <summary>The bounds, in the surface's frame, of a world box turned into it.</summary>
        public void ToLocalBox(RcVec3f min, RcVec3f max, out RcVec3f lmin, out RcVec3f lmax)
        {
            if (!moved)
            {
                lmin = min;
                lmax = max;
                return;
            }
            var c = ToLocal(new RcVec3f((min.X + max.X) * 0.5f, (min.Y + max.Y) * 0.5f, (min.Z + max.Z) * 0.5f));
            float ex = (max.X - min.X) * 0.5f, ey = (max.Y - min.Y) * 0.5f, ez = (max.Z - min.Z) * 0.5f;
            var e = new RcVec3f(
                MathF.Abs(m_I00) * ex + MathF.Abs(m_I01) * ey + MathF.Abs(m_I02) * ez,
                MathF.Abs(m_I10) * ex + MathF.Abs(m_I11) * ey + MathF.Abs(m_I12) * ez,
                MathF.Abs(m_I20) * ex + MathF.Abs(m_I21) * ey + MathF.Abs(m_I22) * ez);
            lmin = RcVec3f.Subtract(c, e);
            lmax = RcVec3f.Add(c, e);
        }

        /// <summary>The part of a world box over the surface's tiles, turned into the surface's frame
        /// (as Unity looks up a surface's tiles); false when the box misses the surface.</summary>
        public bool QueryBox(RcVec3f min, RcVec3f max, out RcVec3f lmin, out RcVec3f lmax)
        {
            var cmin = RcVec3f.Max(min, bmin);
            var cmax = RcVec3f.Min(max, bmax);
            if (cmin.X > cmax.X || cmin.Y > cmax.Y || cmin.Z > cmax.Z)
            {
                lmin = lmax = default;
                return false;
            }
            ToLocalBox(cmin, cmax, out lmin, out lmax);
            return true;
        }

        /// <summary>The surface's grid cell of a coordinate of its frame.</summary>
        public int CellX(float x) => (int)MathF.Floor(x / tileWidth);
        public int CellY(float z) => (int)MathF.Floor(z / tileHeight);

        static long Key(int x, int y) => ((long)x << 32) | (uint)y;

        /// <summary>The first of the surface's tiles at a place of its grid (the others follow
        /// through <see cref="DtMeshTile.surfaceNext"/>); null for none.</summary>
        public DtMeshTile TileAt(int x, int y) => m_Tiles.TryGetValue(Key(x, y), out var tile) ? tile : null;

        internal void Add(DtMeshTile tile)
        {
            var data = tile.data;
            long key = Key(data.surfaceX, data.surfaceY);
            tile.surfaceNext = null;
            if (m_Tiles.TryGetValue(key, out var first))
            {
                while (first.surfaceNext != null) first = first.surfaceNext;
                first.surfaceNext = tile;
            }
            else
            {
                m_Tiles[key] = tile;
            }
            bmin = RcVec3f.Min(bmin, data.header.bmin);
            bmax = RcVec3f.Max(bmax, data.header.bmax);
        }

        internal void Remove(DtMeshTile tile)
        {
            long key = Key(tile.data.surfaceX, tile.data.surfaceY);
            if (!m_Tiles.TryGetValue(key, out var first)) return;
            if (first == tile)
            {
                if (tile.surfaceNext != null) m_Tiles[key] = tile.surfaceNext;
                else m_Tiles.Remove(key);
            }
            else
            {
                while (first.surfaceNext != null && first.surfaceNext != tile) first = first.surfaceNext;
                if (first.surfaceNext == tile) first.surfaceNext = tile.surfaceNext;
            }
            tile.surfaceNext = null;
        }

        /// <summary>The vertices of a tile's polygons in the surface's frame (its world vertices
        /// turned back), and their bounds, for a tile of a moved surface that has none yet (a tile
        /// cut by carving in the world).</summary>
        internal void SetLocalVerts(DtMeshData data)
        {
            var local = new float[data.verts.Length];
            var lmin = new RcVec3f(float.MaxValue, float.MaxValue, float.MaxValue);
            var lmax = new RcVec3f(float.MinValue, float.MinValue, float.MinValue);
            for (int i = 0; i + 2 < local.Length; i += 3)
            {
                var p = ToLocal(new RcVec3f(data.verts[i], data.verts[i + 1], data.verts[i + 2]));
                local[i] = p.X;
                local[i + 1] = p.Y;
                local[i + 2] = p.Z;
                lmin = RcVec3f.Min(lmin, p);
                lmax = RcVec3f.Max(lmax, p);
            }
            data.localVerts = local;
            data.localBmin = lmin;
            data.localBmax = lmax;
        }
    }
}
