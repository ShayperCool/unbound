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
        /// Connects neighbouring terrains now, with the auto-connect pass of <see cref="Terrain"/>:
        /// each grouping ID's placement map of the terrains that allow auto-connect gives every
        /// tile its four neighbours. The links stand until the connectivity is next marked dirty
        /// (Unbound's choice, unmeasured).
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
        /// of <paramref name="gridSize"/> (X, Z) anchored at <paramref name="gridOrigin"/> (X, Z)
        /// (the offset times the reciprocal of the cell size, rounded), whatever its size, height or
        /// offset from the tile; a tile several terrains round to keeps the first, silently; null
        /// when no terrain is placed; validation changes nothing these answers show (matches the
        /// Unity 6000.0.71f1 Editor, tests/projects/terrainoverrides map.*).
        /// </summary>
        public static TerrainMap CreateFromPlacement(Vector2 gridOrigin, Vector2 gridSize, Predicate<Terrain> filter = null, bool fullValidation = true)
        {
            var map = new TerrainMap();
            float inverseX = 1f / gridSize.x, inverseZ = 1f / gridSize.y;
            foreach (var terrain in Terrain.activeTerrains)
            {
                if (terrain.terrainData == null) continue;
                if (filter != null && !filter(terrain)) continue;
                var position = terrain.GetPosition();
                map.m_TerrainTiles.TryAdd(new TerrainTileCoord(Mathf.RoundToInt((position.x - gridOrigin.x) * inverseX),
                    Mathf.RoundToInt((position.z - gridOrigin.y) * inverseZ)), terrain);
            }
            return map.m_TerrainTiles.Count > 0 ? map : null;
        }

        /// <summary>
        /// <paramref name="originTerrain"/> at tile (0, 0) and the terrains reached from it through
        /// the neighbour links, each one tile from the terrain it is reached from (links followed as
        /// they are, without the placement grid), breadth-first: a terrain that passes the filter
        /// (a null filter applies none) takes its tile when the tile is empty, even when it holds
        /// another tile already, and only then are its left (-X), bottom (-Z), right (+X) and top
        /// (+Z) links queued; the walk stops once the map holds more terrains than there are active
        /// terrains. A null origin, or one without data, gives null; an origin that fails the filter
        /// gives an empty map. As Unity's own TerrainMap does; tests/projects/terrainoverrides
        /// map.connected* records two such maps.
        /// </summary>
        public static TerrainMap CreateFromConnectedNeighbors(Terrain originTerrain, Predicate<Terrain> filter = null, bool fullValidation = true)
        {
            if (originTerrain == null || originTerrain.terrainData == null) return null;
            var map = new TerrainMap();
            var queue = new Queue<(Terrain terrain, int tileX, int tileZ)>();
            queue.Enqueue((originTerrain, 0, 0));
            int active = Terrain.activeTerrains.Length;
            while (queue.Count > 0)
            {
                var (terrain, tileX, tileZ) = queue.Dequeue();
                if (filter != null && !filter(terrain)) continue;
                if (map.GetTerrain(tileX, tileZ) != null || !map.m_TerrainTiles.TryAdd(new TerrainTileCoord(tileX, tileZ), terrain)) continue;
                if (map.m_TerrainTiles.Count > active) break;
                Follow(terrain.leftNeighbor, tileX - 1, tileZ);
                Follow(terrain.bottomNeighbor, tileX, tileZ - 1);
                Follow(terrain.rightNeighbor, tileX + 1, tileZ);
                Follow(terrain.topNeighbor, tileX, tileZ + 1);
            }
            return map;

            void Follow(Terrain neighbor, int x, int z)
            {
                if (neighbor != null) queue.Enqueue((neighbor, x, z));
            }
        }
    }
}
