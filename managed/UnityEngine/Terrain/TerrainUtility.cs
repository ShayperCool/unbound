using System;
using System.Collections.Generic;
using Unbound.Runtime;

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
    // order; those are Unbound's choices (unmeasured), stated on each member.

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
        static readonly HashSet<string> s_Warned = new HashSet<string>();

        public TerrainMap() { }

        public Dictionary<TerrainTileCoord, Terrain> terrainTiles => m_TerrainTiles;

        /// <summary>The terrain at tile (<paramref name="tileX"/>, <paramref name="tileZ"/>); null
        /// when the map has none there or that terrain has been destroyed.</summary>
        public Terrain GetTerrain(int tileX, int tileZ) =>
            m_TerrainTiles.TryGetValue(new TerrainTileCoord(tileX, tileZ), out var terrain) && terrain != null ? terrain : null;

        /// <summary>
        /// The grid of <paramref name="originTerrain"/>'s position and X/Z size, filled from the
        /// active terrains; a null filter keeps the terrains of the origin's grouping ID, as
        /// documented. Unbound's choice (unmeasured): a null origin, or one without terrain data,
        /// gives null.
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
        /// The active terrains that pass <paramref name="filter"/>, each at its cell of the grid of
        /// <paramref name="gridSize"/> (X, Z) anchored at <paramref name="gridOrigin"/> (X, Z).
        /// Unbound's choices (unmeasured): a terrain is placed by the tile-grid rule auto-connect
        /// uses (its X/Z size equals the cell size and it sits a whole number of cells from the
        /// origin, within a ten-thousandth of the cell size; heights ignored), other terrains and
        /// terrains without data are left out; a cell that several terrains share stays empty, with
        /// a warning; a null filter keeps every active terrain, because this overload has no origin
        /// terrain whose group the documented fallback could match; the map is null when no
        /// terrain is placed.
        /// </summary>
        public static TerrainMap CreateFromPlacement(Vector2 gridOrigin, Vector2 gridSize, Predicate<Terrain> filter = null, bool fullValidation = true)
        {
            NoteValidation(fullValidation);
            var cells = new Dictionary<TerrainTileCoord, List<Terrain>>();
            foreach (var terrain in Terrain.activeTerrains)
            {
                if (terrain.terrainData == null) continue;
                if (filter != null && !filter(terrain)) continue;
                if (!Terrain.TryGetTileCoord(terrain, gridOrigin, gridSize, out int tileX, out int tileZ)) continue;
                var coord = new TerrainTileCoord(tileX, tileZ);
                if (!cells.TryGetValue(coord, out var list)) cells[coord] = list = new List<Terrain>();
                list.Add(terrain);
            }
            var map = new TerrainMap();
            foreach (var cell in cells)
            {
                if (cell.Value.Count == 1)
                {
                    map.m_TerrainTiles[cell.Key] = cell.Value[0];
                    continue;
                }
                var ids = new List<int>();
                var names = new List<string>();
                foreach (var terrain in cell.Value)
                {
                    ids.Add(terrain.GetInstanceID());
                    names.Add("'" + terrain.name + "'");
                }
                ids.Sort();
                WarnOnce($"placement {cell.Key.tileX},{cell.Key.tileZ} {string.Join(",", ids)}",
                    $"Unbound: TerrainMap: {cell.Value.Count} terrains ({string.Join(", ", names)}) sit on tile ({cell.Key.tileX}, {cell.Key.tileZ}); that tile is left empty.",
                    cell.Value[0]);
            }
            return map.m_TerrainTiles.Count > 0 ? map : null;
        }

        /// <summary>
        /// <paramref name="originTerrain"/> at tile (0, 0) and the terrains reached from it through
        /// the left (-X), right (+X), top (+Z) and bottom (-Z) neighbour links, each one tile from
        /// the terrain it was reached from; a null filter applies none, as documented. Unbound's
        /// choices (unmeasured): the origin must pass the filter, else the map is null; a terrain
        /// that fails the filter is left out and not followed; the links are walked breadth-first
        /// in left, right, top, bottom order, and each terrain keeps the first tile it is reached
        /// at; a terrain reached at a tile another terrain already holds is left out, with a
        /// warning; links are followed as they are, without the placement grid check.
        /// </summary>
        public static TerrainMap CreateFromConnectedNeighbors(Terrain originTerrain, Predicate<Terrain> filter = null, bool fullValidation = true)
        {
            NoteValidation(fullValidation);
            if (originTerrain == null) return null;
            if (filter != null && !filter(originTerrain)) return null;
            var map = new TerrainMap();
            var seen = new HashSet<Terrain>(ReferenceEqualityComparer.Instance);
            var queue = new Queue<(Terrain terrain, TerrainTileCoord coord)>();
            var origin = new TerrainTileCoord(0, 0);
            map.m_TerrainTiles[origin] = originTerrain;
            seen.Add(originTerrain);
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
                if (neighbor == null || !seen.Add(neighbor)) return;
                if (filter != null && !filter(neighbor)) return;
                var tile = new TerrainTileCoord(tileX, tileZ);
                if (map.m_TerrainTiles.TryGetValue(tile, out var holder))
                {
                    WarnOnce($"connected {tileX},{tileZ} {holder.GetInstanceID()} {neighbor.GetInstanceID()}",
                        $"Unbound: TerrainMap: terrain '{neighbor.name}' is linked as tile ({tileX}, {tileZ}), which '{holder.name}' already holds; it is left out of the map.",
                        neighbor);
                    return;
                }
                map.m_TerrainTiles[tile] = neighbor;
                queue.Enqueue((neighbor, tile));
            }
        }

        // Unbound's choice (unmeasured): the docs do not say what validation changes, so both
        // values build the same map; turning it off is reported once.
        static void NoteValidation(bool fullValidation)
        {
            if (!fullValidation)
                Stubs.Ignored("UnityEngine.TerrainUtils.TerrainMap fullValidation: false",
                    "is not distinguished: the map is built by the same rules as with validation");
        }

        static void WarnOnce(string key, string message, UnityEngine.Object context)
        {
            if (s_Warned.Add(key)) Debug.LogWarning(message, context);
        }
    }
}
