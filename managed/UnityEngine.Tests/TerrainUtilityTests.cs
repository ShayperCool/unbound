using System;
using System.Collections.Generic;
using UnityEngine.TerrainUtils;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// TerrainUtility.AutoConnect, TerrainMap and TerrainTileCoord. Documented (Unity 6000.0
    /// Scripting API): AutoConnect connects neighbouring terrains, grouped by grouping ID;
    /// CreateFromPlacement maps the active terrains on the grid of the origin terrain's position
    /// and size, and a null filter keeps the origin's group; CreateFromConnectedNeighbors gives the
    /// origin tile (0,0), and a null filter applies none; both can give null when no terrain passes
    /// the filter; GetTerrain gives null when there is no valid terrain at the tile. Placement by
    /// rounding, overlaps, the link walk and fullValidation follow the Unity 6000.0.71f1 Editor
    /// (tests/projects/terrainoverrides); the tests marked unmeasured check Unbound's choices. Each test
    /// uses its own grouping IDs so terrains of other tests cannot reach its maps.
    /// </summary>
    public class TerrainUtilityTests
    {
        const float k_Size = 10f;

        sealed class Tiles : IDisposable
        {
            readonly List<GameObject> m_Objects = new List<GameObject>();
            readonly Dictionary<float, TerrainData> m_Data = new Dictionary<float, TerrainData>();

            public Terrain Add(string name, float x, float z, int group, bool autoConnect = true, float size = k_Size, float y = 0f)
            {
                if (!m_Data.TryGetValue(size, out var data))
                {
                    data = new TerrainData { heightmapResolution = 33 };
                    data.size = new Vector3(size, 5f, size);
                    m_Data[size] = data;
                }
                var go = new GameObject(name);
                go.transform.position = new Vector3(x, y, z);
                m_Objects.Add(go);
                var terrain = go.AddComponent<Terrain>();
                terrain.groupingID = group;
                terrain.allowAutoConnect = autoConnect;
                terrain.terrainData = data;
                return terrain;
            }

            public void Dispose()
            {
                foreach (var go in m_Objects) if (go != null) Object.DestroyImmediate(go);
                foreach (var data in m_Data.Values) Object.DestroyImmediate(data);
                Terrain.SetConnectivityDirty();
            }
        }

        /// <summary>Collects the warnings given at the logger (Unbound diagnostics do not reach the
        /// game's log callbacks) until disposed, then restores the logger.</summary>
        sealed class WarningCapture : ILogHandler, IDisposable
        {
            readonly ILogger m_Logger = Debug.unityLogger;
            readonly ILogHandler m_Next;
            readonly bool m_Enabled;
            readonly LogType m_Filter;
            public readonly List<string> Warnings = new List<string>();

            public WarningCapture()
            {
                m_Next = m_Logger.logHandler;
                m_Enabled = m_Logger.logEnabled;
                m_Filter = m_Logger.filterLogType;
                m_Logger.logHandler = this;
                m_Logger.logEnabled = true;
                m_Logger.filterLogType = LogType.Log;
            }

            public void LogFormat(LogType logType, Object context, string format, params object[] args)
            {
                if (logType == LogType.Warning) Warnings.Add(args == null || args.Length == 0 ? format : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args));
                m_Next.LogFormat(logType, context, format, args);
            }

            public void LogException(Exception exception, Object context) => m_Next.LogException(exception, context);

            public void Dispose()
            {
                m_Logger.logHandler = m_Next;
                m_Logger.logEnabled = m_Enabled;
                m_Logger.filterLogType = m_Filter;
            }
        }

        [Fact]
        public void CreateFromPlacementMapsATwoByTwoGrid()
        {
            using var tiles = new Tiles();
            var southWest = tiles.Add("SW", 0f, 0f, group: 71);
            var southEast = tiles.Add("SE", k_Size, 0f, group: 71);
            var northWest = tiles.Add("NW", 0f, k_Size, group: 71);
            var northEast = tiles.Add("NE", k_Size, k_Size, group: 71);

            var map = TerrainMap.CreateFromPlacement(southWest);
            Assert.NotNull(map);
            Assert.Equal(4, map.terrainTiles.Count);
            Assert.Same(southWest, map.GetTerrain(0, 0));
            Assert.Same(southEast, map.GetTerrain(1, 0));
            Assert.Same(northWest, map.GetTerrain(0, 1));
            Assert.Same(northEast, map.GetTerrain(1, 1));
            Assert.Same(northEast, map.terrainTiles[new TerrainTileCoord(1, 1)]);

            // The origin terrain defines the grid origin.
            var fromNorthEast = TerrainMap.CreateFromPlacement(northEast);
            Assert.NotNull(fromNorthEast);
            Assert.Same(northEast, fromNorthEast.GetTerrain(0, 0));
            Assert.Same(southWest, fromNorthEast.GetTerrain(-1, -1));

            // fullValidation does not change the map (tests/projects/terrainoverrides map.*).
            var unvalidated = TerrainMap.CreateFromPlacement(southWest, null, false);
            Assert.NotNull(unvalidated);
            Assert.Equal(4, unvalidated.terrainTiles.Count);
            Assert.Same(northEast, unvalidated.GetTerrain(1, 1));
        }

        [Fact]
        public void CreateFromPlacementWithAGridOriginAndSize()
        {
            using var tiles = new Tiles();
            var southWest = tiles.Add("SW", 100f, 50f, group: 72);
            var southEast = tiles.Add("SE", 100f + k_Size, 50f, group: 72);
            var northEast = tiles.Add("NE", 100f + k_Size, 50f + k_Size, group: 72);

            var map = TerrainMap.CreateFromPlacement(new Vector2(100f + k_Size, 50f + k_Size), new Vector2(k_Size, k_Size), terrain => terrain.groupingID == 72);
            Assert.NotNull(map);
            Assert.Equal(3, map.terrainTiles.Count);
            Assert.Same(northEast, map.GetTerrain(0, 0));
            Assert.Same(southEast, map.GetTerrain(0, -1));
            Assert.Same(southWest, map.GetTerrain(-1, -1));
            Assert.Null(map.GetTerrain(-1, 0));
        }

        [Fact]
        public void PlacementFilterSelectsTheTerrains()
        {
            using var tiles = new Tiles();
            var origin = tiles.Add("Origin", 0f, 0f, group: 73);
            var east = tiles.Add("East", k_Size, 0f, group: 73);
            var other = tiles.Add("Other", 0f, k_Size, group: 74);

            // Documented: a null filter keeps the terrains of the origin's group.
            var map = TerrainMap.CreateFromPlacement(origin);
            Assert.NotNull(map);
            Assert.Equal(2, map.terrainTiles.Count);
            Assert.Same(east, map.GetTerrain(1, 0));
            Assert.Null(map.GetTerrain(0, 1));

            var otherMap = TerrainMap.CreateFromPlacement(other);
            Assert.NotNull(otherMap);
            Assert.Single(otherMap.terrainTiles);
            Assert.Same(other, otherMap.GetTerrain(0, 0));
            Assert.Null(otherMap.GetTerrain(0, -1));

            // A given filter replaces the group match.
            var both = TerrainMap.CreateFromPlacement(origin, terrain => terrain.groupingID == 73 || terrain.groupingID == 74);
            Assert.NotNull(both);
            Assert.Equal(3, both.terrainTiles.Count);
            Assert.Same(other, both.GetTerrain(0, 1));

            var withoutEast = TerrainMap.CreateFromPlacement(origin, terrain => terrain.groupingID == 73 && terrain != east);
            Assert.NotNull(withoutEast);
            Assert.Single(withoutEast.terrainTiles);
            Assert.Null(withoutEast.GetTerrain(1, 0));
        }

        [Fact]
        public void NoTerrainPassingTheFilterGivesNull()
        {
            using var tiles = new Tiles();
            var origin = tiles.Add("Origin", 0f, 0f, group: 75);
            // Positive controls: the same terrain does make a map.
            Assert.NotNull(TerrainMap.CreateFromPlacement(origin));
            Assert.NotNull(TerrainMap.CreateFromConnectedNeighbors(origin));
            // Documented: "Can return null when no Terrain objects pass the filter."
            Assert.Null(TerrainMap.CreateFromPlacement(origin, terrain => false));
            Assert.Null(TerrainMap.CreateFromPlacement(Vector2.zero, new Vector2(k_Size, k_Size), terrain => false));
            // As Unity's own walk: an origin that fails the filter gives an empty map.
            Assert.Empty(TerrainMap.CreateFromConnectedNeighbors(origin, terrain => false).terrainTiles);
        }

        [Fact]
        public void GetTerrainOutsideTheMapIsNull()
        {
            using var tiles = new Tiles();
            var origin = tiles.Add("Origin", 0f, 0f, group: 76);
            var east = tiles.Add("East", k_Size, 0f, group: 76);
            var map = TerrainMap.CreateFromPlacement(origin);
            Assert.NotNull(map);
            Assert.Same(east, map.GetTerrain(1, 0));
            // Documented: "Returns a valid Terrain object if successful, null otherwise."
            Assert.Null(map.GetTerrain(2, 0));
            Assert.Null(map.GetTerrain(-1, 0));
            Assert.Null(map.GetTerrain(0, 1));
            // Unbound's reading of "valid": a terrain destroyed after the map was made is null.
            Object.DestroyImmediate(east.gameObject);
            Assert.Null(map.GetTerrain(1, 0));
        }

        [Fact]
        public void PlacementRoundsEachPositionToATileAndKeepsTheFirstOfATile()
        {
            // Measured (tests/projects/terrainoverrides map.*): a terrain goes to the tile its X/Z
            // position rounds to, whatever its size, height or offset; a tile keeps the first
            // terrain placed on it, without a warning. Auto-connect keeps its own tile-grid rule
            // (Unbound's choice, unmeasured).
            using var tiles = new Tiles();
            var origin = tiles.Add("Origin", 0f, 0f, group: 77);
            var aligned = tiles.Add("Aligned", 2f * k_Size, 0f, group: 77);
            var nearly = tiles.Add("Nearly", 0f, k_Size + 0.0005f, group: 77);
            var raised = tiles.Add("Raised", -k_Size, 0f, group: 77, y: 3f);
            var halfway = tiles.Add("Halfway", 0f, -0.5f * k_Size, group: 77);
            var large = tiles.Add("Large", 0f, -2f * k_Size, group: 77, size: 2f * k_Size);
            var first = tiles.Add("First", k_Size, k_Size, group: 77);
            var second = tiles.Add("Second", k_Size, k_Size, group: 77);

            using var capture = new WarningCapture();
            var map = TerrainMap.CreateFromPlacement(origin);
            Assert.NotNull(map);
            Assert.Same(origin, map.GetTerrain(0, 0));
            Assert.Same(aligned, map.GetTerrain(2, 0));
            Assert.Same(nearly, map.GetTerrain(0, 1));
            Assert.Same(raised, map.GetTerrain(-1, 0));
            Assert.Same(large, map.GetTerrain(0, -2));
            Assert.Same(first, map.GetTerrain(1, 1));
            Assert.Equal(6, map.terrainTiles.Count);
            // Halfway rounds to tile (0, 0), which Origin holds; Second shares First's tile.
            Assert.DoesNotContain(halfway, map.terrainTiles.Values);
            Assert.DoesNotContain(second, map.terrainTiles.Values);
            Assert.Empty(capture.Warnings);

            Terrain.SetConnectivityDirty();
            Assert.Same(nearly, origin.topNeighbor);
            Assert.Same(raised, origin.leftNeighbor);
            Assert.Null(origin.bottomNeighbor);
        }

        [Fact]
        public void ALinkedTerrainFillsAnyEmptyTileItIsReachedAt()
        {
            // Measured (tests/projects/terrainoverrides map.connectedClash): B, right of A, links A
            // as its top: A also takes tile (1, 1); C, reaching B again at (1, 1), changes nothing.
            using var tiles = new Tiles();
            var a = tiles.Add("A", 0f, 0f, group: 81, autoConnect: false);
            var b = tiles.Add("B", k_Size, 0f, group: 81, autoConnect: false);
            var c = tiles.Add("C", 0f, k_Size, group: 81, autoConnect: false);
            a.SetNeighbors(null, c, b, null);
            b.SetNeighbors(a, a, null, null);
            c.SetNeighbors(null, null, b, a);
            using var capture = new WarningCapture();
            var map = TerrainMap.CreateFromConnectedNeighbors(a);
            Assert.Equal(4, map.terrainTiles.Count);
            Assert.Same(a, map.GetTerrain(0, 0));
            Assert.Same(b, map.GetTerrain(1, 0));
            Assert.Same(c, map.GetTerrain(0, 1));
            Assert.Same(a, map.GetTerrain(1, 1));
            Assert.Empty(capture.Warnings);

            // The walk follows a terrain each time it takes a tile, until the map holds more
            // terrains than are active: with one more active terrain, A at (1, 1) leads to B at (2, 1).
            tiles.Add("Unrelated", 50f * k_Size, 0f, group: 84, autoConnect: false);
            map = TerrainMap.CreateFromConnectedNeighbors(a);
            Assert.Equal(5, map.terrainTiles.Count);
            Assert.Same(b, map.GetTerrain(2, 1));
        }

        [Fact]
        public void TheWalkQueuesLeftBottomRightTop()
        {
            // A's right B and bottom D both link a terrain to tile (1, -1): D's, queued first, wins.
            using var tiles = new Tiles();
            var a = tiles.Add("A", 0f, 0f, group: 85, autoConnect: false);
            var b = tiles.Add("B", k_Size, 0f, group: 85, autoConnect: false);
            var d = tiles.Add("D", 0f, -k_Size, group: 85, autoConnect: false);
            var x = tiles.Add("X", 5f * k_Size, 0f, group: 85, autoConnect: false);
            var y = tiles.Add("Y", 6f * k_Size, 0f, group: 85, autoConnect: false);
            a.SetNeighbors(null, null, b, d);
            b.SetNeighbors(null, null, null, x);
            d.SetNeighbors(null, null, y, null);
            var map = TerrainMap.CreateFromConnectedNeighbors(a);
            Assert.Same(y, map.GetTerrain(1, -1));
            Assert.DoesNotContain(x, map.terrainTiles.Values);

            // An origin that fails the filter gives an empty map.
            var empty = TerrainMap.CreateFromConnectedNeighbors(a, _ => false);
            Assert.NotNull(empty);
            Assert.Empty(empty.terrainTiles);
        }

        [Fact]
        public void CreateFromConnectedNeighborsFollowsTheLinks()
        {
            using var tiles = new Tiles();
            var southWest = tiles.Add("SW", 0f, 0f, group: 78);
            var southEast = tiles.Add("SE", k_Size, 0f, group: 78);
            var northWest = tiles.Add("NW", 0f, k_Size, group: 78);
            var northEast = tiles.Add("NE", k_Size, k_Size, group: 78);
            var far = tiles.Add("Far", 3f * k_Size, 0f, group: 78);
            Terrain.SetConnectivityDirty();

            // Documented: the origin is tile (0,0); the map holds the neighbours connected to it.
            var map = TerrainMap.CreateFromConnectedNeighbors(northEast);
            Assert.NotNull(map);
            Assert.Equal(4, map.terrainTiles.Count);
            Assert.Same(northEast, map.GetTerrain(0, 0));
            Assert.Same(northWest, map.GetTerrain(-1, 0));
            Assert.Same(southEast, map.GetTerrain(0, -1));
            Assert.Same(southWest, map.GetTerrain(-1, -1));
            Assert.DoesNotContain(far, map.terrainTiles.Values);

            // Placement, unlike the links, also reaches the unconnected tile of the group.
            var placed = TerrainMap.CreateFromPlacement(southWest);
            Assert.NotNull(placed);
            Assert.Same(far, placed.GetTerrain(3, 0));
        }

        [Fact]
        public void ConnectedNeighborsWithoutAFilterFollowEveryLink()
        {
            using var tiles = new Tiles();
            var origin = tiles.Add("Origin", 0f, 0f, group: 79, autoConnect: false);
            var other = tiles.Add("Other", 50f, 0f, group: 80, autoConnect: false);
            var third = tiles.Add("Third", 90f, 0f, group: 79, autoConnect: false);
            origin.SetNeighbors(null, null, other, null);
            other.SetNeighbors(origin, null, third, null);

            // Documented: a null filter applies none, so another group's linked tile is kept.
            // As Unity's own walk: each link is one tile, whatever the positions.
            var map = TerrainMap.CreateFromConnectedNeighbors(origin);
            Assert.NotNull(map);
            Assert.Equal(3, map.terrainTiles.Count);
            Assert.Same(other, map.GetTerrain(1, 0));
            Assert.Same(third, map.GetTerrain(2, 0));

            // As Unity's own walk: a terrain that fails the filter takes no tile and is not followed.
            var filtered = TerrainMap.CreateFromConnectedNeighbors(origin, terrain => terrain.groupingID == 79);
            Assert.NotNull(filtered);
            Assert.Single(filtered.terrainTiles);
            Assert.Same(origin, filtered.GetTerrain(0, 0));
            Assert.Null(filtered.GetTerrain(2, 0));
        }

        [Fact]
        public void AutoConnectConnectsNeighbours()
        {
            using var tiles = new Tiles();
            var west = tiles.Add("West", 0f, 0f, group: 81);
            var east = tiles.Add("East", 3f * k_Size, 0f, group: 81);
            var stranger = tiles.Add("Stranger", -k_Size, 0f, group: 82);
            Terrain.SetConnectivityDirty();
            Assert.Null(west.rightNeighbor);

            // Unbound's choice (unmeasured): moving a tile does not mark the connectivity dirty.
            east.transform.position = new Vector3(k_Size, 0f, 0f);
            Assert.Null(west.rightNeighbor);

            // Documented: AutoConnect connects neighbouring terrains, grouped by grouping ID.
            TerrainUtility.AutoConnect();
            Assert.Same(east, west.rightNeighbor);
            Assert.Same(west, east.leftNeighbor);
            Assert.Null(west.leftNeighbor);
            Assert.Null(stranger.rightNeighbor);

            // Unbound's choice (unmeasured): AutoConnect reassigns links set by hand.
            west.SetNeighbors(null, null, null, null);
            Assert.Null(west.rightNeighbor);
            TerrainUtility.AutoConnect();
            Assert.Same(east, west.rightNeighbor);
        }

        [Fact]
        public void AutoConnectTakesTheNeighboursOfTheGroupsPlacementMap()
        {
            // As Unity's own TerrainUtility.AutoConnect: a tile two terrains round to keeps the
            // first (tests/projects/terrainoverrides map.overlap), silently; an off-grid terrain
            // rounds to its tile (map.offGrid).
            using var tiles = new Tiles();
            var origin = tiles.Add("Origin", 0f, 0f, group: 83);
            var first = tiles.Add("First", k_Size, 0f, group: 83);
            var second = tiles.Add("Second", k_Size, 0f, group: 83);
            var below = tiles.Add("Below", 0f, -k_Size + 0.3f * k_Size, group: 83);

            using var capture = new WarningCapture();
            Terrain.SetConnectivityDirty();
            Assert.Same(first, origin.rightNeighbor);
            Assert.Same(origin, first.leftNeighbor);
            Assert.Null(second.leftNeighbor);
            Assert.Same(below, origin.bottomNeighbor);
            Assert.Empty(capture.Warnings);
        }

        [Fact]
        public void TerrainTileCoordKeepsItsCoordinates()
        {
            var coord = new TerrainTileCoord(3, -2);
            Assert.Equal(3, coord.tileX);
            Assert.Equal(-2, coord.tileZ);
            var byCoord = new Dictionary<TerrainTileCoord, int> { [new TerrainTileCoord(3, -2)] = 1 };
            Assert.True(byCoord.ContainsKey(new TerrainTileCoord(3, -2)));
            Assert.False(byCoord.ContainsKey(new TerrainTileCoord(-2, 3)));
        }
    }
}
