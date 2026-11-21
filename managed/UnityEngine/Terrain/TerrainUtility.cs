using System;
using System.Collections.Generic;

namespace UnityEngine.TerrainUtils
{
    // Unity 6000.0 Scripting API (UnityEngine.TerrainUtils):
    // - TerrainUtility.AutoConnect: "Automatically connects neighboring terrains." Separate groups
    //   of connected terrains come from the grouping ID.
    // - TerrainMap: "Type for mapping 2D (X,Z) tile coordinates to a Terrain object."
    //   terrainTiles: "Mapping from TileCoord to Terrain." GetTerrain: "Returns a valid Terrain
    //   object if successful, null otherwise."
    // - CreateFromPlacement: "Creates a TerrainMap from the positions of all active Terrains." The
    //   origin terrain "Defines the grid origin and size, as well as group id if no filter is
    //   specified"; the grid size is "Typically" the terrain's size.x and size.z; a null filter
    //   falls back to "matching terrains in the same group as the origin".
    // - CreateFromConnectedNeighbors: the origin terrain "is given tile coordinate (0,0)"; a null
    //   filter means "no filter is applied".
    // - Both factories: "Can return null when no Terrain objects pass the filter." fullValidation:
    //   "Validate the Terrain map. Default is true."
    // The docs give no placement tolerance, overlap handling, validation effect or traversal
    // order; tests/projects/terrainoverrides measures them, and each member says what is not.

    /// <summary>Utility functions of the terrain tools.</summary>
    public static partial class TerrainUtility
    {
        /// <summary>
        /// Connects neighbouring terrains now, with the auto-connect rule of <see cref="Terrain"/>:
        /// active tiles that allow auto-connect and share a grouping ID become each other's
        /// neighbours when they sit one tile apart on the shared tile grid. The links stand until
        /// the connectivity is next marked dirty (Unbound's choice, unmeasured).
        /// </summary>
        public static void AutoConnect()
        {
            Terrain.SetConnectivityDirty();
            Terrain.ConnectTiles();
        }
    }

    /// <summary>A set of 2D tile coordinates.</summary>
    public readonly partial struct TerrainTileCoord
    {
        public readonly int tileX;
        public readonly int tileZ;

        public TerrainTileCoord(int tileX, int tileZ)
        {
            this.tileX = tileX;
            this.tileZ = tileZ;
        }
    }

    /// <summary>Maps 2D (X, Z) tile coordinates to terrains.</summary>
    public partial class TerrainMap
    {
        readonly Dictionary<TerrainTileCoord, Terrain> m_TerrainTiles = new Dictionary<TerrainTileCoord, Terrain>();

        public TerrainMap() { }

        public Dictionary<TerrainTileCoord, Terrain> terrainTiles => m_TerrainTiles;

        /// <summary>The terrain at tile (<paramref name="tileX"/>, <paramref name="tileZ"/>); null
        /// when the map has none there or that terrain has been destroyed.</summary>
        public Terrain GetTerrain(int tileX, int tileZ) =>
            m_TerrainTiles.TryGetValue(new TerrainTileCoord(tileX, tileZ), out var terrain) && terrain != null ? terrain : null;

        /// <summary>
        /// The grid of <paramref name="originTerrain"/>'s position and X/Z size, filled from the
        /// active terrains; a null filter keeps the terrains of the origin's grouping ID, as
        /// documented. A null origin gives null (measured); one without terrain data gives null
        /// (Unbound's choice, unmeasured).
        /// </summary>
        public static TerrainMap CreateFromPlacement(Terrain originTerrain, Predicate<Terrain> filter = null, bool fullValidation = true)
        {
            if (originTerrain == null) return null;
            var data = originTerrain.terrainData;
            if (data == null) return null;
            if (filter == null)
            {
                int group = originTerrain.groupingID;
                filter = terrain => terrain.groupingID == group;
            }
            var position = originTerrain.GetPosition();
            var size = data.size;
            return CreateFromPlacement(new Vector2(position.x, position.z), new Vector2(size.x, size.z), filter, fullValidation);
        }

        /// <summary>
        /// The active terrains that pass <paramref name="filter"/> (a null filter keeps every one;
        /// terrains without data are left out), each at the tile its position rounds to on the grid
        /// of <paramref name="gridSize"/> (X, Z) anchored at <paramref name="gridOrigin"/> (X, Z),
        /// whatever its size, height or offset from the tile; a tile several terrains round to keeps
        /// the first, silently; null when no terrain is placed; validation changes nothing these
        /// answers show (matches the Unity 6000.0.71f1 Editor, tests/projects/terrainoverrides map.*).
        /// </summary>
        public static TerrainMap CreateFromPlacement(Vector2 gridOrigin, Vector2 gridSize, Predicate<Terrain> filter = null, bool fullValidation = true)
        {
            var map = new TerrainMap();
            foreach (var terrain in Terrain.activeTerrains)
            {
                if (terrain.terrainData == null) continue;
                if (filter != null && !filter(terrain)) continue;
                var position = terrain.GetPosition();
                var coord = new TerrainTileCoord(Mathf.RoundToInt((position.x - gridOrigin.x) / gridSize.x),
                    Mathf.RoundToInt((position.z - gridOrigin.y) / gridSize.y));
                map.m_TerrainTiles.TryAdd(coord, terrain);
            }
            return map.m_TerrainTiles.Count > 0 ? map : null;
        }

        /// <summary>
        /// <paramref name="originTerrain"/> at tile (0, 0) and the terrains reached from it through
        /// the left (-X), right (+X), top (+Z) and bottom (-Z) neighbour links, breadth-first in that
        /// order, each one tile from the terrain it was reached from, links followed as they are,
        /// without the placement grid; a linked terrain fills any empty tile it is reached at, even
        /// one already in the map, and its own links are followed the first time it is reached; a
        /// tile already held keeps its terrain, silently; validation changes nothing these answers
        /// show (matches the Unity 6000.0.71f1 Editor, tests/projects/terrainoverrides map.connected*).
        /// Unbound's choices (unmeasured): a null origin gives null, as measured, and the origin must
        /// pass the filter, else the map is null; a terrain that fails the filter is left out and not
        /// followed.
        /// </summary>
        public static TerrainMap CreateFromConnectedNeighbors(Terrain originTerrain, Predicate<Terrain> filter = null, bool fullValidation = true)
        {
            if (originTerrain == null) return null;
            if (filter != null && !filter(originTerrain)) return null;
            var map = new TerrainMap();
            var followed = new HashSet<Terrain>(ReferenceEqualityComparer.Instance);
            var queue = new Queue<(Terrain terrain, TerrainTileCoord coord)>();
            var origin = new TerrainTileCoord(0, 0);
            map.m_TerrainTiles[origin] = originTerrain;
            followed.Add(originTerrain);
            queue.Enqueue((originTerrain, origin));
            while (queue.Count > 0)
            {
                var (terrain, coord) = queue.Dequeue();
                Reach(terrain.leftNeighbor, coord.tileX - 1, coord.tileZ);
                Reach(terrain.rightNeighbor, coord.tileX + 1, coord.tileZ);
                Reach(terrain.topNeighbor, coord.tileX, coord.tileZ + 1);
                Reach(terrain.bottomNeighbor, coord.tileX, coord.tileZ - 1);
            }
            return map;

            void Reach(Terrain neighbor, int tileX, int tileZ)
            {
                if (neighbor == null) return;
                if (filter != null && !filter(neighbor)) return;
                var tile = new TerrainTileCoord(tileX, tileZ);
                if (!map.m_TerrainTiles.TryAdd(tile, neighbor)) return;
                if (followed.Add(neighbor)) queue.Enqueue((neighbor, tile));
            }
        }
    }
}
