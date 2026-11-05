using System;
using System.Collections.Generic;
using Xunit;

namespace UnityEngine.Tests
{
    /// <summary>
    /// Terrain auto-connect. Documented (Unity 6000.0 Scripting API and Manual): tiles that allow
    /// auto-connect and share a grouping ID connect to adjacent tiles, both tiles must allow it,
    /// left/right are -X/+X and top/bottom are +Z/-Z, SetConnectivityDirty marks the connectivity
    /// invalid after tiles are added or removed, and SetNeighbors sets only the tile it is called
    /// on. The adjacency tolerance, same-size rule, overlap handling, rebuild timing, the
    /// SetNeighbors/auto-connect interplay and the reaction to enabling or disabling a tile are
    /// Unbound's choices (unmeasured): the tests marked so check the implementation, not a Unity
    /// guarantee.
    /// </summary>
    public class TerrainAutoConnectTests
    {
        const float k_Size = 10f;

        sealed class Tiles : IDisposable
        {
            readonly List<GameObject> m_Objects = new List<GameObject>();
            readonly Dictionary<float, TerrainData> m_Data = new Dictionary<float, TerrainData>();

            public Terrain Add(string name, float x, float z, int group = 0, bool autoConnect = true, float size = k_Size)
            {
                if (!m_Data.TryGetValue(size, out var data))
                {
                    data = new TerrainData { heightmapResolution = 33 };
                    data.size = new Vector3(size, 5f, size);
                    m_Data[size] = data;
                }
                var go = new GameObject(name);
                go.transform.position = new Vector3(x, 0f, z);
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

        static void AssertLinks(Terrain tile, Terrain left, Terrain top, Terrain right, Terrain bottom)
        {
            Assert.Same(left, tile.leftNeighbor);
            Assert.Same(top, tile.topNeighbor);
            Assert.Same(right, tile.rightNeighbor);
            Assert.Same(bottom, tile.bottomNeighbor);
        }

        [Fact]
        public void AdjacentTilesOfOneGroupBecomeLeftAndRightNeighbours()
        {
            using var tiles = new Tiles();
            var west = tiles.Add("West", 0f, 0f, group: 3);
            var east = tiles.Add("East", k_Size, 0f, group: 3);
            Terrain.SetConnectivityDirty();
            AssertLinks(west, null, null, east, null);
            AssertLinks(east, west, null, null, null);
        }

        [Fact]
        public void AnotherGroupOrAutoConnectOffDoesNotConnect()
        {
            using var tiles = new Tiles();
            var centre = tiles.Add("Centre", 0f, 0f, group: 1);
            var sameGroup = tiles.Add("SameGroup", k_Size, 0f, group: 1);
            var otherGroup = tiles.Add("OtherGroup", -k_Size, 0f, group: 2);
            var optedOut = tiles.Add("OptedOut", 0f, k_Size, group: 1, autoConnect: false);
            Terrain.SetConnectivityDirty();
            // Positive control: the same-group tile does connect.
            Assert.Same(sameGroup, centre.rightNeighbor);
            Assert.Null(centre.leftNeighbor);
            Assert.Null(otherGroup.rightNeighbor);
            // Documented for the Manual's Reconnect (both tiles must have Auto Connect enabled),
            // applied to the runtime pass.
            Assert.Null(centre.topNeighbor);
            Assert.Null(optedOut.bottomNeighbor);
        }

        [Fact]
        public void TilesThatAreNotAdjacentDoNotConnect()
        {
            using var tiles = new Tiles();
            var origin = tiles.Add("Origin", 0f, 0f);
            var touching = tiles.Add("Touching", 0f, k_Size);
            var gap = tiles.Add("Gap", 2f * k_Size + 1f, 0f);
            var diagonal = tiles.Add("Diagonal", -k_Size, -k_Size);
            var shifted = tiles.Add("Shifted", -k_Size, k_Size * 0.5f + k_Size);
            Terrain.SetConnectivityDirty();
            // Positive control: the tile one step along +Z is the top neighbour.
            Assert.Same(touching, origin.topNeighbor);
            Assert.Same(origin, touching.bottomNeighbor);
            // A gap, a diagonal and a half-tile offset are not adjacent (Unbound's choice: exact
            // grid adjacency within a ten-thousandth of the tile size, unmeasured).
            Assert.Null(origin.rightNeighbor);
            Assert.Null(gap.leftNeighbor);
            Assert.Null(origin.leftNeighbor);
            Assert.Null(origin.bottomNeighbor);
            Assert.Null(diagonal.rightNeighbor);
            Assert.Null(diagonal.topNeighbor);
            Assert.Null(shifted.rightNeighbor);
            Assert.Null(touching.leftNeighbor);
        }

        [Fact]
        public void DifferentlySizedTilesDoNotConnect()
        {
            // Unbound's choice (unmeasured): neighbours have the same X/Z size.
            using var tiles = new Tiles();
            var small = tiles.Add("Small", 0f, 0f);
            var large = tiles.Add("Large", k_Size, 0f, size: 2f * k_Size);
            var match = tiles.Add("Match", -k_Size, 0f);
            Terrain.SetConnectivityDirty();
            Assert.Same(match, small.leftNeighbor);
            Assert.Null(small.rightNeighbor);
            Assert.Null(large.leftNeighbor);
        }

        [Fact]
        public void TwoByTwoGridConnectsEveryEdge()
        {
            using var tiles = new Tiles();
            var southWest = tiles.Add("SW", 0f, 0f);
            var southEast = tiles.Add("SE", k_Size, 0f);
            var northWest = tiles.Add("NW", 0f, k_Size);
            var northEast = tiles.Add("NE", k_Size, k_Size);
            Terrain.SetConnectivityDirty();
            AssertLinks(southWest, null, northWest, southEast, null);
            AssertLinks(southEast, southWest, northEast, null, null);
            AssertLinks(northWest, null, null, northEast, southWest);
            AssertLinks(northEast, northWest, null, null, southEast);
        }

        [Fact]
        public void SetNeighborsSetsOnlyTheTileItIsCalledOn()
        {
            // Documented: "it isn't enough to call this function on one Terrain".
            using var tiles = new Tiles();
            var a = tiles.Add("A", 0f, 0f, autoConnect: false);
            var b = tiles.Add("B", 5f * k_Size, 0f, autoConnect: false);
            a.SetNeighbors(null, null, b, null);
            Assert.Same(b, a.rightNeighbor);
            Assert.Null(b.leftNeighbor);
        }

        [Fact]
        public void SetNeighborsAndAutoConnect()
        {
            // Unbound's choice (unmeasured): SetNeighbors wins until the connectivity is marked
            // dirty again; the next pass reassigns every auto-connect tile and leaves the manual
            // links of a tile that does not auto-connect alone.
            using var tiles = new Tiles();
            var west = tiles.Add("West", 0f, 0f);
            var east = tiles.Add("East", k_Size, 0f);
            var manual = tiles.Add("Manual", 0f, 5f * k_Size, autoConnect: false);
            Terrain.SetConnectivityDirty();
            west.SetNeighbors(manual, null, null, null);
            manual.SetNeighbors(null, null, east, null);
            Assert.Same(manual, west.leftNeighbor);
            Assert.Null(west.rightNeighbor);
            Assert.Same(west, east.leftNeighbor);

            Terrain.SetConnectivityDirty();
            AssertLinks(west, null, null, east, null);
            AssertLinks(east, west, null, null, null);
            AssertLinks(manual, null, null, east, null);
        }

        [Fact]
        public void DisablingATileDropsItsLinks()
        {
            // Unbound's choice (unmeasured): enabling, disabling or destroying a terrain marks the
            // connectivity dirty; only active terrains (Terrain.activeTerrains) take part.
            using var tiles = new Tiles();
            var west = tiles.Add("West", 0f, 0f);
            var east = tiles.Add("East", k_Size, 0f);
            Assert.Same(east, west.rightNeighbor);

            east.enabled = false;
            Assert.Null(west.rightNeighbor);
            AssertLinks(east, null, null, null, null);

            east.enabled = true;
            Assert.Same(east, west.rightNeighbor);
            Assert.Same(west, east.leftNeighbor);

            east.gameObject.SetActive(false);
            Assert.Null(west.rightNeighbor);

            east.gameObject.SetActive(true);
            Object.DestroyImmediate(west.gameObject);
            Assert.Null(east.leftNeighbor);
        }

        sealed class WarningCapture : ILogHandler
        {
            readonly ILogHandler m_Next;
            public readonly List<string> Warnings = new List<string>();

            public WarningCapture(ILogHandler next) => m_Next = next;

            public void LogFormat(LogType logType, Object context, string format, params object[] args)
            {
                if (logType == LogType.Warning) Warnings.Add(args == null || args.Length == 0 ? format : string.Format(System.Globalization.CultureInfo.InvariantCulture, format, args));
                m_Next.LogFormat(logType, context, format, args);
            }

            public void LogException(Exception exception, Object context) => m_Next.LogException(exception, context);
        }

        [Fact]
        public void OverlappingCandidatesLeaveTheSideEmptyWithAWarning()
        {
            // Unbound's choice (unmeasured): an ambiguous side is not guessed. The warning is an
            // Unbound diagnostic, so it is read at the logger, not from the game's log callbacks.
            using var tiles = new Tiles();
            var origin = tiles.Add("Origin", 0f, 0f);
            tiles.Add("First", k_Size, 0f);
            tiles.Add("Second", k_Size, 0f);
            var below = tiles.Add("Below", 0f, -k_Size);
            Terrain.SetConnectivityDirty();
            var logger = Debug.unityLogger;
            var handler = logger.logHandler;
            bool enabled = logger.logEnabled;
            var filter = logger.filterLogType;
            var capture = new WarningCapture(handler);
            logger.logHandler = capture;
            logger.logEnabled = true;
            logger.filterLogType = LogType.Log;
            try
            {
                Assert.Same(below, origin.bottomNeighbor);
                Assert.Null(origin.rightNeighbor);
            }
            finally
            {
                logger.logHandler = handler;
                logger.logEnabled = enabled;
                logger.filterLogType = filter;
            }
            Assert.Contains(capture.Warnings, w => w.Contains("'Origin'") && w.Contains("overlapping auto-connect tiles on its right"));
        }
    }
}
